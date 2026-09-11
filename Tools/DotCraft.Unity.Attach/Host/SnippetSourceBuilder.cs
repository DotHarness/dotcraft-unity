using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
namespace DotCraft.Unity
{
    /// <summary>Builds target-compatible snippets while preserving source diagnostics.</summary>
    public static class SnippetSourceBuilder
    {
        public static string Build(
            string className,
            string code,
            string sourceLabel,
            bool asynchronous = true,
            string runtimeNamespace = "DotCraft.Unity",
            string? generatedNamespace = null)
        {
            generatedNamespace = generatedNamespace ?? runtimeNamespace + ".Execution.Generated";
            sourceLabel = sourceLabel.Replace("\\", "/").Replace("\"", "\\\"");
            var snippet = ParseSnippet(code);
            var source = new StringBuilder();

            AppendMappedUsings(source, snippet.GlobalUsings, sourceLabel);
            source.AppendLine("using System;");
            source.AppendLine("using System.Linq;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("using UnityEngine;");
            source.AppendLine("using UnityEditor;");
            source.AppendLine("using System.Threading;");
            source.AppendLine("using System.Threading.Tasks;");
            source.AppendLine("using Newtonsoft.Json.Linq;");
            source.Append("using ").Append(runtimeNamespace).AppendLine(";");
            AppendMappedUsings(source, snippet.Usings, sourceLabel);
            source.AppendLine();
            source.Append("namespace ").AppendLine(generatedNamespace);
            source.AppendLine("{");
            source.Append("    public static class ").AppendLine(className);
            source.AppendLine("    {");
            source.Append("        public static ").Append(asynchronous ? "async Task<object>" : "object")
                .AppendLine(" Run(JObject Args, UnityExecutionContext ctx, CancellationToken cancellationToken)");
            source.AppendLine("        {");
            source.Append("#line 1 \"").Append(sourceLabel).AppendLine("\"");
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
            IEnumerable<MappedUsingDirective> directives,
            string sourceLabel)
        {
            foreach (var directive in directives)
            {
                source.Append("#line ")
                    .Append(directive.Line)
                    .Append(" \"")
                    .Append(sourceLabel)
                    .AppendLine("\"");
                source.AppendLine(directive.Text);
                source.AppendLine("#line default");
            }
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
