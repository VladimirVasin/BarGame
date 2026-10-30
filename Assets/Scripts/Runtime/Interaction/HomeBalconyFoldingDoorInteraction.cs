using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Four solid imported leaves, folded on the room side of the threshold.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class HomeBalconyFoldingDoorInteraction : MonoBehaviour, IInteractable
    {
        public const int PanelCount = 4;
        public const float FoldAngle = 80f;
        public const float MoveSeconds = 1.4f;
        private static readonly PlayerAnimatedInteractionDefinition Definition = new PlayerAnimatedInteractionDefinition(
            "DoorUseEnter", "DoorUseLoop", "DoorUseExit", 6, 12f, 2, 8f, 6, 12f);
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private CharacterController body;
        private VillageWorkroomHandContacts hands;
        private Transform room;
        private BoxCollider threshold, northGuard;
        private float outsideFloorDockX, outsideMaximumZ;
        private PlayerAnimatedInteractionPhase previousPhase;
        private Collider[] overlaps = new Collider[16];
        private float width, height, initialFraction, targetFraction, motionTime;
        private bool owns, exitRequested;

        public Transform DoorRoot => transform;
        public Transform[] Panels { get; private set; }
        public BoxCollider[] PanelColliders { get; private set; }
        public Transform InsideHandle { get; private set; }
        public Transform OutsideHandle { get; private set; }
        public float OpenFraction { get; private set; }
        public float LeafHeight => height;
        public bool IsOpen => OpenFraction >= .999f;
        public bool IsClosed => OpenFraction <= .001f;
        public bool IsSealed { get; private set; }
        public bool OwnsActiveInteraction => owns;
        public PlayerDoorActionPlan Plan { get; private set; }
        public Transform RightGrip => hands?.RightGrip;
        public float RightWristDistance => hands?.RightWristDistance ?? 0f;
        public float RightReachLimit => hands?.RightReachLimit ?? 0f;
        public float ContactWeight { get; private set; }
        public string LastBlockReason { get; private set; } = string.Empty;
        public Vector3 GroundedInsideDock => Dock(false);
        public Vector3 GroundedOutsideDock => Dock(true);
        public string PromptKey => OpenFraction >= .5f ? "interaction.close_village_door" : "interaction.open_village_door";
        private bool IsOutside => player.GameObject != null &&
            transform.InverseTransformPoint(player.GameObject.transform.position).x > 0f;
        public Vector3 InteractionPosition => (IsOutside ? OutsideHandle : InsideHandle) != null
            ? (IsOutside ? OutsideHandle : InsideHandle).position : transform.position;

        internal void Initialize(Transform[] panels, BoxCollider[] colliders, Transform insideHandle,
            Transform outsideHandle, float closedWidth, float closedHeight)
        {
            if (panels == null || panels.Length != PanelCount || colliders == null || colliders.Length != PanelCount ||
                insideHandle == null || outsideHandle == null || closedWidth <= 0f || closedHeight <= 0f)
                throw new ArgumentException("The folding door requires four measured imported leaves and two contacts.");
            Panels = panels; PanelColliders = colliders; InsideHandle = insideHandle; OutsideHandle = outsideHandle;
            width = closedWidth; height = closedHeight; room = transform.parent.parent;
            threshold = transform.parent.Find("Home Balcony Threshold")?.GetComponent<BoxCollider>();
            northGuard = transform.parent.Find("Home Balcony North Guard")?.GetComponent<BoxCollider>();
            if (threshold == null || northGuard == null)
                throw new InvalidOperationException("The folding door requires its measured threshold and balcony boundary.");
            ValidateHandleMount(insideHandle, true);
            ValidateHandleMount(outsideHandle, false);
            ApplyPose(1f);
        }

        private void ValidateHandleMount(Transform handle, bool inside)
        {
            Transform freeLeaf = Panels[PanelCount - 1];
            Mesh mesh = handle.GetComponent<MeshFilter>()?.sharedMesh;
            if (handle.parent != freeLeaf || mesh == null)
                throw new InvalidOperationException("A folding-door pull must use the imported handle on the free last leaf.");
            Bounds bounds = mesh.bounds;
            Vector3 first = freeLeaf.InverseTransformPoint(handle.TransformPoint(bounds.min));
            Vector3 second = freeLeaf.InverseTransformPoint(handle.TransformPoint(bounds.max));
            float minZ = Mathf.Min(first.z, second.z), maxZ = Mathf.Max(first.z, second.z);
            float innerX = inside ? Mathf.Max(first.x, second.x) : Mathf.Min(first.x, second.x);
            float panelWidth = width / PanelCount;
            // The stile is 42 mm wide; its bevel leaves a 34 mm flat mounting face.
            // The 28 mm pull base stays wholly inside it and meets its 22.5 mm face.
            if (minZ < panelWidth - .038f - .001f || maxZ > panelWidth - .004f + .001f ||
                Mathf.Abs(innerX - (inside ? -.0225f : .0225f)) > .001f)
                throw new InvalidOperationException("A folding-door pull must touch the free wooden stile without overlapping glass.");
        }

        public void BindPlayer(PlayerRuntime runtime, PlayerAnimatedInteractionController sharedController)
        {
            Cancel(); Unsubscribe();
            player = runtime; controller = sharedController;
            if (runtime.GameObject == null || controller == null || !controller.IsInitialized)
                throw new ArgumentException("The folding door requires the live shared hero interaction controller.");
            body = runtime.GameObject.GetComponent<CharacterController>();
            var registry = runtime.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            if (body == null || registry == null) throw new InvalidOperationException("Missing folding-door hero contacts.");
            hands = new VillageWorkroomHandContacts(runtime.GameObject.transform, registry);
            // Floor support starts beyond the sill plus the actual capsule and
            // its skin. Reuse that clearance at the balcony's north guard.
            Vector3 scale = body.transform.lossyScale;
            float radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float clearance = (body.radius + body.skinWidth) * radialScale + .03f;
            Vector3 outerSill = threshold.transform.TransformPoint(threshold.center + Vector3.right * threshold.size.x * .5f);
            Vector3 innerGuard = northGuard.transform.TransformPoint(northGuard.center - Vector3.forward * northGuard.size.z * .5f);
            outsideFloorDockX = transform.InverseTransformPoint(outerSill).x + clearance / Mathf.Abs(transform.lossyScale.x);
            outsideMaximumZ = transform.InverseTransformPoint(innerGuard).z - clearance / Mathf.Abs(transform.lossyScale.z);
            controller.PhaseChanged += OnPhase;
            controller.InteractionCompleted += OnCompleted;
        }

        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && !IsSealed && !owns &&
            Panels != null && controller != null && controller.isActiveAndEnabled &&
            controller.Phase == PlayerAnimatedInteractionPhase.Idle && interactor != null && interactor == player.Interactor &&
            interactor.InputEnabled && !GameTimeScaleRuntime.IsPaused && !SceneTransitionService.IsTransitioning &&
            !CounterMenuInput.IsBlockedByOtherUi();

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) Begin(); }

        public bool Begin()
        {
            if (!CanInteract(player.Interactor)) { LastBlockReason = "Interaction is not eligible."; return false; }
            bool outside = IsOutside;
            Vector3 dock = Dock(outside);
            float destination = OpenFraction >= .5f ? 0f : 1f;
            if (SweepBlocked(OpenFraction, destination, dock - player.GameObject.transform.position)) return false;
            Vector3 contact = (outside ? OutsideHandle : InsideHandle).position;
            Vector3 facing = contact - dock; facing.y = 0f;
            if (facing.sqrMagnitude < .0001f) return false;
            // Put the reaching right shoulder nearer the handle, as for the shared hinged action.
            facing = Quaternion.AngleAxis(-64f, Vector3.up) * facing.normalized;
            Plan = PlayerDoorActionPlan.CreateStationary(contact, dock, facing);
            initialFraction = OpenFraction; targetFraction = destination; motionTime = 0f;
            owns = true; exitRequested = false;
            try
            {
                bool accepted = controller.BeginPositioned(Definition, Plan.EntryPose, Plan.ActionHipPosition, Plan.ExitPose, .35f);
                if (!accepted) { LastBlockReason = "Shared controller rejected the authored approach."; Finish(); }
                return accepted;
            }
            catch { Finish(); throw; }
        }

        private Vector3 Dock(bool outside)
        {
            // Each contact follows the real free leaf. Both sides stand on
            // flat floor, and the exterior dock clears the complete sill and
            // north guard with the measured hero capsule, including skin.
            Vector3 contact = transform.InverseTransformPoint((outside ? OutsideHandle : InsideHandle).position);
            float x = outside ? outsideFloorDockX : -.72f;
            float z = outside ? Mathf.Min(contact.z, outsideMaximumZ) : contact.z;
            Vector3 point = transform.TransformPoint(new Vector3(x, 0f, z));
            point.y = room.TransformPoint(Vector3.zero).y + PlayerFactory.GroundedRootOffset;
            return point;
        }

        public bool TrySetOpenFraction(float fraction)
        {
            if (Panels == null || IsSealed || GameTimeScaleRuntime.IsPaused || !Finite(fraction)) return false;
            fraction = Mathf.Clamp01(fraction);
            if (SweepBlocked(OpenFraction, fraction, Vector3.zero)) return false;
            ApplyPose(fraction);
            return true;
        }

        internal bool TrySeal()
        {
            if (Panels == null) return false;
            Cancel();
            if (SweepBlocked(OpenFraction, 0f, Vector3.zero)) return false;
            ApplyPose(0f); IsSealed = true;
            return true;
        }

        internal void RestoreOrdinary()
        {
            Cancel(); IsSealed = false;
            if (!SweepBlocked(OpenFraction, 1f, Vector3.zero)) ApplyPose(1f);
        }

        private void ApplyPose(float fraction)
        {
            OpenFraction = Mathf.Clamp01(fraction);
            for (int i = 0; i < PanelCount; i++)
            {
                PanelPose(i, OpenFraction, out Vector3 position, out Quaternion rotation);
                Panels[i].SetLocalPositionAndRotation(position, rotation);
            }
        }

        private void PanelPose(int index, float fraction, out Vector3 position, out Quaternion rotation)
        {
            float angle = FoldAngle * Mathf.Clamp01(fraction) * Mathf.Deg2Rad;
            float panelWidth = width / PanelCount;
            position = new Vector3((index & 1) == 0 ? 0f : -panelWidth * Mathf.Sin(angle),
                0f, index * panelWidth * Mathf.Cos(angle));
            rotation = Quaternion.Euler(0f, ((index & 1) == 0 ? -1f : 1f) * angle * Mathf.Rad2Deg, 0f);
        }

        private void Update()
        {
            if (!owns) return;
            if (controller == null || !controller.isActiveAndEnabled || SceneTransitionService.IsTransitioning)
            { Cancel(); return; }
            if (GameTimeScaleRuntime.IsPaused || exitRequested || controller.Phase != PlayerAnimatedInteractionPhase.Looping) return;
            float step = Mathf.Max(0f, Time.deltaTime);
            motionTime += step;
            float next = Mathf.Lerp(initialFraction, targetFraction, Smooth(motionTime / MoveSeconds));
            if (!TrySetOpenFraction(next)) { Cancel(); return; }
            if (Mathf.Abs(OpenFraction - targetFraction) < .001f)
            {
                exitRequested = true;
                controller.RequestExitAtLoopBoundaryWithClip("DoorUseExit");
            }
        }

        private void LateUpdate() => RefreshHandContact();
        public void RefreshHandContact()
        {
            ContactWeight = 0f;
            if (!owns || controller == null || hands == null) return;
            if (controller.Phase == PlayerAnimatedInteractionPhase.Entering) ContactWeight = Smooth(controller.PhaseProgress);
            else if (controller.Phase == PlayerAnimatedInteractionPhase.Looping) ContactWeight = 1f - Smooth(motionTime / .35f);
            if (ContactWeight <= 0f) return;
            VillageWorkroomHandContacts.RefreshPresentation(player, controller);
            hands.Apply((IsOutside ? OutsideHandle : InsideHandle).position, null, ContactWeight);
        }

        private bool SweepBlocked(float from, float to, Vector3 actorOffset)
        {
            LastBlockReason = string.Empty;
            if (body == null || Panels == null) return false;
            Physics.SyncTransforms();
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) * FoldAngle / 4f));
            // Each sample covers half an angular interval on either side. The
            // analytic derivative bound is per panel and per axis: the fixed
            // first hinge never inherits the last leaf's full-width travel.
            float halfStep = Mathf.Abs(to - from) * FoldAngle * Mathf.Deg2Rad / steps * .5f;
            Vector3 scale = transform.lossyScale;
            for (int sample = 0; sample <= steps; sample++)
            {
                float fraction = Mathf.Lerp(from, to, sample / (float)steps);
                for (int panel = 0; panel < PanelCount; panel++)
                {
                    PanelPose(panel, fraction, out Vector3 position, out Quaternion rotation);
                    Quaternion worldRotation = transform.rotation * rotation;
                    BoxCollider leaf = PanelColliders[panel];
                    Vector3 centre = transform.TransformPoint(position + rotation * leaf.center);
                    float panelWidth = width / PanelCount;
                    Vector3 travelMargin = Vector3.Scale(new Vector3(panelWidth + leaf.size.x * .5f, 0f,
                        (panel + 1) * panelWidth + leaf.size.x * .5f) * halfStep, Abs(scale));
                    float sine = Mathf.Abs(Mathf.Sin(FoldAngle * fraction * Mathf.Deg2Rad));
                    float cosine = Mathf.Abs(Mathf.Cos(FoldAngle * fraction * Mathf.Deg2Rad));
                    Vector3 localMargin = new Vector3(travelMargin.x * cosine + travelMargin.z * sine, 0f,
                        travelMargin.x * sine + travelMargin.z * cosine);
                    Vector3 half = Vector3.Scale(leaf.size * .5f, Abs(scale)) + localMargin + Vector3.one * .002f;
                    Matrix4x4 inverse = Matrix4x4.TRS(centre, worldRotation, Vector3.one).inverse;
                    if (CapsuleTouchesBox(body, actorOffset, inverse, half))
                    { LastBlockReason = "Hero capsule intersects panel " + (panel + 1) + " sweep."; return true; }
                    int count;
                    while (true)
                    {
                        count = Physics.OverlapBoxNonAlloc(centre, half, overlaps, worldRotation,
                            Physics.AllLayers, QueryTriggerInteraction.Ignore);
                        if (count < overlaps.Length) break;
                        if (overlaps.Length >= 128)
                        {
                            Collider[] all = Physics.OverlapBox(centre, half, worldRotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                            for (int i = 0; i < all.Length; i++) if (Blocks(all[i])) return true;
                            count = 0; break;
                        }
                        Array.Resize(ref overlaps, overlaps.Length * 2);
                    }
                    for (int i = 0; i < count; i++) if (Blocks(overlaps[i])) return true;
                }
            }
            return false;
        }

        private bool Blocks(Collider candidate)
        {
            bool blocked = candidate != null && candidate != body && candidate.enabled &&
                !candidate.isTrigger && !candidate.transform.IsChildOf(transform);
            if (blocked) LastBlockReason = "Door sweep intersects " + candidate.name + ".";
            return blocked;
        }

        private static bool CapsuleTouchesBox(CharacterController capsule, Vector3 offset, Matrix4x4 inverse, Vector3 half)
        {
            if (!capsule.enabled) return false;
            Vector3 scale = capsule.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float segment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 centre = capsule.transform.TransformPoint(capsule.center) + offset;
            Vector3 a = inverse.MultiplyPoint3x4(centre - capsule.transform.up * segment);
            Vector3 b = inverse.MultiplyPoint3x4(centre + capsule.transform.up * segment);
            var bounds = new Bounds(Vector3.zero, half * 2f);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 16; i++)
            {
                float first = (2f * lo + hi) / 3f, second = (lo + 2f * hi) / 3f;
                if (bounds.SqrDistance(Vector3.Lerp(a, b, first)) < bounds.SqrDistance(Vector3.Lerp(a, b, second))) hi = second;
                else lo = first;
            }
            return Mathf.Min(Mathf.Min(bounds.SqrDistance(a), bounds.SqrDistance(b)),
                bounds.SqrDistance(Vector3.Lerp(a, b, (lo + hi) * .5f))) <= radius * radius;
        }

        public bool Cancel()
        {
            if (!owns) return false;
            // Keep a blocked leaf in its last safe pose; never roll it through a body.
            if (!SweepBlocked(OpenFraction, initialFraction, Vector3.zero)) ApplyPose(initialFraction);
            Finish(); controller?.CancelActiveInteraction(); return true;
        }

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (owns && phase == PlayerAnimatedInteractionPhase.Idle)
            {
                if (!exitRequested) LastBlockReason = "Shared " + previousPhase +
                    " cancelled before door completion at " + player.GameObject.transform.position +
                    "; authored dock=" + Plan.EntryRootPosition + ".";
                Finish();
            }
            previousPhase = phase;
        }
        private void OnCompleted() { if (owns) Finish(); }
        private void Finish() { owns = false; ContactWeight = 0f; }
        private void Unsubscribe()
        {
            if (controller == null) return;
            controller.PhaseChanged -= OnPhase; controller.InteractionCompleted -= OnCompleted;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
        private void OnDisable() => Cancel();
        private void OnDestroy() { Cancel(); Unsubscribe(); }
    }
}
