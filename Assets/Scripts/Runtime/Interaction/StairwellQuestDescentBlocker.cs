using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// While the feed-the-cat quest is active, descending the lower
    /// flight below the cat's landing interrupts the player with a
    /// Silent Hill style inner monologue line and walks them back up
    /// to the landing, facing the cat, until the cat has been fed.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class StairwellQuestDescentBlocker : MonoBehaviour
    {
        public const string BlockedPromptKey =
            "quest.feed_cat.block.descend";
        public const float BlockedPromptDurationSeconds = 2.5f;
        public const float DescentThresholdBelowCatLevel = 0.35f;
        public const float ReturnPointLandingInset = 0.5f;

        private Transform stairwellRoot;
        private PlayerRuntime player;
        private InteractionPromptView prompt;
        private float blockedLocalY;
        private Vector3 returnLocalPosition;
        private Quaternion returnLocalRotation;
        private float previousLocalY;
        private bool hasPreviousSample;
        private bool ownsInputLock;

        public bool IsInitialized { get; private set; }
        public bool IsTurningBack { get; private set; }
        public float BlockedLocalY => blockedLocalY;
        public Vector3 ReturnLocalPosition => returnLocalPosition;

        public void Initialize(
            Transform authoredStairwellRoot,
            PlayerRuntime playerRuntime,
            InteractionPromptView promptView,
            StairwellLayoutPlan layout)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The quest descent blocker is already initialized.");
            }

            if (authoredStairwellRoot == null)
            {
                throw new ArgumentNullException(
                    nameof(authoredStairwellRoot));
            }

            if (playerRuntime.GameObject == null ||
                playerRuntime.Motor == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "The quest descent blocker requires an initialized " +
                    "player.",
                    nameof(playerRuntime));
            }

            if (promptView == null)
            {
                throw new ArgumentNullException(nameof(promptView));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            stairwellRoot = authoredStairwellRoot;
            player = playerRuntime;
            prompt = promptView;
            blockedLocalY =
                layout.MiddleElevation - DescentThresholdBelowCatLevel;
            returnLocalPosition = new Vector3(
                layout.LowerFlightBounds.center.x,
                layout.MiddleElevation +
                PlayerFactory.GroundedRootOffset,
                layout.MiddleLandingBounds.yMin +
                ReturnPointLandingInset);
            returnLocalRotation =
                Quaternion.LookRotation(Vector3.forward, Vector3.up);
            IsInitialized = true;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                FinishTurnBack();
                hasPreviousSample = false;
                return;
            }

            if (IsTurningBack)
            {
                StepTurnBack();
                return;
            }

            if (!GameSessionState.IsQuestActive(QuestId.FeedTheCat))
            {
                hasPreviousSample = false;
                return;
            }

            float localY = stairwellRoot.InverseTransformPoint(
                player.GameObject.transform.position).y;
            bool crossedBelow = hasPreviousSample &&
                                previousLocalY >= blockedLocalY &&
                                localY < blockedLocalY;
            previousLocalY = localY;
            hasPreviousSample = true;
            if (crossedBelow && CanEngage())
            {
                BeginTurnBack();
            }
        }

        private bool CanEngage()
        {
            return player.Motor.InputEnabled &&
                   player.Interactor.InputEnabled;
        }

        private void BeginTurnBack()
        {
            player.Motor.SetInputEnabled(false);
            player.Interactor.SetInputEnabled(false);
            ownsInputLock = true;
            IsTurningBack = true;
            prompt.ShowFeedback(
                BlockedPromptKey,
                BlockedPromptDurationSeconds);
            GameLog.Info(
                "quest",
                "descent_blocked",
                GameLog.Field(
                    "quest_id",
                    QuestId.FeedTheCat.ToString()),
                GameLog.Field("player_local_y", previousLocalY));
        }

        private void StepTurnBack()
        {
            bool completed = player.Motor.MoveTowardsInteractionPose(
                stairwellRoot.TransformPoint(returnLocalPosition),
                stairwellRoot.rotation * returnLocalRotation,
                Time.deltaTime);
            if (completed || player.Motor.InteractionPoseMoveStalled)
            {
                FinishTurnBack();
            }
        }

        private void FinishTurnBack()
        {
            if (!IsTurningBack && !ownsInputLock)
            {
                return;
            }

            IsTurningBack = false;
            if (ownsInputLock)
            {
                ownsInputLock = false;
                player.Motor?.SetInputEnabled(true);
                player.Interactor?.SetInputEnabled(true);
            }

            hasPreviousSample = false;
        }

        private void OnDisable()
        {
            FinishTurnBack();
        }

        private void OnDestroy()
        {
            FinishTurnBack();
            IsInitialized = false;
        }
    }
}
