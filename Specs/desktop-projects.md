# Unity projects in DotCraft Desktop

| Field | Value |
|---|---|
| Version | 0.5.0 |
| Status | Living |
| Date | 2026-10-12 |
| Related Specs | [Unity integration](unity-integration.md) |

This specification defines the Desktop experience of the native `unity` plugin: a Unity
page in the DotCraft Desktop sidebar that keeps the user's Unity projects, opens one as
a DotCraft workspace and launches its Editor, and a launch button in the conversation
of a Unity project.

## Scope

Goals:

- Find a Unity project and start working on it in DotCraft without leaving the app.
- Launch the Unity Editor for a project from the Unity page or from its conversation.
- Bring in the projects the user already keeps in Unity Hub.

Non-goals:

- Creating projects, changing a project's Editor version or build target, and managing
  Editor installs or modules.
- Launch settings, Editor status, compile errors and test results.
- Source-control actions and Unity Cloud connections. The page only shows which version
  control a project uses and its current branch.
- Remote workspaces. The page and the launch button act on the computer that runs the
  foreground DotCraft AppServer; their behavior on a remote workspace is unspecified.

## Boundaries

- The plugin reads project folders and runs Unity's command-line tool on the AppServer
  host. It never changes a project's files or Unity Hub's own records.
- Importing projects, listing installed Editors and launching an Editor rely on Unity's
  command-line tool, which ships with Unity Hub 3.22 and later. When it is unavailable,
  every action that needs it says that Unity Hub 3.22 or later is required, and the rest
  of the page keeps working.
- The page uses DotCraft's catalog page frame, so it looks and behaves like DotCraft's
  own Plugins and Automations pages.
- The plugin requires DotCraft 0.8.5 or later. If its Desktop module runs on an older
  DotCraft, the page shows "Update DotCraft to use this" instead of the project list.

## Project list

The project list belongs to the plugin, not to Unity Hub. It is personal to the user and
shared by every workspace on the computer. Each entry keeps the project's absolute path,
whether it is a favourite, and when it was added. A path already in the list is never
added twice; on Windows paths compare case-insensitively.

A folder is a Unity project when it contains an `Assets` folder and a readable
`ProjectSettings/ProjectVersion.txt`. Every addition is checked, and a folder that is not
a Unity project is refused with "This folder isn't a Unity project".

Projects enter the list in two ways:

- **Add from disk** lets the user pick a folder and adds it.
- **Import from Unity Hub** adds every Unity Hub project that is not in the list yet and
  keeps Hub's favourites. It reports how many projects were added. Importing again only
  adds new projects, and removing a project from the list never affects Unity Hub.

## Unity page

The page appears in the sidebar as "Unity" with the Unity mark. Its title is "Build your
Unity projects with DotCraft".

The top bar holds **Refresh** and **Add**. Refresh re-reads every project, the Unity Hub
project list and the installed Editors. Add adds from disk; its menu also offers "Import
from Unity Hub".

Under the title, a search field filters projects by name and path, and a filter menu
narrows them by **Editor version** and **Platform**. Each filter offers "All" plus the
values present in the list. An empty list hides the search row, explains what the page is
for and offers both ways to add projects. While the list loads it shows placeholder rows;
a search or filter that matches nothing says so.

The **Projects** group is a table under one quiet header row: Name, Branch, Platform,
Editor and Modified. Favourites come first, then the rest by modification time, newest
first. Each row shows:

- a favourite star that toggles the favourite;
- the project's version control mark, whose tooltip names the provider;
- the folder name and full path;
- the current branch, or nothing when the project is not under version control;
- the platform the project builds for, when Unity Hub knows the project;
- the Editor version the project uses, followed by an alert icon with the tooltip "This
  Editor version isn't installed" when that version is not installed;
- when the folder was last modified;
- **Launch Editor** (a play button) and the row menu, which appear on hover or keyboard
  focus in space the row always reserves, so nothing shifts. While the Editor launches,
  the play button shows progress and stays visible.

As the page narrows, the Branch column hides first, then Platform.

A project whose folder is gone is dimmed, shows "Not found" and offers only removal.

Row actions:

- Clicking a row opens the project as a DotCraft workspace, ready for a new conversation.
- **Launch Editor** launches the project's Editor.
- The row menu offers **Show in File Explorer** (Finder on macOS) and **Remove from
  list**.

Project details are read when the page opens, when DotCraft regains focus and on Refresh.
A detail that cannot be read leaves its cell empty and never hides the row.

### Version control marks

- A Unity Version Control workspace shows the Unity mark and its selected branch.
- A Git repository shows its current branch. Its mark follows the host of the `origin`
  remote: GitHub for `github.com`, GitLab for `gitlab.com`, and the Git mark for any
  other host or when there is no `origin`. Linked worktrees and submodules count as Git
  repositories.
- Any other project shows no mark.

## Launch button in conversations

In a conversation whose workspace root is a Unity project, the conversation header shows
a button with the Unity mark and the tooltip "Open in Unity". It launches the Editor for
that workspace. Conversations in other workspaces do not show it.

## Launching the Editor

- Launching hands the project to Unity and does not wait for the Editor to finish
  starting. The control that started it is disabled until the hand-off completes.
- Which Editor version runs is Unity's decision for that project. When Unity refuses, for
  example because the project's Editor version is missing, DotCraft shows "Unable to open
  Unity" with Unity's own message.
- A project that is already open in a Unity Editor is never launched a second time;
  DotCraft shows "This project is already open in Unity" instead. A project whose Editor
  has exited, including after a crash, launches normally.

## Localization

All page and button text is available in English and Simplified Chinese and follows
DotCraft's language.

## Packaging

The Desktop module ships inside the `unity` plugin bundle and is covered by the bundle's
integrity manifest like every other bundle file.
