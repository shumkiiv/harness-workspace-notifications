import { spawn } from 'node:child_process'

export function launchHelper({ helperPath, dataDir, demo = false, demoArgs = [], onMessage, logger = console, spawnProcess = spawn }) {
  let child
  let closed = false
  let stdinBroken = false
  let paused = false
  let latestWidget = null
  let warned = false
  let buffer = ''
  let closePromise = null
  let terminal = false
  let killTimer = null
  const queue = []
  let resolveTerminal
  const terminalPromise = new Promise((resolve) => { resolveTerminal = resolve })

  const warn = (text) => {
    if (!warned) logger?.warn?.(text)
    warned = true
  }
  const settleTerminal = () => {
    if (terminal) return
    terminal = true
    if (killTimer) clearTimeout(killTimer)
    resolveTerminal()
  }
  try {
    child = spawnProcess(helperPath, demo ? ['--demo', ...demoArgs] : [], {
      shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
      env: {
        ...process.env,
        DSH_WN_HOST_PID: String(process.pid),
        DSH_WN_DESKTOP_PID: String(process.ppid),
        ...(dataDir ? { DSH_WN_DATA_DIR: dataDir } : {}),
      },
    })
  } catch (error) {
    warn(`Графическое окно недоступно: ${error.message}`)
    return unavailable()
  }

  child.on('exit', (code, signal) => {
    settleTerminal()
    if (!closed) warn(`Графическое окно завершилось неожиданно (${code ?? signal})`)
  })
  child.on('close', () => settleTerminal())
  child.on('error', (error) => {
    warn(`Графическое окно недоступно: ${error.message}`)
    if (!child.pid) settleTerminal()
  })

  const write = (message) => {
    if (closed || stdinBroken || !child.stdin?.writable) return false
    try {
      const accepted = child.stdin.write(`${JSON.stringify(message)}\n`, 'utf8')
      if (!accepted) paused = true
      return accepted
    } catch (error) {
      stdinBroken = true
      if (error.code !== 'EPIPE') warn(`Не удалось передать данные окну: ${error.message}`)
      return false
    }
  }
  const flush = () => {
    if (closed || stdinBroken || paused) return
    while (queue.length > 0 && !paused) write(queue.shift())
    if (!paused && latestWidget) {
      const widget = latestWidget
      latestWidget = null
      write(widget)
    }
  }
  const send = (message) => {
    if (closed || stdinBroken || !message?.t) return false
    if (message.t === 'widget.render') {
      latestWidget = message
      if (!paused && queue.length === 0) flush()
      return true
    }
    queue.push(message)
    flush()
    return true
  }

  child.stdin?.on('drain', () => { paused = false; flush() })
  child.stdin?.on('error', (error) => {
    stdinBroken = true
    if (!closed && error.code !== 'EPIPE') warn(`Не удалось передать данные окну: ${error.message}`)
  })
  child.stdout?.setEncoding('utf8')
  child.stdout?.on('data', (data) => {
    buffer = `${buffer}${data}`.slice(-65536)
    let newline
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline)
      buffer = buffer.slice(newline + 1)
      try { onMessage?.(JSON.parse(line)) } catch { warn('Графическое окно прислало некорректное сообщение') }
    }
  })
  child.stderr?.on('data', () => {})

  return { send, close }

  function close() {
    if (closePromise) return closePromise
    closePromise = closeChild()
    return closePromise
  }

  async function closeChild() {
    if (closed) return terminalPromise
    if (!stdinBroken && !paused) write({ t: 'shutdown' })
    closed = true
    try { child.stdin?.end() } catch {}
    if (terminal || child.exitCode != null) {
      settleTerminal()
      return terminalPromise
    }
    if (!child.pid) return terminalPromise
    killTimer = setTimeout(() => {
      if (!terminal) child.kill?.()
    }, 2000)
    return terminalPromise
  }
}

function unavailable() {
  return { send: () => false, close: async () => {} }
}
