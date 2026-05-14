using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Editor.Protocol;

namespace DotCraft.Editor.RuntimeTools
{
    internal static class RuntimeToolCatalog
    {
        private static readonly Regex FunctionNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public static RuntimeToolCatalogSnapshot Discover()
        {
            var tools = new List<RuntimeToolDefinition>();
            var diagnostics = new List<string>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                         .Where(a => !a.IsDynamic && CanContainRuntimeTools(a))
                         .OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
            {
                foreach (var type in GetLoadableTypes(assembly, diagnostics))
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    foreach (var method in methods)
                    {
                        var attribute = method.GetCustomAttribute<DotCraftRuntimeToolAttribute>(false);
                        if (attribute == null)
                            continue;

                        if (TryCreateDefinition(method, attribute, out var definition, out var diagnostic))
                            tools.Add(definition);
                        else
                            diagnostics.Add($"{FormatMethod(method)}: {diagnostic}");
                    }
                }
            }

            return new RuntimeToolCatalogSnapshot(
                tools.OrderBy(t => t.Id, StringComparer.Ordinal).ToList(),
                diagnostics);
        }

        public static RuntimeToolResolution ResolveEnabledTools(
            RuntimeToolCatalogSnapshot snapshot,
            bool enableBuiltins,
            Func<string, bool> isPluginEnabled,
            IEnumerable<string> reservedToolNames = null)
        {
            var resolved = new List<RuntimeToolDefinition>();
            var diagnostics = new List<string>(snapshot.Diagnostics);
            var usedNames = new HashSet<string>(reservedToolNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            var candidates = snapshot.Tools
                .Where(t => t.Source == RuntimeToolSource.Builtin
                    ? enableBuiltins
                    : isPluginEnabled(t.Id))
                .OrderBy(t => t.Source == RuntimeToolSource.Builtin ? 0 : 1)
                .ThenBy(t => t.Id, StringComparer.Ordinal);

            foreach (var tool in candidates)
            {
                if (!usedNames.Add(tool.Descriptor.Name))
                {
                    diagnostics.Add(
                        $"{tool.DisplayName}: skipped because enabled runtime tool name '{tool.Descriptor.Name}' is already in use.");
                    continue;
                }

                resolved.Add(tool);
            }

            return new RuntimeToolResolution(resolved, diagnostics);
        }

        public static RuntimeToolResolution ResolveEnabledTools(
            Func<string, bool> isPluginEnabled,
            IEnumerable<string> reservedToolNames = null)
        {
            return ResolveEnabledTools(Discover(), false, isPluginEnabled, reservedToolNames);
        }

        private static bool TryCreateDefinition(
            MethodInfo method,
            DotCraftRuntimeToolAttribute attribute,
            out RuntimeToolDefinition definition,
            out string diagnostic)
        {
            definition = null;
            diagnostic = string.Empty;

            if (!method.IsStatic)
            {
                diagnostic = "Only static methods are supported.";
                return false;
            }

            if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
            {
                diagnostic = "Generic methods are not supported.";
                return false;
            }

            var name = NormalizeOptional(attribute.Name) ?? method.Name;
            if (!FunctionNameRegex.IsMatch(name))
            {
                diagnostic = $"Tool name '{name}' is invalid. Use letters, digits, and underscores, and start with a letter or underscore.";
                return false;
            }

            var @namespace = NormalizeOptional(attribute.Namespace);
            if (@namespace != null && !FunctionNameRegex.IsMatch(@namespace))
            {
                diagnostic = $"Tool namespace '{@namespace}' is invalid.";
                return false;
            }

            var description = NormalizeOptional(attribute.Description)
                              ?? NormalizeOptional(method.GetCustomAttribute<DescriptionAttribute>()?.Description);
            if (description == null)
            {
                diagnostic = "A tool description is required. Set DotCraftRuntimeToolAttribute.Description or DescriptionAttribute.";
                return false;
            }

            if (!RuntimeToolJsonSchema.TryBuildInputSchema(
                    method,
                    out var inputSchema,
                    out var parameters,
                    out diagnostic))
            {
                return false;
            }

            if (!TryBuildApproval(attribute, inputSchema, out var approval, out diagnostic))
                return false;

            var id = BuildStableId(method);
            var source = GetSource(method);
            var builtinOverride = method.GetCustomAttribute<DotCraftBuiltinRuntimeToolAttribute>(false);
            var acpMethod = NormalizeOptional(builtinOverride?.AcpMethod) ?? $"_unity/dynamic/{name}_{HashId(id)}";
            var descriptor = new AcpRuntimeToolDescriptor
            {
                Namespace = @namespace,
                Name = name,
                Description = description,
                InputSchema = inputSchema,
                AcpMethod = acpMethod,
                Kind = NormalizeOptional(attribute.Kind) ?? AcpToolKind.Other,
                DeferLoading = attribute.DeferLoading,
                Approval = approval
            };

            definition = new RuntimeToolDefinition(id, source, method, parameters, descriptor);
            return true;
        }

        private static bool TryBuildApproval(
            DotCraftRuntimeToolAttribute attribute,
            Dictionary<string, object> inputSchema,
            out AcpRuntimeToolApprovalDescriptor approval,
            out string diagnostic)
        {
            approval = null;
            diagnostic = string.Empty;

            var kind = NormalizeOptional(attribute.ApprovalKind);
            var target = NormalizeOptional(attribute.ApprovalTargetArgument);
            var operation = NormalizeOptional(attribute.ApprovalOperation);
            var operationArgument = NormalizeOptional(attribute.ApprovalOperationArgument);
            var hasAnyApprovalField = kind != null || target != null || operation != null || operationArgument != null;

            if (!hasAnyApprovalField)
                return true;

            if (kind == null || target == null)
            {
                diagnostic = "ApprovalKind and ApprovalTargetArgument are required when approval metadata is declared.";
                return false;
            }

            if (!kind.Equals("file", StringComparison.OrdinalIgnoreCase)
                && !kind.Equals("shell", StringComparison.OrdinalIgnoreCase)
                && !kind.Equals("remoteResource", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = $"ApprovalKind '{kind}' is not supported. Use file, shell, or remoteResource.";
                return false;
            }

            var hasStaticOperation = operation != null;
            var hasOperationArgument = operationArgument != null;
            if (hasStaticOperation == hasOperationArgument)
            {
                diagnostic = "Exactly one of ApprovalOperation or ApprovalOperationArgument must be set.";
                return false;
            }

            if (!RuntimeToolJsonSchema.IsTopLevelStringProperty(inputSchema, target))
            {
                diagnostic = $"ApprovalTargetArgument '{target}' must reference a top-level string parameter.";
                return false;
            }

            if (operationArgument != null
                && !RuntimeToolJsonSchema.IsTopLevelStringProperty(inputSchema, operationArgument))
            {
                diagnostic = $"ApprovalOperationArgument '{operationArgument}' must reference a top-level string parameter.";
                return false;
            }

            approval = new AcpRuntimeToolApprovalDescriptor
            {
                Kind = kind,
                TargetArgument = target,
                Operation = operation,
                OperationArgument = operationArgument
            };
            return true;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, List<string> diagnostics)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                diagnostics.Add(
                    $"{assembly.GetName().Name}: skipped {ex.LoaderExceptions?.Length ?? 0} type load exception(s) during runtime tool discovery.");
                return ex.Types.Where(t => t != null);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"{assembly.GetName().Name}: skipped during runtime tool discovery: {ex.Message}");
                return Array.Empty<Type>();
            }
        }

        private static bool CanContainRuntimeTools(Assembly assembly)
        {
            var attributeAssembly = typeof(DotCraftRuntimeToolAttribute).Assembly;
            if (ReferenceEquals(assembly, attributeAssembly))
                return true;

            var attributeAssemblyName = attributeAssembly.GetName().Name;
            try
            {
                return assembly.GetReferencedAssemblies()
                    .Any(reference => string.Equals(reference.Name, attributeAssemblyName, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }

        private static RuntimeToolSource GetSource(MethodInfo method)
        {
            return ReferenceEquals(method.Module.Assembly, typeof(DotCraftRuntimeToolAttribute).Assembly)
                ? RuntimeToolSource.Builtin
                : RuntimeToolSource.Plugin;
        }

        private static string BuildStableId(MethodInfo method)
        {
            var assemblyName = method.Module.Assembly.GetName().Name ?? "";
            var typeName = method.DeclaringType?.FullName ?? "";
            var parameters = string.Join(
                ",",
                method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
            return $"{assemblyName}|{typeName}|{method.Name}|({parameters})";
        }

        private static string HashId(string id)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(id));
            var builder = new StringBuilder(16);
            for (var i = 0; i < 8 && i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static string FormatMethod(MethodInfo method)
        {
            return $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}";
        }

        private static string NormalizeOptional(string value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
