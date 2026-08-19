using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InventoryTargetInteractionControllerTests
    {
        private GameObject playerObject;
        private GameObject uiObject;
        private InventoryTargetInteractionController controller;
        private PlayerInteractor interactor;

        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
            playerObject = new GameObject("Target Interaction Player");
            uiObject = new GameObject("Target Interaction UI");
            PlayerMotor motor =
                playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();
            InteractionPromptView prompt =
                uiObject.AddComponent<InteractionPromptView>();
            interactor.Initialize(prompt);
            var runtime = new PlayerRuntime(
                playerObject,
                motor,
                interactor,
                null);
            controller = uiObject.AddComponent<
                InventoryTargetInteractionController>();
            controller.Initialize(runtime, null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (uiObject != null)
            {
                Object.DestroyImmediate(uiObject);
            }

            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }

            GameSessionState.BeginNewGame();
        }

        [Test]
        public void ConfirmedExecution_PreparesConsumesAndBeginsOnce()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan,
                    2),
                Is.True);
            var handler = new RecordingHandler(true);

            Assert.That(
                controller.Open(
                    interactor,
                    CreateDefinition(),
                    handler),
                Is.True);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            Assert.That(controller.Confirm(), Is.True);
            controller.SelectConfirmation(true);

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(controller.IsExecuting, Is.True);
            Assert.That(handler.PrepareCount, Is.EqualTo(1));
            Assert.That(handler.BeginCount, Is.EqualTo(1));
            Assert.That(handler.CancelPreparationCount, Is.Zero);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
            Assert.That(controller.Confirm(), Is.False);
            Assert.That(handler.BeginCount, Is.EqualTo(1));
            Assert.That(controller.CompleteExecution(), Is.True);
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(interactor.InputEnabled, Is.True);
        }

        [Test]
        public void FailedPreparation_DoesNotConsumeItem()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            var handler = new RecordingHandler(false);
            controller.Open(
                interactor,
                CreateDefinition(),
                handler);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            controller.Confirm();
            controller.SelectConfirmation(true);

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(handler.PrepareCount, Is.EqualTo(1));
            Assert.That(handler.BeginCount, Is.Zero);
            Assert.That(
                handler.CancelPreparationCount,
                Is.EqualTo(1),
                "A failed prepare may still own partial work and must be " +
                "cleaned through the idempotent handler contract.");
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(interactor.InputEnabled, Is.True);
        }

        [Test]
        public void CloseForHandler_OnlyClosesTheOwningTarget()
        {
            var owner = new RecordingHandler(true);
            var other = new RecordingHandler(true);
            Assert.That(
                controller.Open(
                    interactor,
                    CreateDefinition(),
                    owner),
                Is.True);

            Assert.That(
                controller.CloseForHandler(other),
                Is.False);
            Assert.That(controller.IsOpen, Is.True);
            Assert.That(interactor.InputEnabled, Is.False);

            Assert.That(
                controller.CloseForHandler(owner),
                Is.True);
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(interactor.InputEnabled, Is.True);
        }

        [Test]
        public void AbortExecution_CleansHandlerBeforeRestoringInput()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            var handler = new RecordingHandler(true, interactor);
            controller.Open(
                interactor,
                CreateDefinition(),
                handler);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            controller.Confirm();
            controller.SelectConfirmation(true);
            controller.Confirm();

            Assert.That(controller.AbortExecution(), Is.True);

            Assert.That(handler.CancelPreparationCount, Is.EqualTo(1));
            Assert.That(
                handler.InputEnabledDuringCancel,
                Is.False,
                "Handler cleanup must run while the modal lock still owns input.");
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(controller.IsOpen, Is.False);
        }

        [Test]
        public void BeginFailure_RefundsCommittedItemAndCleansHandler()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            var handler = new RecordingHandler(
                true,
                interactor,
                throwOnBegin: true);
            controller.Open(
                interactor,
                CreateDefinition(),
                handler);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            controller.Confirm();
            controller.SelectConfirmation(true);
            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "InvalidOperationException: Expected test " +
                    "startup failure\\."));

            Assert.That(controller.Confirm(), Is.True);

            Assert.That(handler.BeginCount, Is.EqualTo(1));
            Assert.That(handler.CancelPreparationCount, Is.EqualTo(1));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(interactor.InputEnabled, Is.True);
        }

        [Test]
        public void AbandonedExecution_GivesTheRequirementBack()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            var handler = new RecordingHandler(true, interactor);
            BeginExecution(handler);

            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.Zero,
                "The requirement leaves the bag while the work runs.");

            Assert.That(controller.AbortExecution(), Is.True);

            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1),
                "Work that never happened may not eat the item.");
        }

        [Test]
        public void ClosingForTheHandler_GivesTheRequirementBack()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            var handler = new RecordingHandler(true, interactor);
            BeginExecution(handler);

            // Walking out of the scene mid-animation takes this path.
            Assert.That(controller.CloseForHandler(handler), Is.True);

            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
        }

        [Test]
        public void CommittedRequirement_IsSpentForGood()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            var handler = new RecordingHandler(true, interactor);
            BeginExecution(handler);

            Assert.That(controller.CommitRequirement(), Is.True);
            Assert.That(
                controller.CommitRequirement(),
                Is.False,
                "There is nothing left to commit twice.");

            Assert.That(controller.AbortExecution(), Is.True);

            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.Zero,
                "Once the work is past its point of no return the item " +
                "is gone, however the session ends.");
        }

        private void BeginExecution(RecordingHandler handler)
        {
            Assert.That(
                controller.Open(
                    interactor,
                    CreateDefinition(),
                    handler),
                Is.True);
            controller.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            Assert.That(controller.Confirm(), Is.True);
            controller.SelectConfirmation(true);
            Assert.That(controller.Confirm(), Is.True);
            Assert.That(controller.IsExecuting, Is.True);
        }

        private static InventoryTargetInteractionDefinition
            CreateDefinition()
        {
            return new InventoryTargetInteractionDefinition(
                new InventoryItemRequirement(
                    InventoryItemId.OpenStewCan),
                "interaction.test.talk",
                "interaction.test.confirm",
                "interaction.test.missing");
        }

        private sealed class RecordingHandler :
            IInventoryTargetInteractionHandler
        {
            private readonly bool prepareResult;
            private readonly PlayerInteractor observedInteractor;
            private readonly bool throwOnBegin;

            public RecordingHandler(
                bool prepareResult,
                PlayerInteractor interactor = null,
                bool throwOnBegin = false)
            {
                this.prepareResult = prepareResult;
                observedInteractor = interactor;
                this.throwOnBegin = throwOnBegin;
            }

            public int PrepareCount { get; private set; }
            public int BeginCount { get; private set; }
            public int CancelPreparationCount { get; private set; }
            public bool? InputEnabledDuringCancel { get; private set; }

            public bool TryPrepareInventoryInteraction()
            {
                PrepareCount++;
                return prepareResult;
            }

            public void BeginInventoryInteraction()
            {
                BeginCount++;
                if (throwOnBegin)
                {
                    throw new InvalidOperationException(
                        "Expected test startup failure.");
                }
            }

            public void CancelInventoryInteractionPreparation()
            {
                CancelPreparationCount++;
                InputEnabledDuringCancel =
                    observedInteractor?.InputEnabled;
            }
        }
    }
}
