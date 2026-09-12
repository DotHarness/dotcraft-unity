<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[中文](./README_ZH.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](./LICENSE)

Unified AI agent integration for Unity Editor—in-editor chat, CLI/MCP automation, and package-free Attach on Windows.

*Supports Unity 2021.3 through Unity 6.6 (Mono only, CoreCLR WIP)*

</div>

## What you can do

| Workflow | Use this when |
|----------|---------------|
| AI Assistant | Chat with an ACP-compatible Agent inside Unity |
| MCP Gateway | Let external MCP clients such as Claude Code, Codex, or Cursor call Unity tools |
| CLI | Automate Unity from a terminal or connect to the Editor without installing the Unity package |
| Custom tools | Expose project-specific tools to coding agents through MCP Gateway |

## Install

### Step 1: Install an Agent plugin or a Skill

Choose the installation method for your Agent.

#### Codex

1. Open **Plugins**, then select **Add a marketplace** from the **Add** menu.
2. Enter `DotHarness/dotcraft-unity` under **Source**, then add the marketplace.
3. Find and install **DotCraft Unity**.

![DotCraft Unity plugin in Codex](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/codex-dotcraft-unity-plugin.png)

#### DotCraft

Open **Plugins**, then find and install **Unity**. Once installed, select **Try in chat** to start using it.

![Native Unity plugin in DotCraft](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/dotcraft-unity-plugin.png)

#### Install the Skill manually

Copy [`Plugins/dotcraft-unity/skills/dotcraft-unity`](./Plugins/dotcraft-unity/skills/dotcraft-unity) into your coding agent's Skills directory.

The Codex plugin and manually installed Skill use the CLI to operate Unity. Install the CLI with:

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
```

### Step 2 (optional): Install the Unity package

In-Editor chat, MCP Gateway, and custom project tools require the Unity package.

Open **Window → Package Manager** and add this Git URL:

```text
https://github.com/DotHarness/dotcraft-unity.git?path=/Packages/com.dotcraft.unity
```

## Quick start

### Automate the Editor with C#

After completing the first step, tell Codex or DotCraft what you want to do in the current Unity Editor. For example:

> Inspect the current scene, list every root GameObject, and find any objects with missing scripts.

![How C# automation works with Unity Editor](./Documentations/csharp-automation-how-it-works.svg)

### Operate Unity through MCP

![app-binding](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/app-binding.gif)

![MCP Gateway setup](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

1. Enable **Unity Tool Gateway** in **Project Settings → DotCraft**.
2. Run **Tools → DotCraft → MCP Gateway Setup** and choose Claude Code, Codex, or Cursor.
3. Start your coding agent from the project root.

### Chat inside Unity

![AI Assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. Open **Tools → DotCraft → AI Assistant**.
2. Select **DotCraft** or **Custom ACP Agent** in **Project Settings → DotCraft**.
3. Select **Connect**.

When **DotCraft** is selected, enabled C# automation and custom project tools are available directly in the conversation without MCP setup.

### Add project-specific tools

1. Create a static Editor method marked with `[AgentTool]`.
2. Let Unity compile.
3. Enable the tool in **Project Settings → DotCraft → Unity Tools**.
4. Use it from DotCraft, an MCP client, or `dotcraft-unity call`.

See [Custom project tools](./Documentations/dynamic-tools.md) for the registration contract and supported parameter types.

## License

[Apache License 2.0](./LICENSE)
