# DotCraft Runtime Dynamic Tools

This document defines how Unity Editor plugins expose DotCraft-only runtime dynamic tools through dotcraft-unity.

## Contract

- Runtime dynamic tools are declared only when **Project Settings > DotCraft > Agent Connection** is `DotCraft`.
- `Custom ACP` agents do not receive these tools, even if they support ACP extension methods.
- Tools are discovered from loaded Editor assemblies that reference `DotCraft.Editor`.
- A tool method must be `static`, non-generic, and must not use `ref` or `out` parameters.
- Newly discovered plugin tools default to disabled. Users enable them in **Project Settings > DotCraft > Unity Tools > Plugin Tools (DotCraft only)**.
- Settings changes apply on the next connect, reconnect, new session, or loaded session.

## Registration API

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
