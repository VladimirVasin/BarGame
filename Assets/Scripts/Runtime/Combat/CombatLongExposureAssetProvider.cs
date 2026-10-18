using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared authored assets for the isolated Long Exposure enemy.</summary>
    public static class CombatLongExposureAssetProvider
    {
        public static CombatAuthoredBodyBank Bank { get; } = new CombatAuthoredBodyBank(
            "LongExposure", "long_exposure_body_surface_v1", new[] { "flesh", "emulsion", "residue" }, new[] { .25f, .10f, .16f });
        public static CombatAuthoredBodyBank.BodyManifest LoadBodyManifest() => Bank.LoadBodyManifest();
        public static SkinnedMeshRenderer LoadBodyRenderer(string name) => Bank.LoadBodyRenderer(name);
        public static Texture2D LoadBodyAtlas() => Bank.LoadBodyAtlas();
        public static Material LoadBodyMaterial(string surface = "flesh") => Bank.LoadBodyMaterial(surface);
        public static void ApplyAppearance(GameObject npc, string characterId = null)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            (npc.GetComponent<CombatLongExposureAppearance>() ?? npc.AddComponent<CombatLongExposureAppearance>()).Configure(characterId);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Bank.Clear();
    }
}
