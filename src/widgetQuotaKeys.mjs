const PROVIDERS = new Set(['deepseek-official', 'zai'])
const TTL_MS = 60_000

function result(state, payload) {
  return payload === undefined ? { state } : { state, payload }
}

export class WidgetQuotaKeys {
  constructor(send, { timeout = 15_000, schedule = setTimeout, cancel = clearTimeout } = {}) {
    this.send = typeof send === 'function' ? send : () => false
    this.timeout = timeout
    this.schedule = schedule
    this.cancel = cancel
    this.capability = 'unknown'
    this.nextId = 1
    this.pending = new Map()
    this.pendingByProvider = new Map()
    this.cache = new Map()
  }

  receive(message) {
    if (message?.t === 'ready') {
      this.capability = message.quotaKeys === true ? 'available' : 'legacy'
      return false
    }
    if (message?.t !== 'quota.result' || !PROVIDERS.has(message.provider) || typeof message.requestId !== 'string') return false
    const pending = this.pending.get(message.requestId)
    if (!pending || pending.provider !== message.provider) return false
    if (!['ok', 'absent', 'rejected', 'error'].includes(message.status)) return false
    this.pending.delete(message.requestId)
    this.pendingByProvider.delete(message.provider)
    this.cancel(pending.timer)
    const value = message.status === 'ok' && message.payload && typeof message.payload === 'object'
      ? result('override', message.payload)
      : result(message.status === 'ok' ? 'error' : message.status)
    this.cache.set(message.provider, { at: Date.now(), value })
    pending.resolve(value)
    return true
  }

  read(provider) {
    if (!PROVIDERS.has(provider) || this.capability === 'legacy') return Promise.resolve(result('absent'))
    if (this.capability !== 'available') return Promise.resolve(result('pending'))
    const cached = this.cache.get(provider)
    if (cached && Date.now() - cached.at <= TTL_MS) return Promise.resolve(cached.value)
    const active = this.pendingByProvider.get(provider)
    if (active) return active.promise
    const requestId = `quota-${this.nextId++}`
    const promise = new Promise((resolve) => {
      const timer = this.schedule(() => {
        if (this.pending.delete(requestId)) {
          this.pendingByProvider.delete(provider)
          const value = result('error')
          this.cache.set(provider, { at: Date.now(), value })
          resolve(value)
        }
      }, this.timeout)
      this.pending.set(requestId, { provider, resolve, timer, promise: null })
      if (this.send({ t: 'quota.fetch', provider, requestId }) === false) {
        this.pending.delete(requestId)
        this.cancel(timer)
        const value = result('error')
        this.cache.set(provider, { at: Date.now(), value })
        resolve(value)
      }
    })
    const pending = this.pending.get(requestId)
    if (pending) {
      pending.promise = promise
      this.pendingByProvider.set(provider, { requestId, promise })
    }
    return promise
  }

  invalidate(provider) {
    if (!PROVIDERS.has(provider)) return
    this.cache.delete(provider)
    for (const [requestId, pending] of this.pending) {
      if (pending.provider !== provider) continue
      this.pending.delete(requestId)
      this.pendingByProvider.delete(provider)
      this.cancel(pending.timer)
      pending.resolve(result('error'))
    }
  }

  close() {
    for (const [requestId, pending] of this.pending) {
      this.pending.delete(requestId)
      this.pendingByProvider.delete(pending.provider)
      this.cancel(pending.timer)
      pending.resolve(result('error'))
    }
  }
}
