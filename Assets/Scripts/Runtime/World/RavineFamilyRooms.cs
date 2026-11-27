using System;
using UnityEngine;

namespace BarPromenade
{
    // Docks, standing points and furniture rectangles come from the Blender scene in metres.
    [Serializable]
    internal sealed class RavineFamilyRooms
    {
        private const string Folder = "Pawnshop/Finds/";
        [Serializable] internal sealed class Dock
        {
            public string item;
            public Vector3 position, approach, size;
        }
        [Serializable] private sealed class Furniture
        {
            public int floor;
            public float x, z, width, depth;
        }
        public Dock[] docks;
        [SerializeField] private Furniture[] occupied;
        public Vector3 pillow;

        internal static RavineFamilyRooms Load()
        {
            var asset = Resources.Load<TextAsset>(Folder + "RavineFamilyLayout");
            var layout = asset ? JsonUtility.FromJson<RavineFamilyRooms>(asset.text) : null;
            if (layout?.docks?.Length != 9 || layout.occupied == null)
                throw new InvalidOperationException("Missing surveyed family apartments.");
            return layout;
        }

        internal bool Occupied(Vector2 local, int floor, float radius)
        {
            foreach (var piece in occupied)
                if (piece.floor == floor && Mathf.Abs(piece.x-local.x) < piece.width*.5f+radius &&
                    Mathf.Abs(piece.z-local.y) < piece.depth*.5f+radius) return true;
            return false;
        }

        internal void Place(Transform parent, Vector3 origin, bool interactive, CityHillsideQuarterBridge bridge)
        {
            var rooms = new GameObject("Three ravine households").transform;
            rooms.SetParent(parent, false);
            rooms.localPosition = origin;
            var template = Resources.Load<GameObject>(Folder + "RavineFamilyRooms");
            if (!template) throw new InvalidOperationException("Missing apartment furniture.");
            var furniture = UnityEngine.Object.Instantiate(template, rooms, false);
            if (interactive)
                foreach (var part in furniture.GetComponentsInChildren<MeshFilter>())
                    part.gameObject.AddComponent<MeshCollider>().sharedMesh = part.sharedMesh;

            foreach (var dock in docks)
            {
                var id = (InventoryItemId)Enum.Parse(typeof(InventoryItemId), dock.item);
                var plan = new WorldItemPickupPlan("city.ravine." + dock.item, id, "ravine.inspect." + dock.item,
                    dock.position, dock.size, "ravine.item.taken");
                if (!interactive)
                {
                    if (!GameSessionState.IsWorldItemCollected(plan.SourceId))
                        InventoryItemModelFactory.BuildWorldModel(id,rooms,dock.size).localPosition = dock.position;
                    continue;
                }
                var pickup = WorldItemPickup.Create(rooms, plan, requireClearSight: true);
                if (pickup && id == InventoryItemId.ClampMirror)
                {
                    // The child could see across the deck to the opposite railing over the deep stone sill.
                    var rail = Vector3.Lerp(bridge.DeckCenterWorld,bridge.EastBearingWorld,.55f);
                    rail.z = bridge.DeckBounds.yMax-.09f;
                    rail.y = bridge.Link.PavementTopAt(rail.x)+.82f;
                    pickup.gameObject.AddComponent<RavineBridgeMirror>().Initialize(pickup, rooms.TransformPoint(pillow), rail);
                }
            }
        }
    }
}
