using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Round-local ownership of the existing anatomical ragdoll, on either combat rig.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class CombatRagdoll : MonoBehaviour
    {
        private Player3DRagdollController physicsController;
        private CharacterController capsule;
        private PlayerMotor motor;
        private PlayerBalanceController balance;
        private VillageResidentPresentation npc;
        private Animator npcAnimator;
        private Player3DCharacterPresentation heroPresentation;
        private Transform modelRoot;
        private LocalPose[] initialPose;
        private bool capsuleWasEnabled, motorWasEnabled, inputWasEnabled, balanceWasEnabled;
        private bool npcWasEnabled, animatorWasEnabled;
        private float simulationSeconds, quietSeconds, groundSeconds;
        private bool recoverable, hitStopFrozen;
        private readonly Dictionary<Transform, RagdollBoneMotion> presentedMotion = new Dictionary<Transform, RagdollBoneMotion>();
        private readonly Dictionary<Transform, WorldPose> previousWorldPose = new Dictionary<Transform, WorldPose>();
        private readonly List<CombatRagdollGroundContact> groundContacts = new List<CombatRagdollGroundContact>(4);

        public bool IsActive { get; private set; }
        public bool IsSettled { get; private set; }
        public bool IsRecovering { get; private set; }
        public bool IsRecoverable => IsActive && recoverable;
        public float SimulationSeconds => simulationSeconds;
        public bool HasGroundContact { get; private set; }
        public Vector3 GroundContactPoint { get; private set; }
        public Vector3 GroundContactNormal { get; private set; }
        public Collider GroundContactSurface { get; private set; }
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
            heroPresentation = hero;
            modelRoot = hero.Registry.ModelRoot;
            capsule = player.GameObject.GetComponent<CharacterController>();
            motor = player.Motor;
            balance = player.Balance;
            initialPose = CaptureSkeleton(hero.Registry.ModelRoot, null);
            if (!hero.Registry.TryGetPart(Player3DAnatomicalPart.Head, out Player3DAnatomicalPartBinding head))
                throw new InvalidOperationException("Combat ground contact requires the hero's head bone.");
            PrepareGroundContacts(head.Bone);
        }

        public void InitializeOpponent(VillageResidentPresentation presentation, CharacterController controller)
        {
            if (physicsController != null) throw new InvalidOperationException("Combat ragdoll is already initialized.");
            if (presentation == null || presentation.ModelRoot == null || controller == null)
                throw new ArgumentException("Combat opponent requires its authored rig and controller.");
            npc = presentation;
            modelRoot = presentation.ModelRoot;
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
            PrepareGroundContacts(anatomy[Player3DAnatomicalPart.Head]);
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
            CaptureOwners();
            if (!physicsController.BeginCombat(handoff)) return false;

            IsActive = true;
            recoverable = false;
            IsRecovering = false;
            IsSettled = false;
            ClearGroundContact();
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

        /// <summary>Temporary knockdown, starting on the already presented hit pose. Motion
        /// includes this hit, so activation must not apply its impulse twice.</summary>
        internal bool BeginKnockdown(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (physicsController == null || IsActive || physicsController.IsActive || !isActiveAndEnabled ||
                !Finite(linearVelocity) || !Finite(angularVelocity)) return false;
            CaptureOwners();
            if (!physicsController.BeginCombatSimulation(linearVelocity, angularVelocity, presentedMotion)) return false;
            IsActive = recoverable = true;
            IsRecovering = IsSettled = false;
            simulationSeconds = quietSeconds = 0f;
            ClearGroundContact();
            DisableOwners();
            return true;
        }

        private void CaptureOwners()
        {
            capsuleWasEnabled = capsule != null && capsule.enabled;
            motorWasEnabled = motor != null && motor.enabled;
            inputWasEnabled = motor != null && motor.InputEnabled;
            balanceWasEnabled = balance != null && balance.enabled;
            npcWasEnabled = npc != null && npc.enabled;
            animatorWasEnabled = npcAnimator != null && npcAnimator.enabled;
        }

        private void DisableOwners()
        {
            if (capsule != null) capsule.enabled = false;
            if (motor != null) { motor.SetInputEnabled(false); motor.enabled = false; }
            if (balance != null) balance.enabled = false;
            if (npc != null) npc.enabled = false;
            if (npcAnimator != null) npcAnimator.enabled = false;
        }

        /// <summary>A later, genuinely new contact owns a new physical impulse. A hit during
        /// recovery takes the live rising pose back to physics before applying that impulse.</summary>
        public void AddImpact(CombatImpact impact)
        {
            if (!IsActive || physicsController == null || impact.Impulse.sqrMagnitude <= .000001f) return;
            if (IsRecovering || physicsController.IsFrozen)
            {
                presentedMotion.TryGetValue(PelvisBody.transform, out RagdollBoneMotion motion);
                if (!physicsController.BeginCombatSimulation(motion.Linear, motion.Angular, presentedMotion)) return;
                IsRecovering = IsSettled = false;
                simulationSeconds = quietSeconds = 0f;
                ClearGroundContact();
                DisableOwners();
                // Reattach the weapon's mass before hit-stop records COM velocities.
                GetComponent<CombatActor>()?.EnableHeldWeaponPhysics();
                if (hitStopFrozen) physicsController.SetSimulationSuspended(true);
            }
            IsSettled = false;
            simulationSeconds = quietSeconds = 0f;
            physicsController.AddCombatImpulse(impact.Part, impact.Point, impact.Impulse);
        }

        internal void SetFrozen(bool frozen)
        {
            hitStopFrozen = frozen;
            physicsController?.SetSimulationSuspended(frozen);
        }

        internal bool BeginRecovery(out PlayerRagdollLyingPose lying)
        {
            lying = default;
            if (!IsActive || !recoverable || IsRecovering || hitStopFrozen || !IsSettled) return false;
            if (!physicsController.BeginRise(out lying)) return false;
            IsRecovering = true;
            return true;
        }

        internal void RebaseRecoveryRoot(Vector3 position, Quaternion rotation)
        {
            physicsController.RebaseRecoveryRoot(position, rotation);
            if (heroPresentation != null)
                UnityEngine.Object.FindAnyObjectByType<PlayerCameraFollow>()?.AbsorbTargetShift();
        }

        /// <summary>Defeat may replace a temporary fall without restoring a standing pose.</summary>
        internal void MakeTerminal()
        {
            if (!IsActive) return;
            recoverable = false;
            if (!IsRecovering) return;
            presentedMotion.TryGetValue(PelvisBody.transform, out RagdollBoneMotion motion);
            physicsController.BeginCombatSimulation(motion.Linear, motion.Angular, presentedMotion);
            IsRecovering = IsSettled = false;
            simulationSeconds = quietSeconds = 0f;
            DisableOwners();
            if (hitStopFrozen) physicsController.SetSimulationSuspended(true);
        }

        /// <summary>Return the final visible standing pose, never the initial round pose.</summary>
        internal void FinishRecovery()
        {
            if (!IsActive || !IsRecovering) return;
            LocalPose[] finalPose = CaptureSkeleton(modelRoot, null);
            physicsController.Cancel();
            RestorePose(finalPose);
            IsActive = IsSettled = IsRecovering = recoverable = hitStopFrozen = false;
            RestoreOwners();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || hitStopFrozen || PauseMenuController.IsAnyPaused || physicsController == null) return;
            foreach (Rigidbody body in Bodies)
            {
                Transform bone = body.transform;
                if (previousWorldPose.TryGetValue(bone, out WorldPose previous) && dt <= .1f)
                    presentedMotion[bone] = new RagdollBoneMotion((body.worldCenterOfMass - previous.Position) / dt,
                        Player3DCharacterPresentation.RecoveryAngularVelocity(previous.Rotation, bone.rotation, dt));
                previousWorldPose[bone] = new WorldPose(body.worldCenterOfMass, bone.rotation);
            }
        }

        private void PrepareGroundContacts(Transform head)
        {
            Add(physicsController.PelvisBody);
            Add(physicsController.SpineBody);
            Add(physicsController.ChestBody);
            Add(head.GetComponent<Rigidbody>());
            void Add(Rigidbody body)
            {
                if (body == null) throw new InvalidOperationException("Combat ground contact requires its anatomical rigidbody.");
                CombatRagdollGroundContact contact = body.gameObject.AddComponent<CombatRagdollGroundContact>();
                contact.Initialize(this);
                groundContacts.Add(contact);
            }
        }

        internal void RegisterGroundContact(Collision collision)
        {
            if (!IsActive || HasGroundContact || !isActiveAndEnabled || Time.timeScale <= 0f ||
                PauseMenuController.IsAnyPaused || collision == null) return;
            Collider surface = collision.collider;
            // Limbs already touch at handoff; only the central body owns this
            // sound. A weapon, another fighter or a wall is not ground support.
            if (surface == null || surface.attachedRigidbody != null ||
                surface.GetComponentInParent<CombatActor>() != null) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                // Continuous-speculative bodies may report a future contact;
                // wait until the surface is within the collider contact skin.
                if (contact.normal.y <= .65f || contact.separation > .02f || !Finite(contact.point)) continue;
                HasGroundContact = true;
                GroundContactPoint = contact.point;
                GroundContactNormal = contact.normal.normalized;
                GroundContactSurface = surface;
                // The first actual body landing owns one dull thud. Recontacts
                // and the settled body remain silent until the next round.
                RetroAudio.PlayAt(RetroSfxId.StoneTamp, GroundContactPoint, .8f);
                return;
            }
        }

        private void ClearGroundContact()
        {
            groundSeconds = 0f;
            HasGroundContact = false;
            GroundContactPoint = GroundContactNormal = Vector3.zero;
            GroundContactSurface = null;
        }

        private void FixedUpdate()
        {
            // Shared pause stops PhysX. Neither the round's frozen simulation nor
            // presentation updates own this body's remaining fall time.
            if (!IsActive || IsRecovering || IsSettled || hitStopFrozen || Time.timeScale <= 0f || PauseMenuController.IsAnyPaused) return;
            physicsController.AdvanceCombatAnchor();
            simulationSeconds += Time.fixedDeltaTime;
            foreach (Rigidbody body in Bodies)
            {
                if (body == null || body.isKinematic) continue;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, 8f);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, 10f);
            }
            if (HasGroundContact) groundSeconds += Time.fixedDeltaTime;
            // A settled torso can actively gather still-moving limbs. Waiting for the
            // fastest fingertip/joint to stop made every temporary fall hit a 4 s timeout.
            float speed = recoverable ? Mathf.Max(CentralSpeed(physicsController.PelvisBody),
                CentralSpeed(physicsController.ChestBody)) : MaximumBodySpeed;
            quietSeconds = speed < (recoverable ? .65f : .12f) ? quietSeconds + Time.fixedDeltaTime : 0f;
            bool readyToRise = HasGroundContact && simulationSeconds >= .45f &&
                (quietSeconds >= .18f || (groundSeconds >= 1.1f && speed < 1f));
            bool terminalRest = (simulationSeconds >= 1f && quietSeconds >= .5f) || simulationSeconds >= 4f;
            if (recoverable ? readyToRise : terminalRest)
            {
                physicsController.FreezeInPlace();
                IsSettled = true;
            }
        }

        private static float CentralSpeed(Rigidbody body) => body == null ? 0f :
            body.linearVelocity.magnitude + body.angularVelocity.magnitude * .12f;

        public void Cancel()
        {
            ClearGroundContact();
            hitStopFrozen = false;
            if (!IsActive) return;
            IsActive = IsSettled = IsRecovering = recoverable = hitStopFrozen = false;
            simulationSeconds = quietSeconds = 0f;
            if (physicsController != null) physicsController.Cancel();
            RestorePose(initialPose);
            previousWorldPose.Clear();
            presentedMotion.Clear();
            RestoreOwners();
        }

        private void RestoreOwners()
        {
            if (npcAnimator != null) npcAnimator.enabled = animatorWasEnabled;
            if (npc != null) npc.enabled = npcWasEnabled;
            if (balance != null) balance.enabled = balanceWasEnabled;
            if (motor != null) { motor.SetInputEnabled(inputWasEnabled); motor.enabled = motorWasEnabled; }
            if (capsule != null) capsule.enabled = capsuleWasEnabled;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            foreach (CombatRagdollGroundContact contact in groundContacts)
                if (contact != null) Destroy(contact);
            groundContacts.Clear();
        }

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

        private readonly struct WorldPose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public WorldPose(Vector3 position, Quaternion rotation) { Position = position; Rotation = rotation; }
        }
    }
}
