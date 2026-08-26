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
            MountainRoadTerminalSiteWorldResult site,
            MountainRoadVistaWorldResult vista,
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
            Site = site ?? throw new ArgumentNullException(nameof(site));
            Vista = vista ?? throw new ArgumentNullException(nameof(vista));
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
        public MountainRoadTerminalSiteWorldResult Site { get; }
        public MountainRoadVistaWorldResult Vista { get; }
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
        private static readonly Color MiddleRidgeColor =
            new Color(0.19f, 0.215f, 0.205f, 1f);
        private static readonly Color FarSnowyRidgeColor =
            new Color(0.47f, 0.52f, 0.525f, 1f);
        private static readonly Color CabinetColor =
            new Color(0.20f, 0.265f, 0.24f, 1f);
        private static readonly Color MirrorFaceColor =
            new Color(0.48f, 0.54f, 0.52f, 1f);
        private static readonly Color SnowPoleColor =
            new Color(0.62f, 0.22f, 0.18f, 1f);
        private static readonly Color LampLensColor =
            new Color(0.88f, 0.63f, 0.32f, 1f);

        private static float StoneMetersPerTile =>
            MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.LayeredStone).MetersPerTile;

        private static float BarkMetersPerTile =>
            MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.BarkAndDeadwood).MetersPerTile;

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
                ShadowCastingMode.On,
                MountainRoadSurfaceKind.Asphalt);
            GameObject terminalApron = CreateMeshObject(
                "Visible Terminal Vehicle Apron",
                physicalRoot.transform,
                MountainRoadSurfaceMeshFactory.CreateTerminalApron(
                    plan.Terminal.VehicleApron,
                    plan.Plateau.EntryDistance),
                TerminalApronColor,
                false,
                ShadowCastingMode.Off,
                MountainRoadSurfaceKind.Asphalt);
            // Casts nothing — a flat ground plate shadowing itself is acne
            // and there is nothing underneath it — but it RECEIVES now. The
            // car stops on this apron under its own headlights, and it is
            // the surface the passenger is looking at when he gets out.
            terminalApron.GetComponent<MeshRenderer>().receiveShadows = true;
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
            MountainRoadTerminalSiteWorldResult site =
                MountainRoadTerminalSiteWorldBuilder.Build(
                    physicalRoot.transform,
                    plan.Terminal.Site);
            MergeSemanticObjects(
                semanticObjects,
                site.SemanticObjects);
            BuildForest(physicalRoot.transform, plan.Forest);
            BuildMisc(
                physicalRoot.transform,
                plan.Misc,
                semanticObjects);
            BuildRidges(backdropRoot.transform, plan.Ridges);
            MountainRoadVistaWorldResult vista =
                MountainRoadVistaWorldBuilder.Build(
                    backdropRoot.transform,
                    plan.Vista);

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
                site,
                vista,
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
                ShadowCastingMode.On,
                MountainRoadSurfaceKind.ForestFloor);
            CreateMeshObject(
                "Upper Snow",
                root.transform,
                meshes.Snow,
                SnowColor,
                true,
                ShadowCastingMode.On,
                MountainRoadSurfaceKind.WindSnow);
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
            GameObject shellBatch =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Tunnel Rock Shell",
                    root.transform,
                    shell,
                    TunnelRockColor,
                    true,
                    StoneMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                shellBatch.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.LayeredStone,
                TunnelRockColor);

            // The darkness behind the mouth is the absence of a surface, so
            // it keeps its flat black plate and takes no sheet at all.
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

                // Every layer casts and receives now, the far one included.
                // It used to be excused as backdrop, but it stands 17-28 m
                // out — inside both the shadow distance and the reach of
                // the car's headlights — and during the climb it is the
                // only thing between the beam and the void. A forest the
                // headlights sweep without anything changing is the whole
                // effect thrown away.
                GameObject crowns = CreateMeshObject(
                    $"{layers[layerIndex]} Conifer Crowns",
                    root.transform,
                    MountainRoadSceneryMeshFactory.CreateConiferCrowns(
                        $"{layers[layerIndex]} Conifer Crowns",
                        trees),
                    crownColors[layerIndex],
                    false,
                    ShadowCastingMode.On,
                    MountainRoadSurfaceKind.ConiferNeedles,
                    MountainRoadSurfaceAppearance.FoliageMaterial);
                GameObject trunkBatch =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"{layers[layerIndex]} Conifer Trunks",
                        root.transform,
                        trunks,
                        TrunkColor,
                        layers[layerIndex] ==
                            MountainRoadForestLayer.Physical,
                        BarkMetersPerTile,
                        RuntimeWorldUvMode.BoxProjected);
                MountainRoadSurfaceAppearance.ApplyCombined(
                    trunkBatch.GetComponent<Renderer>(),
                    MountainRoadSurfaceKind.BarkAndDeadwood,
                    TrunkColor);
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
            var logColliders = new List<RuntimeOrientedBox>();
            var stumpColliders = new List<RuntimeOrientedBox>();
            var deadTreeColliders = new List<RuntimeOrientedBox>();
            var importedBatches =
                new Dictionary<int, ImportedMiscBatch>();
            MountainRoadMiscAssetProvider provider = null;
            for (int index = 0; index < misc.Count; index++)
            {
                MountainRoadMiscDescriptor item = misc[index];
                switch (item.Kind)
                {
                    case MountainRoadMiscKind.Boulder:
                        boulders.Add(item);
                        break;
                    case MountainRoadMiscKind.FallenLog:
                        EnsureImportedProvider(ref provider);
                        AppendImportedParts(
                            importedBatches,
                            provider,
                            item);
                        logColliders.Add(new RuntimeOrientedBox(
                            item.Position,
                            item.Rotation,
                            item.Size));
                        break;
                    case MountainRoadMiscKind.Stump:
                        EnsureImportedProvider(ref provider);
                        AppendImportedParts(
                            importedBatches,
                            provider,
                            item);
                        stumpColliders.Add(new RuntimeOrientedBox(
                            item.Position,
                            item.Rotation,
                            item.Size));
                        break;
                    case MountainRoadMiscKind.DeadTree:
                        EnsureImportedProvider(ref provider);
                        AppendImportedParts(
                            importedBatches,
                            provider,
                            item);
                        AppendDeadTree(deadTreeColliders, item);
                        break;
                    default:
                        if (MountainRoadMiscAssetProvider.Supports(item.Kind))
                        {
                            EnsureImportedProvider(ref provider);
                            AppendImportedParts(
                                importedBatches,
                                provider,
                                item);
                        }

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
                    MountainRoadSceneryMeshFactory.CreateBoulders(
                        boulders,
                        MountainRoadSurfaceKind.LayeredStone),
                    RockColor,
                    true,
                    ShadowCastingMode.On,
                    MountainRoadSurfaceKind.LayeredStone);
            }

            BuildImportedBatches(root.transform, importedBatches);
            CreateColliderProxies(
                root.transform,
                "Fallen Log Collision",
                logColliders);
            CreateColliderProxies(
                root.transform,
                "Cut Stump Collision",
                stumpColliders);
            CreateColliderProxies(
                root.transform,
                "Dead Tree Collision",
                deadTreeColliders);
        }

        private sealed class ImportedMiscBatch
        {
            internal ImportedMiscBatch(
                MountainRoadMiscKind kind,
                MountainRoadMiscMeshRole role)
            {
                Kind = kind;
                Role = role;
                Placements = new List<RuntimeMeshPlacement>(32);
            }

            internal MountainRoadMiscKind Kind { get; }
            internal MountainRoadMiscMeshRole Role { get; }
            internal List<RuntimeMeshPlacement> Placements { get; }
        }

        private static void EnsureImportedProvider(
            ref MountainRoadMiscAssetProvider provider)
        {
            if (provider == null)
            {
                provider = MountainRoadMiscAssetProvider.LoadOrThrow();
            }
        }

        private static void AppendImportedParts(
            IDictionary<int, ImportedMiscBatch> batches,
            MountainRoadMiscAssetProvider provider,
            MountainRoadMiscDescriptor item)
        {
            int partCount = provider.GetPartCount(item.Kind);
            Vector3 scale = item.Kind == MountainRoadMiscKind.DeadTree
                ? Vector3.one * item.Size.y
                : item.Size;
            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                MountainRoadMiscMeshPart part = provider.GetPartOrThrow(
                    item.Kind,
                    item.StableId,
                    partIndex);
                int key = ((int)item.Kind << 8) | partIndex;
                if (!batches.TryGetValue(key, out ImportedMiscBatch batch))
                {
                    batch = new ImportedMiscBatch(item.Kind, part.Role);
                    batches.Add(key, batch);
                }
                else if (batch.Role != part.Role)
                {
                    throw new InvalidOperationException(
                        $"Imported misc part role drifted for {item.Kind}, " +
                        $"part {partIndex}.");
                }

                batch.Placements.Add(new RuntimeMeshPlacement(
                    part.Mesh,
                    item.Position,
                    item.Rotation,
                    scale));
            }
        }

        private static void BuildImportedBatches(
            Transform parent,
            IReadOnlyDictionary<int, ImportedMiscBatch> batches)
        {
            var keys = new List<int>(batches.Keys);
            keys.Sort();
            for (int index = 0; index < keys.Count; index++)
            {
                ImportedMiscBatch batch = batches[keys[index]];
                ResolveImportedAppearance(
                    batch.Role,
                    out MountainRoadSurfaceKind surface,
                    out Color tint);
                HomeSurfaceRecipe recipe =
                    MountainRoadSurfaceAppearance.GetRecipe(surface);
                GameObject combined =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        ImportedBatchName(batch.Kind, batch.Role),
                        parent,
                        batch.Placements,
                        tint,
                        false,
                        recipe.MetersPerTile,
                        RuntimeWorldUvMode.BoxProjected,
                        Vector3.zero);
                MountainRoadSurfaceAppearance.ApplyCombined(
                    combined.GetComponent<Renderer>(),
                    surface,
                    tint);
            }
        }

        private static string ImportedBatchName(
            MountainRoadMiscKind kind,
            MountainRoadMiscMeshRole role)
        {
            switch (kind)
            {
                case MountainRoadMiscKind.FallenLog:
                    return "Imported Fallen Logs";
                case MountainRoadMiscKind.Stump:
                    return "Imported Cut Stumps";
                case MountainRoadMiscKind.DeadTree:
                    return "Imported Dead Trees";
                case MountainRoadMiscKind.GuardRail:
                    return "Imported Guard Rails";
                case MountainRoadMiscKind.SnowPole:
                    return role == MountainRoadMiscMeshRole.SnowPoleBody
                        ? "Imported Snow Pole Bodies"
                        : "Imported Snow Pole Bands";
                case MountainRoadMiscKind.ConvexMirror:
                    return $"Imported Convex Mirror {role}";
                case MountainRoadMiscKind.UtilityCabinet:
                    return $"Imported Utility Cabinet {role}";
                case MountainRoadMiscKind.AbandonedChair:
                    return "Imported Abandoned Chair";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        private static void ResolveImportedAppearance(
            MountainRoadMiscMeshRole role,
            out MountainRoadSurfaceKind surface,
            out Color tint)
        {
            switch (role)
            {
                case MountainRoadMiscMeshRole.Wood:
                    surface = MountainRoadSurfaceKind.BarkAndDeadwood;
                    tint = DeadWoodColor;
                    return;
                case MountainRoadMiscMeshRole.GuardRailIron:
                case MountainRoadMiscMeshRole.ConvexMirrorPole:
                case MountainRoadMiscMeshRole.ConvexMirrorFrame:
                    surface = MountainRoadSurfaceKind.RustedIron;
                    tint = RustColor;
                    return;
                case MountainRoadMiscMeshRole.SnowPoleBody:
                    surface = MountainRoadSurfaceKind.PaleEnamel;
                    tint = SnowPoleColor;
                    return;
                case MountainRoadMiscMeshRole.SnowPoleBand:
                    surface = MountainRoadSurfaceKind.PaleEnamel;
                    tint = SnowColor;
                    return;
                case MountainRoadMiscMeshRole.ConvexMirrorFace:
                    surface = MountainRoadSurfaceKind.PaleEnamel;
                    tint = MirrorFaceColor;
                    return;
                case MountainRoadMiscMeshRole.UtilityCabinetBody:
                    surface = MountainRoadSurfaceKind.PaintedMetal;
                    tint = CabinetColor;
                    return;
                case MountainRoadMiscMeshRole.UtilityCabinetTrim:
                    surface = MountainRoadSurfaceKind.RustedIron;
                    tint = IronColor;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role),
                        role,
                        null);
            }
        }

        private static void CreateColliderProxies(
            Transform parent,
            string name,
            IReadOnlyList<RuntimeOrientedBox> boxes)
        {
            for (int index = 0; index < boxes.Count; index++)
            {
                RuntimeOrientedBox box = boxes[index];
                GameObject proxy = CreateBoxColliderProxy(
                    $"{name} {index:00}",
                    parent,
                    box.Center,
                    box.Rotation,
                    box.Size);
                proxy.layer = parent.gameObject.layer;
            }
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
                    BuildGuardRailCollision(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.Culvert:
                    BuildCulvert(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.ConvexMirror:
                    BuildMirrorCollision(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.UtilityCabinet:
                    BuildCabinetCollision(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.UtilityCable:
                    BuildUtilityCable(root.transform, item.Size);
                    break;
                case MountainRoadMiscKind.SnowPole:
                    // This marker is visual-only. Its stable root remains the
                    // source of the authored wind sound.
                    break;
                case MountainRoadMiscKind.AbandonedChair:
                    BuildChairCollision(root.transform, item.Size);
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

        private static void BuildGuardRailCollision(
            Transform root,
            Vector3 size)
        {
            CreateBoxColliderProxy(
                "Loose Iron Beam",
                root,
                Vector3.up * 0.19f,
                Quaternion.identity,
                new Vector3(size.x, 0.18f, size.z));
            for (int index = -1; index <= 1; index++)
            {
                CreateBoxColliderProxy(
                    "Guard Post",
                    root,
                    new Vector3(0f, -0.12f, index * size.z * 0.38f),
                    Quaternion.identity,
                    new Vector3(0.18f, size.y, 0.18f));
            }
        }

        private static void BuildCulvert(Transform root, Vector3 size)
        {
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Culvert Headwall",
                    root,
                    Vector3.zero,
                    size,
                    RockColor,
                    false),
                MountainRoadSurfaceKind.LayeredStone,
                RockColor);

            // The bore is a hole, not a material.
            GameObject mouth = RuntimePrimitiveFactory.CreateCylinder(
                "Visible Culvert Mouth",
                root,
                new Vector3(0f, 0f, -size.z * 0.51f),
                new Vector3(0.62f, 0.12f, 0.62f),
                TunnelDarkColor,
                false);
            mouth.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static void BuildMirrorCollision(
            Transform root,
            Vector3 size)
        {
            CreateBoxColliderProxy(
                "Mirror Pole",
                root,
                new Vector3(0f, -0.15f, 0f),
                Quaternion.identity,
                new Vector3(0.10f, size.y, 0.10f));
        }

        private static void BuildCabinetCollision(
            Transform root,
            Vector3 size)
        {
            CreateBoxColliderProxy(
                "Service Cabinet",
                root,
                Vector3.zero,
                Quaternion.identity,
                size);
        }

        private static void BuildUtilityCable(Transform root, Vector3 size)
        {
            float half = size.x * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                TextureSurface(
                    RuntimePrimitiveFactory.CreateBox(
                        "Cable Pole",
                        root,
                        new Vector3(side * half, -0.05f, 0f),
                        new Vector3(0.16f, size.y, 0.16f),
                        DeadWoodColor,
                        true),
                    MountainRoadSurfaceKind.BarkAndDeadwood,
                    SurfaceProjection.BoxZY,
                    DeadWoodColor);
            }

            for (int segment = 0; segment < 3; segment++)
            {
                float t = (segment + 0.5f) / 3f;
                float x = Mathf.Lerp(-half, half, t);
                float sag = 0.28f * (1f - Mathf.Abs(t * 2f - 1f));
                // Fifty-five millimetres of cable is under one texel of
                // the composite; a sheet on it would only alias.
                RuntimePrimitiveFactory.CreateBox(
                    "Sagging Visible Cable",
                    root,
                    new Vector3(x, size.y * 0.44f - sag, 0f),
                    new Vector3(size.x / 3f + 0.04f, 0.055f, 0.055f),
                    TunnelDarkColor,
                    false);
            }
        }

        private static void BuildChairCollision(
            Transform root,
            Vector3 size)
        {
            CreateBoxColliderProxy(
                "Chair Seat",
                root,
                new Vector3(0f, -0.06f, 0f),
                Quaternion.identity,
                new Vector3(size.x, 0.12f, size.z));
            CreateBoxColliderProxy(
                "Chair Back",
                root,
                new Vector3(0f, size.y * 0.32f, size.z * 0.42f),
                Quaternion.identity,
                new Vector3(size.x, size.y * 0.65f, 0.10f));
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    CreateBoxColliderProxy(
                        "Chair Leg",
                        root,
                        new Vector3(
                            x * size.x * 0.36f,
                            -size.y * 0.32f,
                            z * size.z * 0.36f),
                        Quaternion.identity,
                        new Vector3(0.08f, size.y * 0.6f, 0.08f));
                }
            }
        }

        private static GameObject CreateBoxColliderProxy(
            string name,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 size)
        {
            var proxy = new GameObject(name);
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = localPosition;
            proxy.transform.localRotation = localRotation;
            proxy.AddComponent<BoxCollider>().size = size;
            return proxy;
        }

        private static void BuildTunnelLamp(Transform root, Vector3 size)
        {
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Lamp Housing",
                    root,
                    Vector3.up * 0.07f,
                    size,
                    IronColor,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                IronColor);

            // MountainRoadAtmosphere drives this lens through its own
            // property block every frame; it is a practical, not a
            // material, and takes no sheet.
            RuntimePrimitiveFactory.CreateBox(
                "Warm Lamp Lens",
                root,
                Vector3.down * 0.035f,
                new Vector3(size.x * 0.72f, 0.06f, size.z * 0.78f),
                LampLensColor,
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

            // The two rings do not wear the same sheet, so each names its
            // kind ONCE and hands the same value to the bake and to the
            // recipe. Splitting them would tile the far ring at the stone
            // pitch while its snow recipe declared another.
            const MountainRoadSurfaceKind midSurface =
                MountainRoadSurfaceKind.LayeredStone;
            const MountainRoadSurfaceKind snowSurface =
                MountainRoadSurfaceKind.WindSnow;
            CreateMeshObject(
                "Middle Rock Ridges",
                parent,
                MountainRoadSceneryMeshFactory.CreateRidges(
                    "Middle Rock Ridges",
                    mid,
                    midSurface),
                MiddleRidgeColor,
                false,
                ShadowCastingMode.On,
                midSurface);
            GameObject snow = CreateMeshObject(
                "Far Snowy Mountain Ring",
                parent,
                MountainRoadSceneryMeshFactory.CreateRidges(
                    "Far Snowy Mountain Ring",
                    snowy,
                    snowSurface),
                FarSnowyRidgeColor,
                false,
                ShadowCastingMode.Off,
                snowSurface);
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

            GameObject batch =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    name,
                    parent,
                    boxes,
                    color,
                    collider,
                    BarkMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                batch.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.BarkAndDeadwood,
                color);
        }

        /// <summary>
        /// One hand-built mountain mesh. Every such mesh bakes its UVs at
        /// its own recipe's metre pitch, so it takes the combined path: the
        /// sheet, the compensated tint and the surface response ride a
        /// property block and no per-object material is created.
        /// </summary>
        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            Color color,
            bool collider,
            ShadowCastingMode shadowCasting,
            MountainRoadSurfaceKind surface,
            Material sharedMaterial = null)
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
            renderer.shadowCastingMode = shadowCasting;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer,
                surface,
                color,
                sharedMaterial ?? RuntimePrimitiveFactory.DefaultMaterial);
            if (collider)
            {
                result.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            return result;
        }

        /// <summary>
        /// Gives one mountain primitive its sheet. A caller with nothing to
        /// sample - the darkness behind the tunnel mouth, the culvert bore,
        /// a sagging span of cable, the lamp lens the atmosphere flickers -
        /// passes no surface and keeps its flat colour.
        /// </summary>
        private static void TextureSurface(
            GameObject instance,
            MountainRoadSurfaceKind? surface,
            Color tint)
        {
            if (instance == null || !surface.HasValue)
            {
                return;
            }

            MountainRoadSurfaceAppearance.Apply(
                instance.GetComponent<Renderer>(),
                surface.Value,
                tint);
        }

        /// <summary>
        /// The same, for a primitive whose readable face its proportions
        /// would not find - a thin seam panel, a bent marker pole.
        /// </summary>
        private static void TextureSurface(
            GameObject instance,
            MountainRoadSurfaceKind surface,
            SurfaceProjection projection,
            Color tint)
        {
            if (instance == null)
            {
                return;
            }

            MountainRoadSurfaceAppearance.Apply(
                instance.GetComponent<Renderer>(),
                surface,
                projection,
                tint);
        }
    }
}
