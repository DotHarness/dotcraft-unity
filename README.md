<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

Agent-native Unity Editor integration. Build, debug, and iterate game worlds with DotCraft.

</div>

## About

dotcraft-unity is the agent-native Unity Editor integration. It brings DotCraft into Unity through in-editor ACP chat, App Binding for bound DotCraft threads, and a local MCP Tool Gateway for external agents. Besides [DotCraft](https://github.com/DotHarness/dotcraft) , it also supports any agent that implements the ACP protocol, such as Claude Code, Cursor, and Codex.

- Editor native: open DotCraft from **Tools → DotCraft Assistant** without leaving Unity.
- Project aware: the Unity project root becomes the DotCraft workspace by default.
- ACP based: DotCraft runs as the agent process while Unity acts as the editor client.
- App Binding: expose enabled Unity tools to any bound DotCraft thread while the Unity Editor is running.
- Unity context: built-in tools expose scene, selection, console, project information, and C# execution.

## Get Started

1. Install and configure [DotCraft](https://github.com/DotHarness/dotcraft).
2. Open **Window → Package Manager** and add this Git URL:

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

   Unity resolves the official `com.unity.nuget.newtonsoft-json` dependency automatically.

3. Open **Tools → DotCraft Assistant**.
4. Click **Connect**, then start a conversation from the Unity Editor.

Minimum Unity version: **2022.3**, recommended version: **Unity 6**.

## Configuration

Open **Edit → Project Settings → DotCraft** to configure the client.

| Setting | Default | Description |
|---------|---------|-------------|
| **Agent Connection** | `DotCraft` | `DotCraft` uses Hub-aware startup; `Custom ACP` keeps raw command/arguments for other ACP agents. |
| **DotCraft Command** | `dotcraft` | DotCraft executable name or full path. Used to start Hub and the ACP bridge. |
| **DotCraft AppServer** | `Local via Hub` | `Local via Hub` asks Hub for the workspace AppServer, then starts `dotcraft -acp --remote ...`; `Remote AppServer` uses a manual WebSocket URL. |
| **Command / Arguments** | `dotcraft` / `-acp` | Shown only for `Custom ACP`; starts the configured ACP process directly. |
| **Workspace Path** | empty | Working directory. Defaults to the Unity project root. |
| **Environment Variables** | empty | Key-value pairs passed to the DotCraft process. |
| **Auto Reconnect** | `true` | Reconnect after Unity Domain Reload. |
| **Verbose Logging** | `false` | Print DotCraft stderr to the Unity Console. |
| **Show Thinking Content** | `false` | Show agent reasoning text in expandable chat rows. When disabled, only lightweight thinking status is shown. |
| **Enable Builtin Unity Tools** | `true` | Declare built-in Unity runtime tools, including read tools and `unity_execute_csharp`, when using the `DotCraft` connection profile. |
| **Plugin Tools** | disabled | Attribute-discovered plugin runtime tools. Each tool must be enabled explicitly in **Unity Tools → Plugin Tools (DotCraft only)**. |
| **Enable Local Server** | `true` | Start the localhost App Binding and Tool Gateway server on port `39777` while Unity Editor is open. |

For API keys, prefer environment variables in Project Settings instead of committing secrets to project files.

## Built-in Tools

dotcraft-unity declares built-in Unity runtime tools to DotCraft during ACP initialization:

| Tool | Description |
|------|-------------|
| `unity_scene_query` | Query scene hierarchy with optional component details. |
| `unity_get_selection` | Read the current Unity Editor selection. |
| `unity_get_console_logs` | Retrieve recent Unity Console log entries. |
| `unity_get_project_info` | Read Unity version, project name, and package information. |
| `unity_execute_csharp` | Compile and execute a C# method-body snippet in the Unity Editor process. |

The read tools help DotCraft understand the current scene and project state. `unity_execute_csharp` lets bound agents inspect or mutate Unity Editor state by compiling and running C# on the Unity main thread. The model-visible tool descriptors live in this Unity client; the `_unity/*` ACP methods are private callbacks used to execute stable built-in tools, while App Binding exposes all enabled tools under the `unity` namespace.

## Tool Gateway

dotcraft-unity also exposes a local Unity Agent OS Tool Gateway for external agents. While Unity Editor is running, MCP clients such as Codex and Claude Code can connect to:

```text
http://127.0.0.1:39777/dotcraft/mcp
```

The gateway exposes the currently enabled runtime tool surface through MCP `tools/list` and `tools/call`, including built-in Unity tools, `unity_execute_csharp`, and enabled plugin tools. Plain HTTP adapters are available at `GET /dotcraft/gateway/tools?format=canonical|openai-responses|openai-chat|claude` and `POST /dotcraft/gateway/call`.

Codex config example:

```toml
[mcp_servers.dotcraft_unity]
url = "http://127.0.0.1:39777/dotcraft/mcp"
enabled = true
tool_timeout_sec = 60
default_tools_approval_mode = "approve"
```

See [Documentation~/tool-gateway.md](./Documentation~/tool-gateway.md) for the gateway contract.

## App Binding

When the DotCraft `dotcraft-unity` plugin is installed, DotCraft Desktop can connect to a running Unity Editor through App Binding. The Unity package listens on `http://127.0.0.1:39777/dotcraft/`, accepts DotCraft connect/bind handoffs, and attaches the currently enabled runtime tools to the selected DotCraft thread.

This path is independent of the ACP chat window: agents in Desktop, TUI, automations, or other AppServer clients can use the bound Unity tools as long as Unity Editor stays open. If Unity closes or scripts reload, the binding becomes offline until you bind again.

## Plugin Runtime Tools

Other Unity Editor plugins can expose DotCraft-only runtime tools by marking static methods with `AgentToolAttribute`. New plugin tools are discovered in **Edit → Project Settings → DotCraft → Unity Tools**, default to disabled, and are injected only for the `DotCraft` connection profile.

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
| Tools are unavailable | Runtime tool descriptors were not declared or accepted | Enable **Builtin Unity Tools** and use a DotCraft version that supports runtime dynamic tools over ACP. |

## Contributing

Contributions are welcome in [DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity). For the agent harness itself, use [DotHarness/dotcraft](https://github.com/DotHarness/dotcraft).

## Reference

[DotCraft](https://github.com/DotHarness/dotcraft)

[Agent Client Protocol](https://agentclientprotocol.com/)

## License

Apache License 2.0
