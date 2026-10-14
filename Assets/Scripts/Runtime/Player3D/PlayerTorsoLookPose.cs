using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded look over the real lumbar/chest chain; only Advance owns time.</summary>
    internal sealed class PlayerTorsoLookPose
    {
        internal const float DownPitchDegrees = 35f;
        internal const float UpPitchDegrees = 20f;
        internal const float MaximumYawDegrees = 20f;
        internal const float MaximumRollDegrees = 6f;
        private const float SpineShare = .4f;
        private readonly Transform spine, chest, frame;
        private SecondOrderFilter pitch = new SecondOrderFilter(11f, 1f);
        private SecondOrderFilter yaw = new SecondOrderFilter(12f, 1f);
        private SecondOrderFilter roll = new SecondOrderFilter(10f, 1f);
        private Vector3 worldDirection;
        private float neutralPitch;
        private Pose spineBase, chestBase;
        private bool hasIntent, ordinaryIntent, applied;

        internal Vector3 AnglesDegrees => ClampEnvelope(new Vector3(pitch.Value, yaw.Value, roll.Value));

        internal PlayerTorsoLookPose(Transform spine, Transform chest, Transform actorFrame)
        {
            this.spine = spine != null ? spine : throw new ArgumentNullException(nameof(spine));
            this.chest = chest != null ? chest : throw new ArgumentNullException(nameof(chest));
            frame = actorFrame != null ? actorFrame : throw new ArgumentNullException(nameof(actorFrame));
        }

        internal void SetIntent(Vector3 direction, float neutralPitchDegrees, bool ordinaryLook)
        {
            if (!Finite(direction) || !float.IsFinite(direction.sqrMagnitude) ||
                direction.sqrMagnitude < .000001f || !float.IsFinite(neutralPitchDegrees))
            { Reset(); return; }
            worldDirection = direction.normalized;
            // The chase camera's authored downward angle is framing, not an
            // instruction to keep the hero permanently bent in a neutral walk.
            neutralPitch = ordinaryLook ? neutralPitchDegrees : 0f;
            ordinaryIntent = ordinaryLook;
            hasIntent = true;
        }

        internal void Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f || frame == null) return;
            Vector3 target = Vector3.zero;
            if (hasIntent)
            {
                Vector3 direction = frame.InverseTransformDirection(worldDirection);
                float horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
                float pitchDegrees = Mathf.Atan2(-direction.y, horizontal) * Mathf.Rad2Deg - neutralPitch;
                float yawDegrees = horizontal > .0001f ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : 0f;
                // A small lateral weight shift accompanies the residual twist.
                // Actor yaw already follows firearm aim; this never owns root yaw.
                target = ClampEnvelope(new Vector3(pitchDegrees, yawDegrees, -yawDegrees * .25f));
                if (ordinaryIntent)
                {
                    // Orbiting beside or behind him observes the hero rather
                    // than demanding that he fold around toward that camera.
                    float release = Mathf.InverseLerp(35f, 90f, Mathf.Abs(yawDegrees));
                    target *= 1f - Mathf.SmoothStep(0f, 1f, release);
                }
            }
            pitch.Advance(target.x, seconds);
            yaw.Advance(target.y, seconds);
            roll.Advance(target.z, seconds);
        }

        internal void Apply()
        {
            Restore();
            if (spine == null || chest == null || frame == null) { Forget(); return; }
            Vector3 angles = AnglesDegrees;
            if (angles.sqrMagnitude <= .00000001f) return;
            spineBase = new Pose(spine.localPosition, spine.localRotation);
            chestBase = new Pose(chest.localPosition, chest.localRotation);
            applied = true;
            // Imported local +X points toward anatomical left. World actor axes
            // avoid that source-bone convention and remain correct at every yaw.
            Quaternion bend = Quaternion.AngleAxis(angles.y, frame.up) *
                Quaternion.AngleAxis(angles.x, frame.right) * Quaternion.AngleAxis(angles.z, frame.forward);
            spine.rotation = Quaternion.Slerp(Quaternion.identity, bend, SpineShare) * spine.rotation;
            chest.rotation = Quaternion.Slerp(Quaternion.identity, bend, 1f - SpineShare) * chest.rotation;
        }

        internal void Restore()
        {
            if (!applied) return;
            if (spine != null) spine.SetLocalPositionAndRotation(spineBase.position, spineBase.rotation);
            if (chest != null) chest.SetLocalPositionAndRotation(chestBase.position, chestBase.rotation);
            applied = false;
        }

        internal void Reset()
        {
            Restore();
            ClearState();
        }

        /// <summary>Physics inherits the final visible pose, without restoring its old base.</summary>
        internal void Forget()
        {
            applied = false;
            ClearState();
        }

        private void ClearState()
        {
            pitch.Reset(); yaw.Reset(); roll.Reset();
            worldDirection = Vector3.zero;
            neutralPitch = 0f;
            hasIntent = false;
            ordinaryIntent = false;
        }

        private static Vector3 ClampEnvelope(Vector3 degrees)
        {
            degrees.x = Mathf.Clamp(degrees.x, -UpPitchDegrees, DownPitchDegrees);
            degrees.y = Mathf.Clamp(degrees.y, -MaximumYawDegrees, MaximumYawDegrees);
            degrees.z = Mathf.Clamp(degrees.z, -MaximumRollDegrees, MaximumRollDegrees);
            float pitchLimit = degrees.x >= 0f ? DownPitchDegrees : UpPitchDegrees;
            float p = degrees.x / pitchLimit, y = degrees.y / MaximumYawDegrees, r = degrees.z / MaximumRollDegrees;
            float lengthSquared = p * p + y * y + r * r;
            return lengthSquared > 1f ? degrees / Mathf.Sqrt(lengthSquared) : degrees;
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
