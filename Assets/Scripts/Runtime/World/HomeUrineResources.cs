using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Shared lit liquid resources and normalized Blender-authored effect meshes.</summary>
    internal static class HomeUrineResources
    {
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        private static Material liquid;
        private static Material residue;
        private static AudioClip bowl;
        private static AudioClip solid;
        public static Material Liquid => liquid != null ? liquid : liquid = Material("Home Urine Liquid Shared", new Color(0.64f, 0.58f, 0.20f, 0.82f), 0.8f);
        public static Material Residue
        {
            get
            {
                if (residue == null)
                {
                    residue = Material("Home Urine Wet Film Shared", new Color(0.40f, 0.35f, 0.10f, 0.56f), 0.92f);
                    residue.SetFloat("_Cull", (float)CullMode.Back);
                }
                return residue;
            }
        }
        public static AudioClip Bowl => bowl != null ? bowl : bowl = ContactClip(true);
        public static AudioClip Solid => solid != null ? solid : solid = ContactClip(false);

        public static Mesh Mesh(string name)
        {
            if (meshes.TryGetValue(name, out Mesh existing)) return existing;
            GameObject asset = Resources.Load<GameObject>("HomeToiletAction/Models/" + name);
            if (asset == null) throw new InvalidOperationException("Missing authored toilet effect: " + name);
            MeshFilter[] filters = asset.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) throw new InvalidOperationException("Toilet effect has no Blender mesh: " + name);
            var combine = new CombineInstance[filters.Length];
            for (int i = 0; i < filters.Length; i++)
                combine[i] = new CombineInstance { mesh = filters[i].sharedMesh, transform = filters[i].transform.localToWorldMatrix };
            var mesh = new Mesh { name = "Home Urine " + name + " Metres", hideFlags = HideFlags.HideAndDontSave };
            mesh.CombineMeshes(combine, true, true, false);
            meshes.Add(name, mesh);
            return mesh;
        }

        private static Material Material(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
            if (shader == null) throw new InvalidOperationException("Missing PS1 Lit shader for toilet liquid.");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave, renderQueue = (int)RenderQueue.Transparent };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        private static AudioClip ContactClip(bool inBowl)
        {
            const int rate = 22050;
            var samples = new float[rate];
            uint seed = inBowl ? 971u : 353u;
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 8) / 8388607.5f) - 1f;
                low = Mathf.Lerp(low, noise, inBowl ? 0.12f : 0.34f);
                float t = i / (float)rate;
                float pulse = 0.65f + 0.35f * Mathf.Sin(t * Mathf.PI * 2f * (inBowl ? 37f : 53f));
                float bubble = inBowl ? Mathf.Sin(t * Mathf.PI * 2f * 310f + 0.8f * Mathf.Sin(t * Mathf.PI * 2f * 19f)) * 0.025f : 0f;
                float edge = Mathf.Clamp01(Mathf.Min(i, samples.Length - 1 - i) / 96f);
                samples[i] = (low * pulse * 0.50f + bubble) * edge;
            }
            AudioClip clip = AudioClip.Create(inBowl ? "Home Urine Bowl Contact" : "Home Urine Surface Contact", rate, 1, rate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (Mesh mesh in meshes.Values) Destroy(mesh);
            meshes.Clear(); Destroy(liquid); Destroy(residue); Destroy(bowl); Destroy(solid);
            liquid = null; residue = null; bowl = null; solid = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
