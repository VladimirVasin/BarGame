using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A named transform a hand prop carries besides its meshes: the far
    /// end of the fishing rod the line hangs from, the top of the pipe
    /// bowl the ember glows on, the coffee-pot spout the pour stream
    /// leaves. Measured once by the prefab build off the imported
    /// geometry, exactly as the body anchors used to be, and reached by
    /// name so no consumer has to know the prop's hierarchy.
    /// </summary>
    [Serializable]
    public sealed class CityPedestrianHandPropAnchor
    {
        [SerializeField] private string anchorName;
        [SerializeField] private Transform transform;

        public CityPedestrianHandPropAnchor(
            string configuredName,
            Transform configuredTransform)
        {
            anchorName = configuredName ?? string.Empty;
            transform = configuredTransform;
        }

        public string Name => anchorName;
        public Transform Transform => transform;
    }

    /// <summary>
    /// The asset contract of ONE hand prop prefab under
    /// `Resources/Pedestrians/HandProps/`: the carpet beater, the
    /// cigarette, the funeral bouquet, the chalk, the rod, the pipe, the
    /// cafe woman's cigarette, the attendant's towel and coffee pot.
    ///
    /// Until 2026-09-05 every one of these was a skinned `ACC_*` part of
    /// the body that used it, so a random grandmother walked the promenade
    /// with a carpet beater, and three separate name tables hid the wrong
    /// prop for the wrong role. Now a body ships no prop at all and a role
    /// attaches the one it holds through <see cref="CityPedestrianHandProps"/>.
    ///
    /// The hierarchy is prefab root (identity) -> <see cref="Mount"/> ->
    /// parts and anchors. The Mount carries the measured socket-relative
    /// pose (including the inverse of the 100x FBX bone scale), so the root
    /// is simply parented to the socket with an identity local transform.
    /// Under an identity Mount the parts sit in the import frame in metres
    /// with the socket head at the origin, which is the free-standing pose
    /// <see cref="CityPedestrianHandProps.Place"/> uses for a bouquet laid
    /// on a grave.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityPedestrianHandPropRegistry : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private CityPedestrianHandPropId id;
        [SerializeField] private string manifestId;
        [SerializeField] private string socketName;
        [SerializeField] private string referenceDesignId;
        [SerializeField] private Transform mount;
        [SerializeField] private Vector3 mountLocalPosition;
        [SerializeField] private Quaternion mountLocalRotation =
            Quaternion.identity;
        [SerializeField] private Vector3 mountLocalScale = Vector3.one;
        [SerializeField] private Vector3 referenceSocketRestPosition;
        [SerializeField] private Quaternion referenceSocketRestRotation =
            Quaternion.identity;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private CityPedestrianRendererBinding[]
            rendererBindings = Array.Empty<CityPedestrianRendererBinding>();
        [SerializeField] private CityPedestrianHandPropAnchor[] anchors =
            Array.Empty<CityPedestrianHandPropAnchor>();
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string buildSignature;
        [SerializeField] private int paletteVariant;

        public CityPedestrianHandPropId Id => id;

        /// <summary>The generator's id, e.g. `carpet_beater`.</summary>
        public string ManifestId => manifestId;

        /// <summary>
        /// The canonical rig socket this prop is authored for, e.g.
        /// `SOCKET_Grip.R`. <see cref="CityPedestrianHandProps.Attach"/>
        /// refuses any other socket: a prop measured against one socket
        /// has no meaning under another.
        /// </summary>
        public string SocketName => socketName;

        /// <summary>The design the geometry was authored against.</summary>
        public string ReferenceDesignId => referenceDesignId;

        public Transform Mount => mount;

        /// <summary>
        /// The Mount pose the prefab build measured in the bind pose:
        /// socket space to import space. Stored beside the transform so a
        /// free-standing placement can restore it after
        /// <see cref="ResetMountToFreeStanding"/>.
        /// </summary>
        public Vector3 MountLocalPosition => mountLocalPosition;
        public Quaternion MountLocalRotation => mountLocalRotation;
        public Vector3 MountLocalScale => mountLocalScale;

        /// <summary>
        /// The socket's rest pose in the reference body's import frame
        /// (the body FBX instantiated at identity), recorded at the build
        /// that measured the Mount. The editor validation re-measures the
        /// live FBX against these and queues a rebuild when the skeleton
        /// has moved, because a Mount is only right for the socket it was
        /// measured on.
        /// </summary>
        public Vector3 ReferenceSocketRestPosition => referenceSocketRestPosition;
        public Quaternion ReferenceSocketRestRotation => referenceSocketRestRotation;

        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<CityPedestrianRendererBinding> RendererBindings =>
            rendererBindings;
        public IReadOnlyList<CityPedestrianHandPropAnchor> Anchors => anchors;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string BuildSignature => buildSignature;
        public int PaletteVariant => paletteVariant;

        /// <summary>
        /// True while every renderer is enabled. A prop is hidden as a
        /// whole or not at all — the attendant's pot vanishes for the menu
        /// handoff, never half of it.
        /// </summary>
        public bool IsVisible
        {
            get
            {
                for (int index = 0; index < renderers.Length; index++)
                {
                    if (renderers[index] != null && !renderers[index].enabled)
                    {
                        return false;
                    }
                }

                return renderers.Length > 0;
            }
        }

        public void Configure(
            CityPedestrianHandPropId configuredId,
            string configuredManifestId,
            string configuredSocketName,
            string configuredReferenceDesignId,
            Transform configuredMount,
            Renderer[] configuredRenderers,
            CityPedestrianRendererBinding[] configuredRendererBindings,
            CityPedestrianHandPropAnchor[] configuredAnchors,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredBuildSignature,
            Vector3 configuredReferenceSocketRestPosition,
            Quaternion configuredReferenceSocketRestRotation)
        {
            id = configuredId;
            manifestId = configuredManifestId ?? string.Empty;
            socketName = configuredSocketName ?? string.Empty;
            referenceDesignId = configuredReferenceDesignId ?? string.Empty;
            mount = configuredMount != null
                ? configuredMount
                : throw new ArgumentNullException(nameof(configuredMount));
            mountLocalPosition = configuredMount.localPosition;
            mountLocalRotation = configuredMount.localRotation;
            mountLocalScale = configuredMount.localScale;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredRendererBindings ??
                Array.Empty<CityPedestrianRendererBinding>();
            anchors = configuredAnchors ??
                Array.Empty<CityPedestrianHandPropAnchor>();
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            referenceSocketRestPosition = configuredReferenceSocketRestPosition;
            referenceSocketRestRotation = configuredReferenceSocketRestRotation;
            ApplyPaletteVariant(0);
        }

        /// <summary>
        /// Exact name, `Ordinal`, like every renderer lookup in the
        /// pedestrian library: a renamed mesh must stop matching rather
        /// than quietly take a neighbour's.
        /// </summary>
        public Renderer FindRenderer(string rendererName)
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer != null &&
                    string.Equals(
                        renderer.name,
                        rendererName,
                        StringComparison.Ordinal))
                {
                    return renderer;
                }
            }

            return null;
        }

        public Transform FindAnchor(string anchorName)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                CityPedestrianHandPropAnchor anchor = anchors[index];
                if (anchor != null &&
                    string.Equals(
                        anchor.Name,
                        anchorName,
                        StringComparison.Ordinal))
                {
                    return anchor.Transform;
                }
            }

            return null;
        }

        public Transform RequireAnchor(string anchorName)
        {
            Transform anchor = FindAnchor(anchorName);
            if (anchor == null)
            {
                throw new InvalidOperationException(
                    $"Hand prop '{manifestId}' carries no anchor named " +
                    $"'{anchorName}'.");
            }

            return anchor;
        }

        /// <summary>
        /// The same four-variant tint the bodies wear, through the same
        /// property block, so a bouquet follows the visit's palette and a
        /// cigarette borrowed by a balcony smoker matches nobody in
        /// particular. No atlas: no prop carries one.
        /// </summary>
        public void ApplyPaletteVariant(int variant)
        {
            int normalized = variant % 4;
            paletteVariant = normalized < 0 ? normalized + 4 : normalized;

            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < rendererBindings.Length; index++)
            {
                CityPedestrianRendererBinding binding =
                    rendererBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                Renderer target = binding.Renderer;
                target.GetPropertyBlock(properties);
                Color color = binding.GetColor(paletteVariant);
                properties.SetColor(BaseColorId, color);
                properties.SetColor(LegacyColorId, color);
                target.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        public void SetVisible(bool visible)
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].enabled = visible;
                }
            }
        }

        public void SetSharedMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].sharedMaterial = material;
                }
            }
        }

        /// <summary>
        /// Puts the Mount back to the measured socket-relative pose, for a
        /// prop that has been re-parented under its socket.
        /// </summary>
        public void RestoreMountToSocketPose()
        {
            if (mount == null)
            {
                return;
            }

            mount.localPosition = mountLocalPosition;
            mount.localRotation = mountLocalRotation;
            mount.localScale = mountLocalScale;
        }

        /// <summary>
        /// Identity Mount: the parts then sit in the import frame in
        /// metres with the socket head at the root, which is what a prop
        /// standing in the world (not in a hand) needs.
        /// </summary>
        public void ResetMountToFreeStanding()
        {
            if (mount == null)
            {
                return;
            }

            mount.localPosition = Vector3.zero;
            mount.localRotation = Quaternion.identity;
            mount.localScale = Vector3.one;
        }

        private void OnEnable()
        {
            ApplyPaletteVariant(paletteVariant);
        }
    }
}
