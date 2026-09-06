using System;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageRockMeshRole
    {
        Stone = 0,
        Snow = 1
    }

    [Serializable]
    public sealed class VillageRockMeshEntry
    {
        [SerializeField] private int variant;
        [SerializeField] private VillageRockMeshRole role;
        [SerializeField] private Mesh mesh;
        [SerializeField] private Vector3 importedScale = Vector3.one;

        public int Variant => variant;
        public VillageRockMeshRole Role => role;
        public Mesh Mesh => mesh;
        public Vector3 ImportedScale => importedScale;
    }

    /// <summary>Fixed-metre Blender geometry. The terrain retains collision;
    /// the library only exposes the face of its existing steep mass.</summary>
    public sealed class VillageRockAssetProvider : ScriptableObject
    {
        public const string ResourcePath = "Village/VillageRockAssetProvider";
        public const string GeneratorVersion = "1.0.0";
        public const string DesignId = "alpine_village_bedded_rock_v1";
        public const int VariantCount = 4;
        public const int ExpectedMeshCount = VariantCount * 2;

        // A conservative fixed-metre envelope checked against every imported
        // mesh, and shared by the pure placement exclusion checks.
        public const float HalfWidth = 13f;
        public const float Depth = 19f;
        public const float Height = 49f;
        public const float AuthoredRidgeRise = 3.6f;

        [SerializeField] private string designId = DesignId;
        [SerializeField] private string buildSignature = string.Empty;
        [SerializeField] private VillageRockMeshEntry[] entries =
            Array.Empty<VillageRockMeshEntry>();

        public string BuildSignature => buildSignature;

        public static string MeshName(int variant, VillageRockMeshRole role)
        {
            return $"GEO_VillageRock_Variant{variant:00}_{role}";
        }

        public VillageRockMeshEntry GetPartOrThrow(int variant, VillageRockMeshRole role)
        {
            if (entries != null)
            {
                for (int index = 0; index < entries.Length; index++)
                {
                    VillageRockMeshEntry entry = entries[index];
                    if (entry != null && entry.Variant == variant && entry.Role == role &&
                        entry.Mesh != null)
                    {
                        return entry;
                    }
                }
            }

            throw new InvalidOperationException("Missing village rock mesh " + MeshName(variant, role));
        }

        public void ValidateOrThrow()
        {
            if (designId != DesignId || string.IsNullOrEmpty(buildSignature) ||
                entries == null || entries.Length != ExpectedMeshCount)
            {
                throw new InvalidOperationException(
                    "Invalid village rock provider; run Bar Promenade/Village/Bind Rock Provider.");
            }

            for (int variant = 0; variant < VariantCount; variant++)
            {
                for (int role = 0; role < 2; role++)
                {
                    VillageRockMeshEntry entry = GetPartOrThrow(variant, (VillageRockMeshRole)role);
                    Vector3 scale = entry.ImportedScale;
                    if (!float.IsFinite(scale.x) || !float.IsFinite(scale.y) ||
                        !float.IsFinite(scale.z) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                    {
                        throw new InvalidOperationException("Invalid imported village rock scale.");
                    }
                }
            }
        }

        public static VillageRockAssetProvider LoadOrThrow()
        {
            var provider = Resources.Load<VillageRockAssetProvider>(ResourcePath);
            if (provider == null)
            {
                throw new InvalidOperationException("Missing village rock provider at " + ResourcePath);
            }

            provider.ValidateOrThrow();
            return provider;
        }
    }
}
