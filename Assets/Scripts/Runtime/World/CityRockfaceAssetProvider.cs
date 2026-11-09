using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Readable Blender templates. Local XY spans the cliff patch and +Z faces City.</summary>
    internal static class CityRockfaceAssetProvider
    {
        internal const int VariantCount = 4;
        private static readonly Mesh[] Meshes = new Mesh[VariantCount];

        internal static Mesh GetMesh(int variant)
        {
            if ((uint)variant >= VariantCount)
                throw new ArgumentOutOfRangeException(nameof(variant));
            if (Meshes[variant] != null)
                return Meshes[variant];
            string path = "City/Rockface/CityRockface" + variant;
            GameObject model = Resources.Load<GameObject>(path);
            MeshFilter filter = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Missing City rockface mesh at " + path);
            return Meshes[variant] = filter.sharedMesh;
        }
    }
}
