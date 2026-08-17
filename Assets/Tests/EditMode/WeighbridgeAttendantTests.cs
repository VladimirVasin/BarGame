using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class WeighbridgeAttendantTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Plan_PlacesTheAttendantPairInsideTheWeighbridge()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            WeighbridgeAttendantPlan plan =
                WeighbridgeAttendantPlan.Create(layout);

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(plan.Stances, Has.Count.EqualTo(2));

            CityDistrictPointOfInterestDescriptor descriptor =
                FindWeighbridge(layout);
            Rect bounds = descriptor.PublicBounds;
            var palettes = new HashSet<int>();
            foreach (WeighbridgeAttendantStance stance in plan.Stances)
            {
                Assert.That(
                    bounds.Contains(new Vector2(
                        stance.Position.x,
                        stance.Position.z)),
                    Is.True,
                    $"{stance.Role} stands outside the weighbridge.");
                Assert.That(
                    stance.Facing.magnitude,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    Mathf.Abs(stance.Facing.y),
                    Is.LessThan(0.001f),
                    "Stance facings stay horizontal.");
                Assert.That(palettes.Add(stance.PaletteVariant), Is.True,
                    "Each attendant wears their own palette variant.");
            }

            WeighbridgeAttendantStance weigher = plan.Stances[0];
            WeighbridgeAttendantStance worker = plan.Stances[1];
            Assert.That(
                weigher.Role,
                Is.EqualTo(WeighbridgeAttendantRole.Weigher));
            Assert.That(
                worker.Role,
                Is.EqualTo(WeighbridgeAttendantRole.WeighedWorker));
            Assert.That(weigher.Strolls, Is.False);
            Assert.That(worker.Strolls, Is.True);

            // The pair never falls into lockstep: desynchronized by
            // both playback speed and phase.
            Assert.That(
                weigher.PlaybackSpeed,
                Is.Not.EqualTo(worker.PlaybackSpeed).Within(0.001f));
            Assert.That(
                weigher.PhaseOffsetSeconds,
                Is.Not.EqualTo(worker.PhaseOffsetSeconds).Within(0.001f));

            // The worker paces the deck's long axis on the deck top,
            // not on the public ground.
            Assert.That(
                bounds.Contains(new Vector2(
                    worker.PathEnd.x,
                    worker.PathEnd.z)),
                Is.True,
                "The pace corridor stays inside the weighbridge.");
            float corridorLength = Vector3.Distance(
                worker.Position,
                worker.PathEnd);
            Assert.That(corridorLength, Is.InRange(8f, 14f));
            Assert.That(
                worker.Position.y,
                Is.EqualTo(descriptor.Center.y + 0.27f).Within(0.001f),
                "The worker walks the deck top.");
            Assert.That(
                Vector3.Dot(
                    worker.Facing,
                    (worker.PathEnd - worker.Position).normalized),
                Is.GreaterThan(0.99f),
                "The worker starts facing along his corridor.");
        }

        [Test]
        public void Plan_WeigherStandsBesideTheDeckAxisNeverAcrossIt()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            WeighbridgeAttendantPlan plan =
                WeighbridgeAttendantPlan.Create(layout);
            Assert.That(plan.IsPresent, Is.True);

            WeighbridgeAttendantStance weigher = plan.Stances[0];
            WeighbridgeAttendantStance worker = plan.Stances[1];

            // The art bible forbids the weighbridge reading as a
            // checkpoint: the weigher watches from beside the axis,
            // clear of the whole deck corridor.
            float distanceToAxis = DistancePointToSegment(
                weigher.Position,
                worker.Position,
                worker.PathEnd);
            Assert.That(
                distanceToAxis,
                Is.GreaterThan(2.4f),
                "The weigher must never block the deck axis.");
        }

        [Test]
        public void Build_WeighbridgeRegistersItsNeedle()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Weighbridge Needle Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                Transform needle =
                    CityWeighbridgeIndicatorRegistry.Find(
                        CityWeighbridgeIndicatorRegistry.NeedleId);
                Assert.That(needle, Is.Not.Null);
                Assert.That(needle.name, Is.EqualTo("Scale Needle"));
                // Only the City build may claim the slot; a bounded
                // Home-exterior rebuild has no needle controller and
                // must never steal the registration.
                Assert.That(
                    needle.IsChildOf(root.transform),
                    Is.True,
                    "The registered needle belongs to the city build.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Build_AttendantStancesClearTheWeighbridgeObstacles()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            WeighbridgeAttendantPlan plan =
                WeighbridgeAttendantPlan.Create(layout);
            Assert.That(plan.IsPresent, Is.True);
            var parent = new GameObject("Attendant Stance Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);
                Physics.SyncTransforms();
                foreach (WeighbridgeAttendantStance stance in
                         plan.Stances)
                {
                    // The pacer is checked along his whole corridor.
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
                        // The paving is the ground they stand on, and
                        // the walkable deck is the worker's floor —
                        // neither is an obstacle to him.
                        if (collider.name ==
                            CityDistrictPointOfInterestWorldBuilder
                                .PublicGroundName)
                        {
                            continue;
                        }

                        if (stance.Strolls &&
                            collider.name ==
                            "Walkable Weighbridge Collider")
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
                        // corridor sample must stay clear of every
                        // weighbridge obstacle.
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
        public void Needle_PointOnDeckDetectionHonoursTheDeckRect()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            CityDistrictPointOfInterestDescriptor descriptor =
                FindWeighbridge(layout);
            Assert.That(
                CityDistrictPointOfInterestWorldBuilder
                    .TryDescribeWeighbridgeDeck(
                        descriptor,
                        out CityWeighbridgeDeckRect deck),
                Is.True);

            Assert.That(
                CityWeighbridgeNeedleController.IsPointOnDeck(
                    deck,
                    deck.Center),
                Is.True,
                "The deck centre carries weight.");
            Vector3 alongAxis = deck.Yaw * Vector3.forward;
            Assert.That(
                CityWeighbridgeNeedleController.IsPointOnDeck(
                    deck,
                    deck.Center +
                    alongAxis * (deck.HalfLength + 0.5f)),
                Is.False,
                "Past the deck end there is no weight.");
            Vector3 acrossAxis = deck.Yaw * Vector3.right;
            Assert.That(
                CityWeighbridgeNeedleController.IsPointOnDeck(
                    deck,
                    deck.Center +
                    acrossAxis * (deck.HalfWidth + 0.5f)),
                Is.False,
                "Beside the deck — the weigher's spot — is off scale.");
            Assert.That(
                CityWeighbridgeNeedleController.IsPointOnDeck(
                    deck,
                    deck.Center + Vector3.up * 2f),
                Is.False,
                "Far above the deck top nothing stands on it.");

            // The other three points of interest carry no deck.
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor other =
                    layout.DistrictPointsOfInterest[index];
                if (other.Kind ==
                    CityDistrictPointOfInterestKind
                        .IndustrialWeighbridge)
                {
                    continue;
                }

                Assert.That(
                    CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeWeighbridgeDeck(other, out _),
                    Is.False);
            }
        }

        [Test]
        public void Needle_DeflectionEasesUpAndSettlesBack()
        {
            // Loading: monotone rise toward full deflection.
            float deflection = 0f;
            float previous = 0f;
            for (int step = 0; step < 60; step++)
            {
                deflection =
                    CityWeighbridgeNeedleController.AdvanceDeflection(
                        deflection,
                        1f,
                        1f / 30f);
                Assert.That(deflection, Is.GreaterThan(previous));
                Assert.That(deflection, Is.LessThanOrEqualTo(1f));
                previous = deflection;
            }

            Assert.That(deflection, Is.GreaterThan(0.9f));

            // Unloading: monotone release, slower than the attack,
            // never overshooting past rest.
            float release =
                CityWeighbridgeNeedleController.AdvanceDeflection(
                    1f,
                    0f,
                    1f / 30f);
            float attack =
                CityWeighbridgeNeedleController.AdvanceDeflection(
                    0f,
                    1f,
                    1f / 30f);
            Assert.That(
                1f - release,
                Is.LessThan(attack),
                "The heavy dial releases slower than it takes load.");
            for (int step = 0; step < 240; step++)
            {
                deflection =
                    CityWeighbridgeNeedleController.AdvanceDeflection(
                        deflection,
                        0f,
                        1f / 30f);
                Assert.That(deflection, Is.GreaterThanOrEqualTo(0f));
                Assert.That(deflection, Is.LessThan(previous + 0.0001f));
                previous = deflection;
            }

            Assert.That(deflection, Is.LessThan(0.05f));
        }

        [Test]
        public void Pace_HoldsTheDeckCentreThroughTheWeighingPause()
        {
            // Before the pause the worker approaches the centre.
            Assert.That(
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(0f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(
                        WeighbridgeAttendantPresentation
                            .PauseStartNormalized * 0.5f),
                Is.EqualTo(0.25f).Within(0.0001f));

            // Through the whole authored pause he stands square at
            // the exact deck centre.
            for (float normalized =
                     WeighbridgeAttendantPresentation
                         .PauseStartNormalized;
                 normalized <=
                 WeighbridgeAttendantPresentation.PauseEndNormalized;
                 normalized += 0.02f)
            {
                Assert.That(
                    WeighbridgeAttendantPresentation
                        .EvaluateCorridorProgress(normalized),
                    Is.EqualTo(0.5f).Within(0.0001f),
                    "The weighing pause holds the deck centre.");
            }

            // After the pause he walks on to the far end.
            Assert.That(
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(1f),
                Is.EqualTo(1f).Within(0.0001f));
            float resumed =
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(
                        (WeighbridgeAttendantPresentation
                             .PauseEndNormalized + 1f) * 0.5f);
            Assert.That(resumed, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void Provider_BindsTheStagedPrefabWithoutPublishingIt()
        {
            WeighbridgeAttendantProvider provider =
                WeighbridgeAttendantProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Resources/City/WeighbridgeAttendantProvider.asset " +
                "is missing.");
            GameObject prefab = provider.StagedPrefab;
            Assert.That(
                prefab,
                Is.Not.Null,
                "The provider must reference the staged attendant " +
                "prefab.");

            var registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(WeighbridgeAttendantProvider.DesignId));
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(
                registry.IdleClip.name,
                Is.EqualTo("WeigherCheck"));
            Assert.That(registry.WalkClip, Is.Not.Null);
            Assert.That(
                registry.WalkClip.name,
                Is.EqualTo("WeighedPace"));
            Assert.That(registry.SitClip, Is.Null);
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);

            // The staged prefab ships the weigher's chalk; the
            // runtime hides it for the worker's free hands.
            var rendererNames = new HashSet<string>();
            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                rendererNames.Add(renderer.name);
            }

            Assert.That(
                rendererNames,
                Is.SupersetOf(new[] { "ACC_Chalk" }));

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                Resources.Load<GameObject>(
                    "Pedestrians/WeighbridgeAttendant3D"),
                Is.Null,
                "The staged attendant must stay outside Resources.");
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

        private static CityDistrictPointOfInterestDescriptor
            FindWeighbridge(CityLayout layout)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (layout.DistrictPointsOfInterest[index].Kind ==
                    CityDistrictPointOfInterestKind
                        .IndustrialWeighbridge)
                {
                    return layout.DistrictPointsOfInterest[index];
                }
            }

            Assert.Fail("The default city builds a weighbridge.");
            return default;
        }
    }
}
