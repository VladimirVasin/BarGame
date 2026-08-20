using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>Which part of a work session is running.</summary>
    public enum CemeteryGraveWorkPhase
    {
        None = 0,

        /// <summary>The camera is coming down onto the hole.
        /// </summary>
        Entering = 1,

        /// <summary>His to work.</summary>
        Working = 2,

        /// <summary>The camera is going back over his shoulder.
        /// </summary>
        Leaving = 3
    }

    /// <summary>
    /// Makes the hero earn every act of the gravedigging.
    ///
    /// The job's four rungs used to be four presses of `E`. Each is
    /// now a piece of work: the hole comes out a course at a time
    /// across a lattice of six segments, the coffin goes down on two
    /// ropes that have to be eased in turn, the earth goes back the
    /// same way it came out, and the stone is stood up and tamped in
    /// while it tries to lean.
    ///
    /// Two rules hold the whole thing together.
    ///
    /// The first is that a session commits nothing until it is
    /// finished. Everything it puts in the world is its own and comes
    /// straight back out if the hero walks away, and the stage on the
    /// ladder only moves through
    /// <see cref="CemeteryGravediggingController.TryAdvance"/> at the
    /// last moment. So the job keeps its old promise — the worksite is
    /// a pure function of one stored value — without that value ever
    /// having to describe half a hole.
    ///
    /// The second is that the hero is not animated. The camera takes
    /// his own eye line and his body is leased out of sight while he
    /// works, so the spade is the only moving thing and never has to
    /// agree with a pair of hands. The framing is not decoration: it
    /// is what makes an unanimated hero honest.
    /// </summary>
    [DefaultExecutionOrder(95)]
    [DisallowMultipleComponent]
    public sealed class CemeteryGraveWorkController :
        MonoBehaviour,
        ICemeteryGraveWorkSession
    {
        public const string RuntimeRootName = "Cemetery Grave Work";
        public const float CameraBlendSeconds = 0.55f;
        public const float FeedbackSeconds = 3.0f;

        /// <summary>Localization for what the hero is doing and how.
        /// </summary>
        public const string DigTitleKey = "cemetery.work.dig.title";
        public const string CoffinTitleKey =
            "cemetery.work.coffin.title";
        public const string FillTitleKey = "cemetery.work.fill.title";
        public const string StoneTitleKey =
            "cemetery.work.stone.title";
        public const string SpadeHintKey = "cemetery.work.hint.spade";
        public const string FillHintKey = "cemetery.work.hint.fill";
        public const string CoffinHintKey =
            "cemetery.work.hint.coffin";
        public const string StoneRaiseHintKey =
            "cemetery.work.hint.stone.raise";
        public const string StoneSetHintKey =
            "cemetery.work.hint.stone.set";
        public const string PlaqueHintKey = "cemetery.plaque.hint";
        public const string AbandonedFeedbackKey =
            "cemetery.work.abandoned";

        /// <summary>How proud of the ground the stone starts before
        /// it is trodden in.</summary>
        public const float StoneLiftMeters = 0.24f;

        /// <summary>Roughly where the top of a single-grave monument
        /// is, for aiming a blow at it.</summary>
        public const float StoneHeadHeightMeters = 1.05f;

        /// <summary>Ropes are not creaked more often than this,
        /// however long a key is held.</summary>
        public const float RopeCreakInterval = 0.42f;
        public const float TampInterval = 0.30f;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private CemeteryGravediggingController job;
        private Transform playerRoot;
        private PlayerInteractor interactor;
        private PlayerPresentationVisibility visibility;
        private PlayerCameraFollow cameraFollow;
        private Camera workCamera;
        private IntoxicationHudView hud;
        private CemeteryGraveWorkView view;

        private IDisposable heroHidden;
        private CemeteryGraveWorkPhase phase;
        private CemeteryGraveWorkStage act;
        private string pendingFeedbackKey;

        private CemeteryGraveLatticeModel lattice;
        private readonly CemeteryStrokeModel stroke =
            new CemeteryStrokeModel();
        private CemeteryCoffinLowerModel lower;
        private CemeteryStoneSettleModel settle;

        private GameObject workingPit;
        private GameObject segmentFrame;
        private CemeteryShovelAnimator spadeAnimator;
        private CemeteryGraveSlings slings;
        private bool headOnRight;
        private Transform borrowedCoffin;
        private Transform borrowedStone;
        private GameObject sessionPlaque;
        private int target;
        private int strikeCount;
        private bool inscribing;
        private bool reading;
        private string epitaphDraft = string.Empty;
        private float ropeCue;
        private float tampCue;

        private bool cameraOwned;
        private CemeteryGraveWorkPhase cameraPhase;
        private Vector3 cameraBlendPosition;
        private Quaternion cameraBlendRotation;
        private float cameraBlendFieldOfView;
        private float cameraBlendElapsed;
        private bool previousCinematicMotion;
        private bool previousFixedPose;
        private Pose previousFixedCameraPose;
        private float previousFixedFieldOfView;

        /// <summary>True while an act is the session's business.
        /// </summary>
        public bool IsActive => phase != CemeteryGraveWorkPhase.None;

        public CemeteryGraveWorkPhase Phase => phase;

        /// <summary>Which rung of the ladder is being worked.
        /// </summary>
        public CemeteryGraveWorkStage Act => act;

        /// <summary>The lattice, while one of the two spade acts is
        /// running.</summary>
        public CemeteryGraveLatticeModel Lattice => lattice;

        public CemeteryStrokeModel Stroke => stroke;
        public CemeteryCoffinLowerModel Lower => lower;
        public CemeteryStoneSettleModel Settle => settle;

        /// <summary>Which segment the spade is aimed at, or
        /// <c>-1</c>.</summary>
        public int TargetSegment => target;

        /// <summary>
        /// True while the hero is stood at the finished stone with the
        /// plaque in front of him, deciding what to cut into it.
        /// </summary>
        public bool IsInscribing => inscribing;

        /// <summary>True while the hero is stood at a finished stone
        /// reading the board on it.</summary>
        public bool IsReading => reading;

        /// <summary>What is on the board so far.</summary>
        public string EpitaphDraft => epitaphDraft;

        /// <summary>How many words of the eight are left to him.
        /// </summary>
        public int EpitaphWordsLeft =>
            CemeteryEpitaph.MaximumWords -
            CemeteryEpitaph.CountWords(epitaphDraft);

        public CemeteryGravediggingPlan Plan =>
            job != null ? job.Plan : null;

        /// <summary>True while the act being worked is one the spade
        /// does.</summary>
        public bool IsSpadeAct =>
            act == CemeteryGraveWorkStage.Marked ||
            act == CemeteryGraveWorkStage.Coffined;

        public static CemeteryGraveWorkController Create(
            Transform parent,
            CemeteryGravediggingRegister register,
            PlayerRuntime player,
            PlayerCameraFollow follow,
            Camera camera,
            IntoxicationHudView intoxicationHud,
            Transform uiParent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (register == null || !register.HasWork)
            {
                return null;
            }

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            var controller =
                root.AddComponent<CemeteryGraveWorkController>();
            controller.playerRoot = player.GameObject != null
                ? player.GameObject.transform
                : null;
            controller.interactor = player.Interactor;
            controller.visibility = player.PresentationVisibility;
            controller.cameraFollow = follow;
            controller.workCamera = camera;
            controller.hud = intoxicationHud;
            if (uiParent != null)
            {
                controller.view =
                    uiParent.gameObject
                        .AddComponent<CemeteryGraveWorkView>();
                controller.view.Bind(controller, camera);
            }

            register.SetWorkSession(controller);
            controller.BindBoards(register);
            return controller;
        }

        /// <summary>
        /// Listens at every board in the yard, the ones already
        /// standing and the ones still to be stood up. Reading is a
        /// camera move on one particular stone, so the grave has to
        /// come with the request.
        /// </summary>
        private void BindBoards(CemeteryGravediggingRegister register)
        {
            IReadOnlyList<CemeteryGravediggingController> raised =
                register.Jobs;
            for (int index = 0; index < raised.Count; index++)
            {
                BindBoard(raised[index]);
            }

            BindBoard(register.Pending);
            register.JobRaised += BindBoard;
        }

        private void BindBoard(CemeteryGravediggingController raised)
        {
            if (raised == null)
            {
                return;
            }

            raised.PlaqueRead += () => TryBeginReading(raised);
        }

        /// <summary>
        /// Steps up to a finished board and reads it. The plaque
        /// carries its words as real letters on the brass, so reading
        /// is a camera move and not a panel — there is nothing to put
        /// on screen that is not already on the stone.
        /// </summary>
        public bool TryBeginReading(
            CemeteryGravediggingController grave)
        {
            if (IsActive ||
                !isActiveAndEnabled ||
                grave == null ||
                !grave.HasJob ||
                interactor == null ||
                cameraFollow == null ||
                workCamera == null ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            job = grave;
            reading = true;
            act = CemeteryGraveWorkStage.Sealed;
            phase = CemeteryGraveWorkPhase.Entering;
            pendingFeedbackKey = null;
            heroHidden = visibility?.AcquireHidden(this);
            BeginCameraBlend(CemeteryGraveWorkPhase.Entering);
            return true;
        }

        /// <inheritdoc />
        public bool TryBegin(
            CemeteryGravediggingController grave,
            CemeteryGraveWorkStage stage)
        {
            if (IsActive ||
                !isActiveAndEnabled ||
                grave == null ||
                !grave.HasJob ||
                interactor == null ||
                cameraFollow == null ||
                workCamera == null ||
                !IsPlayableAct(stage) ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            job = grave;
            act = stage;
            phase = CemeteryGraveWorkPhase.Entering;
            pendingFeedbackKey = null;
            heroHidden = visibility?.AcquireHidden(this);
            job.Site?.SetMarksVisible(false);
            BeginAct();
            BeginCameraBlend(CemeteryGraveWorkPhase.Entering);
            GameLog.Info(
                "city",
                "cemetery_grave_work_begun",
                GameLog.Field("plot", job.PlotId),
                GameLog.Field("act", stage.ToString()));
            return true;
        }

        /// <summary>The four rungs a session knows how to run.
        /// </summary>
        internal static bool IsPlayableAct(
            CemeteryGraveWorkStage stage)
        {
            return CemeteryGraveDigSiteInteraction.IsWorkingStage(
                stage);
        }

        /// <summary>What the hero is about to do, for the caption
        /// over the hole.</summary>
        internal static string GetTitleKey(
            CemeteryGraveWorkStage stage)
        {
            switch (stage)
            {
                case CemeteryGraveWorkStage.Dug:
                    return CoffinTitleKey;
                case CemeteryGraveWorkStage.Coffined:
                    return FillTitleKey;
                case CemeteryGraveWorkStage.Filled:
                    return StoneTitleKey;
                default:
                    return DigTitleKey;
            }
        }

        /// <summary>
        /// What the hero is being asked to do this instant. The stone
        /// act asks two different things of him and says so — heaving
        /// and striking are not one instruction.
        /// </summary>
        internal string GetLiveHintKey()
        {
            if (inscribing)
            {
                return PlaqueHintKey;
            }

            if (act == CemeteryGraveWorkStage.Filled &&
                settle != null &&
                settle.Phase != CemeteryStonePhase.Raising)
            {
                return StoneSetHintKey;
            }

            return GetHintKey(act);
        }

        internal static string GetHintKey(
            CemeteryGraveWorkStage stage)
        {
            switch (stage)
            {
                case CemeteryGraveWorkStage.Dug:
                    return CoffinHintKey;
                case CemeteryGraveWorkStage.Filled:
                    return StoneRaiseHintKey;
                case CemeteryGraveWorkStage.Coffined:
                    return FillHintKey;
                default:
                    return SpadeHintKey;
            }
        }

        private void Update()
        {
            if (phase == CemeteryGraveWorkPhase.None)
            {
                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                Abandon();
                return;
            }

            float deltaTime = Time.deltaTime;
            if (phase != CemeteryGraveWorkPhase.Working)
            {
                if (phase == CemeteryGraveWorkPhase.Entering &&
                    cameraPhase != CemeteryGraveWorkPhase.Entering)
                {
                    phase = CemeteryGraveWorkPhase.Working;
                }

                return;
            }

            if (reading)
            {
                if (WasLeavePressed() || WasWorkPressed())
                {
                    reading = false;
                    BeginLeaving();
                }

                return;
            }

            if (inscribing)
            {
                UpdateInscribing();
                return;
            }

            if (WasLeavePressed())
            {
                Abandon();
                return;
            }

            switch (act)
            {
                case CemeteryGraveWorkStage.Marked:
                case CemeteryGraveWorkStage.Coffined:
                    UpdateSpadeAct(deltaTime);
                    break;
                case CemeteryGraveWorkStage.Dug:
                    UpdateCoffinAct(deltaTime);
                    break;
                case CemeteryGraveWorkStage.Filled:
                    UpdateStoneAct(deltaTime);
                    break;
            }
        }

        private void LateUpdate()
        {
            UpdateOwnedCamera(Time.deltaTime);
        }

        /// <summary>
        /// Torn down under the session rather than by it. There is no
        /// frame left to blend a camera over, so the work is dropped
        /// and everything it held — the lock, the hero's own body, the
        /// camera — goes back in one step. Leaving any of that to a
        /// `LateUpdate` that will never run would strand the player
        /// invisible and unable to move.
        /// </summary>
        private void OnDisable()
        {
            if (!IsActive)
            {
                return;
            }

            CemeteryGraveWorkStage dropped = act;
            TearDownAct();
            RestoreAbandonedWorld(dropped);
            job.Site?.SetMarksVisible(true);
            job.SetPitDressingVisible(true);
            // No frame is coming, so the spade cannot be put down over
            // one: stand it back in the ground outright.
            job.RestSpade();
            RestoreCameraOwnership();
            pendingFeedbackKey = null;
            phase = CemeteryGraveWorkPhase.None;
            reading = false;
            heroHidden?.Dispose();
            heroHidden = null;
            modalLock.Restore();
        }

        // ---- the two spade acts -------------------------------

        private void UpdateSpadeAct(float deltaTime)
        {
            if (lattice == null || spadeAnimator == null)
            {
                return;
            }

            RefreshTarget();
            Vector3 face = GetTargetFace();
            spadeAnimator.SetTargets(
                face,
                CemeteryGraveWorkStance.GetHeapTop(
                    job.Plan,
                    CityCemeteryProgressivePitWorldBuilder
                        .GetHeapFullness(lattice)));
            CityCemeterySegmentFrameWorldBuilder.Place(
                segmentFrame != null ? segmentFrame.transform : null,
                job.Plan,
                lattice,
                target);
            if (spadeAnimator.IsStriking)
            {
                return;
            }

            // No bar and no window: pick the square and take the
            // course off it. Both spade acts are eighteen deliberate
            // choices about where to work next, and the rule that a
            // segment may not get ahead of its neighbours is the whole
            // of the difficulty.
            spadeAnimator.SetSwing(-1f);
            if (WasWorkPressed() && target >= 0)
            {
                ApplyStroke(CemeteryStrokeOutcome.Bite);
            }
        }

        private void ApplyStroke(CemeteryStrokeOutcome outcome)
        {
            if (outcome == CemeteryStrokeOutcome.None || target < 0)
            {
                return;
            }

            strikeCount++;
            lattice.TryStrike(target, outcome, out bool courseDone);
            Vector3 face = GetTargetFace();
            spadeAnimator.SetTargets(
                face,
                CemeteryGraveWorkStance.GetHeapTop(
                    job.Plan,
                    CityCemeteryProgressivePitWorldBuilder
                        .GetHeapFullness(lattice)));
            spadeAnimator.PlayStrike(outcome);
            RetroAudio.PlayAt(
                outcome == CemeteryStrokeOutcome.Bite
                    ? RetroSfxId.SpadeBite
                    : RetroSfxId.SpadeGlance,
                face);
            if (outcome == CemeteryStrokeOutcome.Bite)
            {
                RetroAudio.PlayAt(
                    RetroSfxId.SpadeToss,
                    job.Plan.SpoilCenter);
            }

            if (courseDone)
            {
                RebuildWorkingPit();
            }

            if (lattice.IsComplete)
            {
                Complete();
            }
        }

        private void RefreshTarget()
        {
            if (target < 0 || !lattice.IsWorkable(target))
            {
                target = lattice.FindWorkable(
                    Mathf.Max(target, 0),
                    1);
                return;
            }

            int step = ReadStep();
            if (step != 0 && !stroke.IsSwinging)
            {
                int moved = lattice.FindWorkable(
                    WrapSegment(target + step),
                    step);
                if (moved >= 0 && moved != target)
                {
                    target = moved;
                    RetroAudio.Play(RetroSfxId.UiMove);
                }
            }
        }

        private Vector3 GetTargetFace()
        {
            int segment = target >= 0 ? target : 0;
            return CityCemeteryProgressivePitWorldBuilder
                .GetSegmentFace(job.Plan, lattice, segment);
        }

        /// <summary>
        /// Every swing starts somewhere different on the bar, but
        /// always the same somewhere for the same strike of the same
        /// grave.
        /// </summary>
        private int GetStrokeSeed()
        {
            unchecked
            {
                return (int)(
                    PlotHash() ^
                    ((uint)strikeCount * 0x9E3779B9u) ^
                    ((uint)target * 0x85EBCA6Bu));
            }
        }

        // ---- the coffin --------------------------------------

        private void UpdateCoffinAct(float deltaTime)
        {
            if (lower == null || slings == null)
            {
                return;
            }

            // Each key lets its own end down, and the keys are bound to
            // the sides of the screen rather than to the ends of the
            // box: `Q` is the left of the shot and `E` the right,
            // whichever way this particular grave happens to face.
            // Nothing held means the box hangs where it is.
            bool left = IsLeftRopeHeld();
            bool right = IsWorkHeld();
            bool head = headOnRight ? right : left;
            bool foot = headOnRight ? left : right;
            lower.Advance(deltaTime, head, foot);
            slings.SetCoffinDrop(
                lower.HeadPayout,
                lower.FootPayout);
            if (borrowedCoffin != null)
            {
                borrowedCoffin.SetPositionAndRotation(
                    slings.GetCoffinPosition(
                        lower.HeadPayout,
                        lower.FootPayout),
                    slings.GetCoffinRotation(
                        lower.HeadPayout,
                        lower.FootPayout));
            }

            ropeCue -= deltaTime;
            if ((left || right) && ropeCue <= 0f)
            {
                ropeCue = RopeCreakInterval;
                RetroAudio.PlayAt(
                    RetroSfxId.RopeCreak,
                    job.Plan.Ground);
            }

            if (!lower.IsComplete)
            {
                return;
            }

            RetroAudio.PlayAt(
                RetroSfxId.CoffinSettle,
                job.Plan.Ground);
            if (lower.Succeeded)
            {
                Complete();
                return;
            }

            // It got away from him. The box is back on the bearers and
            // he starts the rope again — nothing in the world moved,
            // so there is nothing to undo.
            RetroAudio.Play(RetroSfxId.Bad);
            lower = new CemeteryCoffinLowerModel(
                CemeteryCoffinLowerSettings.Default,
                GetActSeed() + 977);
        }

        // ---- the stone ---------------------------------------

        private void UpdateStoneAct(float deltaTime)
        {
            if (settle == null || borrowedStone == null)
            {
                return;
            }

            if (settle.Phase == CemeteryStonePhase.Raising)
            {
                UpdateStoneRaising(deltaTime);
                return;
            }

            UpdateStoneSetting(deltaTime);
        }

        /// <summary>
        /// Heaving it up. No bar and no window — dead weight answers to
        /// nothing but repeated effort, so every press is worth the
        /// same and letting go only lets it settle back.
        /// </summary>
        private void UpdateStoneRaising(float deltaTime)
        {
            if (WasWorkPressed())
            {
                settle.Press();
                RetroAudio.PlayAt(
                    RetroSfxId.StoneTamp,
                    GetStoneSeat());
            }

            settle.Advance(deltaTime);
            ApplyStonePose();
        }

        /// <summary>
        /// Driving it home: three blows from above, each one timed on
        /// the same swing the spade digs with.
        /// </summary>
        private void UpdateStoneSetting(float deltaTime)
        {
            ApplyStonePose();
            EnsurePlaque();
            if (spadeAnimator != null && spadeAnimator.IsStriking)
            {
                return;
            }

            if (!stroke.IsSwinging)
            {
                spadeAnimator?.SetSwing(-1f);
                if (WasWorkPressed())
                {
                    stroke.Begin(
                        settle.Settings.TampProfile,
                        GetStrokeSeed());
                }

                return;
            }

            // The board is fitted the moment the stone is upright and
            // not before: `Attach` measures the monument to find its
            // face, and a stone still on its back measures to a
            // different shape entirely — which left the plaque hanging
            // in the air once it stood up.
            EnsurePlaque();
            stroke.Advance(deltaTime);
            spadeAnimator?.SetSwing(stroke.Position);
            if (IsWorkHeld())
            {
                return;
            }

            CemeteryStrokeOutcome outcome = stroke.Release();
            strikeCount++;
            settle.Strike(outcome);
            Vector3 head = GetStoneHead();
            spadeAnimator?.SetTargets(head, head);
            spadeAnimator?.PlayTamp(head);
            RetroAudio.PlayAt(
                outcome == CemeteryStrokeOutcome.Bite
                    ? RetroSfxId.StoneTamp
                    : RetroSfxId.SpadeGlance,
                head);
            ApplyStonePose();
            if (settle.IsComplete)
            {
                BeginInscribing();
            }
        }

        /// <summary>
        /// Puts the stone where the two halves say: tipped up out of
        /// the grass while it is being raised, then sinking a third of
        /// its lift into the ground with every blow that lands.
        /// </summary>
        private void ApplyStonePose()
        {
            if (borrowedStone == null)
            {
                return;
            }

            CityCemeterySealedGraveWorldBuilder.ApplyLyingPose(
                borrowedStone,
                job.Plan,
                1f - settle.Lift01);
            if (settle.Phase == CemeteryStonePhase.Raising)
            {
                return;
            }

            borrowedStone.position += new Vector3(
                0f,
                Mathf.Lerp(StoneLiftMeters, 0f, settle.Set01),
                0f);
        }

        /// <summary>
        /// Fits the board once the stone is standing. Measuring it
        /// against a stone still on its back is what put the plaque in
        /// mid-air, so this is deliberately not done at act start.
        /// </summary>
        private void EnsurePlaque()
        {
            if (sessionPlaque != null ||
                borrowedStone == null ||
                settle == null ||
                settle.Phase == CemeteryStonePhase.Raising)
            {
                return;
            }

            sessionPlaque = CityCemeteryPlaqueWorldBuilder.Attach(
                borrowedStone,
                job.Plan);
        }

        /// <summary>Roughly where a blow would land on the top of the
        /// stone.</summary>
        private Vector3 GetStoneHead()
        {
            Vector3 seat = GetStoneSeat();
            return new Vector3(
                seat.x,
                seat.y + StoneHeadHeightMeters,
                seat.z);
        }

        private Vector3 GetStoneSeat()
        {
            return new Vector3(
                job.Plan.Ground.x,
                job.Plan.GroundTopY,
                job.Plan.Ground.z);
        }

        // ---- opening and closing an act ----------------------

        private void BeginAct()
        {
            target = -1;
            strikeCount = 0;
            ropeCue = 0f;
            tampCue = 0f;
            stroke.Cancel();
            switch (act)
            {
                case CemeteryGraveWorkStage.Marked:
                    BeginSpadeAct(
                        CemeteryGraveLatticeMode.Digging);
                    break;
                case CemeteryGraveWorkStage.Dug:
                    BeginCoffinAct();
                    break;
                case CemeteryGraveWorkStage.Coffined:
                    job.SetPitDressingVisible(false);
                    BeginSpadeAct(
                        CemeteryGraveLatticeMode.Filling);
                    break;
                case CemeteryGraveWorkStage.Filled:
                    BeginStoneAct();
                    break;
            }
        }

        private void BeginSpadeAct(CemeteryGraveLatticeMode mode)
        {
            // Digging opens the ground before anything is committed.
            // The register takes the same rectangle twice without
            // complaint, so the act that records the hole later finds
            // its own work already done and simply lines it.
            if (mode == CemeteryGraveLatticeMode.Digging)
            {
                job.Excavation?.Excavate(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        job.Plan));
            }

            lattice = new CemeteryGraveLatticeModel(mode);
            RebuildWorkingPit();
            target = lattice.FindWorkable(0, 1);
            segmentFrame =
                CityCemeterySegmentFrameWorldBuilder.Build(
                    transform,
                    job.Plan);
            TakeUpSpade();
        }

        /// <summary>
        /// Picks up the spade that has been standing beside the plot.
        /// It is the job's object, not the session's — there is one
        /// spade on this worksite and this is it coming to hand.
        /// </summary>
        private void TakeUpSpade()
        {
            spadeAnimator = job.Spade;
            if (spadeAnimator == null)
            {
                return;
            }

            spadeAnimator.enabled = true;
            spadeAnimator.SetWorkDirection(
                CemeteryGraveWorkStance.GetWorkDirection(job.Plan));
            Vector3 face = lattice != null
                ? GetTargetFace()
                : GetStoneHead();
            spadeAnimator.SetTargets(
                face,
                lattice != null
                    ? CemeteryGraveWorkStance.GetHeapTop(
                        job.Plan,
                        CityCemeteryProgressivePitWorldBuilder
                            .GetHeapFullness(lattice))
                    : face);
            spadeAnimator.PlayTakeUp();
        }

        /// <summary>
        /// The box that has been waiting on its blocks is carried over
        /// and set on the bearers. Same object, for the same reason as
        /// the spade: two coffins on one plot would be one too many.
        /// </summary>
        private void BeginCoffinAct()
        {
            slings = CemeteryGraveSlings.Create(
                transform,
                job.Plan);
            borrowedCoffin = job.WaitingCoffin;
            headOnRight = IsHeadOnTheRight();
            lower = new CemeteryCoffinLowerModel(
                CemeteryCoffinLowerSettings.Default,
                GetActSeed());
            if (slings != null && borrowedCoffin != null)
            {
                slings.SetCoffinDrop(0f, 0f);
                borrowedCoffin.SetPositionAndRotation(
                    slings.GetCoffinPosition(0f, 0f),
                    slings.GetCoffinRotation(0f, 0f));
            }
        }

        /// <summary>
        /// Which side of the shot the head of the grave is on.
        ///
        /// The two ropes are the head and the foot of a box, but the
        /// player sees a left end and a right end, and which is which
        /// depends on the plot's own heading against the side the
        /// digger stands on. Binding the keys to head and foot means
        /// they are the right way round on half the graves in the yard
        /// and backwards on the other half, which is what shipped.
        /// </summary>
        private bool IsHeadOnTheRight()
        {
            Vector3 work =
                CemeteryGraveWorkStance.GetWorkDirection(job.Plan);
            Vector3 toCameraRight = Vector3.Cross(Vector3.up, work);
            Vector3 towardHead = job.Plan.Heading * Vector3.forward;
            return Vector3.Dot(toCameraRight, towardHead) >= 0f;
        }

        /// <summary>
        /// Which way the gauge runs, so the needle moves the same way
        /// on screen as the end of the box it stands for.
        /// </summary>
        public float CoffinGaugeSign => headOnRight ? 1f : -1f;

        /// <summary>
        /// Borrows the monument that has been lying by the head since
        /// the job was taken. It is the same object: raising a second
        /// stone beside the first is exactly the doubling the coffin
        /// and the spade were already fixed for.
        /// </summary>
        private void BeginStoneAct()
        {
            borrowedStone = job.LyingStone;
            settle = new CemeteryStoneSettleModel(
                CemeteryStoneSettleSettings.Default);
            ApplyStonePose();
            TakeUpSpade();
        }

        /// <summary>
        /// The stone is fast; now the board on it. The act is not
        /// committed until the hero has answered, because the whole
        /// point of the plaque is that the line goes on with the stone
        /// and not after it.
        /// </summary>
        private void BeginInscribing()
        {
            inscribing = true;
            epitaphDraft = string.Empty;
            stroke.Cancel();
            // The letters go on the brass as they are typed. There is
            // no field and no panel to type into: the board is in
            // front of him at arm's length and it is the board that
            // fills up, which is the only presentation of this that is
            // not a form.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                keyboard.onTextInput += OnEpitaphTyped;
            }

            ShowDraftOnPlaque();
            // Round to the front rather than cut to it.
            BeginCameraBlend(CemeteryGraveWorkPhase.Entering);
        }

        /// <summary>
        /// One character as the platform reports it, so layouts and
        /// dead keys behave the way they do everywhere else. Anything
        /// past the word count is simply not accepted — the board
        /// stops filling rather than quietly trimming later.
        /// </summary>
        private void OnEpitaphTyped(char typed)
        {
            if (!inscribing || char.IsControl(typed))
            {
                return;
            }

            string next = epitaphDraft + typed;
            if (!CemeteryEpitaph.IsWithinLimits(next))
            {
                return;
            }

            epitaphDraft = next;
            ShowDraftOnPlaque();
        }

        /// <summary>Puts the half-written line on the brass.</summary>
        private void ShowDraftOnPlaque()
        {
            sessionPlaque
                ?.GetComponent<CemeteryPlaqueSurface>()
                ?.ShowDraft(epitaphDraft);
        }

        private void StopListeningForText()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                keyboard.onTextInput -= OnEpitaphTyped;
            }
        }

        /// <summary>
        /// Reading the keys while the field has them. Only the two
        /// that end it are looked at — everything else is the player's
        /// text and belongs to the field, not to the game.
        /// </summary>
        private void UpdateInscribing()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.backspaceKey.wasPressedThisFrame &&
                epitaphDraft.Length > 0)
            {
                epitaphDraft = epitaphDraft.Substring(
                    0,
                    epitaphDraft.Length - 1);
                ShowDraftOnPlaque();
                return;
            }

            bool cut = keyboard.enterKey.wasPressedThisFrame ||
                       keyboard.numpadEnterKey.wasPressedThisFrame;
            bool leaveBlank =
                keyboard.escapeKey.wasPressedThisFrame;
            if (!cut && !leaveBlank)
            {
                return;
            }

            StopListeningForText();

            if (cut &&
                GameSessionState.TrySetGraveEpitaph(
                    job.PlotId,
                    epitaphDraft) &&
                sessionPlaque != null)
            {
                sessionPlaque
                    .GetComponent<CemeteryPlaqueSurface>()
                    ?.Refresh();
            }

            inscribing = false;
            epitaphDraft = string.Empty;
            Complete();
        }

        private void Complete()
        {
            CemeteryGraveWorkStage finished = act;
            TearDownAct();
            bool committed = job.TryAdvance();
            pendingFeedbackKey = committed
                ? CemeteryGraveDigSiteInteraction.GetFeedbackKey(
                    finished)
                : null;
            GameLog.Info(
                "city",
                "cemetery_grave_work_finished",
                GameLog.Field("act", finished.ToString()),
                GameLog.Field("committed", committed));
            BeginLeaving();
        }

        private void Abandon()
        {
            CemeteryGraveWorkStage dropped = act;
            TearDownAct();
            RestoreAbandonedWorld(dropped);
            pendingFeedbackKey = AbandonedFeedbackKey;
            GameLog.Info(
                "city",
                "cemetery_grave_work_abandoned",
                GameLog.Field("act", dropped.ToString()));
            BeginLeaving();
        }

        /// <summary>
        /// Puts back exactly what the act borrowed. Digging is the
        /// only one that touched the ground itself, and filling the
        /// rectangle back in is what makes an abandoned hole no hole
        /// at all.
        /// </summary>
        private void RestoreAbandonedWorld(
            CemeteryGraveWorkStage dropped)
        {
            switch (dropped)
            {
                case CemeteryGraveWorkStage.Marked:
                    job.Excavation?.Fill(
                        CityCemeteryPitWorldBuilder
                            .GetExcavationRect(job.Plan));
                    break;
                case CemeteryGraveWorkStage.Dug:
                    // The box goes back on its blocks. Only a finished
                    // act leaves it in the hole.
                    job.RestWaitingCoffin();
                    break;
                case CemeteryGraveWorkStage.Coffined:
                    job.SetPitDressingVisible(true);
                    break;
                case CemeteryGraveWorkStage.Filled:
                    // The stone goes back on its side by the head.
                    job.RestLyingStone();
                    break;
            }
        }

        /// <summary>
        /// Gives back everything the act borrowed and destroys
        /// everything it made. The spade and the coffin belong to the
        /// job and are only released here — the spade with a visible
        /// putting-down, which finishes under the camera blend.
        /// </summary>
        private void TearDownAct()
        {
            stroke.Cancel();
            if (inscribing)
            {
                StopListeningForText();
            }

            inscribing = false;
            epitaphDraft = string.Empty;
            lattice = null;
            lower = null;
            settle = null;
            target = -1;
            DestroyPart(ref workingPit);
            DestroyPart(ref segmentFrame);
            DestroyPart(ref sessionPlaque);
            borrowedStone = null;
            if (spadeAnimator != null)
            {
                spadeAnimator.PlayLayDown(
                    CityGravediggerShovelWorldBuilder
                        .GetRestPosition(job.Plan),
                    CityGravediggerShovelWorldBuilder
                        .GetRestRotation(job.Plan));
                spadeAnimator = null;
            }

            borrowedCoffin = null;
            if (slings != null)
            {
                slings.Dismantle();
                slings = null;
            }
        }

        /// <summary>
        /// Where the board actually is: the session's own while an act
        /// is running, the finished grave's once one is standing, and
        /// a guess from the plan only when neither exists yet.
        /// </summary>
        private Vector3 GetBoardPosition()
        {
            if (sessionPlaque != null)
            {
                return sessionPlaque.transform.position;
            }

            Transform board = job.Plaque;
            return board != null
                ? board.position
                : CityCemeteryPlaqueWorldBuilder.GetNominalSeat(
                    job.Plan);
        }

        private void BeginLeaving()
        {
            job.Site?.SetMarksVisible(true);
            job.SetPitDressingVisible(true);
            phase = CemeteryGraveWorkPhase.Leaving;
            BeginCameraBlend(CemeteryGraveWorkPhase.Leaving);
            if (!cameraOwned)
            {
                FinishLeaving();
            }
        }

        private void FinishLeaving()
        {
            phase = CemeteryGraveWorkPhase.None;
            heroHidden?.Dispose();
            heroHidden = null;
            modalLock.Restore();
            if (!string.IsNullOrEmpty(pendingFeedbackKey))
            {
                interactor?.ShowFeedback(
                    pendingFeedbackKey,
                    FeedbackSeconds);
                pendingFeedbackKey = null;
            }
        }

        private void RebuildWorkingPit()
        {
            DestroyPart(ref workingPit);
            workingPit =
                CityCemeteryProgressivePitWorldBuilder.Build(
                    transform,
                    job.Plan,
                    lattice);
        }

        private int GetActSeed()
        {
            unchecked
            {
                return (int)(
                    PlotHash() ^ ((uint)(int)act * 0x27D4EB2Fu));
            }
        }

        /// <summary>
        /// FNV-1a over the plot id rather than
        /// <see cref="string.GetHashCode"/>. The sway on the ropes and
        /// the lean of the stone are seeded from this, and a runtime
        /// string hash is free to differ between processes — one seed
        /// has to mean one grave everywhere.
        /// </summary>
        private uint PlotHash()
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = job.Plan.Plot.StableId ?? string.Empty;
                for (int index = 0; index < text.Length; index++)
                {
                    hash ^= text[index];
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        // ---- camera ------------------------------------------

        private void BeginCameraBlend(CemeteryGraveWorkPhase target)
        {
            if (cameraFollow == null || workCamera == null)
            {
                return;
            }

            CaptureCameraOwnership();
            cameraBlendPosition = workCamera.transform.position;
            cameraBlendRotation = workCamera.transform.rotation;
            cameraBlendFieldOfView = workCamera.fieldOfView;
            cameraBlendElapsed = 0f;
            cameraPhase = target;
        }

        private void CaptureCameraOwnership()
        {
            if (cameraOwned)
            {
                return;
            }

            previousCinematicMotion =
                cameraFollow.CinematicMotionEnabled;
            previousFixedPose = cameraFollow.FixedPoseActive;
            previousFixedCameraPose = cameraFollow.FixedBasePose;
            previousFixedFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
            cameraFollow.SetCinematicMotionEnabled(false);
            cameraOwned = true;
        }

        private void UpdateOwnedCamera(float deltaTime)
        {
            if (!cameraOwned || cameraFollow == null)
            {
                return;
            }

            if (cameraPhase == CemeteryGraveWorkPhase.Leaving)
            {
                cameraBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = Mathf.Clamp01(
                    cameraBlendElapsed / CameraBlendSeconds);
                float smooth = Mathf.SmoothStep(0f, 1f, amount);
                Pose followPose = cameraFollow.ResolveFollowPose(
                    playerRoot != null
                        ? playerRoot.position
                        : cameraBlendPosition);
                cameraFollow.SetFixedPose(
                    Vector3.Lerp(
                        cameraBlendPosition,
                        followPose.position,
                        smooth),
                    Quaternion.Slerp(
                        cameraBlendRotation,
                        followPose.rotation,
                        smooth),
                    Mathf.Lerp(
                        cameraBlendFieldOfView,
                        cameraFollow.FollowFieldOfView,
                        smooth));
                if (amount >= 1f)
                {
                    RestoreCameraOwnership();
                    FinishLeaving();
                }

                return;
            }

            ResolveWorkShot(
                out Vector3 workPosition,
                out Quaternion workRotation,
                out float workFieldOfView);
            if (cameraPhase == CemeteryGraveWorkPhase.Entering)
            {
                cameraBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = Mathf.Clamp01(
                    cameraBlendElapsed / CameraBlendSeconds);
                float smooth = Mathf.SmoothStep(0f, 1f, amount);
                cameraFollow.SetFixedPose(
                    Vector3.Lerp(
                        cameraBlendPosition,
                        workPosition,
                        smooth),
                    Quaternion.Slerp(
                        cameraBlendRotation,
                        workRotation,
                        smooth),
                    Mathf.Lerp(
                        cameraBlendFieldOfView,
                        workFieldOfView,
                        smooth));
                if (amount >= 1f)
                {
                    cameraPhase = CemeteryGraveWorkPhase.Working;
                }

                return;
            }

            cameraFollow.SetFixedPose(
                workPosition,
                workRotation,
                workFieldOfView);
        }

        /// <summary>
        /// Where the shot stands for whatever is being done.
        ///
        /// Three cases rather than one. Most of the work is watched
        /// from the digger's own eye line over the hole; standing the
        /// stone up leans that shot toward the side the stone is lying
        /// on, because the act happens out there and not in the middle;
        /// and cutting the plaque walks round to the front of the
        /// monument, which is the only way the board is anything but
        /// an edge.
        /// </summary>
        private void ResolveWorkShot(
            out Vector3 position,
            out Quaternion rotation,
            out float fieldOfView)
        {
            if (inscribing || reading)
            {
                CemeteryGraveWorkStance.EvaluatePlaqueCamera(
                    job.Plan,
                    GetBoardPosition(),
                    out position,
                    out rotation);
                fieldOfView =
                    CemeteryGraveWorkStance.PlaqueFieldOfView;
                return;
            }

            float shift = act == CemeteryGraveWorkStage.Filled
                ? CemeteryGraveWorkStance.GetStoneLateralShift(
                    job.Plan)
                : 0f;
            CemeteryGraveWorkStance.EvaluateCamera(
                job.Plan,
                GetCameraInterest(),
                shift,
                out position,
                out rotation);
            fieldOfView = CemeteryGraveWorkStance.FieldOfView;
        }

        /// <summary>What the shot leans toward: whatever the hero is
        /// working on this instant.</summary>
        private Vector3 GetCameraInterest()
        {
            if (lattice != null && target >= 0)
            {
                return GetTargetFace();
            }

            if (borrowedCoffin != null)
            {
                return borrowedCoffin.position;
            }

            if (borrowedStone != null)
            {
                return GetStoneHead();
            }

            return CemeteryGraveWorkStance.GetRestingFocus(job.Plan);
        }

        private void RestoreCameraOwnership()
        {
            if (!cameraOwned || cameraFollow == null)
            {
                return;
            }

            if (previousFixedPose)
            {
                cameraFollow.SetFixedPose(
                    previousFixedCameraPose.position,
                    previousFixedCameraPose.rotation,
                    previousFixedFieldOfView);
            }
            else
            {
                cameraFollow.ClearFixedPose();
            }

            cameraFollow.SetCinematicMotionEnabled(
                previousCinematicMotion);
            cameraOwned = false;
            cameraPhase = CemeteryGraveWorkPhase.None;
        }

        // ---- input -------------------------------------------

        private static bool IsWorkHeld()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.isPressed ||
                 keyboard.enterKey.isPressed ||
                 keyboard.spaceKey.isPressed))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonSouth.isPressed;
        }

        private static bool WasWorkPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        /// <summary>The left-hand rope of the shot.</summary>
        private static bool IsLeftRopeHeld()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.qKey.isPressed)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonWest.isPressed;
        }

        private static bool WasLeavePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static int ReadStep()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool left = keyboard.aKey.wasPressedThisFrame ||
                            keyboard.leftArrowKey.wasPressedThisFrame;
                bool right = keyboard.dKey.wasPressedThisFrame ||
                             keyboard.rightArrowKey
                                 .wasPressedThisFrame;
                if (left != right)
                {
                    return left ? -1 : 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return 0;
            }

            bool padLeft = gamepad.dpad.left.wasPressedThisFrame;
            bool padRight = gamepad.dpad.right.wasPressedThisFrame;
            return padLeft == padRight ? 0 : (padLeft ? -1 : 1);
        }

        private static float ReadLean()
        {
            float input = 0f;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool left = keyboard.aKey.isPressed ||
                            keyboard.leftArrowKey.isPressed;
                bool right = keyboard.dKey.isPressed ||
                             keyboard.rightArrowKey.isPressed;
                input = (right ? 1f : 0f) - (left ? 1f : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return input;
            }

            float stick = gamepad.leftStick.x.ReadValue();
            return Mathf.Abs(stick) > Mathf.Abs(input)
                ? Mathf.Clamp(stick, -1f, 1f)
                : input;
        }

        private static int WrapSegment(int segment)
        {
            int count = CemeteryGraveLatticeModel.SegmentCount;
            return ((segment % count) + count) % count;
        }

        private static void DestroyPart(ref GameObject part)
        {
            if (part == null)
            {
                return;
            }

            GameObject doomed = part;
            part = null;
            if (Application.isPlaying)
            {
                Destroy(doomed);
            }
            else
            {
                DestroyImmediate(doomed);
            }
        }
    }
}
