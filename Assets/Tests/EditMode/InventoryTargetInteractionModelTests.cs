using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InventoryTargetInteractionModelTests
    {
        [Test]
        public void Open_UsesSafeTalkAndNoDefaults()
        {
            var model = new InventoryTargetInteractionModel();

            model.Open();

            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Choice));
            Assert.That(
                model.SelectedChoice,
                Is.EqualTo(InventoryTargetInteractionChoice.Talk));
            Assert.That(model.ConfirmationYesSelected, Is.False);
        }

        [Test]
        public void Talk_ClosesWithTalkFeedbackAction()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();

            Assert.That(
                model.Confirm(false),
                Is.EqualTo(
                    InventoryTargetInteractionAction.ShowTalkFeedback));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Closed));
        }

        [Test]
        public void InteractWithoutItem_ClosesWithMissingFeedbackAction()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();
            model.SelectChoice(
                InventoryTargetInteractionChoice.Interact);

            Assert.That(
                model.Confirm(false),
                Is.EqualTo(
                    InventoryTargetInteractionAction
                        .ShowMissingRequirementFeedback));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Closed));
        }

        [Test]
        public void InteractionConfirmation_DefaultsToNoAndReturnsToChoice()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();
            model.SelectChoice(
                InventoryTargetInteractionChoice.Interact);

            Assert.That(
                model.Confirm(true),
                Is.EqualTo(InventoryTargetInteractionAction.None));
            Assert.That(
                model.State,
                Is.EqualTo(
                    InventoryTargetInteractionState.Confirmation));
            Assert.That(model.ConfirmationYesSelected, Is.False);

            Assert.That(
                model.Confirm(true),
                Is.EqualTo(InventoryTargetInteractionAction.None));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Choice));
            Assert.That(
                model.SelectedChoice,
                Is.EqualTo(InventoryTargetInteractionChoice.Interact));
        }

        [Test]
        public void YesConfirmation_BeginsOnceAndWaitsForCompletion()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();
            model.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            model.Confirm(true);
            model.SelectConfirmation(true);

            Assert.That(
                model.Confirm(true),
                Is.EqualTo(
                    InventoryTargetInteractionAction.BeginExecution));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Executing));
            Assert.That(
                model.Confirm(true),
                Is.EqualTo(InventoryTargetInteractionAction.None));
            Assert.That(model.CompleteExecution(), Is.True);
            Assert.That(model.CompleteExecution(), Is.False);
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Closed));
        }

        [Test]
        public void ItemRemovedWhileConfirming_DoesNotBeginExecution()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();
            model.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            model.Confirm(true);
            model.SelectConfirmation(true);

            Assert.That(
                model.Confirm(false),
                Is.EqualTo(
                    InventoryTargetInteractionAction
                        .ShowMissingRequirementFeedback));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Closed));
        }

        [Test]
        public void RefusedInteraction_AnswersInsteadOfAsking()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();
            model.SelectChoice(
                InventoryTargetInteractionChoice.Interact);

            // Refused before the requirement is even looked at: a target
            // saying no of his own accord does not care what is in the
            // hero's pockets, and the panel never asks "are you sure"
            // about something that cannot happen.
            Assert.That(
                model.Confirm(true, true),
                Is.EqualTo(
                    InventoryTargetInteractionAction
                        .ShowRefusalFeedback));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Closed));
        }

        [Test]
        public void RefusalOnAConfirmedYes_StillNeverBeginsExecution()
        {
            var model = new InventoryTargetInteractionModel();
            model.Open();
            model.SelectChoice(
                InventoryTargetInteractionChoice.Interact);
            model.Confirm(true);
            model.SelectConfirmation(true);

            Assert.That(
                model.Confirm(true, true),
                Is.EqualTo(
                    InventoryTargetInteractionAction
                        .ShowRefusalFeedback));
            Assert.That(
                model.State,
                Is.EqualTo(InventoryTargetInteractionState.Closed));
        }

        [Test]
        public void Definition_CarriesARefusalOnlyWhereOneIsGiven()
        {
            InventoryTargetInteractionDefinition plain =
                InventoryTargetInteractionDefinition.WithoutRequirement(
                    "interaction.talk",
                    "interaction.confirm");
            Assert.That(plain.IsRefused, Is.False);
            Assert.That(plain.RefusalResponseKey, Is.Empty);

            InventoryTargetInteractionDefinition refusing =
                InventoryTargetInteractionDefinition.WithoutRequirement(
                    "interaction.talk",
                    "interaction.confirm",
                    InventoryTargetInteractionDefinition
                        .DefaultFeedbackDurationSeconds,
                    default,
                    "interaction.refusal");
            Assert.That(refusing.IsRefused, Is.True);
            Assert.That(
                refusing.RefusalResponseKey,
                Is.EqualTo("interaction.refusal"));
            Assert.That(refusing.IsValid, Is.True);

            // A target that asks for an item refuses with the item it is
            // missing and with nothing else.
            var withItem = new InventoryTargetInteractionDefinition(
                new InventoryItemRequirement(
                    InventoryItemId.OpenStewCan),
                "interaction.talk",
                "interaction.confirm",
                "interaction.missing");
            Assert.That(withItem.IsRefused, Is.False);
        }

        [Test]
        public void RequirementAndDefinition_RejectInvalidContracts()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new InventoryItemRequirement(
                    InventoryItemId.None));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new InventoryItemRequirement(
                    InventoryItemId.OpenStewCan,
                    0));

            var requirement = new InventoryItemRequirement(
                InventoryItemId.OpenStewCan,
                2);
            Assert.That(requirement.IsSatisfiedBy(1), Is.False);
            Assert.That(requirement.IsSatisfiedBy(2), Is.True);
            Assert.Throws<ArgumentException>(
                () => new InventoryTargetInteractionDefinition(
                    requirement,
                    string.Empty,
                    "interaction.confirm",
                    "interaction.missing"));
        }
    }
}
