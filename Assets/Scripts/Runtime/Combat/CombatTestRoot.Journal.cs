using System;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        internal static CombatTestRoot JournalRoot { get; private set; }
        internal DuelJournal JournalForDiagnostics => duelJournal;
        internal double JournalCollectorMilliseconds { get; private set; }
        internal string JournalDirectory { get; private set; }
        private DuelJournal duelJournal;
        private CombatJournalFrameObserver journalObserver;
        private JournalActorState journalHero, journalOpponent;
        private long journalTick, journalFrameStart, journalSimulationTicks, journalPoseTicks, journalContactTicks;
        private long journalLastCollector, journalSnapshotTicks;
        private long journalQueriesAtFrameStart, journalSaturationsAtFrameStart;
        private int journalMeasuredFrame;
        private int journalRound, journalFrameSubsteps, journalFrozenSteps, journalPoseSamples, journalDecision = -1;
        private double journalSeconds, journalLastSnapshot = -1d, journalFrameMilliseconds;
        private bool journalRoundOpen, journalResultWritten, journalInputAllowed, journalAttackHeld, journalFrozen;
        private bool journalForceSnapshot, journalFocus = true;
        private bool journalFailureReported;
        private CombatOpponentIntent journalIntent;
        private Vector3 journalOpponentRequested, journalOpponentAchieved;
        private ProfilerRecorder journalMain, journalRender, journalGc;
        private readonly FrameTiming[] journalGpu = new FrameTiming[1];
        private bool journalGpuEnabled;
        private static readonly string[] JournalPhases = Enum.GetNames(typeof(MeleePhase));
        private static readonly string[] JournalGrips = Enum.GetNames(typeof(CombatArmSupportState));
        private static readonly string[] JournalIntents = Enum.GetNames(typeof(CombatOpponentIntent));
        private static readonly double JournalMillisecondsPerTick = 1000d / Stopwatch.Frequency;

        private sealed class JournalActorState
        {
            internal CombatActor Actor;
            internal Transform[] Bones;
            internal MeleePhase Phase;
            internal CombatArmSupportState Grip;
            internal bool Recovering, Warned;
            internal double PhaseStarted, RecoveryStarted, GripStarted;
            internal int Action;
            internal static readonly string[] Names = { "pelvis", "chest", "hand.L", "hand.R", "foot.L", "foot.R" };

            internal JournalActorState(CombatActor actor)
            {
                Actor = actor; Bones = new Transform[Names.Length];
                foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                    for (int i = 0; i < Names.Length; i++) if (bone.name == Names[i]) Bones[i] = bone;
                Phase = actor.State.Phase; Grip = actor.SupportArmState; Action = actor.State.AttackSequence;
            }
        }

        private void InitializeDuelJournal()
        {
            bool enabled = !Application.isBatchMode;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-bp-duel-log" && i + 1 < args.Length) enabled = args[++i] == "on";
                else if (args[i].StartsWith("-bp-duel-log=", StringComparison.Ordinal)) enabled = args[i].EndsWith("=on", StringComparison.Ordinal);
            }
            if (enabled) SetDuelLogging(true);
        }

        internal void SetDuelLogging(bool enabled, string outputDirectory = null)
        {
            CloseDuelJournal("logging_changed");
            if (!enabled) return;
            JournalDirectory = outputDirectory ?? Path.Combine(Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..")) : Application.persistentDataPath, "CombatLogs");
            duelJournal = new DuelJournal(JournalDirectory);
            journalFailureReported = false;
            JournalRoot = this;
            journalHero = new JournalActorState(Hero); journalOpponent = new JournalActorState(Opponent);
            if (journalObserver == null) journalObserver = gameObject.AddComponent<CombatJournalFrameObserver>();
            journalObserver.Root = this;
            journalMain = StartJournalRecorder(ProfilerCategory.Internal, "Main Thread");
            journalRender = StartJournalRecorder(ProfilerCategory.Internal, "Render Thread");
            journalGc = StartJournalRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            journalGpuEnabled = FrameTimingManager.IsFeatureEnabled();
            if (journalGpuEnabled) FrameTimingManager.CaptureFrameTimings();
            // Initial Awake starts the round after placement; explicit runtime/test
            // enable captures the current state without resetting or moving anyone.
            if (OpponentRound >= 0 && roundsPlaced > 0) BeginJournalRound();
        }

        private void BeginJournalRound()
        {
            if (duelJournal == null || journalRoundOpen) return;
            journalRoundOpen = true; journalResultWritten = false;
            journalTick = 0; journalSeconds = 0d; journalLastSnapshot = -1d; journalFrameStart = 0;
            journalDecision = -1; journalAttackHeld = journalFrozen = false;
            journalInputAllowed = GameInput.CanRead(GameInputContext.Gameplay);
            journalHero.Phase = Hero.State.Phase; journalOpponent.Phase = Opponent.State.Phase;
            journalHero.Action = Hero.State.AttackSequence; journalOpponent.Action = Opponent.State.AttackSequence;
            journalHero.Grip = Hero.SupportArmState; journalOpponent.Grip = Opponent.SupportArmState;
            journalHero.PhaseStarted = journalOpponent.PhaseStarted = 0;
            journalHero.GripStarted = journalOpponent.GripStarted = 0;
            journalHero.Recovering = journalOpponent.Recovering = false;
            duelJournal.SetClock(Time.frameCount, 0, 0);
            duelJournal.BeginRound(++journalRound,
                GameLog.Field("scene", SceneIds.CombatTest), GameLog.Field("unity", Application.unityVersion),
                GameLog.Field("version", Application.version), GameLog.Field("build_guid", Application.buildGUID),
                GameLog.Field("editor", Application.isEditor), GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field("ai_seed", (long)decisionSeed), GameLog.Field("opponent_round", OpponentRound),
                GameLog.Field("sparring", Sparring), GameLog.Field("source", AutomaticSimulation ? "manual" : "test"),
                GameLog.Field("width", Screen.width), GameLog.Field("height", Screen.height),
                GameLog.Field("render_scale", GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset p ? p.renderScale : 1f),
                GameLog.Field("quality", QualitySettings.GetQualityLevel()), GameLog.Field("vsync", QualitySettings.vSyncCount),
                GameLog.Field("target_fps", Application.targetFrameRate), GameLog.Field("cpu", SystemInfo.processorType),
                GameLog.Field("gpu", SystemInfo.graphicsDeviceName), GameLog.Field("simulation_step", SimulationStep),
                GameLog.Field("max_substeps", MaximumFrameSubsteps), GameLog.Field("snapshot_hz", 20),
                GameLog.Field("gpu_timing_available", journalGpuEnabled),
                GameLog.Field("code_revision", "unavailable"));
            Hero.JournalActorId = 1; Opponent.JournalActorId = 2;
            Hero.Journal = Opponent.Journal = duelJournal;
            Hero.ImpactReceived += RequestJournalSnapshot; Opponent.ImpactReceived += RequestJournalSnapshot;
            WriteJournalActorConfiguration(Hero); WriteJournalActorConfiguration(Opponent);
            journalForceSnapshot = true;
            GameLog.Info("combat", "duel_journal_started", GameLog.Field("duel_session", duelJournal.SessionId),
                GameLog.Field("round", journalRound));
        }

        private void WriteJournalActorConfiguration(CombatActor actor)
        {
            var settings = actor.State.Settings;
            // Settings are immutable; reflect once at the boundary, never in combat.
            foreach (var property in typeof(MeleeCombatSettings).GetProperties())
                if (property.PropertyType == typeof(float) && property.GetIndexParameters().Length == 0)
                    duelJournal.Record("setting", actor: actor.JournalActorId,
                        f0: GameLog.Field("name", property.Name), f1: GameLog.Field("value", (float)property.GetValue(settings)));
            duelJournal.Record("actor_configuration", actor: actor.JournalActorId,
                f0: GameLog.Field("model", actor.DamageRigRoot.name), f1: GameLog.Field("weapon", actor.Weapon.name),
                f2: GameLog.Field("experimental_recovery", actor.ImpactMotion.ExperimentalRecovery),
                f3: GameLog.Field("max_recovery_steps", actor.ImpactMotion.MaximumRecoverySteps),
                f4: GameLog.Field("shove_range", CombatActor.ShoveRange), f5: GameLog.Field("shove_impulse", CombatActor.ShoveImpulse),
                f6: GameLog.Field("animation_asset_revision", "unavailable"), f7: GameLog.Field("intoxication", actor.IsHero ? GameSessionState.IntoxicationLevel : 0));
            duelJournal.Record("character", actor: actor.JournalActorId,
                f0: GameLog.Field("id", actor.IsHero ? "hero" : DefaultNpcPopulation.CombatTestOpponent));
        }

        private void EndJournalRound(string reason)
        {
            if (duelJournal == null || !journalRoundOpen) return;
            duelJournal.Record("round_status", f0: GameLog.Field("finished", RoundFinished),
                f1: GameLog.Field("hero_health", Hero.State.Health), f2: GameLog.Field("opponent_health", Opponent.State.Health));
            WriteJournalOpenIntervals(journalHero); WriteJournalOpenIntervals(journalOpponent);
            Hero.ImpactReceived -= RequestJournalSnapshot; Opponent.ImpactReceived -= RequestJournalSnapshot;
            Hero.Journal = Opponent.Journal = null;
            duelJournal.EndRound(reason); journalRoundOpen = false;
            GameLog.Info("combat", "duel_journal_ended", GameLog.Field("duel_session", duelJournal.SessionId),
                GameLog.Field("round", journalRound), GameLog.Field("reason", reason));
        }

        private void CloseDuelJournal(string reason)
        {
            if (duelJournal == null) return;
            EndJournalRound(reason);
            duelJournal.Dispose(); duelJournal = null;
            journalMain.Dispose();
            journalRender.Dispose();
            journalGc.Dispose();
            if (JournalRoot == this) JournalRoot = null;
        }

        private void WriteJournalOpenIntervals(JournalActorState saved)
        {
            duelJournal.Record("open_phase_interval", actor: saved.Actor.JournalActorId,
                f0: GameLog.Field("label", JournalPhases[(int)saved.Phase]),
                f1: GameLog.Field("duration_seconds", journalSeconds - saved.PhaseStarted));
            duelJournal.Record("open_grip_interval", actor: saved.Actor.JournalActorId,
                f0: GameLog.Field("label", JournalGrips[(int)saved.Grip]),
                f1: GameLog.Field("duration_seconds", journalSeconds - saved.GripStarted));
        }

        internal bool MarkDuelProblem(string reason)
        {
            if (duelJournal == null || !journalRoundOpen) return false;
            duelJournal.Mark(reason); journalForceSnapshot = true;
            return true;
        }

        private void RequestJournalSnapshot(CombatImpact _) => journalForceSnapshot = true;
        private long JournalStamp() => duelJournal != null ? Stopwatch.GetTimestamp() : 0;
        private static void JournalElapsed(long start, ref long accumulator)
        { if (start != 0) accumulator += Stopwatch.GetTimestamp() - start; }

        private void BeginJournalFrame()
        {
            if (duelJournal == null || !journalRoundOpen) return;
            if (!duelJournal.Enabled)
            {
                if (!journalFailureReported)
                {
                    journalFailureReported = true;
                    GameLog.Warning("combat", "duel_journal_failed", GameLog.Field("reason", duelJournal.LastError));
                }
                return;
            }
            long now = Stopwatch.GetTimestamp();
            journalFrameMilliseconds = journalFrameStart == 0 ? 0 : (now - journalFrameStart) * JournalMillisecondsPerTick;
            if (journalFrameStart != 0)
            {
                // The interval ending now belongs to the PREVIOUS frame's work.
                // Never attribute its hitch to the actions about to run this frame.
                duelJournal.SetClock(journalMeasuredFrame, journalTick, journalSeconds);
                double gpu = double.NaN;
                if (journalGpuEnabled && FrameTimingManager.GetLatestTimings(1, journalGpu) > 0 && journalGpu[0].gpuFrameTime > 0)
                    gpu = journalGpu[0].gpuFrameTime;
                duelJournal.Record("frame", f0: GameLog.Field("frame_ms", journalFrameMilliseconds),
                    f1: GameLog.Field("simulation_ms", journalSimulationTicks * JournalMillisecondsPerTick),
                    f2: GameLog.Field("pose_ms", journalPoseTicks * JournalMillisecondsPerTick),
                    f3: GameLog.Field("contact_ms", journalContactTicks * JournalMillisecondsPerTick),
                    f4: GameLog.Field("main_thread_ms", JournalRecorderValue(journalMain, .000001d)),
                    f5: GameLog.Field("render_thread_ms", JournalRecorderValue(journalRender, .000001d)),
                    f6: GameLog.Field("gc_bytes", JournalRecorderValue(journalGc, 1d)), f7: GameLog.Field("latest_gpu_ms", gpu));
            }
            if (journalGpuEnabled) FrameTimingManager.CaptureFrameTimings();
            journalFrameStart = now;
            journalMeasuredFrame = Time.frameCount;
            journalSimulationTicks = journalPoseTicks = journalContactTicks = journalSnapshotTicks = 0;
            journalFrameSubsteps = journalFrozenSteps = journalPoseSamples = 0;
            journalOpponentRequested = journalOpponentAchieved = Vector3.zero;
            journalQueriesAtFrameStart = Hero.JournalPhysicsQueries + Opponent.JournalPhysicsQueries;
            journalSaturationsAtFrameStart = Hero.JournalPhysicsBufferSaturations + Opponent.JournalPhysicsBufferSaturations;
            duelJournal.SetClock(Time.frameCount, journalTick, journalSeconds);
            bool allowed = GameInput.CanRead(GameInputContext.Gameplay);
            if (allowed != journalInputAllowed)
            {
                journalInputAllowed = allowed; journalForceSnapshot = true;
                duelJournal.Record("input_gate", f0: GameLog.Field("allowed", allowed),
                    f1: GameLog.Field("paused", PauseMenuController.IsAnyPaused), f2: GameLog.Field("transitioning", SceneTransitionService.IsTransitioning));
            }
        }

        private void AdvanceJournalClock(bool frozen)
        {
            if (duelJournal == null) return;
            journalTick++; journalFrameSubsteps++;
            if (frozen) journalFrozenSteps++; else journalSeconds += SimulationStep;
            duelJournal.SetClock(Time.frameCount, journalTick, journalSeconds);
            if (journalFrozen != frozen)
            {
                journalFrozen = frozen;
                duelJournal.Record("hit_stop", f0: GameLog.Field("active", frozen), f1: GameLog.Field("remaining_steps", hitStopSubsteps));
            }
        }

        private void JournalAttackInput(bool held)
        {
            if (duelJournal == null || held == journalAttackHeld) return;
            journalAttackHeld = held;
            duelJournal.Record("input", actor: 1, f0: GameLog.Field("command", "attack"), f1: GameLog.Field("held", held),
                f2: GameLog.Field("owned", attackInputOwned), f3: GameLog.Field("require_release", requireAttackRelease));
        }

        private void JournalOpponentDecision()
        {
            if (duelJournal == null || (journalDecision == OpponentDecisionSequence && journalIntent == OpponentIntent)) return;
            journalDecision = OpponentDecisionSequence; journalIntent = OpponentIntent;
            duelJournal.Record("ai_decision", actor: 2, target: 1,
                f0: GameLog.Field("decision", journalDecision), f1: GameLog.Field("intent", JournalIntents[(int)OpponentIntent]),
                f2: GameLog.Field("distance", Vector3.Distance(Hero.transform.position, Opponent.transform.position)),
                f3: GameLog.Field("observed_phase", JournalPhases[(int)Hero.State.Phase]), f4: GameLog.Field("reaction_seconds", observationSeconds),
                f5: GameLog.Field("making_space", opponentMakingSpace), f6: GameLog.Field("delay", opponentDelay),
                f7: GameLog.Field("draw_count", draws));
        }

        private void JournalTransitions(string stage)
        {
            if (duelJournal == null) return;
            JournalTransition(journalHero, stage); JournalTransition(journalOpponent, stage);
        }

        private void JournalTransition(JournalActorState saved, string stage)
        {
            CombatActor actor = saved.Actor;
            if (actor.State.Phase != saved.Phase || actor.State.AttackSequence != saved.Action)
            {
                duelJournal.Record("phase", actor: actor.JournalActorId, action: actor.State.AttackSequence,
                    f0: GameLog.Field("from", JournalPhases[(int)saved.Phase]), f1: GameLog.Field("to", JournalPhases[(int)actor.State.Phase]),
                    f2: GameLog.Field("duration_seconds", journalSeconds - saved.PhaseStarted), f3: GameLog.Field("stage", stage),
                    f4: GameLog.Field("outcome", (int)actor.State.AttackOutcome));
                saved.Phase = actor.State.Phase; saved.Action = actor.State.AttackSequence; saved.PhaseStarted = journalSeconds;
            }
            if (actor.SupportArmState != saved.Grip)
            {
                duelJournal.Record("grip", actor: actor.JournalActorId, action: actor.State.AttackSequence,
                    f0: GameLog.Field("from", JournalGrips[(int)saved.Grip]), f1: GameLog.Field("to", JournalGrips[(int)actor.SupportArmState]),
                    f2: GameLog.Field("stage", stage), f3: GameLog.Field("support_weight", actor.SupportGripWeight),
                    f4: GameLog.Field("duration_seconds", journalSeconds - saved.GripStarted));
                saved.Grip = actor.SupportArmState; saved.GripStarted = journalSeconds;
            }
            bool recovery = actor.ImpactMotion.RecoveryInProgress;
            if (recovery != saved.Recovering)
            {
                duelJournal.Record("recovery", actor: actor.JournalActorId, f0: GameLog.Field("active", recovery),
                    f1: GameLog.Field("duration_seconds", recovery ? 0 : journalSeconds - saved.RecoveryStarted),
                    f2: GameLog.Field("impact_event", actor.LastJournalImpactSequence));
                saved.Recovering = recovery; saved.RecoveryStarted = journalSeconds; saved.Warned = false;
            }
            if (recovery && !saved.Warned && journalSeconds - saved.RecoveryStarted > 2d)
            {
                saved.Warned = true;
                duelJournal.Record("suspected_stall", actor: actor.JournalActorId,
                    f0: GameLog.Field("reason", "RecoveryOverTwoSeconds"), f1: GameLog.Field("duration_seconds", journalSeconds - saved.RecoveryStarted));
                duelJournal.Mark("RecoveryOverTwoSeconds");
            }
        }

        private void JournalRoundResult()
        {
            if (duelJournal == null || journalResultWritten || !RoundFinished) return;
            journalResultWritten = true; journalForceSnapshot = true;
            duelJournal.Record("round_result", f0: GameLog.Field("hero_defeated", Hero.State.IsDefeated),
                f1: GameLog.Field("opponent_defeated", Opponent.State.IsDefeated));
        }

        internal void CaptureJournalFrame()
        {
            if (duelJournal == null || !duelJournal.Enabled || !journalRoundOpen || !IsInitialized) return;
            duelJournal.SetClock(Time.frameCount, journalTick, journalSeconds);
            long start = Stopwatch.GetTimestamp(), collector = duelJournal.CollectorTicks;
            JournalTransitions("final_presentation");
            // Finished-round ragdolls still move while the combat clock is stopped.
            double snapshotSeconds = journalSeconds + roundEndElapsed;
            if (journalForceSnapshot || snapshotSeconds - journalLastSnapshot >= .05d)
            {
                journalForceSnapshot = false; journalLastSnapshot = snapshotSeconds;
                WriteJournalSnapshot(journalHero); WriteJournalSnapshot(journalOpponent);
            }
            journalSnapshotTicks += Stopwatch.GetTimestamp() - start - (duelJournal.CollectorTicks - collector);
            long collected = duelJournal.CollectorTicks;
            JournalCollectorMilliseconds = (Math.Max(0, collected - journalLastCollector) + journalSnapshotTicks) * JournalMillisecondsPerTick;
            duelJournal.Record("frame_work", f0: GameLog.Field("substeps", journalFrameSubsteps),
                f1: GameLog.Field("frozen_substeps", journalFrozenSteps), f2: GameLog.Field("extra_pose_samples", journalPoseSamples),
                f3: GameLog.Field("collector_ms", JournalCollectorMilliseconds),
                f4: GameLog.Field("dropped_records", duelJournal.DroppedRecords), f5: GameLog.Field("maximum_queued", duelJournal.MaximumQueued),
                f6: GameLog.Field("physics_queries", Math.Max(0L, Hero.JournalPhysicsQueries + Opponent.JournalPhysicsQueries - journalQueriesAtFrameStart)),
                f7: GameLog.Field("query_buffer_saturations", Math.Max(0L, Hero.JournalPhysicsBufferSaturations + Opponent.JournalPhysicsBufferSaturations - journalSaturationsAtFrameStart)));
            journalLastCollector = collected;
        }

        private void WriteJournalSnapshot(JournalActorState saved)
        {
            CombatActor actor = saved.Actor;
            int id = actor.JournalActorId;
            var state = actor.State;
            duelJournal.Record("state", actor: id, action: state.AttackSequence, request: actor.JournalRequestId,
                f0: GameLog.Field("phase", JournalPhases[(int)state.Phase]), f1: GameLog.Field("health", state.Health),
                f2: GameLog.Field("stamina", state.Stamina), f3: GameLog.Field("charge", state.Charge01),
                f4: GameLog.Field("action_remaining", state.ActionRemaining), f5: GameLog.Field("buffered_action", (int)state.BufferedAction),
                f6: GameLog.Field("two_hand_support", actor.HasTwoHandSupport), f7: GameLog.Field("movement_scale", actor.MovementScale));
            duelJournal.Record("presentation", actor: id, action: state.AttackSequence,
                f0: GameLog.Field("clip", actor.JournalClip), f1: GameLog.Field("pose_clock", actor.JournalPoseClock),
                f2: GameLog.Field("attack_progress", state.AttackProgress), f3: GameLog.Field("grip", JournalGrips[(int)actor.SupportArmState]),
                f4: GameLog.Field("weapon_blocked", actor.WeaponClearanceBlocked), f5: GameLog.Field("blocking_shape", actor.WeaponBlockingShape),
                f6: GameLog.Field("penetration", actor.WeaponPenetrationDepth), f7: GameLog.Field("ragdoll", actor.IsRagdollActive));
            var impact = actor.ImpactMotion;
            duelJournal.Record("balance", actor: id,
                f0: GameLog.Field("load", impact.BalanceLoad), f1: GameLog.Field("speed", impact.Velocity.magnitude),
                f2: GameLog.Field("angular_speed", impact.AngularVelocity.magnitude), f3: GameLog.Field("catch_active", actor.Footwork.CatchStepActive),
                f4: GameLog.Field("catch_progress", actor.Footwork.CatchStepProgress), f5: GameLog.Field("landed", impact.LandedRecoverySteps),
                f6: GameLog.Field("knockdown_requested", impact.WantsKnockdown), f7: GameLog.Field("impact_event", actor.LastJournalImpactSequence));
            var grip = actor.SupportGrip;
            duelJournal.Record("grip_snapshot", actor: id,
                f0: GameLog.Field("contact_error", grip.JournalContactError), f1: GameLog.Field("contact_angle", grip.JournalContactAngle),
                f2: GameLog.Field("wrist_safe", grip.JournalWristSafe), f3: GameLog.Field("regrip_allowed", grip.JournalRegripAllowed),
                f4: GameLog.Field("release_hold", grip.JournalReleaseHold), f5: GameLog.Field("shoving", grip.IsShoving),
                f6: GameLog.Field("balance_reaching", grip.IsBalanceReaching), f7: GameLog.Field("catch_step", actor.Footwork.CatchStepActive));
            duelJournal.Record("support_snapshot", actor: id,
                f0: GameLog.Field("left_confirmed", actor.Footwork.JournalLeftSupport),
                f1: GameLog.Field("right_confirmed", actor.Footwork.JournalRightSupport),
                f2: GameLog.Field("catch_side", actor.Footwork.LastCatchSide),
                f3: GameLog.Field("hand_supported", impact.HasHandSupport), f4: GameLog.Field("skill", impact.RecoverySkill),
                f5: GameLog.Field("recovery_sequence", impact.RecoverySequence));
            WriteJournalPose(id, "root", actor.transform);
            for (int i = 0; i < saved.Bones.Length; i++) WriteJournalPose(id, JournalActorState.Names[i], saved.Bones[i]);
            WriteJournalPose(id, "weapon", actor.Weapon != null ? actor.Weapon.transform : null);
            WriteJournalVectors(id, "mass_and_capture", impact.CentreOfMass, impact.CaptureOffset);
            WriteJournalVectors(id, "support_and_target", impact.SupportCentre, actor.Footwork.LastCatchTarget);
            WriteJournalVectors(id, "hand_and_grip_target", actor.ShovePalmPosition, actor.SupportGripWorldPosition);
            if (id == 2) WriteJournalVectors(id, "requested_and_achieved_move", journalOpponentRequested, journalOpponentAchieved);
            else WriteJournalVectors(id, "movement", Player.Motor.PlanarVelocity, actor.Body != null ? actor.Body.velocity : Vector3.zero);
        }

        private void WriteJournalPose(int actor, string bone, Transform value)
        {
            if (value == null) return;
            Vector3 p = value.position; Quaternion q = value.rotation;
            duelJournal.Record("pose", actor: actor, f0: GameLog.Field("bone", bone),
                f1: GameLog.Field("x", p.x), f2: GameLog.Field("y", p.y), f3: GameLog.Field("z", p.z),
                f4: GameLog.Field("qx", q.x), f5: GameLog.Field("qy", q.y), f6: GameLog.Field("qz", q.z), f7: GameLog.Field("qw", q.w));
        }

        private void WriteJournalVectors(int actor, string name, Vector3 a, Vector3 b) =>
            duelJournal.Record("vectors", actor: actor, f0: GameLog.Field("name", name),
                f1: GameLog.Field("ax", a.x), f2: GameLog.Field("ay", a.y), f3: GameLog.Field("az", a.z),
                f4: GameLog.Field("bx", b.x), f5: GameLog.Field("by", b.y), f6: GameLog.Field("bz", b.z));

        private static ProfilerRecorder StartJournalRecorder(ProfilerCategory category, string name)
        { try { return ProfilerRecorder.StartNew(category, name, 1); } catch { return default; } }
        private static double JournalRecorderValue(ProfilerRecorder recorder, double scale) =>
            recorder.Valid && recorder.Count > 0 ? recorder.LastValue * scale : double.NaN;

        private void JournalApplicationFocus(bool focus)
        {
            if (journalFocus == focus) return;
            journalFocus = focus;
            duelJournal?.Record("focus", f0: GameLog.Field("focused", focus));
        }

        private void OnApplicationPause(bool paused) => duelJournal?.Record("application_pause", f0: GameLog.Field("paused", paused));
    }
}
