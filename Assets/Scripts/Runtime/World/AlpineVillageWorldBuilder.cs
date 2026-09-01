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
            IDictionary<string, Transform> semanticObjects,
            AlpineVillageSnowTreading snowTreading,
            MothersHouseEntrance mothersHouseEntrance,
            IList<LockedDoorInteraction> houseDoors)
        {
            SnowTreading = snowTreading;
            Root = root ?? throw new ArgumentNullException(nameof(root));
            TerrainRoot = terrainRoot ??
                throw new ArgumentNullException(nameof(terrainRoot));
            LaneSurface = laneSurface ??
                throw new ArgumentNullException(nameof(laneSurface));
            Cableway = cableway ??
                throw new ArgumentNullException(nameof(cableway));
            WalkableArea = walkableArea ??
                throw new ArgumentNullException(nameof(walkableArea));
            MothersHouseEntrance = mothersHouseEntrance ??
                throw new ArgumentNullException(nameof(mothersHouseEntrance));
            HouseDoors = new ReadOnlyCollection<LockedDoorInteraction>(
                new List<LockedDoorInteraction>(
                    houseDoors ??
                    throw new ArgumentNullException(nameof(houseDoors))));
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

        /// <summary>The lying snow, and the thing a boot presses into it.
        /// Null only if the plan yielded no snow at all.</summary>
        public AlpineVillageSnowTreading SnowTreading { get; }

        public GameObject StationRoot => Cableway.StationRoot;
        public AlpineVillageWalkableArea WalkableArea { get; }
        public IReadOnlyDictionary<string, Transform> SemanticObjects { get; }
        public MothersHouseEntrance MothersHouseEntrance { get; }

        /// <summary>
        /// The shut doors: one per house on the lane, the mother's excepted.
        /// They carry the ordinary door gesture and answer with a line.
        /// </summary>
        public IReadOnlyList<LockedDoorInteraction> HouseDoors { get; }
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

        /// <summary>
        /// The ground mesh's two submeshes. Index `0` is the bowl floor on
        /// the ordinary shared primitive material; index `1` is the
        /// enclosing rise on <see cref="AlpineVillageRidgeAppearance"/>'s
        /// fog-floored material. The warmth pass and the tests address the
        /// floor by this index rather than by "the first one".
        /// </summary>
        public const int TerrainFloorMaterialIndex = 0;

        public const int TerrainRiseMaterialIndex = 1;

        /// <summary>
        /// The one object holding the lying snow. Named here because the
        /// contract that keeps it visual - that it carries no collider - is
        /// asserted against this name.
        /// </summary>
        internal const string SnowDriftObjectName = "Village Snow Drifts";

        /// <summary>How far the lane skin is laid over the ground it covers.
        /// </summary>
        internal const float LaneSkinLift = 0.08f;

        /// <summary>
        /// How many quads the lane skin is cut into across its width. At
        /// `3.6 m` that is `0.9 m` a quad, which is fine enough that the
        /// ground's own curve cannot bulge through the chord between two of
        /// them.
        /// </summary>
        internal const int LaneSkinCrossSteps = 6;

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
        private static readonly Color FadedGreenColor =
            new Color(0.225f, 0.300f, 0.255f, 1f);
        private static readonly Color FadedBlueColor =
            new Color(0.235f, 0.285f, 0.315f, 1f);
        private static readonly Color FadedRedColor =
            new Color(0.345f, 0.205f, 0.170f, 1f);

        /// <summary>A door is a door at every house on the lane, whatever
        /// the house is. That is why the kit ships none.</summary>
        public const float DoorHeight = 2.05f;

        public const float DoorWidth = 0.92f;

        /// <summary>Where a hand meets a door.</summary>
        public const float DoorHandleHeight = 1.02f;

        /// <summary>
        /// How high over the threshold a door's interaction trigger sits.
        /// Chest height, so the interactor's own overlap sphere - which is
        /// cast from `0.8 m` over the hero's root - meets it squarely rather
        /// than clipping its bottom cap.
        /// </summary>
        public const float DoorInteractionHeight = 0.82f;

        /// <summary>What a house door on the lane offers.</summary>
        public const string HouseDoorPromptKey =
            "interaction.open_village_door";

        /// <summary>
        /// What it answers with. Every house but the mother's is shut: the
        /// village has one interior and the rest are lived in by people the
        /// hero has no business calling on.
        /// </summary>
        public const string HouseDoorLockedKey =
            "alpine_village.house_door.locked";

        /// <summary>The one object carrying a shut house's interaction.
        /// Named here because the tests address it by name.</summary>
        internal const string HouseDoorObjectName = "Interactive House Door";

        public const float GarlandHeight = 4.6f;
        public const float GarlandAnchorReach = 1.8f;
        public const float GarlandSag = 0.85f;
        public const int GarlandSegments = 14;
        public const float GarlandPostHeight = 3.1f;
        public const float GarlandPostWireAnchorHeight = 2.58f;
        public const float GarlandLampIntensity = 52f;
        public const float WindowSnowPoolIntensity = 34f;
        public const float SummitWindowSnowPoolIntensity = 44f;

        private const float GarlandHouseReach = 6.5f;
        internal const float GarlandHouseAnchorSlack = 1f;
        private static readonly float[] GarlandDistanceBeats =
        {
            8.5f, 16.5f, 27.5f, 37f, 46.5f,
            55f, 63.5f, 71.5f, 76f
        };

        internal static int GarlandSpanCount => GarlandDistanceBeats.Length;

        // The spring's wet ground and its water moved to
        // `AlpineVillageBrookBuilder` when the water stopped being a stone
        // box and became a real surface: both belong to the brook, which
        // runs the length of the village and never fitted in a plot.

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

        internal static Color CleanSnowTint => SnowColor;
        internal static Color GarlandBulbTint => GarlandBulbColor;
        internal static Color WarmWindowTint => WindowGlowColor;

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
            VillageAssetProvider kit = VillageAssetProvider.LoadOrThrow();

            var root = new GameObject("Alpine Village");
            root.transform.SetParent(parent, false);
            var semanticObjects = new Dictionary<string, Transform>(
                StringComparer.Ordinal);

            GameObject terrainRoot = BuildTerrain(root.transform, plan);
            GameObject laneSurface = BuildLane(root.transform, plan);
            BuildPathSurfaces(root.transform, plan);

            // Before the snow, so the drifts can be told where the open
            // water is and keep off it.
            AlpineVillageBrookBuilder.Build(
                root.transform,
                plan,
                kit,
                semanticObjects);
            AlpineVillageSnowTreading snowTreading =
                BuildSnowDrifts(root.transform, plan);

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
            semanticObjects[
                AlpineVillageDressingPlanner
                    .StationMechanismOwnerStableId] =
                cableway.Bullwheel;

            var houseDoors = new List<LockedDoorInteraction>();
            BuildPlots(
                root.transform,
                plan,
                kit,
                semanticObjects,
                houseDoors);
            MothersHouseEntrance mothersHouseEntrance =
                BuildMothersHouseEntrance(
                    root.transform,
                    plan.MothersHouse,
                    plan.MothersHouseReturnPosition);
            BuildVillageDressing(
                root.transform,
                plan,
                kit,
                semanticObjects);
            BuildGarlands(root.transform, plan, kit, semanticObjects);

            var walkableArea = new AlpineVillageWalkableArea(plan);
            return new AlpineVillageWorldResult(
                root,
                terrainRoot,
                laneSurface,
                cableway,
                walkableArea,
                semanticObjects,
                snowTreading,
                mothersHouseEntrance,
                houseDoors);
        }

        /// <summary>
        /// One grid mesh over the whole plan, sampled from the shared height
        /// contract, with ONE collider and TWO submeshes.
        ///
        /// The floor needs no second material. The enclosing rise does:
        /// on the floor's plain Exp2 fog a wall `85 m` off is at `12 %`
        /// between gusts and gone at a gust crest, so the bowl that was
        /// moved in to loom would vanish exactly when the storm is fullest.
        /// A cell is rise when the sampler's ridge term is non-zero there,
        /// and that is the whole rule.
        ///
        /// The cableway cut used to be carved out of it and handed to the
        /// FLOOR material, on the argument that the cabin passes those
        /// slopes at a few metres and should not meet the distant wall's
        /// fog floor and cold tint. What that produced, seen from the
        /// village, was a bright band `38 m` wide running straight up a
        /// `50 m` dark wall - and a pale vertical strip in a dark mountain
        /// does not read as a valley, it reads as a HOLE. The lead saw it
        /// as a gap in the overhang and was right. One material over the
        /// whole rise leaves no seam to misread: the cut is a gorge cut
        /// into the mountain, which is what it is. The ride keeps its
        /// close-up honesty from the shader instead of from the split -
        /// inside `NativeFogNearDistance` the wall is on the same native
        /// Exp2 the floor is. Both materials now apply the same projected PS1
        /// vertex snap, so adjacent floor and rise cells share the grid's
        /// exact toe indices. No duplicated coplanar ring is needed, and the
        /// collider remains the one unsplit grid mesh.
        ///
        /// IT CARRIES NO VERTEX COLOURS, and it never usefully did. Every
        /// vertex used to be tinted snow-to-soil by its distance from the
        /// lane and the paths, on the reasoning above - and `Ps1Lit` is a
        /// verbatim copy of URP Lit, whose `Attributes` has no `COLOR`
        /// semantic at all, so not one of those colours ever reached a
        /// shader. The compacted ground a player actually sees is the path
        /// ribbons' own `ForestFloor` sheet, which is a different mesh and a
        /// different texture. Reviving the tint would mean hand-editing a
        /// clone the architecture notes require to stay verbatim, for one
        /// mesh in one scene; the dead field is gone instead.
        /// </summary>
        private static GameObject BuildTerrain(
            Transform parent,
            AlpineVillagePlan plan)
        {
            // TerrainBounds is the inhabited inner bowl. Sampling only that
            // rectangle means SampleRidgeRise is zero at every built vertex:
            // the planned mountains exist only as descriptors and the ground
            // simply ends. TerrainMeshBounds carries the complete physical
            // rise, the hidden crest and the cableway brink.
            Rect bounds = plan.TerrainMeshBounds;
            int columns = Mathf.Max(
                1,
                Mathf.CeilToInt(bounds.width / TerrainCellSize));
            int rows = Mathf.Max(
                1,
                Mathf.CeilToInt(bounds.height / TerrainCellSize));

            int gridVertexCount = (columns + 1) * (rows + 1);
            var vertices = new List<Vector3>(gridVertexCount);
            var uvs = new List<Vector2>(gridVertexCount);
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
                    vertices.Add(new Vector3(x, height, z));
                    uvs.Add(AlpineVillageRidgeAppearance.CreateWorldUv(point));
                }
            }

            // Classify every cell once at its centre, then split.
            var riseCells = new bool[rows, columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    riseCells[row, column] = IsRiseCell(
                        plan,
                        bounds,
                        columns,
                        rows,
                        row,
                        column);
                }
            }

            var floorTriangles = new List<int>(columns * rows * 6);
            var riseTriangles = new List<int>();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int origin = row * (columns + 1) + column;
                    if (riseCells[row, column])
                    {
                        AppendCell(riseTriangles, origin, columns);
                        continue;
                    }

                    AppendCell(floorTriangles, origin, columns);
                }
            }

            var mesh = new Mesh
            {
                name = "Alpine Village Ground",
                indexFormat = vertices.Count > 65000
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(floorTriangles, TerrainFloorMaterialIndex);
            mesh.SetTriangles(riseTriangles, TerrainRiseMaterialIndex);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = new GameObject("Village Ground");
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            // Shadows stay on for the renderer: the floor casts them as
            // before. The rise's shader has no ShadowCaster pass, so the
            // wall casts none - recorded on AlpineVillageRidgeAppearance.
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            // Both slots exist before either indexed apply, and the array is
            // written again after them: the indexed path never assigns
            // `sharedMaterial`, but the order is the contract and this is
            // what makes it visible.
            Material[] materials =
            {
                RuntimePrimitiveFactory.DefaultMaterial,
                AlpineVillageRidgeAppearance.RidgeMaterial
            };
            renderer.sharedMaterials = materials;
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer,
                AlpineVillageRidgeAppearance.Surface,
                SnowColor,
                TerrainFloorMaterialIndex);

            AlpineVillageRidgeAppearance.Apply(
                renderer,
                TerrainRiseMaterialIndex);
            renderer.sharedMaterials = materials;

            host.AddComponent<MeshCollider>().sharedMesh = mesh;
            return host;
        }

        /// <summary>
        /// Rise iff the sampler's ridge term is non-zero at the cell centre.
        /// Shared with the tests through <see cref="IsRiseCellCentre"/>.
        /// </summary>
        private static bool IsRiseCell(
            AlpineVillagePlan plan,
            Rect bounds,
            int columns,
            int rows,
            int row,
            int column)
        {
            var centre = new Vector2(
                bounds.xMin + bounds.width * ((column + 0.5f) / columns),
                bounds.yMin + bounds.height * ((row + 0.5f) / rows));
            return IsRiseCellCentre(plan, centre);
        }

        /// <summary>
        /// The classification rule at one point, pure, so the mesh test can
        /// re-derive every cell from the plan. One term: the cableway cut
        /// is carved out of the wall and stays part of it.
        /// </summary>
        internal static bool IsRiseCellCentre(
            AlpineVillagePlan plan,
            Vector2 centre)
        {
            return AlpineVillageTerrainSampler.SampleRidgeRise(
                plan,
                centre) > 0f;
        }

        private static void AppendCell(
            List<int> triangles,
            int origin,
            int columns)
        {
            int nearRight = origin + 1;
            int farLeft = origin + columns + 1;
            int farRight = farLeft + 1;
            triangles.Add(origin);
            triangles.Add(farLeft);
            triangles.Add(nearRight);
            triangles.Add(nearRight);
            triangles.Add(farLeft);
            triangles.Add(farRight);
        }

        /// <summary>
        /// The visible half of every traversal branch. The physical terrain
        /// remains the collider; these lifted ribbons are only compacted soil
        /// and snow, sampled from the same ground under each metre.
        /// </summary>
        private static void BuildPathSurfaces(
            Transform parent,
            AlpineVillagePlan plan)
        {
            var root = new GameObject("Visible Village Paths");
            root.transform.SetParent(parent, false);
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            for (int index = 0; index < paths.Count; index++)
            {
                BuildPathSurface(root.transform, plan, paths[index]);
            }
        }

        private static void BuildPathSurface(
            Transform parent,
            AlpineVillagePlan plan,
            AlpineVillagePathDescriptor path)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(path.LengthXZ));
            var vertices = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            Vector3 direction = path.End - path.Start;
            direction.y = 0f;
            direction.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            for (int step = 0; step <= steps; step++)
            {
                float amount = step / (float)steps;
                Vector3 center = Vector3.Lerp(path.Start, path.End, amount);
                Vector3 across = right * path.SurfaceHalfWidth;
                // Each edge on its own ground, for the reason the lane skin
                // learned the hard way: a ribbon laid flat at its centre's
                // height is cut open by any ground that curves under it, and
                // the station exit is `2.5 m` wide.
                Vector3 left = center - across;
                Vector3 rightEdge = center + across;
                left.y = LaneSkinHeight(plan, left);
                rightEdge.y = LaneSkinHeight(plan, rightEdge);
                int vertex = step * 2;
                vertices[vertex] = left;
                vertices[vertex + 1] = rightEdge;
                float travelled = path.LengthXZ * amount * 0.42f;
                uvs[vertex] = new Vector2(0f, travelled);
                uvs[vertex + 1] = new Vector2(1f, travelled);
            }

            var triangles = new int[steps * 6];
            int cursor = 0;
            for (int step = 0; step < steps; step++)
            {
                int origin = step * 2;
                triangles[cursor++] = origin;
                triangles[cursor++] = origin + 2;
                triangles[cursor++] = origin + 1;
                triangles[cursor++] = origin + 1;
                triangles[cursor++] = origin + 2;
                triangles[cursor++] = origin + 3;
            }

            var mesh = new Mesh { name = path.StableId + " Surface" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = new GameObject("Visible Path - " + path.StableId);
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            MountainRoadSurfaceAppearance.Apply(
                renderer,
                MountainRoadSurfaceKind.ForestFloor,
                LaneColor);
        }

        /// <summary>
        /// The snow lying beside every trodden route, as one mesh.
        ///
        /// The ground the village stands on is sampled on a `2 m` grid, and a
        /// two-metre quad cannot hold a drift - so this is not a term in the
        /// height contract but its own skin laid over it, dense where it needs
        /// to be and nowhere else. That split is what makes the feature cheap:
        /// <see cref="AlpineVillageTerrainSampler"/> is untouched, and with it
        /// the collider, the shelves, the cableway brink and the walkable
        /// mask.
        ///
        /// IT CARRIES NO COLLIDER, deliberately. The hero walks the same flat
        /// ground he always did and the snow closes over his shins, which is
        /// both the cheapest and the most honest way to read deep snow -
        /// planar velocity is read back from achieved movement, so ground he
        /// could catch a boot on would read as a crawl.
        /// </summary>
        private static AlpineVillageSnowTreading BuildSnowDrifts(
            Transform parent,
            AlpineVillagePlan plan)
        {
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            // Kept beside the vertices so the treading pass can lower each
            // one back towards its own ground without re-deriving anything.
            var grounds = new List<float>();
            var depths = new List<float>();

            AppendLaneDrifts(
                plan, paths, vertices, uvs, triangles, grounds, depths);
            for (int index = 0; index < paths.Count; index++)
            {
                AppendPathDrifts(
                    plan,
                    paths,
                    paths[index],
                    vertices,
                    uvs,
                    triangles,
                    grounds,
                    depths);
            }

            AppendSnowField(
                plan, paths, vertices, uvs, triangles, grounds, depths);

            if (triangles.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = "Alpine Village Snow Drifts",
                indexFormat = vertices.Count > 65000
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = new GameObject(SnowDriftObjectName);
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            // A `0.45 m` lip casting into a `640x360` frame is acne, not
            // shape; the ground under it carries the shadow it already had.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            // The floor's own sheet and tint, so the drift is the ground
            // getting deeper rather than a second material lying on it.
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer,
                AlpineVillageRidgeAppearance.Surface,
                SnowColor,
                0);

            // The snow keeps its own vertices so a boot can press them. It
            // is armed here and given its walker by the scene root, because
            // the builder has no player and must not wait for one.
            AlpineVillageSnowTreading treading =
                host.AddComponent<AlpineVillageSnowTreading>();
            treading.Initialize(
                mesh,
                vertices.ToArray(),
                grounds.ToArray(),
                depths.ToArray(),
                null,
                null);
            return treading;
        }

        /// <summary>
        /// The lying snow everywhere the routes do not reach.
        ///
        /// The fitted ribbons carry everything that BENDS - the rise out of
        /// each trodden edge, which has to follow its route exactly - and
        /// this carries the flat remainder, where the depth has already
        /// saturated and only the world-space undulation moves it. So the
        /// pitch is set by that undulation's `15 m` waves rather than by
        /// anything near a route, and a cell whose centre the ribbons already
        /// cover is never emitted at all.
        ///
        /// The one cell of deliberate overlap is what closes the join: both
        /// surfaces are at full depth there, so the sheet is drawn
        /// <see cref="AlpineVillageSnowDrift.FieldBurial"/> under its own
        /// height and the fitted ribbon always wins.
        /// </summary>
        private static void AppendSnowField(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<float> grounds,
            List<float> depths)
        {
            Rect bowl = plan.TerrainBounds;
            float outset = AlpineVillageTerrainSampler.RidgeStandoff;
            float cell = AlpineVillageSnowDrift.FieldCellSize;
            var bounds = Rect.MinMaxRect(
                bowl.xMin - outset,
                bowl.yMin - outset,
                bowl.xMax + outset,
                bowl.yMax + outset);
            int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / cell));
            int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / cell));

            // One grid of vertices, then only the cells the ribbons leave.
            // The unused ones cost a vertex each and no triangle, which is
            // far cheaper than re-indexing a sparse grid.
            int origin = vertices.Count;
            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    var point = new Vector2(
                        bounds.xMin + bounds.width * (column / (float)columns),
                        bounds.yMin + bounds.height * (row / (float)rows));
                    float depth = AlpineVillageSnowDrift.SampleDepth(
                        plan,
                        paths,
                        point);
                    float ground = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        point);
                    float height =
                        ground + depth - AlpineVillageSnowDrift.FieldBurial;
                    vertices.Add(new Vector3(point.x, height, point.y));
                    grounds.Add(ground);
                    depths.Add(Mathf.Max(0f, height - ground));
                    uvs.Add(
                        AlpineVillageRidgeAppearance.CreateWorldUv(point));
                }
            }

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    var centre = new Vector2(
                        bounds.xMin +
                        bounds.width * ((column + 0.5f) / columns),
                        bounds.yMin +
                        bounds.height * ((row + 0.5f) / rows));
                    float outside = AlpineVillagePathPlanner
                        .MeasureDistanceOutsideTrodden(
                            plan,
                            paths,
                            centre,
                            out _);
                    if (outside <
                        AlpineVillageSnowDrift.RibbonReach - cell)
                    {
                        continue;
                    }

                    int corner = origin + row * (columns + 1) + column;
                    triangles.Add(corner);
                    triangles.Add(corner + columns + 1);
                    triangles.Add(corner + 1);
                    triangles.Add(corner + 1);
                    triangles.Add(corner + columns + 1);
                    triangles.Add(corner + columns + 2);
                }
            }
        }

        /// <summary>
        /// Both shoulders of the street. The lane's own plan samples are used
        /// rather than a re-walk: they already carry the carriageway's width
        /// and its right vector at every metre.
        /// </summary>
        private static void AppendLaneDrifts(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<float> grounds,
            List<float> depths)
        {
            IReadOnlyList<AlpineVillageLaneSample> samples =
                plan.Lane.Samples;
            for (int side = -1; side <= 1; side += 2)
            {
                var stations = new List<DriftStation>(samples.Count);
                for (int index = 0; index < samples.Count; index++)
                {
                    AlpineVillageLaneSample sample = samples[index];
                    Vector3 outward = sample.Right * side;
                    Vector3 edge = sample.Position +
                                   outward * (sample.Width * 0.5f);
                    stations.Add(new DriftStation(
                        new Vector2(edge.x, edge.z),
                        new Vector2(outward.x, outward.z).normalized));
                }

                AppendDriftStrip(
                    plan,
                    paths,
                    stations,
                    side < 0,
                    vertices,
                    uvs,
                    triangles,
                    grounds,
                    depths);
            }
        }

        private static void AppendPathDrifts(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            AlpineVillagePathDescriptor path,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<float> grounds,
            List<float> depths)
        {
            Vector3 direction = path.End - path.Start;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            direction.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    path.LengthXZ /
                    AlpineVillageSnowDrift.PathSampleStep));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 outward = right * side;
                var stations = new List<DriftStation>(steps + 1);
                for (int step = 0; step <= steps; step++)
                {
                    float amount = step / (float)steps;
                    Vector3 centre = Vector3.Lerp(
                        path.Start,
                        path.End,
                        amount);
                    Vector3 edge = centre +
                                   outward * path.SurfaceHalfWidth;
                    stations.Add(new DriftStation(
                        new Vector2(edge.x, edge.z),
                        new Vector2(outward.x, outward.z).normalized));
                }

                AppendDriftStrip(
                    plan,
                    paths,
                    stations,
                    side < 0,
                    vertices,
                    uvs,
                    triangles,
                    grounds,
                    depths);
            }
        }

        /// <summary>
        /// One shoulder: four vertices per station, marched outward from the
        /// trodden edge. Every vertex asks the depth field for ITS OWN point
        /// rather than sharing the station's, which is what lets a drift
        /// pinch shut on its own where another route crosses under it.
        /// </summary>
        private static void AppendDriftStrip(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            List<DriftStation> stations,
            bool mirrored,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<float> grounds,
            List<float> depths)
        {
            if (stations.Count < 2)
            {
                return;
            }

            // Every station carries the same count, because the strip is
            // indexed as a grid. Exposure only stretches WHERE the offsets
            // fall, never how many there are.
            var offsets = new List<float>();
            AlpineVillageSnowDrift.AppendCrossSectionOffsets(1f, offsets);
            int across = offsets.Count;

            int origin = vertices.Count;
            for (int index = 0; index < stations.Count; index++)
            {
                DriftStation station = stations[index];
                float exposure = AlpineVillageSnowDrift.MeasureExposure(
                    plan,
                    station.Outward);
                AlpineVillageSnowDrift.AppendCrossSectionOffsets(
                    exposure,
                    offsets);
                for (int step = 0; step < across; step++)
                {
                    // The outer edge meets the field sheet, which is drawn
                    // under its own height so the ribbon wins their overlap.
                    // Sink that one with it, or the ribbon ends in a step the
                    // height of that burial, ringing every route.
                    AppendDriftVertex(
                        plan,
                        paths,
                        station,
                        offsets[Mathf.Min(step, offsets.Count - 1)],
                        step == across - 1,
                        vertices,
                        uvs,
                        grounds,
                        depths);
                }
            }

            for (int index = 0; index < stations.Count - 1; index++)
            {
                for (int step = 0; step < across - 1; step++)
                {
                    int a = origin + index * across + step;
                    int b = a + across;
                    int c = a + 1;
                    int d = b + 1;
                    if (mirrored)
                    {
                        // The outward axis flips with the shoulder, and with
                        // it the handedness; without this the far shoulder of
                        // every route is back-face culled and reads as a hole.
                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(d);
                        triangles.Add(b);
                        continue;
                    }

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(d);
                }
            }
        }

        private static void AppendDriftVertex(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            DriftStation station,
            float offset,
            bool meetsTheField,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<float> grounds,
            List<float> depths)
        {
            Vector2 point = station.Edge + station.Outward * offset;
            float depth = AlpineVillageSnowDrift.SampleDepth(
                plan,
                paths,
                point);
            float ground = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                point);
            float height = depth <= 0.0005f
                ? ground - AlpineVillageSnowDrift.ToeBurial
                : ground + depth;
            if (meetsTheField)
            {
                height -= AlpineVillageSnowDrift.FieldBurial;
            }

            vertices.Add(new Vector3(point.x, height, point.y));
            grounds.Add(ground);
            depths.Add(Mathf.Max(0f, height - ground));
            // The ground's own planar UV, so the one sheet runs across the
            // toe at the pitch the snow beside it already has.
            uvs.Add(AlpineVillageRidgeAppearance.CreateWorldUv(point));
        }

        private readonly struct DriftStation
        {
            internal DriftStation(Vector2 edge, Vector2 outward)
            {
                Edge = edge;
                Outward = outward;
            }

            /// <summary>The trodden edge this shoulder starts from.</summary>
            internal Vector2 Edge { get; }

            /// <summary>Unit, away from the route.</summary>
            internal Vector2 Outward { get; }
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
            // ACROSS AS WELL AS ALONG, and each vertex on its own ground.
            //
            // The skin used to be one quad the full `3.6 m` width, laid flat
            // at the plan's centreline height, while the ground under it is
            // the sampler's. Wherever a plot shelf lifted that ground the
            // terrain won the depth test and showed through as a pale wedge
            // with hard polygon edges lying across the street - reported as
            // snow on the path, and not snow at all: the same wedges are
            // there with the snow renderer off. Measured before this: `423`
            // of `2490` probes across the carriageway, the worst standing
            // `0.44 m` proud. Two vertices cannot follow a curve; the path
            // ribbons never showed it only because they are narrower.
            int across = LaneSkinCrossSteps + 1;
            var vertices = new Vector3[samples.Count * across];
            var uvs = new Vector2[vertices.Length];
            for (int index = 0; index < samples.Count; index++)
            {
                AlpineVillageLaneSample sample = samples[index];
                float half = sample.Width * 0.5f;
                for (int step = 0; step < across; step++)
                {
                    float side = Mathf.Lerp(
                        -half,
                        half,
                        step / (float)LaneSkinCrossSteps);
                    Vector3 point = sample.Position + sample.Right * side;
                    point.y = LaneSkinHeight(plan, point);
                    int vertex = index * across + step;
                    vertices[vertex] = point;
                    uvs[vertex] = new Vector2(
                        step / (float)LaneSkinCrossSteps,
                        sample.Distance * 0.35f);
                }
            }

            var triangles =
                new int[(samples.Count - 1) * LaneSkinCrossSteps * 6];
            int cursor = 0;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                for (int step = 0; step < LaneSkinCrossSteps; step++)
                {
                    int origin = index * across + step;
                    triangles[cursor++] = origin;
                    triangles[cursor++] = origin + across;
                    triangles[cursor++] = origin + 1;
                    triangles[cursor++] = origin + 1;
                    triangles[cursor++] = origin + across;
                    triangles[cursor++] = origin + across + 1;
                }
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
        /// Where the lane skin sits over its own ground at one point.
        ///
        /// The lift has to clear more than the two surfaces disagree by: the
        /// terrain is drawn on a `2 m` grid and interpolates straight between
        /// samples, so across a shelf's curve its chord stands above the
        /// smooth height the skin is placed at. `SeamBurial` solves the same
        /// problem at the bowl's toe with the same order of number.
        /// </summary>
        private static float LaneSkinHeight(
            AlpineVillagePlan plan,
            Vector3 point)
        {
            return AlpineVillageTerrainSampler.SampleHeight(
                       plan,
                       new Vector2(point.x, point.z)) +
                   LaneSkinLift;
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
            VillageAssetProvider kit,
            IDictionary<string, Transform> semanticObjects,
            ICollection<LockedDoorInteraction> houseDoors)
        {
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
                    case AlpineVillagePlotKind.Spring:
                        BuildSpring(root.transform, plot, kit);
                        break;
                    default:
                        BuildBuilding(root.transform, plot, kit);
                        break;
                }

                // The mother's door is the one that opens, and it is built
                // with its destination further down. The chapel is a spur
                // errand rather than a home and keeps its plain threshold.
                if (plot.Kind == AlpineVillagePlotKind.House)
                {
                    houseDoors.Add(BuildHouseDoor(root.transform, plot));
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
        internal static void PlaceKitAssembly(
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
                VillageMeshPart part = kit.GetPartOrThrow(
                    kind,
                    variant,
                    role);

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

        private static readonly Color HeideHouseTimberColor =
            new Color(0.285f, 0.195f, 0.125f, 1f);
        private static readonly Color RenaissanceHouseTimberColor =
            new Color(0.385f, 0.285f, 0.195f, 1f);
        private static readonly Color MothersHouseTimberColor =
            new Color(0.365f, 0.275f, 0.190f, 1f);
        private static readonly Color RenaissancePlinthColor =
            new Color(0.515f, 0.495f, 0.455f, 1f);

        private static Color HouseTint(
            AlpineVillagePlotDescriptor plot,
            int houseVariant,
            VillageMeshRole role)
        {
            bool mothersHouse =
                plot.Kind == AlpineVillagePlotKind.MothersHouse;
            switch (role)
            {
                case VillageMeshRole.Roof:
                    return RoofColor;
                case VillageMeshRole.Snow:
                    return SnowColor;
                case VillageMeshRole.Plinth:
                    return !mothersHouse && houseVariant == 1
                        ? RenaissancePlinthColor
                        : StoneColor;
                case VillageMeshRole.Chimney:
                    // The top-house generator groups its whitewashed side
                    // wing with the chimney so the timber body can keep the
                    // ordinary Walls role and surface family.
                    return mothersHouse
                        ? WhitewashColor
                        : ChimneyColor;
                case VillageMeshRole.Walls:
                    if (mothersHouse)
                    {
                        return MothersHouseTimberColor;
                    }

                    // Variant zero is the compact dark Heide house. Variant
                    // one is the taller Renaissance block house; both stay
                    // weathered timber, and light remains the only warmth.
                    return houseVariant == 0
                        ? HeideHouseTimberColor
                        : RenaissanceHouseTimberColor;
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
                case VillageMeshRole.Snow:
                    return SnowColor;
                case VillageMeshRole.Plinth:
                    return StoneColor;
                default:
                    return WhitewashColor;
            }
        }

        private static Color FacadeDetailTint(
            AlpineVillagePlotDescriptor plot,
            int houseVariant,
            int detailVariant,
            VillageMeshRole role)
        {
            switch (role)
            {
                case VillageMeshRole.Repair:
                    return plot.Kind == AlpineVillagePlotKind.MothersHouse
                        ? new Color(0.625f, 0.595f, 0.540f, 1f)
                        : houseVariant == 0
                            ? StoneColor
                            : RenaissancePlinthColor;
                case VillageMeshRole.Bracket:
                    return RustColor;
                case VillageMeshRole.Shutters:
                    if (plot.Kind == AlpineVillagePlotKind.MothersHouse)
                    {
                        return FadedGreenColor;
                    }

                    if (houseVariant == 0)
                    {
                        return detailVariant % 2 == 0
                            ? FadedBlueColor
                            : FadedGreenColor;
                    }

                    return detailVariant % 2 == 0
                        ? FadedRedColor
                        : FadedGreenColor;
                default:
                    return TimberColor;
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
            VillageAssetKind kind = chapel
                ? VillageAssetKind.Chapel
                : tallest
                    ? VillageAssetKind.TopHouse
                    : VillageAssetKind.House;
            int variant =
                VillageAssetProvider.SelectVariant(kind, plot.StableId);

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
                PlaceKitAssembly(
                    parent,
                    kit,
                    kind,
                    variant,
                    plot.FootprintSize,
                    plot.Height,
                    chapel ? (Func<VillageMeshRole, Color>)ChapelTint
                        : role => HouseTint(plot, variant, role));
                if (kit.TryGetPart(
                        kind,
                        variant,
                        VillageMeshRole.Walls,
                        out VillageMeshPart walls))
                {
                    Bounds local = walls.Mesh.bounds;
                    face = new Vector2(
                        Mathf.Max(
                            Mathf.Abs(local.min.x),
                            Mathf.Abs(local.max.x)) *
                        plot.FootprintSize.x,
                        local.max.z * plot.FootprintSize.y);

                    // The mother's whitewashed wing lives in the existing
                    // masonry Chimney bucket and reaches farther right than
                    // the timber Walls mesh. Side openings address the full
                    // authored envelope rather than the asymmetric body.
                    if (tallest && kit.TryGetPart(
                            kind,
                            variant,
                            VillageMeshRole.Chimney,
                            out VillageMeshPart wing))
                    {
                        Bounds wingBounds = wing.Mesh.bounds;
                        face.x = Mathf.Max(
                            face.x,
                            Mathf.Max(
                                Mathf.Abs(wingBounds.min.x),
                                Mathf.Abs(wingBounds.max.x)) *
                            plot.FootprintSize.x);
                    }
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

            BuildFacadeDetail(parent, plot, kit, variant, face);
            BuildLitWindows(parent, plot, tallest, variant, face);
            if (tallest ||
                plot.StableId == "village-house-01" ||
                plot.StableId == "village-house-07")
            {
                BuildWindowSnowPool(parent, tallest, face.y);
            }
        }

        /// <summary>
        /// One close-read repair per house: old shutters, a patch of later
        /// whitewash and the iron bracket that has kept them up. It is an
        /// authored Blender assembly at real human scale, never stretched to
        /// fill the building descriptor.
        /// </summary>
        private static void BuildFacadeDetail(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            VillageAssetProvider kit,
            int houseVariant,
            Vector2 wallFace)
        {
            if (kit == null)
            {
                return;
            }

            int variant = VillageAssetProvider.SelectVariant(
                VillageAssetKind.FacadeDetail,
                plot.StableId);
            bool mothersHouse =
                plot.Kind == AlpineVillagePlotKind.MothersHouse;
            float across;
            float baseHeight;
            Vector2 footprint;
            float height;
            if (mothersHouse)
            {
                // The stone wing occupies local +X. This close-read repair
                // stays on the timber main block instead of crossing the
                // material seam.
                across = -wallFace.x * 0.22f;
                baseHeight = 1.05f;
                footprint = new Vector2(1.90f, 0.18f);
                height = 1.32f;
            }
            else if (houseVariant == 0)
            {
                across = (variant % 2 == 0 ? -1f : 1f) *
                         wallFace.x * 0.34f;
                baseHeight = 0.58f;
                footprint = new Vector2(1.62f, 0.18f);
                height = 1.10f;
            }
            else
            {
                across = (variant % 2 == 0 ? 1f : -1f) *
                         wallFace.x * 0.27f;
                baseHeight = 1.04f;
                footprint = new Vector2(1.86f, 0.18f);
                height = 1.28f;
            }

            var anchor = new GameObject("Weathered Facade Detail");
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = new Vector3(
                across,
                baseHeight,
                wallFace.y + 0.09f);
            PlaceKitAssembly(
                anchor.transform,
                kit,
                VillageAssetKind.FacadeDetail,
                variant,
                footprint,
                height,
                role => FacadeDetailTint(
                    plot,
                    houseVariant,
                    variant,
                    role));
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

            // The plan's, for every kind. It used to be the plan's only for
            // the mother's house and a mesh-variant guess for the rest,
            // which was survivable while nothing stood at these doors; now
            // the hero walks to one, and the leaf has to be where the plan
            // put the threshold, the dock and the path.
            float across = plot.DoorAcrossOffset;
            var frame = RuntimePrimitiveFactory.CreateBox(
                "Door Frame",
                parent,
                new Vector3(
                    across,
                    DoorHeight * 0.5f + 0.06f,
                    half + 0.02f),
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
                    new Vector3(
                        across,
                        DoorHeight * 0.5f,
                        half + 0.07f),
                    new Vector3(DoorWidth, DoorHeight, 0.07f),
                    DoorColor,
                    false),
                MountainRoadSurfaceKind.Timber,
                DoorColor);

            // The handle. Small, and the only part of a door anyone ever
            // touches - which is exactly why it is here: the leaf alone
            // reads as a panel, and a panel does not invite the key that
            // now does something at every house on the lane.
            float handleSide = across >= 0f ? 1f : -1f;
            Texture(
                RuntimePrimitiveFactory.CreateBox(
                    "Door Handle",
                    parent,
                    new Vector3(
                        across + handleSide * DoorWidth * 0.33f,
                        DoorHandleHeight,
                        half + 0.135f),
                    new Vector3(0.055f, 0.055f, 0.16f),
                    IronColor,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                IronColor);

            // A step, because a threshold on soil is a threshold in a puddle.
            Texture(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Door Step",
                    parent,
                    new Vector3(across, 0.05f, half + 0.28f),
                    new Vector3(DoorWidth + 0.5f, 0.1f, 0.55f),
                    StoneColor,
                    true),
                MountainRoadSurfaceKind.LayeredStone,
                StoneColor);
        }

        /// <summary>
        /// The standard door interaction, on a house that stays shut.
        ///
        /// It is <see cref="BuildMothersHouseEntrance"/> with the
        /// destination removed: the same trigger over the plan's threshold,
        /// the same plan-owned dock and facing, the same door gesture. Only
        /// the ending differs - one line instead of a scene load.
        ///
        /// Every position it uses is read from the plan rather than measured
        /// off the shell, so the trigger, the dock, the trodden path that
        /// arrives at it and the leaf the hero reaches for are the same four
        /// numbers. The dock keeps the plot shelf's own height, which is the
        /// only reason the gesture ever starts: a dock more than
        /// <see cref="PlayerMotor.InteractionVerticalTolerance"/> off the
        /// hero's root is refused in silence.
        /// </summary>
        private static LockedDoorInteraction BuildHouseDoor(
            Transform parent,
            AlpineVillagePlotDescriptor plot)
        {
            Vector3 dock = plot.DoorDockPosition +
                           Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 interaction = plot.DoorGroundPosition +
                                  Vector3.up * DoorInteractionHeight;
            var host = new GameObject(HouseDoorObjectName);
            host.transform.SetParent(parent, false);
            host.transform.position = interaction;
            SphereCollider trigger = host.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = AlpineVillagePlanner.HouseDoorTriggerRadius;

            LockedDoorInteraction door =
                host.AddComponent<LockedDoorInteraction>();
            door.Configure(HouseDoorPromptKey, HouseDoorLockedKey);
            PlayerDoorActionTarget doorAction =
                host.AddComponent<PlayerDoorActionTarget>();
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    interaction,
                    dock,
                    -plot.Facing));
            return door;
        }

        private static MothersHouseEntrance BuildMothersHouseEntrance(
            Transform parent,
            AlpineVillagePlotDescriptor house,
            Vector3 returnGroundPosition)
        {
            if (house == null ||
                house.Kind != AlpineVillagePlotKind.MothersHouse)
            {
                throw new ArgumentException(
                    "The village entrance requires the mother's house plan.",
                    nameof(house));
            }

            Vector3 dock = house.DoorDockPosition +
                           Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 interaction = house.DoorGroundPosition +
                                  Vector3.up * DoorInteractionHeight;
            var entranceObject = new GameObject(
                "Interactive Mothers House Entrance");
            entranceObject.transform.SetParent(parent, false);
            entranceObject.transform.position = interaction;
            SphereCollider trigger =
                entranceObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius =
                AlpineVillagePlanner.MothersHouseEntranceTriggerRadius;

            MothersHouseEntrance entrance =
                entranceObject.AddComponent<MothersHouseEntrance>();
            entrance.Configure(
                returnGroundPosition +
                Vector3.up * PlayerFactory.GroundedRootOffset);
            PlayerDoorActionTarget doorAction =
                entranceObject.AddComponent<PlayerDoorActionTarget>();
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    interaction,
                    dock,
                    -house.Facing));
            return entrance;
        }

        private static void BuildLitWindows(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            bool tallest,
            int houseVariant,
            Vector2 wallFace)
        {
            float half = wallFace.y;
            float halfWidth = wallFace.x;
            if (tallest)
            {
                float sill = Mathf.Clamp(
                    plot.Height * 0.29f,
                    1.85f,
                    2.15f);
                float[] across = { -0.54f, -0.08f, 0.46f };
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int index = 0; index < across.Length; index++)
                    {
                        CreateWindow(
                            parent,
                            new Vector3(
                                halfWidth * across[index],
                                sill,
                                side * (half + 0.03f)),
                            new Vector3(0.74f, 0.90f, 0.05f));
                    }

                    CreateWindow(
                        parent,
                        new Vector3(
                            -halfWidth * 0.16f,
                            plot.Height * 0.59f,
                            side * (half + 0.03f)),
                        new Vector3(0.66f, 0.66f, 0.05f));
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    for (int index = -1; index <= 1; index += 2)
                    {
                        CreateWindow(
                            parent,
                            new Vector3(
                                side * (halfWidth + 0.03f),
                                sill,
                                index * half * 0.28f),
                            new Vector3(0.05f, 0.88f, 0.68f));
                    }
                }

                return;
            }

            if (houseVariant == 0)
            {
                // The compact Heide house keeps small, slightly irregular
                // openings in the timber block.
                float sill = Mathf.Clamp(
                    plot.Height * 0.31f,
                    1.28f,
                    1.90f);
                float[] across = { -0.36f, 0.24f };
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int index = 0; index < across.Length; index++)
                    {
                        CreateWindow(
                            parent,
                            new Vector3(
                                halfWidth *
                                    (side > 0
                                        ? across[index]
                                        : -across[index]),
                                sill + index * 0.10f,
                                side * (half + 0.03f)),
                            new Vector3(0.60f, 0.74f, 0.05f));
                    }

                    if (plot.Height > 5.15f)
                    {
                        CreateWindow(
                            parent,
                            new Vector3(
                                side * halfWidth * 0.07f,
                                plot.Height * 0.62f,
                                side * (half + 0.03f)),
                            new Vector3(0.52f, 0.54f, 0.05f));
                    }
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    CreateWindow(
                        parent,
                        new Vector3(
                            side * (halfWidth + 0.03f),
                            sill + (side > 0 ? 0.08f : 0f),
                            side * half * 0.12f),
                        new Vector3(0.05f, 0.72f, 0.58f));
                }

                return;
            }

            // The Renaissance house exposes a high masonry base and a more
            // regular, denser window rhythm in the projecting timber floors.
            float upperSill = Mathf.Clamp(
                plot.Height * 0.50f,
                2.05f,
                3.00f);
            float[] regularAcross = { -0.52f, 0f, 0.52f };
            for (int side = -1; side <= 1; side += 2)
            {
                for (int index = 0; index < regularAcross.Length; index++)
                {
                    CreateWindow(
                        parent,
                        new Vector3(
                            halfWidth * regularAcross[index],
                            upperSill,
                            side * (half + 0.03f)),
                        new Vector3(0.68f, 0.82f, 0.05f));
                }

                if (plot.Height > 5f)
                {
                    CreateWindow(
                        parent,
                        new Vector3(
                            0f,
                            plot.Height * 0.66f,
                            side * (half + 0.03f)),
                        new Vector3(0.58f, 0.60f, 0.05f));
                }
            }

            for (int side = -1; side <= 1; side += 2)
            {
                for (int index = -1; index <= 1; index += 2)
                {
                    CreateWindow(
                        parent,
                        new Vector3(
                            side * (halfWidth + 0.03f),
                            upperSill,
                            index * half * 0.24f),
                        new Vector3(0.05f, 0.80f, 0.62f));
                }
            }
        }

        /// <summary>
        /// Three windows in the whole village earn a real light. They point
        /// into the snow immediately in front of the wall, never into the air;
        /// every other pane remains emissive geometry on the same shared
        /// material.
        /// </summary>
        private static void BuildWindowSnowPool(
            Transform parent,
            bool summit,
            float wallFace)
        {
            var lightObject = new GameObject(
                summit ? "Summit Window Snow Pool" : "Window Snow Pool");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = new Vector3(
                0f,
                1.55f,
                wallFace + 0.22f);
            lightObject.transform.localRotation = Quaternion.LookRotation(
                new Vector3(0f, -0.62f, 1f).normalized,
                Vector3.up);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.75f, 0.46f);
            light.intensity = summit
                ? SummitWindowSnowPoolIntensity
                : WindowSnowPoolIntensity;
            light.range = summit ? 8.5f : 7.2f;
            light.spotAngle = summit ? 76f : 70f;
            light.innerSpotAngle = light.spotAngle * 0.42f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.08f;
        }

        /// <summary>
        /// The three close-read traces that make the old mine part of daily
        /// life: cable as a yard gate, rail as a ditch bridge, and the ordinary
        /// stone catch basin below the chapel pipe.
        /// </summary>
        private static void BuildVillageDressing(
            Transform parent,
            AlpineVillagePlan plan,
            VillageAssetProvider kit,
            IDictionary<string, Transform> semanticObjects)
        {
            AlpineVillagePlotDescriptor chapel = FindPlot(
                plan,
                AlpineVillagePlotKind.Chapel);
            Vector3 bowlGround = AlpineVillagePathPlanner
                .GetChapelSourceBowlPosition(plan, chapel);
            var bowl = new GameObject("Chapel Overflow Catch Basin");
            bowl.transform.SetParent(parent, false);
            bowl.transform.SetPositionAndRotation(
                bowlGround,
                Quaternion.LookRotation(chapel.Facing, Vector3.up));
            PlaceKitAssembly(
                bowl.transform,
                kit,
                VillageAssetKind.SourceBowl,
                0,
                new Vector2(1.15f, 0.75f),
                0.55f,
                _ => StoneColor);
            BoxCollider bowlCollider = bowl.AddComponent<BoxCollider>();
            bowlCollider.center = new Vector3(0f, 0.24f, 0f);
            bowlCollider.size = new Vector3(1.02f, 0.48f, 0.62f);
            semanticObjects[
                AlpineVillageDressingPlanner.SourceBowlOwnerStableId] =
                bowl.transform;

            AlpineVillagePlotDescriptor dogHouse = FindClosestHouse(
                plan,
                plan.Lane.Length *
                AlpineVillageDressingPlanner.DogHouseLaneFraction,
                1);
            Vector3 gateGround = AlpineVillageDressingPlanner
                .GetCableGatePosition(plan, dogHouse);
            var gate = new GameObject("Mine Cable Yard Gate");
            gate.transform.SetParent(parent, false);
            gate.transform.SetPositionAndRotation(
                gateGround,
                Quaternion.LookRotation(dogHouse.Facing, Vector3.up));
            PlaceKitAssembly(
                gate.transform,
                kit,
                VillageAssetKind.CableGate,
                0,
                new Vector2(3.2f, 0.42f),
                1.25f,
                role => role == VillageMeshRole.Cable
                    ? RustColor
                    : TimberColor);
            BoxCollider gateCollider = gate.AddComponent<BoxCollider>();
            gateCollider.center = new Vector3(0f, 0.62f, 0f);
            gateCollider.size = new Vector3(3.06f, 1.20f, 0.18f);
            semanticObjects[
                AlpineVillageDressingPlanner.CableGateOwnerStableId] =
                gate.transform;

            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            for (int index = 0; index < paths.Count; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                if (path.StableId != "village-adit-path-b")
                {
                    continue;
                }

                Vector3 bridgeGround = Vector3.Lerp(
                    path.Start,
                    path.End,
                    0.58f);
                bridgeGround.y = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    new Vector2(bridgeGround.x, bridgeGround.z));
                Vector3 bridgeForward = path.End - path.Start;
                bridgeForward.y = 0f;
                bridgeForward.Normalize();
                var bridge = new GameObject("Mine Rail Ditch Bridge");
                bridge.transform.SetParent(parent, false);
                bridge.transform.SetPositionAndRotation(
                    bridgeGround,
                    Quaternion.LookRotation(bridgeForward, Vector3.up));
                PlaceKitAssembly(
                    bridge.transform,
                    kit,
                    VillageAssetKind.RailBridge,
                    0,
                    new Vector2(1.35f, 3.2f),
                    0.16f,
                    role => role == VillageMeshRole.Rails
                        ? RustColor
                        : TimberColor);
                BoxCollider collider = bridge.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.02f, 0f);
                collider.size = new Vector3(1.22f, 0.04f, 3.0f);
                semanticObjects["village-rail-ditch-bridge"] =
                    bridge.transform;
                break;
            }
        }

        private static AlpineVillagePlotDescriptor FindPlot(
            AlpineVillagePlan plan,
            AlpineVillagePlotKind kind)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind == kind)
                {
                    return plan.Plots[index];
                }
            }

            throw new InvalidOperationException(
                $"The village is missing its '{kind}' plot.");
        }

        private static AlpineVillagePlotDescriptor FindClosestHouse(
            AlpineVillagePlan plan,
            float laneDistance,
            int side)
        {
            AlpineVillagePlotDescriptor closest = null;
            float best = float.PositiveInfinity;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind != AlpineVillagePlotKind.House ||
                    plot.Side != side)
                {
                    continue;
                }

                float distance = Mathf.Abs(plot.LaneDistance - laneDistance);
                if (distance >= best)
                {
                    continue;
                }

                closest = plot;
                best = distance;
            }

            return closest ?? throw new InvalidOperationException(
                $"The village needs a house on side {side}.");
        }

        /// <summary>
        /// Where the water comes out of the hill - MVP.
        ///
        /// This stands where the adit used to, and it is deliberately the
        /// smallest honest thing: a stone catch at the head, the wet ground
        /// it keeps around itself, and the beginning of the runnel leaving
        /// it downhill. The detailed source - flow, sound, the chapel's own
        /// outlet answering it - is the next step, so nothing here invents a
        /// mechanism it cannot yet keep.
        ///
        /// The stone is the kit's own `SourceBowl`, the same piece the
        /// chapel's catch basin is made of. One spring, two places it is
        /// visible, one asset.
        /// </summary>
        /// <summary>
        /// The spring's plot: the stone the water comes out from under.
        ///
        /// THE WATER ITSELF IS NO LONGER HERE. It used to be: a wet apron, a
        /// catch and a runnel, the last two drawn as boxes tinted
        /// `SpringWaterColor` and textured as LAYERED STONE, and this file
        /// said so plainly - "flat and cold for now: the MOVING surface, its
        /// sound and the chapel outlet answering it are the next step, and
        /// faking them with a stone sheet would be a thing to unpick rather
        /// than build on". So it was unpicked rather than built on.
        ///
        /// Water is now a plan - <see cref="AlpineVillageBrookPlan"/>, traced
        /// down the real fall line and cut into the terrain sampler - and is
        /// drawn in one place, by <see cref="AlpineVillageBrookBuilder"/>,
        /// against the whole village rather than inside one plot's local
        /// space. A brook that ran `97 m` could not have lived in a `6 x 5 m`
        /// footprint anyway.
        /// </summary>
        private static void BuildSpring(
            Transform parent,
            AlpineVillagePlotDescriptor plot,
            VillageAssetProvider kit)
        {
            // Nothing. The plot is the SITE of the spring, not its contents:
            // the ledge, the catch, the water and the wet ground are all one
            // feature that runs the length of the village, and it is built
            // once, in world space, by `AlpineVillageBrookBuilder`.
            //
            // The stone catch stood here until the water became real, placed
            // at a local offset while the water was placed from the brook
            // plan - and the first capture showed the result plainly: an
            // EMPTY basin with its water lying in the snow beside it. Two
            // owners of one position is how that happens.
        }

        /// <summary>
        /// Garlands across the lane, in every season and for no occasion.
        ///
        /// This is the zone. It is also the one place where a naive
        /// implementation would fall over: a bulb is not a light. Eighty-odd
        /// real point lights would blow URP's additional-light budget on its
        /// own, so the bulbs are EMISSIVE GEOMETRY in one combined mesh per
        /// span. Two authored spans carry an actual lamp; together with three
        /// window spots that keeps the whole village to five realtime lights.
        /// </summary>
        private static void BuildGarlands(
            Transform parent,
            AlpineVillagePlan plan,
            VillageAssetProvider kit,
            IDictionary<string, Transform> semanticObjects)
        {
            var root = new GameObject("Village Garlands");
            root.transform.SetParent(parent, false);

            for (int span = 0; span < GarlandDistanceBeats.Length; span++)
            {
                float distance = GarlandDistanceBeats[span];
                if (distance >= plan.Lane.Length - 4f)
                {
                    break;
                }

                GetGarlandSpan(
                    plan,
                    span,
                    out Vector3 left,
                    out Vector3 right,
                    out bool leftPost,
                    out bool rightPost);
                AlpineVillageLaneSample sample = plan.Lane.Sample(distance);
                if (leftPost)
                {
                    BuildGarlandPost(
                        root.transform,
                        kit,
                        left - Vector3.up * GarlandPostWireAnchorHeight,
                        sample.Forward,
                        span,
                        "L");
                }

                if (rightPost)
                {
                    BuildGarlandPost(
                        root.transform,
                        kit,
                        right - Vector3.up * GarlandPostWireAnchorHeight,
                        sample.Forward,
                        span,
                        "R");
                }

                string stableId = $"village-garland-wire-{span:00}";
                BuildGarlandSpan(
                    root.transform,
                    left,
                    right,
                    span,
                    span == 1 || span == 6,
                    stableId,
                    semanticObjects);
            }
        }

        /// <summary>
        /// The sound plan reads the exact same cord as the renderer. No hum
        /// may hover where a regularly spaced wire used to be.
        /// </summary>
        internal static void GetGarlandSpan(
            AlpineVillagePlan plan,
            int span,
            out Vector3 left,
            out Vector3 right)
        {
            GetGarlandSpan(
                plan,
                span,
                out left,
                out right,
                out _,
                out _);
        }

        private static void GetGarlandSpan(
            AlpineVillagePlan plan,
            int span,
            out Vector3 left,
            out Vector3 right,
            out bool leftPost,
            out bool rightPost)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (span < 0 || span >= GarlandDistanceBeats.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(span));
            }

            AlpineVillageLaneSample sample = plan.Lane.Sample(
                GarlandDistanceBeats[span]);
            left = ResolveGarlandAnchor(plan, sample, -1, out leftPost);
            right = ResolveGarlandAnchor(plan, sample, 1, out rightPost);
        }

        private static Vector3 ResolveGarlandAnchor(
            AlpineVillagePlan plan,
            AlpineVillageLaneSample sample,
            int side,
            out bool usesPost)
        {
            AlpineVillagePlotDescriptor nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind != AlpineVillagePlotKind.House ||
                    plot.Side != side)
                {
                    continue;
                }

                float distance = Mathf.Abs(
                    plot.LaneDistance - sample.Distance);
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearest = plot;
                nearestDistance = distance;
            }

            if (nearest != null && nearestDistance <= GarlandHouseReach)
            {
                float height = Mathf.Clamp(
                    nearest.Height * 0.72f,
                    GarlandPostHeight,
                    GarlandHeight);
                Vector3 houseAnchor =
                    nearest.GroundCenter +
                    nearest.Facing * (nearest.FootprintSize.y * 0.47f) +
                    Vector3.up * height;
                float lateralReach = Mathf.Abs(Vector3.Dot(
                    houseAnchor - sample.Position,
                    sample.Right));
                float maximumHouseReach =
                    sample.Width * 0.5f +
                    GarlandAnchorReach +
                    GarlandHouseAnchorSlack;
                if (lateralReach <= maximumHouseReach)
                {
                    usesPost = false;
                    return houseAnchor;
                }
            }

            usesPost = true;
            float reach = sample.Width * 0.5f + GarlandAnchorReach;
            Vector3 ground = sample.Position + sample.Right * (side * reach);
            ground.y = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(ground.x, ground.z));
            return ground + Vector3.up * GarlandPostWireAnchorHeight;
        }

        private static void BuildGarlandPost(
            Transform parent,
            VillageAssetProvider kit,
            Vector3 ground,
            Vector3 forward,
            int span,
            string side)
        {
            if (kit == null)
            {
                return;
            }

            var post = new GameObject($"Garland Post {span:00} {side}");
            post.transform.SetParent(parent, false);
            post.transform.SetPositionAndRotation(
                ground,
                Quaternion.LookRotation(forward, Vector3.up));
            PlaceKitAssembly(
                post.transform,
                kit,
                VillageAssetKind.GarlandPost,
                0,
                new Vector2(0.38f, 0.38f),
                GarlandPostHeight,
                role => role == VillageMeshRole.Bracket
                    ? RustColor
                    : TimberColor);
        }

        private static void BuildGarlandSpan(
            Transform parent,
            Vector3 left,
            Vector3 right,
            int span,
            bool realLamp,
            string stableId,
            IDictionary<string, Transform> semanticObjects)
        {
            var wire = new List<RuntimeOrientedBox>(GarlandSegments);
            var bulbs = new List<RuntimeOrientedBox>(GarlandSegments);
            Vector3 previous = SampleGarlandPoint(left, right, 0f);
            for (int step = 1; step <= GarlandSegments; step++)
            {
                float amount = step / (float)GarlandSegments;
                Vector3 current = SampleGarlandPoint(left, right, amount);
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
                    RuntimeWorldUvMode.BoxProjected,
                    true);
            MeshRenderer cordRenderer = cord.GetComponent<MeshRenderer>();
            cordRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // Combined-box vertices are already baked into the garland
            // parent's local space, so the mesh object's transform stays at
            // the parent origin. Register a real midpoint transform instead;
            // semantic consumers must never mistake a batching pivot for the
            // physical wire that explains their sound.
            var semanticAnchor = new GameObject(
                $"Garland Semantic Anchor {span:00}");
            semanticAnchor.transform.SetParent(parent, false);
            semanticAnchor.transform.position = SampleGarlandPoint(
                left,
                right,
                0.5f);
            semanticObjects[stableId] = semanticAnchor.transform;

            GameObject lamps = RuntimePrimitiveFactory
                .CreateCombinedOrientedBoxes(
                    $"Garland Bulbs {span:00}",
                    parent,
                    bulbs,
                    GarlandBulbColor,
                    false,
                    1f,
                    RuntimeWorldUvMode.BoxProjected,
                    true);
            MeshRenderer bulbRenderer = lamps.GetComponent<MeshRenderer>();

            // The combined factory has no material overload, so the emissive
            // sheet is swapped in afterwards; the colour property block it
            // already wrote survives the swap.
            bulbRenderer.sharedMaterial = CityNightResources.EmissiveMaterial;
            bulbRenderer.shadowCastingMode = ShadowCastingMode.Off;

            Transform lampTransform = null;
            if (realLamp)
            {
                // Two authored cords carry real lamps. Intensity is set by
                // THROW, not by taste: this hangs about four metres over the
                // lane and is asked for a warm pool a few metres across,
                // which puts it below the City's door bulbs at `64-110` over
                // `7-8 m`.
                var lightObject = new GameObject(
                    $"Garland Lamp {span:00}");
                lightObject.transform.SetParent(parent, false);
                lightObject.transform.position =
                    SampleGarlandPoint(left, right, 0.5f) +
                    Vector3.down * 0.2f;
                lampTransform = lightObject.transform;
                Light lamp = lightObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.79f, 0.52f);
                lamp.intensity = GarlandLampIntensity;
                lamp.range = 11f;
                lamp.shadows = LightShadows.None;
                lamp.renderMode = LightRenderMode.ForcePixel;
                lamp.bounceIntensity = 0.1f;
            }

            // Both combined meshes keep the batching pivot at the garland
            // root. A bounded vertex deformation is what lets the free middle
            // show the gale while both physical anchors remain attached.
            AlpineVillageGarlandWind motion =
                cord.AddComponent<AlpineVillageGarlandWind>();
            motion.Configure(
                cord.GetComponent<MeshFilter>(),
                lamps.GetComponent<MeshFilter>(),
                semanticAnchor.transform,
                lampTransform,
                left,
                right,
                span * 1.6180339f);
        }

        /// <summary>
        /// A point on the sagging cord. Parabolic rather than a true
        /// catenary: at this span the two are a couple of centimetres apart
        /// and one of them is free.
        /// </summary>
        internal static Vector3 SampleGarlandPoint(
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
