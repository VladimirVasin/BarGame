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
            Assert.That(first.Lamps.Count, Is.InRange(4, 9));
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
                    CityNightSiteLightRegistry.SetNightFactor(0f);
                    foreach (Light light in lights)
                    {
                        Assert.That(
                            light.enabled,
                            Is.False,
                            "Cemetery lamps die by day.");
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

                // Stone, gravel and soil batches carry their cemetery
                // sheets over the shared material; foliage stays flat.
                AssertChunkAppearance(
                    root,
                    CityCemeteryStyle.GraniteDark,
                    CityCemeterySurfaceAppearance.GetTexture(
                        CityCemeterySurfaceKind.Granite),
                    expectCollider: true);
                AssertChunkAppearance(
                    root,
                    CityCemeteryStyle.Gravel,
                    CityCemeterySurfaceAppearance.GetTexture(
                        CityCemeterySurfaceKind.Gravel),
                    expectCollider: false);
                AssertChunkAppearance(
                    root,
                    CityCemeteryStyle.FoliageDark,
                    null,
                    expectCollider: false);
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
            Texture expectedTexture,
            bool expectCollider)
        {
            List<Renderer> chunks = root
                .GetComponentsInChildren<Renderer>(true)
                .Where(item => item.name.EndsWith(
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

                Assert.That(
                    chunk.GetComponent<MeshCollider>() != null,
                    Is.EqualTo(expectCollider),
                    chunk.name);
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

        private static bool Overlaps(Rect left, Rect right)
        {
            return left.xMin < right.xMax &&
                   left.xMax > right.xMin &&
                   left.yMin < right.yMax &&
                   left.yMax > right.yMin;
        }
    }
}
