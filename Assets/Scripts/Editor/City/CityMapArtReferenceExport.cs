using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>North-up source geometry for ImageGen. Art changes ink, never the surveyed city.</summary>
    public static class CityMapArtReferenceExport
    {
        public const string SourceFolder = "ArtSource/Maps/City";
        public const string ImagePath = "Assets/Resources/UI/Maps/CityMapImage.png";
        private const int MaximumEdge = 2048;

        public static void FocusGameViewForCapture()
        {
            if (!EditorApplication.ExecuteMenuItem("Window/General/Game"))
                throw new InvalidOperationException("The native map capture requires a visible Game view.");
        }

        /// <summary>Only migrates the dependency-hash format when the surveyed City itself is identical.</summary>
        public static void MigrateCityMetadataForRegionalLabels()
        {
            string referencePath = Path.Combine(SourceFolder, "map-reference.json");
            string metadataPath = Path.ChangeExtension(ImagePath, ".json");
            if (!File.Exists(referencePath) || !File.Exists(metadataPath))
                throw new FileNotFoundException("The original City survey and artwork metadata are required for migration.");
            Reference original = JsonUtility.FromJson<Reference>(File.ReadAllText(referencePath));
            var host = new GameObject("Temporary City Map Hash Migration") { hideFlags = HideFlags.HideAndDontSave };
            var playerHost = new GameObject("Temporary City Map Hash Migration Player") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                CityLayout layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
                var player = new PlayerRuntime(playerHost, playerHost.AddComponent<PlayerMotor>(),
                    playerHost.AddComponent<PlayerInteractor>(), null);
                CityMapController controller = host.AddComponent<CityMapController>();
                controller.Initialize(layout, player, null, null, CityBusPlanner.Create(layout),
                    CityMountainBoundaryPlanner.Create(layout), CitySeacoastPlanner.Create(layout));
                Reference current = Collect(controller);
                string regionalFingerprint = current.fingerprint;
                // Do not re-tag changed geography as old art. The original
                // generated PNG, prompt and survey provenance remain untouched.
                current.fingerprint = original.fingerprint;
                if (JsonUtility.ToJson(current) != JsonUtility.ToJson(original))
                    throw new InvalidOperationException("City geography or surveyed labels changed; redraw City with ImageGen before migrating metadata.");
                CityMapArtResources.Metadata data = JsonUtility.FromJson<CityMapArtResources.Metadata>(File.ReadAllText(metadataPath));
                data.schemaVersion = CityMapArtResources.SchemaVersion;
                data.fingerprint = regionalFingerprint;
                File.WriteAllText(metadataPath, JsonUtility.ToJson(data, true) + "\n");
                AssetDatabase.ImportAsset(metadataPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("CITY MAP REGIONAL HASH MIGRATION OK: identical surveyed geography and labels; original ImageGen pixels and provenance retained.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerHost);
            }
        }

        [Serializable] private sealed class Polygon
        {
            public string id, role;
            public Vector2[] points;
        }
        [Serializable] private sealed class Stroke
        {
            public string id, role;
            public float width;
            public Vector2[] points;
        }
        [Serializable] private sealed class Place
        {
            public string id, kind, label;
            public int referenceNumber;
            public Vector3 position;
            public bool hasFootprint;
            public Rect footprint;
        }
        [Serializable] private sealed class Reference
        {
            public int schemaVersion = 1;
            public string blueprintId, fingerprint;
            public int citySeed;
            public Rect worldBounds;
            public int imageWidth, imageHeight;
            public string coordinates = "World X right, world Z north/up. Image covers worldBounds exactly; no added margins.";
            public Polygon[] polygons;
            public Stroke[] strokes;
            public Place[] places;
        }

        [MenuItem("Bar Promenade/Map/Export ImageGen Reference")]
        public static void ExportCurrent()
        {
            var host = new GameObject("Temporary Map Art Survey") { hideFlags = HideFlags.HideAndDontSave };
            var playerHost = new GameObject("Temporary Map Art Survey Player") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                CityLayout layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
                CityMountainBoundaryPlan mountain = CityMountainBoundaryPlanner.Create(layout);
                CitySeacoastPlan coast = CitySeacoastPlanner.Create(layout);
                var player = new PlayerRuntime(playerHost, playerHost.AddComponent<PlayerMotor>(),
                    playerHost.AddComponent<PlayerInteractor>(), null);
                CityMapController controller = host.AddComponent<CityMapController>();
                controller.Initialize(layout, player, null, null, CityBusPlanner.Create(layout), mountain, coast);
                Reference data = Collect(controller);
                Directory.CreateDirectory(SourceFolder);
                File.WriteAllText(Path.Combine(SourceFolder, "map-reference.json"), JsonUtility.ToJson(data, true));
                var raster = new Raster(data.worldBounds, data.imageWidth, data.imageHeight);
                foreach (Polygon polygon in data.polygons) raster.Polygon(polygon.points, polygon.role);
                foreach (Stroke stroke in data.strokes) raster.Stroke(stroke.points, stroke.width, stroke.role);
                foreach (Place place in data.places) raster.Marker(place.position, place.referenceNumber);
                raster.Save(Path.Combine(SourceFolder, "map-reference.png"));
                Debug.Log("CITY MAP IMAGEGEN REFERENCE OK: " + data.fingerprint + "; " +
                    data.imageWidth + "x" + data.imageHeight + "; source " + Path.GetFullPath(SourceFolder));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerHost);
            }
        }

        /// <summary>Called once after the generated PNG is copied into the project. No global importer.</summary>
        public static void PrepareImportedImage()
        {
            if (!File.Exists(ImagePath)) throw new FileNotFoundException("Generate the City map with built-in ImageGen first.", ImagePath);
            AssetDatabase.ImportAsset(ImagePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ImagePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("City map art has no texture importer.");
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false; importer.streamingMipmaps = false; importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = MaximumEdge;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Standalone", overridden = true, maxTextureSize = MaximumEdge,
                format = TextureImporterFormat.RGB24, textureCompression = TextureImporterCompression.Uncompressed });
            importer.SaveAndReimport();
            Debug.Log("CITY MAP IMAGEGEN TEXTURE IMPORT OK");
        }

        private static Reference Collect(CityMapController controller)
        {
            CityLayout layout = controller.Layout;
            Rect bounds = controller.DisplayWorldXZBounds;
            int width = bounds.width >= bounds.height ? MaximumEdge : Mathf.CeilToInt(MaximumEdge * bounds.width / bounds.height / 4f) * 4;
            int height = bounds.height >= bounds.width ? MaximumEdge : Mathf.CeilToInt(MaximumEdge * bounds.height / bounds.width / 4f) * 4;
            var polygons = new List<Polygon>();
            var strokes = new List<Stroke>();
            void Poly(string id, string role, IList<Vector2> source)
            {
                var points = new Vector2[source.Count];
                for (int i = 0; i < points.Length; i++) points[i] = source[i];
                polygons.Add(new Polygon { id = id, role = role, points = points });
            }
            void Box(string id, string role, Rect rect) => Poly(id, role, new[] {
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) });
            void Line(string id, string role, float thickness, IList<Vector2> source)
            {
                var points = new Vector2[source.Count];
                for (int i = 0; i < points.Length; i++) points[i] = source[i];
                strokes.Add(new Stroke { id = id, role = role, width = thickness, points = points });
            }
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                string role = surface.IsWater ? "water" : surface.Kind == CitySurfaceKind.ParkGround ? "park" :
                    surface.Kind == CitySurfaceKind.Beach ? "beach" : "ground";
                if (surface.Kind == CitySurfaceKind.BuildableGround)
                {
                    int piece = 0;
                    foreach (Vector2[] polygon in layout.RoadGeometry.GetGroundPolygons(surface.Cell))
                        Poly("surface:" + surface.Cell + ":" + piece++, role, polygon);
                }
                else Box("surface:" + surface.AreaId + ":" + surface.Cell, role, surface.WorldBounds);
            }
            CityMountainBoundaryPlan mountain = controller.MountainBoundaryPlan;
            foreach (CityMountainRidgeDescriptor ridge in mountain.Ridges)
            {
                for (int i = 1; i < ridge.Stations.Count; i++)
                {
                    CityMountainRidgeStation a = ridge.Stations[i - 1], b = ridge.Stations[i];
                    Poly(ridge.StableId + ":" + i, "mountain", new[] { a.WorldXZ, b.WorldXZ, XZ(b.OuterFoot), XZ(a.OuterFoot) });
                    Line(ridge.StableId + ":toe:" + i, "mountain-ink", .55f, new[] { a.WorldXZ, b.WorldXZ });
                    Line(ridge.StableId + ":hatch:" + i, "mountain-ink", .3f, new[] { b.WorldXZ, XZ(b.OuterFoot) });
                }
            }
            if (mountain.HasRiverCave)
            {
                Box("river-cave-water", "water", mountain.RiverCave.WaterApproachBounds);
                Box("river-cave-mouth", "water", mountain.RiverCave.MouthBounds);
            }
            if (mountain.HasTunnel)
            {
                Vector3 a = mountain.Tunnel.PortalGroundCenter;
                Vector3 b = a + mountain.Tunnel.Axis * mountain.Tunnel.MapDisplayDepth;
                Line("mountain-tunnel", "tunnel", 5f, new[] { XZ(a), XZ(b) });
            }
            for (int i = 0; i < layout.River.Promenades.Count; i++)
                Box("river-promenade:" + i, "promenade", layout.River.Promenades[i].Bounds);
            for (int i = 0; i < layout.River.Segments.Count; i++)
                Box("river-water:" + i, "water", layout.River.Segments[i].WaterBounds);
            int massIndex = 0;
            foreach (BuildingLot lot in layout.BuildingMasses)
            {
                if (!lot.HasBuilding) continue;
                string role = lot.IsPlayerHome ? "home-building" : lot.IsBar ? "bar-building" :
                    lot.IsSupermarket ? "shop-building" : "building";
                foreach (Vector2[] polygon in lot.CreateCollisionPolygons())
                    Poly("building:" + lot.Cell.x + ":" + lot.Cell.y + ":" + massIndex++, role, polygon);
            }
            CityLaundryPlan laundry = CityLaundryPlan.ForLayout(layout);
            if (laundry.IsEnabled)
                for (int i = 0; i < laundry.BodyFootprints.Count; i++) Box("laundry:" + i, "building", laundry.BodyFootprints[i]);
            foreach (CityMapPointOfInterest place in controller.PointsOfInterest)
                if (place.HasFootprint) Box("place:" + place.StableId, "public-ground", place.Footprint);
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                CityRoadPath road = layout.RoadGeometry.Get(edge);
                var vertices = new List<Vector2>(road.Vertices);
                Line("road:" + edge.A + ":" + edge.B, layout.GetPathKind(edge) == CityPathKind.Street ? "road" : "park-path",
                    layout.GetTravelWidth(edge), vertices);
            }
            int court = 0;
            foreach (CityRoadPath path in layout.CourtyardPaths)
                Line("court-path:" + court++, "court-path", 1.5f, new List<Vector2>(path.Vertices));
            foreach (CityRiverBridgeDescriptor bridge in layout.River.Bridges)
                Box("bridge:" + bridge.Definition.Id, "bridge", bridge.DeckBounds);
            CityPortAccessPlan access = CityPortAccessPlan.ForLayout(layout);
            if (access != null)
            {
                var points = new List<Vector2>();
                foreach (CityPortAccessPlan.RoadSample sample in access.RoadSamples) points.Add(XZ(access.World(sample.center)));
                Line("port-access", "road", access.CarriagewayWidth, points);
            }
            CityEastExitPlan east = CityEastExitPlanner.Create(layout);
            if (east.IsEnabled)
            {
                Line("east-exit", "road", layout.RoadWidth,
                    new[] { XZ(east.ApproachStart), XZ(east.CheckpointPosition), XZ(east.RoadEnd) });
                Box("east-booth", "building", east.BoothBounds);
            }
            CitySeacoastPlan coast = controller.SeacoastPlan;
            if (coast != null)
            {
                if (coast.Port != null)
                {
                    Box("port-quay", "promenade", coast.Port.QuayBounds);
                    Box("port-breakwater", "promenade", coast.Port.BreakwaterBounds);
                    Box("port-head", "promenade", coast.Port.BreakwaterHeadBounds);
                    Box("port-warehouse", "building", coast.Port.WarehouseBounds);
                }
                foreach (CitySeacoastPartDescriptor part in coast.Parts)
                {
                    if (part.Kind != CitySeacoastPartKind.MolDeck && part.Kind != CitySeacoastPartKind.PierDeck &&
                        part.Kind != CitySeacoastPartKind.FootbridgeDeck && part.Kind != CitySeacoastPartKind.Hut &&
                        part.Kind != CitySeacoastPartKind.Barge) continue;
                    Vector3 a = part.Rotation * new Vector3(-part.Size.x, 0, -part.Size.z) * .5f;
                    Vector3 b = part.Rotation * new Vector3(part.Size.x, 0, -part.Size.z) * .5f;
                    Vector3 c = part.Rotation * new Vector3(part.Size.x, 0, part.Size.z) * .5f;
                    Vector3 d = part.Rotation * new Vector3(-part.Size.x, 0, part.Size.z) * .5f;
                    Poly(part.StableId, part.Kind == CitySeacoastPartKind.Hut ? "building" : "promenade",
                        new[] { XZ(part.Center + a), XZ(part.Center + b), XZ(part.Center + c), XZ(part.Center + d) });
                }
            }
            var places = new List<Place>();
            for (int i = 0; i < controller.PointsOfInterest.Count; i++)
            {
                CityMapPointOfInterest p = controller.PointsOfInterest[i];
                places.Add(new Place { id = p.StableId, kind = p.Kind.ToString(), label = controller.GetPointOfInterestLabel(i),
                    referenceNumber = i + 1, position = p.WorldPosition, hasFootprint = p.HasFootprint, footprint = p.Footprint });
            }
            return new Reference { blueprintId = layout.BlueprintId, citySeed = layout.Seed,
                fingerprint = CityMapArtResources.CreateFingerprint(controller), worldBounds = bounds,
                imageWidth = width, imageHeight = height, polygons = polygons.ToArray(), strokes = strokes.ToArray(), places = places.ToArray() };
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        private sealed class Raster
        {
            private readonly Rect world;
            private readonly int width, height;
            private readonly Color32[] pixels;
            private static readonly Color32 Ink = new Color32(180, 178, 158, 255);
            private static readonly Color32 Void = new Color32(19, 23, 22, 255);
            public Raster(Rect world, int width, int height)
            {
                this.world = world; this.width = width; this.height = height;
                pixels = new Color32[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Void;
            }
            private Vector2 Pixel(Vector2 point) => new Vector2((point.x - world.xMin) / world.width * (width - 1),
                (point.y - world.yMin) / world.height * (height - 1));
            private void Put(int x, int y, Color32 color) { if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = color; }
            public void Polygon(Vector2[] source, string role)
            {
                if (source.Length < 3) return;
                var p = new Vector2[source.Length];
                Vector2 low = new Vector2(width, height), high = Vector2.zero;
                for (int i = 0; i < p.Length; i++) { p[i] = Pixel(source[i]); low = Vector2.Min(low, p[i]); high = Vector2.Max(high, p[i]); }
                Color32 color = Fill(role);
                for (int y = Mathf.Max(0, Mathf.FloorToInt(low.y)); y <= Mathf.Min(height - 1, Mathf.CeilToInt(high.y)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(low.x)); x <= Mathf.Min(width - 1, Mathf.CeilToInt(high.x)); x++)
                {
                    bool inside = false;
                    for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                        if ((p[i].y > y + .5f) != (p[j].y > y + .5f) &&
                            x + .5f < (p[j].x - p[i].x) * (y + .5f - p[i].y) / (p[j].y - p[i].y) + p[i].x) inside = !inside;
                    if (inside) Put(x, y, color);
                }
                if (role.EndsWith("building", StringComparison.Ordinal) || role == "bridge" || role == "public-ground")
                    for (int i = 0; i < p.Length; i++) Segment(p[i], p[(i + 1) % p.Length], 1.4f, Ink);
            }
            public void Stroke(Vector2[] source, float metres, string role)
            {
                float size = metres * width / world.width;
                for (int i = 1; i < source.Length; i++)
                {
                    Vector2 a = Pixel(source[i - 1]), b = Pixel(source[i]);
                    if (role == "road")
                    {
                        Segment(a, b, Mathf.Max(2, size + 1.5f), Ink);
                        Segment(a, b, Mathf.Max(1, size - 1.5f), new Color32(85, 88, 80, 255));
                    }
                    else Segment(a, b, Mathf.Max(1, size), role == "tunnel" ? Void : Ink);
                }
            }
            private void Segment(Vector2 a, Vector2 b, float size, Color32 color)
            {
                float radius = size * .5f; Vector2 delta = b - a;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius));
                int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius));
                int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    Vector2 point = new Vector2(x + .5f, y + .5f);
                    float t = delta.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                    if ((point - a - delta * t).sqrMagnitude <= radius * radius) Put(x, y, color);
                }
            }
            public void Marker(Vector3 position, int number)
            {
                Vector2 at = Pixel(XZ(position));
                Segment(at, at, 10, new Color32(231, 218, 180, 255));
                Segment(at, at, 4, Void);
                // Reference numbers are geometry annotations, not in-fiction labels.
                string digits = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                const string bits = "111101101101111010110010010111111001111100111111001111001111101101111001001111100111001111111100111101111111001001001001111101111101111111101111001111";
                for (int digit = 0; digit < digits.Length; digit++)
                for (int row = 0; row < 5; row++) for (int col = 0; col < 3; col++)
                {
                    int index = (digits[digit] - '0') * 15 + row * 3 + col;
                    if (index >= bits.Length || bits[index] != '1') continue;
                    for (int yy = 0; yy < 2; yy++) for (int xx = 0; xx < 2; xx++)
                        Put(Mathf.RoundToInt(at.x) + 8 + digit * 8 + col * 2 + xx,
                            Mathf.RoundToInt(at.y) + 5 - row * 2 - yy, Ink);
                }
            }
            private static Color32 Fill(string role)
            {
                switch (role)
                {
                    case "water": return new Color32(25, 36, 37, 255);
                    case "park": return new Color32(45, 61, 47, 255);
                    case "beach": return new Color32(79, 76, 61, 255);
                    case "mountain": return new Color32(35, 39, 36, 255);
                    case "promenade": return new Color32(92, 95, 85, 255);
                    case "bridge": return new Color32(133, 128, 107, 255);
                    case "public-ground": return new Color32(67, 76, 67, 255);
                    case "building": return new Color32(91, 96, 88, 255);
                    case "home-building": return new Color32(165, 152, 112, 255);
                    case "bar-building": return new Color32(149, 125, 98, 255);
                    case "shop-building": return new Color32(144, 151, 128, 255);
                    default: return new Color32(54, 60, 53, 255);
                }
            }
            public void Save(string path)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try { texture.SetPixels32(pixels); texture.Apply(false, false); File.WriteAllBytes(path, texture.EncodeToPNG()); }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
        }
    }
}
