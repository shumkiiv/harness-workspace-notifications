export const DEEPSEEK_BALANCE_URL = 'https://api.deepseek.com/user/balance'
export const ZAI_USAGE_URL = 'https://api.z.ai/api/monitor/usage/quota/limit'

const TTL_MS = 60_000
const MAX_ITEMS = 32
const decimal = /^-?\d{1,18}(?:\.\d{1,12})?$/
const secondsPerUnit = new Map([[3, 3600], [6, 604800], [1, 86400]])

function own(object, key) {
  return object && typeof object === 'object' && Object.prototype.hasOwnProperty.call(object, key)
}

function atPath(value, path) {
  let current = value
  for (const key of path) {
    if (typeof key !== 'string' || !own(current, key)) return undefined
    current = current[key]
  }
  return current
}

function descriptor(descriptors, provider, settingsPath) {
  if (!provider || typeof provider.settingsNs !== 'string' || !Array.isArray(settingsPath)) return undefined
  const matches = descriptors.filter((item) => item && item.ns === provider.settingsNs)
  if (matches.length !== 1) return undefined
  return atPath(matches[0].value, settingsPath)
}

function canonicalBase(value, expected) {
  if (typeof value !== 'string' || !value) return false
  try {
    const url = new URL(value)
    const canonical = new URL(expected)
    return url.protocol === 'https:' && !url.username && !url.password && !url.search && !url.hash
      && url.origin === canonical.origin && url.pathname.replace(/\/$/, '') === canonical.pathname.replace(/\/$/, '')
  } catch { return false }
}

function resetAt(value) {
  if (!Number.isFinite(value) || !Number.isInteger(value) || value < 946684800000 || value > 4102444800000) return undefined
  return Math.floor(value / 1000)
}

function amountResponse(body) {
  if (!body || !Array.isArray(body.balance_infos) || body.balance_infos.length > MAX_ITEMS) return null
  const amounts = body.balance_infos.flatMap((item) => item && (item.currency === 'USD' || item.currency === 'CNY')
    && typeof item.total_balance === 'string' && decimal.test(item.total_balance)
    ? [{ currency: item.currency, balance: item.total_balance, kind: 'wallet' }] : [])
  return amounts.length ? { amounts } : null
}

function remainingPercent(item) {
  const total = Number.isFinite(item?.totalValue) ? item.totalValue : item?.usage
  if (Number.isFinite(total) && total > 0 && Number.isFinite(item?.remaining) && item.remaining >= 0 && item.remaining <= total) {
    return Math.max(0, Math.min(100, (item.remaining / total) * 100))
  }
  if (Number.isFinite(total) && total > 0 && Number.isFinite(item?.currentValue) && item.currentValue >= 0 && item.currentValue <= total) {
    return Math.max(0, Math.min(100, ((total - item.currentValue) / total) * 100))
  }
  return Number.isFinite(item?.percentage) && item.percentage >= 0 && item.percentage <= 100 ? 100 - item.percentage : undefined
}

function usageResponse(body) {
  if (!body || body.success !== true || body.code !== 200 || !body.data || !Array.isArray(body.data.limits) || body.data.limits.length > MAX_ITEMS) return null
  const windows = []
  for (const item of body.data.limits) {
    if (!item || !['TOKENS_LIMIT', 'CREDIT_LIMIT'].includes(item.type)) continue
    const seconds = secondsPerUnit.get(item.unit)
    if (!seconds || !Number.isInteger(item.number) || item.number < 1 || item.number > 1000) continue
    const percent = remainingPercent(item)
    if (!Number.isFinite(percent)) continue
    const window = { percent, windowSeconds: seconds * item.number }
    const reset = resetAt(item.nextResetTime)
    if (reset !== undefined) window.resetAt = reset
    windows.push(window)
  }
  return windows.length ? { windows } : null
}

async function request({ cache, key, eligible, resolve, fetchImpl, url, authorization, parse }) {
  if (!eligible()) return null
  const clock = Date.now()
  const cached = cache.get(key)
  if (cached?.pending) return cached.pending
  if (cached && clock - cached.at <= TTL_MS) return cached.value
  const pending = (async () => {
    try {
      const resolved = await resolve()
      if (!eligible() || !resolved || typeof resolved.value !== 'string' || !resolved.value) return null
      const controller = new AbortController()
      const timeout = setTimeout(() => controller.abort(), 10_000)
      try {
        const response = await fetchImpl(url, { method: 'GET', headers: { Accept: 'application/json', Authorization: authorization(resolved.value) }, redirect: 'error', signal: controller.signal })
        if (!response?.ok) return null
        return parse(await response.json())
      } finally { clearTimeout(timeout) }
    } catch { return null }
  })()
  cache.set(key, { at: clock, value: null, pending })
  const value = await pending
  cache.set(key, { at: Date.now(), value, pending: null })
  return value
}

export async function createProviderUsageReaders({ ctx, configurableProviders, current = () => true, eligible = current, fetchImpl = globalThis.fetch, cache = new Map() } = {}) {
  if (!ctx || !Array.isArray(configurableProviders) || typeof fetchImpl !== 'function') return {}
  let descriptors
  try { descriptors = await ctx.get?.('settings')?.describe?.({ redactSecrets: true }) } catch { return {} }
  if (!Array.isArray(descriptors)) return {}
  const credentials = ctx.get?.('credentials')
  if (!credentials || typeof credentials.resolve !== 'function') return {}
  const reader = (id, expectedBase, defaultRef, url, authorization, parse, overrideBase) => {
    const metadata = configurableProviders.filter((item) => item?.provider === id)
    if (metadata.length !== 1 || !Array.isArray(metadata[0].settingsPath)) return null
    const value = descriptor(descriptors, metadata[0], metadata[0].settingsPath)
    if (!value || typeof value !== 'object') return null
    if (own(value, 'apiKeyEnv') && (typeof value.apiKeyEnv !== 'string' || !value.apiKeyEnv)) return null
    const reference = own(value, 'apiKeyEnv') ? value.apiKeyEnv : defaultRef
    const base = own(value, 'baseURL') ? value.baseURL : (overrideBase ?? metadata[0].defaultBaseURL ?? expectedBase)
    if (!canonicalBase(base, expectedBase)) return null
    const key = `${id}\u0000${reference}\u0000${expectedBase}`
    return async () => {
      const result = await request({ cache, key, eligible: () => eligible(id), resolve: () => credentials.resolve(reference), fetchImpl, url, authorization, parse })
      return current() ? result : null
    }
  }
  let deepseekOverride
  try { deepseekOverride = ctx.get?.('launchEnvironment')?.get?.('DEEPSEEK_BASE_URL')?.value } catch { return {} }
  const deepseek = reader('deepseek-official', 'https://api.deepseek.com/anthropic', 'DEEPSEEK_API_KEY', DEEPSEEK_BALANCE_URL, (key) => `Bearer ${key}`, amountResponse, deepseekOverride)
  const zai = reader('zai', 'https://api.z.ai/api/coding/paas/v4', 'ZAI_API_KEY', ZAI_USAGE_URL, (key) => key, usageResponse)
  return { ...(deepseek ? { 'deepseek-official': deepseek } : {}), ...(zai ? { zai } : {}) }
}
