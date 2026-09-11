using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.McpSetup;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Execution
{
    internal sealed class ExternalCSharpExecutionEngine : IExecutionEngine
    {
        private const string InlineSourceLabel = "unity_execute_csharp";

        private readonly ConcurrentDictionary<string, CompiledSnippet> _compiledSnippets = new();
        private readonly IExternalCSharpCompiler _compiler;

        public ExternalCSharpExecutionEngine(IExternalCSharpCompiler compiler = null)
        {
            _compiler = compiler ?? new ExternalCSharpCompiler();
        }

        public string Engine => UnityExecutionEngines.CSharp;

        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mode = NormalizeMode(request.Mode);
            var stopwatch = Stopwatch.StartNew();
            var logs = new List<ExecutionLogEntry>();

            if (!IsKnownMode(mode))
            {
                return ExecutionResult.Failed(
                    mode,
                    "InvalidMode",
                    $"unity_execute_csharp mode must be '{UnityExecutionModes.Editor}' or '{UnityExecutionModes.PlayMode}'.",
                    stopwatch.ElapsedMilliseconds);
            }

            if (mode == UnityExecutionModes.PlayMode && !EditorApplication.isPlaying)
            {
                return ExecutionResult.Failed(
                    mode,
                    "UnityNotInPlayMode",
                    "unity_execute_csharp mode 'playmode' requires the Unity Editor to be in Play Mode.",
                    stopwatch.ElapsedMilliseconds);
            }

            try
            {
                if (!TryResolveSource(request, out var code, out var sourceLabel, out var sourceError))
                {
                    return ExecutionResult.Failed(
                        mode,
                        sourceError.Code,
                        sourceError.Message,
                        stopwatch.ElapsedMilliseconds);
                }

                var hash = ComputeHash(sourceLabel, code);
                if (!_compiledSnippets.TryGetValue(hash, out var compiledSnippet))
                {
                    var compilation = await _compiler.CompileAsync(code, sourceLabel,
                        BuildMetadataReferences(), cancellationToken);
                    if (!compilation.Success)
                    {
                        return ExecutionResult.Failed(
                            mode,
                            compilation.ErrorCode,
                            compilation.ErrorMessage,
                            stopwatch.ElapsedMilliseconds,
                            compilation.Diagnostics);
                    }
                    var assembly = Assembly.Load(compilation.Assembly);
                    var type = assembly.GetType(compilation.EntryType, throwOnError: true);
                    var runMethod = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                    if (runMethod == null)
                        throw new InvalidOperationException("Compiled snippet did not contain an executable Run method.");
                    compiledSnippet = new CompiledSnippet(runMethod, compilation.Diagnostics);
                    _compiledSnippets[hash] = compiledSnippet;
                }

                Application.LogCallback callback = (condition, stackTrace, type) =>
                {
                    logs.Add(new ExecutionLogEntry
                    {
                        Type = type.ToString(),
                        Message = condition,
                        StackTrace = stackTrace
                    });
                };

                cancellationToken.ThrowIfCancellationRequested();
                var ctx = EditorExecutionScheduler.CreateContext(cancellationToken);
                using var executionScope = EditorExecutionScheduler.Begin();
                object rawReturnValue;
                Application.logMessageReceived += callback;
                try
                {
                    var execution = (Task<object>)compiledSnippet.RunMethod.Invoke(
                        null, new object[] { request.Inputs ?? new JObject(), ctx, cancellationToken });
                    rawReturnValue = await execution;
                }
                finally
                {
                    Application.logMessageReceived -= callback;
                }

                var returnValue = UnityValueNormalizer.Normalize(rawReturnValue);
                return ExecutionResult.Ok(
                    mode,
                    returnValue,
                    logs,
                    stopwatch.ElapsedMilliseconds,
                    compiledSnippet.Diagnostics);
            }
            catch (OperationCanceledException) { throw; }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                return ExecutionResult.Failed(
                    mode,
                    "ExecutionException",
                    FormatException(ex.InnerException),
                    stopwatch.ElapsedMilliseconds,
                    logs: logs);
            }
            catch (Exception ex)
            {
                return ExecutionResult.Failed(
                    mode,
                    "ExecutionException",
                    FormatException(ex),
                    stopwatch.ElapsedMilliseconds,
                    logs: logs);
            }
        }

        // The source label feeds both #line diagnostics and the compilation cache key.
        private static bool TryResolveSource(
            ExecutionRequest request,
            out string code,
            out string sourceLabel,
            out (string Code, string Message) error)
        {
            code = null;
            sourceLabel = InlineSourceLabel;
            error = default;

            var hasCode = !string.IsNullOrWhiteSpace(request.Code);
            var hasPath = !string.IsNullOrWhiteSpace(request.Path);

            if (hasCode && hasPath)
            {
                error = ("InvalidArguments", "unity_execute_csharp accepts 'code' or 'path', not both.");
                return false;
            }

            if (!hasCode && !hasPath)
            {
                error = ("EmptyCode", "unity_execute_csharp requires either a 'code' snippet or a script 'path'.");
                return false;
            }

            if (hasCode)
            {
                code = request.Code;
                return true;
            }

            var requestedPath = request.Path.Trim();
            var fullPath = Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(Path.Combine(
                    McpGatewaySetupDefaults.ProjectRoot,
                    requestedPath.Replace('/', Path.DirectorySeparatorChar)));

            if (!File.Exists(fullPath))
            {
                error = ("ScriptNotFound", $"Script not found: {request.Path}");
                return false;
            }

            code = File.ReadAllText(fullPath);
            sourceLabel = fullPath.Replace('\\', '/');
            return true;
        }

        private static List<string> BuildMetadataReferences()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddAssembly(Assembly assembly)
            {
                if (assembly == null || assembly.IsDynamic)
                    return;

                string location;
                try
                {
                    location = assembly.Location;
                }
                catch
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
                    paths.Add(location);
            }

            AddAssembly(typeof(object).Assembly);
            AddAssembly(typeof(Enumerable).Assembly);
            AddAssembly(typeof(Task).Assembly);
            AddAssembly(typeof(GameObject).Assembly);
            AddAssembly(typeof(EditorApplication).Assembly);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                AddAssembly(assembly);

            return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsKnownMode(string mode)
        {
            return mode == UnityExecutionModes.Editor || mode == UnityExecutionModes.PlayMode;
        }

        private static string NormalizeMode(string mode)
        {
            var trimmed = mode?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(trimmed) ? UnityExecutionModes.Editor : trimmed;
        }

        private static string ComputeHash(string sourceLabel, string code)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sourceLabel + "\n" + code));
            var builder = new StringBuilder(16);
            for (var i = 0; i < 8 && i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }

        private sealed class CompiledSnippet
        {
            public CompiledSnippet(MethodInfo runMethod, List<ExecutionDiagnostic> diagnostics)
            {
                RunMethod = runMethod;
                Diagnostics = diagnostics ?? new List<ExecutionDiagnostic>();
            }

            public MethodInfo RunMethod { get; }

            public List<ExecutionDiagnostic> Diagnostics { get; }
        }

    }
}
