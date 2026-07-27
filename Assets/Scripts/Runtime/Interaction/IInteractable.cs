using UnityEngine;

namespace BarPromenade
{
    public interface IInteractable
    {
        string PromptKey { get; }
        Vector3 InteractionPosition { get; }
        bool CanInteract(PlayerInteractor interactor);
        void Interact(PlayerInteractor interactor);
    }
}
