<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[English](./README.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](./LICENSE)

面向 Unity Editor 的统一 Agent 集成：支持 Unity 内对话、CLI/MCP 自动化，不安装 UPM 包也能使用。

*支持 Unity 2021.3 ~ Unity 6.6 (Mono only, CoreCLR WIP)*

</div>

## 你可以用它做什么

| 工作流 | 适用场景 |
|--------|----------|
| AI Assistant | 在 Unity 中和支持 ACP 协议的 Agent 对话 |
| MCP Gateway | 想让 Claude Code、Codex、Cursor 等外部 MCP client 调用 Unity 工具 |
| CLI | 想从终端自动化 Unity，或在不安装 Unity Package 的情况下连接 Editor |
| 自定义工具 | 利用 MCP Gateway 暴露项目专属工具给 Coding Agent |

## 安装

### 第一步：安装 Agent 插件或 Skill

选择适合你的 Agent 的安装方式。

#### Codex

1. 打开 **Plugins**，在 **Add** 菜单中选择 **Add a marketplace**。
2. 在 **Source** 中输入 `DotHarness/dotcraft-unity`，然后添加市场。
3. 找到并安装 **DotCraft Unity**。

![Codex 中的 DotCraft Unity 插件](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/codex-dotcraft-unity-plugin.png)

#### DotCraft

打开 **Plugins**，找到并安装 **Unity**。安装完成后，可以直接点击 **Try in chat** 开始使用。

![DotCraft 中的 Unity 原生插件](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/dotcraft-unity-plugin.png)

#### 手动安装 Skill

将 [`Plugins/dotcraft-unity/skills/dotcraft-unity`](./Plugins/dotcraft-unity/skills/dotcraft-unity) 复制到 coding agent 的 Skills 目录。

Codex 插件和手动安装的 Skill 通过 CLI 操作 Unity。运行下面的命令安装 CLI：

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
```

### 第二步（可选）：安装 Unity Package

Unity 内对话、MCP Gateway 和项目自定义工具需要 Unity Package。

打开 **Window → Package Manager**，添加 Git URL：

```text
https://github.com/DotHarness/dotcraft-unity.git?path=/Packages/com.dotcraft.unity
```

## 快速开始

### 使用 C# 自动化操作 Editor

完成第一步后，直接告诉 Codex 或 DotCraft 要在当前 Unity Editor 中完成什么。例如：

> 检查当前场景，列出所有根 GameObject，并找出挂有缺失脚本的对象。

![C# 自动化如何与 Unity Editor 协作](./Documentations/csharp-automation-how-it-works.svg)

### 通过 MCP 操作 Unity

![app-binding](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/app-binding.gif)

![MCP Gateway 设置](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

1. 在 **Project Settings → DotCraft** 中启用 **Unity Tool Gateway**。
2. 运行 **Tools → DotCraft → MCP Gateway Setup**，选择 Claude Code、Codex 或 Cursor。
3. 从项目根目录启动 coding agent。

### 在 Unity 内聊天

![AI Assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. 打开 **Tools → DotCraft → AI Assistant**。
2. 在 **Project Settings → DotCraft** 中选择 **DotCraft** 或 **Custom ACP Agent**。
3. 点击 **Connect**。

选择 **DotCraft** 后，启用的 C# 自动化和项目自定义工具可以直接在对话中使用，无需配置 MCP。

### 添加项目自定义工具

1. 创建一个带 `[AgentTool]` 的静态 Editor 方法。
2. 等待 Unity 编译。
3. 在 **Project Settings → DotCraft → Unity Tools** 中启用这个工具。
4. 从 DotCraft、MCP client 或 `dotcraft-unity call` 使用它。

注册约定和支持的参数类型参阅[自定义项目工具](./Documentations/dynamic-tools.md)。

## License

[Apache License 2.0](./LICENSE)
