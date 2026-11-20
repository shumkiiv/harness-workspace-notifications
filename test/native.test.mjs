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
  assert.equal(report.checks.providerBadgeAliases, true)
  assert.equal(report.checks.notificationSound, true)
  assert.equal(report.checks.notificationSoundPresets, true)
  assert.equal(report.checks.settingsNoScrollbars, true)
  assert.equal(report.checks.settingsPagingKeepsLimitRowsReachable, true)
  assert.equal(report.checks.settingsFitsWorkingAreaAtAllScales, true)
  assert.equal(report.checks.settingsThemesKeepLayoutStable, true)
  assert.equal(report.checks.settingsCardsRedraw, true)
  assert.equal(report.checks.settingsTogglesKeepLayoutStable, true)
  assert.equal(report.checks.interfaceStyleDefaultsClassic, true)
  assert.equal(report.checks.interfaceStyleModern, true)
  assert.equal(report.checks.interfaceStyleReturnsClassic, true)
  assert.equal(report.checks.modernPaintCoversBuffer, true)
  assert.equal(report.checks.harnessDefaultOff, true)
  assert.equal(report.checks.harnessCheckbox, true)
  assert.equal(report.checks.newCompletedSuppressed, true)
  assert.equal(report.checks.retainedHiddenOnTransition, true)
  assert.equal(report.checks.duplicateRetained, true)
  assert.equal(report.checks.noRepeatedRender, true)
  assert.equal(report.checks.retainedRestoredWithoutReplayingSound, true)
})
