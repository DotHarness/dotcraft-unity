using DotCraft.Editor.Settings;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class SettingsSerializationTests
    {
        [Test]
        public void LegacySettingsWithoutAgentConnectionAreNormalized()
        {
            var settings = DotCraftSettings.FromJson(
                "{\"dotCraftCommand\":\"dotcraft\",\"dotCraftArguments\":\"-acp\"}");

            Assert.That(settings.AgentConnection, Is.EqualTo(DotCraftSettings.AgentConnectionDotCraft));
            Assert.That(settings.DotCraftAppServer, Is.EqualTo(DotCraftSettings.DotCraftAppServerLocalHub));
        }

        [Test]
        public void SettingsSerializationPreservesDictionaryKeysAndOmitsNulls()
        {
            var settings = DotCraftSettings.FromJson(
                "{\"agentConnection\":\"customAcp\",\"dotCraftCommand\":\"agent\",\"dotCraftArguments\":\"--acp\",\"environmentVariables\":{\"OPENAI_API_KEY\":\"secret\"},\"mcpServers\":[{\"name\":\"local\",\"enabled\":true,\"transport\":\"stdio\",\"environmentVariables\":{\"MY_CUSTOM_KEY\":\"value\"}}]}");

            var json = settings.ToJson();

            Assert.That(json, Does.Contain("\"OPENAI_API_KEY\""));
            Assert.That(json, Does.Contain("\"MY_CUSTOM_KEY\""));
            Assert.That(json, Does.Not.Contain("\"url\": null"));
            Assert.That(json, Does.Not.Contain("\"headers\": null"));
        }
    }
}
