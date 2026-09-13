using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Three authored locks: inertial particles, damped bend springs, fixed lengths and own-body contacts.</summary>
    [DisallowMultipleComponent]
    public sealed partial class CanneryWomanHair : MonoBehaviour
    {
        public const int ChainCount = 3, PointsPerChain = 4, PointCount = ChainCount * PointsPerChain;
        public const float RootBendLimitDegrees = 20f, MiddleBendLimitDegrees = 30f, TipBendLimitDegrees = 40f;
        public const double MaximumContinuousStep = .25d;
        private const float StepSeconds = 1f / 120f;
        private static readonly float[] BendCosines = { Mathf.Cos(RootBendLimitDegrees * Mathf.Deg2Rad),
            Mathf.Cos(MiddleBendLimitDegrees * Mathf.Deg2Rad), Mathf.Cos(TipBendLimitDegrees * Mathf.Deg2Rad) };
        private static readonly float[] BendSines = { Mathf.Sin(RootBendLimitDegrees * Mathf.Deg2Rad),
            Mathf.Sin(MiddleBendLimitDegrees * Mathf.Deg2Rad), Mathf.Sin(TipBendLimitDegrees * Mathf.Deg2Rad) };
        private static readonly string[] ChainNames = { "HairBack", "HairLeft", "HairRight" };
        [SerializeField] private Transform head;
        [SerializeField] private Transform[] joints = Array.Empty<Transform>();
        [SerializeField] private Vector3[] bindPositions = Array.Empty<Vector3>(), headLocalRest = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] bindRotations = Array.Empty<Quaternion>();
        [SerializeField] private Quaternion headBindLocalRotation;
        [SerializeField] private Vector3[] hangingOffsets = Array.Empty<Vector3>();
        [SerializeField] private BodyProxy[] body = Array.Empty<BodyProxy>();
        private Vector3[] points, velocities, targets, previousTargets, substepPrevious;
        private float[] lengths;
        private bool initialized, resetPending = true;
        private double previousSeconds;
        private Vector3 previousHead;
        private Vector3 contactTransportVelocity;
        private readonly Vector3[] headRestDirections = new Vector3[ChainCount];
        private Matrix4x4[] bodyToWorld, worldToBody;

        [Serializable] private struct BodyPlane { public Vector3 Normal; public float Distance; }
        [Serializable] private struct BodySample
        {
            public Vector3 A, B, C, D;
            public Vector4 Weights;
            public int BoneA, BoneB, BoneC, BoneD;
        }
        [Serializable] private struct BodyProxy
        {
            public string Name;
            public Transform Bone;
            public Matrix4x4 UnitToBone;
            public Transform[] SampleBones;
            public BodySample[] Samples;
            public BodyPlane[] Planes;
            public bool JoinedSection;
        }
        public bool IsInitialized => initialized;
        public bool HasAuthoredBindings => head != null && joints.Length == PointCount &&
            bindPositions.Length == PointCount && bindRotations.Length == PointCount && headLocalRest.Length == PointCount &&
            hangingOffsets.Length == PointCount && body.Length == 13 && HasSurfaceBindings;
        public int ResetCount { get; private set; }
        public int LastContactCount { get; private set; }
        public float MaximumDisplacement { get; private set; }
        public float MaximumLengthError { get; private set; }
        public float LastStepSeconds { get; private set; }
        public float PeakIntegratedDisplacement { get; private set; }
        public float PeakConstrainedDisplacement { get; private set; }
        public float PeakBoneCorrectionDegrees { get; private set; }
        public float PeakTargetStep { get; private set; }
        public float MaximumBendLimitExcessDegrees
        {
            get
            {
                float excess = 0f;
                if (!initialized) return excess;
                for (int chain = 0; chain < ChainCount; chain++)
                for (int segment = 0; segment < 3; segment++)
                    excess = Mathf.Max(excess, BendDegrees(chain, segment) - BendLimit(segment));
                return excess;
            }
        }
        public float BendDegrees(int chain, int segment)
        {
            int index = chain * PointsPerChain + segment;
            Vector3 reference = segment == 0 ? headRestDirections[chain] : joints[index].position - joints[index - 1].position;
            return Vector3.Angle(reference, joints[index + 1].position - joints[index].position);
        }
        public float MaximumSpeed
        {
            get
            {
                float speed = 0f;
                if (velocities != null)
                    for (int i = 0; i < velocities.Length; i++) speed = Mathf.Max(speed, velocities[i].magnitude);
                return speed;
            }
        }
        public Vector3 WorldPoint(int index) => points[index];
        public Vector3 RestWorldPoint(int index) => targets[index];

        /// <summary>Capture the imported bind transforms once, including their FBX unit factors.</summary>
        public void Configure(VillageResidentPresentation motion)
        {
            head = motion.Head;
            joints = new Transform[PointCount]; bindPositions = new Vector3[PointCount];
            bindRotations = new Quaternion[PointCount]; headLocalRest = new Vector3[PointCount];
            hangingOffsets = new Vector3[PointCount];
            headBindLocalRotation = Quaternion.Inverse(transform.rotation) * head.rotation;
            for (int chain = 0; chain < ChainCount; chain++)
            for (int point = 0; point < PointsPerChain; point++)
            {
                int index = chain * PointsPerChain + point;
                string name = ChainNames[chain] + (point == 3 ? ".Tip" : "." + point.ToString("D2"));
                Transform joint = CityPedestrianHandProps.FindSocket(motion.ModelRoot, name)
                    ?? throw new InvalidOperationException("Missing authored hair joint " + name);
                joints[index] = joint;
                bindPositions[index] = joint.localPosition; bindRotations[index] = joint.localRotation;
                headLocalRest[index] = head.InverseTransformPoint(joint.position);
                hangingOffsets[index] = transform.InverseTransformVector(joint.position - joints[chain * PointsPerChain].position);
            }
            ConfigureMeasuredBody(motion);
            ConfigureSurfaceBindings(motion);
            resetPending = true; initialized = false;
        }

        public void ApplyAt(double seconds, bool paused, bool outside, WindSample wind)
        {
            if (head == null || joints.Length != PointCount) return;
            EnsureState();
            RestoreBindPose();
            // Only attachment points inherit the complete head tilt. Free
            // length hangs in gravity; pitching a working head must not hold
            // a rigid fan behind it. Yaw still carries the authored layering.
            Vector3 up = Physics.gravity.sqrMagnitude > .000001f ? -Physics.gravity.normalized : Vector3.up;
            Quaternion relative = head.rotation * Quaternion.Inverse(transform.rotation * headBindLocalRotation);
            Vector3 twistVector = up * Vector3.Dot(new Vector3(relative.x, relative.y, relative.z), up);
            Quaternion twist = new Quaternion(twistVector.x, twistVector.y, twistVector.z, relative.w);
            float twistLength = Mathf.Sqrt(twistVector.sqrMagnitude + relative.w * relative.w);
            twist = twistLength > .00001f
                ? new Quaternion(twist.x / twistLength, twist.y / twistLength, twist.z / twistLength, twist.w / twistLength)
                : Quaternion.identity;
            // Swing/twist remains continuous when looking beyond vertical;
            // projecting the forward vector would flip the layering by 180°.
            Quaternion hangingFrame = twist * transform.rotation;
            for (int chain = 0; chain < ChainCount; chain++)
            {
                int root = chain * PointsPerChain;
                Vector3 anchor = head.TransformPoint(headLocalRest[root]);
                headRestDirections[chain] = (joints[root + 1].position - joints[root].position).normalized;
                targets[root] = anchor;
                for (int point = 1; point < PointsPerChain; point++)
                {
                    int i = root + point;
                    Vector3 gravityDirection = hangingFrame * Vector3.Scale(hangingOffsets[i] - hangingOffsets[i - 1], transform.lossyScale);
                    Vector3 reference = point == 1 ? headRestDirections[chain] : targets[i - 1] - targets[i - 2];
                    // The scalp attachment retains its authored direction.
                    // Gravity is reached gradually through the free links,
                    // never by cutting straight through the tilted skull.
                    targets[i] = targets[i - 1] + ClampBend(gravityDirection, reference, point - 1) * gravityDirection.magnitude;
                }
            }
            UpdateBody();
            if (paused && initialized && !resetPending) { LastStepSeconds = 0f; ApplyBones(false); return; }
            double elapsed = seconds - previousSeconds;
            if (resetPending || !initialized || !IsFinite(seconds) || elapsed < 0d || elapsed > MaximumContinuousStep ||
                Vector3.Distance(previousHead, head.position) > .55f)
            {
                ResetToCurrentPose(seconds);
                ApplyBones();
                return;
            }
            LastStepSeconds = (float)elapsed;
            contactTransportVelocity = elapsed > 0d
                ? Vector3.ClampMagnitude((head.position - previousHead) / (float)elapsed, 2.5f) : Vector3.zero;
            LastContactCount = 0;
            for (int i = 0; i < PointCount; i++)
                PeakTargetStep = Mathf.Max(PeakTargetStep, Vector3.Distance(previousTargets[i], targets[i]));
            if (elapsed > 0d)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt((float)elapsed / StepSeconds));
                float dt = (float)elapsed / steps;
                Vector3 air = outside ? wind.Velocity(1.25f) : Vector3.zero;
                for (int step = 0; step < steps; step++)
                {
                    float alpha = (step + 1f) / steps;
                    for (int i = 0; i < PointCount; i++) substepPrevious[i] = points[i];
                    for (int chain = 0; chain < ChainCount; chain++)
                    {
                        int root = chain * PointsPerChain;
                        points[root] = Vector3.Lerp(previousTargets[root], targets[root], alpha);
                        velocities[root] = Vector3.zero;
                        for (int point = 1; point < PointsPerChain; point++)
                        {
                            int i = root + point;
                            Vector3 rest = Vector3.Lerp(previousTargets[i], targets[i], alpha);
                            float stiffness = Mathf.Lerp(78f, 34f, point / 3f);
                            Vector3 acceleration = (rest - points[i]) * stiffness + Physics.gravity * .35f +
                                (air - velocities[i]) * 3f - velocities[i] * 3.5f;
                            velocities[i] = Vector3.ClampMagnitude(velocities[i] + acceleration * dt, 2.5f);
                            points[i] += velocities[i] * dt;
                            PeakIntegratedDisplacement = Mathf.Max(PeakIntegratedDisplacement, Vector3.Distance(points[i], rest));
                        }
                        ConstrainChain(root);
                        for (int point = 1; point < PointsPerChain; point++)
                        {
                            int i = root + point;
                            PeakConstrainedDisplacement = Mathf.Max(PeakConstrainedDisplacement,
                                Vector3.Distance(points[i], Vector3.Lerp(previousTargets[i], targets[i], alpha)));
                            velocities[i] = Vector3.ClampMagnitude((points[i] - substepPrevious[i]) / dt, 2.5f);
                        }
                    }
                }
            }
            for (int i = 0; i < PointCount; i++) previousTargets[i] = targets[i];
            previousSeconds = seconds; previousHead = head.position;
            ApplyBones();
        }

        private void EnsureState()
        {
            if (points != null) return;
            points = new Vector3[PointCount]; velocities = new Vector3[PointCount]; targets = new Vector3[PointCount];
            previousTargets = new Vector3[PointCount]; substepPrevious = new Vector3[PointCount]; lengths = new float[PointCount];
        }

        private void ResetToCurrentPose(double seconds)
        {
            for (int i = 0; i < PointCount; i++)
            {
                points[i] = targets[i]; previousTargets[i] = targets[i]; velocities[i] = Vector3.zero;
                lengths[i] = i % PointsPerChain == 0 ? 0f : Vector3.Distance(targets[i], targets[i - 1]);
            }
            previousSeconds = IsFinite(seconds) ? seconds : 0d; previousHead = head.position;
            initialized = true; resetPending = false; ResetCount++;
            LastStepSeconds = 0f; LastContactCount = 0;
            contactTransportVelocity = Vector3.zero;
            PeakIntegratedDisplacement = PeakConstrainedDisplacement = PeakBoneCorrectionDegrees = PeakTargetStep = 0f;
            for (int chain = 0; chain < ChainCount; chain++) ConstrainChain(chain * PointsPerChain);
        }

        private void ConstrainChain(int root)
        {
            for (int iteration = 0; iteration < 8; iteration++)
            for (int point = 1; point < PointsPerChain; point++)
            {
                int i = root + point;
                Vector3 direction = points[i] - points[i - 1];
                Vector3 reference = point == 1 ? headRestDirections[root / PointsPerChain] : points[i - 1] - points[i - 2];
                direction = ClampBend(direction, reference, point - 1);
                points[i] = ResolveBody(points[i - 1] + direction * lengths[i]);
            }
        }

        private static float BendLimit(int segment) => segment == 0 ? RootBendLimitDegrees :
            segment == 1 ? MiddleBendLimitDegrees : TipBendLimitDegrees;

        private static Vector3 ClampBend(Vector3 direction, Vector3 reference, int segment)
        {
            Vector3 rest = reference.normalized;
            Vector3 unit = direction.sqrMagnitude > .000001f ? direction.normalized : rest;
            float cosine = Vector3.Dot(rest, unit);
            if (cosine >= BendCosines[segment]) return unit;
            Vector3 bend = unit - rest * cosine;
            if (bend.sqrMagnitude < .000001f)
                bend = Vector3.Cross(rest, Mathf.Abs(rest.y) < .9f ? Vector3.up : Vector3.right);
            return rest * BendCosines[segment] + bend.normalized * BendSines[segment];
        }

        private void RestoreBindPose()
        {
            surfacePoseDirty = contactDiagnosticsDirty = true;
            for (int i = 0; i < joints.Length; i++)
            {
                joints[i].localPosition = bindPositions[i];
                joints[i].localRotation = bindRotations[i];
            }
        }

        private void ApplyBones(bool resolveSurface = true)
        {
            MaximumDisplacement = MaximumLengthError = 0f;
            AimBonesAtPoints();
            if (resolveSurface) SolveSurfaceContacts();
            for (int chain = 0; chain < ChainCount; chain++)
            {
                int root = chain * PointsPerChain;
                for (int point = 0; point < PointsPerChain; point++)
                {
                    int i = root + point;
                    points[i] = joints[i].position;
                    MaximumDisplacement = Mathf.Max(MaximumDisplacement, Vector3.Distance(points[i], targets[i]));
                    if (point != 0) MaximumLengthError = Mathf.Max(MaximumLengthError,
                        Mathf.Abs(Vector3.Distance(points[i], points[i - 1]) - lengths[i]));
                }
            }
            // Collision diagnostics are an on-demand oracle. Gameplay already
            // resolved these surfaces; reading a public diagnostic property
            // refreshes their exact final-pose measurements once, when needed.
            contactDiagnosticsDirty = true;
        }

        private void AimBonesAtPoints(bool resolveJointContacts = true)
        {
            for (int chain = 0; chain < ChainCount; chain++)
            {
                int root = chain * PointsPerChain;
                for (int point = 0; point < PointsPerChain - 1; point++)
                {
                    int i = root + point;
                    Vector3 original = joints[i + 1].position - joints[i].position;
                    // Contact projection can move a solver endpoint slightly
                    // off its length sphere. Aim from the actual parent after
                    // applying the previous bone, not that approximate point.
                    Vector3 desired = points[i + 1] - joints[i].position;
                    Vector3 reference = point == 0 ? headRestDirections[chain] : joints[i].position - joints[i - 1].position;
                    desired = ClampBend(desired, reference, point);
                    PeakBoneCorrectionDegrees = Mathf.Max(PeakBoneCorrectionDegrees, Vector3.Angle(original, desired));
                    if (original.sqrMagnitude > .000001f && desired.sqrMagnitude > .000001f)
                        // Solve direction only. Supplying short metre vectors
                        // needlessly makes native near-parallel tolerances
                        // depend on the length of the authored hair segment.
                        joints[i].rotation = Quaternion.FromToRotation(original.normalized, desired.normalized) * joints[i].rotation;
                    // Normalizing onto the real bone's fixed radius can undo
                    // the last contact projection. Close that small residual on
                    // the visible joint itself; children follow the same solve.
                    for (int contact = 0; resolveJointContacts && contact < 6 && IsInsideBody(joints[i + 1].position, .002f); contact++)
                    {
                        Vector3 target = ResolveBody(joints[i + 1].position);
                        original = joints[i + 1].position - joints[i].position;
                        desired = ClampBend(target - joints[i].position, reference, point);
                        if (desired.sqrMagnitude < .000001f) break;
                        joints[i].rotation = Quaternion.FromToRotation(original.normalized, desired.normalized) * joints[i].rotation;
                    }
                }
            }
        }

        private void UpdateBody()
        {
            EnsureBodyState();
            for (int i = 0; i < body.Length; i++)
            {
                bodyToWorld[i] = body[i].Bone.localToWorldMatrix * body[i].UnitToBone;
                worldToBody[i] = bodyToWorld[i].inverse;
            }
            UpdateBodyPlanes();
            UpdateSurfaceBody();
        }

        private Vector3 ResolveBody(Vector3 point)
        {
            for (int i = 0; i < body.Length; i++)
            {
                Vector3 next = ResolveBodyVolume(point, i, .0002f);
                if ((next - point).sqrMagnitude > .00000001f) LastContactCount++;
                point = next;
            }
            return point;
        }

        public bool IsInsideBody(Vector3 point, float tolerance = .01f)
        {
            for (int i = 0; i < body.Length; i++)
                if (InsideBodyVolume(point, i, -bodyMinimumRadii[i] * Mathf.Clamp01(tolerance))) return true;
            return false;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void OnEnable() => resetPending = true;
        private void OnDisable() { resetPending = true; initialized = false; }
    }
}
