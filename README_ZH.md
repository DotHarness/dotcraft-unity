<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[English](./README.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

让 coding agent 使用 Unity Editor。

既可以在 Unity 内直接对话，也可以把 Unity 工具通过 MCP 暴露给 Claude Code、Codex、Cursor 等外部 agent。

</div>

## 你可以用它做什么

| 工作流 | 适用场景 | 入口 |
|--------|----------|------|
| Unity 内 Agent 对话 | 想直接在 Unity 中和 DotCraft 或其他 ACP agent 对话 | **Tools → DotCraft Assistant** |
| MCP Tool Gateway | 想让 Claude Code、Codex、Cursor 等外部 MCP client 调用 Unity 工具 | **Tools → DotCraft → MCP Gateway Setup** |
| C# 自动化 | 想让 agent 写 C# 批量读写 Unity | `unity_execute_csharp` |
| 项目自定义工具 | 想暴露项目专属 Unity 操作 | `[AgentTool]` |
| DotCraft 高级 App Binding | 使用 DotCraft Desktop、TUI、automations 或 AppServer 工作流 | Project Settings → DotCraft |

Unity 内聊天和 MCP Tool Gateway 是两条独立路径。

- Unity 内聊天会启动一个 ACP agent 进程，并在 Unity 面板中和它对话。
- MCP Tool Gateway 会启动一个本地 Unity 工具端点，供外部 MCP client 连接。
- 通过 Unity 内聊天连接的 Custom ACP agent 不会自动获得 DotCraft runtime dynamic tools。
- MCP client 可以在 Unity Editor 运行时访问已启用的 Tool Gateway 工具面。

## 快速开始

### Option A：在 Unity 内聊天

1. 从 Package Manager 安装 dotcraft-unity。
2. 打开 **Tools → DotCraft Assistant**。
3. 在 **Project Settings → DotCraft** 中选择 **DotCraft** 或 **Custom ACP Agent**。
4. 点击 **Connect**。

### Option B：通过 MCP 使用 Claude Code / Codex / Cursor

1. 打开 Unity 项目。
2. 在 **Project Settings → DotCraft** 中启用 **Local Tool Gateway**。
3. 运行 **Tools → DotCraft → MCP Gateway Setup**。
4. 选择客户端：Claude Code、Codex 或 Cursor。
5. 从项目根目录启动你的 coding agent。

### Option C：添加项目自定义工具

1. 创建一个带 `[AgentTool]` 的静态 Editor 方法。
2. 等待 Unity 编译。
3. 在 **Project Settings → DotCraft → Unity Tools** 中启用这个工具。
4. 从 DotCraft 或任何已连接 gateway 的 MCP client 使用它。

### 通过 Git 安装

打开 **Window → Package Manager**，添加这个 Git URL：

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

Unity 会自动解析官方 `com.unity.nuget.newtonsoft-json` 依赖。

最低 Unity 版本：**2022.3**，推荐版本 **Unity 6**。

## 配置

打开 **Edit → Project Settings → DotCraft** 配置客户端。

| 设置 | 默认值 | 描述 |
|------|--------|------|
| **Agent** | `DotCraft` | `DotCraft` 用于 Unity 内聊天的 Hub 感知启动；`Custom ACP` 保留原始命令/参数用于其他 ACP agent。 |
| **DotCraft Command** | `dotcraft` | DotCraft 可执行文件名或完整路径，用于启动 Hub 和 ACP bridge。 |
| **DotCraft AppServer** | `Local via Hub` | `Local via Hub` 通过 Hub 获取工作区 AppServer，再启动 `dotcraft -acp --remote ...`；`Remote AppServer` 使用手动填写的 WebSocket URL。 |
| **Command / Arguments** | `dotcraft` / `-acp` | 仅在 `Custom ACP` 下显示，直接启动配置的 ACP 进程。 |
| **Workspace Path** | 空 | 工作目录。默认使用 Unity 项目根目录。 |
| **Environment Variables** | 空 | 传给 DotCraft 进程的键值对。 |
| **Auto Reconnect** | `true` | Unity Domain Reload 后自动重连。 |
| **Verbose Logging** | `false` | 将 DotCraft stderr 输出到 Unity Console。 |
| **Show Thinking Content** | `false` | 在可展开的聊天行中显示 agent 思考内容。关闭时仅显示轻量的思考状态。 |
| **Enable C# Automation** | `true` | 向 DotCraft 和 MCP Tool Gateway 暴露 `unity_execute_csharp`。 |
| **Custom Project Tools** | 关闭 | 通过 `[AgentTool]` 发现的项目自定义工具。每个工具都需要在 **Unity Tools → Custom Project Tools** 中显式开启。 |
| **Enable Local Tool Gateway** | `true` | Unity Editor 打开时，在 `39777` 端口启动 localhost App Binding 与 MCP Tool Gateway 服务。 |

配置 API key 时，建议使用 Project Settings 里的环境变量，不要把密钥提交到项目文件。

## MCP Tool Gateway

dotcraft-unity 会暴露一个本地 MCP Tool Gateway，供外部 coding agent 使用。Unity Editor 运行时，MCP client 可以连接：

```text
http://127.0.0.1:39777/dotcraft/mcp
```

Setup 窗口只写项目级配置；会显示各客户端当前的配置状态；修改已有文件前会创建 `.bak` 备份；uninstall 只删除 `dotcraft-unity` server block。

首版支持：

- Claude Code：`.mcp.json`
- Codex：`.codex/config.toml`
- Cursor：`.cursor/mcp.json`

Claude Code 项目配置：

```json
{
  "mcpServers": {
    "dotcraft-unity": {
      "type": "http",
      "url": "http://127.0.0.1:39777/dotcraft/mcp"
    }
  }
}
```

Codex 项目配置：

```toml
[mcp_servers.dotcraft_unity]
url = "http://127.0.0.1:39777/dotcraft/mcp"
enabled = true
tool_timeout_sec = 60
default_tools_approval_mode = "prompt"
```

Cursor 项目配置：

```json
{
  "mcpServers": {
    "dotcraft-unity": {
      "url": "http://127.0.0.1:39777/dotcraft/mcp"
    }
  }
}
```

完整 gateway 契约与 setup 说明见 [Documentation~/tool-gateway.md](./Documentation~/tool-gateway.md)。

## Unity 工具面

dotcraft-unity 会在 ACP 初始化时向 DotCraft 声明一个内置 Unity 运行时工具：

| 工具 | 描述 |
|------|------|
| `unity_execute_csharp` | 在 Unity Editor 进程中编译并执行一段 C# 方法体代码。 |

可以通过 `unity_execute_csharp` 编写 C# snippet 来读取或修改场景状态、选中对象、Console 输出、项目元数据和资源。重复使用的工作流建议用 `[AgentTool]` 封装成项目自定义工具。`unity_execute_csharp` 是运行在 Unity Editor 内的可信本地 C# 执行能力；它是强大的自动化工具，不是远程安全沙箱。

## App Binding

安装 DotCraft 的 `dotcraft-unity` 插件后，DotCraft Desktop 可以通过 App Binding 连接正在运行的 Unity Editor。Unity package 会监听 `http://127.0.0.1:39777/dotcraft/`，接收 DotCraft connect/bind handoff，并把当前已启用的运行时工具 attach 到选中的 DotCraft thread。

这条路径独立于 Unity 内的 ACP 聊天窗口：Desktop、TUI、automations 或其他 AppServer 客户端中的 agent，都可以在绑定后调用 Unity 工具。Unity 关闭或脚本重载后，绑定会变为 offline，需要重新绑定。

## 项目自定义工具

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

## 故障排除

| 症状 | 可能原因 | 解决方法 |
|------|----------|----------|
| `Failed to start DotCraft process` | `dotcraft` 不在 `PATH` 中 | 安装 DotCraft 并加入 `PATH`，或在 **Command** 中设置完整路径。 |
| 卡在 `Connecting...` | DotCraft 启动失败 | 启用 **Verbose Logging** 并查看 Unity Console。 |
| 脚本编译后断开连接 | 自动重连已关闭 | 在 Project Settings 中启用 **Auto Reconnect**。 |
| MCP client 无法连接 | Local Tool Gateway 已停止或 Unity 已关闭 | 打开 Unity，启用 **Local Tool Gateway**，然后重新运行 **MCP Gateway Setup** 或复制 endpoint。 |
| 工具不可用 | 运行时工具描述符没有声明或没有被接受 | 启用 **C# Automation**，并启用所需的 Custom Project Tools。 |

## 贡献代码

欢迎在 [DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) 贡献代码。Agent Harness 本体请使用 [DotHarness/dotcraft](https://github.com/DotHarness/dotcraft)。

## 引用

[DotCraft](https://github.com/DotHarness/dotcraft)

[Agent Client Protocol](https://agentclientprotocol.com/)

## License

Apache License 2.0
