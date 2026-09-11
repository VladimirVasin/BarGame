using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reserves the local port channel, then binds the foreman to shared dialogue.</summary>
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class CityPortForemanInteraction : MonoBehaviour, IInteractable
    {
        // 1.5 m lands on the bevel between the two quay slabs. Stand on the flat top.
        public const float ConversationDistance = 1.65f;
        private static readonly DialogueGraph graph = new DialogueGraph("offer",
            DialogueNode.Line("offer", DialogueSpeaker.Npc, CityPortConversationController.ForemanOfferKey, "choice"),
            DialogueNode.Choice("choice", new DialogueChoice("interaction.port_foreman_yes", "hero_yes"),
                new DialogueChoice("interaction.port_foreman_no", "hero_no")),
            DialogueNode.Line("hero_yes", DialogueSpeaker.Hero, "city.port.foreman.hero_yes", "accept"),
            DialogueNode.Line("hero_no", DialogueSpeaker.Hero, "city.port.foreman.hero_no", "decline"),
            DialogueNode.Line("accept", DialogueSpeaker.Npc, CityPortConversationController.ForemanAcceptKey, "end"),
            DialogueNode.Line("decline", DialogueSpeaker.Npc, CityPortConversationController.ForemanDeclineKey, "end"),
            DialogueNode.End("end"));
        private CityPortForeman foreman;
        private CityPortConversationController conversation;
        private PlayerInteractor listener;
        private DialogueSessionController session;
        public bool IsOpen => session != null && session.IsChoosing;
        public bool Accepts => session == null || session.SelectedChoice == 0;
        public DialogueSessionController Session => session;
        public static DialogueGraph Graph => graph;
        public Transform Listener => session != null && session.IsActive ? session.HeroHead :
            listener != null ? listener.GetComponentInChildren<Player3DAssetRegistry>()?.Anchors.Head : null;
        public string PromptKey => "interaction.talk_port_foreman";
        public Vector3 InteractionPosition => transform.position + Vector3.up * .85f;

        public void Initialize(CityPortForeman actor, CityPortConversationController channel)
        {
            foreman = actor; conversation = channel;
            session = GetComponent<DialogueSessionController>();
            if (session == null) session = gameObject.AddComponent<DialogueSessionController>();
        }

        public bool CanInteract(PlayerInteractor interactor) => foreman != null && conversation != null &&
            isActiveAndEnabled && foreman.isActiveAndEnabled && foreman.ModelRoot.gameObject.activeInHierarchy &&
            session != null && !session.IsActive && !conversation.ForemanInteractionPending && interactor != null &&
            interactor.isActiveAndEnabled && interactor.InputEnabled &&
            !BarMinigameModalLock.IsAnyLocked && !SceneTransitionService.IsTransitioning &&
            interactor.GetComponent<PlayerAnimatedInteractionController>() is { IsActive: false };

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            listener = interactor;
            if (!conversation.RequestForemanInteraction(interactor, BeginDialogue, CancelReservation)) listener = null;
        }

        public bool TryResolveStaging(out DialogueStagingPlan staging)
        {
            staging = default;
            Vector3 ground = transform.position + transform.forward * ConversationDistance;
            if (!Physics.Raycast(ground + Vector3.up * .4f, Vector3.down, out RaycastHit hit, .8f,
                ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < .8f) return false;
            ground.y = hit.point.y;
            Quaternion facing = Quaternion.LookRotation(-Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up);
            Vector3 entryRoot = ground + facing * PlayerDialogueActions.EntryGroundOffset + Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 exitRoot = ground + facing * PlayerDialogueActions.ExitGroundOffset + Vector3.up * PlayerFactory.GroundedRootOffset;
            staging = new DialogueStagingPlan(
                new PlayerAnimatedInteractionPose(entryRoot, facing, ground + facing * PlayerDialogueActions.EntryPelvisFromGround),
                ground + facing * PlayerDialogueActions.ActionPelvisFromGround,
                new PlayerAnimatedInteractionPose(exitRoot, facing, ground + facing * PlayerDialogueActions.ExitPelvisFromGround));
            return true;
        }

        private void BeginDialogue()
        {
            PlayerInteractor source = listener;
            if (source == null || !isActiveAndEnabled || !TryResolveStaging(out DialogueStagingPlan staging) ||
                !session.Begin(graph, staging,
                    new DialogueParticipant(foreman, transform, foreman.Head, NpcVoiceCatalog.WatchmanDesignId), source,
                    speaking => conversation.SetForemanDialogueSpeaking(source, speaking),
                    () => ReleaseReservation(source)))
                ReleaseReservation(source);
        }

        public bool SelectChoice(bool accepts) => session != null && session.SelectChoice(accepts ? 0 : 1);
        public bool Confirm() => session != null && session.Confirm();
        public void Cancel()
        {
            if (session != null && session.IsActive) session.Cancel();
            else ReleaseReservation(listener);
        }
        private void ReleaseReservation(PlayerInteractor source)
        {
            if (conversation != null && source != null) conversation.CancelForemanInteraction(source);
            listener = null;
        }
        private void CancelReservation()
        {
            session?.RestoreImmediate();
            listener = null;
        }
        private void Update()
        {
            // A pending ambient pair has not taken manual control yet.
            if (listener != null && session != null && !session.IsActive &&
                GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Gameplay)) ReleaseReservation(listener);
        }
        private void OnDisable() { session?.RestoreImmediate(); ReleaseReservation(listener); }
        private void OnDestroy() { session?.RestoreImmediate(); ReleaseReservation(listener); }
    }
}
