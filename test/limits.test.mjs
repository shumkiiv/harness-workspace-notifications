import test from 'node:test'
import assert from 'node:assert/strict'
import { buildLimitsPayload, humanizeWindow, quotaTier } from '../src/limits.mjs'
import { createCodexAuthStatusReader, CODEX_AUTH_STATUS_URL } from '../src/quota/codexAuthStatus.mjs'

test('unknown providers stay explicitly unavailable', async () => {
  const result = await buildLimitsPayload({ listProviders: () => [{ id: 'other', name: 'Other' }], readers: {}, now: 100 })
  assert.deepEqual(result.providers, [{ id: 'other', label: 'Other', state: 'unavailable' }])
})

test('Codex reader normalizes, labels, and sorts genuine windows', async () => {
  const response = { status: 'signed-in', usage: { rateLimits: [{ windows: [
    { windowSeconds: 3600, remainingPercent: 50, resetAt: 200 }, { windowSeconds: 300, remainingPercent: 0, resetAt: 160 },
  ] }] } }
  const reader = createCodexAuthStatusReader({ enabled: true, fetchImpl: async () => ({ ok: true, json: async () => response }) })
  const result = await buildLimitsPayload({ listProviders: () => [{ id: 'codex', name: 'Codex' }], readers: { codex: reader }, now: 100 })
  assert.deepEqual(result.providers[0].windows.map((item) => item.label), ['5 мин', '1 ч'])
  assert.equal(result.providers[0].windows[0].percent, 0)
})

test('invalid quota values become unavailable and disabled reader never fetches', async () => {
  let calls = 0
  const reader = createCodexAuthStatusReader({ enabled: false, fetchImpl: async () => { calls += 1; return {} } })
  const result = await buildLimitsPayload({ listProviders: () => [{ id: 'codex', name: 'Codex' }], readers: { codex: reader }, now: 10 })
  assert.equal(calls, 0)
  assert.equal(result.providers[0].state, 'unavailable')
  assert.equal(humanizeWindow(86400), '24 ч')
  assert.equal(CODEX_AUTH_STATUS_URL, '/plugins/dsh-openai-codex/auth/status')
})

test('quota color tiers preserve true zero and exact boundaries', () => {
  assert.equal(quotaTier(0), 'low')
  assert.equal(quotaTier(24.99), 'low')
  assert.equal(quotaTier(25), 'medium')
  assert.equal(quotaTier(59.99), 'medium')
  assert.equal(quotaTier(60), 'high')
  assert.equal(quotaTier(null), 'unknown')
})
