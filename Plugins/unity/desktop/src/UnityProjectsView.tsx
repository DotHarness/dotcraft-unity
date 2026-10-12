import {
  Button,
  CatalogPage,
  CatalogSection,
  CatalogToolbarButton,
  SplitButton,
  type DesktopPluginViewProps
} from '@dotcraft/plugin'
import { Download, FolderOpen, MonitorSmartphone, RotateCw, Tag } from 'lucide-react'
import { useMemo, useState, type JSX } from 'react'
import { launchEditor, unityRequirementMessage } from './actions'
import { fill, stringsFor, useLocale, useStrings, type UnityStrings } from './i18n'
import { hubIndex, useProjectInsights } from './insights'
import {
  addProjects,
  buildTargetName,
  compareEditorVersions,
  distinctValues,
  hasProject,
  matchesQuery,
  pathKey,
  removeProject,
  setFavorite,
  sortProjects,
  type StoredProject
} from './model'
import { ProjectTable, ProjectTableSkeleton, type ProjectTableRow } from './ProjectTable'
import { currentProjects, useProjects, writeProjects } from './store'
import { isUnityProject, listHubProjects } from './unity'
import { UnityIcon } from './UnityIcon'

const CATALOG_KIT: readonly unknown[] = [CatalogPage, CatalogSection, CatalogToolbarButton, SplitButton]
const UNFILTERED = ''

function revealLabel(strings: UnityStrings): string {
  const agent = navigator.userAgent
  if (/Mac/i.test(agent)) return strings.showInFinder
  if (/Windows/i.test(agent)) return strings.showInExplorer
  return strings.showInFileManager
}

function HostTooOld({ host }: DesktopPluginViewProps): JSX.Element {
  const strings = useStrings(host)
  return <p className="unity-host-too-old">{strings.hostTooOld}</p>
}

function ProjectsPage({ host }: DesktopPluginViewProps): JSX.Element {
  const locale = useLocale(host)
  const strings = stringsFor(locale)
  const projects = useProjects()
  const [insights, setHub, refresh] = useProjectInsights(host, projects)
  const [query, setQuery] = useState('')
  const [editorFilter, setEditorFilter] = useState(UNFILTERED)
  const [platformFilter, setPlatformFilter] = useState(UNFILTERED)
  const [importing, setImporting] = useState(false)
  const [launching, setLaunching] = useState<ReadonlySet<string>>(() => new Set())

  const allRows = useMemo((): ProjectTableRow[] => {
    if (!projects) return []
    const sorted = sortProjects(projects, (project) => insights.details.get(pathKey(project.path))?.modifiedAtMs ?? 0)
    return sorted.map((project) => {
      const key = pathKey(project.path)
      const details = insights.details.get(key)
      const version = details?.editorVersion ?? null
      const target = insights.hub?.get(key)?.buildTarget ?? null
      return {
        key,
        project,
        details,
        platform: target === null ? null : buildTargetName(target),
        installed: version && insights.installedEditors ? insights.installedEditors.has(version) : null,
        launching: launching.has(key)
      }
    })
  }, [projects, insights.details, insights.hub, insights.installedEditors, launching])

  const editorVersions = useMemo(
    () => distinctValues(allRows.map((row) => row.details?.editorVersion ?? null)).sort((left, right) => compareEditorVersions(right, left)),
    [allRows]
  )
  const platforms = useMemo(
    () => distinctValues(allRows.map((row) => row.platform)).sort((left, right) => left.localeCompare(right)),
    [allRows]
  )

  const rows = allRows.filter((row) =>
    matchesQuery(row.project, query)
    && (editorFilter === UNFILTERED || row.details?.editorVersion === editorFilter)
    && (platformFilter === UNFILTERED || row.platform === platformFilter))

  const save = async (next: readonly StoredProject[]): Promise<boolean> => {
    try {
      await writeProjects(next)
      return true
    } catch {
      host.ui.showToast({ message: strings.saveFailed, tone: 'error' })
      return false
    }
  }

  const addFromDisk = async (): Promise<void> => {
    const path = await host.ui.pickFolder({ title: strings.pickFolderTitle })
    if (path === null || hasProject(currentProjects(), path)) return
    if (!(await isUnityProject(host, path))) {
      host.ui.showToast({ message: strings.notUnityProject, tone: 'error' })
      return
    }
    await save(addProjects(currentProjects(), [{ path, favorite: false, addedAt: Date.now() }]))
  }

  const importFromHub = async (): Promise<void> => {
    setImporting(true)
    try {
      const hubProjects = await listHubProjects(host)
      setHub(hubIndex(hubProjects))
      const candidates = hubProjects.filter((project) => !hasProject(currentProjects(), project.path))
      const valid = await Promise.all(candidates.map((project) => isUnityProject(host, project.path)))
      const addedAt = Date.now()
      const additions = candidates
        .filter((_, index) => valid[index])
        .map((project) => ({ path: project.path, favorite: project.favorite, addedAt }))
      if (additions.length > 0 && !(await save(addProjects(currentProjects(), additions)))) return
      const message = additions.length === 0
        ? strings.importedNone
        : additions.length === 1
          ? strings.importedOne
          : fill(strings.importedMany, { count: additions.length })
      host.ui.showToast({ message, tone: additions.length > 0 ? 'success' : 'neutral' })
    } catch (error) {
      host.ui.showToast({ message: unityRequirementMessage(error, strings) ?? strings.importFailed, tone: 'error' })
    } finally {
      setImporting(false)
    }
  }

  const launch = async (path: string): Promise<void> => {
    const key = pathKey(path)
    setLaunching((current) => new Set(current).add(key))
    try {
      await launchEditor(host, strings, path)
    } finally {
      setLaunching((current) => {
        const next = new Set(current)
        next.delete(key)
        return next
      })
    }
  }

  const reveal = (path: string): void => {
    void host.navigation.openPath(path, { target: 'fileManager' }).catch(() => {
      host.ui.showToast({ message: strings.revealFailed, tone: 'error' })
    })
  }

  const openWorkspace = (path: string): void => {
    void host.navigation.openExternal(`dotcraft://threads/new?path=${encodeURIComponent(path)}`)
  }

  const empty = projects !== null && projects.length === 0

  const actions = (
    <>
      <CatalogToolbarButton icon={<RotateCw size={15} strokeWidth={1.8} aria-hidden />} label={strings.refresh} onClick={refresh} />
      <SplitButton
        label={strings.add}
        menuLabel={strings.addMenu}
        onClick={() => void addFromDisk()}
        items={[
          { label: strings.addFromDisk, icon: <FolderOpen size={15} aria-hidden />, onSelect: () => void addFromDisk() },
          {
            label: strings.importFromHub,
            icon: <Download size={15} aria-hidden />,
            disabled: importing,
            onSelect: () => void importFromHub()
          }
        ]}
      />
    </>
  )

  return (
    <CatalogPage
      title={strings.hero}
      actions={actions}
      search={empty ? undefined : { value: query, placeholder: strings.search, onValueChange: setQuery }}
      filters={empty ? undefined : {
        ariaLabel: strings.filter,
        groups: [
          {
            label: strings.editorVersion,
            icon: <Tag size={14} aria-hidden />,
            value: editorFilter,
            onValueChange: setEditorFilter,
            options: [{ value: UNFILTERED, label: strings.all }, ...editorVersions.map((value) => ({ value, label: value }))]
          },
          {
            label: strings.platform,
            icon: <MonitorSmartphone size={14} aria-hidden />,
            value: platformFilter,
            onValueChange: setPlatformFilter,
            options: [{ value: UNFILTERED, label: strings.all }, ...platforms.map((value) => ({ value, label: value }))]
          }
        ]
      }}
    >
      <div className="unity-projects">
        {empty ? (
          <section className="unity-empty">
            <span className="unity-empty-mark" aria-hidden="true">
              <UnityIcon size={28} />
            </span>
            <h2>{strings.emptyTitle}</h2>
            <p>{strings.emptyDescription}</p>
            <div className="unity-empty-actions">
              <Button
                variant="primary"
                iconLeft={<Download size={14} aria-hidden />}
                loading={importing}
                onClick={() => void importFromHub()}
              >
                {strings.importFromHub}
              </Button>
              <Button variant="secondary" iconLeft={<FolderOpen size={14} aria-hidden />} onClick={() => void addFromDisk()}>
                {strings.addFromDisk}
              </Button>
            </div>
          </section>
        ) : (
          <CatalogSection title={strings.projects}>
            {projects === null ? (
              <ProjectTableSkeleton label={strings.loading} />
            ) : rows.length === 0 ? (
              <p className="unity-no-matches">{strings.noMatches}</p>
            ) : (
              <ProjectTable
                rows={rows}
                locale={locale}
                strings={strings}
                revealLabel={revealLabel(strings)}
                onOpen={(project) => openWorkspace(project.path)}
                onLaunch={(project) => void launch(project.path)}
                onToggleFavorite={(project) => void save(setFavorite(currentProjects(), project.path, !project.favorite))}
                onReveal={(project) => reveal(project.path)}
                onRemove={(project) => void save(removeProject(currentProjects(), project.path))}
              />
            )}
          </CatalogSection>
        )}
      </div>
    </CatalogPage>
  )
}

export function UnityProjectsView(props: DesktopPluginViewProps): JSX.Element {
  return CATALOG_KIT.every((component) => component !== undefined) ? <ProjectsPage {...props} /> : <HostTooOld {...props} />
}
