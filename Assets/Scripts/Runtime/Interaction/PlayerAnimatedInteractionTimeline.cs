using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum PlayerAnimatedInteractionPhase
    {
        Idle = 0,
        Entering = 1,
        Looping = 2,
        Exiting = 3
    }

    /// <summary>
    /// Describes the contiguous enter, loop and exit ranges in one atlas.
    /// Frame indices are global and start at zero.
    /// </summary>
    public sealed class PlayerAnimatedInteractionDefinition
    {
        public PlayerAnimatedInteractionDefinition(
            string textureResourcePath,
            int enterFrameCount = 24,
            float enterFramesPerSecond = 12f,
            int loopFrameCount = 16,
            float loopFramesPerSecond = 8f,
            int exitFrameCount = 24,
            float exitFramesPerSecond = 12f,
            bool renderAboveSceneDepth = false,
            IReadOnlyList<float>
                loopFrameExtraHoldSeconds = null,
            bool textureFlipX = true,
            float visualCrossfadeDurationSeconds = 0f,
            bool alignBillboardToCameraPlane = true)
        {
            if (string.IsNullOrWhiteSpace(textureResourcePath))
            {
                throw new ArgumentException(
                    "A texture Resources path is required.",
                    nameof(textureResourcePath));
            }

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
            if (float.IsNaN(visualCrossfadeDurationSeconds) ||
                float.IsInfinity(visualCrossfadeDurationSeconds) ||
                visualCrossfadeDurationSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(visualCrossfadeDurationSeconds),
                    visualCrossfadeDurationSeconds,
                    "Visual crossfade duration must be finite and " +
                    "non-negative.");
            }

            TextureResourcePath = textureResourcePath.Trim();
            EnterFrameCount = enterFrameCount;
            EnterFramesPerSecond = enterFramesPerSecond;
            LoopFrameCount = loopFrameCount;
            LoopFramesPerSecond = loopFramesPerSecond;
            ExitFrameCount = exitFrameCount;
            ExitFramesPerSecond = exitFramesPerSecond;
            RenderAboveSceneDepth =
                renderAboveSceneDepth;
            TextureFlipX = textureFlipX;
            VisualCrossfadeDurationSeconds =
                visualCrossfadeDurationSeconds;
            AlignBillboardToCameraPlane =
                alignBillboardToCameraPlane;
            LoopDurationSeconds =
                loopFrameCount /
                (double)loopFramesPerSecond +
                SumLoopFrameExtraHolds();
            TotalFrameCount = checked(
                enterFrameCount +
                loopFrameCount +
                exitFrameCount);
        }

        public string TextureResourcePath { get; }
        public int EnterFrameCount { get; }
        public float EnterFramesPerSecond { get; }
        public int LoopFrameCount { get; }
        public float LoopFramesPerSecond { get; }
        public int ExitFrameCount { get; }
        public float ExitFramesPerSecond { get; }
        public bool RenderAboveSceneDepth { get; }
        public bool TextureFlipX { get; }
        public float VisualCrossfadeDurationSeconds { get; }
        public bool AlignBillboardToCameraPlane { get; }
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
    /// Pure frame-rate-independent state for a three-range sprite sequence.
    /// The loop persists until RequestExit succeeds.
    /// </summary>
    public sealed class PlayerAnimatedInteractionTimeline
    {
        private readonly PlayerAnimatedInteractionDefinition definition;
        private double phaseElapsedSeconds;
        private float exitDurationMultiplier = 1f;

        public PlayerAnimatedInteractionTimeline(
            PlayerAnimatedInteractionDefinition definition)
        {
            this.definition = definition ??
                throw new ArgumentNullException(nameof(definition));
            Reset();
        }

        public PlayerAnimatedInteractionDefinition Definition =>
            definition;
        public PlayerAnimatedInteractionPhase Phase { get; private set; }
        public int FrameIndex { get; private set; }
        public bool IsActive =>
            Phase != PlayerAnimatedInteractionPhase.Idle;
        public float ExitDurationMultiplier =>
            exitDurationMultiplier;
        public double ExitDurationSeconds =>
            definition.ExitFrameCount /
            (double)definition.ExitFramesPerSecond *
            exitDurationMultiplier;
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

        public bool Begin()
        {
            if (IsActive)
            {
                return false;
            }

            Phase = PlayerAnimatedInteractionPhase.Entering;
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
            Phase = PlayerAnimatedInteractionPhase.Exiting;
            phaseElapsedSeconds = 0d;
            FrameIndex = definition.ExitStartFrame;
            return true;
        }

        public void Reset()
        {
            Phase = PlayerAnimatedInteractionPhase.Idle;
            phaseElapsedSeconds = 0d;
            FrameIndex = -1;
            exitDurationMultiplier = 1f;
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
            phaseElapsedSeconds %= duration;
            FrameIndex = definition.LoopStartFrame +
                GetLoopLocalFrame();
        }

        private void AdvanceExiting()
        {
            double duration = ExitDurationSeconds;
            if (phaseElapsedSeconds >= duration)
            {
                Reset();
                return;
            }

            FrameIndex = definition.ExitStartFrame +
                GetLocalFrameForDuration(
                    definition.ExitFrameCount,
                    duration);
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

        private static double GetDuration(
            int frameCount,
            float framesPerSecond)
        {
            return frameCount / (double)framesPerSecond;
        }
    }
}
