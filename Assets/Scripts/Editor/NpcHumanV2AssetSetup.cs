using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Rebuilds every player-visible humanoid NPC asset against the shared
    /// Hero/NPC V2 skeleton. This explicit entry point is also the production
    /// batch contract: individual postprocessors are intentionally allowed to
    /// defer work while a dependency is still importing.
    /// </summary>
    public static class NpcHumanV2AssetSetup
    {
        private static bool isBuilding;

        public static bool IsBuilding => isBuilding;

        public static bool IsAnyPipelineBuilding =>
            isBuilding ||
            CityPedestrianAssetSetup.IsBuilding ||
            CityArchShelterResidentAssetSetup.IsBuilding ||
            MothersHouseMotherAssetSetup.IsBuilding ||
            MountainRoadCafeCastAssetSetup.IsBuilding ||
            CityPedestrianHandPropAssetSetup.IsBuilding ||
            BarBartenderAssetSetup.IsBuilding ||
            SupermarketCashierAssetSetup.IsBuilding ||
            CityBusDriverAssetSetup.IsBuilding ||
            CityMiscAssetSetup.IsBuilding;

        [MenuItem("Bar Promenade/NPC Human V2/Rebuild All Runtime Assets")]
        public static void RunBatch()
        {
            if (isBuilding)
            {
                return;
            }

            isBuilding = true;
            try
            {
                CityPedestrianAssetSetup.BuildOrThrow();
                CityArchShelterResidentAssetSetup.BuildOrThrow();
                MothersHouseMotherAssetSetup.BuildOrThrow();
                MountainRoadCafeCastAssetSetup.BuildOrThrow();
                // After the bodies: the hand props are measured against
                // the freshly imported reference body FBXs.
                CityPedestrianHandPropAssetSetup.BuildOrThrow();
                BarBartenderAssetSetup.BuildOrThrow();
                SupermarketCashierAssetSetup.BuildOrThrow();
                CityBusDriverAssetSetup.BuildOrThrow();
                CityMiscAssetSetup.BuildOrThrow();

                CityPedestrianAssetSetup.ValidateOrThrow();
                CityArchShelterResidentAssetSetup.ValidateOrThrow();
                MothersHouseMotherAssetSetup.ValidateOrThrow();
                MountainRoadCafeCastAssetSetup.ValidateOrThrow();
                CityPedestrianHandPropAssetSetup.ValidateOrThrow();
                BarBartenderAssetSetup.ValidateOrThrow();
                SupermarketCashierAssetSetup.ValidateOrThrow();
                CityBusDriverAssetSetup.ValidateOrThrow();
                CityMiscAssetSetup.ValidateOrThrow();

                AssetDatabase.SaveAssets();
                Debug.Log(
                    "NPC Human V2: all rigged NPC prefabs, including the " +
                    "arch-shelter residents, were rebuilt and validated.");
            }
            finally
            {
                isBuilding = false;
            }
        }
    }
}
