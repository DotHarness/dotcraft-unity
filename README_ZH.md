<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[English](./README.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

让 coding agents 使用 Unity Editor。

既可以在 Unity 内直接对话，也可以把 Unity 工具通过 MCP 暴露给 DotCraft、Claude Code、Codex、Cursor 等 agents。

</div>

## 你可以用它做什么

| 工作流 | 适用场景 | 入口 |
|--------|----------|------|
| Attach | 在 Windows x64 Mono Editor 中执行 C#，无需安装 Unity 包 | `dotcraft-unity exec --backend attach` |
| DotCraft 原生插件 | 直接从 DotCraft 连接 Unity | 从 DotCraft 官方插件市场安装 **Unity** |
| Unity 内 Agent 对话 | 想直接在 Unity 中和 DotCraft 或其他 ACP agent 对话 | **Tools → DotCraft → AI Assistant** |
| MCP Gateway | 想让 Claude Code、Codex、Cursor 等外部 MCP client 调用 Unity 工具 | **Tools → DotCraft → MCP Gateway Setup** |
| CLI | 想通过终端或 agent 直接调用 Unity，无需配置 MCP | `dotcraft-unity exec` / `dotcraft-unity call` |
| C# 自动化 | 想让 agent 批量操作 Unity | `unity_execute_csharp` |
| 自定义工具 | 想暴露项目专属 Unity 工具 | `[AgentTool]` |

## 快速开始

### 安装 Unity Package

打开 **Window → Package Manager**，添加这个 Git URL：

   ```text
   https://github.com/DotHarness/dotcraft-unity.git?path=/Packages/com.dotcraft.unity
   ```

最低 Unity 版本：**2022.3**。

### Option A：在 Unity 内聊天

![assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. 打开 **Tools → DotCraft → AI Assistant**。
2. 在 **Project Settings → DotCraft** 中选择 **DotCraft** 或 **Custom ACP Agent**。
3. 点击 **Connect**。

### Option B：通过 MCP 操作 Unity

![app-binding](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/app-binding.gif)

1. 在 **Project Settings → DotCraft** 中启用 **Unity Tool Gateway**。
2. 运行 **Tools → DotCraft → MCP Gateway Setup**，选择 Claude Code、Codex 或 Cursor。
3. 从项目根目录启动你的 coding agent。

### Option C：无需 MCP，直接通过 CLI 操作 Unity

Windows x64 用户在 Unity 项目根目录运行以下命令，即可安装 CLI 到 `~/.craft/bin` 并加入用户 PATH：

```powershell
irm https://github.com/DotHarness/dotcraft-unity/releases/latest/download/install.ps1 | iex
$projectRoot = (Get-Location).Path
dotcraft-unity version --json
dotcraft-unity exec --code 'return Application.unityVersion;' --project-root $projectRoot --json
```

CLI 优先使用已有且协议兼容的 Gateway，否则 Attach 到唯一匹配的 Editor。需要时可显式指定 `--backend gateway` 或 `--backend attach`，多个 Editor 用 `--pid` 选择。Gateway 模式需要启用 **Unity Tool Gateway**、**C# Automation**，并在 `~/.craft/bin/dotcraft-unity.exe` 安装协议兼容的 CLI。Attach 不需要 UPM 包或 DotCraft 本体。详见 [CLI 使用说明](./Plugins/dotcraft-unity/skills/dotcraft-unity/references/cli.md)。

### Option D：添加项目自定义工具

1. 创建一个带 `[AgentTool]` 的静态 Editor 方法。
2. 等待 Unity 编译。
3. 在 **Project Settings → DotCraft → Unity Tools** 中启用这个工具。
4. 从 DotCraft、MCP client 或 `dotcraft-unity call` 使用它。

## MCP Gateway

![mcp](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

运行 `dotcraft-unity mcp --project-root "<project>"` 为 coding agent 提供 stdio MCP 连接。Unity 重载时 MCP 进程继续运行，Editor 恢复后建立新连接。被重载中断的 C# 执行不会续跑或重放。

详情见 [Documentations/tool-gateway.md](./Documentations/tool-gateway.md)。

## 内置工具

`unity_execute_csharp` 把 C# snippet 交给全局 CLI 使用 Roslyn 编译，再由 Unity 在主线程加载并执行编译后的程序集。Snippet 由可选的开头 `using` 指令和方法体语句组成，可用来读取或修改场景状态、选中对象、Console 输出、项目元数据和资源。

![C# 自动化在 Unity 内部的工作原理](./Documentations/csharp-automation-how-it-works.svg)

## 自定义工具

给静态 Editor 方法添加 `[AgentTool]` 即可。新工具会显示在 **Project Settings → DotCraft → Unity Tools**，默认关闭，需手动启用。

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

详情见 [Documentations/dynamic-tools.md](./Documentations/dynamic-tools.md)。

## Agent 集成

### Agent 插件

Agent skill 支持 MCP 和 CLI。独立交付的 DotCraft 原生插件通过 Attach 直接提供 `unity.list/connect/status/execute/wait/disconnect`。

在 DotCraft 中：

1. 打开 **Plugins**，选择 DotCraft 官方市场（`DotHarness/dotcraft-plugins`）。
2. 安装并启用 **Unity**（`DotCraft.Unity`），然后让 agent 连接你的 Editor。

在 Codex 中，将 `DotHarness/dotcraft-unity` 添加为 plugin marketplace，然后从该 marketplace 安装 **DotCraft Unity**。

### ACP Extension

使用 DotCraft 作为 ACP Server 时无需 MCP 服务：内置工具和自定义工具通过 ACP 扩展传给会话，非 Unity 会话不会带上 Unity 工具的上下文。

## License

Apache License 2.0
