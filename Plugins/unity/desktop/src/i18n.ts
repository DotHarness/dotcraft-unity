import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'

const LOCALES = ['en', 'zh-Hans'] as const

type Locale = (typeof LOCALES)[number]

export interface UnityStrings {
  readonly title: string
  readonly hero: string
  readonly projects: string
  readonly search: string
  readonly filter: string
  readonly editorVersion: string
  readonly platform: string
  readonly all: string
  readonly refresh: string
  readonly add: string
  readonly addMenu: string
  readonly addFromDisk: string
  readonly importFromHub: string
  readonly pickFolderTitle: string
  readonly columnName: string
  readonly columnBranch: string
  readonly columnPlatform: string
  readonly columnEditor: string
  readonly columnModified: string
  readonly emptyTitle: string
  readonly emptyDescription: string
  readonly noMatches: string
  readonly loading: string
  readonly open: string
  readonly launch: string
  readonly launching: string
  readonly openInUnity: string
  readonly moreActions: string
  readonly showInExplorer: string
  readonly showInFinder: string
  readonly showInFileManager: string
  readonly remove: string
  readonly favorite: string
  readonly unfavorite: string
  readonly notInstalled: string
  readonly notFound: string
  readonly notUnityProject: string
  readonly hubRequired: string
  readonly hostTooOld: string
  readonly alreadyOpen: string
  readonly openFailed: string
  readonly openFailedDetail: string
  readonly importFailed: string
  readonly importedOne: string
  readonly importedMany: string
  readonly importedNone: string
  readonly saveFailed: string
  readonly revealFailed: string
}

const CATALOG: Record<Locale, UnityStrings> = {
  en: {
    title: 'Unity',
    hero: 'Build your Unity projects with DotCraft',
    projects: 'Projects',
    search: 'Search projects',
    filter: 'Filter',
    editorVersion: 'Editor version',
    platform: 'Platform',
    all: 'All',
    refresh: 'Refresh',
    add: 'Add',
    addMenu: 'Add a project',
    addFromDisk: 'Add from disk',
    importFromHub: 'Import from Unity Hub',
    pickFolderTitle: 'Choose a Unity project folder',
    columnName: 'Name',
    columnBranch: 'Branch',
    columnPlatform: 'Platform',
    columnEditor: 'Editor',
    columnModified: 'Modified',
    emptyTitle: 'No Unity projects yet',
    emptyDescription: 'Import the projects you already have in Unity Hub, or add a project folder.',
    noMatches: 'No projects match your search',
    loading: 'Loading projects',
    open: 'Open {name}',
    launch: 'Launch Editor',
    launching: 'Launching Editor',
    openInUnity: 'Open in Unity',
    moreActions: 'More actions',
    showInExplorer: 'Show in File Explorer',
    showInFinder: 'Show in Finder',
    showInFileManager: 'Show in file manager',
    remove: 'Remove from list',
    favorite: 'Add to favorites',
    unfavorite: 'Remove from favorites',
    notInstalled: "This Editor version isn't installed",
    notFound: 'Not found',
    notUnityProject: "This folder isn't a Unity project",
    hubRequired: 'Unity Hub 3.22 or later is required',
    hostTooOld: 'Update DotCraft to use this',
    alreadyOpen: 'This project is already open in Unity',
    openFailed: 'Unable to open Unity',
    openFailedDetail: 'Unable to open Unity: {detail}',
    importFailed: "Couldn't read your Unity Hub projects",
    importedOne: 'Added 1 project from Unity Hub',
    importedMany: 'Added {count} projects from Unity Hub',
    importedNone: 'No new projects in Unity Hub',
    saveFailed: "Couldn't save your project list",
    revealFailed: "Couldn't open the folder"
  },
  'zh-Hans': {
    title: 'Unity',
    hero: '用 DotCraft 开发你的 Unity 项目',
    projects: '项目',
    search: '搜索项目',
    filter: '筛选',
    editorVersion: '编辑器版本',
    platform: '平台',
    all: '全部',
    refresh: '刷新',
    add: '添加',
    addMenu: '添加项目',
    addFromDisk: '从磁盘添加',
    importFromHub: '从 Unity Hub 导入',
    pickFolderTitle: '选择 Unity 项目文件夹',
    columnName: '名称',
    columnBranch: '分支',
    columnPlatform: '平台',
    columnEditor: '编辑器版本',
    columnModified: '修改时间',
    emptyTitle: '还没有 Unity 项目',
    emptyDescription: '导入 Unity Hub 里已有的项目，或添加一个项目文件夹。',
    noMatches: '没有匹配的项目',
    loading: '正在加载项目',
    open: '打开 {name}',
    launch: '启动编辑器',
    launching: '正在启动编辑器',
    openInUnity: '在 Unity 中打开',
    moreActions: '更多操作',
    showInExplorer: '在文件资源管理器中显示',
    showInFinder: '在访达中显示',
    showInFileManager: '在文件管理器中显示',
    remove: '从列表中移除',
    favorite: '收藏',
    unfavorite: '取消收藏',
    notInstalled: '未安装此编辑器版本',
    notFound: '未找到',
    notUnityProject: '这个文件夹不是 Unity 项目',
    hubRequired: '需要 Unity Hub 3.22 或更高版本',
    hostTooOld: '请先更新 DotCraft',
    alreadyOpen: '这个项目已在 Unity 中打开',
    openFailed: '无法打开 Unity',
    openFailedDetail: '无法打开 Unity：{detail}',
    importFailed: '无法读取 Unity Hub 中的项目',
    importedOne: '已从 Unity Hub 添加 1 个项目',
    importedMany: '已从 Unity Hub 添加 {count} 个项目',
    importedNone: 'Unity Hub 中没有新项目',
    saveFailed: '无法保存项目列表',
    revealFailed: '无法打开文件夹'
  }
}

export function stringsFor(locale: string): UnityStrings {
  return locale === 'zh-Hans' ? CATALOG['zh-Hans'] : CATALOG.en
}

export function translationsOf(key: keyof UnityStrings): Record<string, string> {
  return Object.fromEntries(LOCALES.map((locale) => [locale, CATALOG[locale][key]]))
}

export function useLocale(host: DesktopPluginHost): string {
  return useSyncExternalStore(
    (listener) => host.environment.onChange(listener),
    () => host.environment.locale
  )
}

export function useStrings(host: DesktopPluginHost): UnityStrings {
  return stringsFor(useLocale(host))
}

export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) => (key in values ? String(values[key]) : match))
}
