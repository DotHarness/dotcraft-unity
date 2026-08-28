# Compilation and Domain Reload

Use this workflow whenever Unity must compile scripts or reload managed assemblies. The durable operation file is the source of truth; the MCP call that starts the work is not expected to remain connected.

## Protocol

Operation state is written atomically to:

`UserSettings/DotCraft/operations/<operation-id>.json`

The state survives managed Domain Reload and Unity Tool Gateway restarts. It records the operation status, phase, revision, reload count, Editor process ID, timestamps, and any compiler error. `UserSettings` is local Editor state and must not be committed.

The Unity-side host records these transitions:

- `created` -> `compilation-requested` -> `compiling` -> `compiled-awaiting-domain-reload` -> `before-domain-reload` -> `after-domain-reload`
- compilation errors terminate at `compilation-failed`
- a reload-only operation uses `domain-reload-requested` -> `before-domain-reload` -> `after-domain-reload`

Do not infer readiness from window focus, progress bars, Editor log silence, or a timed delay.

## Start

Run the bundled `scripts/unity-operation.cs` directly by passing its absolute installed path to `unity_execute_csharp(path=...)`.

- Use `action=compile` after changing C# sources. The operation state is persisted before `CompilationPipeline.RequestScriptCompilation` is called; Unity performs the compilation asynchronously. Keep `cleanBuildCache=false` unless a clean rebuild is explicitly required.
- Use `action=reload` only when assemblies must be reloaded without compiling changed code. The operation state is persisted before `EditorUtility.RequestScriptReload` requests the next-frame reload.
- Generate a unique lowercase GUID without separators outside Unity and pass it as `id`. This makes the operation address known even if Domain Reload disconnects the initiating MCP request. The returned JSON repeats that ID when the response arrives in time.

The Gateway may disappear before the MCP response arrives; that is normal and does not lose the operation address.

## Wait Outside Unity

Run the bundled `scripts/Wait-DotCraftUnityOperation.ps1` from the agent process:

`pwsh -File <skill>/scripts/Wait-DotCraftUnityOperation.ps1 -ProjectPath <project> -OperationId <id> -RequireGateway`

The waiter reads only durable files and process/TCP state, so it continues through Domain Reload and Gateway restarts. Its compact JSON result distinguishes:

- `succeeded`: Unity completed the operation and, when requested, the Gateway is reachable.
- `failed`: Unity compilation or the requested operation failed.
- `editor-exited`: the owning Unity process exited before completion.
- `gateway-timeout`: Unity completed the operation but the Gateway did not recover.
- `timeout`: Unity never reached a terminal operation state.

After `succeeded`, reconnect with a new bounded `unity_execute_csharp` call and read the Console using `references/console-reading.md`. Do not reuse an in-flight call from before the reload.

## Longer Workflows

For work spanning multiple reloads, use the same script with `begin`, `report`, `complete`, and `fail`. Each Unity-side phase writes a checkpoint to the same operation file. The host increments `reloadCount` and preserves a manual operation as `running` after each reload; only the owning workflow marks it complete or failed. The external waiter can therefore observe one operation across any number of managed reloads and Gateway restarts.
