<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

Use coding agents with Unity Editor.

Chat with an agent inside Unity, or expose Unity tools to DotCraft, Claude Code, Codex, Cursor, and other agents.

</div>

## What you can do

| Workflow | Use this when | Entry point |
|----------|---------------|-------------|
| In-Unity Agent Chat | You want to chat with DotCraft or another ACP agent inside Unity | **Tools → DotCraft → AI Assistant** |
| MCP Gateway | You want external MCP clients such as Claude Code, Codex, or Cursor to call Unity tools | **Tools → DotCraft → MCP Gateway Setup** |
| CLI | You want to call Unity from a terminal or agent without MCP configuration | `dotcraft-unity exec` / `dotcraft-unity call` |
| C# Automation | You want an agent to perform batch operations in Unity | `unity_execute_csharp` |
| Custom Tools | You want to expose project-specific Unity tools | `[AgentTool]` |

## Quick Start

### Install the Unity package

Open **Window → Package Manager** and add this Git URL:

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

Minimum Unity version: **2021.3**, recommended version: **Unity 6**.

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

Enable **Unity Tool Gateway** in **Project Settings → DotCraft**. On Windows x64, run the following from the Unity project root to install the latest CLI to `~/.craft/bin` and the user PATH:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
$projectRoot = (Get-Location).Path
dotcraft-unity version --json
dotcraft-unity exec --code 'return Application.unityVersion;' --project-root $projectRoot --json
```

`exec` also needs **C# Automation** enabled. CLI and Unity package versions must match; no administrator rights are required. See the [CLI reference](./Plugins~/dotcraft-unity/skills/dotcraft-unity/references/cli.md) for scripts, custom tools, JSON input, and error handling.

### Option D: Add project-specific tools

1. Create a static Editor method marked with `[AgentTool]`.
2. Let Unity compile.
3. Enable the tool in **Project Settings → DotCraft → Unity Tools**.
4. Use it from DotCraft, an MCP client, or `dotcraft-unity call`.

## MCP Gateway

![mcp](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

dotcraft-unity provides a stable MCP Gateway for coding agents, unaffected by domain reloads or editor restarts.

See [Documentation~/tool-gateway.md](./Documentation~/tool-gateway.md) for more details.

## Built-in tools

`unity_execute_csharp` compiles a C# snippet with Roslyn and runs it in the Unity Editor process. A snippet is optional leading `using` directives followed by method-body statements; use it to read or modify scene state, selected objects, Console output, project metadata, and assets.

![How C# automation works inside Unity](./Documentation~/csharp-automation-how-it-works.svg)

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

See [Documentation~/dynamic-tools.md](./Documentation~/dynamic-tools.md) for more details.

## Agent integrations

### Agent plugins

The same Unity automation skill is published as a DotCraft plugin and a Codex plugin. It uses MCP tools when available and falls back to the CLI. Enable Unity Tool Gateway, then configure MCP or install the CLI.

For DotCraft:

1. Open **Plugins**, then **Add marketplace** from the menu beside **Create**.
2. Enter `DotHarness/dotcraft-unity` and install **DotCraft Unity**.

For Codex, add `DotHarness/dotcraft-unity` as a plugin marketplace, then install **DotCraft Unity** from that marketplace.

### ACP Extension

With DotCraft as the ACP server, no MCP service is needed: built-in and custom tools reach the session through an ACP extension, so non-Unity sessions carry no Unity tool context.

## License

Apache License 2.0
