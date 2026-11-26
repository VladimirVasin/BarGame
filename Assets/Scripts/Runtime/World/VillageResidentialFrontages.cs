using UnityEngine;

namespace BarPromenade
{
    internal sealed class VillageResidentialFrontages
    {
        private static readonly string[] Types = { "ShutterSingle", "ShutterRepaired", "ShutterSlatted" };
        private readonly ResidentialFrontage kit;
        private readonly uint key;
        private readonly bool occupied;
        private int openings;
        internal VillageResidentialFrontages(Transform house, VillageHousehold household)
        {
            key = ResidentialAddress.VillageKey(household.HouseId);
            occupied = household.Occupied;
            kit = new ResidentialFrontage(house);
        }

        internal void Window(Vector3 center, Vector3 size)
        {
            int ordinal = openings++;
            // Each front gets a readable pair or one surviving leaf. Repeat
            // the household finish at one other opening, leaving the rest quiet.
            if (ordinal != (int)(key & 1) && ordinal != 3 + (int)(key % 3)) return;
            bool side = size.x < size.z;
            Vector3 normal = side ? Vector3.right * Mathf.Sign(center.x) : Vector3.forward * Mathf.Sign(center.z);
            Vector3 across = Vector3.Cross(Vector3.up, normal);
            float halfWidth = Mathf.Max(size.x, size.z) * .63f;
            Vector3 scale = new Vector3(1, size.y * 1.06f, 1);
            int first = (key & 4) == 0 ? -1 : 1;
            Place(first);
            if (occupied || (key & 16) != 0) Place(-first);

            void Place(int sign)
            {
                string shutter = Types[(int)((key >> 8) % 3)];
                kit.Add(shutter, center + across * ((halfWidth + .23f) * sign) + normal * .085f, normal, scale);
            }
        }
        internal void Finish() => kit.Finish();

        internal static void Abandoned(Transform house, AlpineVillageAbandonedPlot plot)
        {
            if (VillageHouseholdCatalog.FindHouse(plot.Id) == null) return;
            uint key = ResidentialAddress.VillageKey(plot.Id);
            var kit = new ResidentialFrontage(house);
            bool twoStoreys = plot.Model.EndsWith("B", System.StringComparison.Ordinal);
            float side = (key & 1) == 0 ? -1 : 1;
            if (plot.Model == "AvalancheRuinedHouse")
            {
                // The impact took the facade. Seat masonry on the actual
                // surviving floor instead of reconstructing the lost wall.
                kit.Add("FoundationPatch", new Vector3(side * 2.3f, .20f, .8f),
                    Vector3.forward, Vector3.one);
            }
            else
            {
                float depth = twoStoreys ? 4f : 3.5f;
                float across = side * ((twoStoreys ? 9f : 8f) * .32f + ((key >> 7) % 3 - 1f) * .18f);
                bool ruin = plot.Model == "RuinedHouse" || plot.Model == "HouseholdFoundation";
                if (!ruin)
                {
                    // Same real first-storey sash centres as the two former
                    // household models. One leaf is nailed over the old joinery.
                    float openingHeight = twoStoreys ? .98f : 1.20f;
                    float storeyHeight = twoStoreys ? 2.11f : 3.27f;
                    float sill = (twoStoreys ? .62f : .48f) + (storeyHeight - openingHeight) * .45f;
                    float sashX = side * (twoStoreys ? 9f : 8f) * .32f;
                    var hinge = new Vector3(sashX + side * .25f, sill + openingHeight * .5f, depth + .19f);
                    kit.Add(Types[(int)((key >> 10) % 3)], hinge, Vector3.forward,
                        new Vector3(1.12f, openingHeight, 1f));
                }
                kit.Add("FoundationPatch", new Vector3(across, ruin ? .19f : .04f, depth + .035f),
                    Vector3.forward, new Vector3(.82f + (key % 7) * .075f, ruin ? .85f : 1, 1));
            }
            kit.Finish();
        }
    }
}
