using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(200)]
    public sealed partial class RidgeKioskVendor : MonoBehaviour, ISpeechFaceActor
    {
        private const string PieQuestion = "kiosk.talk.pies";
        private const string BeerQuestion = "kiosk.talk.beer";
        private static readonly DialogueGraph Conversation = new DialogueGraph("choice",
            DialogueNode.Choice("choice", new DialogueChoice(PieQuestion, "pies"), new DialogueChoice(BeerQuestion, "beer")),
            DialogueNode.Line("pies", DialogueSpeaker.Hero, PieQuestion, "piesReply"),
            DialogueNode.Line("piesReply", DialogueSpeaker.Npc, "kiosk.reply.pies", "end"),
            DialogueNode.Line("beer", DialogueSpeaker.Hero, BeerQuestion, "beerReply"),
            DialogueNode.Line("beerReply", DialogueSpeaker.Npc, "kiosk.reply.beer", "end"),
            DialogueNode.End("end"));

        public VillageResidentPresentation Actor { get; private set; }
        public DialogueSessionController Session { get; private set; }
        public string CharacterId { get; private set; }
        public bool HasDialogue => Session != null;
        private SpeechFaceAtlasPresenter facePresenter;
        private MaterialPropertyBlock ordinaryFace;
        private Renderer face;
        private object faceOwner;
        private RidgeKioskTarget target;
        private Transform approach, kiosk;
        private Camera streetCamera;
        private float nextIdleSample;

        public string PromptKey => "kiosk.talk.prompt";
        public Vector3 InteractionPosition => transform.position;

        public static RidgeKioskVendor Build(Transform kiosk, Transform sellerAnchor,
            Transform talkAnchor, Transform approachAnchor,
            string characterId = DefaultNpcPopulation.RidgeKioskSeller, bool hasDialogue = true)
        {
            if (hasDialogue && characterId != DefaultNpcPopulation.RidgeKioskSeller)
                throw new ArgumentException("Only the existing Ridge seller owns this conversation.", nameof(characterId));
            var actor = DefaultNpcFactory.CreateForCharacter(kiosk, characterId);
            actor.name = characterId == DefaultNpcPopulation.RidgeKioskSeller ? "Ridge kiosk seller" : "City kiosk seller";
            actor.transform.SetPositionAndRotation(sellerAnchor.position, kiosk.rotation);
            CityPortCrew.AlignWorkerModelWithPlacement(actor);
            NpcFootstepSources.Unregister(actor.transform); // The seller stays behind the counter.

            Transform host = new GameObject(hasDialogue ? "Talk through window" : "Silent kiosk seller").transform;
            host.SetParent(kiosk, false);
            host.position = talkAnchor.position;
            var seller = host.gameObject.AddComponent<RidgeKioskVendor>();
            seller.kiosk = kiosk;
            seller.approach = approachAnchor;
            seller.Actor = actor;
            seller.CharacterId = characterId;
            // New city sellers retain a standing idle and hand contact only.
            // They own no speech presentation, talk target or dialogue session.
            if (!hasDialogue)
            {
                seller.SamplePose();
                return seller;
            }
            seller.Session = host.gameObject.AddComponent<DialogueSessionController>();
            seller.face = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "GEO_FaceSurface").GetComponent<Renderer>();
            seller.ordinaryFace = new MaterialPropertyBlock();
            seller.face.GetPropertyBlock(seller.ordinaryFace);
            seller.facePresenter = new SpeechFaceAtlasPresenter();
            string atlas = actor.GetComponent<DefaultNpcAppearance>().CurrentAtlas.name;
            // The original 128-pixel face sits at the bottom right of the 512-pixel wardrobe atlas.
            if (!seller.facePresenter.Configure(seller.face, "Dialogue/Faces/Workers/" + atlas, false,
                    new Rect(.75f, 0, .25f, .25f)))
                throw new InvalidOperationException("Missing kiosk seller speech face for " + atlas);
            seller.SamplePose();
            return seller;
        }

        internal void BindWindow(RidgeKioskTarget window) => target = window;

        public bool CanInteract(PlayerInteractor player) =>
            isActiveAndEnabled && HasDialogue && player != null && player.InputEnabled &&
            AtCounter &&
            !Session.IsActive && !SceneTransitionService.IsTransitioning &&
            !BarMinigameModalLock.IsAnyLocked && !CounterMenuInput.IsBlockedByOtherUi() &&
            target && target.CanReach(player);

        public void Interact(PlayerInteractor player)
        {
            TryStartFromMenu(player);
        }

        public bool TryStartFromMenu(PlayerInteractor player)
        {
            if (!CanInteract(player)) return false;
            Vector3 ground = approach.position;
            if (!Physics.Raycast(ground + Vector3.up * .4f, Vector3.down, out var hit, .85f, ~0,
                    QueryTriggerInteraction.Ignore) || hit.normal.y < .8f) return false;
            ground.y = hit.point.y;
            Quaternion facing = Quaternion.LookRotation(-kiosk.forward, Vector3.up);
            Vector3 root = ground + Vector3.up * PlayerFactory.GroundedRootOffset;
            var entry = new PlayerAnimatedInteractionPose(root + facing * PlayerDialogueActions.EntryGroundOffset,
                facing, ground + facing * PlayerDialogueActions.EntryPelvisFromGround);
            var exit = new PlayerAnimatedInteractionPose(root + facing * PlayerDialogueActions.ExitGroundOffset,
                facing, ground + facing * PlayerDialogueActions.ExitPelvisFromGround);
            var staging = new DialogueStagingPlan(entry, ground + facing * PlayerDialogueActions.ActionPelvisFromGround, exit);
            return Session.Begin(Conversation, staging, new DialogueParticipant(this, Actor.transform, Actor.Head,
                CharacterId), player, null, null);
        }

        private void LateUpdate()
        {
            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            if (courtStation != null) { AdvanceCourtVendor(Time.deltaTime); return; }
            if (Time.time < nextIdleSample) return;
            if (streetCamera == null) streetCamera = Camera.main;
            float distance = streetCamera != null ? (streetCamera.transform.position - transform.position).sqrMagnitude : 0f;
            bool talking = Session != null && Session.IsActive;
            nextIdleSample = Time.time + (talking || distance < 64f ? 0f : distance < 900f ? .12f : .65f);
            if (distance < 3600f || talking) SamplePose();
        }

        private void SamplePose()
        {
            bool talking = Session != null && Session.IsActive;
            Actor.ApplyIdleVariation(Time.time, false, talking ? 1f : 0f,
                talking && Session.HeroHead != null ? Session.HeroHead.position : (Vector3?)null);
            // One hand rests on the rear lip. The other keeps the worker's ordinary idle.
            Actor.ApplyHandContacts(kiosk.TransformPoint(new Vector3(.24f, 1.05f, .92f)), null);
        }

        private void OnDisable()
        {
            Session?.RestoreImmediate();
            ResetCourtVendor();
        }

        public bool TrySetSpeechFace(object owner, SpeechFacePose pose)
        {
            if (!HasDialogue || facePresenter == null) return false;
            if (faceOwner != null && !ReferenceEquals(owner, faceOwner)) return false;
            faceOwner = owner;
            return facePresenter.Apply(pose);
        }

        public void ReleaseSpeechFace(object owner)
        {
            if (!ReferenceEquals(owner, faceOwner)) return;
            if (face != null) face.SetPropertyBlock(ordinaryFace);
            faceOwner = null;
        }
    }
}
