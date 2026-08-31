using System.Collections.Generic;
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
        public void PuddlePlanner_PoolsOnlyOnTheLevelOpenPrecincts()
        {
            // The blueprint overload, not the legacy two-argument one:
            // only this city has the yards, the cemetery terrace and
            // the church ground that this planner pools on.
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            var first = CityPuddlePlanner.CreateOpenGround(
                layout,
                layout.Seed);
            var second = CityPuddlePlanner.CreateOpenGround(
                layout,
                layout.Seed);

            Assert.That(first, Is.Not.Empty);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityPuddlePlanner.MaximumOpenGroundPuddleCount));
            Assert.That(second.Count, Is.EqualTo(first.Count));

            var levelAreas = new HashSet<string>();
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                levelAreas.Add(layout.OpenAreaAccesses[index].AreaId);
            }

            for (int index = 0; index < first.Count; index++)
            {
                RuntimeOrientedBox patch = first[index];
                Assert.That(second[index].Center, Is.EqualTo(patch.Center));
                Assert.That(second[index].Size, Is.EqualTo(patch.Size));
                Assert.That(
                    patch.Rotation,
                    Is.EqualTo(Quaternion.identity),
                    "Level ground has no direction to align a pool to.");
                Assert.That(
                    patch.Size.y,
                    Is.EqualTo(CityPuddlePlanner.Thickness));

                // Every pool must sit inside one level precinct cell,
                // clear of the edge where the terrain skin starts
                // ramping toward its neighbour.
                bool insideALevelCell = false;
                for (int s = 0; s < layout.Surfaces.Count; s++)
                {
                    CitySurfaceDescriptor surface = layout.Surfaces[s];
                    if (!levelAreas.Contains(surface.AreaId) ||
                        surface.IsWater ||
                        surface.Kind == CitySurfaceKind.Beach ||
                        surface.Kind == CitySurfaceKind.BuildableGround ||
                        surface.Kind == CitySurfaceKind.ParkGround)
                    {
                        continue;
                    }

                    Rect bounds = surface.WorldBounds;
                    if (patch.Center.x - (patch.Size.x * 0.5f) <
                            bounds.xMin ||
                        patch.Center.x + (patch.Size.x * 0.5f) >
                            bounds.xMax ||
                        patch.Center.z - (patch.Size.z * 0.5f) <
                            bounds.yMin ||
                        patch.Center.z + (patch.Size.z * 0.5f) >
                            bounds.yMax)
                    {
                        continue;
                    }

                    Assert.That(
                        patch.Center.y,
                        Is.EqualTo(
                                surface.PhysicalTopY +
                                CityPuddlePlanner.SurfaceOffset)
                            .Within(1e-4f),
                        $"Pool {index} does not lie on its own ground.");
                    insideALevelCell = true;
                    break;
                }

                Assert.That(
                    insideALevelCell,
                    Is.True,
                    $"Pool {index} is not inside a level precinct cell.");
            }
        }

        /// <summary>
        /// The fringe yards are terrain since the landscape pass, and a slab
        /// planned on their datum floated 1.8 m over one and drowned 1.5 m
        /// under another. Handed the yard plan, the planner leaves them dry
        /// and keeps pooling on the cemetery terrace and the church ground.
        /// </summary>
        [Test]
        public void PuddlePlanner_LeavesTheTerrainYardsDry()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityFringeYardPlan yards = CityFringeYardPlanner.Create(
                layout,
                CityMountainBoundaryPlanner.Create(layout));
            Assert.That(
                yards.IsEnabled,
                Is.True,
                "The default city has fringe yards.");

            IReadOnlyList<RuntimeOrientedBox> pools =
                CityPuddlePlanner.CreateOpenGround(
                    layout,
                    layout.Seed,
                    yards);
            Assert.That(
                pools,
                Is.Not.Empty,
                "The cemetery terrace and the church ground still pool.");
            for (int index = 0; index < pools.Count; index++)
            {
                Vector3 center = pools[index].Center;
                for (int yard = 0; yard < yards.Yards.Count; yard++)
                {
                    Assert.That(
                        yards.Yards[yard].AreaBounds.Contains(
                            new Vector2(center.x, center.z)),
                        Is.False,
                        $"Pool {index} lies on terrain yard " +
                        $"{yards.Yards[yard].StableId}.");
                }

                // And the plain yards: their skin is the terrain
                // model's bilinear sheet, and a pool may only lie
                // where that sheet lies on the datum. Sampled the way
                // the world builder lays the ground.
                Assert.That(
                    CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        new Vector2(center.x, center.z),
                        out float skinTop,
                        out CitySurfaceDescriptor ground),
                    Is.True,
                    $"Pool {index} stands over no ground surface.");
                if (ground.Kind == CitySurfaceKind.OpenGround)
                {
                    for (int corner = 0; corner < 5; corner++)
                    {
                        Vector2 sample = new Vector2(center.x, center.z);
                        if (corner > 0)
                        {
                            sample.x += ((corner & 1) == 0 ? -0.5f : 0.5f) *
                                pools[index].Size.x;
                            sample.y += ((corner & 2) == 0 ? -0.5f : 0.5f) *
                                pools[index].Size.z;
                        }

                        skinTop = CityTerrainSurfacePlan.SampleTop(
                            layout,
                            ground,
                            sample);
                        Assert.That(
                            center.y - skinTop,
                            Is.EqualTo(CityPuddlePlanner.SurfaceOffset)
                                .Within(0.0035f),
                            $"Pool {index} floats or drowns at {sample}.");
                    }
                }
            }
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

        /// <summary>
        /// Nothing may stand over a gutter puddle. The planner insets its
        /// patches from the STREET box, and at an intersection square that
        /// box runs the full right of way - its edge is under the pavement
        /// slab, 60 mm up - so half the city's puddles lay buried under
        /// the kerb; a graded block's slab can likewise pass under a flat
        /// intersection square. Every surviving patch must have clear air
        /// over its sheet at its centre and its four corners.
        /// </summary>
        [Test]
        public void PuddlePlanner_KeepsEveryGutterPatchClearOfCover()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityStreetSurfacePlan streets =
                CityStreetSurfacePlanner.Create(layout);
            IReadOnlyList<RuntimeOrientedBox> puddles =
                CityPuddlePlanner.Create(streets, layout.Seed);
            Assert.That(
                puddles.Count,
                Is.GreaterThanOrEqualTo(20),
                "The cover rule must not starve the city of puddles.");

            var surfaces = new List<RuntimeOrientedBox>();
            surfaces.AddRange(streets.StreetGeometry);
            surfaces.AddRange(streets.SidewalkGeometry);
            surfaces.AddRange(streets.CrosswalkMarkingGeometry);
            surfaces.AddRange(streets.CenterMarkingGeometry);
            for (int index = 0; index < puddles.Count; index++)
            {
                RuntimeOrientedBox puddle = puddles[index];
                for (int corner = 0; corner < 5; corner++)
                {
                    Vector3 local = corner == 0
                        ? Vector3.zero
                        : new Vector3(
                            ((corner & 1) == 0 ? -0.5f : 0.5f) * puddle.Size.x,
                            0f,
                            ((corner & 2) == 0 ? -0.5f : 0.5f) * puddle.Size.z);
                    Vector3 point = puddle.Center + puddle.Rotation * local;
                    for (int surface = 0; surface < surfaces.Count; surface++)
                    {
                        if (surfaces[surface].TrySampleTop(point, out float top))
                        {
                            // Its own road passes at SurfaceOffset below;
                            // anything nearer than that would cut or
                            // cover the sheet.
                            Assert.That(
                                top,
                                Is.LessThan(point.y - 0.0015f),
                                $"Puddle {index} is covered or cut at {point}.");
                        }
                    }
                }
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
