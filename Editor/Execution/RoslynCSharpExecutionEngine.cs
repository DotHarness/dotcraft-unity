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
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Execution
{
    internal sealed class RoslynCSharpExecutionEngine : IExecutionEngine
    {
        private const int MaxNormalizedDepth = 4;
        private const int MaxNormalizedItems = 32;

        private readonly ConcurrentDictionary<string, CompiledSnippet> _compiledSnippets = new();

        public string Engine => UnityExecutionEngines.CSharp;

        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request)
        {
            var mode = NormalizeMode(request.Mode);
            var stopwatch = Stopwatch.StartNew();
            var logs = new List<ExecutionLogEntry>();

            if (!IsKnownMode(mode))
            {
                return Task.FromResult(ExecutionResult.Failed(
                    mode,
                    "InvalidMode",
                    $"unity_execute_csharp mode must be '{UnityExecutionModes.Editor}' or '{UnityExecutionModes.PlayMode}'.",
                    stopwatch.ElapsedMilliseconds));
            }

            if (mode == UnityExecutionModes.PlayMode && !EditorApplication.isPlaying)
            {
                return Task.FromResult(ExecutionResult.Failed(
                    mode,
                    "UnityNotInPlayMode",
                    "unity_execute_csharp mode 'playmode' requires the Unity Editor to be in Play Mode.",
                    stopwatch.ElapsedMilliseconds));
            }

            var code = request.Code ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                return Task.FromResult(ExecutionResult.Failed(
                    mode,
                    "EmptyCode",
                    "unity_execute_csharp requires a non-empty code snippet.",
                    stopwatch.ElapsedMilliseconds));
            }

            try
            {
                var hash = ComputeHash(code);
                if (!_compiledSnippets.TryGetValue(hash, out var compiledSnippet))
                {
                    if (!TryCompile(code, hash, out var runMethod, out var diagnostics, out var errorMessage))
                    {
                        return Task.FromResult(ExecutionResult.Failed(
                            mode,
                            "CompilationFailed",
                            errorMessage,
                            stopwatch.ElapsedMilliseconds,
                            diagnostics));
                    }

                    compiledSnippet = new CompiledSnippet(runMethod, diagnostics);
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

                object rawReturnValue;
                Application.logMessageReceived += callback;
                try
                {
                    rawReturnValue = compiledSnippet.RunMethod.Invoke(null, Array.Empty<object>());
                }
                finally
                {
                    Application.logMessageReceived -= callback;
                }

                var returnValue = NormalizeValue(rawReturnValue);
                return Task.FromResult(ExecutionResult.Ok(
                    mode,
                    returnValue,
                    logs,
                    stopwatch.ElapsedMilliseconds,
                    compiledSnippet.Diagnostics));
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                return Task.FromResult(ExecutionResult.Failed(
                    mode,
                    "ExecutionException",
                    FormatException(ex.InnerException),
                    stopwatch.ElapsedMilliseconds,
                    logs: logs));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ExecutionResult.Failed(
                    mode,
                    "ExecutionException",
                    FormatException(ex),
                    stopwatch.ElapsedMilliseconds,
                    logs: logs));
            }
        }

        private static bool TryCompile(
            string code,
            string hash,
            out MethodInfo runMethod,
            out List<ExecutionDiagnostic> diagnostics,
            out string errorMessage)
        {
            runMethod = null;
            diagnostics = new List<ExecutionDiagnostic>();
            errorMessage = null;

            var className = $"Snippet_{hash}";
            var source = BuildSource(className, code);
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Latest));

            var compilation = CSharpCompilation.Create(
                $"DotCraftExecution_{hash}",
                new[] { syntaxTree },
                BuildMetadataReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    allowUnsafe: true,
                    optimizationLevel: OptimizationLevel.Release));

            using var peStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream);
            diagnostics = emitResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(ToExecutionDiagnostic)
                .ToList();

            if (!emitResult.Success)
            {
                diagnostics = emitResult.Diagnostics.Select(ToExecutionDiagnostic).ToList();
                errorMessage = "C# compilation failed.";
                return false;
            }

            peStream.Position = 0;
            var assembly = Assembly.Load(peStream.ToArray());
            var type = assembly.GetType($"DotCraft.Editor.Execution.Generated.{className}", throwOnError: true);
            runMethod = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (runMethod == null)
            {
                errorMessage = "Compiled snippet did not contain an executable Run method.";
                return false;
            }

            return true;
        }

        private static string BuildSource(string className, string code)
        {
            var snippet = ParseSnippet(code);
            var source = new StringBuilder();

            AppendMappedUsings(source, snippet.GlobalUsings);
            source.AppendLine("using System;");
            source.AppendLine("using System.Linq;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("using UnityEngine;");
            source.AppendLine("using UnityEditor;");
            source.AppendLine("using DotCraft.Editor;");
            AppendMappedUsings(source, snippet.Usings);
            source.AppendLine();
            source.AppendLine("namespace DotCraft.Editor.Execution.Generated");
            source.AppendLine("{");
            source.Append("    public static class ").AppendLine(className);
            source.AppendLine("    {");
            source.AppendLine("        public static object Run()");
            source.AppendLine("        {");
            source.AppendLine("#line 1 \"unity_execute_csharp\"");
            source.AppendLine(snippet.Body);
            source.AppendLine("#line default");
            source.AppendLine("            return null;");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static SnippetSource ParseSnippet(string code)
        {
            var globalPrefixes = FindGlobalUsingPrefixes(code);
            var normalizedCode = code.ToCharArray();
            foreach (var prefix in globalPrefixes)
                BlankSpan(normalizedCode, prefix.Start, prefix.End);

            var syntaxTree = CSharpSyntaxTree.ParseText(
                new string(normalizedCode),
                new CSharpParseOptions(LanguageVersion.Latest));
            var root = syntaxTree.GetCompilationUnitRoot();
            var body = code.ToCharArray();
            var globalUsings = new List<MappedUsingDirective>();
            var usings = new List<MappedUsingDirective>();

            foreach (var directive in root.Usings)
            {
                var lineSpan = syntaxTree.GetLineSpan(directive.Span);
                var mapped = new MappedUsingDirective(
                    directive.ToString(),
                    lineSpan.StartLinePosition.Line + 1);

                var globalPrefix = globalPrefixes.FirstOrDefault(prefix =>
                    prefix.UsingStart == directive.UsingKeyword.SpanStart);
                if (globalPrefix != null)
                {
                    globalUsings.Add(mapped);
                    BlankSpan(body, globalPrefix.Start, globalPrefix.End);
                }
                else
                    usings.Add(mapped);

                BlankSpan(body, directive.Span.Start, directive.Span.End);
            }

            return new SnippetSource(new string(body), globalUsings, usings);
        }

        private static List<GlobalUsingPrefix> FindGlobalUsingPrefixes(string code)
        {
            // ParseTokens keeps lexically valid tokens that Roslyn 3.7 would otherwise
            // place in skipped trivia because global using was introduced after C# 9.
            var tokens = SyntaxFactory.ParseTokens(code).ToList();
            var prefixes = new List<GlobalUsingPrefix>();

            for (var index = 0; index + 1 < tokens.Count; index++)
            {
                var globalToken = tokens[index];
                var usingToken = tokens[index + 1];
                if (globalToken.ValueText == "global" && usingToken.ValueText == "using")
                {
                    prefixes.Add(new GlobalUsingPrefix(
                        globalToken.SpanStart,
                        globalToken.Span.End,
                        usingToken.SpanStart));
                }
            }

            return prefixes;
        }

        private static void BlankSpan(char[] text, int start, int end)
        {
            for (var index = start; index < end; index++)
            {
                if (text[index] != '\r' && text[index] != '\n')
                    text[index] = ' ';
            }
        }

        private static void AppendMappedUsings(
            StringBuilder source,
            IEnumerable<MappedUsingDirective> directives)
        {
            foreach (var directive in directives)
            {
                source.Append("#line ")
                    .Append(directive.Line)
                    .AppendLine(" \"unity_execute_csharp\"");
                source.AppendLine(directive.Text);
                source.AppendLine("#line default");
            }
        }

        private static List<MetadataReference> BuildMetadataReferences()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var references = new List<MetadataReference>();

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

                if (string.IsNullOrWhiteSpace(location) || !File.Exists(location) || !paths.Add(location))
                    return;

                try
                {
                    references.Add(MetadataReference.CreateFromFile(location));
                }
                catch
                {
                    // Some Unity-loaded assemblies expose locations Roslyn cannot consume. Skip those.
                }
            }

            AddAssembly(typeof(object).Assembly);
            AddAssembly(typeof(Enumerable).Assembly);
            AddAssembly(typeof(Task).Assembly);
            AddAssembly(typeof(GameObject).Assembly);
            AddAssembly(typeof(EditorApplication).Assembly);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                AddAssembly(assembly);

            return references;
        }

        private static ExecutionDiagnostic ToExecutionDiagnostic(Diagnostic diagnostic)
        {
            int? line = null;
            int? column = null;
            if (diagnostic.Location != null && diagnostic.Location.IsInSource)
            {
                var span = diagnostic.Location.GetMappedLineSpan();
                if (span.IsValid)
                {
                    line = span.StartLinePosition.Line + 1;
                    column = span.StartLinePosition.Character + 1;
                }
            }

            return new ExecutionDiagnostic
            {
                Id = diagnostic.Id,
                Severity = diagnostic.Severity.ToString(),
                Message = diagnostic.GetMessage(),
                Line = line,
                Column = column
            };
        }

        private static object NormalizeValue(object value)
        {
            return NormalizeValue(value, 0);
        }

        private static object NormalizeValue(object value, int depth)
        {
            if (value == null)
                return null;

            if (depth >= MaxNormalizedDepth)
                return value.ToString();

            var type = value.GetType();
            if (value is string
                || value is bool
                || value is byte
                || value is sbyte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong
                || value is float
                || value is double
                || value is decimal)
            {
                return value;
            }

            if (value is DateTime dateTime)
                return dateTime.ToString("O");

            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset.ToString("O");

            if (value is Guid guid)
                return guid.ToString("D");

            if (type.IsEnum)
                return value.ToString();

            if (value is UnityEngine.Object unityObject)
                return NormalizeUnityObject(unityObject);

            if (value is Vector2 vector2)
                return new Dictionary<string, object> { ["x"] = vector2.x, ["y"] = vector2.y };

            if (value is Vector3 vector3)
                return new Dictionary<string, object> { ["x"] = vector3.x, ["y"] = vector3.y, ["z"] = vector3.z };

            if (value is Vector4 vector4)
            {
                return new Dictionary<string, object>
                {
                    ["x"] = vector4.x,
                    ["y"] = vector4.y,
                    ["z"] = vector4.z,
                    ["w"] = vector4.w
                };
            }

            if (value is Quaternion quaternion)
            {
                return new Dictionary<string, object>
                {
                    ["x"] = quaternion.x,
                    ["y"] = quaternion.y,
                    ["z"] = quaternion.z,
                    ["w"] = quaternion.w
                };
            }

            if (value is Color color)
            {
                return new Dictionary<string, object>
                {
                    ["r"] = color.r,
                    ["g"] = color.g,
                    ["b"] = color.b,
                    ["a"] = color.a
                };
            }

            if (value is IDictionary dictionary)
            {
                var normalized = new Dictionary<string, object>();
                var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ >= MaxNormalizedItems)
                        break;

                    normalized[entry.Key?.ToString() ?? string.Empty] = NormalizeValue(entry.Value, depth + 1);
                }

                return normalized;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var normalized = new List<object>();
                foreach (var item in enumerable)
                {
                    if (normalized.Count >= MaxNormalizedItems)
                        break;

                    normalized.Add(NormalizeValue(item, depth + 1));
                }

                return normalized;
            }

            return new Dictionary<string, object>
            {
                ["type"] = type.FullName,
                ["value"] = value.ToString()
            };
        }

        private static object NormalizeUnityObject(UnityEngine.Object unityObject)
        {
            if (unityObject == null)
                return null;

            var normalized = new Dictionary<string, object>
            {
                ["type"] = unityObject.GetType().FullName,
                ["name"] = unityObject.name,
                ["instanceId"] = unityObject.GetInstanceID()
            };

            try
            {
                var assetPath = AssetDatabase.GetAssetPath(unityObject);
                if (!string.IsNullOrEmpty(assetPath))
                    normalized["assetPath"] = assetPath;
            }
            catch
            {
                // Scene objects and transient objects do not always have an AssetDatabase path.
            }

            return normalized;
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

        private static string ComputeHash(string code)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(code));
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

        private sealed class SnippetSource
        {
            public SnippetSource(
                string body,
                List<MappedUsingDirective> globalUsings,
                List<MappedUsingDirective> usings)
            {
                Body = body;
                GlobalUsings = globalUsings;
                Usings = usings;
            }

            public string Body { get; }

            public List<MappedUsingDirective> GlobalUsings { get; }

            public List<MappedUsingDirective> Usings { get; }
        }

        private sealed class MappedUsingDirective
        {
            public MappedUsingDirective(string text, int line)
            {
                Text = text;
                Line = line;
            }

            public string Text { get; }

            public int Line { get; }
        }

        private sealed class GlobalUsingPrefix
        {
            public GlobalUsingPrefix(int start, int end, int usingStart)
            {
                Start = start;
                End = end;
                UsingStart = usingStart;
            }

            public int Start { get; }

            public int End { get; }

            public int UsingStart { get; }
        }
    }
}
