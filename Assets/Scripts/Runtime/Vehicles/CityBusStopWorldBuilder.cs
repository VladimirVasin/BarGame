using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

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
        private static readonly Color StopPlateEnamel = new Color32(30, 51, 69, 255);
        private static readonly Color StopPlateInk = new Color32(249, 244, 223, 255);
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
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return Build(parent, new[] { plan });
        }

        public static GameObject Build(Transform parent, IReadOnlyList<CityBusPlan> plans)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plans == null) throw new ArgumentNullException(nameof(plans));
            Stopwatch timer = Stopwatch.StartNew();
            Transform root = new GameObject("City Bus Stops").transform;
            root.SetParent(parent, false);
            CityMiscAssetProvider miscProvider =
                CityMiscAssetProvider.Load();
            foreach (var platform in CityBusNetworkFurniture.Platforms(plans))
                BuildStop(root, platform.stop, miscProvider, new CityBusStopLabel(platform.line.Number,
                    platform.ordinal, platform.stop.TransferNumber, platform.transferOrdinal, platform.shared));

            // One row for the whole roster: every shelter combines the
            // imported pole and shell parts into meshes of its own.
            CityBusPlanner.ReportPhase(
                "stop_world",
                timer,
                GameLog.Field("stops", root.childCount),
                GameLog.Field("imported", miscProvider != null));
            return root.gameObject;
        }

        private static GameObject BuildStop(
            Transform parent,
            CityBusStopDescriptor stop,
            CityMiscAssetProvider miscProvider, CityBusStopLabel label)
        {
            Transform root = CreateStopRoot(parent, stop);
            root.SetPositionAndRotation(
                stop.ShelterPosition,
                ResolveRotation(
                    stop.Forward,
                    stop.RoadsideForward));
            BuildStopVisual(root, true, miscProvider, label);
            return root.gameObject;
        }

        internal static GameObject BuildLocalStop(
            Transform parent,
            CityBusStopDescriptor stop,
            Vector3 localShelterPosition,
            Vector3 localRouteForward,
            Vector3 localRoadsideForward,
            bool collider, int routeNumber)
        {
            Transform root = CreateStopRoot(parent, stop);
            root.localPosition = localShelterPosition;
            root.localRotation = ResolveRotation(
                localRouteForward,
                localRoadsideForward);
            BuildStopVisual(
                root,
                collider,
                CityMiscAssetProvider.Load(), new CityBusStopLabel(routeNumber, stop.SequenceIndex + 1, stop.TransferNumber));
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

        /// <summary>Horizontal clearance owner for the authored shelter and
        /// its route pole. Covers the roof, posts, bench and sign together.</summary>
        public static Rect DescribeFurnitureFootprint(CityBusStopDescriptor stop)
        {
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            return DescribeFurnitureFootprint(stop.ShelterPosition, stop.Forward, stop.RoadsideForward);
        }

        internal static Rect DescribeFurnitureFootprint(Vector3 shelterPosition,
            Vector3 routeForward, Vector3 roadsideForward)
        {
            Quaternion rotation = ResolveRotation(routeForward, roadsideForward);
            const float minX = ShelterOffsetAlongLane - 4.65f * .5f;
            const float maxX = .38f;
            Vector3 center = shelterPosition + rotation * new Vector3((minX+maxX)*.5f,0,0);
            Vector3 side = rotation * Vector3.right * ((maxX-minX)*.5f);
            Vector3 depth = rotation * Vector3.forward * (1.18f*.5f);
            float x = Mathf.Abs(side.x)+Mathf.Abs(depth.x);
            float z = Mathf.Abs(side.z)+Mathf.Abs(depth.z);
            return Rect.MinMaxRect(center.x-x,center.z-z,center.x+x,center.z+z);
        }

        private static void BuildStopVisual(
            Transform root,
            bool collider,
            CityMiscAssetProvider miscProvider, CityBusStopLabel label)
        {
            bool importedPole = BuildPole(root, collider, miscProvider);
            BuildShelter(root, collider, miscProvider);
            // The old imported digits are geometry. Pull its enamel insert beyond
            // the raised strokes and face each inscription toward its own reader.
            foreach (int side in PlateSides)
            {
                Vector3 normal = root.forward * side;
                CityBusPlateLettering.Create(root, root.TransformPoint(new Vector3(0f, 2.18f, .101f * side)),
                    normal, root.up, label.PlateText, new Vector2(.57f, .35f), StopPlateInk);
            }
            if (importedPole) return;
            RuntimePrimitiveFactory.CreateBox("Route Plate", root, new Vector3(0f, 2.18f, 0f),
                new Vector3(.76f, .56f, .10f), RouteColor, false);
            foreach (int side in PlateSides)
                RuntimePrimitiveFactory.CreateBox("Route Plate Inset", root, new Vector3(0f, 2.18f, .085f * side),
                    new Vector3(.58f, .38f, .025f), StopPlateEnamel, false);
        }

        private static readonly int[] PlateSides = { 1, -1 };

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
                    ResolvePolePartColor, true))
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
                    return StopPlateEnamel;
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
            Func<CityMiscMeshRole, Color> resolveColor, bool coverBakedDigits = false)
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
                var placements = coverBakedDigits && part.Role == CityMiscMeshRole.Residential
                    ? new[] {
                        new RuntimeMeshPlacement(part.Mesh, localPosition + Vector3.forward * .03f, Quaternion.identity),
                        new RuntimeMeshPlacement(part.Mesh, localPosition + Vector3.back * .03f, Quaternion.Euler(0f, 180f, 0f)) }
                    : new[] { new RuntimeMeshPlacement(part.Mesh, localPosition, Quaternion.identity) };
                RuntimePrimitiveFactory.CreateCombinedMeshes($"{name} {part.Role}", root, placements, resolveColor(part.Role));
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

    }

    internal static class CityBusPlateLettering
    {
        internal static TextMeshPro Create(Transform parent, Vector3 center, Vector3 facing, Vector3 up,
            string number, Vector2 size, Color ink)
        {
            var host = new GameObject("Route Number", typeof(RectTransform));
            Transform frame = host.transform;
            frame.SetPositionAndRotation(center, Quaternion.LookRotation(-facing, up));
            // Imported bus meshes keep their FBX scale. Size the letters in world metres.
            frame.SetParent(parent, true);
            TextMeshPro label = host.AddComponent<TextMeshPro>();
            label.font = CemeteryPlaqueFont.Get();
            label.alignment = TextAlignmentOptions.Center;
            label.color = ink;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = .6f;
            label.fontSizeMax = 3.8f;
            label.rectTransform.sizeDelta = size;
            label.text = number;
            label.renderer.shadowCastingMode = ShadowCastingMode.Off;
            return label;
        }
    }
}
