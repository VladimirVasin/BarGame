using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [Category("CurrentCombat")]
        [UnityTest]
        [PrebuildSetup(typeof(CombatMixedEndlessAssetsSetup))]
        public IEnumerator Range_GroundEnemiesRouteAroundRailsStairsAndChangedTargets()
        {
            int? previousSeed = CombatTestRoot.WarehouseSeedForTests;
            GameObject footing = null;
            try
            {
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                CombatTestRoot.AuthoredEnemyKindForTests = CombatEnemyKind.Room;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var settings = new CombatEncounterSettings(3, 12f, 12f, 12f, 12f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, settings)), Is.True);
                yield return AwaitSelectedCombatRange();
                ForceAmmoFocus();
                Assert.That(root.WarehousePlan, Is.Not.Null);
                AssertGroundNavigationGateChanges();
                CombatActor room = root.Opponents[0], docker = root.Opponents[1];
                Assert.That(room.IsRoom && docker.IsDocker, Is.True);

                // Both supported endpoints are at one height. The short chest ray
                // clears the real rail, while the capsule cannot cut its corner.
                foreach (CombatActor actor in new[] { docker, room })
                {
                    // The intact carried crate needs its measured cargo clearance;
                    // the smaller body capsule alone cannot validate its start.
                    float startX = actor.IsDocker ? 7.2f : 6.55f;
                    PlaceGroundNavigationPair(actor, new Vector3(startX, 3.2f, 5.65f),
                        new Vector3(5.65f, 3.2f, 6.55f), initialFacing: actor.IsDocker ? Vector3.right : (Vector3?)null);
                    if (actor.IsDocker)
                    {
                        float outward = actor.LimitZombieLoadTravel(Vector3.right * .05f).magnitude;
                        Assert.That(outward, Is.GreaterThan(.045f),
                            "The carried crate must begin with a physically free outward exit from the rail corner." +
                            (outward > .045f ? string.Empty : GroundNavigationLoadDiagnostic(actor)));
                    }
                    Assert.That(GroundNavigationDirect(actor), Is.False, "The rail setup must require a detour.");
                    Assert.That(GroundNavigationChestVisible(actor), Is.True,
                        "Seeing the hero above a low rail must not grant physical melee access.");
                    Vector3 before = actor.transform.position;
                    yield return ReachGroundNavigationTarget(actor, 6f, "Test ground pursuit - Gallery rail");
                    Assert.That(Vector3.Distance(before, actor.transform.position), Is.GreaterThan(.12f),
                        "A close hero across the rail still requires an actual approach.");
                }

                // Reaching the floor height alone is insufficient: the capsule
                // must leave the stair exit and reach a clear combat approach.
                PlaceGroundNavigationPair(docker, new Vector3(7.5f, 0f, -3.75f),
                    new Vector3(6.7f, 3.2f, 5.5f));
                yield return ReachGroundNavigationTarget(docker, 18f, "Test ground pursuit - Stair ascent");
                MoveGroundNavigationHero(new Vector3(7.5f, 0f, -4.3f));
                Assert.That(GroundNavigationDirect(docker), Is.False,
                    "Changing the target floor must invalidate the old combat approach.");
                yield return ReachGroundNavigationTarget(docker, 20f, "Test ground pursuit - Stair descent after target move");

                // The upper left flight is physically broken. A large Room must
                // go around the gallery and climb the intact right-hand flight.
                PlaceGroundNavigationPair(room, new Vector3(-7.5f, 3.2f, -3.75f),
                    new Vector3(-6.7f, 6.4f, 5.5f));
                bool usedRightUpperFlight = false;
                yield return ReachGroundNavigationTarget(room, 48f, "Test ground pursuit - Broken upper flight", () =>
                {
                    Vector3 p = room.transform.position;
                    usedRightUpperFlight |= p.x > 6f && p.z > -2.8f && p.z < 3.2f &&
                        p.y > WarehouseArenaPlan.LevelRise + .5f && p.y < WarehouseArenaPlan.TopHeight - .3f;
                });
                Assert.That(usedRightUpperFlight, Is.True, "The actual body must use the intact upper right stair.");

                // A supported test ledge deliberately has no navigation surface.
                // The enemy may approach its reachable edge, then must wait safely.
                footing = new GameObject("Test unreachable hero footing");
                footing.transform.SetParent(root.transform, false);
                footing.transform.position = new Vector3(0f, 3.1f, -5.2f);
                footing.AddComponent<BoxCollider>().size = new Vector3(1.2f, .2f, 1.2f);
                PlaceGroundNavigationPair(docker, new Vector3(0f, 3.2f, -6.7f), new Vector3(0f, 3.2f, -5.2f),
                    targetMustHaveRouteSurface: false);
                float health = root.Hero.State.Health;
                for (int frame = 0; frame < 180; frame++)
                {
                    TickGroundNavigation(docker, "Test ground pursuit - Unreachable target");
                    if (frame % 60 == 59) yield return null;
                }
                Vector3 waiting = docker.transform.position;
                for (int frame = 0; frame < 90; frame++)
                    TickGroundNavigation(docker, "Test ground pursuit - Safe wait");
                Assert.That(Vector3.Distance(waiting, docker.transform.position), Is.LessThan(.08f));
                Assert.That(root.Hero.State.Health, Is.EqualTo(health));
                Assert.That(docker.State.IsAttacking, Is.False);

                MoveGroundNavigationHero(new Vector3(6.7f, 3.2f, -4.8f));
                Vector3 paused = docker.transform.position;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.Tick(.75f);
                    Assert.That(docker.transform.position, Is.EqualTo(paused), "Pause freezes route travel and its clocks.");
                }
                yield return ReachGroundNavigationTarget(docker, 12f, "Test ground pursuit - Reachable target restored");

                // Reset while a route is active, then send the recycled actor
                // toward the opposite stair. No waypoint from the prior round survives.
                PlaceGroundNavigationPair(docker, new Vector3(7.5f, 0f, -4f), new Vector3(6.7f, 3.2f, 5.5f));
                for (int frame = 0; frame < 24; frame++)
                    TickGroundNavigation(docker, "Test ground pursuit - Before reset");
                PlaceGroundNavigationPair(docker, new Vector3(-7.5f, 0f, -3.75f), new Vector3(-6.7f, 3.2f, 5.5f));
                yield return ReachGroundNavigationTarget(docker, 18f, "Test ground pursuit - Fresh route after reset");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (footing != null) Object.Destroy(footing);
                CombatTestRoot.WarehouseSeedForTests = previousSeed;
            }
        }

        private void AssertGroundNavigationGateChanges()
        {
            var state = new WarehouseGateState();
            var area = new WarehouseArenaArea(root.WarehousePlan, state);
            var route = new List<Vector3>(64);
            Vector3 from = root.WarehousePlan.HeroSpawn;
            Vector3 target = root.WarehousePlan.GoalDoorPosition - Vector3.forward * .4f;
            Assert.That(area.FindRoute(from, target, route), Is.False,
                "A lowered gate blocks the destination in the retained warehouse graph.");
            Assert.That(state.TryUnlock(true) && state.Advance(WarehouseGateState.TurnSeconds, true), Is.True);
            Assert.That(area.FindRoute(from, target, route), Is.True,
                "Opening the gate must enable its route without replacing the area or rebuilding the graph.");
            Assert.That(route[route.Count - 1], Is.EqualTo(target));
            state.Reset();
            Assert.That(area.FindRoute(from, target, route), Is.False,
                "A reset gate must invalidate the same previously open route.");
        }

        private void PlaceGroundNavigationPair(CombatActor actor, Vector3 from, Vector3 target,
            bool targetMustHaveRouteSurface = true, Vector3? initialFacing = null)
        {
            ForceAmmoFocus();
            root.ResetRound();
            IsolateGroundNavigationActor(actor);
            actor.ResetActor(from + Vector3.up * PlayerFactory.GroundedRootOffset,
                initialFacing ?? (actor.IsDocker ? Vector3.right :
                    Vector3.ProjectOnPlane(target - from, Vector3.up).normalized));
            MoveGroundNavigationHero(target);
            Assert.That(actor.ZombieAwake && actor.IsAvailable && actor.BodyDamage.CanStand, Is.True,
                "The selected ground enemy must begin awake and able to pursue.");
            AssertGroundNavigationSupport(actor, "Test ground pursuit - Start");
            if (targetMustHaveRouteSurface)
                Assert.That(root.WarehouseArea.TryGetHeight(root.Hero.transform.position, out _, actor.Body.radius + .04f,
                    GroundNavigationHeight(actor)), Is.True, "The requested combat destination must fit this body.");
            Physics.SyncTransforms();
        }

        private void IsolateGroundNavigationActor(CombatActor actor)
        {
            // The shared clock intentionally ticks spawned slots even when a
            // component is disabled. Park the other prepared bodies at the same
            // slot boundary the production population uses for inactive actors.
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var slots = (IEnumerable)typeof(CombatTestRoot).GetField("opponentSlots", fields).GetValue(root);
            foreach (object slot in slots)
            {
                Type type = slot.GetType();
                var candidate = (CombatActor)type.GetField("Actor", fields).GetValue(slot);
                bool selected = candidate == actor;
                type.GetField("Spawned", fields).SetValue(slot, selected);
                candidate.gameObject.SetActive(selected);
            }
            var pacing = (EndlessCombatState)typeof(CombatTestRoot).GetField("endless", fields).GetValue(root);
            pacing.SetSpawningEnabled(false, 1);
        }

        private void MoveGroundNavigationHero(Vector3 floor)
        {
            root.Hero.ResetActor(floor + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
            root.Hero.SetCombatFocused(false);
            Physics.SyncTransforms();
        }

        private IEnumerator ReachGroundNavigationTarget(CombatActor actor, float seconds, string purpose, Action observe = null)
        {
            int frames = Mathf.CeilToInt(seconds * 60f);
            for (int frame = 0; frame < frames && !GroundNavigationCombatPosition(actor); frame++)
            {
                TickGroundNavigation(actor, purpose);
                observe?.Invoke();
                if (frame % 60 == 59)
                {
                    TestContext.Progress.WriteLine(purpose + ": " + actor.transform.position.ToString("F2") +
                        ", " + root.DescribeWarehouseOpponentNavigation(actor));
                    yield return null;
                    ForceAmmoFocus();
                }
            }
            bool reached = GroundNavigationCombatPosition(actor);
            Assert.That(reached, Is.True,
                purpose + ": actual body did not reach a supported combat approach; actor=" + actor.transform.position +
                ", target=" + root.Hero.transform.position + ", phase=" + actor.State.Phase +
                ", room=" + root.RoomAttackPhase + ", navigation=" + root.DescribeWarehouseOpponentNavigation(actor) +
                (reached ? string.Empty : GroundNavigationLoadDiagnostic(actor)));
        }

        private string GroundNavigationLoadDiagnostic(CombatActor actor)
        {
            CombatZombieLoad load = actor.GetComponent<CombatZombieLoad>();
            if (load == null || !load.IsCarrying) return string.Empty;
            Bounds bounds = load.AttachedBounds;
            var result = new StringBuilder(", cargo_dimensions=" + load.CrateDimensions.ToString("F3") +
                ", cargo_min=" + bounds.min.ToString("F3") + ", cargo_max=" + bounds.max.ToString("F3") +
                ", cargo_free_north=" + actor.LimitZombieLoadTravel(Vector3.forward * .05f).magnitude.ToString("F3") +
                ", cargo_free_south=" + actor.LimitZombieLoadTravel(Vector3.back * .05f).magnitude.ToString("F3") +
                ", cargo_free_east=" + actor.LimitZombieLoadTravel(Vector3.right * .05f).magnitude.ToString("F3") +
                ", cargo_free_west=" + actor.LimitZombieLoadTravel(Vector3.left * .05f).magnitude.ToString("F3"));
            var envelope = (BoxCollider)typeof(CombatZombieLoad).GetField("envelopeCollider",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(load);
            var parts = new List<BoxCollider>(load.BoardCount + 1) { load.Collider };
            for (int board = 0; board < load.BoardCount; board++)
                if (!load.IsBoardDetached(board)) parts.Add(load.GetBoardCollider(board));
            foreach (Vector3 travel in new[] { new Vector3(.035f, 0f, .035f), Vector3.right * .05f })
            {
                result.Append("; cargo_probe=").Append(travel.ToString("F3"));
                AppendGroundLoadContacts(result, actor, envelope, travel, 1, "envelope");
                int remaining = 3;
                foreach (BoxCollider part in parts)
                {
                    remaining -= AppendGroundLoadContacts(result, actor, part, travel, remaining, "part");
                    if (remaining == 0) break;
                }
                if (remaining == 3) result.Append("; actual_parts_no_blocking_cast_contacts");
            }
            Vector3 from = actor.transform.position;
            float bodyRadius = actor.Body.radius + .04f;
            float height = Mathf.Max(WarehouseArenaArea.DefaultHeadroom, actor.GroundObstacleHeight);
            float obstacleRadius = Mathf.Max(bodyRadius, actor.GroundObstacleRadius + .04f);
            foreach (Vector3 offset in new[] { new Vector3(1f, 0f, 1f).normalized * .9f,
                Vector3.right * .6f, Vector3.right * 1.2f })
            {
                Vector3 candidate = from + offset;
                bool fits = root.WarehouseArea.TryGetHeight(candidate, out float support, bodyRadius, height, obstacleRadius);
                bool bodyTravel = root.WarehouseArea.CanTraverse(from, candidate, bodyRadius, height);
                result.Append("; plan_probe=").Append(offset.ToString("F3"))
                    .Append(" fit=").Append(fits).Append(" support=").Append(support.ToString("F3"))
                    .Append(" body_traverse=").Append(bodyTravel);
            }
            return result.ToString();
        }

        private static int AppendGroundLoadContacts(StringBuilder output, CombatActor actor, BoxCollider shape,
            Vector3 travel, int limit, string category)
        {
            if (shape == null || limit <= 0) return 0;
            Transform frame = shape.transform;
            Vector3 scale = frame.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            RaycastHit[] contacts = Physics.BoxCastAll(frame.TransformPoint(shape.center),
                Vector3.Scale(shape.size, scale) * .5f, travel.normalized, frame.rotation, travel.magnitude + .01f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(contacts, (a, b) => a.distance.CompareTo(b.distance));
            int added = 0;
            foreach (RaycastHit hit in contacts)
            {
                Collider other = hit.collider;
                if (other == null || other.transform.IsChildOf(actor.transform) || hit.normal.y > .6f ||
                    CombatCorpseTraversal.IsCorpse(other)) continue;
                bool before = Physics.ComputePenetration(shape, frame.position, frame.rotation,
                    other, other.transform.position, other.transform.rotation, out Vector3 beforeDirection, out float beforeDepth);
                bool after = Physics.ComputePenetration(shape, frame.position + travel, frame.rotation,
                    other, other.transform.position, other.transform.rotation, out Vector3 afterDirection, out float afterDepth);
                output.Append("; ").Append(category).Append('=').Append(shape.name)
                    .Append(" hit=").Append(other.name).Append('/').Append(other.GetType().Name)
                    .Append(" distance=").Append(hit.distance.ToString("F4"))
                    .Append(" normal=").Append(hit.normal.ToString("F3"))
                    .Append(" penetration=").Append(before).Append('/').Append(beforeDepth.ToString("F4"))
                    .Append("->").Append(after).Append('/').Append(afterDepth.ToString("F4"))
                    .Append(" direction=").Append(beforeDirection.ToString("F3"))
                    .Append("->").Append(afterDirection.ToString("F3"));
                if (++added == limit) break;
            }
            return added;
        }

        private void TickGroundNavigation(CombatActor actor, string purpose)
        {
            root.Tick(1f / 60f);
            AssertGroundNavigationSupport(actor, purpose);
            if (!GroundNavigationDirect(actor))
            {
                Assert.That(actor.State.IsAttacking, Is.False, purpose + ": an obstructed target cannot start a strike.");
                Assert.That(root.RoomAttackCinematicActive, Is.False, purpose + ": a rail cannot start the Room sequence.");
            }
            Assert.That(root.RoundFinished, Is.False, purpose + ": pursuit must not end the round.");
        }

        private void AssertGroundNavigationSupport(CombatActor actor, string purpose)
        {
            Assert.That(root.WarehouseArea.TryGetHeight(actor.transform.position, out float floor,
                actor.Body.radius, GroundNavigationHeight(actor)), Is.True,
                purpose + ": body left the authored walking surface at " + actor.transform.position);
            Assert.That(Mathf.Abs(actor.transform.position.y - floor), Is.LessThan(.34f),
                purpose + ": the body must keep its ordinary step and gravity support.");
        }

        private bool GroundNavigationCombatPosition(CombatActor actor)
        {
            Vector3 delta = actor.transform.position - root.Hero.transform.position;
            return Mathf.Abs(delta.y) <= .35f && Vector3.ProjectOnPlane(delta, Vector3.up).magnitude <=
                1.3f * actor.PresentationScale + actor.AdditionalArmReach + .08f && GroundNavigationDirect(actor);
        }

        private bool GroundNavigationDirect(CombatActor actor) => root.WarehouseArea.CanTraverse(actor.transform.position,
            root.Hero.transform.position, actor.Body.radius + .04f, GroundNavigationHeight(actor));

        private static float GroundNavigationHeight(CombatActor actor) => actor.IsRoom
            ? actor.Body.height + PlayerFactory.GroundedRootOffset : WarehouseArenaArea.DefaultHeadroom;

        private bool GroundNavigationChestVisible(CombatActor actor)
        {
            Vector3 from = actor.transform.position + Vector3.up * 1.25f;
            Vector3 delta = root.Hero.transform.position + Vector3.up * 1.25f - from;
            foreach (RaycastHit hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<CombatActor>() == null && !CombatCorpseTraversal.IsCorpse(hit.collider))
                    return false;
            return true;
        }
    }
}
