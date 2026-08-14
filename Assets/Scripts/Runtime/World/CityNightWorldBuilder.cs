using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityNightWorldBuilder
    {
        private const float LampSpatialChunkSize = 48f;

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
            var lampChunks =
                new Dictionary<LampChunkCoordinate, LampChunkGeometry>();
            for (int index = 0; index < plan.StreetLamps.Count; index++)
            {
                StreetLampDescriptor descriptor =
                    plan.StreetLamps[index];
                lampAnchors.Add(CreateStreetLampAnchor(
                    root,
                    descriptor,
                    index));
                AddStreetLampGeometry(lampChunks, descriptor);
            }

            var streetLampBulbRenderers =
                new List<Renderer>(lampChunks.Count);
            BuildStreetLampChunks(
                root,
                lampChunks,
                emissiveMaterial,
                streetLampBulbRenderers);
            var trafficSignals = BuildTrafficSignals(
                root,
                plan.TrafficSignals,
                emissiveMaterial);
            var barLightPositions = new List<Vector3>(bars.Count);
            for (int index = 0; index < bars.Count; index++)
            {
                Vector3 position = bars[index].transform.position;
                position.y += 1.63f;
                barLightPositions.Add(position);
            }

            return new CityNightWorldResult(
                root.gameObject,
                plan,
                lampAnchors,
                trafficSignals,
                barLightPositions,
                streetLampBulbRenderers,
                LampGlow);
        }

        private static Transform CreateStreetLampAnchor(
            Transform parent,
            StreetLampDescriptor descriptor,
            int index)
        {
            Quaternion rotation = Quaternion.LookRotation(
                descriptor.Forward,
                Vector3.up);
            Transform anchor = new GameObject(
                $"Street Lamp Anchor {index + 1}").transform;
            anchor.SetParent(parent, false);
            anchor.localPosition =
                descriptor.Position +
                (rotation * new Vector3(0f, 4.70f, 1.07f));
            anchor.localRotation = rotation;
            return anchor;
        }

        private static void AddStreetLampGeometry(
            IDictionary<LampChunkCoordinate, LampChunkGeometry> chunks,
            StreetLampDescriptor descriptor)
        {
            LampChunkCoordinate coordinate =
                LampChunkCoordinate.FromPosition(descriptor.Position);
            if (!chunks.TryGetValue(
                    coordinate,
                    out LampChunkGeometry geometry))
            {
                geometry = new LampChunkGeometry();
                chunks.Add(coordinate, geometry);
            }

            Quaternion rotation = Quaternion.LookRotation(
                descriptor.Forward,
                Vector3.up);
            Vector3 origin = coordinate.Origin;
            // Full-height street furniture: the base stays exactly on
            // the planned position, only the mast grew to a realistic
            // 5.3 m with every part scaled in proportion.
            geometry.FixtureBoxes.Add(CreateLampBox(
                descriptor.Position,
                rotation,
                origin,
                new Vector3(0f, 2.65f, 0f),
                new Vector3(0.14f, 5.30f, 0.14f)));
            geometry.FixtureBoxes.Add(CreateLampBox(
                descriptor.Position,
                rotation,
                origin,
                new Vector3(0f, 5.22f, 0.48f),
                new Vector3(0.15f, 0.15f, 1.12f)));
            geometry.FixtureBoxes.Add(CreateLampBox(
                descriptor.Position,
                rotation,
                origin,
                new Vector3(0f, 5.12f, 1.06f),
                new Vector3(0.62f, 0.20f, 0.54f)));
            geometry.BulbBoxes.Add(CreateLampBox(
                descriptor.Position,
                rotation,
                origin,
                new Vector3(0f, 5.00f, 1.07f),
                new Vector3(0.38f, 0.15f, 0.35f)));
            geometry.CollisionBoxes.Add(
                CityStaticCollisionBuilder.CreateLowerPoleBounds(
                    descriptor.Position - origin));
        }

        private static Bounds CreateLampBox(
            Vector3 fixturePosition,
            Quaternion fixtureRotation,
            Vector3 chunkOrigin,
            Vector3 localCenter,
            Vector3 localSize)
        {
            Vector3 right = fixtureRotation * Vector3.right;
            Vector3 up = fixtureRotation * Vector3.up;
            Vector3 forward = fixtureRotation * Vector3.forward;
            Vector3 size =
                (Absolute(right) * localSize.x) +
                (Absolute(up) * localSize.y) +
                (Absolute(forward) * localSize.z);
            Vector3 center =
                fixturePosition +
                (fixtureRotation * localCenter) -
                chunkOrigin;
            return new Bounds(center, size);
        }

        private static void BuildStreetLampChunks(
            Transform parent,
            IDictionary<LampChunkCoordinate, LampChunkGeometry> chunks,
            Material emissiveMaterial,
            ICollection<Renderer> bulbRenderers)
        {
            var coordinates =
                new List<LampChunkCoordinate>(chunks.Keys);
            coordinates.Sort(LampChunkCoordinate.Compare);
            for (int index = 0; index < coordinates.Count; index++)
            {
                LampChunkCoordinate coordinate = coordinates[index];
                LampChunkGeometry geometry = chunks[coordinate];
                Transform chunk = new GameObject(
                    $"Street Lamp Chunk {coordinate.X} {coordinate.Z}")
                    .transform;
                chunk.SetParent(parent, false);
                chunk.localPosition = coordinate.Origin;

                CityStaticCollisionBuilder.AddBoxColliders(
                    chunk,
                    geometry.CollisionBoxes);

                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Street Lamp Fixtures",
                    chunk,
                    geometry.FixtureBoxes,
                    FixtureColor);
                GameObject bulbs =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Street Lamp Bulbs",
                        chunk,
                        geometry.BulbBoxes,
                        LampGlow,
                        emissiveMaterial);
                bulbRenderers.Add(bulbs.GetComponent<Renderer>());
            }
        }

        private static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
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
            CityStaticCollisionBuilder.AddLowerPoleCollider(signal);

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

        private readonly struct LampChunkCoordinate :
            IEquatable<LampChunkCoordinate>
        {
            public LampChunkCoordinate(int x, int z)
            {
                X = x;
                Z = z;
            }

            public int X { get; }
            public int Z { get; }
            public Vector3 Origin =>
                new Vector3(
                    X * LampSpatialChunkSize,
                    0f,
                    Z * LampSpatialChunkSize);

            public bool Equals(LampChunkCoordinate other)
            {
                return X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is LampChunkCoordinate other &&
                       Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Z;
                }
            }

            public static LampChunkCoordinate FromPosition(
                Vector3 position)
            {
                return new LampChunkCoordinate(
                    Mathf.FloorToInt(
                        position.x / LampSpatialChunkSize),
                    Mathf.FloorToInt(
                        position.z / LampSpatialChunkSize));
            }

            public static int Compare(
                LampChunkCoordinate left,
                LampChunkCoordinate right)
            {
                int zComparison = left.Z.CompareTo(right.Z);
                return zComparison != 0
                    ? zComparison
                    : left.X.CompareTo(right.X);
            }
        }

        private sealed class LampChunkGeometry
        {
            public List<Bounds> FixtureBoxes { get; } =
                new List<Bounds>();
            public List<Bounds> BulbBoxes { get; } =
                new List<Bounds>();
            public List<Bounds> CollisionBoxes { get; } =
                new List<Bounds>();
        }
    }
}
