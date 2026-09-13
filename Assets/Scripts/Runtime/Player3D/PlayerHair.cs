using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored parted curtains with bounded inertia and own-body contacts.</summary>
    // Final body/scarf pose is ready at 410; the mirror copies this pose at 420.
    [DefaultExecutionOrder(415)]
    [DisallowMultipleComponent]
    public sealed class PlayerHair : MonoBehaviour
    {
        public const int ChainCount = 3;
        public const int PointsPerChain = 4;
        public const int PointCount = ChainCount * PointsPerChain;
        public const double MaximumContinuousStep = .25d;
        public const float RootBendLimitDegrees = 25f;
        public const float MiddleBendLimitDegrees = 35f;
        public const float TipBendLimitDegrees = 45f;
        private const float StepSeconds = 1f / 120f;

        [SerializeField] private Player3DAssetRegistry registry;
        [SerializeField] private Transform head;
        [SerializeField] private Transform[] joints = Array.Empty<Transform>();
        [SerializeField] private Vector3[] bindPositions = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] bindRotations = Array.Empty<Quaternion>();
        [SerializeField] private Vector3[] headLocalRest = Array.Empty<Vector3>();
        [SerializeField] private Vector3[] hangingOffsets = Array.Empty<Vector3>();
        [SerializeField] private Quaternion headBindRotation;

        private readonly Vector3[] points = new Vector3[PointCount];
        private readonly Vector3[] velocities = new Vector3[PointCount];
        private readonly Vector3[] targets = new Vector3[PointCount];
        private readonly Vector3[] previousTargets = new Vector3[PointCount];
        private readonly Vector3[] beforeStep = new Vector3[PointCount];
        private readonly Vector3[] rootDirections = new Vector3[ChainCount];
        private readonly float[] lengths = new float[PointCount];
        private readonly Vector3[] solvedLocalPositions = new Vector3[PointCount];
        private readonly Quaternion[] solvedLocalRotations = new Quaternion[PointCount];
        private PlayerHairContacts contacts;
        private PlayerScarfPresentation scarf;
        private PlayerSecondaryMotionEnvironment environment;
        private Vector3 previousHead;
        private Quaternion previousHeadRotation;
        private double previousSeconds, simulationSeconds;
        private bool driven, initialized, resetPending = true;

        public bool HasAuthoredBindings => registry != null && head != null && joints.Length == PointCount &&
            bindPositions.Length == PointCount && bindRotations.Length == PointCount &&
            headLocalRest.Length == PointCount && hangingOffsets.Length == PointCount && AllJointsPresent();
        public bool IsInitialized => initialized;
        public bool IsRuntimeDriven => driven;
        public int JointCount => joints.Length;
        public int ResetCount { get; private set; }
        public int LastContactCount { get; private set; }
        public float LastStepSeconds { get; private set; }
        public float MaximumDisplacement { get; private set; }
        public float MaximumLengthError { get; private set; }
        public float MaximumSpeed
        {
            get
            {
                float maximum = 0f;
                foreach (Vector3 velocity in velocities) maximum = Mathf.Max(maximum, velocity.magnitude);
                return maximum;
            }
        }
        public Vector3 WorldPoint(int index) => points[index];
        public Vector3 RestWorldPoint(int index) => targets[index];
        internal Vector3 AuthoredWorldPoint(int index) => head.TransformPoint(headLocalRest[index]);

        /// <summary>Editor binds ordered Back/Left/Right .00/.01/.02/.Tip chains in the imported rest pose.</summary>
        public void Configure(Player3DAssetRegistry owner, Transform[] orderedJoints)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (orderedJoints == null || orderedJoints.Length != PointCount)
                throw new ArgumentException("Hero hair requires three four-point chains.", nameof(orderedJoints));
            registry = owner;
            head = registry.Anchors.Head;
            if (head == null) throw new InvalidOperationException("Hero hair requires the registered head.");
            joints = (Transform[])orderedJoints.Clone();
            bindPositions = new Vector3[PointCount]; bindRotations = new Quaternion[PointCount];
            headLocalRest = new Vector3[PointCount]; hangingOffsets = new Vector3[PointCount];
            headBindRotation = Quaternion.Inverse(transform.rotation) * head.rotation;
            for (int i = 0; i < PointCount; i++)
            {
                Transform joint = joints[i];
                if (joint == null) throw new ArgumentException("Hero hair has a missing joint.", nameof(orderedJoints));
                Transform parent = i % PointsPerChain == 0 ? head : joints[i - 1];
                if (joint.parent != parent)
                    throw new ArgumentException("Hero hair must preserve its authored chain hierarchy.", nameof(orderedJoints));
                bindPositions[i] = joint.localPosition; bindRotations[i] = joint.localRotation;
                headLocalRest[i] = head.InverseTransformPoint(joint.position);
                hangingOffsets[i] = transform.InverseTransformVector(joint.position - joints[i / PointsPerChain * PointsPerChain].position);
            }
            contacts = null;
            initialized = false;
            RequestReset();
        }

        /// <summary>Only the actual player drives a simulation. Prefab-derived subsets and mirrors stay passive.</summary>
        public void BindRuntime(PlayerRuntime owner)
        {
            if (!HasAuthoredBindings) throw new InvalidOperationException("The production hero has no authored hair bindings.");
            environment = new PlayerSecondaryMotionEnvironment(owner);
            scarf = registry.GetComponent<PlayerScarfPresentation>();
            contacts = new PlayerHairContacts(registry, scarf);
            driven = true;
            RequestReset();
        }

        private void LateUpdate()
        {
            if (!driven) return;
            bool paused = GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning;
            if (!paused) simulationSeconds += Time.deltaTime;
            environment.Sample(out bool outside, out WindSample wind);
            ApplyAt(simulationSeconds, paused, outside, wind);
        }

        public void RequestReset() => resetPending = true;

        public void ApplyAt(double seconds, bool paused, bool outside, WindSample wind)
        {
            if (!HasAuthoredBindings) return;
            // A paused actor keeps both its solved transforms and particle history unchanged.
            if (paused && initialized && !resetPending)
            {
                // Animator may have written bind rotations again while paused.
                for (int i = 0; i < PointCount; i++)
                { joints[i].localPosition = solvedLocalPositions[i]; joints[i].localRotation = solvedLocalRotations[i]; }
                previousSeconds = IsFinite(seconds) ? seconds : previousSeconds;
                LastStepSeconds = 0f;
                return;
            }
            RestoreBindPose();
            BuildTargets();
            if (contacts == null) contacts = new PlayerHairContacts(registry, scarf);
            contacts.UpdatePose();
            double elapsed = seconds - previousSeconds;
            bool teleported = initialized && ((previousHead - head.position).sqrMagnitude > .3025f ||
                Quaternion.Angle(previousHeadRotation, head.rotation) > 100f);
            if (resetPending || !initialized || !IsFinite(seconds) || elapsed < 0d ||
                elapsed > MaximumContinuousStep || teleported)
            {
                for (int i = 0; i < PointCount; i++)
                {
                    points[i] = targets[i]; previousTargets[i] = targets[i]; velocities[i] = Vector3.zero;
                    lengths[i] = i % PointsPerChain == 0 ? 0f : Vector3.Distance(joints[i - 1].position, joints[i].position);
                }
                resetPending = false; initialized = true; ResetCount++; LastStepSeconds = 0f;
                LastContactCount = 0;
                Constrain(1f);
            }
            else
            {
                LastStepSeconds = (float)elapsed;
                LastContactCount = 0;
                int steps = elapsed > 0d ? Mathf.Max(1, Mathf.CeilToInt((float)elapsed / StepSeconds)) : 0;
                float dt = steps > 0 ? (float)elapsed / steps : 0f;
                Vector3 air = outside ? wind.Velocity(1.25f) : Vector3.zero;
                for (int step = 0; step < steps; step++)
                {
                    float alpha = (step + 1f) / steps;
                    for (int i = 0; i < PointCount; i++)
                    {
                        beforeStep[i] = points[i];
                        if (i % PointsPerChain == 0) continue;
                        float freedom = i % PointsPerChain / 3f;
                        Vector3 rest = Vector3.Lerp(previousTargets[i], targets[i], alpha);
                        Vector3 acceleration = (rest - points[i]) * Mathf.Lerp(94f, 48f, freedom) +
                            Physics.gravity * .3f + (air - velocities[i]) * 2.4f - velocities[i] * 5f;
                        velocities[i] = Vector3.ClampMagnitude(velocities[i] + acceleration * dt, 2f);
                        points[i] += velocities[i] * dt;
                    }
                    Constrain(alpha);
                    for (int i = 0; i < PointCount; i++)
                        velocities[i] = i % PointsPerChain == 0 ? Vector3.zero :
                            Vector3.ClampMagnitude((points[i] - beforeStep[i]) / dt, 2f);
                }
            }
            ApplyBones();
            for (int i = 0; i < PointCount; i++)
            { solvedLocalPositions[i] = joints[i].localPosition; solvedLocalRotations[i] = joints[i].localRotation; }
            for (int i = 0; i < PointCount; i++) previousTargets[i] = targets[i];
            previousSeconds = IsFinite(seconds) ? seconds : 0d;
            previousHead = head.position; previousHeadRotation = head.rotation;
        }

        private void BuildTargets()
        {
            // Swing/twist keeps free lengths gravity-oriented without a yaw flip when the hero falls face-down.
            Vector3 up = Physics.gravity.sqrMagnitude > .000001f ? -Physics.gravity.normalized : Vector3.up;
            Quaternion relative = head.rotation * Quaternion.Inverse(transform.rotation * headBindRotation);
            Vector3 vector = up * Vector3.Dot(new Vector3(relative.x, relative.y, relative.z), up);
            float magnitude = Mathf.Sqrt(vector.sqrMagnitude + relative.w * relative.w);
            Quaternion twist = magnitude > .00001f
                ? new Quaternion(vector.x / magnitude, vector.y / magnitude, vector.z / magnitude, relative.w / magnitude)
                : Quaternion.identity;
            for (int chain = 0; chain < ChainCount; chain++)
            {
                int root = chain * PointsPerChain;
                targets[root] = head.TransformPoint(headLocalRest[root]);
                rootDirections[chain] = (joints[root + 1].position - joints[root].position).normalized;
                for (int point = 1; point < PointsPerChain; point++)
                {
                    int i = root + point;
                    Vector3 direction = twist * transform.TransformVector(hangingOffsets[i] - hangingOffsets[i - 1]);
                    Vector3 reference = point == 1 ? rootDirections[chain] : targets[i - 1] - targets[i - 2];
                    targets[i] = targets[i - 1] + ClampBend(direction, reference, point - 1) * direction.magnitude;
                }
            }
        }

        private void Constrain(float alpha)
        {
            for (int chain = 0; chain < ChainCount; chain++)
            {
                int root = chain * PointsPerChain;
                points[root] = Vector3.Lerp(previousTargets[root], targets[root], alpha);
            }
            for (int pass = 0; pass < 5; pass++)
            {
                for (int chain = 0; chain < ChainCount; chain++)
                for (int point = 1; point < PointsPerChain; point++)
                {
                    int i = chain * PointsPerChain + point;
                    Vector3 reference = point == 1 ? rootDirections[chain] : points[i - 1] - points[i - 2];
                    points[i] = points[i - 1] + ClampBend(points[i] - points[i - 1], reference, point - 1) * lengths[i];
                }
                LastContactCount += contacts.Resolve(points);
            }
        }

        private void ApplyBones()
        {
            MaximumDisplacement = MaximumLengthError = 0f;
            for (int chain = 0; chain < ChainCount; chain++)
            for (int point = 0; point < PointsPerChain - 1; point++)
            {
                int i = chain * PointsPerChain + point;
                Vector3 reference = point == 0 ? rootDirections[chain] : joints[i].position - joints[i - 1].position;
                Vector3 original = joints[i + 1].position - joints[i].position;
                Vector3 desired = ClampBend(points[i + 1] - joints[i].position, reference, point);
                if (original.sqrMagnitude > .000001f && desired.sqrMagnitude > .000001f)
                    joints[i].rotation = Quaternion.FromToRotation(original.normalized, desired.normalized) * joints[i].rotation;
                // Rotate the actual segment to close any residual left by projecting a fixed-length link onto a contact.
                for (int pass = 0; pass < 3; pass++)
                {
                    Vector3 endpoint = joints[i + 1].position;
                    Vector3 corrected = contacts.ResolvePoint(endpoint, point + 1);
                    if ((corrected - endpoint).sqrMagnitude < .00000001f) break;
                    desired = ClampBend(corrected - joints[i].position, reference, point);
                    joints[i].rotation = Quaternion.FromToRotation((endpoint - joints[i].position).normalized, desired) * joints[i].rotation;
                }
            }
            for (int i = 0; i < PointCount; i++)
            {
                points[i] = joints[i].position;
                MaximumDisplacement = Mathf.Max(MaximumDisplacement, Vector3.Distance(points[i], targets[i]));
                if (i % PointsPerChain != 0)
                    MaximumLengthError = Mathf.Max(MaximumLengthError, Mathf.Abs(Vector3.Distance(points[i], points[i - 1]) - lengths[i]));
            }
        }

        public float BendDegrees(int chain, int segment)
        {
            int i = chain * PointsPerChain + segment;
            Vector3 reference = segment == 0 ? rootDirections[chain] : joints[i].position - joints[i - 1].position;
            return Vector3.Angle(reference, joints[i + 1].position - joints[i].position);
        }

        private static Vector3 ClampBend(Vector3 direction, Vector3 reference, int segment)
        {
            if (reference.sqrMagnitude < .000001f) return Vector3.down;
            float limit = segment == 0 ? RootBendLimitDegrees : segment == 1 ? MiddleBendLimitDegrees : TipBendLimitDegrees;
            Vector3 unit = direction.sqrMagnitude > .000001f ? direction.normalized : reference.normalized;
            return Vector3.RotateTowards(reference.normalized, unit, limit * Mathf.Deg2Rad, 0f).normalized;
        }

        /// <summary>The mirror uses the already solved source transforms, including every auxiliary hair bone.</summary>
        public void CopyPoseTo(PlayerHair target)
        {
            if (target == null || target == this || !HasAuthoredBindings || !target.HasAuthoredBindings) return;
            target.driven = false;
            for (int i = 0; i < PointCount; i++)
            {
                target.joints[i].localPosition = joints[i].localPosition;
                target.joints[i].localRotation = joints[i].localRotation;
                target.points[i] = target.joints[i].position;
            }
        }

        private bool AllJointsPresent()
        {
            foreach (Transform joint in joints) if (joint == null) return false;
            return true;
        }

        private void RestoreBindPose()
        {
            for (int i = 0; i < joints.Length; i++)
            { joints[i].localPosition = bindPositions[i]; joints[i].localRotation = bindRotations[i]; }
        }
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void OnEnable() => RequestReset();
        private void OnDisable() { RequestReset(); if (HasAuthoredBindings) RestoreBindPose(); }
    }
}
