export interface StoredProject {
  readonly path: string
  readonly favorite: boolean
  readonly addedAt: number
}

export interface HubProject {
  readonly path: string
  readonly favorite: boolean
  readonly buildTarget: string | null
}

export type VcsProvider = 'github' | 'gitlab' | 'git' | 'uvcs'

export interface ProjectDetails {
  readonly missing: boolean
  readonly editorVersion: string | null
  readonly vcs: VcsProvider | null
  readonly branch: string | null
  readonly modifiedAtMs: number
}

const BUILD_TARGET_NAMES: Readonly<Record<string, string>> = {
  StandaloneWindows: 'Windows',
  StandaloneWindows64: 'Windows',
  StandaloneOSX: 'macOS',
  StandaloneLinux64: 'Linux',
  Android: 'Android',
  iOS: 'iOS',
  tvOS: 'tvOS',
  VisionOS: 'visionOS',
  WebGL: 'Web',
  WSAPlayer: 'Universal Windows Platform',
  PS4: 'PlayStation 4',
  PS5: 'PlayStation 5',
  XboxOne: 'Xbox One',
  GameCoreXboxOne: 'Xbox One',
  GameCoreXboxSeries: 'Xbox Series X|S',
  Switch: 'Nintendo Switch'
}

export function isWindowsPath(path: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(path) || path.startsWith('\\\\')
}

export function normalizePath(path: string): string {
  const trimmed = path.trim()
  if (isWindowsPath(trimmed)) {
    const backslashed = trimmed.replace(/\//g, '\\')
    return /^[A-Za-z]:\\$/.test(backslashed) ? backslashed : backslashed.replace(/\\+$/, '')
  }
  return trimmed.length > 1 ? trimmed.replace(/\/+$/, '') : trimmed
}

export function pathKey(path: string): string {
  const normalized = normalizePath(path)
  return isWindowsPath(normalized) ? normalized.toLowerCase() : normalized
}

export function joinPath(base: string, ...segments: string[]): string {
  const separator = isWindowsPath(base) ? '\\' : '/'
  const root = base.endsWith(separator) ? base.slice(0, -1) : base
  return [root, ...segments].join(separator)
}

export function folderName(path: string): string {
  const parts = normalizePath(path).split(/[\\/]/).filter((part) => part.length > 0)
  return parts.at(-1) ?? path
}

export function parseProjectVersion(text: string): string | null {
  const match = /^m_EditorVersion:\s*(\S+)\s*$/m.exec(text)
  return match ? match[1] : null
}

export function parseGitHead(text: string): string | null {
  const head = text.trim()
  const ref = /^ref:\s*refs\/heads\/(.+)$/.exec(head)
  if (ref) return ref[1]
  return /^[0-9a-f]{40,64}$/i.test(head) ? head.slice(0, 7) : null
}

export function parseGitDirPointer(text: string): string | null {
  const match = /^gitdir:\s*(.+)$/m.exec(text)
  return match ? match[1].trim() : null
}

function unquoteGitValue(raw: string): string {
  let value = ''
  let quoted = false
  for (let index = 0; index < raw.length; index += 1) {
    const char = raw[index]
    if (char === '"') {
      quoted = !quoted
    } else if (char === '\\' && index + 1 < raw.length) {
      index += 1
      value += raw[index]
    } else if (!quoted && (char === '#' || char === ';')) {
      break
    } else {
      value += char
    }
  }
  return value.trim()
}

export function parseGitOriginUrl(config: string): string | null {
  let inOrigin = false
  for (const line of config.split(/\r?\n/)) {
    const section = /^\s*\[\s*([A-Za-z0-9.-]+)(?:\s+"((?:[^"\\]|\\.)*)")?\s*\]/.exec(line)
    if (section) {
      inOrigin = section[1].toLowerCase() === 'remote' && section[2] === 'origin'
      continue
    }
    if (!inOrigin) continue
    const entry = /^\s*url\s*=\s*(.*)$/i.exec(line)
    if (entry) {
      const url = unquoteGitValue(entry[1])
      if (url !== '') return url
    }
  }
  return null
}

export function remoteHost(url: string): string | null {
  const scheme = /^[a-z][a-z0-9+.-]*:\/\/(?:[^@/]*@)?(\[[^\]]+\]|[^/:?#]+)/i.exec(url)
  if (scheme) return url.toLowerCase().startsWith('file:') ? null : scheme[1].toLowerCase()
  const scp = /^(?:[^@/:]+@)?([^/:]+):(?!\/\/)/.exec(url)
  if (scp && !/^[A-Za-z]$/.test(scp[1])) return scp[1].toLowerCase()
  return null
}

export function gitProvider(originUrl: string | null): VcsProvider {
  const host = originUrl === null ? null : remoteHost(originUrl)
  if (host === 'github.com') return 'github'
  if (host === 'gitlab.com') return 'gitlab'
  return 'git'
}

export function parsePlasticSelector(text: string): string | null {
  const match = /^\s*(?:smartbranch|branch|br)\s+"([^"]+)"/m.exec(text)
  return match ? match[1] : null
}

export function parseCliJson(stdout: string): unknown {
  const start = stdout.indexOf('{')
  const end = stdout.lastIndexOf('}')
  if (start < 0 || end < start) return null
  try {
    return JSON.parse(stdout.slice(start, end + 1))
  } catch {
    return null
  }
}

function dataArray(output: unknown): readonly unknown[] | null {
  if (typeof output !== 'object' || output === null) return null
  const record = output as { success?: unknown; data?: unknown }
  if (record.success === false || !Array.isArray(record.data)) return null
  return record.data
}

export function parseHubProjects(output: unknown): HubProject[] | null {
  const data = dataArray(output)
  if (!data) return null
  const projects: HubProject[] = []
  for (const item of data) {
    if (typeof item !== 'object' || item === null) continue
    const entry = item as { path?: unknown; isFavorite?: unknown; buildTarget?: unknown }
    if (typeof entry.path !== 'string' || entry.path.trim() === '') continue
    projects.push({
      path: normalizePath(entry.path),
      favorite: entry.isFavorite === true,
      buildTarget: typeof entry.buildTarget === 'string' && entry.buildTarget !== '' ? entry.buildTarget : null
    })
  }
  return projects
}

export function parseInstalledEditors(output: unknown): Set<string> | null {
  const data = dataArray(output)
  if (!data) return null
  const versions = new Set<string>()
  for (const item of data) {
    if (typeof item !== 'object' || item === null) continue
    const version = (item as { version?: unknown }).version
    if (typeof version === 'string' && version !== '') versions.add(version)
  }
  return versions
}

export function buildTargetName(target: string): string {
  return BUILD_TARGET_NAMES[target] ?? target
}

export function readStoredProjects(value: unknown): StoredProject[] {
  if (!Array.isArray(value)) return []
  const projects: StoredProject[] = []
  const seen = new Set<string>()
  for (const item of value) {
    if (typeof item !== 'object' || item === null) continue
    const entry = item as { path?: unknown; favorite?: unknown; addedAt?: unknown }
    if (typeof entry.path !== 'string' || entry.path.trim() === '') continue
    const key = pathKey(entry.path)
    if (seen.has(key)) continue
    seen.add(key)
    projects.push({
      path: normalizePath(entry.path),
      favorite: entry.favorite === true,
      addedAt: typeof entry.addedAt === 'number' ? entry.addedAt : 0
    })
  }
  return projects
}

export function addProjects(list: readonly StoredProject[], additions: readonly StoredProject[]): StoredProject[] {
  const seen = new Set(list.map((project) => pathKey(project.path)))
  const next = [...list]
  for (const addition of additions) {
    const key = pathKey(addition.path)
    if (seen.has(key)) continue
    seen.add(key)
    next.push({ ...addition, path: normalizePath(addition.path) })
  }
  return next
}

export function hasProject(list: readonly StoredProject[], path: string): boolean {
  const key = pathKey(path)
  return list.some((project) => pathKey(project.path) === key)
}

export function removeProject(list: readonly StoredProject[], path: string): StoredProject[] {
  const key = pathKey(path)
  return list.filter((project) => pathKey(project.path) !== key)
}

export function setFavorite(list: readonly StoredProject[], path: string, favorite: boolean): StoredProject[] {
  const key = pathKey(path)
  return list.map((project) => (pathKey(project.path) === key ? { ...project, favorite } : project))
}

export function sortProjects(
  list: readonly StoredProject[],
  modifiedAt: (project: StoredProject) => number
): StoredProject[] {
  return [...list].sort((left, right) => {
    if (left.favorite !== right.favorite) return left.favorite ? -1 : 1
    const byTime = modifiedAt(right) - modifiedAt(left)
    if (byTime !== 0) return byTime
    return folderName(left.path).localeCompare(folderName(right.path))
  })
}

export function matchesQuery(project: StoredProject, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (needle === '') return true
  return folderName(project.path).toLowerCase().includes(needle) || project.path.toLowerCase().includes(needle)
}

export function distinctValues(values: readonly (string | null)[]): string[] {
  return [...new Set(values.filter((value): value is string => value !== null))]
}

export function compareEditorVersions(left: string, right: string): number {
  return left.localeCompare(right, undefined, { numeric: true })
}
