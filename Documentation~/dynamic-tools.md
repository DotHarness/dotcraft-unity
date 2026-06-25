# DotCraft Runtime Dynamic Tools

This document defines how Unity Editor plugins expose DotCraft-only runtime dynamic tools through dotcraft-unity.

Related: [Unity Agent OS Tool Gateway](tool-gateway.md).

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

## App Binding Exposure

The same enabled runtime tools can also be attached to DotCraft threads through App Binding while Unity Editor is running. App Binding always exposes tools under the `unity` namespace because the DotCraft app descriptor owns one namespace per bound app.

The built-in `unity_execute_csharp` tool uses `unity.execute` and runs C# snippets inside the Unity Editor process. Plugin authors can reserve the same scope for higher-level execution tools.

Optional App Binding metadata can be added to `AgentToolAttribute`:

```csharp
[AgentTool(
    Name = "example_create_marker",
    Description = "Create a scene marker.",
    Kind = AcpToolKind.Edit,
    AppBindingScope = "unity.edit",
    AppBindingRisk = "mutate",
    AppBindingExposure = "deferred")]
public static object CreateMarker(string name)
{
    return new { success = true };
}
```

When the metadata is omitted, dotcraft-unity infers it from `Kind`:

- `read`, `search`, `fetch`, `think`, and `unity` use `unity.read` with `read` risk.
- `edit`, `move`, and `delete` use `unity.edit` with `mutate` risk.
- `execute` and `other` use `unity.execute` with `mutate` risk.

Only tools enabled in Project Settings are attached. Plugin tools still default to disabled, and App Binding does not add a second enablement list.

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
