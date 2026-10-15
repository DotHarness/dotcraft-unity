# Using the Unity package

The Unity package adds chat inside the Editor, an MCP Gateway for coding agents, and custom project tools. Install it from **Window → Package Manager** with this Git URL:

```text
https://github.com/DotHarness/dotcraft-unity.git?path=/Packages/com.dotcraft.unity
```

## Chat inside Unity

![AI Assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. Open **Tools → DotCraft → AI Assistant**.
2. Select **DotCraft** or **Custom ACP Agent** in **Project Settings → DotCraft**. A custom agent must support the [Agent Client Protocol](https://agentclientprotocol.com/).
3. Click **Connect**.

When **DotCraft** is selected, enabled C# automation and custom project tools are available directly in the conversation without MCP setup.

## Operate Unity through MCP

1. Enable **Unity Tool Gateway** in **Project Settings → DotCraft**.
2. Run **Tools → DotCraft → MCP Gateway Setup** and choose Claude Code, Codex, or Cursor.
3. Start your coding agent from the project root.

See [Unity tool gateway](./tool-gateway.md) for how the Gateway connects to the Editor.

## Add project-specific tools

1. Create a static Editor method marked with `[AgentTool]`.
2. Let Unity compile.
3. Enable the tool in **Project Settings → DotCraft → Unity Tools**.
4. Use it from DotCraft, an MCP client, or `dotcraft-unity call`.

See [Custom project tools](./dynamic-tools.md) for the registration contract and supported parameter types.
