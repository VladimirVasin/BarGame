using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One shared albedo atlas on the ground's junction material slot.
    /// Each junction owns a tile; no overlay, material instance or shader pass.</summary>
    internal static class AlpineVillageJunctionAppearance
    {
        internal const string TextureResourcePath = "Village/Textures/Junctions/VillageJunctionAtlas";
        internal const int TileSize = 1024;
        internal const int Gutter = 4;
        internal const int ContentSize = TileSize - 2 * Gutter;
        internal const int AtlasSize = TileSize * 2;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapTransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static Texture2D cachedTexture;

        internal static Texture2D Texture
        {
            get
            {
                if (cachedTexture == null) cachedTexture = Resources.Load<Texture2D>(TextureResourcePath);
                if (cachedTexture == null)
                    throw new InvalidOperationException("Bake the Alpine Village junction textures before building the world.");
                return cachedTexture;
            }
        }

        internal static Vector2 Uv(AlpineVillagePlan plan, Vector2 world)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            for (int index = 0; index < plan.Expansion.Junctions.Count; index++)
            {
                Rect bounds = plan.Expansion.Junctions[index].Bounds;
                const float tolerance = .001f;
                if (world.x < bounds.xMin - tolerance || world.x > bounds.xMax + tolerance ||
                    world.y < bounds.yMin - tolerance || world.y > bounds.yMax + tolerance) continue;
                Vector2 normalized = new Vector2(Mathf.InverseLerp(bounds.xMin, bounds.xMax, world.x),
                    Mathf.InverseLerp(bounds.yMin, bounds.yMax, world.y));
                // Bounds map to the active edge texel centres. The surrounding
                // repeated four pixels keep bilinear filtering inside this tile.
                return (new Vector2(index % 2, index / 2) * TileSize +
                    Vector2.one * (Gutter + .5f) + normalized * (ContentSize - 1)) / AtlasSize;
            }
            throw new ArgumentOutOfRangeException(nameof(world), world, "No junction atlas tile owns this point.");
        }

        internal static void Apply(Renderer renderer, int materialIndex)
        {
            if (renderer == null) return;
            if (materialIndex < 0) throw new ArgumentOutOfRangeException(nameof(materialIndex));
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties, materialIndex);
            properties.SetTexture(BaseMapId, Texture);
            properties.SetColor(BaseColorId, Color.white);
            properties.SetColor(ColorId, Color.white);
            properties.SetVector(BaseMapTransformId, new Vector4(1f, 1f, 0f, 0f));
            properties.SetFloat(SmoothnessId, 0f);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties, materialIndex);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources() => cachedTexture = null;
    }
}
