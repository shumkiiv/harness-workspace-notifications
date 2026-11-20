import test from 'node:test'
import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import os from 'node:os'
import path from 'node:path'
import { launchHelper } from '../src/helper.mjs'

function fakeChild() {
  const child = new EventEmitter()
  child.stdout = new PassThrough()
  child.stderr = new PassThrough()
  child.stdin = new EventEmitter()
  child.stdin.writable = true
  child.stdin.end = () => {}
  child.kill = () => {}
  child.exitCode = 0
  return child
}

test('helper supplies both Harness process identifiers without enabling a shell', async () => {
  const child = fakeChild()
  let invocation
  const helper = launchHelper({
    helperPath: 'fixture', dataDir: 'C:/fixture', logger: { warn() {} },
    spawnProcess(path, args, options) { invocation = { path, args, options }; return child },
  })
  assert.equal(invocation.options.shell, false)
  assert.equal(invocation.options.env.DSH_WN_HOST_PID, String(process.pid))
  assert.equal(invocation.options.env.DSH_WN_DESKTOP_PID, String(process.ppid))
  await helper.close()
})

test('paused transport keeps the latest widget until drain', async () => {
  const child = fakeChild()
  const writes = []
  child.stdin.write = (line) => {
    writes.push(JSON.parse(line))
    return false
  }
  const helper = launchHelper({ helperPath: 'fixture', spawnProcess: () => child, logger: { warn() {} } })
  helper.send({ t: 'widget.render', rows: [1] })
  helper.send({ t: 'widget.render', rows: [2] })
  assert.equal(writes.length, 1)
  child.stdin.emit('drain')
  assert.deepEqual(writes.at(-1).rows, [2])
  await helper.close()
})

test('critical notices retain FIFO order after pause', async () => {
  const child = fakeChild()
  const writes = []
  let first = true
  child.stdin.write = (line) => {
    writes.push(JSON.parse(line))
    if (first) {
      first = false
      return false
    }
    return true
  }
  const helper = launchHelper({ helperPath: 'fixture', spawnProcess: () => child, logger: { warn() {} } })
  helper.send({ t: 'widget.render', rows: [1] })
  helper.send({ t: 'notice.show', id: 'first' })
  helper.send({ t: 'notice.show', id: 'second' })
  child.stdin.emit('drain')
  assert.deepEqual(writes.slice(1).map((item) => item.id), ['first', 'second'])
  await helper.close()
})

test('EPIPE does not resolve cleanup before the owned child exits', async () => {
  const child = fakeChild()
  child.exitCode = null
  child.pid = 42
  child.stdin.write = () => true
  const helper = launchHelper({ helperPath: 'fixture', spawnProcess: () => child, logger: { warn() {} } })
  child.stdin.emit('error', { code: 'EPIPE', message: 'broken input' })
  let settled = false
  const closing = helper.close().then(() => { settled = true })
  await new Promise((resolve) => setImmediate(resolve))
  assert.equal(settled, false)
  child.exitCode = 0
  child.emit('exit', 0, null)
  await closing
  assert.equal(settled, true)
})

test('missing helper closes promptly when close is called immediately', async () => {
  const warnings = []
  const missing = path.join(os.tmpdir(), `missing-dsh-helper-${Date.now()}.exe`)
  const helper = launchHelper({ helperPath: missing, logger: { warn(message) { warnings.push(message) } } })
  const result = await Promise.race([
    helper.close().then(() => 'closed'),
    new Promise((resolve) => setTimeout(() => resolve('timeout'), 1500)),
  ])
  assert.equal(result, 'closed')
  assert.equal(warnings.length, 1)
})
