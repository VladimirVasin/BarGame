using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns the optional Home background composition. The apartment and its
    /// balcony share one scene, so the fixed-camera shot is the authoritative
    /// indoor/outdoor boundary and supplies the existing doorway hysteresis.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class HomeMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/HomeMusic";
        public const string TrackName = "home_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float BalconyFadeDurationSeconds = 1f;

        private HomeFixedCameraController cameraController;
        private bool hasObservedBalconyState;

        public bool IsInitialized { get; private set; }
        public bool IsBalconyActive { get; private set; }
        public HomeFixedCameraController CameraController =>
            cameraController;

        protected override string TrackResourcePath => ResourcePath;

        public void Initialize(
            HomeFixedCameraController homeCameraController)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home music player is already initialized.");
            }

            if (homeCameraController == null)
            {
                throw new ArgumentNullException(
                    nameof(homeCameraController));
            }

            if (!homeCameraController.IsInitialized)
            {
                throw new ArgumentException(
                    "Home music requires an initialized fixed-camera controller.",
                    nameof(homeCameraController));
            }

            cameraController = homeCameraController;
            hasObservedBalconyState = false;
            IsInitialized = true;
            RefreshBalconyState();
        }

        /// <summary>
        /// Synchronizes music with the camera-owned balcony zone immediately.
        /// Returns true only when the observed indoor/outdoor state changed.
        /// </summary>
        public bool RefreshBalconyState()
        {
            if (!IsInitialized ||
                cameraController == null ||
                !cameraController.IsInitialized ||
                !cameraController.isActiveAndEnabled)
            {
                return false;
            }

            bool balconyActive =
                cameraController.ActiveShotKind ==
                HomeCameraShotKind.Balcony;
            if (hasObservedBalconyState &&
                balconyActive == IsBalconyActive)
            {
                return false;
            }

            bool hadObservation = hasObservedBalconyState;
            hasObservedBalconyState = true;
            IsBalconyActive = balconyActive;

            // A scene-exit fade owns the source once requested. A late camera
            // update must never revive the Home track during scene teardown.
            if (IsSceneExitFadeRequested)
            {
                return true;
            }

            if (balconyActive)
            {
                FadeOutAndPause(BalconyFadeDurationSeconds);
            }
            else if (hadObservation)
            {
                ResumeWithFadeIn(BalconyFadeDurationSeconds);
            }

            return true;
        }

        protected override void Update()
        {
            base.Update();
            RefreshBalconyState();
        }
    }
}
