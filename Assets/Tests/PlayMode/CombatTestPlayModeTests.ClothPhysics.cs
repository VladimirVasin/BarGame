using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [Category("CurrentCombat")]
        [UnityTest]
        public IEnumerator Range_CombatClothRemainsRigidAcrossMovementWeaponsAndLifecycle()
        {
            bool previousBakes = SecondaryMotionBakeRuntime.Enabled;
            int? previousSeed = CombatTestRoot.WarehouseSeedForTests;
            var input = new InputTestFixture();
            bool inputStarted = false;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                var pacing = new CombatEncounterSettings(3, 60f, 60f, 60f, 60f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, pacing)), Is.True);
                yield return AwaitSelectedCombatRange();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                SuspendRigidCombatOpponents();

                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                Player3DAssetRegistry rig = visual.Registry;
                PlayerClothPhysicsPolicy policy = root.GetComponent<PlayerClothPhysicsPolicy>();
                PlayerJacketCloth jacket = rig.GetComponent<PlayerJacketCloth>();
                PlayerScarfPresentation scarf = rig.GetComponent<PlayerScarfPresentation>();
                PlayerHair hair = rig.GetComponent<PlayerHair>();
                GameObject shotgun = root.Hero.CarriedWeapon(CombatWeaponId.Shotgun);
                CombatShotgunSling sling = shotgun.GetComponent<CombatShotgunSling>();
                Assert.That(policy, Is.Not.Null);
                Assert.That(jacket != null && scarf != null && hair != null && sling != null, Is.True);
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.Scarf), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true), Is.True);
                yield return null;
                SynchronizeRigidCombatCloth(visual, scarf, sling);

                var off = new CombatClothSnapshot(jacket, scarf, sling);
                Assert.That(off.JacketSteps + off.ScarfSteps + off.SlingSteps, Is.Zero,
                    "Combat must choose its policy before the first character or weapon physics pass.");
                Assert.That(off.JacketBaked + off.JacketFallbacks + off.JacketTransitions + off.SlingBaked, Is.Zero);
                Assert.That(off.NativeSnapshots + off.ProjectionAllocations, Is.Zero);
                AssertCombatClothOff(policy, jacket, scarf, sling, off);
                Assert.That(scarf.IsEquipped && scarf.IsVisible, Is.True,
                    "The idle proof must actually display the equipped scarf.");
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                CaptureRigidCombatCloth("Test combat rigid cloth idle", rear: false);

                // Keep the ordinary recorded-hair path active while proving that neither
                // cloth owner even asks the shared recording service for a sample.
                SecondaryMotionBakeRuntime.Enabled = true;
                long hairCalls = hair.TotalApplyCalls;
                long hairResults = CombatBakeRequests(SecondaryMotionBakePart.Hair);
                yield return AdvanceRigidCombatCloth(12, policy, jacket, scarf, sling, off);
                Assert.That(hair.TotalApplyCalls, Is.GreaterThan(hairCalls));
                Assert.That(CombatBakeRequests(SecondaryMotionBakePart.Hair), Is.GreaterThan(hairResults),
                    "Disabling clothes must leave the existing recorded/live hair selection running.");

                // The live-hair phase also makes a decoder regression unambiguous:
                // no consumer is allowed to load a recording while playback is disabled.
                SecondaryMotionBakeRuntime.Enabled = false;
                long decodeCount = SecondaryMotionBakeRuntime.Diagnostics.DecodeCount;
                long hairSteps = hair.TotalSimulationSteps;
                input.Setup();
                inputStarted = true;
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.Hero.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, -6f), Vector3.forward);
                root.Hero.SetCombatFocused(false);
                yield return null; // Equipment gets its real late pass after the setup teleport.
                Vector3 walkStart = root.Hero.transform.position;
                Vector3 scarfStart = scarf.TailRenderer.transform.position;
                input.Press(keyboard.wKey, queueEventOnly: true);
                yield return AdvanceRigidCombatCloth(24, policy, jacket, scarf, sling, off);
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                Assert.That(Vector3.Distance(root.Hero.transform.position, walkStart), Is.GreaterThan(.25f));
                Assert.That(Vector3.Distance(scarf.TailRenderer.transform.position, scarfStart), Is.GreaterThan(.2f),
                    "A rigid scarf follows the moving head rather than remaining at its old world pose.");
                Assert.That(hair.TotalSimulationSteps, Is.GreaterThan(hairSteps),
                    "The same real frames must still integrate live hair.");

                // This fixture drives the simulation clock explicitly; its root
                // therefore does not poll combat input in Update. Request the same
                // actor aim used by that input owner without advancing it twice.
                root.Hero.SetPistolAim(true, root.Hero.transform.position +
                    root.Hero.transform.forward * 20f + Vector3.up * 1.3f);
                yield return AdvanceRigidCombatCloth(36, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.Firearm.AimRequested && root.Hero.Firearm.IsAiming, Is.True);
                CaptureRigidCombatCloth("Test combat rigid cloth aim", rear: false);
                root.Hero.SetPistolAim(false);
                yield return AdvanceRigidCombatCloth(30, policy, jacket, scarf, sling, off);

                // This closed route visits all six ordered pairs among the three
                // original weapons, including both long-weapon transfers.
                GameObject pistol = root.Hero.CarriedWeapon(CombatWeaponId.Pistol);
                GameObject crowbar = root.Hero.CarriedWeapon(CombatWeaponId.Crowbar);
                bool capturedAccess = false;
                foreach (CombatWeaponId target in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Shotgun,
                    CombatWeaponId.Pistol, CombatWeaponId.Shotgun, CombatWeaponId.Crowbar, CombatWeaponId.Pistol })
                {
                    Assert.That(root.Hero.TrySelectWeapon(target), Is.True);
                    for (int frame = 0; frame < 360 && (root.Hero.IsSwitchingWeapon ||
                        !root.Hero.HasWeaponInHand || root.Hero.WeaponId != target); frame++)
                    {
                        root.Tick(1f / 30f);
                        yield return null;
                        SynchronizeRigidCombatCloth(visual, scarf, sling);
                        AssertCombatClothOff(policy, jacket, scarf, sling, off);
                        if (!capturedAccess && root.Hero.IsSwitchingWeapon &&
                            root.Hero.WeaponCarryActionWeapon == CombatWeaponId.Pistol &&
                            root.Hero.WeaponCarryActionIsDraw && root.Hero.WeaponCarryActionProgress > .48f)
                        {
                            AssertCombatJacketAccessMovesPanel(jacket);
                            CaptureRigidCombatCloth("Test combat pistol jacket access", rear: false);
                            capturedAccess = true;
                        }
                    }
                    Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
                    Assert.That(root.Hero.WeaponId, Is.EqualTo(target));
                    Assert.That(root.Hero.HasWeaponInHand, Is.True);
                    Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                    Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Crowbar), Is.SameAs(crowbar));
                    Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.SameAs(shotgun));
                }
                Assert.That(capturedAccess, Is.True);
                yield return AdvanceRigidCombatCloth(3, policy, jacket, scarf, sling, off);
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                CaptureRigidCombatCloth("Test combat rigid stowed sling", rear: true);

                Assert.That(root.Hero.TrySelectWeapon(CombatWeaponId.Shotgun), Is.True);
                yield return AdvanceRigidCombatCloth(12, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.IsSwitchingWeapon, Is.True);
                float heldCarryProgress = root.Hero.WeaponCarryActionProgress;
                Assert.That(heldCarryProgress, Is.InRange(.01f, .99f));
                Vector3 heldRoot = root.Hero.transform.position;
                Assert.That(root.PauseMenu.Open(), Is.True);
                Transform head = rig.Anchors.Head;
                // Opening the modal changes the motor's animation target. The
                // rigid scarf must follow whichever final head pose owns that
                // frame; its attachment, world root and committed action stay
                // fixed. Use rotation space so this offset remains in metres
                // despite the imported model root's unit conversion scale.
                Vector3 heldTailOffset = Quaternion.Inverse(head.rotation) *
                    (scarf.TailRenderer.transform.position - head.position);
                Quaternion heldTailRotation = Quaternion.Inverse(head.rotation) * scarf.TailRenderer.transform.rotation;
                root.Tick(1f);
                yield return null;
                SynchronizeRigidCombatCloth(visual, scarf, sling);
                AssertCombatClothOff(policy, jacket, scarf, sling, off);
                Vector3 pausedTailOffset = Quaternion.Inverse(head.rotation) *
                    (scarf.TailRenderer.transform.position - head.position);
                Quaternion pausedTailRotation = Quaternion.Inverse(head.rotation) * scarf.TailRenderer.transform.rotation;
                Assert.That(Vector3.Distance(heldTailOffset, pausedTailOffset), Is.LessThan(.002f));
                Assert.That(Quaternion.Angle(heldTailRotation, pausedTailRotation), Is.LessThan(.1f));
                Assert.That(root.Hero.transform.position, Is.EqualTo(heldRoot));
                Assert.That(root.Hero.WeaponCarryActionProgress, Is.EqualTo(heldCarryProgress));
                Assert.That(root.Hero.IsSwitchingWeapon, Is.True);
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Cloth pause did not release input.");

                yield return CompleteRigidClothWeapon(CombatWeaponId.Shotgun, policy, jacket, scarf, sling, off);
                yield return SpendRigidCombatRound(policy, jacket, scarf, sling, off);
                foreach (CombatWeaponId retained in CarryWeaponIds)
                {
                    if (root.Hero.WeaponId != retained)
                    {
                        Assert.That(root.Hero.TrySelectWeapon(retained), Is.True);
                        if (retained != CombatWeaponId.Shotgun)
                            yield return CompleteRigidClothWeapon(retained, policy, jacket, scarf, sling, off);
                        else
                        {
                            // Interrupt the real left-hand catch before the final
                            // right-hand transfer, rather than an already stable hold.
                            for (int frame = 0; frame < 120 && shotgun.transform.parent != rig.Anchors.LeftGrip; frame++)
                                yield return AdvanceRigidCombatCloth(1, policy, jacket, scarf, sling, off);
                            Assert.That(root.Hero.IsSwitchingWeapon && root.Hero.WeaponCarryActionIsDraw, Is.True);
                            Assert.That(root.Hero.WeaponCarryActionWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
                            Assert.That(shotgun.transform.parent, Is.SameAs(rig.Anchors.LeftGrip));
                        }
                    }
                    if (root.Hero.IsFirearm && !root.Hero.IsSwitchingWeapon)
                        yield return SpendRigidCombatRound(policy, jacket, scarf, sling, off);
                    var held = new HeroHeldWeaponSnapshot(root.Hero);
                    float catchProgress = root.Hero.WeaponCarryActionProgress;
                    root.Hero.SendMessage("DropWeapon", SendMessageOptions.RequireReceiver);
                    held.AssertMatches(root.Hero, retained + ": explicit release request");
                    ApplyControlledImpact(root.Hero, -root.Hero.transform.forward, ChargedDiagnosticImpulse);
                    bool sawPhysics = false, sawLeftCatchFall = false, sawCatchResume = false;
                    for (int frame = 0; frame < 720; frame++)
                    {
                        yield return AdvanceRigidCombatCloth(1, policy, jacket, scarf, sling, off);
                        sawPhysics |= root.Hero.IsKnockedDown && root.Hero.Ragdoll.PhysicsController.IsSimulating;
                        if (retained == CombatWeaponId.Shotgun)
                        {
                            sawLeftCatchFall |= root.Hero.IsKnockedDown && shotgun.transform.parent == rig.Anchors.LeftGrip;
                            if (sawPhysics && !root.Hero.IsKnockedDown && root.Hero.IsSwitchingWeapon)
                            {
                                sawCatchResume = true;
                                Assert.That(root.Hero.WeaponCarryActionProgress, Is.GreaterThanOrEqualTo(catchProgress - .01f),
                                    "Recovery resumes the retained catch instead of restarting the sling push.");
                            }
                        }
                        held.AssertMatches(root.Hero, retained + ": physical fall and recovery",
                            preserveParent: retained != CombatWeaponId.Shotgun || !sawCatchResume);
                        if (sawPhysics && !root.Hero.IsKnockedDown && !root.Hero.IsRagdollActive &&
                            !root.Hero.WeaponCarryRecoveryPending && !root.Hero.IsSwitchingWeapon &&
                            root.Hero.State.Phase == MeleePhase.Ready && !root.Hero.ImpactMotion.IsActive &&
                            !root.Hero.Footwork.RecoveryEpisodeActive) break;
                    }
                    Assert.That(sawPhysics, Is.True, retained + ": the real impact must produce a physical knockdown.");
                    Assert.That(root.Hero.IsKnockedDown || root.Hero.IsRagdollActive, Is.False,
                        retained + ": recovery finishes while retaining the held weapon. " + DescribeImpactRecoveryGate(root.Hero));
                    Assert.That(root.Hero.WeaponCarryRecoveryPending || root.Hero.IsSwitchingWeapon, Is.False);
                    if (retained == CombatWeaponId.Shotgun)
                    {
                        Assert.That(sawLeftCatchFall && sawCatchResume, Is.True,
                            "The interrupted left-hand catch survives physical recovery and completes its remaining draw.");
                        Assert.That(shotgun.transform.parent, Is.SameAs(rig.Anchors.RightGrip));
                    }
                }
                Assert.That(root.Hero.Weapon, Is.SameAs(shotgun));
                var defeatedWeapon = new HeroHeldWeaponSnapshot(root.Hero);
                float floorY = root.Hero.transform.position.y - PlayerFactory.GroundedRootOffset;
                root.Hero.State.ReceiveProjectileHit(root.Hero.State.Settings.MaxHealth);
                root.Hero.Step(CombatAssetProvider.DefeatHandoffSeconds + .05f);
                Assert.That(root.Hero.IsRagdollActive, Is.True);
                defeatedWeapon.AssertDroppedMatches(root.Hero, "Terminal fall handoff");
                bool bodyFallen = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    yield return new WaitForFixedUpdate();
                    SynchronizeRigidCombatCloth(visual, scarf, sling);
                    AssertCombatClothOff(policy, jacket, scarf, sling, off);
                    defeatedWeapon.AssertDroppedMatches(root.Hero, "Physically fallen hero");
                    if (frame < 34 || !root.Hero.Ragdoll.HasGroundContact ||
                        rig.Anchors.Head.position.y > floorY + .85f ||
                        root.Hero.Ragdoll.PelvisBody.position.y > floorY + .5f) continue;
                    bodyFallen = true;
                    break;
                }
                Assert.That(bodyFallen, Is.True, "The cloth capture must show a physically fallen body rather than its upright handoff.");
                Assert.That(scarf.TailRenderer.transform.IsChildOf(rig.transform), Is.True);
                CaptureRigidCombatCloth("Test combat rigid cloth fallen hero", rear: false);
                root.ResetRound();
                SuspendRigidCombatOpponents();
                yield return AdvanceRigidCombatCloth(8, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.IsRagdollActive, Is.False);
                Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.SameAs(shotgun));
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                Assert.That(SecondaryMotionBakeRuntime.Diagnostics.DecodeCount, Is.EqualTo(decodeCount));
                CaptureRigidCombatCloth("Test combat rigid cloth reset", rear: false);

                // The area policy can restore accessory simulation, while the
                // jacket stays rig-only throughout gameplay.
                policy.SetSimulationEnabled(true);
                Assert.That(jacket.ClothPhysicsEnabled, Is.False);
                Assert.That(scarf.PhysicsSimulationEnabled && sling.PhysicsSimulationEnabled, Is.True);
                yield return AdvanceRigidCombatCloth(8);
                Assert.That(jacket.TotalSimulationSteps, Is.EqualTo(off.JacketSteps));
                Assert.That(scarf.TotalSimulationSteps, Is.GreaterThan(off.ScarfSteps));
                Assert.That(sling.TotalSimulationSteps, Is.GreaterThan(off.SlingSteps));
                policy.SetSimulationEnabled(false);
                var restoredOff = new CombatClothSnapshot(jacket, scarf, sling);
                SecondaryMotionBakeRuntime.Enabled = true;
                yield return AdvanceRigidCombatCloth(8, policy, jacket, scarf, sling, restoredOff);
                Assert.That(jacket.MaximumSpeed, Is.Zero);
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (inputStarted) input.TearDown();
                SecondaryMotionBakeRuntime.Enabled = previousBakes;
                CombatTestRoot.WarehouseSeedForTests = previousSeed;
            }
        }

        private IEnumerator AdvanceRigidCombatCloth(int frames, PlayerClothPhysicsPolicy policy = null,
            PlayerJacketCloth jacket = null, PlayerScarfPresentation scarf = null,
            CombatShotgunSling sling = null, CombatClothSnapshot expected = default)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                root.Tick(1f / 60f);
                yield return null;
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                if (policy == null) { visual.ReapplyLatePresentationPose(); continue; }
                SynchronizeRigidCombatCloth(visual, scarf, sling);
                AssertCombatClothOff(policy, jacket, scarf, sling, expected);
            }
        }

        private void SuspendRigidCombatOpponents()
        {
            // The live warehouse starts with its mixed roster. This fixture
            // isolates the hero's equipment without changing population rules.
            root.SetSparring(false);
            root.AutomaticOpponentDecisions = false;
            root.SendMessage("SetRoomOpponentSuppression", true, SendMessageOptions.RequireReceiver);
        }

        private IEnumerator CompleteRigidClothWeapon(CombatWeaponId target, PlayerClothPhysicsPolicy policy,
            PlayerJacketCloth jacket, PlayerScarfPresentation scarf, CombatShotgunSling sling, CombatClothSnapshot expected)
        {
            for (int frame = 0; frame < 360 && (root.Hero.IsSwitchingWeapon ||
                !root.Hero.HasWeaponInHand || root.Hero.WeaponId != target); frame++)
                yield return AdvanceRigidCombatCloth(1, policy, jacket, scarf, sling, expected);
            Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
            Assert.That(root.Hero.HasWeaponInHand && root.Hero.WeaponId == target, Is.True);
        }

        private IEnumerator SpendRigidCombatRound(PlayerClothPhysicsPolicy policy, PlayerJacketCloth jacket,
            PlayerScarfPresentation scarf, CombatShotgunSling sling, CombatClothSnapshot expected)
        {
            // Spent guns distinguish retained ammunition from a fresh loadout.
            int beforeShot = root.Hero.Firearm.Rounds;
            root.Hero.SetPistolAim(true, root.Hero.transform.position + Vector3.up * 1.3f + Vector3.forward * 20f);
            root.Tick(.65f);
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.Firearm.Rounds, Is.EqualTo(beforeShot - 1));
            root.Tick(.7f);
            root.Hero.SetPistolAim(false);
            yield return AdvanceRigidCombatCloth(30, policy, jacket, scarf, sling, expected);
        }

        private readonly struct CombatClothSnapshot
        {
            internal readonly long JacketSteps, ScarfSteps, SlingSteps, JacketBaked, JacketFallbacks,
                JacketTransitions, SlingBaked, NativeSnapshots, ProjectionAllocations, JacketRequests, SlingRequests;
            internal CombatClothSnapshot(PlayerJacketCloth jacket, PlayerScarfPresentation scarf, CombatShotgunSling sling)
            {
                JacketSteps = jacket.TotalSimulationSteps; ScarfSteps = scarf.TotalSimulationSteps;
                SlingSteps = sling.TotalSimulationSteps; JacketBaked = jacket.TotalBakedPoseCalls;
                JacketFallbacks = jacket.TotalBakedFallbacks; JacketTransitions = jacket.TotalBakedTransitionCalls;
                SlingBaked = sling.BakedPlaybackFrames; NativeSnapshots = jacket.NativeSnapshotCaptureCount;
                ProjectionAllocations = sling.ProjectionWorkspaceAllocations;
                JacketRequests = CombatBakeRequests(SecondaryMotionBakePart.Jacket);
                SlingRequests = CombatBakeRequests(SecondaryMotionBakePart.Sling);
            }
        }

        private static long CombatBakeRequests(SecondaryMotionBakePart part)
        {
            long count = 0;
            foreach (SecondaryMotionBakeResult result in Enum.GetValues(typeof(SecondaryMotionBakeResult)))
                count += SecondaryMotionBakeRuntime.ResultCount(part, result);
            return count;
        }

        private static void SynchronizeRigidCombatCloth(Player3DCharacterPresentation visual,
            PlayerScarfPresentation scarf, CombatShotgunSling sling)
        {
            // The coroutine resumes before LateUpdate. Observe the same final
            // rig and rigid attachment transforms as a rendered late frame.
            // The off-only scarf branch changes transforms and publishes rest
            // vertices for hair contacts; it advances no presentation clock.
            Assert.That(scarf.PhysicsSimulationEnabled, Is.False);
            visual.ReapplyLatePresentationPose();
            scarf.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
            sling.Present();
        }

        private readonly struct HeroHeldWeaponSnapshot
        {
            private readonly CombatWeaponId id;
            private readonly GameObject model;
            private readonly Transform parent;
            private readonly IFirearmState pistol, shotgun;
            private readonly int pistolRounds, pistolReserve, shotgunRounds, shotgunReserve;

            internal HeroHeldWeaponSnapshot(CombatActor hero)
            {
                id = hero.WeaponId;
                model = hero.Weapon;
                parent = model.transform.parent;
                pistol = hero.CarriedFirearm(CombatWeaponId.Pistol);
                shotgun = hero.CarriedFirearm(CombatWeaponId.Shotgun);
                pistolRounds = pistol.Rounds;
                pistolReserve = pistol.ReserveRounds;
                shotgunRounds = shotgun.Rounds;
                shotgunReserve = shotgun.ReserveRounds;
            }

            internal void AssertMatches(CombatActor hero, string context, bool preserveParent = true)
            {
                Assert.That(hero.IsWeaponDropped, Is.False, context);
                Assert.That(hero.HasWeaponInHand, Is.True, context);
                Assert.That(hero.WeaponId, Is.EqualTo(id), context);
                Assert.That(hero.Weapon, Is.SameAs(model), context);
                Assert.That(hero.CarriedWeaponLocation(id), Is.EqualTo(CombatWeaponCarryLocation.Held), context);
                if (preserveParent && model.transform.parent != parent)
                    Assert.Fail(context + ": attachment changed from " + parent.name + " to " +
                        (model.transform.parent != null ? model.transform.parent.name : "world") +
                        "; knocked down=" + hero.IsKnockedDown + "; pending=" + hero.WeaponCarryRecoveryPending +
                        "; switching=" + hero.IsSwitchingWeapon + "; progress=" + hero.WeaponCarryActionProgress);
                Assert.That(model.transform.IsChildOf(hero.transform), Is.True, context);
                AssertAmmunitionMatches(hero, context);
            }

            internal void AssertDroppedMatches(CombatActor hero, string context)
            {
                Assert.That(hero.State.IsDefeated && hero.IsWeaponDropped, Is.True, context);
                Assert.That(hero.HasWeaponInHand, Is.False, context);
                Assert.That(hero.Weapon, Is.SameAs(model), context);
                Assert.That(hero.CarriedWeaponLocation(id), Is.EqualTo(CombatWeaponCarryLocation.Dropped), context);
                Assert.That(model.transform.parent, Is.Not.SameAs(parent), context);
                Assert.That(model.transform.IsChildOf(hero.transform), Is.False, context);
                Rigidbody body = model.GetComponent<Rigidbody>();
                Assert.That(body, Is.Not.Null, context);
                Assert.That(body.isKinematic, Is.False, context);
                Assert.That(body.useGravity, Is.True, context);
                AssertAmmunitionMatches(hero, context);
            }

            private void AssertAmmunitionMatches(CombatActor hero, string context)
            {
                Assert.That(hero.CarriedFirearm(CombatWeaponId.Pistol), Is.SameAs(pistol), context);
                Assert.That(hero.CarriedFirearm(CombatWeaponId.Shotgun), Is.SameAs(shotgun), context);
                Assert.That(pistol.Rounds, Is.EqualTo(pistolRounds), context);
                Assert.That(pistol.ReserveRounds, Is.EqualTo(pistolReserve), context);
                Assert.That(shotgun.Rounds, Is.EqualTo(shotgunRounds), context);
                Assert.That(shotgun.ReserveRounds, Is.EqualTo(shotgunReserve), context);
            }
        }

        private static void AssertCombatClothOff(PlayerClothPhysicsPolicy policy, PlayerJacketCloth jacket,
            PlayerScarfPresentation scarf, CombatShotgunSling sling, CombatClothSnapshot expected)
        {
            Assert.That(policy.SimulationEnabled || jacket.ClothPhysicsEnabled || scarf.PhysicsSimulationEnabled || sling.PhysicsSimulationEnabled, Is.False);
            Assert.That(jacket.TotalSimulationSteps, Is.EqualTo(expected.JacketSteps));
            Assert.That(scarf.TotalSimulationSteps, Is.EqualTo(expected.ScarfSteps));
            Assert.That(sling.TotalSimulationSteps, Is.EqualTo(expected.SlingSteps));
            Assert.That(jacket.LastSimulationSteps + scarf.LastSimulationSteps + sling.LastSimulationSteps, Is.Zero);
            Assert.That(jacket.LastContactCount + scarf.LastSurfaceContactPassCount + sling.SteadyProjectionIterations + sling.SteadyLengthRepairIterations, Is.Zero);
            Assert.That(jacket.TotalBakedPoseCalls, Is.EqualTo(expected.JacketBaked));
            Assert.That(jacket.TotalBakedFallbacks, Is.EqualTo(expected.JacketFallbacks));
            Assert.That(jacket.TotalBakedTransitionCalls, Is.EqualTo(expected.JacketTransitions));
            Assert.That(sling.BakedPlaybackFrames, Is.EqualTo(expected.SlingBaked));
            Assert.That(jacket.NativeSnapshotCaptureCount, Is.EqualTo(expected.NativeSnapshots));
            Assert.That(sling.ProjectionWorkspaceAllocations, Is.EqualTo(expected.ProjectionAllocations));
            Assert.That(CombatBakeRequests(SecondaryMotionBakePart.Jacket), Is.EqualTo(expected.JacketRequests));
            Assert.That(CombatBakeRequests(SecondaryMotionBakePart.Sling), Is.EqualTo(expected.SlingRequests));
            Assert.That(scarf.TailSimulation == null || !scarf.TailSimulation.IsActive, Is.True);
            Assert.That(scarf.IsEquipped, Is.True);
            CollectionAssert.AreEqual(scarf.TailRestVertices, scarf.TailRenderer.sharedMesh.vertices,
                "Disabled scarf vertices must remain the authored skin rather than frozen physical output.");
            Assert.That(sling.Ribbon.transform.IsChildOf(sling.transform), Is.True);
            Transform stock = CombatAssetProvider.FindAnchor(sling.gameObject, "SlingStock");
            Transform foreend = CombatAssetProvider.FindAnchor(sling.gameObject, "SlingForeend");
            Assert.That(Vector3.Distance(sling.WorldCenterlinePoint(0), stock.position), Is.LessThan(.002f));
            Assert.That(Vector3.Distance(sling.WorldCenterlinePoint(sling.CenterlinePointCount - 1), foreend.position), Is.LessThan(.002f));
        }

        private static void AssertCombatJacketUsesOriginalSkin(PlayerJacketCloth jacket, Player3DAssetRegistry rig)
        {
            SkinnedMeshRenderer[] skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int surface = 0; surface < jacket.SurfaceCount; surface++)
            {
                bool found = false;
                foreach (SkinnedMeshRenderer skin in skins)
                    if (skin.sharedMesh == jacket.SourceMesh(surface))
                    {
                        found = true;
                        Assert.That(skin.bones.Length, Is.GreaterThan(0));
                        foreach (Transform bone in skin.bones) Assert.That(bone.IsChildOf(rig.ModelRoot), Is.True);
                    }
                Assert.That(found, Is.True, "The jacket returns to its original skinned surface outside deterministic hand access.");
            }
        }

        private static void AssertCombatJacketAccessMovesPanel(PlayerJacketCloth jacket)
        {
            Vector3[] source = jacket.SourceMesh(0).vertices;
            Vector3[] opened = jacket.DeformedMesh(0).vertices;
            float maximum = 0f;
            for (int vertex = 0; vertex < source.Length; vertex++)
                maximum = Mathf.Max(maximum, Vector3.Distance(source[vertex], opened[vertex]));
            Assert.That(maximum, Is.GreaterThan(.00001f), "The pistol hand still opens its jacket panel without a cloth solve.");
        }

        private void CaptureRigidCombatCloth(string name, bool rear, Vector3? focusOverride = null)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                SynchronizeRigidCombatCloth(visual, visual.Registry.GetComponent<PlayerScarfPresentation>(),
                    root.Hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>());
                Vector3 focus = focusOverride ?? (root.Hero.IsRagdollActive ? root.Hero.BodyWorldPosition + Vector3.up * .4f :
                    root.Hero.transform.position + Vector3.up * 1.05f);
                Vector3 direction = root.Hero.transform.forward * (rear ? -1f : 1f);
                Vector3 offset = focusOverride.HasValue ? new Vector3(1.25f, .9f, 1.4f) :
                    direction * 2.7f + root.Hero.transform.right * .7f + Vector3.up * .25f;
                camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset));
                camera.fieldOfView = 43f;
                const string folder = "Test combat rigid cloth";
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, folder, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }
    }
}
