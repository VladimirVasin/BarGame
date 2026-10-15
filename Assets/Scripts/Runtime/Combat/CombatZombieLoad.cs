using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One fused fish crate per pooled porter. The visible load follows the
    /// original chest; in a fall its collision becomes part of that physical torso.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(310)]
    public sealed partial class CombatZombieLoad : MonoBehaviour
    {
        private static readonly HashSet<Collider> loadColliders = new HashSet<Collider>();
        private CombatActor owner;
        private Transform chest, socket;
        private Vector3 attachedPosition, attachedScale;
        private Quaternion attachedRotation;
        private Collider[] ownColliders;
        private Vector3 motorCentre;
        private float motorHeight, motorRadius;
        private bool motorCollisionsCurrent;
        private RaycastHit[] travelHits = new RaycastHit[16];
        private readonly List<CombatWeaponGeometry.Segment> contactSegments = new List<CombatWeaponGeometry.Segment>(2);
        private readonly CombatWeaponGeometry.Segment[] authoredSegments = new CombatWeaponGeometry.Segment[2];
        private readonly Part[] railOwners = new Part[2];
        private BoxCollider envelopeCollider;
        private Collider ramProxy;
        private bool ownsBodyProxyIgnore;
        private PlayableGraph catchGraph;
        private AnimationClipPlayable catchPlayable;
        private SkinnedMeshRenderer catchSkin;
        private Bounds attachedCatchBounds;
        private double catchClock;
        private int catchFrame = -1, catchRate;

        public Transform Model { get; private set; }
        public BoxCollider Collider { get; private set; }
        public Rigidbody PhysicsBody { get; private set; }
        public bool IsCarrying { get; private set; }
        internal IReadOnlyList<CombatWeaponGeometry.Segment> ContactSegments => contactSegments;
        internal Pose ContactPose => new Pose(Model.position, Model.rotation);
        internal int CatchGraphBuildCount { get; private set; }
        internal float CatchClockSeconds => (float)catchClock;

        internal static bool IsLoadCollider(Collider shape) => shape != null && loadColliders.Contains(shape);

        internal void Initialize(CombatActor actor)
        {
            owner = actor;
            chest = CityPedestrianHandProps.FindSocket(owner.DamageRigRoot, "chest");
            if (chest == null) throw new InvalidOperationException("A porter requires his original chest bone.");
            Model = Instantiate(CombatZombieAssetProvider.LoadCrate()).transform;
            Model.name = "Porter fish crate";
            socket = CityPedestrianHandProps.FindSocket(Model, "ChestSocket");
            if (socket == null) throw new InvalidOperationException("Porter load lacks its authored chest socket.");
            foreach (Renderer renderer in Model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = CombatZombieAssetProvider.CrateMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Transform collision = CityPedestrianHandProps.FindSocket(Model, "CollisionBox");
            Transform minimum = CityPedestrianHandProps.FindSocket(collision, "BoundsMin");
            Transform maximum = CityPedestrianHandProps.FindSocket(collision, "BoundsMax");
            if (collision == null || minimum == null || maximum == null)
                throw new InvalidOperationException("Fused load lacks its measured wooden collision bounds.");
            Vector3 boundsA = collision.InverseTransformPoint(minimum.position), boundsB = collision.InverseTransformPoint(maximum.position);
            Collider = collision.gameObject.AddComponent<BoxCollider>();
            Collider.center = (boundsA + boundsB) * .5f;
            Collider.size = new Vector3(Mathf.Abs(boundsB.x - boundsA.x), Mathf.Abs(boundsB.y - boundsA.y), Mathf.Abs(boundsB.z - boundsA.z));
            envelopeCollider = Collider;
            envelopeCollider.enabled = false;
            PhysicsBody = owner.Ragdoll.PhysicsController.ChestBody;
            if (PhysicsBody == null) throw new InvalidOperationException("The fused load requires the original physical torso.");
            // Both assets retain their FBX unit factors. Align the authored bone
            // frames in world space before parenting; never guess local axes/metres.
            float ratio = chest.lossyScale.x / socket.lossyScale.x;
            Model.localScale *= ratio;
            Model.rotation = chest.rotation * Quaternion.Inverse(socket.rotation) * Model.rotation;
            Model.position += chest.position - socket.position;
            Model.SetParent(chest, true);
            ownColliders = owner.GetComponentsInChildren<Collider>(true);
            InitializeParts();
            for (int index = 0; index < authoredSegments.Length; index++)
            {
                string prefix = "RamBar" + (index + 1);
                Transform a = CityPedestrianHandProps.FindSocket(Model, prefix + "A");
                Transform b = CityPedestrianHandProps.FindSocket(Model, prefix + "B");
                Transform radial = CityPedestrianHandProps.FindSocket(Model, "RamRadius" + (index + 1));
                if (a == null || b == null || radial == null)
                    throw new InvalidOperationException("The fused load requires measured wooden ram rails.");
                Quaternion inverse = Quaternion.Inverse(Model.rotation);
                authoredSegments[index] = new CombatWeaponGeometry.Segment(
                    inverse * (a.position - Model.position), inverse * (b.position - Model.position),
                    Vector3.Distance(a.position, radial.position));
                foreach (Part part in boards) if (a.IsChildOf(part.Bone)) { railOwners[index] = part; break; }
                if (railOwners[index] == null) throw new InvalidOperationException("Each ram rail belongs to a detachable board.");
            }
            attachedPosition = Model.localPosition; attachedRotation = Model.localRotation;
            attachedScale = Model.localScale;
            InitializeCatch();
            InitializeReleasedCatch();
            ResetLoad();
        }

        internal void ResetLoad()
        {
            if (Model == null) return;
            RestoreRamProxy();
            Model.SetParent(chest, false);
            Model.SetLocalPositionAndRotation(attachedPosition, attachedRotation);
            Model.localScale = attachedScale;
            Model.gameObject.SetActive(true);
            ResetParts();
            IsCarrying = true;
            catchClock = 0d;
            catchFrame = -1;
            ResetReleasedCatch();
            if (catchGraph.IsValid()) { catchGraph.Play(); SampleCatch(true); }
            RefreshPhysicsCollisions();
            CombatCorpseTraversal.RefreshActor(owner);
            UpdatePartBounds();
        }

        internal void RefreshPhysicsCollisions()
        {
            RefreshRamProxyState();
            if (Collider == null || !Collider.enabled || ownColliders == null) return;
            // Enabling anatomical colliders at a ragdoll handoff can reset PhysX
            // exclusions. Reapply at that lifecycle edge, before simulation.
            foreach (Part part in core) IgnoreOwn(part.Attached);
            foreach (Part part in boards) IgnoreOwn(part.Detached ? part.Loose : part.Attached);
            foreach (Fish fish in fishParts) if (fish != null && fish.Released) IgnoreOwn(fish.Shape);
            RefreshMotorCollisionExclusions();
        }

        // Capsule resizing can replace its native shape. Reconcile only this
        // motor's cached pairs, rather than every anatomical pair each frame.
        internal void RefreshMotorCollisionExclusions()
        {
            CharacterController motor = owner != null ? owner.Body : null;
            motorCollisionsCurrent = motor != null && motor.enabled && motor.gameObject.activeInHierarchy;
            if (!motorCollisionsCurrent) return;
            motorCentre = motor.center; motorHeight = motor.height; motorRadius = motor.radius;
            foreach (Part part in core) IgnoreMotor(part.Attached, motor);
            foreach (Part part in boards) IgnoreMotor(part.Detached ? part.Loose : part.Attached, motor);
            foreach (Fish fish in fishParts) if (fish != null && fish.Released) IgnoreMotor(fish.Shape, motor);
            CombatCorpseTraversal.NotifyColliderEnabled(motor);
        }

        private static void IgnoreMotor(Collider shape, CharacterController motor)
        {
            if (shape != null && shape.enabled && shape.gameObject.activeInHierarchy)
                Physics.IgnoreCollision(shape, motor);
        }

        private void RefreshChangedMotorCollisionExclusions()
        {
            CharacterController motor = owner != null ? owner.Body : null;
            if (motor == null || !motor.enabled || !motor.gameObject.activeInHierarchy)
            { motorCollisionsCurrent = false; return; }
            if (!motorCollisionsCurrent || motor.center != motorCentre || motor.height != motorHeight || motor.radius != motorRadius)
                RefreshMotorCollisionExclusions();
        }

        private void InitializeCatch()
        {
            foreach (SkinnedMeshRenderer skin in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.name == "ZombieCatchMesh") catchSkin = skin;
            Animator animator = Model.GetComponent<Animator>() ?? Model.gameObject.AddComponent<Animator>();
            if (catchSkin == null)
                throw new InvalidOperationException("The catch requires its retained fish rig.");
            Transform boundsFrame = CityPedestrianHandProps.FindSocket(Model, "CatchBounds");
            Transform minimum = CityPedestrianHandProps.FindSocket(boundsFrame, "BBoxMin");
            Transform maximum = CityPedestrianHandProps.FindSocket(boundsFrame, "BBoxMax");
            if (boundsFrame == null || minimum == null || maximum == null)
                throw new InvalidOperationException("The catch requires its measured animated bounds.");
            Vector3 a = boundsFrame.InverseTransformPoint(minimum.position);
            Vector3 b = boundsFrame.InverseTransformPoint(maximum.position);
            Bounds bounds = default;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3((corner & 1) == 0 ? a.x : b.x,
                    (corner & 2) == 0 ? a.y : b.y, (corner & 4) == 0 ? a.z : b.z);
                Vector3 point = catchSkin.transform.InverseTransformPoint(boundsFrame.TransformPoint(local));
                if (corner == 0) bounds = new Bounds(point, Vector3.zero);
                else bounds.Encapsulate(point);
            }
            catchSkin.localBounds = bounds;
            attachedCatchBounds = bounds;
            catchSkin.updateWhenOffscreen = false;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            catchGraph = PlayableGraph.Create("Porter catch");
            catchGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            catchPlayable = AnimationClipPlayable.Create(catchGraph, CombatZombieAssetProvider.LoadCatchClip());
            catchPlayable.SetApplyFootIK(false);
            catchPlayable.SetApplyPlayableIK(false);
            catchPlayable.SetSpeed(0d);
            AnimationPlayableOutput.Create(catchGraph, "Fish", animator).SetSourcePlayable(catchPlayable);
            CatchGraphBuildCount++;
        }

        internal void AdvanceCatch(float seconds)
        {
            FreezeParts(simulationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused);
            if (!isActiveAndEnabled || !Model.gameObject.activeInHierarchy || seconds <= 0f ||
                PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            catchClock = (catchClock + seconds) % CombatZombieAssetProvider.CatchCycleSeconds;
            SampleCatch();
            AdvanceReleasedParts(seconds);
        }

        private void SampleCatch(bool reset = false)
        {
            if (!reset && ReleasedFishCount == 8 && SettledFishCount == 8) return;
            int rate = reset || catchSkin.isVisible ? 30 : 10;
            double time = (catchClock + (owner.JournalActorId % 8) * .37d) % CombatZombieAssetProvider.CatchCycleSeconds;
            int frame = (int)(time * rate);
            if (frame == catchFrame && rate == catchRate) return;
            catchFrame = frame; catchRate = rate;
            catchPlayable.SetTime(frame / (double)rate);
            catchGraph.Evaluate(0f);
            CaptureFishSample();
            RestoreReleasedPoses();
        }

        internal Vector3 LimitTravel(Vector3 travel)
        {
            float length = travel.magnitude;
            if (!IsCarrying || length <= .000001f) return travel;
            // Most movement has no nearby solid. Only a broad envelope hit
            // spends the bounded per-board casts needed to respect real holes.
            if (PartTravel(envelopeCollider, travel) + .000001f >= length) return travel;
            float allowed = length;
            foreach (Part part in core) allowed = Mathf.Min(allowed, PartTravel(part.Attached, travel));
            foreach (Part part in boards) if (!part.Detached) allowed = Mathf.Min(allowed, PartTravel(part.Attached, travel));
            return travel * (allowed / length);
        }

        private float PartTravel(BoxCollider shape, Vector3 travel)
        {
            float length = travel.magnitude;
            Transform frame = shape.transform;
            Vector3 scale = frame.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 half = Vector3.Scale(shape.size, scale) * .5f;
            Vector3 centre = frame.TransformPoint(shape.center), direction = travel / length;
            RaycastHit[] contacts = CombatTraversalQueries.BoxCast(centre, half, direction, frame.rotation,
                ref travelHits, length + .01f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, out int count);
            float allowed = length;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = contacts[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(owner.transform) ||
                    hit.collider.transform.IsChildOf(debrisRoot) || CombatCorpseTraversal.IsCorpse(hit.collider) || hit.normal.y > .6f) continue;
                if (hit.collider == ramProxy) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - .01f));
            }
            return allowed;
        }

        internal void BeginRamAgainst(CharacterController proxy)
        {
            if (ramProxy == proxy) return;
            RestoreRamProxy();
            ramProxy = proxy;
            if (ramProxy == null) return;
            foreach (Part part in core) IgnoreRam(part);
            foreach (Part part in boards) if (!part.Detached) IgnoreRam(part);
            // A motor capsule is wider than the hero's thighs at this low
            // contact height. During the ram, real skin sweeps stop the load.
            if (owner.Body != null && !Physics.GetIgnoreCollision(owner.Body, ramProxy))
            {
                Physics.IgnoreCollision(owner.Body, ramProxy, true);
                ownsBodyProxyIgnore = true;
            }
        }

        private void RestoreRamProxy()
        {
            foreach (Part part in core) RestoreRam(part);
            foreach (Part part in boards) RestoreRam(part);
            if (ramProxy != null && owner.Body != null && ownsBodyProxyIgnore)
            {
                Physics.IgnoreCollision(owner.Body, ramProxy, false);
                CombatCorpseTraversal.NotifyColliderEnabled(owner.Body);
            }
            ramProxy = null;
            ownsBodyProxyIgnore = false;
        }

        internal void EndRam() => RestoreRamProxy();

        internal void RefreshRamProxyState()
        {
            if (owner.State.Phase != MeleePhase.Active || owner.IsZombieFrenzied || owner.IsKnockedDown || owner.IsRagdollActive)
                RestoreRamProxy();
        }

        internal void Suspend()
        {
            if (Model == null) return;
            RestoreRamProxy();
            FreezeParts(true);
            if (catchGraph.IsValid()) catchGraph.Stop();
            foreach (Part part in core) part.Attached.enabled = false;
            foreach (Part part in boards) { part.Attached.enabled = false; part.Loose.enabled = false; }
            debrisRoot.gameObject.SetActive(false);
            Model.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            RestoreRamProxy();
            if (catchGraph.IsValid()) catchGraph.Destroy();
            ReleaseParts();
            if (debrisRoot != null) Destroy(debrisRoot.gameObject);
            if (Model != null) Destroy(Model.gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { loadColliders.Clear(); partOwners.Clear(); }
    }
}
