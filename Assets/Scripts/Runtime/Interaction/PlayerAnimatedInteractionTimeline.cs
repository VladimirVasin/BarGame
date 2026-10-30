using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum PlayerAnimatedInteractionPhase
    {
        Idle = 0,
        Entering = 1,
        Looping = 2,
        Exiting = 3,
        Positioning = 4
    }

    /// <summary>
    /// One exact physical-root and pelvis-reference handoff pose. Entry and
    /// exit stay separate so their locations and facings can evolve
    /// independently.
    /// </summary>
    public readonly struct PlayerAnimatedInteractionPose
    {
        public PlayerAnimatedInteractionPose(
            Vector3 rootPosition,
            Quaternion rootRotation,
            Vector3 hipPosition)
        {
            if (!IsFinite(rootPosition))
            {
                throw new ArgumentException(
                    "The interaction root position must be finite.",
                    nameof(rootPosition));
            }

            if (!IsFinite(hipPosition))
            {
                throw new ArgumentException(
                    "The interaction hip position must be finite.",
                    nameof(hipPosition));
            }

            float rotationMagnitudeSquared =
                rootRotation.x * rootRotation.x +
                rootRotation.y * rootRotation.y +
                rootRotation.z * rootRotation.z +
                rootRotation.w * rootRotation.w;
            if (!IsFinite(rootRotation) ||
                rotationMagnitudeSquared <= 0.000001f)
            {
                throw new ArgumentException(
                    "The interaction root rotation must be finite and " +
                    "non-zero.",
                    nameof(rootRotation));
            }

            float inverseMagnitude =
                1f / Mathf.Sqrt(rotationMagnitudeSquared);
            RootPosition = rootPosition;
            RootRotation = new Quaternion(
                rootRotation.x * inverseMagnitude,
                rootRotation.y * inverseMagnitude,
                rootRotation.z * inverseMagnitude,
                rootRotation.w * inverseMagnitude);
            HipPosition = hipPosition;
        }

        public Vector3 RootPosition { get; }
        public Quaternion RootRotation { get; }
        public Vector3 HipPosition { get; }

        internal void Validate(string parameterName)
        {
            float rotationMagnitudeSquared =
                RootRotation.x * RootRotation.x +
                RootRotation.y * RootRotation.y +
                RootRotation.z * RootRotation.z +
                RootRotation.w * RootRotation.w;
            if (!IsFinite(RootPosition) ||
                !IsFinite(HipPosition) ||
                !IsFinite(RootRotation) ||
                rotationMagnitudeSquared <= 0.000001f)
            {
                throw new ArgumentException(
                    "The interaction pose must contain finite root/hip " +
                    "positions and a finite, non-zero root rotation.",
                    parameterName);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Describes the three required production clips and their deterministic
    /// logical sampling ranges. Frame indices remain as stable gameplay timing
    /// markers and start at zero; no texture atlas is involved.
    /// </summary>
    public sealed class PlayerAnimatedInteractionDefinition
    {
        public PlayerAnimatedInteractionDefinition(
            string enterClipName,
            string loopClipName,
            string exitClipName,
            int enterFrameCount = 24,
            float enterFramesPerSecond = 12f,
            int loopFrameCount = 16,
            float loopFramesPerSecond = 8f,
            int exitFrameCount = 24,
            float exitFramesPerSecond = 12f,
            IReadOnlyList<float>
                loopFrameExtraHoldSeconds = null,
            bool requiresMouthAccess = false)
        {
            RequiresMouthAccess = requiresMouthAccess;
            EnterClipName = ValidateClipName(
                enterClipName,
                nameof(enterClipName));
            LoopClipName = ValidateClipName(
                loopClipName,
                nameof(loopClipName));
            ExitClipName = ValidateClipName(
                exitClipName,
                nameof(exitClipName));

            ValidateRange(
                enterFrameCount,
                enterFramesPerSecond,
                nameof(enterFrameCount),
                nameof(enterFramesPerSecond));
            ValidateRange(
                loopFrameCount,
                loopFramesPerSecond,
                nameof(loopFrameCount),
                nameof(loopFramesPerSecond));
            ValidateRange(
                exitFrameCount,
                exitFramesPerSecond,
                nameof(exitFrameCount),
                nameof(exitFramesPerSecond));
            this.loopFrameExtraHoldSeconds =
                CopyAndValidateLoopFrameHolds(
                    loopFrameExtraHoldSeconds,
                    loopFrameCount);
            EnterFrameCount = enterFrameCount;
            EnterFramesPerSecond = enterFramesPerSecond;
            LoopFrameCount = loopFrameCount;
            LoopFramesPerSecond = loopFramesPerSecond;
            ExitFrameCount = exitFrameCount;
            ExitFramesPerSecond = exitFramesPerSecond;
            LoopDurationSeconds =
                loopFrameCount /
                (double)loopFramesPerSecond +
                SumLoopFrameExtraHolds();
            TotalFrameCount = checked(
                enterFrameCount +
                loopFrameCount +
                exitFrameCount);
        }

        public int EnterFrameCount { get; }
        public bool RequiresMouthAccess { get; }
        public float EnterFramesPerSecond { get; }
        public int LoopFrameCount { get; }
        public float LoopFramesPerSecond { get; }
        public int ExitFrameCount { get; }
        public float ExitFramesPerSecond { get; }
        public string EnterClipName { get; }
        public string LoopClipName { get; }
        public string ExitClipName { get; }
        public double LoopDurationSeconds { get; }
        public int TotalFrameCount { get; }
        public int EnterStartFrame => 0;
        public int LoopStartFrame => EnterFrameCount;
        public int ExitStartFrame =>
            EnterFrameCount + LoopFrameCount;
        private readonly float[] loopFrameExtraHoldSeconds;

        public float GetLoopFrameExtraHoldSeconds(
            int localFrameIndex)
        {
            ValidateLoopFrameIndex(localFrameIndex);
            return loopFrameExtraHoldSeconds[localFrameIndex];
        }

        public double GetLoopFrameDurationSeconds(
            int localFrameIndex)
        {
            ValidateLoopFrameIndex(localFrameIndex);
            return 1d / LoopFramesPerSecond +
                loopFrameExtraHoldSeconds[localFrameIndex];
        }

        private static void ValidateRange(
            int frameCount,
            float framesPerSecond,
            string frameCountParameter,
            string framesPerSecondParameter)
        {
            if (frameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    frameCountParameter,
                    frameCount,
                    "A phase must contain at least one frame.");
            }

            if (float.IsNaN(framesPerSecond) ||
                float.IsInfinity(framesPerSecond) ||
                framesPerSecond <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    framesPerSecondParameter,
                    framesPerSecond,
                    "Frames per second must be finite and positive.");
            }
        }

        private static string ValidateClipName(
            string clipName,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                throw new ArgumentException(
                    "A production Player 3D clip name is required.",
                    parameterName);
            }

            return clipName.Trim();
        }

        private static float[] CopyAndValidateLoopFrameHolds(
            IReadOnlyList<float> source,
            int loopFrameCount)
        {
            var result = new float[loopFrameCount];
            if (source == null)
            {
                return result;
            }

            if (source.Count != loopFrameCount)
            {
                throw new ArgumentException(
                    "Loop frame holds must match the loop frame count.",
                    nameof(loopFrameExtraHoldSeconds));
            }

            for (int index = 0; index < source.Count; index++)
            {
                float holdSeconds = source[index];
                if (float.IsNaN(holdSeconds) ||
                    float.IsInfinity(holdSeconds) ||
                    holdSeconds < 0f)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(loopFrameExtraHoldSeconds),
                        holdSeconds,
                        "Loop frame holds must be finite and " +
                        "non-negative.");
                }

                result[index] = holdSeconds;
            }

            return result;
        }

        private double SumLoopFrameExtraHolds()
        {
            double total = 0d;
            for (int index = 0;
                 index < loopFrameExtraHoldSeconds.Length;
                 index++)
            {
                total += loopFrameExtraHoldSeconds[index];
            }

            return total;
        }

        private void ValidateLoopFrameIndex(
            int localFrameIndex)
        {
            if (localFrameIndex < 0 ||
                localFrameIndex >= LoopFrameCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localFrameIndex),
                    localFrameIndex,
                    "Loop frame index is outside the loop range.");
            }
        }
    }

    /// <summary>
    /// Retimes two contiguous portions of one authored exit, with an optional
    /// held landmark and independently timed motion after it. An explicit
    /// release may retain that landmark beyond its minimum hold. Clip and
    /// pelvis sample the same progress, preserving contacts and both endpoints.
    /// </summary>
    public readonly struct PlayerAnimatedInteractionExitTiming
    {
        public PlayerAnimatedInteractionExitTiming(
            float splitClipProgress,
            float firstSegmentSeconds,
            float remainingSegmentSeconds,
            float holdClipProgress = 0f,
            float holdSeconds = 0f,
            float postHoldSeconds = 0f,
            bool waitForHoldRelease = false)
        {
            SplitClipProgress = splitClipProgress;
            FirstSegmentSeconds = firstSegmentSeconds;
            RemainingSegmentSeconds = remainingSegmentSeconds;
            HoldClipProgress = holdClipProgress;
            HoldSeconds = holdSeconds;
            PostHoldSeconds = postHoldSeconds;
            WaitForHoldRelease = waitForHoldRelease;
            Validate();
        }

        public float SplitClipProgress { get; }
        public float FirstSegmentSeconds { get; }
        public float RemainingSegmentSeconds { get; }
        public float HoldClipProgress { get; }
        public float HoldSeconds { get; }
        public float PostHoldSeconds { get; }
        public bool WaitForHoldRelease { get; }
        public double HoldStartSeconds => FirstSegmentSeconds +
            RemainingSegmentSeconds * (double)(HoldClipProgress - SplitClipProgress) /
            (1f - SplitClipProgress);
        /// <summary>Scheduled motion and minimum hold, excluding any explicit-release wait.</summary>
        public double DurationSeconds => PostHoldSeconds > 0f
            ? HoldStartSeconds + HoldSeconds + PostHoldSeconds
            : (double)FirstSegmentSeconds + RemainingSegmentSeconds + HoldSeconds;

        public bool IsHolding(double elapsedSeconds) => HoldSeconds > 0f &&
            elapsedSeconds >= HoldStartSeconds && elapsedSeconds < HoldStartSeconds + HoldSeconds;

        public float HoldProgress(double elapsedSeconds) => HoldSeconds > 0f
            ? Mathf.Clamp01((float)((elapsedSeconds - HoldStartSeconds) / HoldSeconds)) : 0f;

        public float Evaluate(double elapsedSeconds)
        {
            if (elapsedSeconds <= 0d) return 0f;
            if (HoldSeconds > 0f)
            {
                if (IsHolding(elapsedSeconds)) return HoldClipProgress;
                if (elapsedSeconds >= HoldStartSeconds + HoldSeconds)
                {
                    if (PostHoldSeconds > 0f)
                        return Mathf.Lerp(HoldClipProgress, 1f,
                            (float)((elapsedSeconds - HoldStartSeconds - HoldSeconds) / PostHoldSeconds));
                    elapsedSeconds -= HoldSeconds;
                }
            }
            if (elapsedSeconds < FirstSegmentSeconds)
                return (float)(elapsedSeconds / FirstSegmentSeconds) * SplitClipProgress;
            if (elapsedSeconds >= (double)FirstSegmentSeconds + RemainingSegmentSeconds) return 1f;
            return SplitClipProgress +
                (float)((elapsedSeconds - FirstSegmentSeconds) / RemainingSegmentSeconds) *
                (1f - SplitClipProgress);
        }

        internal void Validate()
        {
            if (float.IsNaN(SplitClipProgress) ||
                SplitClipProgress <= 0f || SplitClipProgress >= 1f ||
                float.IsNaN(FirstSegmentSeconds) || float.IsInfinity(FirstSegmentSeconds) ||
                FirstSegmentSeconds <= 0f ||
                float.IsNaN(RemainingSegmentSeconds) || float.IsInfinity(RemainingSegmentSeconds) ||
                RemainingSegmentSeconds <= 0f ||
                float.IsNaN(HoldSeconds) || float.IsInfinity(HoldSeconds) || HoldSeconds < 0f ||
                float.IsNaN(PostHoldSeconds) || float.IsInfinity(PostHoldSeconds) || PostHoldSeconds < 0f ||
                (PostHoldSeconds > 0f && HoldSeconds <= 0f) ||
                (WaitForHoldRelease && HoldSeconds <= 0f) ||
                (HoldSeconds > 0f && (float.IsNaN(HoldClipProgress) ||
                    HoldClipProgress < SplitClipProgress || HoldClipProgress >= 1f)))
                throw new ArgumentOutOfRangeException(nameof(PlayerAnimatedInteractionExitTiming),
                    "Exit timing requires positive finite motion durations and an optional finite second-segment hold.");
        }
    }

    /// <summary>
    /// Pure frame-rate-independent state for a three-range presentation
    /// sequence. The loop persists until RequestExit succeeds.
    /// </summary>
    public sealed class PlayerAnimatedInteractionTimeline
    {
        private readonly PlayerAnimatedInteractionDefinition definition;
        private readonly bool exitAfterOneLoopCycle;
        private double phaseElapsedSeconds;
        private float exitDurationMultiplier = 1f;
        private PlayerAnimatedInteractionExitTiming? exitTiming;
        private bool exitEndpointPresented;
        private bool exitHoldPresented;
        private bool exitHoldReleased;
        private bool exitAtLoopBoundaryRequested;

        public PlayerAnimatedInteractionTimeline(
            PlayerAnimatedInteractionDefinition definition,
            bool exitAfterOneLoopCycle = false)
        {
            this.definition = definition ??
                throw new ArgumentNullException(nameof(definition));
            this.exitAfterOneLoopCycle = exitAfterOneLoopCycle;
            Reset();
        }

        public PlayerAnimatedInteractionDefinition Definition =>
            definition;
        public PlayerAnimatedInteractionPhase Phase { get; private set; }
        public int FrameIndex { get; private set; }
        public bool IsActive =>
            Phase != PlayerAnimatedInteractionPhase.Idle;
        public bool ExitsAfterOneLoopCycle => exitAfterOneLoopCycle;
        public float ExitDurationMultiplier =>
            exitDurationMultiplier;
        public double ExitDurationSeconds =>
            exitTiming?.DurationSeconds ?? definition.ExitFrameCount /
            (double)definition.ExitFramesPerSecond *
            exitDurationMultiplier;
        public bool IsExitHoldActive => Phase == PlayerAnimatedInteractionPhase.Exiting &&
            exitTiming.HasValue && (exitTiming.Value.IsHolding(phaseElapsedSeconds) ||
                exitTiming.Value.WaitForHoldRelease && !exitHoldReleased &&
                phaseElapsedSeconds >= exitTiming.Value.HoldStartSeconds);
        public float ExitHoldProgress => Phase == PlayerAnimatedInteractionPhase.Exiting && exitTiming.HasValue
            ? exitTiming.Value.HoldProgress(phaseElapsedSeconds) : 0f;
        public float PhaseProgress
        {
            get
            {
                switch (Phase)
                {
                    case PlayerAnimatedInteractionPhase.Entering:
                        return GetProgress(
                            definition.EnterFrameCount,
                            definition.EnterFramesPerSecond);
                    case PlayerAnimatedInteractionPhase.Looping:
                        return GetProgress(
                            definition.LoopDurationSeconds);
                    case PlayerAnimatedInteractionPhase.Exiting:
                        return GetProgress(ExitDurationSeconds);
                    default:
                        return 0f;
                }
            }
        }
        public float ClipProgress =>
            Phase == PlayerAnimatedInteractionPhase.Looping
                ? GetLoopClipProgress()
                : Phase == PlayerAnimatedInteractionPhase.Exiting && exitTiming.HasValue
                    ? IsExitHoldActive && exitTiming.Value.WaitForHoldRelease
                        ? exitTiming.Value.HoldClipProgress
                        : exitTiming.Value.Evaluate(phaseElapsedSeconds)
                    : PhaseProgress;

        public bool Begin()
        {
            if (IsActive)
            {
                return false;
            }

            Phase = PlayerAnimatedInteractionPhase.Entering;
            exitHoldReleased = false;
            phaseElapsedSeconds = 0d;
            FrameIndex = definition.EnterStartFrame;
            return true;
        }

        public bool BeginLooping()
        {
            if (IsActive)
            {
                return false;
            }

            Phase = PlayerAnimatedInteractionPhase.Looping;
            exitHoldReleased = false;
            phaseElapsedSeconds = 0d;
            FrameIndex = definition.LoopStartFrame;
            return true;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime),
                    deltaTime,
                    "Delta time must be finite and non-negative.");
            }

            if (!IsActive || deltaTime <= 0f)
            {
                return;
            }

            phaseElapsedSeconds += deltaTime;
            switch (Phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    AdvanceEntering();
                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    AdvanceLooping();
                    break;
                case PlayerAnimatedInteractionPhase.Exiting:
                    AdvanceExiting();
                    break;
            }
        }

        public bool RequestExit()
        {
            return RequestExit(1f);
        }

        /// <summary>Finish the visible loop at its authored seam before exit.</summary>
        public bool RequestExitAtLoopBoundary()
        {
            if (Phase != PlayerAnimatedInteractionPhase.Looping ||
                exitAtLoopBoundaryRequested)
            {
                return false;
            }

            exitAtLoopBoundaryRequested = true;
            return true;
        }

        public bool RequestExit(float durationMultiplier)
        {
            if (float.IsNaN(durationMultiplier) ||
                float.IsInfinity(durationMultiplier) ||
                durationMultiplier <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationMultiplier),
                    durationMultiplier,
                    "Exit duration multiplier must be finite and positive.");
            }

            if (Phase != PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            exitDurationMultiplier = durationMultiplier;
            exitTiming = null;
            exitEndpointPresented = false;
            exitHoldPresented = false;
            exitHoldReleased = false;
            exitAtLoopBoundaryRequested = false;
            Phase = PlayerAnimatedInteractionPhase.Exiting;
            phaseElapsedSeconds = 0d;
            FrameIndex = definition.ExitStartFrame;
            return true;
        }

        public bool RequestExitWithTiming(PlayerAnimatedInteractionExitTiming timing)
        {
            timing.Validate();
            if (!RequestExit()) return false;
            exitTiming = timing;
            exitDurationMultiplier = (float)(timing.DurationSeconds /
                GetDuration(definition.ExitFrameCount, definition.ExitFramesPerSecond));
            return true;
        }

        /// <summary>
        /// Releases an explicitly held exit only after its complete minimum
        /// hold. Waiting never accumulates motion time; the next Advance starts
        /// the remaining path from the same exact authored landmark.
        /// </summary>
        public bool ReleaseExitHold()
        {
            if (Phase != PlayerAnimatedInteractionPhase.Exiting || !exitTiming.HasValue ||
                !exitTiming.Value.WaitForHoldRelease || exitHoldReleased || !exitHoldPresented ||
                phaseElapsedSeconds < exitTiming.Value.HoldStartSeconds + exitTiming.Value.HoldSeconds)
                return false;

            exitHoldReleased = true;
            return true;
        }

        /// <summary>
        /// Returns a persistent loop to its authored seam without ending it.
        /// Nested seated actions use the seam before taking clip ownership and
        /// again when returning it to the parent interaction.
        /// </summary>
        public bool RestartLoopCycle()
        {
            if (Phase != PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            phaseElapsedSeconds = 0d;
            FrameIndex = definition.LoopStartFrame;
            return true;
        }

        public void Reset()
        {
            Phase = PlayerAnimatedInteractionPhase.Idle;
            phaseElapsedSeconds = 0d;
            FrameIndex = -1;
            exitDurationMultiplier = 1f;
            exitTiming = null;
            exitEndpointPresented = false;
            exitHoldPresented = false;
            exitHoldReleased = false;
            exitAtLoopBoundaryRequested = false;
        }

        private void AdvanceEntering()
        {
            double duration = GetDuration(
                definition.EnterFrameCount,
                definition.EnterFramesPerSecond);
            if (phaseElapsedSeconds < duration)
            {
                FrameIndex = definition.EnterStartFrame +
                    GetLocalFrame(
                        definition.EnterFrameCount,
                        definition.EnterFramesPerSecond);
                return;
            }

            phaseElapsedSeconds -= duration;
            Phase = PlayerAnimatedInteractionPhase.Looping;
            AdvanceLooping();
        }

        private void AdvanceLooping()
        {
            double duration =
                definition.LoopDurationSeconds;
            if (exitAtLoopBoundaryRequested && phaseElapsedSeconds >= duration)
            {
                // Present the exact seam even when a hitch crossed it. The
                // chosen exit's first sample has the same authored pose.
                phaseElapsedSeconds = 0d;
                exitAtLoopBoundaryRequested = false;
                exitDurationMultiplier = 1f;
                exitTiming = null;
                exitEndpointPresented = false;
                Phase = PlayerAnimatedInteractionPhase.Exiting;
                FrameIndex = definition.ExitStartFrame;
                return;
            }

            if (exitAfterOneLoopCycle && phaseElapsedSeconds >= duration)
            {
                phaseElapsedSeconds -= duration;
                exitDurationMultiplier = 1f;
                exitTiming = null;
                exitEndpointPresented = false;
                Phase = PlayerAnimatedInteractionPhase.Exiting;
                FrameIndex = definition.ExitStartFrame;
                AdvanceExiting();
                return;
            }

            phaseElapsedSeconds %= duration;
            FrameIndex = definition.LoopStartFrame +
                GetLoopLocalFrame();
        }

        private void AdvanceExiting()
        {
            if (exitTiming.HasValue && exitTiming.Value.HoldSeconds > 0f &&
                !exitHoldPresented && phaseElapsedSeconds >= exitTiming.Value.HoldStartSeconds)
            {
                // A hitch must present the held contact landmark before its
                // complete hold starts, just as the terminal pose owns a frame.
                phaseElapsedSeconds = exitTiming.Value.HoldStartSeconds;
                exitHoldPresented = true;
            }
            if (exitTiming.HasValue && exitTiming.Value.WaitForHoldRelease && !exitHoldReleased)
            {
                double holdEnd = exitTiming.Value.HoldStartSeconds + exitTiming.Value.HoldSeconds;
                // An arbitrarily long wait contributes no post-hold motion.
                // The preceding landmark gate still presents the exact first
                // held frame before counting its finite minimum duration.
                if (phaseElapsedSeconds > holdEnd) phaseElapsedSeconds = holdEnd;
            }
            double duration = ExitDurationSeconds;
            int lastFrame = definition.ExitStartFrame +
                definition.ExitFrameCount - 1;
            if (phaseElapsedSeconds >= duration)
            {
                if (!exitEndpointPresented)
                {
                    phaseElapsedSeconds = duration;
                    FrameIndex = lastFrame;
                    exitEndpointPresented = true;
                    return;
                }

                Reset();
                return;
            }

            FrameIndex = definition.ExitStartFrame +
                (exitTiming.HasValue
                    ? Math.Min(definition.ExitFrameCount - 1,
                        (int)Math.Floor(ClipProgress * definition.ExitFrameCount))
                    : GetLocalFrameForDuration(definition.ExitFrameCount, duration));
        }

        private int GetLocalFrame(
            int frameCount,
            float framesPerSecond)
        {
            int frame = (int)Math.Floor(
                phaseElapsedSeconds * framesPerSecond);
            return Math.Min(frameCount - 1, Math.Max(0, frame));
        }

        private float GetProgress(
            int frameCount,
            float framesPerSecond)
        {
            return GetProgress(
                GetDuration(
                    frameCount,
                    framesPerSecond));
        }

        private float GetProgress(double duration)
        {
            return (float)Math.Min(
                1d,
                Math.Max(0d, phaseElapsedSeconds / duration));
        }

        private int GetLocalFrameForDuration(
            int frameCount,
            double duration)
        {
            int frame = (int)Math.Floor(
                phaseElapsedSeconds /
                duration *
                frameCount);
            return Math.Min(
                frameCount - 1,
                Math.Max(0, frame));
        }

        private int GetLoopLocalFrame()
        {
            double frameEndSeconds = 0d;
            for (int frame = 0;
                 frame < definition.LoopFrameCount;
                 frame++)
            {
                frameEndSeconds +=
                    definition.GetLoopFrameDurationSeconds(frame);
                if (phaseElapsedSeconds < frameEndSeconds)
                {
                    return frame;
                }
            }

            return definition.LoopFrameCount - 1;
        }

        private float GetLoopClipProgress()
        {
            double cursor = 0d;
            double baseFrameDuration =
                1d / definition.LoopFramesPerSecond;
            for (int frame = 0;
                 frame < definition.LoopFrameCount;
                 frame++)
            {
                double baseFrameEnd =
                    cursor + baseFrameDuration;
                if (phaseElapsedSeconds < baseFrameEnd)
                {
                    double local =
                        (phaseElapsedSeconds - cursor) /
                        baseFrameDuration;
                    return Mathf.Clamp01(
                        (float)((frame + local) /
                                definition.LoopFrameCount));
                }

                double heldFrameEnd =
                    baseFrameEnd +
                    definition.GetLoopFrameExtraHoldSeconds(frame);
                if (phaseElapsedSeconds < heldFrameEnd)
                {
                    return (frame + 1f) /
                           definition.LoopFrameCount;
                }

                cursor = heldFrameEnd;
            }

            return 1f;
        }

        private static double GetDuration(
            int frameCount,
            float framesPerSecond)
        {
            return frameCount / (double)framesPerSecond;
        }
    }
}
