using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityBusStopWorldBuilder
    {
        /// <summary>
        /// The shelter stands beside the `01` pole, not on it: shifted
        /// along the lane so the pole, both door docks (`+3.05 m` and
        /// `-1.34 m`) and the pavement wait slots (`+0.30..+1.40 m`)
        /// all stay clear of the structure.
        /// </summary>
        public const float ShelterOffsetAlongLane = -2.55f;

        // The bench under the roof, in stop-local space. Local +Z faces
        // the road, so the sitter watches for the bus.
        public const float ShelterBenchSeatTopHeight = 0.80f;
        public const float ShelterBenchSeatThickness = 0.16f;
        public const float ShelterBenchSeatWidth = 2.65f;
        public const float ShelterBenchSeatDepth = 0.62f;
        public const float ShelterBenchSeatForwardOffset = 0.05f;

        // A wrong-side walk to this bench must round the whole shelter:
        // the corner posts stand 2.05 m out and the back wall spans
        // 4.25 m, so the detour swings wider than around open timber.
        public const float ShelterApproachClearance = 1.30f;

        private static readonly Color PoleColor =
            new Color32(45, 49, 54, 255);
        private static readonly Color RouteColor =
            new Color32(91, 143, 209, 255);
        private static readonly Color PlateInsetColor =
            new Color32(202, 218, 220, 255);
        private static readonly Color ShelterPanelColor =
            new Color32(63, 70, 74, 255);
        private static readonly Color ShelterRoofColor =
            new Color32(52, 58, 63, 255);
        private static readonly Color ShelterBenchColor =
            new Color32(97, 56, 26, 255);

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
            CityMiscAssetProvider miscProvider =
                CityMiscAssetProvider.Load();
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                CityBusStopDescriptor stop = plan.Stops[index];
                BuildStop(
                    root,
                    stop,
                    miscProvider);
            }

            return root.gameObject;
        }

        private static GameObject BuildStop(
            Transform parent,
            CityBusStopDescriptor stop,
            CityMiscAssetProvider miscProvider)
        {
            Transform root = CreateStopRoot(parent, stop);
            root.SetPositionAndRotation(
                stop.ShelterPosition,
                ResolveRotation(
                    stop.Forward,
                    stop.RoadsideForward));
            BuildStopVisual(root, true, miscProvider);
            return root.gameObject;
        }

        internal static GameObject BuildLocalStop(
            Transform parent,
            CityBusStopDescriptor stop,
            Vector3 localShelterPosition,
            Vector3 localRouteForward,
            Vector3 localRoadsideForward,
            bool collider)
        {
            Transform root = CreateStopRoot(parent, stop);
            root.localPosition = localShelterPosition;
            root.localRotation = ResolveRotation(
                localRouteForward,
                localRoadsideForward);
            BuildStopVisual(
                root,
                collider,
                CityMiscAssetProvider.Load());
            return root.gameObject;
        }

        private static Quaternion ResolveRotation(
            Vector3 routeForward,
            Vector3 roadsideForward)
        {
            routeForward.y = 0f;
            routeForward = routeForward.sqrMagnitude > 0.0001f
                ? routeForward.normalized
                : Vector3.forward;
            roadsideForward.y = 0f;
            roadsideForward = roadsideForward.sqrMagnitude > 0.0001f
                ? roadsideForward.normalized
                : -new Vector3(
                    routeForward.z,
                    0f,
                    -routeForward.x);
            return Quaternion.LookRotation(
                roadsideForward,
                Vector3.up);
        }

        private static Transform CreateStopRoot(
            Transform parent,
            CityBusStopDescriptor stop)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (stop == null)
            {
                throw new ArgumentNullException(nameof(stop));
            }

            Transform root = new GameObject(
                $"Bus Stop {stop.SequenceIndex + 1:00}").transform;
            root.SetParent(parent, false);
            return root;
        }

        /// <summary>
        /// Describes the shelter bench of one stop in world space, so
        /// the sit interaction docks against exactly the plank this
        /// builder draws. The sitter faces local +Z: the road.
        /// </summary>
        public static CityBenchSeat DescribeShelterBenchSeat(
            CityBusStopDescriptor stop)
        {
            if (stop == null)
            {
                throw new ArgumentNullException(nameof(stop));
            }

            Quaternion rotation = ResolveRotation(
                stop.Forward,
                stop.RoadsideForward);
            Vector3 seatTopCenter = stop.ShelterPosition +
                rotation * new Vector3(
                    ShelterOffsetAlongLane,
                    ShelterBenchSeatTopHeight,
                    ShelterBenchSeatForwardOffset);
            return new CityBenchSeat(
                $"{stop.Id}-shelter-bench",
                seatTopCenter,
                ShelterBenchSeatWidth,
                ShelterBenchSeatDepth,
                stop.ShelterPosition.y,
                rotation * Vector3.forward,
                ShelterApproachClearance);
        }

        private static void BuildStopVisual(
            Transform root,
            bool collider,
            CityMiscAssetProvider miscProvider)
        {
            bool importedPole = BuildPole(
                root,
                collider,
                miscProvider);
            BuildShelter(root, collider, miscProvider);
            if (importedPole)
            {
                return;
            }

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

        /// <summary>
        /// The waiting shelter, adapted from the retired roadside
        /// decoration recipe: back wall on the frontage side, roof
        /// over the pavement edge, bench facing the road. The sign
        /// pole of the recipe is dropped — the real `01` pole already
        /// stands beside the structure.
        /// </summary>
        private static void BuildShelter(
            Transform root,
            bool collider,
            CityMiscAssetProvider miscProvider)
        {
            const float x = ShelterOffsetAlongLane;
            if (TryBuildImportedShell(
                    root,
                    miscProvider,
                    CityMiscKind.Route01ShelterShell,
                    Vector3.zero,
                    "Imported Route 01 Shelter Shell",
                    ResolveShelterColor))
            {
                if (collider)
                {
                    AddShelterColliders(root);
                }

                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                "Shelter Back Wall",
                root,
                new Vector3(x, 1.32f, -0.48f),
                new Vector3(4.25f, 2.45f, 0.10f),
                ShelterPanelColor,
                collider);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Roof",
                root,
                new Vector3(x, 2.62f, 0f),
                new Vector3(4.65f, 0.18f, 1.18f),
                ShelterRoofColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Post West",
                root,
                new Vector3(x - 2.05f, 1.30f, 0f),
                new Vector3(0.16f, 2.60f, 0.16f),
                PoleColor,
                collider);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Post Back",
                root,
                new Vector3(x, 1.30f, -0.48f),
                new Vector3(0.16f, 2.60f, 0.16f),
                PoleColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Post East",
                root,
                new Vector3(x + 2.05f, 1.30f, 0f),
                new Vector3(0.16f, 2.60f, 0.16f),
                PoleColor,
                collider);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Bench Seat",
                root,
                new Vector3(
                    x,
                    ShelterBenchSeatTopHeight -
                    ShelterBenchSeatThickness * 0.5f,
                    ShelterBenchSeatForwardOffset),
                new Vector3(
                    ShelterBenchSeatWidth,
                    ShelterBenchSeatThickness,
                    ShelterBenchSeatDepth),
                ShelterBenchColor,
                collider);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Bench Backrest",
                root,
                new Vector3(x, 1.10f, -0.20f),
                new Vector3(2.65f, 0.62f, 0.14f),
                ShelterBenchColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Bench Leg West",
                root,
                new Vector3(x - 0.90f, 0.34f, 0.05f),
                new Vector3(0.14f, 0.68f, 0.14f),
                PoleColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Shelter Bench Leg East",
                root,
                new Vector3(x + 0.90f, 0.34f, 0.05f),
                new Vector3(0.14f, 0.68f, 0.14f),
                PoleColor,
                false);
        }

        private static bool BuildPole(
            Transform root,
            bool collider,
            CityMiscAssetProvider miscProvider)
        {
            if (TryBuildImportedShell(
                    root,
                    miscProvider,
                    CityMiscKind.Route01PoleShell,
                    Vector3.zero,
                    "Imported Route 01 Pole Shell",
                    ResolvePolePartColor))
            {
                if (collider)
                {
                    AddProxy(
                        root,
                        "Route 01 Pole Collider",
                        new Vector3(0f, 1.20f, 0f),
                        new Vector3(0.12f, 2.40f, 0.12f));
                }

                return true;
            }

            RuntimePrimitiveFactory.CreateBox(
                "Pole",
                root,
                new Vector3(0f, 1.20f, 0f),
                new Vector3(0.12f, 2.40f, 0.12f),
                PoleColor,
                collider);
            return false;
        }

        private static Color ResolvePolePartColor(
            CityMiscMeshRole role)
        {
            switch (role)
            {
                case CityMiscMeshRole.Street:
                    return RouteColor;
                case CityMiscMeshRole.Residential:
                    return PlateInsetColor;
                default:
                    return PoleColor;
            }
        }

        private static void AddShelterColliders(Transform root)
        {
            const float x = ShelterOffsetAlongLane;
            AddProxy(
                root,
                "Shelter Back Wall Collider",
                new Vector3(x, 1.32f, -0.48f),
                new Vector3(4.25f, 2.45f, 0.10f));
            AddProxy(
                root,
                "Shelter Post West Collider",
                new Vector3(x - 2.05f, 1.30f, 0f),
                new Vector3(0.16f, 2.60f, 0.16f));
            AddProxy(
                root,
                "Shelter Post East Collider",
                new Vector3(x + 2.05f, 1.30f, 0f),
                new Vector3(0.16f, 2.60f, 0.16f));
            AddProxy(
                root,
                "Shelter Bench Seat Collider",
                new Vector3(
                    x,
                    ShelterBenchSeatTopHeight -
                    ShelterBenchSeatThickness * 0.5f,
                    ShelterBenchSeatForwardOffset),
                new Vector3(
                    ShelterBenchSeatWidth,
                    ShelterBenchSeatThickness,
                    ShelterBenchSeatDepth));
        }

        private static void AddProxy(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 size)
        {
            Transform proxy = new GameObject(name).transform;
            proxy.SetParent(parent, false);
            proxy.localPosition = localPosition;
            BoxCollider collider = proxy.gameObject.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static bool TryBuildImportedShell(
            Transform root,
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            Vector3 localPosition,
            string name,
            Func<CityMiscMeshRole, Color> resolveColor)
        {
            if (provider == null ||
                !CityMiscAssetProvider.Supports(kind))
            {
                return false;
            }

            var parts = new List<CityMiscMeshPart>();
            try
            {
                int partCount = CityMiscAssetProvider.GetPartCount(kind);
                if (partCount < 1)
                {
                    return false;
                }

                for (int partIndex = 0;
                     partIndex < partCount;
                     partIndex++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(
                        kind,
                        0,
                        partIndex);
                    if (part.Mesh == null)
                    {
                        return false;
                    }

                    parts.Add(part);
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CityMiscMeshPart part = parts[index];
                RuntimePrimitiveFactory.CreateCombinedMeshes(
                    $"{name} {part.Role}",
                    root,
                    new[]
                    {
                        new RuntimeMeshPlacement(
                            part.Mesh,
                            localPosition,
                            Quaternion.identity)
                    },
                    resolveColor(part.Role));
            }

            return true;
        }

        private static Color ResolveShelterColor(
            CityMiscMeshRole role)
        {
            switch (role)
            {
                case CityMiscMeshRole.Timber:
                    return ShelterBenchColor;
                case CityMiscMeshRole.Residential:
                case CityMiscMeshRole.Masonry:
                    return ShelterPanelColor;
                case CityMiscMeshRole.BacklitSign:
                    return PlateInsetColor;
                default:
                    return ShelterRoofColor;
            }
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
