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
| MCP Tool Gateway | 想让 Claude Code、Codex、Cursor 等外部 MCP client 调用 Unity 工具 | **Tools → DotCraft → MCP Gateway Setup** |
| C# 自动化 | 想让 agent 批量操作 Unity | `unity_execute_csharp` |
| 自定义工具 | 想暴露项目专属 Unity 工具 | `[AgentTool]` |

Unity 内聊天和 MCP Tool Gateway 是两条独立路径。

- Unity 内聊天会启动一个 ACP agent 进程，并在 Unity 面板中和它对话。
- MCP Tool Gateway 会启动一个本地 Unity 工具端点，供外部 Agent 连接。

## 快速开始

### 安装

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
2. 在 **Project Settings → DotCraft** 中启用 **Local Tool Gateway**。
3. 运行 **Tools → DotCraft → MCP Gateway Setup**。
4. 选择 Agent 工具：DotCraft、Claude Code、Codex 或 Cursor。
5. 从项目根目录启动你的 coding agent。

### Option C：添加项目自定义工具

1. 创建一个带 `[AgentTool]` 的静态 Editor 方法。
2. 等待 Unity 编译。
3. 在 **Project Settings → DotCraft → Unity Tools** 中启用这个工具。
4. 从 DotCraft 或任何已连接 gateway 的 MCP client 使用它。

## MCP Tool Gateway

![mcp](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/mcp.png)

dotcraft-unity 会暴露一个本地 MCP Tool Gateway，供外部 coding agent 使用。Unity Editor 运行时，MCP client 可以连接：

```text
http://127.0.0.1:39777/dotcraft/mcp
```

目前支持 DotCraft、Claude Code、Codex、Cursor.

> MCP 端点使用 `2025-11-25` 版本的有状态 Streamable HTTP。`initialize` 会返回 `MCP-Session-Id`，之后的 POST 和 DELETE 请求都需要携带该 header。过期或失效 session 会返回 HTTP `404`，client 应重新 initialize。`GET /dotcraft/mcp` 返回 HTTP `405`，因为当前 gateway 不提供 SSE stream。

## 内置工具

dotcraft-unity 基于 Roslyn 提供了一个内置 Unity 运行时工具：

| 工具 | 描述 |
|------|------|
| `unity_execute_csharp` | 在 Unity Editor 进程中编译并执行一段 C# 方法体代码。 |

可以通过 `unity_execute_csharp` 编写 C# snippet 来读取或修改场景状态、选中对象、Console 输出、项目元数据和资源。

## 自定义工具

Unity Editor 代码可以通过给静态方法添加 `AgentToolAttribute`，暴露项目自定义工具。新工具会显示在 **Edit → Project Settings → DotCraft → Unity Tools**，默认关闭；启用后可由 DotCraft 或已连接 gateway 的 MCP client 使用。

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

方法参数会使用 Newtonsoft.Json 命名规则转换成 JSON Schema。完整注册契约见 [Documentation~/dynamic-tools.md](./Documentation~/dynamic-tools.md)。

## DotCraft 专用功能

dotcraft-unity 支持 dotcraft 的更多特性以提高开发效率的同时降低 agent 使用成本。

### App Binding

安装 dotcraft 的 `dotcraft-unity` 插件后，DotCraft Desktop 可以通过 App Binding 连接正在运行的 Unity Editor 来使用内置工具和自定义工具，不影响其他的非 Unity 会话。

### ACP Extension

使用 dotcraft 作为 ACP Server 时，无需使用 MCP 服务，内置工具和自定义工具会走 ACP 拓展传递给会话，以减少非 Unity 会话的上下文开销。

## 贡献代码

欢迎在 [DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) 贡献代码。Agent Harness 本体请使用 [DotHarness/dotcraft](https://github.com/DotHarness/dotcraft)。

## 引用

[DotCraft](https://github.com/DotHarness/dotcraft)

[Agent Client Protocol](https://agentclientprotocol.com/)

## License

Apache License 2.0
