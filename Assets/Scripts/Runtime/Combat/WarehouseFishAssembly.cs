using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Three bounded physical pieces; the shared narrative session owns the hero and camera.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseFishAssembly : MonoBehaviour, INarrativeActivity
    {
        private readonly Rigidbody[] bodies = new Rigidbody[3];
        private readonly Collider[][] partColliders = new Collider[3][];
        private readonly GameObject[] cans = new GameObject[3];
        private readonly bool[] released = new bool[3];
        private CombatTestRoot owner;
        private Transform station;
        private WarehouseFishAssemblyAudio sounds;
        private PlayerInteractor listener;
        private GameObject fish, key;
        private Transform mouth;
        private Collider[] keyColliders;
        private bool active, initialized, previousFocus, playerPrepared, pointerDragging, confirmArmed;
        private int selected = -1, inputFrame;
        private Vector2 pointerOffset;
        private GUIStyle controlsStyle;
        private string controls, gamepadControls;
        private WarehouseFishAssemblyPhase lastVisualPhase;
        private float nextReceiptAttempt;

        public NarrativeInteraction Interaction { get; private set; }
        public WarehouseFishAssemblyState State => GameSessionState.WarehouseFishAssembly;
        public WarehouseFishAssemblyPhase Phase => State.Phase;
        public bool OwnsInteraction => Interaction != null && Interaction.IsPresenting;
        public bool IsActivityComplete => Phase == WarehouseFishAssemblyPhase.Complete && State.RewardGranted;
        public bool ActivityCursorVisible => Phase == WarehouseFishAssemblyPhase.Assembling;
        public Transform TableFrame => station;
        public int SelectedPartIndex => selected;
        public GameObject AssembledFish => fish;
        public Transform KeyModel => key != null ? key.transform : null;
        public Bounds PresentationBounds { get; private set; }
        public float BulbMaxCameraHeight => station.position.y + 1.30f;

        public static WarehouseFishAssembly Create(GameObject table, WarehouseArenaPlan plan, CombatTestRoot root)
        {
            var host = new GameObject("Warehouse Fish Assembly");
            host.transform.SetParent(table.transform, false);
            var activity = host.AddComponent<WarehouseFishAssembly>();
            activity.Initialize(table, plan, root);
            return activity;
        }

        private void Initialize(GameObject table, WarehouseArenaPlan plan, CombatTestRoot root)
        {
            owner = root; station = table.transform;
            // Capture the old solid proxy before adding the actual fish floor/rims.
            Collider[] playerBlocking = table.GetComponentsInChildren<Collider>(true);
            // Frame the playable floor and lifted pieces, with a small edge
            // margin. The tall outer rim must not push the fish away from the lens.
            Bounds bounds = new Bounds(station.TransformPoint(new Vector3(0f, .84f, 0f)), new Vector3(1.90f, .12f, .92f));
            PresentationBounds = bounds;
            WarehouseFishAssemblyAssets.CreatePlaySurface(station);
            for (int index = 0; index < 3; index++)
            {
                GameObject part = WarehouseFishAssemblyAssets.CreatePart(index, station);
                partColliders[index] = part.GetComponentsInChildren<Collider>(true);
                if (partColliders[index].Length == 0) throw new InvalidOperationException("Fish part needs authored collision.");
                bodies[index] = part.AddComponent<Rigidbody>();
                bodies[index].mass = .12f;
                bodies[index].linearDamping = .8f;
                bodies[index].angularDamping = 1.5f;
                bodies[index].constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                bodies[index].collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                bodies[index].interpolation = RigidbodyInterpolation.Interpolate;
                foreach (Collider collider in partColliders[index])
                    foreach (Collider blocker in playerBlocking) Physics.IgnoreCollision(collider, blocker);
                cans[index] = WarehouseArenaAssetProvider.Create(index == 0 ? "RottenFishTailCan" :
                    index == 1 ? "RottenFishBellyCan" : "RottenFishHeadCan", station, Vector3.zero, Quaternion.identity);
                foreach (Collider collider in cans[index].GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            }
            fish = WarehouseFishAssemblyAssets.CreateAssembled(station);
            mouth = WarehouseFishAssemblyAssets.FindAnchor(fish, "KeySpit");
            key = WarehouseFishAssemblyAssets.CreateKey(station);
            keyColliders = key.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in keyColliders) collider.enabled = false;
            sounds = gameObject.AddComponent<WarehouseFishAssemblyAudio>();
            var soundAnchor = new GameObject("Fish Table Sound Anchor").transform;
            soundAnchor.SetParent(transform, false);
            soundAnchor.localPosition = new Vector3(0f, .80f, 0f);
            sounds.Initialize(soundAnchor, owner);
            for (int index = 0; index < 3; index++) bodies[index].gameObject.AddComponent<WarehouseFishPartImpact>().Initialize(sounds);
            // Both gallery tiles are walkable here. This dock keeps the hero
            // outside the work-surface shot without hiding his world rig.
            Vector3 floor = station.TransformPoint(new Vector3(0f, 0f, -2f));
            var staging = NarrativeStagingPlan.Standing(floor, station.rotation, bounds, station.right,
                NarrativeCameraMode.WorkSurfaceCloseUp, station.TransformDirection(new Vector3(0f, 1f, -.08f).normalized),
                maximumCameraHeight: BulbMaxCameraHeight);
            Interaction = gameObject.AddComponent<NarrativeInteraction>();
            Interaction.Configure(new NarrativeInteractionDefinition("warehouse-fish", "interaction.warehouse_fish",
                new[] { new NarrativePage("combat.warehouse.fish.complete", compact: true) }, requiresRunningGameTime: false),
                station, staging, floor + Vector3.up * .85f, 1.7f);
            Interaction.Activity = this;
            Interaction.Admission = CanBegin;
            Interaction.PreparingPlayer += PreparePlayer;
            Interaction.RestoringPlayer += RestorePlayer;
            Interaction.Completed += TryShowReceipt;
            controls = LocalizationService.Get("combat.warehouse.fish.controls");
            gamepadControls = LocalizationService.Get("combat.warehouse.fish.gamepad_controls");
            initialized = true;
            RestorePresentation();
        }

        private bool CanBegin(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || !initialized || owner == null || !owner.IsInitialized || owner.Player.Interactor != interactor ||
                owner.Hero == null || !owner.Hero.CanBeginStandingInspection ||
                !GameInput.CanRead(GameInputContext.Gameplay) || State.RewardGranted && !State.ReceiptPending) return false;
            return State.HasTransferredCans || GameSessionState.HasInventoryItem(InventoryItemId.RottenFishTailCan) &&
                GameSessionState.HasInventoryItem(InventoryItemId.RottenFishBellyCan) &&
                GameSessionState.HasInventoryItem(InventoryItemId.RottenFishHeadCan);
        }

        public bool BeginInteraction()
        {
            if (owner == null || !Interaction.CanInteract(owner.Player.Interactor)) return false;
            Interaction.Interact(owner.Player.Interactor);
            return Interaction.IsPresenting;
        }

        private void PreparePlayer(PlayerInteractor interactor)
        {
            listener = interactor;
            previousFocus = owner.Hero.CombatFocused;
            playerPrepared = true;
            owner.Hero.PrepareStandingInspection();
        }

        private void RestorePlayer(PlayerInteractor interactor)
        {
            if (!playerPrepared) return;
            playerPrepared = false;
            if (owner != null && owner.Hero != null) owner.Hero.RestoreStandingInspectionFocus(previousFocus);
        }

        public bool BeginActivity(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || active || !initialized || !State.HasTransferredCans && !GameSessionState.TryBeginWarehouseFishAssembly()) return false;
            listener = interactor; active = true; selected = -1; pointerDragging = false;
            confirmArmed = false; inputFrame = Time.frameCount;
            RestorePresentation();
            if (Phase == WarehouseFishAssemblyPhase.Assembling) ConfigureDynamics();
            if (State.IsAssembled) BeginFinale();
            return true;
        }

        public void TickActivity(float seconds)
        {
            Advance(seconds);
            if (!active || Phase != WarehouseFishAssemblyPhase.Assembling || Time.frameCount <= inputFrame) return;
            if (!confirmArmed) confirmArmed = !GameInput.IsHeld(GameInputAction.Interact, GameInputContext.Menu);
            if (GameInput.WasPressed(GameInputAction.CombatMode, GameInputContext.Menu)) SelectPart((WarehouseFishPart)((selected + 1) % 3));
            if (GameInput.ModalPointerPressed)
            {
                int hit = PickPart(GameInput.ModalPointerPosition);
                if (hit >= 0 && SelectPart((WarehouseFishPart)hit))
                {
                    pointerDragging = true;
                    if (PointerOnTable(GameInput.ModalPointerPosition, out Vector2 position))
                    { var pose = State.GetPose((WarehouseFishPart)selected); pointerOffset = new Vector2(pose.X, pose.Z) - position; }
                }
            }
            if (selected >= 0)
            {
                var pose = State.GetPose((WarehouseFishPart)selected);
                Vector2 position = new Vector2(pose.X, pose.Z);
                if (pointerDragging && GameInput.ModalPointerHeld && PointerOnTable(GameInput.ModalPointerPosition, out Vector2 pointer))
                    position = pointer + pointerOffset;
                else position += GameInput.ReadModalMovement() * (.32f * Mathf.Max(0f, seconds));
                MoveSelected(position, pose.Yaw + GameInput.ReadModalRotationDelta(seconds));
                if (pointerDragging && !GameInput.ModalPointerHeld) DropSelected();
            }
            if (confirmArmed && GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Menu))
            {
                if (selected >= 0) DropSelected(); else SelectPart(WarehouseFishPart.Tail);
            }
        }

        public void Advance(float seconds)
        {
            if (!active || Suspended || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return;
            if (Phase == WarehouseFishAssemblyPhase.Pouring) SamplePour(Mathf.Min(WarehouseFishAssemblyState.PourSeconds, State.PourElapsed + seconds));
            State.Advance(seconds);
            if (Phase != lastVisualPhase)
            {
                lastVisualPhase = Phase;
                if (Phase == WarehouseFishAssemblyPhase.Assembling) ConfigureDynamics();
                if (Phase == WarehouseFishAssemblyPhase.Convulsing) BeginFinale();
            }
            if (Phase == WarehouseFishAssemblyPhase.Convulsing || Phase == WarehouseFishAssemblyPhase.Spitting ||
                Phase == WarehouseFishAssemblyPhase.Complete) SampleFinale();
            if (Phase == WarehouseFishAssemblyPhase.Complete) GameSessionState.TryGrantWarehouseFishKey();
        }

        public Rigidbody GetPartBody(WarehouseFishPart part) => bodies[(int)part];

        public bool SelectPart(WarehouseFishPart part)
        {
            if (!active || Suspended || Phase != WarehouseFishAssemblyPhase.Assembling) return false;
            SavePhysics(); selected = (int)part;
            ConfigureDynamics();
            return true;
        }

        public bool MoveSelected(Vector2 tableXZ, float yawDegrees)
        {
            if (!active || Suspended || selected < 0 || Phase != WarehouseFishAssemblyPhase.Assembling ||
                !float.IsFinite(tableXZ.x) || !float.IsFinite(tableXZ.y) || !float.IsFinite(yawDegrees)) return false;
            // Clamp the actual group's projected envelope, rather than shrinking the whole work area.
            var previous = State.GetPose((WarehouseFishPart)selected);
            float turn = Mathf.DeltaAngle(previous.Yaw, yawDegrees) * Mathf.Deg2Rad;
            float minX = -.09f, maxX = .09f, minZ = -.09f, maxZ = .09f;
            if (State.IsPartLinked((WarehouseFishPart)selected))
                for (int index = 0; index < 3; index++)
                {
                    if (index == selected || index != 1 && !State.IsPartLinked((WarehouseFishPart)index)) continue;
                    var other = State.GetPose((WarehouseFishPart)index);
                    float dx = other.X - previous.X, dz = other.Z - previous.Z;
                    float x = dx * Mathf.Cos(turn) + dz * Mathf.Sin(turn);
                    float z = dz * Mathf.Cos(turn) - dx * Mathf.Sin(turn);
                    minX = Mathf.Min(minX, x - .09f); maxX = Mathf.Max(maxX, x + .09f);
                    minZ = Mathf.Min(minZ, z - .09f); maxZ = Mathf.Max(maxZ, z + .09f);
                }
            tableXZ.x = Mathf.Clamp(tableXZ.x, -.88f - minX, .88f - maxX);
            tableXZ.y = Mathf.Clamp(tableXZ.y, -.38f - minZ, .38f - maxZ);
            if (!State.SetPose((WarehouseFishPart)selected, new WarehouseFishPartPose(tableXZ.x, tableXZ.y, yawDegrees))) return false;
            ApplyGroupPose(selected);
            return true;
        }

        public bool DropSelected()
        {
            if (!active || Suspended || selected < 0 || Phase != WarehouseFishAssemblyPhase.Assembling) return false;
            SavePhysics();
            State.TrySnap((WarehouseFishPart)selected);
            selected = -1; pointerDragging = false;
            if (State.IsAssembled) { lastVisualPhase = Phase; BeginFinale(); }
            else { ApplyAllPoses(); ConfigureDynamics(); sounds.PlayImpact(.3f); }
            return true;
        }

        private void SamplePour(float elapsed)
        {
            for (int index = 0; index < 3; index++)
            {
                var pose = State.GetPose((WarehouseFishPart)index);
                float start = index * .75f;
                float t = Mathf.Clamp01((elapsed - start) / .7f);
                cans[index].SetActive(elapsed < WarehouseFishAssemblyState.PourSeconds);
                cans[index].transform.localPosition = new Vector3(pose.X, .78f + .32f + Mathf.Sin(t * Mathf.PI) * .10f, pose.Z);
                cans[index].transform.localRotation = Quaternion.Euler(Mathf.SmoothStep(0f, 155f, t), 0f, 12f * (index - 1));
                if (!released[index] && elapsed >= start + .43f)
                {
                    released[index] = true;
                    var body = bodies[index];
                    body.gameObject.SetActive(true);
                    body.position = station.TransformPoint(new Vector3(pose.X, .78f + .25f, pose.Z));
                    body.rotation = station.rotation * Quaternion.Euler(0f, pose.Yaw, 0f);
                    body.isKinematic = false; body.useGravity = true;
                    body.linearVelocity = station.TransformDirection(new Vector3(.025f * (index - 1), -.12f, -.025f));
                    body.angularVelocity = Vector3.up * (.12f * (index - 1));
                }
            }
        }

        private void ConfigureDynamics()
        {
            for (int index = 0; index < 3; index++)
            {
                bool held = selected == index || selected >= 0 && State.IsPartLinked((WarehouseFishPart)selected) &&
                    (index == 1 || State.IsPartLinked((WarehouseFishPart)index));
                var body = bodies[index];
                bool kinematic = !active || Phase != WarehouseFishAssemblyPhase.Assembling || held || State.IsPartLinked((WarehouseFishPart)index);
                if (!body.isKinematic && kinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = kinematic; body.useGravity = !kinematic;
            }
        }

        private void FixedUpdate()
        {
            if (!active || Suspended || Phase != WarehouseFishAssemblyPhase.Assembling) return;
            SavePhysics();
            for (int index = 0; index < 3; index++)
            {
                Vector3 local = station.InverseTransformPoint(bodies[index].position);
                if (local.y < .65f || Mathf.Abs(local.x) > 1.1f || Mathf.Abs(local.z) > .6f)
                {
                    var pose = State.GetPose((WarehouseFishPart)index);
                    State.SetPose((WarehouseFishPart)index, new WarehouseFishPartPose(Mathf.Clamp(pose.X, -.72f, .72f),
                        Mathf.Clamp(pose.Z, -.24f, .24f), pose.Yaw));
                    ApplyPose(index);
                    bodies[index].linearVelocity = Vector3.zero; bodies[index].angularVelocity = Vector3.zero;
                }
            }
        }

        private void SavePhysics()
        {
            if (Phase != WarehouseFishAssemblyPhase.Assembling) return;
            for (int index = 0; index < 3; index++)
            {
                if (bodies[index].isKinematic) continue;
                Vector3 local = station.InverseTransformPoint(bodies[index].position);
                if (Mathf.Abs(local.x) > .88f || Mathf.Abs(local.z) > .38f || local.y < .65f) continue;
                float yaw = (Quaternion.Inverse(station.rotation) * bodies[index].rotation).eulerAngles.y;
                State.SetPose((WarehouseFishPart)index, new WarehouseFishPartPose(local.x, local.z, yaw));
            }
        }

        private void ApplyPose(int index)
        {
            var pose = State.GetPose((WarehouseFishPart)index);
            bodies[index].position = station.TransformPoint(new Vector3(pose.X, .784f, pose.Z));
            bodies[index].rotation = station.rotation * Quaternion.Euler(0f, pose.Yaw, 0f);
        }

        private void ApplyAllPoses() { for (int index = 0; index < 3; index++) ApplyPose(index); }
        private void ApplyGroupPose(int part)
        {
            ApplyPose(part);
            if (!State.IsPartLinked((WarehouseFishPart)part)) return;
            for (int index = 0; index < 3; index++)
                if (index != part && (index == 1 || State.IsPartLinked((WarehouseFishPart)index))) ApplyPose(index);
        }

        private void BeginFinale()
        {
            selected = -1; pointerDragging = false;
            ConfigureDynamics();
            for (int index = 0; index < 3; index++) bodies[index].gameObject.SetActive(false);
            fish.SetActive(true);
            sounds.BeginConvulsions(State.FinaleElapsed);
            SampleFinale();
        }

        private void SampleFinale()
        {
            var belly = State.GetPose(WarehouseFishPart.Belly);
            fish.transform.localPosition = new Vector3(belly.X, .784f, belly.Z);
            fish.transform.localRotation = Quaternion.Euler(0f, belly.Yaw, 0f);
            bool dying = State.FinaleElapsed >= WarehouseFishAssemblyState.ConvulsionSeconds;
            WarehouseFishAssemblyAssets.Sample(fish, dying ? "Death" : "Convulse",
                dying ? Mathf.Min(1f, State.FinaleElapsed - WarehouseFishAssemblyState.ConvulsionSeconds) : State.FinaleElapsed);
            if (active) sounds.SetFinaleTime(State.FinaleElapsed);
            if (!dying) { key.SetActive(false); return; }
            float sinceDeath = State.FinaleElapsed - WarehouseFishAssemblyState.ConvulsionSeconds;
            key.SetActive(sinceDeath >= .35f && (!State.RewardGranted || State.ReceiptPending));
            if (active && sinceDeath >= .35f) sounds.PlaySpit();
            float t = Mathf.Clamp01((sinceDeath - .35f) / .48f);
            Vector3 from = mouth.position;
            Vector3 destination = station.TransformPoint(new Vector3(Mathf.Clamp(belly.X + .20f, -.65f, .65f), .794f,
                Mathf.Clamp(belly.Z - .16f, -.27f, .27f)));
            key.transform.position = Vector3.Lerp(from, destination, t) + station.up * (.18f * Mathf.Sin(Mathf.PI * t));
            key.transform.rotation = station.rotation * Quaternion.Euler(0f, 225f * t, 0f);
        }

        internal void RestoreRetainedProgressForDebug()
        {
            sounds.Stop();
            RestorePresentation();
        }

        private void RestorePresentation()
        {
            lastVisualPhase = Phase;
            for (int index = 0; index < 3; index++)
            {
                released[index] = Phase != WarehouseFishAssemblyPhase.Pouring;
                bodies[index].isKinematic = true; bodies[index].useGravity = false;
                bodies[index].gameObject.SetActive(State.HasTransferredCans && !State.IsAssembled);
                cans[index].SetActive(false);
            }
            ApplyAllPoses();
            fish.SetActive(State.IsAssembled); key.SetActive(false);
            if (Phase == WarehouseFishAssemblyPhase.Pouring)
            {
                for (int index = 0; index < 3; index++) { released[index] = false; bodies[index].gameObject.SetActive(false); }
                SamplePour(State.PourElapsed);
            }
            if (State.IsAssembled) SampleFinale();
        }

        public void EndActivity()
        {
            if (!active) return;
            SavePhysics();
            if (Phase == WarehouseFishAssemblyPhase.Pouring) State.Advance(WarehouseFishAssemblyState.PourSeconds);
            active = false; selected = -1; pointerDragging = false;
            ConfigureDynamics(); sounds.Stop();
            RestorePresentation();
        }

        private bool Suspended => PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused ||
            owner == null || !owner.isActiveAndEnabled;

        private void Update()
        {
            if (!initialized) return;
            if (Phase == WarehouseFishAssemblyPhase.WaitingForCans && lastVisualPhase != Phase)
            {
                if (OwnsInteraction && listener != null) NarrativeInteractionController.For(listener).RestoreImmediate();
                RestorePresentation();
            }
            sounds.SetPaused(Suspended);
            if (!active && State.ReceiptPending && Time.unscaledTime >= nextReceiptAttempt && !Suspended &&
                !BarMinigameModalLock.IsAnyLocked && owner != null && owner.IsInitialized &&
                !SceneTransitionService.IsTransitioning)
            {
                nextReceiptAttempt = Time.unscaledTime + .25f;
                TryShowReceipt(owner.Player.Interactor);
            }
        }

        private void TryShowReceipt(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || !State.ReceiptPending || interactor == null || BarMinigameModalLock.IsAnyLocked) return;
            WorldItemFoundScreen screen = WorldItemFoundScreen.For(interactor);
            if (screen == null || !screen.IsInitialized) return;
            key.SetActive(true);
            if (screen.TryPresentReceived(interactor, InventoryItemId.WarehouseGateKey, key.transform,
                "combat.warehouse.fish.received", _ => { if (key != null) key.SetActive(false); }))
                State.MarkReceiptPresented();
        }

        private int PickPart(Vector2 pointer)
        {
            Ray ray = owner.CameraFollow.Camera.ScreenPointToRay(pointer);
            int best = -1; float distance = float.MaxValue;
            for (int index = 0; index < 3; index++)
                foreach (Collider collider in partColliders[index])
                    if (collider.Raycast(ray, out RaycastHit hit, 8f) && hit.distance < distance)
                    { best = index; distance = hit.distance; }
            return best;
        }

        private bool PointerOnTable(Vector2 pointer, out Vector2 point)
        {
            Ray ray = owner.CameraFollow.Camera.ScreenPointToRay(pointer);
            var plane = new Plane(station.up, station.TransformPoint(new Vector3(0f, .784f, 0f)));
            point = default;
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector3 local = station.InverseTransformPoint(ray.GetPoint(distance));
            point = new Vector2(local.x, local.z); return true;
        }

        private void OnGUI()
        {
            if (!active || Suspended || Phase != WarehouseFishAssemblyPhase.Assembling) return;
            controlsStyle ??= RetroUiTheme.CreateLabelStyle(10, TextAnchor.MiddleCenter, RetroUiTheme.Text);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                var panel = new Rect(10f, 322f, 620f, 32f);
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
                GUI.Label(panel, UnityEngine.InputSystem.Gamepad.current != null ? gamepadControls : controls, controlsStyle);
                if (selected < 0) return;
                Bounds bounds = partColliders[selected][0].bounds;
                Vector3 a = owner.CameraFollow.Camera.WorldToScreenPoint(bounds.min);
                Vector3 b = owner.CameraFollow.Camera.WorldToScreenPoint(bounds.max);
                if (a.z <= 0f || b.z <= 0f) return;
                Vector2 p = canvas.ScreenToLogical(new Vector2(a.x, Screen.height - a.y));
                Vector2 q = canvas.ScreenToLogical(new Vector2(b.x, Screen.height - b.y));
                Rect outline = Rect.MinMaxRect(Mathf.Min(p.x, q.x) - 3f, Mathf.Min(p.y, q.y) - 3f,
                    Mathf.Max(p.x, q.x) + 3f, Mathf.Max(p.y, q.y) + 3f);
                Color previousColor = GUI.color; GUI.color = RetroUiTheme.ToGuiColor(RetroUiTheme.AccentPale);
                RetroUiTexture.DrawTexture(new Rect(outline.x, outline.y, outline.width, 1f), Texture2D.whiteTexture);
                RetroUiTexture.DrawTexture(new Rect(outline.x, outline.yMax, outline.width, 1f), Texture2D.whiteTexture);
                RetroUiTexture.DrawTexture(new Rect(outline.x, outline.y, 1f, outline.height), Texture2D.whiteTexture);
                RetroUiTexture.DrawTexture(new Rect(outline.xMax, outline.y, 1f, outline.height), Texture2D.whiteTexture);
                GUI.color = previousColor;
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }

        private void OnDisable()
        {
            if (Interaction != null && Interaction.IsPresenting && listener != null)
                NarrativeInteractionController.For(listener).RestoreImmediate();
            EndActivity();
            RestorePlayer(listener);
        }
    }

    internal sealed class WarehouseFishPartImpact : MonoBehaviour
    {
        private WarehouseFishAssemblyAudio audioOwner;
        public void Initialize(WarehouseFishAssemblyAudio owner) => audioOwner = owner;
        private void OnCollisionEnter(Collision collision)
        {
            if (audioOwner != null && collision.relativeVelocity.sqrMagnitude > .08f)
                audioOwner.PlayImpact(Mathf.Clamp01(collision.relativeVelocity.magnitude * .3f));
        }
    }
}
