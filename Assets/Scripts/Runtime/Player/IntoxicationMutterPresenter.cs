using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The drunk hero muttering to himself: which line, when, how far gone it
    /// comes out, and every reason not to say it at all.
    ///
    /// It owns its own <see cref="NpcSpeechBubbleView"/> rather than borrowing
    /// the City's. Two reasons, both load-bearing: seven of the nine scenes he
    /// walks in have no bubble view at all, so borrowing would leave him mute
    /// in most of the game; and a shared view's eviction ladder could trade his
    /// line against a quarrel in the park, in either direction.
    ///
    /// The clock is <see cref="HeroMutterModel"/> and the words are
    /// <see cref="HeroMutterLines"/> put through <see cref="HeroMutterSlur"/>.
    /// This class is the gate and the wiring, and holds no timing of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntoxicationMutterPresenter : MonoBehaviour
    {
        public const string RuntimeObjectName = "Hero Mutter";

        /// <summary>
        /// The seed salt. Distinct from the vertigo whirlpool's `0x7E11`, the
        /// rise model's `0x51AE` and the dolly zoom's `0x5A17`, so the drunk
        /// systems beat against each other instead of landing together.
        /// </summary>
        public const int MutterSeedSalt = 0x6D75;

        /// <summary>
        /// How much wider his keystrokes detune at full slur, in cents on top
        /// of the profile's own. The pitch is the one thing about a voice that
        /// is not baked into the shared clip bank, so it is the one thing that
        /// can degrade with him.
        /// </summary>
        public const float ExtraJitterCentsAtFullSlur = 26f;

        private NpcSpeechBubbleView bubbles;
        private HeroMutterModel model;
        private readonly HeroMutterOrder order = new HeroMutterOrder();
        private InteractionPromptView prompt;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private Player3DCharacterPresentation heroPresentation;
        private IntoxicationProfile profile;
        private bool falling;
        private bool initialized;
        private int lineOrdinal;
        private int seed;

        public bool IsInitialized => initialized;
        public HeroMutterModel Model => model;
        public NpcSpeechBubbleView Bubbles => bubbles;

        /// <summary>The key of the last line he actually opened.</summary>
        public string LastLineKey { get; private set; } = string.Empty;

        /// <summary>That line as it came out of his mouth, slurred.</summary>
        public string LastLineText { get; private set; } = string.Empty;

        public int SpokenLineCount { get; private set; }

        public bool IsMuttering =>
            initialized && bubbles != null && bubbles.IsShowing(this);

        /// <summary>
        /// Raises the view, declares the hero as its one speaker and hangs his
        /// lines off his own head bone.
        /// </summary>
        public bool Initialize(
            PlayerRuntime player,
            Camera camera,
            InteractionPromptView promptView)
        {
            motor = player.Motor;
            interactor = player.Interactor;
            heroPresentation =
                player.Visual as Player3DCharacterPresentation;
            prompt = promptView;
            seed = GameSessionState.CitySeed ^ MutterSeedSalt;
            model = new HeroMutterModel(seed);
            order.Reset();
            lineOrdinal = 0;

            bubbles = GetComponent<NpcSpeechBubbleView>();
            if (bubbles == null)
            {
                bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            }

            Transform head = ResolveHeadAnchor();
            if (head == null || player.GameObject == null)
            {
                initialized = false;
                return false;
            }

            // The listener is the hero himself, so his own words never fade
            // and never cull: the earshot profile is here for the keystroke's
            // rolloff, not for a distance he can never be at.
            bubbles.Initialize(camera, player.GameObject.transform);
            bubbles.DeclareSpeaker(
                this,
                head,
                NpcVoiceCatalog.HeroMutterDesignId,
                NpcEarshotProfile.Conversation);
            initialized = true;
            return true;
        }

        /// <summary>One frame of the hero's state, from the status
        /// controller that already computes it.</summary>
        public void SetState(IntoxicationProfile current, bool isFalling)
        {
            profile = current;
            falling = isFalling;
        }

        /// <summary>Takes down whatever is up and returns to silence.</summary>
        public void Silence()
        {
            if (bubbles != null)
            {
                bubbles.Dismiss(this);
            }

            model?.Reset();
        }

        /// <summary>Test seam: restarts the mutter clock and its pool cursor
        /// from a known seed, mirroring <c>ReseedVertigo</c>.</summary>
        public void ReseedMutter(int newSeed)
        {
            seed = newSeed;
            model = new HeroMutterModel(newSeed);
            order.Reset();
            lineOrdinal = 0;
            Silence();
        }

        /// <summary>
        /// Every reason he keeps his mouth shut. The modal lock alone covers
        /// the prologue, the pause, the map, the inventory, the journal, every
        /// minigame and the shop; the motor's own input covers every seat,
        /// door and transition, and the bus, which switches the component off
        /// rather than the flag.
        /// </summary>
        public bool CanMutter()
        {
            if (!initialized ||
                bubbles == null ||
                profile.Level <= IntoxicationStageRules.BalanceThreshold ||
                !HeroMutterLines.HasPool(profile.Stage) ||
                falling)
            {
                return false;
            }

            if (GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked ||
                GameSessionState.IsRidingAVehicle)
            {
                return false;
            }

            if (motor == null ||
                !motor.enabled ||
                !motor.InputEnabled ||
                (interactor != null && !interactor.InputEnabled))
            {
                return false;
            }

            // Nobody talks over the man answering him in the panel.
            if (prompt != null && prompt.IsSpeaking)
            {
                return false;
            }

            // And nothing hangs over a head that is not being drawn: every
            // first-person view in the game takes his off.
            Player3DAssetRegistry registry = heroPresentation != null
                ? heroPresentation.Registry
                : null;
            return registry != null &&
                   registry.Anchors.Head != null &&
                   Player3DHeadVisibility.IsHeadDrawn(registry);
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            bool allowed = CanMutter();
            if (!allowed && model.IsSpeaking)
            {
                // A line cut in half is not resumed: he was interrupted, and
                // the next thing he says starts from its own beginning.
                Silence();
                return;
            }

            model.Advance(
                GameTimeScaleRuntime.CalendarDeltaTime,
                allowed,
                ResolvePace());
            if (model.ConsumeLineCue())
            {
                Speak();
            }
        }

        /// <summary>Zero at the balance threshold, one at the top — the same
        /// expression the drunk camera and the whirlpool pace on.</summary>
        private float ResolvePace()
        {
            return Mathf.InverseLerp(
                IntoxicationStageRules.BalanceThreshold /
                (float)IntoxicationStageRules.MaximumLevel,
                1f,
                profile.Normalized);
        }

        private void Speak()
        {
            IntoxicationStage stage = profile.Stage;
            if (!HeroMutterLines.HasPool(stage))
            {
                return;
            }

            string key = order.ConsumeKey(stage);
            string text = LocalizationService.Get(key);
            int lineSeed = unchecked((int)CitySoundStableHash.Combine(
                unchecked((uint)seed),
                unchecked((uint)lineOrdinal)));
            lineOrdinal++;
            string slurred = HeroMutterSlur.Apply(
                text,
                profile.MutterSlurAmount,
                lineSeed,
                HeroMutterLines.MaximumSlurredLengthFor(stage));
            if (!bubbles.ShowAt(this, slurred, Time.unscaledTime))
            {
                return;
            }

            // Whether this line scatters is latched HERE, from the pool it
            // came out of, so crossing eighty while it is on screen cannot
            // hand a two-row line to a layout that lays out one row.
            bubbles.SetDrunkenness(
                this,
                HeroMutterLines.ScattersAt(stage)
                    ? profile.MutterScatterAmount
                    : 0f,
                profile.MutterSlurAmount * ExtraJitterCentsAtFullSlur,
                unchecked((uint)lineSeed));
            LastLineKey = key;
            LastLineText = slurred;
            SpokenLineCount++;
        }

        private Transform ResolveHeadAnchor()
        {
            Player3DAssetRegistry registry = heroPresentation != null
                ? heroPresentation.Registry
                : null;
            return registry != null ? registry.Anchors.Head : null;
        }

        private void OnDisable()
        {
            Silence();
        }
    }
}
