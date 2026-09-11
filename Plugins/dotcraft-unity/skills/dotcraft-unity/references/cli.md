# CLI connection

Use this reference when MCP is not configured or its tools are absent from the current session. The same `dotcraft-unity.exe` runs a stdio MCP server with `mcp` or performs one operation with `call` / `exec`. Both entry points select a Gateway or Attach backend.

## Install and locate

The executable is distributed for Windows x64. Attach requires a Windows x64 Mono Editor. The public UPM package requires Unity 2022.3 or later. The Release installer downloads the latest version, verifies the artifact manifest, SHA-256, and executable metadata, and adds `~/.craft/bin` to the user PATH without administrator rights:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
dotcraft-unity version --json
```

Install when installation is requested or authorized by the task. To run a downloaded script explicitly, use `pwsh -File .\install.ps1`; it also accepts `-Version X.Y.Z` and `-InstallDir <directory>`. Existing apps may need a new terminal to see PATH changes. `Get-Command dotcraft-unity` shows which executable will run.

## Choose a backend

Pass `--backend auto`, `--backend gateway`, or `--backend attach` to MCP or CLI commands.
`auto` is the default. It selects Gateway when the project's discovery or cached manifest file
exists, and Attach otherwise. Stale or incompatible Gateway state reports an error; it does not
silently switch to injection. Once a session dispatches through a backend, that selection is fixed.
Never change backends to replay an uncertain operation.

For Gateway, enable **Unity Tool Gateway** in **Project Settings → DotCraft** and **Enable C#
Automation** for C# calls. Unity's MCP setup requires a protocol-compatible CLI at
`~/.craft/bin/dotcraft-unity.exe` and writes `--backend gateway` explicitly. UPM C# automation uses
that same Windows x64 executable for external compilation. Gateway also exposes enabled custom
project tools.

Attach executes C# without installing the UPM package or enabling its Gateway. Pass `--pid` when
multiple Editors match the project. Attach exposes only `unity_execute_csharp`; use Gateway for
custom project tools, in-editor chat, and UPM durable-operation helpers. Calls do not install
packages or download another executable.

## Project and readiness

From the Unity project root:

```powershell
$projectRoot = (Get-Location).Path
dotcraft-unity status --backend auto --project-root $projectRoot --json
dotcraft-unity status --backend attach --pid 12345 --project-root $projectRoot --json
dotcraft-unity exec --code 'return Application.unityVersion;' --project-root $projectRoot --json
```

Pass the intended project explicitly when the agent is working outside that Unity project. Otherwise, CLI commands walk upward from the current directory to the nearest directory containing both `Assets` and `ProjectSettings`. MCP always requires `--project-root`.

`status` and `tools list` / `tools describe` never inject. Gateway status validates discovery and
checks TCP reachability with a two-second limit. Attach status lists matching project/PID candidates.
Neither establishes Editor readiness. An `exec` version probe performs a real call and can attach
when Attach is selected. Do not print discovery files: they contain private authentication tokens.

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

Resolve bundled script paths against the installed skill directory and pass their absolute paths directly. Do not copy them into the project. Relative `--path` values resolve against the selected project root. Gateway reads the script inside Unity; Attach reads it in the host before dispatch. In contrast, `--args-file` and `--arguments-file` are read by the CLI relative to the shell's current directory.

Use `--args <JSON object>` or `--args-file <file>` to populate the snippet's `Args` JObject. Use a UTF-8 file for JSON containing quotes, backslashes, or multiline content to avoid PowerShell native-argument quoting differences. `--args-file -` reads JSON from stdin, but cannot be combined with `--stdin` code. For Unicode pipelines in Windows PowerShell, set `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)`; JSON files avoid this pipeline encoding dependency.

`--mode` defaults to `editor`; `playmode` requires the selected Editor already to be in Play Mode
and does not change it. Both backends provide `Args`, `ctx`, and `cancellationToken`. Use
`await ctx.WaitFrame()` or a bounded `WaitUntil` for work across editor updates; MCP/CLI wait for
completion without detached jobs. Gateway imports `DotCraft.Editor`; Attach imports `DotCraft.Unity`.
Use the shared `Dcu` name without hard-coding either namespace in portable scripts.

## Discover and call tools

```powershell
dotcraft-unity tools list --backend auto --project-root $projectRoot --json
dotcraft-unity tools describe my_tool --backend gateway --project-root $projectRoot --json
dotcraft-unity call my_tool --backend gateway --arguments-file .\request.json --project-root $projectRoot --json
```

`tools` returns `source: "cache"` for a valid Gateway manifest, `"default"` for its built-in fallback,
or `"attach"` for Attach's C# declaration. Listing is not proof of current availability. Gateway's
registry decides which tools are enabled at execution time. `call` does not require a cached
manifest entry, but Attach rejects custom names without dispatching them.

`call` accepts `--arguments <JSON object>` or `--arguments-file <file|->`, with `{}` as the default. Only one input may consume stdin.

## Results, failures, and reloads

Use `--json` for agent workflows. Stdout contains one JSON object; diagnostics use stderr. `call` and `exec` use the common result envelope: `success`, `name`, `result`, `text`, `errorCode`, `errorMessage`, and `durationMs`. They do not flatten or parse the script's return value. Inspect domain-specific fields inside `result` as well as gateway success.

Exit codes are `0` for success, `1` for tool/connection failure, `2` for invalid input, and `130` for Ctrl+C. In PowerShell, capture `$LASTEXITCODE` immediately after the native command; a nonzero native exit does not necessarily throw an exception.

Gateway calls have a 65-second HTTP client deadline. Attach waits for its execution to complete
using bounded transport waits; the Gateway deadline is not an overall Attach execution limit.
Cancellation is cooperative, and stopping a caller's wait does not prove that Unity stopped.
`UnityUnavailable` can report unusable Gateway discovery or an unavailable/ambiguous Attach target.
Gateway uses `UnityDisconnected` / `UnityTimeout` for transport failure / deadline expiry. Attach
can report `UnityExecutionLost` or `UnityOutcomeUnknown` when a dispatched execution's result cannot
be recovered. Never automatically replay a dispatched request.

For compilation or Domain Reload, follow [compilation-and-reload.md](compilation-and-reload.md).
Gateway uses the UPM durable operation script and external waiter. Attach uses native lifecycle
recovery to establish a fresh bridge; its old execution is still lost and must not be replayed.
After recovery, make a new read-only probe and inspect the Console. A connection alone does not
prove that compilation succeeded.
