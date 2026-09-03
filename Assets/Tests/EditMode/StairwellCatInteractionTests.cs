using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatInteractionTests
    {
        private GameObject rootObject;
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
            // The cat asks to be fed from his own day onwards. Every
            // test here but the first-day one below is about the
            // feeding flow rather than about when it opens, so put the
            // clock on that day the way the calendar does.
            GameSessionState.TrySetDebugGameDay(
                GameDaySchedule.GetFirstDayNumber(
                    GameDayEventId.FeedTheCatOpens));
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
            var visual =
                new TestPlayerPresentation(visualObject.transform);
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
            StairwellCatRigAnchors catAnchors =
                StairwellCatTestRig.Create(catObject);
            StairwellCatGrinController catGrin =
                catObject.AddComponent<StairwellCatGrinController>();
            catGrin.Initialize(catAnchors.GrinRenderer);
            catActor = catObject.AddComponent<StairwellCatActor>();
            catActor.Initialize(
                camera,
                playerObject.transform,
                catAnchors,
                catGrin);
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
            Assert.That(animation.EnterFrameCount, Is.EqualTo(24));
            Assert.That(animation.LoopFrameCount, Is.EqualTo(16));
            Assert.That(animation.ExitFrameCount, Is.EqualTo(24));
            Assert.That(animation.LoopFramesPerSecond, Is.EqualTo(6f));
            Assert.That(animation.EnterClipName, Is.EqualTo("CatFeedEnter"));
            Assert.That(animation.LoopClipName, Is.EqualTo("CatFeedLoop"));
            Assert.That(animation.ExitClipName, Is.EqualTo("CatFeedExit"));
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

        /// <summary>
        /// The first day has no feeding in it at all. He is a cat on a
        /// rail: looking at him gives the one line he has always given
        /// and opens nothing, so there is no tin to want and no menu to
        /// refuse. Left available, a day-one feeding would eat the can
        /// — it is consumed the frame his head goes in — while the
        /// quest that is not yet active recorded nothing, and day two
        /// would find the hero shut in his own stairwell with no way
        /// down.
        /// </summary>
        [Test]
        public void FirstDay_GivesTheCatsLineAndOpensNoMenu()
        {
            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.GameDayNumber,
                Is.EqualTo(GameDaySchedule.FirstDayNumber));
            Assert.That(
                StairwellCatInteraction.IsFeedingOffered,
                Is.False);
            Assert.That(
                catInteraction.CanInteract(player.Interactor),
                Is.True,
                "He is still there to be looked at.");

            Assert.That(
                catInteraction.TryOpen(player.Interactor),
                Is.False);

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(prompt.IsFeedbackVisible, Is.True);
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(
                player.Motor.InputEnabled,
                Is.True,
                "Nothing took the hero's input for a menu that never " +
                "opened.");
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

        private sealed class TestPlayerPresentation :
            IPlayerPresentation,
            IPlayerClipPresentation
        {
            private static readonly Renderer[] NoRenderers =
                System.Array.Empty<Renderer>();
            private readonly Transform visualRoot;

            public TestPlayerPresentation(Transform root)
            {
                visualRoot = root;
            }

            public IReadOnlyList<Renderer> Renderers => NoRenderers;
            public Transform VisualRoot => visualRoot;
            public PlayerPresentationMetrics Metrics =>
                new PlayerPresentationMetrics(
                    PlayerCharacterDimensions.StandingHeight,
                    0.32f,
                    visualRoot,
                    visualRoot.position,
                    visualRoot.position,
                    1f,
                    0f,
                    1f);
            public bool InteractionHandoffLocked { get; private set; }
            public string ActiveClipName { get; private set; } =
                string.Empty;
            public bool IsClipActive => ActiveClipName.Length > 0;

            public void SetMotion(in PlayerMotionSample motion)
            {
            }

            public void SetIntoxication(float intensity)
            {
            }

            public void SetBalancePose(float signedLean)
            {
            }

            public void SetFallPose(float signedDirection, float amount)
            {
            }

            public void SetFallAnimation(
                PlayerFallAnimationPhase phase,
                float normalizedProgress)
            {
            }

            public void SetInteractionHandoffLocked(bool locked)
            {
                InteractionHandoffLocked = locked;
            }

            public bool HasClip(string clipName)
            {
                return clipName == "CatFeedEnter" ||
                       clipName == "CatFeedLoop" ||
                       clipName == "CatFeedExit";
            }

            public bool TryBeginClip(string clipName)
            {
                if (!HasClip(clipName))
                {
                    return false;
                }

                ActiveClipName = clipName;
                return true;
            }

            public void SampleActiveClip(float normalizedTime)
            {
            }

            public void AlignActiveClipAnchor(Vector3 worldPelvisTarget)
            {
            }

            public void ResetClipSpatialOffset()
            {
            }

            public void EndClip()
            {
                ActiveClipName = string.Empty;
            }
        }
    }
}
