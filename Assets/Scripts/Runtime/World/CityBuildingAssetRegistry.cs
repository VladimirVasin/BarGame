using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBuildingMeshRole
    {
        Shell = 0,
        Trim = 1,
        Roof = 2,
        Metal = 3,
        WindowFrame = 4,
        WindowGlass = 5
    }

    [Serializable]
    public sealed class CityBuildingPartBinding
    {
        [SerializeField] private string sourceName = string.Empty;
        [SerializeField] private CityBuildingMeshRole role;
        [SerializeField] private Renderer renderer;

        public CityBuildingPartBinding(
            string configuredSourceName,
            CityBuildingMeshRole configuredRole,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public CityBuildingMeshRole Role => role;
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
        [SerializeField] private Vector3 localCenter;
        [SerializeField] private Vector2 sizeMeters;
        [SerializeField] private int uv2SlotId;

        public CityBuildingWindowSlot(
            int configuredSlotId,
            string configuredSide,
            int configuredFloor,
            int configuredBay,
            Vector3 configuredLocalCenter,
            Vector2 configuredSizeMeters,
            int configuredUv2SlotId)
        {
            slotId = configuredSlotId;
            side = configuredSide ?? string.Empty;
            floor = configuredFloor;
            bay = configuredBay;
            localCenter = configuredLocalCenter;
            sizeMeters = configuredSizeMeters;
            uv2SlotId = configuredUv2SlotId;
        }

        public int SlotId => slotId;
        public string Side => side;
        public int Floor => floor;
        public int Bay => bay;
        public Vector3 LocalCenter => localCenter;
        public Vector2 SizeMeters => sizeMeters;
        public int Uv2SlotId => uv2SlotId;
    }

    /// <summary>
    /// Semantic metadata attached to one passive building-prototype wrapper.
    /// Geometry remains authored in Blender; a later integration pass will
    /// decide how a prototype is fitted to an ordinary City lot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBuildingAssetRegistry : MonoBehaviour
    {
        public const int ExpectedRoleCount = 6;
        public const int MaximumTriangleCount = 3500;

        private static readonly CityBuildingMeshRole[] ExpectedRoles =
        {
            CityBuildingMeshRole.Shell,
            CityBuildingMeshRole.Trim,
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
                    $"City building '{stableId}' needs six role meshes.");
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
                    !seenRoles.Add(binding.Role) ||
                    !seenRenderers.Add(binding.Renderer) ||
                    !binding.Renderer.transform.IsChildOf(modelRoot))
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' role binding drifted.");
                }
            }
        }

        private void ValidateAttachmentMetadata()
        {
            if (roofAttachmentBounds.size.x <= 0f ||
                roofAttachmentBounds.size.z <= 0f ||
                facadeAttachments == null ||
                facadeAttachments.Length == 0 ||
                windowSlots == null ||
                windowSlots.Length == 0)
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
            for (int index = 0; index < windowSlots.Length; index++)
            {
                CityBuildingWindowSlot slot = windowSlots[index];
                if (slot == null ||
                    slot.SlotId <= 0 ||
                    string.IsNullOrWhiteSpace(slot.Side) ||
                    slot.Floor < 0 ||
                    slot.Bay < 0 ||
                    slot.SizeMeters.x <= 0f ||
                    slot.SizeMeters.y <= 0f ||
                    slot.Uv2SlotId < 0 ||
                    !slotIds.Add(slot.SlotId) ||
                    !uv2Ids.Add(slot.Uv2SlotId))
                {
                    throw new InvalidOperationException(
                        $"City building '{stableId}' window-slot metadata " +
                        "is invalid.");
                }
            }
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
