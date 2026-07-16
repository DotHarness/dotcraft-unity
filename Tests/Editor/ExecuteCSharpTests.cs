using System;
using System.Collections.Generic;
using System.Linq;
using DotCraft.Editor.ToolGateway;
using DotCraft.Editor.Execution;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
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
