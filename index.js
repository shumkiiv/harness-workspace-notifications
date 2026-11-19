import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { Tracker, aggregate, reasonText, summary, workspaceFor, uniqueWorkspaceByPath } from './src/core.mjs'
import { openStore, normalizeSettings } from './src/store.mjs'
import { launchHelper } from './src/helper.mjs'
import { buildLimitsPayload } from './src/limits.mjs'
import { setupQuotaBridge } from './src/quota/bridge.mjs'

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
  let bridge = setupQuotaBridge(ctx, () => settings)
  let bridgeScope
  const attachBridge = (scope) => {
    const previous = bridge
    previous.close()
    const scoped = scope && typeof scope.get === 'function' ? scope : ctx
    const installed = setupQuotaBridge(scoped, () => settings)
    bridge = installed
    return () => { installed.close() }
  }
  if (typeof ctx.inject === 'function') bridgeScope = ctx.inject(['webServer', 'connection'], (scope) => attachBridge(scope))
  let outcomes = store.outcomes()
  const tracker = new Tracker({
    turnBoundaryOf(session) {
      return ctx.get?.('agents')?.turnBoundaryOf?.(session)
    },
  })
  const helper = (options.createHelper || launchHelper)({
    helperPath: options.helperPath || path.join(root, 'native', 'DshNotifyGui.exe'),
    demo: Boolean(options.demo),
    demoArgs: options.demoArgs || [],
    logger,
    onMessage: receive,
  })
  let timer
  let limitsTimer
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
      helper.send({ t: 'widget.render', rows: aggregate([...areas.values()], states, currentAgents()) })
    } catch (error) {
      logger.warn?.(`Не удалось обновить виджет: ${error.message}`)
    }
  }

  async function refreshLimits() {
    if (closed) return
    const llm = ctx.get?.('llm')
    if (!llm || typeof llm.listProviders !== 'function') return
    const readers = {}
    if (typeof options.readProviderQuota === 'function') {
      try {
        const providers = await llm.listProviders()
        if (Array.isArray(providers)) for (const provider of providers) if (provider?.id) readers[provider.id] = options.readProviderQuota
      } catch { return }
    }
    else readers['openai-codex'] = bridge.read
    const payload = await buildLimitsPayload({ listProviders: () => llm.listProviders(), readers })
    helper.send({ t: 'widget.limits', providers: payload.providers, receivedAt: payload.receivedAt })
  }

  function receive(message) {
    if (message?.t === 'settings.changed') {
      settings = normalizeSettings(message.settings)
      store.saveSettings(settings)
      render()
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
  timer = setInterval(render, 1500)
  limitsTimer = setInterval(refreshLimits, 60000)
  render()
  refreshLimits()

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
      bridge.close()
      if (typeof bridgeScope === 'function') bridgeScope()
      else if (bridgeScope && typeof bridgeScope.dispose === 'function') await bridgeScope.dispose()
      await helper.close()
    },
  }
}

export default apply
