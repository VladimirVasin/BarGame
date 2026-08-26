using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum ChurchAssetKind
    {
        Exterior,
        Interior
    }

    public enum ChurchMaterialSlot
    {
        Plaster,
        Stone,
        Wood,
        Roof,
        Iron,
        Gold,
        Floor,
        Textile,
        SacredArt,
        Mural,
        GlassCold,
        GlassWarm,
        CandleFlame
    }

    public enum ChurchAnchorKind
    {
        Entrance,
        Approach,
        Return,
        Spawn,
        Exit,
        NarthexLight,
        NaveLight,
        SanctuaryLight
    }

    [Serializable]
    public sealed class ChurchRendererBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private ChurchMaterialSlot materialSlot;
        [SerializeField] private Renderer renderer;

        public ChurchRendererBinding(
            string configuredSourceName,
            string configuredRole,
            ChurchMaterialSlot configuredMaterialSlot,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            materialSlot = configuredMaterialSlot;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public ChurchMaterialSlot MaterialSlot => materialSlot;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public struct ChurchDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float length;
        [SerializeField] private float height;
        [SerializeField] private float doorWidth;
        [SerializeField] private float doorHeight;

        public ChurchDimensions(
            float configuredWidth,
            float configuredLength,
            float configuredHeight,
            float configuredDoorWidth,
            float configuredDoorHeight)
        {
            width = configuredWidth;
            length = configuredLength;
            height = configuredHeight;
            doorWidth = configuredDoorWidth;
            doorHeight = configuredDoorHeight;
        }

        public float Width => width;
        public float Length => length;
        public float Height => height;
        public float DoorWidth => doorWidth;
        public float DoorHeight => doorHeight;
    }

    /// <summary>
    /// Serialized semantic bridge from the two passive Blender models to the
    /// City and ChurchInterior builders. Geometry remains collider/light-free;
    /// gameplay plans own collision, navigation and realtime illumination.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChurchAssetRegistry : MonoBehaviour
    {
        public const string ExteriorPrefabResourcePath =
            "Church/ChurchExterior3D";
        public const string InteriorPrefabResourcePath =
            "Church/ChurchInterior3D";

        [SerializeField] private ChurchAssetKind kind;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform entranceAnchor;
        [SerializeField] private Transform approachAnchor;
        [SerializeField] private Transform returnAnchor;
        [SerializeField] private Transform spawnAnchor;
        [SerializeField] private Transform exitAnchor;
        [SerializeField] private Transform narthexLightAnchor;
        [SerializeField] private Transform naveLightAnchor;
        [SerializeField] private Transform sanctuaryLightAnchor;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private ChurchRendererBinding[] rendererBindings =
            Array.Empty<ChurchRendererBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private ChurchDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public ChurchAssetKind Kind => kind;
        public Transform ModelRoot => modelRoot;
        public Transform EntranceAnchor => entranceAnchor;
        public Transform ApproachAnchor => approachAnchor;
        public Transform ReturnAnchor => returnAnchor;
        public Transform SpawnAnchor => spawnAnchor;
        public Transform ExitAnchor => exitAnchor;
        public Transform NarthexLightAnchor => narthexLightAnchor;
        public Transform NaveLightAnchor => naveLightAnchor;
        public Transform SanctuaryLightAnchor => sanctuaryLightAnchor;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<ChurchRendererBinding> RendererBindings =>
            rendererBindings;
        public Bounds LocalBounds => localBounds;
        public ChurchDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool TryGetAnchor(
            ChurchAnchorKind anchorKind,
            out Transform anchor)
        {
            switch (anchorKind)
            {
                case ChurchAnchorKind.Entrance:
                    anchor = entranceAnchor;
                    break;
                case ChurchAnchorKind.Approach:
                    anchor = approachAnchor;
                    break;
                case ChurchAnchorKind.Return:
                    anchor = returnAnchor;
                    break;
                case ChurchAnchorKind.Spawn:
                    anchor = spawnAnchor;
                    break;
                case ChurchAnchorKind.Exit:
                    anchor = exitAnchor;
                    break;
                case ChurchAnchorKind.NarthexLight:
                    anchor = narthexLightAnchor;
                    break;
                case ChurchAnchorKind.NaveLight:
                    anchor = naveLightAnchor;
                    break;
                case ChurchAnchorKind.SanctuaryLight:
                    anchor = sanctuaryLightAnchor;
                    break;
                default:
                    anchor = null;
                    break;
            }

            return anchor != null;
        }

        public void Configure(
            ChurchAssetKind configuredKind,
            Transform configuredModelRoot,
            Transform configuredEntranceAnchor,
            Transform configuredApproachAnchor,
            Transform configuredReturnAnchor,
            Transform configuredSpawnAnchor,
            Transform configuredExitAnchor,
            Transform configuredNarthexLightAnchor,
            Transform configuredNaveLightAnchor,
            Transform configuredSanctuaryLightAnchor,
            Renderer[] configuredRenderers,
            ChurchRendererBinding[] configuredRendererBindings,
            Bounds configuredLocalBounds,
            ChurchDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            kind = configuredKind;
            modelRoot = configuredModelRoot;
            entranceAnchor = configuredEntranceAnchor;
            approachAnchor = configuredApproachAnchor;
            returnAnchor = configuredReturnAnchor;
            spawnAnchor = configuredSpawnAnchor;
            exitAnchor = configuredExitAnchor;
            narthexLightAnchor = configuredNarthexLightAnchor;
            naveLightAnchor = configuredNaveLightAnchor;
            sanctuaryLightAnchor = configuredSanctuaryLightAnchor;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredRendererBindings ??
                Array.Empty<ChurchRendererBinding>();
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }
    }
}
