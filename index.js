import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { Tracker, aggregate, reasonText, summary, workspaceFor, uniqueWorkspaceByPath } from './src/core.mjs'
import { openStore, normalizeSettings } from './src/store.mjs'
import { launchHelper } from './src/helper.mjs'
import { buildLimitsPayload } from './src/limits.mjs'
import { createProviderUsageReaders } from './src/providerUsage.mjs'
import { WidgetQuotaKeys } from './src/widgetQuotaKeys.mjs'
import { setupQuotaBridge } from './src/quota/bridge.mjs'
import { setupSessionOpenBridge } from './src/sessionOpen.mjs'

const root = path.dirname(fileURLToPath(import.meta.url))

export function apply(ctx, options = {}) {
  const runtime = createRuntime(ctx, options)
  ctx.on?.('dispose', () => {
    return runtime.close()
  })
}

export function createRuntime(ctx, options = {}) {
  const logger = ctx.logger?.('dsh-workspace-notifications') || console
  const dataDir = options.dataDir || process.env.DSH_WN_DATA_DIR
    || path.join(os.homedir(), '.dsh', 'workspace-notifications')
  const store = openStore(dataDir, logger)
  let settings = store.settings()
  let bridge = { read: async () => null, close() {} }
  let sessionOpen = { update() {}, accept: async () => false, close() {} }
  let bridgeScope
  let outcomes = store.outcomes()
  const tracker = new Tracker({
    turnBoundaryOf(session) {
      return ctx.get?.('agents')?.turnBoundaryOf?.(session)
    },
  })
  const displayedSessionIds = new Set()
  const noticeSessions = new Map()
  const sessionExists = async (id) => {
    try {
      const attached = ctx.get?.('sessions')?.get?.(id)
      if (attached) return true
      return Boolean(await ctx.get?.('sessionPersistence')?.stat?.(id))
    } catch { return false }
  }
  const attachBridge = (scope) => {
    bridge.close(); sessionOpen.close()
    const scoped = scope && typeof scope.get === 'function' ? scope : ctx
    const scopedBridge = setupQuotaBridge(scoped, () => settings, undefined, () => refreshLimits())
    const scopedSessionOpen = setupSessionOpenBridge(scoped, undefined, sessionExists)
    bridge = scopedBridge; sessionOpen = scopedSessionOpen
    return () => { scopedBridge.close(); scopedSessionOpen.close() }
  }
  if (typeof ctx.inject === 'function') bridgeScope = ctx.inject(['webServer', 'connection'], (scope) => attachBridge(scope))
  else attachBridge(ctx)
  let quotaKeys
  const helper = (options.createHelper || launchHelper)({
    helperPath: options.helperPath || path.join(root, 'native', 'DshNotifyGui.exe'),
    dataDir: path.resolve(dataDir),
    demo: Boolean(options.demo),
    demoArgs: options.demoArgs || [],
    logger,
    onMessage: receive,
  })
  quotaKeys = new WidgetQuotaKeys((message) => helper.send(message), { timeout: 17_000 })
  let timer
  let limitsTimer
  let accountBalanceCache = { at: 0, value: null, pending: null }
  const providerUsageCache = new Map()
  let limitsGeneration = 0
  let closed = false
  for (const outcome of outcomes) tracker.emitted.add(outcome.id)

  function registry() {
    const values = ctx.get?.('workspaceRegistry')?.list?.()
    if (!Array.isArray(values)) return []
    return values.map((item) => {
      const path = String(item?.path || item?.dir || '').replace(/\\/g, '/')
      return { id: String(item?.id || ''), title: String(item?.title || item?.name || path.split('/').filter(Boolean).at(-1) || item?.id || ''), path, sessionIds: Array.isArray(item?.sessionIds) ? [...item.sessionIds] : [], registered: true }
    }).filter((item) => item.id)
  }

  function sessionMap() {
    const values = new Map()
    for (const state of tracker.sessions.values()) values.set(state.session.id, state.session)
    return values
  }

  function currentAgents() {
    const values = ctx.get?.('agents')?.list?.()
    return Array.isArray(values) ? values : []
  }

  function rememberAgents() {
    for (const agent of currentAgents()) {
      const session = agent.session || agent
      if (session?.id) tracker.observe(session)
    }
  }

  function resolvedWorkspace(session, listed = registry()) {
    const known = sessionMap()
    let parent = session?.header?.parentSession
    for (let depth = 0; parent && depth < 5 && !known.has(parent); depth += 1) {
      const resolved = ctx.get?.('sessions')?.get?.(parent)
      if (!resolved) break
      known.set(resolved.id, resolved)
      parent = resolved.header?.parentSession
    }
    return workspaceFor(session, listed, known)
  }

  function render() {
    if (closed) return
    try {
      rememberAgents()
      const listed = registry()
      const areas = new Map(listed.map((item) => [item.id, { id: item.id, title: item.title, path: item.path, sessionIds: [...item.sessionIds], registered: item.registered === true }]))
      const states = new Map(tracker.sessions)
      for (const state of tracker.sessions.values()) {
        const workspace = resolvedWorkspace(state.session, listed)
        const area = areas.get(workspace.id) || { ...workspace, sessionIds: [] }
        if (!area.sessionIds.includes(state.session.id)) area.sessionIds.push(state.session.id)
        areas.set(workspace.id, area)
      }
      for (const outcome of outcomes) {
        const id = `history:${outcome.id}`
        const live = states.get(outcome.sessionId)
        if (live?.outcome && `${outcome.sessionId}#${live.outcome.turn}` === outcome.id) continue
        if (states.has(id)) continue
        states.set(id, { session: { id }, open: new Map(), ended: new Set(), disposed: false, outcome })
        const knownOutcomeWorkspace = uniqueWorkspaceByPath(outcome.path, listed)
        const area = areas.get(outcome.workspaceId) || areas.get(knownOutcomeWorkspace?.id) || {
          id: outcome.workspaceId,
          title: outcome.workspace || 'Без рабочей области',
          path: outcome.path || '',
          sessionIds: [],
        }
        area.sessionIds.push(id)
        areas.set(area.id, area)
      }
      const rows = aggregate([...areas.values()], states, currentAgents(), settings.InlineTaskTime ? Date.now() : null)
      displayedSessionIds.clear()
      for (const row of rows) if (row.sessionId) displayedSessionIds.add(row.sessionId)
      for (const id of noticeSessions.values()) displayedSessionIds.add(id)
      sessionOpen.update({ displayed: [...displayedSessionIds] })
      helper.send({ t: 'widget.render', rows })
    } catch (error) {
      logger.warn?.(`Не удалось обновить виджет: ${error.message}`)
    }
  }

  async function refreshLimits() {
    const generation = limitsGeneration
    const snapshot = settings
    if (closed) return
    const llm = ctx.get?.('llm')
    if (!llm || typeof llm.listProviders !== 'function') return
    const readers = {}
    const account = ctx.get?.('deepseekAccount')
    const hidden = Array.isArray(settings.HiddenLimitProviders) ? settings.HiddenLimitProviders : []
    const enabled = settings.ShowLimits === true
    const current = () => !closed && generation === limitsGeneration && settings === snapshot
    let providers
    try { providers = await llm.listProviders() } catch { return }
    if (!current()) return
    const catalog = Array.isArray(providers) ? providers.filter((item) => item && typeof item.id === 'string' && item.id && typeof (item.name || item.label || item.id) === 'string') : []
    if (typeof options.readProviderQuota === 'function') {
      for (const provider of catalog) readers[provider.id] = options.readProviderQuota
    } else if (enabled && !hidden.includes('openai-codex')) readers['openai-codex'] = bridge.read
    if (enabled && typeof options.readProviderQuota !== 'function') {
      let configurable
      try { configurable = await llm.listConfigurableProviders?.() } catch { configurable = [] }
      if (!current()) return
      const usageReaders = await createProviderUsageReaders({ ctx, configurableProviders: configurable, current,
        eligible: (id) => current() && !closed && settings.ShowLimits === true && !settings.HiddenLimitProviders.includes(id), cache: providerUsageCache,
        widgetQuota: (id) => quotaKeys.read(id) })
      if (!current()) return
      const available = new Set(catalog.map((item) => item.id))
      for (const id of ['deepseek-official', 'zai']) {
        if (available.has(id) && !hidden.includes(id) && usageReaders[id]) readers[id] = usageReaders[id]
      }
    }
    if (enabled && account && typeof account.getBalance === 'function' && !hidden.includes('deepseek-account')) {
      const cache = accountBalanceCache
      readers['deepseek-account'] = async () => {
        if (!current() || settings.HiddenLimitProviders.includes('deepseek-account')) return null
        const clock = Date.now()
        if (cache.pending) return cache.pending
        if (clock - cache.at <= 10 * 60 * 1000) return cache.value
        const version = ctx.get?.('clientModules')?.graph?.()?.release?.version
        if (typeof version !== 'string' || !version) return null
        cache.pending = Promise.resolve(account.getBalance({ version, locale: 'ru-RU', timezoneOffsetSeconds: -new Date().getTimezoneOffset() * 60 }))
          .then((value) => {
            const validAmount = (item, kind) => item && (item.currency === 'USD' || item.currency === 'CNY')
              && typeof item.balance === 'string' && /^-?\d{1,18}(?:\.\d{1,12})?$/.test(item.balance)
              ? { currency: item.currency, balance: item.balance, kind } : null
            const amounts = value && value.status === 'ready' ? [
              ...(Array.isArray(value.value) ? value.value.map((item) => validAmount(item, 'wallet')) : []),
              ...(Array.isArray(value.bonusWallets) ? value.bonusWallets.map((item) => validAmount(item, 'bonus')) : []),
            ].filter(Boolean) : []
            cache.value = amounts.length ? { amounts } : null
            cache.at = Date.now()
            return cache.value
          }).catch(() => null).finally(() => { cache.pending = null })
        return cache.pending
      }
    }
    if (account && typeof account.getBalance === 'function' && !catalog.some((item) => item.id === 'deepseek-account')) catalog.push({ id: 'deepseek-account', name: 'DeepSeek Account' })
    const payload = enabled ? await buildLimitsPayload({ listProviders: () => catalog, readers, hiddenProviders: hidden }) : { providers: [], receivedAt: Date.now() }
    if (!current()) return
    helper.send({ t: 'widget.limits', providers: payload.providers, availableProviders: catalog.map((item) => ({ id: String(item.id), label: String(item.name || item.label || item.id) })), receivedAt: payload.receivedAt })
  }

  function receive(message) {
    if (message?.t === 'ready') {
      quotaKeys.receive(message)
      if (settings.ShowLimits) void refreshLimits()
      return
    }
    if (message?.t === 'quota.result') {
      quotaKeys.receive(message)
      return
    }
    if (message?.t === 'quota-key.changed' && typeof message.provider === 'string') {
      limitsGeneration += 1
      quotaKeys.invalidate(message.provider)
      for (const key of providerUsageCache.keys()) if (key.startsWith(`${message.provider}\u0000`)) providerUsageCache.delete(key)
      if (settings.ShowLimits) void refreshLimits()
      return
    }
    if (message?.t === 'settings.changed') {
      limitsGeneration += 1
      settings = normalizeSettings(message.settings)
      store.saveSettings(settings)
      render()
      if (settings.ShowLimits) void refreshLimits()
      return
    }
    if (message?.t === 'widget.moved' && Number.isInteger(message.x) && Number.isInteger(message.y)) {
      settings = { ...settings, WidgetX: message.x, WidgetY: message.y }
      store.saveSettings(settings)
      return
    }
    if (message?.t === 'notices.moved' && Number.isInteger(message.x) && Number.isInteger(message.y)) {
      settings = { ...settings, NoticeX: message.x, NoticeY: message.y }
      store.saveSettings(settings)
    }
    if (message?.t === 'notice.closed' && typeof message.id === 'string') {
      noticeSessions.delete(message.id)
      render()
      return
    }
    if (message?.t === 'session.open') void sessionOpen.accept(message)
  }

  function isSubagent(session) {
    return session?.header?.origin === 'subagent' || Boolean(session?.header?.parentSession)
  }

  function addOutcome(session, turn, reason, time, state) {
    const workspace = resolvedWorkspace(session)
    const startedAt = state.outcome?.startedAt
    const duration = startedAt == null
      ? 'Время неизвестно'
      : `${Math.max(0, Math.round((time - startedAt) / 1000))} с`
    const record = {
      id: `${session.id}#${turn}`,
      sessionId: session.id,
      workspaceId: workspace.id,
      workspace: workspace.title,
      path: workspace.path || session.header?.cwd || '',
      reason: reason || 'unknown',
      status: reason || 'unknown',
      statusText: reasonText(reason),
      task: state.task || 'Задача не указана',
      duration,
      time,
    }
    state.outcome = { ...state.outcome, ...record }
    outcomes = [...outcomes.filter((item) => item.id !== record.id), record]
    store.saveOutcomes(outcomes)
    outcomes = store.outcomes()
    if (settings.ShowNotifications && (!isSubagent(session) || settings.SubagentNotices)) {
      helper.send({ t: 'notice.show', ...record })
      noticeSessions.set(record.id, record.sessionId)
      displayedSessionIds.add(record.sessionId)
      sessionOpen.update({ displayed: [...displayedSessionIds] })
    }
  }

  function onSessionEvent(session, event) {
    if (closed || !session || !event) return
    tracker.observe(session)
    if (event.type === 'turn/start') {
      tracker.start(session, event.data?.turn, event.time || Date.now())
    } else if (event.type === 'user/message' && event.data?.source?.kind === 'user') {
      const text = (event.data.content || []).filter((part) => part.type === 'text').map((part) => part.text).join(' ')
      const state = tracker.state(session.id)
      if (state) state.task = summary(text)
    } else if (event.type === 'turn/end') {
      const result = tracker.end(session, event.data?.turn, event.data?.reason?.kind, event.time || Date.now())
      if (result.emit) addOutcome(session, event.data?.turn, event.data?.reason?.kind, event.time || Date.now(), result.state)
    }
    render()
  }

  function onDisposed(session) {
    if (!session) return
    tracker.dispose(session)
    render()
  }

  helper.send({ t: 'settings.apply', settings })
  rememberAgents()
  ctx.on?.('session/event', onSessionEvent)
  ctx.on?.('session/disposed', onDisposed)
  ctx.on?.('agent/status', render)
  ctx.on?.('credentials/record-updated', (key) => {
    if (closed || key !== 'deepseek-account-platform/default') return
    accountBalanceCache = { at: 0, value: null, pending: null }
    limitsGeneration += 1
    if (settings.ShowLimits) void refreshLimits()
  })
  ctx.on?.('credentials/reference-updated', () => {
    if (closed) return
    providerUsageCache.clear()
    limitsGeneration += 1
    if (settings.ShowLimits) void refreshLimits()
  })
  timer = setInterval(render, 1500)
  limitsTimer = setInterval(() => { void refreshLimits() }, 60000)
  render()
  void refreshLimits()

  return {
    render,
    receive,
    onSessionEvent,
    onDisposed,
    settings: () => settings,
    outcomes: () => outcomes,
    async close() {
      if (closed) return
      closed = true
      clearInterval(timer)
      clearInterval(limitsTimer)
      quotaKeys.close()
      bridge.close()
      sessionOpen.close()
      if (typeof bridgeScope === 'function') bridgeScope()
      else if (bridgeScope && typeof bridgeScope.dispose === 'function') await bridgeScope.dispose()
      await helper.close()
    },
  }
}

export default apply
