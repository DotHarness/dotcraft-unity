<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[English](./README.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

面向 agent 的 DotCraft Unity Editor 集成。

用 DotCraft 构建、调试并迭代游戏世界。

</div>

## 简介

dotcraft-unity 是 [DotCraft](https://github.com/DotHarness/dotcraft) 面向 agent 的 Unity Editor 集成。它通过编辑器内 ACP 聊天、面向已绑定 DotCraft thread 的 App Binding，以及供外部 agent 使用的本地 MCP Tool Gateway，把 DotCraft 带入 Unity。
除了 DotCraft，它也支持 Claude Code、Cursor、Codex 等任何已实现 ACP 协议的 agent。

- 编辑器原生：通过 **Tools → DotCraft Assistant** 直接打开 DotCraft。
- 项目感知：默认将 Unity 项目根目录作为 DotCraft 工作区。
- 基于 ACP：DotCraft 作为 agent 进程运行，Unity 作为编辑器客户端。
- App Binding：Unity Editor 运行时，可把已启用的 Unity 工具暴露给任意已绑定的 DotCraft thread。
- Unity 上下文：内置工具可暴露场景、选中对象、控制台、项目信息，并支持 C# 执行。

## 快速开始

1. 安装并配置 [DotCraft](https://github.com/DotHarness/dotcraft)。
2. 打开 **Window → Package Manager**，添加这个 Git URL：

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

   Unity 会自动解析官方 `com.unity.nuget.newtonsoft-json` 依赖。

3. 打开 **Tools → DotCraft Assistant**。
4. 点击 **Connect**，然后在 Unity 编辑器中开始对话。

最低 Unity 版本：**2022.3**，推荐版本 **Unity 6**。

## 配置

打开 **Edit → Project Settings → DotCraft** 配置客户端。

| 设置 | 默认值 | 描述 |
|------|--------|------|
| **Agent Connection** | `DotCraft` | `DotCraft` 使用 Hub 感知启动；`Custom ACP` 保留原始命令/参数用于其他 ACP agent。 |
| **DotCraft Command** | `dotcraft` | DotCraft 可执行文件名或完整路径，用于启动 Hub 和 ACP bridge。 |
| **DotCraft AppServer** | `Local via Hub` | `Local via Hub` 通过 Hub 获取工作区 AppServer，再启动 `dotcraft -acp --remote ...`；`Remote AppServer` 使用手动填写的 WebSocket URL。 |
| **Command / Arguments** | `dotcraft` / `-acp` | 仅在 `Custom ACP` 下显示，直接启动配置的 ACP 进程。 |
| **Workspace Path** | 空 | 工作目录。默认使用 Unity 项目根目录。 |
| **Environment Variables** | 空 | 传给 DotCraft 进程的键值对。 |
| **Auto Reconnect** | `true` | Unity Domain Reload 后自动重连。 |
| **Verbose Logging** | `false` | 将 DotCraft stderr 输出到 Unity Console。 |
| **Show Thinking Content** | `false` | 在可展开的聊天行中显示 agent 思考内容。关闭时仅显示轻量的思考状态。 |
| **Enable Builtin Unity Tools** | `true` | 在 `DotCraft` 连接模式下声明内置 Unity 运行时工具，包括只读工具和 `unity_execute_csharp`。 |
| **Plugin Tools** | 关闭 | 通过 attribute 发现的插件运行时工具。每个工具都需要在 **Unity Tools → Plugin Tools (DotCraft only)** 中显式开启。 |
| **Enable Local Server** | `true` | Unity Editor 打开时，在 `39777` 端口启动 localhost App Binding 与 Tool Gateway 服务。 |

配置 API key 时，建议使用 Project Settings 里的环境变量，不要把密钥提交到项目文件。

## 内置工具

dotcraft-unity 会在 ACP 初始化时向 DotCraft 声明内置 Unity 运行时工具：

| 工具 | 描述 |
|------|------|
| `unity_scene_query` | 查询场景层级结构，可选包含组件详情。 |
| `unity_get_selection` | 读取 Unity 编辑器当前选中对象。 |
| `unity_get_console_logs` | 获取最近的 Unity Console 日志。 |
| `unity_get_project_info` | 读取 Unity 版本、项目名称和包信息。 |
| `unity_execute_csharp` | 在 Unity Editor 进程中编译并执行一段 C# 方法体代码。 |

只读工具帮助 DotCraft 理解当前场景与项目状态。`unity_execute_csharp` 允许已绑定的 agent 在 Unity 主线程编译并运行 C#，从而读取或修改 Editor 状态。模型可见的工具描述符位于这个 Unity 客户端中；`_unity/*` ACP 方法用于稳定内置工具，App Binding 会把所有已启用工具暴露在 `unity` namespace 下。

## Tool Gateway

dotcraft-unity 也会暴露一个本地 Unity Agent OS Tool Gateway，供外部 agent 使用。Unity Editor 运行时，Codex、Claude Code 等 MCP client 可以连接：

```text
http://127.0.0.1:39777/dotcraft/mcp
```

当前 gateway 会通过 MCP `tools/list` 和 `tools/call` 暴露已启用的运行时工具面，包括内置 Unity 工具、`unity_execute_csharp` 和已启用的插件工具。普通 HTTP adapter 位于 `GET /dotcraft/gateway/tools?format=canonical|openai-responses|openai-chat|claude` 和 `POST /dotcraft/gateway/call`。

Codex 配置示例：

```toml
[mcp_servers.dotcraft_unity]
url = "http://127.0.0.1:39777/dotcraft/mcp"
enabled = true
tool_timeout_sec = 60
default_tools_approval_mode = "approve"
```

完整 gateway 契约见 [Documentation~/tool-gateway.md](./Documentation~/tool-gateway.md)。

## App Binding

安装 DotCraft 的 `dotcraft-unity` 插件后，DotCraft Desktop 可以通过 App Binding 连接正在运行的 Unity Editor。Unity package 会监听 `http://127.0.0.1:39777/dotcraft/`，接收 DotCraft connect/bind handoff，并把当前已启用的运行时工具 attach 到选中的 DotCraft thread。

这条路径独立于 Unity 内的 ACP 聊天窗口：Desktop、TUI、automations 或其他 AppServer 客户端中的 agent，都可以在绑定后调用 Unity 工具。Unity 关闭或脚本重载后，绑定会变为 offline，需要重新绑定。

## 插件运行时工具

其他 Unity Editor 插件可以通过给静态方法添加 `AgentToolAttribute`，向 DotCraft 暴露仅 DotCraft 可用的运行时工具。新插件工具会显示在 **Edit → Project Settings → DotCraft → Unity Tools**，默认关闭，并且只会在 `DotCraft` 连接模式下注入。

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

## 故障排除

| 症状 | 可能原因 | 解决方法 |
|------|----------|----------|
| `Failed to start DotCraft process` | `dotcraft` 不在 `PATH` 中 | 安装 DotCraft 并加入 `PATH`，或在 **Command** 中设置完整路径。 |
| 卡在 `Connecting...` | DotCraft 启动失败 | 启用 **Verbose Logging** 并查看 Unity Console。 |
| 脚本编译后断开连接 | 自动重连已关闭 | 在 Project Settings 中启用 **Auto Reconnect**。 |
| 工具不可用 | 运行时工具描述符没有声明或没有被接受 | 启用 **Builtin Unity Tools**，并使用支持 ACP runtime dynamic tools 的 DotCraft 版本。 |

## 贡献代码

欢迎在 [DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) 贡献代码。Agent Harness 本体请使用 [DotHarness/dotcraft](https://github.com/DotHarness/dotcraft)。

## 引用

[DotCraft](https://github.com/DotHarness/dotcraft)

[Agent Client Protocol](https://agentclientprotocol.com/)

## License

Apache License 2.0
