using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatInteractionTests
    {
        private GameObject rootObject;
        private Texture2D idleAtlas;
        private Texture2D feedingAtlas;
        private StairwellCatInteraction catInteraction;
        private StairwellCatActor catActor;
        private PlayerAnimatedInteractionController animation;
        private InventoryTargetInteractionController controller;
        private InteractionPromptView prompt;
        private PlayerRuntime player;

        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
            SetTransitioning(false);

            rootObject = new GameObject("Stairwell Test Root");
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(rootObject.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();

            GameObject uiObject = new GameObject("UI");
            uiObject.transform.SetParent(rootObject.transform, false);
            prompt = uiObject.AddComponent<InteractionPromptView>();

            GameObject playerObject = new GameObject("Player");
            playerObject.transform.SetParent(rootObject.transform, false);
            PlayerMotor motor = playerObject.AddComponent<PlayerMotor>();
            PlayerInteractor interactor =
                playerObject.AddComponent<PlayerInteractor>();
            interactor.Initialize(prompt);
            GameObject visualObject = new GameObject("Visual");
            visualObject.transform.SetParent(playerObject.transform, false);
            PlayerSpriteRig visual =
                visualObject.AddComponent<PlayerSpriteRig>();
            player = new PlayerRuntime(
                playerObject,
                motor,
                interactor,
                visual);

            animation =
                playerObject.AddComponent<
                    PlayerAnimatedInteractionController>();
            animation.Initialize(player, camera);
            controller = uiObject.AddComponent<
                InventoryTargetInteractionController>();
            controller.Initialize(player, null, null);

            StairwellLayoutPlan layout =
                StairwellLayoutPlanner.Generate();
            StairwellCatPlan catPlan =
                StairwellCatPlan.Create(layout);
            StairwellCatFeedingPlan feedingPlan =
                StairwellCatFeedingPlan.Create(layout, catPlan);
            playerObject.transform.localPosition =
                feedingPlan.EntryRootLocalPosition;
            GameObject catObject = new GameObject("Cat");
            catObject.transform.SetParent(rootObject.transform, false);
            catObject.transform.localPosition =
                catPlan.VisualLocalPosition;
            idleAtlas = new Texture2D(
                StairwellCatSpriteLibrary.Columns *
                    StairwellCatSpriteLibrary.FrameWidth,
                StairwellCatSpriteLibrary.Rows *
                    StairwellCatSpriteLibrary.FrameHeight,
                TextureFormat.RGBA32,
                false);
            feedingAtlas = new Texture2D(
                StairwellCatFeedingSpriteLibrary.Columns *
                    StairwellCatFeedingSpriteLibrary.FrameWidth,
                StairwellCatFeedingSpriteLibrary.Rows *
                    StairwellCatFeedingSpriteLibrary.FrameHeight,
                TextureFormat.RGBA32,
                false);
            catActor = catObject.AddComponent<StairwellCatActor>();
            catActor.Initialize(
                camera,
                playerObject.transform,
                idleAtlas,
                feedingAtlas);
            catInteraction =
                catObject.AddComponent<StairwellCatInteraction>();
            Vector3 interactionPosition =
                rootObject.transform.TransformPoint(
                    catPlan.InteractionLocalPosition);
            catInteraction.Initialize(
                rootObject.transform,
                interactionPosition,
                player,
                catActor,
                animation,
                controller,
                feedingPlan);
        }

        [TearDown]
        public void TearDown()
        {
            SetTransitioning(false);
            controller?.CloseForHandler(catInteraction);
            Destroy(rootObject);
            Destroy(idleAtlas);
            Destroy(feedingAtlas);
            GameSessionState.BeginNewGame();
        }

        [Test]
        public void Initialize_ExposesReusableInventoryAndAnimationContracts()
        {
            Assert.That(catInteraction.IsInitialized, Is.True);
            Assert.That(
                catInteraction.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
            Assert.That(
                catInteraction.Definition.Requirement.ItemId,
                Is.EqualTo(InventoryItemId.OpenStewCan));
            Assert.That(
                catInteraction.Definition.Requirement.Count,
                Is.EqualTo(1));
            Assert.That(
                catInteraction.Definition.TalkResponseKey,
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(
                catInteraction.Definition.ConfirmationPromptKey,
                Is.EqualTo(
                    StairwellCatInteraction
                        .FeedConfirmationPromptKey));
            Assert.That(
                catInteraction.Definition
                    .MissingRequirementResponseKey,
                Is.EqualTo(
                    StairwellCatInteraction
                        .MissingStewResponsePromptKey));

            PlayerAnimatedInteractionDefinition animation =
                catInteraction.PlayerFeedingDefinition;
            Assert.That(
                animation.TextureResourcePath,
                Is.EqualTo(
                    StairwellCatInteraction
                        .PlayerFeedingAtlasResourcePath));
            Assert.That(animation.EnterFrameCount, Is.EqualTo(24));
            Assert.That(animation.LoopFrameCount, Is.EqualTo(16));
            Assert.That(animation.ExitFrameCount, Is.EqualTo(24));
            Assert.That(animation.LoopFramesPerSecond, Is.EqualTo(6f));
            Assert.That(
                animation.VisualCrossfadeDurationSeconds,
                Is.Zero,
                "Cat feeding must hand off at its authored entry and " +
                "exit poses without fading either player presentation.");
            Assert.That(
                animation.TextureFlipX,
                Is.True,
                "The image-right feeding sheet must mirror toward the " +
                "camera-left cat.");
            Assert.That(
                catInteraction.CanInteract(player.Interactor),
                Is.True);
        }

        [Test]
        public void BeginPositioned_DefaultPoseIsRejectedBeforeInputCapture()
        {
            var validPose = new PlayerAnimatedInteractionPose(
                Vector3.zero,
                Quaternion.identity,
                Vector3.up);

            Assert.Throws<System.ArgumentException>(
                () => animation.BeginPositioned(
                    catInteraction.PlayerFeedingDefinition,
                    default,
                    Vector3.up,
                    validPose));
            Assert.That(animation.IsActive, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);

            Assert.Throws<System.ArgumentException>(
                () => animation.BeginPositioned(
                    catInteraction.PlayerFeedingDefinition,
                    validPose,
                    Vector3.up,
                    default));
            Assert.That(animation.IsActive, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
        }

        [Test]
        public void Talk_ClosesMenuAndShowsExistingCatResponse()
        {
            Assert.That(
                catInteraction.TryOpen(player.Interactor),
                Is.True);
            Assert.That(
                controller.State,
                Is.EqualTo(InventoryTargetInteractionState.Choice));
            Assert.That(
                controller.SelectedChoice,
                Is.EqualTo(InventoryTargetInteractionChoice.Talk));
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(player.Interactor.InputEnabled, Is.False);

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            Assert.That(prompt.IsFeedbackVisible, Is.True);
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(prompt.IsClickable, Is.False);
        }

        [Test]
        public void InteractWithoutStew_ShowsMissingRequirementFeedback()
        {
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.False);
            Assert.That(
                catInteraction.TryOpen(player.Interactor),
                Is.True);
            Assert.That(
                controller.SelectChoice(
                    InventoryTargetInteractionChoice.Interact),
                Is.True);

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction
                        .MissingStewResponsePromptKey));
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
        }

        [Test]
        public void InteractWithStew_DefaultNoReturnsToChoiceWithoutConsumption()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            Assert.That(
                catInteraction.TryOpen(player.Interactor),
                Is.True);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);

            Assert.That(controller.Confirm(), Is.True);
            Assert.That(
                controller.State,
                Is.EqualTo(
                    InventoryTargetInteractionState.Confirmation));
            Assert.That(controller.ConfirmationYesSelected, Is.False);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(
                controller.State,
                Is.EqualTo(InventoryTargetInteractionState.Choice));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
            Assert.That(controller.Cancel(), Is.True);
        }

        [Test]
        public void OwnedTargetClose_RestoresInput()
        {
            Assert.That(
                catInteraction.TryOpen(player.Interactor),
                Is.True);

            Assert.That(
                controller.CloseForHandler(catInteraction),
                Is.True);

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);

            SetTransitioning(true);
            Assert.That(
                catInteraction.CanInteract(player.Interactor),
                Is.False);
        }

        [Test]
        public void DisabledPlayerAnimation_DoesNotConsumePreparedItem()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            Assert.That(
                catInteraction.TryOpen(player.Interactor),
                Is.True);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            controller.Confirm();
            controller.SelectConfirmation(true);
            animation.enabled = false;

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
            Assert.That(catActor.IsFeedingPrepared, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
        }

        [Test]
        public void CleanupWithoutOwnership_DoesNotCancelExternalCatPreparation()
        {
            Assert.That(catActor.TryPrepareFeeding(), Is.True);

            catInteraction.CancelInventoryInteractionPreparation();

            Assert.That(catActor.IsFeedingPrepared, Is.True);
            catActor.CancelFeedingPreparation();
        }

        private static void SetTransitioning(bool value)
        {
            PropertyInfo property = typeof(
                    SceneTransitionService)
                .GetProperty(
                    nameof(
                        SceneTransitionService.IsTransitioning),
                    BindingFlags.Public |
                    BindingFlags.Static);
            MethodInfo setter = property?.GetSetMethod(true);
            Assert.That(
                setter,
                Is.Not.Null,
                "Scene transition test hook is unavailable.");
            setter.Invoke(null, new object[] { value });
        }

        private static void Destroy(Object value)
        {
            if (value != null)
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}
