using System.Collections.Generic;
using DotCraft.Editor.Protocol;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class AcpDtoSerializationTests
    {
        [Test]
        public void BoolOrObjectConverterReadsBooleanCapabilityShorthand()
        {
            var json =
                "{\"protocolVersion\":1,\"clientCapabilities\":{\"fs\":true,\"terminal\":false},\"clientInfo\":{\"name\":\"DotCraft-Unity\"}}";

            var parameters = DotCraftJson.Deserialize<InitializeParams>(json);

            Assert.That(parameters.ClientCapabilities.Fs.ReadTextFile, Is.True);
            Assert.That(parameters.ClientCapabilities.Fs.WriteTextFile, Is.True);
            Assert.That(parameters.ClientCapabilities.Terminal, Is.Null);
        }

        [Test]
        public void RuntimeToolDescriptorUsesProtocolNamesAndOmitsNulls()
        {
            var descriptor = new AcpRuntimeToolDescriptor
            {
                Namespace = "unity",
                Name = "unity_execute_csharp",
                Description = "Compile and execute C# in Unity.",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["code"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    }
                },
                AcpMethod = "_unity/execute_csharp",
                Kind = AcpToolKind.Execute
            };

            var json = DotCraftJson.Serialize(descriptor);

            Assert.That(json, Does.Contain("\"inputSchema\""));
            Assert.That(json, Does.Contain("\"acpMethod\":\"_unity/execute_csharp\""));
            Assert.That(json, Does.Contain("\"code\""));
            Assert.That(json, Does.Not.Contain("deferLoading"));
            Assert.That(json, Does.Not.Contain("approval"));
        }
    }
}
