const PATH = '/plugins/@local/dsh-workspace-notifications/quota/bridge'
const MAX_BODY = 4096
const TTL = 10 * 60 * 1000
const DECIMAL = /^-?\d{1,18}(?:\.\d{1,12})?$/

function reply(res, code, value) { res.writeHead(code, { 'content-type': 'application/json' }); res.end(JSON.stringify(value)) }
function local(req) {
  const address = req.socket && req.socket.remoteAddress
  return address === '127.0.0.1' || address === '::1' || address === '::ffff:127.0.0.1'
}
function valid(payload) {
  if (!payload || payload.provider !== 'openai-codex' || !Array.isArray(payload.windows) || payload.windows.length > 8) return false
  return Object.keys(payload).every((key) => key === 'provider' || key === 'windows' || key === 'credits' || key === 'individualLimit') && payload.windows.every((w) => w && Object.keys(w).every((key) => key === 'percent' || key === 'windowSeconds' || key === 'resetAt') && Number.isFinite(w.percent) && w.percent >= 0 && w.percent <= 100
    && Number.isInteger(w.windowSeconds) && w.windowSeconds > 0 && w.windowSeconds <= 1e9
    && (w.resetAt == null || (Number.isInteger(w.resetAt) && w.resetAt >= 0 && w.resetAt <= 1e10)))
}
function rich(payload) {
  const result = {}
  const credits = payload && payload.credits
  if (credits && typeof credits === 'object' && !Array.isArray(credits)
    && Object.keys(credits).every((key) => key === 'unlimited' || key === 'balance')
    && (credits.unlimited === true || (typeof credits.balance === 'string' && DECIMAL.test(credits.balance)))) {
    result.credits = { unlimited: credits.unlimited === true }
    if (typeof credits.balance === 'string' && DECIMAL.test(credits.balance)) result.credits.balance = credits.balance
  }
  const limit = payload && payload.individualLimit
  if (limit && typeof limit === 'object' && !Array.isArray(limit) && Object.keys(limit).every((key) => key === 'limit' || key === 'used' || key === 'remaining' || key === 'remainingPercent')) {
    const copy = {}
    for (const key of ['limit', 'used', 'remaining']) if (typeof limit[key] === 'string' && DECIMAL.test(limit[key])) copy[key] = limit[key]
    if (Number.isFinite(limit.remainingPercent) && limit.remainingPercent >= 0 && limit.remainingPercent <= 100) copy.remainingPercent = limit.remainingPercent
    if (Object.keys(copy).length) result.individualLimit = copy
  }
  return result
}

export function setupQuotaBridge(ctx, getSettings, now = () => Date.now(), onAccepted = () => {}) {
  const web = ctx.get?.('webServer'); const connection = ctx.get?.('connection')
  if (!web || !connection || typeof web.register !== 'function' || typeof connection.requestRejection !== 'function') return { read: async () => null, close() {} }
  let cache = null; let closed = false
  const registeredDispose = web.register({ kind: 'exact', path: PATH, handler: async (req, res) => {
    let origin; let originUrl
    try { origin = web.getOrigin(); originUrl = new URL(origin) } catch { return reply(res, 403, { error: 'forbidden' }) }
    if (originUrl.hostname !== '127.0.0.1' && originUrl.hostname !== '[::1]' && originUrl.hostname !== 'localhost') return reply(res, 403, { error: 'forbidden' })
    let rejection; try { rejection = connection.requestRejection(req) } catch { return reply(res, 403, { error: 'forbidden' }) }
    if (rejection !== undefined || !local(req) || req.headers.host !== originUrl.host) return reply(res, rejection || 403, { error: 'forbidden' })
    if (req.method === 'GET') return reply(res, 200, { enabled: Boolean(getSettings().ShowLimits) && !(getSettings().HiddenLimitProviders || []).includes('openai-codex') })
    // The official Desktop proxy strips Origin before forwarding an authenticated local request.
    const requestOrigin = req.headers.origin
    const sameOrigin = requestOrigin === undefined || requestOrigin === origin
    if (req.method !== 'POST' || !getSettings().ShowLimits || !sameOrigin || req.headers['sec-fetch-site'] === 'cross-site'
      || !/^application\/json(?:\s*;|$)/i.test(String(req.headers['content-type'] || ''))) return reply(res, 403, { error: 'forbidden' })
    let body = ''; let bytes = 0; let finished = false
    req.on('data', (chunk) => { if (finished) return; bytes += Buffer.byteLength(chunk); if (bytes > MAX_BODY) { finished = true; return reply(res, 413, { error: 'too-large' }) } body += chunk })
    req.on('aborted', () => { finished = true })
    req.on('error', () => { finished = true })
    req.on('end', () => {
      if (finished) return;
      let payload; try { payload = JSON.parse(body) } catch { return reply(res, 400, { error: 'invalid' }) }
      if (!getSettings().ShowLimits || !valid(payload)) return reply(res, 400, { error: 'invalid' })
      if (closed || !getSettings().ShowLimits) return reply(res, 403, { error: 'forbidden' })
      const details = rich(payload)
      cache = { windows: payload.windows.map((w) => ({ percent: w.percent, windowSeconds: w.windowSeconds, resetAt: w.resetAt == null ? null : w.resetAt })), credits: details.credits, individualLimit: details.individualLimit, at: now() }
      reply(res, 204, {})
      try { onAccepted() } catch {}
    })
  } })
  let disposed = false
  const dispose = () => { if (disposed) return; disposed = true; registeredDispose() }
  return {
    async read() {
      if (closed || !getSettings().ShowLimits) { cache = null; return null }
      if (!cache || now() - cache.at > TTL) return null
      const windows = cache.windows.map((w) => ({ ...w }))
      return cache.credits || cache.individualLimit ? { windows, credits: cache.credits, individualLimit: cache.individualLimit } : windows
    },
    close() { closed = true; cache = null; dispose() }, path: PATH,
  }
}
