export function humanizeWindow(seconds) {
  const labels = { 300: '5 мин', 3600: '1 ч', 86400: '24 ч', 604800: '7 дней' }
  return labels[seconds] || `${seconds} с`
}

export function quotaTier(percent) {
  if (!Number.isFinite(percent) || percent < 0 || percent > 100) return 'unknown'
  if (percent < 25) return 'low'
  if (percent < 60) return 'medium'
  return 'high'
}

function unavailable(provider) {
  return { id: String(provider.id || ''), label: String(provider.name || provider.label || provider.id || ''), state: 'unavailable' }
}

export async function buildLimitsPayload({ listProviders, readers = {}, now = Date.now() }) {
  let providers
  try { providers = await listProviders() } catch { return { providers: [] } }
  if (!Array.isArray(providers)) return { providers: [] }
  const result = []
  for (const provider of providers) {
    if (!provider || !provider.id) continue
    const reader = readers[provider.id]
    if (!reader) { result.push(unavailable(provider)); continue }
    let windows
    try { windows = await reader({ now, provider }) } catch { windows = null }
    const valid = Array.isArray(windows) ? windows.filter((item) => item && Number.isFinite(item.percent)
      && item.percent >= 0 && item.percent <= 100 && Number.isFinite(item.windowSeconds) && item.windowSeconds > 0
      ).sort((a, b) => a.windowSeconds - b.windowSeconds) : []
    result.push(valid.length ? {
      id: String(provider.id), label: String(provider.name || provider.label || provider.id), state: 'ok',
      windows: valid.map((item) => ({ label: item.label || humanizeWindow(item.windowSeconds), percent: item.percent,
        windowSeconds: item.windowSeconds, resetAt: Number.isFinite(item.resetAt) ? item.resetAt : null })),
    } : unavailable(provider))
  }
  return { providers: result, receivedAt: now }
}
