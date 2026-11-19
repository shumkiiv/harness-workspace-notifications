export function createDemoContext() {
  const listeners = new Map()
  const sessions = new Map()
  const agents = []
  const workspaces = [
    { id: 'alpha', title: 'Alpha', path: 'C:/demo/alpha', sessionIds: ['alpha-1'] },
    { id: 'beta', title: 'Beta', path: 'C:/demo/beta', sessionIds: ['beta-1', 'beta-2'] },
    { id: 'gamma', title: 'Гамма', path: 'C:/demo/gamma', sessionIds: ['gamma-1'] },
  ]
  const ctx = {
    logger: () => console,
    get(name) {
      if (name === 'workspaceRegistry') return { list: () => workspaces }
      if (name === 'agents') return {
        list: () => agents,
        turnBoundaryOf: () => undefined,
      }
      if (name === 'sessions') return { get: (id) => sessions.get(id) }
      return undefined
    },
    on(name, listener) {
      const values = listeners.get(name) || []
      values.push(listener)
      listeners.set(name, values)
    },
    emit(name, ...args) {
      return (listeners.get(name) || []).map((listener) => listener(...args))
    },
  }
  const add = (id, cwd, status = 'running') => {
    const session = { id, header: { cwd } }
    sessions.set(id, session)
    agents.push({ session, status })
    return session
  }
  return { ctx, add, agents, workspaces }
}

export function runDemo(ctx, add, agents) {
  const alpha = add('alpha-1', 'C:/demo/alpha')
  const betaOne = add('beta-1', 'C:/demo/beta')
  const betaTwo = add('beta-2', 'C:/demo/beta')
  const gamma = add('gamma-1', 'C:/demo/gamma')
  const now = Date.now()
  const turn = now
  const event = (session, type, data, offset) => ctx.emit('session/event', session, { type, data, time: now + offset })
  event(alpha, 'turn/start', { turn }, 0)
  event(alpha, 'user/message', { source: { kind: 'user' }, content: [{ type: 'text', text: 'Синтетическая задача Alpha' }] }, 1)
  event(alpha, 'turn/end', { turn, reason: { kind: 'completed' } }, 2000)
  agents.find((item) => item.session === alpha).status = 'idle'
  event(betaOne, 'turn/start', { turn }, 3)
  event(betaTwo, 'turn/start', { turn }, 4)
  event(betaOne, 'user/message', { source: { kind: 'user' }, content: [{ type: 'text', text: 'Синтетическая задача Beta' }] }, 5)
  event(betaOne, 'turn/end', { turn, reason: { kind: 'completed' } }, 1000)
  agents.find((item) => item.session === betaOne).status = 'idle'
  event(gamma, 'turn/start', { turn }, 6)
  event(gamma, 'user/message', { source: { kind: 'user' }, content: [{ type: 'text', text: 'Синтетическая ошибка Гамма' }] }, 7)
  event(gamma, 'turn/end', { turn, reason: { kind: 'error' } }, 3000)
  agents.find((item) => item.session === gamma).status = 'idle'
}
