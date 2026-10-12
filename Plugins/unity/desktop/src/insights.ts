import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useCallback, useEffect, useState } from 'react'
import { pathKey, type HubProject, type ProjectDetails, type StoredProject } from './model'
import { listHubProjects, listInstalledEditors, readProjectDetails } from './unity'

export interface ProjectInsights {
  readonly details: ReadonlyMap<string, ProjectDetails>
  readonly hub: ReadonlyMap<string, HubProject> | null
  readonly installedEditors: ReadonlySet<string> | null
}

export function hubIndex(projects: readonly HubProject[]): ReadonlyMap<string, HubProject> {
  return new Map(projects.map((project) => [pathKey(project.path), project]))
}

export function useProjectInsights(
  host: DesktopPluginHost,
  projects: readonly StoredProject[] | null
): [ProjectInsights, (hub: ReadonlyMap<string, HubProject>) => void, () => void] {
  const [details, setDetails] = useState<ReadonlyMap<string, ProjectDetails>>(() => new Map())
  const [hub, setHub] = useState<ReadonlyMap<string, HubProject> | null>(null)
  const [installedEditors, setInstalledEditors] = useState<ReadonlySet<string> | null>(null)
  const [revision, setRevision] = useState(0)
  const paths = projects?.map((project) => project.path).join('\n') ?? null

  const refresh = useCallback((): void => setRevision((value) => value + 1), [])

  useEffect(() => {
    window.addEventListener('focus', refresh)
    return () => window.removeEventListener('focus', refresh)
  }, [refresh])

  useEffect(() => {
    if (paths === null) return
    let cancelled = false
    for (const path of paths === '' ? [] : paths.split('\n')) {
      void readProjectDetails(host, path).then((next) => {
        if (cancelled) return
        setDetails((previous) => new Map(previous).set(pathKey(path), next))
      })
    }
    void listHubProjects(host).then(
      (list) => !cancelled && setHub(hubIndex(list)),
      () => !cancelled && setHub(null)
    )
    void listInstalledEditors(host).then(
      (versions) => !cancelled && setInstalledEditors(versions),
      () => !cancelled && setInstalledEditors(null)
    )
    return () => {
      cancelled = true
    }
  }, [host, paths, revision])

  return [{ details, hub, installedEditors }, setHub, refresh]
}
