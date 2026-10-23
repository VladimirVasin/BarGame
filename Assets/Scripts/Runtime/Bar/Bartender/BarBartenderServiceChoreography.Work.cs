using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarBartenderServiceChoreography
    {
        public const float IdleWipePatchSeconds = 12f;
        private const float IdleWipeTravelSpeed = .9f;
        private Vector3 wipePatchTargetLocalPosition;
        private float wipeWorkElapsedSeconds;
        private int nextWipePatch;
        private bool changingWipePatch;

        public bool IsChangingWipePatch => changingWipePatch;
        public Vector3 WipePatchTargetLocalPosition => wipePatchTargetLocalPosition;

        private bool IsAtWipePatch =>
            (transform.localPosition - wipePatchTargetLocalPosition).sqrMagnitude <= PositionToleranceSquared &&
            Quaternion.Angle(transform.localRotation, homeLocalRotation) <= AuthoredFacingToleranceDegrees;

        private void AdvanceWipeWork(float deltaTime, bool servesGuest)
        {
            if (servesGuest) return;
            if (IsAtWipePatch && !presentation.Registry.HasSafeWipePatch)
            { SelectNextWipePatch(); return; }
            if (!IsAtWipePatch || !presentation.IsTowelWipeReady)
            { changingWipePatch = true; return; }
            changingWipePatch = false;
            if (presentation.CurrentClipKind != BarBartenderClipKind.Wipe) return;
            wipeWorkElapsedSeconds += deltaTime;
            if (wipeWorkElapsedSeconds < IdleWipePatchSeconds) return;
            wipeWorkElapsedSeconds = 0f;
            SelectNextWipePatch();
        }

        private void SelectNextWipePatch()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int patch = nextWipePatch;
                nextWipePatch = (nextWipePatch + 1) % 3;
                if (!presentation.Registry.TryResolveWorkPatchTarget(patch, out Vector3 world)) continue;
                Vector3 local = transform.parent != null ? transform.parent.InverseTransformPoint(world) : world;
                local.y = homeLocalPosition.y;
                if ((local - transform.localPosition).sqrMagnitude < .25f) continue;
                wipePatchTargetLocalPosition = local;
                changingWipePatch = true;
                return;
            }
        }
    }
}
