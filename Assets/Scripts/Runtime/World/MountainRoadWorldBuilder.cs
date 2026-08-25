using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed class MountainRoadWorldResult
    {
        internal MountainRoadWorldResult(
            GameObject root,
            GameObject physicalRoot,
            GameObject backdropRoot,
            GameObject roadSurface,
            GameObject terminalApron,
            GameObject terrainRoot,
            MountainRoadWalkableArea walkableArea,
            MountainRoadBridgeWorldResult bridge,
            MountainRoadCafeWorldResult cafe,
            MountainCablewayWorldResult cableway,
            IDictionary<string, Transform> semanticObjects)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            PhysicalRoot = physicalRoot ??
                throw new ArgumentNullException(nameof(physicalRoot));
            BackdropRoot = backdropRoot ??
                throw new ArgumentNullException(nameof(backdropRoot));
            RoadSurface = roadSurface ??
                throw new ArgumentNullException(nameof(roadSurface));
            TerminalApron = terminalApron ??
                throw new ArgumentNullException(nameof(terminalApron));
            TerrainRoot = terrainRoot ??
                throw new ArgumentNullException(nameof(terrainRoot));
            WalkableArea = walkableArea ??
                throw new ArgumentNullException(nameof(walkableArea));
            Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            Cafe = cafe ?? throw new ArgumentNullException(nameof(cafe));
            Cableway = cableway ??
                throw new ArgumentNullException(nameof(cableway));
            SemanticObjects = new ReadOnlyDictionary<string, Transform>(
                new Dictionary<string, Transform>(
                    semanticObjects,
                    StringComparer.Ordinal));
        }

        public GameObject Root { get; }
        public GameObject PhysicalRoot { get; }
        public GameObject BackdropRoot { get; }
        public GameObject RoadSurface { get; }
        public GameObject TerminalApron { get; }
        public GameObject TerrainRoot { get; }
        public MountainRoadWalkableArea WalkableArea { get; }
        public MountainRoadBridgeWorldResult Bridge { get; }
        public MountainRoadCafeWorldResult Cafe { get; }
        public MountainCablewayWorldResult Cableway { get; }
        public IReadOnlyDictionary<string, Transform> SemanticObjects { get; }
    }

    /// <summary>
    /// Composes only the separately loaded mountain-road world. It owns no
    /// scene transition and never keeps the City world alive behind it.
    /// </summary>
    public static class MountainRoadWorldBuilder
    {
        private static readonly Color RoadColor =
            new Color(0.115f, 0.125f, 0.118f, 1f);
        private static readonly Color TerminalApronColor =
            new Color(0.075f, 0.084f, 0.080f, 1f);
        private static readonly Color SoilColor =
            new Color(0.17f, 0.18f, 0.155f, 1f);
        private static readonly Color SnowColor =
            new Color(0.63f, 0.665f, 0.65f, 1f);
        private static readonly Color TrunkColor =
            new Color(0.19f, 0.165f, 0.135f, 1f);
        private static readonly Color DeadWoodColor =
            new Color(0.27f, 0.25f, 0.21f, 1f);
        private static readonly Color IronColor =
            new Color(0.20f, 0.23f, 0.22f, 1f);
        private static readonly Color RustColor =
            new Color(0.33f, 0.245f, 0.17f, 1f);
        private static readonly Color RockColor =
            new Color(0.245f, 0.265f, 0.245f, 1f);
        private static readonly Color TunnelRockColor =
            new Color(0.115f, 0.13f, 0.12f, 1f);
        private static readonly Color TunnelDarkColor =
            new Color(0.025f, 0.03f, 0.028f, 1f);

        public static MountainRoadWorldResult Build(
            Transform parent,
            MountainRoadPlan plan,
            Camera camera)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadValidator.ValidateOrThrow(plan);
            ConfigureCamera(camera);

            var root = new GameObject("Mountain Road World");
            root.transform.SetParent(parent, false);
            var physicalRoot = new GameObject("Physical World");
            physicalRoot.transform.SetParent(root.transform, false);
            var backdropRoot = new GameObject("Mountain Backdrop");
            backdropRoot.transform.SetParent(root.transform, false);
            var semanticObjects = new Dictionary<string, Transform>(
                StringComparer.Ordinal);

            GameObject terrainRoot = BuildTerrain(
                physicalRoot.transform,
                plan);
            GameObject road = CreateMeshObject(
                "Continuous Narrow Road",
                physicalRoot.transform,
                MountainRoadSurfaceMeshFactory.Create(plan),
                RoadColor,
                true,
                ShadowCastingMode.On);
            GameObject terminalApron = CreateMeshObject(
                "Visible Terminal Vehicle Apron",
                physicalRoot.transform,
                MountainRoadSurfaceMeshFactory.CreateTerminalApron(
                    plan.Terminal.VehicleApron),
                TerminalApronColor,
                false,
                ShadowCastingMode.Off);
            terminalApron.GetComponent<MeshRenderer>().receiveShadows = false;
            MountainRoadBridgeWorldResult bridge =
                MountainRoadBridgeWorldBuilder.Build(
                    physicalRoot.transform,
                    plan.Bridge);
            MergeSemanticObjects(
                semanticObjects,
                bridge.SemanticObjects);
            BuildTunnel(physicalRoot.transform, plan.Tunnel);
            MountainRoadCafeWorldResult cafe =
                MountainRoadCafeWorldBuilder.Build(
                    physicalRoot.transform,
                    plan.Terminal.Cafe);
            MergeSemanticObjects(
                semanticObjects,
                cafe.SemanticAnchors);
            MountainCablewayWorldResult cableway =
                MountainCablewayWorldBuilder.Build(
                    physicalRoot.transform,
                    plan.Terminal.Cableway);
            MergeSemanticObjects(
                semanticObjects,
                cableway.SemanticObjects);
            BuildForest(physicalRoot.transform, plan.Forest);
            BuildMisc(
                physicalRoot.transform,
                plan.Misc,
                semanticObjects);
            BuildRidges(backdropRoot.transform, plan.Ridges);

            return new MountainRoadWorldResult(
                root,
                physicalRoot,
                backdropRoot,
                road,
                terminalApron,
                terrainRoot,
                new MountainRoadWalkableArea(plan),
                bridge,
                cafe,
                cableway,
                semanticObjects);
        }

        private static void MergeSemanticObjects(
            IDictionary<string, Transform> target,
            IReadOnlyDictionary<string, Transform> source)
        {
            foreach (KeyValuePair<string, Transform> pair in source)
            {
                if (target.ContainsKey(pair.Key))
                {
                    throw new InvalidOperationException(
                        "Duplicate mountain semantic object ID '" +
                        pair.Key + "'.");
                }

                target.Add(pair.Key, pair.Value);
            }
        }

        private static void ConfigureCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.06f);
            camera.farClipPlane = RuntimeSceneSetup.MountainRoadFarClipPlane;
        }

        private static GameObject BuildTerrain(
            Transform parent,
            MountainRoadPlan plan)
        {
            var root = new GameObject("Continuous Mountain Terrain");
            root.transform.SetParent(parent, false);
            MountainRoadTerrainMeshes meshes =
                MountainRoadTerrainMeshFactory.Create(plan);
            CreateMeshObject(
                "Forest Soil",
                root.transform,
                meshes.Soil,
                SoilColor,
                true,
                ShadowCastingMode.On);
            CreateMeshObject(
                "Upper Snow",
                root.transform,
                meshes.Snow,
                SnowColor,
                true,
                ShadowCastingMode.On);
            return root;
        }

        private static void BuildTunnel(
            Transform parent,
            MountainRoadTunnelDescriptor tunnel)
        {
            var root = new GameObject("Tunnel Exit");
            root.transform.SetParent(parent, false);
            Vector3 right = Vector3.Cross(
                Vector3.up,
                tunnel.OutwardAxis).normalized;
            Quaternion rotation = Quaternion.LookRotation(
                tunnel.OutwardAxis,
                Vector3.up);
            float depth = tunnel.VisualDepth;
            float wallThickness = 0.72f;
            Vector3 middle = tunnel.PortalGroundCenter -
                             tunnel.OutwardAxis * (depth * 0.5f);
            var shell = new List<RuntimeOrientedBox>(3)
            {
                new RuntimeOrientedBox(
                    middle - right *
                    (tunnel.OpeningWidth * 0.5f + wallThickness * 0.5f) +
                    Vector3.up * (tunnel.OpeningHeight * 0.5f),
                    rotation,
                    new Vector3(wallThickness, tunnel.OpeningHeight, depth)),
                new RuntimeOrientedBox(
                    middle + right *
                    (tunnel.OpeningWidth * 0.5f + wallThickness * 0.5f) +
                    Vector3.up * (tunnel.OpeningHeight * 0.5f),
                    rotation,
                    new Vector3(wallThickness, tunnel.OpeningHeight, depth)),
                new RuntimeOrientedBox(
                    middle + Vector3.up *
                    (tunnel.OpeningHeight + wallThickness * 0.5f),
                    rotation,
                    new Vector3(
                        tunnel.OpeningWidth + wallThickness * 2f,
                        wallThickness,
                        depth))
            };
            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                "Tunnel Rock Shell",
                root.transform,
                shell,
                TunnelRockColor,
                true,
                3.5f,
                RuntimeWorldUvMode.BoxProjected);
            RuntimePrimitiveFactory.CreateBox(
                "Tunnel Darkness",
                root.transform,
                tunnel.PortalGroundCenter - tunnel.OutwardAxis *
                (depth + 0.13f) + Vector3.up *
                (tunnel.OpeningHeight * 0.5f),
                new Vector3(
                    tunnel.OpeningWidth,
                    tunnel.OpeningHeight,
                    0.26f),
                TunnelDarkColor,
                true);
        }

        private static void BuildForest(
            Transform parent,
            IReadOnlyList<MountainRoadForestDescriptor> forest)
        {
            var root = new GameObject("Batched Melancholic Forest");
            root.transform.SetParent(parent, false);
            MountainRoadForestLayer[] layers =
            {
                MountainRoadForestLayer.Physical,
                MountainRoadForestLayer.Mid,
                MountainRoadForestLayer.Far
            };
            Color[] crownColors =
            {
                new Color(0.115f, 0.165f, 0.125f, 1f),
                new Color(0.095f, 0.14f, 0.115f, 1f),
                new Color(0.075f, 0.105f, 0.09f, 1f)
            };
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var trees = new List<MountainRoadForestDescriptor>();
                var trunks = new List<RuntimeOrientedBox>();
                for (int index = 0; index < forest.Count; index++)
                {
                    MountainRoadForestDescriptor tree = forest[index];
                    if (tree.Layer != layers[layerIndex])
                    {
                        continue;
                    }

                    trees.Add(tree);
                    float trunkHeight = tree.Height * 0.32f;
                    trunks.Add(new RuntimeOrientedBox(
                        tree.Position + Vector3.up * (trunkHeight * 0.5f),
                        Quaternion.Euler(0f, tree.YawDegrees, 0f),
                        new Vector3(
                            tree.TrunkRadius * 2f,
                            trunkHeight,
                            tree.TrunkRadius * 2f)));
                }

                GameObject crowns = CreateMeshObject(
                    $"{layers[layerIndex]} Conifer Crowns",
                    root.transform,
                    MountainRoadSceneryMeshFactory.CreateConiferCrowns(
                        $"{layers[layerIndex]} Conifer Crowns",
                        trees),
                    crownColors[layerIndex],
                    false,
                    layerIndex == 2
                        ? ShadowCastingMode.Off
                        : ShadowCastingMode.On);
                crowns.GetComponent<MeshRenderer>().receiveShadows =
                    layerIndex != 2;
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    $"{layers[layerIndex]} Conifer Trunks",
                    root.transform,
                    trunks,
                    TrunkColor,
                    layers[layerIndex] == MountainRoadForestLayer.Physical,
                    2.5f,
                    RuntimeWorldUvMode.BoxProjected);
            }
        }

        private static void BuildMisc(
            Transform parent,
            IReadOnlyList<MountainRoadMiscDescriptor> misc,
            IDictionary<string, Transform> semanticObjects)
        {
            var root = new GameObject("Authored Forest Misc");
            root.transform.SetParent(parent, false);
            var boulders = new List<MountainRoadMiscDescriptor>();
            var logs = new List<RuntimeOrientedBox>();
            var stumps = new List<RuntimeOrientedBox>();
            var deadWood = new List<RuntimeOrientedBox>();
            for (int index = 0; index < misc.Count; index++)
            {
                MountainRoadMiscDescriptor item = misc[index];
                switch (item.Kind)
                {
                    case MountainRoadMiscKind.Boulder:
                        boulders.Add(item);
                        break;
                    case MountainRoadMiscKind.FallenLog:
                        logs.Add(new RuntimeOrientedBox(
                            item.Position,
                            item.Rotation,
                            item.Size));
                        break;
                    case MountainRoadMiscKind.Stump:
                        stumps.Add(new RuntimeOrientedBox(
                            item.Position,
                            item.Rotation,
                            item.Size));
                        break;
                    case MountainRoadMiscKind.DeadTree:
                        AppendDeadTree(deadWood, item);
                        break;
                    default:
                        Transform semantic = BuildSemanticObject(
                            root.transform,
                            item);
                        semanticObjects.Add(item.StableId, semantic);
                        break;
                }
            }

            if (boulders.Count > 0)
            {
                CreateMeshObject(
                    "Grounded Rockfall",
                    root.transform,
                    MountainRoadSceneryMeshFactory.CreateBoulders(boulders),
                    RockColor,
                    true,
                    ShadowCastingMode.On);
            }

            CreateBoxBatch(root.transform, "Fallen Logs", logs, DeadWoodColor, true);
            CreateBoxBatch(root.transform, "Cut Stumps", stumps, DeadWoodColor, true);
            CreateBoxBatch(root.transform, "Dead Trees", deadWood, DeadWoodColor, true);
        }

        private static void AppendDeadTree(
            ICollection<RuntimeOrientedBox> target,
            MountainRoadMiscDescriptor item)
        {
            target.Add(new RuntimeOrientedBox(
                item.Position,
                item.Rotation,
                item.Size));
            Vector3 up = item.Rotation * Vector3.up;
            Quaternion firstBranch = item.Rotation *
                                     Quaternion.Euler(38f, 32f, 0f);
            Quaternion secondBranch = item.Rotation *
                                      Quaternion.Euler(-34f, -48f, 0f);
            target.Add(new RuntimeOrientedBox(
                item.Position + up * (item.Size.y * 0.23f),
                firstBranch,
                new Vector3(
                    item.Size.x * 0.34f,
                    item.Size.x * 0.34f,
                    item.Size.y * 0.28f)));
            target.Add(new RuntimeOrientedBox(
                item.Position + up * (item.Size.y * 0.36f),
                secondBranch,
                new Vector3(
                    item.Size.x * 0.29f,
                    item.Size.x * 0.29f,
                    item.Size.y * 0.21f)));
        }

        private static Transform BuildSemanticObject(
            Transform parent,
            MountainRoadMiscDescriptor item)
        {
            var root = new GameObject(item.StableId);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = item.Position;
            root.transform.localRotation = item.Rotation;
            switch (item.Kind)
            {
                case MountainRoadMiscKind.GuardRail:
                    BuildGuardRail(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.Culvert:
                    BuildCulvert(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.ConvexMirror:
                    BuildMirror(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.UtilityCabinet:
                    BuildCabinet(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.UtilityCable:
                    BuildUtilityCable(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.SnowPole:
                    BuildSnowPole(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.AbandonedChair:
                    BuildChair(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.TunnelLamp:
                    BuildTunnelLamp(root.transform, item.Size);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(item.Kind),
                        item.Kind,
                        null);
            }

            return root.transform;
        }

        private static void BuildGuardRail(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Loose Iron Beam",
                root,
                Vector3.up * 0.19f,
                new Vector3(size.x, 0.18f, size.z),
                IronColor,
                true);
            for (int index = -1; index <= 1; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    "Guard Post",
                    root,
                    new Vector3(0f, -0.12f, index * size.z * 0.38f),
                    new Vector3(0.18f, size.y, 0.18f),
                    RustColor,
                    true);
            }
        }

        private static void BuildCulvert(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Culvert Headwall",
                root,
                Vector3.zero,
                size,
                RockColor,
                false);
            GameObject mouth = RuntimePrimitiveFactory.CreateCylinder(
                "Visible Culvert Mouth",
                root,
                new Vector3(0f, 0f, -size.z * 0.51f),
                new Vector3(0.62f, 0.12f, 0.62f),
                TunnelDarkColor,
                false);
            mouth.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static void BuildMirror(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Mirror Pole",
                root,
                new Vector3(0f, -0.15f, 0f),
                new Vector3(0.10f, size.y, 0.10f),
                RustColor,
                true);
            GameObject mirror = RuntimePrimitiveFactory.CreateCylinder(
                "Cracked Convex Mirror",
                root,
                new Vector3(0f, size.y * 0.42f, 0f),
                new Vector3(size.x, size.z, size.x),
                new Color(0.48f, 0.54f, 0.52f, 1f),
                false);
            mirror.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static void BuildCabinet(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Service Cabinet",
                root,
                Vector3.zero,
                size,
                new Color(0.20f, 0.265f, 0.24f, 1f),
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Cabinet Door Seam",
                root,
                new Vector3(0f, 0f, -size.z * 0.51f),
                new Vector3(size.x * 0.78f, size.y * 0.78f, 0.025f),
                IronColor,
                false);
        }

        private static void BuildUtilityCable(Transform root, Vector3 size)
        {
            float half = size.x * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                RuntimePrimitiveFactory.CreateBox(
                    "Cable Pole",
                    root,
                    new Vector3(side * half, -0.05f, 0f),
                    new Vector3(0.16f, size.y, 0.16f),
                    DeadWoodColor,
                    true);
            }

            for (int segment = 0; segment < 3; segment++)
            {
                float t = (segment + 0.5f) / 3f;
                float x = Mathf.Lerp(-half, half, t);
                float sag = 0.28f * (1f - Mathf.Abs(t * 2f - 1f));
                RuntimePrimitiveFactory.CreateBox(
                    "Sagging Visible Cable",
                    root,
                    new Vector3(x, size.y * 0.44f - sag, 0f),
                    new Vector3(size.x / 3f + 0.04f, 0.055f, 0.055f),
                    TunnelDarkColor,
                    false);
            }
        }

        private static void BuildSnowPole(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Bent Snow Pole",
                root,
                Vector3.zero,
                size,
                new Color(0.62f, 0.22f, 0.18f, 1f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Faded White Band",
                root,
                Vector3.up * (size.y * 0.24f),
                new Vector3(size.x * 1.15f, 0.22f, size.z * 1.15f),
                SnowColor,
                false);
        }

        private static void BuildChair(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Chair Seat",
                root,
                new Vector3(0f, -0.06f, 0f),
                new Vector3(size.x, 0.12f, size.z),
                DeadWoodColor,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Chair Back",
                root,
                new Vector3(0f, size.y * 0.32f, size.z * 0.42f),
                new Vector3(size.x, size.y * 0.65f, 0.10f),
                DeadWoodColor,
                true);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    RuntimePrimitiveFactory.CreateBox(
                        "Chair Leg",
                        root,
                        new Vector3(
                            x * size.x * 0.36f,
                            -size.y * 0.32f,
                            z * size.z * 0.36f),
                        new Vector3(0.08f, size.y * 0.6f, 0.08f),
                        DeadWoodColor,
                        true);
                }
            }
        }

        private static void BuildTunnelLamp(Transform root, Vector3 size)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Housing",
                root,
                Vector3.up * 0.07f,
                size,
                IronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Warm Lamp Lens",
                root,
                Vector3.down * 0.035f,
                new Vector3(size.x * 0.72f, 0.06f, size.z * 0.78f),
                new Color(0.88f, 0.63f, 0.32f, 1f),
                false);
        }

        private static void BuildRidges(
            Transform parent,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges)
        {
            var mid = new List<MountainRoadRidgeDescriptor>();
            var snowy = new List<MountainRoadRidgeDescriptor>();
            for (int index = 0; index < ridges.Count; index++)
            {
                if (ridges[index].Layer == MountainRoadRidgeLayer.Mid)
                {
                    mid.Add(ridges[index]);
                }
                else
                {
                    snowy.Add(ridges[index]);
                }
            }

            CreateMeshObject(
                "Middle Rock Ridges",
                parent,
                MountainRoadSceneryMeshFactory.CreateRidges(
                    "Middle Rock Ridges",
                    mid),
                new Color(0.19f, 0.215f, 0.205f, 1f),
                false,
                ShadowCastingMode.On);
            GameObject snow = CreateMeshObject(
                "Far Snowy Mountain Ring",
                parent,
                MountainRoadSceneryMeshFactory.CreateRidges(
                    "Far Snowy Mountain Ring",
                    snowy),
                new Color(0.47f, 0.52f, 0.525f, 1f),
                false,
                ShadowCastingMode.Off);
            snow.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        private static void CreateBoxBatch(
            Transform parent,
            string name,
            IReadOnlyList<RuntimeOrientedBox> boxes,
            Color color,
            bool collider)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                name,
                parent,
                boxes,
                color,
                collider,
                2.5f,
                RuntimeWorldUvMode.BoxProjected);
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            Color color,
            bool collider,
            ShadowCastingMode shadowCasting)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = shadowCasting;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            if (collider)
            {
                result.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            return result;
        }
    }
}
