import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import { apply } from '../index.js'
import { createDemoContext, runDemo } from '../src/demo.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const args = process.argv.slice(2)
const dataIndex = args.indexOf('--data-dir')
const dataDir = dataIndex >= 0 ? args[dataIndex + 1] : path.join(root, '.demo-state')
const demo = args.includes('--demo')
const build = spawnSync(process.execPath, [path.join(root, 'scripts', 'build-helper.mjs')], { stdio: 'inherit' })
if (build.status !== 0) process.exit(build.status || 1)

const { ctx, add, agents } = createDemoContext()
apply(ctx, {
  dataDir,
  demo,
  helperPath: path.join(root, 'native', 'DshNotifyGui.exe'),
})
runDemo(ctx, add, agents)
console.log(`Демонстрация запущена. Данные: ${dataDir}. Нажмите q или Ctrl+C для остановки.`)

let stopping = false
async function stop() {
  if (stopping) return
  stopping = true
  await Promise.all(ctx.emit('dispose'))
  process.exit(0)
}
process.on('SIGINT', stop)
process.on('SIGTERM', stop)
process.stdin.setEncoding('utf8')
process.stdin.on('data', (value) => {
  if (value.trim().toLowerCase() === 'q') stop()
})
process.stdin.resume()
