using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class MothersHouseFireFlicker : MonoBehaviour
    {
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        private Light fireLight;
        private Renderer[] flames = Array.Empty<Renderer>();
        private Renderer embers;
        private Vector3[] baseScales = Array.Empty<Vector3>();
        private Quaternion[] baseRotations = Array.Empty<Quaternion>();
        private float baseIntensity;
        private float phase;
        private MaterialPropertyBlock properties;

        public Light FireLight => fireLight;
        public IReadOnlyList<Renderer> Flames => flames;
        public Renderer Embers => embers;

        public void Initialize(
            Light configuredLight,
            Renderer[] configuredFlames,
            Renderer configuredEmbers,
            uint seed)
        {
            if (configuredLight == null)
            {
                throw new ArgumentNullException(nameof(configuredLight));
            }

            if (configuredFlames == null || configuredFlames.Length != 2 ||
                configuredFlames[0] == null ||
                configuredFlames[1] == null)
            {
                throw new ArgumentException(
                    "The hearth requires its two imported flame layers.",
                    nameof(configuredFlames));
            }

            if (configuredEmbers == null)
            {
                throw new ArgumentNullException(nameof(configuredEmbers));
            }

            fireLight = configuredLight;
            flames = (Renderer[])configuredFlames.Clone();
            embers = configuredEmbers;
            baseIntensity = fireLight.intensity;
            phase = (seed & 1023u) / 1023f * Mathf.PI * 2f;
            properties = new MaterialPropertyBlock();
            baseScales = new Vector3[flames.Length];
            baseRotations = new Quaternion[flames.Length];
            for (int index = 0; index < flames.Length; index++)
            {
                Transform flame = flames[index].transform;
                baseScales[index] = flame.localScale;
                baseRotations[index] = flame.localRotation;
                flames[index].shadowCastingMode = ShadowCastingMode.Off;
                flames[index].receiveShadows = false;
            }

            embers.shadowCastingMode = ShadowCastingMode.Off;
            embers.receiveShadows = false;
            Apply(0f);
        }

        private void Update()
        {
            if (fireLight == null || flames.Length != 2)
            {
                return;
            }

            Apply(Time.time);
        }

        private void Apply(float time)
        {
            float low = Mathf.Sin(time * 2.17f + phase);
            float high = Mathf.Sin(time * 6.91f + phase * 0.47f);
            float flutter = Mathf.Sin(time * 11.3f + 1.7f);
            float intensityScale =
                0.96f + low * 0.035f + high * 0.018f;
            fireLight.intensity = baseIntensity * intensityScale;

            for (int index = 0; index < flames.Length; index++)
            {
                float sign = index == 0 ? -1f : 1f;
                Transform flame = flames[index].transform;
                Vector3 scale = baseScales[index];
                scale.x *= 1f + sign * high * 0.018f;
                scale.y *= 1f + low * 0.035f + flutter * 0.012f;
                scale.z *= 1f - sign * high * 0.015f;
                flame.localScale = scale;
                flame.localRotation = baseRotations[index] *
                    Quaternion.Euler(0f, 0f, sign * high * 0.75f);
                SetEmission(
                    flames[index],
                    new Color(
                        2.0f * intensityScale,
                        0.58f * intensityScale,
                        0.12f * intensityScale,
                        1f));
            }

            SetEmission(
                embers,
                new Color(
                    0.92f * intensityScale,
                    0.16f * intensityScale,
                    0.035f * intensityScale,
                    1f));
        }

        private void SetEmission(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(properties);
            properties.SetColor(EmissionColorId, color);
            renderer.SetPropertyBlock(properties);
            properties.Clear();
        }

        private void OnDisable()
        {
            if (fireLight != null)
            {
                fireLight.intensity = baseIntensity;
            }

            for (int index = 0; index < flames.Length; index++)
            {
                if (flames[index] == null)
                {
                    continue;
                }

                flames[index].transform.localScale = baseScales[index];
                flames[index].transform.localRotation =
                    baseRotations[index];
            }
        }
    }
}
