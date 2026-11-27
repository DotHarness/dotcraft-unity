import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { UnityHeaderAction } from './UnityHeaderAction'

export const activate: DesktopPluginActivate = (host) => {
  host.ui.add('thread.header.actions', UnityHeaderAction)
}
