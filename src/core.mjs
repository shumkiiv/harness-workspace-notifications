import { win32, posix } from 'node:path'

const STATUS_TEXT = {
  running: 'Выполняется',
  completed: 'Завершено',
  error: 'Ошибка',
  'max-tokens': 'Прервано: лимит токенов',
  blocked: 'Заблокировано',
  aborted: 'Прервано',
  interrupted: 'Прервано',
  disposed: 'Сессия закрыта',
  unknown: 'Результат неизвестен',
  idle: 'Ожидает',
}

export function reasonText(reason) {
  return STATUS_TEXT[reason] || STATUS_TEXT.unknown
}

export function basename(value) {
  const parts = String(value || '').replace(/[\\/]+$/, '').split(/[\\/]/).filter(Boolean)
  return parts.at(-1) || 'Без рабочей области'
}

export function canonicalPath(value) {
  if (typeof value !== 'string' || value === '') return null
  const windows = /^[A-Za-z]:[\\/]/.test(value) || value.startsWith('\\\\') || value.startsWith('//')
  if (!windows && !value.startsWith('/')) return null
  const result = windows ? win32.normalize(value) : posix.normalize(value)
  if (!result || result === '.') return null
  return windows ? result.replace(/[\\/]+$/, '').toLowerCase() : result.replace(/\/+$/, '')
}

export function uniqueWorkspaceByPath(value, listed) {
  const key = canonicalPath(value)
  if (!key) return undefined
  const matches = (listed || []).filter((item) => canonicalPath(item.path) === key)
  return matches.length === 1 ? matches[0] : undefined
}

export function summary(value) {
  const text = String(value || '').replace(/\s+/g, ' ').trim()
  if (!text) return 'Задача не указана'
  return text.length > 200 ? `${text.slice(0, 199)}…` : text
}

export class Tracker {
  constructor(agents = {}) {
    this.agents = agents
    this.sessions = new Map()
    this.emitted = new Set()
  }

  observe(session) {
    if (!session?.id) return undefined
    const existing = this.sessions.get(session.id)
    if (existing) {
      existing.session = session
      return existing
    }
    const boundary = this.agents.turnBoundaryOf?.(session)
    const state = {
      session,
      baseline: boundary?.lastTurn,
      activeBaseline: boundary?.openTurnStartSeq != null,
      open: new Map(),
      ended: new Set(),
      disposed: false,
      outcome: undefined,
      task: '',
    }
    this.sessions.set(session.id, state)
    return state
  }

  start(session, turn, time) {
    const state = this.observe(session)
    state.disposed = false
    state.open.set(turn, { time })
    if (state.baseline != null && turn > state.baseline) state.activeBaseline = false
    return state
  }

  end(session, turn, reason, time) {
    const state = this.observe(session)
    const key = `${session.id}#${turn}`
    if (this.emitted.has(key) || state.ended.has(turn)) return { emit: false, state }
    const activeBaseline = state.activeBaseline && turn === state.baseline
    const started = state.open.get(turn)
    const newer = state.baseline != null && turn > state.baseline
    if (!activeBaseline && !started && !newer) return { emit: false, state, historical: true }
    state.open.delete(turn)
    state.ended.add(turn)
    state.activeBaseline = false
    state.outcome = { reason: reason || 'unknown', time, turn, startedAt: started?.time }
    this.emitted.add(key)
    return { emit: true, state }
  }

  dispose(session) {
    const state = this.observe(session)
    state.open.clear()
    state.disposed = true
    if (!state.outcome) state.outcome = { reason: 'disposed', time: Date.now() }
    return state
  }

  state(id) {
    return this.sessions.get(id)
  }
}

export function workspaceFor(session, registry, sessions) {
  const workspaces = Array.isArray(registry) ? registry : []
  const knownSessions = sessions instanceof Map ? sessions : new Map()
  let current = session
  const visited = new Set()
  for (let depth = 0; current && depth < 5 && !visited.has(current.id); depth += 1) {
    visited.add(current.id)
    const match = workspaces.find((workspace) => Array.isArray(workspace.sessionIds)
      && workspace.sessionIds.includes(current.id))
    if (match) return match
    current = knownSessions.get(current.header?.parentSession)
  }
  const cwd = session?.header?.cwd || ''
  const exact = uniqueWorkspaceByPath(cwd, workspaces)
  if (exact) return exact
  return { id: `path:${cwd || session?.id || 'unknown'}`, title: basename(cwd), path: cwd, sessionIds: [] }
}

function stateIsRunning(id, state, status) {
  if (!state || state.disposed) return false
  if (state.open.size > 0) return true
  if (state.ended.size > 0) return false
  return status === 'running'
}

export function aggregate(workspaces, states, agents = [], now = null) {
  const statusBySession = new Map((agents || []).map((agent) => [agent.session?.id || agent.session, agent.status]))
  return (workspaces || []).map((workspace) => {
    const memberIds = Array.isArray(workspace.sessionIds) ? workspace.sessionIds : []
    const members = memberIds.map((id) => ({ id, state: states.get(id) })).filter((item) => item.state)
    const running = members.filter(({ id, state }) => stateIsRunning(id, state, statusBySession.get(id)))
    const outcomes = members.map(({ state }) => state.outcome).filter(Boolean).sort((a, b) => b.time - a.time)
    const latest = outcomes[0]
    const latestRunning = running.map(({ id, state }) => ({ id, state })).sort((a, b) => {
      const aTime = a.state.open.size ? Math.max(...[...a.state.open.values()].map((turn) => turn.time || 0)) : 0
      const bTime = b.state.open.size ? Math.max(...[...b.state.open.values()].map((turn) => turn.time || 0)) : 0
      return bTime - aTime
    })[0]
    const disposed = members.length > 0 && members.every(({ state }) => state.disposed) && !latest
    const status = running.length ? 'running' : (disposed ? 'disposed' : (latest?.reason || 'idle'))
    const completedCount = outcomes.filter((outcome) => outcome.reason === 'completed').length
    const represented = latest?.sessionId || latestRunning?.id || ''
    const activeStart = latestRunning?.state.open.size ? Math.max(...[...latestRunning.state.open.values()].map((turn) => turn.time || 0)) : 0
    const activeDuration = !latest && latestRunning && represented === latestRunning.id && Number.isFinite(activeStart) && activeStart > 0 && Number.isFinite(now) && now >= activeStart
      ? `${Math.max(0, Math.round((now - activeStart) / 1000))} с` : ''
    return {
      id: workspace.id,
      workspace: workspace.title || basename(workspace.path),
      title: workspace.title || basename(workspace.path),
      path: workspace.path || '',
      registered: workspace.registered === true,
      status,
      statusText: reasonText(status),
      taskSummary: latest?.task || latestRunning?.state.task || '',
      sessionId: represented,
      timeText: latest?.duration || activeDuration,
      runningCount: running.length,
      completedCount,
    }
  })
}

export function capOutcomes(items, total = 500, perWorkspace = 50) {
  const seen = new Map()
  return [...items].reverse().filter((item) => {
    const count = seen.get(item.workspaceId) || 0
    seen.set(item.workspaceId, count + 1)
    return count < perWorkspace
  }).reverse().slice(-total)
}
