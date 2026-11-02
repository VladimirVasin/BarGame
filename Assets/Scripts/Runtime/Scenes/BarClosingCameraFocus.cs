using System;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A scoped look at the speaking worker, returning to the exact borrowed menu or gameplay lens.</summary>
    [DisallowMultipleComponent]
    public sealed class BarClosingCameraFocus : MonoBehaviour
    {
        public const float EntrySeconds = .45f;
        public const float ReturnSeconds = .4f;
        private PlayerCameraFollow follow;
        private Camera camera;
        private Transform speaker;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerAnimatedInteractionController interaction;
        private readonly ContextualCameraDirector shotProbe = new ContextualCameraDirector();
        private IDisposable cameraLoan, focusLoan;
        private Pose originalPose, returnStartPose;
        private Vector3 shotPosition;
        private float originalFov, originalNearClip, returnStartFov, elapsed;
        private bool previousMotorInput, previousInteractorInput, previousInteraction, cutShot;

        public bool IsActive { get; private set; }
        public bool IsReturning { get; private set; }

        public bool Begin(BarInteriorRoot bar, PlayerCameraFollow cameraFollow = null)
        {
            if (IsActive || bar == null || bar.Player.GameObject == null || bar.Conversation == null)
                return false;
            cameraFollow ??= Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow>() : null;
            Transform head = bar.Conversation.Head(BarConversationController.BartenderRole);
            if (cameraFollow == null || cameraFollow.Camera == null || cameraFollow.PresentationSuspended ||
                head == null || !head.gameObject.activeInHierarchy) return false;
            follow = cameraFollow;
            camera = follow.Camera;
            speaker = head;
            motor = bar.Player.Motor;
            interactor = bar.Player.Interactor;
            interaction = bar.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            originalPose = new Pose(camera.transform.position, camera.transform.rotation);
            originalFov = camera.fieldOfView;
            originalNearClip = camera.nearClipPlane;
            if (!TryResolveShot(bar, out shotPosition, out cutShot))
            {
                follow = null;
                camera = null;
                speaker = null;
                return false;
            }
            previousMotorInput = motor.InputEnabled;
            previousInteractorInput = interactor.InputEnabled;
            previousInteraction = interaction != null && interaction.IsActive;
            cameraLoan = follow.SuspendPresentation();
            // Keep the parent's live page focus intact while the room's ordinary
            // depth profile presents the worker clearly from this same lens position.
            focusLoan = CinematicDepthOfField.SuspendPresentation();
            IsActive = true;
            IsReturning = false;
            elapsed = 0f;
            LockInput();
            if (cutShot) Tick(0f);
            return true;
        }

        public void End()
        {
            if (!IsActive || IsReturning) return;
            if (camera == null) { RestoreImmediate(); return; }
            if (cutShot) { RestoreImmediate(); return; }
            returnStartPose = new Pose(camera.transform.position, camera.transform.rotation);
            returnStartFov = camera.fieldOfView;
            elapsed = 0f;
            IsReturning = true;
        }

        /// <summary>The closing owner samples this after the rig and ordinary camera owners.</summary>
        public void Tick(float seconds)
        {
            if (!IsActive) return;
            if (camera == null || speaker == null || !speaker.gameObject.activeInHierarchy ||
                SceneTransitionService.IsTransitioning) { RestoreImmediate(); return; }
            LockInput();
            float dt = float.IsFinite(seconds) ? Mathf.Max(0f, seconds) : 0f;
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) dt = 0f;
            elapsed += dt;
            if (IsReturning)
            {
                float t = Smooth(elapsed / ReturnSeconds);
                Apply(Vector3.Lerp(returnStartPose.position, originalPose.position, t),
                    Quaternion.Slerp(returnStartPose.rotation, originalPose.rotation, t),
                    Mathf.Lerp(returnStartFov, originalFov, t));
                if (elapsed >= ReturnSeconds) RestoreImmediate();
                return;
            }
            Vector3 focus = speaker.position - Vector3.up * .12f;
            Vector3 direction = focus - shotPosition;
            if (direction.sqrMagnitude < .01f) { End(); return; }
            // A pan has no lens travel through furniture. Keep a level speaking
            // shot and author the FOV with the head-and-shoulders framing together.
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            float targetFov = Mathf.Clamp(2f * Mathf.Atan(.65f / direction.magnitude) * Mathf.Rad2Deg, 32f, 65f);
            float blend = cutShot ? 1f : Smooth(elapsed / EntrySeconds);
            Apply(shotPosition, Quaternion.Slerp(originalPose.rotation, targetRotation, blend),
                Mathf.Lerp(originalFov, targetFov, blend));
        }

        private bool TryResolveShot(BarInteriorRoot bar, out Vector3 position, out bool cut)
        {
            position = originalPose.position;
            cut = false;
            Vector3 focus = speaker.position - Vector3.up * .12f;
            Transform worker = bar.Bartender.transform;
            Transform hero = bar.Player.GameObject.transform;
            if (shotProbe.CanPrepareAuthored(follow, worker, hero,
                    new Pose(position, Quaternion.LookRotation(focus - position, Vector3.up)), focus)) return true;
            // The view can originate behind a booth partition. Reuse the shared
            // lens/sight validator and cut to a real clear hall view in that case.
            Vector3 forward = Vector3.ProjectOnPlane(worker.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int candidate = 0; candidate < 4; candidate++)
            {
                float side = candidate == 1 ? .65f : candidate == 2 ? -.65f : 0f;
                float distance = candidate == 3 ? 2.5f : 1.8f;
                Vector3 candidatePosition = speaker.position + forward * distance + right * side;
                Pose pose = new Pose(candidatePosition, Quaternion.LookRotation(focus - candidatePosition, Vector3.up));
                if (!shotProbe.CanPrepareAuthored(follow, worker, hero, pose, focus)) continue;
                position = candidatePosition;
                cut = true;
                return true;
            }
            return false;
        }

        public void RestoreImmediate()
        {
            if (!IsActive) return;
            if (camera != null)
            {
                Apply(originalPose.position, originalPose.rotation, originalFov);
                camera.nearClipPlane = originalNearClip;
            }
            bool interactionCompleted = previousInteraction && (interaction == null || !interaction.IsActive);
            if (motor != null) motor.SetInputEnabled(previousMotorInput || interactionCompleted);
            if (interactor != null) interactor.SetInputEnabled(previousInteractorInput || interactionCompleted);
            focusLoan?.Dispose();
            cameraLoan?.Dispose();
            focusLoan = cameraLoan = null;
            IsActive = IsReturning = false;
            follow = null;
            camera = null;
            speaker = null;
            motor = null;
            interactor = null;
            interaction = null;
        }

        private void LockInput()
        {
            motor?.SetInputEnabled(false);
            interactor?.SetInputEnabled(false);
        }

        private void Apply(Vector3 position, Quaternion rotation, float fov)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
        }

        private static float Smooth(float amount)
        {
            float t = Mathf.Clamp01(amount);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private void OnDisable() => RestoreImmediate();
        private void OnDestroy() => RestoreImmediate();
    }
}
