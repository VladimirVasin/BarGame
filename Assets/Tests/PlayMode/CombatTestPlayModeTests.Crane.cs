using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatCraneAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CombatCraneAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        private GameObject craneTestFooting;
        private Transform[,] preparedCraneCables;
        private Transform preparedCraneHook, preparedCraneHookCable;
        private int observedCraneThrows, craneMovieFrames;
        private Vector3 observedCraneAim, observedCraneOrigin;
        private float maximumCraneSwing, maximumCraneLimbEnergy, craneOriginalYaw;
        private bool recordCraneMotion;
        private AudioSource[] craneVoices;
        private Vector3 craneOriginalAnchor, craneMinimumCommand, craneMaximumCommand;

        [Category("CurrentCombat")]
        [UnityTest]
        public IEnumerator Range_CraneCaptureClearsGalleryRailAndKeepsReleaseMomentum()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Crane)), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The gallery capture did not reach gameplay.");
            root.AutomaticSimulation = false;
            root.AutomaticOpponentDecisions = false;
            root.SetSparring(true);
            ForceAmmoFocus();
            CombatActor crane = root.Opponent, hero = root.Hero;
            for (int step = 0; step < 800 && (crane.CraneFloorIndex != 1 ||
                crane.CranePatrolTravelling || crane.CranePhase != CombatCranePhase.Waiting); step++)
            {
                root.Tick(.05f);
                if (step % 40 == 39) yield return null;
            }
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(1));
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Vector3 floor = new Vector3(6.75f, WarehouseArenaPlan.LevelRise, crane.CraneCarriagePosition.z);
            PlaceCraneHero(floor, Vector3.ProjectOnPlane(crane.CraneHookCableExit - floor, Vector3.up).normalized);
            hero.SetCombatFocused(false);
            root.Tick(.7f);
            Assert.That(crane.TryBeginCraneCycle(hero), Is.True, CraneAcquisitionDiagnostic(crane));
            for (int step = 0; step < 600 && !hero.IsRestrained; step++)
            {
                root.Tick(CombatTestRoot.SimulationStep);
                if (step % 120 == 119) yield return null;
            }
            Assert.That(hero.IsRestrained, Is.True,
                "The actual hook must reach the hero standing behind the warehouse rail.");
            Player3DRagdollController physics = hero.Ragdoll.PhysicsController;
            bool crossed = false;
            float crossingPelvisHeight = 0f;
            float highestPelvis = hero.BodyWorldPosition.y;
            Vector3 releaseVelocity = Vector3.zero;
            for (int frame = 0; frame < 300 && hero.IsRestrained; frame++)
            {
                releaseVelocity = physics.IsSimulating ? physics.PelvisBody.linearVelocity : Vector3.zero;
                root.Tick(Time.fixedDeltaTime);
                highestPelvis = Mathf.Max(highestPelvis, hero.BodyWorldPosition.y);
                if (!crossed && hero.BodyWorldPosition.x < 5.45f)
                {
                    crossed = true;
                    crossingPelvisHeight = physics.PelvisBody.position.y;
                    yield return CaptureCraneVictim(crane, "Test crane - Hero pulled across the gallery rail",
                        new Vector3(-3.2f, 1.4f, -2f));
                }
                if (hero.IsRestrained) yield return new WaitForFixedUpdate();
            }
            Assert.That(hero.IsRestrained, Is.False, "Capture must release within its bounded hold.");
            Assert.That(crossed, Is.True,
                $"A real rail must be cleared into the well; body={hero.BodyWorldPosition}, highestPelvis={highestPelvis}, source={crane.CraneHookCableExit}.");
            Assert.That(crossingPelvisHeight, Is.GreaterThan(floor.y + 1.1f),
                "The central body crosses above the gallery rail, rather than falling beneath its bars.");
            Assert.That(hero.IsRagdollActive, Is.True);
            Assert.That(Vector3.ProjectOnPlane(releaseVelocity, Vector3.up).magnitude, Is.GreaterThan(.4f),
                "The final physical haul must leave meaningful horizontal momentum.");
            Assert.That(Vector3.Distance(physics.PelvisBody.linearVelocity, releaseVelocity), Is.LessThan(.05f),
                "Detaching the hook must preserve the existing physical velocity.");
            Vector3 releasePosition = physics.PelvisBody.position;
            Vector3 direction = Vector3.ProjectOnPlane(releaseVelocity, Vector3.up).normalized;
            for (int frame = 0; frame < 12; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Dot(physics.PelvisBody.position - releasePosition, direction), Is.GreaterThan(.08f),
                "The detached hero must continue travelling through the air.");
            yield return CaptureCraneVictim(crane, "Test crane - Free fall beyond the gallery rail",
                new Vector3(-3.2f, 1.4f, -2f));
            LogAssert.NoUnexpectedReceived();
        }

        [Category("CurrentCombat")]
        [UnityTest]
        public IEnumerator Range_CraneHookCaptureEscapeRetreatAndRegeneration()
        {
            var input = new InputTestFixture();
            var modal = new BarMinigameModalLock();
            Keyboard keyboard = null;
            Mouse mouse = null;
            bool background = Application.runInBackground;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                Application.runInBackground = true;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    CombatLoadoutSelection.Default, CombatTestMode.Crane)), Is.True);
                yield return WaitFor(() =>
                {
                    root = Object.FindAnyObjectByType<CombatTestRoot>();
                    return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                        GameInput.CanRead(GameInputContext.Gameplay);
                }, "The crane capture regression did not reach live gameplay.");
                root.AutomaticSimulation = false;
                root.SetSparring(true);
                root.AutomaticOpponentDecisions = false; // Keep genuine simulation/contact while driving each throw explicitly.
                ForceAmmoFocus();
                CombatActor crane = root.Opponent, hero = root.Hero;
                Assert.That(crane.IsCrane, Is.True);
                Assert.That(crane.CraneHookReach, Is.EqualTo(CombatActor.CraneHookMaximumLength));
                Vector3 home = crane.CraneHomePosition;
                int graphs = crane.NpcAnimationGraphBuilds;

                // A supported ledge in the open well isolates height from range and rails.
                PlaceCraneFootingHero(crane, outsideHeight: true);
                root.Tick(.7f);
                Assert.That(CraneChestHeightAboveHero(crane), Is.GreaterThan(CombatActor.CraneMaximumAttackHeightAboveTarget));
                Assert.That(Vector3.Distance(crane.CraneHookCableExit, hero.transform.position + Vector3.up * 1.2f),
                    Is.LessThan(crane.CraneHookReach),
                    "The height rejection fixture must remain inside hook reach: source=" + crane.CraneHookCableExit +
                    ", target=" + (hero.transform.position + Vector3.up * 1.2f) + ".");
                Assert.That(crane.TryBeginCraneCycle(hero), Is.False, "A body too far above the hero cannot attack within otherwise valid cable reach.");
                Assert.That(crane.CraneHookThrowCount, Is.Zero);
                PlaceCraneFootingHero(crane, chestAbove: 1.75f);
                yield return null;
                ForceAmmoFocus();
                root.Tick(.2f);
                Assert.That(CraneChestHeightAboveHero(crane), Is.InRange(0f,
                    CombatActor.CraneMaximumAttackHeightAboveTarget - .2f));
                Assert.That(crane.TryBeginCraneCycle(hero), Is.True, CraneAcquisitionDiagnostic(crane));
                PlaceCraneFootingHero(crane, outsideHeight: true);
                root.Tick(.7f);
                Assert.That(crane.CraneHookThrowCount, Is.Zero, "Losing eligibility during the tell cancels release without a throw.");
                Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
                RemoveCraneTestFooting();
                root.ResetRound();
                ForceAmmoFocus();

                // Holding after a parry expires cannot block this real downward hook.
                PlaceCraneCaptureHero(crane);
                hero.SetBlock(true);
                root.Tick(.7f);
                hero.State.Advance(MeleeCombatSettings.Crowbar.ParryWindowSeconds + .05f);
                Assert.That(crane.TryBeginCraneCycle(hero), Is.True);
                for (int step = 0; step < 600 && hero.ReceivedImpactCount == 0; step++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(hero.ReceivedImpactCount, Is.EqualTo(1), "The actual hook must reach the hero after the parry expires.");
                Assert.That(hero.LastImpact.Result, Is.EqualTo(MeleeHitResult.Hit));
                Assert.That(hero.LastImpact.Damage, Is.GreaterThan(0f));
                Assert.That(hero.State.Health, Is.EqualTo(hero.State.Settings.MaxHealth - hero.LastImpact.Damage).Within(.001f));
                Assert.That(hero.IsRestrained && crane.HasCraneVictim, Is.True);
                Assert.That(root.BloodEffects.WoundCountFor(hero), Is.GreaterThan(0));
                Assert.That(hero.State.Phase, Is.Not.EqualTo(MeleePhase.GuardBroken));
                yield return CaptureGameplayHud("Test crane - Expired parry admits overhead hook");

                root.ResetRound();
                ForceAmmoFocus();
                // A key held before contact cannot become escape effort at capture.
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                yield return BeginActualCraneCapture(crane, guardAway: true);
                Assert.That(CraneChestHeightAboveHero(crane), Is.LessThanOrEqualTo(CombatActor.CraneMaximumAttackHeightAboveTarget));
                Assert.That(hero.LastImpact.Source, Is.SameAs(crane));
                Assert.That(hero.LastImpact.Damage, Is.GreaterThan(0f));
                Assert.That(root.BloodEffects.WoundCountFor(hero), Is.GreaterThan(0), "A real hook punctures its contacted anatomical surface.");
                Assert.That(Vector3.Distance(crane.CraneHookPosition, hero.RestraintAnchor), Is.LessThan(.08f));
                Assert.That(root.RestraintHudVisible, Is.True);
                root.Tick(.25f);
                yield return null;
                root.Tick(.1f);
                Assert.That(hero.RestraintEscapeProgress, Is.Zero, "Holding E across contact is not repeated pressing.");
                float capturedClock = crane.CraneCaptureElapsed;
                Vector3 capturedPoint = crane.CraneHookPosition, capturedRoot = hero.transform.position;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(1f);
                yield return null;
                Assert.That(root.RestraintHudVisible, Is.False);
                Assert.That(crane.CraneCaptureElapsed, Is.EqualTo(capturedClock));
                Assert.That(crane.CraneHookPosition, Is.EqualTo(capturedPoint));
                Assert.That(hero.transform.position, Is.EqualTo(capturedRoot));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Capture pause did not release input.");
                ForceAmmoFocus();
                Assert.That(modal.TryCaptureAndDisable(root.Player.Interactor, root.CameraFollow, null), Is.True);
                Assert.That(root.RestraintHudVisible, Is.False);
                root.Tick(1f);
                Assert.That(crane.CraneCaptureElapsed, Is.EqualTo(capturedClock));
                Assert.That(modal.Restore(), Is.True);
                ForceAmmoFocus();
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                root.Tick(.02f);
                for (int press = 0; press < 6; press++)
                {
                    input.Press(keyboard.eKey, queueEventOnly: true);
                    yield return null;
                    root.Tick(.02f);
                    root.AdvanceHudPresentation(.02f);
                    if (press < 5)
                    {
                        Assert.That(hero.IsRestrained, Is.True);
                        Assert.That(hero.RestraintEscapeProgress, Is.EqualTo((press + 1f) / 6f).Within(.001f));
                        Assert.That(root.RestraintHudProgress, Is.EqualTo(hero.RestraintEscapeProgress));
                        root.Tick(.06f); // Several substeps from one press still count once.
                        Assert.That(hero.RestraintEscapeProgress, Is.EqualTo((press + 1f) / 6f).Within(.001f));
                    }
                    if (press == 2) yield return CaptureCraneVictim(crane, "Test crane - Hook wound and half filled E escape");
                    input.Release(keyboard.eKey, queueEventOnly: true);
                    yield return null;
                    root.Tick(.02f);
                }
                Assert.That(hero.IsRestrained || crane.HasCraneVictim || root.RestraintHudVisible, Is.False);
                AssertCraneStruggleStopped(hero.Ragdoll.PhysicsController);
                Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.ReturningHome));
                Assert.That(crane.CraneCaptureElapsed, Is.LessThan(CombatActor.CraneHookHoldSeconds));
                yield return AwaitCraneWatch(crane, home, regeneration: false);
                int throws = crane.CraneHookThrowCount;
                root.Tick(9.8f - crane.CraneWatchElapsed);
                Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Watching));
                Assert.That(crane.TryBeginCraneCycle(hero), Is.False);
                Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws));
                root.Tick(.22f);
                Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));

                root.ResetRound();
                ForceAmmoFocus();
                Player3DRagdollController heroPhysics = hero.Ragdoll.PhysicsController;
                CraneJointDriveSnapshot[] restingDrives = CaptureCraneJointDrives(heroPhysics);
                Transform heroChest = FindAnatomicalBone(hero, "chest"), heroPelvis = FindAnatomicalBone(hero, "pelvis");
                Transform leftArm = FindAnatomicalBone(hero, "forearm.L"), rightArm = FindAnatomicalBone(hero, "forearm.R");
                Transform leftShin = FindAnatomicalBone(hero, "shin.L"), rightShin = FindAnatomicalBone(hero, "shin.R");
                Quaternion leftArmStart = Quaternion.identity, rightArmStart = Quaternion.identity;
                Quaternion leftShinStart = Quaternion.identity, rightShinStart = Quaternion.identity;
                float activeArmMotion = 0f, activeLegMotion = 0f, earlyTargetTravel = 0f;
                bool earlyStruggleCaptured = false, lateStruggleCaptured = false;
                yield return BeginActualCraneCapture(crane);
                Vector3 originalHeroRoot = hero.transform.position;
                Assert.That(hero.RestraintEscapeProgress, Is.Zero, "The next capture starts with an empty meter.");
                for (int captureStep = 0; captureStep < 400 && hero.IsRestrained &&
                    crane.CraneCaptureElapsed < CombatActor.CraneHookHoldSeconds - .15f; captureStep++)
                {
                    root.Tick(Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                    if (hero.IsRestraintAirborne && !earlyStruggleCaptured && crane.CraneCaptureElapsed >= .8f)
                    {
                        Assert.That(heroPhysics.IsRestraintDriveActive, Is.True);
                        Assert.That(heroPhysics.RestraintDrivenJointCount, Is.GreaterThan(7));
                        Assert.That(heroPhysics.RestraintDrivenLegJointCount, Is.GreaterThanOrEqualTo(4),
                            "Suspension keeps active hip/knee effort instead of leaving the lower body limp.");
                        leftArmStart = Quaternion.Inverse(heroChest.rotation) * leftArm.rotation;
                        rightArmStart = Quaternion.Inverse(heroChest.rotation) * rightArm.rotation;
                        leftShinStart = Quaternion.Inverse(heroPelvis.rotation) * leftShin.rotation;
                        rightShinStart = Quaternion.Inverse(heroPelvis.rotation) * rightShin.rotation;
                        earlyTargetTravel = heroPhysics.RestraintDriveTargetTravelDegrees;
                        yield return ConsumeMouseLookInput(); // Finish the ownership/focus neutral frames before a real mouse delta.
                        AssertCraneLiveBodyCamera(heroPhysics);
                        Assert.That(root.CameraFollow.TargetLockActive, Is.False);
                        Quaternion cameraBeforeOrbit = root.CameraFollow.Camera.transform.rotation;
                        float pitchBeforeOrbit = root.CameraFollow.TargetOrbitPitch;
                        input.Set(mouse.delta, new Vector2(70f, 22f), queueEventOnly: true);
                        yield return ConsumeMouseLookInput();
                        Assert.That(root.CameraFollow.TargetOrbitPitch, Is.LessThan(pitchBeforeOrbit - .5f));
                        // Receipt changes the orbit target immediately; the ordinary
                        // follow deliberately eases its rendered rotation toward it.
                        for (int frame = 0; frame < 8 &&
                            Quaternion.Angle(cameraBeforeOrbit, root.CameraFollow.Camera.transform.rotation) <= 1f; frame++)
                            yield return null;
                        Assert.That(Quaternion.Angle(cameraBeforeOrbit, root.CameraFollow.Camera.transform.rotation), Is.GreaterThan(1f),
                            "Actual mouse input must still orbit the lifted physical hero.");
                        Assert.That(hero.IsRestrained, Is.True);
                        CaptureCraneView(crane, "Test crane - Live follow early struggle body", gameplayCamera: true);
                        yield return CaptureGameplayHud("Test crane - Live follow early struggle");
                        earlyStruggleCaptured = true;
                    }
                    if (earlyStruggleCaptured)
                    {
                        activeArmMotion = Mathf.Max(activeArmMotion,
                            Quaternion.Angle(leftArmStart, Quaternion.Inverse(heroChest.rotation) * leftArm.rotation),
                            Quaternion.Angle(rightArmStart, Quaternion.Inverse(heroChest.rotation) * rightArm.rotation));
                        activeLegMotion = Mathf.Max(activeLegMotion,
                            Quaternion.Angle(leftShinStart, Quaternion.Inverse(heroPelvis.rotation) * leftShin.rotation),
                            Quaternion.Angle(rightShinStart, Quaternion.Inverse(heroPelvis.rotation) * rightShin.rotation));
                    }
                    if (earlyStruggleCaptured && !lateStruggleCaptured && crane.CraneCaptureElapsed >= 2f)
                    {
                        Assert.That(heroPhysics.RestraintDriveTargetTravelDegrees, Is.GreaterThan(earlyTargetTravel + 20f),
                            "The retained muscles keep changing their intended joint poses while the hook pulls.");
                        Assert.That(activeArmMotion, Is.GreaterThan(3f), "The arms visibly struggle relative to the chest, beyond whole-body translation.");
                        Assert.That(activeLegMotion, Is.GreaterThan(3f), "The legs visibly kick relative to the pelvis, beyond whole-body translation.");
                        Assert.That(hero.BodyWorldPosition.y, Is.GreaterThan(originalHeroRoot.y + .75f));
                        yield return ConsumeMouseLookInput();
                        AssertCraneLiveBodyCamera(heroPhysics);
                        Assert.That(root.CameraFollow.CurrentFocusPoint.y, Is.GreaterThan(originalHeroRoot.y + .8f),
                            "The camera rises with the visible body instead of watching the abandoned capsule.");
                        CaptureCraneView(crane, "Test crane - Live follow lifted struggle body", gameplayCamera: true);
                        yield return CaptureGameplayHud("Test crane - Live follow lifted struggle");
                        lateStruggleCaptured = true;
                    }
                }
                Assert.That(earlyStruggleCaptured && lateStruggleCaptured, Is.True);
                Assert.That(hero.IsRestrained, Is.True, "The genuine contact remains embedded for its full capture duration.");
                Assert.That(hero.IsRestraintAirborne, Is.True, "A modest-height capture can pull the continuous rig away from its real stair support.");
                Assert.That(hero.IsRagdollActive, Is.True, "The lifted body uses its prepared partial physical rig.");
                Assert.That(root.BodyEffects.DetachedPartCountFor(hero), Is.Zero);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(hero), Is.Zero);
                foreach (BodyDamageRegion region in Enum.GetValues(typeof(BodyDamageRegion)))
                    Assert.That(hero.BodyDamage.IsAttached(region), Is.True, "Hero anatomy remains continuous: " + region);
                yield return CaptureCraneVictim(crane, "Test crane - Embedded hook airborne partial ragdoll");
                float healthBeforeFall = hero.State.Health;
                float finalCaptureTime = crane.CraneCaptureElapsed;
                while (hero.IsRestrained)
                {
                    root.Tick(Time.fixedDeltaTime);
                    finalCaptureTime += Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(finalCaptureTime, Is.InRange(CombatActor.CraneHookHoldSeconds - .025f,
                    CombatActor.CraneHookHoldSeconds + .04f));
                Assert.That(hero.IsRagdollActive, Is.True, "Release preserves the already airborne physical body.");
                AssertCraneStruggleStopped(heroPhysics);
                AssertCraneJointDrivesRestored(heroPhysics, restingDrives);
                for (int frame = 0; frame < 240 && hero.RestraintFallDamageCount == 0; frame++)
                {
                    root.Tick(Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(hero.RestraintFallDamageCount, Is.EqualTo(1));
                Assert.That(hero.LastRestraintFallDamage, Is.GreaterThan(0f));
                Assert.That(hero.State.Health, Is.LessThan(healthBeforeFall));
                for (int frame = 0; frame < 20; frame++)
                {
                    root.Tick(Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(hero.RestraintFallDamageCount, Is.EqualTo(1), "Several landing bones produce one damage transaction.");
                yield return CaptureCraneVictim(crane, "Test crane - Fall after release");
                RemoveCraneTestFooting();

                root.ResetRound();
                AssertCraneStruggleStopped(heroPhysics);
                AssertCraneJointDrivesRestored(heroPhysics, restingDrives);
                ForceAmmoFocus();
                yield return BeginActualCraneCapture(crane);
                crane.CaptureContactPose();
                Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 headPoint, out _, out _), Is.True);
                var headHit = new CombatHurtboxes.Hit(headPoint, Vector3.up, Vector3.down, 0f,
                    new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front), Player3DAnatomicalPart.Head, Vector3.zero);
                crane.ReceiveProjectile(hero, 1, headHit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
                crane.ReceiveProjectile(hero, 2, headHit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
                Assert.That(hero.IsRestrained || crane.HasCraneVictim, Is.False, "Fatal pain immediately frees the captive.");
                Assert.That(crane.IsCraneRegenerating && crane.IsCraneFrenzied, Is.True);
                Assert.That(crane.State.IsDefeated || root.RoundFinished, Is.False, "Fatal damage creates a retreat, never a kill or completed round.");
                Assert.That(crane.CraneFrenzyPlayCount, Is.EqualTo(1));
                Transform head = FindAnatomicalBone(crane, "head"), hand = FindAnatomicalBone(crane, "hand.L");
                Quaternion headStart = head.localRotation, handStart = hand.localRotation;
                float headMotion = 0f, handMotion = 0f;
                for (int frame = 0; frame < 28; frame++)
                {
                    root.Tick(.1f);
                    headMotion = Mathf.Max(headMotion, Quaternion.Angle(headStart, head.localRotation));
                    handMotion = Mathf.Max(handMotion, Quaternion.Angle(handStart, hand.localRotation));
                }
                Assert.That(crane.IsCraneFrenzied, Is.True, "Fatal convulsions retain their full three-second phase.");
                Assert.That(headMotion, Is.GreaterThan(5f));
                Assert.That(handMotion, Is.GreaterThan(5f));
                CaptureCraneFrenzyView(crane, "Test crane - Fatal scream and convulsions");
                float spasmClock = crane.CraneFrenzySecondsElapsed;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(1f);
                yield return null;
                Assert.That(crane.CraneFrenzySecondsElapsed, Is.EqualTo(spasmClock));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Fatal pain pause did not release input.");
                ForceAmmoFocus();
                root.Tick(.3f);
                Assert.That(crane.IsCraneFrenzied, Is.False);
                yield return AwaitCraneWatch(crane, home, regeneration: true);
                root.Tick(9.8f - crane.CraneWatchElapsed);
                Assert.That(crane.IsCraneRegenerating, Is.True);
                root.Tick(.22f);
                Assert.That(crane.State.Health, Is.EqualTo(crane.State.Settings.MaxHealth));
                Assert.That(crane.State.IsDefeated || crane.IsCraneRegenerating, Is.False);
                Assert.That(root.Opponent, Is.SameAs(crane));
                Assert.That(root.Opponents.Count, Is.EqualTo(1), "Regeneration retains the existing population slot.");
                Assert.That(crane.NpcAnimationGraphBuilds, Is.EqualTo(graphs));

                root.ResetRound();
                yield return BeginActualCraneCapture(crane);
                crane.enabled = false;
                Assert.That(hero.IsRestrained || root.RestraintHudVisible, Is.False, "Disabling the source releases capture and its interface.");
                AssertCraneStruggleStopped(heroPhysics);
                crane.enabled = true;
                root.ResetRound();
                Assert.That(hero.RestraintEscapeProgress, Is.Zero);
                Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "The captured encounter did not unload.");
                Assert.That(crane == null, Is.True);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RemoveCraneTestFooting();
                modal.Restore();
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                Application.runInBackground = background;
            }
        }

        private void AssertCraneLiveBodyCamera(Player3DRagdollController physics)
        {
            PlayerCameraFollow follow = root.CameraFollow;
            Assert.That(follow.enabled, Is.True, "This proof uses the ordinary live gameplay camera.");
            Assert.That(follow.CombatBodyFocusActive, Is.True);
            Vector3 physicalFocus = (physics.PelvisBody.position + physics.ChestBody.position) * .5f +
                Vector3.up * PlayerCameraFollow.FocusOverrideHeight;
            Assert.That(Vector3.Distance(follow.CombatBodyFocusPoint, physicalFocus), Is.LessThan(.1f));
            Assert.That(Vector3.Distance(follow.CurrentFocusPoint, physicalFocus), Is.LessThan(.6f),
                "Ordinary damping may trail the moving body, but it must stay inside its bounded focus lag.");
            Vector3 visible = follow.Camera.WorldToViewportPoint(physicalFocus);
            Assert.That(visible.z, Is.GreaterThan(0f));
            Assert.That(visible.x, Is.InRange(.02f, .98f));
            Assert.That(visible.y, Is.InRange(.02f, .98f), "The lifted hero must remain in the gameplay view.");
        }

        private static void AssertCraneStruggleStopped(Player3DRagdollController physics)
        {
            Assert.That(physics.IsRestraintDriveActive, Is.False);
            Assert.That(physics.RestraintDrivenJointCount, Is.Zero);
            Assert.That(physics.RestraintDrivenLegJointCount, Is.Zero);
        }

        private readonly struct CraneJointDriveSnapshot
        {
            internal readonly string Name, ConnectedName;
            internal readonly RotationDriveMode Mode;
            internal readonly JointDrive Slerp, AngularX, AngularYZ;
            internal readonly Quaternion Target;
            internal readonly Vector3 AngularVelocity;

            internal CraneJointDriveSnapshot(ConfigurableJoint joint)
            {
                Name = joint.name;
                ConnectedName = joint.connectedBody != null ? joint.connectedBody.name : "";
                Mode = joint.rotationDriveMode;
                Slerp = joint.slerpDrive;
                AngularX = joint.angularXDrive;
                AngularYZ = joint.angularYZDrive;
                Target = joint.targetRotation;
                AngularVelocity = joint.targetAngularVelocity;
            }
        }

        private static CraneJointDriveSnapshot[] CaptureCraneJointDrives(Player3DRagdollController physics)
        {
            ConfigurableJoint[] joints = physics.GetComponentsInChildren<ConfigurableJoint>(true);
            Assert.That(joints, Is.Not.Empty);
            return Array.ConvertAll(joints, joint => new CraneJointDriveSnapshot(joint));
        }

        private static void AssertCraneJointDrivesRestored(Player3DRagdollController physics,
            CraneJointDriveSnapshot[] expected)
        {
            ConfigurableJoint[] joints = physics.GetComponentsInChildren<ConfigurableJoint>(true);
            foreach (CraneJointDriveSnapshot saved in expected)
            {
                // The physical handoff replaces elbow joints while retaining the same
                // anatomical link; compare its state rather than the retired instance.
                ConfigurableJoint joint = Array.Find(joints, candidate => candidate != null && candidate.name == saved.Name &&
                    (candidate.connectedBody != null ? candidate.connectedBody.name : "") == saved.ConnectedName);
                Assert.That(joint, Is.Not.Null, "The anatomical joint survives restraint: " + saved.Name);
                Assert.That(joint.rotationDriveMode, Is.EqualTo(saved.Mode), saved.Name);
                AssertCraneJointDrive(joint.slerpDrive, saved.Slerp, saved.Name);
                AssertCraneJointDrive(joint.angularXDrive, saved.AngularX, saved.Name);
                AssertCraneJointDrive(joint.angularYZDrive, saved.AngularYZ, saved.Name);
                Assert.That(Quaternion.Angle(joint.targetRotation, saved.Target), Is.LessThan(.01f), saved.Name);
                Assert.That(Vector3.Distance(joint.targetAngularVelocity, saved.AngularVelocity), Is.LessThan(.001f), saved.Name);
            }
        }

        private static void AssertCraneJointDrive(JointDrive actual, JointDrive expected, string name)
        {
            Assert.That(actual.positionSpring, Is.EqualTo(expected.positionSpring), name);
            Assert.That(actual.positionDamper, Is.EqualTo(expected.positionDamper), name);
            Assert.That(actual.maximumForce, Is.EqualTo(expected.maximumForce), name);
        }

        private void PlaceCraneCaptureHero(CombatActor crane)
        {
            PlaceCraneFootingHero(crane);
        }

        private float CraneChestHeightAboveHero(CombatActor crane) =>
            FindAnatomicalBone(crane, "chest").position.y - FindAnatomicalBone(root.Hero, "chest").position.y;

        private string CraneAcquisitionDiagnostic(CombatActor crane)
        {
            Vector3 target = root.Hero.transform.position + Vector3.up * 1.2f;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            return $"phase={crane.CranePhase}, clock={crane.CranePhaseElapsed}, state={crane.State.Phase}, " +
                $"cooldown={typeof(CombatActor).GetField("craneCooldown", flags).GetValue(crane)}, " +
                $"perception={typeof(CombatActor).GetField("cranePerceptionWait", flags).GetValue(crane)}, " +
                $"source={crane.CraneHookCableExit}, target={target}, chestAbove={CraneChestHeightAboveHero(crane)}, " +
                $"range={Vector3.Distance(crane.CraneHookCableExit, target)}, input={GameInput.CanRead(GameInputContext.Gameplay)}";
        }

        private void PlaceCraneFootingHero(CombatActor crane, bool outsideHeight = false, float chestAbove = 1f)
        {
            // This collider is test support only, like the partition fixture below.
            // Its real contact lets the unmodified rig leave the ledge and fall.
            if (craneTestFooting == null)
            {
                craneTestFooting = new GameObject("Test crane raised footing");
                craneTestFooting.transform.SetParent(root.transform, false);
                craneTestFooting.AddComponent<BoxCollider>().size = new Vector3(1.1f, .3f, 1.1f);
            }
            float chestOffset = FindAnatomicalBone(root.Hero, "chest").position.y - root.Hero.transform.position.y;
            float above = outsideHeight ? CombatActor.CraneMaximumAttackHeightAboveTarget + .5f : chestAbove;
            Vector3 position = crane.transform.position + Vector3.forward * 3f;
            position.y = FindAnatomicalBone(crane, "chest").position.y - chestOffset - above;
            craneTestFooting.transform.position = position - Vector3.up * (.15f + PlayerFactory.GroundedRootOffset);
            root.Hero.ResetActor(position, Vector3.ProjectOnPlane(crane.CraneHookCableExit - position, Vector3.up).normalized);
            root.Hero.SetCombatFocused(false);
            Physics.SyncTransforms();
            root.CameraFollow.Snap();
        }

        private void RemoveCraneTestFooting()
        {
            if (craneTestFooting == null) return;
            craneTestFooting.SetActive(false);
            Object.Destroy(craneTestFooting);
            craneTestFooting = null;
        }

        private IEnumerator BeginActualCraneCapture(CombatActor crane, bool guardAway = false)
        {
            PlaceCraneCaptureHero(crane);
            if (guardAway)
            {
                root.Hero.transform.rotation *= Quaternion.Euler(0f, 180f, 0f);
                root.Hero.SetBlock(true);
            }
            root.Tick(.7f);
            Assert.That(crane.TryBeginCraneCycle(root.Hero), Is.True,
                "The achieved stop must reach the nearby hero within the shared chest-height and six-metre limits. " + CraneAcquisitionDiagnostic(crane));
            for (int step = 0; step < 600 && !root.Hero.IsRestrained; step++)
            {
                root.Tick(CombatTestRoot.SimulationStep);
                if (step % 120 == 119) yield return null;
            }
            Assert.That(root.Hero.IsRestrained && crane.HasCraneVictim, Is.True,
                "Actual hook flight must produce a capture; phase=" + crane.CranePhase + ", hook=" + crane.CraneHookPhase +
                ", impacts=" + root.Hero.ReceivedImpactCount + ", result=" + root.Hero.LastImpact.Result + ".");
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Embedded));
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Capturing));
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            if (guardAway) Assert.That(root.Hero.LastImpact.Result, Is.EqualTo(MeleeHitResult.Hit),
                "Holding a crowbar while facing away cannot block the hook entering the exposed body.");
        }

        private IEnumerator AwaitCraneWatch(CombatActor crane, Vector3 home, bool regeneration)
        {
            CombatCranePhase expected = regeneration ? CombatCranePhase.Regenerating : CombatCranePhase.Watching;
            for (int step = 0; step < 800 && crane.CranePhase != expected; step++)
            {
                Assert.That(crane.CraneWatchElapsed, Is.Zero, "Watching cannot start while the return carriage is still travelling.");
                root.Tick(.05f);
                if (step % 80 == 79) yield return null;
            }
            Assert.That(crane.CranePhase, Is.EqualTo(expected));
            Assert.That(Vector3.Distance(crane.CraneCarriagePosition, home), Is.LessThan(.025f));
            Assert.That(crane.CraneWatchElapsed, Is.LessThanOrEqualTo(.05f));
        }

        private IEnumerator CaptureCraneVictim(CombatActor crane, string name, Vector3? cameraOffset = null)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            bool following = root.CameraFollow.enabled;
            try
            {
                root.CameraFollow.enabled = false;
                Vector3 focus = root.Hero.BodyWorldPosition + Vector3.up * .8f;
                Vector3 eye = focus + (cameraOffset ?? new Vector3(3.2f, .7f, 3.2f));
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 56f;
                root.AdvanceHudPresentation(.02f);
                CaptureCraneView(crane, name + " body", gameplayCamera: true);
                yield return CaptureGameplayHud(name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }

        private void CaptureCraneFrenzyView(CombatActor crane, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 focus = crane.transform.position + Vector3.up * 1.1f;
                camera.transform.SetPositionAndRotation(focus + new Vector3(3.2f, 1.1f, 3.5f),
                    Quaternion.LookRotation(-new Vector3(3.2f, 1.1f, 3.5f)));
                camera.fieldOfView = 55f;
                CaptureCraneView(crane, name, gameplayCamera: true);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
        }

        [Category("CurrentCombat")]
        [UnityTest]
        [PrebuildSetup(typeof(CombatCraneAssetsSetup))]
        public IEnumerator Range_CraneSuspensionKeepsContactOcclusionPauseAndReset()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Crane)), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The ceiling encounter did not release its entry transition.");
            root.AutomaticSimulation = false;
            CombatActor crane = root.Opponent;
            Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Crane));
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.CameraFollow.MinimumOrbitPitch, Is.EqualTo(-70f),
                "The ordinary mouse orbit must allow the hero to look up at this encounter.");
            Assert.That(root.Opponents.Count, Is.EqualTo(1), "The prototype prepares one suspended actor.");
            Assert.That(crane.IsCrane, Is.True);
            Assert.That(crane.Weapon, Is.Null);
            Assert.That(crane.GetComponent<CombatZombieLoad>(), Is.Null,
                "The suspended worker must not retain a hidden porter crate.");
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.CraneCableCount, Is.EqualTo(6));
            AssertCraneAudioBank(crane);
            preparedCraneCables = new Transform[crane.CraneCableCount, CombatActor.CraneCableSegments];
            for (int i = 0; i < crane.CraneCableCount; i++)
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                    preparedCraneCables[i, segment] = crane.GetCraneCableTransform(i, segment);
            preparedCraneHook = crane.GetCraneHookTransform();
            preparedCraneHookCable = crane.GetCraneHookCableTransform();
            observedCraneThrows = crane.CraneHookThrowCount;
            maximumCraneSwing = maximumCraneLimbEnergy = 0f;
            craneMovieFrames = 0;
            recordCraneMotion = Array.IndexOf(Environment.GetCommandLineArgs(), "-craneMotionCapture") >= 0;
            int animationGraphs = crane.NpcAnimationGraphBuilds;
            craneOriginalAnchor = crane.CraneAnchorPosition;
            craneOriginalYaw = crane.transform.eulerAngles.y;
            craneMinimumCommand = craneMaximumCommand = crane.CraneCarriagePosition;
            var cableStarts = new Vector3[crane.CraneCableCount];
            for (int i = 0; i < cableStarts.Length; i++) cableStarts[i] = crane.GetCraneCableStart(i);
            AssertCraneCablesFollowRig(crane, cableStarts);
            AssertCraneHookGeometry(crane);
            AssertCraneAppearance(crane, intact: true);
            Transform leftElbow = FindAnatomicalBone(crane, "forearm.L"), rightElbow = FindAnatomicalBone(crane, "forearm.R");
            Assert.That(Vector3.Distance(leftElbow.position, rightElbow.position), Is.GreaterThan(1.6f),
                "The folded insect silhouette must be visibly broader than the ordinary worker.");
            Assert.That(Vector3.Distance(leftElbow.position, FindAnatomicalBone(crane, "hand.L").position), Is.GreaterThan(.9f),
                "The forearm must be deliberately elongated, beyond uniform worker enlargement.");
            Assert.That(Vector3.Distance(FindAnatomicalBone(crane, "shin.L").position,
                FindAnatomicalBone(crane, "foot.L").position), Is.GreaterThan(.8f),
                "The reverse-folded shin retains its stretched insect proportion.");
            CraneAnatomicalFrame headFrame = CraneAnatomicalFrame.Create(crane, "GEO_Head", "head");
            CraneAnatomicalFrame torsoFrame = CraneAnatomicalFrame.Create(crane, "GEO_Torso", "chest");
            float previousHeadYaw = CraneAnatomicalHeadYaw(headFrame, torsoFrame), headYaw = 0f;
            float minimumHeadYaw = 0f, maximumHeadYaw = 0f, fastestHeadTurn = 0f;
            for (int frame = 0; frame < 105; frame++)
            {
                TickCrane(crane, 1f / 60f);
                float yaw = CraneAnatomicalHeadYaw(headFrame, torsoFrame), turn = Mathf.DeltaAngle(previousHeadYaw, yaw);
                headYaw += turn;
                minimumHeadYaw = Mathf.Min(minimumHeadYaw, headYaw);
                maximumHeadYaw = Mathf.Max(maximumHeadYaw, headYaw);
                fastestHeadTurn = Mathf.Max(fastestHeadTurn, Mathf.Abs(turn));
                previousHeadYaw = yaw;
            }
            CaptureCraneView(crane, "crane-01-suspended");
            Assert.That(maximumHeadYaw - minimumHeadYaw, Is.GreaterThan(150f),
                "The face must look left and right around the anatomical torso-up axis; long-axis bone roll cannot substitute for yaw.");
            Assert.That(fastestHeadTurn, Is.GreaterThan(12f), "Mantis face yaw needs short snaps between held stares.");
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(2));
            PlaceCraneHero(crane.CraneGroundPosition + Vector3.forward * 1.2f, Vector3.back);
            root.CameraFollow.RotatePitch(-60f - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
            yield return null;
            CaptureCraneView(crane, "crane-04-ceiling-shoulder", gameplayCamera: true);
            root.CameraFollow.RotatePitch(-root.CameraFollow.TargetOrbitPitch);

            // A distant hero cannot choose or hold the worker's independent route.
            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return VerifyCraneBlockedGoal(crane, cableStarts);
            if (recordCraneMotion)
            {
                // Stand inside the void so gallery slabs cannot hide the lift.
                // This point stays beyond six metres from the first middle stop.
                PlaceCraneHero(new Vector3(0f, 0f, -5f), Vector3.forward);
                yield return CaptureCraneMotion(crane, cableStarts, 192, transport: true);
            }
            yield return AdvanceCraneFloor(crane, 1, cableStarts);
            CaptureCraneView(crane, "crane-patrol-middle");
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(maximumCraneSwing, Is.GreaterThan(.02f), "The transported body must lag and swing below the carriage.");
            Assert.That(maximumCraneLimbEnergy, Is.GreaterThan(.01f), "The four suspended limbs need their own inertial response.");
            Vector3 rootPose = crane.transform.position;
            var limbPose = new Vector3[crane.CraneCableCount];
            for (int i = 0; i < limbPose.Length; i++) limbPose[i] = crane.GetCraneCableEnd(i);
            for (int i = 0; i < 3; i++) crane.Present();
            Assert.That(Vector3.Distance(rootPose, crane.transform.position), Is.LessThan(.001f));
            for (int i = 0; i < limbPose.Length; i++)
                Assert.That(Vector3.Distance(limbPose[i], crane.GetCraneCableEnd(i)), Is.LessThan(.001f),
                    "Repeated presentation cannot integrate suspended dynamics again.");

            // A real downward shot crosses the open well, not a solid floor.
            Vector3 downwardHero = new Vector3(4.5f, 0f, 3f);
            yield return VerifyCraneHookHit(crane, downwardHero, cableStarts, "crane-hook-downward", captureMotion: true);
            Assert.That(observedCraneAim.y, Is.LessThan(-.2f));
            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 0, cableStarts);
            CaptureCraneView(crane, "crane-patrol-ground");

            Vector3 ringCenter = new Vector3(crane.CraneCarriagePosition.x, 0f, crane.CraneCarriagePosition.z);
            // Leave real cable payout between the enlarged folded arm and the
            // target so pause/inserted-wall checks precede an actual body hit.
            yield return VerifyCraneHookOcclusionAndPause(crane, ringCenter + Vector3.right * 4f, cableStarts);
            PlaceCraneHero(ringCenter + Vector3.right * 2.2f, Vector3.left);
            yield return AdvanceCraneTo(crane, CombatCranePhase.Striking, cableStarts);
            int lockedThrow = crane.CraneHookThrowCount;
            Vector3 lockedOrigin = crane.CraneHookFlightOrigin;
            Vector3 lockedHorizontal = Vector3.ProjectOnPlane(crane.CraneHookAimDirection, Vector3.up).normalized;
            PlaceCraneHero(ringCenter + Vector3.forward * 2.2f, Vector3.back);
            for (int step = 0; crane.CraneHookPhase == CombatCraneHookPhase.Outbound && step < 240; step++)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                Vector3 horizontalFlight = Vector3.ProjectOnPlane(crane.CraneHookEyePosition - lockedOrigin, Vector3.up);
                Assert.That(Vector3.Cross(horizontalFlight, lockedHorizontal).magnitude, Is.LessThan(.08f),
                    "Moving the target after release cannot steer the physical hook toward its new position.");
            }
            yield return AdvanceCraneTo(crane, CombatCranePhase.Waiting, cableStarts);
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "A hero who leaves the locked flight path must evade the hook.");
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(lockedThrow));
            PlaceCraneHero(ringCenter + Vector3.forward * (crane.CraneHookReach + 1f), Vector3.back);
            TickCrane(crane, .8f);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(lockedThrow), "A target beyond the real three-dimensional hook reach cannot start a new throw.");
            for (int angle = 0; angle < 8; angle++)
            {
                float radians = angle * Mathf.PI * .25f;
                Vector3 heroPosition = ringCenter + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * 2.2f;
                yield return VerifyCraneHookHit(crane, heroPosition, cableStarts,
                    angle == 0 ? "crane-hook-ground" : null);
                Assert.That(crane.CraneFloorIndex, Is.EqualTo(2), "Each successful capture completes its return to the original ceiling stop.");
            }

            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 1, cableStarts);
            WarehouseArenaStair upperStair = default;
            foreach (WarehouseArenaStair stair in root.WarehousePlan.Stairs)
                if (stair.Foot.x < 0f && stair.Foot.y > 0f) upperStair = stair;
            Assert.That(upperStair.Foot.y, Is.GreaterThan(0f));
            // Use a nearer real tread: the longer authored wrist offset and
            // inset patrol must still keep this upward shot within six metres.
            yield return VerifyCraneHookHit(crane, new Vector3(-6.95f, upperStair.HeightAt(-.6f), -.6f),
                cableStarts, "crane-hook-upward");
            Assert.That(observedCraneAim.y, Is.GreaterThan(.05f), "The actual throw must aim upward at the supported stair tread.");
            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 2, cableStarts);
            yield return VerifyCraneHookHit(crane, new Vector3(4.5f, WarehouseArenaPlan.TopHeight, -6.45f),
                cableStarts, "Test crane - Same storey gallery hook");
            Assert.That(observedCraneAim.y, Is.InRange(-.35f, .35f));
            Assert.That(root.Opponent, Is.SameAs(crane), "All storeys use the same living actor without resetting its round.");
            Assert.That(crane.NpcAnimationGraphBuilds, Is.EqualTo(animationGraphs));

            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 0, cableStarts);
            Vector3 coverage = craneMaximumCommand - craneMinimumCommand;
            Assert.That(coverage.x, Is.GreaterThan(7f));
            Assert.That(coverage.z, Is.GreaterThan(7f));
            Assert.That(coverage.y, Is.GreaterThan(6f));
            CaptureCraneView(crane, "crane-return-ground");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-craneAppearanceCapture") >= 0)
                yield return CaptureCraneAppearance(crane);
            crane.CaptureContactPose();
            Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
            var hit = new CombatHurtboxes.Hit(head, Vector3.up, Vector3.down, 0f,
                new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front),
                Player3DAnatomicalPart.Head, Vector3.zero);
            crane.ReceiveProjectile(root.Hero, 1, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(crane.State.Health, Is.EqualTo(crane.State.Settings.MaxHealth * .5f));
            Assert.That(crane.State.IsDefeated, Is.False);
            Assert.That(crane.CraneAttached, Is.True, "The first head wound retains the continuous suspended rig.");
            AssertCraneAppearance(crane, intact: false);
            crane.ReceiveProjectile(root.Hero, 2, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(crane.State.IsDefeated, Is.False, "Fatal damage starts regeneration instead of killing the suspended worker.");
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(crane.IsCraneRegenerating && crane.IsCraneFrenzied, Is.True);
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.IsRagdollActive, Is.False);
            foreach (Transform cable in preparedCraneCables) Assert.That(cable.gameObject.activeSelf, Is.True);
            Assert.That(preparedCraneHookCable.gameObject.activeSelf, Is.True);
            yield return null;
            CaptureCraneView(crane, "Test crane - Suspended regeneration begins");
            root.ResetRound();
            observedCraneThrows = crane.CraneHookThrowCount;
            Assert.That(root.Opponent, Is.SameAs(crane));
            Assert.That(crane.State.IsDefeated || crane.IsRagdollActive, Is.False);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(2));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            AssertCraneCablesFollowRig(crane, cableStarts);
            AssertCraneHookGeometry(crane);
            AssertCraneAppearance(crane, intact: true);
            Assert.That(crane.NpcAnimationGraphBuilds, Is.EqualTo(animationGraphs));
            CaptureCraneView(crane, "crane-09-reset");
            if (recordCraneMotion)
            {
                string manifest = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, "crane-motion", "capture.json");
                File.WriteAllText(manifest, "{\"fps\":24,\"frames\":" + craneMovieFrames + "}");
                TestContext.Out.WriteLine("Crane gameplay capture: " + craneMovieFrames + " frames at 24 fps.");
            }

            Assert.That(root.ReturnToMenu(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "The suspension scene did not unload.");
            Assert.That(crane == null, Is.True, "The suspended actor is owned by its unloaded scene.");
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyCraneHookHit(CombatActor crane, Vector3 heroPosition, Vector3[] starts,
            string captureName, bool captureMotion = false)
        {
            PlaceCraneHero(heroPosition, Vector3.ProjectOnPlane(crane.transform.position - heroPosition, Vector3.up).normalized);
            int throws = crane.CraneHookThrowCount;
            Assert.That(Vector3.Distance(crane.CraneHookCableExit, root.Hero.transform.position + Vector3.up * 1.2f),
                Is.LessThan(crane.CraneHookReach), "The supported test target must be in actual three-dimensional cable reach.");
            yield return AdvanceCraneTo(crane, CombatCranePhase.Striking, starts);
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws + 1));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Outbound).Or.EqualTo(CombatCraneHookPhase.Holding));
            if (crane.CraneHookPhase == CombatCraneHookPhase.Holding)
                Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                    "A close target may stop the released hook in its first contact substep, through one real hit. " +
                    $"Target={heroPosition}, blocked anatomy={crane.CraneBlockedAnatomy}, cuff={crane.CraneHookCableExit}.");
            if (captureMotion && recordCraneMotion)
                yield return CaptureCraneMotion(crane, starts, 96, transport: false);
            else
            {
                int steps = 0;
                while (crane.CraneHookPhase == CombatCraneHookPhase.Outbound && crane.CraneHookCableLength < 1f && steps++ < 120)
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                if (captureName != null)
                {
                    AimCraneGameplayCamera(crane);
                    yield return null;
                    CaptureCraneView(crane, captureName + "-shoulder", gameplayCamera: true);
                    CaptureCraneView(crane, captureName);
                }
            }
            yield return AdvanceCraneTo(crane, CombatCranePhase.Waiting, starts);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                "The thrown hook must produce one real contact and its return must remain harmless: " + (captureName ?? "horizontal circle") + ".");
            Assert.That(root.Hero.State.Health, Is.LessThan(root.Hero.State.Settings.MaxHealth));
            Assert.That(float.IsFinite(root.Hero.LastImpact.Point.sqrMagnitude), Is.True);
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws + 1));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            AssertCraneHookGeometry(crane);
        }

        private IEnumerator VerifyCraneHookOcclusionAndPause(CombatActor crane, Vector3 heroPosition, Vector3[] starts)
        {
            PlaceCraneHero(heroPosition, Vector3.ProjectOnPlane(crane.transform.position - heroPosition, Vector3.up).normalized);
            var wall = new GameObject("Test suspended worker partition");
            wall.transform.SetParent(root.transform, false);
            BoxCollider barrier = wall.AddComponent<BoxCollider>();
            barrier.size = new Vector3(4f, 3f, .16f);
            Vector3 target = root.Hero.transform.position + Vector3.up * 1.2f;
            Vector3 source = crane.CraneHookCableExit;
            wall.transform.SetPositionAndRotation((source + target) * .5f, Quaternion.LookRotation(target - source));
            Physics.SyncTransforms();
            int throws = crane.CraneHookThrowCount;
            try
            {
                for (int step = 0; step < 96; step++)
                {
                    source = crane.CraneHookCableExit;
                    target = root.Hero.transform.position + Vector3.up * 1.2f;
                    Vector3 delta = target - source;
                    Assert.That(barrier.Raycast(new Ray(source, delta.normalized), out _, delta.magnitude), Is.True,
                        "The fixed partition must cover the current cuff-to-target ray before testing acquisition: " +
                        $"step={step}, from={source}, target={target}, phase={crane.CranePhase}, " +
                        $"localFrom={wall.transform.InverseTransformPoint(source)}, localTarget={wall.transform.InverseTransformPoint(target)}.");
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                    Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting),
                        "A partition covering the acquisition ray must keep the crane waiting: " +
                        $"step={step}, from={source}, target={target}, currentFrom={crane.CraneHookCableExit}, phase={crane.CranePhase}.");
                }
                Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws), "A solid wall must prevent initial hook acquisition.");
                barrier.enabled = false;
                Physics.SyncTransforms();
                yield return AdvanceCraneTo(crane, CombatCranePhase.Signal, starts);
                CaptureCraneView(crane, "crane-02-signal");
                yield return AdvanceCraneTo(crane, CombatCranePhase.Striking, starts);
                int steps = 0;
                while (crane.CraneHookPhase == CombatCraneHookPhase.Outbound && crane.CraneHookCableLength < .6f && steps++ < 120)
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Outbound));
                Assert.That(crane.CraneHookCableLength, Is.GreaterThan(.5f), "Pause must freeze an actually extending hook.");
                Vector3 pausedBody = crane.transform.position, pausedCommand = crane.CraneCarriagePosition;
                Vector3 pausedAnchor = crane.CraneAnchorPosition, pausedHook = crane.CraneHookPosition;
                Vector3 pausedEye = crane.CraneHookEyePosition, pausedExit = crane.CraneHookCableExit;
                float pausedElapsed = crane.CranePhaseElapsed, pausedLength = crane.CraneHookCableLength;
                var pausedEnds = new Vector3[crane.CraneCableCount];
                for (int i = 0; i < pausedEnds.Length; i++) pausedEnds[i] = crane.GetCraneCableEnd(i);
                Assert.That(root.PauseMenu.Open(), Is.True);
                TickCrane(crane, 1f);
                yield return null;
                Assert.That(AudioListener.pause, Is.True);
                int[] pausedSamples = Array.ConvertAll(craneVoices, voice => voice.timeSamples);
                yield return null;
                for (int i = 0; i < craneVoices.Length; i++) Assert.That(craneVoices[i].timeSamples, Is.EqualTo(pausedSamples[i]));
                Assert.That(crane.transform.position, Is.EqualTo(pausedBody));
                Assert.That(crane.CraneCarriagePosition, Is.EqualTo(pausedCommand));
                Assert.That(crane.CraneAnchorPosition, Is.EqualTo(pausedAnchor));
                Assert.That(crane.CraneHookPosition, Is.EqualTo(pausedHook));
                Assert.That(crane.CraneHookEyePosition, Is.EqualTo(pausedEye));
                Assert.That(crane.CraneHookCableExit, Is.EqualTo(pausedExit));
                Assert.That(crane.CraneHookCableLength, Is.EqualTo(pausedLength));
                Assert.That(crane.CranePhaseElapsed, Is.EqualTo(pausedElapsed));
                for (int i = 0; i < pausedEnds.Length; i++) Assert.That(crane.GetCraneCableEnd(i), Is.EqualTo(pausedEnds[i]));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the hook encounter.");

                // The hook has already left the cuff: block its remaining real
                // flight rather than relying only on acquisition visibility.
                target = root.Hero.transform.position + Vector3.up * 1.2f;
                source = crane.CraneHookPosition;
                wall.transform.SetPositionAndRotation(Vector3.Lerp(source, target, .55f), Quaternion.LookRotation(target - source));
                barrier.enabled = true;
                Physics.SyncTransforms();
                for (int part = -1; part < CombatCraneAssetProvider.HookContactPoints.Length; part++)
                {
                    Vector3 local = part < 0 ? Vector3.up * .1f : CombatCraneAssetProvider.HookContactPoints[part];
                    float radius = part < 0 ? .08f : CombatCraneAssetProvider.HookContactRadii[part];
                    Vector3 centre = crane.GetCraneHookTransform().TransformPoint(local);
                    Assert.That(Vector3.Distance(centre, barrier.ClosestPoint(centre)), Is.GreaterThan(radius + .01f),
                        "The inserted wall must start ahead of the whole released hook, without spawning inside it.");
                }
                yield return AdvanceCraneTo(crane, CombatCranePhase.Waiting, starts);
                Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "An obstacle inserted during flight must stop the actual hook and its damage.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
                Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws + 1));
                Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            }
            finally
            {
                barrier.enabled = false;
                Object.Destroy(wall);
                Physics.SyncTransforms();
            }
        }

        private IEnumerator CaptureCraneMotion(CombatActor crane, Vector3[] starts, int maximumFrames, bool transport)
        {
            const string folder = "CombatTest/crane-motion";
            float captureDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 24f;
                for (int frame = 0; frame < maximumFrames; frame++)
                {
                    TickCrane(crane, 1f / 24f);
                    AimCraneGameplayCamera(crane);
                    yield return null;
                    string name = "frame" + (craneMovieFrames++).ToString("D4");
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                    LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                    AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, folder, name);
                    if (frame % 12 == 0) { AssertCraneCablesFollowRig(crane, starts); AssertCraneHookGeometry(crane); }
                    if (transport ? crane.CraneFloorIndex == 1 && !crane.CranePatrolTravelling :
                        crane.CranePhase == CombatCranePhase.Waiting && crane.CraneHookPhase == CombatCraneHookPhase.Stowed) break;
                }
            }
            finally { Time.captureDeltaTime = captureDelta; }
        }

        private void AimCraneGameplayCamera(CombatActor crane)
        {
            Vector3 focus = crane.transform.position + Vector3.up * 1.1f;
            if (crane.CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding or CombatCraneHookPhase.Returning)
                focus = (focus + crane.CraneHookPosition) * .5f;
            Vector3 delta = focus - (root.Hero.transform.position + Vector3.up * 1.5f);
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y, yaw));
            root.CameraFollow.RotatePitch(Mathf.Clamp(pitch, root.CameraFollow.MinimumOrbitPitch, root.CameraFollow.MaximumOrbitPitch) - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
        }

        private IEnumerator AdvanceCraneTo(CombatActor crane, CombatCranePhase phase, Vector3[] cableStarts)
        {
            const int maximumSteps = 4800;
            int step = 0;
            while ((crane.CranePhase != phase || crane.CranePatrolTravelling) && step < maximumSteps)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if ((step++ % 30) == 0)
                {
                    AssertCraneCablesFollowRig(crane, cableStarts);
                    if (step % 120 == 1) yield return null;
                }
            }
            Assert.That(crane.CranePhase, Is.EqualTo(phase),
                $"The crane did not reach {phase}; current phase={crane.CranePhase}, elapsed={crane.CranePhaseElapsed:F3}, " +
                $"hook={crane.CraneHookPhase}, eye={crane.CraneHookEyePosition}, cuff={crane.CraneHookCableExit}.");
            Assert.That(crane.CranePatrolTravelling, Is.False);
            AssertCraneCablesFollowRig(crane, cableStarts);
        }

        private void TickCrane(CombatActor crane, float seconds)
        {
            double remaining = seconds;
            while (remaining > .0000001d)
            {
                float step = (float)Math.Min(CombatTestRoot.SimulationStep, remaining);
                Vector3 command = crane.CraneCarriagePosition, head = crane.CraneAnchorPosition;
                int beforeAxis = crane.CraneMotionAxis;
                int beforeImpacts = root.Hero.ReceivedImpactCount;
                Vector3 hookBefore = crane.CraneHookPosition;
                root.Tick(step);
                if (root.Hero.ReceivedImpactCount > beforeImpacts && root.Hero.LastImpact.Kind != CombatImpactKind.Fall)
                {
                    Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(beforeImpacts + 1));
                    Assert.That(crane.CraneHookPhase == CombatCraneHookPhase.Outbound ||
                        crane.CraneHookPhase == CombatCraneHookPhase.Holding ||
                        crane.CraneHookPhase == CombatCraneHookPhase.Embedded ||
                        crane.IsCraneFrenzied && crane.CraneHookPhase == CombatCraneHookPhase.Returning, Is.True,
                        "Only the outbound hook can transact damage, never the fist or returning cable.");
                    Vector3 sweep = crane.CraneHookPosition - hookBefore;
                    float t = sweep.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(root.Hero.LastImpact.Point - hookBefore, sweep) / sweep.sqrMagnitude) : 0f;
                    Assert.That(Vector3.Distance(root.Hero.LastImpact.Point, hookBefore + sweep * t), Is.LessThan(.45f),
                        "The received impact must be on the visible hook's actual short swept path.");
                }
                remaining -= step;
                Vector3 delta = crane.CraneCarriagePosition - command;
                int moved = 0, axis = 0;
                if (Mathf.Abs(delta.x) > .00001f) { moved++; axis = 1; }
                if (Mathf.Abs(delta.y) > .00001f) { moved++; axis = 2; }
                if (Mathf.Abs(delta.z) > .00001f) { moved++; axis = 3; }
                Assert.That(moved, Is.LessThanOrEqualTo(1),
                    "The driven load command must move along one world axis per simulation step.");
                if (moved > 0)
                    Assert.That(axis, Is.EqualTo(beforeAxis).Or.EqualTo(crane.CraneMotionAxis),
                        "The reported crane axis must match its actual driven movement.");
                Vector3 headDelta = crane.CraneAnchorPosition - head;
                Assert.That(Mathf.Abs(headDelta.y), Is.LessThan(.00001f));
                Assert.That(Mathf.Abs(headDelta.x) <= .00001f || Mathf.Abs(headDelta.z) <= .00001f, Is.True,
                    "The physical overhead head must not move diagonally while its load is controlled by one axis.");
                Vector3 anchor = crane.CraneAnchorPosition, carriage = crane.CraneCarriagePosition;
                Assert.That(Vector2.Distance(new Vector2(anchor.x, anchor.z),
                    new Vector2(carriage.x, carriage.z)), Is.LessThan(.001f));
                craneMinimumCommand = Vector3.Min(craneMinimumCommand, carriage);
                craneMaximumCommand = Vector3.Max(craneMaximumCommand, carriage);
                Assert.That(float.IsFinite(crane.CraneSwingOffset.sqrMagnitude), Is.True);
                Assert.That(float.IsFinite(crane.CraneLimbMotionEnergy) && crane.CraneLimbMotionEnergy >= 0f, Is.True);
                maximumCraneSwing = Mathf.Max(maximumCraneSwing, crane.CraneSwingOffset.magnitude);
                maximumCraneLimbEnergy = Mathf.Max(maximumCraneLimbEnergy, crane.CraneLimbMotionEnergy);
                Assert.That(float.IsFinite(crane.CraneHookCableLength), Is.True);
                Assert.That(crane.CraneHookCableLength, Is.LessThanOrEqualTo(crane.CraneHookReach + .02f));
                if (crane.CraneHookThrowCount != observedCraneThrows)
                {
                    Assert.That(crane.CraneHookThrowCount, Is.EqualTo(observedCraneThrows + 1));
                    observedCraneThrows = crane.CraneHookThrowCount;
                    observedCraneAim = crane.CraneHookAimDirection;
                    observedCraneOrigin = crane.CraneHookFlightOrigin;
                    Vector3 target = root.Hero.transform.position + Vector3.up * 1.2f;
                    Assert.That(Vector3.Dot(observedCraneAim, (target - observedCraneOrigin).normalized), Is.GreaterThan(.98f),
                        "Release must aim at the actual three-dimensional torso position.");
                }
                if (observedCraneThrows > 0)
                {
                    Assert.That(crane.CraneHookAimDirection, Is.EqualTo(observedCraneAim), "The released aim must remain locked while the cuff and body swing.");
                    Assert.That(crane.CraneHookFlightOrigin, Is.EqualTo(observedCraneOrigin));
                }
            }
        }

        private IEnumerator VerifyCraneBlockedGoal(CombatActor crane, Vector3[] cableStarts)
        {
            int steps = 0;
            while ((!crane.CranePatrolTravelling || crane.CraneMotionAxis != 1) && steps++ < 1200)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if (steps % 120 == 0) yield return null;
            }
            Assert.That(crane.CraneMotionAxis, Is.EqualTo(1), "The real first patrol leg must start its X drive.");
            Assert.That(crane.CraneTravelBlocked, Is.False);
            Vector3 goal = crane.CraneTravelGoal;
            int arrived = crane.CraneArrivedStops, floor = crane.CraneFloorIndex;
            var obstacle = new GameObject("Тестовая преграда крана");
            obstacle.transform.SetParent(root.transform, false);
            obstacle.transform.position = crane.CraneCarriagePosition +
                new Vector3(Mathf.Sign(goal.x - crane.CraneCarriagePosition.x) * 1.4f, .35f, 0f);
            var barrierMesh = new Mesh
            {
                name = "Тестовая сетка преграды крана",
                vertices = new[] {
                    new Vector3(-.1f, -1.3f, -1.5f), new Vector3(.1f, -1.3f, -1.5f),
                    new Vector3(.1f, 1.3f, -1.5f), new Vector3(-.1f, 1.3f, -1.5f),
                    new Vector3(-.1f, -1.3f, 1.5f), new Vector3(.1f, -1.3f, 1.5f),
                    new Vector3(.1f, 1.3f, 1.5f), new Vector3(-.1f, 1.3f, 1.5f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                    0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2 }
            };
            MeshCollider barrier = obstacle.AddComponent<MeshCollider>();
            barrier.sharedMesh = barrierMesh;
            barrier.convex = false;
            Physics.SyncTransforms();
            try
            {
                // Force the reported overlap path at a live posed hand before
                // testing ordinary travel into the same non-convex obstacle.
                Vector3 ahead = obstacle.transform.position;
                obstacle.transform.position = crane.GetCraneCableEnd(1);
                Physics.SyncTransforms();
                crane.Present();
                LogAssert.NoUnexpectedReceived();
                Assert.That(barrier.convex, Is.False, "The warehouse mesh must retain its authored collision shape.");
                obstacle.transform.position = ahead;
                Physics.SyncTransforms();
                steps = 0;
                while (!crane.CraneTravelBlocked && steps++ < 1200)
                {
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                    if (steps % 120 == 0) yield return null;
                }
                Assert.That(crane.CraneTravelBlocked, Is.True, "A solid obstruction must stop the real suspension envelope.");
                TickCrane(crane, .6f);
                Assert.That(crane.CraneArrivedStops, Is.EqualTo(arrived),
                    "A blocked goal must not be counted as a completed patrol stop.");
                Assert.That(crane.CraneFloorIndex, Is.EqualTo(floor),
                    "A blocked route cannot move the authoritative stop to a floor it did not reach.");
                Assert.That(Vector3.Distance(crane.CraneCarriagePosition, goal), Is.GreaterThan(.2f));
                AssertCraneCablesFollowRig(crane, cableStarts);
                CaptureCraneView(crane, "crane-patrol-blocked");
            }
            finally
            {
                barrier.enabled = false;
                barrier.sharedMesh = null;
                Object.Destroy(obstacle);
                Object.Destroy(barrierMesh);
                Physics.SyncTransforms();
            }
        }

        private IEnumerator AdvanceCraneFloor(CombatActor crane, int floor, Vector3[] cableStarts)
        {
            const int maximumSteps = 4800;
            int step = 0;
            while ((crane.CraneFloorIndex != floor || crane.CranePatrolTravelling ||
                    crane.CranePhase != CombatCranePhase.Waiting) && step < maximumSteps)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if ((step++ % 30) == 0)
                {
                    AssertCraneCablesFollowRig(crane, cableStarts);
                    if (step % 120 == 1) yield return null;
                }
            }
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(floor),
                "The autonomous patrol must reach floor " + floor + " independently of the hero.");
            Assert.That(crane.CranePatrolTravelling, Is.False);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneCarriagePosition.y,
                Is.EqualTo(floor * WarehouseArenaPlan.LevelRise + .40f).Within(.02f));
            AssertCraneCablesFollowRig(crane, cableStarts);
        }

        private void PlaceCraneHero(Vector3 floorPosition, Vector3 facing)
        {
            Assert.That(root.WarehouseArea.TryGetHeight(floorPosition, out float height, root.Hero.Body.radius), Is.True,
                "The staged hero must stand on a real warehouse floor or stair tread.");
            Assert.That(height, Is.EqualTo(floorPosition.y).Within(.03f));
            root.Hero.ResetActor(floorPosition + Vector3.up * PlayerFactory.GroundedRootOffset, facing);
            Physics.SyncTransforms();
            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y, yaw));
            root.CameraFollow.Snap();
        }

        private void AssertCraneAudioBank(CombatActor crane)
        {
            Assert.That(CombatCraneAssetProvider.ClipNames, Has.Length.EqualTo(10));
            foreach (string name in CombatCraneAssetProvider.ClipNames)
                Assert.That(CombatCraneAssetProvider.LoadClip(name).events, Is.Empty);
            AudioClip[] cues = { CombatCraneAssetProvider.LoadWinchSound(), CombatCraneAssetProvider.LoadRailSound(),
                CombatCraneAssetProvider.LoadStartSound(), CombatCraneAssetProvider.LoadBrakeSound(),
                CombatCraneAssetProvider.LoadBodySound(), CombatCraneAssetProvider.LoadHurtSound(), CombatCraneAssetProvider.LoadReleaseSound() };
            for (int i = 0; i < cues.Length; i++)
            {
                for (int j = 0; j < i; j++) Assert.That(cues[i], Is.Not.SameAs(cues[j]));
                var samples = new float[cues[i].samples * cues[i].channels];
                Assert.That(cues[i].GetData(samples, 0), Is.True);
                double energy = 0d;
                bool finite = true;
                foreach (float sample in samples)
                {
                    finite &= float.IsFinite(sample);
                    energy += (double)sample * sample;
                }
                Assert.That(finite, Is.True);
                Assert.That(energy, Is.InRange(.001d, (double)samples.Length), "A retained crane cue needs finite audible energy.");
            }
            craneVoices = Array.FindAll(root.GetComponentsInChildren<AudioSource>(true), voice => Array.IndexOf(cues, voice.clip) >= 0);
            Assert.That(craneVoices, Has.Length.EqualTo(4));
            AudioSource body = null, motor = null;
            foreach (AudioSource voice in craneVoices)
            {
                Assert.That(voice.spatialBlend, Is.EqualTo(1f));
                Assert.That(voice.ignoreListenerPause || voice.playOnAwake, Is.False);
                Assert.That(voice.loop, Is.EqualTo(voice.clip == cues[0] || voice.clip == cues[1]));
                if (voice.clip == cues[4]) body = voice;
                if (voice.clip == cues[0]) motor = voice;
            }
            Assert.That(body, Is.Not.Null); Assert.That(motor, Is.Not.Null);
            Assert.That(Vector3.Distance(body.transform.position, motor.transform.position), Is.GreaterThan(.5f),
                "The hanging worker and overhead machinery have separate spatial sound origins.");
        }

        private void AssertCraneCablesFollowRig(CombatActor crane, Vector3[] starts)
        {
            string[] bones = CombatCraneAssetProvider.SuspensionBoneNames;
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.CraneCableCount, Is.EqualTo(bones.Length));
            Vector3 position = crane.transform.position;
            Assert.That(Mathf.Abs(position.x), Is.LessThanOrEqualTo(5.75f));
            Assert.That(Mathf.Abs(position.z), Is.LessThanOrEqualTo(5.75f));
            foreach (WarehouseArenaSurface surface in root.WarehousePlan.Surfaces)
                if (surface.Height > 0f)
                    Assert.That(surface.Bounds.Contains(new Vector2(position.x, position.z)), Is.False,
                        "The suspended root travels through the open well rather than a gallery deck.");
            Assert.That(crane.CraneAnchorPosition.y, Is.EqualTo(craneOriginalAnchor.y).Within(.001f));
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 end = crane.GetCraneCableEnd(i);
                Assert.That(Mathf.Abs(end.x), Is.LessThan(6f));
                Assert.That(Mathf.Abs(end.z), Is.LessThan(6f));
                Assert.That(Vector3.Distance(end, (FindAnatomicalBone(crane, bones[i]).position + FindAnatomicalBone(crane, bones[i]).rotation * CombatCraneAssetProvider.SuspensionLocalOffsets[i])), Is.LessThan(.025f),
                    bones[i] + " must remain attached to the visibly sampled rig, including the throwing wrist.");
                Vector3 initialOffset = starts[i] - craneOriginalAnchor;
                Vector3 feed = crane.GetCraneCableStart(i), previousEnd = feed;
                Vector3 offset = feed - crane.CraneAnchorPosition;
                Vector3 rotatedOffset = Quaternion.AngleAxis(Mathf.DeltaAngle(craneOriginalYaw, crane.transform.eulerAngles.y), Vector3.up) * initialOffset;
                Assert.That(Vector3.Distance(offset, rotatedOffset), Is.LessThan(.002f),
                    "The cable swivel must follow the body's yaw below the fixed overhead case.");
                Assert.That(float.IsFinite(end.sqrMagnitude), Is.True);
                Assert.That(feed.y - end.y, Is.GreaterThan(.01f), bones[i] + " must hang below its cable feed.");
                float visibleLength = 0f;
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                {
                    Vector3 from = crane.GetCraneCableSegmentStart(i, segment), to = crane.GetCraneCableSegmentEnd(i, segment);
                    Assert.That(Vector3.Distance(from, previousEnd), Is.LessThan(.001f), "Every curved suspension rope must be continuous.");
                    Transform piece = preparedCraneCables[i, segment];
                    Assert.That(crane.GetCraneCableTransform(i, segment), Is.SameAs(piece));
                    Assert.That(piece.gameObject.activeSelf, Is.True);
                    AssertCraneCableMesh(piece, from, to);
                    visibleLength += Vector3.Distance(from, to);
                    previousEnd = to;
                }
                Assert.That(Vector3.Distance(previousEnd, end), Is.LessThan(.001f));
                Assert.That(visibleLength + .001f, Is.GreaterThanOrEqualTo(Vector3.Distance(feed, end)));
            }
            AssertCraneHookGeometry(crane);
        }

        private readonly struct CraneAnatomicalFrame
        {
            internal readonly Transform Bone;
            private readonly Vector3 localForward, localUp;
            internal Vector3 Forward => Bone.TransformVector(localForward).normalized;
            internal Vector3 Up => Vector3.ProjectOnPlane(Bone.TransformVector(localUp), Forward).normalized;

            private CraneAnatomicalFrame(Transform bone, Vector3 forward, Vector3 up)
            { Bone = bone; localForward = forward; localUp = up; }

            internal static CraneAnatomicalFrame Create(CombatActor actor, string rendererName, string boneName)
            {
                SkinnedMeshRenderer canonical = CombatCraneAssetProvider.LoadBodyRenderer(rendererName);
                int index = Array.FindIndex(canonical.bones, bone => bone != null && bone.name == boneName);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                Matrix4x4 bind = canonical.transform.localToWorldMatrix * canonical.sharedMesh.bindposes[index].inverse;
                // The sculpt faces Blender -Y, which reaches Unity as -Z.
                // Calibrate against the imported asset's original bind frame,
                // never a collider or a frame captured from the starting pose.
                Vector3 forward = canonical.transform.root.TransformVector(Vector3.back);
                Vector3 up = canonical.transform.root.TransformVector(Vector3.up);
                return new CraneAnatomicalFrame(FindAnatomicalBone(actor, boneName),
                    bind.inverse.MultiplyVector(forward).normalized, bind.inverse.MultiplyVector(up).normalized);
            }
        }

        private static float CraneAnatomicalHeadYaw(CraneAnatomicalFrame head, CraneAnatomicalFrame torso)
        {
            Vector3 headForward = head.Forward, headUp = head.Up, torsoForward = torso.Forward, torsoUp = torso.Up;
            Vector3 heading = Vector3.ProjectOnPlane(headForward, torsoUp);
            Assert.That(heading.sqrMagnitude, Is.GreaterThan(.05f), "Head heading must remain readable through its held glances.");
            Assert.That(Vector3.Dot(headUp, torsoUp), Is.GreaterThan(.25f),
                "A sideways or inverted mask is a head roll, not an anatomical look-left/right.");
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(torsoForward, torsoUp), heading, torsoUp);
        }

        private IEnumerator CaptureCraneAppearance(CombatActor crane)
        {
            Camera camera = root.CameraFollow.Camera;
            float fov = camera.fieldOfView;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float orbitPitch = root.CameraFollow.TargetOrbitPitch, orbitYaw = camera.transform.eulerAngles.y;
            Vector3 heroPosition = root.Hero.transform.position, heroForward = root.Hero.transform.forward;
            CombatCranePhase phase = crane.CranePhase;
            float phaseTime = crane.CranePhaseElapsed;
            Vector3 carriage = crane.CraneCarriagePosition;
            int throws = crane.CraneHookThrowCount;
            var flashlight = root.Hero.GetComponentInChildren<CombatChestFlashlight>(true);
            Assert.That(flashlight, Is.Not.Null);
            Assert.That(flashlight.SpotLight.enabled, Is.True);
            CraneAnatomicalFrame torsoFrame = CraneAnatomicalFrame.Create(crane, "GEO_Torso", "chest");
            Vector3 forward = Vector3.ProjectOnPlane(torsoFrame.Forward, Vector3.up).normalized;
            CraneAnatomicalFrame headFrame = CraneAnatomicalFrame.Create(crane, "GEO_Head", "head");
            Vector3 floor = crane.CraneCarriagePosition; floor.y = crane.CraneGroundPosition.y;
            try
            {
                foreach (int view in new[] { 0, 1 })
                {
                    Vector3 direction = forward;
                    Vector3 standingCenter = floor;
                    if (view == 0)
                    {
                        crane.CaptureContactPose();
                        Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
                        direction = Vector3.ProjectOnPlane(headFrame.Forward, Vector3.up);
                        Assert.That(direction.sqrMagnitude, Is.GreaterThan(.05f));
                        direction.Normalize();
                        standingCenter = new Vector3(head.x, floor.y, head.z);
                    }
                    PlaceCraneHero(standingCenter + direction * 3.2f, -direction);
                    Vector3 focus = Vector3.zero;
                    for (int frame = 0; frame < 4; frame++)
                    {
                        crane.CaptureContactPose();
                        Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
                        focus = view == 0 ? head : crane.transform.position + Vector3.up * .95f;
                        Vector3 delta = focus - (root.Hero.transform.position + Vector3.up * 1.5f);
                        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                        float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                        root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                        root.CameraFollow.RotatePitch(Mathf.Clamp(pitch, root.CameraFollow.MinimumOrbitPitch,
                            root.CameraFollow.MaximumOrbitPitch) - root.CameraFollow.TargetOrbitPitch);
                        root.CameraFollow.Snap();
                        root.Hero.AdvanceSimulation(.1f);
                        root.Hero.Present();
                        yield return null;
                    }
                    string name = view == 0 ? "Test crane - Close head" : "Test crane - Front body";
                    Vector3 eye = focus + direction * 2.8f + Vector3.up * .1f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.fieldOfView = view == 0 ? 22f : 56f;
                    CaptureCraneView(crane, name, gameplayCamera: true);
                }
            }
            finally
            {
                PlaceCraneHero(heroPosition - Vector3.up * PlayerFactory.GroundedRootOffset, heroForward);
                root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, orbitYaw));
                root.CameraFollow.RotatePitch(orbitPitch - root.CameraFollow.TargetOrbitPitch);
                root.CameraFollow.Snap();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = fov;
            }
            Assert.That(crane.CranePhase, Is.EqualTo(phase));
            Assert.That(crane.CranePhaseElapsed, Is.EqualTo(phaseTime));
            Assert.That(crane.CraneCarriagePosition, Is.EqualTo(carriage));
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws), "Appearance capture cannot advance the encounter.");
        }

        private static void AssertCraneAppearance(CombatActor crane, bool intact)
        {
            Assert.That(crane.GetComponentInChildren<CombatCraneAppearance>(true), Is.Not.Null);
            Assert.That(crane.GetComponentInChildren<CombatZombieAppearance>(true), Is.Null,
                "The crane owns its surface restoration independently of the docker's civilian outfit.");
            CombatCraneAssetProvider.BodyManifest manifest = CombatCraneAssetProvider.LoadBodyManifest();
            Assert.That(manifest.geometry_owner, Is.EqualTo("independent_sculpt"));
            Assert.That(manifest.bounds_space, Is.EqualTo("unity_metres"));
            Texture2D atlas = CombatCraneAssetProvider.LoadBodyAtlas();
            Material material = CombatCraneAssetProvider.LoadBodyMaterial();
            Shader shader = Resources.Load<Shader>("Shaders/WarehouseSurface");
            Texture2D response = Resources.Load<Texture2D>("Combat/CraneMaterialResponse");
            Assert.That(atlas.width, Is.EqualTo(2048));
            Assert.That(response, Is.Not.Null);
            Assert.That(response.width, Is.EqualTo(1024));
            Assert.That(material.shader, Is.SameAs(shader));
            Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(atlas));
            Assert.That(material.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
            Assert.That(manifest.estimated_gpu_bytes, Is.LessThan(7L * 1024 * 1024));
            SkinnedMeshRenderer[] skins = crane.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var properties = new MaterialPropertyBlock();
            bool fleshSeen = false, carapaceSeen = false;
            foreach (CombatCraneAssetProvider.BodyMesh part in manifest.meshes)
            {
                SkinnedMeshRenderer skin = Array.Find(skins, candidate => candidate.name == part.name);
                Assert.That(skin, Is.Not.Null, "The retained damage source must keep its authored name: " + part.name);
                Assert.That(skin.sharedMesh, Is.SameAs(CombatCraneAssetProvider.LoadBodyRenderer(part.name).sharedMesh),
                    "Wounds and reset must retain the authored crane skin: " + part.name);
                Material[] assigned = skin.sharedMaterials;
                Assert.That(assigned, Has.Length.EqualTo(part.surfaces.Length));
                for (int slot = 0; slot < assigned.Length; slot++)
                {
                    Assert.That(assigned[slot], Is.SameAs(CombatCraneAssetProvider.LoadBodyMaterial(part.surfaces[slot])),
                        "Each sculpt region retains its own shared material role: " + part.name);
                    if (part.visible)
                    {
                        fleshSeen |= part.surfaces[slot] == "flesh";
                        carapaceSeen |= part.surfaces[slot] == "carapace";
                    }
                }
                if (intact || !part.visible)
                    Assert.That(skin.enabled && skin.gameObject.activeInHierarchy, Is.EqualTo(part.visible),
                        "Only the authored anatomical surface set may return on reset: " + part.name);
            }
            Assert.That(fleshSeen && carapaceSeen, Is.True, "The industrial mutant needs separate flesh and embedded carapace surfaces.");
            int paintedSurfaces = 0;
            foreach (SkinnedMeshRenderer skin in skins)
            {
                bool visible = skin.enabled && skin.gameObject.activeInHierarchy;
                if (skin.name.StartsWith("CLO_", StringComparison.Ordinal) || skin.name.StartsWith("HAIR_", StringComparison.Ordinal))
                    Assert.That(visible, Is.False, "Civilian clothes and hair cannot reappear on the insect body.");
                if (!visible || skin.sharedMaterial == null || skin.sharedMaterial.shader != shader) continue;
                skin.GetPropertyBlock(properties);
                Assert.That(properties.GetTexture("_BaseMap") ?? skin.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(atlas),
                    "Live exterior wounds must keep the crane atlas instead of the original worker painting.");
                Assert.That(skin.sharedMaterial.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
                properties.Clear(); paintedSurfaces++;
            }
            Assert.That(paintedSurfaces, Is.GreaterThan(0));
            MeshFilter[] passive = crane.GetCraneHookTransform().parent.GetComponentsInChildren<MeshFilter>(true);
            foreach (CombatCraneAssetProvider.PassiveMesh part in manifest.passive_meshes)
            {
                int matches = 0;
                foreach (MeshFilter surface in passive)
                {
                    if (surface.name != part.name) continue;
                    matches++;
                    Assert.That(surface.sharedMesh, Is.SameAs(CombatCraneAssetProvider.LoadPassiveSurface(part.name).sharedMesh));
                    Material metal = surface.GetComponent<Renderer>().sharedMaterial;
                    Assert.That(metal.shader, Is.SameAs(shader));
                    Assert.That(metal.GetTexture("_BaseMap"), Is.SameAs(atlas));
                    Assert.That(metal.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
                }
                Assert.That(matches, Is.GreaterThan(0), "The mechanical kit must use the same authored painting: " + part.name);
            }
            if (manifest.hardware != null)
            {
                MeshFilter[] fittings = crane.DamageRigRoot.GetComponentsInChildren<MeshFilter>(true);
                foreach (CombatCraneAssetProvider.HardwareMesh part in manifest.hardware)
                {
                    MeshFilter fitting = Array.Find(fittings, candidate => candidate.name == part.name);
                    Assert.That(fitting, Is.Not.Null, "The sculpt must retain its embedded cable fitting: " + part.name);
                    Assert.That(fitting.transform.parent.name, Is.EqualTo(part.bone));
                    Assert.That(fitting.sharedMesh, Is.SameAs(CombatCraneAssetProvider.LoadPassiveSurface(part.name).sharedMesh));
                    Assert.That(fitting.GetComponent<Renderer>().sharedMaterial, Is.SameAs(CombatCraneAssetProvider.LoadBodyMaterial("steel")));
                    Assert.That(fitting.GetComponent<Renderer>().enabled, Is.True, "Surviving anatomy and reset retain their cable fittings.");
                }
            }
        }

        private void AssertCraneHookGeometry(CombatActor crane)
        {
            Assert.That(crane.CraneRightHandVisible, Is.False, "The cuff and hook replace the ordinary right fist in the live damage rig.");
            Assert.That(crane.GetCraneHookTransform(), Is.SameAs(preparedCraneHook));
            Assert.That(crane.GetCraneHookCableTransform(), Is.SameAs(preparedCraneHookCable));
            Assert.That(preparedCraneHook.gameObject.activeSelf, Is.True);
            Assert.That(preparedCraneHookCable.gameObject.activeSelf, Is.True);
            Assert.That(crane.CraneHookCableLength,
                Is.EqualTo(Vector3.Distance(crane.CraneHookCableExit, crane.CraneHookEyePosition)).Within(.001f));
            AssertCraneCableMesh(preparedCraneHookCable, crane.CraneHookCableExit, crane.CraneHookEyePosition);
            Renderer[] renderers = preparedCraneHook.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
            Assert.That(bounds.size.magnitude, Is.InRange(.2f, 1.5f), "The real hook mesh retains metre scale while it flies.");
            bounds.Expand(.15f);
            Assert.That(bounds.Contains(crane.CraneHookEyePosition), Is.True);
            Assert.That(bounds.Contains(crane.CraneHookPosition), Is.True, "The damage anchor must travel with the actual visible hook.");
        }

        private static void AssertCraneCableMesh(Transform cable, Vector3 from, Vector3 to)
        {
            Renderer[] renderers = cable.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
            Assert.That(Vector3.Distance(bounds.center, (from + to) * .5f), Is.LessThan(.08f),
                "The imported mesh must span its real world endpoints.");
            Assert.That(bounds.size.magnitude, Is.EqualTo(Vector3.Distance(from, to)).Within(.08f),
                "Imported scale must not multiply a visible cable's world length.");
        }

        private void CaptureCraneView(CombatActor actor, string name, bool gameplayCamera = false)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                if (!gameplayCamera)
                {
                    bool closeBody = actor.CranePhase is CombatCranePhase.Signal or CombatCranePhase.Striking or
                        CombatCranePhase.Recovering or CombatCranePhase.Released;
                    // The central hall is open only inside XZ +/-6m. A shot
                    // from the arrival gallery is hidden behind its own decks.
                    Vector3 focus = closeBody ? (actor.transform.position + Vector3.up * 1.1f + actor.CraneHookPosition) * .5f :
                        (actor.CraneAnchorPosition + actor.transform.position) * .5f;
                    Vector3 eye = closeBody ? focus + new Vector3(3.5f, 2f, 4f) :
                        new Vector3(actor.CraneCarriagePosition.x > 0f ? -4.8f : 4.8f, 3.7f,
                            actor.CraneCarriagePosition.z > 0f ? -4.8f : 4.8f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.fieldOfView = closeBody ? 60f : 74f;
                }
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
            }
        }
    }
}
