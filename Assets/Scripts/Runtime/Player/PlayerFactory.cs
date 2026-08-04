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
            PlayerContactShadow contactShadow = null)
        {
            GameObject = gameObject;
            Motor = motor;
            Interactor = interactor;
            Visual = visual;
            ContactShadow = contactShadow;
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
        public PlayerPresentationVisibility PresentationVisibility
        {
            get;
        }
    }

    public static class PlayerFactory
    {
        public const float GroundedRootOffset = 0.04f;

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
            controller.stepOffset = 0.28f;
            controller.slopeLimit = 45f;
            controller.skinWidth = GroundedRootOffset;

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

            PlayerContactShadow contactShadow =
                player.AddComponent<PlayerContactShadow>();
            contactShadow.Initialize(player.transform, visual);

            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            motor.Initialize(camera, walkableArea, visual);

            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            interactor.Initialize(promptView);
            return new PlayerRuntime(
                player,
                motor,
                interactor,
                visual,
                contactShadow);
        }
    }
}
