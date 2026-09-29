using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Refreshes the five detailed ordinary actors without rebuilding the rest of the cast.</summary>
    public static class PrincipalNormalNpcAssetSetup
    {
        public static bool IsBuilding { get; private set; }

        [MenuItem("Bar Promenade/NPC Human V2/Rebuild Principal Ordinary Actors")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log("Principal ordinary actors: fisherman, bartender, cashier, bus driver and mother rebuilt.");
        }

        public static void BuildOrThrow()
        {
            if (IsBuilding) return;
            IsBuilding = true;
            try
            {
                CityPedestrianAssetSetup.BuildLakeFishermanOrThrow();
                BarBartenderV2AssetSetup.BuildOrThrow();
                SupermarketCashierAssetSetup.BuildNormalOrThrow();
                CityBusDriverAssetSetup.BuildOrThrow();
                MothersHouseMotherAssetSetup.BuildOrThrow();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                IsBuilding = false;
            }
        }

        public static void ValidateOrThrow()
        {
            CityPedestrianAssetSetup.ValidateLakeFishermanOrThrow();
            BarBartenderV2AssetSetup.ValidateOrThrow();
            SupermarketCashierAssetSetup.ValidateNormalOrThrow();
            CityBusDriverAssetSetup.ValidateOrThrow();
            MothersHouseMotherAssetSetup.ValidateOrThrow();
        }
    }
}
