using UnityEngine;

namespace BarPromenade
{
    public sealed class CityHallDoor : MonoBehaviour, IInteractable
    {
        private PlayerDoorActionTarget action;
        private bool enters;
        public string PromptKey => enters ? "interaction.enter_city_hall" : "interaction.exit_city_hall";
        public Vector3 InteractionPosition => transform.position;

        internal static CityHallDoor Build(Transform parent, PlayerDoorActionPlan plan, bool enters)
        {
            var host = new GameObject(enters ? "City Hall Entrance" : "Vestibule Exit");
            host.transform.SetParent(parent, false);
            host.transform.position = plan.InteractionPosition;
            var trigger = host.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = .85f;
            var door = host.AddComponent<CityHallDoor>();
            door.enters = enters;
            door.action = host.AddComponent<PlayerDoorActionTarget>();
            door.action.Configure(plan);
            return door;
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            !SceneTransitionService.IsTransitioning && action.CanInteract(interactor);

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            action.TryBegin(interactor, () =>
            {
                var motor = interactor.GetComponent<PlayerMotor>();
                motor.SetInputEnabled(false);
                if (SceneTransitionService.RequestDoorLoad(enters ? SceneIds.CityHallInterior : SceneIds.City,
                    enters ? DoorTransitionDirection.EnterBuilding : DoorTransitionDirection.ExitBuilding))
                {
                    if (enters) GameSessionState.EnterCityHall();
                    else GameSessionState.PrepareCityHallReturn();
                }
                else motor.SetInputEnabled(true);
            });
        }
    }
}
