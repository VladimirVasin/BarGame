using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        [UnityTest]
        public IEnumerator Range_ZombieLungeUsesHandsObstaclesPauseAndRecycledBodies()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange();
            CombatActor zombie = root.Opponent;
            Assert.That(zombie.IsZombie, Is.True);
            Assert.That(zombie.Weapon, Is.Null, "Production opponents have no hidden crowbar.");
            Assert.That(zombie.State.Settings, Is.EqualTo(MeleeCombatSettings.ZombieUnarmed));
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(1));
            Assert.That(CombatZombieAssetProvider.ClipNames, Has.Length.EqualTo(9));
            foreach (var action in new[] { ("ZombieLoadAdjust", 2.4f), ("ZombieWatch", 2.8f) })
            {
                AnimationClip clip = CombatZombieAssetProvider.LoadClip(action.Item1);
                Assert.That(clip.length, Is.EqualTo(action.Item2).Within(.003f));
                Assert.That(clip.isLooping, Is.False);
                Assert.That(clip.events, Is.Empty, "The actor clock owns the complete action.");
            }
            zombie.SetBlock(true);
            Assert.That(zombie.State.IsBlocking || zombie.GuardReady, Is.False);

            // Rear-head contact keeps the unarmed profile rather than the crowbar finisher.
            var rearHeadTarget = new MeleeCombatant();
            Assert.That(rearHeadTarget.ReceiveUnarmedHit(zombie.State.Settings.Damage,
                zombie.State.Settings.BlockCost, false,
                location: new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Rear)),
                Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(rearHeadTarget.IsDefeated, Is.False);

            // The shared clock owns the visible front rails, real root travel
            // and the temporary exclusion of the recipient's broad motor capsule.
            root.SetSparring(true);
            PlaceZombieChasePair(1.5f);
            root.CameraFollow.Snap();
            for (int frame = 0; frame < 8; frame++) yield return null;
            CombatZombieLoad load = zombie.GetComponent<CombatZombieLoad>();
            Assert.That(load, Is.Not.Null);
            Transform crate = load.Model;
            int crateMeshCount = CountLoaderMeshInstances(crate);
            SkinnedMeshRenderer catchSkin = FindLoaderCatch(crate);
            Transform[] catchBones = catchSkin.bones;
            Pose[] initialCatchPose = CaptureLoaderCatchPose(catchBones);
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(1));
            Assert.That(load.CatchClockSeconds, Is.Zero);
            AssertLoaderCarrying(zombie, load);
            AssertLoaderHunch(zombie);
            AssertLoaderFootsteps(zombie);
            AssertLoaderHeadFacing(zombie);
            AssertLoaderLimpHands(zombie);
            AssertLoaderGenericRecoveryAnatomy(zombie);
            Assert.That(load.CatchClockSeconds, Is.GreaterThan(0f));
            Assert.That(LoaderCatchPoseChanged(catchBones, initialCatchPose), Is.True,
                "The real skinned fish must visibly writhe on their owner's clock.");
            CaptureLoaderView(zombie, "loader-side", false);
            CaptureLoaderView(zombie, "loader-rear", true);
            CaptureLoaderView(zombie, "loader-catch", true, true);
            yield return CaptureEndlessGameView("zombie-ready", "ru");

            PlaceZombieChasePair(4.5f);
            Vector3 walkStart = zombie.transform.position;
            bool sampledWalk = false;
            for (int frame = 0; frame < 32; frame++)
            {
                root.Tick(.05f);
                yield return null;
                sampledWalk |= zombie.ActiveClipName == "ZombieWalk";
                CaptureLoaderView(zombie, "loader-walk" + frame.ToString("00"), false);
            }
            Assert.That(sampledWalk, Is.True);
            Assert.That(Vector3.ProjectOnPlane(zombie.transform.position - walkStart, Vector3.up).magnitude,
                Is.InRange(.55f, .85f), "A shared-clock gait cycle advances the real body with its authored .72 m stride.");
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            PlaceZombieChasePair(1.5f);

            Assert.That(Physics.GetIgnoreCollision(load.Collider, root.Hero.Body), Is.False);
            Assert.That(Physics.GetIgnoreCollision(zombie.Body, root.Hero.Body), Is.False);
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
            Vector3 committedRamRoot = zombie.transform.position;
            root.Tick(zombie.State.Settings.WindupSeconds * .55f);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero,
                "The visible preparation cannot damage the target.");
            CaptureLoaderView(zombie, "loader-prepare", false);
            yield return CaptureEndlessGameView("zombie-prepare", "ru");
            string activeContact = "No active midpoint sampled.";
            bool capturedActive = false;
            bool ignoredMotorProxy = false;
            bool ignoredBodyProxy = false;
            for (int step = 0; step < 240 && root.Hero.ReceivedImpactCount == 0; step++)
            {
                root.Tick(CombatTestRoot.SimulationStep);
                ignoredMotorProxy |= Physics.GetIgnoreCollision(load.Collider, root.Hero.Body);
                ignoredBodyProxy |= Physics.GetIgnoreCollision(zombie.Body, root.Hero.Body);
                if (!capturedActive && zombie.State.Phase == MeleePhase.Active &&
                    zombie.State.AttackElapsed >= zombie.State.Settings.WindupSeconds + zombie.State.Settings.ActiveSeconds * .5f)
                {
                    activeContact = $"Active midpoint elapsed={zombie.State.AttackElapsed:F3}: {LoaderRamDiagnostic(zombie, load)}";
                    CaptureLoaderView(zombie, "loader-active", false);
                    capturedActive = true;
                }
            }
            CaptureLoaderView(zombie, "loader-contact", false);
            Vector3 contactPoint = LoaderContactPoint(load);
            root.Hero.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Torso, out Vector3 heroTorso, out _, out _);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                $"The exposed head or real wooden front rail must reach the hero's actual body. Outcome={zombie.State.AttackOutcome}, " +
                $"phase={zombie.State.Phase}, elapsed={zombie.State.AttackElapsed:F3}, rail={contactPoint:F3}, zombie root={zombie.transform.position:F3}, " +
                $"committed root={committedRamRoot:F3}, hero root={root.Hero.transform.position:F3}, torso={heroTorso:F3}. " +
                $"Current: {LoaderRamDiagnostic(zombie, load)} {activeContact}");
            Assert.That(ignoredMotorProxy, Is.True,
                "Only the committed ram temporarily passes the wider motor capsule to reach the actual skin.");
            Assert.That(ignoredBodyProxy, Is.True,
                "The attacker's motor capsule must also yield to the real fused body's first contact.");
            Assert.That(root.Hero.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Unarmed));
            Assert.That(root.Hero.LastImpact.Damage, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(root.Hero.LastImpact.Point.sqrMagnitude), Is.True);
            Assert.That(float.IsFinite(root.Hero.LastImpact.Direction.sqrMagnitude), Is.True);
            Assert.That(root.Hero.State.IsDefeated, Is.False);
            float hitHealth = root.Hero.State.Health;
            yield return CaptureEndlessGameView("zombie-contact", "ru");
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Ready; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(Physics.GetIgnoreCollision(load.Collider, root.Hero.Body), Is.False,
                "Completing recovery must restore the motor pair before another action.");
            Assert.That(Physics.GetIgnoreCollision(zombie.Body, root.Hero.Body), Is.False);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(root.Hero.State.Health, Is.EqualTo(hitHealth).Within(.001f));
            Assert.That(zombie.State.Stamina, Is.EqualTo(zombie.State.Settings.MaxStamina));
            Assert.That(root.SparkEffects.EmissionCount, Is.Zero,
                "The fused wooden ram cannot emit the crowbar's metal clash effects.");

            PlacePair(.70f);
            Assert.That(zombie.TryZombieAttack(), Is.True);
            zombie.Step(zombie.State.Settings.WindupSeconds * .65f);
            Quaternion committedFacing = zombie.transform.rotation;
            root.Hero.ResetActor(root.Hero.transform.position + Vector3.right * 3f, Vector3.forward);
            Physics.SyncTransforms();
            zombie.Step(zombie.State.Settings.WindupSeconds + zombie.State.Settings.ActiveSeconds + .05f);
            Assert.That(Quaternion.Angle(committedFacing, zombie.transform.rotation), Is.LessThan(.01f),
                "A committed lunge cannot turn after the hero dodges.");
            Assert.That(zombie.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(zombie.TryZombieAttack(), Is.False, "A miss retains its recovery window.");
            Assert.That(Physics.GetIgnoreCollision(load.Collider, root.Hero.Body), Is.False);
            Assert.That(Physics.GetIgnoreCollision(zombie.Body, root.Hero.Body), Is.False);
            CaptureLoaderView(zombie, "loader-recovery", false);
            yield return CaptureEndlessGameView("zombie-miss", "ru");

            GameObject wall = null;
            try
            {
                root.SetSparring(true);
                PlaceZombieChasePair(1.5f);
                for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
                // Put a real obstacle into an already committed ram. Visibility
                // must not simply veto the AI before any travel is attempted.
                wall = new GameObject("Test zombie lunge wall");
                wall.transform.SetParent(root.transform, false);
                wall.transform.position = root.Hero.transform.position + new Vector3(0f, 1.2f, .7f);
                wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2.4f, .12f);
                Physics.SyncTransforms();
                Vector3 beforeWall = zombie.transform.position;
                root.Tick(1f);
                Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "A wall blocks the wooden rail and the fused body.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
                Assert.That(zombie.transform.position.z, Is.GreaterThan(.7f),
                    "The lunge's capsule must remain on its side of the obstacle.");
                Assert.That(Vector3.Distance(beforeWall, zombie.transform.position), Is.LessThan(.6f));
                yield return CaptureEndlessGameView("zombie-wall", "ru");
            }
            finally
            {
                if (wall != null) Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
            }

            root.SetSparring(true);
            PlaceZombieChasePair(1.5f);
            root.Hero.SetBlock(true);
            // Both actual rigs must raise the support arm and advance together;
            // changing only the rules clock cannot manufacture a physical guard.
            for (int step = 0; step < 120 && !(root.Hero.GuardReady && root.Hero.State.IsBlocking); step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.GuardReady && root.Hero.State.IsBlocking, Is.True,
                $"Guard did not become physically ready: rejection={root.Hero.GuardSupportRejection ?? "none"}, " +
                $"arm={root.Hero.SupportArmState}, grip={root.Hero.SupportGripWeight:F3}, " +
                $"phase={root.Hero.State.Phase}, clip={root.Hero.ActiveClipName}.");
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "The guard must be ready before the wooden rail contacts it.");
            root.Tick(root.Hero.State.Settings.ParryWindowSeconds + .01f);
            for (int step = 0; step < 240 && root.Hero.ReceivedImpactCount == 0; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                "The real shared-clock ram must reach the prepared guard.");
            Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
            Assert.That(root.Hero.LastImpact.Result, Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(root.Hero.State.Stamina,
                Is.EqualTo(root.Hero.State.Settings.MaxStamina - zombie.State.Settings.BlockCost).Within(.001f));
            Assert.That(root.SparkEffects.EmissionCount, Is.Zero);

            PlacePair(.70f);
            Assert.That(zombie.TryZombieAttack(), Is.True);
            zombie.Step(.2f);
            float elapsed = zombie.State.AttackElapsed;
            Vector3 heldPosition = zombie.transform.position;
            Vector3 heldCratePosition = crate.position;
            Quaternion heldCrateRotation = crate.rotation;
            float heldCatchClock = load.CatchClockSeconds;
            Pose[] heldCatchPose = CaptureLoaderCatchPose(catchBones);
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(2f);
            zombie.Step(2f);
            yield return null;
            Assert.That(zombie.State.AttackElapsed, Is.EqualTo(elapsed));
            Assert.That(zombie.transform.position, Is.EqualTo(heldPosition));
            Assert.That(crate.position, Is.EqualTo(heldCratePosition));
            Assert.That(Quaternion.Angle(crate.rotation, heldCrateRotation), Is.LessThan(.01f));
            Assert.That(load.CatchClockSeconds, Is.EqualTo(heldCatchClock));
            AssertLoaderCatchPose(catchBones, heldCatchPose);
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                "Zombie pause did not release gameplay input.");
            root.ResetRound();
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(zombie.State.AttackElapsed, Is.Zero);
            Assert.That(load.CatchClockSeconds, Is.Zero);
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(1));
            Assert.That(FindLoaderCatch(crate), Is.SameAs(catchSkin));
            CollectionAssert.AreEqual(catchBones, catchSkin.bones);
            AssertLoaderCatchPose(catchBones, initialCatchPose);
            PlacePair(3f);
            bool adjustedLoad = false, watched = false;
            for (int step = 0; step < 160 && !(adjustedLoad && watched); step++)
            {
                zombie.Step(.1f);
                adjustedLoad |= zombie.ActiveClipName == "ZombieLoadAdjust";
                watched |= zombie.ActiveClipName == "ZombieWatch";
                Assert.That(load.IsCarrying, Is.True);
            }
            Assert.That(adjustedLoad && watched, Is.True,
                "A waiting loader must actually play both bounded non-looping work habits.");
            AssertLoaderSideClearance(zombie, load);
            zombie.enabled = false;
            Assert.That(load.PhysicsBody.isKinematic, Is.True,
                "Disabling an actor must suspend its fused load as well.");
            Assert.That(crate.gameObject.activeInHierarchy, Is.False);
            zombie.enabled = true;
            root.SetSparring(true);
            PlaceZombieChasePair(1.5f);
            Assert.That(load.Model, Is.SameAs(crate));
            Assert.That(CountLoaderMeshInstances(crate), Is.EqualTo(crateMeshCount));
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(1));
            AssertLoaderCarrying(zombie, load);
            ApplyKnownBodyContact(zombie, root.Hero, BodyDamageRegion.RightHand, 0, 2f, 1);
            AssertLoaderAttached(zombie, load);
            zombie.Step(0f);
            AssertLoaderHunch(zombie);
            CaptureLoaderView(zombie, "loader-injured", false);
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(zombie.ZombieAttackUsesLeftSide, Is.True,
                "Losing the right hand selects the remaining supported side of the fused ram.");
            for (int step = 0; step < 240 && root.Hero.ReceivedImpactCount == 0; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            ApplyKnownBodyContact(zombie, root.Hero, BodyDamageRegion.LeftHand, 0, 2f, 2);
            Assert.That(zombie.TryZombieAttack(), Is.False);
            AssertLoaderAttached(zombie, load);
            root.ResetRound();
            Assert.That(Physics.GetIgnoreCollision(load.Collider, root.Hero.Body), Is.False,
                "Resetting an interrupted ram cannot leave the motor capsule excluded.");
            Assert.That(Physics.GetIgnoreCollision(zombie.Body, root.Hero.Body), Is.False);
            Assert.That(zombie.BodyDamage.CanUseLeftHand && zombie.BodyDamage.CanUseRightHand, Is.True);
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(1));
            Assert.That(load.Model, Is.SameAs(crate));
            Assert.That(CountLoaderMeshInstances(crate), Is.EqualTo(crateMeshCount));
            AssertLoaderCarrying(zombie, load);

            PlacePair(3f);
            Vector3 fusedPosition = crate.localPosition;
            Quaternion fusedRotation = crate.localRotation;
            ApplyKnownBodyContact(zombie, root.Hero, BodyDamageRegion.RightShin, 0, 2f, 3);
            Assert.That(zombie.IsBodyGrounded, Is.True);
            zombie.Step(.01f);
            AssertLoaderAttached(zombie, load);
            Assert.That(CombatCorpseTraversal.IsCorpse(load.Collider), Is.False,
                "The fused load of a living grounded worker remains a physical body.");
            Assert.That(load.ReleasedFishCount, Is.EqualTo(8),
                "A real fall spills every fish still enclosed by the surviving boards.");
            Assert.That(zombie.TryZombieAttack(), Is.False,
                "A surviving grounded loader cannot start a standing body ram.");
            foreach (var anatomical in zombie.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (anatomical.Key.attachedRigidbody == load.PhysicsBody) continue;
                Assert.That(Physics.GetIgnoreCollision(load.Collider, anatomical.Key), Is.True,
                    "The fused torso load cannot push its own physical limbs apart.");
            }
            for (int frame = 0; frame < 12; frame++)
            {
                zombie.Step(1f / 60f);
                yield return new WaitForFixedUpdate();
                AssertLoaderAttached(zombie, load);
                Assert.That(Vector3.Distance(crate.localPosition, fusedPosition), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(crate.localRotation, fusedRotation), Is.LessThan(.01f),
                    "The crate follows the physical chest without sliding or reparenting.");
            }
            CaptureLoaderView(zombie, "loader-fallen", false);
            root.ResetRound();
            Assert.That(load.Model, Is.SameAs(crate));
            AssertLoaderCarrying(zombie, load);

            // The actual AI and shared clock, rather than isolated actor stepping,
            // own the capsule's lunge travel and must preserve its chosen facing.
            root.SetSparring(true);
            PlaceZombieChasePair(1.5f);
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup),
                "The slow chase must commit to its close attack.");
            Vector3 lungeStart = zombie.transform.position;
            for (int step = 0; step < 240 && root.Hero.ReceivedImpactCount == 0; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(root.Hero.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Unarmed));
            Assert.That(Vector3.Distance(lungeStart, zombie.transform.position), Is.InRange(.1f, .83f),
                "The visible lunge advances its real capsule a short bounded distance.");
            yield return CaptureEndlessGameView("zombie-lunge", "ru");

            root.ResetRound();
            PlaceZombieChasePair(1.5f);
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
            committedFacing = zombie.transform.rotation;
            root.Hero.ResetActor(root.Hero.transform.position + Vector3.right * 3f, Vector3.forward);
            Physics.SyncTransforms();
            for (int step = 0; step < 240 && zombie.State.Phase != MeleePhase.Recovery; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(zombie.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
            Assert.That(Quaternion.Angle(committedFacing, zombie.transform.rotation), Is.LessThan(.01f));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            yield return CaptureEndlessGameView("zombie-dodge", "ru");

            yield return VerifyLoaderBreakage(zombie, load);

            // Re-enter the real warehouse in Endless: a single prepared body is recycled.
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "Zombie range did not release its scene.");
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            var pacing = new CombatEncounterSettings(1, .1f, .1f, .15f, .15f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Crowbar), CombatTestMode.Endless, pacing)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.Tick(.25f);
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
            zombie = root.Opponents[0];
            Assert.That(zombie.IsZombie && zombie.Weapon == null, Is.True);
            load = zombie.GetComponent<CombatZombieLoad>();
            crate = load.Model;
            crateMeshCount = CountLoaderMeshInstances(crate);
            catchSkin = FindLoaderCatch(crate);
            catchBones = catchSkin.bones;
            int catchGraphBuilds = load.CatchGraphBuildCount;
            Assert.That(catchGraphBuilds, Is.EqualTo(1));
            AssertLoaderCarrying(zombie, load);
            int graphBuilds = zombie.NpcAnimationGraphBuilds;
            yield return CaptureEndlessPopulationGameView("zombie-endless");
            Transform leftForearm = FindAnatomicalBone(zombie, "forearm.L");
            Transform rightForearm = FindAnatomicalBone(zombie, "forearm.R");
            float leftForearmScale = leftForearm.localScale.y, rightForearmScale = rightForearm.localScale.y;
            DefeatBodyTargetWithProjectile(zombie, root.Hero, 1);
            Assert.That(zombie.State.IsDefeated && zombie.IsRagdollActive, Is.True);
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(1));
            AssertLoaderAttached(zombie, load);
            Assert.That(CombatCorpseTraversal.IsCorpse(load.Collider), Is.True);
            Assert.That(Physics.GetIgnoreCollision(root.Hero.Body, load.Collider), Is.True,
                "A dead worker's fused box uses the same passable-remains policy as his body.");
            root.Tick(.08f);
            Assert.That(leftForearm.localScale.y, Is.EqualTo(leftForearmScale).Within(.001f));
            Assert.That(rightForearm.localScale.y, Is.EqualTo(rightForearmScale).Within(.001f),
                "The physical handoff must preserve the deformed arms on the original rig.");
            root.Tick(.35f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
            Assert.That(root.Opponents[0], Is.SameAs(zombie));
            Assert.That(zombie.State.IsDefeated || zombie.IsRagdollActive, Is.False);
            Assert.That(zombie.BodyDamage.CanUseLeftHand && zombie.BodyDamage.CanUseRightHand, Is.True);
            Assert.That(zombie.Weapon, Is.Null);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(graphBuilds));
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(catchGraphBuilds));
            Assert.That(FindLoaderCatch(crate), Is.SameAs(catchSkin));
            CollectionAssert.AreEqual(catchBones, catchSkin.bones);
            Assert.That(load.Model, Is.SameAs(crate), "Recycling restores the retained real crate.");
            Assert.That(CountLoaderMeshInstances(crate), Is.EqualTo(crateMeshCount),
                "Death/reset cannot accumulate hidden duplicates beside pooled actors.");
            AssertLoaderCarrying(zombie, load);
            Assert.That(CombatCorpseTraversal.IsCorpse(load.Collider), Is.False);
            Assert.That(root.EndlessKills, Is.EqualTo(1), "Reusing a body cannot credit its previous death twice.");
            root.ResetRound();
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(load.Model, Is.SameAs(crate));
            Assert.That(load.PhysicsBody.isKinematic, Is.True);
            Assert.That(load.CatchClockSeconds, Is.Zero);
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(catchGraphBuilds));
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyLoaderBreakage(CombatActor actor, CombatZombieLoad load)
        {
            root.ResetRound();
            root.SetSparring(false);
            PlacePair(4f);
            Assert.That(load.BoardCount, Is.EqualTo(36));
            int boardCount = load.BoardCount, graphBuilds = load.CatchGraphBuildCount;
            int woodMeshInstances = CountLoaderMeshInstances(load.Model);
            var boardBones = new Transform[boardCount];
            var boardBodies = new Rigidbody[boardCount];
            var fishRoots = new Transform[8];
            var fishBodies = new Rigidbody[8];
            for (int i = 0; i < boardCount; i++)
            {
                boardBones[i] = load.GetBoardBone(i); boardBodies[i] = load.GetBoardBody(i);
                Assert.That(boardBones[i] != null && boardBodies[i] != null, Is.True,
                    "Each finite wooden piece retains its authored bone and prepared physical body.");
            }
            for (int i = 0; i < 8; i++)
            {
                fishRoots[i] = load.GetFishRoot(i); fishBodies[i] = load.GetFishBody(i);
                Assert.That(fishRoots[i] != null && fishBodies[i] != null, Is.True);
            }
            var participants = new[] { root.Hero, actor };
            int front = LoaderRamBoard(load, 1), sequence = 100;
            Ray frontRay = LoaderBoardRay(load, front, out RaycastHit frontSurface);
            Vector3 openingPoint = frontSurface.point, openingNormal = frontSurface.normal;
            float health = actor.State.Health;
            FireLoaderProjectile(frontRay, participants, sequence++);
            Assert.That(load.DetachedBoardCount, Is.Zero, "One 25-damage bullet cannot sever a 40-health wooden bone.");
            Assert.That(load.ReleasedFishCount, Is.Zero);
            Assert.That(actor.State.Health, Is.EqualTo(health), "The nearer solid board intercepts the flight before anatomy.");
            FireLoaderProjectile(frontRay, participants, sequence++);
            Assert.That(load.IsBoardDetached(front), Is.True);
            Assert.That(load.DetachedBoardCount, Is.EqualTo(1));
            Assert.That(load.GetBoardCollider(front).enabled, Is.False);
            Assert.That(load.GetBoardBody(front).isKinematic, Is.False);
            actor.Step(.15f);
            Assert.That(load.ReleasedFishCount, Is.EqualTo(1), "One new opening releases one real fish.");
            Assert.That(actor.State.Health, Is.EqualTo(health));
            Assert.That(load.ContactSegments.Count, Is.EqualTo(1), "A severed wooden rail cannot remain an invisible ram.");
            int firstFish = 0;
            while (!load.IsFishReleased(firstFish)) firstFish++;
            Rigidbody spillingBody = fishBodies[firstFish];
            Assert.That(spillingBody.TryGetComponent(out Collider spillingShape) && spillingShape, Is.True);
            var spillDiagnostic = new StringBuilder(4096);
            spillDiagnostic.AppendLine($"selectedFishIndex={firstFish}, root={fishRoots[firstFish].name}, " +
                $"body={spillingBody.name}, physics={Physics.simulationMode}");
            float furthestExit = float.NegativeInfinity;
            for (int frame = 0; frame < Mathf.CeilToInt(1.6f / Time.fixedDeltaTime); frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
                furthestExit = Mathf.Max(furthestExit, Vector3.Dot(
                    fishBodies[firstFish].worldCenterOfMass - openingPoint, openingNormal));
                if (frame == 0 || frame == 5 || frame == 15 || frame == 30 || frame == 45)
                    AppendLoaderSpillDiagnostic(spillDiagnostic, frame, root, actor, load, spillingBody, spillingShape, openingNormal);
                if (frame % 3 == 0)
                    CaptureLoaderView(actor, "loader-spill" + (frame / 3).ToString("00"), true,
                        openingPoint: openingPoint, openingNormal: openingNormal);
            }
            yield return null; // Render the physical root after the completed LateUpdate.
            CaptureLoaderView(actor, "loader-spill", true, openingPoint: openingPoint, openingNormal: openingNormal);
            Assert.That(furthestExit, Is.GreaterThan(.02f),
                $"The real fish body must cross the missing board's outer plane while every other board remains attached. " +
                $"opening={openingPoint:F5}, normal={openingNormal:F5}, " +
                $"fishCentre={fishBodies[firstFish].worldCenterOfMass:F5}, maxExit={furthestExit:F5}\n" + spillDiagnostic);
            Assert.That(load.DetachedBoardCount, Is.EqualTo(1));
            Assert.That(load.ReleasedFishCount, Is.EqualTo(1));

            // The actual front flights above prove interception. This separate
            // dispatch contract covers every finite plank, including timber
            // legitimately obscured by the fused hand or torso.
            for (int i = 0; i < boardCount; i++)
            {
                if (load.IsBoardDetached(i)) continue;
                int detached = load.DetachedBoardCount;
                BoxCollider shape = load.GetBoardCollider(i);
                Ray ray = LoaderBoardRay(load, i, out RaycastHit boardContact, requireExposed: false);
                Assert.That(CombatZombieLoad.TryReceiveHit(shape, boardContact.point, ray.direction * 32f, 25f), Is.True);
                Assert.That(load.DetachedBoardCount, Is.EqualTo(detached), "A fresh wooden bone needs two 25-damage contacts: " + i);
                Assert.That(CombatZombieLoad.TryReceiveHit(shape, boardContact.point, ray.direction * 32f, 25f), Is.True);
                Assert.That(load.IsBoardDetached(i), Is.True, "Two accepted contacts must detach their registered wooden bone: " + i);
                Assert.That(load.DetachedBoardCount, Is.EqualTo(detached + 1));
                Assert.That(load.GetBoardCollider(i).enabled, Is.False);
                Assert.That(CombatZombieLoad.TryReceiveHit(shape, boardContact.point, ray.direction * 32f, 25f), Is.False,
                    "An already detached piece cannot dispatch another break or fish release.");
            }
            Assert.That(load.DetachedBoardCount, Is.EqualTo(boardCount));
            Assert.That(load.ContactSegments, Is.Empty);
            actor.Step(.15f);
            Assert.That(load.ReleasedFishCount, Is.EqualTo(8), "Repeated openings cannot manufacture more fish than the original catch.");
            // Step ends by presenting the human rig; released visual roots are
            // re-aligned in LateUpdate. Physics owns the exact pause boundary.
            Pose[] pausedFish = CaptureLoaderPhysicsPose(fishBodies);
            float pausedClock = load.CatchClockSeconds;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(2f);
            yield return null;
            yield return null;
            Assert.That(load.CatchClockSeconds, Is.EqualTo(pausedClock));
            AssertLoaderCatchPose(fishRoots, pausedFish, world: true);
            foreach (Rigidbody body in fishBodies) Assert.That(body.isKinematic, Is.True);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                "Spilled-fish pause did not release gameplay input.");
            root.Tick(0f);
            yield return new WaitForFixedUpdate();
            for (int step = 0; step < Mathf.CeilToInt(8.1f / Time.fixedDeltaTime); step++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(load.SettledFishCount, Is.EqualTo(8), "Finite convulsions must end with bounded physical simulation.");
            foreach (Rigidbody body in fishBodies) Assert.That(body.isKinematic, Is.True);
            yield return null;
            Transform[] catchBones = LoaderCatchDeformationBones(load);
            Pose[] settledCatch = CaptureLoaderCatchPose(catchBones);
            Pose[] settledFish = CaptureLoaderPhysicsPose(fishBodies);
            AssertLoaderCatchPose(fishRoots, settledFish, world: true);
            root.Tick(1f);
            yield return new WaitForFixedUpdate();
            yield return null;
            AssertLoaderCatchPose(catchBones, settledCatch);
            AssertLoaderCatchPose(fishRoots, settledFish, world: true);
            CaptureLoaderView(actor, "loader-broken", false);

            actor.CaptureContactPose();
            Assert.That(actor.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Torso, out Vector3 torso, out _, out _), Is.True);
            Vector3 outward = actor.transform.forward;
            Ray openRay = new Ray(torso + outward * .9f, -outward);
            Assert.That(actor.Hurtboxes.SweepProjectile(openRay.origin, torso - outward * .3f,
                CombatProjectilePool.Radius, openRay.direction, out _), Is.True);
            int bodyContacts = actor.ReceivedImpactCount;
            FireLoaderProjectile(openRay, participants, sequence++);
            Assert.That(actor.ReceivedImpactCount, Is.EqualTo(bodyContacts + 1),
                "A bullet through the destroyed enclosure reaches the actual body, without an invisible full-box wall.");

            root.ResetRound();
            PlacePair(4f);
            Assert.That(load.DetachedBoardCount, Is.Zero);
            Assert.That(load.ReleasedFishCount, Is.Zero);
            Assert.That(load.SettledFishCount, Is.Zero);
            Assert.That(load.CatchClockSeconds, Is.Zero);
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(graphBuilds));
            for (int i = 0; i < boardCount; i++)
            {
                Assert.That(load.GetBoardBone(i), Is.SameAs(boardBones[i]));
                Assert.That(load.GetBoardBody(i), Is.SameAs(boardBodies[i]));
                Assert.That(load.GetBoardCollider(i).enabled, Is.True);
                Assert.That(load.IsBoardDetached(i), Is.False);
            }
            for (int i = 0; i < 8; i++)
            {
                Assert.That(load.GetFishRoot(i), Is.SameAs(fishRoots[i]));
                Assert.That(load.GetFishBody(i), Is.SameAs(fishBodies[i]));
                Assert.That(load.IsFishReleased(i), Is.False);
            }
            AssertLoaderCarrying(actor, load);

            frontRay = LoaderBoardRay(load, front, out _);
            Assert.That(root.Projectiles.TrySpawnVolley(root.Hero, frontRay.origin,
                Quaternion.LookRotation(frontRay.direction), sequence++, ShotgunSettings.Prototype), Is.True);
            root.Projectiles.Advance(.01f, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(load.IsBoardDetached(front), Is.True, "Actual close shotgun pellets contribute their distance-weighted damage.");
            int detachedFlags = 0;
            for (int i = 0; i < boardCount; i++) if (load.IsBoardDetached(i)) detachedFlags++;
            Assert.That(load.DetachedBoardCount, Is.EqualTo(detachedFlags).And.InRange(1, boardCount),
                "Later pellets are independent flights through any newly opened hole; each original board detaches once.");
            Assert.That(load.GetBoardCollider(front).enabled, Is.False);
            Assert.That(load.GetBoardBody(front), Is.SameAs(boardBodies[front]));
            Assert.That(CountLoaderMeshInstances(load.Model), Is.EqualTo(woodMeshInstances));
            root.Tick(.3f);
            Assert.That(load.ReleasedFishCount, Is.InRange(1, 8));
            if (actor.IsKnockedDown || actor.IsRagdollActive || actor.IsBodyGrounded || actor.State.IsDefeated)
                Assert.That(load.ReleasedFishCount, Is.EqualTo(8), "A volley-induced fall releases the remaining original catch.");

            root.ResetRound();
            PlacePair(4f);
            frontRay = LoaderBoardRay(load, front, out RaycastHit woodenContact);
            PreparePendingMetalActor(root.Hero);
            CombatActor.Contact contact = CombatActor.Contact.Obstacle(root.Hero, null, woodenContact.point,
                woodenContact.normal, frontRay.direction, .5f, false,
                worldCollider: load.GetBoardCollider(front), weaponSpeed: 5f);
            var pending = new List<CombatActor.Contact> { contact, contact };
            CombatActor.ApplyContacts(pending);
            Assert.That(load.DetachedBoardCount, Is.Zero, "The first accepted melee interception damages its exact plank once.");
            Assert.That(pending.Count, Is.EqualTo(1));
            FireLoaderProjectile(frontRay, participants, sequence++);
            Assert.That(load.IsBoardDetached(front), Is.True, "The saved melee damage and a subsequent bullet share the wooden bone's health.");
            actor.Step(.15f);
            Assert.That(load.ReleasedFishCount, Is.EqualTo(1));
            root.ResetRound();
        }

        private static int LoaderRamBoard(CombatZombieLoad load, int rail)
        {
            Transform marker = CityPedestrianHandProps.FindSocket(load.Model, "RamBar" + rail + "A");
            Assert.That(marker, Is.Not.Null);
            for (int i = 0; i < load.BoardCount; i++)
                if (marker.IsChildOf(load.GetBoardBone(i))) return i;
            Assert.Fail("A real front ram marker must belong to a detachable wooden bone.");
            return -1;
        }

        private Ray LoaderBoardRay(CombatZombieLoad load, int board, out RaycastHit contact, bool requireExposed = true)
        {
            Physics.SyncTransforms();
            Transform bone = load.GetBoardBone(board);
            Transform outward = CityPedestrianHandProps.FindSocket(bone, bone.name + "Outward");
            Assert.That(outward, Is.Not.Null, bone.name);
            BoxCollider shape = load.GetBoardCollider(board);
            Vector3 normal = (outward.position - bone.position).normalized;
            Assert.That(normal.sqrMagnitude, Is.GreaterThan(.9f));
            CombatActor target = load.GetComponent<CombatActor>();
            var approach = new Ray(shape.bounds.center + normal * .24f, -normal);
            Assert.That(shape.Raycast(approach, out contact, .5f), Is.True,
                bone.name + " requires an actual external wooden surface.");
            var ray = new Ray(contact.point + contact.normal * .012f, -contact.normal);
            if (requireExposed)
            {
                target.CaptureContactPose();
                Assert.That(LoaderRayClearToWood(ray, target, shape, out string diagnostic), Is.True,
                    bone.name + " must be the first actual flight surface. " + diagnostic);
            }
            return ray;
        }

        private void FireLoaderProjectile(Ray ray, CombatActor[] participants, int sequence)
        {
            Physics.SyncTransforms();
            participants[1].CaptureContactPose();
            LoaderRayClearToWood(ray, participants[1], null, out string diagnostic);
            int impacts = root.Projectiles.ImpactCount;
            Assert.That(root.Projectiles.TrySpawn(root.Hero, ray.origin,
                ray.direction * CombatProjectilePool.MuzzleSpeed, sequence), Is.True);
            root.Projectiles.Advance(.01f, participants);
            root.Projectiles.ApplyContacts();
            string outcome = diagnostic + $" actualImpact={root.Projectiles.LastImpactPoint:F5}, " +
                $"bodyContacts={participants[1].ReceivedImpactCount}, health={participants[1].State.Health:F2}, " +
                $"lastResult={participants[1].LastImpact.Result}, lastPart={participants[1].LastImpact.Part}";
            Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(impacts + 1), outcome);
            Assert.That(root.Projectiles.ActiveCount, Is.Zero, outcome);
        }

        private bool LoaderRayClearToWood(Ray ray, CombatActor target, Collider expected, out string diagnostic)
        {
            const float length = .06f;
            Collider nearest = null;
            float worldDistance = float.PositiveInfinity;
            foreach (Collider shape in Physics.OverlapSphere(ray.origin, CombatProjectilePool.Radius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (CombatProjectilePool.IsWorld(shape, root.Hero)) { nearest = shape; worldDistance = 0f; break; }
            foreach (RaycastHit hit in Physics.SphereCastAll(ray.origin, CombatProjectilePool.Radius, ray.direction,
                length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (CombatProjectilePool.IsWorld(hit.collider, root.Hero) && hit.distance < worldDistance)
                { nearest = hit.collider; worldDistance = hit.distance; }
            bool body = target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(length), CombatProjectilePool.Radius,
                ray.direction, out CombatHurtboxes.Hit anatomy);
            float bodyDistance = body ? anatomy.Fraction * length : float.PositiveInfinity;
            diagnostic = $"rayOrigin={ray.origin:F5}, direction={ray.direction:F5}, " +
                $"firstWorld={(nearest ? nearest.name : "none")}@{worldDistance:F5}, " +
                $"firstAnatomy={(body ? anatomy.Part.ToString() : "none")}@{bodyDistance:F5}, " +
                $"anatomyPoint={anatomy.Point:F5}, phase={target.State.Phase}.";
            return nearest == expected && nearest && bodyDistance >= worldDistance;
        }

        private static void AssertLoaderCarrying(CombatActor actor, CombatZombieLoad load)
        {
            Assert.That(load, Is.Not.Null);
            Assert.That(load.IsCarrying, Is.True);
            Assert.That(load.Model, Is.Not.Null);
            Assert.That(load.Model.gameObject.activeInHierarchy, Is.True);
            Assert.That(load.Collider, Is.Not.Null);
            Assert.That(load.Collider.enabled && !load.Collider.isTrigger, Is.True,
                "The visible wooden load has a real solid collision volume.");
            Assert.That(load.PhysicsBody, Is.Not.Null);
            Assert.That(load.PhysicsBody.isKinematic, Is.True);
            Assert.That(load.PhysicsBody.mass, Is.GreaterThan(5f));
            Renderer[] renderers = load.Model.GetComponentsInChildren<Renderer>();
            Assert.That(renderers, Is.Not.Empty,
                "A collision-only placeholder does not fulfil the carried-load silhouette.");
            Mesh box = FindLoaderMesh(load.Model, "ZombieCrateMesh", out Renderer woodRenderer);
            Assert.That(box.vertexCount, Is.GreaterThan(24));
            Bounds visibleBounds = woodRenderer.bounds;
            Assert.That(visibleBounds.size.magnitude, Is.InRange(1.8f, 2.8f),
                "The visible imported model must have the same metre scale as its collider.");
            Assert.That(Vector3.Distance(visibleBounds.center, load.AttachedBounds.center), Is.LessThan(.06f));
            SkinnedMeshRenderer catchMesh = FindLoaderCatch(load.Model);
            Assert.That(catchMesh.sharedMesh.vertexCount, Is.GreaterThan(8 * 24),
                "The catch contains authored volumetric fish, rather than an empty marker or a textured box face.");
            Assert.That(catchMesh.sharedMesh.GetIndexCount(0), Is.GreaterThan(8 * 36));
            Bounds catchBounds = catchMesh.bounds;
            Assert.That(catchBounds.size.magnitude, Is.InRange(1.6f, 2.8f));
            Assert.That(catchBounds.Intersects(load.AttachedBounds), Is.True,
                "The fish heap occupies the real crate while remaining free to protrude above its rim.");
            Mesh fusion = FindLoaderMesh(load.Model, "ZombieFusionMesh", out Renderer fusionRenderer);
            Assert.That(fusion.vertexCount, Is.GreaterThan(3 * 24),
                "The crate is connected to the worker by visible three-dimensional tissue.");
            Assert.That(fusion.GetIndexCount(0), Is.GreaterThan(3 * 36));
            Bounds fusionBounds = fusionRenderer.bounds;
            Assert.That(fusionBounds.size.magnitude, Is.InRange(.9f, 2.4f));
            Vector3 size = load.CrateDimensions;
            float[] dimensions = { size.x, size.y, size.z };
            System.Array.Sort(dimensions);
            Assert.That(dimensions[0], Is.EqualTo(.70f).Within(.035f));
            Assert.That(dimensions[1], Is.EqualTo(1.20f).Within(.035f));
            Assert.That(dimensions[2], Is.EqualTo(1.24f).Within(.035f),
                "Measure the placed box in metres: an imported FBX can retain a unit-scale error.");
            Transform chest = FindAnatomicalBone(actor, "chest");
            Assert.That(load.Model.IsChildOf(chest), Is.True);
            Assert.That(fusionBounds.Intersects(visibleBounds), Is.True,
                "The tissue must visibly reach the wooden load.");
            Assert.That(Vector3.Distance(fusionBounds.ClosestPoint(chest.position), chest.position),
                Is.LessThan(.22f), "The tissue must also reach the worker's chest surface.");
            Assert.That(load.ContainsTorso, Is.True,
                "The compressed human torso has grown into the middle of the wooden crate.");
        }

        private static void AssertLoaderHunch(CombatActor actor)
        {
            Transform spine = FindAnatomicalBone(actor, "spine");
            Transform neck = FindAnatomicalBone(actor, "neck");
            Assert.That(Vector3.Dot(neck.position - spine.position, actor.transform.forward),
                Is.GreaterThan(.08f), "The compressed torso stays folded forwards beneath the exposed head.");
            Vector3 stance = FindAnatomicalBone(actor, "foot.L").position - FindAnatomicalBone(actor, "foot.R").position;
            Assert.That(Vector3.ProjectOnPlane(stance, Vector3.up).magnitude, Is.GreaterThanOrEqualTo(.60f),
                "The planted feet spread wide to bear the fused crate's mass.");
        }

        private void AssertLoaderHeadFacing(CombatActor actor)
        {
            actor.Step(.5f);
            Vector3 heroHead = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Head.position;
            Assert.That(actor.CombatAttentionFocus, Is.Not.Null);
            Assert.That(Vector3.Distance(actor.CombatAttentionFocus.Value, heroHead), Is.LessThan(.001f),
                "The arched head follows the real hero's head through the shared attention owner.");
            actor.CaptureContactPose();
            Assert.That(actor.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 center,
                out Vector3 faceForward, out _), Is.True);
            // Hurtboxes derives this frame from the production skin bind pose;
            // imported head-local Euler axes are not the anatomical face direction.
            Assert.That(Vector3.Angle(faceForward, heroHead - center), Is.LessThan(20f),
                "The neck remains grotesquely arched while the visible face looks at the player.");
        }

        private static void AssertLoaderLimpHands(CombatActor actor)
        {
            NpcHandPose pose = actor.GetComponentInChildren<NpcHandPose>();
            Assert.That(pose, Is.Not.Null);
            Assert.That(pose.Hands.Count, Is.EqualTo(2));
            foreach (NpcHandPose.HandBinding hand in pose.Hands)
            {
                string bone = hand.IsLeft ? "hand.L" : "hand.R";
                Assert.That(hand.Hand, Is.SameAs(FindAnatomicalBone(actor, bone)),
                    "The warped arm retains the worker's original visible hand.");
                // The retained palm centre has a normal offset from the bone
                // axis; removing that offset recovers the real finger direction.
                Vector3 fingerAxis = Vector3.ProjectOnPlane(hand.CentreAnchor.position - hand.Hand.position,
                    pose.PalmNormal(hand.IsLeft)).normalized;
                Assert.That(Vector3.Angle(fingerAxis, Vector3.down), Is.LessThan(20f),
                    bone + ": the limp hand hangs down rather than holding an invisible crate grip.");
                bool visible = false;
                foreach (SkinnedMeshRenderer renderer in hand.Renderers)
                    visible |= renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
                Assert.That(visible, Is.True, bone + ": the measured hand must actually be visible.");
            }
        }

        private static void AssertLoaderGenericRecoveryAnatomy(CombatActor actor)
        {
            string[] names = { "spine", "chest", "neck", "head", "clavicle.L", "clavicle.R",
                "forearm.L", "forearm.R", "hand.L", "hand.R" };
            var bones = new Transform[names.Length];
            var positions = new Vector3[names.Length];
            var scales = new Vector3[names.Length];
            var rotations = new Quaternion[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                bones[i] = FindAnatomicalBone(actor, names[i]);
                positions[i] = bones[i].localPosition;
                scales[i] = bones[i].localScale;
                rotations[i] = bones[i].localRotation;
            }
            AnimationClip recovery = CombatAssetProvider.LoadClip(CombatAssetProvider.RiseProneClip, true);
            try
            {
                // A repeated sample checks that permanent anatomy is retained
                // without accumulating its offsets onto the ordinary rise clip.
                for (int sample = 0; sample < 2; sample++)
                {
                    actor.SampleNpcClip(recovery, .4f);
                    bool rotated = false;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (i < 4)
                            Assert.That(bones[i].parent.TransformVector(bones[i].localPosition - positions[i]).magnitude,
                                Is.LessThan(.001f), names[i] + ": generic recovery retains the compressed anatomy.");
                        Assert.That(Vector3.Distance(bones[i].localScale, scales[i]), Is.LessThan(.0001f),
                            names[i] + ": generic recovery retains the deformed proportions.");
                        rotated |= Quaternion.Angle(bones[i].localRotation, rotations[i]) > 1f;
                    }
                    Assert.That(rotated, Is.True, "The ordinary rise still supplies its own recovery rotations.");
                }
            }
            finally { actor.Present(); }
            AssertLoaderHunch(actor);
        }

        private static void AssertLoaderAttached(CombatActor actor, CombatZombieLoad load)
        {
            Assert.That(load.IsCarrying, Is.True);
            Assert.That(load.Model.gameObject.activeInHierarchy, Is.True);
            Assert.That(load.Model.IsChildOf(FindAnatomicalBone(actor, "chest")), Is.True,
                "The load has grown into the chest and cannot detach after injury or death.");
            Assert.That(load.PhysicsBody, Is.SameAs(actor.Ragdoll.PhysicsController.ChestBody));
            Assert.That(load.Collider.attachedRigidbody, Is.SameAs(load.PhysicsBody),
                "The fused load shares the existing torso body as a compound collider.");
            Assert.That(load.Collider.enabled && !load.Collider.isTrigger, Is.True);
            Assert.That(float.IsFinite(load.Model.position.sqrMagnitude), Is.True);
        }

        private static void AssertLoaderFootsteps(CombatActor actor)
        {
            NpcFootstepProfile profile = actor.GetComponentInChildren<NpcFootstepProfile>();
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Stride, Is.EqualTo(.36f).Within(.001f));
            Assert.That(profile.MinimumSpeed, Is.EqualTo(.08f).Within(.001f));
            Assert.That(profile.VolumeScale, Is.EqualTo(1f));
            Assert.That(profile.UsesAuthoredContacts, Is.True);
            profile.ResetContacts();
            profile.NotifyFootfall();
            Assert.That(profile.ConsumeFootfall(), Is.True);
            Assert.That(profile.ConsumeFootfall(), Is.False, "One authored foot plant can sound only once.");
            profile.NotifyFootfall();
            profile.ResetContacts();
            Assert.That(profile.ConsumeFootfall(), Is.False, "Recycling must discard an unconsumed foot plant.");
            var tracker = new NpcStrideTracker();
            Assert.That(tracker.Advance(Vector3.zero, .1f, profile), Is.False);
            Assert.That(tracker.Advance(Vector3.forward * .002f, .1f, profile), Is.False);
            Assert.That(tracker.Advance(Vector3.forward * .002f, .1f, profile), Is.False,
                "Small pose drift and standing still cannot make heavy footfalls.");
            tracker.Reset();
            tracker.Advance(Vector3.zero, .1f, profile);
            const float speed = .72f / 1.6f, delta = .1f;
            for (int sample = 1; sample <= 7; sample++)
                Assert.That(tracker.Advance(Vector3.forward * (speed * delta * sample), delta, profile), Is.False);
            int footfalls = 0;
            for (int sample = 8; sample <= 9; sample++)
                if (tracker.Advance(Vector3.forward * (speed * delta * sample), delta, profile)) footfalls++;
            Assert.That(footfalls, Is.EqualTo(1),
                "A loader moving at .72 m per 1.6 s must sound after its .36 m achieved stride.");
            Assert.That(tracker.Distance, Is.EqualTo(speed * delta * 9f - profile.Stride).Within(.001f));
        }

        private void AssertLoaderSideClearance(CombatActor actor, CombatZombieLoad load)
        {
            var wall = new GameObject("Test loader crate clearance wall");
            try
            {
                wall.transform.SetParent(root.transform, false);
                Bounds bounds = load.AttachedBounds;
                wall.transform.position = new Vector3(bounds.max.x + .15f, bounds.center.y, bounds.center.z);
                wall.AddComponent<BoxCollider>().size = new Vector3(.08f, 3f, 3f);
                Physics.SyncTransforms();
                Assert.That(wall.transform.position.x - .04f - actor.transform.position.x,
                    Is.GreaterThan(actor.Body.radius + .2f), "This obstacle clears the ordinary body capsule.");
                Vector3 permitted = load.LimitTravel(Vector3.right * .4f);
                Assert.That(permitted.x, Is.InRange(.03f, .25f),
                    "The oversized box must stop lateral travel before its timber enters the wall.");
                Assert.That(Vector3.Cross(permitted, Vector3.right).sqrMagnitude, Is.LessThan(.000001f));
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
            }
            Assert.That(load.LimitTravel(Vector3.right * .4f).x, Is.EqualTo(.4f).Within(.001f),
                "Removing the obstacle must restore the actual box's free travel.");
        }

        private static int CountLoaderMeshInstances(Transform crate)
        {
            Mesh source = FindLoaderMesh(crate, "ZombieCrateMesh", out _);
            int count = 0;
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                if (LoaderSharedMesh(renderer) == source) count++;
            return count;
        }

        private static Vector3 LoaderContactPoint(CombatZombieLoad load)
        {
            CombatWeaponGeometry.Segment rail = load.ContactSegments[0];
            Pose pose = load.ContactPose;
            return pose.position + pose.rotation * ((rail.A + rail.B) * .5f);
        }

        private static string LoaderRamDiagnostic(CombatActor actor, CombatZombieLoad load)
        {
            Pose pose = load.ContactPose;
            Bounds bounds = load.Collider.bounds;
            string result = $"root={actor.transform.position:F3}, ContactPose=({pose.position:F3}; {pose.rotation.eulerAngles:F3}), " +
                $"modelScale={load.Model.lossyScale:F3}, boxZ=[{bounds.min.z:F3},{bounds.max.z:F3}], " +
                $"headPoint={actor.ZombieRamHeadPoint:F3}, inscribedHeadRadius={actor.ZombieRamHeadRadius:F4}.";
            for (int index = 0; index < load.ContactSegments.Count; index++)
            {
                string prefix = "RamBar" + (index + 1);
                Transform actualA = CityPedestrianHandProps.FindSocket(load.Model, prefix + "A");
                Transform actualB = CityPedestrianHandProps.FindSocket(load.Model, prefix + "B");
                CombatWeaponGeometry.Segment segment = load.ContactSegments[index];
                Vector3 cachedA = pose.position + pose.rotation * segment.A;
                Vector3 cachedB = pose.position + pose.rotation * segment.B;
                if (actualA == null || actualB == null)
                {
                    result += $" {prefix} missing actual markers.";
                    continue;
                }
                result += $" {prefix} actualA={actualA.position:F3}, actualB={actualB.position:F3}, " +
                    $"actualMid={(actualA.position + actualB.position) * .5f:F3}, " +
                    $"cachedA={cachedA:F3}, cachedB={cachedB:F3}, cachedMid={(cachedA + cachedB) * .5f:F3}, " +
                    $"markerDelta=({Vector3.Distance(actualA.position, cachedA):F6},{Vector3.Distance(actualB.position, cachedB):F6}), " +
                    $"radius={segment.Radius:F4}.";
            }
            return result;
        }

        private static Mesh LoaderSharedMesh(Renderer renderer)
        {
            if (!renderer) return null;
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
            return renderer.TryGetComponent(out MeshFilter filter) && filter ? filter.sharedMesh : null;
        }

        private static Mesh FindLoaderMesh(Transform model, string name, out Renderer found)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = LoaderSharedMesh(renderer);
                if (renderer.name == name || mesh != null && mesh.name == name)
                {
                    Assert.That(mesh, Is.Not.Null, name);
                    Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True, name);
                    found = renderer;
                    return mesh;
                }
            }
            Assert.Fail("Missing authored loader geometry " + name);
            found = null;
            return null;
        }

        private static SkinnedMeshRenderer FindLoaderCatch(Transform model)
        {
            Mesh mesh = FindLoaderMesh(model, "ZombieCatchMesh", out Renderer renderer);
            Assert.That(renderer, Is.TypeOf<SkinnedMeshRenderer>(), "The finite catch shares one animated renderer.");
            var skin = (SkinnedMeshRenderer)renderer;
            int catches = 0;
            foreach (SkinnedMeshRenderer candidate in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (candidate.sharedMesh == mesh) catches++;
            Assert.That(catches, Is.EqualTo(1), "There is one retained catch renderer, alongside the wooden skeleton.");
            Assert.That(skin.sharedMesh, Is.Not.Null);
            Assert.That(skin.enabled && skin.gameObject.activeInHierarchy, Is.True);
            Assert.That(skin.bones, Is.Not.Empty, "The visible fish retain their compact authored rig.");
            return skin;
        }

        private static Pose[] CaptureLoaderCatchPose(Transform[] bones, bool world = false)
        {
            var poses = new Pose[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                poses[i] = new Pose(world ? bones[i].position : bones[i].localPosition,
                    world ? bones[i].rotation : bones[i].localRotation);
            return poses;
        }

        private static void AppendLoaderSpillDiagnostic(StringBuilder report, int frame, CombatTestRoot root,
            CombatActor actor, CombatZombieLoad load, Rigidbody body, Collider shape, Vector3 openingNormal)
        {
            report.AppendLine($"frame={frame}, fixedTime={Time.fixedTime:F5}, timeScale={Time.timeScale:F3}, " +
                $"position={body.position:F6}, COM={body.worldCenterOfMass:F6}, velocity={body.linearVelocity:F6}, " +
                $"gravity={body.useGravity}, constraints={body.constraints}, kinematic={body.isKinematic}, " +
                $"sleeping={body.IsSleeping()}, active={body.gameObject.activeInHierarchy}, detectCollisions={body.detectCollisions}");
            report.AppendLine($"catchClock={load.CatchClockSeconds:F6}, " +
                $"gameplayInput={GameInput.CanRead(GameInputContext.Gameplay)}, rootActive={root.isActiveAndEnabled}, " +
                $"actorEnabled={actor.enabled}, actorActive={actor.isActiveAndEnabled}");
            string localShape = shape is CapsuleCollider capsule ?
                $"capsule centre={capsule.center:F6}, radius={capsule.radius:F6}, height={capsule.height:F6}, axis={capsule.direction}" :
                shape is BoxCollider box ? $"box centre={box.center:F6}, localSize={box.size:F6}" : shape.GetType().Name;
            report.AppendLine($"actor knockdown={actor.IsKnockedDown}, ragdoll={actor.IsRagdollActive}, " +
                $"grounded={actor.IsBodyGrounded}, phase={actor.State.Phase}; " +
                $"shape enabled={shape.enabled}, worldCentre={shape.bounds.center:F6}, worldSize={shape.bounds.size:F6}, " +
                $"{localShape}, scale={shape.transform.lossyScale:F6}");
            int penetrations = 0, ignored = 0;
            foreach (Collider other in Physics.OverlapBox(shape.bounds.center, shape.bounds.extents + Vector3.one * .005f,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!other || other == shape || !other.enabled || !other.gameObject.activeInHierarchy ||
                    other.attachedRigidbody == body) continue;
                bool layerIgnored = Physics.GetIgnoreLayerCollision(shape.gameObject.layer, other.gameObject.layer);
                bool pairIgnored = Physics.GetIgnoreCollision(shape, other);
                if (!Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out Vector3 normal, out float depth)) continue;
                bool collisionIgnored = layerIgnored || pairIgnored;
                int count = collisionIgnored ? ++ignored : ++penetrations;
                if (count > 4) continue;
                CombatActor owner = other.GetComponentInParent<CombatActor>();
                string ownerName = owner ? owner.name + "#" + owner.JournalActorId : "none";
                string bodyName = other.attachedRigidbody ? other.attachedRigidbody.name : "static";
                report.AppendLine($"penetration collider={other.name}, owner={ownerName}, body={bodyName}, " +
                    $"ignored={collisionIgnored}, pairIgnored={pairIgnored}, layerIgnored={layerIgnored}, " +
                    $"load={CombatZombieLoad.IsLoadCollider(other)}, depth={depth:F6}, normal={normal:F6}, " +
                    $"boundsCentre={other.bounds.center:F6}, boundsSize={other.bounds.size:F6}");
            }
            report.AppendLine($"activePenetrations={penetrations}, ignoredPenetrations={ignored}, maxShownPerKind=4");
            if (frame == 0 || frame == 5 || frame == 15)
            {
                RaycastHit[] sweeps = body.SweepTestAll(openingNormal, .6f, QueryTriggerInteraction.Ignore);
                System.Array.Sort(sweeps, (a, b) => a.distance.CompareTo(b.distance));
                int blockers = 0;
                foreach (RaycastHit hit in sweeps)
                {
                    Collider other = hit.collider;
                    if (!other || other == shape || !other.enabled || !other.gameObject.activeInHierarchy ||
                        other.attachedRigidbody == body ||
                        Physics.GetIgnoreLayerCollision(shape.gameObject.layer, other.gameObject.layer) ||
                        Physics.GetIgnoreCollision(shape, other)) continue;
                    CombatActor owner = other.GetComponentInParent<CombatActor>();
                    report.AppendLine($"sweepBlocker collider={other.name}, owner={(owner ? owner.name : "none")}, " +
                        $"distance={hit.distance:F6}, normal={hit.normal:F6}, point={hit.point:F6}");
                    if (++blockers == 4) break;
                }
                report.AppendLine($"sweepDirection={openingNormal:F6}, reach=.6, shownBlockers={blockers}");
            }
        }

        private static Pose[] CaptureLoaderPhysicsPose(Rigidbody[] bodies)
        {
            var poses = new Pose[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) poses[i] = new Pose(bodies[i].position, bodies[i].rotation);
            return poses;
        }

        private static Transform[] LoaderCatchDeformationBones(CombatZombieLoad load)
        {
            var bones = new Transform[24];
            string[] roles = { "Body", "Head", "Tail" };
            for (int fish = 0; fish < 8; fish++)
                for (int role = 0; role < roles.Length; role++)
                {
                    Transform bone = CityPedestrianHandProps.FindSocket(load.GetFishRoot(fish),
                        "Catch" + (fish + 1) + roles[role]);
                    Assert.That(bone, Is.Not.Null);
                    bones[fish * roles.Length + role] = bone;
                }
            return bones;
        }

        private static bool LoaderCatchPoseChanged(Transform[] bones, Pose[] poses)
        {
            for (int i = 0; i < bones.Length; i++)
                if (Vector3.Distance(bones[i].localPosition, poses[i].position) > .001f ||
                    Quaternion.Angle(bones[i].localRotation, poses[i].rotation) > 1f) return true;
            return false;
        }

        private static void AssertLoaderCatchPose(Transform[] bones, Pose[] poses, bool world = false)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.That(Vector3.Distance(world ? bones[i].position : bones[i].localPosition, poses[i].position),
                    Is.LessThan(.0001f), bones[i].name);
                Assert.That(Quaternion.Angle(world ? bones[i].rotation : bones[i].localRotation, poses[i].rotation),
                    Is.LessThan(.05f), bones[i].name);
            }
        }

        private void CaptureLoaderView(CombatActor actor, string name, bool rear, bool catchDetail = false,
            Vector3? openingPoint = null, Vector3? openingNormal = null)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                // Render the actual production camera/lighting at a useful detail
                // scale. The temporary view cannot advance or resample the rig.
                Vector3 focus = actor.transform.position + Vector3.up * .95f;
                Vector3 eye = actor.transform.position + Vector3.up * 1.3f +
                    (rear ? -actor.transform.forward * 3.0f + actor.transform.right * 1.05f
                        : actor.transform.right * 3.2f + actor.transform.forward * .35f);
                if (catchDetail)
                {
                    focus = actor.GetComponent<CombatZombieLoad>().AttachedBounds.center;
                    Vector3 axis = (FindAnatomicalBone(actor, "neck").position -
                        FindAnatomicalBone(actor, "spine").position).normalized;
                    eye = focus + Vector3.ProjectOnPlane(Vector3.up, axis).normalized * 1.8f +
                        actor.transform.right * .45f;
                }
                if (openingPoint.HasValue && openingNormal.HasValue)
                {
                    focus = Vector3.Lerp(focus, openingPoint.Value, .5f);
                    eye = focus + openingNormal.Value * 2.7f + actor.transform.right * .45f + Vector3.up * .45f;
                }
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 42f;
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
        }

        private void PlaceZombieChasePair(float distance)
        {
            Assert.That(root.SetOpponentFocus(true), Is.True);
            Vector3 position = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(position, Vector3.forward);
            root.Opponent.ResetActor(position + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }
    }
}
