using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Explicit authoring after regenerating the hero and its dependent sources.
    /// Packaging only validates these assets and never invokes this repair.
    /// </summary>
    public static class HeroDerivedAssetSetup
    {
        [MenuItem("Bar Promenade/Player/Rebind Generated Hero Dependents")]
        public static void RebuildBatch()
        {
            Player3DV2AssetSetup.BuildOrThrow();
            PlayerScarfAssetSetup.BuildOrThrow();
            CombatFlashlightAssetSetup.RebuildBatch();
            CombatWeaponCarryAssetSetup.BuildOrThrow();
            SecondaryMotionBakeAssetSetup.EnsureCurrent();
            PlayerBuildAssetValidation.ValidateOrThrow();
            Debug.Log("HERO DERIVED ASSETS REBOUND AND READ-ONLY GATE PASSED");
        }
    }
}
