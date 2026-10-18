using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared authored assets for the isolated Imprint enemy.</summary>
    public static class CombatImprintAssetProvider
    {
        public static CombatAuthoredBodyBank Bank { get; } = new CombatAuthoredBodyBank(
            "Imprint", "imprint_body_surface_v1", new[] { "chalk", "glass", "flesh" }, new[] { .16f, .10f, .25f });
        public static CombatAuthoredBodyBank.BodyManifest LoadBodyManifest() => Bank.LoadBodyManifest();
        public static SkinnedMeshRenderer LoadBodyRenderer(string name) => Bank.LoadBodyRenderer(name);
        public static Texture2D LoadBodyAtlas() => Bank.LoadBodyAtlas();
        public static Material LoadBodyMaterial(string surface = "flesh") => Bank.LoadBodyMaterial(surface);
        public static void ApplyAppearance(GameObject npc, string characterId = null)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            (npc.GetComponent<CombatImprintAppearance>() ?? npc.AddComponent<CombatImprintAppearance>()).Configure(characterId);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Bank.Clear();
    }
}
