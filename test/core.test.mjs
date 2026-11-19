import test from 'node:test'
import assert from 'node:assert/strict'
import {
  Tracker,
  aggregate,
  basename,
  reasonText,
  summary,
  workspaceFor,
} from '../src/core.mjs'
import { DEFAULT_SETTINGS, normalizeSettings } from '../src/store.mjs'

test('uses honest Russian labels and short summaries', () => {
  assert.equal(reasonText('completed'), 'Завершено')
  assert.equal(reasonText('error'), 'Ошибка')
  assert.equal(reasonText('unknown'), 'Результат неизвестен')
  assert.equal(summary(' a\n b '), 'a b')
  assert.equal(summary('x'.repeat(250)).length, 200)
})

test('notifies exactly once for an active baseline and ignores closed history', () => {
  const active = new Tracker({ turnBoundaryOf: () => ({ lastTurn: 7, openTurnStartSeq: 4 }) })
  active.observe({ id: 'active', header: {} })
  assert.equal(active.end({ id: 'active', header: {} }, 7, 'completed', 1).emit, true)
  assert.equal(active.end({ id: 'active', header: {} }, 7, 'completed', 2).emit, false)
  const closed = new Tracker({ turnBoundaryOf: () => ({ lastTurn: 7, openTurnStartSeq: null }) })
  closed.observe({ id: 'closed', header: {} })
  assert.equal(closed.end({ id: 'closed', header: {} }, 7, 'completed', 1).emit, false)
})

test('suppresses unmatched historical end without poisoning a later new start', () => {
  const tracker = new Tracker({})
  const session = { id: 'one', header: {} }
  tracker.observe(session)
  assert.equal(tracker.end(session, 4, 'error', 1).emit, false)
  tracker.start(session, 4, 2)
  assert.equal(tracker.end(session, 4, 'error', 3).emit, true)
})

test('ended turn overrides stale running agent while another member remains active', () => {
  const tracker = new Tracker({})
  const one = { id: 'one', header: {} }
  const two = { id: 'two', header: {} }
  tracker.start(one, 1, 1)
  tracker.start(two, 1, 1)
  tracker.end(one, 1, 'error', 2)
  const rows = aggregate([{ id: 'w', title: 'Alpha', path: 'C:/a', sessionIds: ['one', 'two'] }], tracker.sessions, [
    { session: one, status: 'running' },
    { session: two, status: 'running' },
  ])
  assert.equal(rows[0].status, 'running')
  assert.equal(rows[0].runningCount, 1)
})

test('disposed activity is never converted into completed work', () => {
  const tracker = new Tracker({})
  const session = { id: 'one', header: {} }
  tracker.start(session, 1, 1)
  tracker.dispose(session)
  const rows = aggregate([{ id: 'w', title: 'Alpha', path: '', sessionIds: ['one'] }], tracker.sessions, [])
  assert.equal(rows[0].status, 'disposed')
  assert.notEqual(rows[0].status, 'completed')
})

test('resolves registry, parent membership, and Windows path fallback', () => {
  const registry = [{ id: 'w', title: 'Переименовано', path: 'C:/a', sessionIds: ['root'] }]
  const sessions = new Map([['root', { id: 'root', header: {} }]])
  assert.equal(workspaceFor({ id: 'child', header: { parentSession: 'root' } }, registry, sessions).id, 'w')
  assert.equal(workspaceFor({ id: 'x', header: { cwd: '\\\\srv\\demo\\' } }, [], sessions).title, 'demo')
  assert.equal(basename('C:\\a\\b\\'), 'b')
})

test('settings are PascalCase, validated, and preserve move coordinates', () => {
  const settings = normalizeSettings({ FieldPath: false, WidgetX: 12, Unknown: true })
  assert.equal(settings.FieldPath, false)
  assert.equal(settings.WidgetX, 12)
  assert.equal('Unknown' in settings, false)
  assert.equal(DEFAULT_SETTINGS.SubagentNotices, false)
})
