<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](./LICENSE)

Unified AI agent integration for Unity Editor—in-editor chat, CLI/MCP automation, and package-free Attach on Windows.

</div>

## What you can do

| Workflow | Use this when | Entry point |
|----------|---------------|-------------|
| In-Unity Agent Chat | You want to chat with DotCraft or another ACP agent inside Unity | **Tools → DotCraft → AI Assistant** |
| DotCraft native plugin | You want to operate Unity directly from DotCraft | Install **Unity** from the official DotCraft plugin marketplace |
| MCP Gateway | You want external MCP clients such as Claude Code, Codex, or Cursor to call Unity tools | **Tools → DotCraft → MCP Gateway Setup** |
| CLI and Attach | You want to automate Unity from a terminal or connect without installing the Unity package | `dotcraft-unity exec` / `dotcraft-unity call` |
| Custom Tools | You want to expose project-specific Unity tools | `[AgentTool]` |

## Quick start

### Install the Unity package

Install the Unity package for in-Editor chat, MCP Gateway, or custom project tools. If you only need package-free Attach on Windows x64 Mono, skip to **Option C**.

Open **Window → Package Manager** and add this Git URL:

```text
https://github.com/DotHarness/dotcraft-unity.git?path=/Packages/com.dotcraft.unity
```

Minimum Unity version: **2021.3**.

### Option A: Chat inside Unity

![assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. Open **Tools → DotCraft → AI Assistant**.
2. Select **DotCraft** or **Custom ACP Agent** in **Project Settings → DotCraft**.
3. Click **Connect**.

### Option B: Use MCP to operate Unity

![app-binding](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/app-binding.gif)

![MCP Gateway setup](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

1. Enable **Unity Tool Gateway** in **Project Settings → DotCraft**.
2. Run **Tools → DotCraft → MCP Gateway Setup** and choose Claude Code, Codex, or Cursor.
3. Start your coding agent from the project root.

See [Unity tool gateway](./Documentations/tool-gateway.md) for its lifecycle and transport contract.

### Option C: Use the CLI without MCP

On Windows x64, run the following from the Unity project root:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
dotcraft-unity exec --code 'return Application.unityVersion;' --json
```

See the [CLI reference](./Plugins/dotcraft-unity/skills/dotcraft-unity/references/cli.md) for saved scripts, custom project tools, and connection options.

### Option D: Add project-specific tools

1. Create a static Editor method marked with `[AgentTool]`.
2. Let Unity compile.
3. Enable the tool in **Project Settings → DotCraft → Unity Tools**.
4. Use it from DotCraft, an MCP client, or `dotcraft-unity call`.

See [Custom project tools](./Documentations/dynamic-tools.md) for the registration contract and supported parameter types.

## C# automation

`unity_execute_csharp` runs an inline C# snippet or saved script in a live Unity Editor. Use it to inspect or modify scenes, selected objects, Console output, project metadata, and assets.

![How C# automation works inside Unity](./Documentations/csharp-automation-how-it-works.svg)

## Agent integrations

### Plugins

DotCraft and external coding agents use separate plugins:

- **Unity** (`DotCraft.Unity`) is the native DotCraft plugin. It provides `unity.*` tools without adding the Unity package to a project.
- **DotCraft Unity** (`dotcraft-unity`) is the Agent skill plugin for MCP and CLI workflows. Add `DotHarness/dotcraft-unity` as a Codex plugin marketplace, then install **DotCraft Unity**.

In DotCraft, open **Plugins**, then install and enable **Unity**.

### In-Editor tools

When **DotCraft** is selected as the Agent in Unity, enabled C# Automation and custom project tools are available in the in-Editor chat without MCP setup.

## License

[Apache License 2.0](./LICENSE)
