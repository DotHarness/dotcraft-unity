import { IconButton, type DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { useEffect, useState, type JSX } from 'react'
import { launchEditor } from './actions'
import { useStrings } from './i18n'
import { isUnityProject } from './unity'
import { UnityIcon } from './UnityIcon'

export function UnityHeaderAction({ host, context }: DesktopPluginSurfaceProps<'thread.header.actions'>): JSX.Element | null {
  const strings = useStrings(host)
  const workspacePath = context.workspacePath
  const [unityPath, setUnityPath] = useState<string | null>(null)
  const [launching, setLaunching] = useState(false)

  useEffect(() => {
    setUnityPath(null)
    if (!workspacePath) return
    let cancelled = false
    void isUnityProject(host, workspacePath).then((isUnity) => {
      if (!cancelled && isUnity) setUnityPath(workspacePath)
    })
    return () => {
      cancelled = true
    }
  }, [host, workspacePath])

  if (unityPath === null || unityPath !== workspacePath) return null

  const launch = async (): Promise<void> => {
    setLaunching(true)
    try {
      await launchEditor(host, strings, unityPath)
    } finally {
      setLaunching(false)
    }
  }

  return (
    <IconButton
      icon={<UnityIcon size={16} />}
      label={strings.openInUnity}
      tooltipLabel={strings.openInUnity}
      tooltipPlacement="bottom"
      size={28}
      disabled={launching}
      onClick={() => void launch()}
    />
  )
}
