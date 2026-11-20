import { spawnSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
const icon = path.join(root, 'assets', 'icons', 'harness-tasks.ico')
const native = path.join(root, 'native')
const providerLogos = ['deepseek', 'codex', 'zai', 'generic']
  .flatMap(brand => ['light', 'dark'].map(theme => brand + '-' + theme + '.png'))
const soundPresets = ['boing.wav', 'squeak.wav', 'whistle.wav', 'pop.wav', 'scratch.wav']
const args = [
  '/nologo', '/target:winexe', '/langversion:5', '/codepage:65001',
  '/win32icon:' + icon, '/resource:' + icon + ',HarnessTasks.ico',
  ...providerLogos.map(file => '/resource:' + path.join(root, 'assets', 'providers', file) + ',Providers.' + file),
  ...soundPresets.map(file => '/resource:' + path.join(root, 'assets', 'sounds', file) + ',Sounds.' + file),
  '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
  '/out:' + path.join(native, 'DshNotifyGui.exe'),
  path.join(native, 'DshNotifyGui.cs'), path.join(native, 'DshNotifyGuiModern.cs'), path.join(native, 'Visuals.cs'), path.join(native, 'WidgetQuotaKeys.cs'),
  path.join(native, 'ITelegramTransport.cs'), path.join(native, 'TelegramCredentials.cs'), path.join(native, 'TelegramCardPolicy.cs'), path.join(native, 'TelegramTextFormatter.cs'), path.join(native, 'TelegramPublisher.cs'), path.join(native, 'TelegramSettingsUi.cs'), path.join(native, 'TelegramCardTests.cs')
]

const result = spawnSync(compiler, args, { stdio: 'inherit' })
process.exit(result.status ?? 1)
