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

        /// <summary>
        /// A topple rotating slower than this still gets the old scripted
        /// shove, faded in as the rotation fades out, so a forced or a
        /// crawling fall goes down instead of standing there.
        /// </summary>
        public const float SlowToppleAngularVelocity = 1.5f;

        private const float CanonicalHeight = 1.75f;
        private const float RootTetherRadius = 0.68f;
        private const int SolverIterations = 60;
        private const int SolverVelocityIterations = 16;

        /// <summary>
        /// Two of the body's own colliders overlapping by more than this
        /// in the idle pose would explode apart on the first physics step;
        /// such a pair is ignored and logged. The anatomy check asserts the
        /// hero has none, so every limb collides with every other.
        /// </summary>
        public const float RestingOverlapMetres = 0.01f;

        /// <summary>
        /// The joint frames are baked from the pose at initialisation and
        /// the limits count from it: a knee or elbow already bent this far
        /// there would carry that bend into every limit.
        /// </summary>
        public const float JointFrameBentDegrees = 15f;

        /// <summary>
        /// How a hinge's flexion maps onto its joint's angular X. About
        /// the actor's right, a positive <c>AngleAxis</c> turn carries a
        /// hanging segment backward (the knee's flexion) and a negative
        /// one forward (the elbow's) — and the joint's angular X counts
        /// the OTHER way: PhysX measures the parent's frame from the
        /// child's. Pinned by the ragdoll anatomy check, which found the
        /// knees folding 72 degrees backward with the sign at <c>+1</c>.
        /// </summary>
        public const float JointFlexionSign = -1f;

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
            // The shoulders and hips are near enough ball joints, and the
            // pose the ragdoll takes over — arms flung out for balance,
            // a leg lunged to the side — must sit inside their ranges,
            // or the joint snaps to its limit on the first step and whips
            // the forearm or shin past ITS limit. The body's own
            // colliders keep an arm out of the chest now, not the limit.
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftUpperArm,
                Player3DAnatomicalPart.Torso,
                2.2f,
                -110f,
                110f,
                90f,
                90f,
                JointAxis.Forward),
            // The elbow is a hinge about the actor's right, like the knee,
            // and flexes the other way: the forearm swings forward.
            BodySpec.Hinge(
                Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.LeftUpperArm,
                1.5f,
                -5f,
                120f,
                8f,
                8f,
                -1f),
            BodySpec.Jointed(
                Player3DAnatomicalPart.RightUpperArm,
                Player3DAnatomicalPart.Torso,
                2.2f,
                -110f,
                110f,
                90f,
                90f,
                JointAxis.Forward),
            BodySpec.Hinge(
                Player3DAnatomicalPart.RightForearm,
                Player3DAnatomicalPart.RightUpperArm,
                1.5f,
                -5f,
                120f,
                8f,
                8f,
                -1f),
            // The hip flexes forward like the elbow (a thigh swings
            // forward to a crouch or a lunge), a little back.
            BodySpec.Hinge(
                Player3DAnatomicalPart.LeftThigh,
                Player3DAnatomicalPart.Pelvis,
                7f,
                30f,
                110f,
                55f,
                60f,
                -1f),
            BodySpec.Hinge(
                Player3DAnatomicalPart.LeftShin,
                Player3DAnatomicalPart.LeftThigh,
                4f,
                -5f,
                115f,
                6f,
                6f,
                1f),
            BodySpec.Jointed(
                Player3DAnatomicalPart.LeftFoot,
                Player3DAnatomicalPart.LeftShin,
                1.2f,
                -30f,
                45f,
                12f,
                10f,
                JointAxis.Right),
            BodySpec.Hinge(
                Player3DAnatomicalPart.RightThigh,
                Player3DAnatomicalPart.Pelvis,
                7f,
                30f,
                110f,
                55f,
                60f,
                -1f),
            BodySpec.Hinge(
                Player3DAnatomicalPart.RightShin,
                Player3DAnatomicalPart.RightThigh,
                4f,
                -5f,
                115f,
                6f,
                6f,
                1f),
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
        private readonly Dictionary<Collider, Player3DAnatomicalPart>
            partByCollider = new Dictionary<Collider, Player3DAnatomicalPart>();
        private readonly List<ColliderPair> livePairs = new List<ColliderPair>();
        private readonly List<string> restingOverlaps = new List<string>();
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
            WarnIfJointFramesBent();
            BuildPoseTransformList();
            IgnoreOwnedCollisions();
            SetCollidersEnabled(false);
            initialized = true;
        }

        /// <summary>How many of the body's own collider pairs overlapped in the idle pose and were switched off.</summary>
        internal int RestingOverlapPairCount => restingOverlaps.Count;

        /// <summary>The overlapping pairs, for a failure message.</summary>
        internal string DebugRestingOverlaps => string.Join(", ", restingOverlaps);

        /// <summary>
        /// The deepest overlap between any two of the body's colliders
        /// that are meant to collide, at the pose the bones hold now, and
        /// which pair it is. A probe seam: the colliders are switched on
        /// for the measurement if they are off.
        /// </summary>
        internal float DebugMaximumPenetration(out string pair)
        {
            pair = string.Empty;
            if (!initialized)
            {
                return 0f;
            }

            bool wereEnabled = colliders.Count > 0 && colliders[0].enabled;
            if (!wereEnabled)
            {
                SetCollidersEnabled(true);
            }

            Physics.SyncTransforms();
            float deepest = 0f;
            for (int index = 0; index < livePairs.Count; index++)
            {
                ColliderPair candidate = livePairs[index];
                if (Penetration(candidate.First, candidate.Second) is float depth &&
                    depth > deepest)
                {
                    deepest = depth;
                    pair = candidate.Name;
                }
            }

            if (!wereEnabled)
            {
                SetCollidersEnabled(false);
            }

            return deepest;
        }

        /// <summary>A fall known only by its side: the legacy scripted shove.</summary>
        public bool Begin(float signedDirection)
        {
            return Begin(
                PlayerRagdollHandoff.Legacy(signedDirection, gameplayRoot));
        }

        /// <summary>
        /// Takes the body over from the pose the late layer wrote this
        /// frame, with the motion the balance model says it had: every
        /// body starts on the rigid rotation about the support edge, so
        /// the ragdoll continues the topple rather than dropping from a
        /// standstill. A rotation too slow to bring him down still gets
        /// the old lateral shove, faded in as the rotation fades out.
        /// </summary>
        public bool Begin(in PlayerRagdollHandoff handoff)
        {
            if (!initialized || IsActive)
            {
                return false;
            }

            presentation.BeginRagdollPoseFromLatePose();
            RefreshJointAnchors();
            SetCollidersEnabled(true);
            Physics.SyncTransforms();
            for (int index = 0; index < bodyList.Count; index++)
            {
                Rigidbody body = bodyList[index];
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.isKinematic = false;
            }

            IsSimulating = true;
            Vector3 angular = handoff.AngularVelocity;
            for (int index = 0; index < bodyList.Count; index++)
            {
                Rigidbody body = bodyList[index];
                body.linearVelocity = handoff.VelocityAt(body.worldCenterOfMass);
                body.angularVelocity = angular;
            }

            // A hair of downward push keeps a body that is only just past
            // upright from balancing on the tether for a frame.
            PelvisBody.AddForce(Vector3.down * 0.15f, ForceMode.VelocityChange);
            ChestBody.AddForce(Vector3.down * 0.25f, ForceMode.VelocityChange);

            float legacy = 1f - Mathf.Clamp01(
                handoff.AngularSpeed / SlowToppleAngularVelocity);
            if (legacy > 0.001f)
            {
                Vector3 lateral = handoff.FallAxis;
                PelvisBody.AddForce(
                    lateral * (0.45f * legacy),
                    ForceMode.VelocityChange);
                ChestBody.AddForce(
                    lateral * (1.1f * legacy),
                    ForceMode.VelocityChange);
                ChestBody.AddTorque(
                    Vector3.Cross(Vector3.up, lateral) * (2.2f * legacy),
                    ForceMode.VelocityChange);
            }

            return true;
        }

        /// <summary>
        /// A jerk of the lying body toward a direction: the player leaned
        /// on a key while the physics had him. A push at the hips and the
        /// chest with a hair of lift, and a roll about the direction, so
        /// he heaves that way rather than sliding. Nothing outside the
        /// simulation.
        /// </summary>
        public void Twitch(Vector3 worldDirection, float strength)
        {
            if (!IsSimulating)
            {
                return;
            }

            Vector3 direction = worldDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();
            float scale = Mathf.Clamp01(strength);
            PelvisBody.AddForce(
                direction * (TwitchPelvisPush * scale) +
                Vector3.up * (TwitchPelvisLift * scale),
                ForceMode.VelocityChange);
            ChestBody.AddForce(
                direction * (TwitchChestPush * scale) +
                Vector3.up * (TwitchChestLift * scale),
                ForceMode.VelocityChange);
            ChestBody.AddTorque(
                Vector3.Cross(Vector3.up, direction) * (TwitchChestRoll * scale),
                ForceMode.VelocityChange);
        }

        // A heave, not a nudge: a body pressed to the floor loses most of
        // a push to friction within the step, so the lift matters as much
        // as the push — it unloads the floor for the moment the push acts.
        public const float TwitchPelvisPush = 1.0f;
        public const float TwitchPelvisLift = 0.6f;
        public const float TwitchChestPush = 1.5f;
        public const float TwitchChestLift = 0.5f;
        public const float TwitchChestRoll = 0.8f;

        /// <summary>The fastest any body is moving, m/s; zero unless simulating.</summary>
        public float MaximumBodySpeed
        {
            get
            {
                if (!IsSimulating)
                {
                    return 0f;
                }

                float maximum = 0f;
                for (int index = 0; index < bodyList.Count; index++)
                {
                    maximum = Mathf.Max(
                        maximum,
                        bodyList[index].linearVelocity.magnitude);
                }

                return maximum;
            }
        }

        /// <summary>
        /// The physics is over: the bodies freeze where they lie, the
        /// lying pose is captured — the pelvis in world space, so the
        /// root can be moved under it without the body moving — and the
        /// caller learns where and which way he lies.
        /// </summary>
        public bool BeginRise(out PlayerRagdollLyingPose lying)
        {
            lying = default;
            if (!initialized || !IsSimulating)
            {
                return false;
            }

            FreezeBodies();
            SetCollidersEnabled(false);
            recoveryStart = CapturePose();
            Transform leftShoulder = bones[Player3DAnatomicalPart.LeftUpperArm];
            Transform rightShoulder = bones[Player3DAnatomicalPart.RightUpperArm];
            lying = new PlayerRagdollLyingPose(
                PelvisBody.transform.position,
                ChestBody.transform.position,
                leftShoulder.position.y,
                rightShoulder.position.y);
            IsSimulating = false;
            IsRecovering = true;
            return true;
        }

        /// <summary>
        /// Blends the frozen lying pose into whatever the bones hold NOW
        /// (the clip the presentation just sampled): at zero the body is
        /// exactly where physics left it, at one it is the clip's.
        /// </summary>
        public void ApplyRecoveryBlend(float normalizedProgress)
        {
            if (!IsRecovering || recoveryStart == null)
            {
                return;
            }

            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(normalizedProgress));
            for (int index = 0; index < poseTransforms.Count; index++)
            {
                recoveryStart[index].BlendInto(poseTransforms[index], progress);
            }
        }

        /// <summary>The lying pose is no longer needed: the clip owns the bones.</summary>
        public void EndRise()
        {
            IsRecovering = false;
            recoveryStart = null;
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
                // The limbs collide with the body now, so a leg pinned
                // under the torso carries its weight through the knee's
                // limit; the solver needs the iterations to hold it.
                // (Capping the depenetration velocity was tried against
                // the elbow's landing whip and made it worse.)
                body.solverIterations = SolverIterations;
                body.solverVelocityIterations = SolverVelocityIterations;
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
            partByCollider.Add(capsule, part);
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
            partByCollider.Add(box, part);
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
                // A hinge's range is authored as flexion (negative is
                // hyperextension); its sign on the joint's X follows the
                // way that segment flexes about the actor's right.
                float sign = spec.FlexionDirection * JointFlexionSign;
                float lowX = sign >= 0f ? spec.LowX : -spec.HighX;
                float highX = sign >= 0f ? spec.HighX : -spec.LowX;
                joint.lowAngularXLimit = Limit(lowX, 2f);
                joint.highAngularXLimit = Limit(highX, 2f);
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

        /// <summary>
        /// Only what a real body cannot do is switched off: the hero's
        /// own controller capsule, and the two halves of each joint,
        /// which the joint itself keeps apart. Everything else — a thigh
        /// against the other thigh, a forearm against the chest, a shin
        /// against the pelvis — collides, so no limb passes through
        /// another. A pair already overlapping in the idle pose is the
        /// exception: it would explode apart on the first step, so it is
        /// switched off and logged, and the anatomy check asserts there
        /// are none.
        /// </summary>
        private void IgnoreOwnedCollisions()
        {
            var jointed = new HashSet<long>();
            for (int index = 0; index < BodySpecs.Length; index++)
            {
                BodySpec spec = BodySpecs[index];
                if (spec.HasParent)
                {
                    jointed.Add(PairKey(spec.Part, spec.Parent));
                }
            }

            SetCollidersEnabled(true);
            Physics.SyncTransforms();
            livePairs.Clear();
            restingOverlaps.Clear();
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

                Player3DAnatomicalPart currentPart = partByCollider[current];
                for (int second = first + 1;
                     second < colliders.Count;
                     second++)
                {
                    Collider other = colliders[second];
                    Player3DAnatomicalPart otherPart = partByCollider[other];
                    string name = $"{currentPart}/{otherPart}";
                    if (jointed.Contains(PairKey(currentPart, otherPart)))
                    {
                        Physics.IgnoreCollision(current, other, true);
                        continue;
                    }

                    float resting = Penetration(current, other) ?? 0f;
                    if (resting > RestingOverlapMetres)
                    {
                        Physics.IgnoreCollision(current, other, true);
                        restingOverlaps.Add($"{name} {resting:F3} m");
                        GameLog.Warning(
                            "ragdoll",
                            "resting_overlap",
                            GameLog.Field("pair", name),
                            GameLog.Field("metres", resting));
                        continue;
                    }

                    Physics.IgnoreCollision(current, other, false);
                    livePairs.Add(new ColliderPair(current, other, name));
                }
            }

            SetCollidersEnabled(false);
        }

        private static long PairKey(
            Player3DAnatomicalPart first,
            Player3DAnatomicalPart second)
        {
            int low = Mathf.Min((int)first, (int)second);
            int high = Mathf.Max((int)first, (int)second);
            return ((long)low << 32) | (uint)high;
        }

        /// <summary>How deeply two colliders overlap at their current poses; null when they do not.</summary>
        private static float? Penetration(Collider first, Collider second)
        {
            Transform firstTransform = first.transform;
            Transform secondTransform = second.transform;
            return Physics.ComputePenetration(
                first,
                firstTransform.position,
                firstTransform.rotation,
                second,
                secondTransform.position,
                secondTransform.rotation,
                out _,
                out float distance)
                ? distance
                : (float?)null;
        }

        /// <summary>
        /// The joint frames are baked from the pose the bones hold now;
        /// a hinge already bent there would count its limits from that
        /// bend. The idle pose is meant to be straight; say so if not.
        /// </summary>
        private void WarnIfJointFramesBent()
        {
            WarnIfHingeBent(
                Player3DAnatomicalPart.LeftThigh,
                Player3DAnatomicalPart.LeftShin,
                Player3DAnatomicalPart.LeftFoot,
                "left_knee");
            WarnIfHingeBent(
                Player3DAnatomicalPart.RightThigh,
                Player3DAnatomicalPart.RightShin,
                Player3DAnatomicalPart.RightFoot,
                "right_knee");
            WarnIfHingeBent(
                Player3DAnatomicalPart.LeftUpperArm,
                Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.LeftHand,
                "left_elbow");
            WarnIfHingeBent(
                Player3DAnatomicalPart.RightUpperArm,
                Player3DAnatomicalPart.RightForearm,
                Player3DAnatomicalPart.RightHand,
                "right_elbow");
        }

        private void WarnIfHingeBent(
            Player3DAnatomicalPart root,
            Player3DAnatomicalPart hinge,
            Player3DAnatomicalPart tip,
            string name)
        {
            Vector3 pivot = bones[hinge].position;
            float bend = 180f - Vector3.Angle(
                bones[root].position - pivot,
                bones[tip].position - pivot);
            if (bend > JointFrameBentDegrees)
            {
                GameLog.Warning(
                    "ragdoll",
                    "joint_frame_bent",
                    GameLog.Field("joint", name),
                    GameLog.Field("degrees", bend));
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

        /// <summary>
        /// The lying pose: every bone in its parent's space except the
        /// pelvis, the root body physics moved, which is kept in WORLD
        /// space so a root moved under the body afterwards does not
        /// carry the body with it.
        /// </summary>
        private BonePose[] CapturePose()
        {
            Transform pelvisBone = PelvisBody != null
                ? PelvisBody.transform
                : null;
            var result = new BonePose[poseTransforms.Count];
            for (int index = 0; index < poseTransforms.Count; index++)
            {
                Transform target = poseTransforms[index];
                result[index] = new BonePose(target, target == pelvisBone);
            }

            return result;
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

        private readonly struct ColliderPair
        {
            public ColliderPair(Collider first, Collider second, string name)
            {
                First = first;
                Second = second;
                Name = name;
            }

            public Collider First { get; }
            public Collider Second { get; }
            public string Name { get; }
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
                JointAxis axis,
                float flexionDirection)
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
                FlexionDirection = flexionDirection;
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

            /// <summary>
            /// Which way about the actor's right this segment flexes:
            /// <c>+1</c> backward like a shin, <c>-1</c> forward like a
            /// forearm. Ball joints keep their range as written.
            /// </summary>
            public float FlexionDirection { get; }

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
                    JointAxis.Right,
                    1f);
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
                    axis,
                    1f);
            }

            /// <summary>
            /// A knee, an elbow or a hip: a joint whose primary turn is
            /// about the actor's right and whose range is written as
            /// flexion — from this much past straight the other way to
            /// the full fold — mapped onto the joint by the way the
            /// segment flexes (backward for a shin, forward for a forearm
            /// or a thigh).
            /// </summary>
            public static BodySpec Hinge(
                Player3DAnatomicalPart part,
                Player3DAnatomicalPart parent,
                float mass,
                float hyperextension,
                float flexion,
                float yLimit,
                float zLimit,
                float flexionDirection)
            {
                return new BodySpec(
                    part,
                    parent,
                    true,
                    mass,
                    -Mathf.Abs(hyperextension),
                    flexion,
                    yLimit,
                    zLimit,
                    JointAxis.Right,
                    flexionDirection);
            }
        }

        private readonly struct BonePose
        {
            public BonePose(Transform target, bool worldSpace)
            {
                WorldSpace = worldSpace;
                Position = worldSpace ? target.position : target.localPosition;
                Rotation = worldSpace ? target.rotation : target.localRotation;
                LocalScale = target.localScale;
            }

            /// <summary>Captured in world space (the pelvis) rather than the parent's.</summary>
            public bool WorldSpace { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 LocalScale { get; }

            /// <summary>
            /// Moves the target from this captured pose toward the pose
            /// it holds now by <paramref name="progress"/>: zero puts the
            /// capture back, one leaves the target as it is.
            /// </summary>
            public void BlendInto(Transform target, float progress)
            {
                if (WorldSpace)
                {
                    target.SetPositionAndRotation(
                        Vector3.LerpUnclamped(Position, target.position, progress),
                        Quaternion.SlerpUnclamped(Rotation, target.rotation, progress));
                }
                else
                {
                    target.localPosition = Vector3.LerpUnclamped(
                        Position,
                        target.localPosition,
                        progress);
                    target.localRotation = Quaternion.SlerpUnclamped(
                        Rotation,
                        target.localRotation,
                        progress);
                }

                target.localScale = Vector3.LerpUnclamped(
                    LocalScale,
                    target.localScale,
                    progress);
            }
        }
    }

    /// <summary>Where and which way the ragdoll left him lying.</summary>
    public readonly struct PlayerRagdollLyingPose
    {
        public PlayerRagdollLyingPose(
            Vector3 pelvisWorld,
            Vector3 chestWorld,
            float leftShoulderY,
            float rightShoulderY)
        {
            PelvisWorld = pelvisWorld;
            ChestWorld = chestWorld;
            LeftShoulderY = leftShoulderY;
            RightShoulderY = rightShoulderY;
        }

        public Vector3 PelvisWorld { get; }
        public Vector3 ChestWorld { get; }
        public float LeftShoulderY { get; }
        public float RightShoulderY { get; }

        /// <summary>The planar direction from the hips to the chest: which way he lies.</summary>
        public Vector3 LyingAxis
        {
            get
            {
                Vector3 axis = ChestWorld - PelvisWorld;
                axis.y = 0f;
                return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.zero;
            }
        }

        /// <summary>
        /// Which shoulder is nearer the floor, when one clearly is; the
        /// fallback side otherwise (on his back or his face the clip's
        /// side is the fall's).
        /// </summary>
        public FootSide LowerShoulder(FootSide fallback, float deadBand = 0.06f)
        {
            float difference = LeftShoulderY - RightShoulderY;
            if (Mathf.Abs(difference) <= deadBand)
            {
                return fallback;
            }

            return difference < 0f ? FootSide.Left : FootSide.Right;
        }
    }
}
