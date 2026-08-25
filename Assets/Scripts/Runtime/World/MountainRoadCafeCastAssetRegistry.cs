using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class MountainRoadCafeCastRendererBinding
    {
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color color = Color.white;

        public MountainRoadCafeCastRendererBinding(
            Renderer configuredRenderer,
            Color configuredColor)
        {
            renderer = configuredRenderer;
            color = configuredColor;
        }

        public Renderer Renderer => renderer;
        public Color Color => color;
    }

    /// <summary>
    /// Minimal contract carried by each of the four staged cafe prefabs.
    /// It is intentionally separate from the ambient pedestrian registry:
    /// these figures have no walk, bus-seat, palette or pool contract.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCastAssetRegistry : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Animator animator;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip beatClip;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private MountainRoadCafeCastRendererBinding[]
            rendererBindings =
                Array.Empty<MountainRoadCafeCastRendererBinding>();

        public Animator Animator => animator;
        public AnimationClip IdleClip => idleClip;
        public AnimationClip BeatClip => beatClip;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<MountainRoadCafeCastRendererBinding>
            RendererBindings => rendererBindings ??
                Array.Empty<MountainRoadCafeCastRendererBinding>();

        public void Configure(
            Animator configuredAnimator,
            AnimationClip configuredIdleClip,
            AnimationClip configuredBeatClip,
            Transform configuredModelRoot,
            MountainRoadCafeCastRendererBinding[]
                configuredRendererBindings)
        {
            animator = configuredAnimator ??
                throw new ArgumentNullException(nameof(configuredAnimator));
            idleClip = configuredIdleClip ??
                throw new ArgumentNullException(nameof(configuredIdleClip));
            beatClip = configuredBeatClip ??
                throw new ArgumentNullException(nameof(configuredBeatClip));
            modelRoot = configuredModelRoot ??
                throw new ArgumentNullException(nameof(configuredModelRoot));
            rendererBindings = configuredRendererBindings ??
                Array.Empty<MountainRoadCafeCastRendererBinding>();
            ApplyBaseColors();
        }

        private void OnEnable()
        {
            ApplyBaseColors();
        }

        /// <summary>
        /// Reapplies the authored manifest colours without material
        /// instances. The factory calls this explicitly as well as OnEnable:
        /// ordinary MonoBehaviour callbacks do not run while an EditMode
        /// world is assembled for validation.
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
                target.SetPropertyBlock(properties);
                properties.Clear();
            }
        }
    }
}
