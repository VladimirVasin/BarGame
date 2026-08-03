using System;

namespace BarPromenade
{
    public readonly struct InventoryItemRequirement
    {
        public InventoryItemRequirement(
            InventoryItemId itemId,
            int count = 1)
        {
            if (!InventoryItemCatalog.TryGet(itemId, out _))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemId),
                    itemId,
                    "The required item must be present in the inventory catalog.");
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    "The required item count must be positive.");
            }

            ItemId = itemId;
            Count = count;
        }

        public InventoryItemId ItemId { get; }
        public int Count { get; }
        public bool IsValid =>
            Count > 0 && InventoryItemCatalog.TryGet(ItemId, out _);

        public bool IsSatisfiedBy(int availableCount)
        {
            return IsValid && availableCount >= Count;
        }
    }

    public readonly struct InventoryTargetInteractionDefinition
    {
        public const float DefaultFeedbackDurationSeconds = 2.5f;

        public InventoryTargetInteractionDefinition(
            InventoryItemRequirement requirement,
            string talkResponseKey,
            string confirmationPromptKey,
            string missingRequirementResponseKey,
            float feedbackDurationSeconds =
                DefaultFeedbackDurationSeconds)
        {
            if (!requirement.IsValid)
            {
                throw new ArgumentException(
                    "The inventory requirement must be valid.",
                    nameof(requirement));
            }

            Requirement = requirement;
            TalkResponseKey = RequireKey(
                talkResponseKey,
                nameof(talkResponseKey));
            ConfirmationPromptKey = RequireKey(
                confirmationPromptKey,
                nameof(confirmationPromptKey));
            MissingRequirementResponseKey = RequireKey(
                missingRequirementResponseKey,
                nameof(missingRequirementResponseKey));
            if (feedbackDurationSeconds <= 0f ||
                float.IsNaN(feedbackDurationSeconds) ||
                float.IsInfinity(feedbackDurationSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(feedbackDurationSeconds),
                    feedbackDurationSeconds,
                    "Feedback duration must be finite and positive.");
            }

            FeedbackDurationSeconds = feedbackDurationSeconds;
        }

        public InventoryItemRequirement Requirement { get; }
        public string TalkResponseKey { get; }
        public string ConfirmationPromptKey { get; }
        public string MissingRequirementResponseKey { get; }
        public float FeedbackDurationSeconds { get; }
        public bool IsValid =>
            Requirement.IsValid &&
            !string.IsNullOrWhiteSpace(TalkResponseKey) &&
            !string.IsNullOrWhiteSpace(ConfirmationPromptKey) &&
            !string.IsNullOrWhiteSpace(
                MissingRequirementResponseKey) &&
            FeedbackDurationSeconds > 0f &&
            !float.IsNaN(FeedbackDurationSeconds) &&
            !float.IsInfinity(FeedbackDurationSeconds);

        private static string RequireKey(
            string key,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A localization key is required.",
                    parameterName);
            }

            return key.Trim();
        }
    }

    public interface IInventoryTargetInteractionHandler
    {
        bool TryPrepareInventoryInteraction();
        void BeginInventoryInteraction();

        // Must be idempotent and restore both prepared and already-active
        // presentation state when the controller closes abnormally.
        void CancelInventoryInteractionPreparation();
    }

    public enum InventoryTargetInteractionState
    {
        Closed = 0,
        Choice = 1,
        Confirmation = 2,
        Executing = 3
    }

    public enum InventoryTargetInteractionChoice
    {
        Talk = 0,
        Interact = 1
    }

    public enum InventoryTargetInteractionAction
    {
        None = 0,
        ShowTalkFeedback = 1,
        ShowMissingRequirementFeedback = 2,
        BeginExecution = 3,
        Close = 4
    }

    public sealed class InventoryTargetInteractionModel
    {
        public InventoryTargetInteractionState State { get; private set; }
        public InventoryTargetInteractionChoice SelectedChoice
        {
            get;
            private set;
        }
        public bool ConfirmationYesSelected { get; private set; }

        public void Open()
        {
            State = InventoryTargetInteractionState.Choice;
            SelectedChoice = InventoryTargetInteractionChoice.Talk;
            ConfirmationYesSelected = false;
        }

        public bool MoveSelection(int delta)
        {
            if (delta == 0 ||
                State == InventoryTargetInteractionState.Closed ||
                State == InventoryTargetInteractionState.Executing)
            {
                return false;
            }

            if (State == InventoryTargetInteractionState.Confirmation)
            {
                ConfirmationYesSelected =
                    !ConfirmationYesSelected;
                return true;
            }

            SelectedChoice = SelectedChoice ==
                InventoryTargetInteractionChoice.Talk
                    ? InventoryTargetInteractionChoice.Interact
                    : InventoryTargetInteractionChoice.Talk;
            return true;
        }

        public bool SelectChoice(
            InventoryTargetInteractionChoice choice)
        {
            if (State != InventoryTargetInteractionState.Choice ||
                choice < InventoryTargetInteractionChoice.Talk ||
                choice > InventoryTargetInteractionChoice.Interact ||
                SelectedChoice == choice)
            {
                return false;
            }

            SelectedChoice = choice;
            return true;
        }

        public bool SelectConfirmation(bool yes)
        {
            if (State !=
                    InventoryTargetInteractionState.Confirmation ||
                ConfirmationYesSelected == yes)
            {
                return false;
            }

            ConfirmationYesSelected = yes;
            return true;
        }

        public InventoryTargetInteractionAction Confirm(
            bool requirementSatisfied)
        {
            if (State == InventoryTargetInteractionState.Choice)
            {
                if (SelectedChoice ==
                    InventoryTargetInteractionChoice.Talk)
                {
                    State = InventoryTargetInteractionState.Closed;
                    return InventoryTargetInteractionAction
                        .ShowTalkFeedback;
                }

                if (!requirementSatisfied)
                {
                    State = InventoryTargetInteractionState.Closed;
                    return InventoryTargetInteractionAction
                        .ShowMissingRequirementFeedback;
                }

                State = InventoryTargetInteractionState.Confirmation;
                ConfirmationYesSelected = false;
                return InventoryTargetInteractionAction.None;
            }

            if (State !=
                InventoryTargetInteractionState.Confirmation)
            {
                return InventoryTargetInteractionAction.None;
            }

            if (!ConfirmationYesSelected)
            {
                State = InventoryTargetInteractionState.Choice;
                return InventoryTargetInteractionAction.None;
            }

            if (!requirementSatisfied)
            {
                State = InventoryTargetInteractionState.Closed;
                return InventoryTargetInteractionAction
                    .ShowMissingRequirementFeedback;
            }

            State = InventoryTargetInteractionState.Executing;
            return InventoryTargetInteractionAction.BeginExecution;
        }

        public InventoryTargetInteractionAction Cancel()
        {
            if (State ==
                InventoryTargetInteractionState.Confirmation)
            {
                State = InventoryTargetInteractionState.Choice;
                ConfirmationYesSelected = false;
                return InventoryTargetInteractionAction.None;
            }

            if (State == InventoryTargetInteractionState.Choice)
            {
                State = InventoryTargetInteractionState.Closed;
                return InventoryTargetInteractionAction.Close;
            }

            return InventoryTargetInteractionAction.None;
        }

        public bool CompleteExecution()
        {
            if (State != InventoryTargetInteractionState.Executing)
            {
                return false;
            }

            State = InventoryTargetInteractionState.Closed;
            return true;
        }

        public bool Close()
        {
            if (State == InventoryTargetInteractionState.Closed)
            {
                return false;
            }

            State = InventoryTargetInteractionState.Closed;
            ConfirmationYesSelected = false;
            return true;
        }
    }
}
