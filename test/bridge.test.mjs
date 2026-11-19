import test from 'node:test'
import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { setupQuotaBridge } from '../src/quota/bridge.mjs'

function fixture(settings = { ShowLimits: true }) {
  let route; let removed = false; let clock = 100
  const ctx = { get(name) {
    if (name === 'webServer') return { getOrigin: () => 'http://127.0.0.1:8811', register: (r) => { route = r; return () => { removed = true } } }
    if (name === 'connection') return { requestRejection: (req) => req.reject }
  } }
  return { bridge: setupQuotaBridge(ctx, () => settings, () => clock), route: () => route, removed: () => removed, advance: (n) => { clock += n } }
}
async function request(handler, { method = 'GET', headers = {}, body = '', address = '127.0.0.1', reject } = {}) {
  const req = new EventEmitter(); req.method = method; req.headers = headers; req.socket = { remoteAddress: address }; req.reject = reject
  let status; let text = ''; const res = { writeHead: (s) => { status = s }, end: (v) => { text = v || '' } }
  const pending = handler(req, res); req.emit('data', body); req.emit('end'); await pending
  return { status, value: text ? JSON.parse(text) : {} }
}

test('quota bridge fails closed and reports opt-in only', async () => {
  const f = fixture({ ShowLimits: false })
  assert.equal(f.route().path, '/plugins/@local/dsh-workspace-notifications/quota/bridge')
  assert.equal((await request(f.route().handler, { headers: { host: '127.0.0.1:8811' } })).value.enabled, false)
  assert.equal((await request(f.route().handler, { reject: 401 })).status, 401)
  assert.equal((await request(f.route().handler, { address: '10.0.0.2' })).status, 403)
  assert.equal((await request(f.route().handler, { method: 'POST', headers: { origin: 'http://127.0.0.1:8811', host: '127.0.0.1:8811' }, body: '{}' })).status, 403)
})

test('quota bridge stores only bounded projected windows then expires and disposes', async () => {
  const f = fixture(); const headers = { origin: 'http://127.0.0.1:8811', host: '127.0.0.1:8811', 'content-type': 'application/json' }
  const body = JSON.stringify({ provider: 'openai-codex', windows: [{ percent: 0, windowSeconds: 300, resetAt: null }] })
  assert.equal((await request(f.route().handler, { method: 'POST', headers, body })).status, 204)
  assert.deepEqual(await f.bridge.read(), [{ percent: 0, windowSeconds: 300, resetAt: null }])
  assert.equal((await request(f.route().handler, { method: 'POST', headers: { ...headers, origin: 'http://bad' }, body })).status, 403)
  assert.equal((await request(f.route().handler, { method: 'POST', headers, body: 'x'.repeat(4097) })).status, 413)
  f.advance(600001); assert.equal(await f.bridge.read(), null)
  f.bridge.close(); assert.equal(f.removed(), true)
})
