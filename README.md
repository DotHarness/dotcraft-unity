<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

Use coding agents with Unity Editor.

Chat with an agent inside Unity, or expose Unity tools to DotCraft, Claude Code, Codex, Cursor, and other agents.

</div>

## What you can do

| Workflow | Use this when | Entry point |
|----------|---------------|-------------|
| Attach | You want C# automation without installing a Unity package on Windows x64 Mono | `dotcraft-unity exec --backend attach` |
| DotCraft native plugin | You want to connect directly from DotCraft | Install **Unity** from the official DotCraft plugin marketplace |
| In-Unity Agent Chat | You want to chat with DotCraft or another ACP agent inside Unity | **Tools → DotCraft → AI Assistant** |
| MCP Gateway | You want external MCP clients such as Claude Code, Codex, or Cursor to call Unity tools | **Tools → DotCraft → MCP Gateway Setup** |
| CLI | You want to call Unity from a terminal or agent without MCP configuration | `dotcraft-unity exec` / `dotcraft-unity call` |
| C# Automation | You want an agent to perform batch operations in Unity | `unity_execute_csharp` |
| Custom Tools | You want to expose project-specific Unity tools | `[AgentTool]` |

## Quick Start

### Install the Unity package

Open **Window → Package Manager** and add this Git URL:

   ```text
   https://github.com/DotHarness/dotcraft-unity.git?path=/Packages/com.dotcraft.unity
   ```

Minimum Unity version: **2022.3**.

### Option A: Chat inside Unity

![assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. Open **Tools → DotCraft → AI Assistant**.
2. Select **DotCraft** or **Custom ACP Agent** in **Project Settings → DotCraft**.
3. Click **Connect**.

### Option B: Use MCP to operate Unity

![app-binding](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/app-binding.gif)

1. Enable **Unity Tool Gateway** in **Project Settings → DotCraft**.
2. Run **Tools → DotCraft → MCP Gateway Setup** and choose Claude Code, Codex, or Cursor.
3. Start your coding agent from the project root.

### Option C: Use the CLI without MCP

On Windows x64, run the following from the Unity project root to install the CLI to `~/.craft/bin` and the user PATH:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
$projectRoot = (Get-Location).Path
dotcraft-unity version --json
dotcraft-unity exec --code 'return Application.unityVersion;' --project-root $projectRoot --json
```

The CLI prefers an existing protocol-compatible Gateway and otherwise attaches to the unique matching Editor. Select `--backend gateway` or `--backend attach` explicitly when needed, and use `--pid` to choose among multiple Editors. Gateway mode requires **Unity Tool Gateway**, **C# Automation**, and a protocol-compatible CLI at `~/.craft/bin/dotcraft-unity.exe`. Attach needs no UPM package or DotCraft installation. See the [CLI reference](./Plugins/dotcraft-unity/skills/dotcraft-unity/references/cli.md).

### Option D: Add project-specific tools

1. Create a static Editor method marked with `[AgentTool]`.
2. Let Unity compile.
3. Enable the tool in **Project Settings → DotCraft → Unity Tools**.
4. Use it from DotCraft, an MCP client, or `dotcraft-unity call`.

## MCP Gateway

![mcp](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

Run `dotcraft-unity mcp --project-root "<project>"` for a coding agent's stdio MCP connection. The MCP process remains available while Unity reloads. A reloaded Editor gets a fresh connection; interrupted C# executions are not resumed or replayed.

See [Documentations/tool-gateway.md](./Documentations/tool-gateway.md) for more details.

## Built-in tools

`unity_execute_csharp` sends a C# snippet to the global CLI for Roslyn compilation, then loads and runs the compiled assembly on Unity's main thread. A snippet is optional leading `using` directives followed by method-body statements; use it to read or modify scene state, selected objects, Console output, project metadata, and assets.

![How C# automation works inside Unity](./Documentations/csharp-automation-how-it-works.svg)

## Custom tools

Mark a static Editor method with `[AgentTool]`. New tools appear in **Project Settings → DotCraft → Unity Tools** and are disabled until you enable them.

```csharp
using System.ComponentModel;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;

public static class ExampleDotCraftTools
{
    [Description("Return a greeting from an example Unity plugin.")]
    [AgentTool(Namespace = "example", Name = "example_greet", Kind = AcpToolKind.Read)]
    public static object Greet([Description("Name to greet.")] string name = "Unity")
    {
        return new { message = $"Hello, {name}." };
    }
}
```

See [Documentations/dynamic-tools.md](./Documentations/dynamic-tools.md) for more details.

## Agent integrations

### Agent plugins

The Agent skill supports MCP and CLI. The separate DotCraft native plugin provides `unity.list/connect/status/execute/wait/disconnect` directly through Attach.

For DotCraft:

1. Open **Plugins** and select the official DotCraft marketplace (`DotHarness/dotcraft-plugins`).
2. Install and enable **Unity** (`DotCraft.Unity`), then ask the agent to connect to your Editor.

For Codex, add `DotHarness/dotcraft-unity` as a plugin marketplace, then install **DotCraft Unity** from that marketplace.

### ACP Extension

With DotCraft as the ACP server, no MCP service is needed: built-in and custom tools reach the session through an ACP extension, so non-Unity sessions carry no Unity tool context.

## License

Apache License 2.0
