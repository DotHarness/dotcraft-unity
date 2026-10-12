import type { DesktopPluginHost } from '@dotcraft/plugin'
import {
  gitProvider,
  joinPath,
  parseCliJson,
  parseGitDirPointer,
  parseGitHead,
  parseGitOriginUrl,
  parseHubProjects,
  parseInstalledEditors,
  parsePlasticSelector,
  parseProjectVersion,
  type HubProject,
  type ProjectDetails,
  type VcsProvider
} from './model'

const UNITY_CANDIDATES = [
  'unity',
  'C:\\Program Files\\Unity Hub\\resources\\unity.exe',
  '/Applications/Unity Hub.app/Contents/Resources/unity'
]
const LIST_TIMEOUT_MS = 30_000
const OPEN_TIMEOUT_MS = 60_000
const REQUEST_MARGIN_MS = 10_000

export class UnityHubMissingError extends Error {}

export class HostTooOldError extends Error {}

class UnityCommandError extends Error {}

interface RpcError {
  readonly code?: unknown
  readonly data?: { readonly code?: unknown }
}

let resolvedUnity: string | null = null

function rpcError(error: unknown): RpcError {
  return typeof error === 'object' && error !== null ? (error as RpcError) : {}
}

function isMissingCommand(error: unknown): boolean {
  const { code, data } = rpcError(error)
  return code === -32104 && (data?.code === 'CommandNotFound' || data?.code === 'CommandNotExecutable')
}

function isMissingMethod(error: unknown): boolean {
  return rpcError(error).code === -32601
}

async function runUnity(
  host: DesktopPluginHost,
  args: readonly string[],
  timeoutMs: number
): Promise<{ exitCode: number; stdout: string; stderr: string }> {
  const candidates = resolvedUnity
    ? [resolvedUnity, ...UNITY_CANDIDATES.filter((candidate) => candidate !== resolvedUnity)]
    : UNITY_CANDIDATES
  for (const program of candidates) {
    try {
      const result = await host.appServer.request(
        'command/exec',
        { command: [program, ...args], timeoutMs },
        timeoutMs + REQUEST_MARGIN_MS
      )
      resolvedUnity = program
      return result
    } catch (error) {
      if (isMissingMethod(error)) throw new HostTooOldError()
      if (!isMissingCommand(error)) throw error
    }
  }
  throw new UnityHubMissingError()
}

async function runUnityJson(host: DesktopPluginHost, args: readonly string[]): Promise<unknown> {
  const result = await runUnity(host, [...args, '--json'], LIST_TIMEOUT_MS)
  return parseCliJson(result.stdout)
}

export async function listHubProjects(host: DesktopPluginHost): Promise<HubProject[]> {
  const projects = parseHubProjects(await runUnityJson(host, ['projects', 'list']))
  if (!projects) throw new UnityCommandError()
  return projects
}

export async function listInstalledEditors(host: DesktopPluginHost): Promise<Set<string>> {
  const editors = parseInstalledEditors(await runUnityJson(host, ['editors', 'list', '--installed']))
  if (!editors) throw new UnityCommandError()
  return editors
}

export async function openInUnity(host: DesktopPluginHost, path: string): Promise<void> {
  const result = await runUnity(host, ['open', path], OPEN_TIMEOUT_MS)
  if (result.exitCode !== 0) {
    throw new UnityCommandError(result.stderr.trim() || result.stdout.trim())
  }
}

function decodeBase64(data: string): string {
  const binary = atob(data)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index)
  return new TextDecoder().decode(bytes)
}

async function readText(host: DesktopPluginHost, path: string): Promise<string | null> {
  try {
    const { dataBase64 } = await host.appServer.request('fs/readFile', { path })
    return decodeBase64(dataBase64)
  } catch {
    return null
  }
}

async function metadata(host: DesktopPluginHost, path: string): Promise<{ isDirectory: boolean; modifiedAtMs: number } | null> {
  try {
    return await host.appServer.request('fs/getMetadata', { path })
  } catch {
    return null
  }
}

async function readEditorVersion(host: DesktopPluginHost, path: string): Promise<string | null> {
  const text = await readText(host, joinPath(path, 'ProjectSettings', 'ProjectVersion.txt'))
  return text === null ? null : parseProjectVersion(text)
}

export async function isEditorOpen(host: DesktopPluginHost, path: string): Promise<boolean> {
  const lockfile = joinPath(path, 'Temp', 'UnityLockfile')
  if ((await metadata(host, lockfile)) === null) return false
  return (await readText(host, lockfile)) === null
}

export async function isUnityProject(host: DesktopPluginHost, path: string): Promise<boolean> {
  const [assets, version] = await Promise.all([
    metadata(host, joinPath(path, 'Assets')),
    readText(host, joinPath(path, 'ProjectSettings', 'ProjectVersion.txt'))
  ])
  return assets?.isDirectory === true && version !== null
}

function resolveFrom(base: string, path: string): string {
  return /^([A-Za-z]:[\\/]|[\\/])/.test(path) ? path : joinPath(base, path)
}

async function findGitDir(host: DesktopPluginHost, path: string): Promise<string | null> {
  const dotGit = joinPath(path, '.git')
  const entry = await metadata(host, dotGit)
  if (entry === null) return null
  if (entry.isDirectory) return dotGit
  const pointer = await readText(host, dotGit)
  const gitDir = pointer === null ? null : parseGitDirPointer(pointer)
  return gitDir === null ? null : resolveFrom(path, gitDir)
}

async function readGitConfig(host: DesktopPluginHost, gitDir: string): Promise<string | null> {
  const commonDir = (await readText(host, joinPath(gitDir, 'commondir')))?.trim()
  const configDir = commonDir ? resolveFrom(gitDir, commonDir) : gitDir
  return readText(host, joinPath(configDir, 'config'))
}

async function readVersionControl(
  host: DesktopPluginHost,
  path: string
): Promise<{ vcs: VcsProvider | null; branch: string | null }> {
  if ((await metadata(host, joinPath(path, '.plastic'))) !== null) {
    const selector = await readText(host, joinPath(path, '.plastic', 'plastic.selector'))
    return { vcs: 'uvcs', branch: selector === null ? null : parsePlasticSelector(selector) }
  }
  const gitDir = await findGitDir(host, path)
  if (gitDir === null) return { vcs: null, branch: null }
  const [head, config] = await Promise.all([readText(host, joinPath(gitDir, 'HEAD')), readGitConfig(host, gitDir)])
  return {
    vcs: gitProvider(config === null ? null : parseGitOriginUrl(config)),
    branch: head === null ? null : parseGitHead(head)
  }
}

export async function readProjectDetails(host: DesktopPluginHost, path: string): Promise<ProjectDetails> {
  const folder = await metadata(host, path)
  if (folder?.isDirectory !== true) {
    return { missing: true, editorVersion: null, vcs: null, branch: null, modifiedAtMs: 0 }
  }
  const [editorVersion, versionControl] = await Promise.all([readEditorVersion(host, path), readVersionControl(host, path)])
  return { missing: false, editorVersion, ...versionControl, modifiedAtMs: folder.modifiedAtMs }
}
