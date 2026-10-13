using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Constrains the complete held prop by moving its arm, never its grip socket.
    /// The authored elbow/wrist angles survive shoulder correction unchanged.</summary>
    internal sealed partial class CombatWeaponConstraint : IDisposable
    {
        private const float Skin = .004f;
        private static readonly ProfilerMarker GatherMarker = new ProfilerMarker("BarPromenade.Combat.WeaponConstraint.Gather");
        private static readonly ProfilerMarker DepthMarker = new ProfilerMarker("BarPromenade.Combat.WeaponConstraint.Depth");
        private static readonly ProfilerMarker SweepMarker = new ProfilerMarker("BarPromenade.Combat.WeaponConstraint.Sweep");
        private static readonly ProfilerMarker WorldEnvelopeMarker = new ProfilerMarker("BarPromenade.Combat.WeaponConstraint.WorldEnvelope");
        internal const int MaximumCandidateChecksPerApply = 12;
        private int remainingCandidateChecks;
        private bool hasShoulderChoice;
        private int shoulderChoiceFrame = -1;
        private float shoulderChoiceClock;
        private Vector3 shoulderChoiceTargetPosition, solveTargetPosition;
        private Quaternion shoulderChoiceTargetRotation, shoulderChoiceForearm, shoulderChoiceHand, shoulderChoice;
        private Quaternion solveTargetRotation;
        private bool hasRejectedSearch;
        private Vector3 rejectedRootPosition, rejectedTargetPosition;
        private Quaternion rejectedRootRotation, rejectedTargetRotation, rejectedForearm, rejectedHand;
        private int rejectedSearchCursor;
        private readonly CombatActor actor;
        private readonly Transform upper, forearm, hand, weapon;
        private readonly NpcHandPose rightHandPose;
        private readonly Vector3 rightFingersInHand, rightPalmInHand;
        private readonly CapsuleCollider probe;
        private readonly CombatArmClearance armClearance;
        private readonly CombatPrimitiveQueryCache anatomyQueries = new CombatPrimitiveQueryCache();
        private readonly Collider[] nearby = new Collider[96];
        private readonly RaycastHit[] sweeps = new RaycastHit[48];
        private readonly List<Collider> obstacles = new List<Collider>(96);
        private readonly List<Collider> anatomy = new List<Collider>(20);
        private readonly int rightForearmIndex = -1;
        private readonly List<Collider> otherAnatomy = new List<Collider>(20);
        private readonly List<CombatActor> otherOwners = new List<CombatActor>(20);
        private IReadOnlyList<CombatActor> participants;
        private int[] otherGenerations = Array.Empty<int>();
        private readonly List<DepthShape> depthShapes = new List<DepthShape>(48);
        private readonly List<DepthShape> otherDepthShapes = new List<DepthShape>(20);
        private readonly List<int> anatomySweepCandidates = new List<int>(20);
        private readonly List<int> otherSweepCandidates = new List<int>(20);
        private readonly Vector3[] searchAxes = new Vector3[5];
        private static readonly WorldProbeLayout WorldProbes = BuildWorldProbeLayout();
        private readonly int[] worldProbeStamps = new int[WorldProbes.Count];
        private int worldProbeStamp;
        private Quaternion baseUpper, baseForearm, baseHand;
        private Quaternion lastUpper, lastForearm, lastHand;
        private Pose lastWeapon;
        private bool applied, hasLast;
        private CombatActor other;
        private readonly Transform[] poseBones;
        private readonly Vector3[] safePositions;
        private readonly Quaternion[] safeRotations;
        private Vector3 safeRootPosition;
        private Quaternion safeRootRotation;
        private Pose[] previousAnatomy;
        private Pose[] previousOtherAnatomy = Array.Empty<Pose>();
        private readonly BodySweep[] anatomySweep;
        private BodySweep[] otherSweep = Array.Empty<BodySweep>();
        private AnatomyStamp[] preparedOtherAnatomy = Array.Empty<AnatomyStamp>();
        private AnatomyStamp[] gatheredOtherAnatomy = Array.Empty<AnatomyStamp>();
        private bool[] otherAnatomyPrepared = Array.Empty<bool>();
        private bool otherSnapshotActive, otherSnapshotReady;
        private readonly List<Bounds> worldSweepBounds = new List<Bounds>(96);
        private readonly Bounds[] segmentSweepBounds = new Bounds[CombatWeaponGeometry.Segments.Count];
        private bool anatomyReuseActive, hasClearAnatomySweep;
        private Pose clearAnatomyFrom, clearAnatomyTo;
        private float clearAnatomyBodyTravel;
        private bool clearAnatomyNearOther;
        private bool clearAnatomyProbeEnabled;
        private bool clearAnatomyProbeActive, clearAnatomyProbeTrigger;
        private Vector3 clearAnatomyProbeScale, clearAnatomyProbeCenter;
        private int clearAnatomyProbeDirection;
        private readonly AnatomyStamp[] clearAnatomy;
        private AnatomyStamp[] clearOtherAnatomy = Array.Empty<AnatomyStamp>();
        private readonly Vector3[] fallbackBasePositions;
        private readonly Quaternion[] fallbackBaseRotations;
        private bool fullPoseApplied, contactPreview, pendingCommit, checkingDesiredPath;
        private Vector3 escapeAxis;
        private float escapeAngle;
        internal bool RightWristWithinLimits => RightWristCanHold(hand.rotation);
        internal bool MotionBlocked { get; private set; }
        internal string BlockingShape { get; private set; }
        internal Collider BlockingCollider { get; private set; }
        internal CombatActor BlockingActor { get; private set; }
        internal Pose RequestedWeaponPose { get; private set; }
        internal CombatActor RequestedBodyContactActor { get; private set; }
        private Collider requestedBlockingCollider;
        private CombatActor requestedBlockingActor;
        private bool requestedOwnGeometryBlocked;
        internal bool Blocked { get; private set; }
        internal bool WorldBlocked { get; private set; }
        internal float WorldContactFraction { get; private set; } = float.PositiveInfinity;
        internal Vector3 WorldContactPoint { get; private set; }
        internal Vector3 WorldContactNormal { get; private set; }
        internal bool SupportBlocked { get; private set; }
        internal float PenetrationDepth { get; private set; }
        internal float DesiredPoseDepth => journalDesiredDepth;
        internal bool DesiredPoseSweepBlocked => journalDesiredSweep;
        internal long CandidateChecks { get; private set; }
        internal long ShoulderChoicesReused { get; private set; }
        internal long CandidateBudgetExhaustions { get; private set; }
        internal long SweepSamples { get; private set; }
        internal long WorldQueries { get; private set; }
        internal long WorldBoundsCulls { get; private set; }
        internal long RepeatedWorldQueriesAvoided { get; private set; }
        internal long AnatomySweepQueries { get; private set; }
        internal long RepeatedAnatomySweepsAvoided { get; private set; }
        internal long OtherAnatomyGeometryBuilds { get; private set; }
        internal long OtherAnatomyGeometryReuses { get; private set; }
        internal long OtherAnatomyDepthCandidates { get; private set; }
        internal long OtherAnatomyDepthCulled { get; private set; }
        internal long OtherAnatomySweepCandidates { get; private set; }
        internal long OtherAnatomySweepCulled { get; private set; }
        internal long OtherAnatomyInputCaptures { get; private set; }
        internal long OtherAnatomyInputReuses { get; private set; }
        internal int ArmCoreSnapshots => armClearance.CoreSnapshotCount;
        private int journalConstraintState = -1;
        private float journalDesiredDepth;
        private bool journalDesiredSweep;
        private string journalConstraintReason;

        internal CombatWeaponConstraint(CombatActor owner)
        {
            actor = owner; weapon = owner.Weapon.transform;
            foreach (Transform bone in owner.DamageRigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "upper_arm.R") upper = bone;
                else if (bone.name == "forearm.R") forearm = bone;
                else if (bone.name == "hand.R") hand = bone;
            }
            if (upper == null || forearm == null || hand == null)
                throw new InvalidOperationException("Held weapon clearance requires the complete right arm.");
            rightHandPose = owner.GetComponentInChildren<NpcHandPose>(true);
            if (rightHandPose == null)
                throw new InvalidOperationException("Held weapon clearance requires the authored palm frame.");
            rightPalmInHand = Quaternion.Inverse(hand.rotation) * rightHandPose.PalmNormal(false);
            rightFingersInHand = Quaternion.Inverse(hand.rotation) * Vector3.ProjectOnPlane(
                rightHandPose.CylinderCentre(false) - hand.position, rightHandPose.PalmNormal(false)).normalized;
            var ordered = new List<Transform>();
            foreach (SkinnedMeshRenderer skin in owner.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Transform bone in skin.bones)
                    if (bone != null && !ordered.Contains(bone)) ordered.Add(bone);
            ordered.Sort((a, b) => DepthOf(a).CompareTo(DepthOf(b)));
            poseBones = ordered.ToArray();
            safePositions = new Vector3[poseBones.Length];
            safeRotations = new Quaternion[poseBones.Length];
            fallbackBasePositions = new Vector3[poseBones.Length];
            fallbackBaseRotations = new Quaternion[poseBones.Length];
            foreach (var entry in owner.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (entry.Value == Player3DAnatomicalPart.RightForearm) rightForearmIndex = anatomy.Count;
                anatomy.Add(entry.Key);
            }
            previousAnatomy = new Pose[anatomy.Count];
            anatomySweep = new BodySweep[anatomy.Count];
            clearAnatomy = new AnatomyStamp[anatomy.Count];
            var query = new GameObject("Combat weapon clearance query") { hideFlags = HideFlags.HideAndDontSave };
            // ComputePenetration needs a native shape on the pinned Unity/PhysX
            // backend. An immediately disabled fresh collider reports no contact.
            // Keep this geometry-only query as a remote trigger; world casts ignore
            // triggers and no simulation body can meet it during play.
            query.transform.position = Vector3.up * 10000f;
            probe = query.AddComponent<CapsuleCollider>();
            probe.isTrigger = true; probe.direction = 1; probe.enabled = true;
            Physics.SyncTransforms();
            armClearance = new CombatArmClearance(owner, upper, forearm);
        }

        internal void SetOpponent(CombatActor value)
        {
            if (actor.CombatParticipants != null) { SetCombatParticipants(actor.CombatParticipants); return; }
            if (other == value && participants == null) return;
            participants = null; other = value; otherAnatomy.Clear(); otherOwners.Clear();
            if (other != null && other.Ragdoll != null)
                AddOtherAnatomy(other);
            InitializeOtherAnatomy();
        }

        internal void SetCombatParticipants(IReadOnlyList<CombatActor> values)
        {
            if (ReferenceEquals(participants, values)) return;
            participants = values; otherAnatomy.Clear(); otherOwners.Clear();
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                    if (values[i] != null && values[i] != actor && values[i].Ragdoll != null) AddOtherAnatomy(values[i]);
            }
            else if (other != null && other.Ragdoll != null) AddOtherAnatomy(other);
            InitializeOtherAnatomy();
        }

        private void AddOtherAnatomy(CombatActor owner)
        {
            foreach (var entry in owner.Ragdoll.PhysicsController.AnatomicalColliders)
            { otherAnatomy.Add(entry.Key); otherOwners.Add(owner); }
        }

        private void InitializeOtherAnatomy()
        {
            anatomyQueries.Clear();
            previousOtherAnatomy = new Pose[otherAnatomy.Count];
            otherSweep = new BodySweep[otherAnatomy.Count];
            preparedOtherAnatomy = new AnatomyStamp[otherAnatomy.Count];
            gatheredOtherAnatomy = new AnatomyStamp[otherAnatomy.Count];
            otherAnatomyPrepared = new bool[otherAnatomy.Count];
            clearOtherAnatomy = new AnatomyStamp[otherAnatomy.Count];
            otherGenerations = new int[otherAnatomy.Count];
            otherDepthShapes.Clear(); otherSweepCandidates.Clear();
            if (otherDepthShapes.Capacity < otherAnatomy.Count) otherDepthShapes.Capacity = otherAnatomy.Count;
            if (otherSweepCandidates.Capacity < otherAnatomy.Count) otherSweepCandidates.Capacity = otherAnatomy.Count;
            ClearAnatomyReuse();
            for (int i = 0; i < otherAnatomy.Count; i++)
            {
                previousOtherAnatomy[i] = new Pose(otherAnatomy[i].transform.position, otherAnatomy[i].transform.rotation);
                otherGenerations[i] = otherOwners[i].ContactGeneration;
            }
        }

        private bool OtherAnatomyActive(int index) => otherSnapshotActive && otherSnapshotReady
            ? gatheredOtherAnatomy[index].CanParticipate
            : otherAnatomy[index] != null && otherOwners[index] != null &&
              otherOwners[index].isActiveAndEnabled && otherAnatomy[index].gameObject.activeInHierarchy &&
              otherOwners[index].Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(otherAnatomy[index]);

        private AnatomyStamp CaptureOtherInput(int index)
        {
            OtherAnatomyInputCaptures++;
            return new AnatomyStamp(otherAnatomy[index], previousOtherAnatomy[index], otherOwners[index]);
        }

        private AnatomyStamp OtherInput(int index)
        {
            if (!otherSnapshotActive || !otherSnapshotReady) return CaptureOtherInput(index);
            OtherAnatomyInputReuses++;
            return gatheredOtherAnatomy[index];
        }

        private void ObserveOtherGeneration(int index)
        {
            if (otherAnatomy[index] == null || otherOwners[index] == null ||
                otherGenerations[index] == otherOwners[index].ContactGeneration) return;
            previousOtherAnatomy[index] = new Pose(otherAnatomy[index].transform.position, otherAnatomy[index].transform.rotation);
            otherGenerations[index] = otherOwners[index].ContactGeneration;
            otherAnatomyPrepared[index] = false;
            ClearAnatomyReuse();
        }

        private void PrepareOtherAnatomy(int index) => PrepareOtherAnatomy(index, OtherInput(index));

        private void PrepareOtherAnatomy(int index, AnatomyStamp current)
        {
            if (otherAnatomyPrepared[index] && preparedOtherAnatomy[index].Matches(current))
            { OtherAnatomyGeometryReuses++; return; }
            otherSweep[index] = new BodySweep(otherAnatomy[index], previousOtherAnatomy[index]);
            preparedOtherAnatomy[index] = current;
            otherAnatomyPrepared[index] = true;
            OtherAnatomyGeometryBuilds++;
        }

        internal CombatArmClearance SupportArmClearance => armClearance;

        internal void Restore()
        {
            if (!applied) return;
            if (fullPoseApplied)
            {
                for (int i = 0; i < poseBones.Length; i++)
                    poseBones[i].SetPositionAndRotation(actor.transform.TransformPoint(fallbackBasePositions[i]),
                        actor.transform.rotation * fallbackBaseRotations[i]);
                fullPoseApplied = false;
            }
            upper.localRotation = baseUpper;
            forearm.localRotation = baseForearm;
            hand.localRotation = baseHand;
            applied = false;
        }

        internal void Forget() { EndRecoveryContact(); ClearAnatomyReuse(); hasRejectedSearch = hasShoulderChoice = false; applied = fullPoseApplied = hasLast = Blocked = WorldBlocked = SupportBlocked = MotionBlocked = pendingCommit = checkingDesiredPath = false; PenetrationDepth = 0f;
            RequestedBodyContactActor = requestedBlockingActor = BlockingActor = null;
            requestedBlockingCollider = BlockingCollider = null; requestedOwnGeometryBlocked = false; }
        internal void Reset()
        {
            Restore(); Forget(); journalConstraintState = -1; journalConstraintReason = null;
        }

        internal void Apply()
        {
            using var journalTiming = actor.MeasureJournalWork(CombatActor.JournalWork.WeaponConstraint);
            using var otherInputs = new OtherSnapshotScope(this);
            ClearAnatomyReuse();
            anatomyReuseActive = !contactPreview;
            if (weapon == null || actor.IsWeaponDropped ||
                (actor.IsRagdollActive && !actor.Ragdoll.IsRecovering)) { Forget(); return; }
            Restore();
            pendingCommit = false;
            MotionBlocked = false;
            SupportBlocked = false;
            RequestedBodyContactActor = null;
            requestedBlockingCollider = null; requestedBlockingActor = null;
            requestedOwnGeometryBlocked = false;
            baseUpper = upper.localRotation; baseForearm = forearm.localRotation; baseHand = hand.localRotation;
            Quaternion desired = upper.rotation;
            solveTargetRotation = desired; solveTargetPosition = upper.position;
            remainingCandidateChecks = MaximumCandidateChecksPerApply - 1;
            applied = true;
            GatherObstacles();
            using var coreSnapshot = armClearance.BeginWeaponSolve();
            escapeAngle = 0f;
            WorldBlocked = false;
            WorldContactFraction = float.PositiveInfinity;
            checkingDesiredPath = true;
            RequestedWeaponPose = WeaponPose;
            BeginOwnForearmDiagnostic();
            float depth = CandidateDepth();
            requestedBlockingCollider = BlockingCollider; requestedBlockingActor = BlockingActor;
            Vector3 firstEscapeAxis = escapeAxis;
            float firstEscapeAngle = escapeAngle;
            bool swept = hasLast && !SweepClear(lastWeapon, WeaponPose);
            if (requestedOwnGeometryBlocked) RequestedBodyContactActor = null;
            journalDesiredDepth = depth; journalDesiredSweep = swept;
            string desiredBlockingShape = BlockingShape;
            Collider desiredBlockingCollider = BlockingCollider;
            CombatActor desiredBlockingActor = BlockingActor;
            checkingDesiredPath = false;
            Blocked = depth > 0f || swept;
            // A held two-handed bar has one reachable pose. Probe the frozen
            // supporting arm after the right arm's collision gates, including
            // contact previews, so sampled and presented weapon paths agree.
            bool constrainSupport = actor.SupportGrip != null && actor.SupportGrip.RequiresWeaponSupportConstraint;
            SupportBlocked = !Blocked && constrainSupport && !actor.SupportGrip.IsWeaponSupportPoseGeometricallyFeasible();
            if (SupportBlocked)
            {
                // A proven unreachable second hand yields through its normal
                // release; it does not freeze an otherwise clear right arm.
                actor.SupportGrip.RejectObstructedPose("weapon_support_motion");
                hasClearAnatomySweep = false;
                depth = CandidateDepth();
                swept = hasLast && !SweepClear(lastWeapon, WeaponPose);
                Blocked = depth > 0f || swept;
                constrainSupport = actor.SupportGrip.RequiresWeaponSupportConstraint;
                if (!Blocked) { AcceptCandidate(); return; }
            }
            if (!Blocked && !SupportBlocked) { AcceptCandidate(); return; }

            // A stalled rise may explore a small, progressively widening shoulder
            // arc. It uses the same anatomy/depth/sweep gates as every other pose.
            if (RecoveryEscapeActive && TryRecoveryShoulderEscape(constrainSupport, desired))
            { AcceptCandidate(); return; }

            // Reuse only a shoulder preference for this exact authored pose.
            // Moving anatomy and world geometry are always checked again.
            if (!contactPreview && hasShoulderChoice && shoulderChoiceFrame == Time.frameCount &&
                shoulderChoiceClock == actor.JournalPoseClock &&
                shoulderChoiceTargetPosition.Equals(solveTargetPosition) &&
                shoulderChoiceTargetRotation.Equals(solveTargetRotation) &&
                shoulderChoiceForearm.Equals(baseForearm) && shoulderChoiceHand.Equals(baseHand))
            {
                upper.rotation = shoulderChoice;
                if (TryCandidate(constrainSupport, out _))
                { ShoulderChoicesReused++; AcceptCandidate(); return; }
            }

            // Carry the previous safe shoulder with the actor's accepted root yaw.
            // Its local supporting chain follows that same frame; a saved world
            // rotation would leave the right arm behind as the body turns.
            Quaternion best = desired;
            float bestDepth = depth;
            if (hasLast)
            {
                upper.rotation = actor.transform.rotation * lastUpper;
                if (TryCandidate(constrainSupport, out _))
                {
                    Quaternion safe = upper.rotation;
                    // Stop at first obstruction rather than stepping across a thin wall.
                    for (int i = 1; i <= 12; i++)
                    {
                        upper.rotation = Quaternion.Slerp(safe, desired, i / 12f);
                        if (!TryCandidate(constrainSupport, out _))
                        { upper.rotation = Quaternion.Slerp(safe, desired, (i - 1) / 12f); break; }
                    }
                    AcceptCandidate(); return;
                }
            }

            // Resolve shallow contacts by their actual separating normal. A few
            // millimetres of interference should not trigger a conspicuous arm swing.
            if (firstEscapeAngle > 0f)
                for (int scale = 1; scale <= 4; scale *= 2)
                {
                    upper.rotation = Quaternion.AngleAxis(Mathf.Min(36f, firstEscapeAngle * scale), firstEscapeAxis) * desired;
                    if (TryCandidate(constrainSupport, out _))
                    { AcceptCandidate(); return; }
                }

            // Search around the shoulder, not the wrist. Both anatomical hinge angles
            // and the palm/cylinder transform remain exactly those of the authored pose.
            searchAxes[0] = actor.transform.up; searchAxes[1] = actor.transform.right; searchAxes[2] = actor.transform.forward;
            searchAxes[3] = (searchAxes[0] + searchAxes[1]).normalized;
            searchAxes[4] = (searchAxes[0] - searchAxes[1]).normalized;
            // Keep the per-apply budget, but continue an exhausted search when the
            // owning rise retries the same rejected pose. Otherwise its clock rollback
            // makes us test only the first 12-degree ring forever. A successful render
            // of the previous pose must not erase that retry's search position.
            bool sameRejectedPose = hasRejectedSearch &&
                (rejectedRootPosition - actor.transform.position).sqrMagnitude <= .000001f &&
                Quaternion.Angle(rejectedRootRotation, actor.transform.rotation) <= .1f &&
                (rejectedTargetPosition - solveTargetPosition).sqrMagnitude <= .000001f &&
                Quaternion.Angle(rejectedTargetRotation, solveTargetRotation) <= .1f &&
                Quaternion.Angle(rejectedForearm, baseForearm) <= .1f && Quaternion.Angle(rejectedHand, baseHand) <= .1f;
            int cursor = sameRejectedPose ? rejectedSearchCursor : 0;
            int reservedFallback = hasLast && !constrainSupport ? 1 : 0;
            const int searchCount = 7 * 5 * 2;
            for (int checkedDirections = 0; checkedDirections < searchCount && remainingCandidateChecks > reservedFallback; checkedDirections++)
            {
                int direction = cursor;
                cursor = (cursor + 1) % searchCount;
                float angle = (direction / 10 + 1) * 12f;
                Vector3 axis = searchAxes[(direction % 10) / 2];
                int sign = direction % 2 == 0 ? -1 : 1;
                upper.rotation = Quaternion.AngleAxis(angle * sign, axis) * desired;
                bool clear = TryCandidate(constrainSupport, out float candidate);
                if (candidate < bestDepth) { bestDepth = candidate; best = upper.rotation; }
                if (!clear) continue;
                AcceptCandidate(); return;
            }
            // A tightly obstructed action cannot be made safe by twisting the wrist.
            // Keep the last complete reachable arm while the owning action is blocked.
            // The coupled shoulder search retains THIS authored elbow/wrist.
            // The legacy complete-arm fallback remains for independent safety
            // solves; it must not manufacture a different supporting target.
            if (hasLast && !constrainSupport)
            {
                upper.rotation = actor.transform.rotation * lastUpper;
                forearm.localRotation = lastForearm;
                hand.localRotation = lastHand;
                if (TryCandidate(false, out _)) { AcceptCandidate(); return; }
            }
            upper.rotation = best;
            forearm.localRotation = baseForearm; hand.localRotation = baseHand;
            PenetrationDepth = Depth();
            MotionBlocked = true;
            if (remainingCandidateChecks == 0) CandidateBudgetExhaustions++;
            if (!contactPreview)
            {
                hasRejectedSearch = true; rejectedSearchCursor = cursor;
                rejectedRootPosition = actor.transform.position; rejectedRootRotation = actor.transform.rotation;
                rejectedTargetPosition = solveTargetPosition; rejectedTargetRotation = solveTargetRotation;
                rejectedForearm = baseForearm; rejectedHand = baseHand;
            }
            if (hasLast)
            {
                RestoreLastSafePose();
                PenetrationDepth = Depth();
            }
            // The restored pose can be clear while the requested step was blocked.
            // Retain its cause instead of losing the floor/wall name in Depth().
            BlockingShape = desiredBlockingShape ?? BlockingShape;
            if (desiredBlockingShape != null)
            { BlockingCollider = desiredBlockingCollider; BlockingActor = desiredBlockingActor; }
            // Do not accept an intersecting pose as the sweep's next starting point.
            JournalConstraint(SupportBlocked ? "support_contact" : "pose_rejected");
            ClearAnatomyReuse();
        }

        private Pose WeaponPose => new Pose(weapon.position, weapon.rotation);

        private bool TryCandidate(bool constrainSupport, out float depth)
        {
            depth = float.PositiveInfinity;
            // A rejected wrist/reach branch does not need native penetration or
            // a sweep. The final supporting pose still passes its full gates.
            if (constrainSupport && !actor.SupportGrip.IsWeaponSupportPoseGeometricallyFeasible()) return false;
            if (remainingCandidateChecks <= 0) return false;
            remainingCandidateChecks--;
            depth = CandidateDepth();
            return depth <= 0f && (!hasLast || SweepClear(lastWeapon, WeaponPose));
        }

        private bool RightWristCanHold(Quaternion rotation)
        {
            Vector3 direction = (hand.position - forearm.position).normalized;
            Vector3 fingers = rotation * rightFingersInHand;
            Vector3 palm = rotation * rightPalmInHand;
            float forward = Vector3.Dot(direction, fingers);
            // Equivalent to the existing 25/55/65 degree support-hand limits,
            // without inverse trigonometry during ordinary candidate checks.
            return Mathf.Abs(Vector3.Dot(direction, Vector3.Cross(palm, fingers).normalized)) <= .42261827f &&
                forward >= .42261827f && Mathf.Abs(Vector3.Dot(direction, palm)) <= 1.428148f * forward;
        }

        private void AcceptCandidate()
        {
            if (!contactPreview)
            {
                hasShoulderChoice = true; shoulderChoiceFrame = Time.frameCount;
                shoulderChoiceClock = actor.JournalPoseClock;
                shoulderChoiceTargetPosition = solveTargetPosition; shoulderChoiceTargetRotation = solveTargetRotation;
                shoulderChoiceForearm = baseForearm; shoulderChoiceHand = baseHand; shoulderChoice = upper.rotation;
            }
            PenetrationDepth = 0f;
            pendingCommit = true;
            JournalConstraint(SupportBlocked ? "support_contact" : Blocked ? "arm_corrected" : "clear");
        }

        /// <summary>Commit only after the supporting hand has made its final contact.</summary>
        internal void CommitPresentedPose(CombatSupportGrip support)
        {
            using var journalTiming = actor.MeasureJournalWork(CombatActor.JournalWork.WeaponConstraint);
            using var otherInputs = new OtherSnapshotScope(this);
            try
            {
                if (!pendingCommit)
                {
                    if (!contactPreview) support?.CommitPresentedContact(false);
                    return;
                }
                pendingCommit = false;
                // Supporting presentation can run between Apply and Commit.
                // An arriving/moved wall must be visible even at the same clock.
                GatherObstacles();
                float finalDepth = Depth();
                bool finalSweepBlocked = finalDepth <= 0f && hasLast && !SweepClear(lastWeapon, WeaponPose);
                bool clear = finalDepth <= 0f && !finalSweepBlocked;
                if (!clear && support != null)
                {
                    // A blocked supporting reach opens the hand; it cannot push its
                    // forearm through the bar merely to satisfy a two-handed socket.
                    support.RejectObstructedPose(finalDepth > 0f ? "weapon_commit_depth" : "weapon_commit_sweep", BlockingShape, finalDepth);
                    clear = Depth() <= 0f && (!hasLast || SweepClear(lastWeapon, WeaponPose));
                }
                if (clear && !contactPreview) Remember();
                else if (!clear)
                {
                    MotionBlocked = Blocked = true;
                    string rejectedShape = BlockingShape;
                    Collider rejectedCollider = BlockingCollider;
                    CombatActor rejectedActor = BlockingActor;
                    RestoreLastSafePose();
                    PenetrationDepth = Depth();
                    BlockingShape = rejectedShape ?? BlockingShape;
                    if (rejectedShape != null) { BlockingCollider = rejectedCollider; BlockingActor = rejectedActor; }
                }
                if (!contactPreview) support?.CommitPresentedContact(clear);
                JournalConstraint(clear ? "committed" : "commit_rejected");
            }
            finally { ClearAnatomyReuse(); }
        }

        private void RestoreLastSafePose()
        {
            ClearAnatomyReuse();
            if (!hasLast) return;
            for (int i = 0; i < poseBones.Length; i++)
            {
                fallbackBasePositions[i] = actor.transform.InverseTransformPoint(poseBones[i].position);
                fallbackBaseRotations[i] = Quaternion.Inverse(actor.transform.rotation) * poseBones[i].rotation;
            }
            fullPoseApplied = true;
            // Reject the complete obstructed step, including its body/root motion.
            if (actor.Body != null && actor.Body.enabled)
                actor.Body.Move(safeRootPosition - actor.transform.position);
            else actor.transform.position = safeRootPosition;
            actor.transform.rotation = safeRootRotation;
            // A new obstacle can clip Move. Keep the rig attached to its achieved root.
            Vector3 remaining = actor.transform.position - safeRootPosition;
            for (int i = 0; i < poseBones.Length; i++)
                poseBones[i].SetPositionAndRotation(safePositions[i] + remaining, safeRotations[i]);
        }

        private void JournalConstraint(string reason)
        {
            if (contactPreview || actor.Journal == null) return;
            int state = (Blocked ? 1 : 0) | (WorldBlocked ? 2 : 0) | (MotionBlocked ? 4 : 0) | (SupportBlocked ? 8 : 0);
            // A normal acceptance/commit pair is one state, not two events on
            // every render. Failed commits retain their distinct causal reason.
            if (reason == "committed") reason = SupportBlocked ? "support_contact" : Blocked ? "arm_corrected" : "clear";
            if (state == journalConstraintState && reason == journalConstraintReason) return;
            journalConstraintState = state; journalConstraintReason = reason;
            actor.JournalEvent("weapon_constraint", action: actor.State.AttackSequence,
                f0: GameLog.Field("reason", reason), f1: GameLog.Field("blocked", Blocked),
                f2: GameLog.Field("world_blocked", WorldBlocked), f3: GameLog.Field("motion_blocked", MotionBlocked),
                f4: GameLog.Field("desired_depth", journalDesiredDepth), f5: GameLog.Field("desired_sweep_blocked", journalDesiredSweep),
                f6: GameLog.Field("remaining_depth", PenetrationDepth), f7: GameLog.Field("shape", BlockingShape));
        }

        private void Remember()
        {
            if (contactPreview) return;
            lastUpper = Quaternion.Inverse(actor.transform.rotation) * upper.rotation;
            lastForearm = forearm.localRotation; lastHand = hand.localRotation;
            lastWeapon = WeaponPose; hasLast = true; PenetrationDepth = 0f;
            safeRootPosition = actor.transform.position; safeRootRotation = actor.transform.rotation;
            for (int i = 0; i < poseBones.Length; i++)
            { safePositions[i] = poseBones[i].position; safeRotations[i] = poseBones[i].rotation; }
            for (int i = 0; i < anatomy.Count; i++)
                previousAnatomy[i] = new Pose(anatomy[i].transform.position, anatomy[i].transform.rotation);
            for (int i = 0; i < otherAnatomy.Count; i++)
                if (otherAnatomy[i] != null)
                    previousOtherAnatomy[i] = OtherInput(i).CurrentPose;
            RememberRecoveryFloorContacts();
        }

        private void GatherObstacles()
        {
            using var timing = GatherMarker.Auto();
            obstacles.Clear();
            WorldQueries++;
            actor.JournalPhysicsQuery();
            int count = Physics.OverlapSphereNonAlloc(upper.position, 1.7f, nearby, ~0, QueryTriggerInteraction.Ignore);
            Collider[] found = nearby;
            if (!(count < nearby.Length))
            {
                actor.JournalQueryBufferFull(3, "weapon_obstacles", nearby.Length);
                WorldQueries++;
                actor.JournalPhysicsQuery();
                found = Physics.OverlapSphere(upper.position, 1.7f, ~0, QueryTriggerInteraction.Ignore);
            }
            if (found != nearby) count = found.Length;
            for (int i = 0; i < count; i++)
            {
                Collider hit = found[i];
                if (WorldObstacle(hit)) obstacles.Add(hit);
            }
            // Foreign anatomy cannot move inside this synchronous shoulder solve.
            // Prepare it once; Commit gathers again after the supporting hand has
            // run. Keep it separate from world obstacles: these sets cannot overlap
            // and adding every body with List.Contains made population work quadratic.
            otherDepthShapes.Clear();
            for (int i = 0; i < otherAnatomy.Count; i++)
            {
                ObserveOtherGeneration(i);
                AnatomyStamp current = gatheredOtherAnatomy[i] = CaptureOtherInput(i);
                if (!current.CanParticipate) continue;
                PrepareOtherAnatomy(i, current);
                otherDepthShapes.Add(otherSweep[i].EndShape(otherAnatomy[i], otherOwners[i]));
            }
            otherSnapshotReady = true;
        }

        private bool WorldObstacle(Collider hit) => hit != null && hit != probe && !hit.isTrigger &&
            !(hit is CharacterController) && !hit.transform.IsChildOf(actor.transform) &&
            !hit.transform.IsChildOf(weapon) && hit.GetComponentInParent<CombatActor>() == null;

        private float CandidateDepth()
        {
            CandidateChecks++;
            if (!armClearance.IsClear())
            {
                if (checkingDesiredPath) requestedOwnGeometryBlocked = true;
                // A body correction must not hide a simultaneous wall contact
                // from the attack preview's obstacle result.
                if (checkingDesiredPath)
                {
                    float weaponDepth = Depth();
                    if (weaponDepth > 0f) return weaponDepth;
                }
                BlockingShape = armClearance.LastBlockingShape;
                BlockingCollider = null; BlockingActor = actor;
                return .02f;
            }
            return Depth();
        }

        private float Depth()
        {
            using var timing = DepthMarker.Auto();
            float maximum = 0f;
            BlockingShape = null;
            BlockingCollider = null; BlockingActor = null;
            Pose pose = WeaponPose;
            // Each shoulder candidate moves live anatomy. Snapshot it once here,
            // not once per weapon segment, and use tight conservative AABBs even
            // for disabled ragdoll shapes (whose Collider.bounds would be empty).
            depthShapes.Clear();
            foreach (Collider shape in obstacles)
                if (shape != null) depthShapes.Add(new DepthShape(shape, checkingDesiredPath));
            Bounds weaponBounds = WeaponBounds(pose);
            foreach (DepthShape shape in otherDepthShapes)
            {
                // Cull once against the union before the eight segment passes.
                // This uses complete primitive bounds, not actor distance or focus.
                if (!shape.Bounds.Intersects(weaponBounds)) { OtherAnatomyDepthCulled++; continue; }
                depthShapes.Add(shape.WithQuery(anatomyQueries.Prepare(shape.Collider))); OtherAnatomyDepthCandidates++;
            }
            foreach (Collider shape in anatomy)
                if (shape != null && actor.Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(shape))
                {
                    var live = new DepthShape(shape, false, actor);
                    if (live.Bounds.Intersects(weaponBounds))
                        depthShapes.Add(live.WithQuery(anatomyQueries.Prepare(shape)));
                }
            for (int index = 0; index < CombatWeaponGeometry.Segments.Count; index++)
            {
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[index];
                Vector3 a = pose.position + pose.rotation * segment.A;
                Vector3 b = pose.position + pose.rotation * segment.B;
                Vector3 center = (a + b) * .5f;
                float radius = segment.Radius + Skin;
                if (Mathf.Min(a.y, b.y) - radius < actor.transform.position.y + .06f)
                {
                    float floorDepth = Mathf.Max(BelowFloor(a, radius, index), BelowFloor(b, radius, index));
                    if (floorDepth > 0f)
                    { if (checkingDesiredPath) RecordWorldContact(1f, a.y < b.y ? a : b, Vector3.up);
                        BlockingShape = "supporting floor"; SetEscape(a.y < b.y ? a : b, Vector3.up, floorDepth); return floorDepth; }
                }
                probe.radius = radius; probe.height = Vector3.Distance(a, b) + radius * 2f;
                Quaternion rotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
                Bounds segmentBounds = new Bounds(a, Vector3.zero);
                segmentBounds.Encapsulate(b);
                segmentBounds.Expand(radius * 2f);
                foreach (DepthShape shape in depthShapes)
                {
                    if (!shape.Bounds.Intersects(segmentBounds)) continue;
                    actor.JournalPhysicsQuery();
                    if (Physics.ComputePenetration(probe, center, rotation, shape.Query, shape.Pose.position,
                        shape.Pose.rotation, out Vector3 direction, out float depth))
                    {
                        if (AllowsRecoveryFloorContact(index, shape.Collider, depth) &&
                            RecoveryCoreClear(segment, a, b, shape.Collider)) continue;
                        if (shape.WorldObstacle) RecordWorldContact(1f, center - direction * radius, direction);
                        if (depth > maximum)
                        { maximum = depth; BlockingShape = shape.Collider.name;
                            BlockingCollider = shape.Collider; BlockingActor = shape.Owner;
                            SetEscape(center, direction, depth); }
                        if (checkingDesiredPath && depth > 0f)
                        {
                            if (shape.Owner == actor) requestedOwnGeometryBlocked = true;
                            else if (shape.Owner != null) RequestedBodyContactActor = shape.Owner;
                        }
                        if (checkingDesiredPath && rightForearmIndex >= 0 && shape.Collider == anatomy[rightForearmIndex])
                        {
                            RecordOwnForearmPenetration(index, a, b, radius, shape, direction, depth);
                        }
                        if (maximum > 0f && !checkingDesiredPath) return maximum;
                    }
                }
            }
            return maximum;
        }

        private static Bounds WeaponBounds(Pose pose)
        {
            Bounds bounds = default;
            for (int i = 0; i < CombatWeaponGeometry.Segments.Count; i++)
            {
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[i];
                Vector3 a = pose.position + pose.rotation * segment.A;
                Vector3 b = pose.position + pose.rotation * segment.B;
                Bounds part = new Bounds(a, Vector3.zero); part.Encapsulate(b);
                part.Expand((segment.Radius + Skin) * 2f);
                if (i == 0) bounds = part; else bounds.Encapsulate(part);
            }
            return bounds;
        }

        private readonly struct DepthShape
        {
            internal readonly Collider Collider;
            internal readonly Collider Query;
            internal readonly Pose Pose;
            internal readonly Bounds Bounds;
            internal readonly bool WorldObstacle;
            internal readonly CombatActor Owner;

            internal DepthShape(Collider shape, bool worldObstacle, CombatActor owner = null)
            {
                Collider = Query = shape;
                Pose = new Pose(shape.transform.position, shape.transform.rotation);
                Bounds = new CombatWeaponGeometry.ShapeBounds(shape).At(Pose.position, Pose.rotation);
                WorldObstacle = worldObstacle;
                Owner = owner;
            }

            internal DepthShape(Collider shape, Pose pose, Bounds bounds, CombatActor owner)
            { Collider = Query = shape; Pose = pose; Bounds = bounds; WorldObstacle = false; Owner = owner; }

            private DepthShape(DepthShape source, Collider query)
            { Collider = source.Collider; Query = query; Pose = source.Pose; Bounds = source.Bounds;
                WorldObstacle = source.WorldObstacle; Owner = source.Owner; }

            internal DepthShape WithQuery(Collider query) => new DepthShape(this, query);
        }

        private void SetEscape(Vector3 point, Vector3 direction, float depth)
        {
            Vector3 lever = point - upper.position;
            escapeAxis = Vector3.Cross(lever, direction);
            float effectiveLever = Vector3.Cross(escapeAxis.normalized, lever).magnitude;
            escapeAngle = effectiveLever > .01f ? Mathf.Max(.5f, (depth + .002f) / effectiveLever * Mathf.Rad2Deg) : 0f;
            escapeAxis.Normalize();
        }

        private float BelowFloor(Vector3 point, float radius, int segmentIndex)
        {
            Vector3 origin = new Vector3(point.x, actor.transform.position.y + .5f, point.z);
            float length = origin.y - point.y + radius + .02f;
            if (length <= 0f) return 0f;
            WorldQueries++;
            actor.JournalPhysicsQuery();
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, sweeps, length, ~0, QueryTriggerInteraction.Ignore);
            float depth = 0f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweeps[i];
                if (!WorldObstacle(hit.collider) || hit.normal.y < .65f || hit.point.y > actor.transform.position.y + .06f) continue;
                float contactDepth = hit.point.y - (point.y - radius);
                if (contactDepth <= Skin + RecoveryContactTolerance &&
                    AllowsRecoveryFloorContact(segmentIndex, hit.collider, contactDepth)) continue;
                depth = Mathf.Max(depth, contactDepth);
            }
            return depth;
        }

        internal static Pose AttachedWeaponPoseAt(Pose from, Pose to, Pose armFrom, Pose armTo, float t)
        {
            RelativeWeaponPoses(from, to, armFrom, armTo, out Pose localFrom, out Pose localTo);
            Pose arm = CombatWeaponGeometry.At(armFrom, armTo, t);
            Pose local = CombatWeaponGeometry.At(localFrom, localTo, t);
            return new Pose(arm.position + arm.rotation * local.position, arm.rotation * local.rotation);
        }

        private static void RelativeWeaponPoses(Pose from, Pose to, Pose armFrom, Pose armTo,
            out Pose localFrom, out Pose localTo)
        {
            Quaternion first = Quaternion.Inverse(armFrom.rotation), last = Quaternion.Inverse(armTo.rotation);
            localFrom = new Pose(first * (from.position - armFrom.position), first * from.rotation);
            localTo = new Pose(last * (to.position - armTo.position), last * to.rotation);
        }

        private bool SweepClear(Pose from, Pose to)
        {
            using var timing = SweepMarker.Auto();
            if (!RecoveryFloorPathClear(from, to)) return false;
            float angle = Quaternion.Angle(from.rotation, to.rotation);
            float weaponTravel = Vector3.Distance(from.position, to.position) + angle * Mathf.Deg2Rad * .7f;
            Bounds travelBounds = BuildSegmentSweepBounds(from, to, angle);
            bool reuseAnatomy = CanReuseAnatomySweep(from, to);
            if (reuseAnatomy) RepeatedAnatomySweepsAvoided++;
            // The exact live-input guard proves these derived bounds/travel too.
            // Reusing only native query results would still rebuild every body
            // primitive and segment pair again during the final commit.
            float bodyTravel = reuseAnatomy ? clearAnatomyBodyTravel : 0f;
            bool nearOther = reuseAnatomy && clearAnatomyNearOther;
            bool rightForearmPrepared = false;
            if (!reuseAnatomy)
            {
                anatomySweepCandidates.Clear(); otherSweepCandidates.Clear();
                for (int i = 0; i < anatomy.Count; i++)
                    if (anatomy[i] != null && actor.Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(anatomy[i]))
                    {
                        anatomySweep[i] = new BodySweep(anatomy[i], previousAnatomy[i]);
                        // Its palm and the held weapon are one articulated
                        // linkage. A world-space chord is not their relative path.
                        anatomySweep[i].SegmentMask = i == rightForearmIndex ? uint.MaxValue : SweptPairs(anatomySweep[i].TravelBounds);
                        if (anatomySweep[i].SegmentMask == 0) continue;
                        anatomySweep[i].Query = anatomyQueries.Prepare(anatomy[i]);
                        anatomySweepCandidates.Add(i);
                        rightForearmPrepared |= i == rightForearmIndex;
                        bodyTravel = Mathf.Max(bodyTravel, anatomySweep[i].Travel);
                    }
                for (int i = 0; i < otherAnatomy.Count; i++)
                {
                    ObserveOtherGeneration(i);
                    if (!OtherAnatomyActive(i)) { otherAnatomyPrepared[i] = false; continue; }
                    PrepareOtherAnatomy(i);
                    otherSweep[i].SegmentMask = SweptPairs(otherSweep[i].TravelBounds);
                    if (otherSweep[i].SegmentMask == 0) { OtherAnatomySweepCulled++; continue; }
                    otherSweep[i].Query = anatomyQueries.Prepare(otherAnatomy[i]);
                    otherSweepCandidates.Add(i); OtherAnatomySweepCandidates++;
                    nearOther = true;
                    bodyTravel = Mathf.Max(bodyTravel, otherSweep[i].Travel);
                }
            }
            // A planted hand does not make the torso or the other fighter static.
            // Bound relative surface travel, including rotation about each shape's
            // pivot, before choosing either the fast path or interpolation spacing.
            Pose attachedFrom = default, attachedTo = default;
            float attachedTravel = 0f, attachedAngle = 0f;
            Bounds attachedForearmBounds = default;
            if (rightForearmPrepared)
            {
                RelativeWeaponPoses(from, to, anatomySweep[rightForearmIndex].From,
                    anatomySweep[rightForearmIndex].To, out attachedFrom, out attachedTo);
                attachedAngle = Quaternion.Angle(attachedFrom.rotation, attachedTo.rotation);
                attachedTravel = Vector3.Distance(attachedFrom.position, attachedTo.position) +
                    attachedAngle * Mathf.Deg2Rad * .7f;
                attachedForearmBounds = anatomySweep[rightForearmIndex].RelativeBounds;
            }
            // Test the attached pair in the forearm frame. Its common parent
            // orbit cancels exactly; a compensating wrist can still have real
            // relative travel even when both WORLD bar endpoints are identical.
            float motion = Mathf.Max(weaponTravel + bodyTravel, attachedTravel);
            if (motion < .001f) return true;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(motion / .025f,
                Mathf.Max(angle, attachedAngle) / 4f)), 1, 48);
            worldSweepBounds.Clear();
            foreach (Collider obstacle in obstacles)
                if (obstacle != null && obstacle.bounds.Intersects(travelBounds)) worldSweepBounds.Add(obstacle.bounds);
            bool nearWorld = worldSweepBounds.Count > 0;
            if (nearWorld && !WorldEnvelopeOccupied(travelBounds))
            { nearWorld = false; WorldBoundsCulls++; }
            Pose previous = from;
            bool anatomyBlocked = false;
            for (int step = 1; step <= steps; step++)
            {
                bool sampleWorldBlocked = false;
                SweepSamples++;
                // Only identical native casts in THIS time sample are reusable.
                // The next time/shoulder candidate/commit rereads the entire world.
                if (worldProbeStamp == int.MaxValue)
                { Array.Clear(worldProbeStamps, 0, worldProbeStamps.Length); worldProbeStamp = 0; }
                int sampleStamp = ++worldProbeStamp;
                float t = step / (float)steps;
                Pose next = new Pose(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));
                Pose attached = rightForearmPrepared
                    ? CombatWeaponGeometry.At(attachedFrom, attachedTo, t) : next;
                // Interpolate each body once per time sample, not once for every
                // weapon segment. The disabled rig shapes still use their live poses.
                if (!reuseAnatomy)
                {
                    foreach (int body in anatomySweepCandidates) anatomySweep[body].Sample(t);
                    foreach (int body in otherSweepCandidates) otherSweep[body].Sample(t);
                }
                for (int index = 0; index < CombatWeaponGeometry.Segments.Count; index++)
                {
                    CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[index];
                    Vector3 a = next.position + next.rotation * segment.A;
                    Vector3 b = next.position + next.rotation * segment.B;
                    Bounds segmentBounds = new Bounds(a, Vector3.zero);
                    segmentBounds.Encapsulate(b);
                    segmentBounds.Expand(segment.Radius * 2f);
                    probe.radius = segment.Radius;
                    probe.height = Vector3.Distance(a, b) + segment.Radius * 2f;
                    Quaternion capsuleRotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
                    for (int candidate = 0; !reuseAnatomy && candidate < anatomySweepCandidates.Count; candidate++)
                    {
                        int body = anatomySweepCandidates[candidate];
                        Vector3 bodyA = body == rightForearmIndex ? attached.position + attached.rotation * segment.A : a;
                        Vector3 bodyB = body == rightForearmIndex ? attached.position + attached.rotation * segment.B : b;
                        Bounds bodyBounds = segmentBounds;
                        if (body == rightForearmIndex)
                        {
                            bodyBounds = new Bounds(bodyA, Vector3.zero); bodyBounds.Encapsulate(bodyB);
                            bodyBounds.Expand(segment.Radius * 2f);
                        }
                        if ((anatomySweep[body].SegmentMask & (1u << index)) == 0 ||
                            !(body == rightForearmIndex ? attachedForearmBounds : anatomySweep[body].Bounds).Intersects(bodyBounds)) continue;
                        AnatomySweepQueries++;
                        actor.JournalPhysicsQuery();
                        Quaternion bodyRotation = body == rightForearmIndex && (bodyB - bodyA).sqrMagnitude > .000001f
                            ? Quaternion.FromToRotation(Vector3.up, bodyB - bodyA) : capsuleRotation;
                        Pose bodyPose = body == rightForearmIndex
                            ? new Pose(Vector3.zero, Quaternion.identity) : anatomySweep[body].Pose;
                        if (Physics.ComputePenetration(probe, (bodyA + bodyB) * .5f, bodyRotation,
                            anatomySweep[body].Query, bodyPose.position, bodyPose.rotation,
                            out _, out float overlap) && overlap > .001f)
                        {
                            BlockingShape = anatomy[body].name;
                            BlockingCollider = anatomy[body]; BlockingActor = actor;
                            if (checkingDesiredPath) requestedOwnGeometryBlocked = true;
                            if (!checkingDesiredPath || !nearWorld) return false;
                            anatomyBlocked = true;
                        }
                    }
                    if (nearOther && !reuseAnatomy)
                        foreach (int body in otherSweepCandidates)
                        {
                            Collider shape = otherAnatomy[body];
                            if ((otherSweep[body].SegmentMask & (1u << index)) == 0 ||
                                !otherSweep[body].Bounds.Intersects(segmentBounds)) continue;
                            AnatomySweepQueries++;
                            actor.JournalPhysicsQuery();
                            if (Physics.ComputePenetration(probe, (a + b) * .5f, capsuleRotation, otherSweep[body].Query,
                                otherSweep[body].Pose.position, otherSweep[body].Pose.rotation,
                                out _, out float overlap) && overlap > .001f)
                            {
                                BlockingShape = shape.name;
                                BlockingCollider = shape; BlockingActor = otherOwners[body];
                                if (checkingDesiredPath)
                                {
                                    RequestedBodyContactActor = otherOwners[body];
                                    if (requestedBlockingCollider == null)
                                    { requestedBlockingCollider = shape; requestedBlockingActor = otherOwners[body]; }
                                }
                                // Finish the requested interval's OWN geometry
                                // checks even after a foreign body intercepts it.
                                // Its rollback cannot authorize a hidden self-crossing.
                                if (!checkingDesiredPath) return false;
                                anatomyBlocked = true;
                            }
                        }
                    if (!nearWorld) continue;
                    Bounds segmentTravel = new Bounds(a, Vector3.zero);
                    segmentTravel.Encapsulate(b);
                    segmentTravel.Encapsulate(previous.position + previous.rotation * segment.A);
                    segmentTravel.Encapsulate(previous.position + previous.rotation * segment.B);
                    segmentTravel.Expand((segment.Radius + Skin) * 2f);
                    if (!NearWorld(segmentTravel)) continue;
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(segment.A, segment.B) / (segment.Radius * 1.25f)));
                    for (int i = 0; i <= samples; i++)
                    {
                        Vector3 local = Vector3.Lerp(segment.A, segment.B, i / (float)samples);
                        Vector3 start = previous.position + previous.rotation * local;
                        Vector3 delta = next.position + next.rotation * local - start;
                        if (delta.sqrMagnitude <= .00000001f) continue;
                        Bounds pointTravel = new Bounds(start, Vector3.zero);
                        pointTravel.Encapsulate(start + delta);
                        pointTravel.Expand((segment.Radius + Skin) * 2f);
                        if (!NearWorld(pointTravel)) continue;
                        int probeId = WorldProbes.Ids[index][i];
                        if (worldProbeStamps[probeId] == sampleStamp)
                        { RepeatedWorldQueriesAvoided++; continue; }
                        worldProbeStamps[probeId] = sampleStamp;
                        WorldQueries++;
                        actor.JournalPhysicsQuery();
                        int count = Physics.SphereCastNonAlloc(start, segment.Radius + Skin, delta.normalized,
                            sweeps, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
                        if (count == sweeps.Length)
                        {
                            actor.JournalQueryBufferFull(4, "weapon_constraint_sweep", sweeps.Length);
                            if (checkingDesiredPath) RecordWorldContact((step - 1f) / steps, start, -delta.normalized); return false;
                        }
                        for (int h = 0; h < count; h++)
                            if (WorldObstacle(sweeps[h].collider))
                            {
                                // The complete floor-contact path was checked above,
                                // including exact shaft clearance and monotone release.
                                if (HasRecoveryFloorContact(index, sweeps[h].collider)) continue;
                                BlockingShape = sweeps[h].collider.name;
                                if (!checkingDesiredPath) return false;
                                RecordWorldContact((step - 1f + sweeps[h].distance / delta.magnitude) / steps,
                                    sweeps[h].point, sweeps[h].normal);
                                sampleWorldBlocked = true;
                            }
                    }
                }
                if (sampleWorldBlocked) return false;
                previous = next;
            }
            if (!anatomyBlocked && !reuseAnatomy) RememberClearAnatomySweep(from, to, bodyTravel, nearOther);
            return !anatomyBlocked;
        }

        private bool WorldEnvelopeOccupied(Bounds travelBounds)
        {
            using var timing = WorldEnvelopeMarker.Auto();
            // A combined warehouse mesh's AABB can span empty rooms. This box
            // contains every segment's complete swept capsule, including skin
            // and the rotational bow. A complete native miss proves that none
            // of their casts can meet world geometry; collider bounds cannot.
            // Reread PhysX for every Sweep, including raw and final-commit calls.
            WorldQueries++;
            actor.JournalPhysicsQuery();
            int count = Physics.OverlapBoxNonAlloc(travelBounds.center, travelBounds.extents,
                nearby, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (count == nearby.Length)
            {
                // The missing tail may contain a wall even if every returned
                // collider is an excluded actor. Retain all original casts.
                actor.JournalQueryBufferFull(15, "weapon_sweep_envelope", nearby.Length);
                return true;
            }
            for (int i = 0; i < count; i++)
                if (WorldObstacle(nearby[i])) return true;
            return false;
        }

        private Bounds BuildSegmentSweepBounds(Pose from, Pose to, float angle)
        {
            // A slerped endpoint can bow outside its endpoint AABB. The arc's
            // sagitta bounds that bow even when translation is added linearly.
            float bow = 1f - Mathf.Cos(angle * Mathf.Deg2Rad * .5f);
            Bounds all = default;
            for (int i = 0; i < segmentSweepBounds.Length; i++)
            {
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[i];
                Bounds bounds = new Bounds(from.position + from.rotation * segment.A, Vector3.zero);
                bounds.Encapsulate(from.position + from.rotation * segment.B);
                bounds.Encapsulate(to.position + to.rotation * segment.A);
                bounds.Encapsulate(to.position + to.rotation * segment.B);
                float radius = segment.Radius + Skin + bow * Mathf.Max(segment.A.magnitude, segment.B.magnitude);
                bounds.Expand(radius * 2f);
                segmentSweepBounds[i] = bounds;
                if (i == 0) all = bounds;
                else all.Encapsulate(bounds);
            }
            return all;
        }

        private uint SweptPairs(Bounds body)
        {
            uint mask = 0;
            for (int i = 0; i < segmentSweepBounds.Length; i++)
                if (body.Intersects(segmentSweepBounds[i])) mask |= 1u << i;
            return mask;
        }

        private void ClearAnatomyReuse()
        { anatomyReuseActive = hasClearAnatomySweep = false; }

        private static bool SamePose(Pose a, Pose b) => a.position.Equals(b.position) && a.rotation.Equals(b.rotation);

        private bool CanReuseAnatomySweep(Pose from, Pose to)
        {
            if (!anatomyReuseActive || contactPreview || !hasClearAnatomySweep ||
                !SamePose(from, clearAnatomyFrom) || !SamePose(to, clearAnatomyTo) ||
                probe.enabled != clearAnatomyProbeEnabled || probe.gameObject.activeInHierarchy != clearAnatomyProbeActive ||
                probe.isTrigger != clearAnatomyProbeTrigger || probe.direction != clearAnatomyProbeDirection ||
                !probe.transform.lossyScale.Equals(clearAnatomyProbeScale) || !probe.center.Equals(clearAnatomyProbeCenter)) return false;
            for (int i = 0; i < anatomy.Count; i++)
                if (!clearAnatomy[i].Matches(anatomy[i], previousAnatomy[i], actor)) return false;
            for (int i = 0; i < otherAnatomy.Count; i++)
                if (!clearOtherAnatomy[i].Matches(OtherInput(i))) return false;
            return true;
        }

        private void RememberClearAnatomySweep(Pose from, Pose to, float bodyTravel, bool nearOther)
        {
            if (!anatomyReuseActive || contactPreview) return;
            clearAnatomyBodyTravel = bodyTravel; clearAnatomyNearOther = nearOther;
            clearAnatomyFrom = from; clearAnatomyTo = to; clearAnatomyProbeEnabled = probe.enabled;
            clearAnatomyProbeActive = probe.gameObject.activeInHierarchy; clearAnatomyProbeTrigger = probe.isTrigger;
            clearAnatomyProbeDirection = probe.direction; clearAnatomyProbeScale = probe.transform.lossyScale;
            clearAnatomyProbeCenter = probe.center;
            for (int i = 0; i < anatomy.Count; i++) clearAnatomy[i] = new AnatomyStamp(anatomy[i], previousAnatomy[i], actor);
            for (int i = 0; i < otherAnatomy.Count; i++)
                clearOtherAnatomy[i] = OtherInput(i);
            hasClearAnatomySweep = true;
        }

        // Only clear anatomical sweeps survive until this Apply's final Commit.
        // The supporting arm can move between them: exact LIVE primitive inputs,
        // not a pose clock/frame or approximate hash, determine whether reuse is safe.
        private readonly struct AnatomyStamp
        {
            private readonly Collider shape;
            private readonly Pose previous, current;
            private readonly Vector3 scale, center, size;
            private readonly float radius, height;
            private readonly int direction;
            private readonly bool enabled, active, trigger, supported;
            private readonly bool ownerEnabled, allowed;
            private readonly int generation;
            internal Pose CurrentPose => current;
            internal bool CanParticipate => shape != null && ownerEnabled && active && allowed;

            internal AnatomyStamp(Collider value, Pose prior, CombatActor owner = null)
            {
                shape = value; previous = prior;
                ownerEnabled = owner == null || owner.isActiveAndEnabled;
                allowed = value != null && (owner == null || owner.Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(value));
                generation = owner != null ? owner.ContactGeneration : 0;
                current = value != null ? new Pose(value.transform.position, value.transform.rotation) : default;
                scale = value != null ? value.transform.lossyScale : default;
                center = size = default; radius = height = 0f; direction = 0;
                enabled = value != null && value.enabled;
                active = value != null && value.gameObject.activeInHierarchy;
                trigger = value != null && value.isTrigger;
                supported = value == null;
                if (value is CapsuleCollider capsule)
                { center = capsule.center; radius = capsule.radius; height = capsule.height; direction = capsule.direction; supported = true; }
                else if (value is BoxCollider box)
                { center = box.center; size = box.size; supported = true; }
                else if (value is SphereCollider sphere)
                { center = sphere.center; radius = sphere.radius; supported = true; }
            }

            internal bool Matches(Collider value, Pose prior, CombatActor owner = null) =>
                Matches(new AnatomyStamp(value, prior, owner));

            internal bool Matches(AnatomyStamp next)
            {
                return supported && next.supported && ReferenceEquals(shape, next.shape) &&
                    SamePose(previous, next.previous) && SamePose(current, next.current) &&
                    scale.Equals(next.scale) && center.Equals(next.center) && size.Equals(next.size) &&
                    radius.Equals(next.radius) && height.Equals(next.height) && direction == next.direction &&
                    enabled == next.enabled && active == next.active && trigger == next.trigger &&
                    ownerEnabled == next.ownerEnabled && allowed == next.allowed && generation == next.generation;
            }
        }

        // No foreign rig or PhysX simulation runs inside one synchronous solve.
        // Gather rereads exact inputs at Apply and final Commit; the supporting
        // hand may change only this actor. Standalone/preview sweep queries outside
        // a solve still reread live inputs, regardless of frame or animation clock.
        private readonly struct OtherSnapshotScope : IDisposable
        {
            private readonly CombatWeaponConstraint owner;
            private readonly bool active, ready;
            internal OtherSnapshotScope(CombatWeaponConstraint value)
            {
                owner = value; active = value.otherSnapshotActive; ready = value.otherSnapshotReady;
                value.otherSnapshotActive = true; value.otherSnapshotReady = false;
            }
            public void Dispose()
            { owner.otherSnapshotActive = active; owner.otherSnapshotReady = ready; }
        }

        private sealed class WorldProbeLayout
        {
            internal readonly int[][] Ids;
            internal readonly int Count;
            internal WorldProbeLayout(int[][] ids, int count) { Ids = ids; Count = count; }
        }

        private static WorldProbeLayout BuildWorldProbeLayout()
        {
            var unique = new List<(Vector3 Point, float Radius)>();
            var ids = new int[CombatWeaponGeometry.Segments.Count][];
            for (int segmentIndex = 0; segmentIndex < ids.Length; segmentIndex++)
            {
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[segmentIndex];
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(segment.A, segment.B) / (segment.Radius * 1.25f)));
                ids[segmentIndex] = new int[samples + 1];
                for (int i = 0; i <= samples; i++)
                {
                    // Exact equality only: close points and different radii must
                    // retain their independent swept collision coverage.
                    Vector3 point = Vector3.Lerp(segment.A, segment.B, i / (float)samples);
                    int id = -1;
                    for (int existing = 0; existing < unique.Count; existing++)
                        if (unique[existing].Point.Equals(point) && unique[existing].Radius.Equals(segment.Radius))
                        { id = existing; break; }
                    if (id < 0) { id = unique.Count; unique.Add((point, segment.Radius)); }
                    ids[segmentIndex][i] = id;
                }
            }
            return new WorldProbeLayout(ids, unique.Count);
        }

        private bool NearWorld(Bounds travel)
        {
            foreach (Bounds bounds in worldSweepBounds)
                if (bounds.Intersects(travel)) return true;
            return false;
        }

        private struct BodySweep
        {
            private readonly Pose from, to;
            internal Pose From => from;
            internal Pose To => to;
            private readonly CombatWeaponGeometry.ShapeBounds geometry;
            internal Bounds RelativeBounds => geometry.At(Vector3.zero, Quaternion.identity);
            internal readonly float Travel;
            internal readonly Bounds TravelBounds;
            internal uint SegmentMask;
            internal Collider Query;
            internal Pose Pose;
            internal Bounds Bounds;

            internal BodySweep(Collider shape, Pose previous)
            {
                from = previous;
                to = new Pose(shape.transform.position, shape.transform.rotation);
                geometry = new CombatWeaponGeometry.ShapeBounds(shape);
                float radius = geometry.PivotRadius;
                Travel = Vector3.Distance(from.position, to.position) + Quaternion.Angle(from.rotation, to.rotation) * Mathf.Deg2Rad * radius;
                TravelBounds = new Bounds(from.position, Vector3.zero);
                TravelBounds.Encapsulate(to.position);
                TravelBounds.Expand(radius * 2f);
                Pose = default; Bounds = default;
                SegmentMask = 0;
                Query = null;
            }

            internal void Sample(float t)
            {
                Pose = new Pose(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));
                Bounds = geometry.At(Pose.position, Pose.rotation);
            }

            internal DepthShape EndShape(Collider shape, CombatActor owner) =>
                new DepthShape(shape, to, geometry.At(to.position, to.rotation), owner);
        }

        internal PreviewScope BeginContactPreview() => new PreviewScope(this);

        private void RecordWorldContact(float fraction, Vector3 point, Vector3 normal)
        {
            WorldBlocked = true;
            if (fraction >= WorldContactFraction) return;
            WorldContactFraction = fraction; WorldContactPoint = point; WorldContactNormal = normal;
        }

        internal readonly struct PreviewScope : IDisposable
        {
            private readonly CombatWeaponConstraint owner;
            private readonly bool blocked, worldBlocked, supportBlocked, motionBlocked;
            private readonly float penetration;
            private readonly float worldFraction;
            private readonly Vector3 worldPoint, worldNormal;
            private readonly string shape;
            private readonly CombatSupportGrip.ContactPreviewScope supportPreview;
            internal PreviewScope(CombatWeaponConstraint constraint)
            {
                owner = constraint; blocked = owner.Blocked; motionBlocked = owner.MotionBlocked;
                supportPreview = owner.actor.SupportGrip != null ? owner.actor.SupportGrip.BeginContactPreview() : default;
                owner.ClearAnatomyReuse();
                worldBlocked = owner.WorldBlocked;
                worldFraction = owner.WorldContactFraction; worldPoint = owner.WorldContactPoint; worldNormal = owner.WorldContactNormal;
                supportBlocked = owner.SupportBlocked;
                penetration = owner.PenetrationDepth; shape = owner.BlockingShape; owner.contactPreview = true;
            }
            public void Dispose()
            {
                owner.ClearAnatomyReuse();
                owner.contactPreview = false; owner.Blocked = blocked; owner.MotionBlocked = motionBlocked;
                owner.WorldBlocked = worldBlocked;
                owner.WorldContactFraction = worldFraction; owner.WorldContactPoint = worldPoint; owner.WorldContactNormal = worldNormal;
                owner.SupportBlocked = supportBlocked;
                owner.PenetrationDepth = penetration; owner.BlockingShape = shape;
                supportPreview.Dispose();
            }
        }

        private static int DepthOf(Transform bone)
        { int depth = 0; while (bone != null) { depth++; bone = bone.parent; } return depth; }

        public void Dispose()
        {
            Forget();
            armClearance.Dispose();
            anatomyQueries.Dispose();
            if (probe != null) UnityEngine.Object.Destroy(probe.gameObject);
        }
    }
}
