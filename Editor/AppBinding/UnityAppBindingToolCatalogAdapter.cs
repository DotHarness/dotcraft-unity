using System;
using System.Collections.Generic;
using System.Linq;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Settings;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class UnityAppBindingToolAttachment
    {
        public List<AppServerDynamicToolSpec> Tools { get; set; } = new();
        public List<AppBindingToolCatalogEntry> ToolCatalog { get; set; } = new();
        public List<string> DirectToolNames { get; set; } = new();
        public List<string> DeferredToolNames { get; set; } = new();
        public Dictionary<string, RuntimeToolDefinition> ToolsByName { get; set; } = new(StringComparer.Ordinal);
        public List<string> Diagnostics { get; set; } = new();
    }

    internal static class UnityAppBindingToolCatalogAdapter
    {
        public static UnityAppBindingToolAttachment Build(DotCraftSettings settings, IReadOnlyCollection<string> grantedScopes)
        {
            var snapshot = RuntimeToolCatalog.Discover();
            var resolved = RuntimeToolCatalog.ResolveEnabledTools(
                snapshot,
                settings.EnableBuiltinUnityTools,
                id => settings.DynamicToolEnabledById.TryGetValue(id, out var enabled) && enabled);

            var granted = new HashSet<string>(grantedScopes ?? Array.Empty<string>(), StringComparer.Ordinal);
            var attachment = new UnityAppBindingToolAttachment
            {
                Diagnostics = new List<string>(resolved.Diagnostics)
            };

            foreach (var tool in resolved.Tools)
            {
                if (!TryBuildCatalogEntry(tool, out var catalog, out var diagnostic))
                {
                    attachment.Diagnostics.Add($"{tool.DisplayName}: {diagnostic}");
                    continue;
                }

                if (!granted.Contains(catalog.Scope))
                    continue;

                attachment.ToolCatalog.Add(catalog);
                attachment.Tools.Add(BuildToolSpec(tool, catalog));
                attachment.ToolsByName[catalog.Name] = tool;

                if (string.Equals(catalog.DefaultExposure, UnityAppBindingConstants.ExposureDirect, StringComparison.Ordinal)
                    && string.Equals(catalog.Risk, UnityAppBindingConstants.RiskRead, StringComparison.Ordinal))
                {
                    attachment.DirectToolNames.Add(catalog.Name);
                }
                else
                {
                    attachment.DeferredToolNames.Add(catalog.Name);
                }
            }

            return attachment;
        }

        private static bool TryBuildCatalogEntry(
            RuntimeToolDefinition tool,
            out AppBindingToolCatalogEntry catalog,
            out string diagnostic)
        {
            diagnostic = null;
            var scope = Normalize(tool.AppBinding.Scope) ?? InferScope(tool.Descriptor.Kind);
            var risk = Normalize(tool.AppBinding.Risk) ?? InferRisk(scope);
            var exposure = Normalize(tool.AppBinding.Exposure) ?? InferExposure(risk);

            if (!IsKnownScope(scope))
            {
                catalog = null;
                diagnostic = $"Unsupported App Binding scope '{scope}'.";
                return false;
            }

            if (!IsKnownRisk(risk))
            {
                catalog = null;
                diagnostic = $"Unsupported App Binding risk '{risk}'.";
                return false;
            }

            if (!IsKnownExposure(exposure))
            {
                catalog = null;
                diagnostic = $"Unsupported App Binding exposure '{exposure}'.";
                return false;
            }

            catalog = new AppBindingToolCatalogEntry
            {
                Name = tool.Descriptor.Name,
                Scope = scope,
                Risk = risk,
                DefaultExposure = exposure,
                Description = tool.Descriptor.Description
            };
            return true;
        }

        private static AppServerDynamicToolSpec BuildToolSpec(
            RuntimeToolDefinition tool,
            AppBindingToolCatalogEntry catalog)
        {
            return new AppServerDynamicToolSpec
            {
                Namespace = UnityAppBindingConstants.ToolNamespace,
                Name = tool.Descriptor.Name,
                Description = tool.Descriptor.Description,
                InputSchema = tool.Descriptor.InputSchema == null
                    ? new JObject { ["type"] = "object" }
                    : JToken.FromObject(tool.Descriptor.InputSchema, DotCraftJson.CompactSerializer),
                DeferLoading = !string.Equals(catalog.DefaultExposure, UnityAppBindingConstants.ExposureDirect, StringComparison.Ordinal),
                Approval = tool.Descriptor.Approval == null
                    ? null
                    : new AppServerToolApprovalDescriptor
                    {
                        Kind = tool.Descriptor.Approval.Kind,
                        TargetArgument = tool.Descriptor.Approval.TargetArgument,
                        Operation = tool.Descriptor.Approval.Operation,
                        OperationArgument = tool.Descriptor.Approval.OperationArgument
                    }
            };
        }

        private static string InferScope(string kind)
        {
            switch (Normalize(kind))
            {
                case AcpToolKind.Read:
                case AcpToolKind.Search:
                case AcpToolKind.Fetch:
                case AcpToolKind.Think:
                case AcpToolKind.Unity:
                    return UnityAppBindingConstants.ScopeRead;
                case AcpToolKind.Edit:
                case AcpToolKind.Move:
                case AcpToolKind.Delete:
                    return UnityAppBindingConstants.ScopeEdit;
                default:
                    return UnityAppBindingConstants.ScopeExecute;
            }
        }

        private static string InferRisk(string scope)
        {
            return string.Equals(scope, UnityAppBindingConstants.ScopeRead, StringComparison.Ordinal)
                ? UnityAppBindingConstants.RiskRead
                : UnityAppBindingConstants.RiskMutate;
        }

        private static string InferExposure(string risk)
        {
            return string.Equals(risk, UnityAppBindingConstants.RiskRead, StringComparison.Ordinal)
                ? UnityAppBindingConstants.ExposureDirect
                : UnityAppBindingConstants.ExposureDeferred;
        }

        private static bool IsKnownScope(string scope)
        {
            return string.Equals(scope, UnityAppBindingConstants.ScopeRead, StringComparison.Ordinal)
                   || string.Equals(scope, UnityAppBindingConstants.ScopeEdit, StringComparison.Ordinal)
                   || string.Equals(scope, UnityAppBindingConstants.ScopeExecute, StringComparison.Ordinal);
        }

        private static bool IsKnownRisk(string risk)
        {
            return string.Equals(risk, UnityAppBindingConstants.RiskRead, StringComparison.Ordinal)
                   || string.Equals(risk, UnityAppBindingConstants.RiskMutate, StringComparison.Ordinal)
                   || string.Equals(risk, UnityAppBindingConstants.RiskExternalWrite, StringComparison.Ordinal);
        }

        private static bool IsKnownExposure(string exposure)
        {
            return string.Equals(exposure, UnityAppBindingConstants.ExposureDirect, StringComparison.Ordinal)
                   || string.Equals(exposure, UnityAppBindingConstants.ExposureDeferred, StringComparison.Ordinal);
        }

        private static string Normalize(string value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
