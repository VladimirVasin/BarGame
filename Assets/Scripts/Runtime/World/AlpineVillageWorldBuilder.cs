using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed class AlpineVillageWorldResult
    {
        internal AlpineVillageWorldResult(
            GameObject root,
            GameObject terrainRoot,
            GameObject laneSurface,
            MountainCablewayWorldResult cableway,
            AlpineVillageWalkableArea walkableArea,
            IDictionary<string, Transform> semanticObjects)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            TerrainRoot = terrainRoot ??
                throw new ArgumentNullException(nameof(terrainRoot));
            LaneSurface = laneSurface ??
                throw new ArgumentNullException(nameof(laneSurface));
            Cableway = cableway ??
                throw new ArgumentNullException(nameof(cableway));
            WalkableArea = walkableArea ??
                throw new ArgumentNullException(nameof(walkableArea));
            SemanticObjects = new ReadOnlyDictionary<string, Transform>(
                new Dictionary<string, Transform>(
                    semanticObjects,
                    StringComparer.Ordinal));
        }

        public GameObject Root { get; }
        public GameObject TerrainRoot { get; }
        public GameObject LaneSurface { get; }

        /// <summary>The upper terminal and its running line.</summary>
        public MountainCablewayWorldResult Cableway { get; }

        public GameObject StationRoot => Cableway.StationRoot;
        public AlpineVillageWalkableArea WalkableArea { get; }
        public IReadOnlyDictionary<string, Transform> SemanticObjects { get; }
    }

    /// <summary>
    /// Composes the village world from its validated plan.
    ///
    /// This is the broad-strokes pass: ground, lane, station and one shell per
    /// plot, all from runtime primitives, so the place can be walked and its
    /// composition judged before any authored geometry exists. The crooked
    /// houses, the chapel, the adit and the garlands arrive later from the
    /// village Blender kit and replace the shells in place - the plan does not
    /// change when they do, because the plan already owns every position.
    /// </summary>
    public static class AlpineVillageWorldBuilder
    {
        /// <summary>Terrain grid pitch. Fine enough that a gentle slope reads
        /// as ground rather than as facets.</summary>
        /// <summary>Sampling pitch of the ground mesh. The sampler owns the
        /// number, because its station shelf has to be at least this wide to
        /// survive being turned into triangles.</summary>
        public const float TerrainCellSize =
            AlpineVillageTerrainSampler.TerrainCell;

        private static readonly Color SnowColor =
            new Color(0.695f, 0.685f, 0.655f, 1f);
        private static readonly Color SoilColor =
            new Color(0.285f, 0.255f, 0.205f, 1f);
        private static readonly Color LaneColor =
            new Color(0.375f, 0.345f, 0.295f, 1f);
        private static readonly Color TimberColor =
            new Color(0.345f, 0.245f, 0.170f, 1f);
        private static readonly Color WhitewashColor =
            new Color(0.700f, 0.670f, 0.605f, 1f);
        private static readonly Color RoofColor =
            new Color(0.225f, 0.190f, 0.160f, 1f);
        private static readonly Color StoneColor =
            new Color(0.410f, 0.400f, 0.370f, 1f);
        private static readonly Color IronColor =
            new Color(0.200f, 0.215f, 0.205f, 1f);
        private static readonly Color RustColor =
            new Color(0.345f, 0.250f, 0.170f, 1f);
        private static readonly Color ChimneyColor =
            new Color(0.265f, 0.215f, 0.185f, 1f);
        private static readonly Color DoorColor =
            new Color(0.300f, 0.175f, 0.098f, 1f);
        private static readonly Color AditTimberColor =
            new Color(0.250f, 0.155f, 0.088f, 1f);
        private static readonly Color CartIronColor =
            new Color(0.290f, 0.155f, 0.078f, 1f);
        private static readonly Color CartWheelColor =
            new Color(0.170f, 0.105f, 0.062f, 1f);
        private static readonly Color FirewoodColor =
            new Color(0.245f, 0.150f, 0.082f, 1f);

        /// <summary>A door is a door at every house on the lane, whatever
        /// the house is. That is why the kit ships none.</summary>
        public const float DoorHeight = 2.05f;

        public const float DoorWidth = 0.92f;

        public const float GarlandSpacing = 8.5f;
        public const float GarlandFirstDistance = 7f;
        public const float GarlandHeight = 4.6f;
        public const float GarlandAnchorReach = 1.8f;
        public const float GarlandSag = 0.85f;
        public const int GarlandSegments = 14;

        private static readonly Color GarlandWireColor =
            new Color(0.085f, 0.075f, 0.062f, 1f);
        private static readonly Color GarlandBulbColor =
            new Color(1f, 0.775f, 0.44f, 1f);

        /// <summary>
        /// The one colour that carries the zone. Everything else here is a
        /// mountain tone; this is why the place is not the city.
        /// </summary>
        private static readonly Color WindowGlowColor =
            new Color(1f, 0.735f, 0.395f, 1f);

        public static AlpineVillageWorldResult Build(
            Transform parent,
            AlpineVillagePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            plan.ValidateOrThrow();

            var root = new GameObject("Alpine Village");
            root.transform.SetParent(parent, false);
            var semanticObjects = new Dictionary<string, Transform>(
                StringComparer.Ordinal);

            GameObject terrainRoot = BuildTerrain(root.transform, plan);
            GameObject laneSurface = BuildLane(root.transform, plan);

            // The station is the cableway builder's, not this one's. Both
            // terminals are the same building and the second must not be a
            // hand-copy of the first - only its machinery differs, and the
            // station kind is what says how.
            MountainCablewayWorldResult cableway =
                MountainCablewayWorldBuilder.Build(
                    root.transform,
                    plan.Station.Cableway,
                    MountainCablewayStationKind.Return);
            foreach (KeyValuePair<string, Transform> entry in
                     cableway.SemanticObjects)
            {
                semanticObjects[entry.Key] = entry.Value;
            }

            BuildPlots(root.transform, plan, semanticObjects);
            BuildGarlands(root.transform, plan);

            var walkableArea = new AlpineVillageWalkableArea(plan);
            return new AlpineVillageWorldResult(
                root,
                terrainRoot,
                laneSurface,
                cableway,
                walkableArea,
                semanticObjects);
        }

        /// <summary>
        /// One grid mesh over the whole plan, sampled from the shared height
        /// contract. Snow above, soil in the lane's own cut - the tint is a
        /// vertex decision so a single mesh carries both without a second
        /// material.
        /// </summary>
        private static GameObject BuildTerrain(
            Transform parent,
            AlpineVillagePlan plan)
        {
            Rect bounds = plan.TerrainBounds;
            int columns = Mathf.Max(
                1,
                Mathf.CeilToInt(bounds.width / TerrainCellSize));
            int rows = Mathf.Max(
                1,
                Mathf.CeilToInt(bounds.height / TerrainCellSize));

            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var uvs = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    float x = bounds.xMin +
                              bounds.width * (column / (float)columns);
                    float z = bounds.yMin +
                              bounds.height * (row / (float)rows);
                    var point = new Vector2(x, z);
                    float height = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        point);
                    int index = row * (columns + 1) + column;
                    vertices[index] = new Vector3(x, height, z);
                    uvs[index] = new Vector2(x, z) * 0.25f;

                    // Snow lies everywhere except where feet and doors keep
                    // it off, which is the lane and the aprons.
                    plan.Lane.FindNearest(point, out float lateral);
                    float bare = 1f - Mathf.SmoothStep(
                        1.6f,
                        5.5f,
                        lateral);
                    colors[index] = Color.Lerp(SnowColor, SoilColor, bare);
                }
            }

            var triangles = new int[columns * rows * 6];
            int cursor = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int origin = row * (columns + 1) + column;
                    triangles[cursor++] = origin;
                    triangles[cursor++] = origin + columns + 1;
                    triangles[cursor++] = origin + 1;
                    triangles[cursor++] = origin + 1;
                    triangles[cursor++] = origin + columns + 1;
                    triangles[cursor++] = origin + columns + 2;
                }
            }

            var mesh = new Mesh
            {
                name = "Alpine Village Ground",
                indexFormat = vertices.Length > 65000
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = new GameObject("Village Ground");
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            MountainRoadSurfaceAppearance.Apply(
                renderer,
                MountainRoadSurfaceKind.WindSnow,
                SnowColor);
            host.AddComponent<MeshCollider>().sharedMesh = mesh;
            return host;
        }

        /// <summary>
        /// The lane skin, laid as a ribbon over its own cut. Like the mountain
        /// road's asphalt it sits proud of the soil rather than relying on a
        /// coplanar cutout in it.
        /// </summary>
        private static GameObject BuildLane(
            Transform parent,
            AlpineVillagePlan plan)
        {
            IReadOnlyList<AlpineVillageLaneSample> samples =
                plan.Lane.Samples;
            var vertices = new Vector3[samples.Count * 2];
            var uvs = new Vector2[vertices.Length];
            for (int index = 0; index < samples.Count; index++)
            {
                AlpineVillageLaneSample sample = samples[index];
                Vector3 offset = sample.Right * (sample.Width * 0.5f);
                Vector3 lift = Vector3.up * 0.02f;
                vertices[index * 2] = sample.Position - offset + lift;
                vertices[index * 2 + 1] = sample.Position + offset + lift;
                uvs[index * 2] = new Vector2(0f, sample.Distance * 0.35f);
                uvs[index * 2 + 1] = new Vector2(1f, sample.Distance * 0.35f);
            }

            var triangles = new int[(samples.Count - 1) * 6];
            int cursor = 0;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                int origin = index * 2;
                triangles[cursor++] = origin;
                triangles[cursor++] = origin + 2;
                triangles[cursor++] = origin + 1;
                triangles[cursor++] = origin + 1;
                triangles[cursor++] = origin + 2;
                triangles[cursor++] = origin + 3;
            }

            var mesh = new Mesh { name = "Alpine Village Lane" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = new GameObject("Village Lane");
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            MountainRoadSurfaceAppearance.Apply(
                renderer,
                MountainRoadSurfaceKind.ForestFloor,
                LaneColor);
            host.AddComponent<MeshCollider>().sharedMesh = mesh;
            return host;
        }
        /// <summary>
        /// Every plot, dressed from the authored village kit.
        ///
        /// The kit owns mass and material; the plan owns every opening a
        /// person uses and every collider gameplay touches. That split is the
        /// church's, and it is the reason a door is drawn here at real metre
        /// scale instead of being modelled into a cube that is stretched from
        /// a four-metre cottage to the seven-metre house at the top.
        /// </summary>
        private static void BuildPlots(
            Transform parent,
            AlpineVillagePlan plan,
            IDictionary<string, Transform> semanticObjects)
        {
            VillageAssetProvider kit = VillageAssetProvider.Load();
            if (kit == null || !kit.HasCompleteMeshes)
            {
                GameLog.Warning("alpine_village", "village_kit_missing");
                kit = null;
            }

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                var root = new GameObject("Village Plot - " + plot.StableId);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(
                    plot.GroundCenter,
                    Quaternion.LookRotation(plot.Facing, Vector3.up));
                semanticObjects[plot.StableId] = root.transform;
                switch (plot.Kind)
                {
                    case AlpineVillagePlotKind.Adit:
                        BuildAdit(root.transform, plot, kit);
                        break;
                    case AlpineVillagePlotKind.Cemetery:
                        BuildCemetery(root.transform, plot, kit);
                        break;
                    default:
                        BuildBuilding(root.transform, plot, kit);
                        break;
                }
            }
        }

        /// <summary>
        /// Places one authored assembly into a plot.
        ///
        /// The meshes are normalized into a unit cube whose floor is the
        /// descriptor's ground, so the whole placement is one scale and one
        /// lift - no measuring, and nothing that can drift when the kit is
        /// re-authored.
        /// </summary>
        private static void PlaceKitAssembly(
            Transform parent,
            VillageAssetProvider kit,
            VillageAssetKind kind,
            int variant,
            Vector2 footprint,
            float height,
            Func<VillageMeshRole, Color> tintFor)
        {
            VillageMeshRole[] roles = VillageAssetProvider.GetRoles(kind);
            for (int index = 0; index < roles.Length; index++)
            {
                VillageMeshRole role = roles[index];
                if (!kit.TryGetPart(
                        kind,
                        variant,
                        role,
                        out VillageMeshPart part))
                {
                    continue;
                }

                var host = new GameObject($"{kind} {role}");
                host.transform.SetParent(parent, false);
                host.transform.localPosition =
                    Vector3.up * (height * 0.5f);
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = new Vector3(
                    footprint.x,
                    height,
                    footprint.y);
                host.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial =
                    RuntimePrimitiveFactory.DefaultMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                MountainRoadSurfaceAppearance.Apply(
                    renderer,
                    part.Surface,
                    tintFor(role));
            }
        }

        private static Color HouseTint(VillageMeshRole role)
        {
            switch (role)
            {
                case VillageMeshRole.Roof:
                    return RoofColor;
                case VillageMeshRole.Plinth:
                    return StoneColor;
                case VillageMeshRole.Chimney:
                    return ChimneyColor;
                default:
                    return TimberColor;
            }
        }

        private static Color ChapelTint(VillageMeshRole role)
        {
            switch (role)
            {
                case VillageMeshRole.Roof:
                    return RoofColor;
                case VillageMeshRole.Plinth:
                    return StoneColor;
                default:
                    return WhitewashColor;
            }
        }

        /// <summary>
        /// A house, a chapel or the house at the top of the lane: authored
        /// shell, plan-drawn door, plan-drawn lit windows, one plan-derived
        /// collider.
        ///
        /// The windows are the whole zone. They are emissive geometry and not
        /// a hundred point lights, because URP has an additional-light budget
        /// and a village has a great many windows; what lights the snow in
        /// front of a house is a handful of real lamps along the lane.
        /// </summary>
        private static void BuildBuilding(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            VillageAssetProvider kit)
        {
            bool chapel = plot.Kind == AlpineVillagePlotKind.Chapel;
            bool tallest = plot.Kind == AlpineVillagePlotKind.MothersHouse;

            // Where the WALL actually is, which is not where the plot's
            // footprint is. The authored shell stops short of its own cube so
            // the roof can overhang it, and hanging a door on the footprint
            // instead leaves it floating half a metre out in the snow with
            // its own shadow behind it. The mesh knows: ask it.
            var face = new Vector2(
                plot.FootprintSize.x * 0.5f,
                plot.FootprintSize.y * 0.5f);

            if (kit != null)
            {
                VillageAssetKind kind = chapel
                    ? VillageAssetKind.Chapel
                    : VillageAssetKind.House;
                int variant =
                    VillageAssetProvider.SelectVariant(kind, plot.StableId);
                PlaceKitAssembly(
                    parent,
                    kit,
                    kind,
                    variant,
                    plot.FootprintSize,
                    plot.Height,
                    chapel ? (Func<VillageMeshRole, Color>)ChapelTint
                        : HouseTint);
                if (kit.TryGetPart(
                        kind,
                        variant,
                        VillageMeshRole.Walls,
                        out VillageMeshPart walls))
                {
                    Bounds local = walls.Mesh.bounds;
                    face = new Vector2(
                        local.max.x * plot.FootprintSize.x,
                        local.max.z * plot.FootprintSize.y);
                }
            }
            else
            {
                // No kit, no village: a plain block so the composition can
                // still be walked and judged.
                Texture(
                    RuntimePrimitiveFactory.CreateBox(
                        "Fallback Massing",
                        parent,
                        Vector3.up * (plot.Height * 0.5f),
                        new Vector3(
                            plot.FootprintSize.x,
                            plot.Height,
                            plot.FootprintSize.y),
                        chapel ? WhitewashColor : TimberColor,
                        false),
                    chapel
                        ? MountainRoadSurfaceKind.Masonry
                        : MountainRoadSurfaceKind.Timber,
                    chapel ? WhitewashColor : TimberColor);
            }

            // Collision is the plan's, never the model's. An imported part
            // carries no collider by importer rule, and adding one to it is
            // how a floor once became a two-kilometre slab on its side.
            var collider = new GameObject("Physical Shell");
            collider.transform.SetParent(parent, false);
            collider.transform.localPosition =
                Vector3.up * (plot.Height * 0.5f);
            BoxCollider box = collider.AddComponent<BoxCollider>();
            box.size = new Vector3(
                plot.FootprintSize.x,
                plot.Height,
                plot.FootprintSize.y);

            BuildDoor(parent, plot, chapel, face.y);
            if (chapel)
            {
                // No lit windows: the chapel has none, and nothing about it
                // may read as a place anyone is inside.
                return;
            }

            BuildLitWindows(parent, plot, tallest, face);
        }

        /// <summary>
        /// The door, at real metres.
        ///
        /// This is why the kit ships none. A door modelled into the
        /// normalized cube scales with the house, and these run from a four
        /// metre cottage to the seven metre one at the head of the lane - the
        /// same mesh would be a hatch on one and a barn opening on the other.
        /// </summary>
        private static void BuildDoor(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            bool chapel,
            float wallFace)
        {
            float half = wallFace;
            var frame = RuntimePrimitiveFactory.CreateBox(
                "Door Frame",
                parent,
                new Vector3(0f, DoorHeight * 0.5f + 0.06f, half + 0.02f),
                new Vector3(DoorWidth + 0.22f, DoorHeight + 0.16f, 0.09f),
                StoneColor,
                false);
            Texture(
                frame,
                chapel
                    ? MountainRoadSurfaceKind.LayeredStone
                    : MountainRoadSurfaceKind.Timber,
                chapel ? StoneColor : DoorColor);

            Texture(
                RuntimePrimitiveFactory.CreateBox(
                    "Door Leaf",
                    parent,
                    new Vector3(0f, DoorHeight * 0.5f, half + 0.07f),
                    new Vector3(DoorWidth, DoorHeight, 0.07f),
                    DoorColor,
                    false),
                MountainRoadSurfaceKind.Timber,
                DoorColor);

            // A step, because a threshold on soil is a threshold in a puddle.
            Texture(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Door Step",
                    parent,
                    new Vector3(0f, 0.05f, half + 0.28f),
                    new Vector3(DoorWidth + 0.5f, 0.1f, 0.55f),
                    StoneColor,
                    true),
                MountainRoadSurfaceKind.LayeredStone,
                StoneColor);
        }

        private static void BuildLitWindows(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            bool tallest,
            Vector2 wallFace)
        {
            float half = wallFace.y;
            float halfWidth = wallFace.x;
            int perSide = tallest ? 3 : 2;
            float sill = Mathf.Min(1.35f, plot.Height * 0.32f);
            var size = new Vector3(0.78f, 0.92f, 0.05f);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int index = 0; index < perSide; index++)
                {
                    float amount = (index + 1f) / (perSide + 1f);
                    float across = Mathf.Lerp(
                        -halfWidth * 0.74f,
                        halfWidth * 0.74f,
                        amount);
                    CreateWindow(
                        parent,
                        new Vector3(across, sill, side * (half + 0.03f)),
                        size);
                }

                // And one in the gable, which is what makes a night village
                // read as two storeys rather than a row of boxes.
                if (plot.Height > 5f)
                {
                    CreateWindow(
                        parent,
                        new Vector3(
                            0f,
                            plot.Height * 0.62f,
                            side * (half + 0.03f)),
                        new Vector3(0.62f, 0.62f, 0.05f));
                }
            }

            for (int side = -1; side <= 1; side += 2)
            {
                CreateWindow(
                    parent,
                    new Vector3(side * (halfWidth + 0.03f), sill, 0f),
                    new Vector3(0.05f, 0.92f, 0.78f));
            }
        }

        /// <summary>
        /// A hole in the slope behind the houses, the timber holding it open,
        /// and the cart that used to come out of it - standing in the yard
        /// with firewood in it, which is the only way this village keeps
        /// anything the mine left.
        /// </summary>
        private static void BuildAdit(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            VillageAssetProvider kit)
        {
            if (kit != null)
            {
                PlaceKitAssembly(
                    parent,
                    kit,
                    VillageAssetKind.AditFrame,
                    0,
                    new Vector2(plot.FootprintSize.x, plot.FootprintSize.y),
                    plot.Height,
                    _ => AditTimberColor);

                var yard = new GameObject("Adit Yard");
                yard.transform.SetParent(parent, false);
                yard.transform.localPosition = new Vector3(
                    plot.FootprintSize.x * 0.42f,
                    0f,
                    -plot.FootprintSize.y * 0.9f);
                yard.transform.localRotation = Quaternion.Euler(0f, 24f, 0f);
                PlaceKitAssembly(
                    yard.transform,
                    kit,
                    VillageAssetKind.MineCart,
                    0,
                    new Vector2(1.05f, 1.7f),
                    1f,
                    role => role == VillageMeshRole.Wheels
                        ? CartWheelColor
                        : CartIronColor);

                var stack = new GameObject("Firewood In The Cart");
                stack.transform.SetParent(yard.transform, false);
                stack.transform.localPosition = new Vector3(0f, 0.42f, 0f);
                PlaceKitAssembly(
                    stack.transform,
                    kit,
                    VillageAssetKind.Firewood,
                    0,
                    new Vector2(0.62f, 0.86f),
                    0.42f,
                    _ => FirewoodColor);
            }

            // The mouth itself: an absence, and the darkest thing in the
            // village. It takes no sheet because it is not a surface.
            GameObject mouth = RuntimePrimitiveFactory.CreateBox(
                "Adit Darkness",
                parent,
                new Vector3(
                    0f,
                    plot.Height * 0.36f,
                    plot.FootprintSize.y * 0.5f - 0.34f),
                new Vector3(1.9f, plot.Height * 0.7f, 0.3f),
                new Color(0.026f, 0.028f, 0.026f, 1f),
                false);
            mouth.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;

            Texture(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Overgrown Spoil",
                    parent,
                    new Vector3(0f, 0.55f, -plot.FootprintSize.y * 0.32f),
                    new Vector3(
                        plot.FootprintSize.x * 1.2f,
                        1.1f,
                        plot.FootprintSize.y * 0.8f),
                    SoilColor,
                    true),
                MountainRoadSurfaceKind.ForestFloor,
                SoilColor);
        }

        /// <summary>
        /// The burial ground. Rows of low markers and no signpost anywhere:
        /// the hero does not visit his father's grave, so nothing here invites
        /// him to.
        /// </summary>
        private static void BuildCemetery(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            VillageAssetProvider kit)
        {
            Texture(
                RuntimePrimitiveFactory.CreateBox(
                    "Burial Ground",
                    parent,
                    new Vector3(0f, 0.06f, 0f),
                    new Vector3(
                        plot.FootprintSize.x,
                        0.12f,
                        plot.FootprintSize.y),
                    SoilColor,
                    false),
                MountainRoadSurfaceKind.ForestFloor,
                SoilColor);

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    var position = new Vector3(
                        Mathf.Lerp(
                            -plot.FootprintSize.x * 0.36f,
                            plot.FootprintSize.x * 0.36f,
                            column / 4f),
                        0.12f,
                        Mathf.Lerp(
                            -plot.FootprintSize.y * 0.32f,
                            plot.FootprintSize.y * 0.32f,
                            row / 3f));
                    if (kit == null)
                    {
                        Texture(
                            RuntimePrimitiveFactory.CreateBox(
                                "Grave Marker",
                                parent,
                                position + Vector3.up * 0.36f,
                                new Vector3(0.36f, 0.72f, 0.12f),
                                StoneColor,
                                false),
                            MountainRoadSurfaceKind.LayeredStone,
                            StoneColor);
                        continue;
                    }

                    var marker = new GameObject($"Grave {row}-{column}");
                    marker.transform.SetParent(parent, false);
                    marker.transform.localPosition = position;

                    // Every row leans a little differently. A cemetery of
                    // parallel stones is a car park.
                    marker.transform.localRotation = Quaternion.Euler(
                        0f,
                        (row * 5 + column) % 7 * 4.5f - 13.5f,
                        0f);
                    PlaceKitAssembly(
                        marker.transform,
                        kit,
                        VillageAssetKind.GraveMarker,
                        (row * 5 + column) % 3,
                        new Vector2(0.42f, 0.18f),
                        0.8f,
                        _ => StoneColor);
                }
            }
        }

        /// <summary>
        /// Garlands across the lane, in every season and for no occasion.
        ///
        /// This is the zone. It is also the one place where a naive
        /// implementation would fall over: a bulb is not a light. Eighty-odd
        /// real point lights would blow URP's additional-light budget on its
        /// own, so the bulbs are EMISSIVE GEOMETRY in one combined mesh per
        /// span, and only every other span carries an actual lamp. Six
        /// realtime lights light the whole village.
        /// </summary>
        private static void BuildGarlands(
            Transform parent,
            AlpineVillagePlan plan)
        {
            var root = new GameObject("Village Garlands");
            root.transform.SetParent(parent, false);

            int spans = Mathf.Max(
                1,
                Mathf.FloorToInt(
                    (plan.Lane.Length - GarlandFirstDistance * 2f) /
                    GarlandSpacing));
            for (int span = 0; span <= spans; span++)
            {
                float distance = GarlandFirstDistance + span * GarlandSpacing;
                if (distance > plan.Lane.Length - GarlandFirstDistance)
                {
                    break;
                }

                AlpineVillageLaneSample sample = plan.Lane.Sample(distance);
                float reach = sample.Width * 0.5f + GarlandAnchorReach;
                Vector3 left = sample.Position -
                               sample.Right * reach +
                               Vector3.up * GarlandHeight;
                Vector3 right = sample.Position +
                                sample.Right * reach +
                                Vector3.up * GarlandHeight;
                BuildGarlandSpan(root.transform, left, right, span);
            }
        }

        private static void BuildGarlandSpan(
            Transform parent,
            Vector3 left,
            Vector3 right,
            int span)
        {
            var wire = new List<RuntimeOrientedBox>(GarlandSegments);
            var bulbs = new List<RuntimeOrientedBox>(GarlandSegments);
            Vector3 previous = SampleGarland(left, right, 0f);
            for (int step = 1; step <= GarlandSegments; step++)
            {
                float amount = step / (float)GarlandSegments;
                Vector3 current = SampleGarland(left, right, amount);
                Vector3 delta = current - previous;
                float length = delta.magnitude;
                if (length > 0.0001f)
                {
                    wire.Add(new RuntimeOrientedBox(
                        parent.InverseTransformPoint(
                            (previous + current) * 0.5f),
                        Quaternion.FromToRotation(
                            Vector3.forward,
                            delta / length),
                        new Vector3(0.028f, 0.028f, length + 0.01f)));
                }

                if (step % 2 == 0)
                {
                    bulbs.Add(new RuntimeOrientedBox(
                        parent.InverseTransformPoint(
                            current + Vector3.down * 0.09f),
                        Quaternion.identity,
                        new Vector3(0.10f, 0.13f, 0.10f)));
                }

                previous = current;
            }

            GameObject cord = RuntimePrimitiveFactory
                .CreateCombinedOrientedBoxes(
                    $"Garland Wire {span:00}",
                    parent,
                    wire,
                    GarlandWireColor,
                    false,
                    1f,
                    RuntimeWorldUvMode.BoxProjected);
            MeshRenderer cordRenderer = cord.GetComponent<MeshRenderer>();
            cordRenderer.shadowCastingMode = ShadowCastingMode.Off;

            GameObject lamps = RuntimePrimitiveFactory
                .CreateCombinedOrientedBoxes(
                    $"Garland Bulbs {span:00}",
                    parent,
                    bulbs,
                    GarlandBulbColor,
                    false,
                    1f,
                    RuntimeWorldUvMode.BoxProjected);
            MeshRenderer bulbRenderer = lamps.GetComponent<MeshRenderer>();

            // The combined factory has no material overload, so the emissive
            // sheet is swapped in afterwards; the colour property block it
            // already wrote survives the swap.
            bulbRenderer.sharedMaterial = CityNightResources.EmissiveMaterial;
            bulbRenderer.shadowCastingMode = ShadowCastingMode.Off;

            if (span % 2 != 0)
            {
                return;
            }

            // One real lamp every other span. Intensity is set by THROW, not
            // by taste: this hangs about four metres over the lane and is
            // asked for a warm pool a few metres across, which puts it below
            // the City's door bulbs at `64-110` over `7-8 m`.
            var lightObject = new GameObject($"Garland Lamp {span:00}");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.position =
                SampleGarland(left, right, 0.5f) + Vector3.down * 0.2f;
            Light lamp = lightObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.79f, 0.52f);
            lamp.intensity = 52f;
            lamp.range = 11f;
            lamp.shadows = LightShadows.None;
            lamp.renderMode = LightRenderMode.ForcePixel;
            lamp.bounceIntensity = 0.1f;
        }

        /// <summary>
        /// A point on the sagging cord. Parabolic rather than a true
        /// catenary: at this span the two are a couple of centimetres apart
        /// and one of them is free.
        /// </summary>
        private static Vector3 SampleGarland(
            Vector3 left,
            Vector3 right,
            float amount)
        {
            Vector3 straight = Vector3.Lerp(left, right, amount);
            float sag = 4f * GarlandSag * amount * (1f - amount);
            return straight - Vector3.up * sag;
        }

        private static void CreateWindow(
            Transform parent,
            Vector3 localPosition,
            Vector3 size)
        {
            GameObject window = RuntimePrimitiveFactory.CreateBox(
                "Lit Window",
                parent,
                localPosition,
                size,
                WindowGlowColor,
                CityNightResources.EmissiveMaterial,
                false);
            window.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;
        }

        private static void Texture(
            GameObject instance,
            MountainRoadSurfaceKind surface,
            Color tint)
        {
            if (instance == null)
            {
                return;
            }

            MountainRoadSurfaceAppearance.Apply(
                instance.GetComponent<Renderer>(),
                surface,
                tint);
        }
    }
}
