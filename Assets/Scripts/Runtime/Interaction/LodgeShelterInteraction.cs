using UnityEngine;

namespace BarPromenade
{
    public enum LodgeShelterAction { LeftDoor, RightDoor, Cot, Kettle, Lantern }

    /// <summary>Shared prompts and silent feedback for the lodge's fixed props.</summary>
    public sealed class LodgeShelterInteraction : MonoBehaviour, IInteractable
    {
        private LodgeShelterController owner;
        private int lastFrame = -1;
        public LodgeShelterAction Action { get; private set; }
        public bool IsDoor => Action == LodgeShelterAction.LeftDoor || Action == LodgeShelterAction.RightDoor;
        public int DoorIndex => Action == LodgeShelterAction.LeftDoor ? 0 : 1;
        public Vector3 InteractionPosition => owner != null ? owner.InteractionGround(transform) : transform.position;
        public string PromptKey => IsDoor
            ? LodgeShelterSessionState.IsDoorOpen(DoorIndex) ? "interaction.lodge_door.close" : "interaction.lodge_door.open"
            : Action == LodgeShelterAction.Cot ? "interaction.lodge_cot"
            : Action == LodgeShelterAction.Kettle ? "interaction.lodge_kettle"
            : LodgeShelterSessionState.LanternLit ? "interaction.lodge_lantern.extinguish" : "interaction.lodge_lantern.light";

        public void Initialize(LodgeShelterController controller, LodgeShelterAction action)
        { owner = controller; Action = action; }

        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && owner != null &&
            owner.CanInteract(interactor, this);

        public void Interact(PlayerInteractor interactor)
        {
            if (lastFrame == Time.frameCount || !CanInteract(interactor)) return;
            lastFrame = Time.frameCount;
            if (IsDoor)
            {
                if (!owner.TrySetDoorOpen(DoorIndex, !LodgeShelterSessionState.IsDoorOpen(DoorIndex)))
                    interactor.ShowFeedback("lodge.door.blocked", 1.5f);
            }
            else if (Action == LodgeShelterAction.Lantern)
                owner.SetLanternLit(!LodgeShelterSessionState.LanternLit);
            else interactor.ShowFeedback(Action == LodgeShelterAction.Cot ? "lodge.cot.inspect" : "lodge.kettle.inspect", 2f);
        }
    }
}
