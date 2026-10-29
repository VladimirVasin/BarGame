using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Measured village geography inside a fixed tourist-brochure map panel.</summary>
    public static class AlpineVillageMapArtReferenceExport
    {
        public const string SourceFolder = "ArtSource/Maps/AlpineVillage";
        public const string ImagePath = "Assets/Resources/UI/Maps/AlpineVillageMapImage.png";
        private const int ImageWidth = 2048;
        private const int ImageHeight = 1536;

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
        }

        [Serializable] private sealed class Reference
        {
            public int schemaVersion = AlpineVillageMapArtResources.SchemaVersion;
            public int villageSeed;
            public string fingerprint;
            public Rect worldBounds, mapUvRect;
            public int imageWidth = ImageWidth, imageHeight = ImageHeight;
            public string coordinates = "World X right, world Z north/up. Map UV is normalized bottom-left. " +
                "worldBounds maps exactly into mapUvRect; preserve this frame and every surveyed position.";
            public string composerInstructions = "The right column and outer margins are brochure decoration, not geography. " +
                "Render a worn old ski-holiday brochure with faded cream/blue/green/ochre print. " +
                "Use only the supplied Russian place labels. Numbers and survey boundary lines are reference annotations, not final labels. " +
                "Depict the lodge, service shed and tow at their existing surveyed locations as a former intact resort, " +
                "then cross that resort group by hand with red marker. Do not add slopes, buildings, roads, people, " +
                "countries, dates or prices. The avalanche footprint is real current geography, not a new event. " +
                "Seep-line is damp ground, never a second stream. Resort outline is an annotation, never a new physical boundary.";
            public Polygon[] polygons;
            public Stroke[] strokes;
            public Place[] places;
        }

        [MenuItem("Bar Promenade/Map/Export Alpine Village ImageGen Reference")]
        public static void ExportCurrent()
        {
            CityMapArtReferenceExport.MigrateCityMetadataForRegionalLabels();
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(GameSessionState.DefaultCitySeed);
            Reference data = Collect(plan);
            Directory.CreateDirectory(SourceFolder);
            File.WriteAllText(Path.Combine(SourceFolder, "map-reference.json"), JsonUtility.ToJson(data, true));
            var raster = new Raster(data.worldBounds, data.mapUvRect);
            foreach (Polygon polygon in data.polygons)
                if (polygon.role == "ridge" || polygon.role == "avalanche" || polygon.role == "water")
                    raster.Polygon(polygon.points, polygon.role);
            foreach (Stroke stroke in data.strokes) raster.Stroke(stroke.points, stroke.width, stroke.role);
            foreach (Polygon polygon in data.polygons)
                if (polygon.role != "ridge" && polygon.role != "avalanche" && polygon.role != "water")
                    raster.Polygon(polygon.points, polygon.role);
            foreach (Place place in data.places) raster.Marker(place.position, place.referenceNumber);
            raster.Save(Path.Combine(SourceFolder, "map-reference.png"));
            Debug.Log("ALPINE VILLAGE MAP IMAGEGEN REFERENCE OK: " + data.fingerprint + "; " +
                ImageWidth + "x" + ImageHeight + "; source " + Path.GetFullPath(SourceFolder));
        }

        /// <summary>Explicit one-time setup after ImageGen output is copied into Resources.</summary>
        public static void PrepareImportedImage()
        {
            if (!File.Exists(ImagePath))
                throw new FileNotFoundException("Generate the Alpine Village map with built-in ImageGen first.", ImagePath);
            AssetDatabase.ImportAsset(ImagePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ImagePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Alpine Village map art has no texture importer.");
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = ImageWidth;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Standalone", overridden = true, maxTextureSize = ImageWidth,
                format = TextureImporterFormat.RGB24, textureCompression = TextureImporterCompression.Uncompressed });
            importer.SaveAndReimport();
            Debug.Log("ALPINE VILLAGE MAP IMAGEGEN TEXTURE IMPORT OK");
        }

        private static Reference Collect(AlpineVillagePlan plan)
        {
            var polygons = new List<Polygon>();
            var strokes = new List<Stroke>();
            var places = new List<Place>();
            void Poly(string id, string role, Vector2[] points) =>
                polygons.Add(new Polygon { id = id, role = role, points = points });
            void Line(string id, string role, float width, Vector3 a, Vector3 b) =>
                strokes.Add(new Stroke { id = id, role = role, width = width, points = new[] { XZ(a), XZ(b) } });
            void Body(string id, string role, Vector3 center, Vector3 forward, Vector2 size) =>
                Poly(id, role, Corners(center, forward, size));
            void Point(string id, string kind, string key, Vector3 position) => places.Add(new Place {
                id = id, kind = kind, label = LocalizationService.Get(key),
                referenceNumber = places.Count + 1, position = position });

            MountainRoadTerminalRect pad = plan.Station.PadArea;
            Poly("station-pad", "station", new[] {
                XZ(pad.GetCorner(0)), XZ(pad.GetCorner(1)), XZ(pad.GetCorner(3)), XZ(pad.GetCorner(2)) });
            Point("alpine-village:station", "Cableway", "map.alpine_village.station", pad.Center);
            for (int i = 1; i < plan.Lane.Samples.Count; i++)
            {
                AlpineVillageLaneSample a = plan.Lane.Samples[i - 1], b = plan.Lane.Samples[i];
                Line("lane:" + Ordinal(i), "lane", (a.Width + b.Width) * .5f, a.Position, b.Position);
            }
            foreach (AlpineVillagePathDescriptor path in AlpineVillagePathPlanner.Create(plan))
                Line("path:" + path.StableId, path.Kind == AlpineVillagePathKind.AbandonedRoad ? "old-road" : "path",
                    path.SurfaceHalfWidth * 2f, path.Start, path.End);
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
            {
                if (plot.Kind != AlpineVillagePlotKind.Spring)
                    Body(plot.StableId, plot.Kind == AlpineVillagePlotKind.MothersHouse ? "mother-building" :
                        plot.Kind == AlpineVillagePlotKind.Chapel ? "chapel-building" : "inhabited-building",
                        plot.GroundCenter, plot.Facing, plot.FootprintSize);
                string key = plot.Kind == AlpineVillagePlotKind.MothersHouse ? "map.alpine_village.mothers_house" :
                    plot.Kind == AlpineVillagePlotKind.Chapel ? "map.alpine_village.chapel" :
                    plot.Kind == AlpineVillagePlotKind.Spring ? "map.alpine_village.spring" : null;
                if (key != null) Point("alpine-village:" + plot.StableId, plot.Kind.ToString(), key, plot.DoorDockPosition);
            }

            AlpineVillageExpansionPlan expansion = plan.Expansion;
            foreach (AlpineVillageAbandonedPlot plot in expansion.Abandonment.Plots)
                Body("abandoned:" + plot.Id, plot.IsBuilding ? "abandoned-building" : "foundation",
                    plot.GroundCenter, plot.Forward, plot.Size);
            Body("ski-lodge", "resort-building", expansion.LodgeCenter, expansion.LodgeForward, expansion.LodgeSize);
            Body("service-shed", "resort-building", expansion.ServiceShedCenter, expansion.LodgeForward, expansion.ServiceShedSize);
            Body("trade-warehouse", "abandoned-building", expansion.WarehouseCenter, expansion.LodgeForward, new Vector2(10f, 14f));
            Body("lift-base", "lift-base", expansion.LiftBasePosition, expansion.LodgeForward, new Vector2(.7f, .7f));
            Line("old-ski-tow", "ski-tow", .7f, expansion.LiftBasePosition, expansion.LiftTopPosition);
            var avalanche = new Vector2[AlpineVillageAvalanchePlan.Footprint.Count];
            for (int i = 0; i < avalanche.Length; i++)
                avalanche[i] = XZ(expansion.ToWorld(AlpineVillageAvalanchePlan.Origin + AlpineVillageAvalanchePlan.Footprint[i]));
            Poly("old-avalanche", "avalanche", avalanche);
            Vector2[] resortBounds = ResortAnnotationBounds(expansion);
            Poly("resort-annotation", "resort-annotation", resortBounds);
            Point("alpine-village:ski-resort", "Village", "map.alpine_village.ski_resort", expansion.LodgeApproach);

            for (int i = 0; i < plan.Ridges.Count; i++)
            {
                AlpineVillageRidgeDescriptor ridge = plan.Ridges[i];
                Body("ridge:" + Ordinal(i), "ridge", ridge.Center, ridge.Forward,
                    new Vector2(ridge.Thickness, ridge.Length));
            }
            if (plan.Brook != null)
            {
                Body("spring-catch", "water", plan.Brook.BowlCenter, plan.Brook.BowlFacing, plan.Brook.CatchOuterSize);
                for (int i = 1; i < plan.Brook.Samples.Count; i++)
                {
                    AlpineVillageBrookSample a = plan.Brook.Samples[i - 1], b = plan.Brook.Samples[i];
                    Poly("brook:" + Ordinal(i), "water", new[] {
                        XZ(a.Position - a.Right * a.HalfWidth), XZ(a.Position + a.Right * a.HalfWidth),
                        XZ(b.Position + b.Right * b.HalfWidth), XZ(b.Position - b.Right * b.HalfWidth) });
                }
                for (int i = 1; i < plan.Brook.SeepLine.Count; i++)
                {
                    AlpineVillageBrookSample a = plan.Brook.SeepLine[i - 1], b = plan.Brook.SeepLine[i];
                    Line("seep-line:" + Ordinal(i), "wet-ground", (a.Width + b.Width) * .5f, a.Position, b.Position);
                }
            }
            return new Reference {
                villageSeed = plan.Seed, fingerprint = AlpineVillageMapArtResources.CreateFingerprint(plan),
                worldBounds = AlpineVillageMapArtResources.GetWorldBounds(plan), mapUvRect = AlpineVillageMapArtResources.MapUvRect,
                polygons = polygons.ToArray(), strokes = strokes.ToArray(), places = places.ToArray() };
        }

        private static Vector2[] ResortAnnotationBounds(AlpineVillageExpansionPlan expansion)
        {
            Vector2 minimum = XZ(expansion.LiftBasePosition), maximum = minimum;
            void Include(Vector2 point) { minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point); }
            foreach (Vector2 point in Corners(expansion.LodgeCenter, expansion.LodgeForward, expansion.LodgeSize)) Include(point);
            foreach (Vector2 point in Corners(expansion.ServiceShedCenter, expansion.LodgeForward, expansion.ServiceShedSize)) Include(point);
            Include(XZ(expansion.LiftTopPosition));
            minimum -= Vector2.one * 2f; maximum += Vector2.one * 2f;
            return new[] { minimum, new Vector2(maximum.x, minimum.y), maximum, new Vector2(minimum.x, maximum.y) };
        }

        private static Vector2[] Corners(Vector3 center, Vector3 forward, Vector2 size)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized * (size.x * .5f);
            Vector3 along = forward.normalized * (size.y * .5f);
            return new[] { XZ(center - right - along), XZ(center + right - along),
                XZ(center + right + along), XZ(center - right + along) };
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static string Ordinal(int index) => index.ToString("D3", CultureInfo.InvariantCulture);

        private sealed class Raster
        {
            private readonly Rect world, panel;
            private readonly Color32[] pixels = new Color32[ImageWidth * ImageHeight];
            private static readonly Color32 Paper = new Color32(235, 224, 192, 255);
            private static readonly Color32 Ink = new Color32(56, 65, 61, 255);

            public Raster(Rect world, Rect mapUvRect)
            {
                this.world = world;
                panel = new Rect(mapUvRect.x * ImageWidth, mapUvRect.y * ImageHeight,
                    mapUvRect.width * ImageWidth, mapUvRect.height * ImageHeight);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Paper;
                // These separators show the image editor the reserved brochure panels.
                Segment(new Vector2(panel.xMin, panel.yMin), new Vector2(panel.xMax, panel.yMin), 1f, Ink);
                Segment(new Vector2(panel.xMax, panel.yMin), new Vector2(panel.xMax, panel.yMax), 1f, Ink);
                Segment(new Vector2(panel.xMax, panel.yMax), new Vector2(panel.xMin, panel.yMax), 1f, Ink);
                Segment(new Vector2(panel.xMin, panel.yMax), new Vector2(panel.xMin, panel.yMin), 1f, Ink);
            }

            private Vector2 Pixel(Vector2 point) => new Vector2(
                panel.x + (point.x - world.xMin) / world.width * panel.width,
                panel.y + (point.y - world.yMin) / world.height * panel.height);

            private void Put(int x, int y, Color32 color)
            {
                if (x >= 0 && y >= 0 && x < ImageWidth && y < ImageHeight) pixels[y * ImageWidth + x] = color;
            }

            public void Polygon(Vector2[] source, string role)
            {
                if (source.Length < 3) return;
                var points = new Vector2[source.Length];
                Vector2 minimum = new Vector2(ImageWidth, ImageHeight), maximum = Vector2.zero;
                for (int i = 0; i < points.Length; i++)
                { points[i] = Pixel(source[i]); minimum = Vector2.Min(minimum, points[i]); maximum = Vector2.Max(maximum, points[i]); }
                if (role != "resort-annotation")
                {
                    Color32 fill = Fill(role);
                    for (int y = Mathf.Max(0, Mathf.FloorToInt(minimum.y)); y <= Mathf.Min(ImageHeight - 1, Mathf.CeilToInt(maximum.y)); y++)
                    for (int x = Mathf.Max(0, Mathf.FloorToInt(minimum.x)); x <= Mathf.Min(ImageWidth - 1, Mathf.CeilToInt(maximum.x)); x++)
                    {
                        bool inside = false;
                        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                            if ((points[i].y > y + .5f) != (points[j].y > y + .5f) &&
                                x + .5f < (points[j].x - points[i].x) * (y + .5f - points[i].y) /
                                (points[j].y - points[i].y) + points[i].x) inside = !inside;
                        if (inside) Put(x, y, fill);
                    }
                }
                if (role != "water" && role != "ridge")
                    for (int i = 0; i < points.Length; i++)
                        Segment(points[i], points[(i + 1) % points.Length], role == "resort-annotation" ? 2f : 1.3f, Ink);
            }

            public void Stroke(Vector2[] source, float metres, string role)
            {
                float width = Mathf.Max(1.4f, metres * panel.width / world.width);
                for (int i = 1; i < source.Length; i++)
                {
                    Vector2 a = Pixel(source[i - 1]), b = Pixel(source[i]);
                    if (role == "lane" || role == "old-road")
                    {
                        Segment(a, b, width + 2f, Ink);
                        Segment(a, b, width, new Color32(219, 199, 157, 255));
                    }
                    else Segment(a, b, width, role == "wet-ground" ? new Color32(147, 166, 138, 255) : Ink);
                }
            }

            public void Marker(Vector3 position, int number)
            {
                Vector2 at = Pixel(XZ(position));
                Segment(at, at, 11f, Paper);
                Segment(at, at, 4f, Ink);
                string digits = number.ToString(CultureInfo.InvariantCulture);
                const string bits = "111101101101111010110010010111111001111100111111001111001111101101111001001111100111001111111100111101111111001001001001111101111101111111101111001111";
                for (int digit = 0; digit < digits.Length; digit++)
                for (int row = 0; row < 5; row++)
                for (int column = 0; column < 3; column++)
                {
                    int index = (digits[digit] - '0') * 15 + row * 3 + column;
                    if (bits[index] != '1') continue;
                    for (int yy = 0; yy < 3; yy++)
                    for (int xx = 0; xx < 3; xx++)
                        Put(Mathf.RoundToInt(at.x) + 9 + digit * 12 + column * 3 + xx,
                            Mathf.RoundToInt(at.y) + 7 - row * 3 - yy, Ink);
                }
            }

            private void Segment(Vector2 a, Vector2 b, float width, Color32 color)
            {
                float radius = width * .5f;
                Vector2 delta = b - a;
                float squaredLength = delta.sqrMagnitude;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius));
                int x1 = Mathf.Min(ImageWidth - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius));
                int y1 = Mathf.Min(ImageHeight - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 point = new Vector2(x + .5f, y + .5f);
                    float amount = squaredLength < .00001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, delta) / squaredLength);
                    if ((point - a - delta * amount).sqrMagnitude <= radius * radius) Put(x, y, color);
                }
            }

            private static Color32 Fill(string role)
            {
                switch (role)
                {
                    case "water": return new Color32(122, 161, 169, 255);
                    case "ridge": return new Color32(207, 205, 179, 255);
                    case "avalanche": return new Color32(220, 220, 206, 255);
                    case "station": return new Color32(186, 184, 156, 255);
                    case "resort-building": return new Color32(173, 153, 111, 255);
                    case "mother-building": return new Color32(186, 151, 103, 255);
                    case "chapel-building": return new Color32(174, 168, 136, 255);
                    case "inhabited-building": return new Color32(163, 151, 117, 255);
                    case "foundation": return new Color32(207, 195, 168, 255);
                    default: return new Color32(186, 183, 157, 255);
                }
            }

            public void Save(string path)
            {
                var texture = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGBA32, false);
                try
                {
                    texture.SetPixels32(pixels);
                    texture.Apply(false, false);
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
        }
    }
}
