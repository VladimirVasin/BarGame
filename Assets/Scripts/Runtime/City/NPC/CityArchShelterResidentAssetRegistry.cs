using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityArchShelterResidentRole
    {
        StandingWarmer = 0,
        SeatedWarmer = 1,
        Sleeper = 2
    }

    [Serializable]
    public sealed class CityArchShelterResidentRendererBinding
    {
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private bool usesDetailAtlas;

        public CityArchShelterResidentRendererBinding(
            Renderer configuredRenderer,
            Color configuredColor,
            bool configuredUsesDetailAtlas)
        {
            renderer = configuredRenderer;
            color = configuredColor;
            usesDetailAtlas = configuredUsesDetailAtlas;
        }

        public Renderer Renderer => renderer;
        public Color Color => color;
        public bool UsesDetailAtlas => usesDetailAtlas;
    }

    /// <summary>
    /// Authored model, texture and animation contract carried by one of the
    /// three passive residents of the Nightlife arch shelter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityArchShelterResidentAssetRegistry : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");

        [SerializeField] private Animator animator;
        [SerializeField] private CityArchShelterResidentRole role;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private CityArchShelterResidentRendererBinding[]
            rendererBindings =
                Array.Empty<CityArchShelterResidentRendererBinding>();
        [SerializeField] private Transform head;
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;
        [SerializeField] private Texture2D detailAtlas;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int triangleCount;
        [SerializeField] private string generatorVersion = string.Empty;
        [SerializeField] private string designId = string.Empty;
        [SerializeField] private string buildSignature = string.Empty;

        public Animator Animator => animator;
        public CityArchShelterResidentRole Role => role;
        public AnimationClip IdleClip => idleClip;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<CityArchShelterResidentRendererBinding>
            RendererBindings => rendererBindings ??
                Array.Empty<CityArchShelterResidentRendererBinding>();
        public Transform Head => head;
        public Transform Pelvis => pelvis;
        public Transform LeftFoot => leftFoot;
        public Transform RightFoot => rightFoot;
        public Texture2D DetailAtlas => detailAtlas;
        public Bounds LocalBounds => localBounds;
        public int TriangleCount => triangleCount;
        public string GeneratorVersion => generatorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public void Configure(
            Animator configuredAnimator,
            CityArchShelterResidentRole configuredRole,
            AnimationClip configuredIdleClip,
            Transform configuredModelRoot,
            CityArchShelterResidentRendererBinding[]
                configuredRendererBindings,
            Transform configuredHead,
            Transform configuredPelvis,
            Transform configuredLeftFoot,
            Transform configuredRightFoot,
            Texture2D configuredDetailAtlas,
            Bounds configuredLocalBounds,
            int configuredTriangleCount,
            string configuredGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            animator = configuredAnimator ??
                throw new ArgumentNullException(nameof(configuredAnimator));
            role = configuredRole;
            idleClip = configuredIdleClip ??
                throw new ArgumentNullException(nameof(configuredIdleClip));
            modelRoot = configuredModelRoot ??
                throw new ArgumentNullException(nameof(configuredModelRoot));
            rendererBindings = configuredRendererBindings ??
                throw new ArgumentNullException(
                    nameof(configuredRendererBindings));
            head = configuredHead ??
                throw new ArgumentNullException(nameof(configuredHead));
            pelvis = configuredPelvis ??
                throw new ArgumentNullException(nameof(configuredPelvis));
            leftFoot = configuredLeftFoot ??
                throw new ArgumentNullException(nameof(configuredLeftFoot));
            rightFoot = configuredRightFoot ??
                throw new ArgumentNullException(nameof(configuredRightFoot));
            detailAtlas = configuredDetailAtlas ??
                throw new ArgumentNullException(
                    nameof(configuredDetailAtlas));
            localBounds = configuredLocalBounds;
            triangleCount = configuredTriangleCount;
            generatorVersion = configuredGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            ApplyAppearance();
        }

        public void ApplyAppearance()
        {
            if (rendererBindings == null)
            {
                return;
            }

            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < rendererBindings.Length; index++)
            {
                CityArchShelterResidentRendererBinding binding =
                    rendererBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                Renderer target = binding.Renderer;
                target.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, binding.Color);
                properties.SetColor(LegacyColorId, binding.Color);
                if (binding.UsesDetailAtlas && detailAtlas != null)
                {
                    properties.SetTexture(BaseMapId, detailAtlas);
                    properties.SetTexture(LegacyMapId, detailAtlas);
                }

                target.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void OnEnable()
        {
            ApplyAppearance();
        }
    }
}
