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
                Name = "unity_scene_query",
                Description = "Query scene hierarchy.",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["includeComponents"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean"
                        }
                    }
                },
                AcpMethod = "_unity/scene_query",
                Kind = AcpToolKind.Unity
            };

            var json = DotCraftJson.Serialize(descriptor);

            Assert.That(json, Does.Contain("\"inputSchema\""));
            Assert.That(json, Does.Contain("\"acpMethod\":\"_unity/scene_query\""));
            Assert.That(json, Does.Contain("\"includeComponents\""));
            Assert.That(json, Does.Not.Contain("deferLoading"));
            Assert.That(json, Does.Not.Contain("approval"));
        }
    }
}
