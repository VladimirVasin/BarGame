using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One solid interior leaf, operated by the shared visible hero action.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class InteriorHingedDoorInteraction : MonoBehaviour, IInteractable
    {
        public const float SwingSeconds = .65f;
        private static readonly PlayerAnimatedInteractionDefinition Definition = new PlayerAnimatedInteractionDefinition(
            "DoorUseEnter", "DoorUseLoop", "DoorUseExit", 6, 12f, 2, 8f, 6, 12f);
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private CharacterController body;
        private Transform hinge, outsideHandle, insideHandle;
        private BoxCollider leaf;
        private VillageWorkroomHandContacts hands;
        private Quaternion closedRotation;
        private Matrix4x4 leafFromHinge;
        private Vector3 outsideDock, insideDock, closedNormal, contactLocal;
        private float angle, initialFraction, targetFraction, elapsed;
        private bool owns, exitRequested;

        public event Action<bool> InteractionFinished;
        public float OpenFraction { get; private set; }
        public bool IsOpen => OpenFraction >= .999f;
        public bool IsClosed => OpenFraction <= .001f;
        public bool OwnsActiveInteraction => owns;
        public PlayerDoorActionPlan Plan { get; private set; }
        public Transform RightGrip => hands?.RightGrip;
        public float RightWristDistance => hands?.RightWristDistance ?? 0f;
        public float RightReachLimit => hands?.RightReachLimit ?? 0f;
        public float ContactWeight { get; private set; }
        public string PromptKey => OpenFraction >= .5f ? "interaction.close_village_door" : "interaction.open_village_door";
        public Vector3 InteractionPosition => hinge != null ?
            (IsInside ? insideHandle.position : outsideHandle.position) : transform.position;

        private bool IsInside => player.GameObject != null &&
            Vector3.Dot(player.GameObject.transform.position - hinge.position, closedNormal) > 0f;

        public void Initialize(PlayerRuntime runtime, PlayerAnimatedInteractionController sharedController,
            Transform pivot, BoxCollider solidLeaf, Transform outerHandle, Transform innerHandle,
            Vector3 groundedOutsideDock, Vector3 groundedInsideDock, float openAngle = -90f)
        {
            if (runtime.GameObject == null || sharedController == null || !sharedController.IsInitialized ||
                pivot == null || solidLeaf == null || solidLeaf.isTrigger || !solidLeaf.enabled ||
                outerHandle == null || innerHandle == null || !solidLeaf.transform.IsChildOf(pivot) ||
                float.IsNaN(openAngle) || float.IsInfinity(openAngle) || Mathf.Abs(openAngle) > 90f || Mathf.Abs(openAngle) < 1f)
                throw new ArgumentException("An interior door requires a solid leaf, measured handles/docks and the live shared hero.");
            Cancel(); Unsubscribe();
            player = runtime; controller = sharedController; hinge = pivot; leaf = solidLeaf;
            outsideHandle = outerHandle; insideHandle = innerHandle;
            outsideDock = groundedOutsideDock; insideDock = groundedInsideDock; angle = openAngle;
            closedRotation = hinge.localRotation;
            closedNormal = hinge.forward;
            leafFromHinge = hinge.worldToLocalMatrix * leaf.transform.localToWorldMatrix;
            body = runtime.GameObject.GetComponent<CharacterController>();
            Player3DAssetRegistry registry = runtime.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            if (body == null || registry == null) throw new InvalidOperationException("The door requires the production hero capsule and rig.");
            hands = new VillageWorkroomHandContacts(runtime.GameObject.transform, registry);
            OpenFraction = 0f;
            controller.PhaseChanged += OnPhase;
            controller.InteractionCompleted += OnCompleted;
        }

        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && !owns && hinge != null &&
            leaf != null && leaf.enabled && !leaf.isTrigger && controller != null && controller.isActiveAndEnabled &&
            controller.Phase == PlayerAnimatedInteractionPhase.Idle && interactor != null &&
            interactor == player.Interactor && interactor.InputEnabled && !GameTimeScaleRuntime.IsPaused &&
            !SceneTransitionService.IsTransitioning && !CounterMenuInput.IsBlockedByOtherUi();

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) Begin(); }

        public bool Begin()
        {
            if (!CanInteract(player.Interactor)) return false;
            bool inside = IsInside;
            initialFraction = OpenFraction;
            targetFraction = OpenFraction < .5f ? 1f : 0f;
            Vector3 dock = inside ? insideDock : outsideDock;
            contactLocal = hinge.InverseTransformPoint((inside ? insideHandle : outsideHandle).position);
            if (OpenFraction >= .5f)
            {
                if (inside)
                {
                    // Beyond the entire leaf arc, not in the wedge that it must cross.
                    float width = Mathf.Max(leaf.size.x, leaf.size.z) * Mathf.Abs(leaf.transform.lossyScale.x);
                    Vector3 local = new Vector3(.35f, 0f, width + .35f);
                    dock = ClosedPoint(local) + Vector3.up * PlayerFactory.GroundedRootOffset;
                }
                else
                {
                    // An inward open handle is out of reach from the hall. Take
                    // the near panel edge, then release the same physical leaf.
                    contactLocal.x = .18f;
                    dock = ClosedPoint(new Vector3(.40f, 0f, -.42f)) +
                        Vector3.up * PlayerFactory.GroundedRootOffset;
                }
            }
            if (IntersectsSweep(OpenFraction, targetFraction, dock - player.GameObject.transform.position)) return false;
            Vector3 contact = hinge.TransformPoint(contactLocal);
            Vector3 facing = contact - dock; facing.y = 0f;
            if (facing.sqrMagnitude < .0001f) return false;
            facing = Quaternion.AngleAxis(-45f, Vector3.up) * facing.normalized;
            Plan = PlayerDoorActionPlan.CreateStationary(contact, dock, facing);
            owns = true; exitRequested = false; elapsed = 0f;
            try
            {
                bool accepted = controller.BeginPositioned(Definition, Plan.EntryPose, Plan.ActionHipPosition, Plan.ExitPose, .35f);
                if (!accepted) Finish(false);
                return accepted;
            }
            catch { Finish(false); throw; }
        }

        private Vector3 ClosedPoint(Vector3 local) =>
            hinge.parent != null ? hinge.parent.TransformPoint(hinge.localPosition + closedRotation * local) : hinge.position + closedRotation * local;

        /// <summary>Moves only through a capsule-free sweep; never disables the solid leaf.</summary>
        public bool TrySetOpenFraction(float fraction) => ApplyFraction(fraction, false);

        private bool ApplyFraction(float fraction, bool cleanup)
        {
            if (hinge == null || body == null || !cleanup && GameTimeScaleRuntime.IsPaused ||
                float.IsNaN(fraction) || float.IsInfinity(fraction)) return false;
            fraction = Mathf.Clamp01(fraction);
            if (IntersectsSweep(OpenFraction, fraction, Vector3.zero)) return false;
            OpenFraction = fraction;
            hinge.localRotation = closedRotation * Quaternion.Euler(0f, angle * fraction, 0f);
            return true;
        }

        public bool Cancel()
        {
            if (!owns) return false;
            // A blocked rollback stays physically where it is; the next E may
            // finish either direction, and the collider never traps a capsule.
            ApplyFraction(initialFraction, true);
            Finish(false);
            controller?.CancelActiveInteraction();
            return true;
        }

        private void Update()
        {
            if (!owns) return;
            if (hinge == null || leaf == null || controller == null || !controller.isActiveAndEnabled ||
                SceneTransitionService.IsTransitioning) { Cancel(); return; }
            if (GameTimeScaleRuntime.IsPaused || exitRequested || controller.Phase != PlayerAnimatedInteractionPhase.Looping) return;
            float step = Mathf.Max(0f, Time.deltaTime);
            float next = Mathf.MoveTowards(OpenFraction, targetFraction, step / SwingSeconds);
            if (!TrySetOpenFraction(next)) { Cancel(); return; }
            elapsed += step;
            if (Mathf.Abs(OpenFraction - targetFraction) <= .001f)
            {
                exitRequested = true;
                controller.RequestExitAtLoopBoundaryWithClip("DoorUseExit");
            }
        }

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (owns && phase == PlayerAnimatedInteractionPhase.Idle)
            {
                ApplyFraction(initialFraction, true);
                Finish(false);
            }
        }
        private void OnCompleted() { if (owns) Finish(exitRequested); }
        private void LateUpdate() => RefreshHandContact();

        public void RefreshHandContact()
        {
            ContactWeight = 0f;
            if (!owns || controller == null || hinge == null) return;
            if (controller.Phase == PlayerAnimatedInteractionPhase.Entering) ContactWeight = Smooth(controller.PhaseProgress);
            else if (controller.Phase == PlayerAnimatedInteractionPhase.Looping) ContactWeight = Smooth(1f - elapsed / .15f);
            if (ContactWeight <= 0f) return;
            VillageWorkroomHandContacts.RefreshPresentation(player, controller);
            hands.Apply(hinge.TransformPoint(contactLocal), null, ContactWeight);
        }

        private bool IntersectsSweep(float from, float to, Vector3 bodyOffset)
        {
            if (body == null || !body.enabled || leaf == null || !leaf.enabled) return true;
            Vector3 scale = body.transform.lossyScale;
            float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 centre = body.transform.TransformPoint(body.center) + bodyOffset;
            Vector3 a = centre - body.transform.up * halfSegment, b = centre + body.transform.up * halfSegment;
            Bounds bounds = new Bounds(leaf.center, leaf.size);
            Vector3 leafScale = leaf.transform.lossyScale;
            float minimumScale = Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(leafScale.x), Mathf.Abs(leafScale.y), Mathf.Abs(leafScale.z)));
            float reach = 0f;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    bounds.center.y, (corner & 2) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 point = leafFromHinge.MultiplyPoint3x4(local);
                reach = Mathf.Max(reach, new Vector2(point.x, point.z).magnitude);
            }
            reach *= Mathf.Max(Mathf.Abs(hinge.lossyScale.x), Mathf.Abs(hinge.lossyScale.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) * Mathf.Abs(angle) / 2f));
            float arcMargin = Mathf.Abs(to - from) * Mathf.Abs(angle) * Mathf.Deg2Rad / steps * .5f * reach;
            float clearance = (radius + .003f + arcMargin) / minimumScale;
            Matrix4x4 parent = hinge.parent != null ? hinge.parent.localToWorldMatrix : Matrix4x4.identity;
            for (int i = 0; i <= steps; i++)
            {
                Quaternion rotation = closedRotation * Quaternion.Euler(0f, angle * Mathf.Lerp(from, to, i / (float)steps), 0f);
                Matrix4x4 inverse = (parent * Matrix4x4.TRS(hinge.localPosition, rotation, hinge.localScale) * leafFromHinge).inverse;
                if (SegmentBoxDistanceSquared(inverse.MultiplyPoint3x4(a), inverse.MultiplyPoint3x4(b), bounds) <= clearance * clearance) return true;
            }
            return false;
        }

        private static float SegmentBoxDistanceSquared(Vector3 a, Vector3 b, Bounds bounds)
        {
            float low = 0f, high = 1f;
            for (int i = 0; i < 20; i++)
            {
                float first = (2f * low + high) / 3f, second = (low + 2f * high) / 3f;
                if (bounds.SqrDistance(Vector3.Lerp(a, b, first)) <= bounds.SqrDistance(Vector3.Lerp(a, b, second))) high = second;
                else low = first;
            }
            return Mathf.Min(bounds.SqrDistance(a), bounds.SqrDistance(b), bounds.SqrDistance(Vector3.Lerp(a, b, (low + high) * .5f)));
        }
        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
        private void Finish(bool completed)
        {
            if (!owns) return;
            owns = false; ContactWeight = 0f;
            InteractionFinished?.Invoke(completed);
        }
        private void Unsubscribe()
        {
            if (controller == null) return;
            controller.PhaseChanged -= OnPhase;
            controller.InteractionCompleted -= OnCompleted;
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() { Cancel(); Unsubscribe(); InteractionFinished = null; }
    }
}
