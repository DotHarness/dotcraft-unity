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
DELETE /dotcraft/mcp
GET  /dotcraft/gateway/tools?format=canonical|openai-responses|openai-chat|claude
POST /dotcraft/gateway/call
```

The server accepts loopback requests only. Requests with an `Origin` header must come from localhost or loopback addresses.

## Setup UI

Open **Tools > DotCraft > MCP Gateway Setup** or click **Setup MCP Clients** in **Project Settings > DotCraft**.

The setup window:

- Writes project-level configuration only.
- Writes only the loopback MCP endpoint.
- Shows each client's current setup state and installs, updates, or removes them individually.
- Merges existing JSON config and preserves unrelated TOML content.
- Supports uninstall by removing only the `dotcraft-unity` server block.
- Tests the gateway with MCP `initialize`, `notifications/initialized`, and `tools/list`.

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

The MCP endpoint implements MCP Streamable HTTP for request-response JSON-RPC. It does not offer an SSE stream; `GET /dotcraft/mcp` returns `405 Method Not Allowed`.

```text
POST http://127.0.0.1:39777/dotcraft/mcp
```

Every MCP POST request must include:

```text
Accept: application/json, text/event-stream
```

Requests with a missing or incomplete `Accept` header return HTTP `406 Not Acceptable`. Request bodies must be a single JSON-RPC `2.0` object; batch arrays and malformed request IDs are rejected as protocol errors.

Supported methods:

- `initialize`
- `notifications/initialized`
- `notifications/cancelled`
- `ping`
- `tools/list`
- `tools/call`

Discovery flow:

```text
client -> initialize
server -> initialize result + MCP-Session-Id header
client -> notifications/initialized + MCP-Session-Id header
client -> tools/list + MCP-Session-Id header
server -> enabled runtime tool specs
```

Call flow:

```text
client -> tools/call + MCP-Session-Id header
server -> resolve the enabled gateway registry by tool name
server -> validate arguments
server -> dispatch unity_execute_csharp through Execution Core
server -> dispatch other runtime tools through RuntimeToolInvoker
server -> execute Unity API work on the Unity main thread
server -> return MCP content and structuredContent
```

Session rules:

- `initialize` creates a session and returns `MCP-Session-Id`.
- Subsequent MCP HTTP requests must include `MCP-Session-Id`.
- `notifications/initialized` marks the session ready for tool operations.
- `ping` is allowed before `notifications/initialized`; tool operations are not.
- Missing session headers return HTTP `400 Bad Request`.
- Unknown or terminated session IDs return HTTP `404 Not Found`; compliant clients should start a fresh `initialize`.
- Idle sessions expire after two hours and then return HTTP `404 Not Found`.
- Sessions are process-scoped: local server restarts and domain reloads in the same Unity Editor process keep them, while a full Editor restart invalidates them.
- Clients may end a session with `DELETE /dotcraft/mcp` and the `MCP-Session-Id` header.
- `GET /dotcraft/mcp` always returns HTTP `405 Method Not Allowed` when the protocol version header is valid, because this gateway does not expose an SSE stream.
- `MCP-Protocol-Version`, when present, must be `2025-11-25`. Unsupported header values return JSON-RPC error code `-32022` with supported versions in `error.data`.
- If an `initialize` request body asks for an unsupported protocol version, the gateway negotiates down to `2025-11-25`.

Protocol errors, such as invalid JSON-RPC envelopes, invalid MCP parameters, and unknown or disabled tool names, return JSON-RPC errors. Tool-level failures, such as compiler diagnostics, argument conversion failures inside an enabled tool, or Unity execution exceptions, return a successful JSON-RPC response whose MCP tool result has `isError: true`.

Error code meanings:

| Code | Name | Meaning |
|------|------|---------|
| `-32700` | `ParseError` | JSON-RPC body is not valid JSON. |
| `-32600` | `InvalidRequest` | JSON-RPC envelope is malformed or violates lifecycle request rules. |
| `-32601` | `MethodNotFound` | MCP method is not implemented by this tools-only gateway. |
| `-32602` | `InvalidParams` | MCP method params are malformed, or `tools/call` names an unknown/disabled tool. |
| `-32001` | `MissingSession` | `MCP-Session-Id` is missing, unknown, expired, or terminated. HTTP `400` vs `404` distinguishes recoverability. |
| `-32002` | `SessionNotInitialized` | Session exists but has not completed `notifications/initialized`. |
| `-32022` | `UnsupportedProtocolVersion` | `MCP-Protocol-Version` header is unsupported. |

Utility boundaries:

- `ping` returns `{}` and is allowed before initialization completes.
- `notifications/cancelled` is accepted for valid sessions. It cooperatively cancels an in-flight `tools/call` before or during a cancellable runtime tool; unknown, completed, or malformed cancellation notifications are ignored with HTTP `202 Accepted`.
- Runtime tools may include a hidden `CancellationToken` parameter. The parameter is not exposed in the input schema and is injected by the gateway at execution time.
- `_meta.progressToken` is accepted on `tools/call` when it is a string or integer. The gateway does not send progress notifications because it does not provide an SSE stream in this pass.
- The gateway does not declare the MCP logging capability. `logging/setLevel` remains unavailable and returns `Method not found`.

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
- Missing, disabled, or de-duplicated tools return JSON-RPC `-32602` through MCP `tools/call`, and `ToolNotFound` through the plain HTTP `/dotcraft/gateway/call` adapter.
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
