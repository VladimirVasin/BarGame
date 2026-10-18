using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared authored assets for the isolated Room enemy.</summary>
    public static class CombatRoomAssetProvider
    {
        public static CombatAuthoredBodyBank Bank { get; } = new CombatAuthoredBodyBank(
            "Room", "room_body_surface_v1", new[] { "cloth", "plaster", "flesh" }, new[] { .14f, .08f, .25f });
        public static CombatAuthoredBodyBank.BodyManifest LoadBodyManifest() => Bank.LoadBodyManifest();
        public static SkinnedMeshRenderer LoadBodyRenderer(string name) => Bank.LoadBodyRenderer(name);
        public static Texture2D LoadBodyAtlas() => Bank.LoadBodyAtlas();
        public static Material LoadBodyMaterial(string surface = "flesh") => Bank.LoadBodyMaterial(surface);
        public static void ApplyAppearance(GameObject npc, string characterId = null)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            (npc.GetComponent<CombatRoomAppearance>() ?? npc.AddComponent<CombatRoomAppearance>()).Configure(characterId);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Bank.Clear();
    }
}
