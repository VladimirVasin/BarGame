using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class Player3DRagdollController : MonoBehaviour
    {
        public const float FallHandoffTime = 0.16f;
        public const float RecoveryBlendDuration = 0.16f;

        private const float CanonicalHeight = 1.75f;
        private const float RootTetherRadius = 0.68f;

        private static readonly BodySpec[] BodySpecs =
        {
            BodySpec.Root(Player3DAnatomicalPart.Pelvis, 12f),
            BodySpec.Jointed(
                Player3DAnatomicalPart.Torso,
                Player3DAnatomicalPart.Pelvis,
                18f,
                -20f,
                20f,
                20f,
                15f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.Head,
                Player3DAnatomicalPart.Torso,
                5f,
                -35f,
                35f,
                25f,
                25f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftUpperArm,
                Player3DAnatomicalPart.Torso,
                2.2f,
                -55f,
                55f,
                70f,
                55f,
                JointAxis.Forward),
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.LeftUpperArm,
                1.5f,
                -5f,
                120f,
                8f,
                8f,
                JointAxis.Forward),
            BodySpec.Jointed(
                Player3DAnatomicalPart.RightUpperArm,
                Player3DAnatomicalPart.Torso,
                2.2f,
                -55f,
                55f,
                70f,
                55f,
                JointAxis.Forward),
            BodySpec.Jointed(
                Player3DAnatomicalPart.RightForearm,
                Player3DAnatomicalPart.RightUpperArm,
                1.5f,
                -5f,
                120f,
                8f,
                8f,
                JointAxis.Forward),
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftThigh,
                Player3DAnatomicalPart.Pelvis,
                7f,
                -25f,
                25f,
                55f,
                35f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftShin,
                Player3DAnatomicalPart.LeftThigh,
                4f,
                -5f,
                115f,
                6f,
                6f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftFoot,
                Player3DAnatomicalPart.LeftShin,
                1.2f,
                -30f,
                45f,
                12f,
                10f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.RightThigh,
                Player3DAnatomicalPart.Pelvis,
                7f,
                -25f,
                25f,
                55f,
                35f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.RightShin,
                Player3DAnatomicalPart.RightThigh,
                4f,
                -5f,
                115f,
                6f,
                6f,
                JointAxis.Right),
            BodySpec.Jointed(
                Player3DAnatomicalPart.RightFoot,
                Player3DAnatomicalPart.RightShin,
                1.2f,
                -30f,
                45f,
                12f,
                10f,
                JointAxis.Right)
        };

        private readonly Dictionary<Player3DAnatomicalPart, Transform>
            bones = new Dictionary<Player3DAnatomicalPart, Transform>();
        private readonly Dictionary<Player3DAnatomicalPart, Rigidbody>
            bodies = new Dictionary<Player3DAnatomicalPart, Rigidbody>();
        private readonly List<Rigidbody> bodyList = new List<Rigidbody>();
        private readonly List<Collider> colliders = new List<Collider>();
        private readonly List<ConfigurableJoint> joints =
            new List<ConfigurableJoint>();
        private readonly List<Transform> poseTransforms =
            new List<Transform>();

        private Transform gameplayRoot;
        private CharacterController characterController;
        private Player3DCharacterPresentation presentation;
        private Player3DAssetRegistry registry;
        private Rigidbody rootAnchorBody;
        private ConfigurableJoint pelvisTether;
        private BonePose[] recoveryStart;
        private BonePose[] recoveryTarget;
        private bool initialized;

        public bool IsInitialized => initialized;
        public bool IsSimulating { get; private set; }
        public bool IsRecovering { get; private set; }
        public bool IsActive => IsSimulating || IsRecovering;
        public int BodyCount => bodyList.Count;
        public Rigidbody PelvisBody => GetBody(Player3DAnatomicalPart.Pelvis);
        public Rigidbody ChestBody => GetBody(Player3DAnatomicalPart.Torso);

        public void Initialize(
            Transform actorRoot,
            CharacterController controller,
            Player3DCharacterPresentation targetPresentation,
            Player3DAssetRegistry assetRegistry)
        {
            if (initialized)
            {
                return;
            }

            gameplayRoot = actorRoot != null
                ? actorRoot
                : throw new ArgumentNullException(nameof(actorRoot));
            characterController = controller;
            presentation = targetPresentation != null
                ? targetPresentation
                : throw new ArgumentNullException(
                    nameof(targetPresentation));
            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));

            CacheRequiredBones();
            BuildBodies();
            BuildColliders();
            BuildJoints();
            BuildPoseTransformList();
            IgnoreOwnedCollisions();
            SetCollidersEnabled(false);
            initialized = true;
        }

        public bool Begin(float signedDirection)
        {
            if (!initialized || IsActive)
            {
                return false;
            }

            RefreshJointAnchors();
            presentation.SetRagdollPoseActive(true);
            SetCollidersEnabled(true);
            Physics.SyncTransforms();
            for (int index = 0; index < bodyList.Count; index++)
            {
                Rigidbody body = bodyList[index];
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.isKinematic = false;
            }

            IsSimulating = true;
            float direction = Mathf.Approximately(signedDirection, 0f)
                ? 1f
                : Mathf.Sign(signedDirection);
            Vector3 lateral = Vector3.ProjectOnPlane(
                gameplayRoot.right,
                Vector3.up);
            if (lateral.sqrMagnitude <= 0.0001f)
            {
                lateral = Vector3.right;
            }

            lateral.Normalize();
            lateral *= direction;
            PelvisBody.AddForce(
                lateral * 0.45f + Vector3.down * 0.15f,
                ForceMode.VelocityChange);
            ChestBody.AddForce(
                lateral * 1.1f + Vector3.down * 0.25f,
                ForceMode.VelocityChange);
            ChestBody.AddTorque(
                gameplayRoot.forward * (-direction * 2.2f),
                ForceMode.VelocityChange);
            return true;
        }

        public bool BeginRecovery(float signedDirection)
        {
            if (!initialized || !IsSimulating)
            {
                return false;
            }

            FreezeBodies();
            SetCollidersEnabled(false);
            recoveryStart = CapturePose();
            presentation.SetRagdollPoseActive(false);
            presentation.SetFallPose(signedDirection, 1f);
            presentation.SetFallAnimation(
                PlayerFallAnimationPhase.Rising,
                0f);
            recoveryTarget = CapturePose();
            presentation.SetRagdollPoseActive(true);
            ApplyPose(recoveryStart);
            IsSimulating = false;
            IsRecovering = true;
            return true;
        }

        public void SetRecoveryProgress(float normalizedProgress)
        {
            if (!IsRecovering ||
                recoveryStart == null ||
                recoveryTarget == null)
            {
                return;
            }

            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(normalizedProgress));
            for (int index = 0; index < poseTransforms.Count; index++)
            {
                Transform target = poseTransforms[index];
                BonePose start = recoveryStart[index];
                BonePose end = recoveryTarget[index];
                target.localPosition = Vector3.LerpUnclamped(
                    start.LocalPosition,
                    end.LocalPosition,
                    progress);
                target.localRotation = Quaternion.SlerpUnclamped(
                    start.LocalRotation,
                    end.LocalRotation,
                    progress);
                target.localScale = Vector3.LerpUnclamped(
                    start.LocalScale,
                    end.LocalScale,
                    progress);
            }
        }

        public void CompleteRecovery()
        {
            if (!IsRecovering)
            {
                return;
            }

            SetRecoveryProgress(1f);
            IsRecovering = false;
            recoveryStart = null;
            recoveryTarget = null;
            presentation.SetRagdollPoseActive(false);
        }

        public void Cancel()
        {
            if (!initialized)
            {
                return;
            }

            FreezeBodies();
            SetCollidersEnabled(false);
            IsSimulating = false;
            IsRecovering = false;
            recoveryStart = null;
            recoveryTarget = null;
            if (presentation != null)
            {
                presentation.SetRagdollPoseActive(false);
                presentation.SetFallPose(0f, 0f);
                presentation.SetFallAnimation(
                    PlayerFallAnimationPhase.None,
                    0f);
            }
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void OnDestroy()
        {
            Cancel();
        }

        private void CacheRequiredBones()
        {
            for (int index = 0; index < BodySpecs.Length; index++)
            {
                RequirePartBone(BodySpecs[index].Part);
            }

            RequirePartBone(Player3DAnatomicalPart.LeftHand);
            RequirePartBone(Player3DAnatomicalPart.RightHand);
        }

        private Transform RequirePartBone(Player3DAnatomicalPart part)
        {
            if (bones.TryGetValue(part, out Transform cached))
            {
                return cached;
            }

            if (!registry.TryGetPart(part, out var binding) ||
                binding == null ||
                binding.Bone == null)
            {
                throw new InvalidOperationException(
                    $"Player ragdoll requires the registered {part} bone.");
            }

            bones.Add(part, binding.Bone);
            return binding.Bone;
        }

        private void BuildBodies()
        {
            for (int index = 0; index < BodySpecs.Length; index++)
            {
                BodySpec spec = BodySpecs[index];
                Transform bone = bones[spec.Part];
                if (bone.GetComponent<Rigidbody>() != null)
                {
                    throw new InvalidOperationException(
                        $"Player ragdoll bone '{bone.name}' already has a " +
                        "Rigidbody.");
                }

                Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = spec.Mass;
                body.useGravity = true;
                body.isKinematic = true;
                body.detectCollisions = true;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.08f;
                body.angularDamping = 0.8f;
                body.maxAngularVelocity = 12f;
                body.solverIterations = 12;
                body.solverVelocityIterations = 4;
                bodies.Add(spec.Part, body);
                bodyList.Add(body);
            }

            GameObject anchor = new GameObject("Player Ragdoll Root Anchor");
            anchor.layer = gameplayRoot.gameObject.layer;
            anchor.transform.SetParent(gameplayRoot, false);
            anchor.transform.position = PelvisBody.transform.position;
            rootAnchorBody = anchor.AddComponent<Rigidbody>();
            rootAnchorBody.useGravity = false;
            rootAnchorBody.isKinematic = true;
            rootAnchorBody.detectCollisions = false;
        }

        private void BuildColliders()
        {
            float scale = Mathf.Max(
                0.01f,
                registry.Metrics.CanonicalHeight / CanonicalHeight);
            AddBox(
                Player3DAnatomicalPart.Pelvis,
                bones[Player3DAnatomicalPart.Pelvis].position +
                gameplayRoot.up * (0.06f * scale),
                gameplayRoot.rotation,
                new Vector3(0.28f, 0.20f, 0.20f) * scale);
            AddBox(
                Player3DAnatomicalPart.Torso,
                bones[Player3DAnatomicalPart.Torso].position +
                gameplayRoot.up * (0.01f * scale),
                gameplayRoot.rotation,
                new Vector3(0.38f, 0.43f, 0.20f) * scale);
            AddCapsule(
                Player3DAnatomicalPart.Head,
                bones[Player3DAnatomicalPart.Head].position -
                gameplayRoot.up * (0.02f * scale),
                bones[Player3DAnatomicalPart.Head].position +
                gameplayRoot.up * (0.32f * scale),
                0.12f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.LeftUpperArm,
                Player3DAnatomicalPart.LeftForearm,
                0.06f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.LeftHand,
                0.05f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.RightUpperArm,
                Player3DAnatomicalPart.RightForearm,
                0.06f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.RightForearm,
                Player3DAnatomicalPart.RightHand,
                0.05f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.LeftThigh,
                Player3DAnatomicalPart.LeftShin,
                0.085f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.LeftShin,
                Player3DAnatomicalPart.LeftFoot,
                0.07f * scale);
            AddFootBox(Player3DAnatomicalPart.LeftFoot, scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.RightThigh,
                Player3DAnatomicalPart.RightShin,
                0.085f * scale);
            AddLimbCapsule(
                Player3DAnatomicalPart.RightShin,
                Player3DAnatomicalPart.RightFoot,
                0.07f * scale);
            AddFootBox(Player3DAnatomicalPart.RightFoot, scale);
        }

        private void AddLimbCapsule(
            Player3DAnatomicalPart part,
            Player3DAnatomicalPart endpoint,
            float radius)
        {
            AddCapsule(
                part,
                bones[part].position,
                bones[endpoint].position,
                radius);
        }

        private void AddFootBox(
            Player3DAnatomicalPart part,
            float scale)
        {
            Transform bone = bones[part];
            AddBox(
                part,
                bone.position +
                gameplayRoot.forward * (0.065f * scale) -
                gameplayRoot.up * (0.025f * scale),
                gameplayRoot.rotation,
                new Vector3(0.14f, 0.10f, 0.24f) * scale);
        }

        private void AddCapsule(
            Player3DAnatomicalPart part,
            Vector3 start,
            Vector3 end,
            float radius)
        {
            Vector3 segment = end - start;
            float length = segment.magnitude;
            Quaternion rotation = length > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, segment / length)
                : gameplayRoot.rotation;
            Transform proxy = CreateColliderProxy(
                part,
                (start + end) * 0.5f,
                rotation);
            CapsuleCollider capsule =
                proxy.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = Mathf.Max(radius * 2f, length);
            capsule.enabled = false;
            colliders.Add(capsule);
        }

        private void AddBox(
            Player3DAnatomicalPart part,
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            Transform proxy = CreateColliderProxy(part, center, rotation);
            BoxCollider box = proxy.gameObject.AddComponent<BoxCollider>();
            box.size = size;
            box.enabled = false;
            colliders.Add(box);
        }

        private Transform CreateColliderProxy(
            Player3DAnatomicalPart part,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            GameObject proxyObject = new GameObject(
                $"Player Ragdoll Collider {part}");
            proxyObject.layer = gameplayRoot.gameObject.layer;
            Transform proxy = proxyObject.transform;
            proxy.SetPositionAndRotation(worldPosition, worldRotation);
            proxy.SetParent(bones[part], true);
            return proxy;
        }

        private void BuildJoints()
        {
            rootAnchorBody.position = PelvisBody.transform.position;
            pelvisTether = PelvisBody.gameObject.AddComponent<
                ConfigurableJoint>();
            pelvisTether.connectedBody = rootAnchorBody;
            pelvisTether.autoConfigureConnectedAnchor = false;
            pelvisTether.anchor = Vector3.zero;
            pelvisTether.connectedAnchor = Vector3.zero;
            pelvisTether.xMotion = ConfigurableJointMotion.Limited;
            pelvisTether.yMotion = ConfigurableJointMotion.Limited;
            pelvisTether.zMotion = ConfigurableJointMotion.Limited;
            pelvisTether.angularXMotion = ConfigurableJointMotion.Free;
            pelvisTether.angularYMotion = ConfigurableJointMotion.Free;
            pelvisTether.angularZMotion = ConfigurableJointMotion.Free;
            pelvisTether.linearLimit = Limit(RootTetherRadius, 0.04f);
            ConfigureJointCommon(pelvisTether);
            joints.Add(pelvisTether);

            for (int index = 0; index < BodySpecs.Length; index++)
            {
                BodySpec spec = BodySpecs[index];
                if (!spec.HasParent)
                {
                    continue;
                }

                Rigidbody body = bodies[spec.Part];
                Rigidbody parent = bodies[spec.Parent];
                ConfigurableJoint joint = body.gameObject.AddComponent<
                    ConfigurableJoint>();
                joint.connectedBody = parent;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = parent.transform.InverseTransformPoint(
                    body.transform.position);
                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                joint.angularYMotion = ConfigurableJointMotion.Limited;
                joint.angularZMotion = ConfigurableJointMotion.Limited;
                joint.lowAngularXLimit = Limit(spec.LowX, 2f);
                joint.highAngularXLimit = Limit(spec.HighX, 2f);
                joint.angularYLimit = Limit(spec.YLimit, 2f);
                joint.angularZLimit = Limit(spec.ZLimit, 2f);
                Vector3 axisWorld = spec.Axis == JointAxis.Forward
                    ? gameplayRoot.forward
                    : gameplayRoot.right;
                joint.axis = body.transform.InverseTransformDirection(
                    axisWorld).normalized;
                joint.secondaryAxis = body.transform.InverseTransformDirection(
                    gameplayRoot.up).normalized;
                ConfigureJointCommon(joint);
                joints.Add(joint);
            }
        }

        private static void ConfigureJointCommon(ConfigurableJoint joint)
        {
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 12f;
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }

        private static SoftJointLimit Limit(
            float value,
            float contactDistance)
        {
            return new SoftJointLimit
            {
                limit = value,
                contactDistance = contactDistance
            };
        }

        private void BuildPoseTransformList()
        {
            var unique = new HashSet<Transform>();
            for (int index = 0;
                 index < registry.MeshBindings.Count;
                 index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding != null)
                {
                    AddBoneLineage(binding.Bone, unique);
                }
            }

            Player3DBoneAnchors anchors = registry.Anchors;
            AddBoneLineage(anchors.Head, unique);
            AddBoneLineage(anchors.Chest, unique);
            AddBoneLineage(anchors.Pelvis, unique);
            AddBoneLineage(anchors.LeftFoot, unique);
            AddBoneLineage(anchors.RightFoot, unique);
            AddBoneLineage(anchors.LeftGrip, unique);
            AddBoneLineage(anchors.RightGrip, unique);
            AddBoneLineage(anchors.RightCigarette, unique);
            AddBoneLineage(anchors.Mouth, unique);
            poseTransforms.AddRange(unique);
            poseTransforms.Sort((first, second) =>
                GetDepth(first).CompareTo(GetDepth(second)));
        }

        private void AddBoneLineage(
            Transform bone,
            ISet<Transform> unique)
        {
            Transform current = bone;
            while (current != null && current != registry.ModelRoot)
            {
                unique.Add(current);
                current = current.parent;
            }
        }

        private static int GetDepth(Transform target)
        {
            int depth = 0;
            Transform current = target;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        private void IgnoreOwnedCollisions()
        {
            for (int first = 0; first < colliders.Count; first++)
            {
                Collider current = colliders[first];
                if (characterController != null)
                {
                    Physics.IgnoreCollision(
                        current,
                        characterController,
                        true);
                }

                for (int second = first + 1;
                     second < colliders.Count;
                     second++)
                {
                    Physics.IgnoreCollision(
                        current,
                        colliders[second],
                        true);
                }
            }
        }

        private void RefreshJointAnchors()
        {
            rootAnchorBody.position = PelvisBody.transform.position;
            rootAnchorBody.rotation = gameplayRoot.rotation;
            pelvisTether.connectedAnchor = Vector3.zero;
            for (int index = 0; index < joints.Count; index++)
            {
                ConfigurableJoint joint = joints[index];
                if (joint == pelvisTether || joint.connectedBody == null)
                {
                    continue;
                }

                joint.connectedAnchor =
                    joint.connectedBody.transform.InverseTransformPoint(
                        joint.transform.position);
            }
        }

        private void FreezeBodies()
        {
            for (int index = 0; index < bodyList.Count; index++)
            {
                Rigidbody body = bodyList[index];
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }

                body.interpolation = RigidbodyInterpolation.None;
            }
        }

        private void SetCollidersEnabled(bool enabled)
        {
            for (int index = 0; index < colliders.Count; index++)
            {
                colliders[index].enabled = enabled;
            }
        }

        private BonePose[] CapturePose()
        {
            var result = new BonePose[poseTransforms.Count];
            for (int index = 0; index < poseTransforms.Count; index++)
            {
                result[index] = new BonePose(poseTransforms[index]);
            }

            return result;
        }

        private void ApplyPose(IReadOnlyList<BonePose> pose)
        {
            for (int index = 0; index < poseTransforms.Count; index++)
            {
                pose[index].Apply(poseTransforms[index]);
            }
        }

        private Rigidbody GetBody(Player3DAnatomicalPart part)
        {
            return bodies.TryGetValue(part, out Rigidbody body)
                ? body
                : null;
        }

        private enum JointAxis
        {
            Right,
            Forward
        }

        private readonly struct BodySpec
        {
            private BodySpec(
                Player3DAnatomicalPart part,
                Player3DAnatomicalPart parent,
                bool hasParent,
                float mass,
                float lowX,
                float highX,
                float yLimit,
                float zLimit,
                JointAxis axis)
            {
                Part = part;
                Parent = parent;
                HasParent = hasParent;
                Mass = mass;
                LowX = lowX;
                HighX = highX;
                YLimit = yLimit;
                ZLimit = zLimit;
                Axis = axis;
            }

            public Player3DAnatomicalPart Part { get; }
            public Player3DAnatomicalPart Parent { get; }
            public bool HasParent { get; }
            public float Mass { get; }
            public float LowX { get; }
            public float HighX { get; }
            public float YLimit { get; }
            public float ZLimit { get; }
            public JointAxis Axis { get; }

            public static BodySpec Root(
                Player3DAnatomicalPart part,
                float mass)
            {
                return new BodySpec(
                    part,
                    default,
                    false,
                    mass,
                    0f,
                    0f,
                    0f,
                    0f,
                    JointAxis.Right);
            }

            public static BodySpec Jointed(
                Player3DAnatomicalPart part,
                Player3DAnatomicalPart parent,
                float mass,
                float lowX,
                float highX,
                float yLimit,
                float zLimit,
                JointAxis axis)
            {
                return new BodySpec(
                    part,
                    parent,
                    true,
                    mass,
                    lowX,
                    highX,
                    yLimit,
                    zLimit,
                    axis);
            }
        }

        private readonly struct BonePose
        {
            public BonePose(Transform target)
            {
                LocalPosition = target.localPosition;
                LocalRotation = target.localRotation;
                LocalScale = target.localScale;
            }

            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }

            public void Apply(Transform target)
            {
                target.localPosition = LocalPosition;
                target.localRotation = LocalRotation;
                target.localScale = LocalScale;
            }
        }
    }
}
