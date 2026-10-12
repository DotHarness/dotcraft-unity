import type { DesktopPluginIconProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

const UNITY_MARK =
  'm1487.5 1176.1v-784.1l-679-392v301l266 154c10.5 7 10.5 21.1 0 24.6l-315 182c-10.5 7-21 3.5-28 0l-315-182c-10.5-3.5-10.5-21.1 0-24.6l266-154v-301l-682.5 392v784.1-3.5 3.5l259-150.5v-308c0-10.5 14-17.5 21-14l315 182c10.5 7 14 14 14 24.5v364c0 10.5-14 17.5-21 14l-266-154-259 150.5 679 395.6 679-392.1-259-150.5-266 154c-10.5 7-21 0-21-14v-364c0-10.5 7-21 14-24.5l315-182c10.5-7 21 0 21 14v308z'

export function UnityIcon({ size = 16, style }: DesktopPluginIconProps): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="-96 0 1680 1680" fill="currentColor" aria-hidden="true" style={style}>
      <path d={UNITY_MARK} />
    </svg>
  )
}
