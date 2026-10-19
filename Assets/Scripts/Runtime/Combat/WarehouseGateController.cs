using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Session-owned gate travel; the shared narrative session owns approach, rig, camera and input cleanup.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseGateController : MonoBehaviour, INarrativeActivity
    {
        public const float MaximumLift = 3.05f, UnlockSeconds = 2.7f;
        internal const float UnlockCommitSeconds = 1.45f, ChainSlideSeconds = .35f;
        private const float SettleSeconds = 6f;
        private readonly List<Rigidbody> chainBodies = new List<Rigidbody>(29);
        private readonly List<Collider> chainColliders = new List<Collider>(29);
        private readonly List<Vector3> bodyRestPositions = new List<Vector3>(29);
        private readonly List<Quaternion> bodyRestRotations = new List<Quaternion>(29);
        private readonly Vector3[] suspendedVelocity = new Vector3[29], suspendedSpin = new Vector3[29];
        private readonly List<Transform> leafParts = new List<Transform>(8);
        private readonly List<Vector3> leafRest = new List<Vector3>(8);
        private Transform wheel, padlock, shackle;
        private Quaternion wheelRest, shackleRotation;
        private Vector3 shackleRest, shackleLift;
        private GameObject chainPack, key;
        private MeshRenderer originalChain;
        private Rigidbody lockBody;
        private PlayerInteractor listener;
        private CombatTestRoot owner;
        private bool active, unlocking, confirmArmed, initialized, chainSuspended;
        private float unlockElapsed, settleElapsed;
        private int inputFrame;
        private Vector3 keyRest;
        private Quaternion keyRotation;
        private WarehouseGateState State => GameSessionState.WarehouseGate;
        public float Progress => State.Progress;
        public bool IsUnlocked => State.Unlocked;
        public bool IsOpen => State.IsOpen;
        public float GateLift => Progress * MaximumLift;
        public bool ChainReleased { get; private set; }
        internal bool ChainPhysicsReleased { get; private set; }
        internal float UnlockElapsed => unlockElapsed;
        internal Transform KeyModel => key.transform;
        internal Transform Shackle => shackle;
        internal WarehouseGateAudio Sounds { get; private set; }
        public IReadOnlyList<Rigidbody> ChainBodies => chainBodies;
        public NarrativeInteraction ValveInteraction { get; private set; }
        public NarrativeInteraction DoorInteraction { get; private set; }
        public bool IsActivityComplete => IsOpen;
        public bool ActivityCursorVisible => false;

        internal void Initialize(NarrativeInteraction interaction, Transform wheel, Transform chain, Transform padlock)
        {
            ValveInteraction = interaction; this.wheel = wheel; this.padlock = padlock;
            wheelRest = wheel.localRotation;
            foreach (MeshRenderer renderer in chain.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("YellowChain", StringComparison.Ordinal)) originalChain = renderer;
                if (renderer.name.StartsWith("PadlockShackle", StringComparison.Ordinal)) shackle = renderer.transform;
            }
            if (originalChain == null || shackle == null) throw new InvalidOperationException("Gate needs its chain and opening shackle.");
            // The imported mesh is authored in the complete control frame.
            // Rotate at its retained leg, rather than at that distant FBX origin.
            Transform shackleModel = shackle;
            Bounds meshBounds = shackleModel.GetComponent<MeshFilter>().sharedMesh.bounds;
            Bounds localBounds = default;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                point = padlock.InverseTransformPoint(shackleModel.TransformPoint(point));
                if (corner == 0) localBounds = new Bounds(point, Vector3.zero);
                else localBounds.Encapsulate(point);
            }
            shackle = new GameObject("Warehouse Padlock Hinge").transform;
            shackle.SetParent(padlock, false);
            shackle.localPosition = new Vector3(localBounds.max.x - localBounds.extents.z,
                localBounds.min.y + localBounds.extents.z, localBounds.center.z);
            shackleModel.SetParent(shackle, true);
            shackleRest = shackle.localPosition; shackleRotation = shackle.localRotation;
            shackleLift = padlock.InverseTransformVector(transform.up * .075f);
            BuildChain();
            lockBody = padlock.gameObject.AddComponent<Rigidbody>();
            ConfigureBody(lockBody, .32f);
            var lockCollider = padlock.gameObject.AddComponent<BoxCollider>();
            lockCollider.center = new Vector3(0f, .035f, 0f); lockCollider.size = new Vector3(.16f, .27f, .09f);
            chainBodies.Add(lockBody); chainColliders.Add(lockCollider);
            foreach (Rigidbody body in chainBodies)
            { bodyRestPositions.Add(body.transform.localPosition); bodyRestRotations.Add(body.transform.localRotation); }
            // Adjacent visuals interlock; collision proxies meet only the environment, never fight their joints.
            for (int a = 0; a < chainColliders.Count; a++)
                for (int b = a + 1; b < chainColliders.Count; b++) Physics.IgnoreCollision(chainColliders[a], chainColliders[b]);
            key = WarehouseFishAssemblyAssets.CreateKey(transform);
            key.transform.SetParent(ValveInteraction.SubjectRoot, true);
            foreach (Collider collider in key.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            keyRest = transform.InverseTransformPoint(padlock.position) + new Vector3(0f, .012f, -.075f);
            keyRotation = Quaternion.Euler(0f, -90f, 0f);
            key.SetActive(false);
            Sounds = gameObject.AddComponent<WarehouseGateAudio>();
            Sounds.Initialize(wheel, padlock, () => owner != null && !owner.isActiveAndEnabled);
            foreach (Rigidbody body in chainBodies)
                body.gameObject.AddComponent<WarehouseGateChainContact>().Initialize(Sounds);
            initialized = true;
            if (IsUnlocked) ReleaseChain(true);
            ApplyTravel();
        }

        internal void BindWorld(GameObject gate, GameObject door, WarehouseArenaPlan plan, CombatTestRoot owner)
        {
            this.owner = owner;
            foreach (MeshRenderer renderer in gate.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = renderer.name.StartsWith("Collision_", StringComparison.Ordinal)
                    ? renderer.name.Substring("Collision_".Length) : renderer.name;
                if (!name.StartsWith("GateFrame__", StringComparison.Ordinal) &&
                    !name.StartsWith("VerticalBars__", StringComparison.Ordinal) &&
                    !name.StartsWith("GateCrossbars__", StringComparison.Ordinal) &&
                    !name.StartsWith("LeafBands__", StringComparison.Ordinal)) continue;
                leafParts.Add(renderer.transform); leafRest.Add(renderer.transform.position);
            }
            if (leafParts.Count != 8) throw new InvalidOperationException("Gate travel needs the complete leaf and matching collision.");
            var host = new GameObject("Warehouse Goal Door Interaction");
            host.transform.SetParent(door.transform, false);
            DoorInteraction = host.AddComponent<NarrativeInteraction>();
            Bounds bounds = new Bounds(plan.GoalDoorPosition + Vector3.up * 1.5f, new Vector3(4.25f, 3.15f, .35f));
            Vector3 floor = plan.GoalDoorPosition + Vector3.back * 1.15f;
            DoorInteraction.Configure(new NarrativeInteractionDefinition("warehouse-goal-door", "interaction.warehouse_door",
                new[] { new NarrativePage("combat.warehouse.door.placeholder", compact: true) }, requiresRunningGameTime: false),
                door.transform, NarrativeStagingPlan.Standing(floor, Quaternion.identity, bounds, Vector3.right),
                floor + Vector3.up * .85f, 1.8f);
            DoorInteraction.Admission = interactor => IsOpen && owner != null && owner.IsInitialized &&
                owner.Player.Interactor == interactor && owner.Hero != null && owner.Hero.CanBeginStandingInspection;
            bool ownsFocus = false, previousFocus = false;
            DoorInteraction.PreparingPlayer += interactor =>
            { previousFocus = owner.Hero.CombatFocused; ownsFocus = true; owner.Hero.PrepareStandingInspection(); };
            DoorInteraction.RestoringPlayer += interactor =>
            {
                if (!ownsFocus) return;
                ownsFocus = false;
                if (owner != null && owner.Hero != null) owner.Hero.RestoreStandingInspectionFocus(previousFocus);
            };
            ApplyTravel();
        }

        public bool BeginActivity(PlayerInteractor interactor)
        {
            if (!initialized || active || interactor == null || !IsUnlocked &&
                !GameSessionState.HasInventoryItem(InventoryItemId.WarehouseGateKey)) return false;
            listener = interactor; active = true; unlocking = !IsUnlocked; unlockElapsed = 0f;
            confirmArmed = false; inputFrame = Time.frameCount;
            if (!listener.PromptView.TryHoldPage(this, unlocking ? "combat.warehouse.valve.unlocking" : "combat.warehouse.valve.turn",
                null, "combat.warehouse.valve.exit", CloseActivity, compact: true, requiresRunningGameTime: false))
            { EndActivity(); return false; }
            if (unlocking)
            {
                key.transform.SetLocalPositionAndRotation(keyRest + Vector3.back * .045f, keyRotation);
                key.SetActive(true);
            }
            return true;
        }

        private bool CloseActivity()
        { NarrativeInteractionController.For(listener).Cancel(); return true; }

        public void TickActivity(float seconds) => Advance(seconds);

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!active || !isActiveAndEnabled || owner != null && !owner.isActiveAndEnabled ||
                PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning ||
                !GameInput.MovementFocused || !GameInput.CanRead(GameInputContext.Menu)) return;
            bool held = GameInput.IsHeld(GameInputAction.Interact, GameInputContext.Menu);
            if (Time.frameCount > inputFrame && !held) confirmArmed = true;
            if (unlocking)
            {
                float before = unlockElapsed;
                unlockElapsed = Mathf.Min(UnlockSeconds, unlockElapsed + seconds);
                float turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, .6f, unlockElapsed));
                float withdraw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.15f, 1.38f, unlockElapsed));
                key.transform.localPosition = keyRest + Vector3.back *
                    (.045f * (1f - Mathf.Clamp01(unlockElapsed / .25f)) + .12f * withdraw);
                key.transform.localRotation = Quaternion.AngleAxis(turn * 90f, Vector3.forward) * keyRotation;
                if (before < .25f && unlockElapsed >= .25f) Sounds.PlayKeyTurn();
                if (before < .6f && unlockElapsed >= .6f) Sounds.PlayUnlock();
                if (!IsUnlocked)
                {
                    float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.7f, .92f, unlockElapsed));
                    float swivel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.9f, 1.15f, unlockElapsed));
                    shackle.localPosition = shackleRest + shackleLift * lift;
                    shackle.localRotation = shackleRotation * Quaternion.AngleAxis(65f * swivel, Vector3.up);
                }
                if (unlockElapsed >= UnlockCommitSeconds && !IsUnlocked)
                {
                    if (!GameSessionState.TryUnlockWarehouseGate()) { CloseActivity(); return; }
                    key.SetActive(false);
                    ReleaseChain(false);
                }
                if (unlockElapsed < UnlockSeconds) return;
                unlocking = false; key.SetActive(false);
                listener.PromptView.TryHoldPage(this, "combat.warehouse.valve.turn", null,
                    "combat.warehouse.valve.exit", CloseActivity, compact: true, requiresRunningGameTime: false);
                return;
            }
            bool moved = State.Advance(seconds, confirmArmed && held);
            if (moved) ApplyTravel();
            Sounds.SetTurning(moved && !State.IsOpen);
            if (moved && State.IsOpen) Sounds.PlayFullyOpenLatch();
        }

        public void EndActivity()
        {
            if (listener != null && listener.PromptView != null) listener.PromptView.ReleaseHeldPage(this);
            active = unlocking = false; listener = null;
            if (Sounds != null) { Sounds.SetTurning(false); Sounds.StopActions(); }
            if (key != null) key.SetActive(false);
            if (!IsUnlocked && shackle != null) shackle.SetLocalPositionAndRotation(shackleRest, shackleRotation);
        }

        private void ApplyTravel()
        {
            if (wheel != null) wheel.localRotation = wheelRest * Quaternion.AngleAxis(-360f * WarehouseGateState.Turns * Progress, Vector3.forward);
            for (int i = 0; i < leafParts.Count; i++) leafParts[i].position = leafRest[i] + Vector3.up * GateLift;
        }

        private void BuildChain()
        {
            chainPack = WarehouseGateAssets.Create("ValveChain", transform, Vector3.zero, Quaternion.identity);
            // The released links remain the inspected subject, including for camera occlusion ownership.
            chainPack.transform.SetParent(ValveInteraction.SubjectRoot, true);
            MeshRenderer[] links = chainPack.GetComponentsInChildren<MeshRenderer>(true);
            Array.Sort(links, (a, b) => string.CompareOrdinal(a.name, b.name));
            if (links.Length != 28) throw new InvalidOperationException("Gate chain needs its authored 28 links.");
            foreach (MeshRenderer link in links)
            {
                Bounds bounds = link.bounds;
                var pivot = new GameObject("Warehouse Chain " + link.name).transform;
                pivot.SetParent(chainPack.transform, false);
                pivot.SetPositionAndRotation(bounds.center, transform.rotation);
                link.transform.SetParent(pivot, true);
                Rigidbody body = pivot.gameObject.AddComponent<Rigidbody>(); ConfigureBody(body, .045f);
                var collider = pivot.gameObject.AddComponent<BoxCollider>();
                collider.size = bounds.size;
                chainBodies.Add(body); chainColliders.Add(collider);
            }
            for (int i = 0; i < 28; i++)
            {
                // The opened lock separates Link18/19; all other authored neighbours stay joined.
                if (i == 18) continue;
                Rigidbody a = chainBodies[i], b = chainBodies[(i + 27) % 28];
                var joint = a.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = b; joint.autoConfigureConnectedAnchor = false;
                Vector3 midpoint = (a.position + b.position) * .5f;
                joint.anchor = a.transform.InverseTransformPoint(midpoint);
                joint.connectedAnchor = b.transform.InverseTransformPoint(midpoint);
                joint.lowTwistLimit = new SoftJointLimit { limit = -70f }; joint.highTwistLimit = new SoftJointLimit { limit = 70f };
                joint.swing1Limit = joint.swing2Limit = new SoftJointLimit { limit = 70f };
                joint.enableProjection = true; joint.projectionDistance = .02f;
                joint.enablePreprocessing = false;
            }
            chainPack.SetActive(false);
        }

        private static void ConfigureBody(Rigidbody body, float mass)
        {
            body.mass = mass; body.isKinematic = true; body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.linearDamping = .3f; body.angularDamping = .6f;
            body.solverIterations = 12; body.solverVelocityIterations = 4;
            body.maxAngularVelocity = 12f;
        }

        private void ReleaseChain(bool restored)
        {
            if (ChainReleased) return;
            ChainReleased = true; settleElapsed = 0f;
            ChainPhysicsReleased = restored;
            chainSuspended = false;
            originalChain.gameObject.SetActive(false); chainPack.SetActive(true);
            for (int i = 0; i < chainBodies.Count; i++)
            {
                Rigidbody body = chainBodies[i];
                if (restored)
                {
                    // Rebuild an already-open mechanism as a settled strand, without replaying an unlock.
                    float order = i < 18 ? i + 10 : i - 18;
                    body.position = transform.TransformPoint(new Vector3(-.68f + order * .06f, .065f, -.58f + .025f * Mathf.Sin(order)));
                    body.rotation = transform.rotation * Quaternion.Euler(90f, 0f, 90f);
                }
                body.isKinematic = true;
            }
            if (restored)
            {
                shackle.localPosition = shackleRest + shackleLift;
                shackle.localRotation = shackleRotation * Quaternion.AngleAxis(65f, Vector3.up);
                settleElapsed = SettleSeconds;
            }
            else Sounds.PlayChainRelease();
        }

        private void FixedUpdate()
        {
            if (!ChainReleased || settleElapsed >= SettleSeconds) return;
            bool suspended = !isActiveAndEnabled || owner != null && !owner.isActiveAndEnabled ||
                PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning || !GameInput.MovementFocused;
            SuspendChain(suspended);
            if (suspended) return;
            settleElapsed += Time.fixedDeltaTime;
            if (!ChainPhysicsReleased)
            {
                float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(settleElapsed / ChainSlideSeconds));
                // Ease the open loop clear of the fixed lug before gravity owns
                // it. Neighbours keep their original joint separation throughout.
                Vector3 offset = Vector3.back * (.2f * slide) + Vector3.down * (.055f * slide);
                for (int i = 0; i < chainBodies.Count; i++)
                {
                    Rigidbody body = chainBodies[i];
                    body.position = body.transform.parent.TransformPoint(bodyRestPositions[i]) + transform.TransformVector(offset);
                }
                if (settleElapsed < ChainSlideSeconds) return;
                ChainPhysicsReleased = true;
                foreach (Rigidbody body in chainBodies)
                {
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.isKinematic = false;
                    body.linearVelocity = body.angularVelocity = Vector3.zero;
                }
            }
            if (settleElapsed < SettleSeconds) return;
            // Small visual debris has a bounded simulation lifetime and keeps its final contact pose.
            foreach (Rigidbody body in chainBodies) body.isKinematic = true;
        }

        private void SuspendChain(bool suspended)
        {
            if (chainSuspended == suspended || !ChainPhysicsReleased) return;
            chainSuspended = suspended;
            for (int index = 0; index < chainBodies.Count; index++)
            {
                Rigidbody body = chainBodies[index];
                if (suspended)
                {
                    suspendedVelocity[index] = body.linearVelocity;
                    suspendedSpin[index] = body.angularVelocity;
                    body.isKinematic = true;
                }
                else
                {
                    body.isKinematic = false;
                    body.linearVelocity = suspendedVelocity[index];
                    body.angularVelocity = suspendedSpin[index];
                }
            }
        }

        private void RefreshSession()
        {
            if (!initialized || IsUnlocked) return;
            if (ValveInteraction.IsPresenting) NarrativeInteractionController.For(owner.Player.Interactor).RestoreImmediate();
            EndActivity();
            Sounds?.Reset();
            if (ChainReleased)
            {
                // Inventory/new-game reset rebuilds the source geometry. Ordinary combat resets never reach here.
                ChainReleased = false;
                ChainPhysicsReleased = chainSuspended = false;
                chainPack.SetActive(false); originalChain.gameObject.SetActive(true);
                for (int i = 0; i < chainBodies.Count; i++)
                {
                    Rigidbody body = chainBodies[i]; body.isKinematic = true;
                    body.transform.SetLocalPositionAndRotation(bodyRestPositions[i], bodyRestRotations[i]);
                }
                shackle.SetLocalPositionAndRotation(shackleRest, shackleRotation);
            }
            ApplyTravel();
        }

        private void OnEnable() => GameSessionState.WorldItemsChanged += RefreshSession;
        private void OnDisable()
        {
            GameSessionState.WorldItemsChanged -= RefreshSession;
            if (ValveInteraction != null && ValveInteraction.IsPresenting && owner != null && owner.Player.Interactor != null)
                NarrativeInteractionController.For(owner.Player.Interactor).RestoreImmediate();
            EndActivity();
            Sounds?.Stop();
            SuspendChain(true);
        }
    }
}
