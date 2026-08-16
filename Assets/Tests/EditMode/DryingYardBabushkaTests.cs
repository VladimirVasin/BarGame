using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class DryingYardBabushkaTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");

        [Test]
        public void Plan_PlacesThreeBabushkasInsideTheDryingYard()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            DryingYardBabushkaPlan plan =
                DryingYardBabushkaPlan.Create(layout);

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(plan.Stances, Has.Count.EqualTo(3));

            CityDistrictPointOfInterestDescriptor descriptor =
                FindDryingYard(layout);
            Rect bounds = descriptor.PublicBounds;
            int beaterCount = 0;
            int smokerCount = 0;
            var palettes = new HashSet<int>();
            foreach (DryingYardBabushkaStance stance in plan.Stances)
            {
                Assert.That(
                    bounds.Contains(new Vector2(
                        stance.Position.x,
                        stance.Position.z)),
                    Is.True,
                    $"{stance.Role} stands outside the drying yard.");
                Assert.That(
                    stance.Facing.magnitude,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    Mathf.Abs(stance.Facing.y),
                    Is.LessThan(0.001f),
                    "Stance facings stay horizontal.");
                Assert.That(palettes.Add(stance.PaletteVariant), Is.True,
                    "Every babushka wears her own palette variant.");
                if (stance.Role == DryingYardBabushkaRole.CarpetBeater)
                {
                    beaterCount++;
                }
                else
                {
                    smokerCount++;
                }
            }

            Assert.That(beaterCount, Is.EqualTo(2));
            Assert.That(smokerCount, Is.EqualTo(1));

            // The two beaters work opposite sides of the rack, each
            // wired to her own carpet, desynchronized so their strikes
            // never fall into lockstep.
            DryingYardBabushkaStance beaterA = plan.Stances[0];
            DryingYardBabushkaStance beaterB = plan.Stances[1];
            Assert.That(
                Vector3.Dot(beaterA.Facing, beaterB.Facing),
                Is.LessThan(-0.9f),
                "The beaters face each other across the rack.");
            Assert.That(beaterA.Strolls, Is.False);
            Assert.That(beaterB.Strolls, Is.False);
            Assert.That(
                beaterA.CarpetId,
                Is.EqualTo(CityDryingYardCarpetRegistry.SouthCarpetId));
            Assert.That(
                beaterB.CarpetId,
                Is.EqualTo(CityDryingYardCarpetRegistry.NorthCarpetId));
            Assert.That(
                beaterA.PlaybackSpeed,
                Is.Not.EqualTo(beaterB.PlaybackSpeed).Within(0.001f));
            Assert.That(
                beaterA.PhaseOffsetSeconds,
                Is.Not.EqualTo(beaterB.PhaseOffsetSeconds)
                    .Within(0.001f));

            // The smoker strolls her corridor back and forth past the
            // beaters, close enough to read as company.
            DryingYardBabushkaStance smoker = plan.Stances[2];
            Assert.That(smoker.Strolls, Is.True);
            Assert.That(smoker.CarpetId, Is.Null);
            Assert.That(
                bounds.Contains(new Vector2(
                    smoker.PathEnd.x,
                    smoker.PathEnd.z)),
                Is.True,
                "The stroll corridor stays inside the drying yard.");
            float pathLength = Vector3.Distance(
                smoker.Position,
                smoker.PathEnd);
            Assert.That(pathLength, Is.InRange(3f, 6f));
            Assert.That(
                Vector3.Dot(
                    smoker.Facing,
                    (smoker.PathEnd - smoker.Position).normalized),
                Is.GreaterThan(0.99f),
                "The smoker starts facing along her corridor.");
            foreach (DryingYardBabushkaStance beater in
                     new[] { beaterA, beaterB })
            {
                float closest = DistancePointToSegment(
                    beater.Position,
                    smoker.Position,
                    smoker.PathEnd);
                Assert.That(
                    closest,
                    Is.InRange(0.6f, 3.6f),
                    "The stroll passes by each beater without " +
                    "walking through her.");
            }
        }

        private static float DistancePointToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.0001f)
            {
                return Vector3.Distance(point, start);
            }

            float amount = Mathf.Clamp01(
                Vector3.Dot(point - start, segment) / lengthSquared);
            return Vector3.Distance(point, start + segment * amount);
        }

        [Test]
        public void Build_DryingYardCarriesTheCarpetRack()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Carpet Rack Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                string[] rackParts =
                {
                    "Carpet Rack Post South",
                    "Carpet Rack Post North",
                    "Carpet Rack Bar",
                    "Beaten Carpet South",
                    "Beaten Carpet North",
                    "Beaten Carpet South Fold",
                    "Beaten Carpet North Fold"
                };
                var found = new HashSet<string>();
                Texture rugTexture =
                    HomeSurfaceAppearance.GetTexture(
                        HomeSurfaceKind.Rug);
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (string part in rackParts)
                    {
                        if (renderer.name != part)
                        {
                            continue;
                        }

                        found.Add(part);
                        if (part.StartsWith("Beaten Carpet"))
                        {
                            var properties =
                                new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(properties);
                            Assert.That(
                                properties.GetTexture(BaseMapId),
                                Is.SameAs(rugTexture),
                                $"{part} must carry the rug albedo.");
                        }
                    }
                }

                Assert.That(found, Is.EquivalentTo(rackParts));

                // In the city each carpet is real simulated cloth,
                // registered for the strike driver and deliberately
                // outside the laundry's weather-wind registry.
                Cloth south = CityDryingYardCarpetRegistry.Find(
                    CityDryingYardCarpetRegistry.SouthCarpetId);
                Cloth north = CityDryingYardCarpetRegistry.Find(
                    CityDryingYardCarpetRegistry.NorthCarpetId);
                Assert.That(south, Is.Not.Null);
                Assert.That(north, Is.Not.Null);
                Assert.That(south, Is.Not.SameAs(north));
                Assert.That(
                    south.name,
                    Is.EqualTo("Beaten Carpet South"));
                Assert.That(
                    south.transform.IsChildOf(root.transform),
                    Is.True);
                Assert.That(
                    south.damping,
                    Is.GreaterThan(0.5f),
                    "A heavy pile carpet hangs damped.");

                AssertYardCollidersClearApproaches(root, layout);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Build_BabushkaStancesClearTheYardObstacles()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            DryingYardBabushkaPlan plan =
                DryingYardBabushkaPlan.Create(layout);
            Assert.That(plan.IsPresent, Is.True);
            var parent = new GameObject("Babushka Stance Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);
                Physics.SyncTransforms();
                foreach (DryingYardBabushkaStance stance in plan.Stances)
                {
                    // A stroller is checked along her whole corridor.
                    var samples = new List<Vector3> { stance.Position };
                    if (stance.Strolls)
                    {
                        for (int step = 1; step <= 4; step++)
                        {
                            samples.Add(Vector3.Lerp(
                                stance.Position,
                                stance.PathEnd,
                                step / 4f));
                        }
                    }

                    foreach (Collider collider in
                             root.GetComponentsInChildren<Collider>(
                                 true))
                    {
                        // The paving is the ground she stands on, not
                        // an obstacle.
                        if (collider.name ==
                            CityDistrictPointOfInterestWorldBuilder
                                .PublicGroundName)
                        {
                            continue;
                        }

                        Bounds bounds = collider.bounds;
                        var footprint = Rect.MinMaxRect(
                            bounds.min.x,
                            bounds.min.z,
                            bounds.max.x,
                            bounds.max.z);
                        // A 0.30 m body radius around each stance and
                        // stroll sample must stay clear of every yard
                        // obstacle.
                        foreach (Vector3 sample in samples)
                        {
                            var body = Rect.MinMaxRect(
                                sample.x - 0.30f,
                                sample.z - 0.30f,
                                sample.x + 0.30f,
                                sample.z + 0.30f);
                            Assert.That(
                                footprint.Overlaps(body),
                                Is.False,
                                $"'{collider.name}' overlaps the " +
                                $"{stance.Role} stance or corridor.");
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Provider_BindsTheStagedPrefabWithoutPublishingIt()
        {
            DryingYardBabushkaProvider provider =
                DryingYardBabushkaProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Resources/City/DryingYardBabushkaProvider.asset is " +
                "missing.");
            GameObject prefab = provider.StagedPrefab;
            Assert.That(
                prefab,
                Is.Not.Null,
                "The provider must reference the staged babushka " +
                "prefab.");

            var registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(DryingYardBabushkaProvider.DesignId));
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(
                registry.IdleClip.name,
                Is.EqualTo("BabushkaSmoke"));
            Assert.That(registry.WalkClip, Is.Not.Null);
            Assert.That(
                registry.WalkClip.name,
                Is.EqualTo("BabushkaBeat"));
            Assert.That(registry.SitClip, Is.Null);
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);

            // The staged prefab ships both hand props; the runtime
            // enables exactly one per role.
            var rendererNames = new HashSet<string>();
            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                rendererNames.Add(renderer.name);
            }

            Assert.That(
                rendererNames,
                Is.SupersetOf(new[]
                {
                    "ACC_BeaterHandle",
                    "ACC_BeaterNeck",
                    "ACC_BeaterPaddleRise",
                    "ACC_BeaterPaddleTip",
                    "ACC_Cigarette",
                    "ACC_CigaretteEmber"
                }));

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                Resources.Load<GameObject>("Pedestrians/YardBabushka3D"),
                Is.Null,
                "The staged babushka must stay outside Resources.");
        }

        private static CityDistrictPointOfInterestDescriptor
            FindDryingYard(CityLayout layout)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (layout.DistrictPointsOfInterest[index].Kind ==
                    CityDistrictPointOfInterestKind
                        .ResidentialDryingYard)
                {
                    return layout.DistrictPointsOfInterest[index];
                }
            }

            Assert.Fail("The default city builds a drying yard.");
            return default;
        }

        private static void AssertYardCollidersClearApproaches(
            GameObject root,
            CityLayout layout)
        {
            CityDistrictPointOfInterestDescriptor descriptor =
                FindDryingYard(layout);
            Physics.SyncTransforms();
            foreach (Collider collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.name ==
                    CityDistrictPointOfInterestWorldBuilder
                        .PublicGroundName)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                var footprint = Rect.MinMaxRect(
                    bounds.min.x,
                    bounds.min.z,
                    bounds.max.x,
                    bounds.max.z);
                for (int accessIndex = 0;
                     accessIndex < descriptor.Accesses.Count;
                     accessIndex++)
                {
                    Assert.That(
                        footprint.Overlaps(
                            descriptor.Accesses[accessIndex]
                                .ApproachBounds),
                        Is.False,
                        $"'{collider.name}' blocks public access " +
                        $"'{descriptor.Accesses[accessIndex].Id}'.");
                }
            }
        }
    }
}
