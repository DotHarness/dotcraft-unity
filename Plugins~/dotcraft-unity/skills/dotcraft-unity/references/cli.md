# CLI connection

Use this reference when MCP is not configured or its tools are absent from the current session. The same `dotcraft-unity.exe` runs a stdio MCP server with `mcp` or makes a single direct HTTP call with `call` / `exec`.

## Install and locate

Windows x64 is supported. The Release installer downloads the latest version, verifies the artifact manifest, SHA-256, and executable metadata, and adds `~/.craft/bin` to the user PATH without administrator rights:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
dotcraft-unity version --json
```

Install when installation is requested or authorized by the task. To run a downloaded script explicitly, use `pwsh -File .\install.ps1`; it also accepts `-Version X.Y.Z` and `-InstallDir <directory>`. Existing apps may need a new terminal to see PATH changes. `Get-Command dotcraft-unity` shows which executable will run.

Enable **Unity Tool Gateway** in **Project Settings → DotCraft** and **Enable C# Automation** for C# calls. CLI and Unity package versions must match. A version mismatch reports both versions; update the Unity package and CLI together. The CLI never installs packages or downloads another executable during a tool call.

## Project and readiness

From the Unity project root:

```powershell
$projectRoot = (Get-Location).Path
dotcraft-unity status --project-root $projectRoot --json
dotcraft-unity exec --code 'return Application.unityVersion;' --project-root $projectRoot --json
```

Pass the intended project explicitly when the agent is working outside that Unity project. Otherwise, CLI commands walk upward from the current directory to the nearest directory containing both `Assets` and `ProjectSettings`. MCP always requires `--project-root`.

`status` validates discovery and checks TCP reachability with a two-second limit. It does not authenticate a tool call or establish that the Editor is ready. The read-only version snippet above verifies an actual tool call. Do not print the discovery file: it contains the private authentication token.

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

Resolve bundled script paths against the installed skill directory and pass their absolute paths directly. Do not copy them into the project. Relative `--path` values are resolved by Unity against the project root. In contrast, `--args-file` and `--arguments-file` are read by the CLI relative to the shell's current directory.

Use `--args <JSON object>` or `--args-file <file>` to populate the snippet's `Args` JObject. Use a UTF-8 file for JSON containing quotes, backslashes, or multiline content to avoid PowerShell native-argument quoting differences. `--args-file -` reads JSON from stdin, but cannot be combined with `--stdin` code. For Unicode pipelines in Windows PowerShell, set `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)`; JSON files avoid this pipeline encoding dependency.

`--mode` defaults to `editor`; `playmode` requests the existing tool execution mode and does not authorize changing Play Mode state.

## Discover and call custom tools

```powershell
dotcraft-unity tools list --project-root $projectRoot --json
dotcraft-unity tools describe my_tool --project-root $projectRoot --json
dotcraft-unity call my_tool --arguments-file .\request.json --project-root $projectRoot --json
```

`tools` returns `source: "cache"` for a valid on-disk manifest or `"default"` for the built-in fallback. Neither is proof of current tool availability. Unity's registry decides which tools are enabled at execution time. `call` does not require a matching cached manifest entry.

`call` accepts `--arguments <JSON object>` or `--arguments-file <file|->`, with `{}` as the default. Only one input may consume stdin.

## Results, failures, and reloads

Use `--json` for agent workflows. Stdout contains one JSON object; diagnostics use stderr. `call` and `exec` preserve the gateway result: `success`, `name`, `result`, `text`, `errorCode`, `errorMessage`, and `durationMs`. They do not flatten or parse the script's return value. Inspect domain-specific fields inside `result` as well as gateway success.

Exit codes are `0` for success, `1` for tool/connection failure, `2` for invalid input, and `130` for Ctrl+C. In PowerShell, capture `$LASTEXITCODE` immediately after the native command; a nonzero native exit does not necessarily throw an exception.

Calls have a 65-second client deadline. `UnityUnavailable` means discovery is absent, stale, or incompatible; `UnityDisconnected` means transport or response failure; `UnityTimeout` means the client stopped waiting. Cancelling or timing out does not undo Unity work. Never automatically replay a dispatched request.

For compilation or Domain Reload, follow [compilation-and-reload.md](compilation-and-reload.md): generate the operation ID externally, start the existing `unity-operation.cs` with `exec --path`, and run `Wait-DotCraftUnityOperation.ps1` outside Unity. The initiating CLI call may disconnect. Wait for the durable operation, then make a new read-only probe and inspect the Console. Do not use a longer HTTP timeout as a substitute for that workflow.
