using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A fixed menu shot borrows the existing gameplay camera and restores its original focus.</summary>
    public sealed class FixedCameraPoseLease : IDisposable
    {
        private PlayerCameraFollow follow;
        private readonly bool wasFixed;
        private readonly Pose pose;
        private readonly float fieldOfView;
        private readonly FixedCameraFocus focus;

        public FixedCameraPoseLease(PlayerCameraFollow owner)
        {
            follow = owner ? owner : throw new ArgumentNullException(nameof(owner));
            wasFixed = follow.FixedPoseActive; pose = follow.FixedBasePose;
            fieldOfView = follow.FixedBaseFieldOfView; focus = follow.FixedFocus;
        }

        public void Dispose()
        {
            if (!follow) return;
            if (wasFixed)
            {
                follow.SetFixedPose(pose.position, pose.rotation, fieldOfView);
                if (focus.Enabled) follow.SetFixedFocus(focus);
            }
            else { follow.ClearFixedPose(); follow.Snap(); }
            follow = null;
        }
    }
}
