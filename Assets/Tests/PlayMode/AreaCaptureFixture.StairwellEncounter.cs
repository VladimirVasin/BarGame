using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void AssertStairwellEncounterDormant(StairwellInteriorRoot root)
        {
            Assert.That(root.Encounter, Is.Not.Null);
            Assert.That(root.Encounter.State.Awakened, Is.False,
                "The crowd must wait for this physical source's confirmed take and restored gameplay input.");
            Assert.That(root.Encounter.MobileCount, Is.Zero);
            Assert.That(root.Encounter.CommittedAttackCount, Is.Zero);
            Assert.That(root.Encounter.ActiveActors.Count, Is.Zero,
                "The distant dormant population must not allocate combat actors before promotion.");
        }

        private static IEnumerator VerifyStairwellDormantApproach(StairwellInteriorRoot root)
        {
            AssertStairwellEncounterDormant(root);
            var plan = root.Encounter.Plan;
            Assert.That(plan.Enemies.Count, Is.EqualTo(StairwellEncounterState.Population));
            var ids = new bool[StairwellEncounterState.Population];
            var wakeTimes = new HashSet<float>();
            int carriers = 0;
            foreach (var placement in plan.Enemies)
            {
                Assert.That(ids[placement.Id], Is.False);
                ids[placement.Id] = true;
                Assert.That(plan.Contains(placement.Position, .4f), Is.True,
                    "Every authored dormant body requires same-storey support and a clear safe-room boundary.");
                Assert.That(placement.Variant, Is.InRange(0, 2));
                Assert.That(wakeTimes.Add(StairwellEncounterState.WakeDelay(placement.Id)), Is.True,
                    "The finite crowd needs distinct staggered wake times, not repeated synchronized rows.");
                if (placement.Variant == 2)
                {
                    carriers++;
                    Assert.That(placement.Id, Is.EqualTo(11).Or.EqualTo(15).Or.EqualTo(18),
                        "The heavy carrier stays a rare later encounter.");
                }
            }
            Assert.That(carriers, Is.EqualTo(3));
            Assert.That(plan.Contains(new Vector3(29f, 12.04f, 32f), .34f), Is.True,
                "The shared seam between two connected floor slabs must remain traversable at body radius.");
            VerifyStairwellEnemyGeometry();
            var rules = new StairwellEncounterState();
            Assert.That(rules.TryAwaken(false, true) || rules.TryAwaken(true, false), Is.False);
            Assert.That(rules.TryAwaken(true, true), Is.True);
            Assert.That(rules.TryAwaken(true, true), Is.False, "The confirmed source starts one encounter.");
            Assert.That(rules.MarkDefeated(0), Is.True);
            Assert.That(rules.MarkDefeated(0), Is.False);
            rules.Advance(1f);
            int generation = rules.Generation;
            rules.Retry();
            Assert.That(rules.Generation, Is.GreaterThan(generation));
            Assert.That(rules.Awakened && !rules.IsDefeated(0) && rules.Elapsed == 0f, Is.True);
            rules.Complete();
            rules.Retry();
            Assert.That(rules.Completed && rules.Retries == 1, Is.True);

            var room = root.LabyrinthPresentation.Rooms[1];
            root.Player.Motor.SetInputEnabled(false);
            root.Player.Interactor.SetInputEnabled(true);
            Vector3 carrier = plan.Enemies[11].Position;
            root.Player.Motor.Teleport(root.World.Root.TransformPoint(new Vector3(carrier.x - 2f, carrier.y, 23f)));
            yield return null; // Let the real camera switch from the entry shot to the labyrinth orbit.
            AimLabyrinthOrbit(root, carrier + Vector3.up * .9f);
            yield return CaptureCorruptedGameplay(root, "stairwell-selfcarrier-dormant");
            root.Player.Motor.Teleport(root.World.Root.TransformPoint(new Vector3(-1f, 12.04f, 14f)));
            yield return null;
            AimLabyrinthOrbit(root, plan.Enemies[0].Position + Vector3.up * .9f);
            yield return CaptureCorruptedGameplay(root, "stairwell-enemies-dormant");
            AssertStairwellEncounterDormant(root);
            yield return WalkStairwellWaypoint(root, room.OutsideDock);
            var door = root.LabyrinthPresentation.Doors[1];
            Assert.That(door.Begin(), Is.True, "The second room remains reachable through the dormant crowd.");
            yield return WaitForCorruptedStairwell(() => !door.OwnsActiveInteraction,
                "The second-room door did not finish opening for the encounter approach.");
            Assert.That(door.IsOpen, Is.True);
            root.Player.Motor.SetInputEnabled(false);
            yield return WalkStairwellWaypoint(root, room.PassagePoint);
            yield return WalkStairwellWaypoint(root, room.InteriorPoint);
            AssertStairwellEncounterDormant(root);
        }

        private static IEnumerator VerifyStairwellCrowbarAwakening(StairwellInteriorRoot root)
        {
            Assert.That(GameSessionState.IsWorldItemCollected(BarPromenade.StairwellLabyrinthPuzzles.CrowbarSourceId), Is.True);
            root.Player.Motor.SetInputEnabled(true);
            root.Player.Interactor.SetInputEnabled(true);
            yield return WaitForEncounter(root, () => root.Encounter.State.Awakened,
                "Confirmed crowbar pickup did not wake the crowd after the item screen returned control.");
            var hero = root.Player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            yield return WaitForEncounter(root, () => hero.Actor != null,
                "The encounter became live without the acquired crowbar's real combat actor.");
            float health = hero.Actor.State.Health;
            float previousYaw = root.CameraFollow.Camera.transform.eulerAngles.y;
            float previousPitch = root.CameraFollow.TargetOrbitPitch;
            AimLabyrinthOrbit(root, root.Encounter.Plan.Enemies[0].Position + Vector3.up);
            yield return WaitForEncounter(root, () => root.Encounter.State.Elapsed >= .7f,
                "The finite awakening clock did not advance.");
            // This shot observes the crowd through the open doorway from the
            // pickup position; the close room camera can legitimately crop the hero.
            yield return CaptureCorruptedGameplay(root, "stairwell-enemies-rising", requireHero: false);
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y, previousYaw));
            root.CameraFollow.RotatePitch(previousPitch - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
            float elapsed = root.Encounter.State.Elapsed;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(root.Encounter.State.Elapsed, Is.EqualTo(elapsed).Within(.0001f));
            }
            Assert.That(hero.Actor.State.Health, Is.EqualTo(health), "The second puzzle room gives space to ready the crowbar.");
            // Accelerate only this retained setup clock: standing in the room
            // must never consume the player's first actual solo contact.
            root.Encounter.State.Advance(15f);
            yield return null;
            Assert.That(root.Encounter.State.FirstContactPassed, Is.False,
                "Time spent reading or readying the crowbar cannot skip the introductory solo encounter.");
            AssertStairwellEncounterCaps(root);
        }

        private static IEnumerator VerifyStairwellOutboundCombat(StairwellInteriorRoot root, InputTestFixture input)
        {
            var room = root.LabyrinthPresentation.Rooms[1];
            var motor = root.Player.Motor;
            root.Player.Interactor.SetInputEnabled(true);
            motor.SetInputEnabled(true);
            Vector3 before = motor.transform.position;
            yield return WalkEncounterWaypoint(root, room.PassagePoint);
            yield return WalkEncounterWaypoint(root, room.OutsideDock);
            yield return WalkEncounterWaypoint(root, new Vector3(-5.1f, 12.04f, 14f));
            yield return WalkEncounterWaypoint(root, new Vector3(-3.9f, 12.04f, 14f));
            Assert.That(Vector3.Distance(before, motor.transform.position), Is.GreaterThan(3f),
                "The outbound proof must move the real capsule through the doorway and crowded approach.");
            yield return WaitForEncounter(root, () => root.Encounter.GetActor(0) != null && root.Encounter.GetActor(0).ZombieAwake,
                "The first grounded body did not become a live ordinary opponent.");
            CombatActor target = root.Encounter.GetActor(0);
            CombatActor hero = root.Player.GameObject.GetComponent<PlayerCrowbarCombatController>().Actor;
            Assert.That(target.IsOrdinaryZombie, Is.True);
            Assert.That(target.State.AttackWindupSeconds, Is.GreaterThanOrEqualTo(.9f));
            Assert.That(target.GetComponent<CombatZombieLoad>(), Is.Null, "The ordinary body must not borrow the porter's crate.");
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                input.Set(mouse.position, new Vector2(Screen.width * .5f, Screen.height * .5f), queueEventOnly: true);
                yield return FaceEncounterEnemy(root, target);
                AimLabyrinthOrbit(root, root.World.Root.InverseTransformPoint(target.transform.position) + Vector3.up);
                yield return WaitForEncounter(root, () => target.State.Phase == MeleePhase.Windup &&
                    target.State.AttackElapsed < target.State.AttackWindupSeconds * .5f,
                    "The first opponent did not begin its readable attack.");
                int attack = target.State.AttackSequence;
                yield return CaptureAcquiredCrowbarPose(root, "stairwell-enemies-pre-contact", requireHero: false);
                yield return WaitForEncounter(root, () => target.State.Phase == MeleePhase.Windup &&
                    target.State.AttackElapsed >= target.State.AttackWindupSeconds - .04f,
                    "The first opponent did not present its readable committed attack.");
                Assert.That(target.State.AttackSequence, Is.EqualTo(attack));
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return null;
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return WaitForEncounter(root, () => target.State.AttackSequence != attack ||
                    target.State.AttackOutcome != MeleeAttackOutcome.None || target.State.AttackElapsed >= target.State.AttackActiveEnd,
                    "The first real hand sweep did not finish its active contact interval.");
                yield return CaptureAcquiredCrowbarPose(root, "stairwell-enemies-contact-result", requireHero: false);
                Assert.That(target.State.AttackSequence, Is.EqualTo(attack));
                Assert.That(target.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Parried),
                    "A fresh frontal RMB during the telegraph must parry the real hand contact.");
                yield return CaptureAcquiredCrowbarPose(root, "stairwell-enemies-parried");
                yield return WaitForEncounter(root, () => hero.GuardReady && hero.State.Phase == MeleePhase.Ready,
                    "The shared crowbar did not recover after its physical parry.");
                int hits = target.ReceivedImpactCount;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return WaitForEncounter(root, () => target.ReceivedImpactCount > hits,
                    "The real outbound LMB strike did not reach the enemy's physical body.");
                Assert.That(target.LastImpact.Source, Is.SameAs(hero));
                yield return CaptureAcquiredCrowbarPose(root, "stairwell-enemies-crowbar-contact");
                AssertStairwellEncounterCaps(root);

                // Isolate the retry edge after proving the outbound exchange:
                // death must forget a real draw's queued follow-up command.
                motor.Teleport(root.World.Root.TransformPoint(room.InteriorPoint));
                var adapter = root.Player.GameObject.GetComponent<PlayerCrowbarCombatController>();
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatCrowbar, false), Is.True);
                yield return WaitForEncounter(root, () => !hero.HasWeaponInHand && !hero.IsSwitchingWeapon &&
                    hero.State.Phase == MeleePhase.Ready,
                    "The acquired crowbar did not finish its real stow before the interrupted-draw retry.");
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return WaitForEncounter(root, () => hero.IsSwitchingWeapon && adapter.HasPendingAction,
                    "The fresh LMB did not queue its strike behind the actual draw.");
                int retries = root.Encounter.State.Retries;
                hero.State.ReceiveUnarmedHit(hero.State.Settings.MaxHealth * 4f, 0f, false);
                yield return WaitForEncounter(root, () => root.Encounter.State.Retries == retries + 1 && !hero.State.IsDefeated,
                    "The stairwell defeat did not restore its local attempt.");
                Assert.That(GameSessionState.StairwellLabyrinthPuzzles.CanPassGate(2), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatCrowbar), Is.EqualTo(1));
                Assert.That(root.World.Root.InverseTransformPoint(motor.transform.position).x,
                    Is.LessThan(StairwellEncounterPlan.SafeRoomEdge), "Retry must return the hero to the protected second room.");
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(adapter.HasPendingAction || hero.IsSwitchingWeapon || hero.HasWeaponInHand, Is.False,
                    "Holding the pre-defeat LMB cannot revive the interrupted draw or its queued strike.");
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar), Is.False);
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return null;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return WaitForEncounter(root, () => hero.HasWeaponInHand && hero.GuardReady &&
                    hero.State.Phase == MeleePhase.Ready,
                    "A fresh post-retry LMB must restore the retained crowbar through its ordinary draw and strike.");
            }
            finally
            {
                motor.CancelInteractionPoseMove();
                if (mouse.added) InputSystem.RemoveDevice(mouse);
            }
        }

        private static IEnumerator FaceEncounterEnemy(StairwellInteriorRoot root, CombatActor target)
        {
            var motor = root.Player.Motor;
            for (int frame = 0; frame < 45; frame++)
            {
                Vector3 direction = target.transform.position - motor.transform.position;
                direction.y = 0f;
                bool ready = motor.MoveTowardsInteractionPose(motor.transform.position,
                    Quaternion.LookRotation(direction.normalized), 1f / 30f);
                AssertStairwellEncounterCaps(root);
                yield return null;
                if (ready) { motor.CancelInteractionPoseMove(); yield break; }
            }
            motor.CancelInteractionPoseMove();
            Assert.Fail("The hero did not finish his supported turn toward the first encounter body.");
        }

        private static IEnumerator WalkEncounterWaypoint(StairwellInteriorRoot root, Vector3 local)
        {
            var motor = root.Player.Motor;
            Vector3 target = root.World.Root.TransformPoint(local);
            bool reached = false;
            for (int frame = 0; frame < 240 && !reached; frame++)
            {
                reached = motor.MoveTowardsApproachWaypoint(target, .045f, 1f / 30f);
                Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                    "The encounter must leave a real outbound lane at " + local + "; hero=" + motor.transform.position);
                AssertStairwellEncounterCaps(root);
                yield return null;
            }
            motor.CancelInteractionPoseMove();
            Assert.That(reached, Is.True, "The physical outbound walk did not reach " + local);
        }

        private static IEnumerator WaitForEncounter(StairwellInteriorRoot root, Func<bool> ready, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                AssertStairwellEncounterCaps(root);
                yield return null;
            }
            Assert.That(ready(), Is.True, failure);
            AssertStairwellEncounterCaps(root);
        }

        private static void AssertStairwellEncounterCaps(StairwellInteriorRoot root)
        {
            Assert.That(root.Encounter.MobileCount, Is.LessThanOrEqualTo(StairwellEncounterState.MaximumMobile));
            Assert.That(root.Encounter.CommittedAttackCount, Is.LessThanOrEqualTo(StairwellEncounterState.MaximumAttackers));
            if (!root.Encounter.State.FirstContactPassed)
            {
                Assert.That(root.Encounter.MobileCount, Is.LessThanOrEqualTo(1));
                Assert.That(root.Encounter.CommittedAttackCount, Is.LessThanOrEqualTo(1));
            }
            foreach (CombatActor actor in root.Encounter.ActiveActors)
            {
                if (actor == null || actor.State.IsDefeated || actor.IsKnockedDown || actor.IsRagdollActive) continue;
                Vector3 local = root.World.Root.InverseTransformPoint(actor.transform.position);
                Assert.That(root.Encounter.Plan.Contains(local), Is.True,
                    "Mobile enemy must retain second-storey support and stay outside the puzzle room: " + local);
            }
        }

        private static void VerifyStairwellEnemyGeometry()
        {
            // Measure posed vertices, not the deliberately conservative renderer
            // envelopes. This also catches the imported FBX root's metre factor.
            var fixture = new GameObject("Ordinary enemy handoff geometry");
            fixture.transform.position = Vector3.down * 5000f;
            try
            {
                for (int variant = 0; variant < 3; variant++)
                {
                    var actorRoot = new GameObject("Handoff body " + variant);
                    actorRoot.transform.SetParent(fixture.transform, false);
                    var body = actorRoot.AddComponent<CharacterController>();
                    body.height = 1.75f; body.center = Vector3.up * .875f; body.radius = .32f;
                    VillageResidentPresentation visual = DefaultNpcFactory.CreateForCharacter(actorRoot.transform,
                        DefaultNpcPopulation.StairwellEnemyId(variant == 2 ? 11 : variant));
                    CombatZombieAssetProvider.ApplyOrdinaryAppearance(visual.gameObject, variant);
                    var actor = actorRoot.AddComponent<CombatActor>();
                    actor.InitializeOpponent(visual, body, kind: CombatEnemyKind.Zombie);
                    Assert.That(actor.OrdinaryZombieVariant, Is.EqualTo(variant));
                    Assert.That(actor.State.AttackWindupSeconds, Is.EqualTo(variant == 2 ? 1.25f : .95f).Within(.0001f));
                    AssertStairwellArmCapsuleLengths(actor);
                    actor.SetOrdinaryZombieDormant(variant);
                    GameObject passive = CombatZombieAssetProvider.CreateOrdinaryPassive(fixture.transform, variant, false);
                    try
                    {
                        Bounds actual = StairwellVisibleBodyBounds(visual.gameObject, fixture.transform);
                        Bounds expected = StairwellVisibleBodyBounds(passive, fixture.transform);
                        AssertStairwellBodyBounds(actual, expected, "dormant " + variant);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(passive); }
                    Assert.That(actor.TryAwakenZombie(false), Is.True);
                    actor.Step(CombatZombieAssetProvider.OrdinaryRiseSeconds);
                    Assert.That(actor.ZombieAwake, Is.True);
                    passive = CombatZombieAssetProvider.CreateOrdinaryPassive(fixture.transform, variant, true);
                    try
                    {
                        Bounds actual = StairwellVisibleBodyBounds(visual.gameObject, fixture.transform);
                        Bounds expected = StairwellVisibleBodyBounds(passive, fixture.transform);
                        AssertStairwellBodyBounds(actual, expected, "rise endpoint " + variant);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(passive); }
                    AnimationClip idle = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.DegradedIdleClip, variant);
                    const float phase = 2.37f;
                    idle.SampleAnimation(visual.Animator.gameObject, phase);
                    Bounds continuingIdle = StairwellVisibleBodyBounds(visual.gameObject, fixture.transform);
                    actor.RestoreOrdinaryZombieIdlePhase(phase);
                    AssertStairwellBodyBounds(StairwellVisibleBodyBounds(visual.gameObject, fixture.transform), continuingIdle,
                        "standing promotion preserves phase " + variant);
                    NpcFootstepSources.Unregister(visual.transform);
                    UnityEngine.Object.DestroyImmediate(actorRoot);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        private static void AssertStairwellArmCapsuleLengths(CombatActor actor)
        {
            int measured = 0;
            foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                string endpoint = entry.Value switch
                {
                    Player3DAnatomicalPart.LeftUpperArm => "forearm.L",
                    Player3DAnatomicalPart.RightUpperArm => "forearm.R",
                    Player3DAnatomicalPart.LeftForearm => "hand.L",
                    Player3DAnatomicalPart.RightForearm => "hand.R",
                    _ => null
                };
                if (endpoint == null) continue;
                Assert.That(entry.Key, Is.TypeOf<CapsuleCollider>());
                var capsule = (CapsuleCollider)entry.Key;
                Transform end = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, endpoint);
                float segment = Vector3.Distance(capsule.transform.parent.position, end.position);
                Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                float height = capsule.height * capsule.transform.TransformVector(axis).magnitude;
                Assert.That(height, Is.EqualTo(segment).Within(.02f),
                    "The long visible arm requires the same physical segment at ragdoll handoff: " + entry.Value);
                measured++;
            }
            Assert.That(measured, Is.EqualTo(4));
        }

        private static Bounds StairwellVisibleBodyBounds(GameObject body, Transform frame)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    // Reuse the verified source-skin matrix diagnostic; BakeMesh
                    // with a second FBX scale conversion is not world geometry.
                    foreach (Vector3 world in SkinHeroCorruptionDiagnostic(skin).Vertices) Include(world);
                }
                else
                {
                    Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    foreach (Vector3 vertex in mesh.vertices) Include(renderer.transform.TransformPoint(vertex));
                }
            }
            Assert.That(found, Is.True, "The imported ordinary body must have visible geometry.");
            Assert.That(bounds.size.y, Is.InRange(.3f, 3.5f), "The physical body must remain in authored metres: " + bounds);
            return bounds;
            void Include(Vector3 world)
            {
                Vector3 local = frame.InverseTransformPoint(world);
                Assert.That(float.IsFinite(local.x) && float.IsFinite(local.y) && float.IsFinite(local.z), Is.True);
                if (found) bounds.Encapsulate(local); else { bounds = new Bounds(local, Vector3.zero); found = true; }
            }
        }

        private static void AssertStairwellBodyBounds(Bounds actual, Bounds expected, string state)
        {
            Assert.That(Vector3.Distance(actual.min, expected.min), Is.LessThan(.035f), state + " lower silhouette: " + actual + " vs " + expected);
            Assert.That(Vector3.Distance(actual.max, expected.max), Is.LessThan(.035f), state + " upper silhouette: " + actual + " vs " + expected);
        }
    }
}
