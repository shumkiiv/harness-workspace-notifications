const PATH = '/plugins/@local/dsh-workspace-notifications/quota/bridge'
const MAX_BODY = 4096
const TTL = 10 * 60 * 1000

function reply(res, code, value) { res.writeHead(code, { 'content-type': 'application/json' }); res.end(JSON.stringify(value)) }
function local(req) {
  const address = req.socket && req.socket.remoteAddress
  return address === '127.0.0.1' || address === '::1' || address === '::ffff:127.0.0.1'
}
function valid(payload) {
  if (!payload || payload.provider !== 'openai-codex' || !Array.isArray(payload.windows) || payload.windows.length > 8) return false
  return Object.keys(payload).every((key) => key === 'provider' || key === 'windows') && payload.windows.every((w) => w && Object.keys(w).every((key) => key === 'percent' || key === 'windowSeconds' || key === 'resetAt') && Number.isFinite(w.percent) && w.percent >= 0 && w.percent <= 100
    && Number.isInteger(w.windowSeconds) && w.windowSeconds > 0 && w.windowSeconds <= 1e9
    && (w.resetAt == null || (Number.isInteger(w.resetAt) && w.resetAt >= 0 && w.resetAt <= 1e10)))
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
    if (req.method === 'GET') return reply(res, 200, { enabled: Boolean(getSettings().ShowLimits) })
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
      cache = { windows: payload.windows.map((w) => ({ percent: w.percent, windowSeconds: w.windowSeconds, resetAt: w.resetAt == null ? null : w.resetAt })), at: now() }
      reply(res, 204, {})
      try { onAccepted() } catch {}
    })
  } })
  let disposed = false
  const dispose = () => { if (disposed) return; disposed = true; registeredDispose() }
  return {
    async read() { if (closed || !getSettings().ShowLimits) { cache = null; return null } return cache && now() - cache.at <= TTL ? cache.windows.map((w) => ({ ...w })) : null },
    close() { closed = true; cache = null; dispose() }, path: PATH,
  }
}
