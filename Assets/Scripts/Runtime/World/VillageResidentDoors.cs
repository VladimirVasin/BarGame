using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured original default-seed houses, now carrying real authored entrances.</summary>
    public static class VillageResidentDoorPlan
    {
        public static bool IsResidentHouse(string id) => id == "village-house-04" ||
            id == "village-house-08" || id == "village-house-11";

        public static bool TryGetHouseDimensions(int index, out Vector2 size, out float height, out float across)
        {
            switch (index)
            {
                case 4: size = new Vector2(7.23383856f, 6.99728680f); height = 6.36644077f; across = -.0923051834f; return true;
                case 8: size = new Vector2(7.91718388f, 6.21844482f); height = 5.69638920f; across = -.00399827957f; return true;
                case 11: size = new Vector2(6.61841917f, 7.48174620f); height = 6.35850143f; across = -.0230127573f; return true;
                default: size = default; height = across = 0f; return false;
            }
        }
    }

    [Serializable] public sealed class VillageResidentDoorPart
    {
        public string mesh, role, surface;
        public float[] tint, bounds_min, bounds_max;
        public int triangles;
    }
    [Serializable] public sealed class VillageResidentDoorDefinition
    {
        public string plot_id;
        public float width, depth, height, door_across, wall_face;
        public VillageResidentDoorPart[] parts;
        public VillageLifePropAnchor[] anchors;
    }
    [Serializable] public sealed class VillageResidentDoorManifest
    {
        public string generator_version, design_id, build_signature, scale_mode;
        public int mesh_count;
        public VillageResidentDoorDefinition[] houses;
        public VillageResidentDoorPart[] door_parts;
    }

    public static class VillageResidentDoorAssets
    {
        public const string ResourcePath = "VillageLife/VillageResidentDoors3D";
        public const string DesignId = "village_resident_doorways_v1";
        public const string GeneratorVersion = "1.0.0";
        private static VillageResidentDoorManifest manifest;
        private static Dictionary<string, MeshFilter> meshes;
        public static VillageResidentDoorManifest Manifest { get { Load(); return manifest; } }

        private static void Load()
        {
            if (manifest != null && meshes != null) return;
            var text = Resources.Load<TextAsset>(ResourcePath);
            var model = Resources.Load<GameObject>(ResourcePath);
            if (text == null || model == null) throw new InvalidOperationException("Missing authored village resident doorways.");
            var data = JsonUtility.FromJson<VillageResidentDoorManifest>(text.text);
            if (data == null || data.design_id != DesignId || data.generator_version != GeneratorVersion ||
                data.scale_mode != "fixed_metres" || data.houses == null || data.houses.Length != 3 ||
                data.door_parts == null || data.door_parts.Length != 3)
                throw new InvalidOperationException("Invalid village resident doorway manifest.");
            var found = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh == null || !found.TryAdd(mesh.sharedMesh.name, mesh))
                    throw new InvalidOperationException("Duplicate or missing authored village doorway mesh.");
            if (found.Count != data.mesh_count) throw new InvalidOperationException("Village doorway mesh count drifted.");
            manifest = data; meshes = found;
        }

        public static VillageResidentDoorDefinition GetDefinition(string id)
        {
            Load();
            foreach (var house in manifest.houses) if (house.plot_id == id) return house;
            throw new ArgumentException("No resident doorway for " + id, nameof(id));
        }

        public static bool TryGetShellPart(string id, VillageMeshRole role, out MeshFilter filter)
        {
            filter = null;
            if (!VillageResidentDoorPlan.IsResidentHouse(id)) return false;
            if (id == VillageWorkroomPlan.HouseId && VillageWorkroomAssets.TryGetShellPart(role, out filter)) return true;
            foreach (var part in GetDefinition(id).parts)
                if (part.role == role.ToString()) { filter = meshes[part.mesh]; return true; }
            return false;
        }

        public static Transform CreatePart(VillageResidentDoorPart part, Transform parent, bool solid)
        {
            Load();
            MeshFilter template = meshes[part.mesh];
            var host = new GameObject(part.mesh);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = template.transform.position;
            host.transform.localRotation = template.transform.rotation;
            host.transform.localScale = template.transform.lossyScale;
            host.AddComponent<MeshFilter>().sharedMesh = template.sharedMesh;
            var renderer = host.AddComponent<MeshRenderer>();
            var color = new Color(part.tint[0], part.tint[1], part.tint[2], part.tint[3]);
            if (!Enum.TryParse(part.surface, out MountainRoadSurfaceKind surface))
                throw new InvalidOperationException("Unknown doorway surface " + part.surface);
            VillageFacadeAppearance.Apply(renderer, surface, color, verticalTimber: true);
            if (solid) host.AddComponent<MeshCollider>().sharedMesh = template.sharedMesh;
            return host.transform;
        }
    }

    /// <summary>A private, physically openable entrance; resident ownership never disables its body.</summary>
    [DisallowMultipleComponent]
    public sealed class VillageResidentDoor : MonoBehaviour
    {
        public const float SwingSeconds = .65f;
        public const float MaximumAngle = 90f;
        private readonly Vector3[] hidden = new Vector3[2];
        private float targetFraction;
        private Quaternion closedRotation;
        private AlpineVillagePlotDescriptor plot;
        private readonly Vector3[] turns = new Vector3[2];
        private readonly Collider[] playerOverlap = new Collider[64];
        public string PlotId => plot.StableId;
        public Transform HouseRoot { get; private set; }
        public Transform Hinge { get; private set; }
        public Transform Leaf { get; private set; }
        public Transform Handle { get; private set; }
        public Transform Occupant { get; private set; }
        public Vector3 ExteriorDock => plot.DoorDockPosition;
        public Vector3 ThresholdDock { get; private set; }
        public Vector3 InteriorDock { get; private set; }
        public IReadOnlyList<Vector3> HiddenDocks => hidden;
        public float OpenFraction { get; private set; }
        public bool IsOpen => OpenFraction >= .999f;
        public bool IsClosed => OpenFraction <= .001f;
        public bool IsBusy => Occupant != null;

        public static VillageResidentDoor Create(Transform houseRoot, AlpineVillagePlotDescriptor descriptor)
        {
            var definition = VillageResidentDoorAssets.GetDefinition(descriptor.StableId);
            if (Mathf.Abs(definition.width - descriptor.FootprintSize.x) > .0001f ||
                Mathf.Abs(definition.depth - descriptor.FootprintSize.y) > .0001f ||
                Mathf.Abs(definition.height - descriptor.Height) > .0001f ||
                Mathf.Abs(definition.door_across - descriptor.DoorAcrossOffset) > .0001f)
                throw new InvalidOperationException("Resident doorway is not fitted to its authored house: " + descriptor.StableId);
            var root = new GameObject("Resident Doorway"); root.transform.SetParent(houseRoot, false);
            var door = root.AddComponent<VillageResidentDoor>(); door.plot = descriptor; door.HouseRoot = houseRoot;
            Vector3 Local(string name)
            {
                if (descriptor.StableId == VillageWorkroomPlan.HouseId &&
                    VillageWorkroomPlan.LocalAnchors.TryGetValue(name, out Vector3 roomAnchor)) return roomAnchor;
                foreach (var anchor in definition.anchors)
                    if (anchor.name == name) return VillageLifePropLibrary.Vector(anchor.position);
                throw new InvalidOperationException("Missing resident doorway anchor " + name);
            }
            door.ThresholdDock = houseRoot.TransformPoint(Local("Threshold"));
            door.InteriorDock = houseRoot.TransformPoint(Local("Interior"));
            door.turns[0] = houseRoot.TransformPoint(Local("Turn0"));
            door.turns[1] = houseRoot.TransformPoint(Local("Turn1"));
            door.hidden[0] = houseRoot.TransformPoint(Local("Hidden0"));
            door.hidden[1] = houseRoot.TransformPoint(Local("Hidden1"));
            foreach (var part in definition.parts)
                if (part.role == "Lining" && descriptor.StableId != VillageWorkroomPlan.HouseId)
                    VillageResidentDoorAssets.CreatePart(part, root.transform, true);
            var portal = new GameObject("Portal Frame"); portal.transform.SetParent(root.transform, false);
            portal.transform.localPosition = new Vector3(definition.door_across, 0f, definition.wall_face);
            var hinge = new GameObject("Door Hinge"); hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = Local("Hinge");
            door.Hinge = hinge.transform; door.closedRotation = Quaternion.identity;
            foreach (var part in VillageResidentDoorAssets.Manifest.door_parts)
            {
                Transform created = VillageResidentDoorAssets.CreatePart(part,
                    part.role == "Frame" ? portal.transform : hinge.transform, part.role != "Handle");
                if (part.role == "Leaf") door.Leaf = created;
            }
            var grip = new GameObject("Door Handle Contact"); grip.transform.SetParent(hinge.transform, false);
            grip.transform.localPosition = new Vector3(.76f, 1.01f, .075f);
            door.Handle = grip.transform;
            return door;
        }

        public bool TryReserve(Transform actor)
        {
            if (actor == null || (Occupant != null && Occupant != actor)) return false;
            Occupant = actor; return true;
        }
        public void Release(Transform actor) { if (Occupant == actor) Occupant = null; }
        public bool Open(Transform actor) { if (!TryReserve(actor)) return false; targetFraction = 1f; return true; }
        public bool Close(Transform actor) { if (Occupant != actor) return false; targetFraction = 0f; return true; }
        public void SetOpenFraction(Transform actor, float fraction)
        {
            if (Occupant != actor || actor == null || GameTimeScaleRuntime.IsPaused || float.IsNaN(fraction)) return;
            fraction = Mathf.Clamp01(fraction);
            bool playerOwnedWorkroom = PlotId == "village-house-08" && actor.GetComponent<PlayerMotor>() != null;
            if (playerOwnedWorkroom ? PlayerIntersectsLeafSweep(OpenFraction, fraction) :
                fraction < OpenFraction && PlayerOccupiesDoorway()) return;
            OpenFraction = targetFraction = fraction;
            Hinge.localRotation = closedRotation * Quaternion.Euler(0f, MaximumAngle * fraction, 0f);
        }
        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning) return;
            float target = targetFraction;
            SetOpenFraction(Occupant, Mathf.MoveTowards(OpenFraction, target, deltaSeconds / SwingSeconds));
            targetFraction = target;
        }
        public Vector3[] GetInteriorRoute(int slot)
        {
            if (slot < 0 || slot >= hidden.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            return new[] { ExteriorDock, ThresholdDock, InteriorDock, turns[slot], hidden[slot] };
        }
        public Vector3 GetOperatingDock(float fraction, bool inside)
        {
            float radians = Mathf.Clamp01(fraction) * MaximumAngle * Mathf.Deg2Rad;
            float handleX = -.49f + .76f * Mathf.Cos(radians) + .075f * Mathf.Sin(radians);
            float handleZ = .05f - .76f * Mathf.Sin(radians) + .075f * Mathf.Cos(radians);
            Vector3 local = new Vector3(plot.DoorAcrossOffset + (inside ? Mathf.Clamp(handleX, -.15f, .15f) : 0f),
                0f, plot.FootprintSize.y * .43f + handleZ + (inside ? -.55f : .55f));
            return HouseRoot.TransformPoint(local);
        }
        public Vector3 GetOperatingFacing(bool inside) => inside ? plot.Facing : -plot.Facing;
        public bool IsConcealed(Vector3 position)
        {
            Vector3 local = HouseRoot.InverseTransformPoint(position);
            if (plot.StableId == VillageWorkroomPlan.HouseId)
                return local.x >= 2f && local.x <= 3.50f && local.z >= -1.10f && local.z <= 1.40f && local.y < 2.30f;
            float across = local.x - plot.DoorAcrossOffset;
            float depth = plot.FootprintSize.y * .43f - local.z;
            return across >= 1.1f && across <= 2.35f && depth >= 1.65f && depth <= 2.95f && local.y < 2.2f;
        }
        public bool PlayerOccupiesDoorway()
        {
            // A guest may close their own room door. NPCs retain the whole-house
            // guard below and can never shut that same guest into the building.
            if (PlotId == "village-house-08" && Occupant != null && Occupant.GetComponent<PlayerMotor>() != null)
                return PlayerIntersectsLeafSweep(OpenFraction, OpenFraction);
            // Detect a guest anywhere behind the facade, including both concealed pockets.
            // Test the motor origin so a capsule touching the exterior wall cannot hold the door.
            Vector3 houseHalfSize = new Vector3(plot.FootprintSize.x * .475f,
                plot.Height * .5f + .25f, plot.FootprintSize.y * .445f);
            int count = Physics.OverlapBoxNonAlloc(HouseRoot.TransformPoint(Vector3.up * (plot.Height * .5f)),
                houseHalfSize, playerOverlap, HouseRoot.rotation, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                PlayerMotor motor = playerOverlap[i].GetComponentInParent<PlayerMotor>();
                if (motor == null) continue;
                Vector3 local = HouseRoot.InverseTransformPoint(motor.transform.position);
                if (Mathf.Abs(local.x) <= houseHalfSize.x && Mathf.Abs(local.z) <= houseHalfSize.z &&
                    local.y >= -.25f && local.y <= plot.Height + .25f) return true;
            }

            // The narrow exterior check protects the threshold and inward leaf sweep as well.
            Vector3 center = ThresholdDock + Vector3.up * .95f - plot.Facing * .35f;
            count = Physics.OverlapBoxNonAlloc(center, new Vector3(.68f, .95f, .8f), playerOverlap,
                HouseRoot.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (playerOverlap[i].GetComponentInParent<PlayerMotor>() != null) return true;
            return false;
        }

        private bool PlayerIntersectsLeafSweep(float from, float to)
        {
            CharacterController body = Occupant != null ? Occupant.GetComponent<CharacterController>() : null;
            MeshCollider leafCollider = Leaf != null ? Leaf.GetComponent<MeshCollider>() : null;
            if (body == null || !body.enabled || leafCollider == null || leafCollider.sharedMesh == null) return true;
            Vector3 scale = body.transform.lossyScale;
            float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 centre = body.transform.TransformPoint(body.center);
            Vector3 upper = centre + body.transform.up * halfSegment;
            Vector3 lower = centre - body.transform.up * halfSegment;
            // The authored leaf is one closed rectangular solid. Its collider's
            // own mesh bounds include the tiny bevel, never renderer extents.
            Bounds leafBounds = leafCollider.sharedMesh.bounds;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) * MaximumAngle / 2f));
            for (int step = 0; step <= steps; step++)
            {
                float fraction = Mathf.Lerp(from, to, step / (float)steps);
                Quaternion rotation = Hinge.parent.rotation * closedRotation * Quaternion.Euler(0f, MaximumAngle * fraction, 0f);
                Matrix4x4 leafWorld = Matrix4x4.TRS(Hinge.position, rotation, Hinge.lossyScale) *
                    Matrix4x4.TRS(Leaf.localPosition, Leaf.localRotation, Leaf.localScale);
                Matrix4x4 inverse = leafWorld.inverse;
                Vector3 a = inverse.MultiplyPoint3x4(lower), b = inverse.MultiplyPoint3x4(upper);
                Vector3 leafScale = Leaf.lossyScale;
                float minimumScale = Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(leafScale.x), Mathf.Abs(leafScale.y), Mathf.Abs(leafScale.z)));
                // Half the angular sample's arc plus numerical clearance makes
                // this continuous sweep conservative between sampled positions.
                float arcMargin = Mathf.Abs(to - from) * MaximumAngle * Mathf.Deg2Rad / steps * .5f * 1.0f;
                float clearance = (radius + .003f + arcMargin) / minimumScale;
                if (SegmentBoundsDistanceSquared(a, b, leafBounds) < clearance * clearance) return true;
            }
            return false;
        }

        private static float SegmentBoundsDistanceSquared(Vector3 a, Vector3 b, Bounds bounds)
        {
            // Squared distance to a convex box is convex along the capsule's
            // centre segment, including end caps and non-zero collider centre.
            float lo = 0f, hi = 1f;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float first = (2f * lo + hi) / 3f, second = (lo + 2f * hi) / 3f;
                float d1 = bounds.SqrDistance(Vector3.Lerp(a, b, first));
                float d2 = bounds.SqrDistance(Vector3.Lerp(a, b, second));
                if (d1 <= d2) hi = second; else lo = first;
            }
            return Mathf.Min(bounds.SqrDistance(a), bounds.SqrDistance(b),
                bounds.SqrDistance(Vector3.Lerp(a, b, (lo + hi) * .5f)));
        }
    }
}
