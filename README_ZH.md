<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[English](./README.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

让 coding agents 使用 Unity Editor。

既可以在 Unity 内直接对话，也可以把 Unity 工具通过 MCP 暴露给 DotCraft、Claude Code、Codex、Cursor 等 agents。

</div>

## 你可以用它做什么

| 工作流 | 适用场景 | 入口 |
|--------|----------|------|
| Unity 内 Agent 对话 | 想直接在 Unity 中和 DotCraft 或其他 ACP agent 对话 | **Tools → DotCraft → AI Assistant** |
| MCP Gateway | 想让 Claude Code、Codex、Cursor 等外部 MCP client 调用 Unity 工具 | **Tools → DotCraft → MCP Gateway Setup** |
| C# 自动化 | 想让 agent 批量操作 Unity | `unity_execute_csharp` |
| 自定义工具 | 想暴露项目专属 Unity 工具 | `[AgentTool]` |

## 快速开始

### 安装 Unity Package

打开 **Window → Package Manager**，添加这个 Git URL：

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

最低 Unity 版本：**2021.3**，推荐版本 **Unity 6**。

### Option A：在 Unity 内聊天

![assistant](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/assistant.png)

1. 从 Package Manager 安装 dotcraft-unity。
2. 打开 **Tools → DotCraft → AI Assistant**。
3. 在 **Project Settings → DotCraft** 中选择 **DotCraft** 或 **Custom ACP Agent**。
4. 点击 **Connect**。

### Option B：通过 MCP 操作 Unity

![app-binding](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/app-binding.gif)

1. 打开 Unity 项目。
2. 在 **Project Settings → DotCraft** 中启用 **Unity Tool Gateway**。
3. 运行 **Tools → DotCraft → MCP Gateway Setup**。
4. 选择 Agent 工具：Claude Code、Codex 或 Cursor。
5. 从项目根目录启动你的 coding agent。

### Option C：添加项目自定义工具

1. 创建一个带 `[AgentTool]` 的静态 Editor 方法。
2. 等待 Unity 编译。
3. 在 **Project Settings → DotCraft → Unity Tools** 中启用这个工具。
4. 从 DotCraft 或任何通过 MCP Gateway 连接的 MCP client 使用它。

## MCP Gateway

![mcp](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

dotcraft-unity 为 coding agent 提供了一个生命周期稳定的 MCP Gateway，不受 Domain Reload、Editor 重启影响。

详情见 [Documentation~/tool-gateway.md](./Documentation~/tool-gateway.md)。

## 内置工具

dotcraft-unity 基于 Roslyn 提供了一个内置 Unity 运行时工具：

| 工具 | 描述 |
|------|------|
| `unity_execute_csharp` | 在 Unity Editor 进程中编译并执行一段 C# 方法体代码。 |

可以通过 `unity_execute_csharp` 编写 C# snippet 来读取或修改场景状态、选中对象、Console 输出、项目元数据和资源。

## 自定义工具

Unity Editor 代码可以通过给静态方法添加 `AgentToolAttribute`，暴露项目自定义工具。

新工具会显示在 **Edit → Project Settings → DotCraft → Unity Tools**，默认关闭。

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

详情见 [Documentation~/dynamic-tools.md](./Documentation~/dynamic-tools.md)。

## Agent 集成

dotcraft-unity 为外部 agent 提供共享的自动化 skill，并为 DotCraft 提供直接的 ACP 集成。

### Agent 插件

仓库将同一份 Unity 自动化 skill 同时发布为 DotCraft 插件和 Codex 插件。请另外在 Unity 中配置 MCP Gateway，让安装插件后的 agent 能够调用该 skill 所描述的工具。

在 DotCraft 中：

1. 在 DotCraft 中打开 **Plugins**。
2. 打开 **Create** 旁的菜单，然后选择 **Add marketplace**。
3. 输入 `DotHarness/dotcraft-unity` 作为 marketplace source。
4. 添加 marketplace，然后安装 **DotCraft Unity**。

在 Codex 中，将 `DotHarness/dotcraft-unity` 添加为 plugin marketplace，然后从该 marketplace 安装 **DotCraft Unity**。

### ACP Extension

使用 dotcraft 作为 ACP Server 时，无需使用 MCP 服务，内置工具和自定义工具会走 ACP 拓展传递给会话，以减少非 Unity 会话的上下文开销。

## License

Apache License 2.0
