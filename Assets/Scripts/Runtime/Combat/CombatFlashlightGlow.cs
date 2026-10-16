using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>The visible source of the existing chest beam: two fixed, occluded light sprites.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class CombatFlashlightGlow : MonoBehaviour
    {
        public const int ParticleCount = 2;
        public const float CoreDiameter = .038f;
        public const float HaloDiameter = .14f;
        private const float Lifetime = 100000f;
        private const float FrontCosine = .9396926f; // Full at twenty degrees.
        private const float SideCosine = .1736482f; // Gone by eighty degrees.
        private static Material sharedMaterial;
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[ParticleCount];
        private Transform lens;
        private bool lit;
        private bool appliedLampState;
        private bool subscribed;
        private float appliedVisibility = -1f;

        public ParticleSystem Particles { get; private set; }
        public ParticleSystemRenderer GlowRenderer { get; private set; }
        public Renderer LensRenderer { get; private set; }
        public Light Source { get; private set; }
        public float AngularVisibility { get; private set; }

        public static CombatFlashlightGlow Create(Transform lens, Light source)
        {
            if (lens == null) throw new ArgumentNullException(nameof(lens));
            if (source == null) throw new ArgumentNullException(nameof(source));
            Renderer glass = CombatFlashlightAssetProvider.FindLensRenderer(lens.parent.gameObject);
            var host = new GameObject("Chest Lamp Source Glow");
            host.transform.SetParent(lens, false);
            // The authored anchor is already in front of the glass. A further
            // three millimetres avoids the lens occluding its own small core.
            host.transform.localPosition = Vector3.forward * .003f;
            try
            {
                CombatFlashlightGlow glow = host.AddComponent<CombatFlashlightGlow>();
                glow.lens = lens;
                glow.Source = source;
                glow.LensRenderer = glass;
                glow.Configure();
                glow.Synchronize();
                return glow;
            }
            catch
            {
                if (Application.isPlaying) Destroy(host);
                else DestroyImmediate(host);
                throw;
            }
        }

        /// <summary>Keep source appearance on the lamp's switch, independently of near-wall beam exposure.</summary>
        public void Synchronize()
        {
            if (GlowRenderer == null) return;
            bool burning = isActiveAndEnabled && Source != null && Source.isActiveAndEnabled && Source.intensity > 0f;
            if (!appliedLampState || lit != burning)
            {
                lit = burning;
                appliedLampState = true;
                CombatFlashlightAssetProvider.SetLensLit(LensRenderer, lit);
            }
            bool lensVisible = LensRenderer != null && LensRenderer.enabled && !LensRenderer.forceRenderingOff &&
                               LensRenderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
            // Visibility leases own Renderer.enabled; never re-enable their hidden accessory.
            GlowRenderer.forceRenderingOff = !burning || !lensVisible;
            if (!burning)
            {
                AngularVisibility = 0f;
                appliedVisibility = -1f;
            }
        }

        private void OnEnable()
        {
            if (subscribed) return;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            subscribed = true;
            Synchronize();
        }

        private void OnDisable()
        {
            if (subscribed) RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            subscribed = false;
            Synchronize();
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            Synchronize();
            if (GlowRenderer == null || GlowRenderer.forceRenderingOff || camera == null || lens == null) return;
            Vector3 delta = camera.transform.position - transform.position;
            float cosine = delta.sqrMagnitude > .000001f ? Vector3.Dot(lens.forward, delta.normalized) : 0f;
            float visibility = Mathf.Clamp01((cosine - SideCosine) / (FrontCosine - SideCosine));
            AngularVisibility = visibility * visibility * (3f - 2f * visibility);
            if (Mathf.Abs(appliedVisibility - AngularVisibility) < 1f / 1024f) return;
            appliedVisibility = AngularVisibility;
            particles[0].startColor = new Color(1f, 1f, 1f, .28f * AngularVisibility);
            particles[1].startColor = new Color(1f, 1f, 1f, AngularVisibility);
            Particles.SetParticles(particles, ParticleCount);
        }

        private void Configure()
        {
            Particles = GetComponent<ParticleSystem>();
            GlowRenderer = GetComponent<ParticleSystemRenderer>();
            Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Particles.useAutoRandomSeed = false;
            Particles.randomSeed = 0x4C454E53u;
            ParticleSystem.MainModule main = Particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.simulationSpeed = 0f;
            main.maxParticles = ParticleCount;
            main.startLifetime = Lifetime;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = Particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = Particles.shape;
            shape.enabled = false;
            GlowRenderer.sharedMaterial = SharedMaterial;
            GlowRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            GlowRenderer.alignment = ParticleSystemRenderSpace.View;
            GlowRenderer.sortMode = ParticleSystemSortMode.None;
            GlowRenderer.minParticleSize = 0f;
            GlowRenderer.maxParticleSize = .08f;
            GlowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            GlowRenderer.receiveShadows = false;
            GlowRenderer.lightProbeUsage = LightProbeUsage.Off;
            GlowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GlowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // Center has three components and starts TEXCOORD1 after the two UV components.
            GlowRenderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Center
            });
            particles[0] = FixedParticle(HaloDiameter, .28f, 1u);
            particles[1] = FixedParticle(CoreDiameter, 1f, 2u);
            Particles.Play(false);
            Particles.SetParticles(particles, ParticleCount);
        }

        private static ParticleSystem.Particle FixedParticle(float diameter, float alpha, uint seed) =>
            new ParticleSystem.Particle
            {
                position = Vector3.zero,
                startSize = diameter,
                startColor = new Color(1f, 1f, 1f, alpha),
                startLifetime = Lifetime,
                remainingLifetime = Lifetime,
                randomSeed = seed
            };

        private static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/ChestFlashlightGlow");
                if (shader == null) throw new InvalidOperationException("Missing chest lamp source shader.");
                sharedMaterial = new Material(shader)
                {
                    name = "Chest Lamp Source Shared",
                    hideFlags = HideFlags.HideAndDontSave
                };
                return sharedMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            if (sharedMaterial != null) Destroy(sharedMaterial);
            sharedMaterial = null;
        }
    }
}
