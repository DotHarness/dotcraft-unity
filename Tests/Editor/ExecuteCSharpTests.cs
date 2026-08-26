using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotCraft.Editor.ToolGateway;
using DotCraft.Editor.Execution;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Tests
{
    public sealed class ExecuteCSharpTests
    {
        [Test]
        public void ExecuteCSharpCompilesAndReturnsSimpleValue()
        {
            var result = Execute("return 21 + 21;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Mode, Is.EqualTo(UnityExecutionModes.Editor));
            Assert.That(result.ReturnValue, Is.EqualTo(42));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        [Test]
        public void ExecuteCSharpCanUseApiHelpersWithoutExtraUsings()
        {
            var result = Execute(
                "var type = Dcu.Type(\"UnityEngine.GameObject\"); " +
                "var members = Dcu.Members(type, \"transform\"); " +
                "return members.Any(m => m.Kind == \"property\" && m.Name == \"transform\");");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.True);
        }

        [Test]
        public void ExecuteCSharpSupportsLeadingUsingWithCommentsBlankLinesAndCrLf()
        {
            var result = Execute(
                "// Keep this comment on its original line.\r\n" +
                "using System.Text;\r\n" +
                "\r\n" +
                "return new StringBuilder(\"ordinary\").ToString();");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.EqualTo("ordinary"));
        }

        [Test]
        public void ExecuteCSharpSupportsAliasAndStaticUsings()
        {
            var result = Execute(
                "using TextBuilder = System.Text.StringBuilder;\n" +
                "using static System.Math;\n" +
                "return new TextBuilder(Abs(-3).ToString()).ToString();");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.EqualTo("3"));
        }

        [Test]
        public void ExecuteCSharpPlacesGlobalUsingsBeforeBuiltInUsings()
        {
            var result = Execute(
                "global using System.Text;\n" +
                "return new StringBuilder(\"global\").ToString();");

            Assert.That(
                result.Success,
                Is.True,
                result.ErrorMessage + " " + string.Join(" | ", result.Diagnostics.Select(d =>
                    $"{d.Id}({d.Line},{d.Column}): {d.Message}")));
            Assert.That(result.ReturnValue, Is.EqualTo("global"));
        }

        [TestCase("using var stream = new System.IO.MemoryStream(); return stream.CanRead;")]
        [TestCase("using (var stream = new System.IO.MemoryStream()) { return stream.CanRead; }")]
        public void ExecuteCSharpKeepsUsingStatementsInsideMethodBody(string code)
        {
            var result = Execute(code);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.True);
        }

        [Test]
        public void LeadingUsingAndBodyDiagnosticsMapToOriginalLinesAndColumns()
        {
            var result = Execute(
                "// original line 1\r\n" +
                "using Missing.DotCraft.Namespace;\r\n" +
                "\r\n" +
                "return MissingSymbol;");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CompilationFailed"));
            Assert.That(result.Diagnostics.Any(d => d.Line == 2), Is.True, "Using diagnostic should map to line 2.");
            Assert.That(
                result.Diagnostics.Any(d => d.Line == 4 && d.Column == 8),
                Is.True,
                "Method-body diagnostic should map to line 4, column 8.");
        }

        [TestCase("namespace Example { public static class Script { } }")]
        [TestCase("public static class Script { }")]
        [TestCase("public static object Run() { return 1; }")]
        public void ExecuteCSharpStillRejectsCompleteFileDeclarations(string code)
        {
            var result = Execute(code);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CompilationFailed"));
        }

        [Test]
        public void ExecuteCSharpCanCreateGameObjectInEditorMode()
        {
            var name = $"DotCraft ExecuteCSharp Test {Guid.NewGuid():N}";

            try
            {
                var result = Execute($"var go = new GameObject(\"{name}\"); return go;");

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(GameObject.Find(name), Is.Not.Null);
                var returned = result.ReturnValue as Dictionary<string, object>;
                Assert.That(returned, Is.Not.Null);
                Assert.That(returned["name"], Is.EqualTo(name));
                Assert.That(returned["type"], Is.EqualTo(typeof(GameObject).FullName));
            }
            finally
            {
                var created = GameObject.Find(name);
                if (created != null)
                    UnityEngine.Object.DestroyImmediate(created);
            }
        }

        [Test]
        public void InvalidCSharpReturnsCompilerDiagnostics()
        {
            var result = Execute("return ;");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CompilationFailed"));
            Assert.That(result.Diagnostics, Is.Not.Empty);
            Assert.That(result.Diagnostics.Any(d => d.Line == 1), Is.True);
        }

        [Test]
        public void PlaymodeModeFailsClearlyWhenEditorIsNotPlaying()
        {
            Assume.That(EditorApplication.isPlaying, Is.False);

            var result = WaitForResult(ExecutionRouter.Instance.ExecuteAsync(
                new ExecutionRequest(UnityExecutionEngines.CSharp, UnityExecutionModes.PlayMode, "return 1;")));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Mode, Is.EqualTo(UnityExecutionModes.PlayMode));
            Assert.That(result.ErrorCode, Is.EqualTo("UnityNotInPlayMode"));
        }

        [Test]
        public void RuntimeToolCatalogDiscoversExecuteCSharpWithExecuteScope()
        {
            var tool = RuntimeToolCatalog.Discover().Tools.Single(t =>
                t.Source == RuntimeToolSource.Builtin && t.Descriptor.Name == "unity_execute_csharp");

            Assert.That(tool.Source, Is.EqualTo(RuntimeToolSource.Builtin));
            Assert.That(tool.Descriptor.Namespace, Is.EqualTo("unity"));
            Assert.That(tool.Descriptor.AcpMethod, Is.EqualTo("_unity/execute_csharp"));
            Assert.That(tool.Descriptor.Kind, Is.EqualTo(AcpToolKind.Execute));
        }

        [Test]
        public void ExecuteCSharpRunsAScriptFromAProjectRelativePath()
        {
            var relativePath = WriteScript("return 1 + 2;");

            var result = ExecuteScript(relativePath);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.EqualTo(3));
        }

        [Test]
        public void ExecuteCSharpPassesArgsToTheScript()
        {
            var relativePath = WriteScript("return (int)Args[\"count\"] * 2;");

            var result = ExecuteScript(relativePath, new JObject { ["count"] = 21 });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.EqualTo(42));
        }

        [Test]
        public void ExecuteCSharpRejectsAScriptPathOutsideTheProjectRoot()
        {
            var result = ExecuteScript("../outside.cs");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("InvalidScriptPath"));
        }

        [Test]
        public void ExecuteCSharpReportsAMissingScript()
        {
            var result = ExecuteScript(ScriptDirectory + "/missing.cs");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("ScriptNotFound"));
        }

        [Test]
        public void ExecuteCSharpRequiresExactlyOneOfCodeAndPath()
        {
            Assert.That(Execute(null).ErrorCode, Is.EqualTo("EmptyCode"));
            Assert.That(
                WaitForResult(ExecutionRouter.Instance.ExecuteAsync(new ExecutionRequest(
                    UnityExecutionEngines.CSharp,
                    UnityExecutionModes.Editor,
                    "return 1;",
                    WriteScript("return 2;")))).ErrorCode,
                Is.EqualTo("InvalidArguments"));
        }

        [TearDown]
        public void DeleteScripts()
        {
            var directory = Path.Combine(ProjectRoot, ScriptDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private const string ScriptDirectory = "Temp/DotCraftExecuteCSharpTests";

        private static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        private static string WriteScript(string code)
        {
            var relativePath = $"{ScriptDirectory}/{Guid.NewGuid():N}.cs";
            var fullPath = Path.Combine(ProjectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, code);
            return relativePath;
        }

        private static ExecutionResult ExecuteScript(string relativePath, JObject args = null)
        {
            return WaitForResult(ExecutionRouter.Instance.ExecuteAsync(new ExecutionRequest(
                UnityExecutionEngines.CSharp,
                UnityExecutionModes.Editor,
                null,
                relativePath,
                args)));
        }

        private static ExecutionResult Execute(string code)
        {
            return WaitForResult(ExecutionRouter.Instance.ExecuteAsync(
                new ExecutionRequest(UnityExecutionEngines.CSharp, UnityExecutionModes.Editor, code)));
        }

        private static T WaitForResult<T>(System.Threading.Tasks.Task<T> task, int timeoutMilliseconds = 5000)
        {
            if (!task.Wait(timeoutMilliseconds))
                Assert.Fail($"Timed out waiting for task after {timeoutMilliseconds} ms.");
            return task.GetAwaiter().GetResult();
        }
    }
}
