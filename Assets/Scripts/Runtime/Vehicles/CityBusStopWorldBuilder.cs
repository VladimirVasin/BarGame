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

        private static readonly Color StopPlateInk = new Color32(249, 244, 223, 255);

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
            var assets = new StopAssets();
            foreach (var platform in CityBusNetworkFurniture.Platforms(plans))
                BuildStop(root, platform.stop, assets, new CityBusStopLabel(platform.line.Number,
                    platform.ordinal, platform.stop.TransferNumber, platform.transferOrdinal, platform.shared));

            CityBusPlanner.ReportPhase(
                "stop_world",
                timer,
                GameLog.Field("stops", root.childCount),
                GameLog.Field("imported", assets.Shelter.Length > 0 && assets.Pole.Length > 0));
            return root.gameObject;
        }

        private static GameObject BuildStop(
            Transform parent,
            CityBusStopDescriptor stop,
            StopAssets assets, CityBusStopLabel label)
        {
            Transform root = CreateStopRoot(parent, stop);
            root.SetPositionAndRotation(
                stop.ShelterPosition,
                ResolveRotation(
                    stop.Forward,
                    stop.RoadsideForward));
            BuildStopVisual(root, true, assets, label, stop.Forward);
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
                new StopAssets(), new CityBusStopLabel(routeNumber, stop.SequenceIndex + 1, stop.TransferNumber),
                parent.TransformDirection(localRouteForward));
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
            StopAssets assets, CityBusStopLabel label, Vector3 routeForward)
        {
            bool importedPole = BuildPole(root, collider, assets);
            BuildShelter(root, collider, assets);
            // The model carries two enamel faces. Each inscription faces its own reader.
            foreach (int side in PlateSides)
            {
                Vector3 normal = root.forward * side;
                CityBusPlateLettering.Create(root, root.TransformPoint(new Vector3(0f, 2.18f, .101f * side)),
                    normal, root.up, label.PlateText, new Vector2(.57f, .35f), StopPlateInk);
            }
            root.gameObject.AddComponent<CityBusStopLighting>().Initialize(routeForward);
            if (importedPole) return;
            assets.CreateFallbackBox("Route Plate", root, new Vector3(0f, 2.18f, 0f),
                new Vector3(.76f, .56f, .10f), assets.SignRim, false);
            foreach (int side in PlateSides)
                assets.CreateFallbackBox("Route Plate Inset", root, new Vector3(0f, 2.18f, .085f * side),
                    new Vector3(.58f, .38f, .025f), assets.SignFace, false);
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
            StopAssets assets)
        {
            const float x = ShelterOffsetAlongLane;
            // Bottom of the rounded, road-facing lip on the authored canopy.
            CityRainDripEdge.Add(root, new Vector3(x - 2.2f, 2.55f, .592f),
                new Vector3(x + 2.2f, 2.55f, .592f));
            if (assets.Shelter.Length > 0)
            {
                assets.Place(root, assets.Shelter, "Imported Route 01 Shelter Shell");
                if (collider)
                {
                    AddShelterColliders(root);
                }

                return;
            }

            assets.CreateFallbackBox(
                "Shelter Back Wall",
                root,
                new Vector3(x, 1.32f, -0.48f),
                new Vector3(4.25f, 2.45f, 0.10f),
                assets.BackPanel,
                collider);
            assets.CreateFallbackBox(
                "Shelter Roof",
                root,
                new Vector3(x, 2.62f, 0f),
                new Vector3(4.65f, 0.18f, 1.18f),
                assets.Frame,
                false);
            assets.CreateFallbackBox(
                "Shelter Post West",
                root,
                new Vector3(x - 2.05f, 1.30f, 0f),
                new Vector3(0.16f, 2.60f, 0.16f),
                assets.Frame,
                collider);
            assets.CreateFallbackBox(
                "Shelter Post Back",
                root,
                new Vector3(x, 1.30f, -0.48f),
                new Vector3(0.16f, 2.60f, 0.16f),
                assets.Frame,
                false);
            assets.CreateFallbackBox(
                "Shelter Post East",
                root,
                new Vector3(x + 2.05f, 1.30f, 0f),
                new Vector3(0.16f, 2.60f, 0.16f),
                assets.Frame,
                collider);
            assets.CreateFallbackBox(
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
                assets.Bench,
                collider);
            assets.CreateFallbackBox(
                "Shelter Bench Backrest",
                root,
                new Vector3(x, 1.10f, -0.20f),
                new Vector3(2.65f, 0.62f, 0.14f),
                assets.Bench,
                false);
            assets.CreateFallbackBox(
                "Shelter Bench Leg West",
                root,
                new Vector3(x - 0.90f, 0.34f, 0.05f),
                new Vector3(0.14f, 0.68f, 0.14f),
                assets.Frame,
                false);
            assets.CreateFallbackBox(
                "Shelter Bench Leg East",
                root,
                new Vector3(x + 0.90f, 0.34f, 0.05f),
                new Vector3(0.14f, 0.68f, 0.14f),
                assets.Frame,
                false);
        }

        private static bool BuildPole(
            Transform root,
            bool collider,
            StopAssets assets)
        {
            if (assets.Pole.Length > 0)
            {
                assets.Place(root, assets.Pole, "Imported Route 01 Pole Shell");
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

            assets.CreateFallbackBox(
                "Pole",
                root,
                new Vector3(0f, 1.20f, 0f),
                new Vector3(0.12f, 2.40f, 0.12f),
                assets.Frame,
                collider);
            return false;
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

        // Resolve the catalog once per City or balcony build. Identical platforms
        // then reference the imported meshes and material assets directly.
        private sealed class StopAssets
        {
            public readonly Material Frame = LoadMaterial("Frame");
            public readonly Material Bench = LoadMaterial("Bench");
            public readonly Material BackPanel = LoadMaterial("BackPanel");
            public readonly Material SignRim = LoadMaterial("SignRim");
            public readonly Material SignFace = LoadMaterial("SignFace");
            public readonly CityMiscMeshPart[] Shelter;
            public readonly CityMiscMeshPart[] Pole;

            public StopAssets()
            {
                var provider = CityMiscAssetProvider.Load();
                Shelter = LoadParts(provider, CityMiscKind.Route01ShelterShell);
                Pole = LoadParts(provider, CityMiscKind.Route01PoleShell);
            }

            private static Material LoadMaterial(string suffix)
            {
                var material = Resources.Load<Material>("Materials/BusStop" + suffix);
                if (material == null)
                    throw new InvalidOperationException($"Missing bus stop material '{suffix}'.");
                return material;
            }

            private static CityMiscMeshPart[] LoadParts(CityMiscAssetProvider provider, CityMiscKind kind)
            {
                if (provider == null) return Array.Empty<CityMiscMeshPart>();
                var parts = new CityMiscMeshPart[CityMiscAssetProvider.GetPartCount(kind)];
                try
                {
                    for (int i = 0; i < parts.Length; i++)
                        parts[i] = provider.GetPartOrThrow(kind, 0, i);
                }
                catch (InvalidOperationException)
                {
                    return Array.Empty<CityMiscMeshPart>();
                }
                return parts;
            }

            public void Place(Transform root, CityMiscMeshPart[] parts, string name)
            {
                foreach (var part in parts)
                {
                    var host = new GameObject($"{name} {part.Role}");
                    host.transform.SetParent(root, false);
                    host.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                    var renderer = host.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = ResolveMaterial(part);
                    if (part.Role == CityMiscMeshRole.BacklitSign)
                        CityNightGlowRegistry.Register(renderer, CityBusStopLighting.LensColor);
                }
            }

            private Material ResolveMaterial(CityMiscMeshPart part)
            {
                switch (part.Role)
                {
                    case CityMiscMeshRole.Fixture: return Frame;
                    case CityMiscMeshRole.Timber: return Bench;
                    case CityMiscMeshRole.Street: return SignRim;
                    case CityMiscMeshRole.BacklitSign: return CityNightResources.EmissiveMaterial;
                    case CityMiscMeshRole.Residential:
                        return part.Kind == CityMiscKind.Route01PoleShell ? SignFace : BackPanel;
                    default: throw new InvalidOperationException($"Unexpected bus stop part '{part.Component}'.");
                }
            }

            public void CreateFallbackBox(string name, Transform root, Vector3 center, Vector3 size,
                Material material, bool collider)
            {
                // The built-in cube has normalized face UVs. Its vertical texture
                // axis runs along the plank when the unit cube is laid on its side.
                bool lengthwise = material == Bench && size.x > size.y;
                Vector3 dimensions = lengthwise ? new Vector3(size.y, size.x, size.z) : size;
                var box = RuntimePrimitiveFactory.CreateMaterialBox(name, root, center, dimensions, material, collider);
                if (lengthwise) box.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                if (material == BackPanel) return;
                var renderer = box.GetComponent<Renderer>();
                var properties = new MaterialPropertyBlock();
                properties.SetVector("_BaseMap_ST", SurfaceAppearanceCore.CreateBaseMapTransform(renderer,
                    1f / material.mainTextureScale.x, .02f, SurfaceAppearanceCore.ResolveBoxProjection(renderer), 0));
                renderer.SetPropertyBlock(properties);
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
