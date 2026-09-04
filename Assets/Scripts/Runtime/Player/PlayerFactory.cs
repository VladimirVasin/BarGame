using UnityEngine;

namespace BarPromenade
{
    public readonly struct PlayerRuntime
    {
        public PlayerRuntime(
            GameObject gameObject,
            PlayerMotor motor,
            PlayerInteractor interactor,
            IPlayerPresentation visual,
            PlayerContactShadow contactShadow = null,
            Player3DRagdollController ragdoll = null,
            PlayerBalanceController balance = null)
        {
            GameObject = gameObject;
            Motor = motor;
            Interactor = interactor;
            Visual = visual;
            ContactShadow = contactShadow;
            Ragdoll = ragdoll;
            Balance = balance;
            PresentationVisibility = visual != null
                ? new PlayerPresentationVisibility(
                    visual,
                    contactShadow)
                : null;
        }

        public GameObject GameObject { get; }
        public PlayerMotor Motor { get; }
        public PlayerInteractor Interactor { get; }
        public IPlayerPresentation Visual { get; }
        public PlayerContactShadow ContactShadow { get; }
        public Player3DRagdollController Ragdoll { get; }

        /// <summary>The drunk balance model's driver, or null on a bare rig.</summary>
        public PlayerBalanceController Balance { get; }
        public PlayerPresentationVisibility PresentationVisibility
        {
            get;
        }
    }

    public static class PlayerFactory
    {
        public const float GroundedRootOffset = 0.04f;

        /// <summary>
        /// The steepest ground the hero can walk up. Named because terrain
        /// that is meant to be a WALL has to be authored against it - the
        /// village ridge is the first place that mattered, where a slope
        /// gentler than this made "reachable only by cableway" a promise kept
        /// by the walkable mask instead of by the mountain.
        /// </summary>
        public const float SlopeLimitDegrees = 45f;

        public const float StepOffset = 0.28f;

        public static PlayerRuntime Create(
            Transform parent,
            Vector3 position,
            Camera camera,
            IWalkableArea walkableArea,
            InteractionPromptView promptView)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            player.transform.position = position;

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.stepOffset = StepOffset;
            controller.slopeLimit = SlopeLimitDegrees;
            controller.skinWidth = GroundedRootOffset;

            // Cloth only presses against capsules listed per Cloth and a
            // CharacterController is not a CapsuleCollider, so the hero
            // carries a passive trigger capsule — slightly wider than
            // the controller so hanging laundry drapes over the body
            // instead of slicing into it. A trigger blocks nothing and
            // every player query either ignores triggers or skips the
            // hero's own children.
            GameObject clothBody = new GameObject("Cloth Body Capsule");
            clothBody.transform.SetParent(player.transform, false);
            CapsuleCollider clothCapsule =
                clothBody.AddComponent<CapsuleCollider>();
            clothCapsule.isTrigger = true;
            clothCapsule.height = 1.7f;
            clothCapsule.radius = 0.36f;
            clothCapsule.center = new Vector3(0f, 0.85f, 0f);
            CityClothBodyRegistry.RegisterBody(clothCapsule);

            Player3DAssetRegistry registry =
                Player3DResources.Instantiate(player.transform);
            Player3DCharacterPresentation visual =
                registry.GetComponent<Player3DCharacterPresentation>();
            if (visual == null)
            {
                visual = registry.gameObject.AddComponent<
                    Player3DCharacterPresentation>();
            }

            visual.Initialize(player.transform, registry);

            Player3DRagdollController ragdoll =
                player.AddComponent<Player3DRagdollController>();
            ragdoll.Initialize(
                player.transform,
                controller,
                visual,
                registry);

            PlayerContactShadow contactShadow =
                player.AddComponent<PlayerContactShadow>();
            contactShadow.Initialize(player.transform, visual);

            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            motor.Initialize(walkableArea, visual);

            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            interactor.Initialize(promptView);

            // The Silent Hill head: the hero notices interactables and
            // characters near his path and turns his head toward them.
            PlayerAttentionController attention =
                player.AddComponent<PlayerAttentionController>();
            attention.Initialize(interactor, visual);

            // The drunk balance: a model that drifts the capsule through
            // the motor and leans the rig through the presentation. Inert
            // until the status controller raises its intoxication.
            PlayerBalanceController balance =
                player.AddComponent<PlayerBalanceController>();
            var runtime = new PlayerRuntime(
                player,
                motor,
                interactor,
                visual,
                contactShadow,
                ragdoll,
                balance);
            balance.Initialize(
                runtime,
                walkableArea,
                PlayerBalanceRules.EpisodeSeed(
                    GameSessionState.CitySeed,
                    GameSessionState.BalanceCheckSequence));
            PlayerAnimatedInteractionController animatedInteraction =
                player.AddComponent<
                    PlayerAnimatedInteractionController>();
            animatedInteraction.Initialize(runtime, camera);
            PlayerDoorActionController doorAction =
                player.AddComponent<PlayerDoorActionController>();
            doorAction.Initialize(animatedInteraction);
            return runtime;
        }
    }
}
