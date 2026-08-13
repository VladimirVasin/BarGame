using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    public sealed class StairwellCatInteraction :
        MonoBehaviour,
        IInteractable,
        IInventoryTargetInteractionHandler
    {
        public const string DefaultPromptKey = "interaction.cat";
        public const string ResponsePromptKey =
            "stairwell.cat.placeholder";
        public const string FeedConfirmationPromptKey =
            "stairwell.cat.feed.confirm";
        public const string MissingStewResponsePromptKey =
            "stairwell.cat.feed.missing";
        public const float ResponseDurationSeconds = 2.5f;

        private Transform stairwellRoot;
        private PlayerRuntime player;
        private StairwellCatActor cat;
        private PlayerAnimatedInteractionController
            animatedInteraction;
        private InventoryTargetInteractionController
            targetInteraction;
        private StairwellCatFeedingPlan feedingPlan;
        private Vector3 interactionPosition;
        private InventoryTargetInteractionDefinition
            interactionDefinition;
        private PlayerAnimatedInteractionDefinition
            playerFeedingDefinition;
        private bool isInitialized;
        private bool ownsExecution;
        private bool ownsPlayerPresentation;
        private bool ownsCatPreparation;
        private bool ownsCatFeeding;
        private bool catFeedingStarted;
        private bool exitRequested;
        private bool exitPhaseObserved;
        private Transform feedingCanProp;

        public string PromptKey => DefaultPromptKey;
        public Vector3 InteractionPosition =>
            interactionPosition;
        public bool IsInitialized => isInitialized;
        public bool IsFeedingPrepared => ownsCatPreparation;
        public bool OwnsExecution => ownsExecution;
        public InventoryTargetInteractionDefinition Definition =>
            interactionDefinition;
        public PlayerAnimatedInteractionDefinition
            PlayerFeedingDefinition => playerFeedingDefinition;
        public Transform FeedingCanProp => feedingCanProp;

        public void Initialize(
            Transform authoredStairwellRoot,
            Vector3 authoredWorldInteractionPosition,
            PlayerRuntime playerRuntime,
            StairwellCatActor catActor,
            PlayerAnimatedInteractionController
                animationController,
            InventoryTargetInteractionController
                interactionController,
            StairwellCatFeedingPlan authoredFeedingPlan)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The stairwell cat interaction is already initialized.");
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
                    "The cat interaction requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (catActor == null || !catActor.IsInitialized)
            {
                throw new ArgumentException(
                    "The cat interaction requires an initialized cat.",
                    nameof(catActor));
            }

            if (animationController == null ||
                !animationController.IsInitialized)
            {
                throw new ArgumentException(
                    "The cat interaction requires an initialized player " +
                    "animation controller.",
                    nameof(animationController));
            }

            if (interactionController == null ||
                !interactionController.IsInitialized)
            {
                throw new ArgumentException(
                    "The cat interaction requires an initialized target " +
                    "interaction controller.",
                    nameof(interactionController));
            }

            stairwellRoot = authoredStairwellRoot;
            player = playerRuntime;
            cat = catActor;
            animatedInteraction = animationController;
            targetInteraction = interactionController;
            feedingPlan = authoredFeedingPlan;
            interactionPosition =
                authoredWorldInteractionPosition;
            interactionDefinition =
                new InventoryTargetInteractionDefinition(
                    new InventoryItemRequirement(
                        InventoryItemId.OpenStewCan),
                    ResponsePromptKey,
                    FeedConfirmationPromptKey,
                    MissingStewResponsePromptKey,
                    ResponseDurationSeconds);
            playerFeedingDefinition =
                new PlayerAnimatedInteractionDefinition(
                    "CatFeedEnter",
                    "CatFeedLoop",
                    "CatFeedExit",
                    enterFrameCount: 24,
                    enterFramesPerSecond: 12f,
                    loopFrameCount: 16,
                    loopFramesPerSecond: 6f,
                    exitFrameCount: 24,
                    exitFramesPerSecond: 12f);
            RebuildFeedingCanProp();
            animatedInteraction.PhaseChanged +=
                HandlePlayerAnimationPhaseChanged;
            isInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   interactor != null &&
                   interactor == player.Interactor &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   isActiveAndEnabled &&
                   targetInteraction != null &&
                   !targetInteraction.IsOpen &&
                   animatedInteraction != null &&
                   !animatedInteraction.IsActive &&
                   cat != null &&
                   !cat.IsFeeding &&
                   !cat.IsFeedingPrepared &&
                   !ownsPlayerPresentation &&
                   !ownsCatPreparation &&
                   !ownsCatFeeding &&
                   !ownsExecution &&
                   !SceneTransitionService.IsTransitioning &&
                   IsPlayerAtReachableEntryHeight();
        }

        public void Interact(PlayerInteractor interactor)
        {
            TryOpen(interactor);
        }

        public bool TryOpen(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return false;
            }

            return targetInteraction.Open(
                interactor,
                interactionDefinition,
                this);
        }

        public bool TryPrepareInventoryInteraction()
        {
            if (!isInitialized ||
                !isActiveAndEnabled ||
                targetInteraction == null ||
                !targetInteraction.IsExecuting ||
                ownsPlayerPresentation ||
                ownsCatPreparation ||
                ownsCatFeeding ||
                ownsExecution ||
                !animatedInteraction.isActiveAndEnabled ||
                animatedInteraction.IsActive ||
                cat.IsFeeding ||
                cat.IsFeedingPrepared ||
                !IsPlayerAtReachableEntryHeight())
            {
                return false;
            }

            Vector3 worldActionHip = stairwellRoot.TransformPoint(
                feedingPlan.ActionHipLocalPosition);
            if (!animatedInteraction.TryPrepare(
                    playerFeedingDefinition,
                    CreateWorldEntryPose(),
                    worldActionHip,
                    CreateWorldExitPose()))
            {
                return false;
            }

            if (!cat.TryPrepareFeeding())
            {
                return false;
            }

            ownsCatPreparation = true;
            return true;
        }

        public void BeginInventoryInteraction()
        {
            if (!ownsCatPreparation ||
                !cat.IsFeedingPrepared ||
                targetInteraction == null ||
                !targetInteraction.IsExecuting)
            {
                throw new InvalidOperationException(
                    "The cat feeding presentation was not prepared.");
            }

            Transform playerTransform = player.GameObject.transform;
            Vector3 previousPosition = playerTransform.position;
            Quaternion previousRotation = playerTransform.rotation;
            ownsExecution = true;
            ownsPlayerPresentation = true;
            catFeedingStarted = false;
            exitRequested = false;
            exitPhaseObserved = false;
            try
            {
                bool began = animatedInteraction.BeginPositioned(
                    playerFeedingDefinition,
                    CreateWorldEntryPose(),
                    stairwellRoot.TransformPoint(
                        feedingPlan.ActionHipLocalPosition),
                    CreateWorldExitPose());
                if (!began)
                {
                    throw new InvalidOperationException(
                        "The player feeding animation could not begin.");
                }
            }
            catch
            {
                CancelOwnedPresentation();
                player.Motor.Teleport(previousPosition);
                playerTransform.rotation = previousRotation;
                Physics.SyncTransforms();
                throw;
            }
        }

        private PlayerAnimatedInteractionPose CreateWorldEntryPose()
        {
            return new PlayerAnimatedInteractionPose(
                stairwellRoot.TransformPoint(
                    feedingPlan.EntryRootLocalPosition),
                stairwellRoot.rotation *
                    feedingPlan.EntryLocalRotation,
                stairwellRoot.TransformPoint(
                    feedingPlan.EntryHipLocalPosition));
        }

        private PlayerAnimatedInteractionPose CreateWorldExitPose()
        {
            return new PlayerAnimatedInteractionPose(
                stairwellRoot.TransformPoint(
                    feedingPlan.ExitRootLocalPosition),
                stairwellRoot.rotation *
                    feedingPlan.ExitLocalRotation,
                stairwellRoot.TransformPoint(
                    feedingPlan.ExitHipLocalPosition));
        }

        private bool IsPlayerAtReachableEntryHeight()
        {
            if (stairwellRoot == null || player.GameObject == null)
            {
                return false;
            }

            float playerLocalY = stairwellRoot.InverseTransformPoint(
                player.GameObject.transform.position).y;
            return Mathf.Abs(playerLocalY -
                             feedingPlan.EntryRootLocalPosition.y) <=
                   PlayerMotor.InteractionVerticalTolerance;
        }

        public void CancelInventoryInteractionPreparation()
        {
            CancelOwnedPresentation();
        }

        private void Update()
        {
            if (!ownsExecution ||
                !catFeedingStarted ||
                cat.IsFeeding ||
                exitRequested ||
                animatedInteraction.Phase !=
                    PlayerAnimatedInteractionPhase.Looping)
            {
                return;
            }

            ownsCatFeeding = false;
            exitRequested = true;
            if (!animatedInteraction.RequestExit())
            {
                exitRequested = false;
                targetInteraction.AbortExecution();
            }
        }

        private void HandlePlayerAnimationPhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (!ownsExecution)
            {
                return;
            }

            if (phase == PlayerAnimatedInteractionPhase.Entering)
            {
                SetFeedingCanVisible(true);
                return;
            }

            if (phase == PlayerAnimatedInteractionPhase.Looping)
            {
                if (!cat.BeginPreparedFeeding())
                {
                    targetInteraction.AbortExecution();
                    return;
                }

                ownsCatPreparation = false;
                ownsCatFeeding = true;
                catFeedingStarted = true;
                GameSessionState.TryCompleteQuest(
                    QuestId.FeedTheCat);
                return;
            }

            if (phase == PlayerAnimatedInteractionPhase.Exiting)
            {
                if (!exitRequested)
                {
                    targetInteraction.AbortExecution();
                    return;
                }

                exitPhaseObserved = true;
                return;
            }

            if (phase != PlayerAnimatedInteractionPhase.Idle)
            {
                return;
            }

            SetFeedingCanVisible(false);

            if (!exitPhaseObserved)
            {
                targetInteraction.AbortExecution();
                return;
            }

            ownsExecution = false;
            ownsPlayerPresentation = false;
            ownsCatPreparation = false;
            ownsCatFeeding = false;
            catFeedingStarted = false;
            exitRequested = false;
            exitPhaseObserved = false;
            targetInteraction.CompleteExecution();
        }

        private void CancelOwnedPresentation()
        {
            bool cancelPlayer = ownsPlayerPresentation;
            bool cancelCatFeeding = ownsCatFeeding;
            bool cancelCatPreparation = ownsCatPreparation;

            ownsExecution = false;
            ownsPlayerPresentation = false;
            ownsCatPreparation = false;
            ownsCatFeeding = false;
            catFeedingStarted = false;
            exitRequested = false;
            exitPhaseObserved = false;
            SetFeedingCanVisible(false);

            if (cancelPlayer && animatedInteraction != null)
            {
                animatedInteraction.CancelActiveInteraction();
            }

            if (cat == null)
            {
                return;
            }

            if (cancelCatFeeding)
            {
                cat.CancelFeeding();
            }

            if (cancelCatPreparation)
            {
                cat.CancelFeedingPreparation();
            }
        }

        private void OnDisable()
        {
            if (targetInteraction == null ||
                !targetInteraction.CloseForHandler(this))
            {
                CancelInventoryInteractionPreparation();
            }
        }

        private void OnDestroy()
        {
            if (animatedInteraction != null)
            {
                animatedInteraction.PhaseChanged -=
                    HandlePlayerAnimationPhaseChanged;
            }

            if (targetInteraction == null ||
                !targetInteraction.CloseForHandler(this))
            {
                CancelInventoryInteractionPreparation();
            }

            ReleaseFeedingCanProp();
            isInitialized = false;
        }

        private void RebuildFeedingCanProp()
        {
            ReleaseFeedingCanProp();
            if (!(player.Visual is Player3DCharacterPresentation visual))
            {
                return;
            }

            Transform grip = visual.Registry != null
                ? visual.Registry.Anchors.LeftGrip
                : null;
            if (grip == null)
            {
                throw new InvalidOperationException(
                    "The Player 3D cat-feeding presentation requires " +
                    "the serialized left grip anchor.");
            }

            feedingCanProp = InventoryItemModelFactory.BuildWorldModel(
                InventoryItemId.OpenStewCan,
                grip,
                Vector3.one * 0.12f);
            feedingCanProp.name = "Player Cat Feeding Can";
            feedingCanProp.localPosition = Vector3.zero;
            feedingCanProp.localRotation = Quaternion.identity;
            feedingCanProp.gameObject.SetActive(false);
        }

        private void SetFeedingCanVisible(bool visible)
        {
            if (feedingCanProp != null &&
                feedingCanProp.gameObject.activeSelf != visible)
            {
                feedingCanProp.gameObject.SetActive(visible);
            }
        }

        private void ReleaseFeedingCanProp()
        {
            if (feedingCanProp == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(feedingCanProp.gameObject);
            }
            else
            {
                DestroyImmediate(feedingCanProp.gameObject);
            }

            feedingCanProp = null;
        }
    }
}
