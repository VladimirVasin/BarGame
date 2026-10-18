using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared authored assets for the isolated Room enemy.</summary>
    public static class CombatRoomAssetProvider
    {
        public const float PresentationScale = 1.2f;
        public const float UpperArmLengthScale = 1.3f;
        public const float ForearmLengthScale = 1.8f;
        public const float HandLengthScale = 1.5f;
        internal const int HandLengthAxis = 1;
        internal const float RigHeight = 1.75f * PresentationScale;
        internal static float ControllerHeight()
        {
            float height = RigHeight;
            foreach (CombatAuthoredBodyBank.BodyMesh mesh in Bank.LoadBodyManifest().meshes)
                if (mesh.visible) height = Mathf.Max(height, mesh.bounds_max[1] * PresentationScale + .01f);
            return height;
        }
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
