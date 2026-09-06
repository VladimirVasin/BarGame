using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The dirty glass over the mirror's opening: one shared transparent
    /// PS1 Lit material, built the way the toilet's liquid material is,
    /// so the reflection reads through a faint tint of the plate's colour.
    /// </summary>
    internal static class HomeBathroomMirrorResources
    {
        public const string GlassMaterialName = "Home Bathroom Mirror Glass Shared";

        /// <summary>The plate's tint at the transparency the reflection is seen through.</summary>
        public static readonly Color GlassColor = new Color(0.22f, 0.28f, 0.27f, 0.22f);

        private static Material glassMaterial;

        public static Material GlassMaterial
        {
            get
            {
                if (glassMaterial == null)
                {
                    glassMaterial = CreateTransparent(GlassMaterialName, GlassColor, 0.35f);
                }

                return glassMaterial;
            }
        }

        private static Material CreateTransparent(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("Missing PS1 Lit shader for the bathroom mirror glass.");
            }

            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent
            };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            // The pane is a closed box: with culling off its back face would be
            // drawn behind the front one and the tint would compound.
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        // Domain reload is disabled on entering play mode, so the static would
        // carry a material across sessions. It is destroyed rather than merely
        // dropped: a HideAndDontSave material survives every scene load, and
        // one abandoned per play session adds up over an editing day.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            if (glassMaterial != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(glassMaterial);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(glassMaterial);
                }
            }

            glassMaterial = null;
        }
    }
}
