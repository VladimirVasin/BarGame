using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Shared Blender-authored sink and brushing liquid resources.</summary>
    internal static class HomeBrushingResources
    {
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        private static Material foam;

        public static Material Foam
        {
            get
            {
                if (foam != null) return foam;
                Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
                if (shader == null) throw new InvalidOperationException("Missing PS1 Lit shader for brushing foam.");
                foam = new Material(shader)
                {
                    name = "Home Brushing Foam Shared",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };
                foam.SetColor("_BaseColor", new Color(0.82f, 0.81f, 0.72f, 0.9f));
                foam.SetFloat("_Smoothness", 0.45f);
                foam.SetFloat("_Surface", 1f);
                foam.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                foam.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                foam.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                foam.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                foam.SetFloat("_ZWrite", 0f);
                foam.SetFloat("_Cull", (float)CullMode.Off);
                foam.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return foam;
            }
        }

        public static Mesh Mesh(string name)
        {
            if (meshes.TryGetValue(name, out Mesh existing) && existing != null) return existing;
            GameObject asset = Resources.Load<GameObject>("HomeBrushingAction/Models/" + name);
            if (asset == null) throw new InvalidOperationException("Missing authored brushing resource: " + name);
            MeshFilter[] filters = asset.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) throw new InvalidOperationException("Brushing resource has no Blender mesh: " + name);
            var combine = new CombineInstance[filters.Length];
            for (int index = 0; index < filters.Length; index++)
                combine[index] = new CombineInstance
                {
                    mesh = filters[index].sharedMesh,
                    transform = filters[index].transform.localToWorldMatrix
                };
            var mesh = new Mesh
            {
                name = "Home Brushing " + name + " Metres",
                hideFlags = HideFlags.HideAndDontSave
            };
            // Imported child matrices include FBX unit/axis conversion.
            // The shared result is readable and already in local metres.
            mesh.CombineMeshes(combine, true, true, false);
            meshes[name] = mesh;
            return mesh;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (Mesh mesh in meshes.Values) Destroy(mesh);
            meshes.Clear();
            Destroy(foam);
            foam = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
