import { existsSync, readFileSync, statSync } from 'node:fs'
import { dirname, isAbsolute, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const failures = []

function fail(message) {
  failures.push(message)
}

function readJson(path) {
  try {
    return JSON.parse(readFileSync(path, 'utf8'))
  } catch (error) {
    fail(`Could not parse ${relative(repoRoot, path)}: ${error.message}`)
    return null
  }
}

function isObject(value) {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function isInside(path, root) {
  const rel = relative(root, path)
  return rel === '' || (!!rel && !rel.startsWith('..') && !isAbsolute(rel))
}

function resolveRelative(root, value, label) {
  if (typeof value !== 'string' || !value.startsWith('./')) {
    fail(`${label} must start with ./`)
    return null
  }

  const segments = value.slice(2).split(/[\\/]+/).filter(Boolean)
  if (segments.includes('..')) {
    fail(`${label} must not contain ..`)
    return null
  }

  const full = resolve(root, ...segments)
  if (!isInside(full, root)) {
    fail(`${label} must stay inside ${relative(repoRoot, root) || 'the repository'}`)
    return null
  }

  return full
}

function requirePath(root, value, label, expectedKind) {
  const full = resolveRelative(root, value, label)
  if (!full) return null
  if (!existsSync(full)) {
    fail(`${label} points to missing path ${relative(repoRoot, full)}`)
    return null
  }
  if (expectedKind === 'file' && !statSync(full).isFile()) {
    fail(`${label} must point to a file`)
  }
  if (expectedKind === 'directory' && !statSync(full).isDirectory()) {
    fail(`${label} must point to a directory`)
  }
  return full
}

function requireEqual(actual, expected, label) {
  if (actual !== expected) fail(`${label} must be '${expected}'`)
}

const packageManifest = readJson(resolve(repoRoot, 'package.json'))
const marketplace = readJson(resolve(repoRoot, '.craft', 'plugins', 'marketplace.json'))

if (!isObject(marketplace)) {
  fail('Marketplace document must be an object')
} else {
  requireEqual(marketplace.name, 'dotcraft-unity', 'marketplace.name')
  requireEqual(marketplace.interface?.displayName, 'DotCraft Unity', 'marketplace.interface.displayName')
  if (!Array.isArray(marketplace.plugins) || marketplace.plugins.length !== 1) {
    fail('marketplace.plugins must contain exactly one entry')
  }
}

const entry = marketplace?.plugins?.[0]
let pluginRoot = null
if (isObject(entry)) {
  requireEqual(entry.name, 'dotcraft-unity', 'plugin entry name')
  requireEqual(entry.source?.source, 'local', 'plugin source.source')
  requireEqual(entry.source?.path, './Plugins~/dotcraft-unity', 'plugin source.path')
  requireEqual(entry.policy?.installation, 'AVAILABLE', 'plugin policy.installation')
  requireEqual(entry.policy?.authentication, 'ON_INSTALL', 'plugin policy.authentication')
  requireEqual(entry.category, 'Engineering', 'plugin category')
  pluginRoot = requirePath(repoRoot, entry.source?.path, 'plugin source.path', 'directory')
} else {
  fail('Marketplace plugin entry must be an object')
}

let pluginManifest = null
if (pluginRoot) {
  pluginManifest = readJson(resolve(pluginRoot, '.craft-plugin', 'plugin.json'))
  if (!isObject(pluginManifest)) {
    fail('Plugin manifest must be an object')
  } else {
    requireEqual(pluginManifest.schemaVersion, 1, 'plugin schemaVersion')
    requireEqual(pluginManifest.id, entry.name, 'plugin id')
    requireEqual(pluginManifest.version, packageManifest?.version, 'plugin version')
    requireEqual(pluginManifest.displayName, 'DotCraft Unity', 'plugin displayName')

    if (!Array.isArray(pluginManifest.capabilities)
        || pluginManifest.capabilities.length !== 1
        || pluginManifest.capabilities[0] !== 'skill') {
      fail("plugin capabilities must contain only 'skill'")
    }
    if (Object.hasOwn(pluginManifest, 'apps')) fail('plugin manifest must not declare apps')
    if (pluginManifest.interface?.capabilities?.includes('App')) {
      fail("interface capabilities must not include 'App'")
    }

    const skillsRoot = requirePath(pluginRoot, pluginManifest.skills, 'plugin skills', 'directory')
    requirePath(pluginRoot, pluginManifest.interface?.composerIcon, 'interface.composerIcon', 'file')
    requirePath(pluginRoot, pluginManifest.interface?.logo, 'interface.logo', 'file')
    if (skillsRoot) {
      const skillManifest = resolve(skillsRoot, 'dotcraft-unity', 'SKILL.md')
      if (!existsSync(skillManifest)) fail('plugin skills must contain dotcraft-unity/SKILL.md')
    }
  }
}

if (pluginRoot && existsSync(resolve(pluginRoot, 'apps.json'))) {
  fail('skill-only plugin bundle must not contain apps.json')
}

const codexMarketplace = readJson(resolve(repoRoot, '.agents', 'plugins', 'marketplace.json'))
if (!isObject(codexMarketplace)) {
  fail('Codex marketplace document must be an object')
} else {
  requireEqual(codexMarketplace.name, 'dotcraft-unity', 'Codex marketplace.name')
  requireEqual(codexMarketplace.interface?.displayName, 'DotCraft Unity', 'Codex marketplace.interface.displayName')
  if (!Array.isArray(codexMarketplace.plugins) || codexMarketplace.plugins.length !== 1) {
    fail('Codex marketplace.plugins must contain exactly one entry')
  }
}

const codexEntry = codexMarketplace?.plugins?.[0]
if (isObject(codexEntry)) {
  requireEqual(codexEntry.name, 'dotcraft-unity', 'Codex plugin entry name')
  requireEqual(codexEntry.source?.source, 'local', 'Codex plugin source.source')
  requireEqual(codexEntry.source?.path, './Plugins~/dotcraft-unity', 'Codex plugin source.path')
  requireEqual(codexEntry.policy?.installation, 'AVAILABLE', 'Codex plugin policy.installation')
  requireEqual(codexEntry.policy?.authentication, 'ON_INSTALL', 'Codex plugin policy.authentication')
  requireEqual(codexEntry.category, 'Engineering', 'Codex plugin category')

  const codexPluginRoot = requirePath(repoRoot, codexEntry.source?.path, 'Codex plugin source.path', 'directory')
  if (codexPluginRoot) {
    const codexManifest = readJson(resolve(codexPluginRoot, '.codex-plugin', 'plugin.json'))
    if (!isObject(codexManifest)) {
      fail('Codex plugin manifest must be an object')
    } else {
      requireEqual(codexManifest.name, codexEntry.name, 'Codex plugin name')
      requireEqual(codexManifest.version, packageManifest?.version, 'Codex plugin version')
      requireEqual(codexManifest.skills, './skills/', 'Codex plugin skills')
      requireEqual(codexManifest.interface?.displayName, 'DotCraft Unity', 'Codex plugin displayName')
      requirePath(codexPluginRoot, codexManifest.skills, 'Codex plugin skills', 'directory')
      requirePath(codexPluginRoot, codexManifest.interface?.composerIcon, 'Codex interface.composerIcon', 'file')
      requirePath(codexPluginRoot, codexManifest.interface?.logo, 'Codex interface.logo', 'file')
      if (Object.hasOwn(codexManifest, 'apps')) fail('Codex skill-only plugin must not declare apps')
      if (Object.hasOwn(codexManifest, 'mcpServers')) fail('Codex skill-only plugin must not declare MCP servers')
    }
  }
} else {
  fail('Codex marketplace plugin entry must be an object')
}

if (failures.length > 0) {
  for (const message of failures) console.error(`[validate-marketplace] ${message}`)
  process.exit(1)
}

console.log('[validate-marketplace] OK')
