import test from 'node:test'
import assert from 'node:assert/strict'
import { DEEPSEEK_BALANCE_URL, ZAI_USAGE_URL, createProviderUsageReaders } from '../src/providerUsage.mjs'

function context({ settings = {}, providers = [], credentials = {}, launch = {} } = {}) {
  const calls = []
  return {
    calls,
    get(name) {
      if (name === 'settings') return { describe: ({ redactSecrets }) => {
        assert.equal(redactSecrets, true)
        return Object.entries(settings).map(([ns, value]) => ({ ns, value }))
      } }
      if (name === 'credentials') return { resolve: async (ref) => { calls.push(ref); return credentials[ref] } }
      if (name === 'launchEnvironment') return { get: (key) => launch[key] }
    },
    providers,
  }
}

test('DeepSeek preserves bounded USD and CNY decimal balances without summing them', async () => {
  const ctx = context({
    providers: [{ provider: 'deepseek-official', settingsNs: 'deep', settingsPath: [] }],
    settings: { deep: { apiKeyEnv: 'DEEP' } }, credentials: { DEEP: { value: 'fictional-deep-key' } },
  })
  const requests = []
  const readers = await createProviderUsageReaders({ ctx, configurableProviders: ctx.providers, fetchImpl: async (url, init) => {
    requests.push({ url, init })
    return Response.json({ is_available: false, balance_infos: [
      { currency: 'USD', total_balance: '21.906700', account_id: 'never-exported' },
      { currency: 'CNY', total_balance: '0.00' }, { currency: 'EUR', total_balance: '9.0' },
    ] })
  } })
  assert.deepEqual(await readers['deepseek-official'](), { amounts: [
    { currency: 'USD', balance: '21.906700', kind: 'wallet' }, { currency: 'CNY', balance: '0.00', kind: 'wallet' },
  ] })
  assert.equal(requests[0].url, DEEPSEEK_BALANCE_URL)
  assert.equal(requests[0].init.headers.Authorization, 'Bearer fictional-deep-key')
  assert.equal(requests[0].init.redirect, 'error')
  assert.deepEqual(ctx.calls, ['DEEP'])
})

test('Z.ai maps actual unit types to seconds and prefers authoritative count remaining', async () => {
  const ctx = context({
    providers: [{ provider: 'zai', settingsNs: 'zai', settingsPath: ['providers', 'zai'] }],
    settings: { zai: { providers: { zai: { apiKeyEnv: 'ZAI' } } } }, credentials: { ZAI: { value: 'fictional-zai-key' } },
  })
  const readers = await createProviderUsageReaders({ ctx, configurableProviders: ctx.providers, fetchImpl: async (url, init) => {
    assert.equal(url, ZAI_USAGE_URL); assert.equal(init.headers.Authorization, 'fictional-zai-key')
    return Response.json({ success: true, code: 200, data: { limits: [
      { type: 'CREDIT_LIMIT', unit: 3, number: 5, percentage: 27, nextResetTime: 1900000000000 },
      { type: 'TOKENS_LIMIT', unit: 6, number: 1, usage: 1000, remaining: 423, percentage: 57 },
      { type: 'CREDIT_LIMIT', unit: 1, number: 1, percentage: 50 },
      { type: 'TIME_LIMIT', unit: 1, number: 1, percentage: 50 }, { type: 'TOKENS_LIMIT', unit: 5, number: 1, percentage: 0 },
      { type: 'UNKNOWN', unit: 3, number: 1, used: 1 }, { type: 'TOKENS_LIMIT', unit: 99, number: 1, used: 1 },
    ] } })
  } })
  assert.deepEqual(await readers.zai(), { windows: [
    { percent: 73, windowSeconds: 18000, resetAt: 1900000000 }, { percent: 42.3, windowSeconds: 604800 }, { percent: 50, windowSeconds: 86400 },
  ] })
})

test('unsafe metadata, custom base, malformed responses, and missing credentials do not fetch', async () => {
  let calls = 0
  const ctx = context({
    providers: [
      { provider: 'deepseek-official', settingsNs: 'deep', settingsPath: [] },
      { provider: 'zai', settingsNs: 'zai', settingsPath: ['providers', 'zai'] },
    ], settings: { deep: { apiKeyEnv: 'DEEP', baseURL: 'https://proxy.invalid' }, zai: { providers: { zai: { apiKeyEnv: 'ZAI', baseURL: 'https://proxy.invalid' } } } },
    credentials: { DEEP: undefined, ZAI: { value: 'never-used' } }, launch: { DEEPSEEK_BASE_URL: { value: 'https://api.deepseek.com/anthropic' } },
  })
  const readers = await createProviderUsageReaders({ ctx, configurableProviders: ctx.providers, fetchImpl: async () => { calls += 1; return Response.json({}) } })
  assert.equal(readers['deepseek-official'], undefined)
  assert.equal(readers.zai, undefined)
  assert.equal(calls, 0)
})

test('per-route cache deduplicates pending reads and drops a result after its generation becomes inactive', async () => {
  let active = true; let resolveFetch; let calls = 0
  const ctx = context({ providers: [{ provider: 'deepseek-official', settingsNs: 'deep', settingsPath: [] }], settings: { deep: {} }, credentials: { DEEPSEEK_API_KEY: { value: 'fictional' } } })
  const readers = await createProviderUsageReaders({ ctx, configurableProviders: ctx.providers, current: () => active, fetchImpl: async () => {
    calls += 1; await new Promise(resolve => { resolveFetch = resolve }); return Response.json({ balance_infos: [{ currency: 'USD', total_balance: '1' }] })
  } })
  const first = readers['deepseek-official'](); const second = readers['deepseek-official']()
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(calls, 1)
  active = false; resolveFetch()
  assert.equal(await first, null); assert.equal(await second, null)
})
