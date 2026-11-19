import test from 'node:test'
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

test('native helper self-test reports every named check', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
  const result = spawnSync(path.join(root, 'native', 'DshNotifyGui.exe'), ['--self-test'], { encoding: 'utf8' })
  assert.equal(result.status, 0)
  const report = JSON.parse(result.stdout.trim())
  assert.equal(report.t, 'selftest')
  assert.equal(report.pass, true)
  assert.equal(Object.values(report.checks).every(Boolean), true)
  assert.equal(report.checks.taskIconConsistent, true)
})
