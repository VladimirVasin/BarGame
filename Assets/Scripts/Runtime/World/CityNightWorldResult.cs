using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityNightWorldResult
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private readonly IReadOnlyList<Vector3> barLightPositions;
        private readonly Color streetLampBulbNightColor;
        private readonly MaterialPropertyBlock bulbColorProperties =
            new MaterialPropertyBlock();
        private float nightFactor = 1f;
        private bool hasAppliedNightFactor;

        internal CityNightWorldResult(
            GameObject root,
            CityNightFixturePlan plan,
            IList<Transform> lampAnchors,
            IList<TrafficSignalController> trafficSignals,
            IList<Vector3> barPositions,
            IList<Renderer> streetLampBulbRenderers,
            Color streetLampBulbColor)
        {
            Root = root != null
                ? root
                : throw new ArgumentNullException(nameof(root));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            LampAnchors = new ReadOnlyCollection<Transform>(
                new List<Transform>(lampAnchors));
            TrafficSignals =
                new ReadOnlyCollection<TrafficSignalController>(
                    new List<TrafficSignalController>(trafficSignals));
            barLightPositions = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(barPositions));
            StreetLampBulbRenderers =
                new ReadOnlyCollection<Renderer>(
                    new List<Renderer>(streetLampBulbRenderers));
            streetLampBulbNightColor = streetLampBulbColor;
        }

        public GameObject Root { get; }
        public CityNightFixturePlan Plan { get; }
        public IReadOnlyList<Transform> LampAnchors { get; }
        public IReadOnlyList<TrafficSignalController> TrafficSignals { get; }
        public IReadOnlyList<Renderer> StreetLampBulbRenderers { get; }
        public CityNightAtmosphere Atmosphere { get; private set; }
        public CityFogField FogField { get; private set; }
        public float NightFactor => nightFactor;

        public void SetNightFactor(
            float factor,
            bool force = false)
        {
            float clampedFactor = Mathf.Clamp01(factor);
            if (!force &&
                hasAppliedNightFactor &&
                clampedFactor.Equals(nightFactor))
            {
                return;
            }

            nightFactor = clampedFactor;
            hasAppliedNightFactor = true;

            // The lit facade windows follow the same clock as the lamp
            // bulbs: five shared family materials dim instead of
            // thousands of pane renderers. The registered electric
            // glows (neon, sign lightboxes, porch and site lamps)
            // follow it too.
            CityWindowAppearance.SetNightFactor(nightFactor);
            CityNightGlowRegistry.SetNightFactor(nightFactor);
            Color displayedBulbColor = new Color(
                streetLampBulbNightColor.r * nightFactor,
                streetLampBulbNightColor.g * nightFactor,
                streetLampBulbNightColor.b * nightFactor,
                streetLampBulbNightColor.a);
            for (int index = 0;
                 index < StreetLampBulbRenderers.Count;
                 index++)
            {
                Renderer renderer = StreetLampBulbRenderers[index];
                if (renderer == null)
                {
                    continue;
                }

                bulbColorProperties.Clear();
                renderer.GetPropertyBlock(bulbColorProperties);
                bulbColorProperties.SetColor(
                    BaseColorId,
                    displayedBulbColor);
                bulbColorProperties.SetColor(
                    ColorId,
                    displayedBulbColor);
                renderer.SetPropertyBlock(bulbColorProperties);
            }

            if (Atmosphere != null)
            {
                Atmosphere.SetNightFactor(nightFactor, force);
            }
        }

        public void InitializeLighting(Transform player, int citySeed)
        {
            InitializeAtmosphere(
                player,
                barLightPositions);
            if (FogField != null)
            {
                return;
            }

            FogField = Root.AddComponent<CityFogField>();
            FogField.Initialize(
                player,
                CityNightResources.AtmosphereMaterial,
                citySeed);
        }

        public void InitializeAtmosphere(
            Transform player,
            IReadOnlyList<Vector3> lightPositions)
        {
            if (Atmosphere != null)
            {
                return;
            }

            if (lightPositions == null)
            {
                throw new ArgumentNullException(nameof(lightPositions));
            }

            Atmosphere = Root.AddComponent<CityNightAtmosphere>();
            Atmosphere.Initialize(
                player,
                LampAnchors,
                lightPositions);
            Atmosphere.SetNightFactor(nightFactor);
        }
    }
}
