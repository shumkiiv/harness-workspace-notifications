import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../native/WidgetQuotaKeys.cs', import.meta.url), 'utf8')
const gui = await readFile(new URL('../native/DshNotifyGui.cs', import.meta.url), 'utf8')

test('native quota key dialog saves with one button and never uses a masked value as input', () => {
  assert.match(source, /ServicePointManager\.SecurityProtocol\s*=\s*SecurityProtocolType\.Tls12;/)
  assert.match(source, /stored\s*\?\s*"\*{8}"\s*:\s*"Состояние: не задан"/)
  assert.match(source, /Button save = Button\("Сохранить", 14, 362/)
  assert.doesNotMatch(source, /Button remove = Button\("Удалить"/)
  assert.doesNotMatch(source, /Button close = Button\("Готово"/)
  assert.match(source, /if \(String\.IsNullOrEmpty\(value\) && stored\) \{ form\.DialogResult = DialogResult\.OK; return; \}/)
  assert.match(source, /form\.DialogResult = DialogResult\.OK;/)
})

test('key removal stays in the provider settings context menu', () => {
  assert.match(gui, /ConfigureQuotaKeyContextMenu\(button, provider, label\)/)
  assert.match(gui, /new ToolStripMenuItem\("Удалить ключ"/)
  assert.match(gui, /WidgetQuotaKeys\.DeleteStoredKey\(provider, quotaKeyChanged\)/)
})
