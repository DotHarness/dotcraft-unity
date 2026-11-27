# Compilation and Domain Reload

Never replay the initiating call after a disconnect.

## Recovery

Attach's native lifecycle observer coordinates a fresh bridge after managed Domain Reload.
The old execution/context is invalid and may report lost or unknown. Recovery restores the
connection, not the execution or its result. If recovery is unavailable or no fresh handshake
arrives, report that failure instead of repeatedly injecting or resending the mutation.

After authorized source changes, call `unity_recompile` once, or `dotcraft-unity recompile` from
the CLI (see [cli.md](cli.md)). It imports changed assets, requests one script compilation and
waits for that compilation, identified by its sequence number, rather than one that was already
running:

- Success with `domainReloaded: true` means the new scripts are loaded and the bridge recovered;
  `domainReloaded: false` means the Editor settled without a reload.
- `CompilationFailed` returns the compiler errors. Fix them and recompile again.
- `RecompileStillRunning` means the wait ended first. Do not request it again. A compilation
  publishes its new sequence when it starts, so a newer sequence alone does not mean it finished.
  Read Attach `status` until `compile` has a sequence newer than the reported baseline with
  `finished: true`, then check `succeeded`.
- `UnityOutcomeUnknown` means the request may or may not have reached Unity. Read `status` instead
  of requesting it again.

Do not request compilation from a snippet and then poll `EditorApplication.isCompiling`: it is
still false before the requested compilation starts, so the first probe can pass before anything
compiled. If the installed CLI has no `recompile` command, update it first.

For reload without compilation, call `UnityEditor.EditorUtility.RequestScriptReload()` once from a
snippet. Afterward, issue a new bounded read-only probe for the same project and PID. Verify that the Editor is not compiling or importing, then inspect the Console.

Work that must resume after a reload needs an external workflow with explicit checkpoints.
