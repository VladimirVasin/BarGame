using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared, readable unit meshes. Local +Z points back into the escarpment.</summary>
    internal static class CityRockfallAssetProvider
    {
        internal const int VariantCount = 3;
        internal const string ResourcePrefix = "City/Rockfall/CityRockfall";
        private static readonly Mesh[] Meshes = new Mesh[VariantCount];

        internal static Mesh GetMesh(int variant)
        {
            if ((uint)variant >= VariantCount)
                throw new ArgumentOutOfRangeException(nameof(variant));
            if (Meshes[variant] != null)
                return Meshes[variant];

            string path = ResourcePrefix + variant;
            var model = Resources.Load<GameObject>(path);
            MeshFilter filter = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Missing City rockfall mesh at " + path);
            return Meshes[variant] = filter.sharedMesh;
        }
    }
}
