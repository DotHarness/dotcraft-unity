<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

Use coding agents with Unity Editor.

Chat with an agent inside Unity, or expose Unity tools to Claude Code, Codex, Cursor, and other MCP clients.

</div>

## What you can do

| Workflow | Use this when | Entry point |
|----------|---------------|-------------|
| In-Editor Agent Chat | You want to chat with DotCraft or another ACP agent inside Unity | **Tools → DotCraft Assistant** |
| MCP Tool Gateway | You want Claude Code, Codex, Cursor, or another MCP client to call Unity tools | **Tools → DotCraft → MCP Gateway Setup** |
| C# Automation | You want an agent to batch-edit or inspect Unity by writing C# | `unity_execute_csharp` |
| Custom Project Tools | You want to expose project-specific Unity operations | `[AgentTool]` |
| Advanced DotCraft App Binding | You use DotCraft Desktop, TUI, automations, or AppServer workflows | Project Settings → DotCraft |

In-editor ACP chat and MCP Tool Gateway are separate paths.

- In-editor chat starts an ACP agent process and talks to it inside Unity.
- MCP Tool Gateway starts a local Unity tool endpoint that external MCP clients can connect to.
- Custom ACP agents connected through the in-editor chat do not automatically receive DotCraft runtime dynamic tools.
- MCP clients can access the enabled Tool Gateway surface while Unity Editor is running.

## Quick Start

### Option A: Chat inside Unity

1. Install dotcraft-unity from Package Manager.
2. Open **Tools → DotCraft Assistant**.
3. Select **DotCraft** or **Custom ACP Agent** in **Project Settings → DotCraft**.
4. Click **Connect**.

### Option B: Use Claude Code / Codex / Cursor through MCP

1. Open the Unity project.
2. Enable **Local Tool Gateway** in **Project Settings → DotCraft**.
3. Run **Tools → DotCraft → MCP Gateway Setup**.
4. Choose your client: Claude Code, Codex, or Cursor.
5. Start your coding agent from the project root.

### Option C: Add project-specific tools

1. Create a static Editor method marked with `[AgentTool]`.
2. Let Unity compile.
3. Enable the tool in **Project Settings → DotCraft → Unity Tools**.
4. Use it from DotCraft or any MCP client connected to the gateway.

### Install from Git

Open **Window → Package Manager** and add this Git URL:

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

Unity resolves the official `com.unity.nuget.newtonsoft-json` dependency automatically.

Minimum Unity version: **2022.3**, recommended version: **Unity 6**.

## Configuration

Open **Edit → Project Settings → DotCraft** to configure the client.

| Setting | Default | Description |
|---------|---------|-------------|
| **Agent** | `DotCraft` | `DotCraft` uses Hub-aware startup for in-editor chat; `Custom ACP` keeps raw command/arguments for other ACP agents. |
| **DotCraft Command** | `dotcraft` | DotCraft executable name or full path. Used to start Hub and the ACP bridge. |
| **DotCraft AppServer** | `Local via Hub` | `Local via Hub` asks Hub for the workspace AppServer, then starts `dotcraft -acp --remote ...`; `Remote AppServer` uses a manual WebSocket URL. |
| **Command / Arguments** | `dotcraft` / `-acp` | Shown only for `Custom ACP`; starts the configured ACP process directly. |
| **Workspace Path** | empty | Working directory. Defaults to the Unity project root. |
| **Environment Variables** | empty | Key-value pairs passed to the DotCraft process. |
| **Auto Reconnect** | `true` | Reconnect after Unity Domain Reload. |
| **Verbose Logging** | `false` | Print DotCraft stderr to the Unity Console. |
| **Show Thinking Content** | `false` | Show agent reasoning text in expandable chat rows. When disabled, only lightweight thinking status is shown. |
| **Enable C# Automation** | `true` | Expose `unity_execute_csharp` to DotCraft and the MCP Tool Gateway. |
| **Custom Project Tools** | disabled | Attribute-discovered `[AgentTool]` tools. Each custom tool must be enabled explicitly in **Unity Tools → Custom Project Tools**. |
| **Enable Local Tool Gateway** | `true` | Start the localhost App Binding and MCP Tool Gateway server on port `39777` while Unity Editor is open. |

For API keys, prefer environment variables in Project Settings instead of committing secrets to project files.

## MCP Tool Gateway

dotcraft-unity exposes a local MCP Tool Gateway for external coding agents. While Unity Editor is running, MCP clients can connect to:

```text
http://127.0.0.1:39777/dotcraft/mcp
```

The setup window writes project-level config only, shows each client's current setup state, creates `.bak` backups for existing files, and can remove only the `dotcraft-unity` server block later.

Supported first-run targets:

- Claude Code: `.mcp.json`
- Codex: `.codex/config.toml`
- Cursor: `.cursor/mcp.json`

Claude Code project config:

```json
{
  "mcpServers": {
    "dotcraft-unity": {
      "type": "http",
      "url": "http://127.0.0.1:39777/dotcraft/mcp"
    }
  }
}
```

Codex project config:

```toml
[mcp_servers.dotcraft_unity]
url = "http://127.0.0.1:39777/dotcraft/mcp"
enabled = true
tool_timeout_sec = 60
default_tools_approval_mode = "prompt"
```

Cursor project config:

```json
{
  "mcpServers": {
    "dotcraft-unity": {
      "url": "http://127.0.0.1:39777/dotcraft/mcp"
    }
  }
}
```

See [Documentation~/tool-gateway.md](./Documentation~/tool-gateway.md) for the gateway contract and setup details.

## Unity tool surface

dotcraft-unity declares one built-in Unity runtime tool to DotCraft during ACP initialization:

| Tool | Description |
|------|-------------|
| `unity_execute_csharp` | Compile and execute a C# method-body snippet in the Unity Editor process. |

Use `unity_execute_csharp` to inspect or modify scene state, selection, console output, project metadata, or assets with a C# snippet. For repeated workflows, expose a custom project tool with `[AgentTool]`. `unity_execute_csharp` is trusted local C# execution inside Unity Editor; it is powerful automation, not a remote security sandbox.

## App Binding

When the DotCraft `dotcraft-unity` plugin is installed, DotCraft Desktop can connect to a running Unity Editor through App Binding. The Unity package listens on `http://127.0.0.1:39777/dotcraft/`, accepts DotCraft connect/bind handoffs, and attaches the currently enabled runtime tools to the selected DotCraft thread.

This path is independent of the ACP chat window: agents in Desktop, TUI, automations, or other AppServer clients can use the bound Unity tools as long as Unity Editor stays open. If Unity closes or scripts reload, the binding becomes offline until you bind again.

## Custom Project Tools

Unity Editor code can expose custom project tools by marking static methods with `AgentToolAttribute`. New custom tools are discovered in **Edit → Project Settings → DotCraft → Unity Tools**, default to disabled, and can be used from DotCraft or MCP clients after they are enabled.

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

Method parameters are converted to a JSON Schema with Newtonsoft.Json naming rules. See [Documentation~/dynamic-tools.md](./Documentation~/dynamic-tools.md) for the full registration contract.

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| `Failed to start DotCraft process` | `dotcraft` is not on `PATH` | Install DotCraft and add it to `PATH`, or set a full path in **Command**. |
| Stuck at `Connecting...` | DotCraft failed during startup | Enable **Verbose Logging** and check the Unity Console. |
| Disconnects after script compilation | Auto reconnect is disabled | Enable **Auto Reconnect** in Project Settings. |
| MCP client cannot connect | Local Tool Gateway is stopped or Unity is closed | Open Unity, enable **Local Tool Gateway**, then run **MCP Gateway Setup** or copy the endpoint again. |
| Tools are unavailable | Runtime tool descriptors were not declared or accepted | Enable **C# Automation** and enable any required Custom Project Tools. |

## Contributing

Contributions are welcome in [DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity). For the agent harness itself, use [DotHarness/dotcraft](https://github.com/DotHarness/dotcraft).

## Reference

[DotCraft](https://github.com/DotHarness/dotcraft)

[Agent Client Protocol](https://agentclientprotocol.com/)

## License

Apache License 2.0
