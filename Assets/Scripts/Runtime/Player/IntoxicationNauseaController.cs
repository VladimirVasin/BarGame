using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The drunk hero holding it down: when a bout comes, the gauge that
    /// runs it, the key it borrows, the body it asks for, and every reason
    /// a bout does not start or is abandoned.
    ///
    /// A plain object, not a component: <see cref="IntoxicationStatusController"/>
    /// ticks it from its own Update, right after it has pushed the rest of
    /// the drunk pose, so the hand at the mouth reaches the presentation
    /// the same frame and not the one after. Only the gauge's IMGUI view is
    /// a component, on a child of its own like the muttering.
    ///
    /// It takes NO modal lock and never disables the interactor. Both read
    /// as "the hero is busy" to the balance model and the fall gate, and a
    /// man who has stopped staggering because he is about to be sick is the
    /// wrong picture. He keeps walking; only the interact key is lent to
    /// the gauge (<see cref="PlayerInteractor.SetInteractKeyClaimed"/>).
    ///
    /// A success is reported and shown and nothing more. A fail is handed
    /// on: it raises a cue the status controller reads once
    /// (<see cref="ConsumeFailCue"/>) and answers by starting the bout of
    /// vomiting in <see cref="IntoxicationVomitController"/>. The key is
    /// released here on resolve all the same — the bout claims it again
    /// in the same Update, and the interactor runs earlier in the frame
    /// than the status controller, so no press falls through the gap.
    /// While that bout runs the clock is held with its gate closed, which
    /// rearms the full rest: after being sick he is left alone for a while.
    /// </summary>
    public sealed class IntoxicationNauseaController
    {
        /// <summary>
        /// The seed salt. Distinct from the dolly zoom's `0x5A17`, the
        /// whirlpool's `0x7E11`, the mutter's `0x6D75`, the rise's `0x51AE`
        /// and the drunk head's `0x4E0D`, so the bouts beat against the
        /// other drunk systems instead of landing on their beats.
        /// </summary>
        public const int NauseaSeedSalt = 0x4E41;

        /// <summary>How long the result word stays under the gauge.</summary>
        public const float VerdictSeconds = 1.5f;

        /// <summary>The gauge hangs off a point this high on the hero's root — his chest.</summary>
        public const float HeroAnchorHeightMetres = 1.0f;

        private const string LogCategory = "intoxication";

        private readonly HeroNauseaClock clock;
        private readonly HeroNauseaGaugeModel gauge = new HeroNauseaGaugeModel();
        private readonly PlayerNauseaModel poseModel;
        private readonly int seed;
        private readonly PlayerMotor motor;
        private readonly PlayerInteractor interactor;
        private readonly Transform heroRoot;
        private readonly Player3DCharacterPresentation heroPresentation;
        private readonly IPlayerNauseaPresentation nauseaPresentation;
        private int boutOrdinal;
        private float boutPace;
        private bool keyClaimed;
        private float verdictRemaining;
        private bool failDue;

        public IntoxicationNauseaController(PlayerRuntime player, int newSeed)
        {
            seed = newSeed;
            clock = new HeroNauseaClock(newSeed);
            poseModel = new PlayerNauseaModel(newSeed ^ 0x2B);
            motor = player.Motor;
            interactor = player.Interactor;
            heroRoot = player.GameObject != null
                ? player.GameObject.transform
                : null;
            heroPresentation = player.Visual as Player3DCharacterPresentation;
            nauseaPresentation = player.Visual as IPlayerNauseaPresentation;
        }

        public HeroNauseaClock Clock => clock;
        public HeroNauseaGaugeModel Gauge => gauge;
        public PlayerNauseaModel PoseModel => poseModel;

        /// <summary>A bout is running: the gauge is up and the key is his to hold.</summary>
        public bool IsBoutActive => gauge.IsRunning;

        /// <summary>The last bout's outcome, while its word is still up.</summary>
        public HeroNauseaOutcome Verdict { get; private set; }

        public float VerdictRemaining => verdictRemaining;
        public bool IsVerdictShowing => verdictRemaining > 0f;
        public int BoutsBegun { get; private set; }
        public int Successes { get; private set; }
        public int Fails { get; private set; }
        public PlayerNauseaPose Pose => poseModel.Pose;

        /// <summary>The world point the gauge hangs beside, if the hero is there.</summary>
        public bool TryGetHeroAnchor(out Vector3 world)
        {
            if (heroRoot == null)
            {
                world = Vector3.zero;
                return false;
            }

            world = heroRoot.position + Vector3.up * HeroAnchorHeightMetres;
            return true;
        }

        /// <summary>
        /// Whether a bout may START now: the last stage by the session's
        /// own level, on his feet and steady, and everything
        /// <see cref="CanRun"/> asks.
        /// </summary>
        public bool CanBegin(bool isFalling, bool isStaggering)
        {
            return HeroNauseaClock.IsNauseaStage(
                       GameSessionState.IntoxicationLevel) &&
                   !isStaggering &&
                   CanRun(isFalling);
        }

        /// <summary>
        /// Whether a bout may CONTINUE: not falling, not travelling, not
        /// riding, the motor his (no seat, no door, no bus) and the body
        /// not owned by a clip. A pause or another modal owner is not on
        /// this list — those HOLD the bout rather than end it.
        /// </summary>
        public bool CanRun(bool isFalling)
        {
            if (isFalling ||
                SceneTransitionService.IsTransitioning ||
                GameSessionState.IsRidingAVehicle)
            {
                return false;
            }

            if (motor == null ||
                !motor.enabled ||
                !motor.InputEnabled ||
                interactor == null ||
                !interactor.InputEnabled)
            {
                return false;
            }

            return heroPresentation == null ||
                   (!heroPresentation.IsClipActive &&
                    !heroPresentation.InteractionHandoffLocked);
        }

        /// <summary>Somebody else owns the frame: the gauge waits.</summary>
        private static bool IsHeldByOthers =>
            GameTimeScaleRuntime.IsPaused ||
            BarMinigameModalLock.IsAnyLocked;

        /// <summary>
        /// One frame on the calendar clock (unscaled, zero while paused),
        /// from the status controller that already knows whether he is
        /// falling or fighting for his balance. <paramref name="boutsSuspended"/>
        /// closes the clock's gate without touching a running bout: while
        /// he is being sick no new bout may start, and a closed gate
        /// rearms the full rest on purpose.
        /// </summary>
        public void Tick(
            float deltaTime,
            bool isFalling,
            bool isStaggering,
            bool boutsSuspended = false)
        {
            float step = float.IsNaN(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
            if (gauge.IsRunning)
            {
                if (!CanRun(isFalling))
                {
                    Cancel();
                }
                else if (!IsHeldByOthers)
                {
                    gauge.Advance(step, PlayerInteractor.IsInteractHeld());
                    if (!gauge.IsRunning)
                    {
                        Resolve();
                    }
                }
            }
            else if (!IsHeldByOthers)
            {
                clock.Advance(
                    step,
                    CanBegin(isFalling, isStaggering) && !boutsSuspended);
                if (clock.ConsumeBoutCue())
                {
                    BeginBout();
                }
            }

            if (verdictRemaining > 0f)
            {
                verdictRemaining = Mathf.Max(0f, verdictRemaining - step);
            }

            poseModel.Advance(
                step,
                gauge.IsRunning,
                gauge.IsRunning ? gauge.Strain : 0f);
            if (poseModel.ConsumeHiccupCue())
            {
                PlayHiccup();
            }

            PushPose();
        }

        /// <summary>
        /// Debug and test seam: a bout now, stage and clock ignored, the
        /// safety gate kept (no bout over a fall, a seat or a clip).
        /// </summary>
        public bool DebugForceBout()
        {
            if (gauge.IsRunning || !CanRun(false))
            {
                return false;
            }

            BeginBout();
            return true;
        }

        /// <summary>
        /// True once after a bout is lost. The status controller reads it
        /// in the same Update the gauge resolved and starts the vomiting;
        /// nothing else consumes it, and a shutdown drops it.
        /// </summary>
        public bool ConsumeFailCue()
        {
            if (!failDue)
            {
                return false;
            }

            failDue = false;
            return true;
        }

        /// <summary>The bout is abandoned: the key is his again, nothing is reported.</summary>
        public void Cancel()
        {
            if (!gauge.IsRunning)
            {
                return;
            }

            gauge.Cancel();
            ReleaseKey();
            clock.Rearm(HeroNauseaClock.InitialRestSeconds);
            GameLog.Info(
                LogCategory,
                "nausea_bout_cancelled",
                GameLog.Field("ordinal", boutOrdinal),
                GameLog.Field("elapsed", gauge.Elapsed));
        }

        /// <summary>Everything down at once: the scene is going away.</summary>
        public void Shutdown()
        {
            Cancel();
            ReleaseKey();
            failDue = false;
            verdictRemaining = 0f;
            Verdict = HeroNauseaOutcome.None;
            poseModel.Reset();
            PushPose();
        }

        private void BeginBout()
        {
            boutOrdinal++;
            int boutSeed = unchecked((int)CitySoundStableHash.Combine(
                unchecked((uint)seed),
                unchecked((uint)boutOrdinal)));
            boutPace = HeroNauseaClock.ResolvePace(
                GameSessionState.IntoxicationLevel);
            gauge.Begin(boutPace, boutSeed);
            interactor?.SetInteractKeyClaimed(true);
            keyClaimed = interactor != null;
            Verdict = HeroNauseaOutcome.None;
            verdictRemaining = 0f;
            BoutsBegun++;
            GameLog.Info(
                LogCategory,
                "nausea_bout_begun",
                GameLog.Field("ordinal", boutOrdinal),
                GameLog.Field("intoxication", GameSessionState.IntoxicationLevel),
                GameLog.Field("pace", boutPace),
                GameLog.Field("zone_speed", gauge.ZoneSpeed),
                GameLog.Field("zone_half_height", gauge.ZoneHalfHeight));
        }

        private void Resolve()
        {
            ReleaseKey();
            Verdict = gauge.Outcome;
            verdictRemaining = VerdictSeconds;
            if (Verdict == HeroNauseaOutcome.Success)
            {
                Successes++;
                RetroAudio.Play(RetroSfxId.Good);
            }
            else
            {
                Fails++;
                failDue = true;
                RetroAudio.Play(RetroSfxId.Bad);
            }

            GameLog.Info(
                LogCategory,
                "nausea_bout_resolved",
                GameLog.Field("ordinal", boutOrdinal),
                GameLog.Field("outcome", Verdict.ToString()),
                GameLog.Field("strain", gauge.Strain),
                GameLog.Field("elapsed", gauge.Elapsed),
                GameLog.Field("marker", gauge.Marker),
                GameLog.Field("zone_center", gauge.ZoneCenter));
            clock.ArmRest(boutPace);
        }

        private void ReleaseKey()
        {
            if (!keyClaimed)
            {
                return;
            }

            keyClaimed = false;
            interactor?.SetInteractKeyClaimed(false);
        }

        private void PlayHiccup()
        {
            Transform head = heroPresentation != null &&
                             heroPresentation.Registry != null
                ? heroPresentation.Registry.Anchors.Head
                : null;
            Vector3 at;
            if (head != null)
            {
                at = head.position;
            }
            else if (heroRoot != null)
            {
                at = heroRoot.position + Vector3.up * 1.6f;
            }
            else
            {
                return;
            }

            RetroAudio.PlayAt(RetroSfxId.Hiccup, at);
        }

        private void PushPose()
        {
            nauseaPresentation?.SetNausea(poseModel.Pose);
        }
    }
}
