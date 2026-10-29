using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A silent, colliderless resident posed at one authored courtyard dock.
    /// The body and its Idle/Sit loops come from the ordinary Resources
    /// pedestrian catalog; this component only keeps that manual graph
    /// advancing while the resident remains outside the roaming population.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityCourtyardResidentPresentation : MonoBehaviour
    {
        public const float MaximumStepSeconds = 0.1f;

        private CityPedestrianPresentation pedestrian;
        private ArmSupport rightArm, leftArm;
        private CityCourtyardSweepingTool sweepingTool;
        private Transform supportSpine;
        private float supportSeconds;

        public bool IsInitialized { get; private set; }
        public CityCourtyardResidentDescriptor Descriptor { get; private set; }
        public CityPedestrianPresentation Pedestrian => pedestrian;
        public bool HasSupportContacts => rightArm != null && leftArm != null;
        public bool HasSweepingTool => sweepingTool != null;
        public float MaximumSupportContactError { get; private set; }

        private sealed class ArmSupport
        {
            public Transform Upper, Lower, Hand, Grip;
            public SeatedArmHandAttachment Attachment;
        }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            CityPedestrianArchetype archetype,
            CityCourtyardResidentDescriptor descriptor)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The courtyard resident presentation is already " +
                    "initialized.");
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (archetype == null)
            {
                throw new ArgumentNullException(nameof(archetype));
            }

            if (!string.Equals(
                    registry.DesignId,
                    descriptor.DesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    archetype.DesignId,
                    descriptor.DesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The courtyard resident descriptor, archetype and " +
                    "prefab must name the same generic design.");
            }

            if (descriptor.IsSeated && archetype.SeatedRide == null)
            {
                throw new InvalidOperationException(
                    $"Generic design '{archetype.DesignId}' has no seated " +
                    "ride declaration.");
            }

            Descriptor = descriptor;
            transform.SetPositionAndRotation(
                descriptor.Position,
                Quaternion.LookRotation(descriptor.Facing, Vector3.up));
            registry.ApplyPaletteVariant(descriptor.PaletteVariant);
            if (string.Equals(
                    descriptor.DesignId,
                    CityPedestrianResources.BabushkaDesignId,
                    StringComparison.Ordinal))
            {
                // The drying yard already stands three of this body. The
                // cast stays - her smoking loop is what makes a woman
                // commenting on somebody's bicycle repair read at all -
                // but she is a fourth woman, not a fourth copy.
                registry.ApplyOutfit(CityBabushkaOutfits.CourtyardBicycle);
            }

            pedestrian = registry.GetComponent<CityPedestrianPresentation>();
            if (pedestrian == null)
            {
                pedestrian = registry.gameObject.AddComponent<
                    CityPedestrianPresentation>();
            }

            // The whole of the courtyard's "active idle": these bodies are
            // POSED where they belong, so they play the loop they were
            // authored for rather than the pavement breath every anonymous
            // walker shares. See CityPedestrianClipSource.
            pedestrian.Initialize(
                registry,
                CityPedestrianClipSource.Placed);
            pedestrian.ConfigureCycle(
                Mathf.Lerp(
                    archetype.MinimumAnimationSpeed,
                    archetype.MaximumAnimationSpeed,
                    0.5f),
                descriptor.AnimationPhase01);

            if (descriptor.IsSeated)
            {
                Transform seat = new GameObject("Seat Pose Anchor").transform;
                seat.SetParent(transform, true);
                seat.SetPositionAndRotation(
                    descriptor.SeatAnchorPosition,
                    transform.rotation);
                if (!pedestrian.TrySeat(seat, archetype.SeatedRide))
                {
                    DestroyObject(seat.gameObject);
                    pedestrian.Shutdown();
                    pedestrian = null;
                    throw new InvalidOperationException(
                        $"Generic design '{archetype.DesignId}' could not " +
                        "enter its declared seated pose.");
                }
            }

            // Apply the selected loop and stable phase before the first
            // rendered frame. Advance is bounded in LateUpdate afterwards.
            pedestrian.Advance(
                descriptor.IsSeated
                    ? descriptor.AnimationPhase01 * 0.83f
                    : 0f,
                false,
                true);
            if (descriptor.Activity == CityCourtyardResidentActivity.NardiPlayer ||
                descriptor.Activity == CityCourtyardResidentActivity.ChairRepair ||
                descriptor.Activity == CityCourtyardResidentActivity.Sweeping)
            {
                rightArm = CaptureArm(registry.ModelRoot, ".R");
                leftArm = CaptureArm(registry.ModelRoot, ".L");
                supportSpine = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "spine");
                ApplySupportContacts();
            }
            IsInitialized = true;
        }

        public void Shutdown()
        {
            if (!IsInitialized && pedestrian == null)
            {
                return;
            }

            if (pedestrian != null)
            {
                pedestrian.Shutdown();
            }

            pedestrian = null;
            if (sweepingTool != null) sweepingTool.RestoreRestPose();
            sweepingTool = null;
            rightArm = null;
            leftArm = null;
            supportSpine = null;
            IsInitialized = false;
        }

        private void LateUpdate()
        {
            if (!IsInitialized || pedestrian == null)
            {
                return;
            }

            pedestrian.Advance(
                Mathf.Min(Time.deltaTime, MaximumStepSeconds),
                false,
                true);
            supportSeconds += Time.deltaTime;
            ApplySupportContacts();
        }

        private static ArmSupport CaptureArm(Transform root, string side)
        {
            Transform Bone(string name) => CityPedestrianHandProps.FindSocket(root, name + side);
            var arm = new ArmSupport { Upper = Bone("upper_arm"), Lower = Bone("forearm"),
                Hand = Bone("hand"), Grip = Bone("SOCKET_Grip") };
            if (arm.Upper == null || arm.Lower == null || arm.Hand == null || arm.Grip == null)
                throw new InvalidOperationException("Courtyard resident lacks an authored support-contact arm.");
            arm.Attachment = new SeatedArmHandAttachment(arm.Hand, arm.Grip);
            return arm;
        }

        private void ApplySupportContacts()
        {
            if (!HasSupportContacts) return;
            Vector3 right, left;
            if (Descriptor.Activity == CityCourtyardResidentActivity.NardiPlayer)
            {
                bool second = Descriptor.StableId.EndsWith("nardi-b", StringComparison.Ordinal);
                float side = second ? 1f : -1f;
                // Both palms rest on the near timber edge. The baked counters
                // remain where they are; a turn never mimics moving a piece.
                right = Pocket(new Vector3(side * .61f - .11f, .775f, .09f));
                left = Pocket(new Vector3(side * .61f + .11f, .775f, .09f));
            }
            else if (Descriptor.Activity == CityCourtyardResidentActivity.ChairRepair)
            {
                // Inspect the actual upper rail: one hand steadies the joint
                // while the other makes a short, slow checking pass along it.
                float check = .025f * Mathf.Sin(supportSeconds * .8f);
                right = Pocket(new Vector3(-.82f + check, 1.19f, -.265f));
                left = Pocket(new Vector3(-.54f, 1.19f, -.265f));
            }
            else if (Descriptor.Activity == CityCourtyardResidentActivity.Sweeping)
            {
                if (sweepingTool == null && !CityCourtyardSweepingTool.TryGet(
                    Descriptor.SourceStableId, out sweepingTool))
                { MaximumSupportContactError = float.PositiveInfinity; return; }
                sweepingTool.ApplySweep(Mathf.Repeat(supportSeconds / 5.6f + Descriptor.AnimationPhase01, 1f));
                right = sweepingTool.RightGrip.position;
                left = sweepingTool.LeftGrip.position;
            }
            else return;
            Quaternion upright = supportSpine != null ? supportSpine.rotation : Quaternion.identity;
            Vector3 toSupport = (right + left) * .5f - transform.position;
            toSupport.y = 0f;
            Vector3 leanAxis = Vector3.Cross(Vector3.up, toSupport.normalized);
            for (int attempt = 0; attempt <= 12; attempt++)
            {
                if (supportSpine != null)
                    supportSpine.rotation = Quaternion.AngleAxis(attempt * 4f, leanAxis) * upright;
                Vector3 anatomicalRight = Vector3.ProjectOnPlane(
                    rightArm.Upper.position - leftArm.Upper.position, Vector3.up).normalized;
                if (Descriptor.Activity != CityCourtyardResidentActivity.Sweeping &&
                    Vector3.Dot(right - left, anatomicalRight) < 0f)
                { Vector3 temporary = right; right = left; left = temporary; }
                SolveSupport(rightArm, right, anatomicalRight);
                SolveSupport(leftArm, left, -anatomicalRight);
                MaximumSupportContactError = Mathf.Max(Vector3.Distance(rightArm.Grip.position, right),
                    Vector3.Distance(leftArm.Grip.position, left));
                if (MaximumSupportContactError <= .025f || supportSpine == null) break;
            }
        }

        private Vector3 Pocket(Vector3 local) => Descriptor.PocketOrigin + Descriptor.PocketRotation * local;

        private void SolveSupport(ArmSupport arm, Vector3 socketTarget, Vector3 elbowSide)
        {
            Quaternion handRotation = arm.Hand.rotation;
            Vector3 wrist = socketTarget - handRotation * arm.Attachment.SocketPositionInHand;
            Vector3 hint = arm.Upper.position + elbowSide * .24f - Vector3.up * .20f + transform.forward * .08f;
            LimbTwoBoneIk.Solve(arm.Upper, arm.Lower, arm.Hand, wrist, handRotation, hint);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
