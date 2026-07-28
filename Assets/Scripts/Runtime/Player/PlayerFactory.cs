using UnityEngine;

namespace BarPromenade
{
    public readonly struct PlayerRuntime
    {
        public PlayerRuntime(
            GameObject gameObject,
            PlayerMotor motor,
            PlayerInteractor interactor,
            PlayerSpriteRig visual,
            PlayerDynamicShadow shadow = null)
        {
            GameObject = gameObject;
            Motor = motor;
            Interactor = interactor;
            Visual = visual;
            Shadow = shadow;
        }

        public GameObject GameObject { get; }
        public PlayerMotor Motor { get; }
        public PlayerInteractor Interactor { get; }
        public PlayerSpriteRig Visual { get; }
        public PlayerDynamicShadow Shadow { get; }
    }

    public static class PlayerFactory
    {
        public static PlayerRuntime Create(
            Transform parent,
            Vector3 position,
            Camera camera,
            IWalkableArea walkableArea,
            InteractionPromptView promptView)
        {
            GameObject player = new GameObject("Sprite Player");
            player.transform.SetParent(parent, false);
            player.transform.position = position;

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.stepOffset = 0.28f;
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.04f;

            GameObject visualObject =
                new GameObject("8-Direction Jointed Sprite Visual");
            visualObject.transform.SetParent(player.transform, false);
            visualObject.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            PlayerSpriteRig visual = visualObject.AddComponent<PlayerSpriteRig>();
            visual.Initialize(camera, player.transform);

            PlayerDynamicShadow shadow =
                player.AddComponent<PlayerDynamicShadow>();
            shadow.Initialize(player.transform, visual);

            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            motor.Initialize(camera, walkableArea, visual);

            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            interactor.Initialize(promptView);
            return new PlayerRuntime(
                player,
                motor,
                interactor,
                visual,
                shadow);
        }
    }
}
