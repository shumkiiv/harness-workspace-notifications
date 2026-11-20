import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRuntime } from '../index.js'


test('runtime sends PascalCase settings once and persists UI changes without echo', async () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'dsh-workspace-test-'))
  const listeners = new Map()
  const session = { id: 's1', header: { cwd: 'C:/Alpha' } }
  const sent = []
  let incoming
  const helper = {
    send(message) { sent.push(message) },
    async close() {},
  }
  const ctx = {
    logger: () => ({ warn() {} }),
    get(name) {
      if (name === 'agents') return {
        list: () => [{ session, status: 'running' }],
        turnBoundaryOf: () => ({ lastTurn: 1, openTurnStartSeq: 1 }),
      }
      if (name === 'workspaceRegistry') return { list: () => [{ id: 'a', title: 'Alpha', path: 'C:/Alpha', sessionIds: ['s1'] }] }
    },
    on(name, listener) { listeners.set(name, listener) },
  }
  const runtime = createRuntime(ctx, {
    dataDir: temporary,
    createHelper(options) {
      assert.equal(options.dataDir, path.resolve(temporary))
      incoming = options.onMessage
      return helper
    },
  })
  assert.equal(sent[0].t, 'settings.apply')
  assert.equal(sent[0].settings.NotificationSound, false)
  assert.equal(sent[0].settings.NotificationSoundPreset, 'icq')
  const before = sent.filter((item) => item.t === 'settings.apply').length
  incoming({ t: 'settings.changed', settings: { FieldPath: false, WidgetX: 13, NotificationSound: true, NotificationSoundPreset: 'boing' } })
  assert.equal(sent.filter((item) => item.t === 'settings.apply').length, before)
  incoming({ t: 'widget.moved', x: 130, y: 131 })
  listeners.get('session/event')(session, { type: 'turn/end', data: { turn: 1, reason: { kind: 'completed' } }, time: 10 })
  const notice = sent.find((item) => item.t === 'notice.show')
  assert.equal(notice.duration, 'Время неизвестно')
  const rendered = sent.filter((item) => item.t === 'widget.render').at(-1).rows[0]
  assert.equal(rendered.completedCount, 1)
  assert.equal(rendered.taskSummary, 'Задача не указана')
  listeners.get('session/event')(session, { type: 'turn/end', data: { turn: 1, reason: { kind: 'completed' } }, time: 11 })
  assert.equal(sent.filter((item) => item.t === 'notice.show').length, 1)
  assert.equal(runtime.settings().WidgetX, 130)
  const persisted = JSON.parse(fs.readFileSync(path.join(temporary, 'settings.json'), 'utf8'))
  assert.equal(persisted.FieldPath, false)
  assert.equal(persisted.NotificationSound, true)
  assert.equal(persisted.NotificationSoundPreset, 'boing')
  incoming({ t: 'settings.changed', settings: { NotificationSound: true, NotificationSoundPreset: 'old-or-invalid' } })
  assert.equal(runtime.settings().NotificationSoundPreset, 'icq')
  await runtime.close()
  fs.rmSync(temporary, { recursive: true, force: true })
})

test('updates the signed-in DeepSeek balance as soon as the saved account changes', async (t) => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'dsh-account-test-'))
  fs.writeFileSync(path.join(temporary, 'settings.json'), JSON.stringify({ ShowLimits: true }))
  const listeners = new Map()
  const frames = []
  const pending = []
  const runtime = createRuntime({
    on(name, handler) { listeners.set(name, handler) },
    get(name) {
      if (name === 'llm') return { listProviders: () => [] }
      if (name === 'clientModules') return { graph: () => ({ release: { version: 'test' } }) }
      if (name === 'deepseekAccount') return { getBalance: () => new Promise(resolve => pending.push(resolve)) }
    },
  }, {
    dataDir: temporary,
    createHelper: () => ({ send: message => frames.push(message), close: async () => {} }),
  })
  t.after(async () => { await runtime.close(); fs.rmSync(temporary, { recursive: true, force: true }) })
  const idle = () => new Promise(resolve => setImmediate(resolve))
  const answer = balance => ({ status: 'ready', value: [{ currency: 'USD', balance }] })
  await idle()
  assert.equal(pending.length, 1)
  assert.equal(typeof listeners.get('credentials/record-updated'), 'function')
  listeners.get('credentials/record-updated')('deepseek-account-platform/default')
  await idle()
  assert.equal(pending.length, 2)
  pending[1](answer('9'))
  await idle()
  pending[0](answer('2'))
  await idle()
  const payload = frames.filter(frame => frame.t === 'widget.limits').at(-1)
  assert.equal(payload.providers.find(row => row.id === 'deepseek-account').amounts[0].balance, '9')
  assert.equal(frames.some(frame => frame.t === 'widget.limits' &&
    frame.providers.some(row => row.amounts?.some(amount => amount.balance === '2'))), false)
})
