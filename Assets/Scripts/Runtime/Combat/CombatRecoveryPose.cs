using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored one-hand combat recovery on the original rig. The bank owns
    /// the joint arcs and support transfers; runtime owns landing alignment and the
    /// continuous duel clock. No drunken pose, spine reach search or second arm IK.</summary>
    internal sealed class CombatRecoveryPose : IDisposable
    {
        private readonly Transform actor, pelvis, chest, leftFoot, rightFoot;
        private readonly Transform leftThigh, leftShin, rightThigh, rightShin;
        private readonly CharacterController capsule;
        private readonly CombatRagdoll ragdoll;
        private readonly NpcHandPose hands;
        private readonly GameObject sampler;
        private readonly bool npc;
        private readonly List<BoneBinding> bindings = new List<BoneBinding>();
        private readonly Player3DFootGroundProbe footProbe;
        private readonly RaycastHit[] floorHits = new RaycastHit[24];
        private readonly Collider[] clearanceHits = new Collider[24];
        private AnimationClip clip;
        private float elapsed;
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
            CombatRagdoll physics, NpcHandPose handPose, bool isNpc)
        {
            actor = actorRoot; capsule = body; ragdoll = physics; hands = handPose; npc = isNpc;
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
            ClipName = lying.SelectRecoveryRoute() == PlayerRiseRoute.Seated
                ? CombatAssetProvider.RiseSupineClip : CombatAssetProvider.RiseProneClip;
            clip = CombatAssetProvider.LoadClip(ClipName, npc);
            Sample(0f);
            Vector3 authoredAxis = Vector3.ProjectOnPlane(chest.position - pelvis.position, Vector3.up);
            float yaw = authoredAxis.sqrMagnitude > .0001f && lying.LyingAxis.sqrMagnitude > .0001f
                ? Vector3.SignedAngle(authoredAxis, lying.LyingAxis, Vector3.up) : 0f;
            Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
            Vector3 target = lying.PelvisWorld - turn * (pelvis.position - actor.position);
            if (FindFloor(lying.PelvisWorld, out Vector3 floor, out _)) target.y = floor.y + .02f;
            else target.y = actor.position.y;
            Quaternion rotation = turn * actor.rotation;
            if (!TryRecoveryRoot(target, rotation, lying.PelvisWorld, out target))
            {
                ragdoll.PhysicsController.ApplyRecoveryBlend(0f);
                return false;
            }
            ragdoll.RebaseRecoveryRoot(target, rotation);
            begun = true;
            elapsed = 0f;
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
            Sample(ClipProgress);
            ragdoll.PhysicsController.ApplyRecoveryBlend(elapsed / .32f);
            KeepSoleAboveFloor(FootSide.Left, leftThigh, leftShin, leftFoot);
            KeepSoleAboveFloor(FootSide.Right, rightThigh, rightShin, rightFoot);
            hands.SetGrip(false, 1f);
            if (!gripOwnsLeft) hands.SetGrip(true, 0f);
            FeetSupported = FootSupported(FootSide.Left, leftFoot) && FootSupported(FootSide.Right, rightFoot);
        }

        private void KeepSoleAboveFloor(FootSide side, Transform thigh, Transform shin, Transform foot)
        {
            if (footProbe == null || !FindFloor(foot.position, out Vector3 floor, out _) ||
                !footProbe.TryGetSoleHeight(side, out float sole)) return;
            float lift = floor.y + .01f - sole;
            if (lift <= 0f) return;
            // A hierarchy blend from an arbitrary lying pose can swing a boot
            // through the floor. Lift its actual sole, retaining the current knee
            // plane and authored foot pitch; rotate the leg, never stretch it.
            LimbTwoBoneIk.Solve(thigh, shin, foot, foot.position + Vector3.up * lift,
                foot.rotation, shin.position, 1f, .995f, true);
        }

        private bool FootSupported(FootSide side, Transform foot)
        {
            if (!FindFloor(foot.position, out Vector3 floor, out _)) return false;
            float height = footProbe != null && footProbe.TryGetSoleHeight(side, out float sole)
                ? sole : foot.position.y - .07f;
            return height - floor.y >= -.04f && height - floor.y <= .06f;
        }

        internal bool HasStandingClearance() => HasStandingClearance(actor.position, actor.rotation);

        private bool TryRecoveryRoot(Vector3 desired, Quaternion rotation, Vector3 lyingPelvis, out Vector3 result)
        {
            result = desired;
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
                    if (!FindFloor(candidate + Vector3.up * .3f, out Vector3 floor, out _) ||
                        Mathf.Abs(floor.y + .02f - desired.y) > .12f) continue;
                    candidate.y = floor.y + .02f;
                    Vector3 travel = candidate - desired;
                    int hits = Physics.RaycastNonAlloc(lyingPelvis + Vector3.up * .12f, travel.normalized,
                        floorHits, travel.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    bool crossed = hits == floorHits.Length;
                    for (int h = 0; h < hits; h++)
                        if (floorHits[h].collider != null && !floorHits[h].collider.transform.IsChildOf(actor)) crossed = true;
                    if (crossed || !HasStandingClearance(candidate, rotation)) continue;
                    result = candidate;
                    return true;
                }
            }
            return false;
        }

        private bool HasStandingClearance(Vector3 position, Quaternion rotation)
        {
            float radius = Mathf.Max(.08f, capsule.radius - capsule.skinWidth);
            Vector3 centre = position + rotation * capsule.center;
            float half = Mathf.Max(0f, capsule.height * .5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * half,
                centre - Vector3.up * half, radius, clearanceHits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == clearanceHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (clearanceHits[i] != null && !clearanceHits[i].transform.IsChildOf(actor)) return false;
            return true;
        }

        private bool FindFloor(Vector3 point, out Vector3 contact, out Vector3 normal)
        {
            contact = point; normal = Vector3.up;
            float closest = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * .65f, Vector3.down,
                floorHits, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = floorHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(actor) ||
                    hit.collider.GetComponentInParent<CombatActor>() != null || hit.normal.y < .65f || hit.distance >= closest) continue;
                closest = hit.distance; contact = hit.point; normal = hit.normal;
            }
            return !float.IsPositiveInfinity(closest);
        }

        private void Sample(float normalized)
        {
            clip.SampleAnimation(sampler, Mathf.Clamp01(normalized) * clip.length);
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
