import { spawnSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
const icon = path.join(root, 'assets', 'icons', 'harness-tasks.ico')
const native = path.join(root, 'native')
const args = [
  '/nologo', '/target:winexe', '/langversion:5', '/codepage:65001',
  '/win32icon:' + icon, '/resource:' + icon + ',HarnessTasks.ico',
  '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
  '/out:' + path.join(native, 'DshNotifyGui.exe'),
  path.join(native, 'DshNotifyGui.cs'), path.join(native, 'Visuals.cs')
]

const result = spawnSync(compiler, args, { stdio: 'inherit' })
process.exit(result.status ?? 1)
