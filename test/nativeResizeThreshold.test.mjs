import test from 'node:test'
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

test('native widget resize switches at one captured fit threshold and keeps the compact grip', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
  const result = spawnSync(path.join(root, 'native', 'DshNotifyGui.exe'), ['--self-test'], { encoding: 'utf8' })
  assert.equal(result.status, 0)
  const report = JSON.parse(result.stdout.trim())
  assert.equal(report.checks.resizeThreshold, true)
  assert.equal(report.checks.resizeThresholdAllScales, true)
  assert.equal(report.checks.resizeThresholdLongList, true)
  assert.equal(report.checks.resizeThresholdExactRows, true)
  assert.equal(report.checks.manualHeightCanExceedAutomaticDefault, true)
})
