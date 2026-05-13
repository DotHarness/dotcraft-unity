<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

Use DotCraft inside the Unity Editor.

</div>

## About

dotcraft-unity is the Unity editor client for [DotCraft](https://github.com/DotHarness/dotcraft). It connects Unity projects to DotCraft via the Agent Client Protocol (ACP) and provides an in-editor chat window.Besides DotCraft, it also supports any Agent that implements the ACP protocol, such as Claude Code, Cursor, and Codex.

- Editor native: open DotCraft from **Tools → DotCraft Assistant** without leaving Unity.
- Project aware: the Unity project root becomes the DotCraft workspace by default.
- ACP based: DotCraft runs as the agent process while Unity acts as the editor client.
- Unity context: built-in read-only tools expose scene, selection, console, and project information.

## Get Started

1. Install and configure [DotCraft](https://github.com/DotHarness/dotcraft).
2. In Unity, install `System.Text.Json 9.0.10` via [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
3. Open **Window → Package Manager** and add this Git URL:

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

4. Open **Tools → DotCraft Assistant**.
5. Click **Connect**, then start a conversation from the Unity Editor.

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
| **Enable Builtin Unity Tools** | `true` | Declare the built-in read-only Unity runtime tools and enable their `_unity/*` handlers. |

For API keys, prefer environment variables in Project Settings instead of committing secrets to project files.

## Built-in Tools

dotcraft-unity declares four read-only Unity runtime tools to DotCraft during ACP initialization:

| Tool | Description |
|------|-------------|
| `unity_scene_query` | Query scene hierarchy with optional component details. |
| `unity_get_selection` | Read the current Unity Editor selection. |
| `unity_get_console_logs` | Retrieve recent Unity Console log entries. |
| `unity_get_project_info` | Read Unity version, project name, and package information. |

These tools help DotCraft understand the current scene and project state. The model-visible tool descriptors live in this Unity client; the `_unity/*` ACP methods are private callbacks used to execute them. For full Unity editing automation, combine DotCraft with a dedicated Unity tool package such as [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) or [unity-mcp](https://github.com/CoplayDev/unity-mcp).

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
