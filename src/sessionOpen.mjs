const PATH = '/plugins/@local/dsh-workspace-notifications/session/wait'
const WAIT_MS = 20_000
const TARGET_TTL = 30_000
const ID = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/

function local(req) {
  const address = req.socket?.remoteAddress
  return address === '127.0.0.1' || address === '::1' || address === '::ffff:127.0.0.1'
}
function reply(res, status, value, headers = {}) {
  res.writeHead(status, { 'cache-control': 'no-store', ...(value === undefined ? {} : { 'content-type': 'application/json' }), ...headers })
  res.end(value === undefined ? '' : JSON.stringify(value))
}

export function setupSessionOpenBridge(ctx, now = () => Date.now(), sessionExists = async () => true) {
  const web = ctx.get?.('webServer'); const connection = ctx.get?.('connection')
  if (!web || !connection || typeof web.register !== 'function' || typeof connection.admit !== 'function') return { update() {}, accept() { return false }, close() {} }
  let closed = false; let target = null; let targetTimer = null; let lastDelivered = null; let acceptSequence = 0
  const displayed = new Set(); const waiters = new Map()
  const validOrigin = (req) => {
    let origin; let url
    try { origin = web.getOrigin(); url = new URL(origin) } catch { return null }
    if (!['127.0.0.1', '[::1]', 'localhost'].includes(url.hostname) || req.headers.host !== url.host) return null
    if (req.headers.origin !== undefined && req.headers.origin !== origin) return null
    if (req.headers['sec-fetch-site'] === 'cross-site') return null
    return origin
  }
  const finish = (waiter, status, value, headers) => {
    if (!waiter || waiter.finished) return
    waiter.finished = true; clearTimeout(waiter.timer); waiters.delete(waiter.carrier)
    reply(waiter.res, status, value, headers)
  }
  const deliver = () => {
    if (!target || target.expires <= now() || !displayed.has(target.sessionId)) { target = null; clearTimeout(targetTimer); targetTimer = null; return }
    const waiter = waiters.values().next().value
    if (!waiter) return
    const value = target.sessionId; target = null; clearTimeout(targetTimer); targetTimer = null; lastDelivered = { sessionId: value, at: now() }; finish(waiter, 200, { sessionId: value })
  }
  const registeredDispose = web.register({ kind: 'exact', path: PATH, handler: async (req, res) => {
    if (closed) return reply(res, 503, { error: 'closed' })
    if (req.method !== 'POST') return reply(res, 405, { error: 'method' }, { allow: 'POST' })
    let admission; try { admission = connection.admit(req) } catch { return reply(res, 403, { error: 'forbidden' }) }
    if (admission?.rejection !== undefined || admission?.peer?.principal?.kind !== 'owner' || !local(req) || !validOrigin(req)) return reply(res, admission?.rejection || 403, { error: 'forbidden' })
    let done = false
    const complete = () => {
      if (done) return
      if (closed) { done = true; return reply(res, 503, { error: 'closed' }) }
      done = true
      const carrier = req.socket || req
      const previous = waiters.get(carrier); if (previous) finish(previous, 204)
      if (waiters.size >= 2) return reply(res, 429, { error: 'busy' }, { 'retry-after': '2' })
      const waiter = { carrier, res, finished: false, timer: setTimeout(() => finish(waiter, 204), WAIT_MS) }
      waiters.set(carrier, waiter)
      req.once?.('aborted', () => { if (!waiter.finished) { waiter.finished = true; clearTimeout(waiter.timer); waiters.delete(carrier) } })
      req.signal?.addEventListener?.('abort', () => { if (!waiter.finished) { waiter.finished = true; clearTimeout(waiter.timer); waiters.delete(carrier) } }, { once: true })
      res.once?.('close', () => { if (!waiter.finished) { waiter.finished = true; clearTimeout(waiter.timer); waiters.delete(carrier) } })
      deliver()
    }
    if (Number(req.headers['content-length'] || 0) > 0) return reply(res, 400, { error: 'body' })
    req.once('data', () => { if (!done) { done = true; reply(res, 400, { error: 'body' }) } })
    req.once('end', complete)
    req.once('error', () => { done = true })
  } })
  return {
    update({ displayed: visible = [] } = {}) { displayed.clear(); visible.forEach((id) => displayed.add(String(id))); if (target && !displayed.has(target.sessionId)) { target = null; clearTimeout(targetTimer); targetTimer = null } },
    async accept(message) {
      const id = message?.sessionId
      if (message?.t !== 'session.open' || typeof id !== 'string' || !ID.test(id) || !displayed.has(id)) return false
      const sequence = ++acceptSequence
      if (!await sessionExists(id)) return false
      if (closed || sequence !== acceptSequence || !displayed.has(id)) return false
      if ((target?.sessionId === id && now() - target.at <= 1000) || (lastDelivered?.sessionId === id && now() - lastDelivered.at <= 1000)) return false
      target = { sessionId: id, at: now(), expires: now() + TARGET_TTL }; clearTimeout(targetTimer); targetTimer = setTimeout(() => { if (target?.sessionId === id) target = null }, TARGET_TTL); deliver(); return true
    },
    close() { if (closed) return; closed = true; target = null; clearTimeout(targetTimer); targetTimer = null; for (const waiter of [...waiters.values()]) finish(waiter, 204); registeredDispose() },
    path: PATH,
  }
}
