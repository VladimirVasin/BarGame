using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class VillageNeighbourReport
        {
            public int resident_count;
            public int hero_triangles;
            public string[] roles;
            public int[] resident_triangles;
            public int[] completed_outings;
            public int maximum_outdoor_count;
            public int simulated_steps;
            public float simulated_seconds;
            public float maximum_root_step_metres;
            public int body_clearance_samples;
            public int body_clearance_pairs;
            public float maximum_body_overlap_metres;
            public int door_passages;
            public int gust_reactions;
            public int yields;
            public int snow_work_cycles;
            public int shovel_contact_samples;
            public int closed_basket_contact_samples;
            public float maximum_shovel_grip_error_metres;
            public float maximum_closed_basket_grip_error_metres;
            public bool hero_blocks_neighbour;
            public bool pause_freezes_six_residents;
            public bool all_four_returned_home_at_2100;
            public bool homes_occlude_body_and_head;
            public bool day_outings_resume_at_1000;
            public bool same_six_active_models_throughout;
            public bool visiting_player_keeps_exit_open;
        }

        private sealed class VillageNeighbourProbe
        {
            public VillageNeighbourState State;
            public VillageResidentPresentation Actor;
            public Renderer[] Renderers;
            public CapsuleCollider Body;
            public Vector3 PreviousPosition;
        }

        /// <summary>Continues the existing single village journey after finite
        /// firewood delivery. The world and the six imported people remain live.</summary>
        private static IEnumerator VerifyVillageNeighbours(AlpineVillageRoot village, Camera camera)
        {
            AlpineVillageLifeController life = village.Life;
            Assert.That(life.Neighbours.Count, Is.EqualTo(6));
            Assert.That(life.GetComponentsInChildren<VillageResidentPresentation>(true).Length, Is.EqualTo(6));
            Assert.That(life.Shovel, Is.Not.Null);
            Assert.That(life.ClosedBasket, Is.Not.Null);
            var hero = (Player3DCharacterPresentation)village.Player.Visual;
            var report = new VillageNeighbourReport
            {
                resident_count = life.Neighbours.Count,
                hero_triangles = VillageLifeTriangleCount(hero.Registry.transform),
                roles = new string[6], resident_triangles = new int[6], completed_outings = new int[6]
            };
            var probes = new VillageNeighbourProbe[6];
            var identities = new HashSet<VillageResidentPresentation>();
            var roles = new HashSet<string>(StringComparer.Ordinal);
            int comparison = 0;
            for (int i = 0; i < probes.Length; i++)
            {
                VillageNeighbourState state = life.Neighbours[i];
                Assert.That(state.Actor, Is.Not.Null);
                Assert.That(identities.Add(state.Actor), Is.True, "Every permanent role needs its own actual actor.");
                Assert.That(roles.Add(state.Role.ToString()), Is.True);
                Assert.That(state.Actor.Role, Is.EqualTo(state.Role));
                probes[i] = new VillageNeighbourProbe { State = state, Actor = state.Actor,
                    Renderers = state.Actor.ModelRoot.GetComponentsInChildren<Renderer>(true),
                    Body = state.Actor.GetComponent<CapsuleCollider>() };
                Assert.That(probes[i].Body, Is.Not.Null, "The actual resident needs a body capsule: " + state.Role);
                Assert.That(probes[i].Body.isTrigger, Is.False);
                report.roles[i] = state.Role.ToString();
                report.resident_triangles[i] = AssertVillageResidentDetail(state.Actor, report.hero_triangles);
                if (!IsAdditionalVillageNeighbour(state)) continue;
                // Only this paired portrait moves a subject. Restore the actual
                // route root and sampled pose before measuring any simulation.
                Transform actor = state.Actor.transform;
                Vector3 position = actor.position;
                Quaternion rotation = actor.rotation;
                VillageResidentAction action = state.Actor.CurrentAction;
                float seconds = state.Actor.CurrentActionSeconds;
                try
                {
                    actor.SetPositionAndRotation(village.Plan.Lane.Sample(life.Plan.House.LaneDistance).Position,
                        Quaternion.LookRotation(life.Plan.Forward));
                    state.Actor.Apply(VillageResidentAction.Idle, 0f);
                    CaptureVillageResidentComparison(village, camera, state.Actor,
                        "10-neighbour-" + state.Role.ToString().ToLowerInvariant());
                }
                finally
                {
                    actor.SetPositionAndRotation(position, rotation);
                    state.Actor.Apply(action, seconds);
                }
                comparison++;
            }
            Assert.That(comparison, Is.EqualTo(4));
            CollectionAssert.AreEquivalent(new[] { "WoodWoman", "StationWorker", "RepairNeighbor",
                "SewingWoman", "SnowNeighbor", "BasketVisitor" }, roles);
            foreach (VillageNeighbourProbe probe in probes) probe.PreviousPosition = probe.Actor.transform.position;
            ParkVillageLifeHero(village);

            Assert.That(village.World.ResidentDoors.Count, Is.EqualTo(3));
            var doorHinges = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (var pair in village.World.ResidentDoors)
            {
                Assert.That(pair.Value.Leaf, Is.Not.Null);
                Assert.That(pair.Value.Handle, Is.Not.Null);
                Assert.That(pair.Value.HouseRoot, Is.Not.Null);
                Assert.That(pair.Value.HiddenDocks.Count, Is.EqualTo(2));
                doorHinges.Add(pair.Key, pair.Value.Hinge.position);
            }
            Collider[] clearanceSolids = CollectVillageNeighbourClearanceSolids(village);
            CaptureVillageNeighbourYard(village, camera, "village-house-08", "11-middle-courtyard");
            CaptureVillageNeighbourYard(village, camera, "village-house-11", "12-upper-courtyard-gate");

            int initialPassages = life.TotalDoorPassages;
            int initialGusts = life.GustReactions;
            int initialYields = life.YieldCount;
            int initialSnow = life.SnowWorkCycles;
            bool entranceCaptured = false, gustCaptured = false, shovelCaptured = false, basketCaptured = false;
            bool blockingStarted = false;
            VillageNeighbourProbe blocker = null;
            int blockingSteps = 0;
            Vector3 blockPosition = Vector3.zero;
            Vector3 wind = new Vector3(.8f, 0f, .6f).normalized;
            TestContext.Out.WriteLine("Village neighbours: six real models verified; starting bounded 10:00 household journeys.");
            try
            {
                // Usually finishes before the cap; the cap also catches a
                // household route that repeatedly restarts without reaching home.
                for (int step = 0; step < 24000; step++)
                {
                    float rawGust = step % 1500 < 250 ? .97f : .04f;
                    life.Advance(VillageLifeStep, 600d, rawGust, wind);
                    ObserveVillageNeighbours(life, probes, report, clearanceSolids);
                    ObserveVillageNeighbourProps(life, report, camera, ref shovelCaptured, ref basketCaptured);
                    foreach (var pair in village.World.ResidentDoors)
                    {
                        if (Vector3.Distance(pair.Value.Hinge.position, doorHinges[pair.Key]) > .001f)
                            Assert.Fail("The real door hinge moved while its leaf opened: " + pair.Key);
                        if (!entranceCaptured && pair.Value.Occupant != null && pair.Value.OpenFraction > .35f)
                        {
                            Vector3 direction = (pair.Value.ExteriorDock - pair.Value.ThresholdDock).normalized;
                            VillageLifeFrame(camera, "13-real-house-door-passage", pair.Value.ExteriorDock + direction * 2.3f +
                                Vector3.Cross(Vector3.up, direction) * 1.05f + Vector3.up * 1.5f,
                                pair.Value.ThresholdDock + Vector3.up * 1.0f, 45f);
                            entranceCaptured = true;
                        }
                    }
                    if (!gustCaptured)
                        foreach (VillageNeighbourProbe probe in probes)
                        {
                            if (!IsAdditionalVillageNeighbour(probe.State) || !probe.State.IsOutside ||
                                !probe.State.IsReactingToGust) continue;
                            Transform actor = probe.Actor.transform;
                            VillageLifeFrame(camera, "14-neighbour-wind-reaction", actor.position + actor.forward * 2.7f +
                                actor.right * 1.3f + Vector3.up * 1.4f, actor.position + Vector3.up * 1.0f, 48f);
                            gustCaptured = true;
                            break;
                        }
                    if (!blockingStarted && step > 300 && rawGust < .1f)
                        foreach (VillageNeighbourProbe probe in probes)
                        {
                            if (!IsAdditionalVillageNeighbour(probe.State) || !probe.State.IsOutside ||
                                !probe.State.IsWalkingOutside || probe.State.IsReactingToGust || probe.State.IsBlocked ||
                                !probe.Actor.CurrentAction.ToString().Contains("Walk")) continue;
                            blocker = probe;
                            blockPosition = probe.Actor.transform.position;
                            village.Player.Motor.Teleport(blockPosition + probe.Actor.transform.forward * .50f +
                                Vector3.up * PlayerFactory.GroundedRootOffset);
                            Physics.SyncTransforms();
                            blockingStarted = true;
                            break;
                        }
                    else if (blocker != null)
                    {
                        blockingSteps++;
                        if (blockingSteps > 2)
                        {
                            Assert.That(blocker.State.IsBlocked || blocker.State.IsYielding, Is.True,
                                "A new neighbour must wait for the real hero instead of crossing his capsule.");
                            Assert.That(Vector3.Distance(blockPosition, blocker.Actor.transform.position), Is.LessThan(.045f));
                        }
                        if (blockingSteps == 30)
                        {
                            report.hero_blocks_neighbour = true;
                            VillageLifeFrame(camera, "15-neighbour-waits-for-hero", blocker.Actor.transform.position +
                                blocker.Actor.transform.right * 3.0f + Vector3.up * 1.5f,
                                blocker.Actor.transform.position + Vector3.up * 1.0f, 48f);
                            yield return VerifyVillageNeighbourPause(village, probes, report);
                            ParkVillageLifeHero(village);
                            blocker = null;
                        }
                    }
                    if (step > 3000 && AllAdditionalNeighboursCompleted(probes) &&
                        report.hero_blocks_neighbour && entranceCaptured && gustCaptured && shovelCaptured && basketCaptured)
                        break;
                    if (step % 120 == 0) yield return null;
                    if (step % 2500 == 0)
                        TestContext.Out.WriteLine($"Village neighbours: {step * VillageLifeStep:0}s day; " +
                            $"outside {life.OutdoorCount}; doors {life.TotalDoorPassages}; snow {life.SnowWorkCycles}.");
                }
                bool completedJourneys = AllAdditionalNeighboursCompleted(probes);
                string stalledJourneyDetails = string.Empty;
                if (!completedJourneys)
                {
                    object Field(object source, string name) => source?.GetType().GetField(name,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(source);
                    var states = new List<string>();
                    var area = (AlpineVillageWalkableArea)Field(life, "walkable");
                    foreach (VillageNeighbourProbe probe in probes)
                    {
                        VillageNeighbourState state = probe.State;
                        object queued = Field(state, "Steps");
                        object current = null;
                        foreach (object item in (IEnumerable)queued) { current = item; break; }
                        var points = Field(current, "Points") as Vector3[];
                        int index = Convert.ToInt32(Field(state, "PointIndex"));
                        Vector3? target = Field(state, "YieldPoint") as Vector3?;
                        if (!target.HasValue && points != null && index < points.Length) target = points[index];
                        Vector3 position = state.Actor.transform.position;
                        string nextWalkable = "n/a";
                        if (target.HasValue)
                        {
                            Vector3 delta = target.Value - position; delta.y = 0f;
                            nextWalkable = area.Contains(position + delta.normalized * Mathf.Min(.0164f, delta.magnitude), .26f).ToString();
                        }
                        var door = (VillageResidentDoor)Field(state, "Door");
                        string owner = door.Occupant == null ? "none" : door.Occupant.name + "@" + door.Occupant.position.ToString("F3");
                        states.Add($"{state.Role}: task={state.Task}; pos={position:F3}; blocked={state.IsBlocked}; " +
                            $"yielding={state.IsYielding}; yield={Field(state, "YieldPoint")}; target={target}; " +
                            $"point={index}/{points?.Length}; steps={queued.GetType().GetProperty("Count").GetValue(queued)}; " +
                            $"indoors={Field(current, "Indoors")}; walkableNext={nextWalkable}; time={Field(state, "Time")}; " +
                            $"outings={state.CompletedOutings}; outside={state.IsOutside}; door={door.PlotId}; " +
                            $"open={door.OpenFraction:F3}; owner={owner}");
                    }
                    stalledJourneyDetails = "\n" + string.Join("\n", states);
                    TestContext.Out.WriteLine(stalledJourneyDetails);
                }
                Assert.That(completedJourneys, Is.True,
                    "Each of the four new neighbours must complete an actual outing within 480 simulated seconds." + stalledJourneyDetails);
                Assert.That(report.hero_blocks_neighbour && report.pause_freezes_six_residents, Is.True);
                Assert.That(entranceCaptured && gustCaptured && shovelCaptured && basketCaptured, Is.True,
                    "The bounded journey must show a real door passage, wind reaction, shovel and closed-basket contacts.");
                Assert.That(life.TotalDoorPassages, Is.GreaterThan(initialPassages));
                Assert.That(life.GustReactions, Is.GreaterThan(initialGusts));
                Assert.That(life.YieldCount, Is.GreaterThan(initialYields));
                Assert.That(life.SnowWorkCycles, Is.GreaterThan(initialSnow));

                TestContext.Out.WriteLine("Village neighbours: day journeys complete; testing return home at 21:00.");
                for (int step = 0; step < 12000; step++)
                {
                    life.Advance(VillageLifeStep, 1260d, .04f, wind);
                    ObserveVillageNeighbours(life, probes, report, clearanceSolids);
                    if (AllAdditionalNeighboursHome(probes) && AllVillageResidentDoorsClosed(village)) break;
                    if (step % 120 == 0) yield return null;
                }
                Assert.That(AllAdditionalNeighboursHome(probes), Is.True,
                    "Night must finish the active work and return all six neighbours to their real homes. " +
                    string.Join("; ", Array.ConvertAll(probes, p => $"{p.State.Role}: {p.State.Task}, {p.Actor.transform.position}, blocked={p.State.IsBlocked}, yielding={p.State.IsYielding}")));
                Assert.That(AllVillageResidentDoorsClosed(village), Is.True);
                report.all_four_returned_home_at_2100 = true;
                Physics.SyncTransforms();
                foreach (VillageNeighbourProbe probe in probes)
                {
                    if (!IsAdditionalVillageNeighbour(probe.State)) continue;
                    bool concealed = false;
                    foreach (var pair in village.World.ResidentDoors)
                    {
                        var door = pair.Value;
                        if (!door.IsConcealed(probe.Actor.transform.position)) continue;
                        AssertVillageHomeOcclusion(door, probe.Actor.transform.position + Vector3.up * .95f);
                        AssertVillageHomeOcclusion(door, probe.Actor.Head.position);
                        concealed = true;
                        break;
                    }
                    Assert.That(concealed, Is.True, "The home actor must occupy a real concealed interior dock: " + probe.State.Role);
                }
                report.homes_occlude_body_and_head = true;
                foreach (var door in village.World.ResidentDoors.Values)
                {
                    Transform owner = probes[2].Actor.transform;
                    Assert.That(door.TryReserve(owner), Is.True);
                    door.SetOpenFraction(owner, 1f);
                    village.Player.Motor.Teleport(door.HiddenDocks[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    Assert.That(door.PlayerOccupiesDoorway(), Is.True);
                    door.SetOpenFraction(owner, 0f);
                    Assert.That(door.IsOpen, Is.True, "A visitor inside the vestibule must retain an open way out.");
                    ParkVillageLifeHero(village);
                    door.SetOpenFraction(owner, 0f);
                    Assert.That(door.IsClosed, Is.True);
                    door.Release(owner);
                }
                report.visiting_player_keeps_exit_open = true;
                CaptureVillageNeighbourYard(village, camera, "village-house-11", "18-closed-house-after-return");

                for (int step = 0; step < 6000; step++)
                {
                    life.Advance(VillageLifeStep, 600d, .04f, wind);
                    ObserveVillageNeighbours(life, probes, report, clearanceSolids);
                    foreach (VillageNeighbourProbe probe in probes)
                        report.day_outings_resume_at_1000 |= IsAdditionalVillageNeighbour(probe.State) && probe.State.IsOutside;
                    if (report.day_outings_resume_at_1000 && step > 250) break;
                    if (step % 120 == 0) yield return null;
                }
                Assert.That(report.day_outings_resume_at_1000, Is.True, "Returning to daytime must resume village household life.");
                report.same_six_active_models_throughout = true;
                report.door_passages = life.TotalDoorPassages - initialPassages;
                report.gust_reactions = life.GustReactions - initialGusts;
                report.yields = life.YieldCount - initialYields;
                report.snow_work_cycles = life.SnowWorkCycles - initialSnow;
                for (int i = 0; i < probes.Length; i++) report.completed_outings[i] = probes[i].State.CompletedOutings;
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "VillageLife");
                Directory.CreateDirectory(folder);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(folder, "neighbours-verification.json"), json);
                TestContext.Out.WriteLine(json);
            }
            finally { ParkVillageLifeHero(village); }
        }

        private static bool IsAdditionalVillageNeighbour(VillageNeighbourState state) =>
            state.Role != VillageResidentRole.WoodWoman && state.Role != VillageResidentRole.StationWorker;

        private static bool AllAdditionalNeighboursCompleted(VillageNeighbourProbe[] probes)
        {
            foreach (VillageNeighbourProbe probe in probes)
                if (IsAdditionalVillageNeighbour(probe.State) && probe.State.CompletedOutings < 1) return false;
            return true;
        }

        private static bool AllAdditionalNeighboursHome(VillageNeighbourProbe[] probes)
        {
            foreach (VillageNeighbourProbe probe in probes)
                if (!probe.State.IsHome || probe.State.IsOutside) return false;
            return true;
        }

        private static bool AllVillageResidentDoorsClosed(AlpineVillageRoot village)
        {
            foreach (var door in village.World.ResidentDoors.Values) if (!door.IsClosed) return false;
            return true;
        }

        private static void ObserveVillageNeighbours(AlpineVillageLifeController life,
            VillageNeighbourProbe[] probes, VillageNeighbourReport report, Collider[] clearanceSolids)
        {
            int outside = 0;
            foreach (VillageNeighbourProbe probe in probes)
            {
                if (probe.State.Actor != probe.Actor || probe.Actor == null || !probe.Actor.gameObject.activeInHierarchy)
                    Assert.Fail("A permanent resident was replaced or deactivated during an outing.");
                foreach (Renderer renderer in probe.Renderers)
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        Assert.Fail("A resident disappeared by hiding a renderer instead of walking behind the real house wall.");
                float distance = Vector3.Distance(probe.PreviousPosition, probe.Actor.transform.position);
                report.maximum_root_step_metres = Mathf.Max(report.maximum_root_step_metres, distance);
                if (distance > .08f)
                    Assert.Fail($"{probe.State.Role} jumped {distance:0.000}m during {probe.State.Task}; doors cannot conceal teleports.");
                probe.PreviousPosition = probe.Actor.transform.position;
                if (probe.State.IsOutside) outside++;
            }
            if (outside != life.OutdoorCount || outside > 4)
                Assert.Fail($"Outdoor roster does not match its four-person cap: actual states {outside}, counter {life.OutdoorCount}.");
            report.maximum_outdoor_count = Mathf.Max(report.maximum_outdoor_count, outside);
            if (report.simulated_steps % 10 == 0)
                AssertVillageNeighbourBodyClearance(probes, clearanceSolids, report);
            report.simulated_steps++;
            report.simulated_seconds = report.simulated_steps * VillageLifeStep;
        }

        private static Collider[] CollectVillageNeighbourClearanceSolids(AlpineVillageRoot village)
        {
            var unique = new HashSet<Collider>();
            foreach (Collider solid in village.Life.SolidProps)
                if (solid != null && !solid.isTrigger) unique.Add(solid);
            foreach (var door in village.World.ResidentDoors.Values)
                foreach (MeshCollider solid in door.HouseRoot.GetComponentsInChildren<MeshCollider>(true))
                    if (!solid.isTrigger) unique.Add(solid);
            Assert.That(unique.Count, Is.GreaterThan(village.Life.SolidProps.Count),
                "Neighbour clearance must include the real carved houses and their moving door leaves.");
            var result = new Collider[unique.Count];
            unique.CopyTo(result);
            return result;
        }

        private static void AssertVillageNeighbourBodyClearance(VillageNeighbourProbe[] probes,
            Collider[] solids, VillageNeighbourReport report)
        {
            // The simulation moves roots and articulated leaves between fixed
            // ticks. Refresh collision poses once for this sampled observation.
            Physics.SyncTransforms();
            foreach (VillageNeighbourProbe probe in probes)
            {
                CapsuleCollider body = probe.Body;
                Transform bodyTransform = body.transform;
                Vector3 scale = bodyTransform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                int axis = body.direction;
                int acrossA = (axis + 1) % 3, acrossB = (axis + 2) % 3;
                float radius = body.radius * Mathf.Max(scale[acrossA], scale[acrossB]);
                float halfSegment = Mathf.Max(0f, body.height * scale[axis] * .5f - radius);
                Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                Vector3 halfAxis = bodyTransform.TransformDirection(localAxis) * halfSegment;
                Vector3 center = bodyTransform.TransformPoint(body.center);
                Vector3 halfBounds = new Vector3(Mathf.Abs(halfAxis.x), Mathf.Abs(halfAxis.y),
                    Mathf.Abs(halfAxis.z)) + Vector3.one * radius;
                var envelope = new Bounds(center, halfBounds * 2f);
                report.body_clearance_samples++;
                // Home/indoor states deliberately share the same geometric
                // proof. A schedule flag cannot license crossing a solid wall.
                foreach (Collider solid in solids)
                {
                    if (solid == null || !solid.enabled || solid.isTrigger ||
                        !solid.gameObject.activeInHierarchy || solid == body ||
                        solid.transform.IsChildOf(probe.Actor.transform)) continue;
                    // Only actual collider bounds reject far-away candidates;
                    // imported renderer bounds play no role in this check.
                    if (!envelope.Intersects(solid.bounds)) continue;
                    report.body_clearance_pairs++;
                    if (!Physics.ComputePenetration(body, bodyTransform.position, bodyTransform.rotation,
                            solid, solid.transform.position, solid.transform.rotation,
                            out Vector3 direction, out float depth)) continue;
                    report.maximum_body_overlap_metres = Mathf.Max(report.maximum_body_overlap_metres, depth);
                    if (depth <= .025f) continue;
                    string path = solid.name;
                    for (Transform parent = solid.transform.parent; parent != null; parent = parent.parent)
                        path = parent.name + "/" + path;
                    Assert.Fail($"Village neighbour body intersects real geometry: Role={probe.State.Role}; " +
                        $"Task={probe.State.Task}; Collider={path}; Root={probe.Actor.transform.position:F4}; " +
                        $"CapsuleCenter={center:F4}; Radius={radius:F4}m; Overlap={depth:F4}m; " +
                        $"SeparationDirection={direction:F4}. Allowed numerical overlap is 0.025m.");
                }
            }
        }

        private static IEnumerator VerifyVillageNeighbourPause(AlpineVillageRoot village,
            VillageNeighbourProbe[] probes, VillageNeighbourReport report)
        {
            var roots = new Vector3[probes.Length];
            var left = new Vector3[probes.Length];
            var right = new Vector3[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                roots[i] = probes[i].Actor.transform.position;
                left[i] = probes[i].Actor.LeftGrip.position;
                right[i] = probes[i].Actor.RightGrip.position;
            }
            Vector3 shovel = village.Life.Shovel.position, basket = village.Life.ClosedBasket.position;
            var doors = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var pair in village.World.ResidentDoors) doors[pair.Key] = pair.Value.OpenFraction;
            using (GameTimeScaleRuntime.AcquirePause())
                for (int frame = 0; frame < 4; frame++)
                {
                    village.Life.Advance(.25f, 600d, .97f, Vector3.forward);
                    for (int i = 0; i < probes.Length; i++)
                    {
                        Assert.That(Vector3.Distance(roots[i], probes[i].Actor.transform.position), Is.LessThan(.0001f));
                        Assert.That(Vector3.Distance(left[i], probes[i].Actor.LeftGrip.position), Is.LessThan(.0001f));
                        Assert.That(Vector3.Distance(right[i], probes[i].Actor.RightGrip.position), Is.LessThan(.0001f));
                    }
                    Assert.That(Vector3.Distance(shovel, village.Life.Shovel.position), Is.LessThan(.0001f));
                    Assert.That(Vector3.Distance(basket, village.Life.ClosedBasket.position), Is.LessThan(.0001f));
                    foreach (var pair in village.World.ResidentDoors)
                        Assert.That(pair.Value.OpenFraction, Is.EqualTo(doors[pair.Key]).Within(.0001f));
                    yield return null;
                }
            report.pause_freezes_six_residents = true;
        }

        private static void ObserveVillageNeighbourProps(AlpineVillageLifeController life,
            VillageNeighbourReport report, Camera camera, ref bool shovelCaptured, ref bool basketCaptured)
        {
            foreach (VillageNeighbourState state in life.Neighbours)
            {
                if (!state.IsOutside) continue;
                bool shovel = state.Role.ToString() == "SnowNeighbor";
                bool basket = state.Role.ToString() == "BasketVisitor";
                if (!shovel && !basket) continue;
                string action = state.Actor.CurrentAction.ToString();
                if (shovel && !state.IsCarryingShovel) continue;
                if (basket && !state.IsCarryingBasket) continue;
                Transform prop = shovel ? life.Shovel : life.ClosedBasket;
                float error = Mathf.Max(Vector3.Distance(prop.Find("ANCHOR_LeftGrip").position, state.Actor.LeftGrip.position),
                    Vector3.Distance(prop.Find("ANCHOR_RightGrip").position, state.Actor.RightGrip.position));
                if (error >= .035f)
                    VillageLifeFrame(camera, "19-contact-failure", state.Actor.transform.position + state.Actor.transform.right * 2.4f + Vector3.up * 1.4f,
                        (state.Actor.LeftGrip.position + state.Actor.RightGrip.position) * .5f, 48f);
                Assert.That(error, Is.LessThan(.035f), $"Visible two-hand work lost its authored prop contacts: {state.Role}, {state.Task}, {action} at {state.Actor.CurrentActionSeconds:0.000}s; " +
                    $"root {state.Actor.transform.position}, left {state.Actor.LeftGrip.position}, right {state.Actor.RightGrip.position}, prop {prop.position}");
                if (shovel)
                {
                    report.shovel_contact_samples++;
                    report.maximum_shovel_grip_error_metres = Mathf.Max(report.maximum_shovel_grip_error_metres, error);
                }
                else
                {
                    report.closed_basket_contact_samples++;
                    report.maximum_closed_basket_grip_error_metres = Mathf.Max(report.maximum_closed_basket_grip_error_metres, error);
                }
                if (shovel ? shovelCaptured || action != "ShovelWork" || state.Actor.CurrentActionSeconds < .9f : basketCaptured || !action.Contains("Carry")) continue;
                AssertVillageHandGeometry(state.Actor);
                Transform actor = state.Actor.transform;
                VillageLifeFrame(camera, shovel ? "16-real-shovel-two-hand-work" : "17-closed-basket-carry",
                    actor.position + actor.right * 2.9f + actor.forward * .6f + Vector3.up * 1.3f,
                    (state.Actor.LeftGrip.position + state.Actor.RightGrip.position) * .5f, 43f);
                if (shovel) shovelCaptured = true; else basketCaptured = true;
            }
        }

        private static void CaptureVillageNeighbourYard(AlpineVillageRoot village, Camera camera, string id, string name)
        {
            foreach (AlpineVillagePlotDescriptor plot in village.Plan.Plots)
            {
                if (plot.StableId != id) continue;
                Vector3 right = Vector3.Cross(Vector3.up, plot.Facing);
                VillageLifeFrame(camera, name, plot.DoorGroundPosition + plot.Facing * 7.5f + right * 2.3f + Vector3.up * 1.9f,
                    plot.DoorGroundPosition + plot.Facing * 2.0f + Vector3.up * .9f, 56f);
                return;
            }
            Assert.Fail("Missing inhabited yard " + id);
        }

        private static void AssertVillageHomeOcclusion(VillageResidentDoor door, Vector3 target)
        {
            Vector3 origin = door.ExteriorDock + Vector3.up * 1.1f;
            Vector3 direction = target - origin;
            bool blockedByHouse = false;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, direction.normalized, direction.magnitude - .02f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(door.HouseRoot)) blockedByHouse = true;
            Assert.That(blockedByHouse, Is.True,
                "The active home resident must be hidden by actual house geometry, at both body and head height: " + door.PlotId);
        }
    }
}
