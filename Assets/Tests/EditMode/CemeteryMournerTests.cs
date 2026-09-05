using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CemeteryMournerTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Plan_CollectsOnlyUnenclosedGravesAtTheFootSide()
        {
            (CityLayout _, CityCemeteryPlan plan) = GenerateCemetery();
            List<CemeteryGraveAnchor> candidates =
                CemeteryMournerPlan.CollectCandidateGraves(plan);

            Assert.That(candidates, Is.Not.Empty,
                "The default cemetery must offer graves to visit.");

            var enclosed = new HashSet<int>();
            foreach (CityCemeteryPartDescriptor part in plan.Parts)
            {
                if (part.Kind == CityCemeteryPartKind.GraveEnclosure)
                {
                    enclosed.Add(part.GraveOrdinal);
                }
            }

            foreach (CemeteryGraveAnchor anchor in candidates)
            {
                Assert.That(enclosed.Contains(anchor.Ordinal), Is.False,
                    $"Grave {anchor.Ordinal} has an оградка and must " +
                    "not be visited.");
                Assert.That(
                    anchor.Ground.y,
                    Is.EqualTo(plan.GroundTopY).Within(0.001f));

                Vector3 stand = CemeteryMournerPlan.ComputeStandPoint(
                    anchor,
                    plan.GroundTopY);
                Vector3 facing =
                    CemeteryMournerPlan.ComputeStandFacing(anchor);
                Assert.That(
                    plan.Grounds.Contains(
                        new Vector2(stand.x, stand.z)),
                    Is.True,
                    "The stand point stays inside the grounds.");
                Assert.That(
                    Vector3.Distance(stand, anchor.Ground),
                    Is.EqualTo(CemeteryMournerPlan.StandBackMeters)
                        .Within(0.001f));
                Assert.That(
                    facing.magnitude,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(Mathf.Abs(facing.y), Is.LessThan(0.001f));

                // She stands at the foot and faces the monument: the
                // grave's ground point lies straight ahead of her.
                Vector3 toGrave = anchor.Ground - stand;
                toGrave.y = 0f;
                Assert.That(
                    Vector3.Dot(facing, toGrave.normalized),
                    Is.GreaterThan(0.99f));
            }
        }

        [Test]
        public void Plan_AccessNormalPointsIntoTheGrounds()
        {
            (CityLayout layout, CityCemeteryPlan plan) =
                GenerateCemetery();
            Assert.That(
                CemeteryMournerPlan.TryGetAccess(
                    layout,
                    out CityOpenAreaAccessDescriptor access),
                Is.True);

            // The defensive check behind the whole route geometry:
            // despite its name, OutwardNormal points from the street
            // into the grounds.
            Vector3 probe = access.Center +
                access.OutwardNormal.normalized * 2f;
            Assert.That(
                plan.Grounds.Contains(new Vector2(probe.x, probe.z)),
                Is.True,
                "OutwardNormal must point from the street into the " +
                "cemetery grounds.");
        }

        [Test]
        public void Plan_SelectsGravesDeterministicallyPerVisit()
        {
            (CityLayout _, CityCemeteryPlan plan) = GenerateCemetery();
            List<CemeteryGraveAnchor> candidates =
                CemeteryMournerPlan.CollectCandidateGraves(plan);

            var chosen = new HashSet<int>();
            for (int visit = 0; visit < 8; visit++)
            {
                uint first = CemeteryMournerPlan.CreateVisitRandomState(
                    Seed,
                    visit);
                uint second = CemeteryMournerPlan.CreateVisitRandomState(
                    Seed,
                    visit);
                int firstIndex = CemeteryMournerPlan.SelectGraveIndex(
                    candidates.Count,
                    ref first);
                int secondIndex = CemeteryMournerPlan.SelectGraveIndex(
                    candidates.Count,
                    ref second);
                Assert.That(firstIndex, Is.EqualTo(secondIndex),
                    "The same visit must mourn at the same grave.");
                Assert.That(
                    firstIndex,
                    Is.InRange(0, candidates.Count - 1));
                chosen.Add(firstIndex);
            }

            Assert.That(chosen.Count, Is.GreaterThan(1),
                "Eight visits must not all pick one grave.");
        }

        [Test]
        public void Plan_RouteEntersThroughTheGateAndStaysInside()
        {
            (CityLayout layout, CityCemeteryPlan plan) =
                GenerateCemetery();
            CemeteryMournerPlan.TryGetAccess(
                layout,
                out CityOpenAreaAccessDescriptor access);
            List<CemeteryGraveAnchor> candidates =
                CemeteryMournerPlan.CollectCandidateGraves(plan);
            Vector3 inward = access.OutwardNormal.normalized;

            Vector3 spawn = CemeteryMournerPlan.SelectSpawnPoint(
                access,
                access.Center - inward * 3f,
                access.Center - inward * 3f,
                inward);
            foreach (CemeteryGraveAnchor anchor in candidates)
            {
                Vector3 stand = CemeteryMournerPlan.ComputeStandPoint(
                    anchor,
                    plan.GroundTopY);
                Vector3[] route =
                    CemeteryMournerPlan.BuildApproachRoute(
                        layout,
                        access,
                        plan,
                        spawn,
                        stand);

                Assert.That(route, Has.Length.EqualTo(5));
                Assert.That(route[0], Is.EqualTo(spawn));
                Assert.That(
                    plan.Grounds.Contains(
                        new Vector2(route[0].x, route[0].z)),
                    Is.False,
                    "The mourner spawns outside the grounds.");
                Assert.That(
                    plan.Grounds.Contains(
                        new Vector2(route[1].x, route[1].z)),
                    Is.False,
                    "The outer gate waypoint stays on the street side.");

                // She enters through the one gate, never over the
                // fence: both threshold waypoints hug the access
                // centre.
                Assert.That(
                    PlanarDistance(route[1], access.Center),
                    Is.LessThan(4f));
                Assert.That(
                    PlanarDistance(route[2], access.Center),
                    Is.LessThan(4f));

                for (int index = 2; index < route.Length; index++)
                {
                    Assert.That(
                        plan.Grounds.Contains(
                            new Vector2(route[index].x, route[index].z)),
                        Is.True,
                        $"Waypoint {index} stays inside the grounds.");
                    Assert.That(
                        route[index].y,
                        Is.EqualTo(plan.GroundTopY).Within(0.001f));
                }

                Assert.That(
                    PlanarDistance(route[route.Length - 1], stand),
                    Is.LessThan(0.001f));

                float length =
                    CemeteryMournerPlan.ComputeRouteLength(route);
                Assert.That(length, Is.GreaterThan(10f));
                Vector3 end = CemeteryMournerPlan.EvaluateRoute(
                    route,
                    length + 5f,
                    out _);
                Assert.That(
                    PlanarDistance(end, stand),
                    Is.LessThan(0.001f),
                    "Overshooting the route clamps at the stand point.");
            }
        }

        [Test]
        public void Plan_SpawnPointIsFarOrOutsideTheCameraCone()
        {
            (CityLayout layout, CityCemeteryPlan _) =
                GenerateCemetery();
            CemeteryMournerPlan.TryGetAccess(
                layout,
                out CityOpenAreaAccessDescriptor access);
            Vector3 inward = access.OutwardNormal.normalized;

            // The hero stands on the street before the gate, looking
            // at the cemetery: candidates along the street flank are
            // outside the view cone and qualify immediately.
            Vector3 player = access.Center - inward * 3f;
            Vector3 spawn = CemeteryMournerPlan.SelectSpawnPoint(
                access,
                player,
                player,
                inward);

            Vector3 toSpawn = spawn - player;
            toSpawn.y = 0f;
            float viewDot = Vector3.Dot(toSpawn.normalized, inward);
            bool farEnough = toSpawn.magnitude >=
                CemeteryMournerPlan.MinimumSpawnDistance;
            bool unseen = viewDot <
                CemeteryMournerPlan.SpawnViewCosine;
            Assert.That(farEnough || unseen, Is.True,
                "The spawn honours the director's distance-or-unseen " +
                "rule.");
            Assert.That(
                plan_ContainsSpawn(layout, spawn),
                Is.False,
                "The mourner never spawns inside the grounds.");
        }

        [Test]
        public void Timeline_RunsThePhasesInOrderWithAThirtySecondCry()
        {
            var timeline = new CemeteryMournerTimeline(10f, 12f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.Approach));
            Assert.That(timeline.ConsumeLayCue(), Is.False,
                "No lay cue while she is still walking in.");

            timeline.Advance(10f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.LayFlowers));
            Assert.That(timeline.ConsumeLayCue(), Is.False,
                "The cue waits for the authored bow moment.");

            timeline.Advance(CemeteryMournerTimeline.LayCueSeconds);
            Assert.That(timeline.ConsumeLayCue(), Is.True);
            Assert.That(timeline.ConsumeLayCue(), Is.False,
                "The lay cue is a one-shot.");

            timeline.Advance(
                CemeteryMournerTimeline.LaySeconds -
                CemeteryMournerTimeline.LayCueSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.Cry));

            // The user contract: the crying lasts exactly thirty
            // seconds before she wipes her eyes.
            timeline.Advance(29.9f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.Cry));
            timeline.Advance(0.1f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.WipeTears));

            timeline.Advance(CemeteryMournerTimeline.WipeSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.Depart));

            timeline.Advance(11.9f);
            Assert.That(timeline.IsDone, Is.False);
            timeline.Advance(0.2f);
            Assert.That(timeline.IsDone, Is.True);
        }

        [Test]
        public void Timeline_CarriesTheRemainderAcrossPhaseBoundaries()
        {
            var timeline = new CemeteryMournerTimeline(1f, 1f);
            // One oversized hitch step still lands mid-Cry instead of
            // stretching the ritual.
            timeline.Advance(
                1f + CemeteryMournerTimeline.LaySeconds + 5f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(CemeteryMournerPhase.Cry));
            Assert.That(
                timeline.PhaseElapsed,
                Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void Plan_TriggerBandCoversTheApproachNotTheWholeCity()
        {
            (CityLayout _, CityCemeteryPlan plan) = GenerateCemetery();
            Vector2 centre = plan.Grounds.center;
            Assert.That(
                CemeteryMournerPlan.IsInsideTriggerBand(
                    plan.Grounds,
                    centre),
                Is.True,
                "Standing among the graves counts as near.");
            Assert.That(
                CemeteryMournerPlan.IsInsideTriggerBand(
                    plan.Grounds,
                    new Vector2(plan.Grounds.xMin - 10f, centre.y)),
                Is.True,
                "The street along the fence counts as near.");
            Assert.That(
                CemeteryMournerPlan.IsInsideTriggerBand(
                    plan.Grounds,
                    new Vector2(
                        plan.Grounds.xMin -
                        CemeteryMournerPlan.TriggerInflationMeters -
                        10f,
                        centre.y)),
                Is.False,
                "Two blocks away is not near the cemetery.");
        }

        /// <summary>
        /// The pure half of laying the bouquet: a prop standing upright
        /// (stems at the root, bloom 0.4 m above) is rotated so its axis
        /// lies along grave-local +Z, and the offset then centres the
        /// rotated box on the slab point with its bottom exactly on the
        /// slab. Every number here is chosen so the expected pose is
        /// known by hand.
        /// </summary>
        [Test]
        public void LaidBouquet_PureMathLaysTheAxisAlongPlusZOnTheSlab()
        {
            var stems = new Vector3(0f, 0.05f, 0f);
            var bloom = new Vector3(0f, 0.45f, 0f);
            var boundsMin = new Vector3(-0.06f, -0.02f, -0.06f);
            var boundsMax = new Vector3(0.06f, 0.50f, 0.06f);

            CemeteryLaidBouquet.ComputeLaidPose(
                stems,
                bloom,
                boundsMin,
                boundsMax,
                out Quaternion rotation,
                out Vector3 offset);

            Vector3 laidAxis = rotation * (bloom - stems).normalized;
            Assert.That(
                Vector3.Dot(laidAxis, Vector3.forward),
                Is.GreaterThan(0.9999f),
                "The stems-to-bloom axis must lie along grave-local +Z.");

            CemeteryLaidBouquet.RotateBounds(
                rotation,
                boundsMin,
                boundsMax,
                out Vector3 laidMin,
                out Vector3 laidMax);
            // The upright 0.52 m box lies down: 0.52 m long along Z,
            // 0.12 m wide and tall.
            Assert.That(laidMax.z - laidMin.z, Is.EqualTo(0.52f).Within(0.0001f));
            Assert.That(laidMax.y - laidMin.y, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(laidMax.x - laidMin.x, Is.EqualTo(0.12f).Within(0.0001f));

            // Shifted by the offset, the box bottom is at y = 0 and its
            // XZ centre at the origin: the slab point.
            Assert.That(laidMin.y + offset.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                0.5f * (laidMin.x + laidMax.x) + offset.x,
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                0.5f * (laidMin.z + laidMax.z) + offset.z,
                Is.EqualTo(0f).Within(0.0001f));

            // Degenerate axis: identity, never NaN.
            Assert.That(
                CemeteryLaidBouquet.ComputeLaidRotation(stems, stems),
                Is.EqualTo(Quaternion.identity));
        }

        /// <summary>
        /// The bouquet actually placed on a real grave of the default
        /// cemetery: the SAME funeral-bouquet hand prop she carried,
        /// resting on the slab top (not floating over the thin slabs,
        /// not sunk into the thick one), inside the slab's footprint,
        /// with its stems-to-bloom axis pointing at the stone. Measured
        /// in world space under a rotated, offset parent so the frame
        /// conversions are exercised, not assumed.
        /// </summary>
        [Test]
        public void LaidBouquet_RestsOnTheSlabInsideTheGraveFootprint()
        {
            (CityLayout _, CityCemeteryPlan plan) = GenerateCemetery();
            List<CemeteryGraveAnchor> candidates =
                CemeteryMournerPlan.CollectCandidateGraves(plan);
            Assert.That(candidates, Is.Not.Empty);
            Assert.That(
                CityPedestrianHandProps.IsAvailable(
                    CityPedestrianHandPropId.FuneralBouquet),
                Is.True,
                "The funeral bouquet hand prop prefab is missing.");

            var parentObject = new GameObject("Laid Bouquet Test");
            parentObject.transform.SetPositionAndRotation(
                new Vector3(3.5f, -1.25f, -7f),
                Quaternion.Euler(0f, 37f, 0f));
            try
            {
                // Three graves, so more than one slab thickness and yaw
                // is covered when the default cemetery offers them.
                int graveCount = Mathf.Min(3, candidates.Count);
                for (int index = 0; index < graveCount; index++)
                {
                    CemeteryGraveAnchor anchor = candidates[index];
                    CityCemeteryPartDescriptor slab = FindSlab(plan, anchor);
                    Assert.That(
                        anchor.SlabTopY,
                        Is.EqualTo(slab.Center.y + slab.Size.y * 0.5f)
                            .Within(0.0001f),
                        "SlabTopY must be the slab's real top face.");
                    Assert.That(
                        anchor.SlabTopY - plan.GroundTopY,
                        Is.InRange(0.05f, 0.20f),
                        "Every grave variant stands a 0.08-0.16 m slab.");

                    Vector3 slabPoint =
                        CityCemeteryMournerController.ComputeLaidBouquetSlabPoint(
                            anchor);
                    Assert.That(
                        slabPoint.y,
                        Is.EqualTo(anchor.SlabTopY).Within(0.0001f));

                    CityPedestrianHandPropRegistry bouquet =
                        CemeteryLaidBouquet.Place(
                            parentObject.transform,
                            slabPoint,
                            anchor.Yaw,
                            null,
                            index % 4);
                    try
                    {
                        Assert.That(bouquet, Is.Not.Null);
                        Assert.That(
                            bouquet.Id,
                            Is.EqualTo(CityPedestrianHandPropId.FuneralBouquet));
                        Assert.That(
                            bouquet.name,
                            Is.EqualTo(CemeteryLaidBouquet.RuntimeObjectName));
                        Assert.That(
                            bouquet.transform.parent,
                            Is.SameAs(parentObject.transform));
                        Assert.That(
                            bouquet.PaletteVariant,
                            Is.EqualTo(index % 4));

                        MeasureWorldBounds(
                            bouquet,
                            out Vector3 min,
                            out Vector3 max);

                        // Resting on the slab: the lowest point on the top
                        // face within a centimetre, the whole thing lower
                        // than a hand's breadth above it.
                        Assert.That(
                            min.y,
                            Is.GreaterThanOrEqualTo(anchor.SlabTopY - 0.01f),
                            $"Grave {anchor.Ordinal}: the bouquet sinks " +
                            $"{anchor.SlabTopY - min.y:F3} m into the slab.");
                        // The helper rests the AABB of the rotated AABB,
                        // which is conservative: the true lowest point
                        // may hover by the box inflation, never sink.
                        Assert.That(
                            min.y,
                            Is.LessThanOrEqualTo(anchor.SlabTopY + 0.06f),
                            $"Grave {anchor.Ordinal}: the bouquet floats " +
                            $"{min.y - anchor.SlabTopY:F3} m over the slab.");
                        Assert.That(
                            max.y,
                            Is.LessThanOrEqualTo(anchor.SlabTopY + 0.20f),
                            $"Grave {anchor.Ordinal}: the bouquet stands " +
                            $"{max.y - anchor.SlabTopY:F3} m tall.");

                        // Inside the slab's footprint, judged in the
                        // grave's own frame (+Z toward the stone).
                        Quaternion inverseYaw = Quaternion.Inverse(anchor.Yaw);
                        Vector3 halfSlab = slab.Size * 0.5f;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            var world = new Vector3(
                                (corner & 1) == 0 ? min.x : max.x,
                                (corner & 2) == 0 ? min.y : max.y,
                                (corner & 4) == 0 ? min.z : max.z);
                            Vector3 graveLocal =
                                inverseYaw * (world - anchor.Ground);
                            Assert.That(
                                Mathf.Abs(graveLocal.x),
                                Is.LessThanOrEqualTo(halfSlab.x + 0.02f),
                                $"Grave {anchor.Ordinal}: the bouquet " +
                                "overhangs the slab's side.");
                            Assert.That(
                                Mathf.Abs(graveLocal.z),
                                Is.LessThanOrEqualTo(halfSlab.z + 0.02f),
                                $"Grave {anchor.Ordinal}: the bouquet " +
                                "overhangs the slab's end.");
                        }

                        // Centred on the slab point in XZ, where the old
                        // boxes were.
                        Vector3 centre = 0.5f * (min + max);
                        Assert.That(
                            PlanarDistance(centre, slabPoint),
                            Is.LessThan(0.06f),
                            $"Grave {anchor.Ordinal}: the bouquet is not " +
                            "centred on the slab point.");

                        // Blooms toward the stone: stems to bloom along
                        // the grave's +Z.
                        Vector3 stemsCentre = MeasureMeshCentre(
                            bouquet.FindRenderer(
                                CemeteryLaidBouquet.StemsRendererName));
                        Vector3 bloomCentre = MeasureMeshCentre(
                            bouquet.FindRenderer(
                                CemeteryLaidBouquet.BloomRendererName));
                        Vector3 axis = (bloomCentre - stemsCentre).normalized;
                        Assert.That(
                            Vector3.Dot(axis, anchor.Yaw * Vector3.forward),
                            Is.GreaterThan(0.95f),
                            $"Grave {anchor.Ordinal}: the bouquet does not " +
                            "point at the stone.");
                    }
                    finally
                    {
                        CityPedestrianHandProps.Detach(ref bouquet);
                    }

                    Assert.That(bouquet, Is.Null,
                        "Detach must null the reference.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parentObject);
            }
        }

        private static CityCemeteryPartDescriptor FindSlab(
            CityCemeteryPlan plan,
            CemeteryGraveAnchor anchor)
        {
            foreach (CityCemeteryPartDescriptor part in plan.Parts)
            {
                if (part.Kind == CityCemeteryPartKind.GraveSlab &&
                    part.GraveOrdinal == anchor.Ordinal)
                {
                    return part;
                }
            }

            Assert.Fail($"Grave {anchor.Ordinal} has no slab part.");
            return default;
        }

        /// <summary>World AABB of every prop part from its mesh bounds
        /// through its transform: the prop meshes import non-readable,
        /// and for a rigid part the box corners bound every vertex.</summary>
        private static void MeasureWorldBounds(
            CityPedestrianHandPropRegistry registry,
            out Vector3 min,
            out Vector3 max)
        {
            min = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            max = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            bool any = false;
            foreach (Renderer renderer in registry.Renderers)
            {
                var filter = renderer != null
                    ? renderer.GetComponent<MeshFilter>()
                    : null;
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Assert.That(mesh, Is.Not.Null, "A bouquet part has no mesh.");
                Bounds local = mesh.bounds;
                Matrix4x4 localToWorld = renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = localToWorld.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z));
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    any = true;
                }
            }

            Assert.That(any, Is.True, "The bouquet has no parts.");
        }

        private static Vector3 MeasureMeshCentre(Renderer renderer)
        {
            Assert.That(renderer, Is.Not.Null);
            var filter = renderer.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null, renderer.name);
            Assert.That(filter.sharedMesh, Is.Not.Null, renderer.name);
            return renderer.localToWorldMatrix.MultiplyPoint3x4(
                filter.sharedMesh.bounds.center);
        }

        private static (CityLayout, CityCemeteryPlan) GenerateCemetery()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityCemeteryPlan plan = CityCemeteryPlanner.Create(layout);
            Assert.That(plan, Is.Not.Null,
                "The default city must carry a dressable cemetery.");
            return (layout, plan);
        }

        private static bool plan_ContainsSpawn(
            CityLayout layout,
            Vector3 spawn)
        {
            CityCemeteryPlan plan = CityCemeteryPlanner.Create(layout);
            return plan.Grounds.Contains(new Vector2(spawn.x, spawn.z));
        }

        private static float PlanarDistance(Vector3 left, Vector3 right)
        {
            return Vector2.Distance(
                new Vector2(left.x, left.z),
                new Vector2(right.x, right.z));
        }
    }
}
