using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityResidentialFrontages
    {
        private static readonly string[] Windows = { "WindowKitchen", "WindowPlants", "WindowSitting", "WindowStorage" };
        private static readonly string[] Decks = { "DeckKitchen", "DeckPlants", "DeckSitting", "DeckStorage" };

        internal static void Build(CityBuildingAssetRegistry registry, BuildingLot lot, int seed,
            CityBuildingPrototypePose cityPose, CityLayout layout)
        {
            if (!ResidentialAddress.Applies(lot)) return;
            var address = new ResidentialAddress(lot, seed);
            var lines = layout != null ? CityBalconyWashPlan.ForLayout(layout) : null;
            ulong placed = 0;
            foreach (var balcony in registry.BalconySlots)
            {
                if (balcony.Floor <= 1) continue;
                var pane = NeighbourPane(registry, balcony.DoorSlotId);
                if (pane == null || (address.Family == 1 && IsRepairedStack(address, pane))) continue;
                uint household = address.Household(pane);
                // The plant household keeps flowers more often. Presence, flower
                // variety and position use separate bits of the apartment hash.
                int chanceInTen = household % 4 == 1 ? 5 : 2;
                if ((household >> 8) % 10 >= chanceInTen) continue;
                var anchor = FlowerBoxAnchor(balcony, household);
                if (ClearsLaundry(cityPose.TransformPoint(anchor), lines))
                    placed |= 1UL << balcony.DoorSlotId;
            }
            registry.RailFlowerBoxMask = placed;
            BuildVisible(registry, address);
        }

        private static CityBuildingWindowSlot NeighbourPane(CityBuildingAssetRegistry registry, int doorId)
        {
            foreach (var slot in registry.WindowSlots)
                if (slot.SlotId == doorId + 1) return slot;
            return null;
        }

        private static bool IsRepairedStack(ResidentialAddress address, CityBuildingWindowSlot pane) =>
            (pane.Bay / 2 & 1) == (int)(address.Key & 1);

        internal static Vector3 FlowerBoxAnchor(CityBuildingBalconySlot balcony, uint household)
        {
            var deck = balcony.LocalDeckBounds;
            var normal = balcony.LocalOutward;
            var right = Vector3.Cross(Vector3.up, normal);
            // The real top rail is centred 4 cm behind the edge and tops out
            // 99 cm above the slab. The hooks are authored around its top face.
            return new Vector3(deck.center.x, deck.max.y + .99f, deck.center.z) +
                normal * (deck.extents.z - .04f) + right * ((household & (1u << 16)) == 0 ? -.38f : .38f);
        }

        private static bool ClearsLaundry(Vector3 anchor, IReadOnlyList<CityBalconyWashLine> lines)
        {
            if (lines == null) return true;
            foreach (var line in lines)
            {
                // Envelope of the hanging sheet, rope sag and wind travel,
                // expanded by the size of the box on either side of its hooks.
                if (anchor.y < Mathf.Min(line.Start.y, line.End.y) - line.Sag - 1.25f ||
                    anchor.y > Mathf.Max(line.Start.y, line.End.y) + .45f) continue;
                Vector2 span = new Vector2(line.End.x - line.Start.x, line.End.z - line.Start.z);
                Vector2 delta = new Vector2(anchor.x - line.Start.x, anchor.z - line.Start.z);
                float t = Mathf.Clamp01(Vector2.Dot(delta, span) / Mathf.Max(.0001f, span.sqrMagnitude));
                float reach = .60f + .64f + line.ClothTravel;
                if ((delta - t * span).sqrMagnitude <= reach * reach) return false;
            }
            return true;
        }

        private static void BuildVisible(CityBuildingAssetRegistry registry, ResidentialAddress address)
        {
            ulong covered = registry.GroundedWindowMask;
            var kit = new ResidentialFrontage(registry.transform);
            var slots = registry.WindowSlots;
            foreach (var balcony in registry.BalconySlots)
            {
                // Leave the lowest decks free for the basket and balcony actors.
                int door = balcony.DoorSlotId;
                if ((covered & (3UL << door)) != 0) continue;
                var pane = NeighbourPane(registry, door);
                if (pane == null) continue;
                uint household = address.Household(pane);
                if (balcony.Floor <= 1)
                {
                    if (IsRepairedStack(address, pane)) AddWindow(kit, pane, (int)(household % 4));
                    continue;
                }
                Vector3 forward = balcony.LocalOutward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Bounds deck = balcony.LocalDeckBounds;
                Vector3 floor = new Vector3(deck.center.x, deck.max.y, deck.center.z);
                Vector3 rail = floor + forward * (deck.extents.z - .075f);
                bool repairedStack = IsRepairedStack(address, pane);
                if (repairedStack)
                {
                    string insert = address.Family == 0 ? "BalconyPanel" :
                        address.Family == 1 ? "BalconyGlazed" : "BalconyBrick";
                    kit.Add(insert, rail, forward, Vector3.one);
                    if (address.Family == 1) kit.Add("BalconyGlass", rail, forward, Vector3.one);
                }
                if ((registry.RailFlowerBoxMask & (1UL << door)) != 0)
                {
                    string flowers = (household & (1u << 20)) == 0 ? "RailGeraniums" : "RailPetunias";
                    kit.Add(flowers, FlowerBoxAnchor(balcony, household), forward, Vector3.one);
                }
                if (balcony.Floor != 2) continue;
                int kind = (int)(household % 4);
                float sign = Mathf.Sign(Vector3.Dot(pane.LocalCenter - floor, right));
                kit.Add(Decks[kind], floor + right * (.71f * sign), forward, Vector3.one);
                if (!repairedStack || address.Family != 1) AddWindow(kit, pane, kind);
            }
            // One near-eye-level street opening and one courtyard sill give
            // pedestrians a close read even when looking up hides the decks.
            CityBuildingWindowSlot front = null, rear = null;
            uint frontRank = uint.MaxValue, rearRank = uint.MaxValue;
            foreach (var pane in slots)
            {
                uint rank = address.Household(pane);
                if (pane.Side == "Front" && pane.Floor == 0 && (front == null || rank < frontRank))
                {
                    front = pane;
                    frontRank = rank;
                }
                if (pane.Side == "Rear" && (rear == null || rank < rearRank))
                {
                    rear = pane;
                    rearRank = rank;
                }
            }
            if (front != null && (covered & (1UL << front.Uv2SlotId)) == 0) AddWindow(kit, front, (int)(frontRank % 4));
            if (rear != null && (covered & (1UL << rear.Uv2SlotId)) == 0) AddWindow(kit, rear, (int)(rearRank % 4));
            kit.Finish();
        }

        internal static void AddWindow(ResidentialFrontage kit, CityBuildingWindowSlot pane, int kind)
        {
            var outward = pane.Side == "Rear" ? Vector3.back : Vector3.forward;
            // The prototype has an opaque pane 18 mm from the wall and a sash
            // at 30 mm. Only the clear opening between its four bars is available.
            Vector2 size = pane.SizeMeters;
            float bar = Mathf.Min(.14f, Mathf.Min(size.x * .14f, size.y * .10f));
            AddWindow(kit, kind, pane.LocalCenter + outward * .018f, outward,
                size - Vector2.one * (2f * bar), .030f - .018f);
        }

        internal static void AddWindow(ResidentialFrontage kit, int kind, Vector3 backingCenter,
            Vector3 outward, Vector2 opening, float availableDepth)
        {
            // The four authored modules share a metre-wide sill. In their
            // local space its back is at -8 mm and its nose is at +262 mm.
            // Fit the whole assembly, including the glass, between the opaque
            // backing and the face of the original frame.
            const float clearance = .0015f;
            if (availableDepth <= 2f * clearance) return;
            var scale = new Vector3(opening.x, opening.y * .97f, (availableDepth - 2f * clearance) / .270f);
            Vector3 point = backingCenter - Vector3.up * (.495f * scale.y) +
                outward * (clearance + .008f * scale.z);
            kit.Add(Windows[kind], point, outward, scale);
            kit.Add("WindowGlass", point, outward, scale);
        }

        internal static void RebuildMasked(CityBuildingAssetRegistry registry, BuildingLot lot, int seed)
        {
            if (!ResidentialAddress.Applies(lot)) return;
            var previous = registry.transform.Find(ResidentialFrontage.RootName);
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                previous.SetParent(null, true);
                if (Application.isPlaying) Object.Destroy(previous.gameObject);
                else Object.DestroyImmediate(previous.gameObject);
            }
            BuildVisible(registry, new ResidentialAddress(lot, seed));
        }
    }
}
