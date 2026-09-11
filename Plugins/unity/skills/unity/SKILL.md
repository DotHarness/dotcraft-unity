---
name: unity
description: Inspect, automate, and debug a running Unity Editor. Use for Unity scenes, assets, project state, Console logs, and Editor C# execution.
---

# Unity

Use `unity.list`, `unity.connect`, `unity.status`, `unity.execute`, `unity.wait`, and `unity.disconnect` to work with a running Unity Editor. These DotCraft-native tools use Attach on a Windows x64 Mono Editor without requiring the UPM package. They execute Editor C# on Unity's main thread and do not replace normal source-file editing. Other agents can use the standalone MCP/CLI with `--backend auto|gateway|attach`; Gateway additionally exposes enabled custom project tools.

Load only the reference needed for the task:

- For loaded-type lookup and reflection helpers, read [references/api.md](references/api.md).
- For Console logs, read [references/console-reading.md](references/console-reading.md) and use the bundled script.
- For Editor, GameView, or SceneView images, read [references/editor-screenshot.md](references/editor-screenshot.md).
- For compilation or Domain Reload, read [references/compilation-and-reload.md](references/compilation-and-reload.md).
- For compilation errors, timeouts, or unexpected results, read [references/snippet-craft.md](references/snippet-craft.md).

## Connect

`unity.list` and `unity.status` are read-only and never inject or reconnect. Call `unity.list` when the target process is not already known. If several Editors are running, identify the intended one from its process information instead of guessing. Pass its `pid` to `unity.connect`; the result includes the Unity version, project path, Play Mode state, compilation state, and connection generation. That Editor remains selected for the current task.

Call `unity.status`, `unity.execute`, and `unity.disconnect` without a PID. After managed Domain Reload, the native observer coordinates bridge recovery. Call `unity.connect` without a PID to establish a fresh handshake for the selected Editor; old executions and compiled contexts remain invalid. Do not replay the call that caused a reload. A new task must select its own Editor with `unity.connect(pid)`.

Use `unity.disconnect` to release this task's connection. It does not disconnect other clients or unload assemblies already loaded in the Editor.

## Execute C#

Pass exactly one of `code` or `path` to `unity.execute`. A path is resolved by the DotCraft host and can point to a bundled skill script or a workspace script.

The input consists of optional leading `using` directives followed by method-body statements. Do not wrap it in a namespace, class, or method. Return a compact string, anonymous object, or other JSON-serializable value. Common Unity value types such as vectors, colors, bounds, matrices, rays, and planes can be returned directly. A `UnityEngine.Object` is represented by its type, name, and instance ID; return an explicit anonymous object when more fields are needed.

Use `args` for variable input. It is available to the snippet as a Newtonsoft `JObject` named `Args`:

```csharp
var name = (string)Args["name"] ?? "Player";
var target = GameObject.Find(name);
return new { found = target != null, name };
```

Use inline `code` for a short one-off operation. Store recurring project-specific snippets under `.craft/scripts/` and use `path`. Bundled scripts live under this skill's `scripts/` directory and can be passed by absolute path without copying them into the Unity project.

Attach snippets import `DotCraft.Unity`; UPM/Gateway snippets import `DotCraft.Editor`. Both expose the same `Dcu` helper surface, so portable scripts should use the unqualified helper name. The payload provides `Dcu.Type`, `Dcu.Components`, `Dcu.Get`, `Dcu.Set`, `Dcu.Call`, and `Dcu.Members` for loaded types and reflection-heavy inspection. Use ordinary Unity APIs directly when they are shorter.

### Await Editor updates

The snippet can use `await` directly. Use the provided `ctx` helpers when Unity work must continue on the Editor main thread:

```csharp
var before = System.Threading.Thread.CurrentThread.ManagedThreadId;
await ctx.WaitFrames(2);
return new {
    before,
    after = System.Threading.Thread.CurrentThread.ManagedThreadId,
    scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name
};
```

Available helpers are `WaitFrame()`, `WaitFrames(int)`, `WaitSeconds(float)`, `WaitUntil(Func<bool>)`, `ThrowIfCancellationRequested()`, and `CancellationToken`. Prefer these helpers before accessing Unity APIs after an await. A general `Task` await may resume away from Unity's main thread when its synchronization context is bypassed.

### Run in the background

Pass `runInBackground: true` to let a long execution outlive the tool call. If it is still active after `yieldTimeMs`, `unity.execute` returns an `executionId`. Poll it with `unity.wait`; use `terminate: true` only when the execution should be cancelled.

Cancellation is cooperative. Waiting helpers observe it automatically, and longer synchronous loops should call `ctx.ThrowIfCancellationRequested()` or check `cancellationToken`. A running result with `cancellationRequested: true` means the code has not stopped yet. Do not replay an execution whose result is `unknown`.

## Work with the Editor

Inspect before mutating when the current state determines the safe change. For requested scene, asset, prefab, or settings edits, make the smallest bounded change, use Unity's `Undo` APIs where practical, mark modified objects deliberately, and report what changed. Save scenes or assets only when the request requires persistence.

Keep work in the background unless the user asks for visible interaction. Avoid opening or focusing windows, changing selection, showing dialogs, executing menu items, entering Play Mode, or triggering broad imports as incidental steps.

Do not block the Unity main thread with `Thread.Sleep` or long synchronous waits. Use the context helpers and background execution for work that spans Editor updates.

## Failures and reloads

A timeout can mean the code started but the result was not received. Inspect Editor state before deciding what happened and never replay the operation automatically.

Compilation errors report the failing C# diagnostics. Resolve ambiguous Unity and System type names with fully qualified names. Internal Unity APIs vary by version; inspect their members with reflection instead of repeatedly guessing signatures.

If an operation triggers Domain Reload, the old execution becomes `lost`. Native recovery establishes a new bridge; it never replays or resumes that execution. Follow [references/compilation-and-reload.md](references/compilation-and-reload.md), reconnect with `unity.connect` without a PID, and verify the intended result with a fresh read-only call and Console inspection. If recovery cannot establish a fresh handshake, report the failure instead of repeatedly attaching.
