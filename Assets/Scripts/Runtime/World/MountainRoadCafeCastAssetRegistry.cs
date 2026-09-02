using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeCastClipKind
    {
        Idle = 0,
        Drink = 1,
        Wipe = 2,
        Walk = 3,
        Pour = 4,
        Notice = 5,
        Interject = 6
    }

    [Serializable]
    public sealed class MountainRoadCafeCastClipBinding
    {
        [SerializeField] private MountainRoadCafeCastClipKind kind;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private bool loop;

        public MountainRoadCafeCastClipBinding(
            MountainRoadCafeCastClipKind configuredKind,
            AnimationClip configuredClip,
            bool configuredLoop)
        {
            kind = configuredKind;
            clip = configuredClip ??
                throw new ArgumentNullException(nameof(configuredClip));
            loop = configuredLoop;
        }

        public MountainRoadCafeCastClipKind Kind => kind;
        public AnimationClip Clip => clip;
        public bool Loop => loop;
    }

    [Serializable]
    public sealed class MountainRoadCafeCastRendererBinding
    {
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private bool usesDetailAtlas;

        public MountainRoadCafeCastRendererBinding(
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
    /// Authored animation and renderer contract carried by one of the four
    /// staged cafe prefabs. Across the set it owns exactly ten named clips:
    /// one sleeping loop and one lone-patron interjection, four pair clips,
    /// and four attendant clips.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCastAssetRegistry : MonoBehaviour
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
        [SerializeField] private MountainRoadCafeCastRole role;
        [SerializeField] private MountainRoadCafeCastClipKind defaultClipKind;
        [SerializeField] private MountainRoadCafeCastClipBinding[]
            clipBindings = Array.Empty<MountainRoadCafeCastClipBinding>();
        [SerializeField] private Transform modelRoot;
        [SerializeField] private MountainRoadCafeCastRendererBinding[]
            rendererBindings =
                Array.Empty<MountainRoadCafeCastRendererBinding>();
        [SerializeField] private Texture2D detailAtlas;

        public Animator Animator => animator;
        public MountainRoadCafeCastRole Role => role;
        public MountainRoadCafeCastClipKind DefaultClipKind =>
            defaultClipKind;
        public IReadOnlyList<MountainRoadCafeCastClipBinding> ClipBindings =>
            clipBindings ?? Array.Empty<MountainRoadCafeCastClipBinding>();
        public AnimationClip IdleClip => GetClip(defaultClipKind);
        public AnimationClip BeatClip => role switch
        {
            MountainRoadCafeCastRole.Attendant => GetClip(
                MountainRoadCafeCastClipKind.Notice),
            MountainRoadCafeCastRole.LonePatron => GetClip(
                MountainRoadCafeCastClipKind.Interject),
            _ => GetClip(MountainRoadCafeCastClipKind.Drink)
        };
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<MountainRoadCafeCastRendererBinding>
            RendererBindings => rendererBindings ??
                Array.Empty<MountainRoadCafeCastRendererBinding>();
        public Texture2D DetailAtlas => detailAtlas;

        public void Configure(
            Animator configuredAnimator,
            MountainRoadCafeCastRole configuredRole,
            MountainRoadCafeCastClipKind configuredDefaultClipKind,
            MountainRoadCafeCastClipBinding[] configuredClipBindings,
            Transform configuredModelRoot,
            MountainRoadCafeCastRendererBinding[]
                configuredRendererBindings,
            Texture2D configuredDetailAtlas)
        {
            animator = configuredAnimator ??
                throw new ArgumentNullException(nameof(configuredAnimator));
            role = configuredRole;
            defaultClipKind = configuredDefaultClipKind;
            clipBindings = configuredClipBindings ??
                throw new ArgumentNullException(
                    nameof(configuredClipBindings));
            modelRoot = configuredModelRoot ??
                throw new ArgumentNullException(nameof(configuredModelRoot));
            rendererBindings = configuredRendererBindings ??
                Array.Empty<MountainRoadCafeCastRendererBinding>();
            detailAtlas = configuredDetailAtlas ??
                throw new ArgumentNullException(
                    nameof(configuredDetailAtlas));
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
            ValidateClipContract();
            ApplyBaseColors();
            SetCoffeePotVisible(false);
        }

        public AnimationClip GetClip(
            MountainRoadCafeCastClipKind kind)
        {
            return TryGetClip(kind, out AnimationClip clip, out _)
                ? clip
                : null;
        }

        public bool TryGetClip(
            MountainRoadCafeCastClipKind kind,
            out AnimationClip clip,
            out bool loop)
        {
            if (clipBindings != null)
            {
                for (int index = 0; index < clipBindings.Length; index++)
                {
                    MountainRoadCafeCastClipBinding binding =
                        clipBindings[index];
                    if (binding != null && binding.Kind == kind &&
                        binding.Clip != null)
                    {
                        clip = binding.Clip;
                        loop = binding.Loop;
                        return true;
                    }
                }
            }

            clip = null;
            loop = false;
            return false;
        }

        public Transform FindModelTransform(string transformName)
        {
            if (modelRoot == null || string.IsNullOrWhiteSpace(transformName))
            {
                return null;
            }

            Transform[] transforms =
                modelRoot.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (string.Equals(
                        transforms[index].name,
                        transformName,
                        StringComparison.Ordinal))
                {
                    return transforms[index];
                }
            }

            return null;
        }

        public void SetCoffeePotVisible(bool visible)
        {
            if (rendererBindings == null)
            {
                return;
            }

            for (int index = 0; index < rendererBindings.Length; index++)
            {
                Renderer target = rendererBindings[index]?.Renderer;
                if (target != null && target.name.StartsWith(
                        "ACC_CoffeePot",
                        StringComparison.Ordinal))
                {
                    target.enabled = visible;
                }
            }
        }

        private void Awake()
        {
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
        }

        private void OnEnable()
        {
            ApplyBaseColors();
            SetCoffeePotVisible(false);
        }

        private void ValidateClipContract()
        {
            var unique = new HashSet<MountainRoadCafeCastClipKind>();
            for (int index = 0; index < clipBindings.Length; index++)
            {
                MountainRoadCafeCastClipBinding binding =
                    clipBindings[index];
                if (binding == null || binding.Clip == null ||
                    !unique.Add(binding.Kind))
                {
                    throw new InvalidOperationException(
                        "Cafe clip bindings must be non-null and unique.");
                }
            }

            MountainRoadCafeCastClipKind[] expected;
            if (role == MountainRoadCafeCastRole.Attendant)
            {
                expected = new[]
                    {
                        MountainRoadCafeCastClipKind.Wipe,
                        MountainRoadCafeCastClipKind.Walk,
                        MountainRoadCafeCastClipKind.Pour,
                        MountainRoadCafeCastClipKind.Notice
                    };
            }
            else if (role == MountainRoadCafeCastRole.LonePatron)
            {
                expected = new[]
                {
                    MountainRoadCafeCastClipKind.Idle,
                    MountainRoadCafeCastClipKind.Interject
                };
            }
            else
            {
                expected = new[]
                    {
                        MountainRoadCafeCastClipKind.Idle,
                        MountainRoadCafeCastClipKind.Drink
                    };
            }
            if (clipBindings.Length != expected.Length)
            {
                throw new InvalidOperationException(
                    "Cafe cast role has the wrong authored clip count.");
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (!unique.Contains(expected[index]))
                {
                    throw new InvalidOperationException(
                        "Cafe cast role is missing clip " + expected[index] +
                        ".");
                }
            }

            MountainRoadCafeCastClipKind expectedDefault =
                role == MountainRoadCafeCastRole.Attendant
                    ? MountainRoadCafeCastClipKind.Wipe
                    : MountainRoadCafeCastClipKind.Idle;
            if (defaultClipKind != expectedDefault ||
                GetClip(defaultClipKind) == null)
            {
                throw new InvalidOperationException(
                    "Cafe cast role has the wrong default loop.");
            }
        }

        /// <summary>
        /// Reapplies authored manifest colours and detail atlas without
        /// material instances.
        /// </summary>
        public void ApplyBaseColors()
        {
            if (rendererBindings == null)
            {
                return;
            }

            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < rendererBindings.Length; index++)
            {
                MountainRoadCafeCastRendererBinding binding =
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
    }
}
