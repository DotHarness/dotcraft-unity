using System;
using System.Linq;
using DotCraft.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotCraft.Editor.Tests
{
    public sealed class ApiTests
    {
        [Test]
        public void TypeFindsLoadedTypesAndReportsMissingTypes()
        {
            Assert.That(Dcu.Type("UnityEngine.GameObject"), Is.EqualTo(typeof(GameObject)));
            Assert.That(Dcu.Type("Definitely.Missing.Type", throwIfMissing: false), Is.Null);

            var ex = Assert.Throws<InvalidOperationException>(() => Dcu.Type("Definitely.Missing.Type"));
            Assert.That(ex.Message, Does.Contain("Type not found"));
        }

        [Test]
        public void ComponentsFindsInactiveSceneComponents()
        {
            var go = new GameObject("DotCraft Api Components Test");
            go.SetActive(false);
            var collider = go.AddComponent<BoxCollider>();

            try
            {
                var activeOnly = Dcu.Components(typeof(BoxCollider), includeInactive: false);
                var includingInactive = Dcu.Components(typeof(BoxCollider).FullName, includeInactive: true);

                Assert.That(activeOnly, Does.Not.Contain(collider));
                Assert.That(includingInactive, Does.Contain(collider));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ReflectionGetSetCallAndMembersWorkOnPrivateMembers()
        {
            var target = new ReflectionTarget();

            Assert.That(Dcu.Get(target, "_value"), Is.EqualTo(7));
            Dcu.Set(target, "_value", 12);
            Assert.That(Dcu.Get(target, "Value"), Is.EqualTo(12));
            Assert.That(Dcu.Call(target, "Scale", 3), Is.EqualTo(36));

            var members = Dcu.Members(typeof(ReflectionTarget), "Scale");
            Assert.That(members.Single().Kind, Is.EqualTo("method"));
        }

        [Test]
        public void ReflectionReportsMissingMemberClearly()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Dcu.Get(new ReflectionTarget(), "Missing"));

            Assert.That(ex.Message, Does.Contain("Member not found"));
            Assert.That(ex.Message, Does.Contain("Missing"));
        }

        private sealed class ReflectionTarget
        {
            private int _value = 7;

            private int Value => _value;

            private int Scale(int multiplier)
            {
                return _value * multiplier;
            }
        }
    }
}
