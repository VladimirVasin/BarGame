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

    /// <summary>
    /// Lets a seat retain the ordinary interaction prompt while a seated
    /// activity temporarily changes what E does. The seat stays the sole
    /// input consumer, so one press cannot both close a menu and stand up.
    /// </summary>
    public interface ISeatedInteractionHandler
    {
        string SeatedPromptKey { get; }
        bool CanHandleSeatedInteraction(PlayerInteractor interactor);
        void HandleSeatedInteraction(PlayerInteractor interactor);
    }
}
