# Unity Agent OS Tool Gateway

| Field | Value |
|-------|-------|
| **Status** | Draft |
| **Owner** | dotcraft-unity |
| **Runtime** | Unity Editor |
| **Network Scope** | 127.0.0.1 only |

## Purpose

The Tool Gateway is the dotcraft-unity-owned entry point that lets external agents discover and invoke Unity tools while the Unity Editor is running.

The gateway belongs to the Unity package, not to DotCraft Core, DotCraft AppServer, or App Binding. DotCraft App Binding remains a separate integration path for attaching Unity tools to DotCraft threads. The Tool Gateway is the direct local surface for external clients such as MCP-compatible agents, OpenAI function-call hosts, and Claude tool-use hosts.

```text
LLM / Agent Host
  -> Tool Gateway (MCP / HTTP adapters)
  -> Unity Tool Gateway registry
  -> Execution Core
  -> Unity Runtime
```

## Ownership Boundaries

- dotcraft-unity owns tool registration, schema projection, request validation, Unity main-thread dispatch, and result normalization.
- External agent hosts own model prompting, approval UX, tool-call scheduling, retries, and model-result replay.
- DotCraft AppServer is not required for Tool Gateway discovery or invocation.
- App Binding descriptors do not define the Tool Gateway contract.

## Canonical Tool Registry

The gateway exposes a small canonical tool surface through `UnityToolGateway`. Tools are registered explicitly. The gateway must not automatically expose every `AgentToolAttribute` runtime tool, because external agent clients need a stable and intentionally curated surface.

Each canonical tool has:

- `name`: stable public tool name.
- `description`: model-facing behavior summary.
- `inputSchema`: JSON Schema object for arguments.
- `handler`: async Unity-side implementation that returns a `ToolGatewayResult`.

The current canonical tool set contains `ExecuteCSharp`.

Future canonical tools should be added only when they represent a stable external-agent workflow, for example `QueryUnityState` or higher-level scene editing tools.

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
server -> canonical tool specs
```

Call flow:

```text
client -> tools/call
server -> validate tool name and arguments
server -> dispatch handler through UnityToolGateway
server -> route execution through Execution Core
server -> execute on the Unity main thread when Unity APIs are required
server -> return MCP content and structuredContent
```

Protocol errors, such as invalid JSON-RPC methods or malformed parameters, return JSON-RPC errors. Tool-level failures, such as compiler diagnostics or Unity execution exceptions, return a successful JSON-RPC response whose MCP tool result has `isError: true`.

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
  "tool": "ExecuteCSharp",
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
  "name": "ExecuteCSharp",
  "result": {},
  "text": "short model-readable summary",
  "durationMs": 42
}
```

## ExecuteCSharp Contract

`ExecuteCSharp` compiles and executes a C# method body in the running Unity Editor process.

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

- Every tool result must include `success`, `durationMs`, and either a structured payload or an error code and message.
- Compiler diagnostics are included when compilation fails.
- Unity logs captured during execution are returned in `logs`.
- Successful results should keep `diagnostics` empty unless the diagnostic directly affects the external agent workflow.
- Transport errors are reserved for protocol, parsing, routing, and server failures.
- Tool failures are represented in the tool result, not by throwing through the transport.

## Extension Rules

New gateway tools must:

- Be explicitly registered in `UnityToolGateway`.
- Have a stable public name and JSON Schema.
- Return `ToolGatewayResult`.
- Dispatch Unity API work onto the Unity main thread.
- Preserve structured failure results across MCP and HTTP adapters.
- Add focused EditMode coverage for discovery, successful call, failure call, and adapter schema projection.

Plugin runtime tools may continue to use `AgentToolAttribute` for DotCraft App Binding. That mechanism is intentionally separate from the canonical Tool Gateway registry.
