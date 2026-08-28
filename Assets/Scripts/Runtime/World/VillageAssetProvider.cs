using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Blender-authored village assembly families.
    ///
    /// Values are explicit and stable: a serialized provider names them by
    /// number, so appending a family must never renumber an existing one.
    /// </summary>
    public enum VillageAssetKind
    {
        House = 0,
        Chapel = 1,
        MineCart = 2,
        AditFrame = 3,
        GraveMarker = 4,
        Firewood = 5
    }

    /// <summary>
    /// What a mesh is for inside its assembly. The role, not the mesh name, is
    /// what the world builder asks by - a rename in the generator must not be
    /// a rename in the scene code.
    /// </summary>
    public enum VillageMeshRole
    {
        Walls = 0,
        Roof = 1,
        Plinth = 2,
        Chimney = 3,
        Body = 4,
        Wheels = 5,
        Timber = 6,
        Rubble = 7,
        Stone = 8,
        Wood = 9
    }

    /// <summary>
    /// One row of the provider's flat table.
    ///
    /// Flat, rather than one serialized field per authored part, because that
    /// is what keeps a later wave additive: a new family appends rows instead
    /// of forcing a new field and a re-serialization of the asset.
    /// </summary>
    [Serializable]
    public sealed class VillageMeshEntry
    {
        [SerializeField] private VillageAssetKind kind;
        [SerializeField] private int variant;
        [SerializeField] private VillageMeshRole role;
        [SerializeField] private Mesh mesh;

        public VillageAssetKind Kind => kind;
        public int Variant => variant;
        public VillageMeshRole Role => role;
        public Mesh Mesh => mesh;
    }

    public readonly struct VillageMeshPart
    {
        internal VillageMeshPart(
            VillageAssetKind kind,
            int variant,
            VillageMeshRole role,
            MountainRoadSurfaceKind surface,
            Mesh mesh)
        {
            Kind = kind;
            Variant = variant;
            Role = role;
            Surface = surface;
            Mesh = mesh;
        }

        public VillageAssetKind Kind { get; }
        public int Variant { get; }
        public VillageMeshRole Role { get; }
        public Mesh Mesh { get; }

        /// <summary>
        /// Internal because the surface enum is: the village raises no new
        /// material family, it wears the mountain's, and art bible 10g says
        /// so in as many words.
        /// </summary>
        internal MountainRoadSurfaceKind Surface { get; }
    }

    /// <summary>
    /// Geometry only.
    ///
    /// The provider carries meshes and nothing else. Material, tint,
    /// placement, light and collision stay with the world builder and the
    /// plan, which is the same division the City and Mountain Road kits draw
    /// - and the reason an imported model can never quietly acquire a
    /// collider that belongs to gameplay.
    /// </summary>
    public sealed class VillageAssetProvider : ScriptableObject
    {
        public const string ResourcePath = "Village/VillageAssetProvider";
        public const string GeneratorVersion = "1.0.0";
        public const string DesignId = "village_wave1_v1";
        public const int ExpectedAssemblyCount = 11;
        public const int ExpectedMeshCount = 27;

        public const int HouseVariantCount = 4;
        public const int GraveMarkerVariantCount = 3;

        [SerializeField] private string designId = DesignId;
        [SerializeField] private string buildSignature = string.Empty;
        [SerializeField] private VillageMeshEntry[] entries =
            Array.Empty<VillageMeshEntry>();

        public string DesignIdentifier => designId;
        public string BuildSignature => buildSignature;
        public int EntryCount => entries?.Length ?? 0;

        public bool HasCompleteMeshes
        {
            get
            {
                if (entries == null || entries.Length != ExpectedMeshCount)
                {
                    return false;
                }

                for (int index = 0; index < entries.Length; index++)
                {
                    if (entries[index] == null || entries[index].Mesh == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public static VillageAssetProvider Load()
        {
            return Resources.Load<VillageAssetProvider>(ResourcePath);
        }

        public static VillageAssetProvider LoadOrThrow()
        {
            VillageAssetProvider provider = Load();
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Missing village provider at '{ResourcePath}'.");
            }

            provider.ValidateOrThrow();
            return provider;
        }

        public void ValidateOrThrow()
        {
            if (!string.Equals(designId, DesignId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Village provider design is '{designId}', expected " +
                    $"'{DesignId}'.");
            }

            if (!HasCompleteMeshes)
            {
                throw new InvalidOperationException(
                    "The village provider is missing authored meshes; run " +
                    "Bar Promenade/Village/Bind Provider.");
            }
        }

        // ----------------------------------------------------------------
        // The catalog. The editor binder derives what it expects to import
        // from THIS, never from a second list of its own, so the C# side and
        // the generator's `make_assemblies()` cannot drift apart in silence.
        // ----------------------------------------------------------------

        public static int SupportedKindCount => 6;

        public static VillageAssetKind GetSupportedKind(int index)
        {
            switch (index)
            {
                case 0: return VillageAssetKind.House;
                case 1: return VillageAssetKind.Chapel;
                case 2: return VillageAssetKind.MineCart;
                case 3: return VillageAssetKind.AditFrame;
                case 4: return VillageAssetKind.GraveMarker;
                case 5: return VillageAssetKind.Firewood;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static int GetVariantCount(VillageAssetKind kind)
        {
            switch (kind)
            {
                case VillageAssetKind.House:
                    return HouseVariantCount;
                case VillageAssetKind.GraveMarker:
                    return GraveMarkerVariantCount;
                default:
                    return 1;
            }
        }

        public static VillageMeshRole[] GetRoles(VillageAssetKind kind)
        {
            switch (kind)
            {
                case VillageAssetKind.House:
                    return new[]
                    {
                        VillageMeshRole.Walls,
                        VillageMeshRole.Roof,
                        VillageMeshRole.Plinth,
                        VillageMeshRole.Chimney
                    };
                case VillageAssetKind.Chapel:
                    return new[]
                    {
                        VillageMeshRole.Walls,
                        VillageMeshRole.Roof,
                        VillageMeshRole.Plinth
                    };
                case VillageAssetKind.MineCart:
                    return new[]
                    {
                        VillageMeshRole.Body,
                        VillageMeshRole.Wheels
                    };
                case VillageAssetKind.AditFrame:
                    return new[]
                    {
                        VillageMeshRole.Timber,
                        VillageMeshRole.Rubble
                    };
                case VillageAssetKind.GraveMarker:
                    return new[] { VillageMeshRole.Stone };
                case VillageAssetKind.Firewood:
                    return new[] { VillageMeshRole.Wood };
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal static MountainRoadSurfaceKind GetExpectedSurface(
            VillageAssetKind kind,
            VillageMeshRole role)
        {
            switch (role)
            {
                case VillageMeshRole.Walls:
                    return kind == VillageAssetKind.Chapel
                        ? MountainRoadSurfaceKind.Masonry
                        : MountainRoadSurfaceKind.Timber;
                case VillageMeshRole.Roof:
                case VillageMeshRole.Timber:
                    return MountainRoadSurfaceKind.Timber;
                case VillageMeshRole.Plinth:
                case VillageMeshRole.Rubble:
                case VillageMeshRole.Stone:
                    return MountainRoadSurfaceKind.LayeredStone;
                case VillageMeshRole.Chimney:
                    return MountainRoadSurfaceKind.Masonry;
                case VillageMeshRole.Body:
                case VillageMeshRole.Wheels:
                    return MountainRoadSurfaceKind.RustedIron;
                case VillageMeshRole.Wood:
                    return MountainRoadSurfaceKind.BarkAndDeadwood;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        /// <summary>
        /// The generator's own naming, rebuilt from the catalog. This is the
        /// single string that binds the two halves of the pipeline.
        /// </summary>
        public static string GetExpectedMeshName(
            VillageAssetKind kind,
            int variant,
            VillageMeshRole role)
        {
            return GetVariantCount(kind) > 1
                ? $"GEO_VIL_{kind}_Variant{variant:00}_{role}"
                : $"GEO_VIL_{kind}_{role}";
        }

        public static int GetExpectedMeshTotal()
        {
            int total = 0;
            for (int index = 0; index < SupportedKindCount; index++)
            {
                VillageAssetKind kind = GetSupportedKind(index);
                total += GetVariantCount(kind) * GetRoles(kind).Length;
            }

            return total;
        }

        /// <summary>
        /// Picks a variant for a plot from its stable id, so a house keeps
        /// the same shape across every rebuild of the same seed.
        /// </summary>
        public static int SelectVariant(
            VillageAssetKind kind,
            string stableId)
        {
            int count = GetVariantCount(kind);
            if (count <= 1 || string.IsNullOrEmpty(stableId))
            {
                return 0;
            }

            uint hash = 2166136261u;
            for (int index = 0; index < stableId.Length; index++)
            {
                unchecked
                {
                    hash = (hash ^ stableId[index]) * 16777619u;
                }
            }

            return (int)(hash % (uint)count);
        }

        public bool TryGetPart(
            VillageAssetKind kind,
            int variant,
            VillageMeshRole role,
            out VillageMeshPart part)
        {
            part = default;
            if (entries == null)
            {
                return false;
            }

            for (int index = 0; index < entries.Length; index++)
            {
                VillageMeshEntry entry = entries[index];
                if (entry == null ||
                    entry.Kind != kind ||
                    entry.Variant != variant ||
                    entry.Role != role ||
                    entry.Mesh == null)
                {
                    continue;
                }

                part = new VillageMeshPart(
                    kind,
                    variant,
                    role,
                    GetExpectedSurface(kind, role),
                    entry.Mesh);
                return true;
            }

            return false;
        }

        public VillageMeshPart GetPartOrThrow(
            VillageAssetKind kind,
            int variant,
            VillageMeshRole role)
        {
            if (!TryGetPart(kind, variant, role, out VillageMeshPart part))
            {
                throw new InvalidOperationException(
                    "The village provider has no mesh for " +
                    $"{GetExpectedMeshName(kind, variant, role)}.");
            }

            return part;
        }
    }
}
