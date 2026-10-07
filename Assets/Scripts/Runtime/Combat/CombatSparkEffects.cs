using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One bounded scene pool, advanced only by the duel's live clock.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatSparkEffects : MonoBehaviour
    {
        public const int MaximumParticles = 192;
        private ParticleSystem particles;
        private uint random = 0x21a4e93u;
        public int EmissionCount { get; private set; }
        public int ActiveParticleCount => particles != null ? particles.particleCount : 0;
        internal float SimulationSeconds { get; private set; }
        internal Vector3 LastEmissionPoint { get; private set; }

        public void Initialize(Transform sceneRoot)
        {
            if (particles != null) return;
            if (sceneRoot == null) throw new ArgumentNullException(nameof(sceneRoot));
            var pool = new GameObject("Combat metal sparks");
            pool.transform.SetParent(sceneRoot, false);
            particles = pool.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false; particles.randomSeed = 0x21a4e93u;
            var main = particles.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = MaximumParticles; main.startLifetime = .22f;
            main.startSpeed = 0f; main.startSize = .016f; main.gravityModifier = .8f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var collision = particles.collision; collision.enabled = false;
            var lights = particles.lights; lights.enabled = false;
            var trails = particles.trails; trails.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, .38f, .08f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystemRenderer renderer = pool.GetComponent<ParticleSystemRenderer>();
            // The existing PS1 atmosphere shader already owns small emissive
            // billboards. No material instance, light or post effect per impact.
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = .025f; renderer.lengthScale = 1.5f;
            renderer.minParticleSize = 0f; renderer.maxParticleSize = .035f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            particles.Pause(false);
        }

        internal void Emit(Vector3 point, Vector3 normal, Vector3 travel)
        {
            if (particles == null) return;
            if (normal.sqrMagnitude < .5f) normal = Vector3.up;
            normal.Normalize(); travel.Normalize();
            LastEmissionPoint = point; EmissionCount++;
            for (int i = 0; i < 12; i++)
            {
                Vector3 scatter = new Vector3(Range(-1f, 1f), Range(-.35f, 1f), Range(-1f, 1f));
                Vector3 direction = (scatter + normal * .65f + travel * .25f).normalized;
                particles.Emit(new ParticleSystem.EmitParams
                {
                    position = point, velocity = direction * Range(.9f, 2.8f),
                    startLifetime = Range(.12f, .28f), startSize = Range(.009f, .019f),
                    startColor = new Color(2.6f, 1.35f, .26f, 1f), applyShapeToPosition = false
                }, 1);
            }
            particles.Pause(false);
        }

        internal void Tick(float seconds)
        {
            if (particles == null || seconds <= 0f || ActiveParticleCount == 0) return;
            particles.Simulate(seconds, false, false, false);
            particles.Pause(false);
            SimulationSeconds += seconds;
        }

        internal void Clear()
        { if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }

        public void ResetRound()
        {
            Clear(); EmissionCount = 0; SimulationSeconds = 0f;
            LastEmissionPoint = Vector3.zero; random = 0x21a4e93u;
        }

        private float Range(float from, float to)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return Mathf.Lerp(from, to, (random & 0xffffffu) / 16777215f);
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        { if (particles != null) Destroy(particles.gameObject); particles = null; }
    }
}
