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
    /// One exact physical-root and upright hip-reference handoff pose. The hip
    /// reference is resolved into the atlas billboard plane at presentation.
    /// Entry and exit stay separate so their locations and facings can evolve
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

        internal Vector3 ResolveHandoffHip(
            Vector3 uprightHip,
            Camera camera)
        {
            if (!AlignBillboardToCameraPlane || camera == null)
            {
                return uprightHip;
            }

            float feetToHip =
                (PlayerAnimatedInteractionController.HipPivotYPixels -
                 PlayerSpriteRig.FeetPivotPixels) /
                PlayerAnimatedInteractionController.PixelsPerUnit;
            return uprightHip +
                (camera.transform.up - Vector3.up) * feetToHip;
        }

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
        private bool exitEndpointPresented;

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
            exitEndpointPresented = false;
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
            exitEndpointPresented = false;
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
                GetLocalFrameForDuration(
                    definition.ExitFrameCount,
                    duration);
            exitEndpointPresented = FrameIndex == lastFrame;
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
