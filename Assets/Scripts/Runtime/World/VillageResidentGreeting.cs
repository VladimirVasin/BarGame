using UnityEngine;

namespace BarPromenade
{
    public sealed class VillageResidentGreeting : MonoBehaviour, IInteractable
    {
        private AlpineVillageLifeController life;
        private VillageResidentRole role;
        public string PromptKey => "interaction.village.greet";
        public Vector3 InteractionPosition => life.ResidentPosition(role);
        public void Initialize(AlpineVillageLifeController controller, VillageResidentRole resident)
        { life = controller; role = resident; }
        public bool CanInteract(PlayerInteractor interactor) => life != null &&
            isActiveAndEnabled && interactor != null && interactor.InputEnabled &&
            life.CanTalk(role);
        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor)) life.TryGreet(role);
        }
    }
}
