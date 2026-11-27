# CLI connection

Use this reference when MCP is not configured or its tools are absent from the current session. The same `dotcraft-unity.exe` runs a stdio MCP server with `mcp` or performs one operation with `call` / `exec`. Both connect to Unity through Attach.

## Install and locate

The executable is distributed for Windows x64. Attach requires a Windows x64 Mono Editor. The Release installer downloads the latest version, verifies the artifact manifest, SHA-256, and executable metadata, and adds `~/.craft/bin` to the user PATH without administrator rights:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
dotcraft-unity version --json
```

Install when installation is requested or authorized by the task. To run a downloaded script explicitly, use `pwsh -File .\install.ps1`; it also accepts `-Version X.Y.Z` and `-InstallDir <directory>`. Existing apps may need a new terminal to see PATH changes. `Get-Command dotcraft-unity` shows which executable will run.

## Attach

Attach executes C# in a running Windows x64 Mono Editor without installing anything into the
project. Pass `--pid` when multiple Editors match the project. The tools are
`unity_execute_csharp` and `unity_recompile`. Calls do not install packages or download another
executable.

## Project and readiness

From the Unity project root:

```powershell
$projectRoot = (Get-Location).Path
dotcraft-unity status --project-root $projectRoot --json
dotcraft-unity status --pid 12345 --project-root $projectRoot --json
dotcraft-unity exec --code 'return Application.unityVersion;' --project-root $projectRoot --json
```

Pass the intended project explicitly when the agent is working outside that Unity project. Otherwise, CLI commands walk upward from the current directory to the nearest directory containing both `Assets` and `ProjectSettings`. MCP always requires `--project-root`.

`status` and `tools list` / `tools describe` never inject. `status` lists matching project/PID candidates
with `editorState`, the record each bridge last published: connection, play mode, compiling and
reloading flags, and the latest compilation's sequence and outcome. It is absent until the Editor
has been attached, and a `connection` other than `Ready` means it stopped at the last Domain
Reload. Status does not establish Editor readiness. An `exec` version probe performs a real call and can attach.

## Execute C#

`exec` maps to `unity_execute_csharp`. Choose exactly one of `--code`, `--path`, or `--stdin`. Set `$skillRoot` to the installed dotcraft-unity skill directory:

```powershell
dotcraft-unity exec --path (Join-Path $skillRoot 'scripts/console-read.cs') --project-root $projectRoot --json
dotcraft-unity exec --path '.craft/scripts/inspect.cs' --args-file .\args.json --project-root $projectRoot --json

@'
var label = "A quoted value";
return new { label, unityVersion = Application.unityVersion };
'@ | dotcraft-unity exec --stdin --project-root $projectRoot --json
```

Resolve bundled script paths against the installed skill directory and pass their absolute paths directly. Do not copy them into the project. Relative `--path` values resolve against the selected project root, and the CLI reads the script before dispatch. In contrast, `--args-file` and `--arguments-file` are read by the CLI relative to the shell's current directory.

Use `--args <JSON object>` or `--args-file <file>` to populate the snippet's `Args` JObject. Use a UTF-8 file for JSON containing quotes, backslashes, or multiline content to avoid PowerShell native-argument quoting differences. `--args-file -` reads JSON from stdin, but cannot be combined with `--stdin` code. For Unicode pipelines in Windows PowerShell, set `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)`; JSON files avoid this pipeline encoding dependency.

`--mode` defaults to `editor`; `playmode` requires the selected Editor already to be in Play Mode
and does not change it. Snippets get `Args`, `ctx`, and `cancellationToken`. Use
`await ctx.WaitFrame()` or a bounded `WaitUntil` for work across editor updates; MCP/CLI wait for
completion without detached jobs. Snippets import `DotCraft.Unity`, which provides `Dcu`.

## Discover and call tools

```powershell
dotcraft-unity tools list --project-root $projectRoot --json
dotcraft-unity tools describe unity_recompile --project-root $projectRoot --json
dotcraft-unity call unity_execute_csharp --arguments-file .
equest.json --project-root $projectRoot --json
```

`tools` returns the two tool definitions. `call` accepts only those names and rejects others
without dispatching them.

`call` accepts `--arguments <JSON object>` or `--arguments-file <file|->`, with `{}` as the default. Only one input may consume stdin.

## Results, failures, and reloads

Use `--json` for agent workflows. Stdout contains one JSON object, and diagnostics go to stderr. `call` and `exec` use the common result envelope: `success`, `name`, `result`, `text`, `errorCode`, `errorMessage`, and `durationMs`. They do not flatten or parse the script's return value. Inspect domain-specific fields inside `result` as well as `success`.

Exit codes are `0` for success, `1` for tool/connection failure, `2` for invalid input, and `130` for Ctrl+C. In PowerShell, capture `$LASTEXITCODE` immediately after the native command; a nonzero native exit does not necessarily throw an exception.

Attach waits for its execution to complete using bounded transport waits.
Cancellation is cooperative, and stopping a caller's wait does not prove that Unity stopped.
`UnityUnavailable` reports an unavailable or ambiguous Attach target. Attach can report `UnityExecutionLost` or `UnityOutcomeUnknown` when a dispatched execution's result cannot
be recovered. Never automatically replay a dispatched request.

For compilation or Domain Reload, follow [compilation-and-reload.md](compilation-and-reload.md) and
use `recompile`:

```powershell
dotcraft-unity recompile --pid 12345 --project-root $projectRoot --json
```

It sends one request, then waits up to `--wait-ms` (default 60000, at most 120000) for that
compilation and the bridge recovery after its Domain Reload. Success reports whether a reload
happened. `CompilationFailed` carries the compiler errors in `result.errors`.
`RecompileStillRunning` means the wait ended first: do not run it again. Follow
[compilation-and-reload.md](compilation-and-reload.md) to wait for it. After success, make a new
read-only probe and inspect the Console.
