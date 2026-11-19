import { humanizeWindow } from '../limits.mjs'

export const CODEX_AUTH_STATUS_URL = '/plugins/dsh-openai-codex/auth/status'

export function createCodexAuthStatusReader({ fetchImpl, enabled = false, cooldownMs = 60000 } = {}) {
  let lastAttempt = -Infinity
  let pending = null
  return async function readProviderQuota({ now = Date.now() } = {}) {
    if (!enabled || typeof fetchImpl !== 'function' || now - lastAttempt < cooldownMs) return null
    if (pending) return null
    lastAttempt = now
    pending = Promise.resolve().then(async () => {
      const response = await fetchImpl(CODEX_AUTH_STATUS_URL, { method: 'GET' })
      if (!response || !response.ok) return null
      const data = await response.json()
      if (!data || data.status !== 'signed-in') return null
      const rates = data.usage && Array.isArray(data.usage.rateLimits) ? data.usage.rateLimits : []
      const values = []
      for (const rate of rates) {
        if (!rate || !Array.isArray(rate.windows)) continue
        for (const item of rate.windows) values.push({
          windowSeconds: item.windowSeconds, percent: item.remainingPercent, resetAt: item.resetAt,
          label: rate.name || rate.id || humanizeWindow(item.windowSeconds),
        })
      }
      return values
    }).catch(() => null).finally(() => { pending = null })
    return pending
  }
}
