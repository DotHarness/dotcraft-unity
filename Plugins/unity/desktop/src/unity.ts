import type { DesktopPluginHost } from '@dotcraft/plugin'

const UNITY_CANDIDATES = [
  'unity',
  'C:\\Program Files\\Unity Hub\\resources\\unity.exe',
  '/Applications/Unity Hub.app/Contents/Resources/unity'
]
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

function joinPath(base: string, ...segments: string[]): string {
  const separator = /^[A-Za-z]:[\\/]/.test(base) || base.startsWith('\\\\') ? '\\' : '/'
  const root = base.endsWith(separator) ? base.slice(0, -1) : base
  return [root, ...segments].join(separator)
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

export async function openInUnity(host: DesktopPluginHost, path: string): Promise<void> {
  const result = await runUnity(host, ['open', path], OPEN_TIMEOUT_MS)
  if (result.exitCode !== 0) {
    throw new UnityCommandError(result.stderr.trim() || result.stdout.trim())
  }
}

async function readText(host: DesktopPluginHost, path: string): Promise<string | null> {
  try {
    const { dataBase64 } = await host.appServer.request('fs/readFile', { path })
    const binary = atob(dataBase64)
    const bytes = new Uint8Array(binary.length)
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index)
    return new TextDecoder().decode(bytes)
  } catch {
    return null
  }
}

async function metadata(host: DesktopPluginHost, path: string): Promise<{ isDirectory: boolean } | null> {
  try {
    return await host.appServer.request('fs/getMetadata', { path })
  } catch {
    return null
  }
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
