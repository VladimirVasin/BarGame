using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeOcclusionRegistryTests
    {
        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanupObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (cleanupObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        cleanupObjects[index]);
                }
            }

            cleanupObjects.Clear();
        }

        [Test]
        public void Register_ExposesStableGroupContract()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            Renderer first = CreateRenderer("Rail A");
            Renderer second = CreateRenderer("Rail B");

            HomeOccluderGroup group = registry.Register(
                "balcony.outer",
                HomeOccluderKind.VisualRail,
                0.16f,
                first,
                second);

            Assert.That(registry.Groups, Has.Count.EqualTo(1));
            Assert.That(registry.Groups[0], Is.SameAs(group));
            Assert.That(group.Id, Is.EqualTo("balcony.outer"));
            Assert.That(
                group.Kind,
                Is.EqualTo(HomeOccluderKind.VisualRail));
            Assert.That(
                group.MinimumVisibility,
                Is.EqualTo(0.16f).Within(0.0001f));
            Assert.That(
                group.Renderers,
                Is.EquivalentTo(new[] { first, second }));
            Assert.That(
                registry.TryGetGroup(
                    "balcony.outer",
                    out HomeOccluderGroup resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(group));
            Assert.That(
                registry.TryGetGroup(
                    "BALCONY.OUTER",
                    out _),
                Is.False,
                "Stable group ids are ordinal and case-sensitive.");
        }

        [Test]
        public void Register_RejectsDuplicateIdsWithoutReplacingGroup()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            Renderer original = CreateRenderer("Original");
            Renderer replacement = CreateRenderer("Replacement");
            HomeOccluderGroup group = registry.Register(
                "furniture.table",
                HomeOccluderKind.FurnitureBlocker,
                0.24f,
                original);

            Assert.That(
                () => registry.Register(
                    "furniture.table",
                    HomeOccluderKind.SoftDecoration,
                    0.30f,
                    replacement),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(registry.Groups, Has.Count.EqualTo(1));
            Assert.That(registry.Groups[0], Is.SameAs(group));
            Assert.That(group.Renderers, Is.EqualTo(new[] { original }));
        }

        [Test]
        public void AddRenderers_AppendsAndDeduplicatesWithinGroup()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            Renderer first = CreateRenderer("Table Top");
            Renderer second = CreateRenderer("Table Leg");
            HomeOccluderGroup group = registry.Register(
                "furniture.table",
                HomeOccluderKind.FurnitureBlocker,
                0.24f,
                first);

            registry.AddRenderers(
                group.Id,
                first,
                second,
                second);

            Assert.That(group.Renderers, Has.Count.EqualTo(2));
            Assert.That(
                group.Renderers,
                Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public void GameObjectOverloads_CollectInactiveChildRenderers()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            GameObject visualRoot = CreateObject("Sofa Visual Root");
            Renderer rootRenderer =
                visualRoot.AddComponent<SpriteRenderer>();
            GameObject child = CreateObject("Sofa Cushion");
            child.transform.SetParent(visualRoot.transform, false);
            Renderer childRenderer =
                child.AddComponent<SpriteRenderer>();
            child.SetActive(false);

            HomeOccluderGroup group = registry.Register(
                "furniture.sofa",
                HomeOccluderKind.FurnitureBlocker,
                0.22f,
                visualRoot);

            Assert.That(group.Renderers, Has.Count.EqualTo(2));
            Assert.That(
                group.Renderers,
                Is.EquivalentTo(
                    new[] { rootRenderer, childRenderer }));
        }

        [Test]
        public void GameObjectOverloads_AccumulateEverySourceObject()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            GameObject frame = CreateObject("Bed Frame");
            Renderer frameRenderer =
                frame.AddComponent<SpriteRenderer>();
            GameObject mattress = CreateObject("Bed Mattress");
            Renderer mattressRenderer =
                mattress.AddComponent<SpriteRenderer>();

            HomeOccluderGroup group = registry.Register(
                "furniture.bed",
                HomeOccluderKind.FurnitureBlocker,
                0.23f,
                frame,
                mattress);

            Assert.That(group.Renderers, Has.Count.EqualTo(2));
            Assert.That(
                group.Renderers,
                Is.EquivalentTo(
                    new[] { frameRenderer, mattressRenderer }));
        }

        [Test]
        public void Renderer_CannotBelongToTwoGroups()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            Renderer owned = CreateRenderer("Shared Renderer");
            Renderer unowned = CreateRenderer("Unowned Renderer");
            registry.Register(
                "first",
                HomeOccluderKind.StructuralCutaway,
                0.18f,
                owned);
            HomeOccluderGroup second = registry.Register(
                "second",
                HomeOccluderKind.SoftDecoration,
                0.30f,
                unowned);

            Assert.That(
                () => registry.AddRenderers(
                    second.Id,
                    CreateRenderer("Candidate"),
                    owned),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                second.Renderers,
                Is.EqualTo(new[] { unowned }),
                "A rejected batch must not partially mutate the group.");
        }

        [TestCase(-0.001f)]
        [TestCase(1.001f)]
        [TestCase(float.NaN)]
        public void Register_RejectsInvalidMinimumVisibility(
            float minimumVisibility)
        {
            HomeOcclusionRegistry registry = CreateRegistry();

            Assert.That(
                () => registry.Register(
                    "invalid.minimum",
                    HomeOccluderKind.FurnitureBlocker,
                    minimumVisibility,
                    CreateRenderer("Invalid Minimum")),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(registry.Groups, Is.Empty);
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void Register_AcceptsMinimumVisibilityBoundaries(
            float minimumVisibility)
        {
            HomeOcclusionRegistry registry = CreateRegistry();

            HomeOccluderGroup group = registry.Register(
                $"boundary.{minimumVisibility}",
                HomeOccluderKind.InteractiveProtected,
                minimumVisibility,
                CreateRenderer("Boundary Renderer"));

            Assert.That(
                group.MinimumVisibility,
                Is.EqualTo(minimumVisibility));
        }

        [Test]
        public void Registry_RejectsInvalidIdsAndRendererSources()
        {
            HomeOcclusionRegistry registry = CreateRegistry();
            Renderer valid = CreateRenderer("Valid Renderer");

            Assert.That(
                () => registry.Register(
                    "invalid.kind",
                    (HomeOccluderKind)999,
                    0.2f,
                    valid),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => registry.Register(
                    " ",
                    HomeOccluderKind.VisualRail,
                    0.2f,
                    valid),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => registry.Register(
                    "null.renderers",
                    HomeOccluderKind.VisualRail,
                    0.2f,
                    (Renderer[])null),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => registry.Register(
                    "empty.renderers",
                    HomeOccluderKind.VisualRail,
                    0.2f,
                    Array.Empty<Renderer>()),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => registry.Register(
                    "null.renderer",
                    HomeOccluderKind.VisualRail,
                    0.2f,
                    new Renderer[] { null }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => registry.AddRenderers(
                    "missing.group",
                    valid),
                Throws.TypeOf<KeyNotFoundException>());
            Assert.That(registry.Groups, Is.Empty);
        }

        private HomeOcclusionRegistry CreateRegistry()
        {
            return CreateObject("Home Occlusion Registry")
                .AddComponent<HomeOcclusionRegistry>();
        }

        private Renderer CreateRenderer(string name)
        {
            return CreateObject(name)
                .AddComponent<SpriteRenderer>();
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            cleanupObjects.Add(result);
            return result;
        }
    }
}
