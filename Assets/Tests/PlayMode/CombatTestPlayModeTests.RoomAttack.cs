using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatRoomAttackAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            bool existing = Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatExistingAssets");
            Type.GetType("BarPromenade.Editor.CombatRoomAttackAssetSetup, BarPromenade.Editor", true)
                .GetMethod(existing ? "ValidateImportedOrThrow" : "BuildOrThrow", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, null);
#endif
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(CombatRoomAssetsSetup))]
        public IEnumerator Range_RoomAttackKeepsShotsDamageLaughPauseAndCleanup()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            CombatTestRoot.AuthoredEnemyKindForTests = CombatEnemyKind.Room;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var settings = new CombatEncounterSettings(3, .1f, .1f, .15f, .15f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, settings)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.AutomaticHudPresentation = false;
            root.SendMessage("OnApplicationFocus", true);
            CombatActor room = root.Opponents[0];
            Assert.That(room.IsRoom, Is.True);
            PlaceRoomAttackTestPair(room);
            CombatActor waitingDocker = root.Opponents[1], waitingCrane = root.Opponents[2];
            Assert.That(waitingDocker.TryZombieAttack(), Is.True);
            Assert.That(waitingDocker.State.Phase, Is.EqualTo(MeleePhase.Windup),
                "A real pending Docker strike must yield when the Room takes the shot.");
            Vector3 heroRoot = root.Hero.transform.position;
            float ordinaryNearClip = root.CameraFollow.Camera.nearClipPlane;
            root.CameraFollow.RotateYaw(18f); // Leave an orbit target pending when the camera is borrowed.
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.PointHero, "Finding the pointing hand");
            Assert.That(root.RoomAttackCinematicActive, Is.True);
            Assert.That(root.RoomAttackTableauVisible, Is.True, "The seated hero is already present inside the real chest opening.");
            Assert.That(root.Player.Motor.InputEnabled, Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
            float pointingStartFov = root.CameraFollow.Camera.fieldOfView;
            float pointingStartRoll = Mathf.Abs(RoomTestCameraRoll(root.CameraFollow.Camera));
            yield return AtCombatPresentation(() => AssertRoomPointShotComposition(room));

            var opponents = new Vector3[root.Opponents.Count];
            for (int i = 0; i < opponents.Length; i++)
                opponents[i] = root.Opponents[i].transform.position;
            Quaternion idleDockerHead = FindAnatomicalBone(waitingDocker, "head").localRotation;
            float idleCraneClock = waitingCrane.CranePhaseElapsed;
            // A slow flight above the hall avoids incidental geometry contacts;
            // its normal lifetime remains shorter than the camera sequence.
            Assert.That(root.Projectiles.TrySpawn(root.Hero, heroRoot + Vector3.up * 100f,
                Vector3.right * 4f, 1), Is.True);
            Vector3 bullet = root.Projectiles.LastPosition;
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.PointHero, "The threatening finger", .75f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.PointHero));
            Transform pointingHand = FindAnatomicalBone(room, "hand.R");
            Vector3 towardHero = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Head.position - pointingHand.position;
            Assert.That(Vector3.Dot(pointingHand.TransformDirection(Vector3.up).normalized, towardHero.normalized), Is.GreaterThan(.65f),
                "The pointing finger is directed at the actual hero.");
            AssertRoomTestLiveIdle(room, heroRoot, opponents);
            Assert.That(Vector3.Distance(root.Projectiles.LastPosition, bullet), Is.GreaterThan(1f),
                "An existing projectile keeps its ordinary ballistic simulation during the camera loan.");
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Assert.That(waitingCrane.CranePhaseElapsed, Is.GreaterThan(idleCraneClock + .5f),
                "The yielding crane's real idle clock continues instead of freezing its presentation.");
            yield return AtCombatPresentation(() =>
            {
                AssertRoomPointShotComposition(room);
                AssertRoomHeroHeadHeld();
                Assert.That(Quaternion.Angle(idleDockerHead, FindAnatomicalBone(waitingDocker, "head").localRotation), Is.GreaterThan(.1f),
                    "A yielding Docker samples a moving idle rather than holding its cancelled attack frame.");
                Assert.That(root.CameraFollow.Camera.fieldOfView, Is.GreaterThan(pointingStartFov + 5f),
                    "The held gesture expands the wide lens instead of freezing the approach lens.");
                Assert.That(Mathf.Abs(RoomTestCameraRoll(root.CameraFollow.Camera)), Is.GreaterThan(pointingStartRoll + 4f),
                    "The Dutch angle grows with the pointing threat.");
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Pointing at hero");
            });

            float held = root.RoomAttackPhaseElapsed;
            Pose heldCamera = new Pose(root.CameraFollow.Camera.transform.position, root.CameraFollow.Camera.transform.rotation);
            float heldFov = root.CameraFollow.Camera.fieldOfView;
            Vector3 pausedBullet = root.Projectiles.LastPosition;
            Quaternion pausedDockerHead = FindAnatomicalBone(waitingDocker, "head").localRotation;
            float pausedCraneClock = waitingCrane.CranePhaseElapsed;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(1f);
            yield return null;
            Assert.That(root.RoomAttackPhaseElapsed, Is.EqualTo(held));
            Assert.That(root.CameraFollow.Camera.transform.position, Is.EqualTo(heldCamera.position));
            Assert.That(Quaternion.Angle(root.CameraFollow.Camera.transform.rotation, heldCamera.rotation), Is.LessThan(.01f));
            Assert.That(root.CameraFollow.Camera.fieldOfView, Is.EqualTo(heldFov).Within(.0001f),
                "Pause freezes the lens as well as the Dutch camera pose.");
            Assert.That(root.Projectiles.LastPosition, Is.EqualTo(pausedBullet), "Actual pause freezes the live projectile.");
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Assert.That(waitingCrane.CranePhaseElapsed, Is.EqualTo(pausedCraneClock));
            Assert.That(Quaternion.Angle(pausedDockerHead, FindAnatomicalBone(waitingDocker, "head").localRotation), Is.LessThan(.01f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && !GameTimeScaleRuntime.IsPaused,
                "Pause did not release its overlay on the Room shot.");
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True, "Closing pause preserves the attack's input loan.");
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.PointHero, "The final pointing threat", .95f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.PointHero),
                "The one-second pointing hold is separate from the preceding camera approach.");
            yield return AtCombatPresentation(() => AssertRoomPointShotComposition(room));
            root.Tick(.06f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.PointChest));
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.SeatedHero, "Entering the room");
            Assert.That(root.Projectiles.ActiveCount, Is.Zero,
                "The projectile expires on its ordinary lifetime while the attack camera continues.");
            Assert.That(root.RoomAttackTableauVisible, Is.True);
            Player3DAssetRegistry image = root.RoomAttackHeroImage;
            Assert.That(image, Is.Not.Null);
            Assert.That(image.GetComponent<CombatActor>(), Is.Null, "The seated hero is a visual of the production model.");
            foreach (Collider collider in image.GetComponentsInChildren<Collider>(true)) Assert.That(collider.enabled, Is.False);
            MeshRenderer corridor = null, chair = null;
            foreach (MeshRenderer surface in image.transform.parent.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (surface.name == "GEO_RoomAttackOriginalCorridor") corridor = surface;
                if (surface.name == "GEO_RoomAttackWoodChair") chair = surface;
            }
            Assert.That(corridor, Is.Not.Null, "The insert retains the original chest corridor.");
            Assert.That(corridor.enabled, Is.False, "The insert must not draw overlapping corridor walls.");
            Assert.That(corridor.sharedMaterial, Is.SameAs(CombatRoomAssetProvider.LoadBodyMaterial("plaster")));
            SkinnedMeshRenderer originalCorridor = null;
            foreach (SkinnedMeshRenderer surface in room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (surface.name == "GEO_Torso") originalCorridor = surface;
            Assert.That(originalCorridor, Is.Not.Null, "The camera enters the NPC's actual authored torso corridor.");
            Assert.That(originalCorridor.enabled, Is.True, "The original corridor remains visible throughout the camera journey.");
            Assert.That(chair, Is.Not.Null);
            Assert.That(chair.sharedMaterials, Has.Length.EqualTo(1), "The authored chair has a drawable material slot.");
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "The room insert cannot apply damage early.");
            AssertRoomTestSeatedFootContact(image, originalCorridor);
            root.Tick(.45f);
            yield return AtCombatPresentation(() => AssertRoomTestSeatedFootContact(image, originalCorridor));
            root.Tick(.45f);
            yield return AtCombatPresentation(() =>
            {
                AssertRoomTestSeatedFootContact(image, originalCorridor);
                AssertRoomTestInFrame(image.Anchors.Head.position);
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Seated hero");
            });
            float interiorNearClip = root.CameraFollow.Camera.nearClipPlane;
            Assert.That(interiorNearClip, Is.LessThan(ordinaryNearClip), "The lens can see the actual miniature interior without clipping it.");
            float seatedHeld = root.RoomAttackPhaseElapsed;
            Pose seatedCamera = new Pose(root.CameraFollow.Camera.transform.position, root.CameraFollow.Camera.transform.rotation);
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.5f);
            yield return null;
            Assert.That(root.RoomAttackPhaseElapsed, Is.EqualTo(seatedHeld));
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(interiorNearClip));
            Assert.That(root.CameraFollow.Camera.transform.position, Is.EqualTo(seatedCamera.position));
            Assert.That(Quaternion.Angle(root.CameraFollow.Camera.transform.rotation, seatedCamera.rotation), Is.LessThan(.01f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && !GameTimeScaleRuntime.IsPaused,
                "Pause did not release its overlay inside the Room interior.");
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(interiorNearClip));
            root.Tick(.5f);
            yield return AtCombatPresentation(() => AssertRoomTestSeatedFootContact(image, originalCorridor));
            root.Tick(.55f);
            yield return AtCombatPresentation(() => AssertRoomTestSeatedFootContact(image, originalCorridor));
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.SeatedHero));
            AssertRoomTestLiveIdle(room, heroRoot, opponents);
            root.Tick(.06f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.ReturnRoom));
            Assert.That(root.RoomAttackTableauVisible, Is.True, "The hero stays inside the real chest during the reverse journey.");
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.HeadPain, "Returning to the hero");
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth - 18f).Within(.001f));
            Assert.That(root.Hero.LastImpact.Source, Is.SameAs(room));
            Assert.That(root.Hero.LastImpact.Impulse, Is.EqualTo(Vector3.zero));
            Assert.That(root.Hero.LastImpact.WoundDamage, Is.Zero);
            Assert.That(root.BloodEffects.EmissionCount, Is.Zero);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(ordinaryNearClip).Within(.00001f),
                "The miniature interior lens must release its near plane with the camera.");
            Assert.That(root.Hero.RoomHeadPainActive, Is.True);
            Assert.That(room.RoomLaughRecoveryActive, Is.True,
                "The monster starts laughing as soon as the camera returns, over the hero's head pain.");
            Assert.That(room.ActiveClipName, Is.EqualTo(CombatRoomAttackAssetProvider.LaughClip));
            AudioSource laugh = RoomTestLaughSource(room);
            root.AdvanceHudPresentation(.2f);
            Assert.That(root.HealthHudOpacity, Is.Zero, "The health event stays deferred while the pain action owns input.");
            root.Tick(.9f);
            yield return AtCombatPresentation(() =>
            {
                var hero = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
                Assert.That(Vector3.Distance(hero.Anchors.LeftGrip.position, hero.Anchors.Head.position), Is.LessThan(.4f),
                    "The actual left hand contacts the head during the pain action.");
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Left hand on head");
            });
            float overlappingLaugh = root.RoomLaughElapsed;
            Quaternion laughingHead = FindAnatomicalBone(room, "head").rotation;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.5f);
            Assert.That(root.RoomLaughElapsed, Is.EqualTo(overlappingLaugh), "Pause freezes the overlapping laugh clock too.");
            Assert.That(FindAnatomicalBone(room, "head").rotation, Is.EqualTo(laughingHead));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && !GameTimeScaleRuntime.IsPaused,
                "Pause did not release the overlapping Room laugh.");
            root.Tick(1.05f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.HeadPain));
            AssertRoomTestLiveIdle(room, heroRoot, opponents);
            root.Tick(.06f);
            yield return null; // The terminal pain sample must reach LateUpdate before input is returned.
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Laugh));
            AssertRoomTestRestored(ordinaryNearClip);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(room.RoomLaughRecoveryActive, Is.True);
            Assert.That(root.Hero.RoomHeadPainActive, Is.False);
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready), "Visible head pain consumes the hit recovery before control returns.");
            Assert.That(root.RoomLaughElapsed, Is.GreaterThanOrEqualTo(2f), "Releasing hero input must not restart the laugh.");
            Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth - 18f).Within(.001f));
            root.AdvanceHudPresentation(.2f);
            Assert.That(root.HealthHudOpacity, Is.EqualTo(1f).Within(.001f),
                "The real health loss remains visible after the modal camera and pain loan.");
            root.AdvanceHudPresentation(2.5f);
            Assert.That(root.HealthHudOpacity, Is.EqualTo(1f).Within(.001f), "The deferred damage gets the ordinary readable hold.");
            Assert.That(laugh.clip.length, Is.EqualTo(5f).Within(.02f));
            root.SendMessage("OnApplicationFocus", false);
            float laughHeld = root.RoomAttackPhaseElapsed;
            root.Tick(.5f);
            Assert.That(root.RoomAttackPhaseElapsed, Is.EqualTo(laughHeld));
            root.SendMessage("OnApplicationFocus", true);
            root.Tick(.8f);
            yield return AtCombatPresentation(() => CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Laughing recovery"));
            root.Hero.ResetActor(heroRoot + Vector3.back * 14f, Vector3.forward);
            Vector3 laughingRoot = room.transform.position;
            root.Tick(2.1f); // The first two seconds of laughter already overlapped the pain action.
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Laugh));
            Assert.That(Vector3.Distance(room.transform.position, laughingRoot), Is.LessThan(.001f));
            root.Tick(.6f); // A hitch crossing the laugh end must preserve the whole new wait.
            Assert.That(room.RoomLaughRecoveryActive, Is.False);
            Assert.That(laugh.isPlaying, Is.False);
            Assert.That(room.RoomAttackCooldownRemaining, Is.EqualTo(5f).Within(.00001f),
                "The frame that ends laughter cannot also spend the new cooldown.");
            root.Hero.ResetActor(heroRoot, Vector3.forward);
            HoldOtherRoomTestOpponentsAway(room);
            root.CameraFollow.Snap();
            root.Tick(.2f);
            yield return AtCombatPresentation(() =>
            {
                Assert.That(room.ActiveClipName, Is.EqualTo(CombatRoomAttackAssetProvider.IdleClip));
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Waiting after laugh");
            });
            float cooldownHeld = room.RoomAttackCooldownRemaining;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(10f);
            Assert.That(room.RoomAttackCooldownRemaining, Is.EqualTo(cooldownHeld));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && !GameTimeScaleRuntime.IsPaused,
                "Pause did not release the Room cooldown.");
            root.SendMessage("OnApplicationFocus", false);
            root.Tick(10f);
            Assert.That(room.RoomAttackCooldownRemaining, Is.EqualTo(cooldownHeld));
            root.SendMessage("OnApplicationFocus", true);
            Quaternion idleHead = FindAnatomicalBone(room, "head").localRotation;
            for (int sample = 0; sample < 4; sample++)
            {
                root.Tick(1f);
                Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Idle));
                Assert.That(Vector3.Distance(room.transform.position, laughingRoot), Is.LessThan(.001f));
                HoldOtherRoomTestOpponentsAway(room);
                yield return AtCombatPresentation(() => CaptureMixedCameraFrame(root.CameraFollow.Camera,
                    "Test Room attack - Uneasy idle - Pose " + (sample + 1)));
            }
            Assert.That(Quaternion.Angle(idleHead, FindAnatomicalBone(room, "head").localRotation), Is.GreaterThan(1f),
                "The planted wait presents the authored uneasy idle rather than a frozen pose.");
            root.Tick(.79f);
            Assert.That(room.RoomAttackCooldownRemaining, Is.GreaterThan(0f));
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Idle),
                "Even a nearby facing hero cannot be attacked before five full seconds.");
            Assert.That(Vector3.Distance(room.transform.position, laughingRoot), Is.LessThan(.001f));
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.PointHero);
            Assert.That(room.RoomAttackCooldownRemaining, Is.Zero);

            PlaceRoomAttackTestPair(room);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.Laugh);
            CombatHurtboxes.Hit chestHit = AuthoredSurfaceContact(room, CombatRoomAssetProvider.Bank, "GEO_Torso");
            room.ReceiveProjectile(root.Hero, 2, chestHit, -chestHit.Normal * CombatProjectilePool.MuzzleSpeed);
            Assert.That(room.RoomAttackCooldownRemaining, Is.EqualTo(5f), "Interrupting laughter starts the same full wait.");
            root.Tick(.05f);
            Assert.That(room.RoomAttackPoseActive, Is.False, "A new hit interrupts the exposed laughing enemy.");
            Assert.That(laugh.isPlaying, Is.False);
            AssertRoomTestRestored(ordinaryNearClip);
            float afterHit = room.RoomAttackCooldownRemaining;
            room.ReceiveProjectile(root.Hero, 3, chestHit, Vector3.zero);
            Assert.That(room.RoomAttackCooldownRemaining, Is.EqualTo(afterHit), "Later hits do not restart the wait.");
            root.enabled = false;
            Assert.That(room.RoomAttackCooldownRemaining, Is.Zero, "Disabling the encounter clears actor cooldown ownership.");
            root.enabled = true;
            root.AdvanceHudPresentation(.2f);
            Assert.That(root.HealthHudOpacity, Is.Zero, "Disable clears a pending health reveal from the completed attack.");

            PlaceRoomAttackTestPair(room);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.HeadPain);
            root.ResetRound();
            root.AdvanceHudPresentation(.2f);
            Assert.That(root.HealthHudOpacity, Is.Zero, "Reset clears the deferred health event.");
            Assert.That(root.Hero.RoomHeadPainActive, Is.False);
            Assert.That(laugh.isPlaying, Is.False, "Reset stops the overlapping laugh as well as hero pain.");
            Assert.That(room.RoomAttackCooldownRemaining, Is.Zero);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Idle));
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            Assert.That(room.RoomAttackPoseActive, Is.False);
            AssertRoomTestRestored(ordinaryNearClip);
            PlaceRoomAttackTestPair(room);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.PointHero);
            root.enabled = false;
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Idle));
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            Assert.That(room.RoomAttackPoseActive, Is.False);
            AssertRoomTestRestored(ordinaryNearClip);
            root.enabled = true;

            PlaceRoomAttackTestPair(room);
            root.Hero.State.ReceiveUnarmedHit(root.Hero.State.Settings.MaxHealth - 9f, 0f, false);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.SeatedHero);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.Idle);
            Assert.That(root.Hero.State.IsDefeated, Is.True, "A lethal Room hit enters the ordinary defeat lifecycle.");
            Assert.That(root.Hero.RoomHeadPainActive, Is.False);
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            AssertRoomTestRestored(ordinaryNearClip);
            LogAssert.NoUnexpectedReceived();
        }

        private void PlaceRoomAttackTestPair(CombatActor room)
        {
            root.ResetRound();
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(ground, Vector3.forward);
            room.ResetActor(ground + Vector3.forward * (1.25f * room.PresentationScale + room.AdditionalArmReach), Vector3.back);
            foreach (CombatActor actor in root.Opponents)
                if (actor != room && !actor.IsCrane) actor.ResetActor(ground + Vector3.right * 12f, Vector3.left);
            root.CameraFollow.Snap();
            Physics.SyncTransforms();
        }

        private void HoldOtherRoomTestOpponentsAway(CombatActor room)
        {
            foreach (CombatActor actor in root.Opponents)
                if (actor != room && !actor.IsCrane)
                    actor.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset + Vector3.right * 12f, Vector3.left);
            Physics.SyncTransforms();
        }

        private IEnumerator AdvanceRoomTestToPhase(CombatRoomSequencePhase phase)
        {
            for (int step = 0; step < 500 && root.RoomAttackPhase != phase; step++)
            {
                root.Tick(.05f);
                if ((root.RoomAttackPhase == CombatRoomSequencePhase.HeadPain && root.RoomAttackPhaseElapsed >= 2f) || step % 40 == 39)
                    yield return null;
            }
            Assert.That(root.RoomAttackPhase, Is.EqualTo(phase), "The real Room pursuit/sequence did not reach " + phase + ".");
        }

        /// <summary>
        /// Samples the actual presentation at 60 Hz, including phase boundaries
        /// and the frame after fixed-camera ownership is returned. The bounded
        /// 12 Hz captures retain the real lighting/post-processing for motion review.
        /// </summary>
        private IEnumerator TraceRoomCameraToPhase(CombatRoomSequencePhase phase, string journey, float minimumPhaseElapsed = 0f)
        {
            const float stepSeconds = 1f / 60f;
            Camera camera = root.CameraFollow.Camera;
            Pose previous = new Pose(camera.transform.position, camera.transform.rotation);
            float previousFov = camera.fieldOfView;
            CombatRoomSequencePhase previousPhase = root.RoomAttackPhase;
            Vector3 previousHead = RoomTestHeadProjection(camera);
            int captured = 0;
            yield return AtCombatPresentation(() => CaptureMixedCameraFrame(camera,
                "Test Room attack - " + journey + " - Frame " + (++captured).ToString("D2")));
            previous = new Pose(camera.transform.position, camera.transform.rotation);
            previousFov = camera.fieldOfView;
            previousHead = RoomTestHeadProjection(camera);
            for (int step = 0; step < 600 &&
                (root.RoomAttackPhase != phase || root.RoomAttackPhaseElapsed + .000001f < minimumPhaseElapsed); step++)
            {
                root.Tick(stepSeconds);
                bool sampleCapture = step % 5 == 4 || root.RoomAttackPhase != previousPhase;
                yield return AtCombatPresentation(() =>
                {
                    AssertRoomCameraFrameContinuity(camera, previous, previousFov, previousPhase, previousHead);
                    if (root.RoomAttackCinematicActive)
                    {
                        Assert.That(root.Hero.RoomHeadPainActive, Is.True,
                            "The real hero owns the head action from the first Room attack frame.");
                        AssertRoomOtherActorsYield(root.Opponents[0]);
                        if (root.RoomAttackPhase != CombatRoomSequencePhase.HeadPain &&
                            (root.RoomAttackPhase != CombatRoomSequencePhase.FocusEntry || root.RoomAttackPhaseElapsed >= .32f))
                            AssertRoomHeroHeadHeld();
                    }
                    if (root.RoomAttackPhase == CombatRoomSequencePhase.PointHero)
                        AssertRoomPointShotComposition(root.Opponents[0]);
                    if (sampleCapture) CaptureMixedCameraFrame(camera,
                        "Test Room attack - " + journey + " - Frame " + (++captured).ToString("D2"));
                });
                previous = new Pose(camera.transform.position, camera.transform.rotation);
                previousFov = camera.fieldOfView;
                previousPhase = root.RoomAttackPhase;
                previousHead = RoomTestHeadProjection(camera);
            }
            Assert.That(root.RoomAttackPhase, Is.EqualTo(phase), "The smooth Room camera did not reach " + phase + ".");
            Assert.That(root.RoomAttackPhaseElapsed + .000001f, Is.GreaterThanOrEqualTo(minimumPhaseElapsed));
            yield return AtCombatPresentation(() => AssertRoomCameraFrameContinuity(camera,
                previous, previousFov, previousPhase, previousHead));
        }

        private Vector3 RoomTestHeadProjection(Camera camera) => root.RoomAttackHeroImage == null
            ? Vector3.back : camera.WorldToViewportPoint(root.RoomAttackHeroImage.Anchors.Head.position);

        private void AssertRoomCameraFrameContinuity(Camera camera, Pose previous, float previousFov,
            CombatRoomSequencePhase previousPhase, Vector3 previousHead)
        {
            if (previousPhase == CombatRoomSequencePhase.Idle && root.RoomAttackPhase == CombatRoomSequencePhase.Idle) return;
            string boundary = previousPhase + " → " + root.RoomAttackPhase;
            Assert.That(Vector3.Distance(previous.position, camera.transform.position), Is.LessThan(.2f),
                "There must be no one-frame camera position cut during " + boundary + ".");
            Assert.That(Quaternion.Angle(previous.rotation, camera.transform.rotation), Is.LessThan(8f),
                "There must be no one-frame camera rotation cut during " + boundary + ".");
            Assert.That(Mathf.Abs(previousFov - camera.fieldOfView), Is.LessThan(3f),
                "The lens must change continuously during " + boundary + ".");
            Vector3 currentHead = RoomTestHeadProjection(camera);
            if (previousPhase != root.RoomAttackPhase && RoomTestProjectionOnScreen(previousHead) && RoomTestProjectionOnScreen(currentHead))
                Assert.That(new Vector2((previousHead.x - currentHead.x) * 1280f,
                    (previousHead.y - currentHead.y) * 720f).magnitude, Is.LessThan(20f),
                    "The seated hero must not jump across the screen at " + boundary + ".");
        }

        private static bool RoomTestProjectionOnScreen(Vector3 point) => point.z > .001f &&
            point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f;

        private void AssertRoomTestSeatedFootContact(Player3DAssetRegistry image, SkinnedMeshRenderer originalCorridor)
        {
            Transform stage = image.transform.parent;
            float pelvisHeight = stage.InverseTransformPoint(image.Anchors.Pelvis.position).y;
            int plaster = Array.IndexOf(originalCorridor.sharedMaterials, CombatRoomAssetProvider.LoadBodyMaterial("plaster"));
            Assert.That(plaster, Is.GreaterThanOrEqualTo(0));
            var scratch = new Mesh { name = "Test Room seated foot contact" };
            try
            {
                // Preserve the imported rig's scale, as the production sole probe
                // does, then measure in normalized stage metres.
                originalCorridor.BakeMesh(scratch, true);
                Vector3[] floorVertices = scratch.vertices;
                for (int i = 0; i < floorVertices.Length; i++)
                    floorVertices[i] = stage.InverseTransformPoint(originalCorridor.transform.TransformPoint(floorVertices[i]));
                int[] plasterTriangles = scratch.GetTriangles(plaster);
                var floorTriangles = new List<int>();
                for (int i = 0; i < plasterTriangles.Length; i += 3)
                {
                    Vector3 a = floorVertices[plasterTriangles[i]], b = floorVertices[plasterTriangles[i + 1]],
                        c = floorVertices[plasterTriangles[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (Mathf.Abs(normal.y) < normal.magnitude * .5f || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) > pelvisHeight) continue;
                    floorTriangles.Add(plasterTriangles[i]);
                    floorTriangles.Add(plasterTriangles[i + 1]);
                    floorTriangles.Add(plasterTriangles[i + 2]);
                }
                Assert.That(floorTriangles, Is.Not.Empty, "The actual skinned plaster must provide the floor and raised thresholds.");
                var floor = new List<HeadContactSurface> { new HeadContactSurface
                    { Name = originalCorridor.name, Vertices = floorVertices, Triangles = floorTriangles.ToArray() } };
                var minimum = new[] { float.PositiveInfinity, float.PositiveInfinity };
                var points = new int[2];
                foreach (SkinnedMeshRenderer skin in image.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy ||
                        !(skin.name.StartsWith("CLO_Boot", StringComparison.Ordinal) || skin.name.StartsWith("GEO_Foot", StringComparison.Ordinal))) continue;
                    int side = skin.name.EndsWith(".L", StringComparison.Ordinal) ? 0 :
                        skin.name.EndsWith(".R", StringComparison.Ordinal) ? 1 : -1;
                    if (side < 0) continue;
                    skin.BakeMesh(scratch, true);
                    foreach (Vector3 vertex in scratch.vertices)
                    {
                        Vector3 point = stage.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        Assert.That(IndependentHeadRaycast(floor, new Ray(new Vector3(point.x, pelvisHeight + .05f, point.z), Vector3.down),
                            out Vector3 support), Is.True, "Every visible " + skin.name + " vertex needs an actual corridor surface beneath it.");
                        minimum[side] = Mathf.Min(minimum[side], point.y - support.y);
                        points[side]++;
                    }
                }
                for (int side = 0; side < 2; side++)
                {
                    string context = (side == 0 ? "Left" : "Right") + " seated boot at " + root.RoomAttackPhaseElapsed.ToString("F2") + " s";
                    Assert.That(points[side], Is.GreaterThan(0), context + " must use the rendered production footwear.");
                    Assert.That(minimum[side], Is.GreaterThanOrEqualTo(-.005f), context + " must not penetrate the real floor or nested-frame threshold.");
                    Assert.That(minimum[side], Is.LessThanOrEqualTo(.015f), context + " must remain planted instead of floating above the room.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(scratch); }
        }

        private void AssertRoomTestLiveIdle(CombatActor room, Vector3 heroRoot, Vector3[] opponents)
        {
            Assert.That(Vector3.Distance(root.Hero.transform.position, heroRoot), Is.LessThan(.01f), "The actual hero stays in the arena.");
            AssertRoomOtherActorsYield(room);
            for (int i = 1; i < opponents.Length; i++)
            {
                CombatActor actor = root.Opponents[i];
                if (actor.IsCrane) continue; // Residual pendulum and hook return remain physical.
                Assert.That(Vector3.ProjectOnPlane(actor.transform.position - opponents[i], Vector3.up).magnitude, Is.LessThan(.02f),
                    "A healthy yielding ground enemy does not voluntarily chase the hero.");
                Assert.That(actor.ActiveClipName, Is.EqualTo(CombatZombieAssetProvider.IdleClip),
                    "The awake Docker yields through its ordinary living idle.");
            }
        }

        private void AssertRoomOtherActorsYield(CombatActor room)
        {
            foreach (CombatActor actor in root.Opponents)
            {
                if (actor == room || actor.State.IsDefeated) continue;
                Assert.That(actor.RoomAttackSuppressed, Is.True, "Every other living opponent yields voluntary combat to the Room shot.");
                Assert.That(actor.State.IsAttacking || actor.State.IsCharging || actor.State.IsShoving ||
                    actor.State.IsKicking || actor.State.Phase == MeleePhase.Step, Is.False,
                    "An existing windup is cancelled and no other attack can overlap the camera sequence.");
                if (actor.IsCrane) Assert.That(actor.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            }
        }

        private void AssertRoomHeroHeadHeld()
        {
            Assert.That(root.Hero.RoomHeadPainActive, Is.True);
            var hero = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
            Assert.That(Vector3.Distance(hero.Anchors.LeftGrip.position, hero.Anchors.Head.position), Is.LessThan(.4f),
                "The actual hero keeps his left hand at his head while the camera points and travels inside the monster.");
        }

        private void AssertRoomTestRestored(float nearClipPlane)
        {
            Assert.That(root.RoomAttackCinematicActive, Is.False);
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(root.CameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
            foreach (CombatActor actor in root.GetComponentsInChildren<CombatActor>(true))
                Assert.That(actor.RoomAttackSuppressed, Is.False, "Completion, reset and disable release every prepared actor's Room suppression.");
            Assert.That(root.CameraFollow.Camera.fieldOfView, Is.EqualTo(root.CameraFollow.FollowFieldOfView).Within(.001f),
                "Releasing a Room shot restores the ordinary lens.");
            Assert.That(RoomTestCameraRoll(root.CameraFollow.Camera), Is.EqualTo(0f).Within(.01f),
                "The Dutch angle cannot leak into the gameplay camera.");
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(nearClipPlane).Within(.00001f),
                "Cancellation and reset release the miniature-room near plane too.");
        }

        private void AssertRoomPointShotComposition(CombatActor room)
        {
            Camera camera = root.CameraFollow.Camera;
            Transform shoulder = FindAnatomicalBone(room, "upper_arm.R");
            Transform forearm = FindAnatomicalBone(room, "forearm.R");
            Transform hand = FindAnatomicalBone(room, "hand.R");
            Vector3 tip = room.RoomPointingTipWorld;
            Vector3 head = FindAnatomicalBone(room, "head").position;
            Vector3 pointing = room.RoomPointingDirectionWorld;
            Assert.That(Vector3.Dot((camera.transform.position - tip).normalized, pointing), Is.GreaterThan(.995f),
                "The raised index finger points directly into the lens throughout the pointing shot.");
            Assert.That(hand.position.y, Is.GreaterThanOrEqualTo(head.y - .15f * room.PresentationScale),
                "The pointing wrist stays raised near the monster's head rather than reaching up from the chest.");
            float armLength = Vector3.Distance(shoulder.position, forearm.position) +
                Vector3.Distance(forearm.position, hand.position);
            Assert.That(Vector3.Distance(shoulder.position, hand.position), Is.GreaterThanOrEqualTo(.97f * armLength),
                "The raised pointing arm reaches forward almost straight, with only a small natural elbow bend.");
            Assert.That(Vector3.Dot(camera.transform.forward, (head - camera.transform.position).normalized), Is.GreaterThan(.95f),
                "The tilted lens still looks toward the monster's head behind the finger.");
            AssertRoomTestInFrame(head);
            AssertRoomTestInFrame(tip);
            Vector3 fingerView = camera.WorldToViewportPoint(tip);
            Vector3 headView = camera.WorldToViewportPoint(head);
            Assert.That(fingerView.z, Is.GreaterThan(camera.nearClipPlane * 1.5f),
                "The foreground finger stays in front of the borrowed lens's near plane.");
            Assert.That(fingerView.z, Is.LessThan(headView.z * .75f),
                "The foreground finger is visibly closer than the head for the wide-angle threat.");
            Assert.That(camera.fieldOfView, Is.InRange(70f, 95f), "The pointing shot uses its dramatic wide lens.");
            Assert.That(Mathf.Abs(RoomTestCameraRoll(camera)), Is.InRange(8f, 30f),
                "A readable Dutch angle is part of the pointing composition.");
        }

        private static float RoomTestCameraRoll(Camera camera) => Vector3.SignedAngle(
            Vector3.ProjectOnPlane(Vector3.up, camera.transform.forward), camera.transform.up, camera.transform.forward);

        private void AssertRoomTestInFrame(Vector3 point)
        {
            Vector3 screen = root.CameraFollow.Camera.WorldToViewportPoint(point);
            Assert.That(screen.z, Is.GreaterThan(0f));
            Assert.That(screen.x, Is.InRange(.02f, .98f));
            Assert.That(screen.y, Is.InRange(.02f, .98f));
        }

        private static AudioSource RoomTestLaughSource(CombatActor room)
        {
            foreach (AudioSource source in room.GetComponentsInChildren<AudioSource>(true))
                if (source.name == "Room laughter") return source;
            Assert.Fail("Room must retain its own spatial laughter voice.");
            return null;
        }
    }
}
