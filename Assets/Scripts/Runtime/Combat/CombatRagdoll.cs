using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Round-local ownership of the existing anatomical ragdoll, on either combat rig.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatRagdoll : MonoBehaviour
    {
        private Player3DRagdollController physicsController;
        private CharacterController capsule;
        private PlayerMotor motor;
        private PlayerBalanceController balance;
        private VillageResidentPresentation npc;
        private Animator npcAnimator;
        private LocalPose[] initialPose;
        private bool capsuleWasEnabled, motorWasEnabled, inputWasEnabled, balanceWasEnabled;
        private bool npcWasEnabled, animatorWasEnabled;
        private float simulationSeconds, quietSeconds;

        public bool IsActive { get; private set; }
        public bool IsSettled { get; private set; }
        public Player3DRagdollController PhysicsController => physicsController;
        public IReadOnlyList<Rigidbody> Bodies => physicsController != null
            ? physicsController.Bodies : Array.Empty<Rigidbody>();
        public Rigidbody PelvisBody => physicsController != null ? physicsController.PelvisBody : null;
        public int BodyCount => physicsController != null ? physicsController.BodyCount : 0;
        public float MaximumBodySpeed => physicsController != null ? physicsController.MaximumBodySpeed : 0f;

        public void InitializeHero(PlayerRuntime player)
        {
            if (physicsController != null) throw new InvalidOperationException("Combat ragdoll is already initialized.");
            if (!(player.Visual is Player3DCharacterPresentation hero) || player.Ragdoll == null ||
                !player.Ragdoll.IsInitialized)
                throw new ArgumentException("Combat requires the production hero's initialized ragdoll.", nameof(player));
            physicsController = player.Ragdoll;
            capsule = player.GameObject.GetComponent<CharacterController>();
            motor = player.Motor;
            balance = player.Balance;
            initialPose = CaptureSkeleton(hero.Registry.ModelRoot, null);
        }

        public void InitializeOpponent(VillageResidentPresentation presentation, CharacterController controller)
        {
            if (physicsController != null) throw new InvalidOperationException("Combat ragdoll is already initialized.");
            if (presentation == null || presentation.ModelRoot == null || controller == null)
                throw new ArgumentException("Combat opponent requires its authored rig and controller.");
            npc = presentation;
            npcAnimator = npc.Animator;
            capsule = controller;
            Dictionary<Player3DAnatomicalPart, Transform> anatomy = ResolveAnatomy(npc.ModelRoot);
            initialPose = CaptureSkeleton(npc.ModelRoot, anatomy.Values);
            npc.ReleaseAnimation();
            // Joint limits are measured from bind anatomy, never from a bent guard or idle.
            // The same rendered rig returns to its live pose before this method exits.
            try
            {
                RestoreSkinBindPose(npc.ModelRoot, anatomy);
                physicsController = controller.gameObject.AddComponent<Player3DRagdollController>();
                physicsController.Initialize(controller.transform, controller, npc.ModelRoot, anatomy, controller.height);
            }
            finally { RestorePose(initialPose); }
        }

        /// <summary>Called after the defeat clip's handoff sample. The live visible pose stays intact.</summary>
        public bool Begin(Vector3 impactDirection, Vector3 impactPoint)
        {
            if (physicsController == null || IsActive || physicsController.IsActive || !isActiveAndEnabled) return false;
            Vector3 direction = Finite(impactDirection) ? impactDirection : -transform.forward;
            direction.y = 0f;
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : -transform.forward;
            Vector3 rotation = Vector3.Cross(Vector3.up, direction) * 1.2f;
            float side = Vector3.Dot(direction, transform.right) < 0f ? -1f : 1f;
            var handoff = new PlayerRagdollHandoff(Vector3.zero, rotation, direction, transform.position, side);
            capsuleWasEnabled = capsule != null && capsule.enabled;
            motorWasEnabled = motor != null && motor.enabled;
            inputWasEnabled = motor != null && motor.InputEnabled;
            balanceWasEnabled = balance != null && balance.enabled;
            npcWasEnabled = npc != null && npc.enabled;
            animatorWasEnabled = npcAnimator != null && npcAnimator.enabled;
            if (!physicsController.Begin(handoff)) return false;

            IsActive = true;
            IsSettled = false;
            simulationSeconds = quietSeconds = 0f;
            if (capsule != null) capsule.enabled = false;
            if (motor != null) { motor.SetInputEnabled(false); motor.enabled = false; }
            if (balance != null) balance.enabled = false;
            if (npc != null) npc.enabled = false;
            if (npcAnimator != null) npcAnimator.enabled = false;
            Rigidbody chest = physicsController.ChestBody;
            Vector3 point = Finite(impactPoint) ? impactPoint : chest.worldCenterOfMass;
            point = chest.worldCenterOfMass + Vector3.ClampMagnitude(point - chest.worldCenterOfMass, .3f);
            chest.AddForceAtPosition(direction * .3f, point, ForceMode.VelocityChange);
            return true;
        }

        private void FixedUpdate()
        {
            // Shared pause stops PhysX. Neither the round's frozen simulation nor
            // presentation updates own this body's remaining fall time.
            if (!IsActive || IsSettled || Time.timeScale <= 0f || PauseMenuController.IsAnyPaused) return;
            simulationSeconds += Time.fixedDeltaTime;
            foreach (Rigidbody body in Bodies)
            {
                if (body == null || body.isKinematic) continue;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, 8f);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, 10f);
            }
            quietSeconds = MaximumBodySpeed < .12f ? quietSeconds + Time.fixedDeltaTime : 0f;
            if ((simulationSeconds >= 1f && quietSeconds >= .5f) || simulationSeconds >= 4f)
            {
                physicsController.FreezeInPlace();
                IsSettled = true;
            }
        }

        public void Cancel()
        {
            if (!IsActive) return;
            IsActive = IsSettled = false;
            simulationSeconds = quietSeconds = 0f;
            if (physicsController != null) physicsController.Cancel();
            RestorePose(initialPose);
            if (npcAnimator != null) npcAnimator.enabled = animatorWasEnabled;
            if (npc != null) npc.enabled = npcWasEnabled;
            if (balance != null) balance.enabled = balanceWasEnabled;
            if (motor != null) { motor.SetInputEnabled(inputWasEnabled); motor.enabled = motorWasEnabled; }
            if (capsule != null) capsule.enabled = capsuleWasEnabled;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();

        private static Dictionary<Player3DAnatomicalPart, Transform> ResolveAnatomy(Transform modelRoot)
        {
            var names = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in modelRoot.GetComponentsInChildren<Transform>(true)) names[bone.name] = bone;
            var result = new Dictionary<Player3DAnatomicalPart, Transform>();
            void Add(Player3DAnatomicalPart part, string name)
            {
                if (!names.TryGetValue(name, out Transform bone))
                    throw new InvalidOperationException("Combat NPC ragdoll is missing bone " + name);
                result.Add(part, bone);
            }
            Add(Player3DAnatomicalPart.Pelvis, "pelvis");
            Add(Player3DAnatomicalPart.LowerTorso, "spine");
            Add(Player3DAnatomicalPart.Torso, "chest");
            Add(Player3DAnatomicalPart.Neck, "neck");
            Add(Player3DAnatomicalPart.Head, "head");
            Add(Player3DAnatomicalPart.LeftUpperArm, "upper_arm.L");
            Add(Player3DAnatomicalPart.LeftForearm, "forearm.L");
            Add(Player3DAnatomicalPart.LeftHand, "hand.L");
            Add(Player3DAnatomicalPart.RightUpperArm, "upper_arm.R");
            Add(Player3DAnatomicalPart.RightForearm, "forearm.R");
            Add(Player3DAnatomicalPart.RightHand, "hand.R");
            Add(Player3DAnatomicalPart.LeftThigh, "thigh.L");
            Add(Player3DAnatomicalPart.LeftShin, "shin.L");
            Add(Player3DAnatomicalPart.LeftFoot, "foot.L");
            Add(Player3DAnatomicalPart.RightThigh, "thigh.R");
            Add(Player3DAnatomicalPart.RightShin, "shin.R");
            Add(Player3DAnatomicalPart.RightFoot, "foot.R");
            return result;
        }

        private static LocalPose[] CaptureSkeleton(Transform root, IEnumerable<Transform> anatomy)
        {
            var bones = new HashSet<Transform>();
            void Add(Transform bone)
            {
                while (bone != null && bone != root && bone.IsChildOf(root))
                { bones.Add(bone); bone = bone.parent; }
            }
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Transform bone in skin.bones) Add(bone);
            if (anatomy != null) foreach (Transform bone in anatomy) Add(bone);
            var poses = new List<LocalPose>(bones.Count);
            foreach (Transform bone in bones) poses.Add(new LocalPose(bone));
            return poses.ToArray();
        }

        private static void RestoreSkinBindPose(Transform root, Dictionary<Player3DAnatomicalPart, Transform> anatomy)
        {
            var matrices = new Dictionary<Transform, Matrix4x4>();
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                Matrix4x4[] bind = skin.sharedMesh.bindposes;
                Transform[] bones = skin.bones;
                for (int i = 0; i < bones.Length && i < bind.Length; i++)
                    if (bones[i] != null && !matrices.ContainsKey(bones[i]))
                        matrices.Add(bones[i], skin.localToWorldMatrix * bind[i].inverse);
            }
            foreach (Transform bone in anatomy.Values)
                if (!matrices.ContainsKey(bone))
                    throw new InvalidOperationException("Combat ragdoll requires a skin bind pose for " + bone.name);
            var ordered = new List<Transform>(matrices.Keys);
            ordered.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            foreach (Transform bone in ordered)
            {
                Matrix4x4 pose = matrices[bone];
                bone.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            }
        }

        private static int Depth(Transform bone)
        {
            int depth = 0;
            while (bone != null) { depth++; bone = bone.parent; }
            return depth;
        }

        private static void RestorePose(LocalPose[] poses)
        {
            if (poses == null) return;
            foreach (LocalPose pose in poses) pose.Restore();
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        private readonly struct LocalPose
        {
            private readonly Transform bone;
            private readonly Vector3 position, scale;
            private readonly Quaternion rotation;
            public LocalPose(Transform target)
            { bone = target; position = target.localPosition; rotation = target.localRotation; scale = target.localScale; }
            public void Restore()
            {
                if (bone == null) return;
                bone.SetLocalPositionAndRotation(position, rotation);
                bone.localScale = scale;
            }
        }
    }
}
