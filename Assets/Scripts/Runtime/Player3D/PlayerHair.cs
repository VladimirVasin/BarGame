using System;
using System.Globalization;
using Unity.Profiling;
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
        public const int MaximumFrameSubsteps = 2;
        public const double MaximumContinuousStep = .25d;
        public const float RootBendLimitDegrees = 25f;
        public const float MiddleBendLimitDegrees = 35f;
        public const float TipBendLimitDegrees = 45f;
        private const float StepSeconds = 1f / 120f;
        private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Hair.Apply");
        private static readonly ProfilerMarker PoseMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Hair.Pose");
        private static readonly ProfilerMarker ConstrainMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Hair.Constrain");
        private static readonly ProfilerMarker BonesMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Hair.Bones");

        [SerializeField] private Player3DAssetRegistry registry;
        [SerializeField] private Transform head;
        [SerializeField] private Transform[] joints = Array.Empty<Transform>();
        [SerializeField] private Vector3[] bindPositions = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] bindRotations = Array.Empty<Quaternion>();
        [SerializeField] private Vector3[] headLocalRest = Array.Empty<Vector3>();
        [SerializeField] private Vector3[] hangingOffsets = Array.Empty<Vector3>();
        [SerializeField] private Quaternion headBindRotation;
        [SerializeField] private float shortestLengthScale = .55f;
        [SerializeField] private float longestLengthScale = 1.8f;
        private float lengthScale = 1f;

        private readonly Vector3[] points = new Vector3[PointCount];
        private readonly Vector3[] velocities = new Vector3[PointCount];
        private readonly Vector3[] targets = new Vector3[PointCount];
        private readonly Vector3[] previousTargets = new Vector3[PointCount];
        private readonly Vector3[] beforeStep = new Vector3[PointCount];
        private readonly Vector3[] rootDirections = new Vector3[ChainCount];
        private readonly float[] lengths = new float[PointCount];
        private readonly Vector3[] solvedLocalPositions = new Vector3[PointCount];
        private readonly Quaternion[] solvedLocalRotations = new Quaternion[PointCount];
        private readonly Vector3[] bakedEntryPositions = new Vector3[PointCount];
        private readonly Quaternion[] bakedEntryRotations = new Quaternion[PointCount];
        private PlayerHairContacts contacts;
        private PlayerScarfPresentation scarf;
        private PlayerSecondaryMotionEnvironment environment;
        private Vector3 previousHead;
        private Quaternion previousHeadRotation;
        private double previousSeconds, simulationSeconds;
        private bool driven, initialized, resetPending = true;
        private bool bakedPlaybackActive, bakedEntryActive;
        private double bakedEntrySeconds;
        private int bakeCaptureDepth;
        private string bakedPoseSignature;

        public bool HasAuthoredBindings => registry != null && head != null && joints.Length == PointCount &&
            bindPositions.Length == PointCount && bindRotations.Length == PointCount &&
            headLocalRest.Length == PointCount && hangingOffsets.Length == PointCount && AllJointsPresent();
        public bool IsInitialized => initialized;
        public bool IsRuntimeDriven => driven;
        public int JointCount => joints.Length;
        public float LengthScale => lengthScale;
        public int ResetCount { get; private set; }
        public int LastContactCount { get; private set; }
        public float LastStepSeconds { get; private set; }
        public float MaximumDisplacement { get; private set; }
        public float MaximumLengthError { get; private set; }
        public long TotalApplyTicks { get; private set; }
        public long TotalApplyCalls { get; private set; }
        public long TotalSimulationSteps { get; private set; }
        public long LastApplyTicks { get; private set; }
        public int LastSimulationSteps { get; private set; }
        public double TotalDiscardedSeconds { get; private set; }
        public double LastDiscardedSeconds { get; private set; }
        public long TotalBakedPoseCalls { get; private set; }
        public long TotalBakedFallbacks { get; private set; }
        public bool LastPoseWasBaked { get; private set; }
        internal int BakedPoseValueCount => PointCount * 7;
        internal string BakedPoseSignature => bakedPoseSignature ??= CreateBakedPoseSignature();
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
        internal Vector3 AuthoredWorldPoint(int index)
        {
            int root = index / PointsPerChain * PointsPerChain;
            return head.TransformPoint(headLocalRest[root] +
                (headLocalRest[index] - headLocalRest[root]) * lengthScale);
        }
        internal void CaptureBakedPose(float[] values)
        {
            if (!HasAuthoredBindings || !initialized || LastPoseWasBaked)
                throw new InvalidOperationException("A hair bake requires an initialized physical solver pose.");
            if (values == null || values.Length != BakedPoseValueCount)
                throw new ArgumentException("A hair frame requires twelve local positions and rotations.", nameof(values));
            for (int i = 0; i < PointCount; i++)
            {
                int offset = i * 7;
                Vector3 position = joints[i].localPosition;
                Quaternion rotation = joints[i].localRotation;
                values[offset] = position.x; values[offset + 1] = position.y; values[offset + 2] = position.z;
                values[offset + 3] = rotation.x; values[offset + 4] = rotation.y;
                values[offset + 5] = rotation.z; values[offset + 6] = rotation.w;
            }
        }

        internal IDisposable BeginBakeCapture()
        {
            if (!HasAuthoredBindings) throw new InvalidOperationException("A hair bake requires the production hair bindings.");
            if (bakeCaptureDepth++ == 0)
            {
                scarf = registry.GetComponent<PlayerScarfPresentation>();
                bakedPlaybackActive = bakedEntryActive = false;
                initialized = false;
                RestoreBindPose();
                RequestReset();
            }
            return new BakeCaptureScope(this);
        }

        private sealed class BakeCaptureScope : IDisposable
        {
            private PlayerHair owner;
            internal BakeCaptureScope(PlayerHair owner) => this.owner = owner;
            public void Dispose()
            {
                PlayerHair hair = owner;
                owner = null;
                if (hair == null) return;
                hair.bakeCaptureDepth--;
                if (hair.bakeCaptureDepth == 0) hair.RequestReset();
            }
        }

        private string CreateBakedPoseSignature()
        {
            if (!HasAuthoredBindings) return string.Empty;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < PointCount; i++)
            {
                Add((uint)joints[i].name.Length);
                foreach (char character in joints[i].name) Add(character);
                AddVector(bindPositions[i]); AddQuaternion(bindRotations[i]);
                AddVector(headLocalRest[i]); AddVector(hangingOffsets[i]);
            }
            AddQuaternion(headBindRotation);
            // Medium-length recordings remain valid only for their original
            // rest geometry. Every changed length has its own bounded cache key.
            if (lengthScale != 1f) AddFloat(lengthScale);
            return registry.BuildSignature + ":hair-bones-v1:" + hash.ToString("x16", CultureInfo.InvariantCulture);

            void Add(uint value) { unchecked { hash = (hash ^ value) * 1099511628211UL; } }
            void AddFloat(float value) => Add(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
            void AddVector(Vector3 value) { AddFloat(value.x); AddFloat(value.y); AddFloat(value.z); }
            void AddQuaternion(Quaternion value) { AddFloat(value.x); AddFloat(value.y); AddFloat(value.z); AddFloat(value.w); }
        }
        internal bool TryCompareContactSolverForDiagnostics(out Vector3[] managed, out Vector3[] native,
            out int managedContacts, out int nativeContacts, out bool executedInBurst)
        {
            managed = native = null; managedContacts = nativeContacts = 0; executedInBurst = false;
            if (!HasAuthoredBindings) return false;
            if (contacts == null) contacts = new PlayerHairContacts(registry, scarf ?? registry.GetComponent<PlayerScarfPresentation>());
            // Diagnostic equivalence always uses this pose's real contacts,
            // including when ordinary presentation came from the bake bank.
            contacts.UpdatePose();
            return contacts != null && contacts.TryCompareForDiagnostics(points, out managed, out native,
                out managedContacts, out nativeContacts, out executedInBurst);
        }

        /// <summary>Editor binds ordered Back/Left/Right .00/.01/.02/.Tip chains in the imported rest pose.</summary>
        public void Configure(Player3DAssetRegistry owner, Transform[] orderedJoints,
            float configuredShortestScale = .55f, float configuredLongestScale = 1.8f)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (orderedJoints == null || orderedJoints.Length != PointCount)
                throw new ArgumentException("Hero hair requires three four-point chains.", nameof(orderedJoints));
            registry = owner;
            if (configuredShortestScale <= 0f || configuredShortestScale >= 1f ||
                configuredLongestScale <= 1f || configuredLongestScale > 3f)
                throw new ArgumentOutOfRangeException(nameof(configuredShortestScale),
                    "Hair length profiles require a shorter and a bounded longer rest shape.");
            shortestLengthScale = configuredShortestScale;
            longestLengthScale = configuredLongestScale;
            bakedPoseSignature = null;
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
            contacts?.Dispose(); contacts = null;
            initialized = false;
            RequestReset();
        }

        /// <summary>Changes free-link rest lengths; scalp roots, bones and bind arrays stay authored.</summary>
        public void ApplyCorruptionLevel(int level)
        {
            float next = 1f + (shortestLengthScale - 1f) * HeroCorruptionRules.ShortHairWeight(level) +
                (longestLengthScale - 1f) * HeroCorruptionRules.LongHairWeight(level);
            if (Mathf.Abs(next - lengthScale) < .00001f) return;
            lengthScale = next;
            bakedPoseSignature = null;
            bakedPlaybackActive = bakedEntryActive = false;
            RequestReset();
            if (HasAuthoredBindings) RestoreBindPose();
        }

        /// <summary>Only the actual player drives a simulation. Prefab-derived subsets and mirrors stay passive.</summary>
        public void BindRuntime(PlayerRuntime owner)
        {
            if (!HasAuthoredBindings) throw new InvalidOperationException("The production hero has no authored hair bindings.");
            environment = new PlayerSecondaryMotionEnvironment(owner);
            scarf = registry.GetComponent<PlayerScarfPresentation>();
            contacts?.Dispose(); contacts = new PlayerHairContacts(registry, scarf);
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
            using var profile = ApplyMarker.Auto();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            LastSimulationSteps = 0;
            LastDiscardedSeconds = 0d;
            LastPoseWasBaked = false;
            try { ApplyAtCore(seconds, paused, outside, wind); }
            finally
            {
                LastApplyTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                TotalApplyTicks += LastApplyTicks;
                TotalApplyCalls++;
            }
        }

        private void ApplyAtCore(double seconds, bool paused, bool outside, WindSample wind)
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
                LastPoseWasBaked = bakedPlaybackActive;
                return;
            }
            if (bakeCaptureDepth == 0 && IsFinite(seconds) &&
                SecondaryMotionBakeRuntime.TrySample(registry, SecondaryMotionBakePart.Hair,
                    BakedPoseSignature, BakedPoseValueCount, outside, wind, out SecondaryMotionBakeSample sample) &&
                TryApplyBakedPose(sample, seconds)) return;

            bool resumeFromBake = bakedPlaybackActive;
            if (resumeFromBake)
            {
                // The body may have advanced since the last baked frame. Carry
                // its displayed local hair pose into the current head frame.
                for (int i = 0; i < PointCount; i++)
                {
                    joints[i].localPosition = solvedLocalPositions[i];
                    joints[i].localRotation = solvedLocalRotations[i];
                    points[i] = joints[i].position;
                    velocities[i] = Vector3.zero;
                }
                bakedPlaybackActive = bakedEntryActive = false;
                TotalBakedFallbacks++;
            }
            using (PoseMarker.Auto())
            {
                RestoreBindPose();
                BuildTargets();
                if (contacts == null) contacts = new PlayerHairContacts(registry, scarf);
                contacts.UpdatePose();
            }
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
            else if (resumeFromBake)
            {
                for (int i = 0; i < PointCount; i++)
                    lengths[i] = i % PointsPerChain == 0 ? 0f : Vector3.Distance(joints[i - 1].position, joints[i].position);
                Array.Copy(targets, previousTargets, PointCount);
                LastStepSeconds = 0f;
                LastContactCount = 0;
                Constrain(1f);
            }
            else
            {
                // Advance at most two cosmetic steps, consuming the full
                // timestamp so slow frames cannot create catch-up feedback.
                double simulated = Math.Min(elapsed, MaximumFrameSubsteps * (double)StepSeconds);
                LastDiscardedSeconds = elapsed - simulated;
                TotalDiscardedSeconds += LastDiscardedSeconds;
                LastStepSeconds = (float)simulated;
                LastContactCount = 0;
                int steps = simulated > 0d ? Mathf.Min(MaximumFrameSubsteps,
                    Mathf.Max(1, Mathf.CeilToInt((float)simulated / StepSeconds))) : 0;
                LastSimulationSteps = steps;
                TotalSimulationSteps += steps;
                float dt = steps > 0 ? (float)simulated / steps : 0f;
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

        private bool TryApplyBakedPose(SecondaryMotionBakeSample sample, double seconds)
        {
            if (sample.From == null || sample.To == null || sample.From.Length != BakedPoseValueCount ||
                sample.To.Length != BakedPoseValueCount || !IsFinite(sample.Alpha)) return false;
            for (int i = 0; i < BakedPoseValueCount; i++)
                if (!IsFinite(sample.From[i]) || !IsFinite(sample.To[i])) return false;
            for (int i = 0; i < PointCount; i++)
            {
                Quaternion from = ReadRotation(sample.From, i * 7), to = ReadRotation(sample.To, i * 7);
                float fromLength = Quaternion.Dot(from, from), toLength = Quaternion.Dot(to, to);
                if (!IsFinite(fromLength) || !IsFinite(toLength) || fromLength < .000001f || toLength < .000001f) return false;
            }

            bool discontinuity = initialized && (seconds < previousSeconds || seconds - previousSeconds > MaximumContinuousStep ||
                (previousHead - head.position).sqrMagnitude > .3025f || Quaternion.Angle(previousHeadRotation, head.rotation) > 100f);
            if (!bakedPlaybackActive && initialized && !resetPending && !discontinuity)
            {
                Array.Copy(solvedLocalPositions, bakedEntryPositions, PointCount);
                Array.Copy(solvedLocalRotations, bakedEntryRotations, PointCount);
                bakedEntrySeconds = seconds;
                bakedEntryActive = true;
            }
            if (!initialized || resetPending || discontinuity)
            {
                ResetCount++;
                bakedEntryActive = false;
            }
            using (PoseMarker.Auto())
            {
                RestoreBindPose();
                BuildTargets();
                for (int i = 0; i < PointCount; i++)
                    lengths[i] = i % PointsPerChain == 0 ? 0f : Vector3.Distance(joints[i - 1].position, joints[i].position);
            }
            float alpha = Mathf.Clamp01(sample.Alpha);
            float entry = bakedEntryActive ? Mathf.Clamp01((float)((seconds - bakedEntrySeconds) / .12d)) : 1f;
            using (BonesMarker.Auto())
            {
                for (int i = 0; i < PointCount; i++)
                {
                    int offset = i * 7;
                    Vector3 position = Vector3.Lerp(ReadPosition(sample.From, offset), ReadPosition(sample.To, offset), alpha);
                    Quaternion rotation = Quaternion.Slerp(ReadRotation(sample.From, offset).normalized,
                        ReadRotation(sample.To, offset).normalized, alpha);
                    if (entry < 1f)
                    {
                        position = Vector3.Lerp(bakedEntryPositions[i], position, entry);
                        rotation = Quaternion.Slerp(bakedEntryRotations[i], rotation, entry);
                    }
                    joints[i].localPosition = solvedLocalPositions[i] = position;
                    joints[i].localRotation = solvedLocalRotations[i] = rotation;
                }
            }
            MaximumDisplacement = MaximumLengthError = 0f;
            for (int i = 0; i < PointCount; i++)
            {
                points[i] = joints[i].position;
                velocities[i] = Vector3.zero;
                MaximumDisplacement = Mathf.Max(MaximumDisplacement, Vector3.Distance(points[i], targets[i]));
                if (i % PointsPerChain != 0)
                    MaximumLengthError = Mathf.Max(MaximumLengthError, Mathf.Abs(Vector3.Distance(points[i], points[i - 1]) - lengths[i]));
                previousTargets[i] = targets[i];
            }
            bakedEntryActive &= entry < 1f;
            initialized = bakedPlaybackActive = LastPoseWasBaked = true;
            resetPending = false;
            LastStepSeconds = 0f;
            LastContactCount = 0;
            previousSeconds = seconds;
            previousHead = head.position;
            previousHeadRotation = head.rotation;
            TotalBakedPoseCalls++;
            return true;

            static Vector3 ReadPosition(float[] values, int offset) => new Vector3(values[offset], values[offset + 1], values[offset + 2]);
            static Quaternion ReadRotation(float[] values, int offset) => new Quaternion(values[offset + 3], values[offset + 4], values[offset + 5], values[offset + 6]);
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
            Matrix4x4 headToWorld = head.localToWorldMatrix;
            Matrix4x4 actorToWorld = transform.localToWorldMatrix;
            for (int chain = 0; chain < ChainCount; chain++)
            {
                int root = chain * PointsPerChain;
                targets[root] = headToWorld.MultiplyPoint3x4(headLocalRest[root]);
                rootDirections[chain] = (joints[root + 1].position - joints[root].position).normalized;
                for (int point = 1; point < PointsPerChain; point++)
                {
                    int i = root + point;
                    Vector3 direction = twist * actorToWorld.MultiplyVector(
                        (hangingOffsets[i] - hangingOffsets[i - 1]) * lengthScale);
                    Vector3 reference = point == 1 ? rootDirections[chain] : targets[i - 1] - targets[i - 2];
                    targets[i] = targets[i - 1] + ClampBend(direction, reference, point - 1) * direction.magnitude;
                }
            }
        }

        private void Constrain(float alpha)
        {
            using var profile = ConstrainMarker.Auto();
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
            using var profile = BonesMarker.Auto();
            MaximumDisplacement = MaximumLengthError = 0f;
            for (int chain = 0; chain < ChainCount; chain++)
            for (int point = 0; point < PointsPerChain - 1; point++)
            {
                int i = chain * PointsPerChain + point;
                Transform joint = joints[i];
                // Rotating this segment moves its children, not its own
                // origin. Read that origin once after the parent is solved.
                Vector3 origin = joint.position;
                Vector3 reference = point == 0 ? rootDirections[chain] : origin - joints[i - 1].position;
                Vector3 original = joints[i + 1].position - origin;
                Vector3 desired = ClampBend(points[i + 1] - origin, reference, point);
                if (original.sqrMagnitude > .000001f && desired.sqrMagnitude > .000001f)
                    joint.rotation = Quaternion.FromToRotation(original.normalized, desired.normalized) * joint.rotation;
                // Rotate the actual segment to close any residual left by projecting a fixed-length link onto a contact.
                for (int pass = 0; pass < 3; pass++)
                {
                    Vector3 endpoint = joints[i + 1].position;
                    Vector3 corrected = contacts.ResolvePoint(endpoint, point + 1, i + 1);
                    if ((corrected - endpoint).sqrMagnitude < .00000001f) break;
                    desired = ClampBend(corrected - origin, reference, point);
                    joint.rotation = Quaternion.FromToRotation((endpoint - origin).normalized, desired) * joint.rotation;
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
            target.lengthScale = lengthScale;
            target.bakedPoseSignature = null;
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
            {
                joints[i].localPosition = bindPositions[i] * (i % PointsPerChain == 0 ? 1f : lengthScale);
                joints[i].localRotation = bindRotations[i];
            }
        }
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void OnEnable() => RequestReset();
        private void OnDisable() { RequestReset(); if (HasAuthoredBindings) RestoreBindPose(); }
        internal void DisposeOwnedBakeResources(GameObject authoringOwner)
        {
            if (Application.isPlaying || authoringOwner == null || !transform.IsChildOf(authoringOwner.transform) ||
                (authoringOwner.hideFlags & HideFlags.HideAndDontSave) != HideFlags.HideAndDontSave)
                throw new InvalidOperationException("Only an owned hidden authoring clone may release hair bake resources.");
            DisposeContacts();
        }
        private void DisposeContacts() { contacts?.Dispose(); contacts = null; }
        private void OnDestroy() => DisposeContacts();
    }
}
