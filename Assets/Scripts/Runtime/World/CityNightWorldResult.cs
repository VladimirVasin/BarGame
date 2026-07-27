using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityNightWorldResult
    {
        private readonly IReadOnlyList<Vector3> barLightPositions;

        internal CityNightWorldResult(
            GameObject root,
            CityNightFixturePlan plan,
            IList<Transform> lampAnchors,
            IList<TrafficSignalController> trafficSignals,
            IList<Vector3> barPositions)
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
        }

        public GameObject Root { get; }
        public CityNightFixturePlan Plan { get; }
        public IReadOnlyList<Transform> LampAnchors { get; }
        public IReadOnlyList<TrafficSignalController> TrafficSignals { get; }
        public CityNightAtmosphere Atmosphere { get; private set; }
        public CityFogField FogField { get; private set; }

        public void InitializeLighting(Transform player, int citySeed)
        {
            if (Atmosphere != null)
            {
                return;
            }

            Atmosphere = Root.AddComponent<CityNightAtmosphere>();
            Atmosphere.Initialize(player, LampAnchors, barLightPositions);
            FogField = Root.AddComponent<CityFogField>();
            FogField.Initialize(
                player,
                CityNightResources.AtmosphereMaterial,
                citySeed);
        }
    }
}
