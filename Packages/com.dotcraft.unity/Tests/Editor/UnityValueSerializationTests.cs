using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DotCraft.Editor.Tests
{
    public sealed class UnityValueSerializationTests
    {
        [Test]
        public void NestedAnonymousResultsPreserveUnityValueFields()
        {
            var value = UnityValueNormalizer.Normalize(new { position = new Vector3Int(1, 2, 3), bounds = new Bounds(Vector3.one, Vector3.one * 2) });
            var json = JObject.FromObject(value);
            Assert.That((int)json["position"]["z"], Is.EqualTo(3));
            Assert.That((float)json["bounds"]["size"]["x"], Is.EqualTo(2));
        }

        [Test]
        public void DestroyedUnityObjectSerializesAsNull()
        {
            var gameObject = new GameObject("serialization test");
            Object.DestroyImmediate(gameObject);
            var settings = new JsonSerializerSettings { Converters = { new UnityJsonConverter() } };
            Assert.That(JsonConvert.SerializeObject(gameObject, settings), Is.EqualTo("null"));
            Assert.That(UnityValueNormalizer.Normalize(gameObject), Is.Null);
        }

        [Test]
        public void NormalizationBoundsCollectionsAndRecursiveObjects()
        {
            var rows = new List<int>();
            for (var i = 0; i < 100; i++) rows.Add(i);
            var result = UnityValueNormalizer.Normalize(rows) as List<object>;
            Assert.That(result.Count, Is.EqualTo(32));
            var recursive = new Recursive();
            recursive.Self = recursive;
            Assert.DoesNotThrow(() => JsonConvert.SerializeObject(UnityValueNormalizer.Normalize(recursive)));
        }

        sealed class Recursive { public Recursive Self { get; set; } }
    }
}
