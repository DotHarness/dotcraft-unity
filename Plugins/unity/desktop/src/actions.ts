import type { DesktopPluginHost } from '@dotcraft/plugin'
import { fill, type UnityStrings } from './i18n'
import { HostTooOldError, UnityHubMissingError, isEditorOpen, openInUnity } from './unity'

function errorText(error: unknown): string {
  return error instanceof Error ? error.message.trim() : ''
}

export function unityRequirementMessage(error: unknown, strings: UnityStrings): string | null {
  if (error instanceof UnityHubMissingError) return strings.hubRequired
  if (error instanceof HostTooOldError) return strings.hostTooOld
  return null
}

export async function launchEditor(host: DesktopPluginHost, strings: UnityStrings, path: string): Promise<void> {
  if (await isEditorOpen(host, path)) {
    host.ui.showToast({ message: strings.alreadyOpen })
    return
  }
  try {
    await openInUnity(host, path)
  } catch (error) {
    const detail = errorText(error)
    const message = unityRequirementMessage(error, strings)
      ?? (detail ? fill(strings.openFailedDetail, { detail }) : strings.openFailed)
    host.ui.showToast({ message, tone: 'error' })
  }
}
