using System.Linq;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class UnityToolParameterTests
    {
        [Test]
        public void BuiltinToolsUseStableUnityExtensionMethods()
        {
            AssertBuiltinTool("unity_execute_csharp", "_unity/execute_csharp");
        }

        private static void AssertBuiltinTool(string name, string acpMethod)
        {
            var snapshot = RuntimeToolCatalog.Discover();
            var tool = snapshot.Tools.Single(t =>
                t.Source == RuntimeToolSource.Builtin && t.Descriptor.Name == name);

            Assert.That(tool.Descriptor.AcpMethod, Is.EqualTo(acpMethod));
        }
    }
}
