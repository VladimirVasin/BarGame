using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityBusStopWorldBuilder
    {
        private static readonly Color PoleColor =
            new Color32(45, 49, 54, 255);
        private static readonly Color RouteColor =
            new Color32(91, 143, 209, 255);
        private static readonly Color PlateInsetColor =
            new Color32(202, 218, 220, 255);

        public static GameObject Build(
            Transform parent,
            CityBusPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject("City Bus Stops").transform;
            root.SetParent(parent, false);
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                BuildStop(root, plan.Stops[index]);
            }

            return root.gameObject;
        }

        private static void BuildStop(
            Transform parent,
            CityBusStopDescriptor stop)
        {
            Vector3 routeForward = stop.Forward;
            routeForward.y = 0f;
            routeForward = routeForward.sqrMagnitude > 0.0001f
                ? routeForward.normalized
                : Vector3.forward;
            Vector3 roadsideForward = stop.RoadsideForward;
            roadsideForward.y = 0f;
            roadsideForward = roadsideForward.sqrMagnitude > 0.0001f
                ? roadsideForward.normalized
                : -new Vector3(
                    routeForward.z,
                    0f,
                    -routeForward.x);
            Transform root = new GameObject(
                $"Bus Stop {stop.SequenceIndex + 1:00}").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(
                stop.ShelterPosition,
                Quaternion.LookRotation(roadsideForward, Vector3.up));

            RuntimePrimitiveFactory.CreateBox(
                "Pole",
                root,
                new Vector3(0f, 1.20f, 0f),
                new Vector3(0.12f, 2.40f, 0.12f),
                PoleColor,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Route Plate",
                root,
                new Vector3(0f, 2.18f, 0f),
                new Vector3(0.76f, 0.56f, 0.10f),
                RouteColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Route Plate Inset",
                root,
                new Vector3(0f, 2.18f, 0.056f),
                new Vector3(0.58f, 0.38f, 0.025f),
                PlateInsetColor,
                false);
            BuildRouteNumber(root);
        }

        private static void BuildRouteNumber(Transform parent)
        {
            const float z = 0.075f;
            CreateDigitStroke(
                "Zero Top",
                parent,
                new Vector3(-0.16f, 2.30f, z),
                new Vector3(0.18f, 0.045f, 0.018f));
            CreateDigitStroke(
                "Zero Bottom",
                parent,
                new Vector3(-0.16f, 2.06f, z),
                new Vector3(0.18f, 0.045f, 0.018f));
            CreateDigitStroke(
                "Zero Left",
                parent,
                new Vector3(-0.25f, 2.18f, z),
                new Vector3(0.045f, 0.28f, 0.018f));
            CreateDigitStroke(
                "Zero Right",
                parent,
                new Vector3(-0.07f, 2.18f, z),
                new Vector3(0.045f, 0.28f, 0.018f));
            CreateDigitStroke(
                "One",
                parent,
                new Vector3(0.18f, 2.18f, z),
                new Vector3(0.05f, 0.30f, 0.018f));
        }

        private static void CreateDigitStroke(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                position,
                size,
                PoleColor,
                false);
        }
    }
}
