# MCP Tool Gateway

| Field | Value |
|-------|-------|
| **Status** | Draft |
| **Owner** | dotcraft-unity |
| **Runtime** | Unity Editor |
| **Network Scope** | 127.0.0.1 only |

## Purpose

The MCP Tool Gateway is the dotcraft-unity-owned local entry point that lets external agents discover and invoke Unity Editor tools while the editor is running.

The gateway belongs to the Unity package. It is not part of DotCraft Core, DotCraft AppServer, or the DotCraft App Binding protocol. App Binding remains a separate path for attaching Unity tools to DotCraft threads; the MCP Tool Gateway is the direct local surface for MCP-compatible agents, OpenAI function-call hosts, and Claude tool-use hosts.

```text
LLM / Agent Host
  -> MCP Tool Gateway (MCP / HTTP adapters)
  -> Unity Tool Gateway registry
  -> Execution Core or Runtime Tool Invoker
  -> Unity Runtime
```

## Ownership Boundaries

- dotcraft-unity owns tool discovery, schema projection, request validation, Unity main-thread dispatch, and result normalization.
- External agent hosts own model prompting, approval UX, tool-call scheduling, retries, and model-result replay.
- DotCraft AppServer is not required for MCP Tool Gateway discovery or invocation.
- App Binding descriptors do not define the Tool Gateway contract.

## Runtime Tool Registry

`UnityToolGateway` builds the gateway registry from the current dotcraft-unity runtime tool surface. The registry is generated on every `tools/list` and `tools/call`, so Project Settings changes apply to the next request.

Settings rules:

- **Enable Local Tool Gateway** starts or stops the localhost server used by App Binding and the MCP Tool Gateway.
- **Enable C# Automation** controls the built-in `unity_execute_csharp` tool exposed through the gateway.
- Custom Project Tools are exposed only when their `AgentToolAttribute` entry is enabled in **Project Settings > DotCraft > Unity Tools > Custom Project Tools**.
- The gateway does not add a second per-tool enablement list.

Name rules:

- Public tool names come from the runtime tool descriptor `Name`.
- The C# execution tool public name is `unity_execute_csharp`.
- The legacy names `ExecuteCSharp` and `execute_csharp` are not aliases.
- `unity_execute_csharp` is a reserved gateway name with a dedicated execution handler. Enabled runtime tools with the same name are skipped by name de-duplication.

Default built-in tool:

- `unity_execute_csharp`

## Local HTTP Surface

The gateway is served by the existing dotcraft-unity local server:

```text
http://127.0.0.1:39777
```

Endpoints:

```text
POST /dotcraft/mcp
GET  /dotcraft/gateway/tools?format=canonical|openai-responses|openai-chat|claude
POST /dotcraft/gateway/call
```

The server accepts loopback requests only. Requests with an `Origin` header must come from localhost or loopback addresses.

## Setup UI

Open **Tools > DotCraft > MCP Gateway Setup** or click **Setup MCP Clients** in **Project Settings > DotCraft**.

The setup window:

- Writes project-level configuration only.
- Writes only the loopback MCP endpoint.
- Shows a preview before installing.
- Creates a timestamped `.bak` backup before modifying an existing file.
- Merges existing JSON config and preserves unrelated TOML content.
- Supports uninstall by removing only the `dotcraft-unity` server block.
- Tests the gateway with MCP `initialize` and `tools/list`.

Supported targets:

| Client | Project file | Notes |
|--------|--------------|-------|
| Claude Code | `.mcp.json` | Project-scoped servers require approval in Claude Code before use. |
| Codex | `.codex/config.toml` | Uses prompt approval by default. |
| Cursor | `.cursor/mcp.json` | Verify the server in Cursor MCP settings after opening the project. |

Claude Code:

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

Codex:

```toml
[mcp_servers.dotcraft_unity]
url = "http://127.0.0.1:39777/dotcraft/mcp"
enabled = true
tool_timeout_sec = 60
default_tools_approval_mode = "prompt"
```

Cursor:

```json
{
  "mcpServers": {
    "dotcraft-unity": {
      "url": "http://127.0.0.1:39777/dotcraft/mcp"
    }
  }
}
```

## MCP Flow

The MCP endpoint is request-response JSON-RPC over HTTP:

```text
POST http://127.0.0.1:39777/dotcraft/mcp
```

Supported methods:

- `initialize`
- `notifications/initialized`
- `ping`
- `tools/list`
- `tools/call`

Discovery flow:

```text
client -> initialize
client -> notifications/initialized
client -> tools/list
server -> enabled runtime tool specs
```

Call flow:

```text
client -> tools/call
server -> resolve the enabled gateway registry by tool name
server -> validate arguments
server -> dispatch unity_execute_csharp through Execution Core
server -> dispatch other runtime tools through RuntimeToolInvoker
server -> execute Unity API work on the Unity main thread
server -> return MCP content and structuredContent
```

Protocol errors, such as invalid JSON-RPC methods or malformed MCP parameters, return JSON-RPC errors. Tool-level failures, such as compiler diagnostics, argument conversion failures, missing disabled tools, or Unity execution exceptions, return a successful JSON-RPC response whose MCP tool result has `isError: true`.

## HTTP Adapter Flow

The plain HTTP adapter is a schema projection and local call bridge. It does not implement an OpenAI or Claude agent loop.

Tool schema projection:

```text
GET /dotcraft/gateway/tools?format=canonical
GET /dotcraft/gateway/tools?format=openai-responses
GET /dotcraft/gateway/tools?format=openai-chat
GET /dotcraft/gateway/tools?format=claude
```

Direct local call:

```json
{
  "namespace": "unity",
  "tool": "unity_execute_csharp",
  "arguments": {
    "code": "return UnityEngine.Application.unityVersion;",
    "mode": "editor"
  }
}
```

The adapter normalizes tool results into:

```json
{
  "success": true,
  "name": "unity_execute_csharp",
  "result": {},
  "text": "short model-readable summary",
  "durationMs": 42
}
```

## unity_execute_csharp Contract

`unity_execute_csharp` compiles and executes a C# method body in the running Unity Editor process.

Input schema:

```json
{
  "type": "object",
  "properties": {
    "code": { "type": "string" },
    "mode": { "type": "string", "enum": ["editor", "playmode"] }
  },
  "required": ["code"],
  "additionalProperties": false
}
```

`mode` defaults to `editor`. `playmode` may execute only when the Unity Editor is already in Play Mode.

Example request:

```json
{
  "code": "var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = \"Agent Cube\"; return cube.name;",
  "mode": "editor"
}
```

Successful structured result:

```json
{
  "success": true,
  "mode": "editor",
  "returnValue": "Agent Cube",
  "diagnostics": [],
  "logs": [],
  "durationMs": 42
}
```

Failed structured result:

```json
{
  "success": false,
  "mode": "editor",
  "errorCode": "CompilationFailed",
  "errorMessage": "C# compilation failed.",
  "diagnostics": [],
  "logs": [],
  "durationMs": 42
}
```

## Result Rules

- `unity_execute_csharp` returns the Execution Core result model directly in MCP `structuredContent`.
- Other runtime tools return their method result in MCP `structuredContent`.
- HTTP `/dotcraft/gateway/call` wraps every call in the gateway result envelope with `success`, `name`, `result`, `text`, `errorCode`, `errorMessage`, and `durationMs`.
- Missing, disabled, or de-duplicated tools return `ToolNotFound`.
- Argument conversion failures return `InvalidArguments`.
- Tool invocation exceptions return `ToolExecutionException`.
- Transport errors are reserved for protocol, parsing, routing, and server failures.

## Extension Rules

Project and plugin authors use `AgentToolAttribute` to expose static Editor methods as Custom Project Tools. New custom tools are discovered in Project Settings, default to disabled, and appear in the gateway only after the user enables them.

New built-in gateway tools should be added to the runtime tool catalog unless they need a dedicated result contract like `unity_execute_csharp`. Dedicated handlers must:

- Have a stable public snake_case tool name.
- Provide a JSON Schema input object.
- Return `ToolGatewayResult`.
- Dispatch Unity API work onto the Unity main thread.
- Preserve structured failure results across MCP and HTTP adapters.
- Add focused EditMode coverage for discovery, successful call, failure call, and adapter schema projection.
