using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The drunk hero being sick: the bout the nausea gauge lost, run to
    /// its end — the head down, the stream on and off three times, the
    /// key lent and returned, the sounds at his mouth and on the floor,
    /// the relief taken off his intoxication and the mark left on his
    /// face.
    ///
    /// A controller of its own rather than a branch of
    /// <see cref="IntoxicationNauseaController"/>, because the two have
    /// different gates and a different life. The gauge starts only on
    /// his feet, steady and free, and is abandoned the moment he falls or
    /// sits; the bout, once begun, goes on regardless — a man can be sick
    /// lying where he fell, and the fall is the one thing most likely to
    /// follow the gauge. So the bout takes NO <see cref="BarMinigameModalLock"/>
    /// (the fall holds that lock, and the bout must run under it), never
    /// disables the interactor, and is cut short only by a scene
    /// transition, a vehicle ride or <see cref="Shutdown"/>. It borrows the
    /// interact key the way the gauge does (<see cref="PlayerInteractor.SetInteractKeyClaimed"/>),
    /// so that E over a door does not open the door mid-heave.
    ///
    /// It runs on SCALED time, unlike the gauge's calendar clock: the
    /// stream's arc, the particles, the sounds and the ragdoll's joints all
    /// live on <c>Time.deltaTime</c>, and a pause must freeze the whole of
    /// it together.
    ///
    /// The relief is the user's decision of 2026-09-05: twenty points a
    /// bout, split 7 / 7 / 6 across the bursts and granted as each burst
    /// ends, so a bout cut short keeps what it earned. Nothing here is
    /// seeded but the residue; the seed goes to the effect through the
    /// status controller.
    /// </summary>
    public sealed class IntoxicationVomitController
    {
        /// <summary>
        /// The seed salt. Distinct from the nausea's `0x4E41`, the mutter's
        /// `0x6D75` and the rest, so the residue's hashes never line up
        /// with another drunk system's draws.
        /// </summary>
        public const int VomitSeedSalt = 0x564D;

        /// <summary>
        /// The splat sound at most this often, on the bout's own clock.
        /// The stream lands a rod nearly every frame; the sound table's
        /// cooldown would thin that too, but the rate limit belongs to
        /// the one who plays the sound.
        /// </summary>
        public const float SplatMinimumIntervalSeconds = 0.25f;

        /// <summary>Where the sounds come from when the rig has no mouth and no head.</summary>
        public const float FallbackMouthHeightMetres = 1.55f;

        private const string LogCategory = "intoxication";
        private const string ReliefReason = "vomit";

        private readonly HeroVomitModel model = new HeroVomitModel();
        private readonly int seed;
        private readonly PlayerInteractor interactor;
        private readonly Transform heroRoot;
        private readonly Player3DCharacterPresentation heroPresentation;
        private readonly IPlayerVomitPresentation presentation;
        private readonly Player3DRagdollController ragdoll;
        private HeroVomitStreamEffect effect;
        private bool keyClaimed;
        private float clock;
        private float lastSplatTime = float.NegativeInfinity;
        private bool headDriveApplied;

        public IntoxicationVomitController(PlayerRuntime player, int newSeed)
        {
            seed = newSeed;
            interactor = player.Interactor;
            heroRoot = player.GameObject != null
                ? player.GameObject.transform
                : null;
            heroPresentation = player.Visual as Player3DCharacterPresentation;
            presentation = player.Visual as IPlayerVomitPresentation;
            ragdoll = player.Ragdoll;
        }

        public int Seed => seed;
        public HeroVomitModel Model => model;
        public HeroVomitStreamEffect Effect => effect;

        /// <summary>A bout is under way, the recovery of the head included.</summary>
        public bool IsActive => model.IsActive;

        /// <summary>The stream is running: inside one of the bursts.</summary>
        public bool IsVomiting => model.IsVomiting;

        public PlayerVomitPose Pose => model.Pose;

        /// <summary>The key is his to hold while the bursts play.</summary>
        public bool IsKeyClaimed => keyClaimed;

        /// <summary>Splats played this bout, for diagnostics.</summary>
        public int SplatsPlayed { get; private set; }

        /// <summary>
        /// Hands the controller the stream. The effect reports what its
        /// rods hit; the splat sound is played from here off that report.
        /// Rebinding unhooks the previous effect first.
        /// </summary>
        public void Bind(HeroVomitStreamEffect newEffect)
        {
            if (ReferenceEquals(effect, newEffect))
            {
                return;
            }

            if (effect != null)
            {
                effect.OnImpact -= OnImpact;
            }

            effect = newEffect;
            if (effect != null)
            {
                effect.OnImpact += OnImpact;
            }
        }

        /// <summary>
        /// The bout starts now, from the top. Called by the status
        /// controller on the gauge's fail cue; a bout already running
        /// starts over, as the model does.
        /// </summary>
        public void Begin()
        {
            model.Begin();
            clock = 0f;
            lastSplatTime = float.NegativeInfinity;
            SplatsPlayed = 0;
            ClaimKey();
            GameLog.Info(
                LogCategory,
                "vomit_begun",
                GameLog.Field("intoxication", GameSessionState.IntoxicationLevel));
        }

        /// <summary>
        /// Debug and test seam: a bout now, the gauge skipped. No gate on
        /// the fall on purpose — the bout is meant to run lying down.
        /// </summary>
        public bool DebugForceBout()
        {
            if (model.IsActive)
            {
                return false;
            }

            Begin();
            return true;
        }

        /// <summary>
        /// One frame of SCALED time, zero while paused, from the status
        /// controller after the drunk pose and the nausea have been pushed,
        /// so the head-down reaches the presentation's late pass this same
        /// frame and the stream finds the mouth already lowered.
        /// </summary>
        public void Tick(float scaledDeltaTime, bool paused)
        {
            if (model.IsActive &&
                (SceneTransitionService.IsTransitioning ||
                 GameSessionState.IsRidingAVehicle))
            {
                Cancel();
                return;
            }

            float step = paused || float.IsNaN(scaledDeltaTime)
                ? 0f
                : Mathf.Max(0f, scaledDeltaTime);
            if (model.IsActive)
            {
                clock += step;
            }

            model.Advance(step);
            DrainCues();
            // The gauge releases the key when its own bout resolves or is
            // cancelled. Under a bout of vomiting that can only happen off
            // the F9 button, but the claim is taken back all the same:
            // the interactor keeps one flag, not a count.
            if (keyClaimed &&
                interactor != null &&
                !interactor.InteractKeyClaimed)
            {
                interactor.SetInteractKeyClaimed(true);
            }

            PlayerVomitPose pose = model.Pose;
            presentation?.SetVomit(pose);
            ApplyHeadDrive(pose);
        }

        /// <summary>
        /// The bout stops at once: the stream is cleared (the floor keeps
        /// its marks), the head is let go, the key is his again and the
        /// presentation is handed None. Safe to call on a bout that is
        /// not running.
        /// </summary>
        public void Cancel()
        {
            bool wasActive = model.IsActive;
            float elapsed = model.Time;
            model.Cancel();
            // The queue is empty in practice — every frame drains it — but
            // a Begin cancelled before its first Tick still holds the
            // opening retch, and a retch after the fact is wrong.
            while (model.TryConsumeCue(out _))
            {
            }

            effect?.StopAndClear();
            ClearHeadDrive();
            ReleaseKey();
            presentation?.SetVomit(PlayerVomitPose.None);
            if (wasActive)
            {
                GameLog.Info(
                    LogCategory,
                    "vomit_cancelled",
                    GameLog.Field("elapsed", elapsed),
                    GameLog.Field("relief_granted", model.ReliefGranted),
                    GameLog.Field("intoxication", GameSessionState.IntoxicationLevel));
            }
        }

        /// <summary>Everything down at once: the scene is going away.</summary>
        public void Shutdown()
        {
            Cancel();
            model.Reset();
            Bind(null);
        }

        private void DrainCues()
        {
            while (model.TryConsumeCue(out HeroVomitCue cue))
            {
                switch (cue.Kind)
                {
                    case HeroVomitCueKind.Retch:
                        RetroAudio.PlayAt(RetroSfxId.Retch, ResolveMouthPosition());
                        break;
                    case HeroVomitCueKind.Gush:
                        RetroAudio.PlayAt(RetroSfxId.VomitGush, ResolveMouthPosition());
                        break;
                    case HeroVomitCueKind.BurstBegin:
                        effect?.SetFlow(cue.Strength, cue.BurstIndex);
                        GameLog.Info(
                            LogCategory,
                            "vomit_burst",
                            GameLog.Field("burst", cue.BurstIndex),
                            GameLog.Field("strength", cue.Strength),
                            GameLog.Field("at", cue.AtSeconds));
                        break;
                    case HeroVomitCueKind.BurstEnd:
                        effect?.SetFlow(0f, -1);
                        break;
                    case HeroVomitCueKind.Relief:
                        int removed = GameSessionState.RelieveIntoxication(
                            cue.Points,
                            ReliefReason);
                        GameLog.Info(
                            LogCategory,
                            "vomit_relieved",
                            GameLog.Field("burst", cue.BurstIndex),
                            GameLog.Field("points", cue.Points),
                            GameLog.Field("removed", removed),
                            GameLog.Field("intoxication", GameSessionState.IntoxicationLevel));
                        break;
                    case HeroVomitCueKind.Soil:
                        GameSessionState.SetHeroMouthSoiled(true, ReliefReason);
                        break;
                    case HeroVomitCueKind.Finished:
                        ReleaseKey();
                        GameLog.Info(
                            LogCategory,
                            "vomit_finished",
                            GameLog.Field("relief_granted", model.ReliefGranted),
                            GameLog.Field("intoxication", GameSessionState.IntoxicationLevel));
                        break;
                }
            }
        }

        /// <summary>
        /// The ragdoll's head follows the pose only while the ragdoll has
        /// the body; the angle changes every frame, so the drive is set
        /// every frame while it applies and cleared once when it stops.
        /// </summary>
        private void ApplyHeadDrive(in PlayerVomitPose pose)
        {
            if (ragdoll == null)
            {
                return;
            }

            if (ragdoll.IsSimulating && pose.HeadDownDegrees > 0.01f)
            {
                ragdoll.SetHeadDrive(pose.HeadDownDegrees);
                headDriveApplied = true;
            }
            else
            {
                ClearHeadDrive();
            }
        }

        private void ClearHeadDrive()
        {
            if (!headDriveApplied)
            {
                return;
            }

            headDriveApplied = false;
            if (ragdoll != null)
            {
                ragdoll.ClearHeadDrive();
            }
        }

        private void OnImpact(Vector3 point, Vector3 normal, int burstIndex)
        {
            if (!model.IsActive ||
                clock - lastSplatTime < SplatMinimumIntervalSeconds)
            {
                return;
            }

            lastSplatTime = clock;
            SplatsPlayed++;
            RetroAudio.PlayAt(RetroSfxId.VomitSplat, point);
        }

        private Vector3 ResolveMouthPosition()
        {
            Player3DAssetRegistry registry = heroPresentation != null
                ? heroPresentation.Registry
                : null;
            if (registry != null)
            {
                Transform anchor = registry.Anchors.Mouth != null
                    ? registry.Anchors.Mouth
                    : registry.Anchors.Head;
                if (anchor != null)
                {
                    return anchor.position;
                }
            }

            return heroRoot != null
                ? heroRoot.position + Vector3.up * FallbackMouthHeightMetres
                : Vector3.zero;
        }

        private void ClaimKey()
        {
            if (interactor == null)
            {
                return;
            }

            interactor.SetInteractKeyClaimed(true);
            keyClaimed = true;
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
    }
}
