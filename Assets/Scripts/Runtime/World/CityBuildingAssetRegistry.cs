using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBuildingMeshRole
    {
        FacadePrimary = 0,
        FacadeSecondary = 1,
        Plinth = 2,
        Roof = 3,
        Metal = 4,
        WindowFrame = 5,
        WindowGlass = 6
    }

    public enum CityBuildingOpeningKind
    {
        Window = 0,
        BalconyDoor = 1
    }

    [Serializable]
    public sealed class CityBuildingPartBinding
    {
        [SerializeField] private string sourceName = string.Empty;
        [SerializeField] private CityBuildingMeshRole role;
        [SerializeField] private string surfaceKind = string.Empty;
        [SerializeField] private string uvScheme = string.Empty;
        [SerializeField] private float metersPerTile;
        [SerializeField] private Renderer renderer;

        public CityBuildingPartBinding(
            string configuredSourceName,
            CityBuildingMeshRole configuredRole,
            string configuredSurfaceKind,
            string configuredUvScheme,
            float configuredMetersPerTile,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole;
            surfaceKind = configuredSurfaceKind ?? string.Empty;
            uvScheme = configuredUvScheme ?? string.Empty;
            metersPerTile = configuredMetersPerTile;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public CityBuildingMeshRole Role => role;
        public string SurfaceKind => surfaceKind;
        public string UvScheme => uvScheme;
        public float MetersPerTile => metersPerTile;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public sealed class CityBuildingFacadeAttachment
    {
        [SerializeField] private string side = string.Empty;
        [SerializeField] private Bounds localBounds;

        public CityBuildingFacadeAttachment(
            string configuredSide,
            Bounds configuredLocalBounds)
        {
            side = configuredSide ?? string.Empty;
            localBounds = configuredLocalBounds;
        }

        public string Side => side;
        public Bounds LocalBounds => localBounds;
    }

    [Serializable]
    public sealed class CityBuildingWindowSlot
    {
        [SerializeField] private int slotId;
        [SerializeField] private string side = string.Empty;
        [SerializeField] private int floor;
        [SerializeField] private int bay;
        [SerializeField] private CityBuildingOpeningKind openingKind;
        [SerializeField] private Vector3 localCenter;
        [SerializeField] private Vector2 sizeMeters;
        [SerializeField] private int uv2SlotId;

        public CityBuildingWindowSlot(
            int configuredSlotId,
            string configuredSide,
            int configuredFloor,
            int configuredBay,
            CityBuildingOpeningKind configuredOpeningKind,
            Vector3 configuredLocalCenter,
            Vector2 configuredSizeMeters,
            int configuredUv2SlotId)
        {
            slotId = configuredSlotId;
            side = configuredSide ?? string.Empty;
            floor = configuredFloor;
            bay = configuredBay;
            openingKind = configuredOpeningKind;
            localCenter = configuredLocalCenter;
            sizeMeters = configuredSizeMeters;
            uv2SlotId = configuredUv2SlotId;
        }

        public int SlotId => slotId;
        public string Side => side;
        public int Floor => floor;
        public int Bay => bay;
        public CityBuildingOpeningKind OpeningKind => openingKind;
        public Vector3 LocalCenter => localCenter;
        public Vector2 SizeMeters => sizeMeters;
        public int Uv2SlotId => uv2SlotId;
    }

    [Serializable]
    public sealed class CityBuildingBalconySlot
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private int floor;
        [SerializeField] private string side = string.Empty;
        [SerializeField] private int doorSlotId;
        [SerializeField] private Bounds localDeckBounds;
        [SerializeField] private Vector3 localNpcDock;
        [SerializeField] private Vector3 localOutward;

        public CityBuildingBalconySlot(
            string configuredStableId,
            int configuredFloor,
            string configuredSide,
            int configuredDoorSlotId,
            Bounds configuredLocalDeckBounds,
            Vector3 configuredLocalNpcDock,
            Vector3 configuredLocalOutward)
        {
            stableId = configuredStableId ?? string.Empty;
            floor = configuredFloor;
            side = configuredSide ?? string.Empty;
            doorSlotId = configuredDoorSlotId;
            localDeckBounds = configuredLocalDeckBounds;
            localNpcDock = configuredLocalNpcDock;
            localOutward = configuredLocalOutward;
        }

        public string StableId => stableId;
        public int Floor => floor;
        public string Side => side;
        public int DoorSlotId => doorSlotId;
        public Bounds LocalDeckBounds => localDeckBounds;
        public Vector3 LocalNpcDock => localNpcDock;
        public Vector3 LocalOutward => localOutward;
    }

    /// <summary>
    /// Semantic metadata attached to one passive building-prototype wrapper.
    /// Geometry remains authored in Blender; runtime placement fits the
    /// wrapper to an ordinary City lot through its stable front anchor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBuildingAssetRegistry : MonoBehaviour
    {
        public const int ExpectedRoleCount = 7;
        public const int MaximumTriangleCount = 3500;
        public const int MaximumWindowSlotId = 63;
        public const int WindowSlotUv2Divisor = 256;

        private static readonly CityBuildingMeshRole[] ExpectedRoles =
        {
            CityBuildingMeshRole.FacadePrimary,
            CityBuildingMeshRole.FacadeSecondary,
            CityBuildingMeshRole.Plinth,
            CityBuildingMeshRole.Roof,
            CityBuildingMeshRole.Metal,
            CityBuildingMeshRole.WindowFrame,
            CityBuildingMeshRole.WindowGlass
        };

        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private CityDistrictKind district;
        [SerializeField] private string grammar = string.Empty;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform frontAnchor;
        [SerializeField] private CityBuildingPartBinding[] parts =
            Array.Empty<CityBuildingPartBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private Bounds roofAttachmentBounds;
        [SerializeField] private CityBuildingFacadeAttachment[]
            facadeAttachments =
                Array.Empty<CityBuildingFacadeAttachment>();
        [SerializeField] private CityBuildingWindowSlot[] windowSlots =
            Array.Empty<CityBuildingWindowSlot>();
        [SerializeField] private CityBuildingBalconySlot[] balconySlots =
            Array.Empty<CityBuildingBalconySlot>();
        [SerializeField] private float frontageWidth;
        [SerializeField] private float depth;
        [SerializeField] private float height;
        [SerializeField] private float unitFactor;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion = string.Empty;
        [SerializeField] private string designId = string.Empty;
        [SerializeField] private string buildSignature = string.Empty;

        public string StableId => stableId;
        public CityDistrictKind District => district;
        public string Grammar => grammar;
        public Transform ModelRoot => modelRoot;
        public Transform FrontAnchor => frontAnchor;
        public IReadOnlyList<CityBuildingPartBinding> Parts => parts;
        public Bounds LocalBounds => localBounds;
        public Bounds RoofAttachmentBounds => roofAttachmentBounds;
        public IReadOnlyList<CityBuildingFacadeAttachment>
            FacadeAttachments => facadeAttachments;
        public IReadOnlyList<CityBuildingWindowSlot> WindowSlots =>
            windowSlots;
        public IReadOnlyList<CityBuildingBalconySlot> BalconySlots =>
            balconySlots;
        public float FrontageWidth => frontageWidth;
        public float Depth => depth;
        public float Height => height;
        public float UnitFactor => unitFactor;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public static CityBuildingMeshRole GetExpectedRole(int index)
        {
            if (index < 0 || index >= ExpectedRoles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ExpectedRoles[index];
        }

        public bool TryGetRenderer(
            CityBuildingMeshRole role,
            out Renderer renderer)
        {
            for (int index = 0; index < parts.Length; index++)
            {
                CityBuildingPartBinding binding = parts[index];
                if (binding != null && binding.Role == role &&
                    binding.Renderer != null)
                {
                    renderer = binding.Renderer;
                    return true;
                }
            }

            renderer = null;
            return false;
        }

        public void Configure(
            string configuredStableId,
            CityDistrictKind configuredDistrict,
            string configuredGrammar,
            Transform configuredModelRoot,
            Transform configuredFrontAnchor,
            CityBuildingPartBinding[] configuredParts,
            Bounds configuredLocalBounds,
            Bounds configuredRoofAttachmentBounds,
            CityBuildingFacadeAttachment[] configuredFacadeAttachments,
            CityBuildingWindowSlot[] configuredWindowSlots,
            CityBuildingBalconySlot[] configuredBalconySlots,
            float configuredFrontageWidth,
            float configuredDepth,
            float configuredHeight,
            float configuredUnitFactor,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            stableId = configuredStableId ?? string.Empty;
            district = configuredDistrict;
            grammar = configuredGrammar ?? string.Empty;
            modelRoot = configuredModelRoot;
            frontAnchor = configuredFrontAnchor;
            parts = configuredParts ??
                Array.Empty<CityBuildingPartBinding>();
            localBounds = configuredLocalBounds;
            roofAttachmentBounds = configuredRoofAttachmentBounds;
            facadeAttachments = configuredFacadeAttachments ??
                Array.Empty<CityBuildingFacadeAttachment>();
            windowSlots = configuredWindowSlots ??
                Array.Empty<CityBuildingWindowSlot>();
            balconySlots = configuredBalconySlots ??
                Array.Empty<CityBuildingBalconySlot>();
            frontageWidth = configuredFrontageWidth;
            depth = configuredDepth;
            height = configuredHeight;
            unitFactor = configuredUnitFactor;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(stableId) ||
                string.IsNullOrWhiteSpace(grammar) ||
                modelRoot == null ||
                frontAnchor == null ||
                frontageWidth <= 0f ||
                depth <= 0f ||
                height <= 0f ||
                Mathf.Abs(unitFactor - 1f) > 0.0001f ||
                sourceTriangleCount <= 0 ||
                sourceTriangleCount > MaximumTriangleCount ||
                string.IsNullOrWhiteSpace(sourceGeneratorVersion) ||
                !string.Equals(
                    designId,
                    CityBuildingAssetProvider.ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !CityBuildingAssetProvider.IsSha256(buildSignature))
            {
                throw new InvalidOperationException(
                    "The City building registry source contract is stale.");
            }

            if (!modelRoot.IsChildOf(transform) ||
                !frontAnchor.IsChildOf(transform) ||
                Mathf.Abs(localBounds.min.y) > 0.003f ||
                localBounds.size.x <= 0f ||
                localBounds.size.y <= 0f ||
                localBounds.size.z <= 0f ||
                localBounds.size.x < frontageWidth - 0.02f ||
                localBounds.size.x > frontageWidth + 0.16f ||
                Mathf.Abs(localBounds.size.y - height) > 0.02f ||
                localBounds.size.z < depth - 0.02f ||
                localBounds.size.z > depth + 0.16f)
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' has invalid local geometry.");
            }

            Vector3 anchorPosition = transform.InverseTransformPoint(
                frontAnchor.position);
            Vector3 anchorForward = transform.InverseTransformDirection(
                frontAnchor.forward).normalized;
            if (Mathf.Abs(anchorPosition.y) > 0.003f ||
                Vector3.Dot(anchorForward, Vector3.forward) < 0.999f ||
                Mathf.Abs(anchorPosition.z - depth * 0.5f) > 0.003f ||
                localBounds.max.z - anchorPosition.z > 0.08f)
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' front anchor drifted.");
            }

            ValidateParts();
            ValidateAttachmentMetadata();
            ValidatePassiveHierarchy();
        }

        private void ValidateParts()
        {
            if (parts == null || parts.Length != ExpectedRoleCount)
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' needs seven semantic " +
                    "surface meshes.");
            }

            var seenRoles = new HashSet<CityBuildingMeshRole>();
            var seenRenderers = new HashSet<Renderer>();
            for (int index = 0; index < parts.Length; index++)
            {
                CityBuildingPartBinding binding = parts[index];
                string expectedName = stableId + "__" +
                    GetExpectedRole(index);
                if (binding == null || binding.Renderer == null ||
                    binding.Role != GetExpectedRole(index) ||
                    !string.Equals(
                        binding.SourceName,
                        expectedName,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        binding.SurfaceKind,
                        binding.Role.ToString(),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        binding.UvScheme,
                        GetExpectedUvScheme(binding.Role),
                        StringComparison.Ordinal) ||
                    !HasExpectedUvScale(binding) ||
                    !seenRoles.Add(binding.Role) ||
                    !seenRenderers.Add(binding.Renderer) ||
                    !binding.Renderer.transform.IsChildOf(modelRoot))
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' role binding drifted.");
                }
            }
        }

        private static string GetExpectedUvScheme(
            CityBuildingMeshRole role)
        {
            switch (role)
            {
                case CityBuildingMeshRole.FacadePrimary:
                case CityBuildingMeshRole.FacadeSecondary:
                    return "building_side_atlas_0_1";
                case CityBuildingMeshRole.Plinth:
                    return "full_face_projected_0_1";
                case CityBuildingMeshRole.Roof:
                case CityBuildingMeshRole.Metal:
                case CityBuildingMeshRole.WindowFrame:
                    return "world_metre_projected";
                case CityBuildingMeshRole.WindowGlass:
                    return "per_window_face_projected_0_1";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role),
                        role,
                        "Unknown City building semantic surface role.");
            }
        }

        private static bool HasExpectedUvScale(
            CityBuildingPartBinding binding)
        {
            bool metric = string.Equals(
                binding.UvScheme,
                "world_metre_projected",
                StringComparison.Ordinal);
            return metric
                ? binding.MetersPerTile > 0f &&
                  !float.IsNaN(binding.MetersPerTile) &&
                  !float.IsInfinity(binding.MetersPerTile)
                : Mathf.Abs(binding.MetersPerTile) <= 0.0001f;
        }

        private void ValidateAttachmentMetadata()
        {
            if (roofAttachmentBounds.size.x <= 0f ||
                roofAttachmentBounds.size.z <= 0f ||
                facadeAttachments == null ||
                facadeAttachments.Length == 0 ||
                windowSlots == null ||
                windowSlots.Length == 0 ||
                balconySlots == null)
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' attachment metadata is " +
                    "incomplete.");
            }

            var facadeSides = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < facadeAttachments.Length; index++)
            {
                CityBuildingFacadeAttachment attachment =
                    facadeAttachments[index];
                if (attachment == null ||
                    string.IsNullOrWhiteSpace(attachment.Side) ||
                    !facadeSides.Add(attachment.Side))
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' facade attachment " +
                        "metadata is invalid.");
                }
            }

            var slotIds = new HashSet<int>();
            var uv2Ids = new HashSet<int>();
            var slotsById = new Dictionary<int, CityBuildingWindowSlot>();
            var declaredDoorIds = new HashSet<int>();
            for (int index = 0; index < windowSlots.Length; index++)
            {
                CityBuildingWindowSlot slot = windowSlots[index];
                if (slot == null ||
                    slot.SlotId <= 0 ||
                    !IsKnownSide(slot.Side) ||
                    slot.Floor < 0 ||
                    slot.Bay < 0 ||
                    !Enum.IsDefined(
                        typeof(CityBuildingOpeningKind),
                        slot.OpeningKind) ||
                    !IsFinite(slot.LocalCenter) ||
                    slot.SizeMeters.x <= 0f ||
                    slot.SizeMeters.y <= 0f ||
                    !IsFinite(slot.SizeMeters) ||
                    slot.Uv2SlotId <= 0 ||
                    slot.Uv2SlotId > MaximumWindowSlotId ||
                    slot.Uv2SlotId != slot.SlotId ||
                    !slotIds.Add(slot.SlotId) ||
                    !uv2Ids.Add(slot.Uv2SlotId))
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' window-slot metadata " +
                        "is invalid.");
                }

                slotsById.Add(slot.SlotId, slot);
                if (slot.OpeningKind ==
                    CityBuildingOpeningKind.BalconyDoor)
                {
                    declaredDoorIds.Add(slot.SlotId);
                }
            }

            var balconyIds = new HashSet<string>(StringComparer.Ordinal);
            var referencedDoorIds = new HashSet<int>();
            var balconiesPerFloor = new Dictionary<int, int>();
            for (int index = 0; index < balconySlots.Length; index++)
            {
                CityBuildingBalconySlot balcony = balconySlots[index];
                if (balcony == null)
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' balcony-slot metadata " +
                        "is invalid.");
                }

                if (!slotsById.TryGetValue(
                        balcony.DoorSlotId,
                        out CityBuildingWindowSlot door))
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' balcony " +
                        $"'{balcony.StableId}' references a missing door.");
                }

                if (string.IsNullOrWhiteSpace(balcony.StableId) ||
                    !balconyIds.Add(balcony.StableId) ||
                    balcony.Floor <= 0 ||
                    !IsKnownSide(balcony.Side) ||
                    !IsFinite(balcony.LocalDeckBounds) ||
                    balcony.LocalDeckBounds.size.x <= 0f ||
                    balcony.LocalDeckBounds.size.y <= 0f ||
                    balcony.LocalDeckBounds.size.z <= 0f ||
                    !ContainsWithTolerance(
                        localBounds,
                        balcony.LocalDeckBounds.min) ||
                    !ContainsWithTolerance(
                        localBounds,
                        balcony.LocalDeckBounds.max) ||
                    !IsFinite(balcony.LocalNpcDock) ||
                    !ContainsWithTolerance(
                        balcony.LocalDeckBounds,
                        balcony.LocalNpcDock) ||
                    Mathf.Abs(
                        balcony.LocalNpcDock.y -
                        balcony.LocalDeckBounds.max.y) > 0.0001f ||
                    !IsFinite(balcony.LocalOutward) ||
                    Mathf.Abs(balcony.LocalOutward.magnitude - 1f) >
                        0.0001f ||
                    Vector3.Dot(
                        balcony.LocalOutward,
                        ExpectedOutward(balcony.Side)) < 0.9999f ||
                    !referencedDoorIds.Add(balcony.DoorSlotId) ||
                    door.OpeningKind !=
                        CityBuildingOpeningKind.BalconyDoor ||
                    door.Floor != balcony.Floor ||
                    !string.Equals(
                        door.Side,
                        balcony.Side,
                        StringComparison.Ordinal) ||
                    Mathf.Abs(
                        door.LocalCenter.y - door.SizeMeters.y * 0.5f -
                        balcony.LocalDeckBounds.max.y) > 0.0001f ||
                    door.LocalCenter.x <
                        balcony.LocalDeckBounds.min.x - 0.0001f ||
                    door.LocalCenter.x >
                        balcony.LocalDeckBounds.max.x + 0.0001f ||
                    door.LocalCenter.z <
                        balcony.LocalDeckBounds.min.z - 0.0001f ||
                    door.LocalCenter.z >
                        balcony.LocalDeckBounds.max.z + 0.0001f)
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' balcony-slot metadata " +
                        "is invalid.");
                }

                balconiesPerFloor.TryGetValue(
                    balcony.Floor,
                    out int floorCount);
                balconiesPerFloor[balcony.Floor] = floorCount + 1;
            }

            if (!referencedDoorIds.SetEquals(declaredDoorIds))
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' balcony doors are not " +
                    "paired one-to-one.");
            }

            if (district == CityDistrictKind.Residential)
            {
                if (balconySlots.Length != 8)
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' needs eight balcony " +
                        "slots.");
                }

                float[] expectedLevels = { 7f, 12f, 17f, 22f };
                for (int floor = 1; floor <= expectedLevels.Length; floor++)
                {
                    if (!balconiesPerFloor.TryGetValue(
                            floor,
                            out int floorCount) ||
                        floorCount != 2)
                    {
                        throw new InvalidOperationException(
                            $"City building '{stableId}' floor {floor} " +
                            "needs two balconies.");
                    }

                    for (int index = 0; index < balconySlots.Length; index++)
                    {
                        CityBuildingBalconySlot balcony = balconySlots[index];
                        if (balcony.Floor != floor)
                        {
                            continue;
                        }

                        if (!string.Equals(
                                balcony.Side,
                                "Front",
                                StringComparison.Ordinal) ||
                            Mathf.Abs(
                                balcony.LocalDeckBounds.max.y -
                                expectedLevels[floor - 1]) > 0.0001f ||
                            Mathf.Abs(
                                balcony.LocalDeckBounds.size.x - 2.5f) >
                                0.0001f ||
                            Mathf.Abs(
                                balcony.LocalDeckBounds.size.z - 1.2f) >
                                0.0001f)
                        {
                            throw new InvalidOperationException(
                                $"City building '{stableId}' residential " +
                                "balcony layout drifted.");
                        }
                    }
                }
            }
            else if (balconySlots.Length != 0)
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' cannot have balconies.");
            }
        }

        private static bool IsKnownSide(string side)
        {
            return string.Equals(side, "Front", StringComparison.Ordinal) ||
                string.Equals(side, "Rear", StringComparison.Ordinal) ||
                string.Equals(side, "Left", StringComparison.Ordinal) ||
                string.Equals(side, "Right", StringComparison.Ordinal);
        }

        private static Vector3 ExpectedOutward(string side)
        {
            switch (side)
            {
                case "Front":
                    return Vector3.forward;
                case "Rear":
                    return Vector3.back;
                case "Left":
                    return Vector3.left;
                case "Right":
                    return Vector3.right;
                default:
                    return Vector3.zero;
            }
        }

        private static bool ContainsWithTolerance(
            Bounds bounds,
            Vector3 point)
        {
            const float tolerance = 0.0001f;
            Vector3 minimum = bounds.min - Vector3.one * tolerance;
            Vector3 maximum = bounds.max + Vector3.one * tolerance;
            return point.x >= minimum.x && point.x <= maximum.x &&
                point.y >= minimum.y && point.y <= maximum.y &&
                point.z >= minimum.z && point.z <= maximum.z;
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(Bounds value)
        {
            return IsFinite(value.center) && IsFinite(value.size);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ValidatePassiveHierarchy()
        {
            if (GetComponentsInChildren<Collider>(true).Length != 0 ||
                GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                GetComponentsInChildren<Light>(true).Length != 0 ||
                GetComponentsInChildren<Camera>(true).Length != 0 ||
                GetComponentsInChildren<Animator>(true).Length != 0 ||
                GetComponentsInChildren<Animation>(true).Length != 0 ||
                GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                GetComponentsInChildren<ParticleSystem>(true).Length != 0 ||
                GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"City building '{stableId}' must stay passive.");
            }
        }
    }
}
