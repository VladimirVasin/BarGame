using UnityEngine;

namespace BarPromenade
{
    /// <summary>Actual remaining snow, including tracks and cleared ground.</summary>
    public interface IPlayerSnowSurface
    {
        float SampleMovementSnowDepth(Vector3 position, Vector3 travelDirection);
    }

    public sealed partial class PlayerMotor
    {
        internal const float SnowEnterDepth = .20f;
        internal const float SnowExitDepth = .12f;
        internal const float SnowForwardSpeed = 1.05f;
        internal const float SnowBackwardSpeed = .65f;
        internal const float SnowFootstepStride = .55f;
        private IPlayerSnowSurface snowSurface;
        private Player3DCharacterPresentation snowFootPresentation;
        private Vector3 lastSnowDirection;
        public bool InDeepSnow { get; private set; }
        public float SnowBlend { get; private set; }

        private void UpdateSnowMotion(Vector3 travelDirection, float seconds)
        {
            if (seconds <= 0f) return;
            if (snowSurface == null || snowSurface is Object surfaceObject && surfaceObject == null)
            {
                ResetSnowMotion();
                return;
            }

            travelDirection.y = 0f;
            if (travelDirection.sqrMagnitude > .0001f)
                lastSnowDirection = travelDirection.normalized;
            Vector3 direction = lastSnowDirection.sqrMagnitude > .0001f
                ? lastSnowDirection : transform.forward;
            float depth = snowSurface.SampleMovementSnowDepth(transform.position, direction);
            if (!IsFinite(depth)) depth = 0f;
            InDeepSnow = depth >= (InDeepSnow ? SnowExitDepth : SnowEnterDepth);
            SnowBlend = Mathf.MoveTowards(SnowBlend, InDeepSnow ? 1f : 0f, seconds * 5f);
        }

        private float SnowSpeed(float ordinary, bool backwards = false) =>
            Mathf.Lerp(ordinary, backwards ? SnowBackwardSpeed : SnowForwardSpeed, SnowBlend);

        private void ResetSnowMotion()
        {
            InDeepSnow = false;
            SnowBlend = 0f;
            lastSnowDirection = Vector3.zero;
        }

        private PlayerMotionSample StationaryMotion =>
            new PlayerMotionSample(Vector3.zero, 0f, 0f, snowBlend: SnowBlend);

        private void BindSnowFootContacts(IPlayerMotionPresentation visual)
        {
            if (snowFootPresentation != null) snowFootPresentation.SnowFootContact -= OnSnowFootContact;
            snowFootPresentation = visual as Player3DCharacterPresentation;
            if (snowFootPresentation != null) snowFootPresentation.SnowFootContact += OnSnowFootContact;
        }

        private void OnSnowFootContact(bool left)
        {
            if (!isActiveAndEnabled || SnowBlend <= .5f || PlanarVelocity.sqrMagnitude < .01f ||
                (!InputEnabled && !interactionPoseMoveActive) || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning) return;
            Vector3 at = left ? snowFootPresentation.Metrics.LeftFootWorldPosition
                : snowFootPresentation.Metrics.RightFootWorldPosition;
            EmitFootstep(at, 0f);
        }

        private void OnDestroy()
        {
            if (snowFootPresentation != null) snowFootPresentation.SnowFootContact -= OnSnowFootContact;
        }
    }
}
