using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The City-wide audit camera.
    ///
    /// Three passes over one scene load, every one of them an instrument
    /// rather than a test: a top-down hole map of the whole city rendered
    /// against a magenta clear colour so that every place the ground is
    /// missing reads as a magenta pixel, an eye-height station grid on the
    /// map's own teleport lattice with four headings per square plus the
    /// named places a human would walk to, and evidence frames for every
    /// finding an EditMode probe wrote to
    /// <c>TestResults/CityAudit/findings.ndjson</c> - two eye-height
    /// frames, a top view with the walkable mask drawn over the ground, and
    /// for a z-fight suspect a flicker pair rendered in the same frame two
    /// millimetres apart with the differing pixels painted red.
    ///
    /// Nothing is asserted per frame. Blank frames are logged and indexed,
    /// and one assertion at the very end asks that at least half of the
    /// lattice frames saw something. Every number in a file name or an
    /// NDJSON line is written in the invariant culture, because the host
    /// runs ru-RU and a decimal comma in a JSON file is a parse error in
    /// every other clone.
    ///
    /// Frames land in <c>Captures/CityAudit/{topdown,stations,findings}/</c>
    /// beside NDJSON index files that map every file back to the world.
    /// </summary>
    public sealed partial class AreaCaptureFixture
    {
        [Flags]
        private enum AuditPass
        {
            None = 0,
            TopDown = 1,
            Stations = 2,
            Findings = 4,
            All = TopDown | Stations | Findings
        }

        private const string AuditLogPrefix = "[CityAudit] ";

        /// <summary>
        /// The root object of the east exit, matched by name only: its
        /// builder is in-progress work in this checkout and may vanish, so
        /// this fixture must never reference its type.
        /// </summary>
        private const string AuditEastExitRootName = "Eastern Mainland Exit";

        private const float AuditStationFieldOfView = 75f;
        private const float AuditNamedFieldOfView = 60f;
        private const float AuditFindingFieldOfView = 60f;
        private const int AuditTilePixels = 2048;
        private const float AuditTileMetres = 240f;
        private const float AuditTileOverlap = 8f;
        private const float AuditTileCameraLift = 150f;
        private const int AuditClassCell = 4;
        private const float AuditMinimumClusterArea = 0.25f;
        private const int AuditMaximumClustersPerClass = 200;
        private const int AuditFlickerThreshold = 40;
        private const float AuditFlickerStep = 0.002f;
        private const int AuditJpegQuality = 85;

        private static readonly Regex AuditHasYPattern =
            new Regex("\"y\"\\s*:", RegexOptions.Compiled);

        private static readonly Regex AuditMetricPattern = new Regex(
            "\"metrics\"\\s*:\\s*\\{\\s*\"[^\"]*\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)",
            RegexOptions.Compiled);

        [UnityTest]
        [Timeout(14400000)] // the test framework's default 3 min ends a 4 000-frame pass mid-way
        [Explicit("City audit: all passes on one scene load. CITY_AUDIT_PASSES=topdown,stations,findings")]
        public IEnumerator CityAudit() => RunCityAudit(AuditPassesFromEnvironment());

        [UnityTest]
        [Timeout(14400000)] // the test framework's default 3 min ends a 4 000-frame pass mid-way
        [Explicit("City audit: top-down tiles only.")]
        public IEnumerator CityAuditTopDown() => RunCityAudit(AuditPass.TopDown);

        [UnityTest]
        [Timeout(14400000)] // the test framework's default 3 min ends a 4 000-frame pass mid-way
        [Explicit("City audit: station grid + named places.")]
        public IEnumerator CityAuditStations() => RunCityAudit(AuditPass.Stations);

        [UnityTest]
        [Timeout(14400000)] // the test framework's default 3 min ends a 4 000-frame pass mid-way
        [Explicit("City audit: evidence frames for findings.")]
        public IEnumerator CityAuditFindings() => RunCityAudit(AuditPass.Findings);

        // ------------------------------------------------------------
        // Orchestration
        // ------------------------------------------------------------

        private IEnumerator RunCityAudit(AuditPass passes)
        {
            GameSessionState.BeginNewGame();
            // Noon BEFORE the scene builds: the exterior lighting is applied
            // while the world composes, so the first frame is already lit.
            GameSessionState.TryStartGameTimeAt(12 * 60);
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.City,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(load.isDone, Is.True, "City did not load.");
            CityGameRoot city = null;
            deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (city != null && city.IsInitialized && Camera.main != null)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                city != null && city.IsInitialized,
                Is.True,
                "City never initialized.");
            Assert.That(Camera.main, Is.Not.Null, "City has no main camera.");
            for (int frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }

            string root = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                "CityAudit");
            var report = new AuditReport(root);
            Debug.Log(
                AuditLogPrefix +
                $"passes={passes} output={root} " +
                $"minute={GameSessionState.GameMinuteOfDay} " +
                $"seed={city.Layout.Seed}");
            try
            {
                using (AuditRig rig = AuditRig.Borrow(city, true))
                {
                    rig.ClearAir = AuditEnvFlag("CITY_AUDIT_CLEAR_AIR");
                    // The first Camera.Render() of a session has no shadows
                    // and a cold shader cache; nobody keeps that frame.
                    rig.WarmUp();
                    yield return null;
                    if ((passes & AuditPass.TopDown) != 0)
                    {
                        yield return AuditTopDownPass(city, rig, report);
                    }

                    if ((passes & AuditPass.Stations) != 0)
                    {
                        yield return AuditStationsPass(city, rig, report);
                    }

                    if ((passes & AuditPass.Findings) != 0)
                    {
                        yield return AuditFindingsPass(city, rig, report);
                    }
                }
            }
            finally
            {
                report.Flush();
            }

            Debug.Log(
                $"CITY AUDIT: tiles={report.Tiles} stations={report.Stations} " +
                $"findings={report.Findings} blank={report.Blank}");
            if ((passes & AuditPass.Stations) != 0 && report.LatticeFrames > 0)
            {
                Assert.That(
                    report.LatticeFrames - report.LatticeBlank,
                    Is.GreaterThanOrEqualTo(report.LatticeFrames / 2),
                    "More than half of the lattice frames came out blank: " +
                    "the camera saw nothing from most standing squares.");
            }
        }

        private static AuditPass AuditPassesFromEnvironment()
        {
            string value = AuditEnv("CITY_AUDIT_PASSES");
            if (string.IsNullOrEmpty(value))
            {
                return AuditPass.All;
            }

            AuditPass passes = AuditPass.None;
            foreach (string raw in value.Split(',', ';', ' '))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "topdown":
                    case "top-down":
                    case "top":
                        passes |= AuditPass.TopDown;
                        break;
                    case "stations":
                    case "station":
                        passes |= AuditPass.Stations;
                        break;
                    case "findings":
                    case "finding":
                        passes |= AuditPass.Findings;
                        break;
                    case "all":
                        passes |= AuditPass.All;
                        break;
                }
            }

            return passes == AuditPass.None ? AuditPass.All : passes;
        }

        // ------------------------------------------------------------
        // Pass A - top-down tiles
        // ------------------------------------------------------------

        private IEnumerator AuditTopDownPass(
            CityGameRoot city,
            AuditRig rig,
            AuditReport report)
        {
            CityLayout layout = city.Layout;
            CityWorldResult world = city.World;
            string folder = report.Folder("topdown");
            const string IndexFile = "topdown/topdown-index.ndjson";
            const string FindingsFile = "topdown/topdown-findings.ndjson";
            report.Truncate(IndexFile);
            report.Truncate(FindingsFile);

            Rect bounds = AuditExpand(layout.MapWorldXZBounds, 24f);
            if (world.SeacoastPlan != null)
            {
                bounds = AuditUnion(bounds, world.SeacoastPlan.Grounds);
            }

            float maxGroundY = AuditMaximumGroundY(layout, bounds);
            float cameraY = maxGroundY + AuditTileCameraLift;
            float stride = AuditTileMetres - AuditTileOverlap;
            int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / stride));
            int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / stride));
            float metresPerPixel = AuditTileMetres / AuditTilePixels;
            List<Rect> lotRects = AuditLotRects(layout);
            LightShadows reliefShadows =
                rig.SavedSunShadows == LightShadows.None
                    ? LightShadows.Hard
                    : rig.SavedSunShadows;
            Debug.Log(
                AuditLogPrefix +
                $"topdown: bounds=({AuditNumber(bounds.xMin)},{AuditNumber(bounds.yMin)})" +
                $"-({AuditNumber(bounds.xMax)},{AuditNumber(bounds.yMax)}) " +
                $"tiles={columns}x{rows} maxGroundY={AuditNumber(maxGroundY)} " +
                $"cameraY={AuditNumber(cameraY)} mask rects={world.WalkableArea.Rectangles.Count}");

            GameObject overlay = AuditBuildMaskOverlay(layout, world);
            overlay.SetActive(false);
            int classCells = AuditTilePixels / AuditClassCell;
            var classes = new Texture2D(
                classCells,
                classCells,
                TextureFormat.RGB24,
                false);
            int nextFinding = 0;
            try
            {
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        float xMin = bounds.xMin + column * stride;
                        float xMax = xMin + AuditTileMetres;
                        float zMax = bounds.yMax - row * stride;
                        float zMin = zMax - AuditTileMetres;
                        Rect tile = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
                        Vector2 centre = tile.center;
                        string tileName =
                            "c" + AuditInt(column) + "r" + AuditInt(row);
                        string suffix =
                            $"{tileName}_x{AuditSigned(xMin)}_{AuditSigned(xMax)}" +
                            $"_z{AuditSigned(zMin)}_{AuditSigned(zMax)}";

                        // 1. Holes: magenta clear, no fog, no shadows. Every
                        //    magenta pixel is a place with no ground at all.
                        rig.SetTopDown(
                            centre,
                            AuditTileMetres * 0.5f,
                            cameraY,
                            Color.magenta,
                            false);
                        rig.SetSunShadows(LightShadows.None);
                        Texture2D holes = rig.Render(AuditTilePixels, AuditTilePixels);
                        AuditWriteTile(
                            report,
                            folder,
                            IndexFile,
                            "td_holes_" + suffix + ".png",
                            "holes",
                            tile,
                            holes,
                            1f / metresPerPixel);
                        AuditClassifyTile(
                            holes,
                            tile,
                            metresPerPixel,
                            layout,
                            world,
                            lotRects,
                            tileName,
                            suffix,
                            classes,
                            report,
                            folder,
                            IndexFile,
                            FindingsFile,
                            ref nextFinding);

                        // 2. Mask: the walkable rectangles drawn half a metre
                        //    over the ground, so a hole under the mask and
                        //    ground outside it can both be read by eye.
                        overlay.SetActive(true);
                        Texture2D mask = rig.Render(AuditTilePixels, AuditTilePixels);
                        overlay.SetActive(false);
                        AuditWriteTile(
                            report,
                            folder,
                            IndexFile,
                            "td_mask_" + suffix + ".png",
                            "mask",
                            tile,
                            mask,
                            1f / metresPerPixel);

                        // 3. Relief: a low sun from the north-west with
                        //    shadows, so kerbs, steps and floating slabs
                        //    show their height.
                        rig.SetSunShadows(reliefShadows);
                        rig.SetSunRotation(Quaternion.Euler(28f, 315f, 0f));
                        rig.SetShadowDistance(cameraY + 60f);
                        Texture2D relief = rig.Render(AuditTilePixels, AuditTilePixels);
                        rig.RestoreSunRotation();
                        rig.RestoreShadowDistance();
                        rig.SetSunShadows(LightShadows.None);
                        AuditWriteTile(
                            report,
                            folder,
                            IndexFile,
                            "td_relief_" + suffix + ".png",
                            "relief",
                            tile,
                            relief,
                            1f / metresPerPixel);

                        report.Tiles++;
                        Debug.Log(
                            AuditLogPrefix +
                            $"topdown tile {tileName} done ({report.Tiles}/{columns * rows}), " +
                            $"findings so far={nextFinding}");
                        yield return null;
                    }
                }

                float half = Mathf.Max(bounds.width, bounds.height) * 0.5f;
                Vector2 overviewCentre = bounds.center;
                rig.SetTopDown(overviewCentre, half, cameraY, Color.magenta, false);
                rig.SetSunShadows(LightShadows.None);
                Texture2D overview = rig.Render(1024, 1024);
                AuditWriteTile(
                    report,
                    folder,
                    IndexFile,
                    "td_overview.png",
                    "overview",
                    Rect.MinMaxRect(
                        overviewCentre.x - half,
                        overviewCentre.y - half,
                        overviewCentre.x + half,
                        overviewCentre.y + half),
                    overview,
                    1024f / (2f * half));
            }
            finally
            {
                rig.RestoreSunRotation();
                rig.RestoreShadowDistance();
                Object.DestroyImmediate(classes);
                AuditDestroyOverlay(overlay);
                report.Flush();
            }
        }

        private static void AuditWriteTile(
            AuditReport report,
            string folder,
            string indexFile,
            string file,
            string pass,
            Rect tile,
            Texture2D frame,
            float pixelsPerMetre)
        {
            File.WriteAllBytes(Path.Combine(folder, file), frame.EncodeToPNG());
            bool blank = IsBlank(frame);
            if (blank)
            {
                report.Blank++;
            }

            report.Line(
                indexFile,
                new AuditJson()
                    .Add("file", file)
                    .Add("pass", pass)
                    .Add("xMin", tile.xMin)
                    .Add("xMax", tile.xMax)
                    .Add("zMin", tile.yMin)
                    .Add("zMax", tile.yMax)
                    .Add("pxPerMetre", pixelsPerMetre)
                    .Add("width", frame.width)
                    .Add("height", frame.height)
                    .Add("blank", blank)
                    .ToString());
        }

        /// <summary>
        /// Classifies one hole tile on a 4-pixel grid: a magenta cell is a
        /// hole, and whether it lies under the walkable mask or on land
        /// that should have ground says which kind; a non-magenta land cell
        /// outside the mask is ground the player can see and not reach.
        /// Same-class cells are flood-filled into clusters and every
        /// cluster of at least a quarter square metre becomes a finding in
        /// the probe's own schema, so the findings pass can photograph it.
        /// </summary>
        private static void AuditClassifyTile(
            Texture2D holes,
            Rect tile,
            float metresPerPixel,
            CityLayout layout,
            CityWorldResult world,
            List<Rect> lotRects,
            string tileName,
            string suffix,
            Texture2D classes,
            AuditReport report,
            string folder,
            string indexFile,
            string findingsFile,
            ref int nextFinding)
        {
            Color32[] pixels = holes.GetPixels32();
            int size = holes.width;
            int cells = size / AuditClassCell;
            var grid = new byte[cells * cells];
            Rect map = layout.MapWorldXZBounds;
            float cellMetres = metresPerPixel * AuditClassCell;
            RoadWalkableArea mask = world.WalkableArea;
            int halfCell = AuditClassCell / 2;
            for (int gy = 0; gy < cells; gy++)
            {
                for (int gx = 0; gx < cells; gx++)
                {
                    int px = gx * AuditClassCell + halfCell;
                    int py = gy * AuditClassCell + halfCell;
                    Color32 colour = pixels[py * size + px];
                    bool magenta = AuditIsMagenta(colour);
                    float x = tile.xMin + (px + 0.5f) * metresPerPixel;
                    float z = tile.yMin + (py + 0.5f) * metresPerPixel;
                    var point = new Vector3(x, 0f, z);
                    bool land =
                        map.Contains(new Vector2(x, z)) &&
                        !layout.IsWater(point) &&
                        !AuditInsideAnyRect(lotRects, x, z);
                    byte cls = 0;
                    if (magenta)
                    {
                        if (mask.Contains(point, 0f))
                        {
                            cls = 1;
                        }
                        else if (land)
                        {
                            cls = 2;
                        }
                    }
                    else if (land && !mask.Contains(point, 0f))
                    {
                        cls = 3;
                    }

                    grid[gy * cells + gx] = cls;
                }
            }

            var colours = new Color32[grid.Length];
            var black = new Color32(0, 0, 0, 255);
            var red = new Color32(255, 0, 0, 255);
            var orange = new Color32(255, 140, 0, 255);
            var yellow = new Color32(255, 255, 0, 255);
            for (int index = 0; index < grid.Length; index++)
            {
                switch (grid[index])
                {
                    case 1:
                        colours[index] = red;
                        break;
                    case 2:
                        colours[index] = orange;
                        break;
                    case 3:
                        colours[index] = yellow;
                        break;
                    default:
                        colours[index] = black;
                        break;
                }
            }

            classes.SetPixels32(colours);
            classes.Apply();
            AuditWriteTile(
                report,
                folder,
                indexFile,
                "td_classes_" + suffix + ".png",
                "classes",
                tile,
                classes,
                1f / cellMetres);

            List<AuditCluster> clusters = AuditFindClusters(grid, cells, cells);
            float cellArea = cellMetres * cellMetres;
            var perClass = new Dictionary<byte, List<AuditCluster>>();
            for (int index = 0; index < clusters.Count; index++)
            {
                AuditCluster cluster = clusters[index];
                if (cluster.Cells * cellArea < AuditMinimumClusterArea)
                {
                    continue;
                }

                if (!perClass.TryGetValue(cluster.Class, out List<AuditCluster> list))
                {
                    list = new List<AuditCluster>();
                    perClass[cluster.Class] = list;
                }

                list.Add(cluster);
            }

            int written = 0;
            int dropped = 0;
            foreach (KeyValuePair<byte, List<AuditCluster>> entry in perClass)
            {
                List<AuditCluster> list = entry.Value;
                list.Sort((a, b) => b.Cells.CompareTo(a.Cells));
                int count = Mathf.Min(list.Count, AuditMaximumClustersPerClass);
                dropped += list.Count - count;
                string className;
                string subtype;
                string category;
                switch (entry.Key)
                {
                    case 1:
                        className = "HoleUnderMask";
                        subtype = "hole_under_mask";
                        category = "B";
                        break;
                    case 2:
                        className = "HoleOnLand";
                        subtype = "hole_on_land";
                        category = "B";
                        break;
                    default:
                        className = "GroundOutsideMask";
                        subtype = "ground_outside_mask";
                        category = "A";
                        break;
                }

                for (int index = 0; index < count; index++)
                {
                    AuditCluster cluster = list[index];
                    float gx = (float)cluster.SumX / cluster.Cells;
                    float gy = (float)cluster.SumY / cluster.Cells;
                    float x = tile.xMin + (gx + 0.5f) * cellMetres;
                    float z = tile.yMin + (gy + 0.5f) * cellMetres;
                    int px = Mathf.RoundToInt(gx) * AuditClassCell + halfCell;
                    int pyTop = size - 1 - (Mathf.RoundToInt(gy) * AuditClassCell + halfCell);
                    float area = cluster.Cells * cellArea;
                    string id = "td-" + nextFinding.ToString("0000", CultureInfo.InvariantCulture);
                    nextFinding++;
                    written++;
                    string bbox =
                        "[" + AuditNumber(tile.xMin + cluster.MinX * cellMetres) +
                        "," + AuditNumber(tile.yMin + cluster.MinY * cellMetres) +
                        "," + AuditNumber(tile.xMin + (cluster.MaxX + 1) * cellMetres) +
                        "," + AuditNumber(tile.yMin + (cluster.MaxY + 1) * cellMetres) + "]";
                    report.Line(
                        findingsFile,
                        new AuditJson()
                            .Add("id", id)
                            .Add("category", category)
                            .Add("subtype", subtype)
                            .Add("tag", "topdown")
                            .Add("x", x)
                            .Add("z", z)
                            .Add("dir", "top")
                            .Add("area", "topdown")
                            .Raw("objects", "[]")
                            .Raw(
                                "metrics",
                                "{\"area_m2\":" + AuditNumber(area) +
                                ",\"cells\":" + AuditInt(cluster.Cells) +
                                ",\"bbox\":" + bbox + "}")
                            .Add(
                                "note",
                                $"{className} {AuditNumber(area)} m2 tile {tileName} " +
                                $"px ({AuditInt(px)},{AuditInt(pyTop)}) of {AuditInt(size)}")
                            .ToString());
                }
            }

            Debug.Log(
                AuditLogPrefix +
                $"topdown tile {tileName}: clusters={clusters.Count} written={written} " +
                $"dropped over cap={dropped}");
        }

        private static bool AuditIsMagenta(Color32 colour)
        {
            return Mathf.Abs(colour.r - 255) < 24 &&
                   colour.g < 40 &&
                   Mathf.Abs(colour.b - 255) < 24;
        }

        private struct AuditCluster
        {
            public byte Class;
            public int Cells;
            public long SumX;
            public long SumY;
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;
        }

        /// <summary>
        /// Eight-neighbour flood fill over a byte grid. Class zero is the
        /// background; every other value groups with its equals.
        /// </summary>
        private static List<AuditCluster> AuditFindClusters(
            byte[] grid,
            int width,
            int height)
        {
            var clusters = new List<AuditCluster>();
            var visited = new bool[grid.Length];
            var stack = new Stack<int>();
            for (int start = 0; start < grid.Length; start++)
            {
                if (grid[start] == 0 || visited[start])
                {
                    continue;
                }

                byte cls = grid[start];
                var cluster = new AuditCluster
                {
                    Class = cls,
                    MinX = int.MaxValue,
                    MinY = int.MaxValue,
                    MaxX = int.MinValue,
                    MaxY = int.MinValue
                };
                visited[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int x = index % width;
                    int y = index / width;
                    cluster.Cells++;
                    cluster.SumX += x;
                    cluster.SumY += y;
                    if (x < cluster.MinX) cluster.MinX = x;
                    if (y < cluster.MinY) cluster.MinY = y;
                    if (x > cluster.MaxX) cluster.MaxX = x;
                    if (y > cluster.MaxY) cluster.MaxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height)
                        {
                            continue;
                        }

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= width || (dx == 0 && dy == 0))
                            {
                                continue;
                            }

                            int next = ny * width + nx;
                            if (!visited[next] && grid[next] == cls)
                            {
                                visited[next] = true;
                                stack.Push(next);
                            }
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static float AuditMaximumGroundY(CityLayout layout, Rect bounds)
        {
            float best = 0f;
            for (float z = bounds.yMin; z <= bounds.yMax; z += 10f)
            {
                for (float x = bounds.xMin; x <= bounds.xMax; x += 10f)
                {
                    if (AuditSampledGroundTop(layout, new Vector2(x, z), out float top) &&
                        top > best)
                    {
                        best = top;
                    }
                }
            }

            return best;
        }

        private static List<Rect> AuditLotRects(CityLayout layout)
        {
            var rects = new List<Rect>(layout.BuildingLots.Count);
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding)
                {
                    continue;
                }

                rects.Add(Rect.MinMaxRect(
                    lot.Center.x - lot.Size.x * 0.5f,
                    lot.Center.z - lot.Size.y * 0.5f,
                    lot.Center.x + lot.Size.x * 0.5f,
                    lot.Center.z + lot.Size.y * 0.5f));
            }

            return rects;
        }

        private static bool AuditInsideAnyRect(List<Rect> rects, float x, float z)
        {
            for (int index = 0; index < rects.Count; index++)
            {
                Rect rect = rects[index];
                if (x >= rect.xMin && x <= rect.xMax &&
                    z >= rect.yMin && z <= rect.yMax)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One mesh with a quad per walkable rectangle, each corner lifted
        /// half a metre over the sampled ground, drawn transparent magenta.
        /// Built once per pass and destroyed with its material.
        /// </summary>
        private static GameObject AuditBuildMaskOverlay(
            CityLayout layout,
            CityWorldResult world)
        {
            var host = new GameObject("City Audit Mask Overlay");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning(AuditLogPrefix + "URP Unlit shader not found; the mask overlay is empty.");
                return host;
            }

            IReadOnlyList<Rect> rects = world.WalkableArea.Rectangles;
            var vertices = new List<Vector3>(rects.Count * 4);
            var triangles = new List<int>(rects.Count * 12);
            for (int index = 0; index < rects.Count; index++)
            {
                Rect rect = rects[index];
                int baseIndex = vertices.Count;
                vertices.Add(AuditOverlayCorner(layout, rect.xMin, rect.yMin));
                vertices.Add(AuditOverlayCorner(layout, rect.xMin, rect.yMax));
                vertices.Add(AuditOverlayCorner(layout, rect.xMax, rect.yMax));
                vertices.Add(AuditOverlayCorner(layout, rect.xMax, rect.yMin));
                // Both windings: the sheet must read from above and from a
                // finding's low top view whichever way the quad happens to
                // face, and depth writes are off so nothing is lost.
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex);
            }

            var mesh = new Mesh { name = "City Audit Mask Overlay" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var material = new Material(shader) { name = "City Audit Mask Overlay" };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", 5f);
            material.SetFloat("_DstBlend", 10f);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            material.SetColor("_BaseColor", new Color(1f, 0f, 1f, 0.4f));

            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return host;
        }

        private static Vector3 AuditOverlayCorner(CityLayout layout, float x, float z)
        {
            var xz = new Vector2(x, z);
            float top;
            if (!AuditSampledGroundTop(layout, xz, out top) &&
                !AuditRaycastGround(x, z, 80f, 200f, out top))
            {
                top = 0f;
            }

            return new Vector3(x, top + 0.5f, z);
        }

        private static void AuditDestroyOverlay(GameObject overlay)
        {
            if (overlay == null)
            {
                return;
            }

            MeshFilter filter = overlay.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Object.DestroyImmediate(filter.sharedMesh);
            }

            MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Object.DestroyImmediate(renderer.sharedMaterial);
            }

            Object.DestroyImmediate(overlay);
        }

        // ------------------------------------------------------------
        // Pass B - station grid and named places
        // ------------------------------------------------------------

        private IEnumerator AuditStationsPass(
            CityGameRoot city,
            AuditRig rig,
            AuditReport report)
        {
            CityLayout layout = city.Layout;
            string folder = report.Folder("stations");
            const string IndexFile = "stations/stations-index.ndjson";
            report.Truncate(IndexFile);
            float pitch = Mathf.Max(
                CityMapTeleportLatticeBuilder.MinimumCellSize,
                AuditEnvFloat("CITY_AUDIT_STATION_PITCH", 13f));
            var ground = new CityMapCityTeleportGround(layout);
            CityMapTeleportLattice lattice = CityMapTeleportLatticeBuilder.Create(
                AuditExpand(layout.MapWorldXZBounds, 8f),
                new Vector2(layout.WorldOrigin.x, layout.WorldOrigin.z),
                pitch,
                ground);
            Debug.Log(
                AuditLogPrefix +
                $"stations: pitch={AuditNumber(pitch)} squares={lattice.Squares.Count}");
            var names = new HashSet<string>(StringComparer.Ordinal);
            int[] yaws = { 0, 90, 180, 270 };
            int frames = 0;
            for (int index = 0; index < lattice.Squares.Count; index++)
            {
                CityMapTeleportSquare square = lattice.Squares[index];
                Vector3 stand = square.StandingPosition;
                city.Player.Motor.Teleport(stand);
                yield return null;
                yield return null;
                var eye = new Vector3(
                    stand.x,
                    stand.y - PlayerFactory.GroundedRootOffset + EyeHeight,
                    stand.z);
                for (int y = 0; y < yaws.Length; y++)
                {
                    int yaw = yaws[y];
                    Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    rig.SetPerspective(
                        eye,
                        eye + direction * 10f - Vector3.up * 1.4f,
                        AuditStationFieldOfView);
                    Texture2D frame = rig.Render(Width, Height);
                    bool blank = IsBlank(frame);
                    string file = AuditUniqueName(
                        names,
                        $"st_x{AuditSigned(eye.x)}_z{AuditSigned(eye.z)}_yaw{AuditYawText(yaw)}",
                        ".jpg");
                    File.WriteAllBytes(
                        Path.Combine(folder, file),
                        frame.EncodeToJPG(AuditJpegQuality));
                    report.Line(
                        IndexFile,
                        new AuditJson()
                            .Add("file", file)
                            .Add("kind", "lattice")
                            .Add("x", eye.x)
                            .Add("z", eye.z)
                            .Add("y", eye.y)
                            .Add("yaw", yaw)
                            .Raw("cell", "[" + AuditInt(square.Cell.x) + "," + AuditInt(square.Cell.y) + "]")
                            .Add("standing", true)
                            .Add("blank", blank)
                            .ToString());
                    report.Stations++;
                    report.LatticeFrames++;
                    if (blank)
                    {
                        report.Blank++;
                        report.LatticeBlank++;
                    }

                    frames++;
                    if (frames % 100 == 0)
                    {
                        Debug.Log(
                            AuditLogPrefix +
                            $"stations: {frames} lattice frames " +
                            $"({index + 1}/{lattice.Squares.Count} squares), " +
                            $"blank={report.LatticeBlank}");
                    }
                }
            }

            report.Flush();
            List<AuditNamedShot> named = new AuditNamedPlaceBuilder(city, ground).Build();
            Debug.Log(AuditLogPrefix + $"stations: {named.Count} named frames");
            for (int index = 0; index < named.Count; index++)
            {
                AuditNamedShot shot = named[index];
                if (shot.HasStand)
                {
                    city.Player.Motor.Teleport(shot.Stand);
                }

                yield return null;
                rig.SetPerspective(shot.Eye, shot.Target, AuditNamedFieldOfView);
                Texture2D frame = rig.Render(Width, Height);
                bool blank = IsBlank(frame);
                int yaw = AuditYaw(shot.Eye, shot.Target);
                string file = AuditUniqueName(
                    names,
                    $"np_{shot.Slug}_{AuditInt(shot.Index)}" +
                    $"_x{AuditSigned(shot.Eye.x)}_z{AuditSigned(shot.Eye.z)}_yaw{AuditYawText(yaw)}",
                    ".jpg");
                File.WriteAllBytes(
                    Path.Combine(folder, file),
                    frame.EncodeToJPG(AuditJpegQuality));
                report.Line(
                    IndexFile,
                    new AuditJson()
                        .Add("file", file)
                        .Add("kind", "named")
                        .Add("slug", shot.Slug)
                        .Add("k", shot.Index)
                        .Add("x", shot.Eye.x)
                        .Add("z", shot.Eye.z)
                        .Add("y", shot.Eye.y)
                        .Add("yaw", yaw)
                        .Add("target", shot.Target)
                        .Add("standing", shot.Standing)
                        .Add("blank", blank)
                        .ToString());
                report.Stations++;
                if (blank)
                {
                    report.Blank++;
                }

                frames++;
                if (frames % 100 == 0)
                {
                    Debug.Log(AuditLogPrefix + $"stations: {frames} frames");
                }
            }

            report.Flush();
        }

        private sealed class AuditNamedShot
        {
            public string Slug;
            public int Index;
            public Vector3 Eye;
            public Vector3 Target;
            public bool Standing;
            public bool HasStand;
            public Vector3 Stand;
        }

        /// <summary>
        /// The places a human auditor would walk to. Every group is built
        /// under its own guard and every member is read through the plan
        /// classes verified at authoring time; a group whose plan is absent
        /// on this seed is skipped with a log line, never a failure.
        /// </summary>
        private sealed class AuditNamedPlaceBuilder
        {
            private static readonly CitySeacoastPartKind[] SeacoastKinds =
            {
                CitySeacoastPartKind.Hut,
                CitySeacoastPartKind.MolDeck,
                CitySeacoastPartKind.PierDeck,
                CitySeacoastPartKind.EsplanadeSlab,
                CitySeacoastPartKind.FootbridgeDeck,
                CitySeacoastPartKind.Slipway,
                CitySeacoastPartKind.Barge
            };

            private readonly CityGameRoot city;
            private readonly CityLayout layout;
            private readonly CityWorldResult world;
            private readonly CityMapCityTeleportGround ground;
            private readonly List<AuditNamedShot> shots = new List<AuditNamedShot>();
            private readonly Dictionary<string, int> counters =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public AuditNamedPlaceBuilder(
                CityGameRoot city,
                CityMapCityTeleportGround ground)
            {
                this.city = city;
                layout = city.Layout;
                world = city.World;
                this.ground = ground;
            }

            public List<AuditNamedShot> Build()
            {
                Guard("doors", Doors);
                Guard("points-of-interest", PointsOfInterest);
                Guard("bus-stops", BusStops);
                Guard("tunnel", Tunnel);
                Guard("seacoast", Seacoast);
                Guard("port", Port);
                Guard("lighthouse", Lighthouse);
                Guard("river", River);
                Guard("park", Park);
                Guard("cemetery", Cemetery);
                Guard("church", Church);
                Guard("stairs", Stairs);
                Guard("fringe-yards", FringeYards);
                Guard("open-area-accesses", OpenAreaAccesses);
                Guard("arch-shelter", ArchShelter);
                Guard("last-route", LastRoute);
                Guard("east-exit", EastExit);
                return shots;
            }

            private void Guard(string group, Action build)
            {
                int before = shots.Count;
                try
                {
                    build();
                }
                catch (Exception error)
                {
                    Debug.LogWarning(
                        AuditLogPrefix +
                        $"named place group '{group}' skipped: {error.GetType().Name}: {error.Message}");
                }

                Debug.Log(AuditLogPrefix + $"named '{group}': {shots.Count - before} frames");
            }

            private void Skip(string what)
            {
                Debug.Log(AuditLogPrefix + "named place skipped (absent on this seed): " + what);
            }

            /// <summary>
            /// One frame. <paramref name="eyeGround"/> is where the feet
            /// go; <paramref name="trustHeight"/> says its Y is a real
            /// surface height rather than a guess to be re-sampled.
            /// </summary>
            private void Add(string slug, Vector3 eyeGround, Vector3 target, bool trustHeight)
            {
                slug = AuditSlug(slug);
                counters.TryGetValue(slug, out int index);
                counters[slug] = index + 1;
                shots.Add(AuditResolveNamedShot(layout, ground, slug, index, eyeGround, target, trustHeight));
            }

            private void Doors()
            {
                for (int index = 0; index < world.Bars.Count; index++)
                {
                    BarEntrance bar = world.Bars[index];
                    if (bar != null)
                    {
                        Door("bar-" + AuditInt(index), bar.ReturnPosition);
                    }
                }

                if (world.PlayerHome != null)
                {
                    Door("home", world.PlayerHome.ReturnPosition);
                }
                else
                {
                    Skip("home");
                }

                if (world.Supermarket != null)
                {
                    Door("supermarket", world.Supermarket.ReturnPosition);
                }
                else
                {
                    Skip("supermarket");
                }
            }

            private void Door(string slug, Vector3 returnPosition)
            {
                Vector3 target = AuditNearestDoor(layout, returnPosition, out bool found)
                    + Vector3.up * 1.5f;
                if (!found)
                {
                    target = returnPosition + Vector3.forward * 5f + Vector3.up * 1.2f;
                }

                Add(slug, returnPosition, target, true);
                Add(slug, AuditBackOff(returnPosition, target, 6f), target, false);
            }

            private void PointsOfInterest()
            {
                IReadOnlyList<CityDistrictPointOfInterestDescriptor> points =
                    layout.DistrictPointsOfInterest;
                for (int index = 0; index < points.Count; index++)
                {
                    CityDistrictPointOfInterestDescriptor poi = points[index];
                    string slug = "poi-" + poi.Kind.ToString() + "-" + AuditInt(index);
                    Vector3 centre = poi.Center;
                    Add(slug, centre + Vector3.back * 9f, centre + Vector3.up, false);
                    Add(slug, centre + Vector3.right * 9f, centre + Vector3.up, false);
                }
            }

            private void BusStops()
            {
                if (city.BusPlan == null)
                {
                    Skip("bus stops");
                    return;
                }

                IReadOnlyList<CityBusStopDescriptor> stops = city.BusPlan.Stops;
                for (int index = 0; index < stops.Count; index++)
                {
                    CityBusStopDescriptor stop = stops[index];
                    Vector3 forward = stop.Forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.001f)
                    {
                        forward = Vector3.forward;
                    }

                    forward.Normalize();
                    Vector3 target = (stop.HasMappedShelter ? stop.ShelterPosition : stop.Position)
                        + Vector3.up * 1.2f;
                    Add("busstop-" + AuditInt(index), stop.Position - forward * 8f, target, true);
                }
            }

            private void Tunnel()
            {
                CityMountainBoundaryPlan boundary = world.MountainBoundaryPlan;
                if (boundary == null || !boundary.HasTunnel)
                {
                    Skip("tunnel");
                    return;
                }

                CityMountainTunnelDescriptor tunnel = boundary.Tunnel;
                Vector3 portal = tunnel.PortalGroundCenter;
                Vector3 axis = tunnel.Axis;
                axis.y = 0f;
                axis.Normalize();
                Add("tunnel", portal - axis * 12f, portal + Vector3.up * 2f, true);
                Add("tunnel", portal - axis * 1f, portal + axis * 10f + Vector3.up * 1.5f, true);
                Add("tunnel", portal + axis * 6f, portal - axis * 10f + Vector3.up * 1.5f, true);
            }

            private void Seacoast()
            {
                CitySeacoastPlan coast = world.SeacoastPlan;
                if (coast == null)
                {
                    Skip("seacoast");
                    return;
                }

                for (int k = 0; k < SeacoastKinds.Length; k++)
                {
                    CitySeacoastPartKind kind = SeacoastKinds[k];
                    var parts = new List<CitySeacoastPartDescriptor>();
                    for (int index = 0; index < coast.Parts.Count; index++)
                    {
                        if (coast.Parts[index].Kind == kind)
                        {
                            parts.Add(coast.Parts[index]);
                        }
                    }

                    if (parts.Count == 0)
                    {
                        Skip("seacoast " + kind);
                        continue;
                    }

                    int[] picks = parts.Count <= 3
                        ? AuditRange(parts.Count)
                        : new[] { 0, parts.Count / 2, parts.Count - 1 };
                    for (int p = 0; p < picks.Length; p++)
                    {
                        CitySeacoastPartDescriptor part = parts[picks[p]];
                        float top = part.Center.y + part.Size.y * 0.5f;
                        var eyeGround = new Vector3(part.Center.x, top, part.Center.z - 6f);
                        var target = new Vector3(part.Center.x, top + 0.5f, part.Center.z);
                        Add("coast-" + kind.ToString(), eyeGround, target, true);
                    }
                }

                CitySeacoastFrame frame = coast.Frame;
                float y = frame.BeachEdgeTopY;
                Add(
                    "coast-channel-west",
                    new Vector3(frame.ChannelXMin - 5f, y, frame.WaterlineZ - 8f),
                    new Vector3(frame.ChannelXMin, y - 0.5f, frame.WaterlineZ),
                    true);
                Add(
                    "coast-channel-east",
                    new Vector3(frame.ChannelXMax + 5f, y, frame.WaterlineZ - 8f),
                    new Vector3(frame.ChannelXMax, y - 0.5f, frame.WaterlineZ),
                    true);
            }

            private void Port()
            {
                CityPortPlan port = world.PortPlan;
                if (port == null || port.Access == null)
                {
                    Skip("port");
                    return;
                }

                var centres = new List<Vector3>();
                foreach (var sample in port.Access.RoadSamples)
                {
                    centres.Add(sample.center);
                }

                if (centres.Count < 2)
                {
                    Skip("port road samples");
                    return;
                }

                int mid = centres.Count / 2;
                Add("port-road", centres[0], centres[mid] + Vector3.up, true);
                Add("port-road", centres[mid], centres[centres.Count - 1] + Vector3.up, true);
                Add("port-road", centres[centres.Count - 1], centres[mid] + Vector3.up, true);
            }

            private void Lighthouse()
            {
                CitySeacoastPlan coast = world.SeacoastPlan;
                if (coast == null)
                {
                    Skip("lighthouse");
                    return;
                }

                CityLighthouseIslandPlan island =
                    CityLighthouseIslandPlanner.Create(layout.Seed, coast);
                if (island == null)
                {
                    Skip("lighthouse island");
                    return;
                }

                Vector3 lantern = island.LanternPosition;
                CitySeacoastFrame frame = coast.Frame;
                float sandZ = frame.WaterlineZ - 4f;
                float x = Mathf.Clamp(
                    lantern.x,
                    frame.BeachRowBounds.xMin + 4f,
                    frame.BeachRowBounds.xMax - 4f);
                if (x > frame.ChannelXMin - 3f && x < frame.ChannelXMax + 3f)
                {
                    x = frame.ChannelXMax + 3f;
                }

                Add(
                    "lighthouse",
                    new Vector3(x, frame.BeachEdgeTopY, sandZ),
                    new Vector3(lantern.x, lantern.y - 2.5f, lantern.z),
                    true);
            }

            private void River()
            {
                CityRiverPlan river = layout.River;
                if (river == null || !river.IsEnabled)
                {
                    Skip("river");
                    return;
                }

                for (int bank = 0; bank < 2; bank++)
                {
                    bool west = bank == 0;
                    string side = west ? "west" : "east";
                    bool hasNorth = false;
                    bool hasSouth = false;
                    CityRiverPromenadeDescriptor north = default;
                    CityRiverPromenadeDescriptor south = default;
                    for (int index = 0; index < river.Promenades.Count; index++)
                    {
                        CityRiverPromenadeDescriptor promenade = river.Promenades[index];
                        if (promenade.WestBank != west)
                        {
                            continue;
                        }

                        if (!hasNorth || promenade.Bounds.yMax > north.Bounds.yMax)
                        {
                            north = promenade;
                            hasNorth = true;
                        }

                        if (!hasSouth || promenade.Bounds.yMin < south.Bounds.yMin)
                        {
                            south = promenade;
                            hasSouth = true;
                        }
                    }

                    if (hasNorth)
                    {
                        float x = north.Bounds.center.x;
                        float yMax = north.Bounds.yMax;
                        Add(
                            "quay-shore-" + side,
                            new Vector3(x, north.NorthY, yMax - 6f),
                            new Vector3(x, north.NorthY + 0.3f, yMax + 5f),
                            true);
                        Add(
                            "quay-shore-" + side,
                            new Vector3(x, north.NorthY, yMax - 1f),
                            new Vector3(x, north.NorthY + 0.3f, yMax - 12f),
                            true);
                    }

                    if (hasSouth)
                    {
                        float x = south.Bounds.center.x;
                        float yMin = south.Bounds.yMin;
                        Add(
                            "quay-cave-" + side,
                            new Vector3(x, south.SouthY, yMin + 6f),
                            new Vector3(x, south.SouthY + 0.3f, yMin - 5f),
                            true);
                    }
                }

                for (int index = 0; index < river.Bridges.Count; index++)
                {
                    CityRiverBridgeDescriptor bridge = river.Bridges[index];
                    Rect deck = bridge.DeckBounds;
                    float z = deck.center.y;
                    string slug = "bridge-" + AuditInt(index);
                    Add(
                        slug,
                        new Vector3(deck.xMin - 5f, bridge.WestY, z),
                        new Vector3(deck.xMax, bridge.EastY + 0.3f, z),
                        true);
                    Add(
                        slug,
                        new Vector3(deck.xMax + 5f, bridge.EastY, z),
                        new Vector3(deck.xMin, bridge.WestY + 0.3f, z),
                        true);
                }

                for (int index = 0; index < river.Landings.Count; index++)
                {
                    CityRiverLandingDescriptor landing = river.Landings[index];
                    Vector2 platform = landing.PlatformBounds.center;
                    Vector2 stair = landing.StairBounds.center;
                    string slug = "landing-" + AuditInt(index);
                    Add(
                        slug,
                        new Vector3(platform.x, landing.LowerY, platform.y),
                        new Vector3(stair.x, (landing.UpperY + landing.LowerY) * 0.5f, stair.y),
                        true);
                    Vector3 descent = landing.DescentDirection;
                    descent.y = 0f;
                    if (descent.sqrMagnitude < 0.001f)
                    {
                        descent = Vector3.forward;
                    }

                    descent.Normalize();
                    float run = Mathf.Max(landing.StairBounds.width, landing.StairBounds.height) * 0.5f;
                    Vector3 top = new Vector3(stair.x, landing.UpperY, stair.y) - descent * (run + 0.8f);
                    Add(slug, top, new Vector3(platform.x, landing.LowerY, platform.y), true);
                }

                CityMountainBoundaryPlan boundary = world.MountainBoundaryPlan;
                if (boundary != null && boundary.HasRiverNotch)
                {
                    CityMountainRiverNotchDescriptor notch = boundary.RiverNotch;
                    Vector3 axis = notch.Axis;
                    axis.y = 0f;
                    if (axis.sqrMagnitude < 0.001f)
                    {
                        axis = Vector3.back;
                    }

                    axis.Normalize();
                    CavePromenade("cave-promenade-west", notch.WestPromenadeBounds, notch.WestCityBankY, axis);
                    CavePromenade("cave-promenade-east", notch.EastPromenadeBounds, notch.EastCityBankY, axis);
                }
            }

            private void CavePromenade(string slug, Rect bounds, float y, Vector3 axis)
            {
                if (bounds.width <= 0f || bounds.height <= 0f)
                {
                    Skip(slug);
                    return;
                }

                var centre = new Vector3(bounds.center.x, y, bounds.center.y);
                float half = Mathf.Max(bounds.width, bounds.height) * 0.5f;
                Add(slug, centre - axis * Mathf.Max(0f, half - 1f), centre + axis * (half + 2f) + Vector3.up * 0.3f, true);
                Add(slug, centre + axis * Mathf.Max(0f, half - 1f), centre - axis * (half + 2f) + Vector3.up * 0.3f, true);
            }

            private void Park()
            {
                CityParkPlan park = layout.Park;
                if (park == null || !park.IsEnabled)
                {
                    Skip("park");
                    return;
                }

                for (int index = 0; index < park.Regions.Count; index++)
                {
                    CityParkRegionPlan region = park.Regions[index];
                    string slug = "park-" + AuditSlug(region.Id);
                    Add(slug, region.Center + Vector3.back * 10f, region.Center + Vector3.up, false);
                    if (AuditPlanar(region.PlazaPosition, region.Center) > 1f)
                    {
                        Add(slug, region.PlazaPosition + Vector3.back * 8f, region.PlazaPosition + Vector3.up, false);
                    }
                }

                for (int index = 0; index < park.Gates.Count; index++)
                {
                    CityParkGateDescriptor gate = park.Gates[index];
                    Vector3 normal = gate.OutwardNormal;
                    normal.y = 0f;
                    if (normal.sqrMagnitude < 0.001f)
                    {
                        normal = Vector3.forward;
                    }

                    normal.Normalize();
                    string slug = "park-gate-" + AuditInt(index);
                    // The normal's sense is not read from the name: one frame
                    // from each side of the opening.
                    Add(slug, gate.Center + normal * 6f, gate.Center + Vector3.up * 1.2f, false);
                    Add(slug, gate.Center - normal * 6f, gate.Center + Vector3.up * 1.2f, false);
                }

                Transform root = world.Root != null ? world.Root.transform : null;
                Transform fountain = AuditFindByName(root, "Park Fountain Water");
                if (fountain != null && AuditRendererBounds(fountain.gameObject, out Bounds basin))
                {
                    Vector3 centre = basin.center;
                    Add(
                        "park-fountain",
                        new Vector3(centre.x, centre.y, centre.z - 7.5f),
                        new Vector3(centre.x, centre.y + 1.3f, centre.z),
                        false);
                }
                else
                {
                    Skip("park fountain");
                }

                Transform swings = AuditFindByName(root, "Park Playground Swings");
                if (swings != null)
                {
                    Vector3 position = swings.position;
                    Add("park-swings", position + Vector3.back * 6f, position + Vector3.up * 1.2f, false);
                }
                else
                {
                    Skip("park swings");
                }

                if (city.ParkChessSetMen != null &&
                    AuditRendererBounds(city.ParkChessSetMen, out Bounds chess))
                {
                    Vector3 centre = chess.center;
                    Add("park-chess", new Vector3(centre.x, chess.min.y, centre.z - 5f), centre, false);
                    Add("park-chess", new Vector3(centre.x + 5f, chess.min.y, centre.z), centre, false);
                }
                else
                {
                    Skip("park chess set");
                }
            }

            private void Cemetery()
            {
                CityCemeteryPlan cemetery = world.CemeteryPlan;
                if (cemetery == null || cemetery.Plots.Count == 0)
                {
                    Skip("cemetery");
                    return;
                }

                Vector3 sum = Vector3.zero;
                for (int index = 0; index < cemetery.Plots.Count; index++)
                {
                    sum += cemetery.Plots[index].Ground;
                }

                Vector3 centre = sum / cemetery.Plots.Count;
                Vector3 from = world.ChurchPlan != null
                    ? world.ChurchPlan.DoorGroundPosition
                    : centre + Vector3.back * 20f;
                Vector3 direction = centre - from;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1f)
                {
                    direction = Vector3.forward;
                }

                direction.Normalize();
                Add("cemetery-plots", centre - direction * 12f, centre + Vector3.up * 0.8f, true);
                Add("cemetery-plots", centre + direction * 12f, centre + Vector3.up * 0.8f, true);

                Vector3 groundsCentre = new Vector3(
                    cemetery.Grounds.center.x,
                    centre.y,
                    cemetery.Grounds.center.y);
                bool gateFound = false;
                bool lodgeFound = false;
                for (int index = 0; index < cemetery.Parts.Count; index++)
                {
                    CityCemeteryPartDescriptor part = cemetery.Parts[index];
                    if (part.Kind == CityCemeteryPartKind.GateArch && !gateFound)
                    {
                        gateFound = true;
                        Vector3 outward = part.Center - groundsCentre;
                        outward.y = 0f;
                        if (outward.sqrMagnitude < 0.001f)
                        {
                            outward = Vector3.back;
                        }

                        outward.Normalize();
                        var foot = new Vector3(part.Center.x, centre.y, part.Center.z);
                        Add("cemetery-gate", foot + outward * 6f, foot + Vector3.up * 1.5f, false);
                        Add("cemetery-gate", foot - outward * 6f, foot + Vector3.up * 1.5f, false);
                    }
                    else if (part.Kind == CityCemeteryPartKind.Lodge && !lodgeFound)
                    {
                        lodgeFound = true;
                        Vector3 inward = groundsCentre - part.Center;
                        inward.y = 0f;
                        if (inward.sqrMagnitude < 0.001f)
                        {
                            inward = Vector3.forward;
                        }

                        inward.Normalize();
                        var foot = new Vector3(part.Center.x, centre.y, part.Center.z);
                        Add("cemetery-lodge", foot + inward * 7f, foot + Vector3.up * 1.2f, false);
                    }
                }

                if (!gateFound)
                {
                    Skip("cemetery gate arch");
                }

                if (!lodgeFound)
                {
                    Skip("cemetery lodge");
                }
            }

            private void Church()
            {
                CityChurchPlan church = world.ChurchPlan;
                if (church == null)
                {
                    Skip("church");
                }
                else
                {
                    Vector3 door = church.DoorGroundPosition;
                    Vector3 outward = church.EntranceOutwardDirection;
                    outward.y = 0f;
                    if (outward.sqrMagnitude < 0.001f)
                    {
                        outward = Vector3.back;
                    }

                    outward.Normalize();
                    Add("church", door + outward * 10f, door + Vector3.up * 2.5f, true);
                    Add("church", door + outward * 3f, door + Vector3.up * 1.5f, true);
                }

                CityChurchCourtyardPlan courtyard = world.ChurchCourtyardPlan;
                if (courtyard == null || courtyard.Grounds.width <= 0f)
                {
                    Skip("church courtyard");
                    return;
                }

                Rect grounds = courtyard.Grounds;
                var centre = new Vector3(grounds.center.x, 0f, grounds.center.y);
                Add(
                    "church-courtyard",
                    new Vector3(grounds.center.x, 0f, grounds.yMin + 1.5f),
                    centre + Vector3.up * 0.5f,
                    false);
                Add(
                    "church-courtyard",
                    new Vector3(grounds.center.x, 0f, grounds.yMax - 1.5f),
                    centre + Vector3.up * 0.5f,
                    false);
            }

            private void Stairs()
            {
                if (layout.ElevationPlan == null)
                {
                    Skip("elevation plan");
                    return;
                }

                IReadOnlyList<CityElevationStairDescriptor> stairs =
                    layout.ElevationPlan.SignatureStairs;
                if (stairs.Count == 0)
                {
                    Skip("signature stairs");
                    return;
                }

                for (int index = 0; index < stairs.Count; index++)
                {
                    CityElevationStairPlacement placement =
                        CityElevationStairPlacementPlanner.Create(layout, stairs[index]);
                    string slug = "stair-" + AuditInt(index);
                    Add(slug, placement.LowerApproachStart, placement.LowerApproachEnd + Vector3.up, true);
                    Add(slug, placement.LowerApproachEnd, placement.UpperApproachStart + Vector3.up * 0.5f, true);
                    Add(slug, placement.UpperApproachEnd, placement.UpperApproachStart + Vector3.up * 0.3f, true);
                }
            }

            private void FringeYards()
            {
                CityFringeYardPlan yards = world.FringeYardPlan;
                if (yards == null)
                {
                    Skip("fringe yards");
                    return;
                }

                for (int index = 0; index < yards.Yards.Count; index++)
                {
                    CityFringeYardDescriptor yard = yards.Yards[index];
                    CityOpenAreaAccessDescriptor access = yard.Access;
                    Vector3 into = access.OutwardNormal;
                    into.y = 0f;
                    if (into.sqrMagnitude < 0.001f)
                    {
                        into = Vector3.forward;
                    }

                    into.Normalize();
                    string slug = "fringe-yard-" + AuditInt(index);
                    Add(slug, access.Center - into * 5f, access.Center + into * 8f + Vector3.up * 0.5f, true);
                    Rect traversal = yard.TraversalBounds;
                    Add(
                        slug,
                        new Vector3(traversal.center.x, access.Center.y, traversal.center.y),
                        access.Center + Vector3.up,
                        false);
                }

                if (yards.HasTunnelForecourt)
                {
                    CityTunnelForecourtDescriptor forecourt = yards.TunnelForecourt;
                    Vector3 axis = forecourt.Axis;
                    axis.y = 0f;
                    if (axis.sqrMagnitude < 0.001f)
                    {
                        axis = Vector3.forward;
                    }

                    axis.Normalize();
                    Add("tunnel-forecourt", forecourt.StreetAnchor, forecourt.StreetAnchor + axis * 12f + Vector3.up, true);
                    Add("tunnel-forecourt", forecourt.StreetAnchor, forecourt.StreetAnchor - axis * 12f + Vector3.up, true);
                }
            }

            private void OpenAreaAccesses()
            {
                IReadOnlyList<CityOpenAreaAccessDescriptor> accesses = layout.OpenAreaAccesses;
                for (int index = 0; index < accesses.Count; index++)
                {
                    CityOpenAreaAccessDescriptor access = accesses[index];
                    // OutwardNormal points INTO the area.
                    Vector3 into = access.OutwardNormal;
                    into.y = 0f;
                    if (into.sqrMagnitude < 0.001f)
                    {
                        into = Vector3.forward;
                    }

                    into.Normalize();
                    Add(
                        "access-" + AuditSlug(access.Id),
                        access.Center - into * 4f,
                        access.Center + into * 8f + Vector3.up * 0.5f,
                        true);
                }
            }

            private void ArchShelter()
            {
                if (world.ArchShelterPlan == null || !world.ArchShelterPlan.IsEnabled ||
                    world.ArchShelter == null || world.ArchShelter.Root == null ||
                    !AuditRendererBounds(world.ArchShelter.Root, out Bounds bounds))
                {
                    Skip("arch shelter");
                    return;
                }

                Vector3 axis = bounds.extents.x >= bounds.extents.z ? Vector3.right : Vector3.forward;
                float extent = Mathf.Max(bounds.extents.x, bounds.extents.z);
                Vector3 centre = bounds.center;
                var foot = new Vector3(centre.x, bounds.min.y, centre.z);
                Add("arch-shelter", foot + axis * (extent + 4f), centre, false);
                Add("arch-shelter", foot - axis * (extent + 4f), centre, false);
            }

            private void LastRoute()
            {
                if (city.LastRouteCar == null)
                {
                    Skip("last route car");
                    return;
                }

                Vector3 position = city.LastRouteCar.transform.position;
                Add("last-route-car", position + Vector3.back * 7f, position + Vector3.up, true);
                Add("last-route-car", position + Vector3.right * 7f, position + Vector3.up, true);
            }

            private void EastExit()
            {
                GameObject exit = GameObject.Find(AuditEastExitRootName);
                if (exit == null || !AuditRendererBounds(exit, out Bounds bounds))
                {
                    Skip("east exit");
                    return;
                }

                float z = bounds.center.z;
                var start = new Vector3(bounds.min.x + 2f, bounds.min.y, z);
                var middle = new Vector3(bounds.center.x, bounds.min.y, z);
                var far = new Vector3(bounds.max.x, bounds.min.y + 1f, z);
                Add("east-exit", start, far, false);
                Add("east-exit", middle, far, false);
                Add("east-exit", middle, start + Vector3.up, false);
            }
        }

        private static AuditNamedShot AuditResolveNamedShot(
            CityLayout layout,
            CityMapCityTeleportGround ground,
            string slug,
            int index,
            Vector3 eyeGround,
            Vector3 target,
            bool trustHeight)
        {
            var shot = new AuditNamedShot { Slug = slug, Index = index, Target = target };
            var xz = new Vector2(eyeGround.x, eyeGround.z);
            if (ground.TryResolveStandingPosition(xz, out Vector3 stand) &&
                AuditPlanar(stand, eyeGround) <= 6f)
            {
                shot.Standing = true;
                shot.HasStand = true;
                shot.Stand = stand;
                shot.Eye = stand + Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset);
                return shot;
            }

            if (ground.TryClampArrival(eyeGround, out stand) &&
                AuditPlanar(stand, eyeGround) <= 6f)
            {
                shot.Standing = true;
                shot.HasStand = true;
                shot.Stand = stand;
                shot.Eye = stand + Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset);
                return shot;
            }

            float estimate = eyeGround.y;
            if (!trustHeight && AuditSampledGroundTop(layout, xz, out float sampled))
            {
                estimate = sampled;
            }

            float y;
            if (AuditRaycastGround(eyeGround.x, eyeGround.z, estimate + 3f, 12f, out y) ||
                AuditRaycastGround(eyeGround.x, eyeGround.z, estimate + 80f, 200f, out y))
            {
                shot.Eye = new Vector3(eyeGround.x, y + EyeHeight, eyeGround.z);
            }
            else
            {
                shot.Eye = new Vector3(eyeGround.x, estimate + EyeHeight, eyeGround.z);
            }

            shot.Standing = false;
            // The hero still has to be somewhere near for the streamed
            // presentation (port crew, lamps, fog) to be there at all.
            if (ground.TryResolveStandingPosition(xz, out stand) &&
                AuditPlanar(stand, eyeGround) <= 40f)
            {
                shot.HasStand = true;
                shot.Stand = stand;
            }

            return shot;
        }

        // ------------------------------------------------------------
        // Pass C - findings
        // ------------------------------------------------------------

        [Serializable]
        private sealed class AuditFinding
        {
            public string id;
            public string category;
            public string subtype;
            public string tag;
            public string note;
            public string area;
            public string dir;
            public float x;
            public float y;
            public float z;
            public string[] objects;
            [NonSerialized] public bool hasY;
            [NonSerialized] public float metric;
            [NonSerialized] public bool hasMetric;
            [NonSerialized] public string source;
        }

        private IEnumerator AuditFindingsPass(
            CityGameRoot city,
            AuditRig rig,
            AuditReport report)
        {
            CityLayout layout = city.Layout;
            CityWorldResult world = city.World;
            string folder = report.Folder("findings");
            const string IndexFile = "findings/findings-index.ndjson";
            const string FlickerFile = "findings/findings-flicker.ndjson";
            report.Truncate(IndexFile);
            report.Truncate(FlickerFile);
            string input = AuditEnv("CITY_AUDIT_FINDINGS") ?? Path.Combine(
                Directory.GetCurrentDirectory(),
                "TestResults",
                "CityAudit",
                "findings.ndjson");
            var findings = new List<AuditFinding>();
            AuditLoadFindings(input, "probe", findings);
            AuditLoadFindings(
                Path.Combine(report.Root, "topdown", "topdown-findings.ndjson"),
                "topdown",
                findings);
            findings.Sort(AuditCompareFindings);
            int cap = Mathf.Max(0, AuditEnvInt("CITY_AUDIT_MAX_FINDINGS", 300));
            int dropped = Mathf.Max(0, findings.Count - cap);
            if (findings.Count > cap)
            {
                findings.RemoveRange(cap, findings.Count - cap);
            }

            var perCategory = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < findings.Count; index++)
            {
                string category = findings[index].category;
                perCategory.TryGetValue(category, out int count);
                perCategory[category] = count + 1;
            }

            var summary = new StringBuilder();
            foreach (KeyValuePair<string, int> entry in perCategory)
            {
                summary.Append(entry.Key).Append('=').Append(AuditInt(entry.Value)).Append(' ');
            }

            Debug.Log(
                AuditLogPrefix +
                $"findings: selected={findings.Count} dropped={dropped} " +
                $"cap={cap} input={input} per category: {summary}");

            var ground = new CityMapCityTeleportGround(layout);
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            GameObject overlay = null;
            Texture2D pair0 = null;
            Texture2D pair1 = null;
            Texture2D diff = null;
            try
            {
                for (int index = 0; index < findings.Count; index++)
                {
                    AuditFinding finding = findings[index];
                    var p = new Vector3(
                        finding.x,
                        finding.hasY ? finding.y : AuditGroundY(layout, finding.x, finding.z),
                        finding.z);
                    string category = finding.category;
                    GameObject marker = AuditCreateMarker(p, AuditMarkerMaterial(materials, category));
                    string stem =
                        $"f_{AuditSlug(finding.id)}_{AuditSlug(category)}" +
                        $"_x{AuditSigned(p.x)}_z{AuditSigned(p.z)}";
                    try
                    {
                        Vector3 eye = AuditChooseEye(
                            ground,
                            p,
                            out bool standing,
                            out Vector3 stand,
                            out bool hasStand);
                        if (hasStand)
                        {
                            city.Player.Motor.Teleport(stand);
                        }

                        yield return null;
                        Vector3 aim = p + Vector3.up * 0.3f;
                        AuditEyeFrame(rig, report, folder, IndexFile, finding, p, eye, aim, stem + "_a.jpg", standing);
                        Vector3 eyeB = AuditRotateEye(p, eye);
                        AuditEyeFrame(rig, report, folder, IndexFile, finding, p, eyeB, aim, stem + "_b.jpg", false);

                        if (category == "A" || category == "A2" || category == "A3" || category == "B")
                        {
                            if (overlay == null)
                            {
                                overlay = AuditBuildMaskOverlay(layout, world);
                            }

                            overlay.SetActive(true);
                            Vector3 topEye = p + Vector3.up * 8f;
                            rig.SetPerspective(topEye, p, AuditFindingFieldOfView);
                            Texture2D top = rig.Render(Width, Height);
                            overlay.SetActive(false);
                            bool blank = IsBlank(top);
                            string file = stem + "_top.png";
                            File.WriteAllBytes(Path.Combine(folder, file), top.EncodeToPNG());
                            AuditIndexFrame(report, IndexFile, finding, file, topEye, 0, "top", false, blank, false);
                        }

                        if (category == "C")
                        {
                            AuditFlicker(rig, report, folder, IndexFile, FlickerFile, finding, eye, aim, stem, 1, 0f, ref pair0, ref pair1, ref diff);
                            AuditFlicker(rig, report, folder, IndexFile, FlickerFile, finding, eye, aim, stem, 2, AuditFlickerStep, ref pair0, ref pair1, ref diff);
                        }

                        report.Findings++;
                        if (report.Findings % 25 == 0)
                        {
                            Debug.Log(AuditLogPrefix + $"findings: {report.Findings}/{findings.Count}");
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(marker);
                    }
                }
            }
            finally
            {
                foreach (KeyValuePair<string, Material> entry in materials)
                {
                    Object.DestroyImmediate(entry.Value);
                }

                AuditDestroyOverlay(overlay);
                if (pair0 != null) Object.DestroyImmediate(pair0);
                if (pair1 != null) Object.DestroyImmediate(pair1);
                if (diff != null) Object.DestroyImmediate(diff);
                report.Flush();
            }
        }

        private static void AuditLoadFindings(
            string path,
            string source,
            List<AuditFinding> into)
        {
            if (!File.Exists(path))
            {
                Debug.Log(AuditLogPrefix + $"no {source} findings at {path}");
                return;
            }

            int accepted = 0;
            int rejected = 0;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                AuditFinding finding;
                try
                {
                    finding = JsonUtility.FromJson<AuditFinding>(line);
                }
                catch (Exception)
                {
                    finding = null;
                }

                if (finding == null)
                {
                    rejected++;
                    continue;
                }

                finding.hasY = AuditHasYPattern.IsMatch(line);
                Match metric = AuditMetricPattern.Match(line);
                if (metric.Success &&
                    float.TryParse(
                        metric.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float value))
                {
                    finding.metric = value;
                    finding.hasMetric = true;
                }

                if (string.IsNullOrEmpty(finding.id))
                {
                    finding.id = source + "-" + into.Count.ToString("0000", CultureInfo.InvariantCulture);
                }

                if (string.IsNullOrEmpty(finding.category))
                {
                    finding.category = "X";
                }

                if (finding.tag == null)
                {
                    finding.tag = string.Empty;
                }

                finding.source = source;
                into.Add(finding);
                accepted++;
            }

            Debug.Log(AuditLogPrefix + $"{source} findings: accepted={accepted} rejected={rejected} from {path}");
        }

        private static int AuditTagRank(string tag)
        {
            switch (tag)
            {
                case "new": return 0;
                case "suspect": return 1;
                case "topdown": return 2;
                case "by_design": return 3;
                default: return 4;
            }
        }

        private static int AuditCategoryRank(string category)
        {
            switch (category)
            {
                case "A": return 0;
                case "A2": return 1;
                case "A3": return 2;
                case "B": return 3;
                case "C": return 4;
                case "D": return 5;
                case "E": return 6;
                default: return 7;
            }
        }

        private static int AuditCompareFindings(AuditFinding a, AuditFinding b)
        {
            int order = AuditTagRank(a.tag).CompareTo(AuditTagRank(b.tag));
            if (order != 0)
            {
                return order;
            }

            order = AuditCategoryRank(a.category).CompareTo(AuditCategoryRank(b.category));
            if (order != 0)
            {
                return order;
            }

            if (a.hasMetric != b.hasMetric)
            {
                return a.hasMetric ? -1 : 1;
            }

            if (a.hasMetric)
            {
                order = b.metric.CompareTo(a.metric);
                if (order != 0)
                {
                    return order;
                }
            }

            return string.CompareOrdinal(a.id, b.id);
        }

        /// <summary>
        /// Eight compass eyes five metres out at eye height, snapped onto a
        /// legal standing point when one lies within three metres; the one
        /// with the clearest line to the finding wins, and among clear
        /// lines a legal standing point wins.
        /// </summary>
        private static Vector3 AuditChooseEye(
            CityMapCityTeleportGround ground,
            Vector3 p,
            out bool standing,
            out Vector3 stand,
            out bool hasStand)
        {
            Vector3 aim = p + Vector3.up * 0.3f;
            Vector3 bestEye = p + Vector3.back * 5f + Vector3.up * EyeHeight;
            float bestScore = float.NegativeInfinity;
            standing = false;
            stand = default;
            hasStand = false;
            for (int k = 0; k < 8; k++)
            {
                Vector3 direction = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                Vector3 candidate = p + direction * 5f;
                float groundY = AuditRaycastGround(candidate.x, candidate.z, p.y + 30f, 60f, out float hitY)
                    ? hitY
                    : p.y;
                Vector3 eye = new Vector3(candidate.x, groundY + EyeHeight, candidate.z);
                bool legal = false;
                Vector3 legalStand = default;
                if (ground.TryResolveStandingPosition(new Vector2(candidate.x, candidate.z), out Vector3 resolved) &&
                    AuditPlanar(resolved, candidate) <= 3f)
                {
                    legal = true;
                    legalStand = resolved;
                    eye = resolved + Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset);
                }

                float distance = Vector3.Distance(eye, aim);
                float ratio = 1f;
                if (Physics.Linecast(eye, aim, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    ratio = distance > 0.001f ? hit.distance / distance : 0f;
                }

                float clear = ratio >= 0.9f ? 1f : ratio;
                float score = clear + (legal ? 0.05f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEye = eye;
                    standing = legal;
                    if (legal)
                    {
                        stand = legalStand;
                        hasStand = true;
                    }
                    else
                    {
                        hasStand = false;
                    }
                }
            }

            if (!hasStand &&
                ground.TryResolveStandingPosition(new Vector2(bestEye.x, bestEye.z), out Vector3 nearest) &&
                AuditPlanar(nearest, bestEye) <= 40f)
            {
                stand = nearest;
                hasStand = true;
            }

            return bestEye;
        }

        private static Vector3 AuditRotateEye(Vector3 p, Vector3 eye)
        {
            Vector3 offset = eye - p;
            float height = offset.y;
            offset.y = 0f;
            Vector3 rotated = Quaternion.Euler(0f, 90f, 0f) * offset;
            Vector3 candidate = p + rotated;
            float groundY = AuditRaycastGround(candidate.x, candidate.z, p.y + 30f, 60f, out float hitY)
                ? hitY + EyeHeight
                : p.y + height;
            return new Vector3(candidate.x, groundY, candidate.z);
        }

        private static void AuditEyeFrame(
            AuditRig rig,
            AuditReport report,
            string folder,
            string indexFile,
            AuditFinding finding,
            Vector3 p,
            Vector3 eye,
            Vector3 aim,
            string file,
            bool standing)
        {
            rig.SetPerspective(eye, aim, AuditFindingFieldOfView);
            Texture2D frame = rig.Render(Width, Height);
            bool blank = IsBlank(frame);
            bool retried = false;
            Vector3 used = eye;
            if (blank)
            {
                Debug.Log(AuditLogPrefix + $"{finding.id} {file}: blank frame (camera inside geometry); retrying from the top pose");
                retried = true;
                used = p + Vector3.up * 8f;
                rig.SetPerspective(used, p, AuditFindingFieldOfView);
                frame = rig.Render(Width, Height);
                blank = IsBlank(frame);
            }

            File.WriteAllBytes(Path.Combine(folder, file), frame.EncodeToJPG(AuditJpegQuality));
            AuditIndexFrame(report, indexFile, finding, file, used, AuditYaw(used, p), "eye", standing && !retried, blank, retried);
        }

        private static void AuditIndexFrame(
            AuditReport report,
            string indexFile,
            AuditFinding finding,
            string file,
            Vector3 eye,
            int yaw,
            string kind,
            bool standing,
            bool blank,
            bool retried)
        {
            if (blank)
            {
                report.Blank++;
            }

            report.Line(
                indexFile,
                new AuditJson()
                    .Add("id", finding.id)
                    .Add("category", finding.category)
                    .Add("file", file)
                    .Add("x", eye.x)
                    .Add("z", eye.z)
                    .Add("y", eye.y)
                    .Add("yaw", yaw)
                    .Add("kind", kind)
                    .Add("standing", standing)
                    .Add("blank", blank)
                    .Add("retried", retried)
                    .Add("tag", finding.tag)
                    .Add("source", finding.source)
                    .ToString());
        }

        /// <summary>
        /// Two renders in the same coroutine step, the second from a camera
        /// moved two millimetres to the right with its rotation untouched,
        /// so <c>_Time</c> and every animated uniform match and the only
        /// thing that can differ is depth precision - which is what
        /// z-fighting is. Pixels whose largest channel moved by more than
        /// the threshold are painted red over the half-bright first frame.
        /// </summary>
        private static void AuditFlicker(
            AuditRig rig,
            AuditReport report,
            string folder,
            string indexFile,
            string flickerFile,
            AuditFinding finding,
            Vector3 eye,
            Vector3 aim,
            string stem,
            int pair,
            float forwardOffset,
            ref Texture2D frame0,
            ref Texture2D frame1,
            ref Texture2D diff)
        {
            if (frame0 == null) frame0 = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            if (frame1 == null) frame1 = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            if (diff == null) diff = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            rig.SetPerspective(eye, aim, AuditFindingFieldOfView);
            Transform cameraTransform = rig.Camera.transform;
            if (forwardOffset != 0f)
            {
                rig.Translate(cameraTransform.forward * forwardOffset);
            }

            Vector3 origin = cameraTransform.position;
            Quaternion rotation = cameraTransform.rotation;
            rig.RenderInto(frame0);
            rig.Translate(cameraTransform.right * AuditFlickerStep);
            rig.RenderInto(frame1);
            cameraTransform.SetPositionAndRotation(origin, rotation);

            Color32[] a = frame0.GetPixels32();
            Color32[] b = frame1.GetPixels32();
            var painted = new Color32[a.Length];
            var changedGrid = new byte[a.Length];
            var red = new Color32(255, 0, 0, 255);
            int changed = 0;
            for (int index = 0; index < a.Length; index++)
            {
                int delta = Mathf.Max(
                    Mathf.Abs(a[index].r - b[index].r),
                    Mathf.Max(
                        Mathf.Abs(a[index].g - b[index].g),
                        Mathf.Abs(a[index].b - b[index].b)));
                if (delta > AuditFlickerThreshold)
                {
                    painted[index] = red;
                    changedGrid[index] = 1;
                    changed++;
                }
                else
                {
                    painted[index] = new Color32(
                        (byte)(a[index].r / 2),
                        (byte)(a[index].g / 2),
                        (byte)(a[index].b / 2),
                        255);
                }
            }

            diff.SetPixels32(painted);
            diff.Apply();

            string first = pair == 1 ? "_p0.png" : "_p2.png";
            string second = pair == 1 ? "_p1.png" : "_p3.png";
            string diffName = pair == 1 ? "_diff.png" : "_diff2.png";
            File.WriteAllBytes(Path.Combine(folder, stem + first), frame0.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, stem + second), frame1.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, stem + diffName), diff.EncodeToPNG());
            int yaw = AuditYaw(origin, aim);
            bool blank = IsBlank(frame0);
            AuditIndexFrame(report, indexFile, finding, stem + first, origin, yaw, "flicker", false, blank, false);
            AuditIndexFrame(report, indexFile, finding, stem + second, origin, yaw, "flicker", false, IsBlank(frame1), false);
            AuditIndexFrame(report, indexFile, finding, stem + diffName, origin, yaw, "diff", false, changed == 0, false);

            AuditJson line = new AuditJson()
                .Add("id", finding.id)
                .Add("pair", pair)
                .Add("file", stem + diffName)
                .Add("changedPixels", changed)
                .Add("changedFraction", changed / (float)a.Length);
            if (changed > 0)
            {
                List<AuditCluster> clusters = AuditFindClusters(changedGrid, Width, Height);
                AuditCluster largest = clusters[0];
                for (int index = 1; index < clusters.Count; index++)
                {
                    if (clusters[index].Cells > largest.Cells)
                    {
                        largest = clusters[index];
                    }
                }

                float cx = (float)largest.SumX / largest.Cells;
                float cy = (float)largest.SumY / largest.Cells;
                line.Add("clusterPixels", largest.Cells)
                    .Raw(
                        "clusterBBoxFromTop",
                        "[" + AuditInt(largest.MinX) + "," + AuditInt(Height - 1 - largest.MaxY) +
                        "," + AuditInt(largest.MaxX) + "," + AuditInt(Height - 1 - largest.MinY) + "]")
                    .Raw(
                        "clusterCentroidFromTop",
                        "[" + AuditNumber(cx) + "," + AuditNumber(Height - 1 - cy) + "]");
                Ray ray = rig.Camera.ScreenPointToRay(new Vector3(cx, cy, 0f));
                if (Physics.Raycast(ray, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                {
                    line.Add("hit", hit.point)
                        .Add("hitObject", hit.collider != null ? hit.collider.name : string.Empty)
                        .Add("hitDistance", hit.distance);
                }
                else
                {
                    line.Raw("hit", "null");
                }
            }

            report.Line(flickerFile, line.ToString());
        }

        private static GameObject AuditCreateMarker(Vector3 position, Material material)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "City Audit Marker";
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            marker.transform.position = position + Vector3.up * 0.15f;
            marker.transform.localScale = Vector3.one * 0.3f;
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return marker;
        }

        private static Material AuditMarkerMaterial(
            Dictionary<string, Material> materials,
            string category)
        {
            if (materials.TryGetValue(category, out Material material))
            {
                return material;
            }

            Color colour;
            switch (category)
            {
                case "A":
                case "A2":
                case "A3":
                    colour = Color.yellow;
                    break;
                case "B":
                    colour = Color.cyan;
                    break;
                case "C":
                    colour = Color.red;
                    break;
                case "D":
                    colour = new Color(1f, 0.55f, 0f);
                    break;
                case "E":
                    colour = Color.white;
                    break;
                default:
                    colour = Color.magenta;
                    break;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            material.name = "City Audit Marker " + category;
            material.SetColor("_BaseColor", colour);
            material.color = colour;
            materials[category] = material;
            return material;
        }

        // ------------------------------------------------------------
        // The rig: every scene change made for a clean frame, undone in
        // reverse order when disposed.
        // ------------------------------------------------------------

        private sealed class AuditRig : IDisposable
        {
            private sealed class CompositeState
            {
                public Ps1CompositeRendererFeature Feature;
                public Ps1PresentationSettings Settings;
                public Material Material;
                public Ps1PresentationSettings Clone;
            }

            private readonly CityGameRoot city;
            private readonly Camera camera;
            private readonly PlayerCameraFollow follow;
            private readonly Light sun;
            private readonly UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData;
            private readonly UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset pipeline;
            private readonly List<Renderer> hiddenHero = new List<Renderer>();
            private readonly List<CompositeState> composites = new List<CompositeState>();
            private readonly Dictionary<long, RenderTexture> renderTextures =
                new Dictionary<long, RenderTexture>();
            private readonly Dictionary<long, Texture2D> frames =
                new Dictionary<long, Texture2D>();
            private readonly Ps1VertexJitterExclusion addedExclusion;

            private readonly bool weatherEnabled;
            private readonly bool lightningEnabled;
            private readonly bool dayNightEnabled;
            private readonly bool cloudsVisible;
            private readonly bool fogFieldActive;
            private readonly bool pedestriansEnabled;
            private readonly bool busEnabled;
            private readonly bool smokersEnabled;
            private readonly bool followEnabled;
            private readonly bool postProcessing;
            private readonly float savedCaptureDelta;
            private readonly RenderTexture savedTarget;
            private readonly Vector3 savedPosition;
            private readonly Quaternion savedRotation;
            private readonly float savedFieldOfView;
            private readonly bool savedOrthographic;
            private readonly float savedOrthographicSize;
            private readonly float savedNear;
            private readonly float savedFar;
            private readonly CameraClearFlags savedClearFlags;
            private readonly Color savedBackground;
            private readonly bool savedFog;
            private readonly Color savedFogColor;
            private readonly FogMode savedFogMode;
            private readonly float savedFogDensity;
            private readonly Quaternion savedSunRotation;
            private readonly float savedShadowDistance;
            private bool disposed;

            public static AuditRig Borrow(CityGameRoot city, bool disableComposite)
            {
                return new AuditRig(city, disableComposite);
            }

            private AuditRig(CityGameRoot city, bool disableComposite)
            {
                this.city = city ?? throw new ArgumentNullException(nameof(city));
                camera = city.Camera != null ? city.Camera : Camera.main;
                if (camera == null)
                {
                    throw new InvalidOperationException("The City has no camera to borrow.");
                }

                // 1. The hero: no input, no body in the frame. PlayerRuntime
                //    is a struct; its members are what can be null.
                if (city.Player.Motor != null)
                {
                    city.Player.Motor.SetInputEnabled(false);
                }

                if (city.Player.GameObject != null)
                {
                    foreach (Renderer renderer in
                             city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.enabled)
                        {
                            renderer.enabled = false;
                            hiddenHero.Add(renderer);
                        }
                    }
                }

                // 2. Dry weather, held.
                if (city.Weather != null)
                {
                    weatherEnabled = city.Weather.enabled;
                    city.Weather.enabled = false;
                }

                if (city.Rain != null)
                {
                    city.Rain.SetIntensity(0f);
                }

                CityWetSurfaceRegistry.SetImmediate(0f);
                CityWaterResources.SetRainIntensity(0f);
                if (city.Lightning != null)
                {
                    lightningEnabled = city.Lightning.enabled;
                    city.Lightning.enabled = false;
                }

                // 3. Noon, pinned: applied once, then the clock-driven
                //    controller is stopped so the sun holds still.
                if (city.DayNight != null)
                {
                    city.DayNight.ApplyCurrentTime(true);
                    dayNightEnabled = city.DayNight.enabled;
                    city.DayNight.enabled = false;
                }

                // 4. Atmosphere off the frame.
                if (city.Clouds != null)
                {
                    cloudsVisible = city.Clouds.IsVisible;
                    city.Clouds.SetVisible(false);
                }

                if (city.Night != null && city.Night.FogField != null)
                {
                    fogFieldActive = city.Night.FogField.gameObject.activeSelf;
                    city.Night.FogField.gameObject.SetActive(false);
                }

                // 5. Determinism: the crowds, the bus and the smokers are
                //    released by their own OnDisable; time steps evenly.
                if (city.Pedestrians != null)
                {
                    pedestriansEnabled = city.Pedestrians.enabled;
                    city.Pedestrians.enabled = false;
                }

                if (city.Bus != null)
                {
                    busEnabled = city.Bus.enabled;
                    city.Bus.enabled = false;
                }

                if (city.BalconySmokers != null)
                {
                    smokersEnabled = city.BalconySmokers.enabled;
                    city.BalconySmokers.enabled = false;
                }

                savedCaptureDelta = Time.captureDeltaTime;
                Time.captureDeltaTime = 1f / 30f;

                // 6. The camera: its follow script rewrites projection and
                //    field of view every LateUpdate, so it is stopped.
                follow = camera.GetComponent<PlayerCameraFollow>();
                if (follow != null)
                {
                    followEnabled = follow.enabled;
                    follow.enabled = false;
                }

                savedTarget = camera.targetTexture;
                savedPosition = camera.transform.position;
                savedRotation = camera.transform.rotation;
                savedFieldOfView = camera.fieldOfView;
                savedOrthographic = camera.orthographic;
                savedOrthographicSize = camera.orthographicSize;
                savedNear = camera.nearClipPlane;
                savedFar = camera.farClipPlane;
                savedClearFlags = camera.clearFlags;
                savedBackground = camera.backgroundColor;
                savedFog = RenderSettings.fog;
                savedFogColor = RenderSettings.fogColor;
                savedFogMode = RenderSettings.fogMode;
                savedFogDensity = RenderSettings.fogDensity;
                sun = RenderSettings.sun;
                if (sun != null)
                {
                    SavedSunShadows = sun.shadows;
                    savedSunRotation = sun.transform.rotation;
                }

                cameraData = UnityEngine.Rendering.Universal.CameraExtensions
                    .GetUniversalAdditionalCameraData(camera);
                if (cameraData != null)
                {
                    postProcessing = cameraData.renderPostProcessing;
                    cameraData.renderPostProcessing = false;
                }

                pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                    as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (pipeline != null)
                {
                    savedShadowDistance = pipeline.shadowDistance;
                }

                // 7. The PS1 composite: the vertex snap is a screen-space
                //    jitter that would drown a two-millimetre flicker diff,
                //    and the print holds a picture across frames.
                if (disableComposite)
                {
                    if (camera.GetComponent<Ps1VertexJitterExclusion>() == null)
                    {
                        addedExclusion = camera.gameObject.AddComponent<Ps1VertexJitterExclusion>();
                    }

                    FieldInfo field = typeof(Ps1PresentationSettings).GetField(
                        "effectEnabled",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    Ps1CompositeRendererFeature[] features =
                        Resources.FindObjectsOfTypeAll<Ps1CompositeRendererFeature>();
                    for (int index = 0; index < features.Length; index++)
                    {
                        Ps1CompositeRendererFeature feature = features[index];
                        Ps1PresentationSettings settings = feature != null ? feature.PresentationSettings : null;
                        Material material = feature != null ? feature.CompositeMaterial : null;
                        if (feature == null || settings == null || material == null || field == null)
                        {
                            Debug.Log(AuditLogPrefix + "composite feature left on: profile, material or field missing.");
                            continue;
                        }

                        Ps1PresentationSettings clone = Object.Instantiate(settings);
                        clone.name = settings.name + " (audit, effect off)";
                        field.SetValue(clone, false);
                        feature.SetConfiguration(clone, material);
                        composites.Add(new CompositeState
                        {
                            Feature = feature,
                            Settings = settings,
                            Material = material,
                            Clone = clone
                        });
                    }

                    Debug.Log(AuditLogPrefix + $"composite bypassed on {composites.Count} feature(s)");
                }
            }

            public Camera Camera => camera;
            public LightShadows SavedSunShadows { get; }

            /// <summary>
            /// Optional: no fog and a long far plane for perspective frames
            /// (<c>CITY_AUDIT_CLEAR_AIR=1</c>). Off by default so the frames
            /// carry the production air.
            /// </summary>
            public bool ClearAir { get; set; }

            public void SetPerspective(Vector3 eye, Vector3 target, float fieldOfView)
            {
                camera.orthographic = false;
                camera.fieldOfView = fieldOfView;
                camera.nearClipPlane = savedNear;
                camera.farClipPlane = ClearAir ? Mathf.Max(savedFar, 300f) : savedFar;
                camera.clearFlags = savedClearFlags;
                camera.backgroundColor = savedBackground;
                RenderSettings.fog = savedFog && !ClearAir;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogDensity = savedFogDensity;
                if (sun != null)
                {
                    sun.shadows = SavedSunShadows;
                }

                Pose(eye, target);
            }

            public void Pose(Vector3 eye, Vector3 target)
            {
                Vector3 direction = target - eye;
                if (direction.sqrMagnitude < 0.000001f)
                {
                    direction = Vector3.forward;
                }

                direction.Normalize();
                Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.999f
                    ? Vector3.forward
                    : Vector3.up;
                camera.transform.SetPositionAndRotation(
                    eye,
                    Quaternion.LookRotation(direction, up));
            }

            public void Translate(Vector3 delta)
            {
                camera.transform.position += delta;
            }

            public void SetTopDown(
                Vector2 centreXZ,
                float halfSize,
                float cameraY,
                Color clear,
                bool fog)
            {
                camera.orthographic = true;
                camera.orthographicSize = halfSize;
                camera.nearClipPlane = 1f;
                camera.farClipPlane = 600f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = clear;
                RenderSettings.fog = fog;
                camera.transform.SetPositionAndRotation(
                    new Vector3(centreXZ.x, cameraY, centreXZ.y),
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
            }

            public void SetSunShadows(LightShadows shadows)
            {
                if (sun != null)
                {
                    sun.shadows = shadows;
                }
            }

            public void SetSunRotation(Quaternion rotation)
            {
                if (sun != null)
                {
                    sun.transform.rotation = rotation;
                }
            }

            public void RestoreSunRotation()
            {
                if (sun != null)
                {
                    sun.transform.rotation = savedSunRotation;
                }
            }

            public void SetShadowDistance(float distance)
            {
                if (pipeline != null)
                {
                    pipeline.shadowDistance = distance;
                }
            }

            public void RestoreShadowDistance()
            {
                if (pipeline != null)
                {
                    pipeline.shadowDistance = savedShadowDistance;
                }
            }

            public void WarmUp()
            {
                Render(Width, Height);
            }

            /// <summary>
            /// Renders into the cached frame for this size. The texture is
            /// the rig's; read or encode it before the next render.
            /// </summary>
            public Texture2D Render(int width, int height)
            {
                long key = ((long)width << 32) | (uint)height;
                if (!frames.TryGetValue(key, out Texture2D frame))
                {
                    frame = new Texture2D(width, height, TextureFormat.RGB24, false);
                    frames[key] = frame;
                }

                RenderInto(frame);
                return frame;
            }

            public void RenderInto(Texture2D frame)
            {
                int width = frame.width;
                int height = frame.height;
                long key = ((long)width << 32) | (uint)height;
                if (!renderTextures.TryGetValue(key, out RenderTexture target))
                {
                    target = new RenderTexture(width, height, 24);
                    target.name = "City Audit " + width + "x" + height;
                    renderTextures[key] = target;
                }

                // The target stays assigned between renders so the camera's
                // pixel rect - and ScreenPointToRay - describe the frame.
                camera.targetTexture = target;
                camera.Render();
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                frame.Apply();
                RenderTexture.active = previousActive;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Step(() => RuntimeSceneSetup.ApplyCityExteriorVisibility(camera));
                Step(() =>
                {
                    for (int index = composites.Count - 1; index >= 0; index--)
                    {
                        CompositeState state = composites[index];
                        if (state.Feature != null)
                        {
                            state.Feature.SetConfiguration(state.Settings, state.Material);
                        }

                        if (state.Clone != null)
                        {
                            Object.DestroyImmediate(state.Clone);
                        }
                    }

                    composites.Clear();
                });
                Step(() =>
                {
                    if (addedExclusion != null)
                    {
                        Object.DestroyImmediate(addedExclusion);
                    }
                });
                Step(() =>
                {
                    if (pipeline != null)
                    {
                        pipeline.shadowDistance = savedShadowDistance;
                    }

                    if (cameraData != null)
                    {
                        cameraData.renderPostProcessing = postProcessing;
                    }

                    if (sun != null)
                    {
                        sun.shadows = SavedSunShadows;
                        sun.transform.rotation = savedSunRotation;
                    }

                    RenderSettings.fog = savedFog;
                    RenderSettings.fogColor = savedFogColor;
                    RenderSettings.fogMode = savedFogMode;
                    RenderSettings.fogDensity = savedFogDensity;
                    camera.targetTexture = savedTarget;
                    camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                    camera.fieldOfView = savedFieldOfView;
                    camera.orthographic = savedOrthographic;
                    camera.orthographicSize = savedOrthographicSize;
                    camera.nearClipPlane = savedNear;
                    camera.farClipPlane = savedFar;
                    camera.clearFlags = savedClearFlags;
                    camera.backgroundColor = savedBackground;
                    if (follow != null)
                    {
                        follow.enabled = followEnabled;
                    }
                });
                Step(() =>
                {
                    Time.captureDeltaTime = savedCaptureDelta;
                    if (city.BalconySmokers != null) city.BalconySmokers.enabled = smokersEnabled;
                    if (city.Bus != null) city.Bus.enabled = busEnabled;
                    if (city.Pedestrians != null) city.Pedestrians.enabled = pedestriansEnabled;
                });
                Step(() =>
                {
                    if (city.Night != null && city.Night.FogField != null)
                    {
                        city.Night.FogField.gameObject.SetActive(fogFieldActive);
                    }

                    if (city.Clouds != null)
                    {
                        city.Clouds.SetVisible(cloudsVisible);
                    }
                });
                Step(() =>
                {
                    if (city.DayNight != null)
                    {
                        city.DayNight.enabled = dayNightEnabled;
                        city.DayNight.ApplyCurrentTime(true);
                    }
                });
                Step(() =>
                {
                    if (city.Lightning != null) city.Lightning.enabled = lightningEnabled;
                    if (city.Weather != null) city.Weather.enabled = weatherEnabled;
                });
                Step(() =>
                {
                    foreach (Renderer renderer in hiddenHero)
                    {
                        if (renderer != null)
                        {
                            renderer.enabled = true;
                        }
                    }

                    hiddenHero.Clear();
                    if (city.Player.Motor != null)
                    {
                        city.Player.Motor.SetInputEnabled(true);
                    }
                });
                Step(() =>
                {
                    foreach (KeyValuePair<long, Texture2D> entry in frames)
                    {
                        if (entry.Value != null)
                        {
                            Object.DestroyImmediate(entry.Value);
                        }
                    }

                    frames.Clear();
                    foreach (KeyValuePair<long, RenderTexture> entry in renderTextures)
                    {
                        if (entry.Value != null)
                        {
                            entry.Value.Release();
                            Object.DestroyImmediate(entry.Value);
                        }
                    }

                    renderTextures.Clear();
                });
            }

            private static void Step(Action restore)
            {
                try
                {
                    restore();
                }
                catch (Exception error)
                {
                    Debug.LogWarning(AuditLogPrefix + "restore step failed: " + error);
                }
            }
        }

        // ------------------------------------------------------------
        // Report: counters and NDJSON index files, flushed in finally.
        // ------------------------------------------------------------

        private sealed class AuditReport
        {
            private const int FlushThreshold = 32 * 1024;

            private readonly Dictionary<string, StringBuilder> pending =
                new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            private readonly HashSet<string> opened =
                new HashSet<string>(StringComparer.Ordinal);

            public AuditReport(string root)
            {
                Root = root;
                Directory.CreateDirectory(root);
            }

            public string Root { get; }
            public int Tiles;
            public int Stations;
            public int Findings;
            public int Blank;
            public int LatticeFrames;
            public int LatticeBlank;

            public string Folder(string name)
            {
                string path = Path.Combine(Root, name);
                Directory.CreateDirectory(path);
                return path;
            }

            /// <summary>Starts a file afresh, so a re-run never appends
            /// to the previous run's index.</summary>
            public void Truncate(string relativeFile)
            {
                string path = Path.Combine(Root, relativeFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Empty);
                opened.Add(relativeFile);
            }

            public void Line(string relativeFile, string json)
            {
                if (!pending.TryGetValue(relativeFile, out StringBuilder buffer))
                {
                    buffer = new StringBuilder();
                    pending[relativeFile] = buffer;
                }

                buffer.Append(json).Append('\n');
                if (buffer.Length > FlushThreshold)
                {
                    FlushOne(relativeFile, buffer);
                }
            }

            public void Flush()
            {
                foreach (KeyValuePair<string, StringBuilder> entry in pending)
                {
                    FlushOne(entry.Key, entry.Value);
                }
            }

            private void FlushOne(string relativeFile, StringBuilder buffer)
            {
                if (buffer.Length == 0)
                {
                    return;
                }

                string path = Path.Combine(Root, relativeFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (opened.Add(relativeFile))
                {
                    File.WriteAllText(path, buffer.ToString());
                }
                else
                {
                    File.AppendAllText(path, buffer.ToString());
                }

                buffer.Clear();
            }
        }

        // ------------------------------------------------------------
        // Small helpers. Every number goes through the invariant culture.
        // ------------------------------------------------------------

        private sealed class AuditJson
        {
            private readonly StringBuilder text = new StringBuilder(192);
            private bool first = true;

            public AuditJson()
            {
                text.Append('{');
            }

            public AuditJson Add(string key, string value)
            {
                Key(key);
                if (value == null)
                {
                    text.Append("null");
                }
                else
                {
                    text.Append('"').Append(Escape(value)).Append('"');
                }

                return this;
            }

            public AuditJson Add(string key, float value)
            {
                Key(key);
                text.Append(AuditNumber(value));
                return this;
            }

            public AuditJson Add(string key, int value)
            {
                Key(key);
                text.Append(AuditInt(value));
                return this;
            }

            public AuditJson Add(string key, bool value)
            {
                Key(key);
                text.Append(value ? "true" : "false");
                return this;
            }

            public AuditJson Add(string key, Vector3 value)
            {
                return Raw(
                    key,
                    "[" + AuditNumber(value.x) + "," + AuditNumber(value.y) + "," + AuditNumber(value.z) + "]");
            }

            public AuditJson Raw(string key, string raw)
            {
                Key(key);
                text.Append(raw);
                return this;
            }

            public override string ToString()
            {
                return text + "}";
            }

            private void Key(string key)
            {
                if (!first)
                {
                    text.Append(',');
                }

                first = false;
                text.Append('"').Append(Escape(key)).Append("\":");
            }

            private static string Escape(string value)
            {
                var builder = new StringBuilder(value.Length + 8);
                for (int index = 0; index < value.Length; index++)
                {
                    char c = value[index];
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }

                return builder.ToString();
            }
        }

        private static string AuditNumber(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "null";
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string AuditInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Sign always written, four digits: <c>+0118</c>, <c>-0034</c>.</summary>
        private static string AuditSigned(float value)
        {
            int rounded = Mathf.RoundToInt(value);
            return (rounded < 0 ? "-" : "+") +
                   Math.Abs(rounded).ToString("0000", CultureInfo.InvariantCulture);
        }

        private static string AuditYawText(int yaw)
        {
            return yaw.ToString("000", CultureInfo.InvariantCulture);
        }

        private static int AuditYaw(Vector3 from, Vector3 to)
        {
            float degrees = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            int yaw = Mathf.RoundToInt(degrees) % 360;
            return yaw < 0 ? yaw + 360 : yaw;
        }

        private static string AuditSlug(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "x";
            }

            var builder = new StringBuilder(value.Length);
            bool dash = false;
            for (int index = 0; index < value.Length; index++)
            {
                char c = char.ToLowerInvariant(value[index]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                    dash = false;
                }
                else if (!dash && builder.Length > 0)
                {
                    builder.Append('-');
                    dash = true;
                }
            }

            string slug = builder.ToString().TrimEnd('-');
            return slug.Length == 0 ? "x" : slug;
        }

        private static string AuditUniqueName(HashSet<string> names, string stem, string extension)
        {
            string candidate = stem + extension;
            int ordinal = 1;
            while (!names.Add(candidate))
            {
                ordinal++;
                candidate = stem + "_" + AuditInt(ordinal) + extension;
            }

            return candidate;
        }

        private static string AuditEnv(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool AuditEnvFlag(string name)
        {
            string value = AuditEnv(name);
            return value != null &&
                   value != "0" &&
                   !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static float AuditEnvFloat(string name, float fallback)
        {
            string value = AuditEnv(name);
            return value != null &&
                   float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static int AuditEnvInt(string name, int fallback)
        {
            string value = AuditEnv(name);
            return value != null &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static int[] AuditRange(int count)
        {
            var range = new int[count];
            for (int index = 0; index < count; index++)
            {
                range[index] = index;
            }

            return range;
        }

        private static Rect AuditExpand(Rect rect, float amount)
        {
            return Rect.MinMaxRect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.xMax + amount,
                rect.yMax + amount);
        }

        private static Rect AuditUnion(Rect a, Rect b)
        {
            if (b.width <= 0f || b.height <= 0f)
            {
                return a;
            }

            return Rect.MinMaxRect(
                Mathf.Min(a.xMin, b.xMin),
                Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax),
                Mathf.Max(a.yMax, b.yMax));
        }

        private static float AuditPlanar(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        private static Vector3 AuditBackOff(Vector3 from, Vector3 toward, float distance)
        {
            Vector3 away = from - toward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
            {
                away = Vector3.back;
            }

            return from + away.normalized * distance;
        }

        /// <summary>
        /// The planned ground: terrain cell top first, then the road top,
        /// as the mask overlay is built. False where no plan answers.
        /// </summary>
        private static bool AuditSampledGroundTop(CityLayout layout, Vector2 xz, out float top)
        {
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout, xz, out top, out _))
            {
                return true;
            }

            if (layout.ElevationPlan != null &&
                layout.ElevationPlan.TrySampleSurface(xz, CitySurfaceRole.RoadTop, out top, out _))
            {
                return true;
            }

            top = 0f;
            return false;
        }

        private static bool AuditRaycastGround(float x, float z, float fromY, float distance, out float y)
        {
            if (Physics.Raycast(
                    new Vector3(x, fromY, z),
                    Vector3.down,
                    out RaycastHit hit,
                    distance,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return true;
            }

            y = 0f;
            return false;
        }

        /// <summary>
        /// The physical ground under a finding with no height of its own:
        /// the colliders first, because that is what a photograph shows,
        /// then the plan, then zero.
        /// </summary>
        private static float AuditGroundY(CityLayout layout, float x, float z)
        {
            float estimate = AuditSampledGroundTop(layout, new Vector2(x, z), out float sampled)
                ? sampled
                : 0f;
            if (AuditRaycastGround(x, z, estimate + 30f, 90f, out float y))
            {
                return y;
            }

            return estimate;
        }

        private static Vector3 AuditNearestDoor(CityLayout layout, Vector3 position, out bool found)
        {
            found = false;
            Vector3 best = position;
            float bestDistance = 10f;
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding)
                {
                    continue;
                }

                float distance = AuditPlanar(lot.DoorPosition, position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = lot.DoorPosition;
                    found = true;
                }
            }

            return best;
        }

        private static Transform AuditFindByName(Transform root, string name)
        {
            if (root != null)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                    {
                        return child;
                    }
                }
            }

            GameObject fallback = GameObject.Find(name);
            return fallback != null ? fallback.transform : null;
        }

        private static bool AuditRendererBounds(GameObject host, out Bounds bounds)
        {
            bounds = default;
            if (host == null)
            {
                return false;
            }

            Renderer[] renderers = host.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] == null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderers[index].bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
            }

            return any;
        }
    }
}
