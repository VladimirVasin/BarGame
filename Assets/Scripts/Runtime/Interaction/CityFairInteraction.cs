using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded, positioned action; the visible mechanism owns its sound.</summary>
    [DefaultExecutionOrder(400)]
    [DisallowMultipleComponent]
    public sealed class CityFairInteraction : MonoBehaviour, IInteractable
    {
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private VillageWorkroomHandContacts hands;
        private Transform mechanism, gripTarget, bell;
        private Transform ropeSpan, ropeTop, ropeTail, ropeSpanTop, ropeSpanBottom;
        private Quaternion mechanismRestRotation, bellRestRotation;
        private Vector3 mechanismRestPosition;
        private Quaternion ropeSpanRestRotation;
        private Vector3 ropeSpanRestPosition, ropeSpanRestScale;
        private float ropeSpanLength;
        private AudioSource sound;
        private AudioClip clip;
        private bool ready, owns, struck, soundPaused;

        public CityFairInteractionKind Kind { get; private set; }
        public PlayerDoorActionPlan Plan { get; private set; }
        public PlayerAnimatedInteractionController Controller => controller;
        public bool IsPlaying => owns;
        public Transform RightGrip => hands?.RightGrip;
        public Transform GripTarget => gripTarget;
        public Transform RopeTopAnchor => ropeTop;
        public Transform RopeTailAnchor => ropeTail;
        public Transform RopeSpanTopAnchor => ropeSpanTop;
        public Transform RopeSpanBottomAnchor => ropeSpanBottom;
        public float ContactWeight { get; private set; }
        public int CompletedCount { get; private set; }
        public string PromptKey => Kind == CityFairInteractionKind.Organ ? "interaction.fair_organ" : "interaction.fair_bell";
        public Vector3 InteractionPosition => ready ? Plan.InteractionPosition : transform.position;

        public static CityFairInteraction Attach(Transform owner, CityFairInteractionKind kind, PlayerRuntime player,
            PlayerAnimatedInteractionController controller, PlayerDoorActionPlan plan, Transform mechanismPivot,
            Transform gripAnchor, Transform bellSwingPivot = null)
        {
            if (owner == null || mechanismPivot == null || gripAnchor == null ||
                kind == CityFairInteractionKind.Bell && bellSwingPivot == null)
                throw new ArgumentException("Fair actions require their physical mechanism and grip anchors.");
            var result = owner.gameObject.AddComponent<CityFairInteraction>();
            result.Initialize(kind, player, controller, plan, mechanismPivot, gripAnchor, bellSwingPivot);
            return result;
        }

        private void Initialize(CityFairInteractionKind kind, PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController sharedController, PlayerDoorActionPlan plan,
            Transform mechanismPivot, Transform gripAnchor, Transform bellSwingPivot)
        {
            if (playerRuntime.GameObject == null || sharedController == null || !sharedController.IsInitialized)
                throw new ArgumentException("The fair requires the live hero and its initialized shared controller.");
            plan.Validate(nameof(plan));
            Kind = kind; Plan = plan; player = playerRuntime; controller = sharedController;
            mechanism = mechanismPivot; gripTarget = gripAnchor; bell = bellSwingPivot;
            mechanismRestPosition = mechanism.localPosition; mechanismRestRotation = mechanism.localRotation;
            if (bell != null)
            {
                bellRestRotation = bell.localRotation;
                ropeSpan = CityFairAssetProvider.FindPart(gameObject, "RopeSpanPivot");
                ropeTop = CityFairAssetProvider.FindPart(gameObject, "RopeTopAnchor");
                ropeTail = CityFairAssetProvider.FindPart(gameObject, "RopeTailAnchor");
                ropeSpanTop = CityFairAssetProvider.FindPart(gameObject, "RopeSpanTopAnchor");
                ropeSpanBottom = CityFairAssetProvider.FindPart(gameObject, "RopeSpanBottomAnchor");
                ropeSpanRestPosition = ropeSpan.localPosition;
                ropeSpanRestRotation = ropeSpan.localRotation;
                ropeSpanRestScale = ropeSpan.localScale;
                ropeSpanLength = Vector3.Distance(ropeSpanTop.position, ropeSpanBottom.position);
                if (ropeSpanLength < .1f)
                    throw new InvalidOperationException("The bell requires its full-size imported connecting rope.");
            }
            var registry = player.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            ready = CityFairPlayerActions.TryAttach(registry);
            if (!ready) throw new InvalidOperationException("The fair requires its authored production hero action bank.");
            hands = new VillageWorkroomHandContacts(player.GameObject.transform, registry);
            controller.PhaseChanged += OnPhase;
            controller.InteractionCompleted += OnCompleted;
            var voice = new GameObject(kind + " physical sound");
            voice.transform.SetParent(kind == CityFairInteractionKind.Bell ? bell : mechanism, false);
            sound = voice.AddComponent<AudioSource>();
            sound.playOnAwake = false; sound.spatialBlend = 1f; sound.dopplerLevel = 0f;
            sound.rolloffMode = AudioRolloffMode.Linear; sound.minDistance = 1.5f;
            sound.maxDistance = kind == CityFairInteractionKind.Organ ? 9f : 12f;
            sound.volume = kind == CityFairInteractionKind.Organ ? .22f : .32f;
            sound.priority = 175;
            GameAudioMixer.Route(sound, GameAudioGroup.SfxWorld);
            clip = CreateSound(kind); sound.clip = clip;
        }

        public bool CanInteract(PlayerInteractor interactor) => ready && isActiveAndEnabled && !owns &&
            interactor != null && interactor == player.Interactor && interactor.InputEnabled &&
            controller != null && controller.isActiveAndEnabled && !controller.IsActive &&
            !GameTimeScaleRuntime.IsPaused && !SceneTransitionService.IsTransitioning && !CounterMenuInput.IsBlockedByOtherUi() &&
            Mathf.Abs(player.GameObject.transform.position.y - Plan.EntryRootPosition.y) <= .35f;

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) Begin(); }

        public bool Begin()
        {
            if (!CanInteract(player.Interactor)) return false;
            owns = true; struck = false;
            try
            {
                bool accepted = controller.BeginPositioned(CityFairPlayerActions.Definition(Kind),
                    Plan.EntryPose, Plan.ActionHipPosition, Plan.ExitPose, .35f);
                if (!accepted) Finish(false);
                return accepted;
            }
            catch { Finish(false); throw; }
        }

        public bool Cancel()
        {
            if (!owns) return false;
            controller?.CancelActiveInteraction();
            Finish(false);
            return true;
        }

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (!owns) return;
            if (phase == PlayerAnimatedInteractionPhase.Looping)
            {
                controller.RequestExitAtLoopBoundaryWithClip(CityFairPlayerActions.ClipName(Kind, "Exit"));
                if (Kind == CityFairInteractionKind.Organ) sound.Play();
            }
            else if (phase == PlayerAnimatedInteractionPhase.Exiting && Kind == CityFairInteractionKind.Organ) sound.Stop();
            else if (phase == PlayerAnimatedInteractionPhase.Idle) Finish(false);
        }

        private void OnCompleted() { if (owns) Finish(true); }

        private void Update()
        {
            if (!owns) return;
            if (SceneTransitionService.IsTransitioning) { Cancel(); return; }
            if (GameTimeScaleRuntime.IsPaused)
            {
                if (sound != null && sound.isPlaying) { sound.Pause(); soundPaused = true; }
                return;
            }
            if (soundPaused) { sound.UnPause(); soundPaused = false; }
            if (Kind == CityFairInteractionKind.Bell && !struck && controller.Phase == PlayerAnimatedInteractionPhase.Looping &&
                controller.PhaseProgress >= .25f)
            {
                struck = true; sound.Play();
            }
        }

        private void LateUpdate() => RefreshPresentation();

        /// <summary>The normal late pass also supports deterministic scene captures.</summary>
        public void RefreshPresentation()
        {
            ContactWeight = 0f;
            if (!owns || controller == null || hands == null) return;
            PlayerAnimatedInteractionPhase phase = controller.Phase;
            float progress = controller.PhaseProgress;
            if (phase == PlayerAnimatedInteractionPhase.Entering) ContactWeight = CityFairPlayerActions.Smooth(progress);
            else if (phase == PlayerAnimatedInteractionPhase.Looping) ContactWeight = 1f;
            else if (phase == PlayerAnimatedInteractionPhase.Exiting) ContactWeight = CityFairPlayerActions.Smooth(1f - progress);
            float motion = phase == PlayerAnimatedInteractionPhase.Looping ? progress : 0f;
            if (Kind == CityFairInteractionKind.Organ)
                mechanism.localRotation = mechanismRestRotation * Quaternion.AngleAxis(CityFairPlayerActions.CrankDegrees(motion), Vector3.forward);
            else
            {
                // FBX authoring roots retain their unit factor: apply metres in
                // world space, never as hundredths-scale child local coordinates.
                Vector3 restWorld = mechanism.parent.TransformPoint(mechanismRestPosition);
                mechanism.position = restWorld - transform.up * CityFairPlayerActions.RopeDrop(motion);
                float swing = Mathf.Sin(motion * 4f * Mathf.PI) * Mathf.Sin(motion * Mathf.PI) * 18f;
                bell.localRotation = bellRestRotation * Quaternion.AngleAxis(swing, Vector3.right);
                // The knot follows the hand while the upper attachment follows
                // the bell lever. Only the authored connecting section changes
                // length/orientation; translating the whole rope detaches it.
                Vector3 ropeDirection = ropeTail.position - ropeTop.position;
                ropeSpan.position = ropeTop.position;
                ropeSpan.rotation = Quaternion.FromToRotation(Vector3.down, ropeDirection.normalized);
                ropeSpan.localScale = Vector3.Scale(ropeSpanRestScale,
                    new Vector3(1f, ropeDirection.magnitude / ropeSpanLength, 1f));
            }
            if (ContactWeight > 0f)
            {
                VillageWorkroomHandContacts.RefreshPresentation(player, controller);
                // The authored pose owns approach/release. Correct only the
                // held contact so that a late resample cannot detach the grip.
                if (ContactWeight >= .9999f) hands.Apply(gripTarget.position, null, 1f);
            }
        }

        private void Finish(bool completed)
        {
            if (!owns) return;
            owns = false; ContactWeight = 0f; soundPaused = false;
            if (completed) CompletedCount++;
            if (sound != null) sound.Stop();
            if (mechanism != null) { mechanism.localPosition = mechanismRestPosition; mechanism.localRotation = mechanismRestRotation; }
            if (bell != null) bell.localRotation = bellRestRotation;
            if (ropeSpan != null)
            {
                ropeSpan.localPosition = ropeSpanRestPosition;
                ropeSpan.localRotation = ropeSpanRestRotation;
                ropeSpan.localScale = ropeSpanRestScale;
            }
        }

        private static AudioClip CreateSound(CityFairInteractionKind kind)
        {
            const int rate = 22050;
            bool organ = kind == CityFairInteractionKind.Organ;
            var data = new float[Mathf.RoundToInt((organ ? 4f : 2.5f) * rate)];
            // An original eight-note pipe phrase; the bell uses decaying,
            // inharmonic partials. Both are emitted only by the moving object.
            int[] phrase = { 67, 71, 74, 72, 71, 69, 66, 67 };
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                if (organ)
                {
                    int note = Mathf.Min(7, Mathf.FloorToInt(t * 2f));
                    float frequency = 440f * Mathf.Pow(2f, (phrase[note] - 69f) / 12f);
                    float beat = t - note * .5f;
                    float envelope = Mathf.Clamp01(beat / .025f) * Mathf.Clamp01((.5f - beat) / .055f);
                    float phase = 2f * Mathf.PI * frequency * t;
                    data[i] = envelope * (.36f * Mathf.Sin(phase) + .12f * Mathf.Sin(phase * 2f) + .05f * Mathf.Sin(phase * 3f));
                }
                else
                {
                    float attack = Mathf.Clamp01(t / .003f);
                    float fundamental = 2f * Mathf.PI * 620f * t;
                    data[i] = attack * (.43f * Mathf.Sin(fundamental) * Mathf.Exp(-t * 2.1f) +
                        .22f * Mathf.Sin(fundamental * 2.76f) * Mathf.Exp(-t * 3.8f) +
                        .13f * Mathf.Sin(fundamental * 5.41f) * Mathf.Exp(-t * 8f));
                }
            }
            AudioClip result = AudioClip.Create(organ ? "Fair hand-cranked pipes" : "Fair small brass bell", data.Length, 1, rate, false);
            result.SetData(data, 0); return result;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            if (controller != null) { controller.PhaseChanged -= OnPhase; controller.InteractionCompleted -= OnCompleted; }
            if (sound != null) Destroy(sound.gameObject);
            if (clip != null) Destroy(clip);
        }
    }
}
