using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeRefrigeratorInteractionPlayModeTests
    {
        private const float TimeoutSeconds = 15f;

        private HomeInteriorRoot home;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene homeScene =
                SceneManager.GetSceneByName(SceneIds.HomeInterior);
            if (homeScene.IsValid() && homeScene.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene(
                    "Home Refrigerator Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload =
                    SceneManager.UnloadSceneAsync(homeScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            OpenInspectClose_UsesFirstPersonCameraAndRestoresGameplay()
        {
            yield return LoadHome();
            HomeRefrigeratorInteraction interaction =
                home.RefrigeratorInteraction;
            HomeRefrigeratorView view = home.Refrigerator;
            Assert.That(interaction, Is.Not.Null);
            Assert.That(view, Is.Not.Null);
            Assert.That(
                interaction.PromptKey,
                Is.EqualTo(
                    HomeRefrigeratorInteraction.OpenPromptKey));
            AssertClosedPresentation(interaction, view);
            AssertRefrigeratorVisibleFromMainShot();

            home.Player.Motor.Teleport(
                home.RefrigeratorPlan.ApproachPosition);
            Physics.SyncTransforms();
            yield return WaitForActiveRefrigerator();

            Vector3 gameplayCameraPosition =
                home.CameraFollow.FixedBasePosition;
            Quaternion gameplayCameraRotation =
                home.CameraFollow.FixedBaseRotation;
            float gameplayFieldOfView =
                home.CameraFollow.FixedBaseFieldOfView;
            Camera camera = Camera.main;
            Vector3 renderedCameraPosition =
                camera.transform.position;
            Quaternion renderedCameraRotation =
                camera.transform.rotation;
            float renderedFieldOfView = camera.fieldOfView;
            Assert.That(interaction.BeginInteraction(), Is.True);
            Assert.That(interaction.OwnsInteraction, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(home.Player.Interactor.InputEnabled, Is.False);
            Assert.That(home.IntoxicationHud.Visible, Is.False);
            AssertPlayerVisualState(true);
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    renderedCameraPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    camera.transform.rotation,
                    renderedCameraRotation),
                Is.LessThan(0.01f));
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(renderedFieldOfView).Within(0.001f));

            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .CameraApproachDurationSeconds);
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Reach));
            Assert.That(
                interaction.FirstPersonHand.IsVisible,
                Is.False);
            AssertPlayerVisualState(true);
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    home.transform.TransformPoint(
                        home.RefrigeratorPlan.CameraPosition)),
                Is.LessThan(0.001f));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(
                    home.RefrigeratorPlan.CameraFieldOfView)
                    .Within(0.001f));

            const int reachProbeStepCount = 128;
            float reachProbeStep =
                HomeRefrigeratorInteractionTimeline
                    .ReachDurationSeconds /
                reachProbeStepCount;
            bool handBecameVisible = false;
            for (int index = 0;
                 index < reachProbeStepCount;
                 index++)
            {
                interaction.AdvanceInteraction(reachProbeStep);
                bool handVisible =
                    interaction.FirstPersonHand.IsVisible;
                AssertPlayerVisualState(!handVisible);
                if (handVisible)
                {
                    handBecameVisible = true;
                    break;
                }
            }

            Assert.That(handBecameVisible, Is.True);
            Assert.That(
                interaction.FirstPersonHand.ReachAmount,
                Is.GreaterThan(0f));
            AssertFirstPersonHandIsInFrame(
                interaction.FirstPersonHand);

            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds);
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Inspecting));
            Assert.That(view.DoorOpenAmount, Is.EqualTo(1f));
            Assert.That(view.InteriorLightAmount, Is.EqualTo(1f));
            Assert.That(view.InteriorLightStrip.enabled, Is.True);
            Assert.That(view.InteriorHalo.IsVisible, Is.True);
            Assert.That(
                home.Soundscape.RefrigeratorDoorOpenAmount,
                Is.EqualTo(1f));
            Assert.That(
                home.InteractionPrompt.PromptKey,
                Is.EqualTo(
                    HomeRefrigeratorInteraction.ClosePromptKey));
            Assert.That(
                interaction.FirstPersonHand.IsVisible,
                Is.False);
            AssertPlayerVisualState(false);
            Assert.That(view.SlotRoots, Has.Count.EqualTo(8));

            interaction.AdvanceInteraction(30f);
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Inspecting),
                "Inspection must persist until the player closes the door.");
            bool closedEventSawRestoredState = false;
            interaction.PhaseChanged += phase =>
            {
                if (phase == HomeRefrigeratorInteractionPhase.Closed)
                {
                    closedEventSawRestoredState =
                        !BarMinigameModalLock.IsAnyLocked &&
                        home.Player.Motor.InputEnabled &&
                        home.Player.Interactor.InputEnabled;
                }
            };
            Assert.That(interaction.RequestClose(), Is.True);
            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .ClosingDurationSeconds);
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Sealing));
            AssertPlayerVisualState(false);

            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .SealingDurationSeconds);
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.CameraReturn));
            Assert.That(
                interaction.FirstPersonHand.IsVisible,
                Is.False);
            AssertPlayerVisualState(true);
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(home.Player.Interactor.InputEnabled, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);

            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .CameraReturnDurationSeconds);
            yield return null;

            AssertClosedPresentation(interaction, view);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(home.IntoxicationHud.Visible, Is.True);
            AssertPlayerVisualState(true);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(closedEventSawRestoredState, Is.True);
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    gameplayCameraPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    gameplayCameraRotation),
                Is.LessThan(0.01f));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(gameplayFieldOfView).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator
            LargeAdvanceSkipsRenderedReach_HidesRigForInspection()
        {
            yield return LoadHome();
            HomeRefrigeratorInteraction interaction =
                home.RefrigeratorInteraction;

            Assert.That(interaction.BeginInteraction(), Is.True);
            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds +
                0.01f);

            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Inspecting));
            Assert.That(
                interaction.FirstPersonHand.IsVisible,
                Is.False);
            AssertPlayerVisualState(false);

            Assert.That(interaction.CancelInteraction(), Is.True);
            yield return null;

            AssertClosedPresentation(
                interaction,
                home.Refrigerator);
            AssertPlayerVisualState(true);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        [UnityTest]
        public IEnumerator DisableDuringOpening_CancelsAndRestoresModalState()
        {
            yield return LoadHome();
            HomeRefrigeratorInteraction interaction =
                home.RefrigeratorInteraction;
            Assert.That(interaction.BeginInteraction(), Is.True);
            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .CameraApproachDurationSeconds +
                HomeRefrigeratorInteractionTimeline
                    .ReachDurationSeconds +
                0.08f);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);

            interaction.enabled = false;
            yield return null;

            AssertClosedPresentation(
                interaction,
                home.Refrigerator);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(home.IntoxicationHud.Visible, Is.True);
            AssertPlayerVisualState(true);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        private IEnumerator LoadHome()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home =
                    Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(home, Is.Not.Null);
            Assert.That(home.IsInitialized, Is.True);
        }

        private IEnumerator WaitForActiveRefrigerator()
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (!ReferenceEquals(
                       home.Player.Interactor.ActiveInteractable,
                       home.RefrigeratorInteraction) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.Player.Interactor.ActiveInteractable,
                Is.SameAs(home.RefrigeratorInteraction));
        }

        private void AssertRefrigeratorVisibleFromMainShot()
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Vector3 center = home.transform.TransformPoint(
                home.RefrigeratorPlan.RootPosition +
                home.RefrigeratorPlan.BodyCenterLocal);
            Vector3 viewport = camera.WorldToViewportPoint(center);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(viewport.x, Is.InRange(0.07f, 0.93f));
            Assert.That(viewport.y, Is.InRange(0.02f, 0.98f));

            Vector3 halfHeight =
                home.transform.up *
                home.RefrigeratorPlan.BodySize.y * 0.5f;
            float displayedHeight = Mathf.Abs(
                camera.WorldToScreenPoint(center + halfHeight).y -
                camera.WorldToScreenPoint(center - halfHeight).y);
            Assert.That(
                displayedHeight,
                Is.GreaterThan(90f),
                "The refrigerator must read as a major fixture, not a speck.");
        }

        private void AssertRigRendererState(bool expected)
        {
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                Assert.That(
                    home.Player.Visual.Renderers[index].enabled,
                    Is.EqualTo(expected),
                    $"Unexpected player renderer state {index}.");
            }
        }

        private void AssertPlayerVisualState(bool expected)
        {
            AssertRigRendererState(expected);
            Assert.That(
                home.Player.Shadow.enabled,
                Is.EqualTo(expected),
                "Unexpected dynamic-shadow state.");
            Assert.That(
                home.Player.ContactShadow.enabled,
                Is.EqualTo(expected),
                "Unexpected contact-shadow state.");
        }

        private static void AssertFirstPersonHandIsInFrame(
            HomeRefrigeratorFirstPersonHand hand)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(hand.PresentationRoot, Is.Not.Null);
            Renderer[] renderers =
                hand.PresentationRoot.GetComponentsInChildren<Renderer>();
            Assert.That(renderers, Is.Not.Empty);

            bool visibleRendererFound = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(
                    renderers[index].bounds.center);
                if (viewport.z > camera.nearClipPlane &&
                    viewport.x >= 0.02f &&
                    viewport.x <= 0.98f &&
                    viewport.y >= 0.02f &&
                    viewport.y <= 0.98f)
                {
                    visibleRendererFound = true;
                    break;
                }
            }

            Assert.That(
                visibleRendererFound,
                Is.True,
                "The first-person hand must visibly reach into the frame.");
        }

        private static void AssertClosedPresentation(
            HomeRefrigeratorInteraction interaction,
            HomeRefrigeratorView view)
        {
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Closed));
            Assert.That(interaction.OwnsInteraction, Is.False);
            Assert.That(view.DoorOpenAmount, Is.Zero);
            Assert.That(view.HandleTurnAmount, Is.Zero);
            Assert.That(view.InteriorLightAmount, Is.Zero);
            Assert.That(view.InteriorLightStrip.enabled, Is.False);
            Assert.That(view.InteriorHalo.IsVisible, Is.False);
            Assert.That(
                interaction.FirstPersonHand.IsVisible,
                Is.False);
        }
    }
}
