using DotCraft.Editor.Extensions;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class UnityToolParameterTests
    {
        [Test]
        public void SceneQueryAcceptsMissingOptionalParameters()
        {
            var result = UnitySceneHandlers.HandleSceneQuery(new JObject()).Result;
            var json = DotCraftJson.Serialize(result);

            Assert.That(json, Does.Contain("\"objects\""));
        }

        [Test]
        public void ConsoleLogsParseTypesAndLimitFromJTokenParameters()
        {
            var parameters = JObject.Parse("{\"types\":[\"error\",\"warning\"],\"limit\":1}");

            var result = UnityEditorHandlers.HandleGetConsoleLogs(parameters).Result;
            var json = DotCraftJson.Serialize(result);

            Assert.That(json, Does.Contain("\"logs\""));
        }
    }
}
