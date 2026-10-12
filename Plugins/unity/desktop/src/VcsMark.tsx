import { ActionTooltip } from '@dotcraft/plugin'
import type { JSX } from 'react'
import type { VcsProvider } from './model'
import { UnityIcon } from './UnityIcon'

const GITHUB_MARK =
  'M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.58.1.79-.25.79-.56v-2c-3.2.7-3.87-1.54-3.87-1.54-.52-1.33-1.28-1.68-1.28-1.68-1.05-.72.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.55-.29-5.24-1.28-5.24-5.69 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.04 0 0 .97-.31 3.18 1.18a11.04 11.04 0 0 1 5.78 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.58.23 2.75.11 3.04.74.81 1.18 1.84 1.18 3.1 0 4.42-2.69 5.39-5.26 5.68.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56C20.22 21.39 23.5 17.08 23.5 12 23.5 5.65 18.35.5 12 .5Z'

const GITLAB_MARK =
  'm23.6 9.6-.03-.09L20.42.7a.83.83 0 0 0-1.55.05L16.74 7.2H7.27L5.13.75a.82.82 0 0 0-.78-.56.83.83 0 0 0-.78.56L.43 9.5l-.03.1a5.85 5.85 0 0 0 2.13 6.78l.01.01.03.02 5.27 3.94 2.61 1.97 1.58 1.2a.97.97 0 0 0 1.18 0l1.58-1.2 2.6-1.97 5.32-3.96.02-.02a5.86 5.86 0 0 0 2.12-6.77Z'

const GIT_MARK =
  'M23.546 10.93L13.067.452c-.604-.603-1.582-.603-2.188 0L8.708 2.627l2.76 2.76c.645-.215 1.379-.07 1.889.441.516.515.658 1.258.438 1.9l2.658 2.66c.645-.223 1.387-.078 1.9.435.721.72.721 1.884 0 2.604-.719.719-1.881.719-2.6 0-.539-.541-.674-1.337-.404-1.996L12.86 8.955v6.525c.176.086.342.203.488.348.713.721.713 1.883 0 2.6-.719.721-1.889.721-2.609 0-.719-.719-.719-1.879 0-2.598.182-.18.387-.316.605-.406V8.835c-.217-.091-.424-.222-.6-.401-.545-.545-.676-1.342-.396-2.009L7.636 3.7.45 10.881c-.6.605-.6 1.584 0 2.189l10.48 10.477c.604.604 1.582.604 2.186 0l10.43-10.43c.605-.603.605-1.582 0-2.187'

const PROVIDER_NAMES: Record<VcsProvider, string> = {
  github: 'GitHub',
  gitlab: 'GitLab',
  git: 'Git',
  uvcs: 'Unity Version Control'
}

const PROVIDER_PATHS: Record<Exclude<VcsProvider, 'uvcs'>, string> = {
  github: GITHUB_MARK,
  gitlab: GITLAB_MARK,
  git: GIT_MARK
}

function Glyph({ provider }: { provider: VcsProvider }): JSX.Element {
  if (provider === 'uvcs') return <UnityIcon size={16} />
  return (
    <svg width={16} height={16} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
      <path d={PROVIDER_PATHS[provider]} />
    </svg>
  )
}

export function VcsMark({ provider }: { provider: VcsProvider | null }): JSX.Element | null {
  if (provider === null) return null
  const name = PROVIDER_NAMES[provider]
  return (
    <ActionTooltip label={name}>
      <span className="unity-vcs" role="img" aria-label={name}>
        <Glyph provider={provider} />
      </span>
    </ActionTooltip>
  )
}
