# Custom Project Tools

This document defines how Unity Editor code exposes Custom Project Tools through dotcraft-unity.

Related: [MCP Tool Gateway](tool-gateway.md).

## Contract

- Custom Project Tools are declared to in-editor ACP sessions only when **Project Settings > DotCraft > Agent** is `DotCraft`.
- `Custom ACP` agents connected through the in-editor chat do not receive these tools, even if they support ACP extension methods.
- Enabled Custom Project Tools are also available to external MCP clients through the MCP Tool Gateway.
- Tools are discovered from loaded Editor assemblies that reference `DotCraft.Editor`.
- A tool method must be `static`, non-generic, and must not use `ref` or `out` parameters.
- Newly discovered custom tools default to disabled. Users enable them in **Project Settings > DotCraft > Unity Tools > Custom Project Tools**.
- Settings changes apply on the next connect, reconnect, new session, or loaded session.

## DotCraft ACP Extension

Custom Project Tools use a private DotCraft extension to stable ACP v1. This is not an ACP standard runtime-tool envelope and does not use the draft MCP-over-ACP transport.

During `initialize`, dotcraft-unity advertises enabled tools only under the ACP `_meta` extension point:

```json
{
  "clientCapabilities": {
    "_meta": {
      "dotcraft": {
        "runtimeTools": {
          "version": 1,
          "tools": []
        }
      }
    }
  }
}
```

Each descriptor's `acpMethod` is a private JSON-RPC method beginning with `_`. Runtime callbacks return a DotCraft version 1 result envelope:

```json
{
  "success": false,
  "contentItems": [{ "type": "text", "text": "Compilation failed." }],
  "structuredContent": {},
  "errorCode": "CompilationFailed",
  "errorMessage": "C# compilation failed."
}
```

Expected tool failures are valid JSON-RPC results with `success: false`. An unknown private method returns JSON-RPC `-32601`, invalid parameters return `-32602`, and unexpected routing or infrastructure failures return `-32603`. The root-level `clientCapabilities.extensions` field is not used.

## Registration API

The built-in `unity_execute_csharp` runtime tool accepts optional leading ordinary, alias, `static`, or `global` using directives followed by method-body statements. It does not accept a complete namespace, class, or method declaration.

Mark a static method with `AgentToolAttribute`:

```csharp
using System.ComponentModel;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;

public static class ExampleDotCraftTools
{
    [Description("Return a greeting from an example Unity plugin.")]
    [AgentTool(
        Namespace = "example",
        Name = "example_greet",
        Kind = AcpToolKind.Read)]
    public static object Greet(
        [Description("Name to greet.")] string name = "Unity")
    {
        return new { message = $"Hello, {name}." };
    }
}
```

`Name` is optional and defaults to the method name. `Description` can be set on the attribute or supplied by `DescriptionAttribute`. `Namespace`, `Kind`, and `DeferLoading` are optional. `DeferLoading` defaults to `true`.

## Schema Inference

dotcraft-unity infers the JSON Schema sent to DotCraft from the method signature:

- Each method parameter becomes a top-level object property.
- Parameter and DTO member descriptions are read from `DescriptionAttribute`.
- JSON names prefer Newtonsoft `JsonPropertyAttribute.PropertyName`; otherwise camelCase is used.
- Supported parameter shapes: primitive values, strings, enums, arrays/lists, `Dictionary<string, T>`, simple DTOs, `JObject`, and `JToken`.
- Non-optional method parameters are marked as required.
- DTO members are marked as required only when Newtonsoft `JsonPropertyAttribute.Required` requires presence.

Unsupported tools are skipped instead of failing ACP initialization. Diagnostics are shown in Project Settings and written to the Unity Console when verbose logging is enabled.

## Approval Metadata

Tools that need DotCraft approval can declare optional approval metadata:

```csharp
[AgentTool(
    Name = "example_write_file",
    Description = "Write generated content to a project file.",
    Kind = AcpToolKind.Edit,
    ApprovalKind = "file",
    ApprovalTargetArgument = "path",
    ApprovalOperation = "write")]
public static object WriteFile(string path, string content)
{
    // Plugin implementation.
    return new { success = true };
}
```

`ApprovalTargetArgument` and `ApprovalOperationArgument`, when used, must reference top-level string parameters. Approval policy is owned by DotCraft; dotcraft-unity only forwards the descriptor.
