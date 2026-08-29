using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityCemeteryPlannerTests
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        [Test]
        public void DefaultCity_PlansDeterministicVariedCemetery()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityCemeteryPlan first = CityCemeteryPlanner.Create(layout);
            CityCemeteryPlan second = CityCemeteryPlanner.Create(layout);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            CollectionAssert.AreEqual(first.Parts, second.Parts);
            CollectionAssert.AreEqual(first.Lamps, second.Lamps);
            CollectionAssert.AreEqual(first.Plots, second.Plots);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(CityCemeteryPlan.MaximumPartCount));

            // The variety contract: a populated yard showing all six
            // monument silhouettes and all three stone tints.
            Assert.That(first.GraveCount, Is.GreaterThanOrEqualTo(30));
            foreach (CityCemeteryGraveVariant variant in
                     System.Enum.GetValues(
                         typeof(CityCemeteryGraveVariant)))
            {
                Assert.That(
                    first.GetGraveVariantCount(variant),
                    Is.GreaterThanOrEqualTo(1),
                    $"Missing grave variant {variant}.");
            }

            CityCemeteryStyle[] stoneStyles = first.Parts
                .Where(part => part.GraveOrdinal >= 0)
                .Select(part => part.Style)
                .Distinct()
                .ToArray();
            Assert.That(
                stoneStyles,
                Contains.Item(CityCemeteryStyle.GraniteDark));
            Assert.That(
                stoneStyles,
                Contains.Item(CityCemeteryStyle.MarbleLight));
            Assert.That(
                stoneStyles,
                Contains.Item(CityCemeteryStyle.WeatheredConcrete));

            // Alleys, gate dressing, vegetation and lamps all present.
            Assert.That(
                first.GetCount(CityCemeteryPartKind.Alley),
                Is.GreaterThanOrEqualTo(3));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.GatePillar),
                Is.EqualTo(2));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.GateArch),
                Is.EqualTo(2));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.GateLeaf),
                Is.EqualTo(4));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.CornerPillar),
                Is.EqualTo(4));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.FencePost),
                Is.GreaterThan(12));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.FenceRail),
                Is.GreaterThan(8));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.GraveEnclosure),
                Is.GreaterThan(0));

            // Every enclosure stands on four grounded corner posts:
            // the rail band itself floats at knee height, so without
            // posts the оградка visibly hovers.
            CityCemeteryPartDescriptor[] enclosureRails = first.Parts
                .Where(part =>
                    part.Kind == CityCemeteryPartKind.GraveEnclosure &&
                    part.StableId.EndsWith(
                        "-rail-a",
                        System.StringComparison.Ordinal))
                .ToArray();
            CityCemeteryPartDescriptor[] enclosurePosts = first.Parts
                .Where(part =>
                    part.Kind == CityCemeteryPartKind.GraveEnclosure &&
                    part.StableId.Contains("rail-post"))
                .ToArray();
            Assert.That(
                enclosurePosts.Length,
                Is.EqualTo(enclosureRails.Length * 4));
            foreach (CityCemeteryPartDescriptor post in enclosurePosts)
            {
                Assert.That(
                    post.Center.y - post.Size.y * 0.5f,
                    Is.EqualTo(first.GroundTopY).Within(0.01f),
                    $"{post.StableId} must stand on the ground.");
            }
            Assert.That(
                first.GetCount(CityCemeteryPartKind.GraveOffering),
                Is.GreaterThan(0));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.TreeTrunk),
                Is.GreaterThanOrEqualTo(5));
            Assert.That(
                first.GetCount(CityCemeteryPartKind.Bush),
                Is.GreaterThan(0));

            // The lamp chain walks the whole main alley and benches
            // wait beside it: at least two benches of four parts each.
            Assert.That(
                first.GetLampCount(CityCemeteryLampKind.Alley),
                Is.InRange(4, 9));
            Assert.That(
                first.GetLampCount(CityCemeteryLampKind.LodgePorch),
                Is.EqualTo(1),
                "The gate lodge lights its own doorstep.");
            Assert.That(
                first.GetCount(CityCemeteryPartKind.Bench),
                Is.GreaterThanOrEqualTo(8));
            float lampSpread = Mathf.Max(
                first.Lamps.Max(lamp => lamp.GroundPosition.x) -
                first.Lamps.Min(lamp => lamp.GroundPosition.x),
                first.Lamps.Max(lamp => lamp.GroundPosition.z) -
                first.Lamps.Min(lamp => lamp.GroundPosition.z));
            Assert.That(
                lampSpread,
                Is.GreaterThan(12f),
                "Lamps must be spread along the alley, not clustered " +
                "at the gate.");

            // Graves are individually posed: at least one part carries
            // a non-identity yaw, so the grid never reads as stamped.
            Assert.That(
                first.Parts.Any(part =>
                    part.GraveOrdinal >= 0 &&
                    Quaternion.Angle(
                        part.Rotation,
                        Quaternion.identity) > 0.5f),
                Is.True);

            Assert.DoesNotThrow(() =>
                CityCemeteryPlanner.ValidateOrThrow(layout, first));

            // No two graves overlap: conservative slab-versus-slab
            // check across distinct ordinals.
            CityCemeteryPartDescriptor[] slabs = first.Parts
                .Where(part =>
                    part.Kind == CityCemeteryPartKind.GraveSlab)
                .ToArray();
            for (int left = 0; left < slabs.Length; left++)
            {
                for (int right = left + 1;
                     right < slabs.Length;
                     right++)
                {
                    Assert.That(
                        Overlaps(
                            ToXZRect(slabs[left]),
                            ToXZRect(slabs[right])),
                        Is.False,
                        $"{slabs[left].StableId} overlaps " +
                        $"{slabs[right].StableId}.");
                }
            }

            // Ground-level blockers keep every cemetery street
            // approach walkable; the overhead gate arch is exempt.
            foreach (CityCemeteryPartDescriptor part in
                     first.Parts.Where(item => item.BlocksMovement))
            {
                if (part.Kind == CityCemeteryPartKind.GateArch)
                {
                    continue;
                }

                foreach (CityOpenAreaAccessDescriptor access in
                         layout.OpenAreaAccesses.Where(item =>
                             item.Feature ==
                             CityAreaFeatureKind.Cemetery))
                {
                    Assert.That(
                        Overlaps(
                            ToXZRect(part),
                            access.ApproachBounds),
                        Is.False,
                        part.StableId);
                }
            }
        }

        [Test]
        [Category("CityChurchCemeteryPassage")]
        public void DefaultCity_OpensMiddleCrossAlleyIntoChurchGrounds()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            CityChurchCemeteryPassagePlan passage =
                CityChurchCemeteryPassagePlanner.Create(layout, church);
            CityCemeteryPlan cemetery =
                CityCemeteryPlanner.Create(layout);

            Assert.That(church, Is.Not.Null);
            Assert.That(passage, Is.Not.Null);
            Assert.That(cemetery, Is.Not.Null);
            Assert.That(cemetery.ChurchPassage, Is.EqualTo(passage));
            Assert.That(
                passage.AxisX,
                Is.EqualTo(226f).Within(0.001f),
                "The nearest clear real cross alley is the middle one.");
            Assert.That(
                passage.BoundaryZ,
                Is.EqualTo(-104f).Within(0.001f));
            Assert.That(
                passage.OpeningWidth,
                Is.EqualTo(3f).Within(0.001f));
            Assert.That(
                passage.FenceOpeningBounds.xMin,
                Is.EqualTo(224.5f).Within(0.001f));
            Assert.That(
                passage.FenceOpeningBounds.xMax,
                Is.EqualTo(227.5f).Within(0.001f));
            Assert.That(
                passage.StepHeight,
                Is.InRange(0.18f, 0.19f));
            Assert.That(
                passage.StepHeight,
                Is.LessThanOrEqualTo(
                    CityVerticalTraversalAudit.MaximumSafeStep));
            Assert.DoesNotThrow(() =>
                CityChurchCemeteryPassagePlanner.ValidateOrThrow(
                    layout,
                    church,
                    passage));

            CityCemeteryPartDescriptor[] endPosts = cemetery.Parts
                .Where(part =>
                    part.Kind == CityCemeteryPartKind.FencePost &&
                    Mathf.Abs(part.Center.z - passage.BoundaryZ) <
                        0.001f &&
                    (Mathf.Abs(
                         part.Center.x -
                         passage.FenceBreakBounds.xMin) < 0.001f ||
                     Mathf.Abs(
                         part.Center.x -
                         passage.FenceBreakBounds.xMax) < 0.001f))
                .ToArray();
            Assert.That(endPosts, Has.Length.EqualTo(2));
            Assert.That(
                endPosts.Max(post => ToXZRect(post).xMin) -
                endPosts.Min(post => ToXZRect(post).xMax),
                Is.EqualTo(3f).Within(0.001f),
                "Ordinary end posts leave three physical metres clear.");

            Assert.That(
                cemetery.Parts.Any(part =>
                    part.Kind == CityCemeteryPartKind.Alley &&
                    ContainsRect(
                        ToXZRect(part),
                        passage.CemeteryAlleyExtensionBounds)),
                Is.True,
                "The cross alley reaches the new north-fence opening.");
            foreach (CityCemeteryPartDescriptor blocker in
                     cemetery.Parts.Where(part =>
                         part.BlocksMovement &&
                         MinimumWorldY(part) <
                            cemetery.GroundTopY + 2.1f))
            {
                Assert.That(
                    OverlapsInterior(
                        ToXZRect(blocker),
                        passage.FenceOpeningBounds),
                    Is.False,
                    blocker.StableId);
                Assert.That(
                    OverlapsInterior(
                        ToXZRect(blocker),
                        passage.CemeteryAlleyExtensionBounds),
                    Is.False,
                    blocker.StableId);
            }

            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);
            for (float z = passage.BoundaryZ - 1.2f;
                 z <= passage.BoundaryZ + 1.2f;
                 z += 0.2f)
            {
                Assert.That(
                    walkable.Contains(
                        new Vector3(passage.AxisX, 0f, z),
                        CityGroundTraversalPlanner.MaximumAgentRadius),
                    Is.True,
                    $"Passage centerline at world Z={z:0.0}");
            }

            Assert.That(
                layout.OpenAreaAccesses.Count(access =>
                    access.Feature == CityAreaFeatureKind.Cemetery),
                Is.EqualTo(1),
                "The internal passage is not a second street gate.");
            Assert.That(
                cemetery.GetCount(CityCemeteryPartKind.GatePillar),
                Is.EqualTo(2));
            Assert.That(
                cemetery.GetCount(CityCemeteryPartKind.GateArch),
                Is.EqualTo(2));
            Assert.That(
                cemetery.GetCount(CityCemeteryPartKind.GateLeaf),
                Is.EqualTo(4));
            Assert.That(
                cemetery.GetCount(CityCemeteryPartKind.Lodge),
                Is.GreaterThan(0));
            Assert.That(
                cemetery.GetLampCount(
                    CityCemeteryLampKind.LodgePorch),
                Is.EqualTo(1));
        }

        [Test]
        [Category("CityChurchCemeteryPassage")]
        public void PassageGround_IsNeverOfferedForGraveWork()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityCemeteryPlan connected =
                CityCemeteryPlanner.Create(layout);
            CityCemeteryPlan independent =
                CityCemeteryPlanner.Create(layout, null);
            CityChurchCemeteryPassagePlan passage =
                connected.ChurchPassage;

            Assert.That(passage, Is.Not.Null);
            CityCemeteryPlotDescriptor[] graveWorkPlots = connected.Plots
                .Where(plot =>
                    plot.State == CityCemeteryPlotState.Vacant)
                .ToArray();
            Assert.That(
                graveWorkPlots,
                Is.Not.Empty,
                "The passage must not consume the grave-work pool.");
            foreach (CityCemeteryPlotDescriptor plot in graveWorkPlots)
            {
                Assert.That(
                    OverlapsInterior(
                        plot.Footprint,
                        passage.CemeteryAlleyExtensionBounds),
                    Is.False,
                    plot.StableId);
            }

            Assert.That(connected.VacantPlotCount, Is.GreaterThan(0));
            Assert.That(
                connected.TryGetNextVacantPlot(out _),
                Is.True);

            Assert.That(
                CemeteryMournerPlan.TryGetAccess(
                    layout,
                    out CityOpenAreaAccessDescriptor mournerAccess),
                Is.True);
            Assert.That(
                mournerAccess.OutwardNormal,
                Is.EqualTo(Vector3.right),
                "The mourner still enters through the west street gate.");
            Assert.That(
                CemeteryMournerPlan.CollectCandidateGraves(connected),
                Is.Not.Empty,
                "The passage must not disable mourner visits.");

            Assert.That(independent.ChurchPassage, Is.Null);
            Assert.That(
                independent.GetCount(CityCemeteryPartKind.GatePillar),
                Is.EqualTo(
                    connected.GetCount(
                        CityCemeteryPartKind.GatePillar)));
            Assert.That(
                independent.GetCount(CityCemeteryPartKind.GateArch),
                Is.EqualTo(
                    connected.GetCount(
                        CityCemeteryPartKind.GateArch)));
            Assert.That(
                independent.GetCount(CityCemeteryPartKind.GateLeaf),
                Is.EqualTo(
                    connected.GetCount(
                        CityCemeteryPartKind.GateLeaf)));
            Assert.That(
                independent.GetCount(CityCemeteryPartKind.Lodge),
                Is.EqualTo(
                    connected.GetCount(CityCemeteryPartKind.Lodge)));

            Vector2 closedPoint = new Vector2(
                passage.AxisX,
                passage.BoundaryZ);
            Assert.That(
                independent.Parts.Any(part =>
                    part.Kind == CityCemeteryPartKind.FenceRail &&
                    ToXZRect(part).Contains(closedPoint)),
                Is.True,
                "The compatible null overload retains the old north fence.");
        }

        [Test]
        public void DefaultCity_BuildsTexturedCemeteryWithNightLamps()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityCemeteryPlan plan = CityCemeteryPlanner.Create(layout);
            Assert.That(plan, Is.Not.Null);

            var owner = new GameObject("Cemetery Test World");
            try
            {
                GameObject root = CityCemeteryWorldBuilder.Build(
                    owner.transform,
                    plan);
                Assert.That(
                    root.name,
                    Is.EqualTo(CityCemeteryWorldBuilder.RootName));

                // Every lamp descriptor became one night-scaled point
                // light with a fog halo and an emissive mantle.
                Light[] lights =
                    root.GetComponentsInChildren<Light>(true);
                Assert.That(
                    lights,
                    Has.Length.EqualTo(plan.Lamps.Count));
                foreach (Light light in lights)
                {
                    Assert.That(
                        light.type,
                        Is.EqualTo(LightType.Point));
                    Assert.That(
                        light.shadows,
                        Is.EqualTo(LightShadows.None));
                    Assert.That(
                        light.GetComponentInChildren<CityLightHalo>(
                            true),
                        Is.Not.Null);
                }

                try
                {
                    // §20: every fixture burns always, and the day takes
                    // at most a third off it. The alley lamps used to die
                    // at dawn and the porch bulb dropped to a 23% day
                    // filament; both readings are repealed - the registry
                    // now enforces the two-thirds floor for everything it
                    // holds.
                    var nightStrength =
                        new System.Collections.Generic
                            .Dictionary<Light, float>();
                    foreach (Light light in lights)
                    {
                        nightStrength[light] = light.intensity;
                    }

                    CityNightSiteLightRegistry.SetNightFactor(0f);
                    foreach (Light light in lights)
                    {
                        Assert.That(
                            light.enabled,
                            Is.True,
                            "No cemetery fixture dies by day any more.");
                        Assert.That(
                            light.intensity,
                            Is.GreaterThanOrEqualTo(
                                nightStrength[light] *
                                GameTimeDayNightRules.DayFixtureFloor -
                                0.01f),
                            "The day takes a third off a fixture, " +
                            "no more.");
                    }
                }
                finally
                {
                    CityNightSiteLightRegistry.SetNightFactor(1f);
                }

                Renderer mantle = root
                    .GetComponentsInChildren<Renderer>(true)
                    .First(item => item.name == "Lamp Mantle");
                Assert.That(
                    mantle.sharedMaterial,
                    Is.SameAs(CityNightResources.EmissiveMaterial));

                // The lodge's porch bulb is its own warm fixture, hung
                // at head height over the doorstep rather than on an
                // alley pole.
                Transform porch = root.transform.Find(
                    "Cemetery Lodge Porch Lamp");
                Assert.That(porch, Is.Not.Null);
                Light porchLight =
                    porch.GetComponentInChildren<Light>(true);
                Assert.That(
                    porchLight.color,
                    Is.EqualTo(CityCemeteryWorldBuilder.PorchLightColor));
                Assert.That(
                    porchLight.transform.position.y -
                    plan.GroundTopY,
                    Is.InRange(1.7f, 2.4f));
                Assert.That(
                    porchLight.intensity,
                    Is.EqualTo(
                        CityCemeteryWorldBuilder.PorchNightIntensity)
                        .Within(0.01f),
                    "At full night it throws real light, not a glow.");
                Assert.That(
                    porchLight.renderMode,
                    Is.EqualTo(LightRenderMode.ForcePixel),
                    "It must survive the per-object additional light " +
                    "limit standing next to the watchman.");
                Renderer bulb = porch
                    .GetComponentsInChildren<Renderer>(true)
                    .First(item => item.name == "Porch Lamp Bulb");
                Assert.That(
                    bulb.sharedMaterial,
                    Is.SameAs(CityNightResources.EmissiveMaterial));

                // Stone, gravel and soil batches carry their cemetery
                // sheets over the shared material; foliage stays flat.
                AssertChunkAppearance(
                    root,
                    CityCemeteryStyle.GraniteDark,
                    CityCemeterySurfaceAppearance.GetTexture(
                        CityCemeterySurfaceKind.Granite));
                AssertChunkAppearance(
                    root,
                    CityCemeteryStyle.Gravel,
                    CityCemeterySurfaceAppearance.GetTexture(
                        CityCemeterySurfaceKind.Gravel));
                AssertChunkAppearance(
                    root,
                    CityCemeteryStyle.FoliageDark,
                    null);
                Assert.That(
                    root.GetComponentsInChildren<Renderer>(true).Any(
                        renderer =>
                            !renderer.enabled &&
                            renderer.name.StartsWith(
                                "Cemetery Chunk Imported Collision ",
                                System.StringComparison.Ordinal) &&
                            renderer.GetComponent<MeshCollider>() != null),
                    Is.True,
                    "Imported blocking shells retain hidden collision " +
                    "proxy batches.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void DefaultCity_CemeteryBenchesJoinTheSittableSeats()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityCemeteryPlan cemeteryPlan =
                CityCemeteryPlanner.Create(layout);
            Assert.That(cemeteryPlan, Is.Not.Null);
            CityOpenAreaDecorationPlan openArea =
                CityOpenAreaDecorationPlanner.Create(layout);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan decorations =
                CityDecorationPlanner.CreatePlan(layout, fence, night);
            CityBusPlan busPlan = CityBusPlanner.Create(
                layout,
                decorations);
            CityStreetSurfacePlan streetSurface =
                CityStreetSurfacePlanner.Create(layout);

            List<CityBenchSitPlan> seats = CityBenchSitPlan.CreateAll(
                layout,
                openArea,
                cemeteryPlan,
                busPlan,
                decorations,
                streetSurface);

            // One sit offer per drawn bench, derived from its seat
            // plank, docked toward the gravel alley it faces.
            int benchSeatParts = cemeteryPlan.Parts.Count(part =>
                part.Kind == CityCemeteryPartKind.Bench &&
                part.StableId.EndsWith(
                    "-seat",
                    System.StringComparison.Ordinal));
            Assert.That(benchSeatParts, Is.GreaterThanOrEqualTo(2));
            CityBenchSitPlan[] cemeterySeats = seats
                .Where(seat => seat.Id.StartsWith(
                    "cemetery-bench-",
                    System.StringComparison.Ordinal))
                .ToArray();
            Assert.That(
                cemeterySeats,
                Has.Length.EqualTo(benchSeatParts));
            foreach (CityBenchSitPlan seat in cemeterySeats)
            {
                Assert.That(seat.IsPresent, Is.True);
                Assert.That(
                    cemeteryPlan.Grounds.Contains(new Vector2(
                        seat.InteractionPosition.x,
                        seat.InteractionPosition.z)),
                    Is.True,
                    $"{seat.Id} seat must stay inside the grounds.");
                Assert.That(
                    seat.ActionHipPosition.y,
                    Is.EqualTo(
                        cemeteryPlan.GroundTopY + 0.49f +
                        CityBenchSitPlan.SeatClearance).Within(0.001f),
                    $"{seat.Id} pelvis must land on the seat plank.");
            }
        }

        [TestCase(
            (int)CityCemeterySurfaceKind.Granite,
            "Textures/CityCemeteryGraniteAlbedo",
            1.4f,
            0.18f,
            1.398f)]
        [TestCase(
            (int)CityCemeterySurfaceKind.Stone,
            "Textures/CityCemeteryStoneAlbedo",
            1.8f,
            0.05f,
            1.397f)]
        [TestCase(
            (int)CityCemeterySurfaceKind.Gravel,
            "Textures/CityCemeteryGravelAlbedo",
            1.6f,
            0.04f,
            1.4055f)]
        [TestCase(
            (int)CityCemeterySurfaceKind.Soil,
            "Textures/CityCemeterySoilAlbedo",
            3.0f,
            0.03f,
            1.4755f)]
        public void Recipe_LoadsConfiguredTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedAlbedoCompensation)
        {
            var kind = (CityCemeterySurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                CityCemeterySurfaceAppearance.GetRecipe(kind);
            Assert.That(
                recipe.ResourcePath,
                Is.EqualTo(expectedResourcePath));
            Assert.That(
                recipe.MetersPerTile,
                Is.EqualTo(expectedMetersPerTile));
            Assert.That(
                recipe.Smoothness,
                Is.EqualTo(expectedSmoothness));
            Assert.That(recipe.Metallic, Is.EqualTo(0f));
            Assert.That(
                recipe.AlbedoCompensation,
                Is.EqualTo(expectedAlbedoCompensation));

            Texture2D resource = Resources.Load<Texture2D>(
                expectedResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                CityCemeterySurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));
            Assert.That(resource.height, Is.EqualTo(512));
        }

        [Test]
        public void ApplyCombined_TintsWithoutCloningTheSharedMaterial()
        {
            var tint = new Color(0.21f, 0.22f, 0.24f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Cemetery Surface Test Box",
                null,
                Vector3.zero,
                Vector3.one,
                tint,
                false);
            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                CityCemeterySurfaceAppearance.ApplyCombined(
                    renderer,
                    CityCemeterySurfaceKind.Granite,
                    tint);

                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityCemeterySurfaceAppearance.GetTexture(
                            CityCemeterySurfaceKind.Granite)));
                Color display = properties.GetColor(BaseColorId);
                Assert.That(
                    display.r,
                    Is.EqualTo(tint.r * 1.398f).Within(0.0001f));
                Assert.That(
                    display.g,
                    Is.EqualTo(tint.g * 1.398f).Within(0.0001f));
                Assert.That(
                    display.b,
                    Is.EqualTo(tint.b * 1.398f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(surface);
            }
        }

        private static void AssertChunkAppearance(
            GameObject root,
            CityCemeteryStyle style,
            Texture expectedTexture)
        {
            List<Renderer> chunks = root
                .GetComponentsInChildren<Renderer>(true)
                .Where(item => item.enabled && item.name.EndsWith(
                    style.ToString(),
                    System.StringComparison.Ordinal))
                .ToList();
            Assert.That(
                chunks,
                Is.Not.Empty,
                $"The default cemetery builds a {style} batch.");
            foreach (Renderer chunk in chunks)
            {
                Assert.That(
                    chunk.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial),
                    chunk.name);
                var properties = new MaterialPropertyBlock();
                chunk.GetPropertyBlock(properties);
                if (expectedTexture != null)
                {
                    Assert.That(
                        properties.GetTexture(BaseMapId),
                        Is.SameAs(expectedTexture),
                        chunk.name);
                }
                else
                {
                    Assert.That(
                        properties.GetTexture(BaseMapId),
                        Is.Null,
                        chunk.name);
                }

            }
        }

        /// <summary>
        /// The burial lattice divides the whole precinct: what is
        /// buried, what is free to bury, and what is not burial ground
        /// at all. This is the contract the coming burials rest on —
        /// a vacant plot must be somewhere a monument can go up
        /// without moving anything already standing.
        /// </summary>
        [Test]
        public void DefaultCity_DividesTheGroundsIntoOccupiedAndVacantPlots()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityCemeteryPlan plan = CityCemeteryPlanner.Create(layout);

            Assert.That(plan, Is.Not.Null);
            TestContext.WriteLine(
                $"CEMETERY-CENSUS plots={plan.PlotCount} " +
                $"occupied={plan.OccupiedPlotCount} " +
                $"vacant={plan.VacantPlotCount} " +
                $"obstructed={plan.ObstructedPlotCount} " +
                $"rows={plan.Plots.Max(plot => plot.Row) + 1} " +
                $"columns={plan.Plots.Max(plot => plot.Column) + 1} " +
                $"grounds={plan.Grounds.width:F1}x" +
                $"{plan.Grounds.height:F1}");

            // The lattice is a solid rectangle with no holes, and the
            // three states partition it exactly once.
            int rows = plan.Plots.Max(plot => plot.Row) + 1;
            int columns = plan.Plots.Max(plot => plot.Column) + 1;
            Assert.That(plan.PlotCount, Is.EqualTo(rows * columns));
            Assert.That(
                plan.Plots.Select(plot => plot.StableId).Distinct()
                    .Count(),
                Is.EqualTo(plan.PlotCount));
            Assert.That(
                plan.OccupiedPlotCount +
                plan.VacantPlotCount +
                plan.ObstructedPlotCount,
                Is.EqualTo(plan.PlotCount));
            Assert.That(
                plan.GetPlotCount(CityCemeteryPlotState.Vacant),
                Is.EqualTo(plan.VacantPlotCount));

            // Every grave stands on exactly one plot, at that plot's
            // own ground point and heading.
            Assert.That(
                plan.OccupiedPlotCount,
                Is.EqualTo(plan.GraveCount));
            CityCemeteryPlotDescriptor[] occupied = plan.Plots
                .Where(plot =>
                    plot.State == CityCemeteryPlotState.Occupied)
                .ToArray();
            Assert.That(
                occupied.Select(plot => plot.GraveOrdinal)
                    .OrderBy(ordinal => ordinal),
                Is.EqualTo(Enumerable.Range(0, plan.GraveCount)));
            foreach (CityCemeteryPlotDescriptor plot in occupied)
            {
                CityCemeteryPartDescriptor slab = plan.Parts.Single(
                    part =>
                        part.Kind == CityCemeteryPartKind.GraveSlab &&
                        part.GraveOrdinal == plot.GraveOrdinal);
                Assert.That(
                    slab.Center.x,
                    Is.EqualTo(plot.Ground.x).Within(0.001f),
                    plot.StableId);
                Assert.That(
                    slab.Center.z,
                    Is.EqualTo(plot.Ground.z).Within(0.001f),
                    plot.StableId);
                Assert.That(
                    Quaternion.Angle(slab.Rotation, plot.Yaw),
                    Is.LessThan(0.01f),
                    plot.StableId);
            }

            // Vacant and obstructed plots carry no grave identity.
            Assert.That(
                plan.Plots.Where(plot =>
                        plot.State != CityCemeteryPlotState.Occupied)
                    .All(plot => plot.GraveOrdinal == -1),
                Is.True);

            // Plots never overlap and never leave the grounds, so the
            // envelope a vacant plot promises is really its own.
            for (int left = 0; left < plan.Plots.Count; left++)
            {
                CityCemeteryPlotDescriptor plot = plan.Plots[left];
                Assert.That(
                    plan.Grounds.xMin <= plot.Footprint.xMin &&
                    plan.Grounds.xMax >= plot.Footprint.xMax &&
                    plan.Grounds.yMin <= plot.Footprint.yMin &&
                    plan.Grounds.yMax >= plot.Footprint.yMax,
                    Is.True,
                    $"{plot.StableId} leaves the grounds.");
                for (int right = left + 1;
                     right < plan.Plots.Count;
                     right++)
                {
                    Assert.That(
                        Overlaps(
                            plot.Footprint,
                            plan.Plots[right].Footprint),
                        Is.False,
                        $"{plot.StableId} overlaps " +
                        $"{plan.Plots[right].StableId}.");
                }
            }

            // The yard has room left, and the next burial goes into
            // the free plot nearest the gate.
            Assert.That(plan.VacantPlotCount, Is.GreaterThan(0));
            Assert.That(
                plan.TryGetNextVacantPlot(
                    out CityCemeteryPlotDescriptor next),
                Is.True);
            Assert.That(next.IsVacant, Is.True);
            Assert.That(next.GraveOrdinal, Is.EqualTo(-1));
            Assert.That(
                next.StableId,
                Is.EqualTo(plan.Plots
                    .First(plot => plot.IsVacant).StableId));

            // Nothing at grave height stands on a vacant plot: no
            // gravel, no lamp, no bench, no lodge, no tree, no bush
            // and no neighbouring monument.
            foreach (CityCemeteryPlotDescriptor plot in
                     plan.Plots.Where(item => item.IsVacant))
            {
                foreach (CityCemeteryPartDescriptor part in plan.Parts)
                {
                    if (MinimumWorldY(part) >= plan.GroundTopY + 2.1f)
                    {
                        continue;
                    }

                    Assert.That(
                        Overlaps(ToXZRect(part), plot.Footprint),
                        Is.False,
                        $"{part.StableId} stands on vacant plot " +
                        $"{plot.StableId}.");
                }
            }
        }

        private static Rect ToXZRect(CityCemeteryPartDescriptor part)
        {
            Vector3 right = part.Rotation * Vector3.right;
            Vector3 up = part.Rotation * Vector3.up;
            Vector3 forward = part.Rotation * Vector3.forward;
            float halfX =
                Mathf.Abs(right.x) * part.Size.x * 0.5f +
                Mathf.Abs(up.x) * part.Size.y * 0.5f +
                Mathf.Abs(forward.x) * part.Size.z * 0.5f;
            float halfZ =
                Mathf.Abs(right.z) * part.Size.x * 0.5f +
                Mathf.Abs(up.z) * part.Size.y * 0.5f +
                Mathf.Abs(forward.z) * part.Size.z * 0.5f;
            return Rect.MinMaxRect(
                part.Center.x - halfX,
                part.Center.z - halfZ,
                part.Center.x + halfX,
                part.Center.z + halfZ);
        }

        private static float MinimumWorldY(
            CityCemeteryPartDescriptor part)
        {
            Vector3 right = part.Rotation * Vector3.right;
            Vector3 up = part.Rotation * Vector3.up;
            Vector3 forward = part.Rotation * Vector3.forward;
            float halfY =
                Mathf.Abs(right.y) * part.Size.x * 0.5f +
                Mathf.Abs(up.y) * part.Size.y * 0.5f +
                Mathf.Abs(forward.y) * part.Size.z * 0.5f;
            return part.Center.y - halfY;
        }

        private static bool Overlaps(Rect left, Rect right)
        {
            return left.xMin < right.xMax &&
                   left.xMax > right.xMin &&
                   left.yMin < right.yMax &&
                   left.yMax > right.yMin;
        }

        private static bool OverlapsInterior(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static bool ContainsRect(Rect outer, Rect inner)
        {
            const float epsilon = 0.001f;
            return inner.xMin >= outer.xMin - epsilon &&
                   inner.xMax <= outer.xMax + epsilon &&
                   inner.yMin >= outer.yMin - epsilon &&
                   inner.yMax <= outer.yMax + epsilon;
        }
    }
}
