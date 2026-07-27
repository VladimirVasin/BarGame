using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityNightWorldBuilder
    {
        private static readonly Color FixtureColor =
            new Color(0.085f, 0.095f, 0.120f);
        private static readonly Color LampGlow =
            new Color(3.20f, 1.65f, 0.45f);
        private static readonly Color SignalHousing =
            new Color(0.070f, 0.080f, 0.095f);
        private static readonly Color SignalRed =
            new Color(0.28f, 0.022f, 0.014f);
        private static readonly Color SignalAmber =
            new Color(0.10f, 0.045f, 0.012f);
        private static readonly Color SignalGreen =
            new Color(0.014f, 0.18f, 0.060f);
        private static readonly Color SignalHaloInner =
            new Color(3.4f, 1.65f, 0.38f, 0.16f);
        private static readonly Color SignalHaloOuter =
            new Color(2.0f, 0.95f, 0.22f, 0.035f);

        public static CityNightWorldResult Build(
            Transform parent,
            CityNightFixturePlan plan,
            IReadOnlyList<BarEntrance> bars)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (bars == null)
            {
                throw new ArgumentNullException(nameof(bars));
            }

            Transform root = new GameObject("Night Street Furniture").transform;
            root.SetParent(parent, false);
            Material emissiveMaterial = CityNightResources.EmissiveMaterial;

            var lampAnchors =
                new List<Transform>(plan.StreetLamps.Count);
            for (int index = 0; index < plan.StreetLamps.Count; index++)
            {
                lampAnchors.Add(BuildStreetLamp(
                    root,
                    plan.StreetLamps[index],
                    emissiveMaterial,
                    index));
            }

            var trafficSignals = BuildTrafficSignals(
                root,
                plan.TrafficSignals,
                emissiveMaterial);
            var barLightPositions = new List<Vector3>(bars.Count);
            for (int index = 0; index < bars.Count; index++)
            {
                Vector3 position = bars[index].transform.position;
                position.y = 2.45f;
                barLightPositions.Add(position);
            }

            return new CityNightWorldResult(
                root.gameObject,
                plan,
                lampAnchors,
                trafficSignals,
                barLightPositions);
        }

        private static Transform BuildStreetLamp(
            Transform parent,
            StreetLampDescriptor descriptor,
            Material emissiveMaterial,
            int index)
        {
            Transform lamp = new GameObject($"Street Lamp {index + 1}").transform;
            lamp.SetParent(parent, false);
            lamp.localPosition = descriptor.Position;
            lamp.localRotation = Quaternion.LookRotation(
                descriptor.Forward,
                Vector3.up);

            RuntimePrimitiveFactory.CreateCylinder(
                "Pole",
                lamp,
                new Vector3(0f, 1.65f, 0f),
                new Vector3(0.09f, 1.65f, 0.09f),
                FixtureColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Arm",
                lamp,
                new Vector3(0f, 3.24f, 0.30f),
                new Vector3(0.10f, 0.10f, 0.70f),
                FixtureColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Hood",
                lamp,
                new Vector3(0f, 3.18f, 0.66f),
                new Vector3(0.40f, 0.13f, 0.34f),
                FixtureColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Glowing Bulb",
                lamp,
                new Vector3(0f, 3.10f, 0.67f),
                new Vector3(0.24f, 0.10f, 0.22f),
                LampGlow,
                emissiveMaterial,
                false);

            Transform anchor = new GameObject("Light Anchor").transform;
            anchor.SetParent(lamp, false);
            anchor.localPosition = new Vector3(0f, 2.92f, 0.67f);
            return anchor;
        }

        private static List<TrafficSignalController> BuildTrafficSignals(
            Transform parent,
            IReadOnlyList<TrafficSignalDescriptor> descriptors,
            Material emissiveMaterial)
        {
            var controllers = new List<TrafficSignalController>(
                descriptors.Count / 2);
            int descriptorIndex = 0;
            while (descriptorIndex < descriptors.Count)
            {
                Vector2Int node =
                    descriptors[descriptorIndex].IntersectionNode;
                Transform intersection = new GameObject(
                    $"Traffic Signal Intersection {node.x}-{node.y}").transform;
                intersection.SetParent(parent, false);
                var amberLenses = new List<Renderer>(2);
                var amberHalos = new List<CityLightHalo>(2);
                float phase01 =
                    descriptors[descriptorIndex].BlinkPhase01;

                while (descriptorIndex < descriptors.Count &&
                       descriptors[descriptorIndex].IntersectionNode == node)
                {
                    TrafficSignalVisual visual = BuildTrafficSignalHead(
                        intersection,
                        descriptors[descriptorIndex],
                        emissiveMaterial);
                    amberLenses.Add(visual.Lens);
                    amberHalos.Add(visual.Halo);
                    descriptorIndex++;
                }

                TrafficSignalController controller =
                    intersection.gameObject.AddComponent<
                        TrafficSignalController>();
                controller.Initialize(
                    amberLenses,
                    amberHalos,
                    phase01 * TrafficSignalController.BlinkPeriod);
                controllers.Add(controller);
            }

            return controllers;
        }

        private static TrafficSignalVisual BuildTrafficSignalHead(
            Transform parent,
            TrafficSignalDescriptor descriptor,
            Material emissiveMaterial)
        {
            Transform signal = new GameObject(
                $"Traffic Signal {descriptor.PairIndex + 1}").transform;
            signal.SetParent(parent, false);
            signal.localPosition = descriptor.Position;
            signal.localRotation = Quaternion.LookRotation(
                descriptor.Forward,
                Vector3.up);

            RuntimePrimitiveFactory.CreateCylinder(
                "Signal Pole",
                signal,
                new Vector3(0f, 1.28f, 0f),
                new Vector3(0.075f, 1.28f, 0.075f),
                FixtureColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Signal Housing",
                signal,
                new Vector3(0f, 2.55f, 0.06f),
                new Vector3(0.50f, 1.02f, 0.28f),
                SignalHousing,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Red Lens",
                signal,
                new Vector3(0f, 2.86f, 0.22f),
                new Vector3(0.30f, 0.24f, 0.10f),
                SignalRed,
                emissiveMaterial,
                false);
            GameObject amber = RuntimePrimitiveFactory.CreateBox(
                "Amber Lens",
                signal,
                new Vector3(0f, 2.55f, 0.20f),
                new Vector3(0.30f, 0.24f, 0.10f),
                SignalAmber,
                emissiveMaterial,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Green Lens",
                signal,
                new Vector3(0f, 2.24f, 0.22f),
                new Vector3(0.30f, 0.24f, 0.10f),
                SignalGreen,
                emissiveMaterial,
                false);

            GameObject haloObject = new GameObject("Amber Fog Halo");
            haloObject.transform.SetParent(amber.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.34f,
                0.82f,
                SignalHaloInner,
                SignalHaloOuter);
            return new TrafficSignalVisual(
                amber.GetComponent<Renderer>(),
                halo);
        }

        private readonly struct TrafficSignalVisual
        {
            public TrafficSignalVisual(
                Renderer lens,
                CityLightHalo halo)
            {
                Lens = lens;
                Halo = halo;
            }

            public Renderer Lens { get; }
            public CityLightHalo Halo { get; }
        }
    }
}
