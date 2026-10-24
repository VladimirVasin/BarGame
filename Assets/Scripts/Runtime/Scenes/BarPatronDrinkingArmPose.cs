using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarPatronDrinkingArmPose
    {
        private AnatomicalHandFrame attentionHand;
        private Pose counterRightRestPose;
        private Quaternion counterRightClavicleRotation;
        private Vector3 counterRightElbow;
        private bool hasCounterRightRestPose;
        private float counterRightGripWeight = 1f;
        private Vector3 counterCupOfferOffset, counterCupOfferAppliedOffset;
        private bool counterCupOfferAttached;
        private bool counterCupOfferFollowing;
        private float counterCupReturnElapsed;
        public BarCounterPatronRefill CounterRefill { get; private set; }
        public float AttentionWaveWeight { get; private set; }
        public Vector3 AttentionPalmTarget { get; private set; }
        public float AttentionPalmError => AttentionWaveWeight > .999f
            ? Vector3.Distance(leftHand.position + leftHand.rotation * attentionHand.PalmCentreInHand,
                AttentionPalmTarget) : 0f;
        public bool IsHoldingCounterRightSupport => CounterRefill != null && CounterRefill.IsRefilling;
        public bool IsHoldingCounterCupOffer => IsHoldingCounterRightSupport && counterCupOfferAttached;
        public Vector3 CounterCupOfferOffset => counterCupOfferAppliedOffset;
        public Vector3 CounterRightSupportPosition => counterRightRestPose.position + counterCupOfferAppliedOffset;
        public float CounterRightSupportError => IsHoldingCounterRightSupport
            ? Vector3.Distance(rightHand.position, CounterRightSupportPosition) : 0f;

        internal float ConfigureCounterAttention(BarCounterPatronRefill refill)
        {
            // Contact frames already describe this guest's actual imported hand,
            // whereas hero generic-clip paths need not bind to the NPC model root.
            attentionHand = handPose.GetAnatomicalFrame(true);
            CounterRefill = refill;
            CacheCounterRightRestPose();
            return BarRepeatGesture.EnterSeconds;
        }

        private void CacheCounterRightRestPose()
        {
            if (CounterRefill == null || CounterRefill.IsRefilling || timeline.IsActive) return;
            counterRightRestPose = new Pose(rightHand.position, rightHand.rotation);
            counterRightClavicleRotation = rightClavicle.localRotation;
            counterRightElbow = rightForearm.position;
            hasCounterRightRestPose = true;
        }

        internal void BeginCounterRefillSupport()
        {
            // Use the previous final Rest solve, not Animator's intermediate
            // transform-write stage when the queue grants service in Update.
            if (!hasCounterRightRestPose) CacheCounterRightRestPose();
            ClearCounterCupOffer();
        }

        internal void SetCounterCupOfferOffset(Vector3 offset, bool attached = true)
        {
            counterCupOfferOffset = counterCupOfferAppliedOffset = offset;
            counterCupOfferAttached = attached;
            counterCupOfferFollowing = true;
            counterCupReturnElapsed = 0f;
        }

        internal void ReleaseCounterCupOffer()
        {
            if (!counterCupOfferAttached) return;
            counterCupOfferAttached = false;
            counterCupOfferFollowing = false;
            counterCupReturnElapsed = 0f;
        }

        internal void ClearCounterCupOffer()
        {
            counterCupOfferAttached = false;
            counterCupOfferFollowing = false;
            counterCupOfferOffset = counterCupOfferAppliedOffset = Vector3.zero;
            counterCupReturnElapsed = 0f;
        }

        private void SolveCounterRightRestSupport()
        {
            if (!counterCupOfferFollowing)
            {
                if (!GameTimeScaleRuntime.IsPaused) counterCupReturnElapsed += Mathf.Max(0f, Time.deltaTime);
                counterCupOfferAppliedOffset = counterCupOfferOffset *
                    (1f - Mathf.SmoothStep(0f, 1f, counterCupReturnElapsed / .40f));
            }
            rightClavicle.localRotation = counterRightClavicleRotation;
            Vector3 target = CounterRightSupportPosition;
            if (counterCupOfferAppliedOffset.sqrMagnitude > .000001f)
                for (int iteration = 0; iteration < SolveIterations; iteration++)
                {
                    RotateTowards(rightForearm, rightHand, target);
                    RotateTowards(rightUpperArm, rightHand, target);
                    RotateTowards(rightClavicle, rightHand, target);
                }
            LimbTwoBoneIk.Solve(rightUpperArm, rightForearm, rightHand,
                target, counterRightRestPose.rotation, counterRightElbow + counterCupOfferAppliedOffset * .5f,
                1f, .995f, true);
        }

        private float CounterRightGripWeight(bool advanceBlend)
        {
            if (CounterRefill == null) return 1f;
            float target = CounterRefill.IsRefilling && !counterCupOfferAttached ? 0f : 1f;
            if (advanceBlend && !GameTimeScaleRuntime.IsPaused)
                counterRightGripWeight = Mathf.MoveTowards(counterRightGripWeight, target,
                    Mathf.Max(0f, Time.deltaTime) / .25f);
            return counterRightGripWeight;
        }

        private void ApplyCounterAttentionWave()
        {
            AttentionWaveWeight = 0f;
            if (CounterRefill == null || !CounterRefill.IsWaitingForRefill) return;
            if (!CounterRefill.IsAttentionActive) return;
            float phase = Mathf.Clamp01(CounterRefill.AttentionElapsedSeconds / BarRepeatGesture.EnterSeconds);
            AttentionWaveWeight = Mathf.Min(Mathf.SmoothStep(0f, 1f, phase / .22f),
                Mathf.SmoothStep(0f, 1f, (1f - phase) / .25f));

            Vector3 up = ownerRoot.up;
            Vector3 right = Vector3.ProjectOnPlane(rightUpperArm.position - leftUpperArm.position, up).normalized;
            if (right.sqrMagnitude < .5f) right = ownerRoot.right;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            Vector3 shoulderMiddle = (rightUpperArm.position + leftUpperArm.position) * .5f;
            if (Vector3.Dot(forward, tableSupportPoint - shoulderMiddle) < 0f) forward = -forward;
            float reach = LimbTwoBoneIk.ChainLength(leftUpperArm, leftForearm, leftHand);
            float wave = Mathf.Sin(phase * Mathf.PI * 4f);
            // Raise beside the face, clear of both the glass and the wood. The
            // moving elbow stays on the anatomical left rather than crossing the chest.
            AttentionPalmTarget = leftUpperArm.position + up * (reach * .45f) +
                forward * (reach * .38f) - right * (reach * (.10f + wave * .10f));
            if (surfaceRenderer != null)
                AttentionPalmTarget += up * Mathf.Max(0f,
                    surfaceBounds.max.y + reach * .35f - Vector3.Dot(AttentionPalmTarget, up));
            Vector3 distal = Quaternion.AngleAxis(wave * 14f, forward) * up;
            Quaternion rotation = attentionHand.ResolveHandRotation(forward, distal);
            Vector3 wrist = attentionHand.ResolveHandPosition(AttentionPalmTarget, rotation);
            Vector3 elbow = leftUpperArm.position - right * (reach * .45f) - up * (reach * .24f) +
                forward * (reach * .08f);
            LimbTwoBoneIk.Solve(leftUpperArm, leftForearm, leftHand, wrist, rotation, elbow,
                AttentionWaveWeight, .96f, true);
            handPose?.SetGrip(true, 0f);
        }
    }
}
