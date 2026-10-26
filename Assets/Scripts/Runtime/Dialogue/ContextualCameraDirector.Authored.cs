using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    public partial class ContextualCameraDirector
    {
        private bool authoredShot;
        private Pose authoredPose;
        private float authoredFov;
        private Vector3 authoredFocus;
        private float authoredEntrySeconds, authoredExitSeconds, authoredNearClipPlane;
        private float authoredOpticalScale = 1f;
        private bool authoredSmoothTransitions;

        /// <summary>The lens clipping value borrowed with this camera session.</summary>
        public float OriginalNearClipPlane { get; private set; }

        /// <summary>Loans the same camera lifecycle to an explicitly authored shot sequence.</summary>
        public bool BeginAuthored(PlayerCameraFollow cameraFollow, Transform subject,
            Transform playerRoot, Pose pose, float fieldOfView, Vector3 focus,
            float entrySeconds = EntrySeconds, float exitSeconds = ExitSeconds,
            bool smoothTransitions = false, float nearClipPlane = float.NaN)
        {
            if (active || activeOwner != null || cameraFollow == null ||
                !cameraFollow.isActiveAndEnabled || cameraFollow.Camera == null ||
                subject == null || playerRoot == null || CinematicDepthOfField.IsActive) return false;
            follow = cameraFollow; camera = cameraFollow.Camera;
            npcRoot = subject; heroRoot = playerRoot;
            objectShot = false; authoredShot = true;
            authoredPose = pose; authoredFov = Mathf.Clamp(fieldOfView, 20f, 100f);
            authoredFocus = focus;
            authoredEntrySeconds = ValidTransitionSeconds(entrySeconds, EntrySeconds);
            authoredExitSeconds = ValidTransitionSeconds(exitSeconds, ExitSeconds);
            authoredSmoothTransitions = smoothTransitions;
            authoredNearClipPlane = ResolveAuthoredNearClip(nearClipPlane);
            authoredOpticalScale = 1f;
            if (!CameraPositionClear(pose.position) || !SightClear(focus, pose.position))
            { ClearReferences(); return false; }
            return AcquireCamera();
        }

        /// <summary>The sequence owns spatial staging; the director retains camera/focus cleanup.</summary>
        public void SetAuthoredShot(Pose pose, float fieldOfView, Vector3 focus, bool cut = false,
            float nearClipPlane = float.NaN, float opticalScale = 1f)
        {
            if (!active || !authoredShot || returning) return;
            authoredPose = pose; authoredFov = Mathf.Clamp(fieldOfView, 20f, 100f);
            authoredFocus = focus;
            if (float.IsFinite(nearClipPlane)) authoredNearClipPlane = ResolveAuthoredNearClip(nearClipPlane);
            authoredOpticalScale = float.IsFinite(opticalScale) ? Mathf.Clamp(opticalScale, 1f / 45f, 1f) : 1f;
            if (!cut) return;
            entering = false;
            Apply(authoredPose, authoredFov);
            camera.nearClipPlane = authoredNearClipPlane;
            UpdateAuthoredFocalLength(authoredOpticalScale);
            UpdateFocusDistance();
        }

        /// <summary>A later shot must satisfy the same clearance contract as the first one.</summary>
        public bool TrySetAuthoredShot(Pose pose, float fieldOfView, Vector3 focus, bool cut = false,
            Transform sightSubject = null, float nearClipPlane = float.NaN, float opticalScale = 1f,
            float clearanceRadius = CameraClearanceMeters, Collider ignoredCoarseProxy = null)
        {
            if (!IsAuthoredShotClear(pose, focus, sightSubject, clearanceRadius, ignoredCoarseProxy)) return false;
            SetAuthoredShot(pose, fieldOfView, focus, cut, nearClipPlane, opticalScale);
            return true;
        }

        /// <summary>A subject may be ignored by its own sight ray, never by lens clearance.</summary>
        public bool IsAuthoredShotClear(Pose pose, Vector3 focus, Transform sightSubject = null,
            float clearanceRadius = CameraClearanceMeters, Collider ignoredCoarseProxy = null) =>
            active && authoredShot && !returning &&
            CameraPositionClear(pose.position, Mathf.Clamp(clearanceRadius, .015f, CameraClearanceMeters), ignoredCoarseProxy) &&
            SightClear(focus, pose.position, sightSubject, ignoredCoarseProxy);

        private float ResolveAuthoredNearClip(float value) => float.IsFinite(value)
            ? Mathf.Clamp(value, .001f, Mathf.Max(.001f, camera.farClipPlane - .001f))
            : camera.nearClipPlane;

        private void UpdateAuthoredFocalLength(float opticalScale) =>
            CinematicDepthOfField.SetOwnedFocalLength(this, 45f * opticalScale);

        private static float ValidTransitionSeconds(float value, float fallback) =>
            float.IsFinite(value) ? Mathf.Max(.01f, value) : fallback;
    }
}
