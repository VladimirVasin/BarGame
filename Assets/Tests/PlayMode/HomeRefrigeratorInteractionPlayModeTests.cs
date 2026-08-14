using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeRefrigeratorInteractionPlayModeTests
    {
        private const float TimeoutSeconds = 15f;

        private HomeInteriorRoot home;
        private InputTestFixture inputFixture;
        private Mouse mouse;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
            GameSessionState.ResetInventoryState();
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (mouse != null && mouse.added)
            {
                InputSystem.RemoveDevice(mouse);
            }

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
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetInventoryState();
            inputFixture?.TearDown();
            inputFixture = null;
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
            Assert.That(
                home.InteractionPrompt.PromptKey,
                Is.EqualTo(
                    HomeRefrigeratorInteraction.OpenPromptKey));
            Assert.That(home.InteractionPrompt.IsClickable, Is.True);

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
            Assert.That(
                home.InteractionPrompt.TryInvokePrompt(),
                Is.True);
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
            Assert.That(home.InteractionPrompt.IsClickable, Is.True);
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
            Assert.That(
                home.InteractionPrompt.TryInvokePrompt(),
                Is.True);
            Assert.That(home.InteractionPrompt.PromptKey, Is.Empty);
            Assert.That(home.InteractionPrompt.IsClickable, Is.False);
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

        [UnityTest]
        public IEnumerator
            HoverClickInspectReturn_UsesNestedPs1Presentation()
        {
            yield return LoadHome();
            HomeRefrigeratorInteraction interaction =
                home.RefrigeratorInteraction;
            HomeRefrigeratorItemInspectionController itemInspection =
                interaction.ItemInspection;
            Assert.That(itemInspection, Is.Not.Null);
            Assert.That(
                interaction.GetComponent<
                    HomeRefrigeratorItemInspectionView>(),
                Is.Not.Null);

            Assert.That(interaction.BeginInteraction(), Is.True);
            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds +
                0.01f);
            Assert.That(
                interaction.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Inspecting));
            Assert.That(itemInspection.BrowsingEnabled, Is.True);

            Assert.That(
                home.Refrigerator.TryGetItem(
                    HomeRefrigeratorItemKind.VodkaBottle,
                    out HomeRefrigeratorItemView item),
                Is.True);
            Renderer hoverRenderer = item.Renderers[0];
            Material originalSharedMaterial =
                hoverRenderer.sharedMaterial;
            int baseColorId = Shader.PropertyToID("_BaseColor");
            int colorId = Shader.PropertyToID("_Color");
            Color originalBaseColor = ReadRendererColor(
                hoverRenderer,
                baseColorId);
            Color originalColor = ReadRendererColor(
                hoverRenderer,
                colorId);

            Physics.SyncTransforms();
            Vector3 itemScreen = Camera.main.WorldToScreenPoint(
                item.SelectionCollider.bounds.center);
            Assert.That(itemScreen.z, Is.GreaterThan(0f));
            inputFixture.Set(
                mouse.position,
                new Vector2(itemScreen.x, itemScreen.y),
                queueEventOnly: true);
            yield return null;
            Assert.That(
                itemInspection.HoveredItem,
                Is.SameAs(item));
            var hoverProperties = new MaterialPropertyBlock();
            hoverRenderer.GetPropertyBlock(hoverProperties);
            float hoverTintDistance =
                Vector4.Distance(
                    hoverProperties.GetColor(baseColorId),
                    originalBaseColor) +
                Vector4.Distance(
                    hoverProperties.GetColor(colorId),
                    originalColor);
            Assert.That(hoverTintDistance, Is.GreaterThan(0.01f));
            Assert.That(
                hoverRenderer.sharedMaterial,
                Is.SameAs(originalSharedMaterial));

            inputFixture.Press(
                mouse.leftButton,
                queueEventOnly: true);
            yield return null;
            inputFixture.Release(
                mouse.leftButton,
                queueEventOnly: true);
            yield return null;

            Assert.That(itemInspection.ActiveItem, Is.SameAs(item));
            Assert.That(
                itemInspection.Timeline.Phase,
                Is.EqualTo(
                        HomeRefrigeratorItemInspectionPhase.FlyingIn)
                    .Or.EqualTo(
                        HomeRefrigeratorItemInspectionPhase.Inspecting));
            Assert.That(item.SelectionCollider.enabled, Is.False);
            hoverRenderer.GetPropertyBlock(hoverProperties);
            Assert.That(
                Vector4.Distance(
                    hoverProperties.GetColor(baseColorId),
                    originalBaseColor),
                Is.LessThan(0.001f));
            Assert.That(
                Vector4.Distance(
                    hoverProperties.GetColor(colorId),
                    originalColor),
                Is.LessThan(0.001f));
            Assert.That(
                hoverRenderer.sharedMaterial,
                Is.SameAs(originalSharedMaterial));
            Assert.That(
                home.InteractionPrompt.PromptKey,
                Is.Empty);

            interaction.AdvanceInteraction(
                HomeRefrigeratorItemInspectionTimeline
                    .FlyingInDurationSeconds);
            Assert.That(itemInspection.IsInspecting, Is.True);
            Assert.That(
                itemInspection.BackdropRenderer.gameObject.activeSelf,
                Is.True);
            Assert.That(
                itemInspection.BackdropRenderer.sharedMaterial,
                Is.SameAs(HomeBalconyResources.GlassMaterial));
            var backdropProperties = new MaterialPropertyBlock();
            itemInspection.BackdropRenderer.GetPropertyBlock(
                backdropProperties);
            Assert.That(
                backdropProperties.GetColor(baseColorId).a,
                Is.GreaterThan(0.80f));
            Assert.That(
                item.OriginalRoot.parent,
                Is.SameAs(itemInspection.PresentationPivot));

            Bounds centeredBounds = GetItemBounds(item);
            Vector3 viewport = Camera.main.WorldToViewportPoint(
                centeredBounds.center);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(viewport.x, Is.InRange(0.43f, 0.57f));
            Assert.That(viewport.y, Is.InRange(0.38f, 0.62f));
            AssertItemBoundsInsideViewport(item);
            Quaternion rotationBefore =
                itemInspection.PresentationPivot.localRotation;
            interaction.AdvanceInteraction(1f);
            Assert.That(
                Quaternion.Angle(
                    rotationBefore,
                    itemInspection.PresentationPivot.localRotation),
                Is.GreaterThan(10f));

            Assert.That(
                itemInspection.InvokeAction(
                    HomeRefrigeratorItemAction.Take),
                Is.True);
            Assert.That(
                itemInspection.FeedbackKey,
                Is.Empty);
            Assert.That(
                home.Refrigerator.Items,
                Has.Count.EqualTo(2));
            Assert.That(
                GameSessionState.IsWorldItemCollected(
                    HomeRefrigeratorInventoryAdapter.GetSourceId(
                        item.SlotId)),
                Is.True);
            Assert.That(
                GameSessionState.InventoryItems,
                Has.Count.EqualTo(3));
            Assert.That(
                GameSessionState.InventoryItems[2].ItemId,
                Is.EqualTo(InventoryItemId.VodkaBottle));
            Assert.That(item.gameObject.activeSelf, Is.False);

            Assert.That(interaction.RequestClose(), Is.True);
            Assert.That(itemInspection.IsActive, Is.False);
            Assert.That(
                itemInspection.BackdropRenderer.gameObject.activeSelf,
                Is.False);
            interaction.CancelInteraction();
            yield return null;
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        [UnityTest]
        public IEnumerator
            DisableDuringItemInspection_RestoresExactItemAndModalState()
        {
            yield return LoadHome();
            HomeRefrigeratorInteraction interaction =
                home.RefrigeratorInteraction;
            interaction.BeginInteraction();
            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds +
                0.01f);
            HomeRefrigeratorItemView item =
                home.Refrigerator.Items[1];
            Transform originalParent = item.OriginalRoot.parent;
            Vector3 originalLocalPosition =
                item.OriginalRoot.localPosition;
            Quaternion originalLocalRotation =
                item.OriginalRoot.localRotation;
            Vector3 originalLocalScale =
                item.OriginalRoot.localScale;

            Assert.That(
                interaction.ItemInspection.TryBeginInspection(item),
                Is.True);
            interaction.AdvanceInteraction(
                HomeRefrigeratorItemInspectionTimeline
                    .FlyingInDurationSeconds);
            Assert.That(interaction.ItemInspection.IsInspecting, Is.True);

            interaction.enabled = false;
            yield return null;

            Assert.That(interaction.ItemInspection.IsActive, Is.False);
            Assert.That(item.OriginalRoot.parent, Is.SameAs(originalParent));
            Assert.That(
                item.OriginalRoot.localPosition,
                Is.EqualTo(originalLocalPosition));
            Assert.That(
                Quaternion.Angle(
                    item.OriginalRoot.localRotation,
                    originalLocalRotation),
                Is.LessThan(0.001f));
            Assert.That(
                item.OriginalRoot.localScale,
                Is.EqualTo(originalLocalScale));
            Assert.That(item.SelectionCollider.enabled, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        [UnityTest]
        public IEnumerator
            DisabledNestedInspector_RejectsNewItemInspection()
        {
            yield return LoadHome();
            HomeRefrigeratorInteraction interaction =
                home.RefrigeratorInteraction;
            Assert.That(interaction.BeginInteraction(), Is.True);
            interaction.AdvanceInteraction(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds +
                0.01f);

            HomeRefrigeratorItemInspectionController itemInspection =
                interaction.ItemInspection;
            HomeRefrigeratorItemView item =
                home.Refrigerator.Items[0];
            Assert.That(itemInspection.BrowsingEnabled, Is.True);

            itemInspection.enabled = false;
            yield return null;

            Assert.That(itemInspection.BrowsingEnabled, Is.False);
            Assert.That(
                itemInspection.TryBeginInspection(item),
                Is.False);
            Assert.That(itemInspection.HandleInput(), Is.False);
            Assert.That(itemInspection.ActiveItem, Is.Null);
            Assert.That(item.SelectionCollider.enabled, Is.True);

            interaction.CancelInteraction();
            yield return null;
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

        private static Bounds GetItemBounds(
            HomeRefrigeratorItemView item)
        {
            Bounds bounds = item.Renderers[0].bounds;
            for (int index = 1; index < item.Renderers.Count; index++)
            {
                bounds.Encapsulate(item.Renderers[index].bounds);
            }

            return bounds;
        }

        private static Color ReadRendererColor(
            Renderer renderer,
            int propertyId)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Color color = properties.GetColor(propertyId);
            if (color != default)
            {
                return color;
            }

            Material material = renderer.sharedMaterial;
            return material != null && material.HasProperty(propertyId)
                ? material.GetColor(propertyId)
                : Color.white;
        }

        private static void AssertItemBoundsInsideViewport(
            HomeRefrigeratorItemView item)
        {
            Bounds bounds = GetItemBounds(item);
            Camera camera = Camera.main;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = bounds.center +
                                         Vector3.Scale(
                                             bounds.extents,
                                             new Vector3(x, y, z));
                        Vector3 viewport =
                            camera.WorldToViewportPoint(corner);
                        Assert.That(viewport.z, Is.GreaterThan(0f));
                        Assert.That(viewport.x, Is.InRange(0.01f, 0.99f));
                        Assert.That(viewport.y, Is.InRange(0.01f, 0.99f));
                    }
                }
            }
        }
    }
}
