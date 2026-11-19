import fs from 'node:fs'
import path from 'node:path'
import { capOutcomes } from './core.mjs'

export const DEFAULT_SETTINGS = Object.freeze({
  ShowWidget: true,
  ShowNotifications: true,
  SubagentNotices: false,
  FieldWorkspace: true,
  FieldTask: true,
  FieldStatus: true,
  FieldTime: true,
  FieldPath: true,
  WidgetCounts: true,
  CompactWidget: true,
  ThemeDark: true,
  ShowLimits: false,
  StatusRunning: true,
  StatusCompleted: true,
  StatusError: true,
  StatusMaxTokens: true,
  StatusBlocked: true,
  StatusAborted: true,
  StatusInterrupted: true,
  StatusIdle: false,
  StatusUnknown: true,
  StatusDisposed: true,
  WidgetX: 30,
  WidgetY: 30,
  NoticeX: 500,
  NoticeY: 30,
})

const BOOLEAN_KEYS = Object.keys(DEFAULT_SETTINGS).filter((key) => typeof DEFAULT_SETTINGS[key] === 'boolean')
const NUMBER_KEYS = Object.keys(DEFAULT_SETTINGS).filter((key) => typeof DEFAULT_SETTINGS[key] === 'number')

export function normalizeSettings(value) {
  const input = value && typeof value === 'object' ? value : {}
  const settings = { ...DEFAULT_SETTINGS }
  for (const key of BOOLEAN_KEYS) if (typeof input[key] === 'boolean') settings[key] = input[key]
  for (const key of NUMBER_KEYS) if (Number.isInteger(input[key])) settings[key] = input[key]
  return settings
}

function readJson(file, fallback, logger) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'))
  } catch (error) {
    if (error.code !== 'ENOENT') logger?.warn?.(`Не удалось прочитать настройки: ${error.message}`)
    return fallback
  }
}

function writeJson(file, value, logger) {
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true })
    const temporary = `${file}.tmp`
    fs.writeFileSync(temporary, JSON.stringify(value, null, 2), 'utf8')
    fs.renameSync(temporary, file)
  } catch (error) {
    logger?.warn?.(`Не удалось сохранить данные: ${error.message}`)
  }
}

export function openStore(dataDir, logger) {
  const settingsFile = path.join(dataDir, 'settings.json')
  const outcomesFile = path.join(dataDir, 'outcomes.json')
  return {
    settings() {
      return normalizeSettings(readJson(settingsFile, {}, logger))
    },
    saveSettings(settings) {
      writeJson(settingsFile, normalizeSettings(settings), logger)
    },
    outcomes() {
      const values = readJson(outcomesFile, [], logger)
      return Array.isArray(values) ? capOutcomes(values) : []
    },
    saveOutcomes(outcomes) {
      writeJson(outcomesFile, capOutcomes(outcomes), logger)
    },
    files: { settingsFile, outcomesFile },
  }
}
