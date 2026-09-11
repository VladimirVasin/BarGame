using System;

namespace BarPromenade
{
    public enum NewGameLocation
    {
        AlpineVillage = 0,
        MothersHouse = 1,
        City = 2,
        Docks = 3,
        Cannery = 4,
        MountainRoad = 5,
        Home = 6,
        Stairwell = 7,
        Bar = 8,
        Supermarket = 9,
        Church = 10,
        Count = 11
    }

    /// <summary>Launch choices, independent of travel areas and build indices.</summary>
    public static class NewGameLocationCatalog
    {
        public const int Count = (int)NewGameLocation.Count;

        public static NewGameLocation Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return (NewGameLocation)index;
        }

        public static bool IsSupported(NewGameLocation location) =>
            (int)location >= 0 && (int)location < Count;

        public static string LabelKey(NewGameLocation location)
        {
            switch (location)
            {
                case NewGameLocation.AlpineVillage: return "opening.location.village";
                case NewGameLocation.MothersHouse: return "opening.location.mothers_house";
                case NewGameLocation.City: return "opening.location.city";
                case NewGameLocation.Docks: return "opening.location.docks";
                case NewGameLocation.Cannery: return "opening.location.cannery";
                case NewGameLocation.MountainRoad: return "opening.location.mountain_road";
                case NewGameLocation.Home: return "opening.location.home";
                case NewGameLocation.Stairwell: return "opening.location.stairwell";
                case NewGameLocation.Bar: return "opening.location.bar";
                case NewGameLocation.Supermarket: return "opening.location.supermarket";
                case NewGameLocation.Church: return "opening.location.church";
                default: throw new ArgumentOutOfRangeException(nameof(location));
            }
        }

        public static string SceneName(NewGameLocation location)
        {
            switch (location)
            {
                case NewGameLocation.AlpineVillage: return SceneIds.AlpineVillage;
                case NewGameLocation.MothersHouse: return SceneIds.MothersHouseInterior;
                case NewGameLocation.City:
                case NewGameLocation.Docks:
                case NewGameLocation.Cannery: return SceneIds.City;
                case NewGameLocation.MountainRoad: return SceneIds.MountainRoad;
                case NewGameLocation.Home: return SceneIds.HomeInterior;
                case NewGameLocation.Stairwell: return SceneIds.StairwellInterior;
                case NewGameLocation.Bar: return SceneIds.BarInterior;
                case NewGameLocation.Supermarket: return SceneIds.SupermarketInterior;
                case NewGameLocation.Church: return SceneIds.ChurchInterior;
                default: throw new ArgumentOutOfRangeException(nameof(location));
            }
        }
    }
}
