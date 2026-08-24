using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityWetSurfaceTests
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");

        // A colour read back out of a MaterialPropertyBlock is not the
        // colour that was written: the native round-trip drifts by about
        // one ULP (0.31f returns as 0.309999973), which NUnit's exact
        // struct equality rejects while both sides still print
        // identically. The drift is already there when the appearance
        // writes the authored tint, before any weather runs, so it says
        // nothing about the wetness path. White is the one value that
        // survives it bit-for-bit, which is why the plain surface case
        // could get away with exact equality for so long. Assert the
        // contract that matters instead: the authored tint comes back.
        private const float TintTolerance = 1e-5f;

        private static void AssertTint(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(TintTolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(TintTolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(TintTolerance));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(TintTolerance));
        }

        [SetUp]
        public void SetUp()
        {
            CityWetSurfaceRegistry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CityWetSurfaceRegistry.ResetForTests();
        }

        [Test]
        public void Advance_WetsQuicklyAndDriesWithAVisibleLag()
        {
            float wetAfterOneSecond = CityWetSurfaceRules.Advance(
                0f,
                1f,
                1f);
            float dryAfterOneSecond = CityWetSurfaceRules.Advance(
                1f,
                0f,
                1f);

            Assert.That(
                wetAfterOneSecond,
                Is.EqualTo(CityWetSurfaceRules.WettingRatePerSecond)
                    .Within(0.0001f));
            Assert.That(
                dryAfterOneSecond,
                Is.EqualTo(1f - CityWetSurfaceRules.DryingRatePerSecond)
                    .Within(0.0001f));
            Assert.That(
                wetAfterOneSecond,
                Is.GreaterThan(1f - dryAfterOneSecond));
        }

        [Test]
        public void Registry_AccumulatesSubMillisecondDryingSteps()
        {
            CityWetSurfaceRegistry.SetImmediate(1f);
            for (int frame = 1; frame <= 60; frame++)
            {
                CityWetSurfaceRegistry.Advance(
                    0f,
                    1f / 60f,
                    frame / 60d);
            }

            Assert.That(
                CityWetSurfaceRegistry.CurrentWetness,
                Is.EqualTo(1f - CityWetSurfaceRules.DryingRatePerSecond)
                    .Within(0.0002f));
        }

        [Test]
        public void SceneResume_PreservesFilmAndAccountsForElapsedGameTime()
        {
            CityWetSurfaceRegistry.InitializeOrResume(1f, 100d);
            CityWetSurfaceRegistry.InitializeOrResume(0f, 110d);

            Assert.That(
                CityWetSurfaceRegistry.CurrentWetness,
                Is.EqualTo(
                    1f -
                    (CityWetSurfaceRules.DryingRatePerSecond * 10f))
                    .Within(0.0001f));
            Assert.That(
                CityWetSurfaceRegistry.CurrentWetness,
                Is.GreaterThan(0f));
        }

        [Test]
        public void RoadRecipe_DarkensAndRaisesSmoothnessWithRain()
        {
            CityWetSurfaceSample dry = CityWetSurfaceRules.Evaluate(
                CityWetSurfaceKind.Road,
                0f);
            CityWetSurfaceSample wet = CityWetSurfaceRules.Evaluate(
                CityWetSurfaceKind.Road,
                1f);

            Assert.That(dry.Tint, Is.EqualTo(Color.white));
            Assert.That(
                dry.Smoothness,
                Is.EqualTo(CityExteriorAppearance.RoadSmoothness));
            Assert.That(wet.Tint.grayscale, Is.LessThan(0.7f));
            Assert.That(wet.Smoothness, Is.GreaterThan(0.6f));
            CityWetSurfaceSample dryPuddle = CityWetSurfaceRules.Evaluate(
                CityWetSurfaceKind.Puddle,
                0f);
            Assert.That(dryPuddle.Tint, Is.EqualTo(dry.Tint));
            Assert.That(dryPuddle.Smoothness, Is.EqualTo(dry.Smoothness));
        }

        [Test]
        public void PuddlePlanner_IsDeterministicAndKeepsPatchesBounded()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                43819);
            CityStreetSurfacePlan streets =
                CityStreetSurfacePlanner.Create(layout);

            var first = CityPuddlePlanner.Create(streets, layout.Seed);
            var second = CityPuddlePlanner.Create(streets, layout.Seed);

            Assert.That(first, Is.Not.Empty);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(CityPuddlePlanner.MaximumPuddleCount));
            Assert.That(second.Count, Is.EqualTo(first.Count));
            float roadMinimumX = float.PositiveInfinity;
            float roadMaximumX = float.NegativeInfinity;
            float roadMinimumZ = float.PositiveInfinity;
            float roadMaximumZ = float.NegativeInfinity;
            for (int index = 0; index < streets.StreetGeometry.Count; index++)
            {
                Vector3 center = streets.StreetGeometry[index].Center;
                roadMinimumX = Mathf.Min(roadMinimumX, center.x);
                roadMaximumX = Mathf.Max(roadMaximumX, center.x);
                roadMinimumZ = Mathf.Min(roadMinimumZ, center.z);
                roadMaximumZ = Mathf.Max(roadMaximumZ, center.z);
            }

            float puddleMinimumX = float.PositiveInfinity;
            float puddleMaximumX = float.NegativeInfinity;
            float puddleMinimumZ = float.PositiveInfinity;
            float puddleMaximumZ = float.NegativeInfinity;
            for (int index = 0; index < first.Count; index++)
            {
                Assert.That(
                    second[index].Center,
                    Is.EqualTo(first[index].Center));
                Assert.That(
                    second[index].Rotation,
                    Is.EqualTo(first[index].Rotation));
                Assert.That(second[index].Size, Is.EqualTo(first[index].Size));
                Assert.That(
                    first[index].Size.y,
                    Is.EqualTo(CityPuddlePlanner.Thickness));
                Assert.That(first[index].Size.x, Is.GreaterThan(0f));
                Assert.That(first[index].Size.z, Is.GreaterThan(0f));
                puddleMinimumX = Mathf.Min(
                    puddleMinimumX,
                    first[index].Center.x);
                puddleMaximumX = Mathf.Max(
                    puddleMaximumX,
                    first[index].Center.x);
                puddleMinimumZ = Mathf.Min(
                    puddleMinimumZ,
                    first[index].Center.z);
                puddleMaximumZ = Mathf.Max(
                    puddleMaximumZ,
                    first[index].Center.z);
                Assert.That(
                    IsGroundedOnAStreet(first[index], streets),
                    Is.True,
                    $"Puddle {index} left its source road surface.");
            }

            Assert.That(
                puddleMinimumX,
                Is.LessThan(Mathf.Lerp(roadMinimumX, roadMaximumX, 0.35f)));
            Assert.That(
                puddleMaximumX,
                Is.GreaterThan(Mathf.Lerp(roadMinimumX, roadMaximumX, 0.65f)));
            Assert.That(
                puddleMinimumZ,
                Is.LessThan(Mathf.Lerp(roadMinimumZ, roadMaximumZ, 0.35f)));
            Assert.That(
                puddleMaximumZ,
                Is.GreaterThan(Mathf.Lerp(roadMinimumZ, roadMaximumZ, 0.65f)));
        }

        [Test]
        public void RegisteredRoad_UsesPropertyBlocksAndRestoresDryRecipe()
        {
            GameObject owner = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            try
            {
                Renderer renderer = owner.GetComponent<Renderer>();
                Material sharedBefore = RuntimePrimitiveFactory.DefaultMaterial;
                CityExteriorAppearance.ApplyRoadSurface(renderer);

                CityWetSurfaceRegistry.SetImmediate(1f);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Color wetTint = properties.GetColor(BaseColorId);
                float wetSmoothness = properties.GetFloat(SmoothnessId);

                Assert.That(renderer.sharedMaterial, Is.SameAs(sharedBefore));
                Assert.That(wetTint.grayscale, Is.LessThan(0.7f));
                Assert.That(wetSmoothness, Is.GreaterThan(0.6f));
                Assert.That(
                    CityWetSurfaceRegistry.RegisteredSurfaceCount,
                    Is.EqualTo(1));

                CityWetSurfaceRegistry.SetImmediate(0f);
                renderer.GetPropertyBlock(properties);
                AssertTint(
                    properties.GetColor(BaseColorId),
                    Color.white);
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(CityExteriorAppearance.RoadSmoothness)
                        .Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void CustomGroundTint_SurvivesWetAndDryWeather()
        {
            GameObject owner = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            Color authoredTint = new Color(0.31f, 0.22f, 0.14f, 1f);
            try
            {
                Renderer renderer = owner.GetComponent<Renderer>();
                CityExteriorAppearance.ApplyGroundSurface(
                    renderer,
                    authoredTint);

                CityWetSurfaceRegistry.SetImmediate(1f);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Color wetTint = properties.GetColor(BaseColorId);
                Assert.That(wetTint.r, Is.LessThan(authoredTint.r));
                Assert.That(wetTint.g, Is.LessThan(authoredTint.g));
                Assert.That(wetTint.b, Is.LessThan(authoredTint.b));

                CityWetSurfaceRegistry.SetImmediate(0f);
                renderer.GetPropertyBlock(properties);
                AssertTint(
                    properties.GetColor(BaseColorId),
                    authoredTint);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void Register_PrunesDestroyedSurfacesAndRefreshesTheirKind()
        {
            GameObject stale = GameObject.CreatePrimitive(PrimitiveType.Quad);
            CityWetSurfaceRegistry.Register(
                stale.GetComponent<Renderer>(),
                CityWetSurfaceKind.Road);
            Object.DestroyImmediate(stale);

            GameObject current = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                Renderer renderer = current.GetComponent<Renderer>();
                CityWetSurfaceRegistry.Register(
                    renderer,
                    CityWetSurfaceKind.Road);
                CityWetSurfaceRegistry.Register(
                    renderer,
                    CityWetSurfaceKind.Sidewalk);
                CityWetSurfaceRegistry.SetImmediate(1f);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                CityWetSurfaceSample sidewalk = CityWetSurfaceRules.Evaluate(
                    CityWetSurfaceKind.Sidewalk,
                    1f);
                Assert.That(
                    CityWetSurfaceRegistry.RegisteredSurfaceCount,
                    Is.EqualTo(1));
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(sidewalk.Smoothness).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(current);
            }
        }

        [Test]
        public void ReRegisteringAWetSurface_DoesNotBakeWetTintIntoDryState()
        {
            GameObject owner = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            try
            {
                Renderer renderer = owner.GetComponent<Renderer>();
                CityExteriorAppearance.ApplyRoadSurface(renderer);
                CityWetSurfaceRegistry.SetImmediate(1f);

                CityWetSurfaceRegistry.Register(
                    renderer,
                    CityWetSurfaceKind.Sidewalk);
                CityWetSurfaceRegistry.SetImmediate(0f);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor(BaseColorId),
                    Is.EqualTo(Color.white));
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(CityExteriorAppearance.SidewalkSmoothness)
                        .Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static bool IsGroundedOnAStreet(
            RuntimeOrientedBox puddle,
            CityStreetSurfacePlan streets)
        {
            for (int index = 0;
                 index < streets.StreetGeometry.Count;
                 index++)
            {
                RuntimeOrientedBox street = streets.StreetGeometry[index];
                if (street.Rotation != puddle.Rotation ||
                    !street.TrySampleTop(puddle.Center, out float topY))
                {
                    continue;
                }

                float expectedCenterY = topY + CityPuddlePlanner.SurfaceOffset;
                if (Mathf.Abs(expectedCenterY - puddle.Center.y) < 0.001f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
