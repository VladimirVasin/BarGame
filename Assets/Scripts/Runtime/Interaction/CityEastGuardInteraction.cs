using UnityEngine;

namespace BarPromenade
{
    /// <summary>A short reply shares the post's ambient channel and never takes player control.</summary>
    [DisallowMultipleComponent]
    public sealed class CityEastGuardInteraction : MonoBehaviour, IInteractable
    {
        private CityEastGuardController post;
        private int index;
        public string PromptKey => "interaction.talk_east_guard";
        public Vector3 InteractionPosition => transform.position + Vector3.up * 1.2f;
        public void Initialize(CityEastGuardController owner, int guardIndex) { post = owner; index = guardIndex; }
        public bool CanInteract(PlayerInteractor interactor) => post != null &&
            isActiveAndEnabled && post.CanReply(index, interactor);
        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor)) post.RequestReply(index, interactor);
        }
    }
}
