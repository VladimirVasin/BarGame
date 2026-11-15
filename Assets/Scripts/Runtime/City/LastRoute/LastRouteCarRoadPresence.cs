using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Passive rear lenses and a small condensate trail from the running engine on the cold road.</summary>
    [DisallowMultipleComponent]
    internal sealed class LastRouteCarRoadPresence : MonoBehaviour
    {
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private Renderer[] tails, heads;
        private Transform outlet;
        private LastRouteCarDriver driver;
        private LastRouteCarAudio sound;
        private LastRouteCarHeadlights beams;
        private MaterialPropertyBlock properties;
        private ParticleSystem exhaust;
        private bool coldRoad, appliedBrake, hasAppliedBrake;
        private float appliedBeam = -1f;

        internal void Initialize(LastRouteCarAssetRegistry car, LastRouteCarDriver motion, bool showColdExhaust)
        {
            driver = motion;
            outlet = car.ExhaustOutlet;
            coldRoad = showColdExhaust;
            sound = GetComponent<LastRouteCarAudio>();
            beams = GetComponent<LastRouteCarHeadlights>();
            var tailParts = new System.Collections.Generic.List<Renderer>(2);
            var headParts = new System.Collections.Generic.List<Renderer>(2);
            foreach (var part in car.Bindings)
            {
                if (part.Role == "tail_light") tailParts.Add(part.Renderer);
                if (part.Role == "headlight") headParts.Add(part.Renderer);
            }
            tails = tailParts.ToArray();
            heads = headParts.ToArray();
            properties = new MaterialPropertyBlock();
            WriteLenses(heads, 1f);
            ApplyLights();
        }

        private void LateUpdate()
        {
            if (driver == null || Time.deltaTime <= 0f) return;
            ApplyLights();
            if (!coldRoad || outlet == null || sound == null) return;
            bool running = sound.Engine.Phase == LastRouteCarEnginePhase.Running;
            if (running && exhaust == null) BuildExhaust();
            if (exhaust == null) return;
            if (running)
            {
                exhaust.transform.SetPositionAndRotation(outlet.position,
                    Quaternion.LookRotation(-transform.forward + Vector3.up * .19f));
                if (!exhaust.isEmitting) exhaust.Play();
            }
            else if (exhaust.isEmitting)
                exhaust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void ApplyLights()
        {
            bool brake = driver.IsDriving && driver.Model != null &&
                driver.Model.LongitudinalAcceleration < -.46f && driver.Speed > .25f;
            if (!hasAppliedBrake || brake != appliedBrake)
            {
                WriteLenses(tails, brake ? 2.25f : 1f);
                hasAppliedBrake = true;
                appliedBrake = brake;
            }

            if (beams == null) return;
            float power = beams.Power;
            if (Mathf.Abs(power - appliedBeam) <= .001f) return;
            WriteLenses(heads, power <= 0f ? 0f : Mathf.Lerp(.48f, 1f, Mathf.Sqrt(power)));
            appliedBeam = power;
        }

        private void WriteLenses(Renderer[] group, float factor)
        {
            foreach (Renderer lens in group)
            {
                if (lens == null) continue;
                lens.GetPropertyBlock(properties);
                bool headlight = ReferenceEquals(group, heads);
                if (headlight) properties.SetColor(BaseColor, LastRouteCarHeadlights.LensColor);
                properties.SetColor(Emission, (headlight
                    ? LastRouteCarHeadlights.LensColor * LastRouteCarHeadlights.LensEmission
                    : lens.sharedMaterial.GetColor(Emission)) * factor);
                lens.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void BuildExhaust()
        {
            var holder = new GameObject("Cold Exhaust");
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);
            exhaust = holder.AddComponent<ParticleSystem>();
            var main = exhaust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            main.maxParticles = 12;
            main.startLifetime = .86f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .54f);
            main.startSize = new ParticleSystem.MinMaxCurve(.085f, .13f);
            main.startColor = new Color(.63f, .66f, .65f, .23f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            exhaust.useAutoRandomSeed = false;
            exhaust.randomSeed = 470307;
            var shape = exhaust.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = .02f;
            var emission = exhaust.emission;
            emission.rateOverTime = 8f;
            var colour = exhaust.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.75f, .19f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var size = exhaust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .72f, 1f, 2.2f));
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .045f;
            holder.SetActive(true);
        }

        private void OnDisable()
        {
            if (exhaust != null) exhaust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (properties != null && hasAppliedBrake && appliedBrake) WriteLenses(tails, 1f);
            hasAppliedBrake = false;
            appliedBeam = -1f;
        }
    }
}
