using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.RuntimeTools
{
    internal static class RuntimeToolInvoker
    {
        public static async Task<object> InvokeAsync(RuntimeToolDefinition tool, JToken paramsJson)
        {
            return await InvokeAsync(tool, paramsJson, CancellationToken.None).ConfigureAwait(false);
        }

        public static async Task<object> InvokeAsync(
            RuntimeToolDefinition tool,
            JToken paramsJson,
            CancellationToken cancellationToken)
        {
            var parameters = tool.Parameters;
            var args = new object[parameters.Count];
            var paramObject = paramsJson as JObject ?? new JObject();

            for (var i = 0; i < parameters.Count; i++)
            {
                var runtimeParameter = parameters[i];
                var parameter = runtimeParameter.Parameter;
                if (runtimeParameter.InjectCancellationToken)
                {
                    args[i] = cancellationToken;
                    continue;
                }

                if (paramObject.TryGetValue(runtimeParameter.JsonName, StringComparison.Ordinal, out var token)
                    && token.Type != JTokenType.Undefined)
                {
                    args[i] = ConvertToken(token, parameter.ParameterType);
                }
                else
                {
                    args[i] = GetDefaultValue(parameter);
                }
            }

            object rawResult;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                rawResult = tool.Method.Invoke(null, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            if (rawResult is Task task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!task.GetType().IsGenericType)
                    return new { success = true };

                return task.GetType()
                           .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
                           ?.GetValue(task)
                       ?? new { success = true };
            }

            return rawResult ?? new { success = true };
        }

        private static object ConvertToken(JToken token, Type targetType)
        {
            if (typeof(JToken).IsAssignableFrom(targetType))
            {
                if (targetType.IsInstanceOfType(token))
                    return token.DeepClone();

                return token.ToObject(targetType, DotCraftJson.CompactSerializer);
            }

            return token.Type == JTokenType.Null && CanBeNull(targetType)
                ? null
                : token.ToObject(targetType, DotCraftJson.CompactSerializer);
        }

        private static object GetDefaultValue(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue
                && parameter.DefaultValue != DBNull.Value
                && parameter.DefaultValue != Type.Missing)
            {
                return parameter.DefaultValue;
            }

            var type = parameter.ParameterType;
            return CanBeNull(type) ? null : Activator.CreateInstance(type);
        }

        private static bool CanBeNull(Type type) =>
            !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }
}
