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
