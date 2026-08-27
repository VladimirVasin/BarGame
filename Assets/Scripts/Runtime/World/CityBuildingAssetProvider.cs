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
    /// Serialized Resources bridge to four passive wrapper prefabs. Runtime
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
            "city_buildings_prototypes_v1";
        public const int ExpectedPrototypeCount = 4;

        private static readonly PrototypeSpec[] ExpectedPrototypes =
        {
            new PrototypeSpec(
                "old-town-prototype-01",
                CityDistrictKind.OldTown,
                14f,
                13.5f,
                42f),
            new PrototypeSpec(
                "residential-prototype-01",
                CityDistrictKind.Residential,
                11.5f,
                11.5f,
                40f),
            new PrototypeSpec(
                "industrial-prototype-01",
                CityDistrictKind.Industrial,
                14f,
                13.5f,
                36f),
            new PrototypeSpec(
                "nightlife-prototype-01",
                CityDistrictKind.Nightlife,
                12.5f,
                12f,
                48f)
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
                    .GetExpectedRoofAttachmentBounds(expected.District);
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
                    Vector3.Distance(
                        registry.RoofAttachmentBounds.center,
                        expectedRoof.center) > 0.003f ||
                    Vector3.Distance(
                        registry.RoofAttachmentBounds.size,
                        expectedRoof.size) > 0.003f)
                {
                    throw new InvalidOperationException(
                        $"City building prefab '{entry.StableId}' is stale.");
                }

                registry.ValidateOrThrow();
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
