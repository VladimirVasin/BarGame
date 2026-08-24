using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Temporary City-side refusal at the visually open south tunnel. It owns
    /// no prompt and no transition: walking inward across the plan shows the
    /// inner-monologue line and visibly guides the ordinary rig back.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class CityTunnelTravelController : MonoBehaviour
    {
        public const string RootName = "City Tunnel Travel";
        public const string UnavailableFeedbackKey =
            "city.tunnel.unavailable";
        public const float FeedbackDurationSeconds = 2.5f;

        private PlayerRuntime player;
        private InteractionPromptView prompt;
        private CityTunnelTravelCrossingModel crossing;
        private bool ownsInputLock;

        public bool IsInitialized { get; private set; }
        public bool IsTurningBack { get; private set; }
        public CityTunnelTravelPlan Plan { get; private set; }

        public static CityTunnelTravelController Create(
            Transform parent,
            CityTunnelTravelPlan plan,
            PlayerRuntime player,
            InteractionPromptView prompt)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            CityTunnelTravelController controller =
                root.AddComponent<CityTunnelTravelController>();
            controller.Initialize(plan, player, prompt);
            return controller;
        }

        public void Initialize(
            CityTunnelTravelPlan plan,
            PlayerRuntime playerRuntime,
            InteractionPromptView promptView)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The tunnel-travel controller is already initialized.");
            }

            if (playerRuntime.GameObject == null ||
                playerRuntime.Motor == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "Tunnel travel requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (promptView == null)
            {
                throw new ArgumentNullException(nameof(promptView));
            }

            Plan = plan;
            player = playerRuntime;
            prompt = promptView;
            crossing = new CityTunnelTravelCrossingModel(plan);
            IsInitialized = true;
        }

        private void Update()
        {
            if (!IsInitialized || Plan.TravelAvailable)
            {
                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                FinishTurnBack();
                crossing.Reset();
                return;
            }

            if (IsTurningBack)
            {
                StepTurnBack();
                return;
            }

            if (!CanEngage())
            {
                crossing.Reset();
                return;
            }

            if (crossing.Observe(player.GameObject.transform.position))
            {
                BeginTurnBack();
            }
        }

        private bool CanEngage()
        {
            return player.GameObject != null &&
                   player.Motor != null &&
                   player.Interactor != null &&
                   player.Motor.InputEnabled &&
                   player.Interactor.InputEnabled;
        }

        private void BeginTurnBack()
        {
            Vector3 position = player.GameObject.transform.position;
            player.Motor.SetInputEnabled(false);
            player.Interactor.SetInputEnabled(false);
            ownsInputLock = true;
            IsTurningBack = true;

            // PlayerInteractor clears feedback when it is locked, so this
            // must happen after both input owners have been disabled.
            prompt.ShowFeedback(
                UnavailableFeedbackKey,
                FeedbackDurationSeconds);
            GameLog.Info(
                "interaction",
                "tunnel_travel_unavailable",
                GameLog.Field("tunnel_id", Plan.StableId),
                GameLog.Field(
                    "distance",
                    Plan.GetSignedDistance(position)),
                GameLog.Field(
                    "lateral",
                    Plan.GetLateralDistance(position)));
        }

        private void StepTurnBack()
        {
            bool completed = player.Motor.MoveTowardsInteractionPose(
                Plan.ReturnRootPosition,
                Plan.ReturnRootRotation,
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
            if (player.Motor != null)
            {
                player.Motor.CancelInteractionPoseMove();
            }

            if (!ownsInputLock)
            {
                return;
            }

            ownsInputLock = false;
            if (player.Motor != null)
            {
                player.Motor.SetInputEnabled(true);
            }

            if (player.Interactor != null)
            {
                player.Interactor.SetInputEnabled(true);
            }
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
