import type { DesktopPluginHost } from '@dotcraft/plugin'
import { fill, type UnityStrings } from './i18n'
import { HostTooOldError, UnityHubMissingError, isEditorOpen, openInUnity } from './unity'

function failureMessage(error: unknown, strings: UnityStrings): string {
  if (error instanceof UnityHubMissingError) return strings.hubRequired
  if (error instanceof HostTooOldError) return strings.hostTooOld
  const detail = error instanceof Error ? error.message.trim() : ''
  return detail ? fill(strings.openFailedDetail, { detail }) : strings.openFailed
}

export async function launchEditor(host: DesktopPluginHost, strings: UnityStrings, path: string): Promise<void> {
  if (await isEditorOpen(host, path)) {
    host.ui.showToast({ message: strings.alreadyOpen })
    return
  }
  try {
    await openInUnity(host, path)
  } catch (error) {
    host.ui.showToast({ message: failureMessage(error, strings), tone: 'error' })
  }
}
