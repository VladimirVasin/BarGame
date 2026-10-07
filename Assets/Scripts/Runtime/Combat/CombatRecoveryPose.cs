using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored one-hand combat recovery on the original rig. The bank owns
    /// the joint arcs and support transfers; runtime owns landing alignment,
    /// anatomical limits and the continuous duel clock.</summary>
    internal sealed class CombatRecoveryPose : IDisposable
    {
        private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Prepare");
        private static readonly ProfilerMarker BeginMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Begin");
        private static readonly ProfilerMarker PoseMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Pose");
        private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Sample");
        private static readonly ProfilerMarker SolesMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Soles");
        private readonly Transform actor, ownedWeapon, pelvis, chest, leftFoot, rightFoot;
        private readonly Transform leftThigh, leftShin, rightThigh, rightShin;
        private readonly CharacterController capsule;
        private readonly CombatRagdoll ragdoll;
        private readonly NpcHandPose hands;
        private readonly GameObject sampler;
        private readonly bool npc;
        private readonly List<BoneBinding> bindings = new List<BoneBinding>();
        private readonly Player3DFootGroundProbe footProbe;
        private readonly Vector3 pelvisDown, pelvisForward, pelvisRight, leftKneeForward, rightKneeForward;
        private readonly Vector3 leftHipPosition, leftKneePosition, leftAnklePosition, rightHipPosition, rightKneePosition, rightAnklePosition;
        private readonly Quaternion leftAnkleRest, rightAnkleRest;
        private readonly RaycastHit[] floorHits = new RaycastHit[24];
        private readonly Collider[] clearanceHits = new Collider[24];
        private readonly ClearanceRefusal[] clearanceRefusals = new ClearanceRefusal[17];
        private ClearanceRefusal clearanceCandidate;
        private Collider floorSurface;
        internal int ClearanceRefusalCount { get; private set; }
        internal ClearanceRefusal GetClearanceRefusal(int index) => clearanceRefusals[index];

        internal struct ClearanceRefusal
        {
            internal string Reason;
            internal Collider Obstacle, Floor;
            internal int CandidateIndex;
            internal Vector3 Candidate, PathFrom, PathTo, CapsuleTop, CapsuleBottom, FloorPoint, FloorNormal;
            internal float CapsuleRadius;
            internal bool PathTested, CapsuleTested, FloorTested;
        }
        private AnimationClip clip;
        private AnimationClip sampledClip;
        private float sampledTime;
        private float elapsed;
        private float minimumBlend;
        private bool begun;
        public float ClipProgress => begun ? Mathf.Clamp01(elapsed / clip.length) : 0f;
        public bool FeetSupported { get; private set; }
        public bool HandsReleased => begun && ClipProgress >= .80f && FeetSupported;
        public bool IsComplete => begun && ClipProgress >= 1f;
        public string ClipName { get; private set; }
        public string StageLabel => !begun ? "landing" : ClipProgress < .32f ? "brace" :
            ClipProgress < .52f ? "gather" : ClipProgress < .72f ?
                (ClipName == CombatAssetProvider.RiseSupineClip ? "squat" : "half-kneel") :
            ClipProgress < .80f ? "stand" : "regrip";
        public string SupportReason => FeetSupported ? "boots" : StageLabel;

        internal CombatRecoveryPose(Transform actorRoot, Transform rig, CharacterController body,
            CombatRagdoll physics, NpcHandPose handPose, bool isNpc, Transform weapon)
        {
            using var marker = PrepareMarker.Auto();
            actor = actorRoot; ownedWeapon = weapon; capsule = body; ragdoll = physics; hands = handPose; npc = isNpc;
            Transform Bone(string name) => CityPedestrianHandProps.FindSocket(rig, name) ??
                throw new InvalidOperationException("Combat recovery requires " + name);
            pelvis = Bone("pelvis"); chest = Bone("chest");
            leftFoot = Bone("foot.L"); rightFoot = Bone("foot.R");
            leftThigh = Bone("thigh.L"); leftShin = Bone("shin.L");
            rightThigh = Bone("thigh.R"); rightShin = Bone("shin.R");
            GameObject template = npc ? DefaultNpcCatalog.GetPrefab() : Resources.Load<GameObject>("Player/Player3DV2");
            Animator sourceAnimator = npc ? template.GetComponent<VillageResidentPresentation>()?.Animator :
                template != null ? template.GetComponentInChildren<Player3DAssetRegistry>(true)?.Animator : null;
            if (sourceAnimator == null)
                throw new InvalidOperationException("Combat recovery requires the matching production skeleton.");
            sampler = new GameObject("Combat Recovery Bone Sampler");
            sampler.transform.SetParent(actor, false);
            var rest = RestPositions(rig);
            leftHipPosition = rest[leftThigh]; leftKneePosition = rest[leftShin]; leftAnklePosition = rest[leftFoot];
            rightHipPosition = rest[rightThigh]; rightKneePosition = rest[rightShin]; rightAnklePosition = rest[rightFoot];
            // Limits use the imported bind frame, never a lying root's world axes
            // or the already bent first animation sample.
            Matrix4x4 BindFrame(Transform target)
            {
                foreach (SkinnedMeshRenderer skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    Transform[] bones = skin.bones;
                    Matrix4x4[] poses = skin.sharedMesh.bindposes;
                    for (int i = 0; i < bones.Length && i < poses.Length; i++)
                        if (bones[i] == target) return skin.localToWorldMatrix * poses[i].inverse;
                }
                throw new InvalidOperationException("Combat recovery needs the bind frame of " + target.name);
            }
            Matrix4x4 pelvisBind = BindFrame(pelvis).inverse;
            pelvisDown = pelvisBind.MultiplyVector(-actor.up).normalized;
            pelvisForward = pelvisBind.MultiplyVector(actor.forward).normalized;
            pelvisRight = pelvisBind.MultiplyVector(actor.right).normalized;
            leftKneeForward = BindFrame(leftThigh).inverse.MultiplyVector(actor.forward).normalized;
            rightKneeForward = BindFrame(rightThigh).inverse.MultiplyVector(actor.forward).normalized;
            leftAnkleRest = Quaternion.Inverse(BindFrame(leftShin).rotation) * BindFrame(leftFoot).rotation;
            rightAnkleRest = Quaternion.Inverse(BindFrame(rightShin).rotation) * BindFrame(rightFoot).rotation;
            // Generic clips bind by the full path under their Animator. The NPC and
            // hero banks share bone names, but have different armature parent paths.
            Transform sourceRoot = CityPedestrianHandProps.FindSocket(sourceAnimator.transform, "root");
            if (sourceRoot == null) throw new InvalidOperationException("The combat bank has no root joint.");
            Transform CopyParent(Transform original)
            {
                if (original == sourceAnimator.transform) return sampler.transform;
                return CopyTransform(original, CopyParent(original.parent));
            }
            void CopyBones(Transform original, Transform parent)
            {
                Transform copy = CopyTransform(original, parent);
                Transform target = CityPedestrianHandProps.FindSocket(rig, original.name);
                if (target != null && !original.name.StartsWith("SOCKET_", StringComparison.Ordinal))
                    bindings.Add(new BoneBinding(copy, target, rest.TryGetValue(target, out Vector3 position)
                        ? position : target.localPosition));
                for (int i = 0; i < original.childCount; i++) CopyBones(original.GetChild(i), copy);
            }
            CopyBones(sourceRoot, CopyParent(sourceRoot.parent));
            var heroRegistry = rig.GetComponentInParent<Player3DAssetRegistry>();
            var leftSoles = new List<SkinnedMeshRenderer>();
            var rightSoles = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.name.EndsWith("Sole.L", StringComparison.Ordinal)) leftSoles.Add(skin);
                if (skin.name.EndsWith("Sole.R", StringComparison.Ordinal)) rightSoles.Add(skin);
            }
            footProbe = heroRegistry != null ? Player3DFootGroundProbe.CreateForHero(heroRegistry, actor)
                : Player3DFootGroundProbe.Create(leftSoles, rightSoles, actor);
        }

        internal bool Begin(in PlayerRagdollLyingPose lying)
        {
            using var marker = BeginMarker.Auto();
            ClipName = lying.SelectRecoveryRoute() == PlayerRiseRoute.Seated
                ? CombatAssetProvider.RiseSupineClip : CombatAssetProvider.RiseProneClip;
            clip = CombatAssetProvider.LoadClip(ClipName, npc);
            Sample(0f);
            Vector3 authoredAxis = Vector3.ProjectOnPlane(chest.position - pelvis.position, Vector3.up);
            float yaw = authoredAxis.sqrMagnitude > .0001f && lying.LyingAxis.sqrMagnitude > .0001f
                ? Vector3.SignedAngle(authoredAxis, lying.LyingAxis, Vector3.up) : 0f;
            Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
            Vector3 target = lying.PelvisWorld - turn * (pelvis.position - actor.position);
            bool hasFloor = FindFloor(lying.PelvisWorld, out Vector3 floor, out Vector3 floorNormal);
            Collider landingFloor = floorSurface;
            if (hasFloor) target.y = floor.y + .02f;
            else target.y = actor.position.y;
            Quaternion rotation = turn * actor.rotation;
            if (!TryRecoveryRoot(target, rotation, lying.PelvisWorld, hasFloor, floor, floorNormal, landingFloor, out target))
            {
                ragdoll.PhysicsController.ApplyRecoveryBlend(0f);
                return false;
            }
            ragdoll.RebaseRecoveryRoot(target, rotation);
            begun = true;
            elapsed = 0f;
            minimumBlend = 0f;
            return true;
        }

        // Presentation may be evaluated several times per frame. Only Advance spends time.
        internal void Advance(float seconds)
        {
            if (begun && seconds > 0f && float.IsFinite(seconds))
                elapsed = Mathf.Min(clip.length, elapsed + seconds);
        }

        internal void RejectAdvance(float seconds) => elapsed = Mathf.Max(0f, elapsed - Mathf.Max(0f, seconds));

        internal void Present(bool gripOwnsLeft = false)
        {
            if (!begun) return;
            using var marker = PoseMarker.Auto();
            float blend = Mathf.Max(minimumBlend, Mathf.Clamp01(elapsed / .32f));
            bool fitted = PoseWithBlend(blend, out float leftSole, out float rightSole);
            if (!fitted && blend < 1f && PoseWithBlend(1f, out leftSole, out rightSole))
            {
                // A frozen landing can make an otherwise bounded hierarchy blend
                // infeasible. Keep the closest checked blend toward this SAME clip
                // time; no clock advance or root relocation bridges the bad pose.
                float invalid = blend, valid = 1f;
                for (int pass = 0; pass < 3; pass++)
                {
                    float candidate = (invalid + valid) * .5f;
                    if (PoseWithBlend(candidate, out leftSole, out rightSole)) valid = candidate;
                    else invalid = candidate;
                }
                minimumBlend = valid;
                fitted = PoseWithBlend(valid, out leftSole, out rightSole);
            }
            hands.SetGrip(false, 1f);
            if (!gripOwnsLeft) hands.SetGrip(true, 0f);
            FeetSupported = fitted && FootSupported(FootSide.Left, leftFoot, true, leftSole) &&
                FootSupported(FootSide.Right, rightFoot, true, rightSole);
        }

        private bool PoseWithBlend(float blend, out float leftSole, out float rightSole)
        {
            Sample(ClipProgress);
            ragdoll.PhysicsController.ApplyRecoveryBlend(blend);
            RestoreLegLinks(leftThigh, leftShin, leftFoot, leftHipPosition, leftKneePosition, leftAnklePosition);
            RestoreLegLinks(rightThigh, rightShin, rightFoot, rightHipPosition, rightKneePosition, rightAnklePosition);
            bool leftFitted = FitLeg(FootSide.Left, leftThigh, leftShin, leftFoot, leftKneeForward, leftAnkleRest, out leftSole);
            bool rightFitted = FitLeg(FootSide.Right, rightThigh, rightShin, rightFoot, rightKneeForward, rightAnkleRest, out rightSole);
            return leftFitted && rightFitted;
        }

        private static void RestoreLegLinks(Transform thigh, Transform shin, Transform foot,
            Vector3 hipPosition, Vector3 kneePosition, Vector3 anklePosition)
        {
            // PhysX's locked joints can retain small positional errors in the
            // frozen landing. Keep the visible segment directions while restoring
            // their actual bind offsets, so recovery never blends a stretched leg.
            Vector3 upper = shin.position - thigh.position;
            Vector3 lower = foot.position - shin.position;
            Quaternion footRotation = foot.rotation;
            thigh.localPosition = hipPosition;
            shin.localPosition = kneePosition;
            thigh.rotation = Quaternion.FromToRotation(shin.position - thigh.position, upper) * thigh.rotation;
            foot.localPosition = anklePosition;
            shin.rotation = Quaternion.FromToRotation(foot.position - shin.position, lower) * shin.rotation;
            foot.rotation = footRotation;
        }

        private bool ConstrainLeg(Transform thigh, Transform shin, Transform foot, Vector3 kneeForward, Quaternion ankleRest)
        {
            bool unchanged = true;
            Quaternion footRotation = foot.rotation;
            Vector3 down = pelvis.TransformDirection(pelvisDown).normalized;
            Vector3 forward = pelvis.TransformDirection(pelvisForward).normalized;
            Vector3 right = pelvis.TransformDirection(pelvisRight).normalized;
            Vector3 upper = (shin.position - thigh.position).normalized;
            float flexion = Mathf.Atan2(Vector3.Dot(upper, forward), Vector3.Dot(upper, down)) * Mathf.Rad2Deg;
            float limited = Mathf.Clamp(flexion, -30f, 110f);
            float lateral = Vector3.Dot(upper, right);
            float safeLateral = Mathf.Clamp(lateral, -Mathf.Sin(60f * Mathf.Deg2Rad), Mathf.Sin(60f * Mathf.Deg2Rad));
            if (Mathf.Abs(limited - flexion) > .001f || Mathf.Abs(lateral - safeLateral) > .00001f)
            {
                Vector3 desired = (down * Mathf.Cos(limited * Mathf.Deg2Rad) + forward * Mathf.Sin(limited * Mathf.Deg2Rad)) *
                    Mathf.Sqrt(Mathf.Max(0f, 1f - safeLateral * safeLateral)) + right * safeLateral;
                thigh.rotation = Quaternion.FromToRotation(upper, desired) * thigh.rotation;
                unchanged = false;
            }
            upper = (shin.position - thigh.position).normalized;
            Vector3 lower = (foot.position - shin.position).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(thigh.TransformDirection(kneeForward), upper).normalized;
            Vector3 hinge = Vector3.Cross(bend, upper).normalized;
            if (hinge.sqrMagnitude > .5f)
            {
                float knee = Vector3.SignedAngle(upper, lower, hinge);
                float safeKnee = Mathf.Clamp(knee, 0f, 130f);
                // A knee is a hinge, including during the frozen-pose blend and
                // floor IK. Endpoint reach alone cannot authorize a side/back fold.
                Vector3 desired = Quaternion.AngleAxis(safeKnee, hinge) * upper;
                if (Vector3.Angle(lower, desired) > .1f)
                {
                    shin.rotation = Quaternion.FromToRotation(lower, desired) * shin.rotation;
                    unchanged = false;
                }
            }
            foot.rotation = footRotation;
            Quaternion ankle = Quaternion.Inverse(shin.rotation) * foot.rotation;
            if (Quaternion.Angle(ankleRest, ankle) > 75f)
            {
                foot.rotation = shin.rotation * Quaternion.RotateTowards(ankleRest, ankle, 75f);
                unchanged = false;
            }
            return unchanged;
        }

        private bool FitLeg(FootSide side, Transform thigh, Transform shin, Transform foot,
            Vector3 kneeForward, Quaternion ankleRest, out float sole)
        {
            ConstrainLeg(thigh, shin, foot, kneeForward, ankleRest);
            sole = 0f;
            // Floor IK and anatomical limits share one bounded solve. A limit
            // applied after a single floor correction can lower the boot again.
            for (int pass = 0; pass < 4; pass++)
            {
                if (KeepSoleAboveFloor(side, thigh, shin, foot, kneeForward, out sole)) return true;
                ConstrainLeg(thigh, shin, foot, kneeForward, ankleRest);
            }
            // The last limit write can lower the tip. Only the FINAL sole proves
            // the displayed pose, and that bake also serves the support decision.
            return footProbe != null && FindFloor(foot.position, out Vector3 floor, out _) &&
                footProbe.TryGetSoleHeight(side, out sole) && sole >= floor.y - .001f;
        }

        private bool KeepSoleAboveFloor(FootSide side, Transform thigh, Transform shin, Transform foot, Vector3 kneeForward, out float sole)
        {
            using var marker = SolesMarker.Auto();
            sole = 0f;
            if (footProbe == null || !FindFloor(foot.position, out Vector3 floor, out _) ||
                !footProbe.TryGetSoleHeight(side, out sole)) return false;
            float lift = floor.y + .01f - sole;
            if (lift <= 0f) return true;
            // A hierarchy blend from an arbitrary lying pose can swing a boot
            // through the floor. Lift its actual sole toward the calibrated front
            // of the knee, retaining foot pitch and link lengths. A straight or
            // reversed frozen knee cannot supply its own bend hint.
            Vector3 target = foot.position + Vector3.up * lift;
            Vector3 bend = Vector3.ProjectOnPlane(thigh.TransformDirection(kneeForward), target - thigh.position).normalized;
            Vector3 hint = thigh.position + bend * .4f;
            LimbTwoBoneIk.Solve(thigh, shin, foot, target, foot.rotation, hint, 1f, .995f, true);
            return false;
        }

        private bool FootSupported(FootSide side, Transform foot, bool hasSole, float sole)
        {
            using var marker = SolesMarker.Auto();
            if (!FindFloor(foot.position, out Vector3 floor, out _)) return false;
            float height = hasSole || (footProbe != null && footProbe.TryGetSoleHeight(side, out sole))
                ? sole : foot.position.y - .07f;
            return height - floor.y >= -.04f && height - floor.y <= .06f;
        }

        internal bool HasStandingClearance()
        {
            ClearanceRefusalCount = 0;
            PrepareClearanceCandidate(0, actor.position, actor.rotation, actor.position);
            return HasStandingClearance(actor.position, actor.rotation);
        }

        private bool TryRecoveryRoot(Vector3 desired, Quaternion rotation, Vector3 lyingPelvis,
            bool hasFloor, Vector3 floorPoint, Vector3 floorNormal, Collider landingFloor, out Vector3 result)
        {
            result = desired;
            ClearanceRefusalCount = 0;
            PrepareClearanceCandidate(0, desired, rotation, lyingPelvis + Vector3.up * .12f);
            clearanceCandidate.FloorTested = true;
            clearanceCandidate.FloorPoint = floorPoint; clearanceCandidate.FloorNormal = floorNormal;
            clearanceCandidate.Floor = hasFloor ? landingFloor : null;
            if (HasStandingClearance(desired, rotation)) return true;
            // A nearby blocked capsule is not a reason to snap upright or give up. Search
            // a small, deterministic support neighbourhood; rebasing preserves the frozen
            // world's pose and the initial gathering arc brings it into the chosen support.
            for (int ring = 1; ring <= 2; ring++)
            {
                float radius = ring * .16f;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * .25f;
                    Vector3 delta = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    Vector3 candidate = desired + rotation * delta;
                    PrepareClearanceCandidate(1 + (ring - 1) * 8 + i, candidate, rotation, lyingPelvis + Vector3.up * .12f);
                    bool foundFloor = FindFloor(candidate + Vector3.up * .3f, out Vector3 floor, out Vector3 normal);
                    clearanceCandidate.FloorTested = true; clearanceCandidate.Floor = floorSurface;
                    clearanceCandidate.FloorPoint = floor; clearanceCandidate.FloorNormal = normal;
                    if (!foundFloor) { RejectClearance("floor_missing"); continue; }
                    if (Mathf.Abs(floor.y + .02f - desired.y) > .12f)
                    { RejectClearance("floor_height"); continue; }
                    candidate.y = floor.y + .02f;
                    clearanceCandidate.Candidate = candidate;
                    SetClearanceCapsule(candidate, rotation);
                    Vector3 travel = candidate - desired;
                    clearanceCandidate.PathTested = true;
                    clearanceCandidate.PathTo = clearanceCandidate.PathFrom + travel;
                    int hits = Physics.RaycastNonAlloc(lyingPelvis + Vector3.up * .12f, travel.normalized,
                        floorHits, travel.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    bool crossed = hits == floorHits.Length;
                    Collider pathObstacle = null; float nearest = float.PositiveInfinity;
                    for (int h = 0; h < hits; h++)
                        if (floorHits[h].collider != null && !OwnsCollider(floorHits[h].collider))
                        {
                            crossed = true;
                            if (floorHits[h].distance < nearest) { nearest = floorHits[h].distance; pathObstacle = floorHits[h].collider; }
                        }
                    if (crossed)
                    { RejectClearance(hits == floorHits.Length ? "path_buffer_full" : "path_obstacle", pathObstacle); continue; }
                    if (!HasStandingClearance(candidate, rotation)) continue;
                    result = candidate;
                    ClearanceRefusalCount = 0;
                    return true;
                }
            }
            return false;
        }

        private bool HasStandingClearance(Vector3 position, Quaternion rotation)
        {
            clearanceCandidate.CapsuleTested = true;
            float radius = Mathf.Max(.08f, capsule.radius - capsule.skinWidth);
            Vector3 centre = position + rotation * capsule.center;
            float half = Mathf.Max(0f, capsule.height * .5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * half,
                centre - Vector3.up * half, radius, clearanceHits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == clearanceHits.Length) return RejectClearance("capsule_buffer_full");
            for (int i = 0; i < count; i++)
                if (clearanceHits[i] != null && !OwnsCollider(clearanceHits[i]))
                    return RejectClearance("capsule_overlap", clearanceHits[i]);
            return true;
        }

        // The standing capsule reserves room for the body, not its own loose
        // equipment. Detaching the bar must preserve this ownership exclusion;
        // the actual prop retains its physical world and anatomy collisions.
        private bool OwnsCollider(Collider shape) => shape.transform.IsChildOf(actor) ||
            (ownedWeapon != null && shape.transform.IsChildOf(ownedWeapon));

        private void PrepareClearanceCandidate(int index, Vector3 candidate, Quaternion rotation, Vector3 pathFrom)
        {
            clearanceCandidate = new ClearanceRefusal { CandidateIndex = index, Candidate = candidate,
                PathFrom = pathFrom, PathTo = pathFrom };
            SetClearanceCapsule(candidate, rotation);
        }

        private void SetClearanceCapsule(Vector3 position, Quaternion rotation)
        {
            float radius = Mathf.Max(.08f, capsule.radius - capsule.skinWidth);
            Vector3 centre = position + rotation * capsule.center;
            float half = Mathf.Max(0f, capsule.height * .5f - radius);
            clearanceCandidate.CapsuleTop = centre + Vector3.up * half;
            clearanceCandidate.CapsuleBottom = centre - Vector3.up * half;
            clearanceCandidate.CapsuleRadius = radius;
        }

        private bool RejectClearance(string reason, Collider obstacle = null)
        {
            clearanceCandidate.Reason = reason; clearanceCandidate.Obstacle = obstacle;
            if (ClearanceRefusalCount < clearanceRefusals.Length)
                clearanceRefusals[ClearanceRefusalCount++] = clearanceCandidate;
            return false;
        }

        private bool FindFloor(Vector3 point, out Vector3 contact, out Vector3 normal)
        {
            contact = point; normal = Vector3.up;
            floorSurface = null;
            float closest = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * .65f, Vector3.down,
                floorHits, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = floorHits[i];
                if (hit.collider == null || OwnsCollider(hit.collider) ||
                    hit.collider.GetComponentInParent<CombatActor>() != null || hit.normal.y < .65f || hit.distance >= closest) continue;
                closest = hit.distance; contact = hit.point; normal = hit.normal; floorSurface = hit.collider;
            }
            return !float.IsPositiveInfinity(closest);
        }

        private void Sample(float normalized)
        {
            using var marker = SampleMarker.Auto();
            float time = Mathf.Clamp01(normalized) * clip.length;
            // Only this method writes the private sampler. Root/world changes
            // do not change its authored local pose; a new time/clip does.
            if (sampledClip != clip || sampledTime != time)
            {
                clip.SampleAnimation(sampler, time);
                sampledClip = clip;
                sampledTime = time;
            }
            ApplySample();
        }

        private void ApplySample()
        {
            foreach (BoneBinding bone in bindings)
            {
                bone.Target.localRotation = bone.Source.localRotation;
                bone.Target.localPosition = bone.TargetRest + bone.Source.localPosition - bone.SourceRest;
            }
        }

        private static Transform CopyTransform(Transform source, Transform parent)
        {
            var copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            copy.localScale = source.localScale;
            return copy;
        }

        private static Dictionary<Transform, Vector3> RestPositions(Transform rig)
        {
            var world = new Dictionary<Transform, Matrix4x4>();
            foreach (SkinnedMeshRenderer skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                Matrix4x4[] bind = skin.sharedMesh.bindposes;
                Transform[] bones = skin.bones;
                for (int i = 0; i < bones.Length && i < bind.Length; i++)
                    if (bones[i] != null && !world.ContainsKey(bones[i])) world.Add(bones[i], skin.localToWorldMatrix * bind[i].inverse);
            }
            var result = new Dictionary<Transform, Vector3>();
            foreach (var pair in world)
            {
                Matrix4x4 parent = pair.Key.parent != null && world.TryGetValue(pair.Key.parent, out Matrix4x4 rest)
                    ? rest : pair.Key.parent != null ? pair.Key.parent.localToWorldMatrix : Matrix4x4.identity;
                result.Add(pair.Key, (parent.inverse * pair.Value).GetColumn(3));
            }
            return result;
        }

        public void Dispose()
        {
            footProbe?.Dispose();
            if (sampler != null) UnityEngine.Object.Destroy(sampler);
        }

        private readonly struct BoneBinding
        {
            public readonly Transform Source, Target;
            public readonly Vector3 SourceRest, TargetRest;
            public BoneBinding(Transform source, Transform target, Vector3 rest)
            { Source = source; Target = target; SourceRest = source.localPosition; TargetRest = rest; }
        }
    }
}
