# Open in Unity from DotCraft Desktop

| Field | Value |
|---|---|
| Version | 0.7.0 |
| Status | Living |
| Date | 2026-11-27 |
| Related Specs | [Unity integration](unity-integration.md) |

This specification defines the Desktop module of the native `unity` plugin: a button in
the conversation of a Unity project that launches the project's Unity Editor.

## Scope

Goals:

- Launch the Unity Editor for the project of a conversation without leaving DotCraft.

Non-goals:

- Keeping a list of Unity projects, importing projects from Unity Hub, creating projects,
  and managing Editor installs or modules. A Unity project is added to DotCraft like any
  other project.
- Launch settings, Editor status, compile errors and test results.
- Remote workspaces. The button acts on the computer that runs the foreground DotCraft
  AppServer. Its behavior on a remote workspace is unspecified.

## Boundaries

- The plugin reads project folders and runs Unity's command-line tool on the AppServer
  host. It never changes a project's files or Unity Hub's own records.
- Launching an Editor relies on Unity's command-line tool, which ships with Unity Hub
  3.22 and later. When it is unavailable, launching says that Unity Hub 3.22 or later is
  required.
- The plugin requires DotCraft 0.8.6 or later. On an older DotCraft, launching says
  "Update DotCraft to use this".

## Launch button

A folder is a Unity project when it contains an `Assets` folder and a readable
`ProjectSettings/ProjectVersion.txt`.

In a conversation whose workspace root is a Unity project, the conversation header shows
a button with the Unity mark and the tooltip "Open in Unity". It launches the Editor for
that workspace. Conversations in other workspaces do not show it.

## Launching the Editor

- Launching hands the project to Unity and does not wait for the Editor to finish
  starting. The button is disabled until the hand-off completes.
- Which Editor version runs is Unity's decision for that project. When Unity refuses, for
  example because the project's Editor version is missing, DotCraft shows "Unable to open
  Unity" with Unity's own message.
- A project that is already open in a Unity Editor is never launched a second time.
  DotCraft shows "This project is already open in Unity" instead. A project whose Editor
  has exited, including after a crash, launches normally.

## Localization

All button text is available in English and Simplified Chinese and follows DotCraft's
language.

## Packaging

The Desktop module ships inside the `unity` plugin bundle and is covered by the bundle's
integrity manifest like every other bundle file.
