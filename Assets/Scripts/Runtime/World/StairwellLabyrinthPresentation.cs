using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    internal sealed class StairwellLabyrinthManifest
    {
        public string contract_signature;
        public Part[] parts;
        public Door[] doors;
        public Motion[] motions;
        public PuzzleNode[] puzzle_nodes;
        public Gate[] gates;
        [Serializable] internal sealed class PuzzleNode
        {
            public string id, kind;
            public int stage;
            public string[] parts;
            public float step_angle;
            public float[] pivot, axis, travel, dock, facing, exit_dock, exit_facing, contact,
                bounds_center, bounds_size;
        }
        [Serializable] internal sealed class Gate
        {
            public string id;
            public int stage;
            public string[] parts;
            public float[] pivot, travel, collider_center, collider_size;
        }
        [Serializable] internal sealed class Part
        { public string name, role; public int stage; public float[] bounds_min, bounds_max; }
        [Serializable] internal sealed class Door
        {
            public string id, branch_id;
            public int stage;
            public string[] parts;
            public float yaw;
            public float[] hinge, outer_handle, inner_handle, outside_dock, inside_dock,
                leaf_center, leaf_size, room_center, interior_point, passage_point;
        }
        [Serializable] internal sealed class Motion
        {
            public string id, kind;
            public int stage, light_index = -1;
            public string[] parts;
            public float[] pivot, axis;
            public float amplitude, period, phase;
        }
        internal static Vector3 Vector(float[] value)
        {
            if (value == null || value.Length != 3) throw new InvalidOperationException("Missing metric labyrinth vector.");
            var result = new Vector3(value[0], value[1], value[2]);
            if (!float.IsFinite(result.x) || !float.IsFinite(result.y) || !float.IsFinite(result.z))
                throw new InvalidOperationException("Invalid metric labyrinth vector.");
            return result;
        }
    }

    public readonly struct StairwellLabyrinthRoom
    {
        internal StairwellLabyrinthRoom(StairwellLabyrinthManifest.Door door)
        {
            Stage = door.stage; BranchId = door.branch_id;
            OutsideDock = StairwellLabyrinthManifest.Vector(door.outside_dock);
            InsideDock = StairwellLabyrinthManifest.Vector(door.inside_dock);
            InteriorPoint = StairwellLabyrinthManifest.Vector(door.interior_point);
            PassagePoint = StairwellLabyrinthManifest.Vector(door.passage_point);
        }
        public int Stage { get; }
        public string BranchId { get; }
        public Vector3 OutsideDock { get; }
        public Vector3 InsideDock { get; }
        public Vector3 InteriorPoint { get; }
        public Vector3 PassagePoint { get; }
    }

    /// <summary>Three real local doors and a finite set of authored, non-traversable mechanisms.</summary>
    [DisallowMultipleComponent]
    public sealed class StairwellLabyrinthPresentation : MonoBehaviour
    {
        public const int MaximumMotionRoots = 8;
        private sealed class Mechanism
        {
            internal StairwellLabyrinthManifest.Motion Plan;
            internal Transform Root;
            internal Vector3 Rest, Axis;
            internal Light Light;
            internal Vector3 LightRest;
            internal Quaternion LightRotation;
            internal long LastHalfCycle;
        }
        private StairwellLabyrinthManifest manifest;
        private Transform[] hinges, outerHandles, innerHandles;
        private BoxCollider[] leaves;
        private Mechanism[] mechanisms;
        private Transform hero;
        private StairwellSoundscape soundscape;
        private Material abyssMaterial;
        private float nextAudioUpdate;
        public IReadOnlyList<InteriorHingedDoorInteraction> Doors { get; private set; } = Array.Empty<InteriorHingedDoorInteraction>();
        public IReadOnlyList<StairwellLabyrinthRoom> Rooms { get; private set; } = Array.Empty<StairwellLabyrinthRoom>();
        public IReadOnlyList<Transform> MotionRoots { get; private set; } = Array.Empty<Transform>();
        public double AnimationTime { get; private set; }

        internal void Prepare(StairwellLabyrinthManifest data, Dictionary<string, MeshRenderer> renderers, Material abyss)
        {
            if (manifest != null || data.doors == null || data.doors.Length != 3 ||
                data.motions == null || data.motions.Length > MaximumMotionRoots)
                throw new InvalidOperationException("Labyrinth requires three rooms and bounded authored mechanisms.");
            manifest = data; abyssMaterial = abyss;
            hinges = new Transform[3]; outerHandles = new Transform[3]; innerHandles = new Transform[3];
            leaves = new BoxCollider[3]; var rooms = new StairwellLabyrinthRoom[3];
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 3; i++)
            {
                var door = data.doors[i];
                if (door.stage != i + 1) throw new InvalidOperationException("One puzzle room is required per stage.");
                rooms[i] = new StairwellLabyrinthRoom(door);
                Transform hinge = Pivot(door.id, StairwellLabyrinthManifest.Vector(door.hinge), Quaternion.Euler(0f, door.yaw, 0f));
                hinges[i] = hinge;
                MoveParts(hinge, door.parts, renderers, claimed);
                leaves[i] = hinge.gameObject.AddComponent<BoxCollider>();
                leaves[i].center = StairwellLabyrinthManifest.Vector(door.leaf_center);
                leaves[i].size = StairwellLabyrinthManifest.Vector(door.leaf_size);
                outerHandles[i] = Contact(hinge, "Outside handle", door.outer_handle);
                innerHandles[i] = Contact(hinge, "Inside handle", door.inner_handle);
            }
            Rooms = Array.AsReadOnly(rooms);
            mechanisms = new Mechanism[data.motions.Length]; var roots = new Transform[mechanisms.Length];
            for (int i = 0; i < mechanisms.Length; i++)
            {
                var motion = data.motions[i];
                if (motion.period < 1f || !float.IsFinite(motion.period) || !float.IsFinite(motion.amplitude) ||
                    !float.IsFinite(motion.phase) || (motion.kind != "swing" && motion.kind != "fan" && motion.kind != "lift" && motion.kind != "flap"))
                    throw new InvalidOperationException("Invalid labyrinth mechanism: " + motion.id);
                Vector3 rest = StairwellLabyrinthManifest.Vector(motion.pivot);
                Vector3 axis = StairwellLabyrinthManifest.Vector(motion.axis).normalized;
                if (axis.sqrMagnitude < .99f) throw new InvalidOperationException("Mechanism requires an axis.");
                roots[i] = Pivot(motion.id, rest, Quaternion.identity);
                MoveParts(roots[i], motion.parts, renderers, claimed);
                mechanisms[i] = new Mechanism { Plan = motion, Root = roots[i], Rest = rest, Axis = axis };
            }
            MotionRoots = Array.AsReadOnly(roots);
        }

        public void Initialize(PlayerRuntime player, PlayerAnimatedInteractionController controller,
            StairwellInteriorAtmosphere atmosphere, StairwellSoundscape audio)
        {
            if (manifest == null || hero != null) throw new InvalidOperationException("Prepare labyrinth presentation once before initialization.");
            hero = player.GameObject.transform; soundscape = audio;
            var doors = new InteriorHingedDoorInteraction[3];
            for (int i = 0; i < doors.Length; i++)
            {
                doors[i] = hinges[i].gameObject.AddComponent<InteriorHingedDoorInteraction>();
                doors[i].Initialize(player, controller, hinges[i], leaves[i], outerHandles[i], innerHandles[i],
                    transform.TransformPoint(Rooms[i].OutsideDock), transform.TransformPoint(Rooms[i].InsideDock));
            }
            Doors = Array.AsReadOnly(doors);
            Transform fan = null, lamp = null, lift = null, flap = null;
            foreach (Mechanism mechanism in mechanisms)
            {
                int index = mechanism.Plan.light_index;
                if (index >= 0)
                {
                    if (index >= atmosphere.LabyrinthLights.Length) throw new InvalidOperationException("Mechanism light index outside authored fixtures.");
                    mechanism.Light = atmosphere.LabyrinthLights[index];
                    mechanism.LightRest = transform.InverseTransformPoint(mechanism.Light.transform.position);
                    mechanism.LightRotation = Quaternion.Inverse(transform.rotation) * mechanism.Light.transform.rotation;
                }
                switch (mechanism.Plan.kind)
                {
                    case "fan": fan = mechanism.Root; break;
                    case "swing": if (mechanism.Plan.light_index >= 0) lamp = mechanism.Root; break;
                    case "lift": lift = mechanism.Root; break;
                    case "flap": flap = mechanism.Root; break;
                }
                Apply(mechanism);
                mechanism.LastHalfCycle = HalfCycle(mechanism);
            }
            soundscape.ConfigureLabyrinthMechanisms(fan, lamp, lift, flap);
        }

        private void Update()
        {
            if (hero == null || GameTimeScaleRuntime.IsPaused) return;
            AnimationTime += Time.deltaTime;
            foreach (Mechanism mechanism in mechanisms)
            {
                // Time continues while presentation is distant. Returning samples the present phase;
                // it never replays missed sounds or rebuilds meshes/collision.
                bool near = (hero.position - transform.TransformPoint(mechanism.Rest)).sqrMagnitude < 65f * 65f;
                long half = HalfCycle(mechanism);
                if (near)
                {
                    Apply(mechanism);
                    if (half != mechanism.LastHalfCycle && mechanism.Plan.kind == "lift")
                        soundscape.TryPlayMechanismCue(mechanism.Root.position, StairwellSoundscapeCueKind.MetalStress);
                    if (half != mechanism.LastHalfCycle && mechanism.Plan.kind == "flap")
                        soundscape.TryPlayMechanismCue(mechanism.Root.position, StairwellSoundscapeCueKind.DistantMovement);
                }
                mechanism.LastHalfCycle = half;
            }
            if (Time.unscaledTime >= nextAudioUpdate)
            {
                nextAudioUpdate = Time.unscaledTime + .1f;
                soundscape.UpdateMechanismAnchors();
            }
        }

        private long HalfCycle(Mechanism mechanism) => (long)Math.Floor((AnimationTime / mechanism.Plan.period + mechanism.Plan.phase) * 2d);

        private void Apply(Mechanism mechanism)
        {
            var plan = mechanism.Plan;
            double cycle = AnimationTime / plan.period + plan.phase;
            float wave = (float)Math.Sin(cycle * Math.PI * 2d);
            Vector3 position = mechanism.Rest;
            Quaternion rotation = Quaternion.identity;
            if (plan.kind == "lift") position += mechanism.Axis * (plan.amplitude * wave);
            else rotation = Quaternion.AngleAxis(plan.kind == "fan" ? (float)(cycle % 1d) * 360f : plan.amplitude * wave, mechanism.Axis);
            mechanism.Root.SetLocalPositionAndRotation(position, rotation);
            if (mechanism.Light != null)
                mechanism.Light.transform.SetPositionAndRotation(
                    transform.TransformPoint(position + rotation * (mechanism.LightRest - mechanism.Rest)),
                    transform.rotation * rotation * mechanism.LightRotation);
        }

        private Transform Pivot(string label, Vector3 position, Quaternion rotation)
        {
            var pivot = new GameObject(label).transform;
            pivot.SetParent(transform, false); pivot.SetLocalPositionAndRotation(position, rotation);
            return pivot;
        }

        private Transform Contact(Transform hinge, string label, float[] localPosition)
        {
            var contact = new GameObject(label).transform;
            contact.SetParent(hinge, false);
            contact.position = transform.TransformPoint(StairwellLabyrinthManifest.Vector(localPosition));
            return contact;
        }

        private static void MoveParts(Transform pivot, string[] names, Dictionary<string, MeshRenderer> renderers, HashSet<string> claimed)
        {
            if (names == null || names.Length == 0) throw new InvalidOperationException("Moving assembly has no authored parts.");
            foreach (string name in names)
            {
                if (!claimed.Add(name) || !renderers.TryGetValue(name, out MeshRenderer renderer))
                    throw new InvalidOperationException("Missing or duplicate movable labyrinth part: " + name);
                renderer.transform.SetParent(pivot, true); // Retain imported FBX unit compensation.
            }
        }

        private void OnEnable()
        {
            if (hero == null || mechanisms == null) return;
            Transform fan = null, lamp = null, lift = null, flap = null;
            foreach (Mechanism mechanism in mechanisms) Apply(mechanism);
            foreach (Mechanism mechanism in mechanisms)
            {
                switch (mechanism.Plan.kind)
                {
                    case "fan": fan = mechanism.Root; break;
                    case "swing": if (mechanism.Plan.light_index >= 0) lamp = mechanism.Root; break;
                    case "lift": lift = mechanism.Root; break;
                    case "flap": flap = mechanism.Root; break;
                }
            }
            if (soundscape != null) soundscape.ConfigureLabyrinthMechanisms(fan, lamp, lift, flap);
            foreach (var door in Doors) door.enabled = true;
        }

        private void OnDisable()
        {
            if (soundscape != null) soundscape.ReleaseLabyrinthMechanisms();
            foreach (var door in Doors) if (door != null) door.enabled = false;
            if (mechanisms == null) return;
            foreach (Mechanism mechanism in mechanisms)
            {
                if (mechanism.Root != null) mechanism.Root.SetLocalPositionAndRotation(mechanism.Rest, Quaternion.identity);
                if (mechanism.Light != null)
                    mechanism.Light.transform.SetPositionAndRotation(transform.TransformPoint(mechanism.LightRest), transform.rotation * mechanism.LightRotation);
            }
        }

        private void OnDestroy()
        {
            if (abyssMaterial != null) Destroy(abyssMaterial);
        }
    }
}
