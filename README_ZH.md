<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft-unity/banner.png)

[English](./README.md) · [DotCraft](https://github.com/DotHarness/dotcraft) · [ACP](https://agentclientprotocol.com/) · [License](https://github.com/DotHarness/dotcraft-unity)

在 Unity 编辑器中使用 DotCraft。

</div>

## 简介

dotcraft-unity 是 [DotCraft](https://github.com/DotHarness/dotcraft) 的 Unity 编辑器客户端。它通过 Agent Client Protocol (ACP) 将 Unity 项目连接到 DotCraft，提供编辑器内聊天窗口。
除了 DotCraft ，它也支持 Claude Code、Cursor、Codex 等任何已实现 ACP 协议的 Agent。

- 编辑器原生：通过 **Tools → DotCraft Assistant** 直接打开 DotCraft。
- 项目感知：默认将 Unity 项目根目录作为 DotCraft 工作区。
- 基于 ACP：DotCraft 作为 agent 进程运行，Unity 作为编辑器客户端。
- Unity 上下文：内置只读工具可暴露场景、选中对象、控制台和项目信息。

## 快速开始

1. 安装并配置 [DotCraft](https://github.com/DotHarness/dotcraft)。
2. 在 Unity 中通过 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) 安装 `System.Text.Json 9.0.10`。
3. 打开 **Window → Package Manager**，添加这个 Git URL：

   ```text
   https://github.com/DotHarness/dotcraft-unity.git
   ```

4. 打开 **Tools → DotCraft Assistant**。
5. 点击 **Connect**，然后在 Unity 编辑器中开始对话。

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
| **Enable Builtin Unity Tools** | `true` | 声明内置只读 Unity 运行时工具，并启用对应的 `_unity/*` 处理器。 |

配置 API key 时，建议使用 Project Settings 里的环境变量，不要把密钥提交到项目文件。

## 内置工具

dotcraft-unity 会在 ACP 初始化时向 DotCraft 声明四个只读 Unity 运行时工具：

| 工具 | 描述 |
|------|------|
| `unity_scene_query` | 查询场景层级结构，可选包含组件详情。 |
| `unity_get_selection` | 读取 Unity 编辑器当前选中对象。 |
| `unity_get_console_logs` | 获取最近的 Unity Console 日志。 |
| `unity_get_project_info` | 读取 Unity 版本、项目名称和包信息。 |

这些工具帮助 DotCraft 理解当前场景与项目状态。模型可见的工具描述符位于这个 Unity 客户端中；`_unity/*` ACP 方法只是执行这些工具时使用的私有回调。如需完整 Unity 编辑自动化能力，可以将 DotCraft 与 [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) 或 [unity-mcp](https://github.com/CoplayDev/unity-mcp) 等专用 Unity 工具包配合使用。

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
