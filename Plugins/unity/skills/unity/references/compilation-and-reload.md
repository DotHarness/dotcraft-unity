# Compilation and Domain Reload

These DotCraft-native tools use Attach. Managed Domain Reload invalidates the current execution
context, even when the requested Unity operation succeeds. The native lifecycle observer
coordinates a fresh bridge; it does not resume or replay the old execution.

## Request the operation once

Finish authorized source-file edits first. If Unity does not compile automatically, request it once:

```csharp
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
return "compilation requested";
```

For a reload without compilation, request it once:

```csharp
UnityEditor.EditorUtility.RequestScriptReload();
return "reload requested";
```

The initiating response may be lost. Do not replay either request or an execution reported as
`lost` / `unknown`.

## Verify recovery

Call `unity.connect` without a PID to reconnect the selected Editor. A successful fresh
main-thread handshake establishes the new bridge generation. If recovery is unavailable or the
handshake does not arrive, report that failure; repeated injection is not a recovery strategy.

Use `unity.status` and a new bounded read-only execution to verify that the Editor is no longer
compiling or importing. Read the Console for compiler errors before continuing. Compilation may
fail without a Domain Reload, so an unchanged generation or successful connection does not prove
that compilation succeeded. Never infer readiness from a fixed delay or window focus.

## Work that spans reloads

An attached snippet and its background execution cannot survive the domain containing them.
Persist checkpoints outside that domain or put resumable behavior in project source.
`DcuLongRunningOperation` and the `unity-operation.cs` / PowerShell waiter workflow are provided by
the UPM Gateway integration, not by Attach. If using that integration through the standalone
MCP/CLI, select `--backend gateway` and its Gateway skill's durable-operation workflow.
