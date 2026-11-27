import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'

type Locale = 'en' | 'zh-Hans'

export interface UnityStrings {
  readonly openInUnity: string
  readonly hubRequired: string
  readonly hostTooOld: string
  readonly alreadyOpen: string
  readonly openFailed: string
  readonly openFailedDetail: string
}

const CATALOG: Record<Locale, UnityStrings> = {
  en: {
    openInUnity: 'Open in Unity',
    hubRequired: 'Unity Hub 3.22 or later is required',
    hostTooOld: 'Update DotCraft to use this',
    alreadyOpen: 'This project is already open in Unity',
    openFailed: 'Unable to open Unity',
    openFailedDetail: 'Unable to open Unity: {detail}'
  },
  'zh-Hans': {
    openInUnity: '在 Unity 中打开',
    hubRequired: '需要 Unity Hub 3.22 或更高版本',
    hostTooOld: '请先更新 DotCraft',
    alreadyOpen: '这个项目已在 Unity 中打开',
    openFailed: '无法打开 Unity',
    openFailedDetail: '无法打开 Unity：{detail}'
  }
}

export function useStrings(host: DesktopPluginHost): UnityStrings {
  const locale = useSyncExternalStore(
    (listener) => host.environment.onChange(listener),
    () => host.environment.locale
  )
  return locale === 'zh-Hans' ? CATALOG['zh-Hans'] : CATALOG.en
}

export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) => (key in values ? String(values[key]) : match))
}
