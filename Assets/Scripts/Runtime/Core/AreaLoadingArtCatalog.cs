using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Loading art describes the last directed leg of a journey. A map jump
    /// across the mountain uses that final leg without loading an extra area.
    /// </summary>
    public static class AreaLoadingArtCatalog
    {
        public const string CityToMountain = "UI/Loading/city-to-mountain";
        public const string MountainToCity = "UI/Loading/mountain-to-city";
        public const string MountainToVillage = "UI/Loading/mountain-to-village";
        public const string VillageToMountain = "UI/Loading/village-to-mountain";

        public static IReadOnlyList<string> ResourcePaths { get; } =
            Array.AsReadOnly(new[]
            {
                CityToMountain, MountainToCity,
                MountainToVillage, VillageToMountain
            });

        public static string GetResourcePath(
            GameAreaId? sourceArea, GameAreaId destinationArea)
        {
            if (!sourceArea.HasValue ||
                !AreaSceneCatalog.IsSupported(sourceArea.Value) ||
                !AreaSceneCatalog.IsSupported(destinationArea) ||
                sourceArea.Value == destinationArea)
            {
                return string.Empty;
            }

            switch (destinationArea)
            {
                case GameAreaId.City:
                    return MountainToCity;
                case GameAreaId.MountainRoad:
                    return sourceArea.Value == GameAreaId.City
                        ? CityToMountain : VillageToMountain;
                case GameAreaId.AlpineVillage:
                    return MountainToVillage;
                default:
                    return string.Empty;
            }
        }
    }
}
