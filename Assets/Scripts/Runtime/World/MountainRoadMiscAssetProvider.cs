using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Semantic material/tint role of one imported mountain-road misc mesh.
    /// The provider carries geometry only; the world builder continues to own
    /// the shared material, surface recipe, tint, placement and collision.
    /// </summary>
    public enum MountainRoadMiscMeshRole
    {
        Wood = 0,
        GuardRailIron = 1,
        SnowPoleBody = 2,
        SnowPoleBand = 3,
        ConvexMirrorPole = 4,
        ConvexMirrorFrame = 5,
        ConvexMirrorFace = 6,
        UtilityCabinetBody = 7,
        UtilityCabinetTrim = 8
    }

    public readonly struct MountainRoadMiscMeshPart
    {
        internal MountainRoadMiscMeshPart(
            MountainRoadMiscMeshRole role,
            Mesh mesh)
        {
            Role = role;
            Mesh = mesh;
        }

        public MountainRoadMiscMeshRole Role { get; }
        public Mesh Mesh { get; }
    }

    /// <summary>
    /// The single Resources bridge from the deterministic Blender misc kit to
    /// the runtime-composed Mountain Road. Meshes stay passive and normalized:
    /// descriptors remain authoritative for transform, scale and collision.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MountainRoadMiscAssetProvider",
        menuName = "Bar Promenade/Mountain Road Misc Asset Provider")]
    public sealed class MountainRoadMiscAssetProvider : ScriptableObject
    {
        public const string ResourcePath =
            "MountainRoad/MountainRoadMiscAssetProvider";
        public const string DesignId = "mountain_road_misc_wave1_v1";
        public const int ExpectedMeshCount = 19;
        public const int FallenLogVariantCount = 3;
        public const int StumpVariantCount = 4;
        public const int DeadTreeVariantCount = 3;

        private const float NormalizedBoundsLimit = 0.501f;

        [SerializeField] private Mesh snowPoleBody;
        [SerializeField] private Mesh snowPoleBand;
        [SerializeField] private Mesh[] fallenLogVariants =
            new Mesh[FallenLogVariantCount];
        [SerializeField] private Mesh[] stumpVariants =
            new Mesh[StumpVariantCount];
        [SerializeField] private Mesh[] deadTreeVariants =
            new Mesh[DeadTreeVariantCount];
        [SerializeField] private Mesh guardRailIron;
        [SerializeField] private Mesh convexMirrorPole;
        [SerializeField] private Mesh convexMirrorFrame;
        [SerializeField] private Mesh convexMirrorFace;
        [SerializeField] private Mesh utilityCabinetBody;
        [SerializeField] private Mesh utilityCabinetTrim;
        [SerializeField] private Mesh abandonedChairWood;
        [SerializeField] private string buildSignature = string.Empty;

        public string BuildSignature => buildSignature;

        public bool HasCompleteMeshes =>
            HasExactCompleteArray(
                fallenLogVariants,
                FallenLogVariantCount) &&
            HasExactCompleteArray(stumpVariants, StumpVariantCount) &&
            HasExactCompleteArray(deadTreeVariants, DeadTreeVariantCount) &&
            snowPoleBody != null &&
            snowPoleBand != null &&
            guardRailIron != null &&
            convexMirrorPole != null &&
            convexMirrorFrame != null &&
            convexMirrorFace != null &&
            utilityCabinetBody != null &&
            utilityCabinetTrim != null &&
            abandonedChairWood != null;

        public static MountainRoadMiscAssetProvider Load()
        {
            return Resources.Load<MountainRoadMiscAssetProvider>(ResourcePath);
        }

        public static MountainRoadMiscAssetProvider LoadOrThrow()
        {
            MountainRoadMiscAssetProvider provider = Load();
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Missing Mountain Road misc provider at Resources/" +
                    $"{ResourcePath}.");
            }

            provider.ValidateOrThrow();
            return provider;
        }

        public void ValidateOrThrow()
        {
            if (!HasCompleteMeshes)
            {
                throw new InvalidOperationException(
                    "The Mountain Road misc provider is incomplete. All " +
                    $"{ExpectedMeshCount} imported meshes are required.");
            }

            if (string.IsNullOrWhiteSpace(buildSignature))
            {
                throw new InvalidOperationException(
                    "The Mountain Road misc provider has no build signature.");
            }

            foreach (MountainRoadMiscKind kind in MigratedKinds)
            {
                int variants = GetVariantCount(kind);
                int parts = GetPartCount(kind);
                for (int variant = 0; variant < variants; variant++)
                {
                    for (int part = 0; part < parts; part++)
                    {
                        MountainRoadMiscMeshPart binding = GetPart(
                            kind,
                            variant,
                            part);
                        ValidateMesh(
                            binding.Mesh,
                            GetExpectedMeshName(kind, variant, part));
                    }
                }
            }
        }

        public static bool Supports(MountainRoadMiscKind kind)
        {
            switch (kind)
            {
                case MountainRoadMiscKind.FallenLog:
                case MountainRoadMiscKind.Stump:
                case MountainRoadMiscKind.DeadTree:
                case MountainRoadMiscKind.GuardRail:
                case MountainRoadMiscKind.ConvexMirror:
                case MountainRoadMiscKind.UtilityCabinet:
                case MountainRoadMiscKind.SnowPole:
                case MountainRoadMiscKind.AbandonedChair:
                    return true;
                default:
                    return false;
            }
        }

        public int GetVariantCount(MountainRoadMiscKind kind)
        {
            switch (kind)
            {
                case MountainRoadMiscKind.FallenLog:
                    return FallenLogVariantCount;
                case MountainRoadMiscKind.Stump:
                    return StumpVariantCount;
                case MountainRoadMiscKind.DeadTree:
                    return DeadTreeVariantCount;
                case MountainRoadMiscKind.GuardRail:
                case MountainRoadMiscKind.ConvexMirror:
                case MountainRoadMiscKind.UtilityCabinet:
                case MountainRoadMiscKind.SnowPole:
                case MountainRoadMiscKind.AbandonedChair:
                    return 1;
                default:
                    throw UnsupportedKind(kind);
            }
        }

        public int GetPartCount(MountainRoadMiscKind kind)
        {
            switch (kind)
            {
                case MountainRoadMiscKind.SnowPole:
                case MountainRoadMiscKind.UtilityCabinet:
                    return 2;
                case MountainRoadMiscKind.ConvexMirror:
                    return 3;
                case MountainRoadMiscKind.FallenLog:
                case MountainRoadMiscKind.Stump:
                case MountainRoadMiscKind.DeadTree:
                case MountainRoadMiscKind.GuardRail:
                case MountainRoadMiscKind.AbandonedChair:
                    return 1;
                default:
                    throw UnsupportedKind(kind);
            }
        }

        public MountainRoadMiscMeshPart GetPartOrThrow(
            MountainRoadMiscKind kind,
            string stableId,
            int partIndex)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A stable misc ID is required to select a mesh variant.",
                    nameof(stableId));
            }

            int variant = SelectVariant(stableId, GetVariantCount(kind));
            MountainRoadMiscMeshPart part = GetPart(kind, variant, partIndex);
            if (part.Mesh == null)
            {
                throw new InvalidOperationException(
                    $"The Mountain Road misc provider has no mesh for " +
                    $"{kind}, variant {variant}, part {partIndex}.");
            }

            return part;
        }

        public static string GetExpectedMeshName(
            MountainRoadMiscKind kind,
            int variantIndex,
            int partIndex)
        {
            switch (kind)
            {
                case MountainRoadMiscKind.FallenLog:
                    RequireIndex(variantIndex, FallenLogVariantCount, "variant");
                    RequireIndex(partIndex, 1, "part");
                    return $"GEO_MRM_FallenLog_Variant{variantIndex + 1:00}_Wood";
                case MountainRoadMiscKind.Stump:
                    RequireIndex(variantIndex, StumpVariantCount, "variant");
                    RequireIndex(partIndex, 1, "part");
                    return $"GEO_MRM_Stump_Variant{variantIndex + 1:00}_Wood";
                case MountainRoadMiscKind.DeadTree:
                    RequireIndex(variantIndex, DeadTreeVariantCount, "variant");
                    RequireIndex(partIndex, 1, "part");
                    return $"GEO_MRM_DeadTree_Variant{variantIndex + 1:00}_Wood";
                case MountainRoadMiscKind.GuardRail:
                    RequireSingleVariantPart(variantIndex, partIndex);
                    return "GEO_MRM_GuardRail_Iron";
                case MountainRoadMiscKind.SnowPole:
                    RequireIndex(variantIndex, 1, "variant");
                    RequireIndex(partIndex, 2, "part");
                    return partIndex == 0
                        ? "GEO_MRM_SnowPole_Body"
                        : "GEO_MRM_SnowPole_Band";
                case MountainRoadMiscKind.ConvexMirror:
                    RequireIndex(variantIndex, 1, "variant");
                    RequireIndex(partIndex, 3, "part");
                    return partIndex == 0
                        ? "GEO_MRM_ConvexMirror_Pole"
                        : partIndex == 1
                            ? "GEO_MRM_ConvexMirror_Frame"
                            : "GEO_MRM_ConvexMirror_Face";
                case MountainRoadMiscKind.UtilityCabinet:
                    RequireIndex(variantIndex, 1, "variant");
                    RequireIndex(partIndex, 2, "part");
                    return partIndex == 0
                        ? "GEO_MRM_UtilityCabinet_Body"
                        : "GEO_MRM_UtilityCabinet_Trim";
                case MountainRoadMiscKind.AbandonedChair:
                    RequireSingleVariantPart(variantIndex, partIndex);
                    return "GEO_MRM_AbandonedChair_Wood";
                default:
                    throw UnsupportedKind(kind);
            }
        }

        private static readonly MountainRoadMiscKind[] MigratedKinds =
        {
            MountainRoadMiscKind.FallenLog,
            MountainRoadMiscKind.Stump,
            MountainRoadMiscKind.DeadTree,
            MountainRoadMiscKind.GuardRail,
            MountainRoadMiscKind.SnowPole,
            MountainRoadMiscKind.ConvexMirror,
            MountainRoadMiscKind.UtilityCabinet,
            MountainRoadMiscKind.AbandonedChair
        };

        private MountainRoadMiscMeshPart GetPart(
            MountainRoadMiscKind kind,
            int variantIndex,
            int partIndex)
        {
            RequireIndex(variantIndex, GetVariantCount(kind), "variant");
            RequireIndex(partIndex, GetPartCount(kind), "part");
            switch (kind)
            {
                case MountainRoadMiscKind.FallenLog:
                    return Wood(fallenLogVariants[variantIndex]);
                case MountainRoadMiscKind.Stump:
                    return Wood(stumpVariants[variantIndex]);
                case MountainRoadMiscKind.DeadTree:
                    return Wood(deadTreeVariants[variantIndex]);
                case MountainRoadMiscKind.GuardRail:
                    return new MountainRoadMiscMeshPart(
                        MountainRoadMiscMeshRole.GuardRailIron,
                        guardRailIron);
                case MountainRoadMiscKind.SnowPole:
                    return new MountainRoadMiscMeshPart(
                        partIndex == 0
                            ? MountainRoadMiscMeshRole.SnowPoleBody
                            : MountainRoadMiscMeshRole.SnowPoleBand,
                        partIndex == 0 ? snowPoleBody : snowPoleBand);
                case MountainRoadMiscKind.ConvexMirror:
                    if (partIndex == 0)
                    {
                        return new MountainRoadMiscMeshPart(
                            MountainRoadMiscMeshRole.ConvexMirrorPole,
                            convexMirrorPole);
                    }

                    return new MountainRoadMiscMeshPart(
                        partIndex == 1
                            ? MountainRoadMiscMeshRole.ConvexMirrorFrame
                            : MountainRoadMiscMeshRole.ConvexMirrorFace,
                        partIndex == 1 ? convexMirrorFrame : convexMirrorFace);
                case MountainRoadMiscKind.UtilityCabinet:
                    return new MountainRoadMiscMeshPart(
                        partIndex == 0
                            ? MountainRoadMiscMeshRole.UtilityCabinetBody
                            : MountainRoadMiscMeshRole.UtilityCabinetTrim,
                        partIndex == 0
                            ? utilityCabinetBody
                            : utilityCabinetTrim);
                case MountainRoadMiscKind.AbandonedChair:
                    return Wood(abandonedChairWood);
                default:
                    throw UnsupportedKind(kind);
            }
        }

        private static MountainRoadMiscMeshPart Wood(Mesh mesh)
        {
            return new MountainRoadMiscMeshPart(
                MountainRoadMiscMeshRole.Wood,
                mesh);
        }

        private static int SelectVariant(string stableId, int variantCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int index = 0; index < stableId.Length; index++)
                {
                    hash ^= stableId[index];
                    hash *= 16777619u;
                }

                return (int)(hash % (uint)variantCount);
            }
        }

        private static bool HasExactCompleteArray(Mesh[] meshes, int count)
        {
            if (meshes == null || meshes.Length != count)
            {
                return false;
            }

            for (int index = 0; index < meshes.Length; index++)
            {
                if (meshes[index] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateMesh(Mesh mesh, string expectedName)
        {
            if (mesh == null ||
                mesh.vertexCount == 0 ||
                !mesh.isReadable ||
                !string.Equals(mesh.name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid Mountain Road misc mesh '{expectedName}'. " +
                    "It must be readable, non-empty and keep its authored name.");
            }

            Bounds bounds = mesh.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            if (!IsFinite(min) ||
                !IsFinite(max) ||
                min.x < -NormalizedBoundsLimit ||
                min.y < -NormalizedBoundsLimit ||
                min.z < -NormalizedBoundsLimit ||
                max.x > NormalizedBoundsLimit ||
                max.y > NormalizedBoundsLimit ||
                max.z > NormalizedBoundsLimit)
            {
                throw new InvalidOperationException(
                    $"Mountain Road misc mesh '{expectedName}' exceeds its " +
                    "normalized [-0.5, 0.5] assembly envelope.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void RequireSingleVariantPart(
            int variantIndex,
            int partIndex)
        {
            RequireIndex(variantIndex, 1, "variant");
            RequireIndex(partIndex, 1, "part");
        }

        private static void RequireIndex(int index, int count, string name)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    index,
                    $"Expected 0..{count - 1}.");
            }
        }

        private static ArgumentOutOfRangeException UnsupportedKind(
            MountainRoadMiscKind kind)
        {
            return new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "This misc kind is not part of the imported first pass.");
        }
    }
}
