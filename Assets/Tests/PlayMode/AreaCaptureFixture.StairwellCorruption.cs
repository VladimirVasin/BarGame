using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static Shot[] CorruptedStairwellShots(StairwellInteriorRoot stairwell)
        {
            Assert.That(stairwell.Layout.IsCorrupted, Is.True);
            Assert.That(stairwell.Arrival, Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
            Assert.That(stairwell.Layout.GetSpawn(StairwellArrivalKind.StreetDoor),
                Is.EqualTo(stairwell.Layout.ApartmentSpawn));
            Assert.That(stairwell.World.UpperBlocker, Is.Null);
            Assert.That(stairwell.World.StreetDoor, Is.Null);
            Assert.That(stairwell.StreetExit, Is.Null);
            Assert.That(stairwell.Cat, Is.Null);
            Assert.That(stairwell.DescentBlocker, Is.Null);
            Assert.That(stairwell.Soundscape, Is.Not.Null);
            Assert.That(stairwell.Ambience, Is.Not.Null);
            Assert.That(stairwell.Atmosphere.Dust, Is.Not.Null);
            Assert.That(stairwell.World.DescentWall, Is.Not.Null);
            Assert.That(stairwell.World.UpperLanding, Is.Not.Null);
            Assert.That(stairwell.World.ApartmentDoor, Is.Not.Null);
            Assert.That(stairwell.World.CatRelief, Is.Not.Null);
            Assert.That(stairwell.CatReliefInspection, Is.Not.Null);
            Assert.That(stairwell.World.StairColliders, Has.Count.EqualTo(3));
            Transform room = stairwell.World.Root;
            foreach (string absent in new[] { "Stairwell Ceiling", "Stairwell Corrupted Ceiling",
                "Stairwell Back Wall", "Stairwell Corrupted Back Wall", "Street Door", "Under Stair Grille",
                "Corrupted Upper Temporary Grille", "Corrupted Upper Landing End Wall" })
                Assert.That(room.Find(absent), Is.Null, "The broken route retained " + absent);
            foreach (string required in new[] { "Stairwell Ground Floor", "Stairwell Dressing",
                "Stairwell Front Wall", "Stairwell Left Wall", "Stairwell Right Wall",
                "Corrupted Back Wall Base", "Corrupted Back Wall Broken Left", "Corrupted Back Wall Broken Right",
                "Corrupted Roof East", "Corrupted Roof South", "Middle Landing" })
                Assert.That(room.Find(required), Is.Not.Null, "The recognizable stairwell is missing " + required);
            Assert.That(room.Find("Corrupted Endless Staircase").childCount, Is.EqualTo(3));
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.Linear));
            Assert.That(RenderSettings.fogStartDistance, Is.EqualTo(StairwellInteriorAtmosphere.CorruptedFogStart).Within(.001f));
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(StairwellInteriorAtmosphere.CorruptedFogEnd).Within(.001f));
            Assert.That(RenderSettings.fogColor, Is.EqualTo(StairwellInteriorAtmosphere.CorruptedFogColor));
            Assert.That(Camera.main.backgroundColor, Is.EqualTo(RenderSettings.fogColor));
            Assert.That(stairwell.Atmosphere.UpperGlow, Is.Not.Null);
            Assert.That(stairwell.Atmosphere.UpperGlowHalo.IsVisible, Is.True);
            Assert.That(room.InverseTransformPoint(stairwell.Atmosphere.UpperGlow.transform.position),
                Is.EqualTo(StairwellInteriorAtmosphere.UpperGlowPosition));
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(7));
            var hero = (Player3DCharacterPresentation)stairwell.Player.Visual;
            Assert.That(hero.CorruptionAppearance.AppliedLevel, Is.EqualTo(7));
            var shots = StairwellFixedCameraController.CreateDefaultShots(stairwell.Layout);
            Assert.That(shots.Count, Is.EqualTo(3));
            Physics.SyncTransforms();
            foreach (StairwellCameraShot shot in shots)
            {
                Assert.That(shot.Focus.Enabled, Is.True);
                Assert.That(shot.FieldOfView, Is.LessThanOrEqualTo(64f));
                Assert.That(Vector3.Angle(shot.Rotation * Vector3.forward,
                    Vector3.ProjectOnPlane(shot.Rotation * Vector3.forward, Vector3.up)), Is.LessThan(30f),
                    "The broken route must retain diagonal views rather than overhead views.");
                Assert.That(Mathf.Abs(shot.Position.x), Is.LessThan(stairwell.Layout.RoomSize.x * .5f));
                Assert.That(Mathf.Abs(shot.Position.z), Is.LessThan(stairwell.Layout.RoomSize.y * .5f));
                Assert.That(Physics.CheckSphere(room.TransformPoint(shot.Position), .08f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False,
                    "The interior camera must stand clear of the broken shell: " + shot.Kind);
            }
            StairwellCameraShot apartment = shots[0];
            Assert.That(apartment.Kind, Is.EqualTo(StairwellCameraShotKind.ApartmentLanding));
            return new[] { Shot.At("corrupted-00-broken-stairwell",
                room.TransformPoint(apartment.Position),
                room.TransformPoint(apartment.Position + apartment.Rotation * Vector3.forward * 4f),
                apartment.FieldOfView, delayFrames: 4) };
        }

        private static IEnumerator VerifyCorruptedStairwellRoute(StairwellInteriorRoot stairwell)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            Transform hero = stairwell.Player.GameObject.transform;
            Transform room = stairwell.World.Root;
            bool previousMotorInput = motor.InputEnabled;
            bool previousInteractorInput = stairwell.Player.Interactor.InputEnabled;
            bool previousFocus = GameInput.MovementFocused;
            float previousDelta = Time.captureDeltaTime;
            int previousIntoxication = GameSessionState.IntoxicationLevel;
            DrinkId previousDrink = GameSessionState.LastAlcoholicDrink;
            int previousDrinks = GameSessionState.DrinksConsumed;
            Time.captureDeltaTime = 1f / 30f;
            GameInput.HandleApplicationFocus(true);
            motor.SetInputEnabled(false);
            try
            {
                if (!GameSessionState.IsGameTimeRunning)
                    Assert.That(GameSessionState.TryStartPreparedGameTime() ||
                        GameSessionState.TryStartGameTimeFromWake(), Is.True);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Debug.Log("Corrupted stairwell proof: spawn and blocked descent");
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-01-spawn");
                yield return WalkStairwellWaypoint(stairwell, new Vector3(1.45f, 3.34f, -3.62f));
                for (int frame = 0; frame < 90 && !motor.InteractionPoseMoveStalled; frame++)
                {
                    motor.MoveTowardsApproachWaypoint(room.TransformPoint(new Vector3(1.45f, 3.34f, -1.9f)), .025f, 1f / 60f);
                    yield return null;
                }
                Assert.That(room.InverseTransformPoint(hero.position).z, Is.LessThan(-3.20f),
                    "Real wall collision must stop the descent.");
                motor.CancelInteractionPoseMove();
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.2f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.44f, -.16f));
                Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.UpperFlight));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-02-climbing");
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.35f));
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(room.InverseTransformPoint(hero.position).y,
                    Is.EqualTo(stairwell.Layout.UpperFlight.TopElevation + PlayerFactory.GroundedRootOffset).Within(.03f));
                Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.UpperLanding));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-03-upper-end");
                Debug.Log("Corrupted stairwell proof: upper barrier and fog terminal");
                Assert.That(stairwell.Atmosphere.RuntimeProfile.TryGet(out Bloom bloom), Is.True);
                bool bloomActive = bloom.active;
                try
                {
                    bloom.active = false;
                    yield return CaptureCorruptedGameplay(stairwell, "corrupted-04-bloom-off-end", proveFog: true);
                    AssertCorruptedTerminalInvisible(stairwell);
                }
                finally { bloom.active = bloomActive; }
                Collider barrier = room.Find("Corrupted Upper Limit Rail").GetComponent<Collider>();
                Assert.That(barrier, Is.Not.Null);
                Assert.That(barrier.bounds.min.z, Is.EqualTo(2.2f).Within(.005f));
                for (int frame = 0; frame < 60 && !motor.InteractionPoseMoveStalled; frame++)
                {
                    motor.MoveTowardsApproachWaypoint(room.TransformPoint(new Vector3(-1.45f, 4.94f, 2.8f)), .025f, 1f / 60f);
                    yield return null;
                }
                Assert.That(room.InverseTransformPoint(hero.position).z, Is.LessThan(2f),
                "The visible crossbar must block the unsupported continuation.");
                motor.CancelInteractionPoseMove();
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, stairwell.Layout.ApartmentDoorActionRoot);
                Assert.That(stairwell.ApartmentEntrance.isActiveAndEnabled, Is.True);
                Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.ApartmentLanding));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-05-return-door");
                yield return VerifyCatReliefInspection(stairwell);

                motor.SetInputEnabled(false);
                GameSessionState.UpdateDrinkingProgress(100, previousDrink, previousDrinks);
                stairwell.CameraFollow.SetIntoxication(1f);
                for (int frame = 0; frame < 30; frame++)
                {
                    GameSessionState.UpdateDrinkingProgress(100, previousDrink, previousDrinks);
                    yield return null;
                }
                Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-07-maximum-intoxication");
            }
            finally
            {
                NarrativeInteractionController.For(stairwell.Player.Interactor).RestoreImmediate();
                motor.CancelInteractionPoseMove();
                GameSessionState.UpdateDrinkingProgress(previousIntoxication, previousDrink, previousDrinks);
                stairwell.CameraFollow.SetIntoxication(previousIntoxication / 100f);
                motor.SetInputEnabled(previousMotorInput);
                stairwell.Player.Interactor.SetInputEnabled(previousInteractorInput);
                Time.captureDeltaTime = previousDelta;
                GameInput.HandleApplicationFocus(previousFocus);
            }
        }

        private static IEnumerator VerifyCatReliefInspection(StairwellInteriorRoot stairwell)
        {
            Camera camera = Camera.main;
            float previousAspect = camera.aspect;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 21f / 9f })
                {
                    camera.aspect = aspect;
                    yield return VerifyCatReliefInspectionAtAspect(stairwell, input, keyboard);
                }
            }
            finally
            {
                NarrativeInteractionController.For(stairwell.Player.Interactor).RestoreImmediate();
                camera.aspect = previousAspect;
                input.TearDown();
            }
        }

        private static IEnumerator VerifyCatReliefInspectionAtAspect(StairwellInteriorRoot stairwell,
            InputTestFixture input, Keyboard keyboard)
        {
            Debug.Log("Corrupted stairwell proof: keyboard relief inspection, aspect=" + Camera.main.aspect);
            NarrativeInteraction inspection = stairwell.CatReliefInspection;
            Assert.That(inspection.Definition.IsSilent, Is.True);
            Assert.That(inspection.Definition.Pages, Is.Empty);
            Assert.That(inspection.SubjectRoot, Is.SameAs(stairwell.World.CatRelief));
            Assert.That(inspection.Staging.CameraTransition, Is.EqualTo(NarrativeCameraTransition.Cut));
            Assert.That(inspection.PlayerToHide, Is.SameAs(stairwell.Player.PresentationVisibility));
            var quests = new List<QuestLogEntry>(GameSessionState.Quests);
            var inventory = new List<InventoryItemStack>(GameSessionState.InventoryItems);
            var controller = NarrativeInteractionController.For(stairwell.Player.Interactor);
            Vector3 approach = stairwell.World.Root.TransformPoint(new Vector3(1.45f,
                stairwell.Layout.ApartmentElevation + PlayerFactory.GroundedRootOffset, -3.95f));
            yield return WalkStairwellWaypoint(stairwell, stairwell.World.Root.InverseTransformPoint(approach));
            stairwell.Player.GameObject.transform.rotation = inspection.Staging.Entry.RootRotation;
            stairwell.Player.Motor.SetInputEnabled(true);
            stairwell.Player.Interactor.SetInputEnabled(true);
            Physics.SyncTransforms();
            yield return WaitForCorruptedStairwell(() =>
                ReferenceEquals(stairwell.Player.Interactor.ActiveInteractable, inspection),
                "The stone head did not offer its actual E inspection prompt.");
            Vector3 start = stairwell.Player.GameObject.transform.position;
            Camera camera = Camera.main;
            Vector3 gameplayCameraPosition = camera.transform.position;
            float gameplayCameraFov = camera.fieldOfView;
            Pose closeUp = default;
            bool cutSeen = false;
            var rendererStates = new Dictionary<Renderer, bool>();
            foreach (Renderer renderer in stairwell.Player.Visual.Renderers)
                if (renderer != null) rendererStates.Add(renderer, renderer.enabled);
            bool shadowEnabled = stairwell.Player.ContactShadow.enabled;
            input.Press(keyboard.eKey, queueEventOnly: true);
            Assert.That(stairwell.Player.GameObject.transform.position, Is.EqualTo(start),
                "E must begin a guided approach, not teleport the hero into the inspection.");
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            Assert.That(controller.IsActive, Is.True, "Actual keyboard E must start inspection: " + controller.LastFailureReason);
            Assert.That(Vector3.Distance(stairwell.Player.GameObject.transform.position, start), Is.LessThan(.2f),
                "The first approach frame must walk, not jump directly into the dock.");
            yield return WaitForCorruptedStairwell(() =>
            {
                Assert.That(controller.IsActive, Is.True, "Inspection aborted after keyboard E: " +
                    controller.LastFailureReason + "; " + controller.CameraDirector.LastRejectedShotReason);
                if (controller.CameraDirector.IsFinished)
                {
                    Assert.That(cutSeen, Is.False, "The inspection unexpectedly released its close-up.");
                    Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False,
                        "The hero must stay visible throughout the physical approach.");
                    Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f),
                        "The camera must keep the gameplay position throughout the guided approach.");
                }
                else
                {
                    Assert.That(controller.CameraDirector.IsSettled, Is.True,
                        "The first inspection camera frame must already be the final close-up, without a blend.");
                    Assert.That(stairwell.Player.PresentationVisibility.IsHiddenBy(controller.CameraDirector), Is.True,
                        "The close-up camera must own the shared visibility lease in its first frame.");
                    Assert.That(stairwell.Player.ContactShadow.enabled, Is.False);
                    foreach (Renderer renderer in rendererStates.Keys)
                        Assert.That(renderer.enabled, Is.False, "The world hero must not obscure the object close-up.");
                    if (!cutSeen)
                    {
                        closeUp = new Pose(camera.transform.position, camera.transform.rotation);
                        cutSeen = true;
                    }
                    Assert.That(Vector3.Distance(camera.transform.position, closeUp.position), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(camera.transform.rotation, closeUp.rotation), Is.LessThan(.05f));
                }
                return controller.Phase == NarrativeInteractionPhase.Reading;
            },
                "The stone head did not reach its silent inspection: " + controller.LastFailureReason);
            Assert.That(cutSeen, Is.True);
            Assert.That(stairwell.Player.GameObject.activeInHierarchy, Is.True);
            Assert.That(stairwell.Player.GameObject.GetComponent<CharacterController>().enabled, Is.True,
                "Inspection hides only presentation, not the physical character.");
            Assert.That(Vector3.Distance(camera.transform.position, inspection.Staging.FocusBounds.center), Is.LessThan(1.5f),
                "Inspection must physically bring the camera next to the stone head.");
            Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.GreaterThan(3f));
            Vector2 low = Vector2.one, high = Vector2.zero;
            Bounds head = inspection.Staging.FocusBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(head.center + Vector3.Scale(head.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f)));
                Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(viewport.x, Is.InRange(.025f, .975f));
                Assert.That(viewport.y, Is.InRange(.025f, .975f));
                low = Vector2.Min(low, viewport); high = Vector2.Max(high, viewport);
            }
            Assert.That(high.y - low.y, Is.GreaterThan(.4f), "The complete head must occupy a readable close-up.");
            if (GraphicsEffectsSettings.DepthOfFieldEnabled)
            {
                var focusVolume = GameObject.Find("Cinematic Depth Of Field").GetComponent<UnityEngine.Rendering.Volume>();
                Assert.That(focusVolume.weight, Is.EqualTo(1f), "Focus must cut with the camera, without a late blur transition.");
            }
            Assert.That(controller.CurrentPage.TextKey, Is.Null.Or.Empty);
            Assert.That(stairwell.InteractionPrompt.HasHeldPage, Is.False);
            Assert.That(inspection.IsPresenting, Is.True);
            Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
            CollectionAssert.AreEqual(quests, GameSessionState.Quests);
            CollectionAssert.AreEqual(inventory, GameSessionState.InventoryItems);
            yield return null;
            CaptureCurrentCamera(Camera.main, SceneIds.StairwellInterior,
                "corrupted-06-silent-head-inspection-" + Mathf.RoundToInt(camera.aspect * 100f),
                width: Mathf.RoundToInt(camera.aspect * 720f));
            controller.Cancel();
            Assert.That(controller.CameraDirector.IsFinished, Is.True, "Closing must cut back immediately.");
            Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(gameplayCameraFov).Within(.001f));
            Assert.That(stairwell.CameraFollow.FixedFocusActive, Is.True);
            Assert.That(CinematicDepthOfField.IsActive, Is.False);
            Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False);
            Assert.That(stairwell.Player.ContactShadow.enabled, Is.EqualTo(shadowEnabled));
            foreach (var state in rendererStates)
                Assert.That(state.Key.enabled, Is.EqualTo(state.Value), "Returning camera must restore each original renderer state.");
            yield return WaitForCorruptedStairwell(() => !controller.IsActive && !inspection.IsPresenting &&
                stairwell.Player.Motor.InputEnabled && stairwell.Player.Interactor.InputEnabled,
                "Cancelling the inspection did not restore hero, camera and input ownership.");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(stairwell.InteractionPrompt.HasHeldPage, Is.False);
            Assert.That(stairwell.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(stairwell.CameraFollow.FixedFocusActive, Is.True);
            Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.ApartmentLanding));
            Assert.That(Vector3.Distance(stairwell.CameraFollow.FixedBasePosition,
                stairwell.FixedCamera.ActiveShot.Position), Is.LessThan(.001f));
            Assert.That(stairwell.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(stairwell.FixedCamera.ActiveShot.FieldOfView).Within(.001f));
            Assert.That(Vector3.Distance(stairwell.Player.GameObject.transform.position,
                inspection.Staging.Exit.RootPosition), Is.LessThan(.035f));
            CollectionAssert.AreEqual(quests, GameSessionState.Quests);
            CollectionAssert.AreEqual(inventory, GameSessionState.InventoryItems);
            // The same director can later serve an ordinary painting-style blend;
            // the opt-in cut must not survive the previous session's cleanup.
            float inspectionAspect = camera.aspect;
            try
            {
                camera.aspect = 4f / 3f;
                Assert.That(controller.CameraDirector.BeginObject(stairwell.CameraFollow, inspection.SubjectRoot,
                    head, stairwell.Player.GameObject.transform, inspection.Staging.CameraSideHint,
                    NarrativeCameraMode.ObjectCloseUp, inspection.Staging.CameraFront,
                    authoredFocusBoundsOnly: true), Is.True);
                Assert.That(controller.CameraDirector.IsSettled, Is.False);
                Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));
                controller.CameraDirector.BeginExit();
                Assert.That(controller.CameraDirector.IsReturning, Is.True, "Ordinary object inspections still blend.");
            }
            finally { controller.CameraDirector.RestoreImmediate(); camera.aspect = inspectionAspect; }

            // A bad shot must release even a lease acquired before camera admission.
            Bounds blockedHead = new Bounds(head.center + Vector3.forward, head.size);
            Assert.That(controller.CameraDirector.BeginObject(stairwell.CameraFollow, inspection.SubjectRoot,
                blockedHead, stairwell.Player.GameObject.transform, inspection.Staging.CameraSideHint,
                NarrativeCameraMode.ObjectCloseUp, inspection.Staging.CameraFront, authoredFocusBoundsOnly: true,
                cameraTransition: NarrativeCameraTransition.Cut, playerToHide: inspection.PlayerToHide), Is.False,
                "The existing descent wall must reject an obstructed lens even when the hero is hidden.");
            Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False,
                "Rejected camera framing must not leave the hero hidden.");
            Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));

            yield return WaitForCorruptedStairwell(() =>
                ReferenceEquals(stairwell.Player.Interactor.ActiveInteractable, inspection),
                "The final dock must remain in range for a repeat E inspection.");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => controller.Phase == NarrativeInteractionPhase.Reading,
                "Repeat keyboard E did not reopen the inspection from its final dock.");
            // Release the opening press and let the shared close action arm.
            yield return null;
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            Assert.That(controller.CameraDirector.IsFinished, Is.True, "The second E must cut back to gameplay.");
            Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False);
            yield return WaitForCorruptedStairwell(() => !controller.IsActive &&
                stairwell.Player.Motor.InputEnabled && stairwell.Player.Interactor.InputEnabled,
                "Keyboard close did not restore gameplay input.");

            if (inspectionAspect > 1.7f && inspectionAspect < 1.8f)
            {
                for (int frame = 0; frame < 3; frame++) yield return null;
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitForCorruptedStairwell(() => controller.Phase == NarrativeInteractionPhase.Reading,
                    "The inspection did not open for visibility cleanup verification.");
                try
                {
                    controller.enabled = false;
                    Assert.That(controller.IsActive, Is.False);
                    Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False);
                    Assert.That(stairwell.Player.ContactShadow.enabled, Is.EqualTo(shadowEnabled));
                    foreach (var state in rendererStates)
                        Assert.That(state.Key.enabled, Is.EqualTo(state.Value));
                    Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));
                }
                finally { controller.enabled = true; }
            }
        }

        private static IEnumerator CaptureCorruptedGameplay(StairwellInteriorRoot stairwell,
            string name, bool proveFog = false)
        {
            Camera camera = Camera.main;
            RenderTexture oldTarget = camera.targetTexture;
            float oldAspect = camera.aspect;
            try
            {
                foreach (int width in new[] { 1280, 960 })
                {
                    const int height = 720;
                    var target = new RenderTexture(width, height, 24);
                    target.Create();
                    camera.targetTexture = target;
                    camera.aspect = width / (float)height;
                    stairwell.CameraFollow.Snap();
                    try
                    {
                        yield return ScarfRenderedFrame(camera, () =>
                        {
                            AssertCorruptedHeroInFrame(stairwell, camera);
                            if (proveFog) AssertStairwellTerminalBeyondFog(stairwell);
                            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                            RenderTexture previous = RenderTexture.active;
                            try
                            {
                                RenderTexture.active = target;
                                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                                pixels.Apply();
                                Assert.That(IsBlank(pixels), Is.False);
                                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.StairwellInterior);
                                Directory.CreateDirectory(folder);
                                string path = Path.Combine(folder, name + (width == 960 ? "-4x3.png" : "-16x9.png"));
                                File.WriteAllBytes(path, pixels.EncodeToPNG());
                                Debug.Log("Area capture wrote " + path);
                            }
                            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
                        });
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget;
                        target.Release();
                        Object.DestroyImmediate(target);
                    }
                }
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.aspect = oldAspect;
                stairwell.CameraFollow.Snap();
            }
        }

        private static void AssertCorruptedHeroInFrame(StairwellInteriorRoot stairwell, Camera camera)
        {
            Vector3 root = stairwell.Player.GameObject.transform.position;
            foreach (Vector3 point in new[]
            {
                root + Vector3.up * FixedCameraFocus.BodyLowerHeight,
                root + Vector3.up * FixedCameraFocus.BodyUpperHeight,
                root + Vector3.up * FixedCameraFocus.BodyAimHeight + Vector3.right * .32f,
                root + Vector3.up * FixedCameraFocus.BodyAimHeight - Vector3.right * .32f
            })
            {
                Vector3 view = camera.WorldToViewportPoint(point);
                Assert.That(view.z, Is.GreaterThan(0f));
                Assert.That(view.x, Is.InRange(.02f, .98f), stairwell.FixedCamera.ActiveShotKind + " horizontal body framing");
                Assert.That(view.y, Is.InRange(.02f, .98f), stairwell.FixedCamera.ActiveShotKind + " vertical body framing");
            }
            if (Physics.Linecast(camera.transform.position, root + Vector3.up * 1.2f,
                    out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Assert.That(hit.transform == stairwell.Player.GameObject.transform ||
                    hit.transform.IsChildOf(stairwell.Player.GameObject.transform), Is.True,
                    "The gameplay camera cannot look through a wall or column at the hero: " + hit.collider.name);
        }

        private static void AssertStairwellTerminalBeyondFog(StairwellInteriorRoot stairwell)
        {
            Transform terminal = stairwell.World.Root.Find("Corrupted Endless Staircase/Distant Flight 3");
            Bounds bounds = terminal.GetComponent<Renderer>().bounds;
            Assert.That(bounds.min.z, Is.GreaterThan(StairwellInteriorAtmosphere.CorruptedHazeEndZ),
                "The entire third flight must lie beyond the local stair haze even with Bloom disabled.");
        }

        private static void AssertCorruptedTerminalInvisible(StairwellInteriorRoot stairwell)
        {
            // Inspect actual pixels, not only the configured fog numbers. A
            // hidden finite end must make no silhouette against the luminous air.
            Camera camera = Camera.main;
            Renderer terminal = stairwell.World.Root.Find("Corrupted Endless Staircase/Distant Flight 3").GetComponent<Renderer>();
            Pose pose = new Pose(camera.transform.position, camera.transform.rotation);
            float fov = camera.fieldOfView;
            RenderTexture savedTarget = camera.targetTexture, savedActive = RenderTexture.active;
            var target = new RenderTexture(640, 360, 24);
            var image = new Texture2D(640, 360, TextureFormat.RGB24, false);
            stairwell.Atmosphere.RuntimeProfile.TryGet(out FilmGrain grain);
            bool grainActive = grain.active;
            try
            {
                grain.active = false;
                camera.transform.position = stairwell.World.Root.TransformPoint(new Vector3(-1.45f, 6.6f, 1.7f));
                camera.transform.LookAt(terminal.bounds.center);
                camera.fieldOfView = 48f;
                camera.targetTexture = target;
                Color32[] Pixels()
                {
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); image.Apply();
                    return image.GetPixels32();
                }
                Color32[] shown = Pixels();
                terminal.enabled = false;
                Color32[] absent = Pixels();
                long sum = 0;
                int peak = 0;
                for (int i = 0; i < shown.Length; i++)
                {
                    int error = Math.Abs(shown[i].r - absent[i].r) + Math.Abs(shown[i].g - absent[i].g) +
                        Math.Abs(shown[i].b - absent[i].b);
                    sum += error; peak = Math.Max(peak, error);
                }
                Debug.Log($"Corrupted terminal pixel difference: mean={sum / (shown.Length * 3d):F3}, peak={peak}");
                Assert.That(sum / (shown.Length * 3d), Is.LessThan(.4d));
                Assert.That(peak, Is.LessThanOrEqualTo(12), "The finite terminal still leaves a visible edge with Bloom off.");
            }
            finally
            {
                terminal.enabled = true; grain.active = grainActive;
                camera.targetTexture = savedTarget; RenderTexture.active = savedActive;
                camera.transform.SetPositionAndRotation(pose.position, pose.rotation); camera.fieldOfView = fov;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }

        private static IEnumerator WaitForCorruptedStairwell(Func<bool> ready, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, failure);
        }

        private static IEnumerator WalkStairwellWaypoint(StairwellInteriorRoot stairwell, Vector3 local)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            Vector3 target = stairwell.World.Root.TransformPoint(local);
            bool reached = false;
            for (int frame = 0; frame < 600 && !reached; frame++)
            {
                reached = motor.MoveTowardsApproachWaypoint(target, .025f, 1f / 60f);
                Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                    "The real stairwell capsule must reach " + local + "; stopped at " + motor.transform.position);
                yield return null;
            }
            Assert.That(reached, Is.True, "The stairwell route must reach " + local);
            motor.CancelInteractionPoseMove();
        }
    }
}
