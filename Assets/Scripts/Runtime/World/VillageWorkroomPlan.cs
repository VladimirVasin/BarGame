using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One room in the real lower storey of house 08, in its existing plot frame.</summary>
    public sealed class VillageWorkroomPlan
    {
        public const string HouseId = "village-house-08";
        public static readonly Bounds LocalRoomBounds = new Bounds(new Vector3(-1.05f, 1.15f, .13f), new Vector3(4.5f, 2.3f, 4.8f));
        public const float BenchWidth = 1.35f, BenchDepth = .46f, BenchHeight = .44f;
        internal const float TerrainBedDepth = .16f, TerrainCell = .125f, TerrainBedInset = .178f;
        public readonly AlpineVillagePlotDescriptor House;
        public readonly Quaternion Rotation;
        public VillageWorkroomPlan(AlpineVillagePlotDescriptor house)
        {
            House = house ?? throw new ArgumentNullException(nameof(house));
            if (house.StableId != HouseId) throw new ArgumentException("The workroom belongs to house 08.");
            Rotation = Quaternion.LookRotation(house.Facing);
        }
        public Vector3 World(Vector3 local) => House.GroundCenter + Rotation * local;
        public Vector3 Local(Vector3 world) => Quaternion.Inverse(Rotation) * (world - House.GroundCenter);
        public Vector3 Anchor(string name) => World(LocalAnchor(name));
        public Vector3 Facing(string name) => Rotation * LocalFacing(name);
        internal static float LowerTerrainBed(AlpineVillagePlotDescriptor house, Vector2 point, float height)
        {
            if (house.StableId != HouseId) return height;
            Vector2 delta = point - new Vector2(house.GroundCenter.x, house.GroundCenter.z);
            var forward = new Vector2(house.Facing.x, house.Facing.z);
            var right = new Vector2(forward.y, -forward.x);
            float inside = Mathf.Min(house.FootprintSize.x * .475f - TerrainBedInset - Mathf.Abs(Vector2.Dot(delta, right)),
                house.FootprintSize.y * .445f - TerrainBedInset - Mathf.Abs(Vector2.Dot(delta, forward)));
            if (inside <= 0f) return height;
            // The floor and every actor dock stay at zero. Soil belongs below
            // the boards. A full cell diagonal is reserved inside the real
            // plinth: the interpolated return stays under its solid foundation,
            // and cannot make a ditch beyond the facade.
            return Mathf.Min(height, house.GroundCenter.y - TerrainBedDepth);
        }
        public bool Contains(Vector3 world, float padding = 0f)
        {
            Vector3 local = Local(world);
            Bounds room = LocalRoomBounds;
            return local.x >= room.min.x - padding && local.x <= room.max.x + padding &&
                local.z >= room.min.z - padding && local.z <= room.max.z + padding &&
                local.y >= -.10f - padding && local.y <= room.max.y + padding;
        }
        public static Vector3 LocalFacing(string name) => name == "RepairDock" ? Vector3.back :
            name == "PlayerHelpDock" ? Vector3.left : name == "BenchSeat" || name == "BenchApproach" ? Vector3.right : Vector3.forward;
        public bool ContainsInterior(Vector3 world)
        {
            Vector3 p = Local(world);
            if (p.y < -.10f || p.y > 2.30f) return false;
            return Contains(world) || (p.x >= 1.2f && p.x <= 3.50f && p.z >= -2.27f && p.z <= 1.40f) ||
                (Mathf.Abs(p.x - House.DoorAcrossOffset) <= .52f && p.z >= 2.53f && p.z <= 2.79f);
        }
        public Vector3 LocalAnchor(string name)
        {
            if (!localAnchors.TryGetValue(name, out Vector3 value)) throw new ArgumentException("Unknown workroom anchor " + name);
            return value;
        }
        public static IReadOnlyDictionary<string, Vector3> LocalAnchors => localAnchors;
        public IEnumerable<Bounds> SolidWallFootprints()
        {
            float x = House.FootprintSize.x * .475f, z = House.FootprintSize.y * .445f;
            yield return Rect(-x, -3.30f, -z, z);
            yield return Rect(-3.30f, x, -z, -2.27f);
            yield return Rect(3.50f, x, -2.27f, z);
            yield return Rect(-3.30f, House.DoorAcrossOffset - .52f, 2.53f, z);
            yield return Rect(House.DoorAcrossOffset + .52f, 1.20f, 2.53f, z);
            yield return Rect(1.20f, 3.50f, 1.40f, z);
            yield return Rect(1.20f, 1.36f, -2.27f, -2.13f);
            yield return Rect(1.20f, 1.36f, -1.17f, 1.40f);
            yield return Rect(1.73f, 1.87f, -1.10f, 1.40f);
        }
        private static Bounds Rect(float x0, float x1, float z0, float z1) =>
            new Bounds(new Vector3((x0 + x1) * .5f, 1.15f, (z0 + z1) * .5f), new Vector3(x1 - x0, 2.30f, z1 - z0));
        private static readonly Dictionary<string, Vector3> localAnchors = new Dictionary<string, Vector3>(StringComparer.Ordinal)
        {
            { "RoomEntry", new Vector3(-.004f, 0f, 1.60f) },
            { "RepairDock", new Vector3(-2.02f, 0f, -.97f) },
            { "RepairJoin", new Vector3(-1.86f, .90f, -1.45f) },
            { "RepairLeftSupport", new Vector3(-1.75f, .90f, -1.45f) },
            { "HammerRest", new Vector3(-2.20f, .90f, -1.37f) },
            { "RepairRailRest", new Vector3(-1.86f, .90f, -1.45f) },
            { "RepairRailFixed", new Vector3(-1.875f, .90f, -1.45f) },
            { "SewingDock", new Vector3(-2.10f, 0f, .98f) },
            { "SewingSeat", new Vector3(-2.10f, .42f, .78f) },
            { "ClothRest", new Vector3(-2.10f, .674f, 1.36f) },
            { "MittenRest", new Vector3(-2.02f, .690f, 1.36f) },
            { "SewingBox", new Vector3(-1.73f, .666f, 1.33f) },
            { "BoxStow", new Vector3(-1.73f, .706f, 1.42f) },
            { "BoxRest", new Vector3(-1.55f, .46f, 1.10f) },
            { "PlayerHelpDock", new Vector3(-.62f, 0f, -1.70f) },
            { "ChairPartLeftGrip", new Vector3(-1.04f, 1.00f, -1.92f) },
            { "ChairPartRightGrip", new Vector3(-1.04f, 1.00f, -1.48f) },
            { "BenchSeat", new Vector3(-2.99f, .44f, -.10f) },
            { "BenchApproach", new Vector3(-2.24f, 0f, -.10f) },
            { "BasketStand", new Vector3(.77f, 0f, .45f) },
            { "BasketRest", new Vector3(.77f, .38f, .45f) },
            { "BasketDock", new Vector3(.12f, 0f, .45f) },
            { "FrontWindow", new Vector3(-2.10f, 1.50f, 2.674f) },
            { "LeftWindow", new Vector3(-3.70f, 1.50f, -1.70f) },
            { "RoomLamp", new Vector3(-1.05f, 2.10f, .13f) },
            { "Floor", Vector3.zero },
            { "AmbientLight", new Vector3(-1.05f, 2.10f, .13f) },
            { "RepairLamp", new Vector3(-2.70f, 1.78f, -2.10f) },
            { "SewingLamp", new Vector3(-2.65f, 1.20f, 1.85f) },
            { "Interior", new Vector3(-.25f, 0f, -1.65f) },
            { "Turn0", new Vector3(2.26f, 0f, -1.65f) },
            { "Turn1", new Vector3(3.22f, 0f, -1.65f) },
            { "Hidden0", new Vector3(2.26f, 0f, .85f) },
            { "Hidden1", new Vector3(3.22f, 0f, .85f) }
        };
    }
}
