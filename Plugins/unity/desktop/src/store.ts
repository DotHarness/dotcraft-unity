import type { DesktopPluginSettings } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'
import { readStoredProjects, type StoredProject } from './model'

interface UnitySettings {
  readonly projects: unknown
}

let current: readonly StoredProject[] | null = null
let store: DesktopPluginSettings | null = null
let mutationRevision = 0
const listeners = new Set<() => void>()

function publish(next: readonly StoredProject[]): void {
  current = next
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function snapshot(): readonly StoredProject[] | null {
  return current
}

export function connectProjectStore(settings: DesktopPluginSettings): () => void {
  store = settings
  let disposed = false
  const stop = settings.onChange<UnitySettings>((next) => publish(readStoredProjects(next.value.projects)))
  void settings.get<UnitySettings>()
    .then((initial) => {
      if (!disposed && current === null) publish(readStoredProjects(initial.value.projects))
    })
    .catch((error: unknown) => {
      console.error('Unity could not read its project list:', error)
      if (!disposed && current === null) publish([])
    })
  return () => {
    disposed = true
    stop()
    store = null
    current = null
  }
}

export function useProjects(): readonly StoredProject[] | null {
  return useSyncExternalStore(subscribe, snapshot, snapshot)
}

export function currentProjects(): readonly StoredProject[] {
  return current ?? []
}

export async function writeProjects(next: readonly StoredProject[]): Promise<void> {
  if (!store) return
  publish(next)
  const revision = ++mutationRevision
  const settings = store
  try {
    const result = await settings.mutate<UnitySettings>('personal', [{ op: 'set', key: 'projects', value: next }])
    if (revision === mutationRevision) publish(readStoredProjects(result.value.projects))
  } catch (error) {
    const stored = await settings.get<UnitySettings>().catch(() => null)
    if (stored && revision === mutationRevision) publish(readStoredProjects(stored.value.projects))
    throw error
  }
}
