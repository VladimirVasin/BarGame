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
        public const string FlashlightSourceId = "stairwell.labyrinth.first_puzzle.flashlight";
        public StairwellLabyrinthPuzzleState State { get; private set; }
        public StairwellLabyrinthPuzzleModeController PuzzleMode { get; private set; }
        public WorldItemPickup FlashlightPickup { get; private set; }
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
            if (controls.Count != 12) throw new InvalidOperationException("Three labyrinth puzzles require exactly twelve distinct controls.");
            Controls = controls.AsReadOnly();
            PuzzleMode = gameObject.AddComponent<StairwellLabyrinthPuzzleModeController>();
            PuzzleMode.Initialize(this, player, controller);
            CreateFlashlightPickup();
            Apply(State.Current);
        }

        public IReadOnlyList<Renderer> GetControlRenderers(StairwellLabyrinthPuzzleInteraction control)
        {
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
            blocked ? StairwellSoundscapeCueKind.MetalStress : StairwellSoundscapeCueKind.DistantMovement);

        internal void Preview(StairwellLabyrinthPuzzleState.Snapshot before, StairwellLabyrinthPuzzleState.Snapshot after,
            string actionId, float fraction, bool blocked)
        {
            fraction = Mathf.Clamp01(fraction);
            foreach (Node node in nodes)
            {
                float from = Value(node.Plan.id, before), to = Value(node.Plan.id, after);
                // Rotations are authored as forward detents: 2→0 completes the same third revolution.
                if (node.Plan.id == actionId && !blocked && (actionId.StartsWith("roll-", StringComparison.Ordinal) || actionId.StartsWith("bird-", StringComparison.Ordinal)) && to < from)
                    to += 3f;
                float value = Mathf.LerpUnclamped(from, to, fraction);
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
                gate.Root.localPosition = gate.Rest + gate.Travel * Mathf.Lerp(from, to, fraction);
            }
            SetCabinet(Mathf.Lerp(before.CanPassGate(1) ? 1f : 0f, after.CanPassGate(1) ? 1f : 0f, fraction));
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
        }
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
