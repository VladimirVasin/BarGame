using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        // Recovery is composed after the clip, ground solve and attention.
        // A change of owner starts at the last visible pose AND velocity.
        private Transform[] recoveryBones;
        private RecoverySample[] visibleRecoveryPose;
        private RecoverySample[] previousRecoveryPose;
        private RecoverySample[] transitionRecoveryPose;
        private Vector3[] transitionLinearVelocity;
        private Vector3[] transitionAngularVelocity;
        private float visibleRecoveryDelta;
        private int visibleRecoverySamples;
        private float recoveryTransitionDuration;
        private float recoveryTransitionElapsed;
        private Player3DRagdollController recoveryPhysics;
        private float recoveryPhysicsProgress;
        private readonly Vector3[] riseHandContacts = new Vector3[2];
        private readonly Vector3[] riseHandNormals = new Vector3[2];
        private readonly bool[] riseHandContactValid = new bool[2];

        private struct RecoverySample
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 WorldPosition;
            public Quaternion WorldRotation;

            public static RecoverySample Read(Transform bone) => new RecoverySample
            {
                Position = bone.localPosition,
                Rotation = bone.localRotation,
                WorldPosition = bone.position,
                WorldRotation = bone.rotation
            };
        }

        private void EnsureRecoveryBones()
        {
            if (recoveryBones != null || registry == null) return;
            var unique = new HashSet<Transform>();
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                Transform bone = binding != null ? binding.Bone : null;
                while (bone != null && bone != registry.ModelRoot)
                {
                    unique.Add(bone);
                    bone = bone.parent;
                }
            }
            var ordered = new List<Transform>(unique);
            ordered.Sort((a, b) => RecoveryDepth(a).CompareTo(RecoveryDepth(b)));
            recoveryBones = ordered.ToArray();
            visibleRecoveryPose = new RecoverySample[recoveryBones.Length];
            previousRecoveryPose = new RecoverySample[recoveryBones.Length];
            transitionRecoveryPose = new RecoverySample[recoveryBones.Length];
            transitionLinearVelocity = new Vector3[recoveryBones.Length];
            transitionAngularVelocity = new Vector3[recoveryBones.Length];
        }

        private static int RecoveryDepth(Transform bone)
        {
            int depth = 0;
            while (bone != null) { depth++; bone = bone.parent; }
            return depth;
        }

        private void RememberRecoveryPose(float deltaTime)
        {
            EnsureRecoveryBones();
            if (recoveryBones == null || deltaTime <= 0f) return;
            for (int i = 0; i < recoveryBones.Length; i++)
            {
                previousRecoveryPose[i] = visibleRecoveryPose[i];
                visibleRecoveryPose[i] = RecoverySample.Read(recoveryBones[i]);
            }
            visibleRecoveryDelta = deltaTime;
            visibleRecoverySamples = Mathf.Min(2, visibleRecoverySamples + 1);
        }

        private static Vector3 RecoveryAngularVelocity(Quaternion from, Quaternion to, float dt)
        {
            Quaternion delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            return axis.sqrMagnitude > 0.00001f && IsFinite(axis)
                ? axis.normalized * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
        }

        internal bool TryGetPresentedBoneVelocity(Transform bone, out Vector3 linear, out Vector3 angular)
        {
            linear = angular = Vector3.zero;
            if (recoveryBones == null || visibleRecoverySamples < 2 ||
                visibleRecoveryDelta <= 0f || visibleRecoveryDelta > 0.1f) return false;
            for (int i = 0; i < recoveryBones.Length; i++)
            {
                if (recoveryBones[i] != bone) continue;
                linear = (visibleRecoveryPose[i].WorldPosition - previousRecoveryPose[i].WorldPosition) /
                         visibleRecoveryDelta;
                angular = RecoveryAngularVelocity(previousRecoveryPose[i].WorldRotation,
                    visibleRecoveryPose[i].WorldRotation, visibleRecoveryDelta);
                return IsFinite(linear) && IsFinite(angular);
            }
            return false;
        }

        internal void BeginRecoveryPoseTransition(float duration = 0.32f)
        {
            EnsureRecoveryBones();
            if (recoveryBones == null || visibleRecoverySamples == 0) return;
            recoveryTransitionDuration = Mathf.Max(0.08f, duration);
            recoveryTransitionElapsed = 0f;
            for (int i = 0; i < recoveryBones.Length; i++)
            {
                transitionRecoveryPose[i] = visibleRecoveryPose[i];
                bool moving = visibleRecoverySamples >= 2 && visibleRecoveryDelta > 0f;
                transitionLinearVelocity[i] = moving
                    ? (visibleRecoveryPose[i].Position - previousRecoveryPose[i].Position) / visibleRecoveryDelta
                    : Vector3.zero;
                transitionAngularVelocity[i] = moving
                    ? Vector3.ClampMagnitude(RecoveryAngularVelocity(previousRecoveryPose[i].Rotation,
                        visibleRecoveryPose[i].Rotation, visibleRecoveryDelta), 8f)
                    : Vector3.zero;
            }
        }

        internal void SetRecoveryPhysicsBlend(Player3DRagdollController physics, float progress)
        {
            recoveryPhysics = physics;
            recoveryPhysicsProgress = Mathf.Clamp01(progress);
        }

        private void CompleteRecoveryPresentation(float deltaTime)
        {
            if (ragdollPoseActive) return;
            if (recoveryPhysics != null && recoveryPhysics.IsRecovering)
            {
                // Blend the COMPLETE target, including its IK, once. The
                // hands cannot get ahead of a still-frozen chest.
                recoveryPhysics.ApplyRecoveryBlend(recoveryPhysicsProgress);
            }
            if (recoveryTransitionDuration <= 0f || recoveryBones == null) return;
            float elapsed = Mathf.Min(recoveryTransitionDuration, recoveryTransitionElapsed + Mathf.Max(0f, deltaTime));
            float t = Mathf.Clamp01(elapsed / recoveryTransitionDuration);
            float blend = t * t * t * (t * (t * 6f - 15f) + 10f);
            for (int i = 0; i < recoveryBones.Length; i++)
            {
                Transform bone = recoveryBones[i];
                RecoverySample source = transitionRecoveryPose[i];
                Vector3 angular = transitionAngularVelocity[i];
                Quaternion predicted = angular.sqrMagnitude > 0.000001f
                    ? Quaternion.AngleAxis(angular.magnitude * elapsed * Mathf.Rad2Deg,
                        angular.normalized) * source.Rotation : source.Rotation;
                bone.localPosition = Vector3.Lerp(source.Position +
                    transitionLinearVelocity[i] * elapsed, bone.localPosition, blend);
                bone.localRotation = Quaternion.Slerp(predicted, bone.localRotation, blend);
            }
        }

        private void AdvanceRecoveryPresentationClock(float deltaTime)
        {
            if (recoveryTransitionDuration <= 0f || deltaTime <= 0f) return;
            if (recoveryTransitionElapsed >= recoveryTransitionDuration)
            {
                recoveryTransitionDuration = 0f;
                return;
            }
            recoveryTransitionElapsed = Mathf.Min(recoveryTransitionDuration,
                recoveryTransitionElapsed + deltaTime);
        }

        private void ClearRecoveryPresentation()
        {
            recoveryPhysics = null;
            recoveryTransitionDuration = 0f;
            visibleRecoverySamples = 0;
            ResetRiseHandContacts();
        }

        internal void CancelRecoveryPoseTransition()
        {
            recoveryTransitionDuration = 0f;
            recoveryPhysics = null;
            ResetRiseHandContacts();
        }

        private void ResetRiseHandContacts()
        {
            riseHandContactValid[0] = riseHandContactValid[1] = false;
        }

        /// <summary>The same final composition as LateUpdate, with an explicit test clock.</summary>
        internal void DebugAdvanceRecoveryPresentation(float deltaTime)
        {
            if (!ragdollPoseActive)
            {
                if (!IsClipActive)
                {
                    ApplyLocomotionWeights(immediate: false);
                    EvaluateGraph(deltaTime);
                }
                ApplyLatePose(deltaTime);
                ApplyFacialPose();
                ApplyAttentionPose(deltaTime);
                CompleteRecoveryPresentation(deltaTime);
            }
            RememberRecoveryPose(deltaTime);
            AdvanceRecoveryPresentationClock(deltaTime);
        }
    }
}
