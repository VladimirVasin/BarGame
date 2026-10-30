using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored physical puzzle assemblies; the retained rules own all three passage locks.</summary>
    [DisallowMultipleComponent]
    public sealed class StairwellLabyrinthPuzzles : MonoBehaviour
    {
        private sealed class Node
        {
            internal StairwellLabyrinthManifest.PuzzleNode Plan;
            internal Transform Root, Lever;
            internal Vector3 Rest, Axis, Travel, LeverAxis;
            internal Quaternion LeverRest;
            internal Renderer[] LeverRenderers;
        }
        private sealed class Gate
        {
            internal int Stage;
            internal Transform Root;
            internal Vector3 Rest, Travel;
        }
        private Node[] nodes;
        private Gate[] gates;
        private StairwellLabyrinthManifest manifest;
        private Material weaveMaterial;
        private Material clothMaterial, threadMaterial;
        private StairwellLabyrinthPuzzleInteraction active;
        private StairwellInteriorAtmosphere atmosphere;
        private StairwellSoundscape soundscape;
        private Transform cabinetLeaf;
        private Quaternion cabinetRest;
        private Vector3 cabinetAxis;
        private Collider pickupTrigger;
        private Collider crowbarTrigger;
        public const string FlashlightSourceId = "stairwell.labyrinth.first_puzzle.flashlight";
        public const string CrowbarSourceId = "stairwell.labyrinth.second_puzzle.crowbar";
        public StairwellLabyrinthPuzzleState State { get; private set; }
        public StairwellLabyrinthPuzzleModeController PuzzleMode { get; private set; }
        public WorldItemPickup FlashlightPickup { get; private set; }
        public WorldItemPickup CrowbarPickup { get; private set; }
        private Renderer[] crowbarControlRenderers;
        private readonly Bounds[] viewBounds = new Bounds[3];
        private readonly bool[] hasViewBounds = new bool[3];
        private readonly List<Bounds>[] viewParts = new List<Bounds>[3];
        private readonly Pose[] viewPoses = new Pose[3];
        private readonly float[] viewAspects = new float[3];
        public Transform CabinetLeaf => cabinetLeaf;
        public bool IsCabinetOpen => State != null && State.CanPassGate(1);
        public IReadOnlyList<StairwellLabyrinthPuzzleInteraction> Controls { get; private set; } = Array.Empty<StairwellLabyrinthPuzzleInteraction>();
        public IReadOnlyList<Transform> PuzzleRoots { get; private set; } = Array.Empty<Transform>();
        public IReadOnlyList<Transform> GateRoots { get; private set; } = Array.Empty<Transform>();

        internal void Prepare(StairwellLabyrinthManifest data, Dictionary<string, MeshRenderer> renderers)
        {
            if (manifest != null || data.puzzle_nodes == null || data.puzzle_nodes.Length > 16 || data.gates == null || data.gates.Length != 3)
                throw new InvalidOperationException("Labyrinth requires three authored puzzle gates and their metric controls.");
            manifest = data;
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            nodes = new Node[data.puzzle_nodes.Length];
            var roots = new Transform[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var plan = data.puzzle_nodes[i];
                Vector3 rest = StairwellLabyrinthManifest.Vector(plan.pivot);
                Vector3 axis = StairwellLabyrinthManifest.Vector(plan.axis);
                if (plan.stage < 1 || plan.stage > 3 || !float.IsFinite(plan.step_angle) || axis.sqrMagnitude < .9f)
                    throw new InvalidOperationException("Invalid authored puzzle node " + plan.id);
                roots[i] = CreatePivot(plan.id, rest);
                MoveParts(roots[i], plan.parts, renderers, claimed);
                nodes[i] = new Node { Plan = plan, Root = roots[i], Rest = rest, Axis = axis.normalized,
                    Travel = StairwellLabyrinthManifest.Vector(plan.travel) };
                Node node = nodes[i];
                if (!TryAction(plan.id, out _)) continue;
                Vector3 leverPivot = StairwellLabyrinthManifest.Vector(plan.lever_pivot);
                node.LeverAxis = StairwellLabyrinthManifest.Vector(plan.lever_axis);
                node.Lever = CreatePivot("Handle " + plan.id, leverPivot);
                if (plan.lever_follows_node) node.Lever.SetParent(node.Root, true);
                MoveParts(node.Lever, plan.lever_parts, renderers, claimed);
                node.LeverRest = node.Lever.localRotation;
                node.LeverRenderers = new Renderer[plan.lever_parts.Length];
                for (int part = 0; part < plan.lever_parts.Length; part++)
                    node.LeverRenderers[part] = renderers[plan.lever_parts[part]];
            }
            PuzzleRoots = Array.AsReadOnly(roots);
            gates = new Gate[3];
            var gateRoots = new Transform[3];
            for (int i = 0; i < gates.Length; i++)
            {
                var plan = data.gates[i];
                if (plan.stage != i + 1) throw new InvalidOperationException("One sequential gate is required per labyrinth stage.");
                Vector3 rest = StairwellLabyrinthManifest.Vector(plan.pivot);
                gateRoots[i] = CreatePivot(plan.id, rest);
                MoveParts(gateRoots[i], plan.parts, renderers, claimed);
                var leaf = gateRoots[i].gameObject.AddComponent<BoxCollider>();
                leaf.center = StairwellLabyrinthManifest.Vector(plan.collider_center);
                leaf.size = StairwellLabyrinthManifest.Vector(plan.collider_size);
                gates[i] = new Gate { Stage = plan.stage, Root = gateRoots[i], Rest = rest,
                    Travel = StairwellLabyrinthManifest.Vector(plan.travel) };
            }
            GateRoots = Array.AsReadOnly(gateRoots);
            PrepareCabinet(data.reward_cabinet, renderers, claimed);
            Shader clothShader = Resources.Load<Shader>("Shaders/Lit");
            if (clothShader == null) throw new InvalidOperationException("Missing project Lit shader for labyrinth cloth.");
            foreach (var part in data.parts)
            {
                if (part.role != "Weave" && part.role != "Cloth" && part.role != "Thread") continue;
                MeshRenderer renderer = renderers[part.name];
                renderer.SetPropertyBlock(null);
                if (part.role == "Cloth")
                {
                    if (clothMaterial == null) clothMaterial = ClothMaterial(clothShader, "Shared Labyrinth Cloth", new Color(.72f, .68f, .55f));
                    renderer.sharedMaterial = clothMaterial;
                }
                else if (part.role == "Thread")
                {
                    if (threadMaterial == null) threadMaterial = ClothMaterial(clothShader, "Shared Labyrinth Red Thread", new Color(.37f, .028f, .018f));
                    renderer.sharedMaterial = threadMaterial;
                }
                else
                {
                    if (weaveMaterial == null)
                    {
                        Texture2D texture = Resources.Load<Texture2D>(HomeWallPainting.TexturePath);
                        if (texture == null) throw new InvalidOperationException("The labyrinth cloth requires the existing Home painting.");
                        weaveMaterial = ClothMaterial(clothShader, "Shared Labyrinth Woven Painting", Color.white);
                        weaveMaterial.SetTexture("_BaseMap", texture);
                        weaveMaterial.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
                    }
                    renderer.sharedMaterial = weaveMaterial;
                }
            }
            Apply(GameSessionState.StairwellLabyrinthPuzzles.Current);
        }

        public void Initialize(PlayerRuntime player, PlayerAnimatedInteractionController controller,
            StairwellInteriorAtmosphere lighting, StairwellSoundscape audio)
        {
            if (manifest == null || State != null) throw new InvalidOperationException("Prepare puzzle assemblies before initializing them once.");
            State = GameSessionState.StairwellLabyrinthPuzzles;
            atmosphere = lighting; soundscape = audio;
            var controls = new List<StairwellLabyrinthPuzzleInteraction>(12);
            foreach (Node node in nodes)
            {
                if (!TryAction(node.Plan.id, out StairwellLabyrinthPuzzleAction action)) continue;
                var control = new GameObject("Operate " + node.Plan.id);
                control.transform.SetParent(node.Lever, false);
                control.transform.position = transform.TransformPoint(StairwellLabyrinthManifest.Vector(node.Plan.contact));
                var trigger = control.AddComponent<SphereCollider>();
                trigger.isTrigger = true; trigger.radius = .20f;
                var interaction = control.AddComponent<StairwellLabyrinthPuzzleInteraction>();
                interaction.Initialize(this, node.Plan, action, player, controller);
                controls.Add(interaction);
            }
            if (controls.Count != 11) throw new InvalidOperationException("Labyrinth puzzles require four, three and four distinct controls.");
            Controls = controls.AsReadOnly();
            PuzzleMode = gameObject.AddComponent<StairwellLabyrinthPuzzleModeController>();
            PuzzleMode.Initialize(this, player, controller);
            CreateFlashlightPickup();
            CreateCrowbarPickup();
            PrepareViewBounds();
            Apply(State.Current);
        }

        /// <summary>The complete mechanism and hand controls, in a stable front-facing inspection frame.</summary>
        public Quaternion GetStageViewRotation(int stage)
        {
            if (stage < 1 || stage > 3) throw new ArgumentOutOfRangeException(nameof(stage));
            return transform.rotation * Quaternion.Euler(0f, manifest.doors[stage - 1].yaw, 0f);
        }

        public Bounds GetStageViewBounds(int stage)
        {
            Quaternion rotation = GetStageViewRotation(stage);
            Bounds source = viewBounds[stage - 1], result = default;
            Vector3 origin = transform.position;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = origin + rotation * Corner(source, corner);
                if (corner == 0) result = new Bounds(point, Vector3.zero); else result.Encapsulate(point);
            }
            return result;
        }

        public Pose GetStageViewPose(int stage, float aspect)
        {
            Quaternion rotation = GetStageViewRotation(stage);
            int index = stage - 1;
            aspect = Mathf.Max(.5f, aspect);
            if (Mathf.Approximately(viewAspects[index], aspect)) return viewPoses[index];
            Bounds bounds = viewBounds[stage - 1];
            float tangent = Mathf.Tan(StairwellLabyrinthPuzzleModeController.FieldOfView * .5f * Mathf.Deg2Rad);
            // Reserve the bottom quarter for the existing action panel. The camera remains
            // perpendicular to the mechanism; aspect changes distance, never roll or yaw.
            // Fit each real swept part: a union AABB invents nearest-depth corners on
            // the much wider rear screen and can put the camera through the room door.
            float distance = .20f;
            foreach (Bounds part in viewParts[index])
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = Corner(part, corner) - bounds.center;
                    distance = Mathf.Max(distance, Mathf.Abs(point.x) / (tangent * aspect * .88f) - point.z);
                    distance = Mathf.Max(distance, (point.y / tangent - .88f * point.z) / .68f);
                    distance = Mathf.Max(distance, (-point.y / tangent - .48f * point.z) / .68f);
                    distance = Mathf.Max(distance, .20f - point.z);
                }
            Vector3 position = bounds.center - Vector3.forward * distance - Vector3.up * (.20f * distance * tangent);
            viewAspects[index] = aspect;
            return viewPoses[index] = new Pose(transform.position + rotation * position, rotation);
        }

        private void PrepareViewBounds()
        {
            var parts = new Dictionary<string, StairwellLabyrinthManifest.Part>(manifest.parts.Length, StringComparer.Ordinal);
            for (int stage = 0; stage < 3; stage++) viewParts[stage] = new List<Bounds>();
            foreach (var part in manifest.parts) parts.Add(part.name, part);
            foreach (var part in manifest.parts)
            {
                // The first cloth's frame and the third puzzle's real receiving screen
                // are fixed geometry, but remain part of what the player must read.
                if (!part.name.Contains("__PuzzleFrame__") ||
                    !(part.stage == 1 && part.role == "Wood" || part.stage == 3 && part.role == "Cloth")) continue;
                Quaternion intoView = Quaternion.Inverse(Quaternion.Euler(0f, manifest.doors[part.stage - 1].yaw, 0f));
                Bounds bounds = new Bounds();
                bounds.SetMinMax(StairwellLabyrinthManifest.Vector(part.bounds_min), StairwellLabyrinthManifest.Vector(part.bounds_max));
                Bounds projected = default;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = intoView * Corner(bounds, corner);
                    if (corner == 0) projected = new Bounds(point, Vector3.zero); else projected.Encapsulate(point);
                }
                IncludeViewPart(part.stage, projected);
            }
            foreach (var node in manifest.puzzle_nodes)
            {
                int stage = node.stage;
                Quaternion intoView = Quaternion.Inverse(Quaternion.Euler(0f, manifest.doors[stage - 1].yaw, 0f));
                float angle = node.kind == "roll" || node.kind == "bird" ? 360f : node.step_angle;
                IncludeViewParts(parts, node.parts, stage, intoView, StairwellLabyrinthManifest.Vector(node.pivot),
                    StairwellLabyrinthManifest.Vector(node.axis), angle, StairwellLabyrinthManifest.Vector(node.travel));
                if (node.lever_parts == null || node.lever_parts.Length == 0) continue;
                IncludeViewParts(parts, node.lever_parts, stage, intoView, StairwellLabyrinthManifest.Vector(node.lever_pivot),
                    StairwellLabyrinthManifest.Vector(node.lever_axis), node.lever_angle,
                    node.lever_follows_node ? StairwellLabyrinthManifest.Vector(node.travel) : Vector3.zero);
            }
            if (CrowbarPickup != null)
            {
                var plan = manifest.reward_crowbar;
                Vector3 mounted = StairwellLabyrinthManifest.Vector(plan.item_position);
                Quaternion mountRotation = Quaternion.Euler(StairwellLabyrinthManifest.Vector(plan.item_euler));
                Vector3 braced = StairwellLabyrinthManifest.Vector(plan.braced_position);
                Quaternion braceRotation = Quaternion.Euler(StairwellLabyrinthManifest.Vector(plan.braced_euler));
                Quaternion intoView = Quaternion.Inverse(Quaternion.Euler(0f, manifest.doors[1].yaw, 0f));
                foreach (var renderer in crowbarControlRenderers)
                {
                    Bounds projected = default;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = transform.InverseTransformPoint(Corner(renderer.bounds, corner));
                        if (corner == 0) projected = new Bounds(intoView * point, Vector3.zero); else projected.Encapsulate(intoView * point);
                        Vector3 offset = Quaternion.Inverse(mountRotation) * (point - mounted);
                        projected.Encapsulate(intoView * (braced + braceRotation * offset));
                    }
                    IncludeViewPart(2, projected);
                }
            }
            for (int stage = 0; stage < 3; stage++)
            {
                if (!hasViewBounds[stage]) throw new InvalidOperationException("A puzzle requires its complete inspection bounds.");
            }
        }

        private void IncludeViewParts(Dictionary<string, StairwellLabyrinthManifest.Part> parts, string[] names,
            int stage, Quaternion intoView, Vector3 pivot, Vector3 axis, float angle, Vector3 travel)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(angle) / 5f));
            foreach (string name in names)
            {
                var part = parts[name];
                Vector3 low = StairwellLabyrinthManifest.Vector(part.bounds_min), high = StairwellLabyrinthManifest.Vector(part.bounds_max);
                Bounds bounds = new Bounds((low + high) * .5f, high - low);
                Bounds projected = default;
                bool initialized = false;
                for (int sample = 0; sample <= samples; sample++)
                {
                    float fraction = sample / (float)samples;
                    Quaternion rotation = Quaternion.AngleAxis(angle * fraction, axis);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = pivot + rotation * (Corner(bounds, corner) - pivot);
                        Vector3 local = intoView * point;
                        if (!initialized) { projected = new Bounds(local, Vector3.zero); initialized = true; }
                        else projected.Encapsulate(local);
                        projected.Encapsulate(intoView * (point + travel));
                    }
                }
                IncludeViewPart(stage, projected);
            }
        }

        private void IncludeViewPart(int stage, Bounds bounds)
        {
            // At most five-degree sampling plus a two-centimetre edge allowance.
            bounds.Expand(.04f);
            viewParts[stage - 1].Add(bounds);
            IncludeViewPoint(stage, bounds.min);
            IncludeViewPoint(stage, bounds.max);
        }

        private void IncludeViewPoint(int stage, Vector3 point)
        {
            int index = stage - 1;
            if (!hasViewBounds[index]) { viewBounds[index] = new Bounds(point, Vector3.zero); hasViewBounds[index] = true; }
            else viewBounds[index].Encapsulate(point);
        }

        private static Vector3 Corner(Bounds bounds, int corner) => bounds.center + Vector3.Scale(bounds.extents,
            new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));

        public IReadOnlyList<Renderer> GetControlRenderers(StairwellLabyrinthPuzzleInteraction control)
        {
            if (control != null && control.Action == StairwellLabyrinthPuzzleAction.ShutterStop && crowbarControlRenderers != null)
                return crowbarControlRenderers;
            if (control != null)
                foreach (Node node in nodes)
                    if (node.Plan.id == control.Id) return node.LeverRenderers;
            return Array.Empty<Renderer>();
        }

        private void PrepareCabinet(StairwellLabyrinthManifest.RewardCabinet plan,
            Dictionary<string, MeshRenderer> renderers, HashSet<string> claimed)
        {
            if (plan == null || !float.IsFinite(plan.open_angle) || plan.open_angle < 80f || plan.open_angle > 150f)
                throw new InvalidOperationException("The first labyrinth puzzle requires its measured reward cabinet.");
            cabinetAxis = StairwellLabyrinthManifest.Vector(plan.axis).normalized;
            if (cabinetAxis.sqrMagnitude < .9f) throw new InvalidOperationException("The cabinet needs a rotation axis.");
            cabinetLeaf = CreatePivot("Flashlight Cabinet Leaf", StairwellLabyrinthManifest.Vector(plan.pivot));
            cabinetRest = cabinetLeaf.localRotation;
            MoveParts(cabinetLeaf, plan.parts, renderers, claimed);
            var collider = cabinetLeaf.gameObject.AddComponent<BoxCollider>();
            collider.center = StairwellLabyrinthManifest.Vector(plan.collider_center);
            collider.size = StairwellLabyrinthManifest.Vector(plan.collider_size);
        }

        private void CreateFlashlightPickup()
        {
            var plan = manifest.reward_cabinet;
            FlashlightPickup = WorldItemPickup.Create(transform, new WorldItemPickupPlan(
                FlashlightSourceId, InventoryItemId.ChestFlashlight, "interaction.take_flashlight",
                StairwellLabyrinthManifest.Vector(plan.item_position), new Vector3(.05f, .1055f, .0845f),
                "interaction.flashlight_taken"));
            if (FlashlightPickup == null) return;
            pickupTrigger = FlashlightPickup.GetComponent<Collider>();
            FlashlightPickup.Model.localRotation = Quaternion.Euler(StairwellLabyrinthManifest.Vector(plan.item_euler));
            // The trigger sits at the reachable shelf edge; the authored model stays on its support.
            Vector3 itemPosition = FlashlightPickup.transform.position;
            FlashlightPickup.transform.position = transform.TransformPoint(StairwellLabyrinthManifest.Vector(plan.interaction_position));
            FlashlightPickup.Model.position += itemPosition - FlashlightPickup.transform.position;
        }

        private void CreateCrowbarPickup()
        {
            var plan = manifest.reward_crowbar;
            if (plan == null) throw new InvalidOperationException("The delivery puzzle requires its measured crowbar supports.");
            CrowbarPickup = WorldItemPickup.Create(transform, new WorldItemPickupPlan(
                CrowbarSourceId, InventoryItemId.CombatCrowbar, "interaction.labyrinth_take_crowbar",
                StairwellLabyrinthManifest.Vector(plan.interaction_position), new Vector3(.06f, .724f, .19f)));
            if (CrowbarPickup == null) return;
            crowbarTrigger = CrowbarPickup.GetComponent<Collider>();
            crowbarControlRenderers = CrowbarPickup.Model.GetComponentsInChildren<Renderer>(true);
            // The actual weapon model keeps its authored metre scale; this is also the pickup model.
            SetCrowbar(0f);
        }

        private void SetCrowbar(float braced)
        {
            if (CrowbarPickup == null || CrowbarPickup.IsPresenting) return;
            var plan = manifest.reward_crowbar;
            CrowbarPickup.Model.SetPositionAndRotation(transform.TransformPoint(Vector3.Lerp(
                StairwellLabyrinthManifest.Vector(plan.item_position), StairwellLabyrinthManifest.Vector(plan.braced_position), braced)),
                transform.rotation * Quaternion.Slerp(Quaternion.Euler(StairwellLabyrinthManifest.Vector(plan.item_euler)),
                    Quaternion.Euler(StairwellLabyrinthManifest.Vector(plan.braced_euler)), braced));
        }

        public bool CanPassGate(int stage) => State != null && State.CanPassGate(stage);
        internal bool CanOperate(StairwellLabyrinthPuzzleInteraction control) => isActiveAndEnabled && active == null &&
            State != null && State.CanOperate(control.Action) && atmosphere != null && atmosphere.IsLabyrinthPositionLit(control.InteractionPosition);
        internal bool TryAcquire(StairwellLabyrinthPuzzleInteraction control)
        {
            if (!CanOperate(control)) return false;
            active = control;
            return true;
        }
        internal void Release(StairwellLabyrinthPuzzleInteraction control)
        {
            if (active != control) return;
            active = null;
            Apply(State.Current);
        }
        internal void PlayContactCue(Vector3 position, bool blocked) => soundscape?.TryPlayMechanismCue(position,
            blocked ? StairwellSoundscapeCueKind.MetalStress : StairwellSoundscapeCueKind.DistantMovement, true);
        internal void PlayLatchCue(Vector3 position) => soundscape?.TryPlayMechanismCue(position,
            StairwellSoundscapeCueKind.PipeKnock, true);

        internal Vector3 HandContact(StairwellLabyrinthPuzzleInteraction control)
        {
            if (control.Action == StairwellLabyrinthPuzzleAction.ShutterStop && CrowbarPickup != null)
                return CrowbarPickup.Model.TransformPoint(Vector3.up * .46f);
            return control.InteractionPosition;
        }

        internal void Preview(StairwellLabyrinthPuzzleState.Snapshot before, StairwellLabyrinthPuzzleState.Snapshot after,
            string actionId, float fraction, bool blocked)
        {
            fraction = Mathf.Clamp01(fraction);
            foreach (Node node in nodes)
            {
                float from = Value(node.Plan.id, before), to = Value(node.Plan.id, after);
                // Trying the bare handle demonstrates the weight: lift, then return to its stop.
                if (actionId == "shutter" && !blocked && node.Plan.id == "shutter")
                {
                    SetNode(node, Mathf.Sin(fraction * Mathf.PI));
                    continue;
                }
                if (actionId == "shutter" && !blocked && node.Plan.id == "transfer-weight")
                {
                    SetNode(node, Mathf.Sin(fraction * Mathf.PI) * .5f);
                    continue;
                }
                // Rotations are authored as forward detents: 2→0 completes the same third revolution.
                if (node.Plan.id == actionId && !blocked && (actionId.StartsWith("roll-", StringComparison.Ordinal) || actionId.StartsWith("bird-", StringComparison.Ordinal)) && to < from)
                    to += 3f;
                float nodeFraction = actionId == "carriage-send" && node.Plan.id == "delivery-catch"
                    ? Mathf.InverseLerp(.82f, 1f, fraction) : fraction;
                float value = Mathf.LerpUnclamped(from, to, nodeFraction);
                if (blocked && (node.Plan.id == actionId || actionId == "carriage-send" && node.Plan.id == "carriage"))
                    value += Mathf.Sin(fraction * Mathf.PI) * .08f;
                SetNode(node, value);
                if (node.Lever != null && !node.Plan.lever_follows_node)
                {
                    float stroke = node.Plan.id == actionId ? Mathf.Sin(fraction * Mathf.PI) * (blocked ? .15f : 1f) : 0f;
                    node.Lever.localRotation = node.LeverRest * Quaternion.AngleAxis(node.Plan.lever_angle * stroke,
                        node.LeverAxis);
                }
            }
            foreach (Gate gate in gates)
            {
                float from = before.CanPassGate(gate.Stage) ? 1f : 0f, to = after.CanPassGate(gate.Stage) ? 1f : 0f;
                float gateFraction = actionId == "carriage-send" ? Mathf.InverseLerp(.82f, 1f, fraction) : fraction;
                gate.Root.localPosition = gate.Rest + gate.Travel * Mathf.Lerp(from, to, gateFraction);
            }
            SetCabinet(Mathf.Lerp(before.CanPassGate(1) ? 1f : 0f, after.CanPassGate(1) ? 1f : 0f, fraction));
            SetCrowbar(Mathf.Lerp(CrowbarBrace(before), CrowbarBrace(after), fraction));
        }

        private void Apply(StairwellLabyrinthPuzzleState.Snapshot value)
        {
            foreach (Node node in nodes)
            {
                SetNode(node, Value(node.Plan.id, value));
                if (node.Lever != null) node.Lever.localRotation = node.LeverRest;
            }
            foreach (Gate gate in gates) gate.Root.localPosition = gate.Rest + gate.Travel * (value.CanPassGate(gate.Stage) ? 1f : 0f);
            SetCabinet(value.CanPassGate(1) ? 1f : 0f);
            if (FlashlightPickup != null)
            {
                FlashlightPickup.enabled = value.CanPassGate(1);
                pickupTrigger.enabled = value.CanPassGate(1);
            }
            SetCrowbar(CrowbarBrace(value));
            if (CrowbarPickup != null)
            {
                CrowbarPickup.enabled = value.CanPassGate(2);
                crowbarTrigger.enabled = value.CanPassGate(2);
            }
        }
        private static float CrowbarBrace(StairwellLabyrinthPuzzleState.Snapshot state) =>
            state.StopEngaged ? 1f : 0f;
        private void SetCabinet(float fraction) => cabinetLeaf.localRotation = cabinetRest *
            Quaternion.AngleAxis(manifest.reward_cabinet.open_angle * Mathf.Clamp01(fraction), cabinetAxis);
        private static void SetNode(Node node, float value) => node.Root.SetLocalPositionAndRotation(
            node.Rest + node.Travel * value, Quaternion.AngleAxis(node.Plan.step_angle * value, node.Axis));
        private static Material ClothMaterial(Shader shader, string label, Color color)
        {
            var material = new Material(shader) { name = label, hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .04f);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            return material;
        }
        private static float Value(string id, StairwellLabyrinthPuzzleState.Snapshot state)
        {
            switch (id)
            {
                case "roll-0": return state.Roll0;
                case "roll-1": return state.Roll1;
                case "roll-2": return state.Roll2;
                case "cloth-bolt": return state.CanPassGate(1) ? 1f : 0f;
                case "shutter": return state.ShutterRaised ? 1f : 0f;
                case "shutter-stop": return state.StopEngaged ? 1f : 0f;
                case "weight-switch": return state.WeightOnCarriage ? 1f : 0f;
                case "carriage": case "carriage-send": return state.CanPassGate(2) ? 1f : 0f;
                case "transfer-weight": return state.CanPassGate(2) ? 1f : state.ShutterRaised ? .5f : 0f;
                case "delivery-catch": return state.CanPassGate(2) ? 1f : 0f;
                case "bird-0": return state.Bird0;
                case "bird-1": return state.Bird1;
                case "bird-2": return state.Bird2;
                case "bird-lift": return state.CanPassGate(3) ? 1f : 0f;
                default: throw new InvalidOperationException("Unbound labyrinth mechanism " + id);
            }
        }
        private static bool TryAction(string id, out StairwellLabyrinthPuzzleAction action)
        {
            switch (id)
            {
                case "roll-0": action = StairwellLabyrinthPuzzleAction.Roll0; return true;
                case "roll-1": action = StairwellLabyrinthPuzzleAction.Roll1; return true;
                case "roll-2": action = StairwellLabyrinthPuzzleAction.Roll2; return true;
                case "cloth-bolt": action = StairwellLabyrinthPuzzleAction.ClothBolt; return true;
                case "shutter": action = StairwellLabyrinthPuzzleAction.Shutter; return true;
                case "shutter-stop": action = StairwellLabyrinthPuzzleAction.ShutterStop; return true;
                case "weight-switch": action = StairwellLabyrinthPuzzleAction.WeightSwitch; return true;
                case "carriage-send": action = StairwellLabyrinthPuzzleAction.CarriageSend; return true;
                case "bird-0": action = StairwellLabyrinthPuzzleAction.Bird0; return true;
                case "bird-1": action = StairwellLabyrinthPuzzleAction.Bird1; return true;
                case "bird-2": action = StairwellLabyrinthPuzzleAction.Bird2; return true;
                case "bird-lift": action = StairwellLabyrinthPuzzleAction.BirdLift; return true;
                default: action = default; return false;
            }
        }
        private Transform CreatePivot(string label, Vector3 position)
        {
            var root = new GameObject(label).transform;
            root.SetParent(transform, false); root.localPosition = position;
            return root;
        }
        private static void MoveParts(Transform root, string[] names, Dictionary<string, MeshRenderer> renderers, HashSet<string> claimed)
        {
            if (names == null || names.Length == 0) throw new InvalidOperationException("Puzzle assembly has no authored geometry.");
            foreach (string name in names)
            {
                if (!claimed.Add(name) || !renderers.TryGetValue(name, out MeshRenderer renderer))
                    throw new InvalidOperationException("Missing or duplicate labyrinth puzzle part " + name);
                renderer.transform.SetParent(root, true);
            }
        }
        private void OnDisable()
        {
            if (PuzzleMode != null) PuzzleMode.Cancel();
            if (active != null) active.Cancel();
        }
        private void OnDestroy()
        {
            if (weaveMaterial != null) Destroy(weaveMaterial);
            if (clothMaterial != null) Destroy(clothMaterial);
            if (threadMaterial != null) Destroy(threadMaterial);
        }
    }
}
