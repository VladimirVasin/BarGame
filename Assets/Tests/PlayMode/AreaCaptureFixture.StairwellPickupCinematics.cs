using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Three confirmed Stairwell finds: same-rig contacts, camera handoff, equipment and delayed crowd waking.")]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator StairwellPickupCinematics()
        {
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            float previousDelta = Time.captureDeltaTime;
            bool previousFocus = GameInput.MovementFocused;
            try
            {
                for (int stage = 1; stage <= 3; stage++)
                {
                    PreparePickupCinematicStage(stage);
                    yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior, LoadSceneMode.Single);
                    StairwellInteriorRoot scene = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                    yield return WaitForCorruptedStairwell(() => scene != null && scene.IsInitialized,
                        "The focused pickup scene did not initialize.");
                    Time.captureDeltaTime = 1f / 30f;
                    GameInput.HandleApplicationFocus(true);
                    if (!GameSessionState.IsGameTimeRunning)
                        Assert.That(GameSessionState.TryStartPreparedGameTime() || GameSessionState.TryStartGameTimeFromWake(), Is.True);
                    yield return VerifyPickupCinematic(scene, stage, input, keyboard);
                }
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                if (keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                GameInput.HandleApplicationFocus(previousFocus);
                GameSessionState.BeginNewGame();
            }
        }

        [UnityTest]
        [Explicit("Disabling a committed pickup cinematic retains its item and restores every presentation owner.")]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator StairwellPickupCinematicCommittedCleanup()
        {
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            float previousDelta = Time.captureDeltaTime;
            bool previousFocus = GameInput.MovementFocused;
            try
            {
                PreparePickupCinematicStage(1);
                yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior, LoadSceneMode.Single);
                StairwellInteriorRoot scene = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                yield return WaitForCorruptedStairwell(() => scene != null && scene.IsInitialized,
                    "The committed-cleanup scene did not initialize.");
                Time.captureDeltaTime = 1f / 30f;
                GameInput.HandleApplicationFocus(true);
                if (!GameSessionState.IsGameTimeRunning)
                    Assert.That(GameSessionState.TryStartPreparedGameTime() || GameSessionState.TryStartGameTimeFromWake(), Is.True);
                var pickup = scene.LabyrinthPuzzles.FlashlightPickup;
                Vector3 point = pickup.InteractionPosition;
                Vector3 interior = scene.World.Root.TransformPoint(scene.LabyrinthPresentation.Rooms[0].InteriorPoint);
                Vector3 stand = point + Vector3.ProjectOnPlane(interior - point, Vector3.up).normalized * .75f;
                foreach (var control in scene.LabyrinthPuzzles.Controls)
                    if (control.Stage == 1) { stand.y = control.Plan.EntryRootPosition.y; break; }
                scene.Player.Motor.Teleport(stand);
                scene.Player.Motor.SetInputEnabled(true);
                scene.Player.Interactor.SetInputEnabled(true);
                AimLabyrinthOrbit(scene, scene.World.Root.InverseTransformPoint(point));
                Physics.SyncTransforms();
                yield return new WaitForSecondsRealtime(.2f);
                var rendererStates = new Dictionary<Renderer, bool>();
                foreach (Renderer renderer in scene.Player.Visual.Renderers) rendererStates.Add(renderer, renderer.enabled);
                var screen = WorldItemFoundScreen.For(scene.Player.Interactor);
                pickup.Interact(scene.Player.Interactor);
                yield return WaitForCorruptedStairwell(() => screen.IsShowing, "The shared cleanup-test find did not settle.");
                input.Press(keyboard.eKey, queueEventOnly: true);
                var cinematic = scene.LabyrinthPuzzles.GetComponent<StairwellPickupCinematicController>();
                float deadline = Time.realtimeSinceStartup + 12f;
                while (scene.AnimatedInteraction.Phase != PlayerAnimatedInteractionPhase.Looping &&
                    Time.realtimeSinceStartup < deadline)
                {
                    // Inventory commits before the item screen finishes returning its model and camera.
                    // Only a closed screen with no successor owner proves an interrupted handoff.
                    if (GameSessionState.HasInventoryItem(InventoryItemId.ChestFlashlight) &&
                        !screen.IsPresenting && !cinematic.IsActive) break;
                    yield return null;
                }
                Assert.That(scene.AnimatedInteraction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping),
                    "The committed pickup must reach its held-model action. " + DescribePickupCinematic(scene, cinematic));
                input.Release(keyboard.eKey, queueEventOnly: true);
                Assert.That(cinematic.IsActive, Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.ChestFlashlight), Is.EqualTo(1));
                cinematic.enabled = false;
                yield return null;
                yield return null;
                Assert.That(cinematic.IsActive, Is.False);
                Assert.That(scene.AnimatedInteraction.IsActive, Is.False);
                Assert.That(screen.IsPresenting, Is.False);
                Assert.That(scene.Player.Motor.InputEnabled && scene.Player.Interactor.InputEnabled, Is.True);
                Assert.That(GameSessionState.IsWorldItemCollected(BarPromenade.StairwellLabyrinthPuzzles.FlashlightSourceId), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.ChestFlashlight), Is.EqualTo(1));
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.ChestFlashlight), Is.True);
                Assert.That(pickup == null, Is.True, "A committed cancelled shot must clean its original source.");
                foreach (var state in rendererStates)
                    Assert.That(state.Key.enabled, Is.EqualTo(state.Value), "Cleanup must restore each preceding world renderer state.");
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                if (keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                GameInput.HandleApplicationFocus(previousFocus);
                GameSessionState.BeginNewGame();
            }
        }

        private static void PreparePickupCinematicStage(int stage)
        {
            GameSessionState.BeginNewGame();
            GameSessionState.CommitHomeSleepAppearance();
            GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
            var state = GameSessionState.StairwellLabyrinthPuzzles;
            foreach (var action in new[] { StairwellLabyrinthPuzzleAction.Roll0, StairwellLabyrinthPuzzleAction.Roll0,
                StairwellLabyrinthPuzzleAction.Roll1, StairwellLabyrinthPuzzleAction.Roll2,
                StairwellLabyrinthPuzzleAction.Roll2, StairwellLabyrinthPuzzleAction.ClothBolt })
                Assert.That(state.TryApply(action), Is.True);
            if (stage >= 2)
                foreach (var action in new[] { StairwellLabyrinthPuzzleAction.ShutterStop, StairwellLabyrinthPuzzleAction.CarriageSend })
                    Assert.That(state.TryApply(action), Is.True);
            if (stage >= 3)
                foreach (var action in new[] { StairwellLabyrinthPuzzleAction.Bird0, StairwellLabyrinthPuzzleAction.Bird0,
                    StairwellLabyrinthPuzzleAction.Bird1, StairwellLabyrinthPuzzleAction.Bird2,
                    StairwellLabyrinthPuzzleAction.Bird2, StairwellLabyrinthPuzzleAction.BirdLift })
                    Assert.That(state.TryApply(action), Is.True);
        }

        private static IEnumerator VerifyPickupCinematic(StairwellInteriorRoot scene, int stage,
            InputTestFixture input, Keyboard keyboard)
        {
            WorldItemPickup pickup = stage == 1 ? scene.LabyrinthPuzzles.FlashlightPickup :
                stage == 2 ? scene.LabyrinthPuzzles.CrowbarPickup : scene.LabyrinthPuzzles.PistolPickup;
            Assert.That(pickup, Is.Not.Null);
            InventoryItemId item = pickup.Plan.ItemId;
            string source = pickup.Plan.SourceId;
            string shot = "pickup-" + stage;
            Vector3 point = pickup.InteractionPosition;
            Vector3 interior = scene.World.Root.TransformPoint(scene.LabyrinthPresentation.Rooms[stage - 1].InteriorPoint);
            Vector3 stand = point + Vector3.ProjectOnPlane(interior - point, Vector3.up).normalized * .75f;
            foreach (var control in scene.LabyrinthPuzzles.Controls)
                if (control.Stage == stage) { stand.y = control.Plan.EntryRootPosition.y; break; }
            scene.Player.Motor.Teleport(stand);
            scene.Player.Motor.SetInputEnabled(true);
            scene.Player.Interactor.SetInputEnabled(true);
            AimLabyrinthOrbit(scene, scene.World.Root.InverseTransformPoint(point));
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(pickup.CanInteract(scene.Player.Interactor), Is.True,
                "The solved room's physical source must be reachable.");
            var cinematic = scene.LabyrinthPuzzles.GetComponent<StairwellPickupCinematicController>();
            Assert.That(cinematic, Is.Not.Null);
            var visual = scene.Player.Visual;
            var screen = WorldItemFoundScreen.For(scene.Player.Interactor);
            pickup.Interact(scene.Player.Interactor);
            yield return WaitForCorruptedStairwell(() => screen.IsShowing, "The common item screen did not settle.");
            Assert.That(screen.ActiveItemId, Is.EqualTo(item));
            Assert.That(GameSessionState.HasInventoryItem(item), Is.False);
            input.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.escapeKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => !screen.IsPresenting, "Cancel must return the original uncollected model.");
            Assert.That(GameSessionState.HasInventoryItem(item), Is.False);
            Assert.That(pickup.Model.parent, Is.SameAs(pickup.transform));
            Assert.That(scene.AnimatedInteraction.IsActive, Is.False, "Cancelling inspection must not start the pickup action.");
            yield return null;
            pickup.Interact(scene.Player.Interactor);
            yield return WaitForCorruptedStairwell(() => screen.IsShowing, "The cancelled source must remain usable.");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => scene.AnimatedInteraction.IsActive,
                "Confirm must synchronously transfer ownership to the contextual pickup action.");
            Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
            Assert.That(scene.Player.Motor.InputEnabled, Is.False);
            Assert.That(scene.Player.Interactor.InputEnabled, Is.False);
            Assert.That(scene.Player.Visual, Is.SameAs(visual));
            if (stage == 2) AssertStairwellEncounterDormant(scene);

            // Keep the confirming physical E held through the whole handoff and the first restored world Update.
            // A reused input must neither reopen the source nor operate a nearby puzzle lever.
            bool reachedLoop = false;
            bool capturedReach = false, capturedHold = false, capturedReturn = false;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (cinematic.IsActive && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(scene.Player.Visual, Is.SameAs(visual));
                Assert.That(scene.Player.Motor.InputEnabled, Is.False);
                Assert.That(scene.Player.Interactor.InputEnabled, Is.False);
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
                if (stage == 2) AssertStairwellEncounterDormant(scene);
                var phase = scene.AnimatedInteraction.Phase;
                float progress = scene.AnimatedInteraction.PhaseProgress;
                if (!capturedReach && phase == PlayerAnimatedInteractionPhase.Entering && progress >= .45f)
                {
                    yield return CapturePickupCinematicFrame(scene, shot + "-reach");
                    capturedReach = true;
                }
                else if (!capturedHold && phase == PlayerAnimatedInteractionPhase.Looping && progress >= .4f)
                {
                    reachedLoop = true;
                    yield return CapturePickupCinematicFrame(scene, shot + "-hold");
                    capturedHold = true;
                }
                else if (!capturedReturn && phase == PlayerAnimatedInteractionPhase.Exiting && progress >= .4f)
                {
                    yield return CapturePickupCinematicFrame(scene, shot + "-return");
                    capturedReturn = true;
                }
                yield return null;
            }
            Assert.That(cinematic.IsActive || scene.AnimatedInteraction.IsActive, Is.False, "The short pickup action and camera must finish.");
            Assert.That(reachedLoop && capturedReach && capturedHold && capturedReturn, Is.True,
                "Review a real rendered frame from every authored pickup phase. " + DescribePickupCinematic(scene, cinematic));
            yield return WaitForCorruptedStairwell(() => scene.Player.Motor.InputEnabled && scene.Player.Interactor.InputEnabled,
                "The completed action and camera return must restore gameplay ownership.");
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(screen.IsPresenting, Is.False);
            Assert.That(scene.LabyrinthPuzzles.PuzzleMode.IsActive, Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
            Assert.That(GameSessionState.TryCollectWorldItem(source, item), Is.False,
                "A completed cinematic must not reopen a committed permanent source.");
            Assert.That(GameSessionState.IsInventoryItemEquipped(item), Is.True,
                "The final authored attachment/grip must hand off to the equipment owner.");
            Assert.That(pickup == null, Is.True, "The committed physical source must be cleaned up after the shot.");
            if (stage == 1)
            {
                var equipment = scene.Player.GameObject.GetComponent<PlayerChestFlashlightController>();
                Assert.That(equipment.Flashlight != null && equipment.Flashlight.isActiveAndEnabled && equipment.Flashlight.SpotLight.enabled, Is.True);
            }
            else
            {
                var combat = scene.Player.GameObject.GetComponent<PlayerCrowbarCombatController>();
                CombatWeaponId weapon = stage == 2 ? CombatWeaponId.Crowbar : CombatWeaponId.Pistol;
                yield return WaitForCorruptedStairwell(() => combat.Actor != null && combat.Actor.WeaponId == weapon &&
                    combat.Actor.HasWeaponInHand && !combat.Actor.IsSwitchingWeapon, "The found weapon must remain in its shared ready grip.");
                if (stage == 3)
                {
                    IFirearmState pistol = combat.Actor.CarriedFirearm(CombatWeaponId.Pistol);
                    Assert.That(pistol.Rounds, Is.EqualTo(8));
                    Assert.That(pistol.ReserveRounds, Is.Zero);
                }
                else yield return WaitForCorruptedStairwell(() => scene.Encounter.State.Awakened,
                    "The dormant crowd must wake after the crowbar's completed camera/input handoff.");
            }
            yield return CapturePickupCinematicFrame(scene, shot + "-equipped");
        }

        private static string DescribePickupCinematic(StairwellInteriorRoot scene, StairwellPickupCinematicController cinematic)
        {
            Vector3 root = scene.Player.GameObject.transform.position;
            string diagnostic = $"active={cinematic.IsActive},phase={scene.AnimatedInteraction.Phase}," +
                $"progress={scene.AnimatedInteraction.PhaseProgress:F3},root={root:F3}";
            if (cinematic.Plan == null) return diagnostic + ",plan=unprepared";
            Vector3 dock = cinematic.Plan.EntryPose.RootPosition;
            diagnostic += $",dock={dock:F3},contact={cinematic.Plan.SourceContact:F3}";
            CharacterController capsule = scene.Player.GameObject.GetComponent<CharacterController>();
            Vector3 center = dock + capsule.center;
            Vector3 half = Vector3.up * (capsule.height * .5f - capsule.radius);
            foreach (Collider obstacle in Physics.OverlapCapsule(center - half, center + half,
                capsule.radius + capsule.skinWidth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!obstacle.transform.IsChildOf(scene.Player.GameObject.transform))
                    diagnostic += ",dockOverlap=" + obstacle.name + ":" + obstacle.bounds;
            return diagnostic;
        }

        private static IEnumerator WaitForStairwellPickupCinematic(StairwellInteriorRoot scene)
        {
            var cinematic = scene.LabyrinthPuzzles.GetComponent<StairwellPickupCinematicController>();
            yield return WaitForCorruptedStairwell(() => cinematic == null || !cinematic.IsActive,
                "The confirmed find must finish its physical action and camera return before the next world operation.");
        }

        private static IEnumerator CapturePickupCinematicFrame(StairwellInteriorRoot scene, string name)
        {
            Camera camera = scene.CameraFollow.Camera;
            var target = new RenderTexture(1280, 720, 24);
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            target.Create();
            camera.targetTexture = target;
            camera.aspect = 16f / 9f;
            try
            {
                yield return ScarfRenderedFrame(camera, () =>
                {
                    RenderTexture previousActive = RenderTexture.active;
                    var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    try
                    {
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                        pixels.Apply();
                        string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "stairwell-pickup-cinematics");
                        Directory.CreateDirectory(folder);
                        string path = Path.Combine(folder, name + ".png");
                        File.WriteAllBytes(path, pixels.EncodeToPNG());
                        Debug.Log("Pickup cinematic capture wrote " + path);
                        Assert.That(IsBlank(pixels), Is.False);
                        bool heroVisible = false;
                        foreach (Renderer renderer in scene.Player.Visual.Renderers)
                            heroVisible |= renderer.enabled && renderer.gameObject.activeInHierarchy;
                        Assert.That(heroVisible, Is.True, "The same world hero must stay visible through the pickup.");
                        if (name.EndsWith("-hold", StringComparison.Ordinal))
                        {
                            var cinematic = scene.LabyrinthPuzzles.GetComponent<StairwellPickupCinematicController>();
                            Assert.That(cinematic.ContactWeight, Is.GreaterThan(.99f));
                            Assert.That(cinematic.HeldModel != null && cinematic.HeldModel.gameObject.activeInHierarchy, Is.True);
                            float gripRadius = cinematic.ItemId == InventoryItemId.CombatCrowbar ? .34f : .10f;
                            Assert.That(Vector3.Distance(cinematic.HeldModel.position, cinematic.RightGrip.position), Is.LessThan(gripRadius),
                                "The visible found model must remain seated in its real right-hand grip.");
                            Vector3 grip = camera.WorldToViewportPoint(cinematic.RightGrip.position);
                            Assert.That(grip.z, Is.GreaterThan(camera.nearClipPlane));
                            Assert.That(grip.x, Is.InRange(0f, 1f));
                            Assert.That(grip.y, Is.InRange(0f, 1f));
                        }
                    }
                    finally { RenderTexture.active = previousActive; Object.DestroyImmediate(pixels); }
                });
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
