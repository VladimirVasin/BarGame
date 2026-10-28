using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class
        StairwellInteriorPresentationPlayModeTests
    {
        private const string RootName =
            "[Bar Promenade] Stairwell Interior Runtime";
        private const float TimeoutSeconds = 15f;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private bool previousMovementFocus;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousMovementFocus = GameInput.MovementFocused;
            GameInput.HandleApplicationFocus(true);
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ConsumeStairwellArrival();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ConsumeStairwellArrival();
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            keyboard = null;
            inputFixture?.TearDown();
            inputFixture = null;
            GameInput.HandleApplicationFocus(previousMovementFocus);
            yield return null;
        }

        [UnityTest]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator Scene_BootstrapsVerticalRouteAndAtmosphere()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.ApartmentDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.StairwellInterior));
            Assert.That(root.PauseMenu, Is.Not.Null);
            Assert.That(root.PauseMenu.IsInitialized, Is.True);
            Assert.That(root.Inventory, Is.Not.Null);
            Assert.That(root.Inventory.IsInitialized, Is.True);
            Assert.That(root.InteractionPrompt, Is.Not.Null);
            Assert.That(root.IntoxicationHud, Is.Not.Null);
            Assert.That(root.TargetInteraction, Is.Not.Null);
            Assert.That(root.TargetInteraction.IsInitialized, Is.True);
            Assert.That(root.AnimatedInteraction, Is.Not.Null);
            Assert.That(root.AnimatedInteraction.IsInitialized, Is.True);
            Assert.That(
                GameAudioMixer.CurrentProfile,
                Is.EqualTo(GameAudioProfile.Stairwell));
            Assert.That(
                root.Arrival,
                Is.EqualTo(
                    StairwellArrivalKind.ApartmentDoor));
            Assert.That(
                GameSessionState.StairwellArrival,
                Is.EqualTo(StairwellArrivalKind.StreetDoor));
            Assert.That(
                Vector3.Distance(
                    root.Player.GameObject.transform.position,
                    root.Layout.ApartmentSpawn),
                Is.LessThan(0.05f));
            Assert.That(
                root.World.StairColliders,
                Has.Count.EqualTo(3));
            Assert.That(root.World.UpperBlocker, Is.Not.Null);
            Assert.That(
                root.World.UpperBlocker.enabled,
                Is.True);
            Assert.That(
                root.World.UpperBlocker.isTrigger,
                Is.False);
            Assert.That(
                root.World.UpperBlocker.bounds.min.x,
                Is.LessThanOrEqualTo(
                    root.Layout.UpperFlightBounds.xMin));
            Assert.That(
                root.World.UpperBlocker.bounds.max.x,
                Is.GreaterThanOrEqualTo(
                    root.Layout.UpperFlightBounds.xMax));

            Assert.That(root.StreetExit, Is.Not.Null);
            Assert.That(
                root.StreetExit.PromptKey,
                Is.EqualTo("interaction.exit_building"));
            Assert.That(root.ApartmentEntrance, Is.Not.Null);
            Assert.That(
                root.ApartmentEntrance.PromptKey,
                Is.EqualTo("interaction.enter_apartment"));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));

            Assert.That(root.Atmosphere, Is.Not.Null);
            Assert.That(
                root.Atmosphere.IsInitialized,
                Is.True);
            Assert.That(
                root.Atmosphere.PracticalLights,
                Has.Count.EqualTo(
                    StairwellInteriorAtmosphere
                        .MaximumPracticalLights));
            Assert.That(
                root.Atmosphere.Dust.main.maxParticles,
                Is.EqualTo(
                    StairwellInteriorAtmosphere
                        .MaximumDustParticles));
            Assert.That(
                root.Atmosphere.GetComponentsInChildren<
                    StairwellLightFlicker>(),
                Has.Length.EqualTo(3));
            AssertPracticalSources(root);
            AssertAuthoredStairwell(root);
            Assert.That(root.Ambience, Is.Not.Null);
            Assert.That(root.Ambience.Source.loop, Is.True);
            Assert.That(
                root.Ambience.ActiveClip.name,
                Is.EqualTo("RetroAmbience_Stairwell"));
            Assert.That(root.Soundscape, Is.Not.Null);
            Assert.That(root.Soundscape.IsInitialized, Is.True);
            Assert.That(
                root.Soundscape.GetComponentsInChildren<
                    AudioSource>(true),
                Has.Length.EqualTo(
                    StairwellSoundscape.OwnedSourceCount));
            // Hero effects also live under Runtime UI. Exclude their concrete
            // owner when checking the room's independent audio budget.
            Transform vomitHost = root.IntoxicationStatus.VomitHost;
            var environmentSources = new List<AudioSource>();
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                if (!source.transform.IsChildOf(root.Player.GameObject.transform) &&
                    (vomitHost == null || !source.transform.IsChildOf(vomitHost)))
                    environmentSources.Add(source);
            Assert.That(
                environmentSources,
                Has.Count.EqualTo(
                    2 +
                    StairwellSoundscape.OwnedSourceCount),
                "Stairwell audio must remain one music source, " +
                "one base ambience source and three soundscape " +
                "sources.");
            Assert.That(root.Music, Is.Not.Null);
            Assert.That(root.Music.Source, Is.Not.Null);
            Assert.That(root.Music.Source.loop, Is.True);
            Assert.That(
                root.Music.Source.playOnAwake,
                Is.False);
            Assert.That(
                root.Music.Source.spatialBlend,
                Is.Zero);
            Assert.That(root.Music.ToneFilter, Is.Not.Null);
            Assert.That(
                root.Music.transform.IsChildOf(root.transform),
                Is.True);
            Assert.That(
                StairwellMusicPlayer.ResourcePath,
                Is.EqualTo(
                    "Audio/StairwellMusic/" +
                    "stairwell_theme"));
            Assert.That(root.Cat, Is.Not.Null);
            Assert.That(root.Cat.IsInitialized, Is.True);
            Assert.That(root.Cat.Renderer, Is.Not.Null);
            Assert.That(root.Cat.Anchors, Is.Not.Null);
            Assert.That(root.Cat.Anchors.IsBound, Is.True);
            // The Cheshire grin exists in the scene but not on the
            // screen: hidden by default until a script asks for it.
            Assert.That(root.CatGrin, Is.Not.Null);
            Assert.That(root.CatGrin.IsGrinVisible, Is.False);
            Assert.That(root.CatGrin.GrinProgress, Is.Zero);
            Assert.That(
                root.Cat.Anchors.GrinRenderer.enabled,
                Is.False);
            Assert.That(root.CatInteraction, Is.Not.Null);
            Assert.That(
                root.CatInteraction.IsInitialized,
                Is.True);
            Assert.That(root.CatTrigger, Is.Not.Null);
            Assert.That(root.CatTrigger.isTrigger, Is.True);
            Assert.That(
                root.Cat.transform.localPosition,
                Is.EqualTo(root.CatPlan.VisualLocalPosition));
            Assert.That(
                root.CatInteraction.InteractionPosition,
                Is.EqualTo(
                    root.transform.TransformPoint(
                        root.CatPlan.InteractionLocalPosition)));
            Assert.That(root.CameraFollow, Is.Not.Null);
            Assert.That(root.FixedCamera, Is.Not.Null);
            Assert.That(
                root.FixedCamera.IsInitialized,
                Is.True);
            Assert.That(
                root.CameraFollow.FixedPoseActive,
                Is.True);
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind
                        .ApartmentLanding));
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Apartment Fluorescent Tube"));
            AssertCatVisible(Camera.main, root.Cat);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    CityMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    BarMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    StairwellMusicPlayer>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    StairwellCatActor>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    StairwellCatInteraction>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator
            Scene_PlayerMotorCrossesGroundFloorSeamWithForwardInput()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.GroundFlight));
            AssertCatVisible(Camera.main, root.Cat);
            Vector3 start =
                root.Player.GameObject.transform.position;
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            float deadline =
                Time.realtimeSinceStartup + 4f;
            while (root.Player.GameObject.transform.position.y <
                       0.62f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;

            Vector3 reached =
                root.Player.GameObject.transform.position;
            Assert.That(
                reached.z,
                Is.GreaterThan(start.z + 1.2f),
                "PlayerMotor did not cross the lobby/flight seam.");
            Assert.That(
                reached.y,
                Is.GreaterThan(0.60f),
                "PlayerMotor reached the first flight but did not climb it.");
        }

        [UnityTest]
        public IEnumerator
            Scene_PlayerMotorDescendsLowerFlightAcrossCameraCut()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.ApartmentDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            GameSessionState.TryCompleteQuest(QuestId.FeedTheCat);
            root.Player.Motor.Teleport(
                new Vector3(-1.45f, 1.74f, 1.30f));
            // Tank controls walk the hero's own forward axis, so the
            // descent aims him down the lower flight before W is held.
            root.Player.GameObject.transform.rotation =
                Quaternion.Euler(0f, 180f, 0f);
            yield return null;
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.MiddleFlight));

            // Pin the frame step so the eight seconds below are eight
            // seconds of walking rather than however many frames the
            // machine can push through them. Unpinned, batch mode runs
            // frames far faster than real time and the hero covers the
            // flight even while crawling — which is exactly how a real
            // obstruction hid here: the upper-flight debris used to graze
            // his head on the top treads, and every contact cost him all
            // his speed. Held to a real stride, this test measures the
            // descent a player would make, and fails if he is scraping
            // something again.
            Vector3 position =
                root.Player.GameObject.transform.position;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                inputFixture.Press(
                    keyboard.wKey,
                    queueEventOnly: true);
                float deadline = Time.realtimeSinceStartup + 8f;
                while (position.y > 0.30f &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    position =
                        root.Player.GameObject.transform.position;
                }

                inputFixture.Release(
                    keyboard.wKey,
                    queueEventOnly: true);
                yield return null;
            }
            finally
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
            }

            Assert.That(
                position.y,
                Is.LessThanOrEqualTo(0.30f),
                "The player stalled while descending the lower " +
                $"flight; final position {position}, active shot " +
                $"{root.FixedCamera.ActiveShotKind}.");
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.GroundFlight));
        }

        [UnityTest]
        public IEnumerator
            Scene_PlayerClimbsToApartmentButUpperDebrisBlocks()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            root.Player.Motor.SetInputEnabled(false);
            root.Player.Motor.enabled = false;
            CharacterController controller =
                root.Player.GameObject.GetComponent<
                    CharacterController>();
            Assert.That(controller, Is.Not.Null);
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Ground Fluorescent Tube"));

            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, -2.92f));
            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, 0.70f));
            Assert.That(
                controller.transform.position.y,
                Is.GreaterThan(1.42f));

            yield return MoveControllerTo(
                controller,
                new Vector2(1.45f, 1.28f));
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.MiddleFlight));
            AssertCatVisible(Camera.main, root.Cat);
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Middle Fluorescent Tube"));
            yield return MoveControllerTo(
                controller,
                new Vector2(1.45f, 0.70f));
            yield return MoveControllerTo(
                controller,
                new Vector2(1.45f, -2.92f));
            Assert.That(
                controller.transform.position.y,
                Is.GreaterThan(3.0f));

            yield return MoveControllerTo(
                controller,
                new Vector2(3.12f, -3.52f));
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind
                        .ApartmentLanding));
            AssertCatVisible(Camera.main, root.Cat);
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Apartment Fluorescent Tube"));
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        controller.transform.position.x,
                        controller.transform.position.z),
                    new Vector2(3.12f, -3.52f)),
                Is.LessThan(0.16f));

            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, -3.52f));
            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, -2.96f));
            for (int index = 0; index < 90; index++)
            {
                controller.Move(
                    new Vector3(0f, -0.08f, 0.055f));
                yield return null;
            }

            Bounds blocker =
                root.World.UpperBlocker.bounds;
            Assert.That(
                controller.transform.position.z,
                Is.LessThanOrEqualTo(
                    blocker.min.z -
                    controller.radius +
                    0.08f),
                "The player squeezed through the upper debris.");
        }

        [UnityTest]
        public IEnumerator
            Scene_CatTalkChoiceShowsExistingResponseAndRestoresPlayer()
        {
            GameSessionState.TryActivateQuest(QuestId.FeedTheCat);
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");

            root.Player.Motor.Teleport(
                root.transform.TransformPoint(
                    root.CatPlan.InteractionLocalPosition));
            Physics.SyncTransforms();
            yield return WaitUntil(
                () => ReferenceEquals(
                    root.Player.Interactor.ActiveInteractable,
                    root.CatInteraction),
                "The cat did not become the active interaction.");

            InteractionPromptView prompt =
                root.GetComponentInChildren<
                    InteractionPromptView>();
            Assert.That(prompt, Is.Not.Null);
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
            Assert.That(prompt.IsClickable, Is.True);

            Assert.That(prompt.TryInvokePrompt(), Is.True);
            yield return null;

            Assert.That(
                root.TargetInteraction.IsOpen,
                Is.True);
            Assert.That(
                root.TargetInteraction.State,
                Is.EqualTo(
                    InventoryTargetInteractionState.Choice));
            Assert.That(
                root.TargetInteraction.SelectedChoice,
                Is.EqualTo(
                    InventoryTargetInteractionChoice.Talk));
            Assert.That(root.Player.Motor.InputEnabled, Is.False);
            Assert.That(root.Player.Interactor.InputEnabled, Is.False);

            Assert.That(
                root.TargetInteraction.Confirm(),
                Is.True);
            yield return null;

            Assert.That(root.TargetInteraction.IsOpen, Is.False);
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(prompt.IsFeedbackVisible, Is.True);
            Assert.That(prompt.IsClickable, Is.False);
        }

        [UnityTest]
        public IEnumerator
            Scene_CatInteractionWithoutStewShowsMissingMessage()
        {
            GameSessionState.TryActivateQuest(QuestId.FeedTheCat);
            while (GameSessionState.TryRemoveInventoryItem(
                       InventoryItemId.OpenStewCan))
            {
            }

            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");

            root.Player.Motor.Teleport(
                root.transform.TransformPoint(
                    root.CatPlan.InteractionLocalPosition));
            Physics.SyncTransforms();
            yield return WaitUntil(
                () => ReferenceEquals(
                    root.Player.Interactor.ActiveInteractable,
                    root.CatInteraction),
                "The cat did not become the active interaction.");

            Assert.That(
                root.CatInteraction.TryOpen(
                    root.Player.Interactor),
                Is.True);
            Assert.That(
                root.TargetInteraction.SelectChoice(
                    InventoryTargetInteractionChoice.Interact),
                Is.True);
            Assert.That(
                root.TargetInteraction.Confirm(),
                Is.True);
            yield return null;

            Assert.That(root.TargetInteraction.IsOpen, Is.False);
            Assert.That(
                root.InteractionPrompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction
                        .MissingStewResponsePromptKey));
            Assert.That(root.InteractionPrompt.IsClickable, Is.False);
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            yield return null;

            if (Application.isBatchMode)
            {
                // Batch mode has no game view, so OnGUI never runs and the
                // prompt can never report a rendered layout. Everything
                // above — the refused feeding, the missing-stew prompt key
                // and the restored input — has already been asserted and
                // would have failed this test headless. Only the IMGUI
                // panel measurements below are unverifiable.
                Assert.Ignore(
                    "IMGUI layout cannot be exercised in batch mode (no " +
                    "game view, so OnGUI never runs); the interaction " +
                    "assertions above did run. Batchmode-environment " +
                    "limit, stash-verified 2026-08-23; see work-log " +
                    "latent.");
            }

            InteractionPromptView prompt = root.InteractionPrompt;
            Assert.That(prompt.HasRenderedLayout, Is.True);
            Assert.That(
                prompt.LastRenderedText,
                Is.EqualTo(
                    LocalizationService.Get(
                        StairwellCatInteraction
                            .MissingStewResponsePromptKey)));
            Assert.That(
                prompt.LastRenderedPanelRect.width,
                Is.GreaterThan(
                    InteractionPromptView.MinimumPanelWidth),
                "The hungry-cat feedback must expand beyond the legacy panel.");
            Assert.That(prompt.LastRenderedTextFits, Is.True);
            Assert.That(
                prompt.LastRenderedPanelRect.xMin,
                Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                prompt.LastRenderedPanelRect.xMax,
                Is.LessThanOrEqualTo(RetroUiTheme.LogicalWidth));
            Assert.That(
                prompt.LastRenderedPanelRect.yMin,
                Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                prompt.LastRenderedPanelRect.yMax,
                Is.LessThanOrEqualTo(RetroUiTheme.LogicalHeight));
        }

        [UnityTest]
        public IEnumerator
            Scene_FeedingConsumesOneStewAndCompletesPairedAnimation()
        {
            GameSessionState.TryActivateQuest(QuestId.FeedTheCat);
            while (GameSessionState.TryRemoveInventoryItem(
                       InventoryItemId.OpenStewCan))
            {
            }

            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");

            Assert.That(
                root.CatInteraction.PlayerFeedingDefinition.EnterClipName,
                Is.EqualTo("CatFeedEnter"));
            Assert.That(
                root.CatInteraction.PlayerFeedingDefinition.LoopClipName,
                Is.EqualTo("CatFeedLoop"));
            Assert.That(
                root.CatInteraction.PlayerFeedingDefinition.ExitClipName,
                Is.EqualTo("CatFeedExit"));
            Assert.That(root.CatInteraction.FeedingCanProp, Is.Not.Null);
            Assert.That(
                root.CatInteraction.FeedingCanProp.gameObject.activeSelf,
                Is.False);

            StairwellCatFeedingPlan feedingPlan =
                root.CatFeedingPlan;
            Vector3 positioningStartLocal =
                feedingPlan.EntryRootLocalPosition -
                (feedingPlan.EntryFacingLocalDirection * 0.30f);
            Vector3 positioningStartWorld =
                root.transform.TransformPoint(positioningStartLocal);
            Quaternion positioningStartRotation =
                root.transform.rotation *
                Quaternion.LookRotation(
                    -feedingPlan.EntryFacingLocalDirection,
                    Vector3.up);
            Vector3 entryWorldPosition =
                root.transform.TransformPoint(
                    feedingPlan.EntryRootLocalPosition);
            Quaternion entryWorldRotation =
                root.transform.rotation *
                feedingPlan.EntryLocalRotation;
            Vector3 exitWorldPosition =
                root.transform.TransformPoint(
                    feedingPlan.ExitRootLocalPosition);
            Quaternion exitWorldRotation =
                root.transform.rotation *
                feedingPlan.ExitLocalRotation;

            root.Player.Motor.Teleport(
                entryWorldPosition +
                Vector3.up *
                (PlayerMotor.InteractionVerticalTolerance + 0.01f));
            Physics.SyncTransforms();
            Assert.That(
                root.CatInteraction.CanInteract(
                    root.Player.Interactor),
                Is.False,
                "The feeding UI must not open from a height the guided " +
                "entry cannot reach.");

            root.Player.Motor.Teleport(positioningStartWorld);
            root.Player.GameObject.transform.rotation =
                positioningStartRotation;
            Physics.SyncTransforms();
            yield return WaitUntil(
                () => ReferenceEquals(
                    root.Player.Interactor.ActiveInteractable,
                    root.CatInteraction),
                "The cat did not become the active interaction.");

            // Optional anatomy covered by the authored outfit is already off.
            // Feeding must preserve the exact world-rig selection throughout.
            var originalRendererStates = new Dictionary<Renderer, bool>();
            foreach (Renderer renderer in root.Player.Visual.Renderers)
            {
                Assert.That(renderer, Is.Not.Null);
                originalRendererStates.Add(renderer, renderer.enabled);
            }
            Assert.That(originalRendererStates.Values, Does.Contain(true),
                "The pre-feeding snapshot must include the visible world hero.");

            Assert.That(
                root.CatInteraction.TryOpen(
                    root.Player.Interactor),
                Is.True);
            root.TargetInteraction.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            Assert.That(
                root.TargetInteraction.Confirm(),
                Is.True);
            Assert.That(
                root.TargetInteraction.State,
                Is.EqualTo(
                    InventoryTargetInteractionState.Confirmation));
            Assert.That(
                root.TargetInteraction.ConfirmationYesSelected,
                Is.False);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));

            Vector3 positionBeforeConfirmation =
                root.Player.GameObject.transform.position;
            Quaternion rotationBeforeConfirmation =
                root.Player.GameObject.transform.rotation;
            root.TargetInteraction.SelectConfirmation(true);
            Assert.That(
                root.TargetInteraction.Confirm(),
                Is.True);

            Assert.That(root.TargetInteraction.IsExecuting, Is.True);
            Assert.That(root.AnimatedInteraction.IsActive, Is.True);
            Assert.That(
                root.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.Zero);
            Assert.That(
                Vector3.Distance(
                    root.Player.GameObject.transform.position,
                    positionBeforeConfirmation),
                Is.LessThan(0.001f),
                "Confirming feeding must not teleport the ordinary player " +
                "to the authored entry point.");
            Assert.That(
                Quaternion.Angle(
                    root.Player.GameObject.transform.rotation,
                    rotationBeforeConfirmation),
                Is.LessThan(0.01f),
                "Confirming feeding must preserve the non-target facing " +
                "until visible positioning begins.");
            Assert.That(
                Vector3.Distance(
                    root.Player.GameObject.transform.position,
                    entryWorldPosition),
                Is.GreaterThan(0.20f),
                "The test must observe a material automatic approach.");
            Assert.That(
                Quaternion.Angle(
                    root.Player.GameObject.transform.rotation,
                    entryWorldRotation),
                Is.GreaterThan(90f),
                "The test must begin with a materially non-target facing.");
            AssertRigRendererState(root, originalRendererStates);
            Assert.That(root.Player.ContactShadow.enabled, Is.True);
            Assert.That(
                root.CatInteraction.FeedingCanProp.gameObject.activeSelf,
                Is.False);
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.MiddleFlight));

            yield return WaitUntil(
                () => root.AnimatedInteraction.Phase ==
                    PlayerAnimatedInteractionPhase.Entering,
                "The player did not finish automatic cat-feeding " +
                "positioning.");

            Assert.That(
                Vector3.Distance(
                    root.Player.GameObject.transform.position,
                    entryWorldPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    root.Player.GameObject.transform.rotation,
                    entryWorldRotation),
                Is.LessThan(0.01f));
            AssertContinuous3DPresentation(
                root,
                "CatFeedEnter",
                originalRendererStates);
            Assert.That(
                root.CatInteraction.FeedingCanProp.gameObject.activeSelf,
                Is.True);
            Assert.That(
                root.Cat.IsFeeding,
                Is.False,
                "The cat track must wait for the player's loop boundary.");

            yield return WaitUntil(
                () => root.Cat.IsFeeding,
                "The cat feeding animation did not start with the " +
                "player loop.");
            Assert.That(
                root.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            AssertContinuous3DPresentation(
                root,
                "CatFeedLoop",
                originalRendererStates);
            Assert.That(
                root.CatInteraction.FeedingCanProp.gameObject.activeSelf,
                Is.True);
            Assert.That(
                root.Cat.CurrentFeedingFrame,
                Is.InRange(
                    0,
                    StairwellCatFeedingTimeline
                        .FrameCount - 1));
            Bounds heldCanBounds = ComputeRendererBounds(
                root.CatInteraction.FeedingCanProp);
            Assert.That(
                heldCanBounds.size.magnitude,
                Is.LessThan(0.30f),
                "The held tin must stay tin-sized: the grip bone " +
                "carries a 100x FBX scale the prop root must cancel.");
            Assert.That(
                Vector3.Distance(
                    heldCanBounds.center,
                    root.Cat.Anchors.MuzzleAnchor.position),
                Is.LessThan(0.40f),
                "The offered tin must sit at the feeding cat's muzzle.");
            AssertCatVisible(Camera.main, root.Cat);
            Vector3 playerViewport =
                Camera.main.WorldToViewportPoint(
                    ((Player3DCharacterPresentation)root.Player.Visual)
                        .Registry.Anchors.Pelvis.position);
            Assert.That(playerViewport.z, Is.GreaterThan(0f));
            Assert.That(playerViewport.x, Is.InRange(0.05f, 0.95f));
            Assert.That(playerViewport.y, Is.InRange(0.05f, 0.95f));
            Vector3 catViewport =
                Camera.main.WorldToViewportPoint(
                    root.Cat.Renderer.bounds.center);
            Assert.That(
                catViewport.x,
                Is.LessThan(playerViewport.x),
                "The orientation contract assumes the cat is camera-left " +
                "of the feeding hero.");

            yield return WaitUntil(
                () => !root.TargetInteraction.IsOpen,
                "The paired feeding animation did not finish.");

            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.OpenStewCan), Is.Zero);
            Assert.That(GameSessionState.GetQuestStatus(QuestId.FeedTheCat), Is.EqualTo(QuestStatus.Completed));
            Assert.That(root.AnimatedInteraction.IsActive, Is.False);
            Assert.That(root.Cat.IsFeeding, Is.False);
            Assert.That(root.Cat.IsFeedingPrepared, Is.False);
            Assert.That(root.CatInteraction.OwnsExecution, Is.False);
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(
                Vector3.Distance(
                    root.Player.GameObject.transform.position,
                    exitWorldPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    root.Player.GameObject.transform.rotation,
                    exitWorldRotation),
                Is.LessThan(0.01f));
            AssertRigRendererState(root, originalRendererStates);
            Assert.That(root.Player.ContactShadow.enabled, Is.True);
            Assert.That(
                ((IPlayerClipPresentation)root.Player.Visual)
                    .IsClipActive,
                Is.False);
            Assert.That(
                root.CatInteraction.FeedingCanProp.gameObject.activeSelf,
                Is.False);
        }

        private static void AssertAuthoredStairwell(StairwellInteriorRoot root)
        {
            StairwellInteriorModelLibrary library = StairwellInteriorModelLibrary.Load();
            Assert.That(library.BuildSignature, Has.Length.EqualTo(64));
            Assert.That(library.Parts.Count, Is.LessThanOrEqualTo(128));
            int visibleCount = 0;
            foreach (MeshRenderer renderer in root.World.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue; // Hidden plan-owned ramp/safety proxies.
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null, renderer.name);
                Assert.That(filter.sharedMesh.name, Does.StartWith("StairwellPart_"), renderer.name);
                Bounds bounds = filter.sharedMesh.bounds;
                Assert.That(Vector3.Distance(bounds.center, Vector3.zero), Is.LessThan(.0001f), renderer.name);
                Assert.That(bounds.size.x, Is.EqualTo(1f).Within(.001f), renderer.name);
                Assert.That(bounds.size.z, Is.EqualTo(1f).Within(.001f), renderer.name);
                visibleCount++;
            }
            Assert.That(visibleCount, Is.InRange(1, 300));
            // The imported shape and the retained authoritative proxy must still
            // occupy the same actual metres after the FBX root's unit factor.
            Transform floor = root.World.Root.Find("Stairwell Ground Floor");
            Assert.That(floor.GetComponent<Renderer>().bounds.size.x,
                Is.EqualTo(root.Layout.RoomSize.x).Within(.001f));
            Assert.That(floor.GetComponent<Renderer>().bounds.size.z,
                Is.EqualTo(root.Layout.RoomSize.y).Within(.001f));
            Transform tread = root.World.Root.Find("Lower Stair 16");
            Assert.That(tread.GetComponent<Renderer>().bounds.max.y,
                Is.EqualTo(root.Layout.MiddleElevation).Within(.001f));
            Assert.That(tread.Find(FootProbeSurface.ProbeChildName).GetComponent<BoxCollider>().size,
                Is.EqualTo(Vector3.one));
            Assert.That(root.World.Root.Find("Apartment Door/Apartment Door Leaf")
                .GetComponent<Renderer>().bounds.size.y, Is.EqualTo(2.32f).Within(.001f));
            AssertAuthoredRailAndHardwareContacts(root);
        }

        private static void AssertAuthoredRailAndHardwareContacts(StairwellInteriorRoot root)
        {
            var meshes = new Dictionary<Mesh, (Vector3[] vertices, int[] triangles)>();
            void Contains(Transform shape, Vector3 worldPoint, string contact)
            {
                Assert.That(shape, Is.Not.Null, contact);
                Mesh mesh = shape.GetComponent<MeshFilter>().sharedMesh;
                if (!meshes.TryGetValue(mesh, out var data))
                {
                    data = (mesh.vertices, mesh.triangles);
                    meshes.Add(mesh, data);
                }
                Vector3 p = shape.InverseTransformPoint(worldPoint);
                double angle = 0;
                for (int i = 0; i < data.triangles.Length; i += 3)
                {
                    Vector3 a = data.vertices[data.triangles[i]] - p;
                    Vector3 b = data.vertices[data.triangles[i + 1]] - p;
                    Vector3 c = data.vertices[data.triangles[i + 2]] - p;
                    double la = a.magnitude, lb = b.magnitude, lc = c.magnitude;
                    angle += 2 * Math.Atan2(Vector3.Dot(a, Vector3.Cross(b, c)),
                        la * lb * lc + Vector3.Dot(a, b) * lc + Vector3.Dot(b, c) * la + Vector3.Dot(c, a) * lb);
                }
                // Generalized winding also handles the saddle/neck overlap;
                // bounds and odd/even rays cannot prove these union contacts.
                Assert.That(Math.Abs(angle), Is.GreaterThan(Math.PI * 2), contact);
            }
            string[] labels = { "Lower", "Apartment", "Upper" };
            StairwellFlightPlan[] flights = { root.Layout.LowerFlight, root.Layout.ApartmentFlight, root.Layout.UpperFlight };
            for (int f = 0; f < flights.Length; f++)
            {
                StairwellFlightPlan flight = flights[f];
                float rise = flight.TopElevation - flight.BaseElevation;
                float cosine = flight.RunLength / Mathf.Sqrt(flight.RunLength * flight.RunLength + rise * rise);
                foreach (int side in new[] { -1, 1 })
                {
                    string suffix = side < 0 ? "A" : "B";
                    Transform wood = root.World.Root.Find(labels[f] + " Handrail " + suffix);
                    Transform supports = root.World.Root.Find(labels[f] + " Handrail " + suffix + " Supports");
                    Vector2 lateral = new Vector2(-flight.Direction.y, flight.Direction.x) *
                        (flight.Width * .5f + .055f) * side;
                    for (int i = 0; i < flight.StepCount; i++)
                        foreach (int end in new[] { -1, 1 })
                        {
                            float along = (i + .5f) * flight.StepDepth + end * .095f;
                            Vector2 planar = flight.Start + flight.Direction * along + lateral;
                            Vector3 foot = new Vector3(planar.x,
                                flight.BaseElevation + (i + 1) * flight.StepRise + .68f - .015f, planar.y);
                            Vector3 head = new Vector3(planar.x,
                                flight.BaseElevation + 1.02f + rise / flight.RunLength * along - .0475f / cosine + .006f, planar.y);
                            foot = root.World.Root.TransformPoint(foot); head = root.World.Root.TransformPoint(head);
                            Transform metal = root.World.Root.Find(labels[f] + " Rail Curb " + (i + 1).ToString("00") + " " + suffix);
                            string contact = labels[f] + " " + suffix + " post " + i + "/" + end;
                            Contains(metal, foot, contact + " metal");
                            Contains(supports, foot, contact + " support foot");
                            Contains(supports, head, contact + " support head");
                            Contains(wood, head, contact + " wood");
                        }
                }
            }
            foreach (string name in new[] { "Middle Landing Back Rail", "Apartment Landing Center Rail",
                "Apartment Landing Right Rail", "Apartment Landing Left Edge" })
            {
                Transform metal = root.World.Root.Find(name);
                Transform cap = root.World.Root.Find(name + " Top");
                Vector3 size = metal.localScale;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 local = size.x > size.z
                        ? new Vector3(side * (.5f - .0225f / size.x), .5f - .02f / size.y, 0f)
                        : new Vector3(0f, .5f - .02f / size.y, side * (.5f - .0225f / size.z));
                    Vector3 point = metal.TransformPoint(local);
                    Contains(metal, point, name + " post");
                    Contains(cap, point, name + " wood cap");
                }
            }
            foreach (string name in new[] { "Street", "Apartment" })
            {
                Transform door = name == "Street" ? root.World.StreetDoor : root.World.ApartmentDoor;
                Transform hardware = door.Find(name + " Door Hardware");
                Vector3 point = hardware.position + hardware.rotation * new Vector3(.090f, 0f, -.0365f);
                Contains(hardware, point, name + " backplate");
                Contains(door.Find(name + " Door Leaf"), point, name + " backplate into leaf");
                Assert.That(hardware.GetComponent<Collider>(), Is.Null);
            }
            Transform pull = root.World.Root.Find("Stairwell Dressing/Electrical Cabinet Pull");
            Vector3 pullContact = pull.position + pull.rotation * new Vector3(0f, 0f, -.0165f);
            Contains(pull, pullContact, "Cabinet pull");
            Contains(root.World.Root.Find("Stairwell Dressing/Ground Electrical Cabinet Door"), pullContact, "Cabinet pull into leaf");
        }

        private static Bounds ComputeRendererBounds(Transform root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Assert.That(
                renderers,
                Is.Not.Empty,
                "The feeding can prop must have renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void AssertPracticalSources(
            StairwellInteriorRoot root)
        {
            string[] labels =
            {
                "Ground",
                "Middle",
                "Apartment"
            };
            for (int index = 0; index < labels.Length; index++)
            {
                string prefix =
                    "Stairwell Dressing/" + labels[index];
                Transform tube = root.World.Root.Find(
                    prefix + " Fluorescent Tube");
                Transform housing = root.World.Root.Find(
                    prefix + " Fluorescent Housing");
                Transform halo = root.World.Root.Find(
                    prefix + " Fluorescent Halo");
                Assert.That(tube, Is.Not.Null);
                Assert.That(housing, Is.Not.Null);
                Assert.That(halo, Is.Not.Null);
                Renderer tubeRenderer =
                    tube.GetComponent<Renderer>();
                Renderer housingRenderer =
                    housing.GetComponent<Renderer>();
                Assert.That(tubeRenderer.enabled, Is.True);
                Assert.That(
                    housingRenderer.bounds.min.y,
                    Is.GreaterThan(
                        tubeRenderer.bounds.max.y),
                    $"{labels[index]} fixture housing hides its tube.");
                Assert.That(
                    halo.GetComponent<CityLightHalo>(),
                    Is.Not.Null);

                Light light =
                    root.Atmosphere.PracticalLights[index];
                Assert.That(light.enabled, Is.True);
                Assert.That(
                    light.type,
                    Is.EqualTo(LightType.Point));
                Assert.That(
                    light.intensity,
                    Is.GreaterThanOrEqualTo(4.0f));
                Assert.That(
                    light.range,
                    Is.GreaterThanOrEqualTo(5.8f));
                Assert.That(
                    Vector3.Distance(
                        light.transform.position,
                        tube.position),
                    Is.LessThan(0.22f));
            }
        }

        private static void AssertEmitterVisible(
            Camera camera,
            Transform emitter)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(emitter, Is.Not.Null);
            Vector3 viewport =
                camera.WorldToViewportPoint(emitter.position);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.05f, 0.95f));
            Assert.That(
                viewport.y,
                Is.InRange(0.05f, 0.95f));
        }

        private static void AssertCatVisible(
            Camera camera,
            StairwellCatActor cat)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(cat, Is.Not.Null);
            Assert.That(cat.Renderer, Is.Not.Null);
            Vector3 target = cat.Renderer.bounds.center;
            Vector3 viewport =
                camera.WorldToViewportPoint(
                    target);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.05f, 0.95f));
            Assert.That(
                viewport.y,
                Is.InRange(0.05f, 0.95f));

            Vector3 toCat =
                target - camera.transform.position;
            bool occluded = Physics.Raycast(
                camera.transform.position,
                toCat.normalized,
                out RaycastHit hit,
                Mathf.Max(0f, toCat.magnitude - 0.04f),
                ~0,
                QueryTriggerInteraction.Ignore);
            Assert.That(
                occluded,
                Is.False,
                occluded && hit.collider != null
                    ? $"Cat is hidden by {hit.collider.name}."
                    : "Cat is hidden in its fixed-camera shot.");
        }

        private static void AssertRigRendererState(
            StairwellInteriorRoot root,
            IReadOnlyDictionary<Renderer, bool> originalStates)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Player.Visual, Is.Not.Null);
            Assert.That(
                root.Player.Visual.Renderers,
                Is.Not.Empty);
            Assert.That(root.Player.Visual.Renderers.Count, Is.EqualTo(originalStates.Count),
                "Feeding must retain the original world-rig renderers.");
            for (int index = 0;
                 index < root.Player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer =
                    root.Player.Visual.Renderers[index];
                Assert.That(renderer, Is.Not.Null);
                Assert.That(originalStates.TryGetValue(renderer, out bool expectedEnabled), Is.True,
                    "Feeding replaced a world-rig renderer.");
                Assert.That(
                    renderer.enabled,
                    Is.EqualTo(expectedEnabled),
                    $"Player rig renderer {index} ({renderer.name}) differs from its pre-feeding state.");
            }
        }

        private static void AssertContinuous3DPresentation(
            StairwellInteriorRoot root,
            string expectedClip,
            IReadOnlyDictionary<Renderer, bool> originalStates)
        {
            Assert.That(root.Player.ContactShadow.enabled, Is.True);
            AssertRigRendererState(root, originalStates);
            IPlayerClipPresentation clips =
                (IPlayerClipPresentation)root.Player.Visual;
            Assert.That(clips.IsClipActive, Is.True);
            Assert.That(clips.ActiveClipName, Is.EqualTo(expectedClip));
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<StairwellInteriorRoot> capture)
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.StairwellInterior),
                Is.True,
                "StairwellInterior must be enabled in Build Settings.");
            AsyncOperation operation =
                SceneManager.LoadSceneAsync(
                    SceneIds.StairwellInterior,
                    LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.name == SceneIds.StairwellInterior)
                {
                    GameObject[] roots =
                        scene.GetRootGameObjects();
                    for (int index = 0;
                         index < roots.Length;
                         index++)
                    {
                        if (roots[index].name != RootName)
                        {
                            continue;
                        }

                        StairwellInteriorRoot root =
                            roots[index].GetComponent<
                                StairwellInteriorRoot>();
                        if (root != null && root.IsInitialized &&
                            !SceneTransitionService.IsTransitioning && GameInput.CanRead(GameInputContext.Gameplay))
                        {
                            capture(root);
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Assert.Fail(
                "StairwellInterior did not create its runtime root.");
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                predicate(),
                Is.True,
                failureMessage);
        }

        private static IEnumerator MoveControllerTo(
            CharacterController controller,
            Vector2 target)
        {
            const int maximumSteps = 180;
            const float stepDistance = 0.055f;
            for (int index = 0;
                 index < maximumSteps;
                 index++)
            {
                Vector3 position =
                    controller.transform.position;
                Vector2 remaining =
                    target -
                    new Vector2(position.x, position.z);
                if (remaining.magnitude < 0.08f)
                {
                    yield break;
                }

                Vector2 planar =
                    Vector2.ClampMagnitude(
                        remaining,
                        stepDistance);
                controller.Move(
                    new Vector3(
                        planar.x,
                        -0.08f,
                        planar.y));
                yield return null;
            }

            Assert.Fail(
                $"Controller did not reach {target} from " +
                $"{controller.transform.position}. " +
                BuildCollisionReport(controller, target));
        }

        private static string BuildCollisionReport(
            CharacterController controller,
            Vector2 target)
        {
            Vector3 origin =
                controller.transform.position +
                Vector3.up * 0.85f;
            Vector3 targetWorld =
                new Vector3(target.x, origin.y, target.y);
            Vector3 direction =
                (targetWorld - origin).normalized;
            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                controller.radius,
                direction,
                2f,
                ~0,
                QueryTriggerInteraction.Ignore);
            string result = "Forward colliders:";
            for (int index = 0; index < hits.Length; index++)
            {
                Collider collider = hits[index].collider;
                if (collider == null ||
                    collider == controller)
                {
                    continue;
                }

                result +=
                    $" {collider.name}@{hits[index].distance:F2};";
            }

            return result;
        }
    }
}
