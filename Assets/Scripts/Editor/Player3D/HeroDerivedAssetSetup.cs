using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Explicit authoring step after the three deterministic source generators.
    /// Packaging only validates these assets and never invokes this repair.
    /// </summary>
    public static class HeroDerivedAssetSetup
    {
        [MenuItem("Bar Promenade/Player/Rebind Generated Hero Dependents")]
        public static void RebuildBatch()
        {
            DefaultNpcAssetSetup.BuildOrThrow();
            PlayerScarfAssetSetup.BuildOrThrow();
            CombatFlashlightAssetSetup.RebuildBatch();
            CombatWeaponCarryAssetSetup.BuildOrThrow();
            PlayerBuildAssetValidation.ValidateOrThrow();
            Debug.Log("HERO DERIVED ASSETS REBOUND AND READ-ONLY GATE PASSED");
        }
    }
}
