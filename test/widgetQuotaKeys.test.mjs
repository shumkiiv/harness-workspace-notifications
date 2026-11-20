import assert from 'node:assert/strict'
import test from 'node:test'
import { WidgetQuotaKeys } from '../src/widgetQuotaKeys.mjs'

test('uses an opaque correlated request and never places a key in a frame', async () => {
  const frames = []
  const keys = new WidgetQuotaKeys((frame) => frames.push(frame))
  assert.deepEqual(await keys.read('deepseek-official'), { state: 'pending' })
  keys.receive({ t: 'ready', quotaKeys: true })
  const pending = keys.read('deepseek-official')
  assert.deepEqual(frames, [{ t: 'quota.fetch', provider: 'deepseek-official', requestId: 'quota-1' }])
  keys.receive({ t: 'quota.result', provider: 'zai', requestId: 'quota-1', status: 'ok', payload: {} })
  keys.receive({ t: 'quota.result', provider: 'deepseek-official', requestId: 'quota-1', status: 'ok', payload: { balance_infos: [] } })
  assert.deepEqual(await pending, { state: 'override', payload: { balance_infos: [] } })
  assert.equal(JSON.stringify(frames).includes('key'), false)
})

test('falls back only when no widget key exists and reports a rejected widget key', async () => {
  const frames = []
  const keys = new WidgetQuotaKeys((frame) => frames.push(frame))
  keys.receive({ t: 'ready', quotaKeys: true })
  const absent = keys.read('zai')
  keys.receive({ t: 'quota.result', provider: 'zai', requestId: 'quota-1', status: 'absent' })
  assert.deepEqual(await absent, { state: 'absent' })
  keys.invalidate('zai')
  const rejected = keys.read('zai')
  keys.receive({ t: 'quota.result', provider: 'zai', requestId: 'quota-2', status: 'rejected' })
  assert.deepEqual(await rejected, { state: 'rejected' })
})

test('ignores unsupported providers, invalid result frames, and fails closed after invalidation', async () => {
  const frames = []
  const keys = new WidgetQuotaKeys((frame) => frames.push(frame))
  assert.deepEqual(await keys.read('other'), { state: 'absent' })
  assert.deepEqual(frames, [])
  keys.receive({ t: 'ready', quotaKeys: true })
  const pending = keys.read('deepseek-official')
  keys.invalidate('deepseek-official')
  keys.receive({ t: 'quota.result', provider: 'deepseek-official', requestId: 'quota-1', status: 'ok', payload: { balance_infos: [{ currency: 'USD', total_balance: '9' }] } })
  assert.deepEqual(await pending, { state: 'error' })
  const fresh = keys.read('deepseek-official')
  keys.receive({ t: 'quota.result', provider: 'deepseek-official', requestId: 'quota-2', status: 'absent' })
  assert.deepEqual(await fresh, { state: 'absent' })
})

test('fails closed when a native request times out after announcing support', async () => {
  let expire
  const keys = new WidgetQuotaKeys(() => true, { schedule: (callback) => { expire = callback; return 1 }, cancel: () => {} })
  keys.receive({ t: 'ready', quotaKeys: true })
  const pending = keys.read('zai')
  expire()
  assert.deepEqual(await pending, { state: 'error' })
})
