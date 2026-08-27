---
name: dotcraft-unity
description: Use when dotcraft-unity MCP or unity_execute_csharp is available, or when the user asks to inspect, automate, capture, or debug Unity Editor state, scenes, assets, Console logs, or GameView output. Provides background-first Unity Editor automation rules.
---

# DotCraft Unity

## Overview

Use `unity_execute_csharp` for short Unity Editor snippets containing optional leading `using` directives followed by method-body statements. Treat it as live editor automation: it can inspect and mutate scene, asset, and project state. Do not send namespace, class, or method declarations.

This skill governs Unity tool calls, especially `unity_execute_csharp`. It does not replace normal repository file editing workflows.

## Inline Code Or A Saved Script

`unity_execute_csharp` takes either `code` or `path`, never both. `path` is project-relative and must stay inside the Unity project root. A script file holds exactly the same thing `code` does — optional leading `using` directives followed by method-body statements — and is never compiled by Unity.

Pass `args` to parameterise a script. It arrives as a Newtonsoft `JObject` named `Args`:

```csharp
var maxRows = (int?)Args["maxRows"] ?? 20;
```

Use `path` when a task recurs — the same script re-runs from a short path instead of resending the whole snippet, and it hits the compilation cache. Use inline `code` for one-off work.

## Where Scripts Live

Bundled scripts ship in `scripts/` inside this skill directory.

Write new scripts to `.craft/scripts/`. Before writing one, check whether it already exists there.

## References

Load only the reference needed for the current task:

- **Editor window screenshots**: Read `references/gameview-screenshot.md` when the user asks to capture, compare, inspect, or save what a Unity Editor window looks like — GameView, SceneView, or the whole Editor.
- **Console reading**: Read `references/console-reading.md` when the user asks to inspect, summarize, search, or diagnose Unity Console logs.
- **API helpers**: Read `references/api.md` when a `unity_execute_csharp` snippet needs loaded-type lookup or third-party component reflection.
- **Snippet failure modes**: Read `references/snippet-craft.md` when a snippet times out, returns a wall of stack traces, fails to compile without detail, or when the effect of a call is not visible in the same call.

## Default Behavior

Keep Unity in the background: prefer read-only inspection first, then make the smallest useful change when the user's request clearly asks to fix, create, modify, or automate something in Unity.

## Waiting For Editor Readiness

After an action triggers script compilation, asset import, Domain Reload, or asynchronous Shader compilation, do not report completion from the action's immediate return value alone. Probe the current Editor state with `unity_execute_csharp`:

```csharp
return new Dictionary<string, object>
{
    ["isCompiling"] = EditorApplication.isCompiling,
    ["isUpdating"] = EditorApplication.isUpdating,
    ["isShaderCompiling"] = ShaderUtil.anythingCompiling
};
```

If the call reports `UnityUnavailable` or `UnityDisconnected`, treat Unity as still reloading or recovering and retry after a short delay. When the call succeeds, require all three values to be `false` in two consecutive probes before reporting the Editor ready. Bound the whole retry sequence with a deadline appropriate to the operation; use 120 seconds by default. On timeout, report the last observed state instead of claiming completion.

For script or assembly-definition changes, read the Console after the Editor becomes ready and report visible compiler errors. Do not attribute pre-existing or filtered Console errors to the current operation without evidence.

## Good Uses

- Read Console logs, Editor state, Play Mode state, build settings, scene names, selected objects, GameObjects, Components, serialized fields, asset metadata, and project settings.
- Run small, bounded Editor automations the user requested, such as creating objects, adjusting components, updating selected assets, saving a scene, or validating a setup.
- Use `AssetDatabase`, `EditorSceneManager`, `PrefabUtility`, and `Undo` deliberately for bounded asset, scene, and prefab work.

## Avoid By Default

Do not call these APIs unless the user explicitly asked for the visible effect or there is no background alternative and you explain it first:

- `EditorWindow.Show()`, `ShowUtility()`, `Focus()`, `GetWindow(..., focus: true)`, or any equivalent foreground activation.
- `EditorUtility.DisplayDialog`, modal prompts, object pickers, menu execution, or other UI that requires user interaction.
- `EditorGUIUtility.PingObject`, `AssetDatabase.OpenAsset`, `Selection.activeObject`, or selection changes whose purpose is only visual navigation.
- `EditorApplication.ExecuteMenuItem`, layout changes, opening Unity windows, docking changes, or changing the active view.
- Entering or exiting Play Mode, pausing, stepping frames, or changing time scale unless the user requested runtime inspection.
- Broad `AssetDatabase.Refresh`, `ImportAsset`, `ForceReserializeAssets`, scene saves, package changes, or project-wide rewrites.

Do not poll or sleep in a `unity_execute_csharp` snippet. For compilation, import, Domain Reload, and Shader work, perform bounded external readiness probes as described above. For other asynchronous actions, return the current state and perform one bounded follow-up check.

## Confirmation Policy

If the user's request clearly asks for a Unity change, perform ordinary bounded changes without extra confirmation. Ask before high-risk actions:

- Deleting scenes, assets, GameObjects, Components, prefabs, or project settings.
- Entering or exiting Play Mode, changing scenes, saving dirty scenes, or modifying build settings.
- Package Manager changes, project-wide asset import/reimport, `AssetDatabase.Refresh`, or large batch operations.
- Running external processes, opening network resources, or writing outside the workspace/project tree.
- Any action that may interrupt the user's visible Editor session.

When confirmation is needed, describe the exact Unity action and the state it may change.

## Snippet Style

Keep snippets short and deterministic. Prefer one clear task per call.

Place any required ordinary, alias, `static`, or `global` using directives at the beginning of the snippet. `using var` declarations and `using (...)` statements are method-body statements and can be used normally.

Use `DotCraft.Editor` API helpers for high-boilerplate inspection work. `unity_execute_csharp` snippets already import this namespace, so use `Dcu.Type`, `Dcu.Components`, `Dcu.Get`, and `Dcu.Call` directly when they replace substantial reflection or type-search code.

Validate inputs and return early with a readable message when required objects, assets, scenes, or Unity internal APIs are missing.

For mutations, use `Undo.RecordObject` or `Undo.RegisterCreatedObjectUndo` where practical, mark dirty objects deliberately, and return what changed. Avoid silent changes.

For inspection, return a compact string or small object with only the fields needed for the next decision.
