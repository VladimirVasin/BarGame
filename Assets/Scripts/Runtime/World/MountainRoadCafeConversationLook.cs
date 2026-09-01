using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Late additive look for one cafe patron. The authored Idle is evaluated
    /// first; this component then shares a clamped horizontal turn between
    /// neck and head. It never runs over Drink, so the cup/mouth fit remains
    /// the authored one.
    /// </summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeConversationLook : MonoBehaviour
    {
        public const float MaximumYawDegrees = 62f;
        public const float NeckShare = 0.42f;
        public const float TurnInSeconds = 0.25f;
        public const float TurnOutSeconds = 0.42f;

        private MountainRoadCafeCastPresentation presentation;
        private Transform neck;
        private Transform head;
        private Transform mouth;
        private Transform targetHead;
        private float desiredWeight;
        private float currentWeight;

        public bool IsInitialized { get; private set; }
        public bool IsSpeaking => desiredWeight > 0.5f;
        public float CurrentWeight => currentWeight;
        public float LastAppliedYawDegrees { get; private set; }
        public Transform SpeechAnchor => head;

        public void Initialize(
            MountainRoadCafeCastPresentation configuredPresentation,
            Transform configuredTargetHead)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The cafe conversation look is already initialized.");
            }

            presentation = configuredPresentation != null
                ? configuredPresentation
                : throw new ArgumentNullException(
                    nameof(configuredPresentation));
            if (!presentation.IsInitialized ||
                (presentation.Role != MountainRoadCafeCastRole.PairMan &&
                 presentation.Role != MountainRoadCafeCastRole.PairWoman))
            {
                throw new InvalidOperationException(
                    "Only an initialized member of the cafe pair may look " +
                    "toward a conversation partner.");
            }

            targetHead = configuredTargetHead != null
                ? configuredTargetHead
                : throw new ArgumentNullException(
                    nameof(configuredTargetHead));
            neck = presentation.Registry.FindModelTransform("neck");
            head = presentation.Registry.FindModelTransform("head");
            mouth = presentation.Registry.FindModelTransform(
                "SOCKET_Mouth");
            if (neck == null || head == null || mouth == null)
            {
                throw new InvalidOperationException(
                    "The cafe pair rig requires neck, head and mouth " +
                    "transforms for conversation looks.");
            }

            desiredWeight = 0f;
            currentWeight = 0f;
            LastAppliedYawDegrees = 0f;
            IsInitialized = true;
        }

        public void SetSpeaking(bool speaking)
        {
            desiredWeight = speaking ? 1f : 0f;
        }

        /// <summary>
        /// Drink takes priority over speech. Removing the additive turn in
        /// one frame is preferable to dragging an authored cup past a mouth
        /// that has been procedurally moved sideways.
        /// </summary>
        public void CancelImmediately()
        {
            desiredWeight = 0f;
            currentWeight = 0f;
            LastAppliedYawDegrees = 0f;
        }

        public static float ResolveWeight(
            float current,
            bool speaking,
            float deltaSeconds)
        {
            if (float.IsNaN(current) || float.IsInfinity(current))
            {
                current = 0f;
            }

            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds <= 0f)
            {
                return Mathf.Clamp01(current);
            }

            float duration = speaking ? TurnInSeconds : TurnOutSeconds;
            return Mathf.MoveTowards(
                Mathf.Clamp01(current),
                speaking ? 1f : 0f,
                deltaSeconds / duration);
        }

        public static float ResolveYawDegrees(
            Vector3 currentFaceDirection,
            Vector3 targetDirection)
        {
            currentFaceDirection.y = 0f;
            targetDirection.y = 0f;
            if (currentFaceDirection.sqrMagnitude < 0.000001f ||
                targetDirection.sqrMagnitude < 0.000001f)
            {
                return 0f;
            }

            float yaw = Vector3.SignedAngle(
                currentFaceDirection.normalized,
                targetDirection.normalized,
                Vector3.up);
            return Mathf.Clamp(
                yaw,
                -MaximumYawDegrees,
                MaximumYawDegrees);
        }

        private void LateUpdate()
        {
            if (!IsInitialized || Time.deltaTime <= 0f)
            {
                return;
            }

            if (presentation.CurrentClipKind !=
                MountainRoadCafeCastClipKind.Idle)
            {
                CancelImmediately();
                return;
            }

            currentWeight = ResolveWeight(
                currentWeight,
                desiredWeight > 0.5f,
                Time.deltaTime);
            if (currentWeight <= 0.0001f)
            {
                LastAppliedYawDegrees = 0f;
                return;
            }

            Vector3 faceDirection = mouth.position - head.position;
            Vector3 targetDirection = targetHead.position - head.position;
            float eased = currentWeight * currentWeight *
                          (3f - 2f * currentWeight);
            float yaw = ResolveYawDegrees(
                faceDirection,
                targetDirection) * eased;
            Quaternion neckTurn = Quaternion.AngleAxis(
                yaw * NeckShare,
                Vector3.up);
            Quaternion headTurn = Quaternion.AngleAxis(
                yaw * (1f - NeckShare),
                Vector3.up);
            neck.rotation = neckTurn * neck.rotation;
            head.rotation = headTurn * head.rotation;
            LastAppliedYawDegrees = yaw;
        }
    }
}
