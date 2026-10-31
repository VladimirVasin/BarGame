using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    // Uses existing production assets; deliberately has no generator/prebuild setup.
    public sealed class OrdinaryCombatPlayModeTests
    {
        private const float FrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private readonly object cursorOwner = new object();
        private readonly Vector3 origin = new Vector3(2000f, 0f, 2000f);
        private InputTestFixture input;
        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject fixture;
        private Scene previousScene, fixtureScene;
        private PlayerRuntime player;
        private Player3DCharacterPresentation presentation;
        private PlayerCrowbarCombatController controller;
        private CombatActor hero, enemy, flankEnemy;
        private CombatActor[] pairParticipants;
        private Player3DAssetRegistry rawRegistry;
        private Transform rawFrame;
        private AnimationClip rawReady, rawStrike;
        private Mesh handBake;
        private List<FingerClosureProbe> fingerProbes;
        private NpcHandPose heldHands;
        private GameObject orderedWeapon;
        private Transform orderedGrip, orderedTip;
        private float orderedButtStation;
        private int lastUnsettledSequence = int.MinValue;
        private MeleeAttackOutcome lastUnsettledOutcome;
        private CombatArmSupportState lastUnsettledSupport;
        private Camera camera;
        private float previousDelta, previousScale;
        private bool previousFocus;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousDelta = Time.captureDeltaTime; previousScale = Time.timeScale;
            previousFocus = GameInput.MovementFocused;
            Time.captureDeltaTime = FrameSeconds; Time.timeScale = 1f;
            input = new InputTestFixture(); input.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            GameSessionState.BeginNewGame(); GameInput.HandleApplicationFocus(true);
            GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
            previousScene = SceneManager.GetActiveScene();
            fixtureScene = SceneManager.CreateScene("OrdinaryCombatFixture");
            SceneManager.SetActiveScene(fixtureScene);
            fixture = new GameObject("Ordinary combat fixture");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Combat test floor"; floor.transform.SetParent(fixture.transform, false);
            floor.transform.position = origin + Vector3.down * .1f;
            floor.transform.localScale = new Vector3(40f, .2f, 40f);
            floor.layer = CaptureLayer;
            var cameraObject = new GameObject("Combat inspection camera");
            cameraObject.transform.SetParent(fixture.transform, false);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            if (Object.FindAnyObjectByType<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
            camera.nearClipPlane = .03f; camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.19f, .21f, .24f);
            camera.cullingMask = 1 << CaptureLayer;
            camera.allowHDR = camera.allowMSAA = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = 0;
            var lightObject = new GameObject("Combat inspection key");
            lightObject.transform.SetParent(fixture.transform, false);
            Light key = lightObject.AddComponent<Light>(); key.type = LightType.Directional;
            key.intensity = 1.7f; key.color = new Color(1f, .9f, .8f); key.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            player = PlayerFactory.Create(fixture.transform, origin + Vector3.up * PlayerFactory.GroundedRootOffset,
                camera, null, null);
            presentation = (Player3DCharacterPresentation)player.Visual;
            controller = player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            var follow = cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, true); follow.enabled = false;
            GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
            Assert.That(GameSessionState.TryCollectWorldItem("test-ordinary-combat", InventoryItemId.CombatCrowbar), Is.True);
            Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatCrowbar, true), Is.True);
            for (int frame = 0; frame < 180 && (controller.Actor == null || !controller.Actor.HasWeaponInHand ||
                controller.Actor.IsSwitchingWeapon); frame++) yield return null;
            hero = controller.Actor;
            Assert.That(hero != null && hero.HasWeaponInHand && !hero.IsSwitchingWeapon, Is.True);

            enemy = CreateEnemy(0);
            pairParticipants = new[] { hero, enemy };
            hero.SetCombatParticipants(pairParticipants); enemy.SetCombatParticipants(pairParticipants);
            hero.SetContactTarget(enemy); enemy.SetContactTarget(hero);
            yield return PlacePair(4f);
            yield return Frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameCursor.Release(cursorOwner);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            input?.TearDown();
            if (fixture != null) Object.Destroy(fixture);
            if (handBake != null) Object.Destroy(handBake);
            GameSessionState.BeginNewGame(); GameInput.HandleApplicationFocus(previousFocus);
            Time.captureDeltaTime = previousDelta; Time.timeScale = previousScale;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (fixtureScene.IsValid() && fixtureScene.isLoaded) yield return SceneManager.UnloadSceneAsync(fixtureScene);
            yield return null;
        }

        [UnityTest, Category("CurrentCombat")]
        public IEnumerator OrdinaryHitsKeepControlAndCrowbarStillContactsWallsAndParries()
        {
            yield return EarlyStrikePresentation(false);
            yield return EarlyStrikePresentation(true);
            // Fixed facing separates the physical lateral arc from target-follow
            // aiming. Every case receives the real authored metal sweep.
            foreach (Vector2 placement in new[] { new Vector2(.90f, 0f), new Vector2(.65f, 0f),
                new Vector2(.90f, 35f), new Vector2(.90f, -35f), new Vector2(.90f, 180f) })
            {
                float distance = placement.x, angle = placement.y;
                bool expectedHit = angle != 180f, captureArc = angle == 0f && distance == .90f;
                yield return PlacePair(distance, angle);
                hero.SetContactTarget(null);
                yield return Frames(2);
                Vector3 enemyStart = enemy.transform.position;
                Quaternion facing = hero.transform.rotation;
                Transform chest = CityPedestrianHandProps.FindSocket(enemy.DamageRigRoot, "chest");
                Quaternion chestBefore = chest.localRotation;
                if (captureArc) Capture("ordinary-hit-before", true);
                int sequence = hero.State.AttackSequence;
                int impacts = enemy.ReceivedImpactCount;
                float windup = hero.State.Settings.WindupSeconds, active = hero.State.Settings.ActiveSeconds;
                float activeEnd = windup + active;
                float[] captureTimes = { windup * .5f, windup + .01f, windup + active * .5f,
                    activeEnd - .01f, activeEnd + hero.State.Settings.AnimationRecoverySeconds * .6f };
                string[] captureNames = { "windup", "right-entry", "centre", "left-follow-through", "return" };
                int nextCapture = 0;
                float contactElapsed = -1f, activePath = 0f, leftExtent = 0f, rightExtent = 0f;
                Transform tip = CombatAssetProvider.FindAnchor(hero.Weapon, "StrikeTip");
                Vector3 previousTip = tip.position;
                input.Press(mouse.leftButton, queueEventOnly: true);
                bool reacted = false;
                for (int frame = 0; frame < 180; frame++)
                {
                    yield return Frames(1);
                    if (frame == 1) input.Release(mouse.leftButton, queueEventOnly: true);
                    if (hero.State.Phase == MeleePhase.Active)
                    {
                        activePath += Vector3.Distance(previousTip, tip.position);
                        float side = Vector3.Dot(tip.position - hero.transform.position, hero.transform.right);
                        leftExtent = Mathf.Min(leftExtent, side); rightExtent = Mathf.Max(rightExtent, side);
                    }
                    previousTip = tip.position;
                    if (contactElapsed < 0f && enemy.ReceivedImpactCount > impacts)
                        contactElapsed = hero.State.AttackElapsed;
                    if (captureArc && nextCapture < captureTimes.Length && hero.State.IsAttacking &&
                        hero.State.AttackElapsed >= captureTimes[nextCapture])
                    {
                        Capture("ordinary-arc-" + captureNames[nextCapture] + "-" +
                            Mathf.RoundToInt(hero.State.AttackElapsed * 1000f) + "ms", true);
                        nextCapture++;
                    }
                    Assert.That(hero.State.AttackOutcome, Is.Not.EqualTo(MeleeAttackOutcome.Obstacle),
                        "The clear close swing must not be cancelled by its own anatomy or support solver.");
                    if (!reacted && enemy.State.Phase == MeleePhase.Stagger && enemy.State.StaggerElapsed >= .09f)
                    {
                        reacted = true;
                        Assert.That(enemy.State.StaggerDuration, Is.LessThanOrEqualTo(1.25f));
                        Assert.That(enemy.ActiveClipName, Is.EqualTo(CombatZombieAssetProvider.OrdinaryHitClip));
                        Assert.That(Quaternion.Angle(chestBefore, chest.localRotation), Is.GreaterThan(2f),
                            "A successful hit must visibly change the actual chest pose.");
                        if (captureArc) Capture("ordinary-hit-onset", true);
                    }
                    if (reacted && enemy.State.Phase == MeleePhase.Ready && hero.State.Phase == MeleePhase.Ready) break;
                    if (!expectedHit && hero.State.AttackSequence > sequence && hero.State.Phase == MeleePhase.Ready) break;
                }
                Assert.That(hero.State.AttackSequence, Is.EqualTo(sequence + 1));
                Assert.That(Quaternion.Angle(facing, hero.transform.rotation), Is.LessThan(1f),
                    "Lateral coverage must come from the metal arc rather than rotating the hero onto each victim.");
                if (!expectedHit)
                {
                    Assert.That(enemy.ReceivedImpactCount, Is.EqualTo(impacts), "A victim behind the forward arc must remain untouched.");
                    Assert.That(hero.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                    continue;
                }
                Assert.That(enemy.ReceivedImpactCount, Is.EqualTo(impacts + 1), $"One physical arc, one hit at {distance}m/{angle}deg.");
                Assert.That(contactElapsed, Is.InRange(windup - FrameSeconds,
                    activeEnd + FrameSeconds + CombatTestRoot.SimulationStep), "Only the damaging metal interval may produce a contact.");
                TestContext.Out.WriteLine($"Real arc contact: range={distance:F2}m, angle={angle:F0}deg, time={contactElapsed:F3}s.");
                Assert.That(enemy.State.Health, Is.LessThan(enemy.State.Settings.MaxHealth));
                Assert.That(reacted, Is.True, "The real contact must enter a visible standing hit reaction.");
                Assert.That(enemy.State.Phase, Is.EqualTo(MeleePhase.Ready),
                    $"distance={distance}; stagger={enemy.State.StaggerElapsed}/{enemy.State.StaggerDuration}; available={enemy.IsAvailable}");
                Assert.That(Vector3.Distance(enemyStart, enemy.transform.position), Is.LessThan(.30f),
                    "An ordinary crowbar hit has bounded push and returns the enemy to standing.");
                if (captureArc)
                {
                    Assert.That(activePath, Is.GreaterThan(.8f), "The damaging interval needs visible lateral travel.");
                    Assert.That(rightExtent, Is.GreaterThan(.25f));
                    Assert.That(leftExtent, Is.LessThan(-.25f), "The damaging metal must continue through the left flank.");
                    Assert.That(nextCapture, Is.EqualTo(captureTimes.Length));
                    Capture("ordinary-arc-settled", true);
                }
            }

            yield return PlacePair(.90f, 35f);
            hero.SetContactTarget(null);
            flankEnemy = CreateEnemy(1);
            Vector3 leftDirection = Quaternion.AngleAxis(-35f, Vector3.up) * Vector3.forward;
            flankEnemy.ResetActor(hero.transform.position + leftDirection * .90f, -leftDirection);
            var group = new[] { hero, enemy, flankEnemy };
            hero.SetCombatParticipants(group); enemy.SetCombatParticipants(group); flankEnemy.SetCombatParticipants(group);
            Physics.SyncTransforms();
            yield return Frames(2);
            int groupSequence = hero.State.AttackSequence;
            int rightImpacts = enemy.ReceivedImpactCount, leftImpacts = flankEnemy.ReceivedImpactCount;
            float rightContact = -1f, leftContact = -1f;
            input.Press(mouse.leftButton, queueEventOnly: true);
            for (int frame = 0; frame < 180; frame++)
            {
                yield return Frames(1);
                if (frame == 1) input.Release(mouse.leftButton, queueEventOnly: true);
                Assert.That(enemy.ReceivedImpactCount, Is.LessThanOrEqualTo(rightImpacts + 1));
                Assert.That(flankEnemy.ReceivedImpactCount, Is.LessThanOrEqualTo(leftImpacts + 1));
                if (rightContact < 0f && enemy.ReceivedImpactCount > rightImpacts) rightContact = hero.State.AttackElapsed;
                if (leftContact < 0f && flankEnemy.ReceivedImpactCount > leftImpacts) leftContact = hero.State.AttackElapsed;
                if (rightContact >= 0f && leftContact >= 0f && hero.State.Phase == MeleePhase.Ready &&
                    enemy.State.Phase == MeleePhase.Ready && flankEnemy.State.Phase == MeleePhase.Ready) break;
            }
            Assert.That(hero.State.AttackSequence, Is.EqualTo(groupSequence + 1), "One press commits one shared arc.");
            Assert.That(enemy.ReceivedImpactCount, Is.EqualTo(rightImpacts + 1), "The early right target gets one actual metal contact.");
            Assert.That(flankEnemy.ReceivedImpactCount, Is.EqualTo(leftImpacts + 1), "The same arc continues to the late left target once.");
            Assert.That(rightContact, Is.GreaterThanOrEqualTo(MeleeCombatSettings.Crowbar.WindupSeconds - FrameSeconds));
            Assert.That(leftContact, Is.GreaterThan(rightContact), "The physical sweep reaches right then left, without duplicating either receipt.");
            Assert.That(leftContact, Is.LessThanOrEqualTo(MeleeCombatSettings.Crowbar.WindupSeconds +
                MeleeCombatSettings.Crowbar.ActiveSeconds + FrameSeconds + CombatTestRoot.SimulationStep));
            TestContext.Out.WriteLine($"Shared real arc: right={rightContact:F3}s, left={leftContact:F3}s.");
            Capture("ordinary-arc-two-targets-settled", true);
            flankEnemy.gameObject.SetActive(false);
            Object.Destroy(flankEnemy.gameObject); flankEnemy = null;
            hero.SetCombatParticipants(pairParticipants); enemy.SetCombatParticipants(pairParticipants);

            yield return PlacePair(4f);
            hero.SetContactTarget(enemy);
            Vector3 walkStart = hero.transform.position;
            for (int hit = 0; hit < 3; hit++)
            {
                float interruptedCooldown = 0f;
                int interruptedSequence = hero.State.AttackSequence;
                if (hit == 0)
                {
                    input.Press(mouse.leftButton, queueEventOnly: true);
                    yield return Frames(2);
                    input.Release(mouse.leftButton, queueEventOnly: true);
                    Assert.That(hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                    interruptedSequence = hero.State.AttackSequence;
                    interruptedCooldown = hero.State.CooldownRemaining(MeleeBufferedAction.Attack);
                }
                if (hit == 1)
                {
                    input.Press(keyboard.wKey, queueEventOnly: true);
                    yield return Frames(12);
                    walkStart = hero.transform.position;
                }
                float health = hero.State.Health;
                Vector3 hitStart = hero.transform.position;
                ApplyOrdinaryHit();
                Assert.That(hero.State.Health, Is.LessThan(health), "Each separated ordinary contact still deals damage.");
                if (hit == 0)
                {
                    float healthAfterHit = hero.State.Health;
                    int acceptedImpacts = hero.ReceivedImpactCount;
                    Assert.That(hero.State.StaggerDuration, Is.EqualTo(.22f).Within(.001f));
                    Assert.That(hero.State.CooldownRemaining(MeleeBufferedAction.Attack),
                        Is.EqualTo(interruptedCooldown).Within(.001f), "A hit cannot refund the committed swing's cooldown.");
                    float impactAge = hero.ImpactMotion.Age;
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        // No explicit simulation step while paused; wait longer
                        // than the grace period in rendered frames alone.
                        for (int frame = 0; frame < 35; frame++) yield return null;
                        Assert.That(hero.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                        Assert.That(hero.ImpactMotion.Age, Is.EqualTo(impactAge));
                        Assert.That(hero.transform.position, Is.EqualTo(hitStart));
                        Assert.That(hero.State.CooldownRemaining(MeleeBufferedAction.Attack),
                            Is.EqualTo(interruptedCooldown).Within(.001f));
                        Assert.That(hero.IsKnockedDown || hero.IsRagdollActive, Is.False);
                    }
                    ApplyOrdinaryHit();
                    Assert.That(hero.State.Health, Is.EqualTo(healthAfterHit),
                        "The brief ordinary-hit grace prevents immediate chained damage.");
                    Assert.That(hero.ReceivedImpactCount, Is.EqualTo(acceptedImpacts),
                        "An ignored contact cannot restart presentation or push.");
                }
                Assert.That(hero.State.StaggerDuration, Is.LessThanOrEqualTo(.30f),
                    "A routine unarmed hit only briefly interrupts the hero.");
                yield return Frames(14);
                Assert.That(hero.State.Phase, Is.EqualTo(MeleePhase.Ready), "Control must return after the short reaction.");
                Assert.That(player.Motor.enabled && player.Motor.InputEnabled && hero.Body.enabled, Is.True);
                if (hit == 0)
                {
                    Assert.That(Vector3.Distance(hitStart, hero.transform.position), Is.LessThan(.16f));
                    Assert.That(hero.State.CooldownRemaining(MeleeBufferedAction.Attack), Is.GreaterThan(0f));
                    Assert.That(hero.TryAttack(), Is.False);
                    Assert.That(hero.State.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.Cooldown));
                    Assert.That(hero.State.AttackSequence, Is.EqualTo(interruptedSequence));
                    for (int frame = 0; frame < 120 && hero.State.CooldownRemaining(MeleeBufferedAction.Attack) > 0f; frame++)
                        yield return Frames(1);
                    Assert.That(hero.TryAttack(), Is.True, "A fresh attack becomes available when the original cooldown expires.");
                    Assert.That(hero.State.AttackSequence, Is.EqualTo(interruptedSequence + 1));
                    for (int frame = 0; frame < 120 && hero.State.Phase != MeleePhase.Ready; frame++) yield return Frames(1);
                }
                yield return Frames(20);
            }
            Assert.That(Vector3.Distance(walkStart, hero.transform.position), Is.GreaterThan(.3f),
                "Held movement must continue through repeated ordinary hits without a recovery lock.");
            input.Release(keyboard.wKey, queueEventOnly: true);
            yield return Frames(2);

            yield return PlacePair(4f);
            var wall = new GameObject("Solid strike obstacle"); wall.transform.SetParent(fixture.transform, false);
            wall.transform.position = hero.transform.position + hero.transform.forward * .65f + Vector3.up * 1.2f;
            BoxCollider obstacle = wall.AddComponent<BoxCollider>(); obstacle.size = new Vector3(2f, 2f, .08f);
            Physics.SyncTransforms();
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return Frames(2);
            input.Release(mouse.leftButton, queueEventOnly: true);
            for (int frame = 0; frame < 90 && hero.State.AttackOutcome == MeleeAttackOutcome.None; frame++) yield return Frames(1);
            Assert.That(hero.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle), "A real wall still stops the metal arc.");
            Assert.That(presentation.ActiveClipName, Is.EqualTo("CombatRecoil"));
            obstacle.enabled = false; Object.Destroy(wall);
            yield return Frames(60);
            presentation.ReapplyLatePresentationPose();
            AssertOrderedGrip(GetHeldHands());

            yield return PlacePair(4f);
            input.Press(mouse.rightButton, queueEventOnly: true);
            for (int frame = 0; frame < 30 && !hero.State.IsBlocking; frame++) yield return Frames(1);
            Assert.That(hero.State.IsBlocking, Is.True, "A fresh RMB opens the finite front parry.");
            float parryHealth = hero.State.Health;
            ApplyOrdinaryHit();
            Assert.That(hero.LastImpact.Result, Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(hero.State.Health, Is.EqualTo(parryHealth));
            yield return Frames(40);
            Assert.That(hero.State.IsBlocking, Is.False, "Holding RMB cannot renew the parry window.");
            ApplyOrdinaryHit();
            Assert.That(hero.State.Health, Is.LessThan(parryHealth), "The same front contact lands after the finite window.");
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return Frames(24);
            // Ready restores commands before the existing visible hit-to-gait
            // handoff has finished. Inspect a settled grip after that return.
            int settleFrames = 0;
            for (; settleFrames < 60 && !(hero.State.Phase == MeleePhase.Ready && !hero.ImpactMotion.IsActive &&
                hero.SupportGrip.IsSupportingWeapon && !hero.SupportGrip.IsSupportingGuard); settleFrames++)
                yield return Frames(1);
            presentation.ReapplyLatePresentationPose();
            bool settled = hero.State.Phase == MeleePhase.Ready && !hero.ImpactMotion.IsActive &&
                hero.SupportGrip.IsSupportingWeapon && !hero.SupportGrip.IsSupportingGuard;
            string gripDiagnostic = $"Post-hit grip: waited={settleFrames}, phase={hero.State.Phase}, " +
                $"clip={presentation.ActiveClipName}, actorClip={hero.ActiveClipName}, " +
                $"impact={hero.ImpactMotion.IsActive}/{hero.ImpactMotion.Age:F3}, " +
                $"support={hero.SupportArmState}/{hero.SupportGripWeight:F3}, error={hero.SupportGrip.JournalContactError:F4}";
            TestContext.Out.WriteLine(gripDiagnostic);
            Capture(settled ? "post-hit-grip-settled" : "post-hit-grip-unsettled", true);
            CaptureHandViews(settled ? "post-hit-hands-settled" : "post-hit-hands-unsettled", GetHeldHands());
            Assert.That(settled, Is.True, "The visible ordinary-hit return must regain its real held contact. " + gripDiagnostic);
            AssertOrderedGrip(GetHeldHands());
            LogAssert.NoUnexpectedReceived();
        }

        private CombatActor CreateEnemy(int index)
        {
            var opponent = new GameObject("Ordinary zombie " + index); opponent.transform.SetParent(fixture.transform, false);
            opponent.transform.position = origin + Vector3.up * PlayerFactory.GroundedRootOffset + Vector3.forward * 4f;
            CharacterController body = opponent.AddComponent<CharacterController>();
            body.height = 1.75f; body.radius = .32f; body.center = Vector3.up * .875f;
            body.skinWidth = PlayerFactory.GroundedRootOffset; body.stepOffset = PlayerFactory.StepOffset;
            body.slopeLimit = PlayerFactory.SlopeLimitDegrees; body.minMoveDistance = 0f;
            VillageResidentPresentation visual = DefaultNpcFactory.CreateForCharacter(opponent.transform,
                DefaultNpcPopulation.CombatTestOpponentId(index));
            CombatZombieAssetProvider.ApplyAppearance(visual.gameObject, index);
            CombatActor actor = opponent.AddComponent<CombatActor>();
            actor.InitializeOpponent(visual, body, kind: CombatEnemyKind.Zombie);
            return actor;
        }

        private IEnumerator PlacePair(float distance, float angle = 0f)
        {
            Vector3 position = origin + Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            hero.ResetActor(position, Vector3.forward); enemy.ResetActor(position + direction * distance, -direction);
            Physics.SyncTransforms();
            // Reset restores the world actor's initially stowed loadout. Complete
            // the real draw before measuring the independent strike/hit clock.
            if (!hero.HasWeaponInHand) Assert.That(hero.TrySelectWeapon(CombatWeaponId.Crowbar), Is.True);
            for (int frame = 0; frame < 120 && (!hero.HasWeaponInHand || hero.IsSwitchingWeapon); frame++)
                yield return Frames(1);
            Assert.That(hero.HasWeaponInHand && !hero.IsSwitchingWeapon, Is.True);
            // The carry endpoint has its own continuous blend into Ready. A
            // strike entry is measured from settled Ready, never from draw.
            yield return Frames(24);
        }

        private IEnumerator EarlyStrikePresentation(bool walking)
        {
            // The front inspection camera sits ahead of the hero. Keep the
            // unrelated NPC well beyond it, including the walking approach.
            yield return PlacePair(12f);
            hero.SetContactTarget(null);
            string mode = walking ? "walking" : "idle";
            float earlyEnd = CombatAssetProvider.CrowbarShoulderLoadSeconds + .03f;
            if (walking)
            {
                input.Press(keyboard.wKey, queueEventOnly: true);
                yield return Frames(24);
                Assert.That(player.Motor.PlanarVelocity.magnitude, Is.GreaterThan(.5f));
            }
            StrikePose ready = ReadStrikePose();
            StrikePose rawReadyPose = ReadRawStrikePose(0f, true);
            StrikePose previousRaw = rawReadyPose;
            if (!walking) CaptureClosedHands("hands-ready");
            Capture("entry-" + mode + "-000-0000ms-front", true, new Vector3(.35f, .2f, 3f));
            Capture("entry-" + mode + "-000-0000ms-side", true, new Vector3(3f, .2f, .2f));
            int sequence = hero.State.AttackSequence;
            StrikePose previous = ready;
            bool started = false;
            int sample = 0, earlySamples = 0;
            float previousElapsed = 0f;
            bool capturedFinishHands = false, capturedReturnHands = false;
            input.Press(mouse.leftButton, queueEventOnly: true);
            for (int frame = 0; frame < 120; frame++)
            {
                yield return Frames(1);
                if (frame == 1) input.Release(mouse.leftButton, queueEventOnly: true);
                if (hero.State.AttackSequence == sequence) continue;
                Assert.That(hero.State.AttackSequence, Is.EqualTo(sequence + 1), "Holding the entry press cannot repeat a strike.");
                float elapsed = hero.State.AttackElapsed;
                if (!started)
                {
                    Assert.That(elapsed, Is.LessThan(.05f), "The rendered entry must include the beginning of the actual attack clock.");
                    started = true;
                }
                StrikePose posed = ReadStrikePose();
                StrikePose rawPosed = ReadRawStrikePose(elapsed);
                float chestTurn = Mathf.Abs(Vector3.SignedAngle(ready.ShoulderLine, posed.ShoulderLine, Vector3.up));
                float pelvisTurn = Mathf.Abs(Vector3.SignedAngle(ready.PelvisLine, posed.PelvisLine, Vector3.up));
                Assert.That(chestTurn, Is.LessThan(24f), mode + ": the stronger torso turn stays inside the authored arc.");
                Assert.That(pelvisTurn, Is.LessThan(11f), mode + ": the stance must not turn into a full-body rotational swing.");
                if (elapsed <= earlyEnd + CombatTestRoot.SimulationStep)
                {
                    earlySamples++;
                    Assert.That(Vector3.Distance(previous.RightShoulder, posed.RightShoulder), Is.LessThan(.06f),
                        mode + ": the final rendered right shoulder must not jump at entry.");
                    Assert.That(Vector3.Distance(previous.LeftShoulder, posed.LeftShoulder), Is.LessThan(.06f));
                    Assert.That(Quaternion.Angle(previous.RightUpperArm, posed.RightUpperArm), Is.LessThan(20f),
                        mode + ": the right upper arm cannot twist suddenly around a stationary elbow.");
                    Assert.That(Quaternion.Angle(previous.LeftUpperArm, posed.LeftUpperArm), Is.LessThan(20f));
                    Assert.That(Vector3.Distance(previous.RightElbow, posed.RightElbow), Is.LessThan(.10f),
                        mode + ": the elbow must travel continuously from the visible Ready pose.");
                    Assert.That(Vector3.Distance(previous.LeftElbow, posed.LeftElbow), Is.LessThan(.10f));
                    Assert.That(posed.LeftElbow.y - posed.LeftShoulder.y, Is.LessThan(.08f),
                        mode + ": the supporting elbow stays naturally below the shoulder.");
                    Assert.That(posed.RightGripError, Is.LessThan(.006f));
                    Assert.That(posed.LeftGripError, Is.LessThan(.025f), mode + ": the left palm cannot detach during the early transition.");
                }
                if (previousElapsed > hero.State.Settings.WindupSeconds + hero.State.Settings.ActiveSeconds + .02f)
                {
                    Assert.That(Quaternion.Angle(previous.RightUpperArm, posed.RightUpperArm), Is.LessThan(20f),
                        mode + ": the right shoulder must return continuously after the damaging arc.");
                    Assert.That(Quaternion.Angle(previous.LeftUpperArm, posed.LeftUpperArm), Is.LessThan(20f));
                    Assert.That(posed.RightElbow.y - posed.RightShoulder.y, Is.LessThanOrEqualTo(.04f),
                        mode + ": the returning right elbow must not fold upward into the neck.");
                    Assert.That(posed.LeftElbow.y - posed.LeftShoulder.y, Is.LessThanOrEqualTo(.04f));
                    Assert.That(posed.RightGripError, Is.LessThan(.006f));
                    Assert.That(posed.LeftGripError, Is.LessThan(.025f));
                }
                TestContext.Out.WriteLine($"Rendered entry,{mode},{elapsed:F4},torso={chestTurn:F2},pelvis={pelvisTurn:F2}," +
                    $"shoulderR={posed.RightShoulder:F4},shoulderL={posed.LeftShoulder:F4}," +
                    $"elbowR={posed.RightElbow:F4},elbowL={posed.LeftElbow:F4}," +
                    $"armStepR={Quaternion.Angle(previous.RightUpperArm, posed.RightUpperArm):F2}," +
                    $"armStepL={Quaternion.Angle(previous.LeftUpperArm, posed.LeftUpperArm):F2}," +
                    $"rawArmStepR={Quaternion.Angle(previousRaw.RightUpperArm, rawPosed.RightUpperArm):F2}," +
                    $"rawArmStepL={Quaternion.Angle(previousRaw.LeftUpperArm, rawPosed.LeftUpperArm):F2}," +
                    $"rawTorso={Mathf.Abs(Vector3.SignedAngle(rawReadyPose.ShoulderLine, rawPosed.ShoulderLine, Vector3.up)):F2}," +
                    $"gripR={posed.RightGripError:F4},gripL={posed.LeftGripError:F4}");
                // Full-speed front sequence at 30 Hz, with a simultaneous side
                // sequence through the shoulder load. Filenames carry true clocks.
                if (sample % 2 == 0 || !hero.State.IsAttacking)
                {
                    string stem = "entry-" + mode + "-" + (sample + 1).ToString("D3") + "-" +
                        Mathf.RoundToInt(elapsed * 1000f).ToString("D4") + "ms";
                    Capture(stem + "-front", true, new Vector3(.35f, .2f, 3f));
                    if (elapsed <= earlyEnd + CombatTestRoot.SimulationStep)
                        Capture(stem + "-side", true, new Vector3(3f, .2f, .2f));
                }
                if (!walking && !capturedFinishHands && elapsed >= hero.State.Settings.WindupSeconds + hero.State.Settings.ActiveSeconds + .01f)
                {
                    CaptureClosedHands("hands-finish-" + Mathf.RoundToInt(elapsed * 1000f) + "ms");
                    capturedFinishHands = true;
                }
                if (!walking && !capturedReturnHands && elapsed >= hero.State.Settings.WindupSeconds +
                    hero.State.Settings.ActiveSeconds + hero.State.Settings.AnimationRecoverySeconds * .65f)
                {
                    CaptureClosedHands("hands-return-" + Mathf.RoundToInt(elapsed * 1000f) + "ms");
                    capturedReturnHands = true;
                }
                previous = posed; previousRaw = rawPosed; previousElapsed = elapsed; sample++;
                if (!hero.State.IsAttacking) break;
            }
            Assert.That(started && hero.State.Phase == MeleePhase.Ready, Is.True, mode + ": the whole strike must return to Ready.");
            Assert.That(earlySamples, Is.GreaterThanOrEqualTo(7), mode + ": inspect actual early frames rather than only the loaded pose.");
            Assert.That(previousElapsed, Is.GreaterThan(hero.State.Settings.WindupSeconds + hero.State.Settings.ActiveSeconds));
            if (!walking) Assert.That(capturedFinishHands && capturedReturnHands, Is.True);
            if (walking) input.Release(keyboard.wKey, queueEventOnly: true);
            yield return Frames(24);
        }

        private readonly struct StrikePose
        {
            internal readonly Vector3 RightShoulder, LeftShoulder, RightElbow, LeftElbow, ShoulderLine, PelvisLine;
            internal readonly Quaternion RightUpperArm, LeftUpperArm;
            internal readonly float RightGripError, LeftGripError;
            internal StrikePose(Transform frame, Transform rightShoulder, Transform leftShoulder,
                Transform rightElbow, Transform leftElbow, Transform chest, Transform pelvis, float rightGripError, float leftGripError)
            {
                RightShoulder = frame.InverseTransformPoint(rightShoulder.position);
                LeftShoulder = frame.InverseTransformPoint(leftShoulder.position);
                RightElbow = frame.InverseTransformPoint(rightElbow.position);
                LeftElbow = frame.InverseTransformPoint(leftElbow.position);
                ShoulderLine = Vector3.ProjectOnPlane(RightShoulder - LeftShoulder, Vector3.up).normalized;
                PelvisLine = Vector3.ProjectOnPlane(frame.InverseTransformDirection(pelvis.right), Vector3.up).normalized;
                RightUpperArm = Quaternion.Inverse(chest.rotation) * rightShoulder.rotation;
                LeftUpperArm = Quaternion.Inverse(chest.rotation) * leftShoulder.rotation;
                RightGripError = rightGripError; LeftGripError = leftGripError;
            }
        }

        private StrikePose ReadStrikePose()
        {
            presentation.ReapplyLatePresentationPose();
            NpcHandPose hands = GetHeldHands();
            AssertOrderedGrip(hands);
            Transform rig = hero.DamageRigRoot;
            return new StrikePose(hero.transform,
                CityPedestrianHandProps.FindSocket(rig, "upper_arm.R"), CityPedestrianHandProps.FindSocket(rig, "upper_arm.L"),
                CityPedestrianHandProps.FindSocket(rig, "forearm.R"), CityPedestrianHandProps.FindSocket(rig, "forearm.L"),
                CityPedestrianHandProps.FindSocket(rig, "chest"),
                CityPedestrianHandProps.FindSocket(rig, "pelvis"),
                Vector3.Distance(hands.CylinderCentre(false), hero.Weapon.transform.TransformPoint(Vector3.up * hero.CrowbarGripStation)),
                Vector3.Distance(hands.CylinderCentre(true), hero.Weapon.transform.TransformPoint(Vector3.up * CombatSupportGrip.ReadyDistance)));
        }

        private StrikePose ReadRawStrikePose(float elapsed, bool ready = false)
        {
            if (rawRegistry == null)
            {
                // A hidden production-prefab twin samples the imported clip
                // without taking any presentation ownership from the live hero.
                rawFrame = new GameObject("Raw imported strike reference").transform;
                rawFrame.SetParent(fixture.transform, false);
                rawFrame.position = origin + new Vector3(100f, 0f, 100f);
                GameObject model = Object.Instantiate(Player3DResources.LoadPrefab(), rawFrame, false);
                rawRegistry = model.GetComponent<Player3DAssetRegistry>();
                rawRegistry.Animator.enabled = false;
                foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                    renderer.forceRenderingOff = true;
                }
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                rawReady = CombatAssetProvider.LoadClip(CombatAssetProvider.ReadyClip);
                rawStrike = CombatAssetProvider.LoadClip(CombatAssetProvider.AttackClip);
            }
            (ready ? rawReady : rawStrike).SampleAnimation(rawRegistry.Animator.gameObject, elapsed);
            Transform rig = rawRegistry.ModelRoot;
            return new StrikePose(rawFrame,
                CityPedestrianHandProps.FindSocket(rig, "upper_arm.R"), CityPedestrianHandProps.FindSocket(rig, "upper_arm.L"),
                CityPedestrianHandProps.FindSocket(rig, "forearm.R"), CityPedestrianHandProps.FindSocket(rig, "forearm.L"),
                CityPedestrianHandProps.FindSocket(rig, "chest"), CityPedestrianHandProps.FindSocket(rig, "pelvis"), 0f, 0f);
        }

        private sealed class FingerClosureProbe
        {
            internal SkinnedMeshRenderer Renderer;
            internal Transform Hand;
            internal Vector3[] Open, Closed;
        }

        private Vector3[] BakeHandLocal(SkinnedMeshRenderer renderer, Transform hand)
        {
            handBake ??= new Mesh();
            renderer.BakeMesh(handBake, true);
            Vector3[] points = handBake.vertices;
            for (int vertex = 0; vertex < points.Length; vertex++)
                points[vertex] = hand.InverseTransformPoint(renderer.transform.TransformPoint(points[vertex]));
            return points;
        }

        private void CaptureClosedHands(string name)
        {
            presentation.ReapplyLatePresentationPose();
            NpcHandPose hands = GetHeldHands();
            AssertOrderedGrip(hands);
            if (fingerProbes == null)
            {
                // Open/closed geometry comes from a hidden copy of the same
                // prefab; no live grip or presentation owner is changed.
                NpcHandPose reference = rawRegistry.GetComponentInChildren<NpcHandPose>(true);
                if (reference == null) reference = rawRegistry.GetComponentInParent<NpcHandPose>();
                fingerProbes = new List<FingerClosureProbe>(10);
                foreach (NpcHandPose.HandBinding hand in hands.Hands)
                {
                    NpcHandPose.HandBinding rawHand = null;
                    foreach (NpcHandPose.HandBinding candidate in reference.Hands)
                        if (candidate.IsLeft == hand.IsLeft) rawHand = candidate;
                    Assert.That(rawHand, Is.Not.Null);
                    foreach (SkinnedMeshRenderer renderer in hand.Renderers)
                    {
                        if (!renderer.name.Contains("Finger") && !renderer.name.Contains("Thumb")) continue;
                        SkinnedMeshRenderer rawRenderer = null;
                        foreach (SkinnedMeshRenderer candidate in rawHand.Renderers)
                            if (candidate.name == renderer.name) rawRenderer = candidate;
                        Assert.That(rawRenderer, Is.Not.Null);
                        reference.SetGrip(hand.IsLeft, 0f);
                        Vector3[] open = BakeHandLocal(rawRenderer, rawHand.Hand);
                        reference.SetGrip(hand.IsLeft, 1f);
                        Vector3[] closed = BakeHandLocal(rawRenderer, rawHand.Hand);
                        fingerProbes.Add(new FingerClosureProbe { Renderer = renderer, Hand = hand.Hand, Open = open, Closed = closed });
                    }
                }
                Assert.That(fingerProbes.Count, Is.EqualTo(10), "Four fingers and a thumb must visibly close on each hand.");
            }
            foreach (FingerClosureProbe probe in fingerProbes)
            {
                Vector3[] actual = BakeHandLocal(probe.Renderer, probe.Hand);
                Assert.That(actual.Length, Is.EqualTo(probe.Closed.Length));
                float openTravel = 0f, closedError = 0f;
                for (int vertex = 0; vertex < actual.Length; vertex++)
                {
                    openTravel += probe.Hand.TransformVector(probe.Open[vertex] - probe.Closed[vertex]).sqrMagnitude;
                    closedError += probe.Hand.TransformVector(actual[vertex] - probe.Closed[vertex]).sqrMagnitude;
                }
                openTravel = Mathf.Sqrt(openTravel / actual.Length);
                closedError = Mathf.Sqrt(closedError / actual.Length);
                Assert.That(openTravel, Is.GreaterThan(.003f), probe.Renderer.name + ": the authored grip must bend the actual digit geometry.");
                Assert.That(closedError, Is.LessThan(Mathf.Max(.0005f, openTravel * .15f)),
                    name + ": " + probe.Renderer.name + " must visibly squeeze the bar, rather than retain its open mesh.");
            }
            CaptureHandViews(name, hands);
        }

        private void CaptureHandViews(string name, NpcHandPose hands)
        {
            Vector3 focus = (hands.CylinderCentre(false) + hands.CylinderCentre(true)) * .5f;
            Capture(name + "-front", true, new Vector3(.6f, .2f, 1f), focus, .20f);
            Capture(name + "-side", true, new Vector3(1f, .25f, -.3f), focus, .20f);
        }

        private NpcHandPose GetHeldHands()
        {
            if (heldHands != null) return heldHands;
            heldHands = presentation.Registry.GetComponentInChildren<NpcHandPose>(true);
            if (heldHands == null) heldHands = presentation.Registry.GetComponentInParent<NpcHandPose>();
            Assert.That(heldHands, Is.Not.Null);
            foreach (NpcHandPose.HandBinding hand in heldHands.Hands)
                Assert.That(hand.Hand.name, Does.EndWith(hand.IsLeft ? ".L" : ".R"),
                    "The grip test follows anatomical sides on the production rig.");
            return heldHands;
        }

        private void AssertOrderedGrip(NpcHandPose hands)
        {
            if (orderedWeapon != hero.Weapon)
            {
                orderedWeapon = hero.Weapon;
                orderedGrip = CombatAssetProvider.FindAnchor(orderedWeapon, "Grip");
                orderedTip = CombatAssetProvider.FindAnchor(orderedWeapon, "StrikeTip");
                Vector3 physicalAxis = (orderedTip.position - orderedGrip.position).normalized;
                orderedButtStation = float.PositiveInfinity;
                foreach (MeshFilter mesh in orderedWeapon.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.sharedMesh != null)
                        foreach (Vector3 point in mesh.sharedMesh.vertices)
                            orderedButtStation = Mathf.Min(orderedButtStation,
                                Vector3.Dot(mesh.transform.TransformPoint(point) - orderedGrip.position, physicalAxis));
                Assert.That(float.IsFinite(orderedButtStation), Is.True, "Measure the physical metal end from the rendered weapon.");
            }
            Vector3 towardTip = (orderedTip.position - orderedGrip.position).normalized;
            float leftStation = Vector3.Dot(hands.CylinderCentre(true) - hands.CylinderCentre(false), towardTip);
            string context = $"phase={hero.State.Phase}, outcome={hero.State.AttackOutcome}, clock={hero.State.AttackElapsed:F3}, " +
                $"support={hero.SupportArmState}, weight={hero.SupportGripWeight:F3}, error={hero.SupportGrip?.JournalContactError:F4}";
            Assert.That(leftStation, Is.LessThan(-.05f),
                "The anatomical left hand stays nearer the butt than the right by at least half a hand's width. " + context);
            float leftFromButt = Vector3.Dot(hands.CylinderCentre(true) - orderedGrip.position, towardTip) - orderedButtStation;
            Assert.That(leftFromButt, Is.GreaterThanOrEqualTo(.10f),
                "Both hands must sit visibly higher on the unchanged metal, leaving shaft below the lower left fist. " + context);
        }

        private IEnumerator Frames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                enemy.Step(FrameSeconds);
                flankEnemy?.Step(FrameSeconds);
                yield return null;
                if (hero.HasWeaponInHand && hero.State.IsAttacking)
                {
                    presentation.ReapplyLatePresentationPose();
                    CombatSupportGrip support = hero.SupportGrip;
                    if (support != null && support.IsSupportingWeapon && !support.IsSupportingGuard)
                        AssertOrderedGrip(GetHeldHands());
                    else if (lastUnsettledSequence != hero.State.AttackSequence || lastUnsettledOutcome != hero.State.AttackOutcome ||
                        lastUnsettledSupport != hero.SupportArmState)
                    {
                        TestContext.Out.WriteLine($"Unsettled attack contact: phase={hero.State.Phase}, outcome={hero.State.AttackOutcome}, " +
                            $"clock={hero.State.AttackElapsed:F3}, support={hero.SupportArmState}, " +
                            $"weight={hero.SupportGripWeight:F3}, error={support?.JournalContactError:F4}");
                        lastUnsettledSequence = hero.State.AttackSequence;
                        lastUnsettledOutcome = hero.State.AttackOutcome;
                        lastUnsettledSupport = hero.SupportArmState;
                    }
                }
                Assert.That(hero.IsKnockedDown || hero.IsRagdollActive, Is.False,
                    "Ordinary zombie hits must keep the hero standing; accumulated crowbar hits may knock a zombie down.");
            }
        }

        private void ApplyOrdinaryHit()
        {
            Transform chest = presentation.Registry.Anchors.Chest;
            Vector3 direction = -hero.transform.forward;
            new CombatActor.Contact(enemy, hero, chest.position, -direction, direction,
                new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front), Player3DAnatomicalPart.Torso,
                Vector3.zero, physicalBody: true, bodyRegion: BodyDamageRegion.Chest).Apply();
        }

        private void Capture(string name, bool heroView = false, Vector3? view = null,
            Vector3? focusOverride = null, float? sizeOverride = null)
        {
            presentation.ReapplyLatePresentationPose();
            foreach (Transform child in fixture.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = CaptureLayer;
            Transform subject = heroView ? hero.transform : enemy.transform;
            Vector3 focus = focusOverride ?? subject.position + Vector3.up * (heroView ? 1.05f : .95f);
            Vector3 eye = focus + (view.HasValue ? subject.TransformDirection(view.Value) :
                subject.right * 2.5f + subject.forward * 1.6f + Vector3.up * .3f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
            camera.orthographic = true; camera.orthographicSize = sizeOverride ?? (heroView ? 1.4f : 1.2f);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "OrdinaryCombat", name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(camera, "OrdinaryCombat", name, 640, 640);
        }
    }
}
