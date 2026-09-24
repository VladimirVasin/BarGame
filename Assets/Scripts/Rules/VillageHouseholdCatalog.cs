using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>A permanent household assignment, including homes whose occupants have left.</summary>
    public sealed class VillageHousehold
    {
        internal VillageHousehold(string houseId, string familyId, bool occupied = false)
        {
            HouseId = houseId;
            FamilyId = familyId;
            Occupied = occupied;
        }

        public string HouseId { get; }
        public string FamilyId { get; }
        public bool Occupied { get; }
        // The hero's household deliberately has no public surname.
        public string FamilyNameKey => FamilyId == "hero-family" ? string.Empty : "village.family." + FamilyId;
    }

    public sealed class VillageHouseholdResident
    {
        internal VillageHouseholdResident(string roleId, string characterId, string houseId)
        {
            RoleId = roleId;
            CharacterId = characterId;
            HouseId = houseId;
        }

        public string RoleId { get; }
        public string CharacterId { get; }
        public string HouseId { get; }
        public string NameKey => "village.resident." + CharacterId.Substring("village.".Length) + ".name";
        public string FirstNameKey => "village.resident." + CharacterId.Substring("village.".Length) + ".first_name";
        public string GreetingPromptKey => "village.resident." + CharacterId.Substring("village.".Length) + ".greet";
        public VillageHousehold Household => VillageHouseholdCatalog.FindHouse(HouseId);
    }

    /// <summary>
    /// Canonical house ownership, independent of Unity, placement and lighting.
    /// Absent relatives are data only; this catalog never creates new people.
    /// </summary>
    public static class VillageHouseholdCatalog
    {
        public static IReadOnlyList<VillageHousehold> Houses { get; } = Array.AsReadOnly(new[]
        {
            new VillageHousehold("village-house-00", "talven"),
            new VillageHousehold("village-house-01", "brenk"),
            new VillageHousehold("village-house-02", "zorden"),
            new VillageHousehold("village-house-03", "ilvar"),
            new VillageHousehold("village-house-04", "veidra", true),
            new VillageHousehold("village-house-05", "norska"),
            new VillageHousehold("village-house-06", "velt"),
            new VillageHousehold("village-house-07", "drelis"),
            new VillageHousehold("village-house-08", "drauven", true),
            new VillageHousehold("village-house-09", "savren"),
            new VillageHousehold("village-house-10", "klaven"),
            new VillageHousehold("village-house-11", "kersna", true),
            new VillageHousehold("village-mothers-house", "hero-family", true),
            new VillageHousehold("homestead-01", "ruven"),
            new VillageHousehold("homestead-02", "brenis"),
            new VillageHousehold("homestead-03", "zelven"),
            new VillageHousehold("homestead-04", "dorska"),
            new VillageHousehold("homestead-05", "keldar"),
            new VillageHousehold("homestead-06", "merkas"),
            new VillageHousehold("homestead-07", "runska"),
            new VillageHousehold("homestead-08", "valtis"),
            new VillageHousehold("homestead-09", "eldarn"),
            new VillageHousehold("homestead-10", "vardis"),
            new VillageHousehold("homestead-11", "selk"),
            new VillageHousehold("homestead-12", "tarvek"),
            new VillageHousehold("homestead-13", "drenov"),
            new VillageHousehold("homestead-14", "velska"),
            new VillageHousehold("homestead-15", "orsven"),
            new VillageHousehold("homestead-16", "lauren"),
            new VillageHousehold("homestead-17", "kravis"),
            new VillageHousehold("homestead-18", "straven")
        });

        public static IReadOnlyList<VillageHouseholdResident> Residents { get; } = Array.AsReadOnly(new[]
        {
            new VillageHouseholdResident("StationWorker", "village.station-worker", "village-house-04"),
            new VillageHouseholdResident("WoodWoman", "village.wood-woman", "village-house-04"),
            new VillageHouseholdResident("RepairNeighbor", "village.repair-neighbor", "village-house-08"),
            new VillageHouseholdResident("SewingWoman", "village.sewing-woman", "village-house-08"),
            new VillageHouseholdResident("SnowNeighbor", "village.snow-neighbor", "village-house-11"),
            new VillageHouseholdResident("BasketVisitor", "village.basket-visitor", "village-house-11")
        });

        public static VillageHousehold FindHouse(string houseId)
        {
            foreach (VillageHousehold house in Houses)
                if (string.Equals(house.HouseId, houseId, StringComparison.Ordinal)) return house;
            return null;
        }

        public static VillageHouseholdResident FindResident(string roleId)
        {
            foreach (VillageHouseholdResident resident in Residents)
                if (string.Equals(resident.RoleId, roleId, StringComparison.Ordinal)) return resident;
            return null;
        }

        public static bool IsOccupied(string houseId) => FindHouse(houseId)?.Occupied ?? false;
    }
}
