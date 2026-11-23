using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class HomeFurnitureSurface
    {
        private static Material shared;
        private static Texture atlas, household;
        internal static Material Shared => shared != null ? shared :
            shared = Resources.Load<Material>("Home/HomeFurnitureFinish") ??
                throw new InvalidOperationException("Missing apartment furniture finish.");

        internal static bool IsMaterial(Material material) => shared != null && material == shared;

        internal static void Apply(Renderer renderer, MaterialPropertyBlock properties, HomeAuthoredPart part)
        {
            renderer.sharedMaterial = Shared;
            if (atlas == null) atlas = Shared.GetTexture("_BaseMap");
            if (atlas == null) throw new InvalidOperationException("Missing apartment furniture finish atlas.");
            Texture texture = atlas;
            if (part.uv_mode == "household")
            {
                if (household == null) household = Resources.Load<Texture2D>("Home/Textures/HomeHouseholdFinishes");
                texture = household != null ? household : throw new InvalidOperationException("Missing household dressing atlas.");
            }
            properties.SetTexture("_BaseMap", texture);
            properties.SetColor("_BaseColor", Color.white);
            properties.SetColor("_Color", Color.white);
            properties.SetFloat("_Metallic", part.uv_mode == "furniture_metal" ? .56f : 0f);
            properties.SetFloat("_Smoothness", .85f);
            properties.SetFloat("_HomeFinishGloss", 1f);
        }
    }
}
