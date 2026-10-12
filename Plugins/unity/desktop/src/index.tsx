import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { translationsOf } from './i18n'
import { connectProjectStore } from './store'
import { UnityHeaderAction } from './UnityHeaderAction'
import { UnityIcon } from './UnityIcon'
import { UnityProjectsView } from './UnityProjectsView'
import './styles.css'

export const activate: DesktopPluginActivate = (host) => {
  host.effect(() => connectProjectStore(host.settings))
  host.ui.add('thread.header.actions', UnityHeaderAction)

  return {
    mainViews: [
      {
        id: 'projects',
        label: { default: 'Unity', translations: translationsOf('title') },
        icon: UnityIcon,
        component: UnityProjectsView
      }
    ]
  }
}
