import { ActionTooltip, IconButton, Menu, Skeleton, Spinner, type MenuEntry } from '@dotcraft/plugin'
import { CircleAlert, FolderOpen, GitBranch, MoreHorizontal, Play, Star, X } from 'lucide-react'
import type { JSX } from 'react'
import { fill, type UnityStrings } from './i18n'
import { folderName, type ProjectDetails, type StoredProject } from './model'
import { VcsMark } from './VcsMark'

export interface ProjectTableRow {
  readonly key: string
  readonly project: StoredProject
  readonly details: ProjectDetails | undefined
  readonly platform: string | null
  readonly installed: boolean | null
  readonly launching: boolean
}

export interface ProjectTableProps {
  readonly rows: readonly ProjectTableRow[]
  readonly locale: string
  readonly strings: UnityStrings
  readonly revealLabel: string
  readonly onOpen: (project: StoredProject) => void
  readonly onLaunch: (project: StoredProject) => void
  readonly onToggleFavorite: (project: StoredProject) => void
  readonly onReveal: (project: StoredProject) => void
  readonly onRemove: (project: StoredProject) => void
}

const UNITS: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600_000],
  ['month', 30 * 24 * 3600_000],
  ['week', 7 * 24 * 3600_000],
  ['day', 24 * 3600_000],
  ['hour', 3600_000],
  ['minute', 60_000]
]

function relativeTime(timestamp: number, locale: string): string | null {
  if (timestamp <= 0) return null
  const elapsed = timestamp - Date.now()
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style: 'short' })
  for (const [unit, size] of UNITS) {
    if (Math.abs(elapsed) >= size) return format.format(Math.round(elapsed / size), unit)
  }
  return format.format(0, 'minute')
}

function EditorVersion({ version, installed, strings }: { version: string | null; installed: boolean | null; strings: UnityStrings }): JSX.Element | null {
  if (version === null) return null
  return (
    <span className="unity-editor">
      <span className="unity-editor-version">{version}</span>
      {installed === false ? (
        <ActionTooltip label={strings.notInstalled}>
          <span className="unity-editor-missing" tabIndex={0} role="img" aria-label={strings.notInstalled}>
            <CircleAlert size={14} strokeWidth={1.8} aria-hidden />
          </span>
        </ActionTooltip>
      ) : null}
    </span>
  )
}

function ProjectRow({ row, table }: { row: ProjectTableRow; table: ProjectTableProps }): JSX.Element {
  const { project, details, launching } = row
  const { strings } = table
  const missing = details?.missing === true
  const name = folderName(project.path)
  const modified = details ? relativeTime(details.modifiedAtMs, table.locale) : null
  const favoriteLabel = project.favorite ? strings.unfavorite : strings.favorite
  const launchLabel = launching ? strings.launching : strings.launch

  const menu: MenuEntry[] = missing
    ? [{ label: strings.remove, icon: <X size={15} aria-hidden />, onSelect: () => table.onRemove(project) }]
    : [
        { label: table.revealLabel, icon: <FolderOpen size={15} aria-hidden />, onSelect: () => table.onReveal(project) },
        { type: 'separator' },
        { label: strings.remove, icon: <X size={15} aria-hidden />, onSelect: () => table.onRemove(project) }
      ]

  return (
    <div className="unity-table-row unity-row" role="row" data-missing={missing || undefined}>
      <span role="cell" className="unity-cell unity-cell-star">
        {missing ? null : (
          <span className="unity-star" data-on={project.favorite || undefined}>
            <IconButton
              icon={<Star size={15} fill={project.favorite ? 'currentColor' : 'none'} aria-hidden />}
              label={favoriteLabel}
              tooltipLabel={favoriteLabel}
              tooltipPlacement="bottom"
              size={28}
              aria-pressed={project.favorite}
              onClick={() => table.onToggleFavorite(project)}
            />
          </span>
        )}
      </span>
      <span role="cell" className="unity-cell unity-cell-vcs">
        <VcsMark provider={details?.vcs ?? null} />
      </span>
      <span role="cell" className="unity-cell unity-cell-name">
        <button
          type="button"
          className="unity-open"
          aria-label={fill(strings.open, { name })}
          disabled={missing}
          onClick={() => table.onOpen(project)}
        >
          <span className="unity-identity">
            <span className="unity-identity-title">
              <strong>{name}</strong>
              {missing ? <span className="unity-badge">{strings.notFound}</span> : null}
            </span>
            <span className="unity-identity-path">{project.path}</span>
          </span>
        </button>
      </span>
      <span role="cell" className="unity-cell unity-cell-branch">
        {details?.branch ? (
          <span className="unity-branch" title={details.branch}>
            <GitBranch size={12} aria-hidden />
            <span>{details.branch}</span>
          </span>
        ) : null}
      </span>
      <span role="cell" className="unity-cell unity-cell-platform">{missing ? null : row.platform}</span>
      <span role="cell" className="unity-cell unity-cell-editor">
        <EditorVersion version={details?.editorVersion ?? null} installed={row.installed} strings={strings} />
      </span>
      <span role="cell" className="unity-cell unity-cell-modified">{modified}</span>
      <span role="cell" className="unity-cell unity-cell-actions">
        {missing ? null : (
          <span className="unity-action" data-pending={launching || undefined}>
            <IconButton
              icon={launching ? <Spinner size={14} /> : <Play size={15} aria-hidden />}
              label={launchLabel}
              tooltipLabel={launchLabel}
              tooltipPlacement="bottom"
              size={28}
              aria-busy={launching || undefined}
              onClick={() => {
                if (!launching) table.onLaunch(project)
              }}
            />
          </span>
        )}
        <span className="unity-action">
          <Menu items={menu}>
            <IconButton
              icon={<MoreHorizontal size={16} aria-hidden />}
              label={strings.moreActions}
              tooltipLabel={strings.moreActions}
              tooltipPlacement="bottom"
              size={28}
            />
          </Menu>
        </span>
      </span>
    </div>
  )
}

function HeaderCells({ strings }: { strings: UnityStrings }): JSX.Element {
  return (
    <div className="unity-table-head" role="row">
      <span role="columnheader" className="unity-cell unity-cell-star" />
      <span role="columnheader" className="unity-cell unity-cell-vcs" />
      <span role="columnheader" className="unity-cell unity-cell-name">{strings.columnName}</span>
      <span role="columnheader" className="unity-cell unity-cell-branch">{strings.columnBranch}</span>
      <span role="columnheader" className="unity-cell unity-cell-platform">{strings.columnPlatform}</span>
      <span role="columnheader" className="unity-cell unity-cell-editor">{strings.columnEditor}</span>
      <span role="columnheader" className="unity-cell unity-cell-modified">{strings.columnModified}</span>
      <span role="columnheader" className="unity-cell unity-cell-actions" />
    </div>
  )
}

export function ProjectTable(props: ProjectTableProps): JSX.Element {
  return (
    <div className="unity-table" role="table" aria-label={props.strings.projects}>
      <HeaderCells strings={props.strings} />
      <div className="unity-table-body" role="rowgroup">
        {props.rows.map((row) => <ProjectRow key={row.key} row={row} table={props} />)}
      </div>
    </div>
  )
}

export function ProjectTableSkeleton({ label }: { label: string }): JSX.Element {
  return (
    <div className="unity-table" role="status" aria-busy="true" aria-label={label}>
      <div className="unity-table-head" aria-hidden="true">
        <span className="unity-cell unity-cell-star" />
        <span className="unity-cell unity-cell-vcs" />
        <span className="unity-cell unity-cell-name"><Skeleton width={40} height={10} /></span>
        <span className="unity-cell unity-cell-branch"><Skeleton width={36} height={10} /></span>
        <span className="unity-cell unity-cell-platform"><Skeleton width={36} height={10} /></span>
        <span className="unity-cell unity-cell-editor"><Skeleton width={56} height={10} /></span>
        <span className="unity-cell unity-cell-modified"><Skeleton width={48} height={10} /></span>
        <span className="unity-cell unity-cell-actions" />
      </div>
      {[0, 1, 2, 3].map((index) => (
        <div key={index} className="unity-table-row" aria-hidden="true">
          <span className="unity-cell unity-cell-star"><Skeleton width={14} height={14} circle /></span>
          <span className="unity-cell unity-cell-vcs"><Skeleton width={16} height={16} circle /></span>
          <span className="unity-cell unity-cell-name">
            <span className="unity-skeleton-lines">
              <Skeleton width="44%" height={12} />
              <Skeleton width="70%" height={10} />
            </span>
          </span>
          <span className="unity-cell unity-cell-branch"><Skeleton width="70%" height={10} /></span>
          <span className="unity-cell unity-cell-platform"><Skeleton width="80%" height={10} /></span>
          <span className="unity-cell unity-cell-editor"><Skeleton width="76%" height={10} /></span>
          <span className="unity-cell unity-cell-modified"><Skeleton width="60%" height={10} /></span>
          <span className="unity-cell unity-cell-actions" />
        </div>
      ))}
    </div>
  )
}
