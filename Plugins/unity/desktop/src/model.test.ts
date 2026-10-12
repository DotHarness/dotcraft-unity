import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  addProjects,
  folderName,
  gitProvider,
  joinPath,
  parseCliJson,
  parseGitDirPointer,
  parseGitHead,
  parseGitOriginUrl,
  parseHubProjects,
  parseInstalledEditors,
  parsePlasticSelector,
  parseProjectVersion,
  readStoredProjects,
  sortProjects
} from './model.ts'

test('reads the Editor version from ProjectVersion.txt', () => {
  const text = 'm_EditorVersion: 6000.3.16f1\r\nm_EditorVersionWithRevision: 6000.3.16f1 (abc123)\r\n'
  assert.equal(parseProjectVersion(text), '6000.3.16f1')
  assert.equal(parseProjectVersion('m_EditorVersionWithRevision: 1 (x)'), null)
})

test('reads the branch from git HEAD', () => {
  assert.equal(parseGitHead('ref: refs/heads/feature/scene-tools\n'), 'feature/scene-tools')
  assert.equal(parseGitHead('0123456789abcdef0123456789abcdef01234567\n'), '0123456')
  assert.equal(parseGitHead(''), null)
  assert.equal(parseGitDirPointer('gitdir: ../.git/worktrees/ceres\n'), '../.git/worktrees/ceres')
})

test('reads the origin URL from a git config', () => {
  const config = [
    '[core]',
    '\trepositoryformatversion = 0',
    '[remote "upstream"]',
    '\turl = https://gitlab.com/other/repo.git',
    '[remote "origin"]',
    '\tfetch = +refs/heads/*:refs/remotes/origin/*',
    '\tURL = "git@github.com:studio/game.git" ; primary',
    ''
  ].join('\r\n')
  assert.equal(parseGitOriginUrl(config), 'git@github.com:studio/game.git')
  assert.equal(parseGitOriginUrl('[remote "upstream"]\n\turl = https://github.com/o/r.git\n'), null)
})

test('names the git host of the origin', () => {
  assert.equal(gitProvider('https://github.com/studio/game.git'), 'github')
  assert.equal(gitProvider('https://user@GitHub.com/studio/game'), 'github')
  assert.equal(gitProvider('git@github.com:studio/game.git'), 'github')
  assert.equal(gitProvider('ssh://git@gitlab.com:2222/studio/game.git'), 'gitlab')
  assert.equal(gitProvider('git@gitlab.com:studio/game.git'), 'gitlab')
  assert.equal(gitProvider('https://git.example.com/studio/game.git'), 'git')
  assert.equal(gitProvider('https://github.com.example.net/studio/game.git'), 'git')
  assert.equal(gitProvider('C:\\repos\\game.git'), 'git')
  assert.equal(gitProvider('file:///srv/github.com/game.git'), 'git')
  assert.equal(gitProvider(null), 'git')
})

test('reads the branch from a Unity Version Control selector', () => {
  assert.equal(parsePlasticSelector('repository "Game@Studio@cloud"\n  path "/"\n    smartbranch "/main/feature"\n'), '/main/feature')
  assert.equal(parsePlasticSelector('rep "Game"\n  path "/"\n    br "/main"\n'), '/main')
  assert.equal(parsePlasticSelector('rep "Game"\n'), null)
})

test('reads Unity Hub JSON and ignores unknown fields', () => {
  const stdout = `notice\n${JSON.stringify({
    success: true,
    command: 'projects list',
    data: [
      { title: 'Ceres', path: 'E:/Unity/Ceres/', version: '6000.3.16f1', buildTarget: 'StandaloneWindows64', isFavorite: true, extra: 1 },
      { title: 'Broken' },
      { path: '/Users/me/Game', isFavorite: false, buildTarget: '' }
    ],
    errors: [],
    warnings: []
  })}\n`
  assert.deepEqual(parseHubProjects(parseCliJson(stdout)), [
    { path: 'E:\\Unity\\Ceres', favorite: true, buildTarget: 'StandaloneWindows64' },
    { path: '/Users/me/Game', favorite: false, buildTarget: null }
  ])
  assert.equal(parseHubProjects(parseCliJson('{"success":false,"data":[]}')), null)
  assert.equal(parseCliJson('not json'), null)
  const editors = parseInstalledEditors({ success: true, data: [{ version: '6000.3.16f1', location: 'x' }, { alias: 'y' }] })
  assert.deepEqual([...(editors ?? [])], ['6000.3.16f1'])
})

test('adds projects once, comparing Windows paths case-insensitively', () => {
  const list = readStoredProjects([
    { path: 'E:\\Unity\\Ceres', favorite: true, addedAt: 1 },
    { path: 'e:/unity/ceres/', favorite: false, addedAt: 2 },
    { path: 42 }
  ])
  assert.equal(list.length, 1)
  const next = addProjects(list, [
    { path: 'E:\\UNITY\\CERES', favorite: false, addedAt: 3 },
    { path: '/home/me/Game', favorite: false, addedAt: 3 },
    { path: '/home/me/game', favorite: false, addedAt: 3 }
  ])
  assert.deepEqual(next.map((project) => project.path), ['E:\\Unity\\Ceres', '/home/me/Game', '/home/me/game'])
})

test('sorts favourites first, then newest first', () => {
  const projects = readStoredProjects([
    { path: 'C:\\a\\Old', favorite: false },
    { path: 'C:\\a\\New', favorite: false },
    { path: 'C:\\a\\Pinned', favorite: true }
  ])
  const times: Record<string, number> = { Old: 1, New: 5, Pinned: 0 }
  assert.deepEqual(
    sortProjects(projects, (project) => times[folderName(project.path)]).map((project) => folderName(project.path)),
    ['Pinned', 'New', 'Old']
  )
})

test('joins paths with the platform separator', () => {
  assert.equal(joinPath('E:\\Unity\\Ceres', 'ProjectSettings', 'ProjectVersion.txt'), 'E:\\Unity\\Ceres\\ProjectSettings\\ProjectVersion.txt')
  assert.equal(joinPath('/Users/me/Game', '.git', 'HEAD'), '/Users/me/Game/.git/HEAD')
})
