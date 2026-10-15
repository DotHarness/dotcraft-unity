# Compilation and Domain Reload

These DotCraft-native tools use Attach. Managed Domain Reload invalidates the current execution
context, even when the requested Unity operation succeeds. The native lifecycle observer
coordinates a fresh bridge; it does not resume or replay the old execution.

## Compile once

Finish authorized source-file edits first, then call `unity.recompile` once. It imports changed
assets, requests one script compilation, and waits for that compilation rather than one that was
already running:

- `succeeded: false` returns the compiler errors. Fix them and call `unity.recompile` again.
- `succeeded: true` with `domainReloaded: true` means the new scripts are loaded and the bridge
  has recovered.
- `succeeded: true` with `domainReloaded: false` means the Editor settled without a reload.
- `state: "running"` means the wait ended first. Do not call it again; the `## Unity Editor`
  block reports the compilation result and connection when they change.

Do not request compilation through `unity.execute`, and do not replay a `unity.recompile` whose
result is `unknown`.

For a reload without compilation, request it once:

```csharp
UnityEditor.EditorUtility.RequestScriptReload();
return "reload requested";
```

The initiating response may be lost. Do not replay the request or an execution reported as
`lost` / `unknown`.

## Verify recovery

The `## Unity Editor` block shows the connection as reloading scripts, recovering, ready, or
recovery failed. `unity.execute` and `unity.recompile` reconnect the selected Editor themselves
once recovery completes. If recovery fails, report that failure; repeated injection is not a
recovery strategy. Never infer readiness from a fixed delay or window focus.

## Work that spans reloads

An attached snippet and its background execution cannot survive the domain containing them.
Persist checkpoints outside that domain or put resumable behavior in project source.
`DcuLongRunningOperation` and the `unity-operation.cs` / PowerShell waiter workflow are provided by
the UPM Gateway integration, not by Attach. If using that integration through the standalone
MCP/CLI, select `--backend gateway` and its Gateway skill's durable-operation workflow.
