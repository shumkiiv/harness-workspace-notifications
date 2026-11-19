import test from 'node:test'
import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { setupSessionOpenBridge } from '../src/sessionOpen.mjs'

function fixture(admit = () => ({ peer: { principal: { kind: 'owner' } } })) {
  let route
  const socket = { remoteAddress: '127.0.0.1' }
  const ctx = { get(name) {
    if (name === 'webServer') return { getOrigin: () => 'http://127.0.0.1:8811', register: (value) => { route = value; return () => {} } }
    if (name === 'connection') return { admit }
  } }
  return { bridge: setupSessionOpenBridge(ctx), route: () => route, socket }
}
function request(handler, options = {}) {
  const req = new EventEmitter(); req.method = options.method || 'POST'; req.headers = options.headers || { host: '127.0.0.1:8811' }; req.socket = options.socket
  const result = {}; const res = { writeHead(status, headers) { result.status = status; result.headers = headers }, end(value = '') { result.body = value; result.done?.() } }
  const pending = handler(req, res)
  if (options.body !== undefined) { if (options.body !== '') req.emit('data', options.body); req.emit('end') }
  return { req, res, result, pending }
}

test('delivers only a displayed registered exact id to an authenticated owner waiter', async () => {
  const f = fixture(); const id = 'session-123'
  f.bridge.update({ registry: [id], displayed: [id] })
  assert.equal(await f.bridge.accept({ t: 'session.open', sessionId: 'unknown' }), false)
  assert.equal(await f.bridge.accept({ t: 'session.open', sessionId: id }), true)
  const call = request(f.route().handler, { socket: f.socket, body: '' })
  await call.pending
  assert.equal(call.result.status, 200)
  assert.equal(call.result.headers['cache-control'], 'no-store')
  assert.deepEqual(JSON.parse(call.result.body), { sessionId: id })
  f.bridge.close()
})

test('rejects non-owner, foreign origin, non-empty bodies, and non-POST waits', async () => {
  const f = fixture(() => ({ peer: { principal: { kind: 'user' } } }))
  const handler = f.route().handler
  let call = request(handler, { socket: f.socket, body: '' }); await call.pending; assert.equal(call.result.status, 403)
  f.bridge.close()
})

test('closed route answers a request whose EOF arrives after disposal', async () => {
  const f = fixture(); const call = request(f.route().handler, { socket: f.socket })
  f.bridge.close(); call.req.emit('end'); await call.pending
  assert.equal(call.result.status, 503)
})

test('an invalid message does not supersede an in-flight valid existence check', async () => {
  let release; const exists = () => new Promise((resolve) => { release = resolve })
  const f = fixture(); f.bridge.close()
  const route = { getOrigin: () => 'http://127.0.0.1:8811', register: () => () => {} }
  const bridge = setupSessionOpenBridge({ get: (name) => name === 'webServer' ? route : { admit: () => ({ peer: { principal: { kind: 'owner' } } }) } }, () => 1, exists)
  bridge.update({ displayed: ['valid'] })
  const pending = bridge.accept({ t: 'session.open', sessionId: 'valid' })
  assert.equal(await bridge.accept({ t: 'session.open', sessionId: 'not visible' }), false)
  release(true); assert.equal(await pending, true); bridge.close()
})
