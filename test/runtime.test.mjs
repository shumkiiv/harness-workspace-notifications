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
  const before = sent.filter((item) => item.t === 'settings.apply').length
  incoming({ t: 'settings.changed', settings: { FieldPath: false, WidgetX: 13, NotificationSound: true } })
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
  await runtime.close()
  fs.rmSync(temporary, { recursive: true, force: true })
})
