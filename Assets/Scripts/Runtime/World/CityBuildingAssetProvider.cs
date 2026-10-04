using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class CityBuildingPrefabEntry
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private CityDistrictKind district;
        [SerializeField] private GameObject prefab;

        public CityBuildingPrefabEntry(
            string configuredStableId,
            CityDistrictKind configuredDistrict,
            GameObject configuredPrefab)
        {
            stableId = configuredStableId ?? string.Empty;
            district = configuredDistrict;
            prefab = configuredPrefab;
        }

        public string StableId => stableId;
        public CityDistrictKind District => district;
        public GameObject Prefab => prefab;
    }

    /// <summary>
    /// Serialized Resources bridge to the passive district massing catalog. Runtime
    /// never loads the FBX directly; wrappers reference its imported meshes.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CityBuildingAssetProvider",
        menuName = "Bar Promenade/City Building Asset Provider")]
    public sealed class CityBuildingAssetProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/CityBuildingAssetProvider";
        public const string ExpectedDesignId =
            "city_buildings_prototypes_v3";
        public const int ExpectedPrototypeCount = 20;

        private static readonly PrototypeSpec[] ExpectedPrototypes =
        {
            new PrototypeSpec(
                "old-town-prototype-01",
                CityDistrictKind.OldTown,
                14f,
                13.5f,
                18f),
            new PrototypeSpec(
                "residential-prototype-01",
                CityDistrictKind.Residential,
                11.5f,
                11.5f,
                15f),
            new PrototypeSpec(
                "industrial-prototype-01",
                CityDistrictKind.Industrial,
                14f,
                13.5f,
                10f),
            new PrototypeSpec(
                "nightlife-prototype-01",
                CityDistrictKind.Nightlife,
                12.5f,
                12f,
                27f),
            new PrototypeSpec("old-town-prototype-02", CityDistrictKind.OldTown, 22f, 11.5f, 16.2f),
            new PrototypeSpec("residential-prototype-02", CityDistrictKind.Residential, 22f, 11.5f, 14.4f),
            new PrototypeSpec("industrial-prototype-02", CityDistrictKind.Industrial, 22f, 11.5f, 8.4f),
            new PrototypeSpec("nightlife-prototype-02", CityDistrictKind.Nightlife, 17f, 9.5f, 24.6f),
            new PrototypeSpec("old-town-prototype-03", CityDistrictKind.OldTown, 15f, 14f, 17.4f),
            new PrototypeSpec("residential-prototype-03", CityDistrictKind.Residential, 15f, 14f, 15.6f),
            new PrototypeSpec("industrial-prototype-03", CityDistrictKind.Industrial, 15f, 14f, 9.3f),
            new PrototypeSpec("nightlife-prototype-03", CityDistrictKind.Nightlife, 15f, 14f, 26.4f),
            new PrototypeSpec("old-town-prototype-04", CityDistrictKind.OldTown, 22f, 11.5f, 16.2f),
            new PrototypeSpec("residential-prototype-04", CityDistrictKind.Residential, 22f, 11.5f, 14.4f),
            new PrototypeSpec("industrial-prototype-04", CityDistrictKind.Industrial, 22f, 11.5f, 8.4f),
            new PrototypeSpec("nightlife-prototype-04", CityDistrictKind.Nightlife, 17f, 9.5f, 24.6f),
            new PrototypeSpec("old-town-prototype-05", CityDistrictKind.OldTown, 10f, 6f, 12.6f),
            new PrototypeSpec("residential-prototype-05", CityDistrictKind.Residential, 10f, 6f, 11.8f),
            new PrototypeSpec("industrial-prototype-05", CityDistrictKind.Industrial, 10f, 6f, 6.9f),
            new PrototypeSpec("nightlife-prototype-05", CityDistrictKind.Nightlife, 10f, 6f, 18.6f)
        };

        [SerializeField] private CityBuildingPrefabEntry[] entries =
            Array.Empty<CityBuildingPrefabEntry>();
        [SerializeField] private string designId = string.Empty;
        [SerializeField] private string buildSignature = string.Empty;

        public IReadOnlyList<CityBuildingPrefabEntry> Entries => entries;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool HasCompletePrefabs
        {
            get
            {
                try
                {
                    ValidateOrThrow();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public static CityBuildingAssetProvider Load()
        {
            return Resources.Load<CityBuildingAssetProvider>(ResourcePath);
        }

        public static CityBuildingAssetProvider LoadOrThrow()
        {
            CityBuildingAssetProvider provider = Load();
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Missing City building provider at Resources/" +
                    $"{ResourcePath}.");
            }

            provider.ValidateOrThrow();
            return provider;
        }

        public static string GetExpectedStableId(int index)
        {
            return GetExpectedPrototype(index).StableId;
        }

        public static CityDistrictKind GetExpectedDistrict(int index)
        {
            return GetExpectedPrototype(index).District;
        }

        public static Vector3 GetExpectedEnvelope(int index)
        {
            PrototypeSpec prototype = GetExpectedPrototype(index);
            return new Vector3(
                prototype.FrontageWidth,
                prototype.Height,
                prototype.Depth);
        }

        public static Vector3 GetExpectedEnvelope(
            CityDistrictKind district)
        {
            for (int index = 0; index < ExpectedPrototypes.Length; index++)
            {
                PrototypeSpec prototype = ExpectedPrototypes[index];
                if (prototype.District == district)
                {
                    return new Vector3(
                        prototype.FrontageWidth,
                        prototype.Height,
                        prototype.Depth);
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(district),
                district,
                "Only ordinary urban districts own prototypes.");
        }

        public static int GetVariantCount(CityDistrictKind district)
        {
            GetExpectedEnvelope(district);
            return 5;
        }

        public static Vector3 GetExpectedEnvelope(CityDistrictKind district, int variantIndex)
        {
            return GetExpectedEnvelope(GetPrototypeIndex(district, variantIndex));
        }

        public static IReadOnlyList<Bounds> GetExpectedCollisionBounds(
            CityDistrictKind district, int variantIndex)
        {
            Vector3 envelope = GetExpectedEnvelope(district, variantIndex);
            if (variantIndex == 0)
            {
                switch (district)
                {
                    case CityDistrictKind.OldTown:
                        return new[] { Solid(-7f, 0f, -6.75f, -0.6f, 13f, 6.75f),
                            Solid(0.8f, 0f, -6.75f, 7f, 11.8f, 5.15f),
                            Solid(-1.5f, 0f, -6.71f, 1.5f, 15f, -2.21f) };
                    case CityDistrictKind.Residential:
                        return new[] { Solid(-5.75f, 0f, -5.75f, 5.75f, 13.7f, -1.55f),
                            Solid(-5.75f, 0f, -1.55f, -2.95f, 13.7f, 3.65f),
                            Solid(2.95f, 0f, -1.55f, 5.75f, 13.7f, 3.65f),
                            Solid(-1.6f, 0f, -3.2f, 1.6f, 14.7f, 0f) };
                    case CityDistrictKind.Industrial:
                        return new[] { Solid(-7f, 0f, -6.75f, 7f, 6.6f, 6.75f),
                            Solid(-6.6f, 0f, -5.6f, -2.4f, 8.1f, -1.6f),
                            Solid(2.9f, 0f, -5.6f, 6.7f, 7.4f, -2.4f) };
                    case CityDistrictKind.Nightlife:
                        return new[] { Solid(-6.25f, 0f, -6f, 6.25f, 4.8f, 6f),
                            Solid(-5.85f, 3f, -5.7f, 4.65f, 22f, 4.9f),
                            Solid(-2.45f, 22f, -5f, 4.65f, 24f, 3f) };
                }
            }

            float halfWidth = envelope.x * .5f;
            float halfDepth = envelope.z * .5f;
            float front = halfDepth - (district == CityDistrictKind.Residential ? 1.2f : 0f);
            float bodyTop = envelope.y - (district == CityDistrictKind.OldTown ? 2.4f :
                district == CityDistrictKind.Industrial ? .75f : .35f);
            if (variantIndex == 3)
            {
                float clearance = GetExpectedPassageBounds(district, variantIndex).size.y;
                return new[] { Solid(-halfWidth, 0f, -halfDepth, -1.8f, clearance, front),
                    Solid(1.8f, 0f, -halfDepth, halfWidth, clearance, front),
                    Solid(-halfWidth, clearance, -halfDepth, halfWidth, bodyTop, front) };
            }
            return variantIndex == 2
                ? new[] { Solid(-halfWidth, 0f, 0f, halfWidth, bodyTop, front),
                    Solid(-halfWidth, 0f, -halfDepth, -halfWidth + 5f, bodyTop, 0f) }
                : new[] { Solid(-halfWidth, 0f, -halfDepth, halfWidth, bodyTop, front) };
        }

        public static Bounds GetExpectedPassageBounds(CityDistrictKind district, int variantIndex)
        {
            Vector3 envelope = GetExpectedEnvelope(district, variantIndex);
            if (variantIndex != 3) return new Bounds();
            float clearance = district == CityDistrictKind.Industrial ? 4.2f : 3.2f;
            float frontRecess = district == CityDistrictKind.Residential ? 1.2f : 0f;
            return new Bounds(new Vector3(0f, clearance * .5f, -frontRecess * .5f),
                new Vector3(3.6f, clearance, envelope.z - frontRecess));
        }

        public static IReadOnlyList<Bounds> GetExpectedGroundCollisionBounds(
            CityDistrictKind district, int variantIndex)
        {
            IReadOnlyList<Bounds> solids = GetExpectedCollisionBounds(district, variantIndex);
            if (variantIndex != 3) return solids;
            var ground = new List<Bounds>(2);
            for (int index = 0; index < solids.Count; index++)
            {
                if (solids[index].min.y < 2.5f) ground.Add(solids[index]);
            }
            return ground;
        }

        private static Bounds Solid(float minX, float minY, float minZ,
            float maxX, float maxY, float maxZ)
        {
            var minimum = new Vector3(minX, minY, minZ);
            var maximum = new Vector3(maxX, maxY, maxZ);
            return new Bounds((minimum + maximum) * .5f, maximum - minimum);
        }

        private static int GetPrototypeIndex(CityDistrictKind district, int variantIndex)
        {
            if (variantIndex < 0 || variantIndex >= GetVariantCount(district))
            {
                throw new ArgumentOutOfRangeException(nameof(variantIndex));
            }

            for (int index = 0; index < 4; index++)
            {
                if (ExpectedPrototypes[index].District == district)
                {
                    return index + variantIndex * 4;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(district));
        }

        public static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isHex = character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f';
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetPrefab(
            CityDistrictKind district,
            out GameObject prefab)
        {
            for (int index = 0; index < entries.Length; index++)
            {
                CityBuildingPrefabEntry entry = entries[index];
                if (entry != null && entry.District == district &&
                    entry.Prefab != null)
                {
                    prefab = entry.Prefab;
                    return true;
                }
            }

            prefab = null;
            return false;
        }

        public GameObject GetPrefabOrThrow(CityDistrictKind district)
        {
            if (!TryGetPrefab(district, out GameObject prefab))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(district),
                    district,
                    "No City building prototype is bound for this district.");
            }

            return prefab;
        }

        public bool TryGetPrefab(CityDistrictKind district, int variantIndex, out GameObject prefab)
        {
            string expectedId = GetExpectedStableId(GetPrototypeIndex(district, variantIndex));
            for (int index = 0; index < entries.Length; index++)
            {
                CityBuildingPrefabEntry entry = entries[index];
                if (entry != null && entry.District == district && entry.Prefab != null &&
                    string.Equals(entry.StableId, expectedId, StringComparison.Ordinal))
                {
                    prefab = entry.Prefab;
                    return true;
                }
            }

            prefab = null;
            return false;
        }

        public GameObject GetPrefabOrThrow(CityDistrictKind district, int variantIndex)
        {
            if (!TryGetPrefab(district, variantIndex, out GameObject prefab))
            {
                throw new InvalidOperationException($"Missing {district} massing variant {variantIndex}.");
            }

            return prefab;
        }

        public void Configure(
            CityBuildingPrefabEntry[] configuredEntries,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            entries = configuredEntries ??
                Array.Empty<CityBuildingPrefabEntry>();
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        public void ValidateOrThrow()
        {
            if (!string.Equals(
                    designId,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !IsSha256(buildSignature) ||
                entries == null ||
                entries.Length != ExpectedPrototypeCount)
            {
                throw new InvalidOperationException(
                    "The City building provider source contract is stale.");
            }

            var seenPrefabs = new HashSet<GameObject>();
            for (int index = 0; index < ExpectedPrototypeCount; index++)
            {
                PrototypeSpec expected = ExpectedPrototypes[index];
                CityBuildingPrefabEntry entry = entries[index];
                if (entry == null || entry.Prefab == null ||
                    !string.Equals(
                        entry.StableId,
                        expected.StableId,
                        StringComparison.Ordinal) ||
                    entry.District != expected.District ||
                    !seenPrefabs.Add(entry.Prefab))
                {
                    throw new InvalidOperationException(
                        $"City building provider entry {index} drifted.");
                }

                CityBuildingAssetRegistry registry =
                    entry.Prefab.GetComponent<CityBuildingAssetRegistry>();
                Bounds expectedRoof = CityBuildingPrototypePlacement
                    .GetExpectedRoofAttachmentBounds(expected.District, index / 4);
                if (registry == null ||
                    !string.Equals(
                        registry.StableId,
                        entry.StableId,
                        StringComparison.Ordinal) ||
                    registry.District != entry.District ||
                    !string.Equals(
                        registry.DesignId,
                        designId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        registry.BuildSignature,
                        buildSignature,
                        StringComparison.Ordinal) ||
                    (Vector3.Distance(
                        registry.RoofAttachmentBounds.center,
                        expectedRoof.center) > 0.003f ||
                    Vector3.Distance(
                        registry.RoofAttachmentBounds.size,
                        expectedRoof.size) > 0.003f))
                {
                    throw new InvalidOperationException(
                        $"City building prefab '{entry.StableId}' is stale.");
                }

                registry.ValidateOrThrow();
                IReadOnlyList<Bounds> collision = GetExpectedCollisionBounds(expected.District, index / 4);
                if (registry.ColliderBounds.Count != collision.Count)
                {
                    throw new InvalidOperationException($"City building '{entry.StableId}' collision count drifted.");
                }

                for (int solid = 0; solid < collision.Count; solid++)
                {
                    if (Vector3.Distance(registry.ColliderBounds[solid].center, collision[solid].center) > .003f ||
                        Vector3.Distance(registry.ColliderBounds[solid].size, collision[solid].size) > .003f)
                    {
                        throw new InvalidOperationException($"City building '{entry.StableId}' collision geometry drifted.");
                    }
                }
            }
        }

        private static PrototypeSpec GetExpectedPrototype(int index)
        {
            if (index < 0 || index >= ExpectedPrototypes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ExpectedPrototypes[index];
        }

        private readonly struct PrototypeSpec
        {
            public PrototypeSpec(
                string stableId,
                CityDistrictKind district,
                float frontageWidth,
                float depth,
                float height)
            {
                StableId = stableId;
                District = district;
                FrontageWidth = frontageWidth;
                Depth = depth;
                Height = height;
            }

            public string StableId { get; }
            public CityDistrictKind District { get; }
            public float FrontageWidth { get; }
            public float Depth { get; }
            public float Height { get; }
        }
    }
}
