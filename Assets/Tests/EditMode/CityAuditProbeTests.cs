using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// One-off diagnostic instrument over the production City: builds it
    /// headlessly and writes machine-readable findings for invisible
    /// walls, terrain gaps, coplanar surfaces and illogical placement.
    /// It is an instrument, not a gate: the audit tests assert only that
    /// each check ran and flushed; nothing about the city's content.
    /// <c>Controls_EveryDetectorFires</c> proves every detector on a
    /// planted miniature world without building the city.
    /// </summary>
    [TestFixture]
    [Explicit("One-off City location audit; builds the production city and writes TestResults/CityAudit/<stamp>/")]
    [Category("CityAudit")]
    public sealed partial class CityAuditProbeTests
    {
        private const float GridStep = 0.25f;
        private const float HeroRadius = 0.32f;
        private const float HeroHeight = 1.7f;
        private const float StepMax = 0.28f;
        private const float OverlapRadius = 0.30f;
        private const float HeightMismatch = 0.05f;
        private const float BeachMismatch = 0.10f;
        private const float SeamCover = 0.005f;
        private const float MinSeamLength = 0.05f;
        private const float ZCertain = 0.001f;
        private const float ZLikely = 0.004f;
        private const float ZPossible = 0.0125f;
        private const float MinOverlapArea = 0.01f;
        private const float Floating = 0.08f;
        private const float Sunk = 0.50f;
        private const float Overhang = 0.30f;
        private const int StaticMask = 1 << 0;
        private const float CapsuleCastReach = 0.75f;
        private const float MapEdgeMargin = 0.5f;
        private const float SpanTolerance = 0.5f;

        private const string OutputDirEnv = "BARPROMENADE_CITY_AUDIT_DIR";
        private const string PlantEnv = "BARPROMENADE_CITY_AUDIT_PLANT";
        private const string ZFightMaxYEnv = "BARPROMENADE_CITY_AUDIT_ZFIGHT_MAXY";

        private static readonly int[] DirX = { 1, -1, 0, 0 };
        private static readonly int[] DirZ = { 0, 0, 1, -1 };
        private static readonly string[] DirName = { "+x", "-x", "+z", "-z" };

        private AuditContext city;
        private GameObject cityHost;

        /// <summary>The production city, built on first use only.</summary>
        private AuditContext City
        {
            get
            {
                if (city == null)
                {
                    city = BuildCity();
                }

                return city;
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (city != null)
            {
                city.Output.Close();
                city = null;
            }

            if (cityHost != null)
            {
                Object.DestroyImmediate(cityHost);
                cityHost = null;
            }
        }

        // ------------------------------------------------------------------
        // Context.
        // ------------------------------------------------------------------

        internal sealed class AuditContext
        {
            public string Label = string.Empty;
            public Transform Host;
            public RoadWalkableArea Mask;
            public CityLayout Layout;
            public CityWorldResult World;
            public CityStreetSurfacePlan Streets;
            public CityRoadGroundBoundaryPlan Boundaries;
            public Vector3 SeedPoint;
            public AuditOutput Output;
            public Rect GridBounds;
            public RendererRegistry Renderers;
            public TriangleIndex Tris;
            public ColliderRegistry Colliders;
            public StandingGrid Grid;
            public RectBucketIndex RoadRects;
            public RectBucketIndex SidewalkRects;
            public IReadOnlyList<Rect> SidewalkRectList = Array.Empty<Rect>();
            public IReadOnlyList<Rect> RoadRectList = Array.Empty<Rect>();
            public bool Planted;
            public Vector3 PlantPosition;
            public int ForeignHitCount;
            public readonly List<int> Scratch = new List<int>(256);
            public readonly RaycastHit[] Hits = new RaycastHit[64];
            public readonly Collider[] Overlaps = new Collider[32];

            public bool IsAudited(Collider collider, out ColliderRecord record)
            {
                record = Colliders.Find(collider);
                if (record == null)
                {
                    ForeignHitCount++;
                    return false;
                }

                return true;
            }

            public void Log(string message)
            {
                Debug.Log("[CityAudit " + Label + "] " + message);
            }
        }

        private static string ResolveOutputDirectory(string subfolder)
        {
            string root = Environment.GetEnvironmentVariable(OutputDirEnv);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "TestResults",
                    "CityAudit",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }

            return string.IsNullOrEmpty(subfolder) ? root : Path.Combine(root, subfolder);
        }

        private static void RecordThresholds(AuditOutput output)
        {
            output.Threshold("GridStep", GridStep);
            output.Threshold("HeroRadius", HeroRadius);
            output.Threshold("HeroHeight", HeroHeight);
            output.Threshold("StepMax", StepMax);
            output.Threshold("OverlapRadius", OverlapRadius);
            output.Threshold("HeightMismatch", HeightMismatch);
            output.Threshold("BeachMismatch", BeachMismatch);
            output.Threshold("SeamCover", SeamCover);
            output.Threshold("MinSeamLength", MinSeamLength);
            output.Threshold("ZCertain", ZCertain);
            output.Threshold("ZLikely", ZLikely);
            output.Threshold("ZPossible", ZPossible);
            output.Threshold("MinOverlapArea", MinOverlapArea);
            output.Threshold("Floating", Floating);
            output.Threshold("Sunk", Sunk);
            output.Threshold("Overhang", Overhang);
            output.Threshold("CapsuleCastReach", CapsuleCastReach);
        }

        private AuditContext BuildCity()
        {
            var timer = Stopwatch.StartNew();
            int foreignBefore = Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Include).Length;
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            cityHost = new GameObject("City Audit Host");
            CityWorldResult world = CityWorldBuilder.Build(
                cityHost.transform,
                layout,
                CityGenerationSettings.Default);
            Physics.SyncTransforms();
            double buildMs = timer.Elapsed.TotalMilliseconds;

            var output = new AuditOutput(ResolveOutputDirectory(null))
            {
                Seed = GameSessionState.DefaultCitySeed,
                Label = "city"
            };
            RecordThresholds(output);
            output.Count("foreignColliderCount", foreignBefore);
            output.Timing("city_build", buildMs);

            var context = new AuditContext
            {
                Label = "city",
                Host = cityHost.transform,
                Mask = world.WalkableArea,
                Layout = layout,
                World = world,
                Streets = CityStreetSurfacePlanner.Create(layout),
                Boundaries = CityRoadGroundBoundaryPlanner.Create(layout),
                SeedPoint = layout.SpawnWorldPosition,
                Output = output
            };
            Rect bounds = layout.MapWorldXZBounds;
            context.GridBounds = Rect.MinMaxRect(
                bounds.xMin - 2f,
                bounds.yMin - 2f,
                bounds.xMax + 2f,
                bounds.yMax + 2f);

            if (Environment.GetEnvironmentVariable(PlantEnv) == "1")
            {
                context.PlantPosition = layout.SpawnWorldPosition + new Vector3(3f, 0f, 0f);
                PlantInSitu(context);
                context.Planted = true;
                output.Notes.Add("in-situ plant at " + F(context.PlantPosition.x) + "," + F(context.PlantPosition.z));
            }

            Prepare(context);
            output.WriteDistricts(CityMapAreaOverlayBuilder.Create(layout));
            output.WriteRunJson();
            return context;
        }

        private static void PlantInSitu(AuditContext context)
        {
            Vector3 p = context.PlantPosition;
            float ground = p.y;
            if (CityTerrainSurfacePlan.TrySampleGroundTop(
                    context.Layout,
                    new Vector2(p.x, p.z),
                    out float top,
                    out _))
            {
                ground = top;
            }

            var box = new GameObject("Audit Plant Invisible Box");
            box.transform.SetParent(context.Host, false);
            box.transform.position = new Vector3(p.x, ground + 0.5f, p.z);
            box.AddComponent<BoxCollider>().size = Vector3.one;

            var quad = new GameObject("Audit Plant Z Quad");
            quad.transform.SetParent(context.Host, false);
            quad.AddComponent<MeshFilter>().sharedMesh = BuildQuadMesh(
                p.x - 0.5f, p.x + 0.5f, p.z - 0.5f, p.z + 0.5f, ground + 0.002f);
            quad.AddComponent<MeshRenderer>().sharedMaterial = ControlMaterial();
            context.PlantPosition = new Vector3(p.x, ground, p.z);
            Physics.SyncTransforms();
        }

        /// <summary>Builds every registry and the standing grid.</summary>
        private static void Prepare(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            context.Renderers = RendererRegistry.Build(context.Host);
            context.Output.Timing("renderer_registry", timer.Elapsed.TotalMilliseconds);
            timer.Restart();
            context.Tris = TriangleIndex.Build(context.Renderers, context.Log);
            context.Output.Timing("triangle_index", timer.Elapsed.TotalMilliseconds);
            timer.Restart();
            context.Colliders = ColliderRegistry.Build(context.Host, context.Tris, context.Scratch);
            context.Output.Timing("collider_registry", timer.Elapsed.TotalMilliseconds);
            context.Output.Count("renderers", context.Renderers.Records.Count);
            context.Output.Count("renderersUnreadable", context.Renderers.Unreadable);
            context.Output.Count("renderersSkippedText", context.Renderers.SkippedText);
            context.Output.Count("triangles", context.Tris.Count);
            context.Output.Count("trianglesDegenerate", context.Tris.Degenerate);
            context.Output.Count("trianglesBig", context.Tris.BigCount);
            context.Output.Count("components", context.Tris.TotalComponents);
            context.Output.Count("colliders", context.Colliders.Records.Count);
            context.Output.WriteRenderers(context.Renderers);
            context.Output.WriteColliders(context.Colliders);

            if (context.Layout != null)
            {
                context.RoadRectList = context.Layout.CreateRoadRects();
                context.SidewalkRectList = context.Streets != null
                    ? context.Streets.SidewalkWalkableRectangles
                    : Array.Empty<Rect>();
            }

            context.RoadRects = new RectBucketIndex(context.RoadRectList);
            context.SidewalkRects = new RectBucketIndex(context.SidewalkRectList);

            timer.Restart();
            BuildStandingGrid(context);
            context.Output.Timing("standing_grid", timer.Elapsed.TotalMilliseconds);
            context.Output.Count("gridCells", context.Grid.CellCount);
            context.Output.Count("gridInside", context.Grid.InsideCount);
            context.Output.Count("gridRing", context.Grid.RingCount);
            context.Output.Count("gridRays", context.Grid.RayCount);
            context.Output.Count("gridOverlaps", context.Grid.OverlapCount);
            context.Output.Count("foreignHits", context.Grid.ForeignHits);
            context.Log(
                "grid " + context.Grid.NX + "x" + context.Grid.NZ + " cells, " +
                context.Grid.InsideCount + " inside, " + context.Grid.RingCount + " ring");
        }

        private static void BuildStandingGrid(AuditContext context)
        {
            var grid = new StandingGrid
            {
                XMin = context.GridBounds.xMin,
                ZMin = context.GridBounds.yMin,
                Step = GridStep
            };
            grid.NX = Math.Max(1, (int)Math.Ceiling(context.GridBounds.width / GridStep));
            grid.NZ = Math.Max(1, (int)Math.Ceiling(context.GridBounds.height / GridStep));
            int count = grid.NX * grid.NZ;
            grid.Flags = new byte[count];
            grid.GroundY = new float[count];
            grid.ExpectedY = new float[count];
            grid.Occupant = new int[count];
            grid.GroundCollider = new int[count];
            for (int index = 0; index < count; index++)
            {
                grid.GroundY[index] = float.NaN;
                grid.ExpectedY[index] = float.NaN;
                grid.Occupant[index] = -1;
                grid.GroundCollider[index] = -1;
            }

            RoadWalkableArea mask = context.Mask;
            CityLayout layout = context.Layout;
            for (int iz = 0; iz < grid.NZ; iz++)
            {
                float z = grid.CenterZ(iz);
                for (int ix = 0; ix < grid.NX; ix++)
                {
                    float x = grid.CenterX(ix);
                    int index = grid.Index(ix, iz);
                    var p = new Vector3(x, 0f, z);
                    byte flags = 0;
                    if (mask.Contains(p, HeroRadius))
                    {
                        flags |= StandingGrid.Inside;
                        grid.InsideCount++;
                    }

                    if (mask.Contains(p, 0f))
                    {
                        flags |= StandingGrid.Touching;
                    }

                    if (layout != null && layout.IsWater(p))
                    {
                        flags |= StandingGrid.Water;
                    }

                    grid.Flags[index] = flags;
                }
            }

            // Ring: every non-inside cell 8-adjacent to an inside cell.
            for (int iz = 0; iz < grid.NZ; iz++)
            for (int ix = 0; ix < grid.NX; ix++)
            {
                int index = grid.Index(ix, iz);
                if ((grid.Flags[index] & StandingGrid.Inside) != 0)
                {
                    continue;
                }

                bool ring = false;
                for (int dz = -1; dz <= 1 && !ring; dz++)
                for (int dx = -1; dx <= 1 && !ring; dx++)
                {
                    if ((dx != 0 || dz != 0) && grid.IsInside(ix + dx, iz + dz))
                    {
                        ring = true;
                    }
                }

                if (ring)
                {
                    grid.Flags[index] |= StandingGrid.Ring;
                    grid.RingCount++;
                }
            }

            // Expected heights and physics samples for inside + ring cells.
            for (int iz = 0; iz < grid.NZ; iz++)
            {
                float z = grid.CenterZ(iz);
                for (int ix = 0; ix < grid.NX; ix++)
                {
                    int index = grid.Index(ix, iz);
                    byte flags = grid.Flags[index];
                    bool inside = (flags & StandingGrid.Inside) != 0;
                    if (!inside && (flags & StandingGrid.Ring) == 0)
                    {
                        continue;
                    }

                    float x = grid.CenterX(ix);
                    float expected = float.NaN;
                    if (layout != null)
                    {
                        var xz = new Vector2(x, z);
                        if (context.SidewalkRects.Find(x, z) >= 0)
                        {
                            grid.Flags[index] |= StandingGrid.SidewalkRect;
                            if (layout.ElevationPlan.TrySampleSurface(
                                    xz, CitySurfaceRole.SidewalkTop, out float h, out _))
                            {
                                expected = h;
                            }
                        }
                        else if (context.RoadRects.Find(x, z) >= 0)
                        {
                            grid.Flags[index] |= StandingGrid.RoadRect;
                            if (layout.ElevationPlan.TrySampleSurface(
                                    xz, CitySurfaceRole.RoadTop, out float h, out _))
                            {
                                expected = h;
                            }
                        }
                        else if (CityTerrainSurfacePlan.TrySampleGroundTop(
                                     layout, xz, out float top, out _))
                        {
                            expected = top;
                        }
                    }

                    grid.ExpectedY[index] = expected;

                    float originY = float.IsNaN(expected) ? 40f : expected + 2f;
                    float reach = float.IsNaN(expected) ? 80f : 6f;
                    int hits = Physics.RaycastNonAlloc(
                        new Vector3(x, originY, z),
                        Vector3.down,
                        context.Hits,
                        reach,
                        StaticMask,
                        QueryTriggerInteraction.Ignore);
                    grid.RayCount++;
                    // The highest ground-class hit is the floor; anything
                    // audited above it is an obstacle the capsule overlap
                    // below reports. Only without a ground-class hit does
                    // the highest hit of any class stand in (decks, ramps).
                    float best = float.NaN;
                    int bestCollider = -1;
                    float bestFlat = float.NaN;
                    int bestFlatCollider = -1;
                    for (int hit = 0; hit < hits; hit++)
                    {
                        RaycastHit h = context.Hits[hit];
                        if (!context.IsAudited(h.collider, out ColliderRecord record))
                        {
                            grid.ForeignHits++;
                            continue;
                        }

                        if (float.IsNaN(best) || h.point.y > best)
                        {
                            best = h.point.y;
                            bestCollider = record.Index;
                        }

                        if (IsFlatClass(record.Class) &&
                            (float.IsNaN(bestFlat) || h.point.y > bestFlat))
                        {
                            bestFlat = h.point.y;
                            bestFlatCollider = record.Index;
                        }
                    }

                    if (!float.IsNaN(bestFlat))
                    {
                        best = bestFlat;
                        bestCollider = bestFlatCollider;
                    }

                    if (!float.IsNaN(best))
                    {
                        grid.GroundY[index] = best;
                        grid.GroundCollider[index] = bestCollider;
                        grid.Flags[index] |= StandingGrid.HasGround;
                    }

                    if (!inside || float.IsNaN(best))
                    {
                        continue;
                    }

                    var p0 = new Vector3(x, best + StepMax + OverlapRadius, z);
                    var p1 = new Vector3(x, best + HeroHeight - OverlapRadius, z);
                    int overlaps = Physics.OverlapCapsuleNonAlloc(
                        p0, p1, OverlapRadius, context.Overlaps, StaticMask,
                        QueryTriggerInteraction.Ignore);
                    grid.OverlapCount++;
                    for (int o = 0; o < overlaps; o++)
                    {
                        Collider collider = context.Overlaps[o];
                        if (collider == null || collider.isTrigger)
                        {
                            continue;
                        }

                        if (!context.IsAudited(collider, out ColliderRecord record))
                        {
                            grid.ForeignHits++;
                            continue;
                        }

                        // A ledge of terrain, road or slab inside the capsule's lower
                        // hemisphere is a STEP, not an obstacle: A3 measures it as a
                        // ground delta. Counting it here made every cell beside a
                        // 0.5 m terrace "occupied" and hid the step barrier itself.
                        if (IsFlatClass(record.Class))
                        {
                            continue;
                        }

                        grid.Occupant[index] = record.Index;
                        grid.Flags[index] |= StandingGrid.Occupied;
                        break;
                    }
                }
            }

            context.Grid = grid;
        }

        // ------------------------------------------------------------------
        // Shared enrichment helpers.
        // ------------------------------------------------------------------

        private static void Locate(AuditContext context, Finding finding)
        {
            CityLayout layout = context.Layout;
            if (layout == null)
            {
                return;
            }

            if (TryFindSurface(layout, (float)finding.X, (float)finding.Z, out CitySurfaceDescriptor surface))
            {
                finding.Area = surface.AreaId;
                finding.Cell = surface.Cell.x.ToString(CultureInfo.InvariantCulture) + "," +
                               surface.Cell.y.ToString(CultureInfo.InvariantCulture);
                finding.Surface = surface.Kind.ToString();
            }
        }

        private static bool TryFindSurface(
            CityLayout layout,
            float x,
            float z,
            out CitySurfaceDescriptor surface)
        {
            const float tolerance = 0.001f;
            bool found = false;
            surface = default;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor candidate = layout.Surfaces[index];
                Rect bounds = candidate.WorldBounds;
                if (x < bounds.xMin - tolerance || x > bounds.xMax + tolerance ||
                    z < bounds.yMin - tolerance || z > bounds.yMax + tolerance)
                {
                    continue;
                }

                if (!found || (surface.IsWater && !candidate.IsWater))
                {
                    surface = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static float GroundAt(AuditContext context, float x, float z)
        {
            StandingGrid grid = context.Grid;
            if (grid.TryCell(x, z, out int ix, out int iz))
            {
                int index = grid.Index(ix, iz);
                if (grid.Has(index, StandingGrid.HasGround))
                {
                    return grid.GroundY[index];
                }

                return grid.ExpectedY[index];
            }

            return float.NaN;
        }

        private static bool AnyTriangleInBox(
            AuditContext context,
            Vector3 min,
            Vector3 max,
            bool excludeFlat)
        {
            if (min.x > max.x || min.y > max.y || min.z > max.z)
            {
                return false;
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 half = (max - min) * 0.5f;
            context.Tris.QueryBox(min, max, context.Scratch);
            for (int index = 0; index < context.Scratch.Count; index++)
            {
                ref Tri t = ref context.Tris.Tris[context.Scratch[index]];
                if (excludeFlat && IsFlatClass(t.Class))
                {
                    continue;
                }

                if (Geo.TriBoxOverlap(center, half, t.A, t.B, t.C))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWaterAt(AuditContext context, Vector3 point)
        {
            if (context.Layout != null && context.Layout.IsWater(point))
            {
                return true;
            }

            CitySeacoastPlan coast = context.World != null ? context.World.SeacoastPlan : null;
            // The mask stops one hero radius short of the waterline, so the cell
            // just outside it is still sand: a metre of tolerance keeps the shore
            // "by design" instead of reporting 300 m of invisible wall.
            if (coast != null &&
                point.z > coast.Frame.WaterlineZ - 1f &&
                Geo.RectContains(coast.Frame.SeaRowBounds, point.x, point.z))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Re-implementation of the private
        /// CityRoadGroundBoundaryPlanner.RequiresAuthoredAccess
        /// (CityRoadGroundBoundaryPlan.cs:384-434).
        /// </summary>
        private static bool RequiresAuthoredAccess(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            RoadEdge edge)
        {
            if (surface.Kind == CitySurfaceKind.ParkGround)
            {
                return layout.HasParkBoundaryHedges &&
                       layout.GetPathKind(edge) == CityPathKind.Street;
            }

            if (surface.Kind == CitySurfaceKind.OpenGround &&
                layout.GetPathKind(edge) == CityPathKind.Street &&
                CityMountainBoundaryDefinition.TryResolve(layout.BlueprintId, out _) &&
                CityMountainBoundaryDefinition.IsMountainFacingAreaId(surface.AreaId))
            {
                return false;
            }

            if (surface.Kind == CitySurfaceKind.Beach &&
                layout.GetPathKind(edge) == CityPathKind.Street)
            {
                return false;
            }

            return surface.Kind == CitySurfaceKind.Beach ||
                   surface.Kind == CitySurfaceKind.CemeteryGround ||
                   surface.Kind == CitySurfaceKind.OpenGround ||
                   surface.Kind == CitySurfaceKind.ChurchGround;
        }

        private static bool OnSpan(CityRoadGroundBoundarySpan span, float x, float z, float tolerance)
        {
            float fixedValue = span.IsHorizontal ? z : x;
            float variable = span.IsHorizontal ? x : z;
            return Math.Abs(fixedValue - span.FixedCoordinate) <= tolerance &&
                   variable >= span.MinimumCoordinate - tolerance &&
                   variable <= span.MaximumCoordinate + tolerance;
        }

        private static bool OnAnySpan(
            IReadOnlyList<CityRoadGroundBoundarySpan> spans,
            float x,
            float z,
            float tolerance)
        {
            if (spans == null)
            {
                return false;
            }

            for (int index = 0; index < spans.Count; index++)
            {
                if (OnSpan(spans[index], x, z, tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// First matching by-design rule for an outside point, or null.
        /// Map edges are special: the east side is by design under a
        /// prefixed subtype, the other sides only downgrade to suspect.
        /// </summary>
        private static string ApplyDesignRules(
            AuditContext context,
            Vector3 o,
            bool water,
            ref string subtype,
            ref string tag)
        {
            if (water)
            {
                tag = "by_design";
                return "water";
            }

            CityLayout layout = context.Layout;
            if (layout == null)
            {
                return null;
            }

            CityMountainBoundaryPlan mountain =
                context.World != null ? context.World.MountainBoundaryPlan : null;
            if (mountain != null && mountain.HasRiverCave)
            {
                CityMountainRiverNotchDescriptor cave = mountain.RiverCave;
                if (Geo.RectContains(cave.MouthBounds, o.x, o.z) ||
                    Geo.RectContains(cave.ThroatWaterBounds, o.x, o.z) ||
                    o.z < cave.ApproachBounds.yMin)
                {
                    tag = "by_design";
                    return "river_cave";
                }
            }

            if (mountain != null && mountain.HasTunnel)
            {
                CityMountainTunnelDescriptor tunnel = mountain.Tunnel;
                Vector3 axis = tunnel.Axis;
                axis.y = 0f;
                Vector3 delta = o - tunnel.PortalGroundCenter;
                delta.y = 0f;
                if (axis.sqrMagnitude > 1e-6f &&
                    Vector3.Dot(delta, axis.normalized) > tunnel.WalkableDepth)
                {
                    tag = "by_design";
                    return "tunnel";
                }
            }

            Rect map = layout.MapWorldXZBounds;
            if (o.x >= map.xMax - MapEdgeMargin)
            {
                subtype = "map_edge_east_" + subtype;
                tag = "by_design";
                return "map_edge_east";
            }

            if (o.x <= map.xMin + MapEdgeMargin)
            {
                tag = "suspect";
                return "map_edge_west";
            }

            if (o.z >= map.yMax - MapEdgeMargin)
            {
                tag = "suspect";
                return "map_edge_north";
            }

            if (o.z <= map.yMin + MapEdgeMargin)
            {
                tag = "suspect";
                return "map_edge_south";
            }

            if (CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout, new Vector2(o.x, o.z), out _, out CitySurfaceDescriptor surface) &&
                !OnAnySpan(context.Boundaries?.SafeConnections, o.x, o.z, CapsuleCastReach))
            {
                Rect bounds = surface.WorldBounds;
                for (int direction = 0; direction < 4; direction++)
                {
                    float distance;
                    Vector2Int frontage;
                    switch (direction)
                    {
                        case 0:
                            distance = Math.Abs(o.x - bounds.xMax);
                            frontage = Vector2Int.right;
                            break;
                        case 1:
                            distance = Math.Abs(o.x - bounds.xMin);
                            frontage = Vector2Int.left;
                            break;
                        case 2:
                            distance = Math.Abs(o.z - bounds.yMax);
                            frontage = Vector2Int.up;
                            break;
                        default:
                            distance = Math.Abs(o.z - bounds.yMin);
                            frontage = Vector2Int.down;
                            break;
                    }

                    if (distance > CapsuleCastReach)
                    {
                        continue;
                    }

                    RoadEdge edge = RoadEdge.ForCellFrontage(surface.Cell, frontage);
                    if (layout.HasRoad(edge) && RequiresAuthoredAccess(layout, surface, edge))
                    {
                        tag = "by_design";
                        return "authored_access_perimeter";
                    }
                }
            }

            CityArchShelterPlan shelter = context.World != null ? context.World.ArchShelterPlan : null;
            if (shelter != null && shelter.IsEnabled)
            {
                if (Geo.RectContains(shelter.Placement.ShelteredFootprint, o.x, o.z) ||
                    Geo.RectContains(shelter.Placement.TableauFootprint, o.x, o.z))
                {
                    tag = "by_design";
                    return "arch_shelter";
                }
            }

            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding)
                {
                    continue;
                }

                if (Math.Abs(o.x - lot.Center.x) <= lot.Size.x * 0.5f &&
                    Math.Abs(o.z - lot.Center.z) <= lot.Size.y * 0.5f)
                {
                    tag = "by_design";
                    return "building_lot";
                }
            }

            if (layout.HasParkBoundaryHedges)
            {
                for (int region = 0; region < layout.Park.Regions.Count; region++)
                {
                    CityParkRegionPlan park = layout.Park.Regions[region];
                    if (Geo.RectContains(park.WalkableBounds, o.x, o.z))
                    {
                        continue;
                    }

                    if (TryFindSurface(layout, o.x, o.z, out CitySurfaceDescriptor cell) &&
                        park.ContainsCell(cell.Cell))
                    {
                        tag = "by_design";
                        return "park_hedge_ring";
                    }
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Cluster helper: union-find over grid cells with an axis label.
        // ------------------------------------------------------------------

        private sealed class CellCluster
        {
            public readonly List<int> Members = new List<int>();
        }

        /// <summary>
        /// Groups the given items by 8-connectivity of their grid cells
        /// (items sharing an axis label only). Returns the clusters in
        /// first-member order.
        /// </summary>
        private static List<CellCluster> ClusterCells(
            StandingGrid grid,
            List<int> cells,
            List<int> axes,
            bool eightConnected)
        {
            var lookup = new Dictionary<long, int>(cells.Count);
            for (int index = 0; index < cells.Count; index++)
            {
                long key = ((long)cells[index] << 3) | (long)(axes != null ? axes[index] : 0);
                if (!lookup.ContainsKey(key))
                {
                    lookup.Add(key, index);
                }
            }

            var union = new UnionFind(cells.Count);
            for (int index = 0; index < cells.Count; index++)
            {
                int cell = cells[index];
                int axis = axes != null ? axes[index] : 0;
                int ix = cell % grid.NX;
                int iz = cell / grid.NX;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    if (!eightConnected && dx != 0 && dz != 0)
                    {
                        continue;
                    }

                    if (!grid.InGrid(ix + dx, iz + dz))
                    {
                        continue;
                    }

                    long key = ((long)grid.Index(ix + dx, iz + dz) << 3) | (long)axis;
                    if (lookup.TryGetValue(key, out int other))
                    {
                        union.Union(index, other);
                    }
                }
            }

            var byRoot = new Dictionary<int, CellCluster>();
            var result = new List<CellCluster>();
            for (int index = 0; index < cells.Count; index++)
            {
                int root = union.Find(index);
                if (!byRoot.TryGetValue(root, out CellCluster cluster))
                {
                    cluster = new CellCluster();
                    byRoot.Add(root, cluster);
                    result.Add(cluster);
                }

                cluster.Members.Add(index);
            }

            return result;
        }

        // ------------------------------------------------------------------
        // A — invisible walls.
        // ------------------------------------------------------------------

        private struct WallCandidate
        {
            public int Cell;
            public int Dir;
            public string Subtype;
            public string Tag;
            public string Note;
            public string ObjectPath;
            public float StepDelta;
            public float Gap;
            public float MidX;
            public float MidZ;
            public float GroundY;
        }

        private static void RunA(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            StandingGrid grid = context.Grid;
            var candidates = new List<WallCandidate>();
            int visibleBarriers = 0;
            int skippedNoGround = 0;
            IReadOnlyList<CityRoadGroundBoundarySpan> protectedDrops =
                context.Boundaries?.ProtectedDrops;

            for (int iz = 0; iz < grid.NZ; iz++)
            for (int ix = 0; ix < grid.NX; ix++)
            {
                int c = grid.Index(ix, iz);
                if (!grid.Has(c, StandingGrid.Inside))
                {
                    continue;
                }

                float cx = grid.CenterX(ix);
                float cz = grid.CenterZ(iz);
                float g = grid.GroundY[c];
                for (int d = 0; d < 4; d++)
                {
                    int nx = ix + DirX[d];
                    int nz = iz + DirZ[d];
                    if (grid.IsInside(nx, nz))
                    {
                        continue;
                    }

                    float ox = grid.CenterX(nx);
                    float oz = grid.CenterZ(nz);
                    var candidate = new WallCandidate
                    {
                        Cell = c,
                        Dir = d,
                        Tag = "new",
                        MidX = (cx + ox) * 0.5f,
                        MidZ = (cz + oz) * 0.5f,
                        GroundY = g,
                        StepDelta = float.NaN,
                        Gap = float.NaN
                    };

                    // (1) Abutting rectangles leave a dead band.
                    int bx = ix + 3 * DirX[d];
                    int bz = iz + 3 * DirZ[d];
                    if (grid.IsInside(bx, bz))
                    {
                        int gapCells = 0;
                        for (int k = 1; k <= 3; k++)
                        {
                            if (!grid.IsInside(ix + k * DirX[d], iz + k * DirZ[d]))
                            {
                                gapCells++;
                            }
                        }

                        candidate.Subtype = "mask_dead_band";
                        candidate.Gap = gapCells * grid.Step;
                        // A dead band along a precinct that is entered only through
                        // its authored access IS that design (the ~0.7 m band the
                        // archive documents); the rule decides, not the subtype.
                        int bandCell = grid.InGrid(nx, nz) ? grid.Index(nx, nz) : -1;
                        float bandGround = bandCell >= 0 && !float.IsNaN(grid.GroundY[bandCell])
                            ? grid.GroundY[bandCell]
                            : g;
                        var bandPoint = new Vector3(ox, float.IsNaN(bandGround) ? 0f : bandGround, oz);
                        bool bandWater = IsWaterAt(context, bandPoint);
                        string bandSubtype = candidate.Subtype;
                        string bandTag = candidate.Tag;
                        string bandRule = ApplyDesignRules(context, bandPoint, bandWater, ref bandSubtype, ref bandTag);
                        if (bandRule != null)
                        {
                            candidate.Subtype = bandSubtype;
                            candidate.Tag = bandTag;
                            candidate.Note = bandRule;
                        }

                        candidates.Add(candidate);
                        continue;
                    }

                    if (float.IsNaN(g))
                    {
                        skippedNoGround++;
                        continue;
                    }

                    // (2) What lies beyond.
                    int n = grid.InGrid(nx, nz) ? grid.Index(nx, nz) : -1;
                    float gn = n >= 0 ? grid.GroundY[n] : float.NaN;
                    bool groundBeyond = !float.IsNaN(gn);
                    float stepDelta = groundBeyond ? Math.Abs(gn - g) : float.NaN;
                    var o = new Vector3(ox, groundBeyond ? gn : g, oz);
                    bool water = IsWaterAt(context, o);
                    candidate.StepDelta = stepDelta;

                    // (3) A physical barrier?
                    var p0 = new Vector3(cx, g + StepMax + OverlapRadius, cz);
                    var p1 = new Vector3(cx, g + HeroHeight - OverlapRadius, cz);
                    var direction = new Vector3(DirX[d], 0f, DirZ[d]);
                    bool blocked = false;
                    if (Physics.CapsuleCast(
                            p0, p1, OverlapRadius, direction, out RaycastHit hit,
                            CapsuleCastReach, StaticMask, QueryTriggerInteraction.Ignore))
                    {
                        if (context.IsAudited(hit.collider, out ColliderRecord record))
                        {
                            if (record.Visible)
                            {
                                visibleBarriers++;
                                continue;
                            }

                            candidate.Subtype = "invisible_collider_barrier";
                            candidate.Tag = record.KnownInvisible ? "by_design" : "new";
                            candidate.ObjectPath = record.Path;
                            candidate.Note = record.KnownInvisible ? "known_invisible" : record.Type;
                            blocked = true;
                        }
                    }

                    if (!blocked)
                    {
                        // (4) Nothing physical: is anything drawn there?
                        var min = new Vector3(candidate.MidX - 0.3f, g + 0.05f, candidate.MidZ - 0.3f);
                        var max = new Vector3(candidate.MidX + 0.3f, g + 1.4f, candidate.MidZ + 0.3f);
                        if (AnyTriangleInBox(context, min, max, true))
                        {
                            candidate.Subtype = "mask_edge_visual_only";
                            candidate.Tag = "suspect";
                        }
                        else if (groundBeyond && stepDelta <= StepMax && !water)
                        {
                            candidate.Subtype = "mask_edge_no_barrier";
                            candidate.Tag = "new";
                        }
                        else if (groundBeyond && stepDelta > StepMax)
                        {
                            candidate.Subtype = "mask_edge_drop";
                            candidate.Tag = "suspect";
                            if (OnAnySpan(protectedDrops, candidate.MidX, candidate.MidZ, SpanTolerance))
                            {
                                candidate.Subtype = "missing_rail";
                                candidate.Tag = "new";
                            }
                        }
                        else
                        {
                            candidate.Subtype = "mask_edge_void";
                            candidate.Tag = water ? "by_design" : "suspect";
                        }
                    }

                    string subtype = candidate.Subtype;
                    string tag = candidate.Tag;
                    string rule = ApplyDesignRules(context, o, water, ref subtype, ref tag);
                    if (rule != null)
                    {
                        candidate.Subtype = subtype;
                        candidate.Tag = tag;
                        candidate.Note = string.IsNullOrEmpty(candidate.Note) ? rule : candidate.Note + ";" + rule;
                    }

                    candidates.Add(candidate);
                }
            }

            context.Output.Count("A_visibleBarriers", visibleBarriers);
            context.Output.Count("A_skippedNoGround", skippedNoGround);
            context.Output.Count("A_candidates", candidates.Count);

            var cells = new List<int>(candidates.Count);
            var axes = new List<int>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                cells.Add(candidates[index].Cell);
                axes.Add(candidates[index].Dir);
            }

            List<CellCluster> clusters = ClusterCells(grid, cells, axes, true);
            foreach (CellCluster cluster in clusters)
            {
                EmitWallRun(context, candidates, cluster);
            }

            context.Output.Timing("A_invisible_walls", timer.Elapsed.TotalMilliseconds);
            context.Log("A: " + candidates.Count + " candidate pairs, " + clusters.Count +
                        " runs, " + visibleBarriers + " visible barriers");
        }

        private static void EmitWallRun(
            AuditContext context,
            List<WallCandidate> candidates,
            CellCluster cluster)
        {
            var subtypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var notes = new List<string>();
            var objects = new List<string>();
            string worstTag = "by_design";
            float sumX = 0f, sumZ = 0f, sumY = 0f;
            int yCount = 0;
            float stepMin = float.PositiveInfinity, stepMax = float.NegativeInfinity;
            float gapMax = 0f;
            var box = new BoxXZ();
            int dir = -1;
            foreach (int member in cluster.Members)
            {
                WallCandidate candidate = candidates[member];
                dir = candidate.Dir;
                subtypeCounts.TryGetValue(candidate.Subtype, out int count);
                subtypeCounts[candidate.Subtype] = count + 1;
                worstTag = WorstTag(worstTag, candidate.Tag);
                sumX += candidate.MidX;
                sumZ += candidate.MidZ;
                if (!float.IsNaN(candidate.GroundY))
                {
                    sumY += candidate.GroundY;
                    yCount++;
                }

                if (!float.IsNaN(candidate.StepDelta))
                {
                    stepMin = Math.Min(stepMin, candidate.StepDelta);
                    stepMax = Math.Max(stepMax, candidate.StepDelta);
                }

                if (!float.IsNaN(candidate.Gap))
                {
                    gapMax = Math.Max(gapMax, candidate.Gap);
                }

                box.Add(candidate.MidX, candidate.MidZ);
                if (!string.IsNullOrEmpty(candidate.Note) && !notes.Contains(candidate.Note))
                {
                    notes.Add(candidate.Note);
                }

                if (!string.IsNullOrEmpty(candidate.ObjectPath) &&
                    !objects.Contains(candidate.ObjectPath) && objects.Count < 5)
                {
                    objects.Add(candidate.ObjectPath);
                }
            }

            string dominant = string.Empty;
            int dominantCount = -1;
            foreach (KeyValuePair<string, int> pair in subtypeCounts)
            {
                if (pair.Value > dominantCount ||
                    (pair.Value == dominantCount && string.CompareOrdinal(pair.Key, dominant) < 0))
                {
                    dominant = pair.Key;
                    dominantCount = pair.Value;
                }
            }

            int members = cluster.Members.Count;
            Finding finding = NewFinding("A", dominant, worstTag);
            finding.At(sumX / members, yCount > 0 ? sumY / yCount : double.NaN, sumZ / members);
            finding.Dir = dir >= 0 ? DirName[dir] : null;
            finding.Metric("run_m", members * context.Grid.Step);
            finding.Metric("cells", members);
            if (!float.IsPositiveInfinity(stepMin))
            {
                finding.Metric("step_min", stepMin);
                finding.Metric("step_max", stepMax);
            }

            if (gapMax > 0f)
            {
                finding.Metric("gap_to_next_rect", gapMax);
            }

            finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
            finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
            foreach (string path in objects)
            {
                finding.Object(path);
            }

            if (subtypeCounts.Count > 1)
            {
                var mix = new List<string>();
                foreach (KeyValuePair<string, int> pair in subtypeCounts)
                {
                    mix.Add(pair.Key + "=" + Json.Int(pair.Value));
                }

                mix.Sort(StringComparer.Ordinal);
                notes.Add("mixed:" + string.Join(",", mix));
            }

            finding.Note = string.Join("; ", notes);
            Locate(context, finding);
            context.Output.Add(finding);
        }

        // ------------------------------------------------------------------
        // A2 — invisible obstacles and proxy overhang.
        // ------------------------------------------------------------------

        private static void RunA2(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            StandingGrid grid = context.Grid;
            var cellsByOccupant = new Dictionary<int, List<int>>();
            for (int index = 0; index < grid.CellCount; index++)
            {
                int occupant = grid.Occupant[index];
                if (occupant < 0)
                {
                    continue;
                }

                if (!cellsByOccupant.TryGetValue(occupant, out List<int> cells))
                {
                    cells = new List<int>();
                    cellsByOccupant.Add(occupant, cells);
                }

                cells.Add(index);
            }

            int invisible = 0;
            int overhangs = 0;
            foreach (KeyValuePair<int, List<int>> pair in cellsByOccupant)
            {
                ColliderRecord record = context.Colliders.Records[pair.Key];
                List<int> cells = pair.Value;
                var box = new BoxXZ();
                float sumX = 0f, sumZ = 0f, sumY = 0f;
                foreach (int cell in cells)
                {
                    float x = grid.CenterX(cell % grid.NX);
                    float z = grid.CenterZ(cell / grid.NX);
                    box.Add(x, z);
                    sumX += x;
                    sumZ += z;
                    sumY += grid.GroundY[cell];
                }

                if (!record.Visible)
                {
                    invisible++;
                    Finding finding = NewFinding(
                        "A2",
                        "invisible_obstacle_in_mask",
                        record.KnownInvisible ? "by_design" : "new");
                    finding.At(sumX / cells.Count, sumY / cells.Count, sumZ / cells.Count);
                    finding.Metric("m2", cells.Count * grid.CellArea);
                    finding.Metric("cells", cells.Count);
                    finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
                    finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
                    finding.Object(record.Path);
                    finding.Note = record.Type + (record.KnownInvisible ? ";known_invisible" : string.Empty);
                    Locate(context, finding);
                    context.Output.Add(finding);
                }

                if (record.IsProxy)
                {
                    if (EmitOverhang(context, record))
                    {
                        overhangs++;
                    }
                }
            }

            context.Output.Count("A2_occupants", cellsByOccupant.Count);
            context.Output.Timing("A2_invisible_obstacles", timer.Elapsed.TotalMilliseconds);
            context.Log("A2: " + cellsByOccupant.Count + " occupant colliders, " + invisible +
                        " invisible, " + overhangs + " overhangs");
        }

        private static bool EmitOverhang(AuditContext context, ColliderRecord record)
        {
            // 0.25 m raster and a 2.5 m column: cover is anything drawn near the
            // footprint within reach of the eye, not the whole facade up to the roof
            // (the first run rasterised building masses at 0.1 m against 40 m columns).
            const float raster = 0.25f;
            const float coverColumn = 2.5f;
            // Only the RIM of the footprint can overhang the art: the interior of a
            // building mass is empty by construction (a shell), and a thin collider
            // is a floor you walk on, not a wall you bump into.
            const float rimBand = 0.6f;
            Bounds bounds = record.Bounds;
            if (bounds.size.y < 0.35f)
            {
                return false;
            }

            int nx = Math.Max(1, (int)Math.Ceiling(bounds.size.x / raster));
            int nz = Math.Max(1, (int)Math.Ceiling(bounds.size.z / raster));
            int total = 0;
            int uncovered = 0;
            float sumX = 0f, sumZ = 0f;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float px = bounds.min.x + (ix + 0.5f) * raster;
                float pz = bounds.min.z + (iz + 0.5f) * raster;
                if (px > bounds.max.x || pz > bounds.max.z)
                {
                    continue;
                }

                float rim = Math.Min(
                    Math.Min(px - bounds.min.x, bounds.max.x - px),
                    Math.Min(pz - bounds.min.z, bounds.max.z - pz));
                if (rim > rimBand)
                {
                    continue;
                }

                float g = GroundAt(context, px, pz);
                if (float.IsNaN(g))
                {
                    g = bounds.min.y;
                }

                var min = new Vector3(px - Overhang, g + 0.05f, pz - Overhang);
                var max = new Vector3(px + Overhang, Math.Min(bounds.max.y, g + coverColumn), pz + Overhang);
                total++;
                if (max.y <= min.y)
                {
                    continue;
                }

                if (!AnyTriangleInBox(context, min, max, true))
                {
                    uncovered++;
                    sumX += px;
                    sumZ += pz;
                }
            }

            if (total == 0)
            {
                return false;
            }

            float overhangArea = uncovered * raster * raster;
            float ratio = (float)uncovered / total;
            if (overhangArea < 0.15f && ratio < 0.35f)
            {
                return false;
            }

            Finding finding = NewFinding("A2", "collider_overhang", "suspect");
            finding.At(bounds.center.x, bounds.min.y, bounds.center.z);
            finding.Metric("overhang_m2", overhangArea);
            finding.Metric("overhang_ratio", ratio);
            finding.Metric("footprint_m2", total * raster * raster);
            finding.Metric("uncovered_x", uncovered > 0 ? sumX / uncovered : double.NaN);
            finding.Metric("uncovered_z", uncovered > 0 ? sumZ / uncovered : double.NaN);
            finding.Object(record.Path);
            finding.Note = record.Type;
            Locate(context, finding);
            context.Output.Add(finding);
            return true;
        }

        // ------------------------------------------------------------------
        // A3 — reachability.
        // ------------------------------------------------------------------

        private static int FindSeedCell(AuditContext context)
        {
            StandingGrid grid = context.Grid;
            Vector3 seed = context.SeedPoint;
            if (grid.TryCell(seed.x, seed.z, out int ix, out int iz) && grid.IsInside(ix, iz))
            {
                return grid.Index(ix, iz);
            }

            Vector3 closest = context.Mask.ClosestPoint(seed, HeroRadius);
            if (grid.TryCell(closest.x, closest.z, out ix, out iz) && grid.IsInside(ix, iz))
            {
                return grid.Index(ix, iz);
            }

            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < grid.CellCount; index++)
            {
                if (!grid.Has(index, StandingGrid.Inside))
                {
                    continue;
                }

                float dx = grid.CenterX(index % grid.NX) - seed.x;
                float dz = grid.CenterZ(index / grid.NX) - seed.z;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            return best;
        }

        private static bool PhysicsTraversable(StandingGrid grid, int a, int b)
        {
            const byte required = StandingGrid.Inside | StandingGrid.HasGround;
            if ((grid.Flags[a] & required) != required || (grid.Flags[b] & required) != required)
            {
                return false;
            }

            if (grid.Has(a, StandingGrid.Occupied) || grid.Has(b, StandingGrid.Occupied))
            {
                return false;
            }

            return Math.Abs(grid.GroundY[a] - grid.GroundY[b]) <= StepMax + 0.01f;
        }

        private static void RunA3(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            StandingGrid grid = context.Grid;
            int seed = FindSeedCell(context);
            var component = new int[grid.CellCount];
            for (int index = 0; index < component.Length; index++)
            {
                component[index] = -1;
            }

            var queue = new Queue<int>();
            int componentCount = 0;
            if (seed >= 0)
            {
                FloodMask(grid, seed, componentCount++, component, queue);
            }

            for (int index = 0; index < grid.CellCount; index++)
            {
                if (grid.Has(index, StandingGrid.Inside) && component[index] < 0)
                {
                    FloodMask(grid, index, componentCount++, component, queue);
                }
            }

            var pockets = new List<List<int>>();
            for (int c = 0; c < componentCount; c++)
            {
                pockets.Add(new List<int>());
            }

            for (int index = 0; index < grid.CellCount; index++)
            {
                if (component[index] >= 0)
                {
                    pockets[component[index]].Add(index);
                }
            }

            for (int c = 1; c < componentCount; c++)
            {
                EmitPocket(context, "unreachable_pocket", pockets[c], "mask component " + Json.Int(c));
            }

            // Physics pass inside the seed component.
            var physReached = new bool[grid.CellCount];
            if (seed >= 0)
            {
                FloodPhysics(grid, seed, physReached, queue, null);
            }

            var physicalPockets = new List<List<int>>();
            var pocketVisited = new bool[grid.CellCount];
            for (int index = 0; index < grid.CellCount; index++)
            {
                if (component[index] != 0 || physReached[index] || pocketVisited[index] ||
                    grid.Has(index, StandingGrid.Occupied) || !grid.Has(index, StandingGrid.HasGround))
                {
                    continue;
                }

                var pocket = new List<int>();
                FloodPhysics(grid, index, pocketVisited, queue, pocket);
                physicalPockets.Add(pocket);
            }

            foreach (List<int> pocket in physicalPockets)
            {
                Finding finding = EmitPocket(context, "physically_unreachable_pocket", pocket, "reachable by mask only");
                if (finding != null && pocket.Count < 4)
                {
                    finding.Tag = "suspect";
                }
            }

            // Step barriers between mask-connected cells.
            var cells = new List<int>();
            var axes = new List<int>();
            var deltas = new List<float>();
            for (int iz = 0; iz < grid.NZ; iz++)
            for (int ix = 0; ix < grid.NX; ix++)
            {
                int c = grid.Index(ix, iz);
                if (!grid.Has(c, StandingGrid.Inside) || !grid.Has(c, StandingGrid.HasGround) ||
                    grid.Has(c, StandingGrid.Occupied))
                {
                    continue;
                }

                for (int axis = 0; axis < 2; axis++)
                {
                    int nx = ix + (axis == 0 ? 1 : 0);
                    int nz = iz + (axis == 0 ? 0 : 1);
                    if (!grid.IsInside(nx, nz))
                    {
                        continue;
                    }

                    int n = grid.Index(nx, nz);
                    if (component[n] != component[c] || !grid.Has(n, StandingGrid.HasGround) ||
                        grid.Has(n, StandingGrid.Occupied))
                    {
                        continue;
                    }

                    float delta = Math.Abs(grid.GroundY[n] - grid.GroundY[c]);
                    if (delta > StepMax)
                    {
                        cells.Add(c);
                        axes.Add(axis);
                        deltas.Add(delta);
                    }
                }
            }

            List<CellCluster> runs = ClusterCells(grid, cells, axes, true);
            foreach (CellCluster run in runs)
            {
                var box = new BoxXZ();
                float sumX = 0f, sumZ = 0f, sumY = 0f;
                float deltaMax = 0f, deltaMin = float.PositiveInfinity;
                int axis = 0;
                foreach (int member in run.Members)
                {
                    int c = cells[member];
                    axis = axes[member];
                    float x = grid.CenterX(c % grid.NX) + (axis == 0 ? grid.Step * 0.5f : 0f);
                    float z = grid.CenterZ(c / grid.NX) + (axis == 1 ? grid.Step * 0.5f : 0f);
                    box.Add(x, z);
                    sumX += x;
                    sumZ += z;
                    sumY += grid.GroundY[c];
                    deltaMax = Math.Max(deltaMax, deltas[member]);
                    deltaMin = Math.Min(deltaMin, deltas[member]);
                }

                int count = run.Members.Count;
                Finding finding = NewFinding("A3", "mask_step_barrier", "new");
                finding.At(sumX / count, sumY / count, sumZ / count);
                finding.Dir = axis == 0 ? "+x" : "+z";
                finding.Metric("run_m", count * grid.Step);
                finding.Metric("delta_max", deltaMax);
                finding.Metric("delta_min", deltaMin);
                finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
                finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
                Locate(context, finding);
                context.Output.Add(finding);
            }

            context.Output.Count("A3_maskComponents", componentCount);
            context.Output.Count("A3_physicalPockets", physicalPockets.Count);
            context.Output.Timing("A3_reachability", timer.Elapsed.TotalMilliseconds);
            context.Log("A3: seed cell " + seed + ", " + componentCount + " mask components, " +
                        physicalPockets.Count + " physical pockets, " + runs.Count + " step barriers");
        }

        private static void FloodMask(StandingGrid grid, int start, int label, int[] component, Queue<int> queue)
        {
            queue.Clear();
            queue.Enqueue(start);
            component[start] = label;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int ix = c % grid.NX;
                int iz = c / grid.NX;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ix + DirX[d];
                    int nz = iz + DirZ[d];
                    if (!grid.IsInside(nx, nz))
                    {
                        continue;
                    }

                    int n = grid.Index(nx, nz);
                    if (component[n] >= 0)
                    {
                        continue;
                    }

                    component[n] = label;
                    queue.Enqueue(n);
                }
            }
        }

        private static void FloodPhysics(StandingGrid grid, int start, bool[] visited, Queue<int> queue, List<int> members)
        {
            queue.Clear();
            queue.Enqueue(start);
            visited[start] = true;
            members?.Add(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int ix = c % grid.NX;
                int iz = c / grid.NX;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ix + DirX[d];
                    int nz = iz + DirZ[d];
                    if (!grid.InGrid(nx, nz))
                    {
                        continue;
                    }

                    int n = grid.Index(nx, nz);
                    if (visited[n] || !PhysicsTraversable(grid, c, n))
                    {
                        continue;
                    }

                    visited[n] = true;
                    members?.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        private static Finding EmitPocket(AuditContext context, string subtype, List<int> cells, string note)
        {
            if (cells.Count == 0)
            {
                return null;
            }

            StandingGrid grid = context.Grid;
            var box = new BoxXZ();
            float sumX = 0f, sumZ = 0f;
            foreach (int cell in cells)
            {
                float x = grid.CenterX(cell % grid.NX);
                float z = grid.CenterZ(cell / grid.NX);
                box.Add(x, z);
                sumX += x;
                sumZ += z;
            }

            float centroidX = sumX / cells.Count;
            float centroidZ = sumZ / cells.Count;
            int sample = cells[0];
            float bestDistance = float.PositiveInfinity;
            foreach (int cell in cells)
            {
                float dx = grid.CenterX(cell % grid.NX) - centroidX;
                float dz = grid.CenterZ(cell / grid.NX) - centroidZ;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    sample = cell;
                }
            }

            Finding finding = NewFinding("A3", subtype, "new");
            finding.At(grid.CenterX(sample % grid.NX), grid.GroundY[sample], grid.CenterZ(sample / grid.NX));
            finding.Metric("m2", cells.Count * grid.CellArea);
            finding.Metric("cells", cells.Count);
            finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
            finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
            finding.Note = note;
            Locate(context, finding);
            context.Output.Add(finding);
            return finding;
        }

        // ------------------------------------------------------------------
        // B — gaps and seams.
        // ------------------------------------------------------------------

        private static void RunB(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            StandingGrid grid = context.Grid;
            var holeCells = new List<int>();
            var mismatchCells = new List<int>();
            var mismatchDelta = new List<float>();
            var mismatchBeach = new List<bool>();
            var visualHoleCells = new List<int>();
            var visualHoleCovered = new List<bool>();
            var doubleCells = new List<int>();
            var doublePaths = new List<string>();
            var pierceCells = new List<int>();
            var pierceDelta = new List<float>();
            var renderersAtCell = new List<int>();
            var heightsAtCell = new List<float>();

            for (int iz = 0; iz < grid.NZ; iz++)
            for (int ix = 0; ix < grid.NX; ix++)
            {
                int c = grid.Index(ix, iz);
                if (!grid.Has(c, StandingGrid.Inside))
                {
                    continue;
                }

                float x = grid.CenterX(ix);
                float z = grid.CenterZ(iz);
                if (!grid.Has(c, StandingGrid.HasGround))
                {
                    holeCells.Add(c);
                    continue;
                }

                float g = grid.GroundY[c];
                float expected = grid.ExpectedY[c];
                int groundCollider = grid.GroundCollider[c];
                bool beach = groundCollider >= 0 &&
                             context.Colliders.Records[groundCollider].Name == "Beach";
                if (!float.IsNaN(expected))
                {
                    float delta = g - expected;
                    float threshold = beach ? BeachMismatch : HeightMismatch;
                    if (Math.Abs(delta) > threshold)
                    {
                        mismatchCells.Add(c);
                        mismatchDelta.Add(delta);
                        mismatchBeach.Add(beach);
                    }
                }

                // B3/B4: which drawn ground contains this point?
                var p = new Vector3(x, g, z);
                context.Tris.QueryPoint(p, new Vector3(0.01f, 0.6f, 0.01f), context.Scratch);
                renderersAtCell.Clear();
                heightsAtCell.Clear();
                bool drawn = false;
                bool otherCover = false;
                float highest = float.NegativeInfinity;
                for (int index = 0; index < context.Scratch.Count; index++)
                {
                    ref Tri t = ref context.Tris.Tris[context.Scratch[index]];
                    if (!Geo.ContainsXZ(in t.A, in t.B, in t.C, x, z, out float h) ||
                        Math.Abs(h - g) > 0.6f)
                    {
                        continue;
                    }

                    if (!IsDrawnGroundClass(t.Class))
                    {
                        if (!IsFlatClass(t.Class))
                        {
                            otherCover = true;
                        }

                        continue;
                    }

                    drawn = true;
                    if (!renderersAtCell.Contains(t.Renderer))
                    {
                        renderersAtCell.Add(t.Renderer);
                        heightsAtCell.Add(h);
                    }
                    else
                    {
                        int slot = renderersAtCell.IndexOf(t.Renderer);
                        heightsAtCell[slot] = Math.Max(heightsAtCell[slot], h);
                    }

                    highest = Math.Max(highest, h);
                }

                if (!drawn)
                {
                    visualHoleCells.Add(c);
                    visualHoleCovered.Add(otherCover);
                }
                else if (renderersAtCell.Count >= 2)
                {
                    int near = 0;
                    var paths = new List<string>();
                    for (int index = 0; index < renderersAtCell.Count; index++)
                    {
                        if (highest - heightsAtCell[index] <= 0.02f)
                        {
                            near++;
                            paths.Add(context.Renderers.Records[renderersAtCell[index]].Path);
                        }
                    }

                    if (near >= 2)
                    {
                        paths.Sort(StringComparer.Ordinal);
                        doubleCells.Add(c);
                        doublePaths.Add(string.Join("|", paths));
                    }
                }

                // B6: terrain plan poking through a road or sidewalk slab.
                if (context.Layout != null &&
                    (grid.Has(c, StandingGrid.RoadRect) || grid.Has(c, StandingGrid.SidewalkRect)))
                {
                    CitySurfaceRole role = grid.Has(c, StandingGrid.SidewalkRect)
                        ? CitySurfaceRole.SidewalkTop
                        : CitySurfaceRole.RoadTop;
                    var xz = new Vector2(x, z);
                    if (CityTerrainSurfacePlan.TrySampleGroundTop(context.Layout, xz, out float groundTop, out _) &&
                        context.Layout.ElevationPlan.TrySampleSurface(xz, role, out float slabTop, out _) &&
                        groundTop > slabTop - 0.005f)
                    {
                        pierceCells.Add(c);
                        pierceDelta.Add(groundTop - slabTop);
                    }
                }
            }

            foreach (CellCluster cluster in ClusterCells(grid, holeCells, null, false))
            {
                int waterCells = 0;
                foreach (int member in cluster.Members)
                {
                    if (grid.Has(holeCells[member], StandingGrid.Water))
                    {
                        waterCells++;
                    }
                }

                bool water = waterCells * 2 >= cluster.Members.Count;
                Finding finding = EmitCellCluster(
                    context, "B", "hole", water ? "by_design" : "new", cluster, holeCells, null, -1);
                finding.Metric("water_cells", waterCells);
                if (water)
                {
                    finding.Note = "water";
                }
            }

            foreach (CellCluster cluster in ClusterCells(grid, mismatchCells, null, false))
            {
                int worst = -1;
                float worstAbs = -1f;
                float sum = 0f;
                bool beach = true;
                foreach (int member in cluster.Members)
                {
                    float delta = mismatchDelta[member];
                    sum += delta;
                    beach &= mismatchBeach[member];
                    if (Math.Abs(delta) > worstAbs)
                    {
                        worstAbs = Math.Abs(delta);
                        worst = member;
                    }
                }

                Finding finding = EmitCellCluster(
                    context, "B", "height_mismatch", beach ? "by_design" : "new",
                    cluster, mismatchCells, mismatchDelta, worst);
                finding.Metric("delta_mean", sum / cluster.Members.Count);
                int worstCell = mismatchCells[worst];
                finding.Metric("expected_y", grid.ExpectedY[worstCell]);
                if (grid.GroundCollider[worstCell] >= 0)
                {
                    finding.Object(context.Colliders.Records[grid.GroundCollider[worstCell]].Path);
                }

                if (beach)
                {
                    finding.Note = "coarse collision skin";
                }
            }

            foreach (CellCluster cluster in ClusterCells(grid, visualHoleCells, null, false))
            {
                bool covered = true;
                foreach (int member in cluster.Members)
                {
                    covered &= visualHoleCovered[member];
                }

                Finding finding = EmitCellCluster(
                    context, "B", "visual_hole", covered ? "suspect" : "new",
                    cluster, visualHoleCells, null, -1);
                int sampleCell = visualHoleCells[cluster.Members[0]];
                if (grid.GroundCollider[sampleCell] >= 0)
                {
                    finding.Object(context.Colliders.Records[grid.GroundCollider[sampleCell]].Path);
                }

                if (covered)
                {
                    finding.Note = "covered by non-ground geometry";
                }
            }

            foreach (CellCluster cluster in ClusterCells(grid, doubleCells, null, false))
            {
                Finding finding = EmitCellCluster(
                    context, "B", "double_ground", "new", cluster, doubleCells, null, -1);
                string[] paths = doublePaths[cluster.Members[0]].Split('|');
                foreach (string path in paths)
                {
                    finding.Object(path);
                }
            }

            foreach (CellCluster cluster in ClusterCells(grid, pierceCells, null, false))
            {
                int worst = -1;
                float worstDelta = float.NegativeInfinity;
                foreach (int member in cluster.Members)
                {
                    if (pierceDelta[member] > worstDelta)
                    {
                        worstDelta = pierceDelta[member];
                        worst = member;
                    }
                }

                EmitCellCluster(
                    context, "B", "ground_pierces_slab", "new", cluster, pierceCells, pierceDelta, worst);
            }

            int seams = RunSeams(context);
            context.Output.Count("B_holeCells", holeCells.Count);
            context.Output.Count("B_mismatchCells", mismatchCells.Count);
            context.Output.Count("B_visualHoleCells", visualHoleCells.Count);
            context.Output.Count("B_doubleGroundCells", doubleCells.Count);
            context.Output.Count("B_pierceCells", pierceCells.Count);
            context.Output.Timing("B_gaps", timer.Elapsed.TotalMilliseconds);
            context.Log("B: " + holeCells.Count + " hole cells, " + mismatchCells.Count +
                        " mismatch cells, " + visualHoleCells.Count + " visual-hole cells, " +
                        doubleCells.Count + " double-ground cells, " + pierceCells.Count +
                        " pierce cells, " + seams + " seams");
        }

        /// <summary>
        /// Emits one finding for a cluster of grid cells: sample at the
        /// worst member (or the member nearest the centroid), area, box.
        /// </summary>
        private static Finding EmitCellCluster(
            AuditContext context,
            string category,
            string subtype,
            string tag,
            CellCluster cluster,
            List<int> cells,
            List<float> metric,
            int worstMember)
        {
            StandingGrid grid = context.Grid;
            var box = new BoxXZ();
            float sumX = 0f, sumZ = 0f;
            foreach (int member in cluster.Members)
            {
                int cell = cells[member];
                float x = grid.CenterX(cell % grid.NX);
                float z = grid.CenterZ(cell / grid.NX);
                box.Add(x, z);
                sumX += x;
                sumZ += z;
            }

            int count = cluster.Members.Count;
            int sample = worstMember;
            if (sample < 0)
            {
                float centroidX = sumX / count;
                float centroidZ = sumZ / count;
                float best = float.PositiveInfinity;
                foreach (int member in cluster.Members)
                {
                    int cell = cells[member];
                    float dx = grid.CenterX(cell % grid.NX) - centroidX;
                    float dz = grid.CenterZ(cell / grid.NX) - centroidZ;
                    float distance = dx * dx + dz * dz;
                    if (distance < best)
                    {
                        best = distance;
                        sample = member;
                    }
                }
            }

            int sampleCell = cells[sample];
            Finding finding = NewFinding(category, subtype, tag);
            finding.At(
                grid.CenterX(sampleCell % grid.NX),
                grid.GroundY[sampleCell],
                grid.CenterZ(sampleCell / grid.NX));
            if (metric != null)
            {
                finding.Metric("delta", metric[sample]);
            }

            finding.Metric("m2", count * grid.CellArea);
            finding.Metric("cells", count);
            finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
            finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
            Locate(context, finding);
            context.Output.Add(finding);
            return finding;
        }

        private struct SeamEdge
        {
            public Vector3 A;
            public Vector3 B;
            public int KeyA;
            public int KeyB;
            public int Owner;
        }

        /// <summary>B5: boundary edges of drawn ground no other geometry covers.</summary>
        private static int RunSeams(AuditContext context)
        {
            const float quantum = 0.0005f;
            TriangleIndex tris = context.Tris;
            int emitted = 0;
            int dropped = 0;
            float waterline = context.World != null && context.World.SeacoastPlan != null
                ? context.World.SeacoastPlan.Frame.WaterlineZ
                : float.NaN;
            Rect map = context.Layout != null ? context.Layout.MapWorldXZBounds : default;
            var buildingMasses = new List<Bounds>();
            foreach (ColliderRecord record in context.Colliders.Records)
            {
                if (record.Name == "Building Mass")
                {
                    buildingMasses.Add(record.Bounds);
                }
            }

            var edges = new Dictionary<long, SeamEdge>();
            var counts = new Dictionary<long, int>();
            var keys = new Dictionary<VKey, int>();
            var candidates = new List<SeamEdge>();
            foreach (RendererRecord record in context.Renderers.Records)
            {
                if (!IsSeamClass(record.Class) || record.TriangleCount == 0 ||
                    record.Class == SurfaceClass.SeaBed)
                {
                    continue;
                }

                edges.Clear();
                counts.Clear();
                keys.Clear();
                for (int t = record.TriStart; t < record.TriEnd; t++)
                {
                    ref Tri tri = ref tris.Tris[t];
                    int ka = KeyId(keys, tri.A, quantum);
                    int kb = KeyId(keys, tri.B, quantum);
                    int kc = KeyId(keys, tri.C, quantum);
                    AddEdge(edges, counts, ka, kb, tri.A, tri.B, t);
                    AddEdge(edges, counts, kb, kc, tri.B, tri.C, t);
                    AddEdge(edges, counts, kc, ka, tri.C, tri.A, t);
                }

                int candidateStart = candidates.Count;
                foreach (KeyValuePair<long, SeamEdge> pair in edges)
                {
                    if (counts[pair.Key] != 1)
                    {
                        continue;
                    }

                    SeamEdge edge = pair.Value;
                    Vector3 mid = (edge.A + edge.B) * 0.5f;
                    if (context.Layout != null &&
                        Geo.DistanceToRectPerimeter(map, mid.x, mid.z) <= 0.5f)
                    {
                        continue;
                    }

                    if (record.IsBeach && !float.IsNaN(waterline) && mid.z >= waterline - 0.05f)
                    {
                        continue;
                    }

                    bool uncovered = true;
                    for (int sample = 1; sample <= 3 && uncovered; sample++)
                    {
                        Vector3 p = Vector3.Lerp(edge.A, edge.B, sample * 0.25f);
                        if (InsideBuildingMass(buildingMasses, p))
                        {
                            uncovered = false;
                            break;
                        }

                        tris.QueryPoint(p, SeamCover, context.Scratch);
                        for (int index = 0; index < context.Scratch.Count; index++)
                        {
                            int other = context.Scratch[index];
                            if (other == edge.Owner)
                            {
                                continue;
                            }

                            ref Tri o = ref tris.Tris[other];
                            if (Geo.PointTriDistanceSq(p, o.A, o.B, o.C) <= SeamCover * SeamCover)
                            {
                                uncovered = false;
                                break;
                            }
                        }
                    }

                    if (uncovered)
                    {
                        candidates.Add(edge);
                    }
                }

                // Chain this renderer's candidate edges into polylines.
                int candidateCount = candidates.Count - candidateStart;
                if (candidateCount == 0)
                {
                    continue;
                }

                var union = new UnionFind(candidateCount);
                var firstByKey = new Dictionary<int, int>();
                for (int local = 0; local < candidateCount; local++)
                {
                    SeamEdge edge = candidates[candidateStart + local];
                    Link(firstByKey, union, edge.KeyA, local);
                    Link(firstByKey, union, edge.KeyB, local);
                }

                var chains = new Dictionary<int, List<int>>();
                for (int local = 0; local < candidateCount; local++)
                {
                    int root = union.Find(local);
                    if (!chains.TryGetValue(root, out List<int> chain))
                    {
                        chain = new List<int>();
                        chains.Add(root, chain);
                    }

                    chain.Add(candidateStart + local);
                }

                foreach (KeyValuePair<int, List<int>> chain in chains)
                {
                    float length = 0f;
                    float longest = -1f;
                    Vector3 sample = Vector3.zero;
                    var box = new BoxXZ();
                    float gap = float.PositiveInfinity;
                    foreach (int edgeIndex in chain.Value)
                    {
                        SeamEdge edge = candidates[edgeIndex];
                        float edgeLength = Vector3.Distance(edge.A, edge.B);
                        length += edgeLength;
                        box.Add(edge.A.x, edge.A.z);
                        box.Add(edge.B.x, edge.B.z);
                        Vector3 mid = (edge.A + edge.B) * 0.5f;
                        if (edgeLength > longest ||
                            (edgeLength == longest &&
                             (mid.x < sample.x || (mid.x == sample.x && mid.z < sample.z))))
                        {
                            longest = edgeLength;
                            sample = mid;
                        }

                        gap = Math.Min(gap, NearestOtherGround(context, mid, edge.Owner, 0.5f));
                    }

                    if (length < MinSeamLength)
                    {
                        dropped++;
                        continue;
                    }

                    Finding finding = NewFinding("B", "open_seam", "new");
                    finding.At(sample.x, sample.y, sample.z);
                    finding.Metric("length_m", length);
                    finding.Metric("gap_mm", float.IsPositiveInfinity(gap) ? 500.0 : gap * 1000.0);
                    finding.Metric("edges", chain.Value.Count);
                    finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
                    finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
                    finding.Object(record.Path);
                    finding.Note = record.Class.ToString();
                    Locate(context, finding);
                    context.Output.Add(finding);
                    emitted++;
                }
            }

            context.Output.Count("B_seamCandidates", candidates.Count);
            context.Output.Count("B_seamsDroppedShort", dropped);
            return emitted;
        }

        private static int KeyId(Dictionary<VKey, int> keys, Vector3 position, float quantum)
        {
            VKey key = VKey.Of(position, quantum);
            if (!keys.TryGetValue(key, out int id))
            {
                id = keys.Count;
                keys.Add(key, id);
            }

            return id;
        }

        private static void AddEdge(
            Dictionary<long, SeamEdge> edges,
            Dictionary<long, int> counts,
            int ka,
            int kb,
            Vector3 a,
            Vector3 b,
            int owner)
        {
            if (ka == kb)
            {
                return;
            }

            long key = ka < kb ? ((long)ka << 32) | (uint)kb : ((long)kb << 32) | (uint)ka;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
            if (count == 0)
            {
                edges[key] = new SeamEdge { A = a, B = b, KeyA = ka, KeyB = kb, Owner = owner };
            }
        }

        private static void Link(Dictionary<int, int> firstByKey, UnionFind union, int key, int local)
        {
            if (firstByKey.TryGetValue(key, out int first))
            {
                union.Union(first, local);
            }
            else
            {
                firstByKey.Add(key, local);
            }
        }

        private static bool InsideBuildingMass(List<Bounds> masses, Vector3 p)
        {
            for (int index = 0; index < masses.Count; index++)
            {
                Bounds b = masses[index];
                if (p.x >= b.min.x && p.x <= b.max.x &&
                    p.z >= b.min.z && p.z <= b.max.z &&
                    p.y <= b.max.y + 0.1f && p.y >= b.min.y - 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        private static float NearestOtherGround(AuditContext context, Vector3 p, int owner, float reach)
        {
            TriangleIndex tris = context.Tris;
            int ownerRenderer = tris.Tris[owner].Renderer;
            int ownerComponent = tris.Tris[owner].Component;
            tris.QueryPoint(p, reach, context.Scratch);
            float best = float.PositiveInfinity;
            for (int index = 0; index < context.Scratch.Count; index++)
            {
                ref Tri t = ref tris.Tris[context.Scratch[index]];
                if (!IsSeamClass(t.Class) ||
                    (t.Renderer == ownerRenderer && t.Component == ownerComponent))
                {
                    continue;
                }

                float distance = Geo.PointTriDistanceSq(p, t.A, t.B, t.C);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return float.IsPositiveInfinity(best) ? best : (float)Math.Sqrt(best);
        }

        // ------------------------------------------------------------------
        // C — coplanar overlapping surfaces.
        // ------------------------------------------------------------------

        private sealed class ZCluster
        {
            public int RendererA;
            public int RendererB;
            public bool Anti;
            public int Pairs;
            public double Area;
            public float SepMin = float.PositiveInfinity;
            public readonly List<float> Seps = new List<float>();
            public float LargestArea = -1f;
            public Vector3 Example;
            public BoxXZ Box;
        }

        private struct ZPair
        {
            public float Area;
            public float Sep;
            public Vector3 Point;
            public int RendererA;
            public int RendererB;
        }

        private static long ZKey(Vector3Int nq, int x, int y, int z)
        {
            long packed = ((long)(nq.x + 64) << 16) | ((long)(nq.y + 64) << 8) | (long)(nq.z + 64);
            return (packed << 36) | ((long)(x + 2048) << 24) | ((long)(y + 2048) << 12) | (long)(z + 2048);
        }

        private static Vector3Int QuantizeNormal(Vector3 n)
        {
            return new Vector3Int(
                Mathf.Clamp(Mathf.RoundToInt(n.x * 32f), -32, 32),
                Mathf.Clamp(Mathf.RoundToInt(n.y * 32f), -32, 32),
                Mathf.Clamp(Mathf.RoundToInt(n.z * 32f), -32, 32));
        }

        private static void PlaneBasis(Vector3 n, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(n, Math.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            v = Vector3.Cross(n, u);
        }

        private static void RunC(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            TriangleIndex tris = context.Tris;
            int count = tris.Count;
            float maxY = float.PositiveInfinity;
            string maxYText = Environment.GetEnvironmentVariable(ZFightMaxYEnv);
            if (!string.IsNullOrEmpty(maxYText) &&
                float.TryParse(maxYText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                maxY = parsed;
            }

            var eligible = new bool[Math.Max(1, count)];
            int noDepth = 0;
            int droppedHigh = 0;
            for (int t = 0; t < count; t++)
            {
                ref Tri tri = ref tris.Tris[t];
                RendererRecord record = context.Renderers.Records[tri.Renderer];
                int submesh = Math.Min(tri.Submesh, record.WritesDepth.Length - 1);
                if (submesh >= 0 && !record.WritesDepth[submesh])
                {
                    noDepth++;
                    continue;
                }

                float minY = Math.Min(tri.A.y, Math.Min(tri.B.y, tri.C.y));
                if (minY > maxY)
                {
                    droppedHigh++;
                    continue;
                }

                eligible[t] = true;
            }

            // Two levels, like the triangle index: a facade or water-sheet triangle
            // spanning thousands of 1 m cells goes into a 16 m hash instead, or the
            // fine hash alone needs more memory than the editor has.
            var hashes = new[] { new CsrHash(), new CsrHash() };
            float[] cellSizes = { 1f, TriangleIndex.CoarseCellSize };
            var isBig = new bool[Math.Max(1, count)];
            var pad = new Vector3(ZPossible, ZPossible, ZPossible);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int t = 0; t < count; t++)
                {
                    if (!eligible[t])
                    {
                        continue;
                    }

                    ref Tri tri = ref tris.Tris[t];
                    Vector3Int nq = QuantizeNormal(tri.N);
                    TriangleIndex.TriBounds(in tri, out Vector3 min, out Vector3 max);
                    min -= pad;
                    max += pad;
                    if (pass == 0)
                    {
                        long fineCells = (long)(FloorCell(max.x, 1f) - FloorCell(min.x, 1f) + 1) *
                                         (FloorCell(max.y, 1f) - FloorCell(min.y, 1f) + 1) *
                                         (FloorCell(max.z, 1f) - FloorCell(min.z, 1f) + 1);
                        isBig[t] = fineCells > TriangleIndex.BigCellLimit;
                    }

                    int level = isBig[t] ? 1 : 0;
                    float size = cellSizes[level];
                    CsrHash hash = hashes[level];
                    int x0 = FloorCell(min.x, size), x1 = FloorCell(max.x, size);
                    int y0 = FloorCell(min.y, size), y1 = FloorCell(max.y, size);
                    int z0 = FloorCell(min.z, size), z1 = FloorCell(max.z, size);
                    for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (pass == 0)
                        {
                            hash.Count(ZKey(nq, x, y, z));
                        }
                        else
                        {
                            hash.Fill(ZKey(nq, x, y, z), t);
                        }
                    }
                }

                if (pass == 0)
                {
                    hashes[0].Finish();
                    hashes[1].Finish();
                }
            }

            context.Log("C: fine hash " + hashes[0].SlotCount + " slots, " + hashes[0].EntryCount +
                        " entries; coarse hash " + hashes[1].SlotCount + " slots, " + hashes[1].EntryCount + " entries");

            var accepted = new HashSet<long>();
            var clusters = new Dictionary<long, ZCluster>();
            var topPairs = new List<ZPair>();
            var subject = new Vector2[3];
            var clip = new Vector2[3];
            var work1 = new List<Vector2>(8);
            var work2 = new List<Vector2>(8);
            long tested = 0;
            int acceptedCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (!eligible[i])
                {
                    continue;
                }

                if (i > 0 && i % 200000 == 0)
                {
                    context.Log("C: " + i + "/" + count + " triangles, " + acceptedCount + " pairs, " +
                                timer.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
                }

                ref Tri ti = ref tris.Tris[i];
                RendererRecord recordI = context.Renderers.Records[ti.Renderer];
                bool cullOff = recordI.Cull[Math.Max(0, Math.Min(ti.Submesh, recordI.Cull.Length - 1))] == 0f;
                Vector3Int nq = QuantizeNormal(ti.N);
                TriangleIndex.TriBounds(in ti, out Vector3 minI, out Vector3 maxI);
                Vector3 emin = minI - pad;
                Vector3 emax = maxI + pad;
                bool bigI = isBig[i];
                // Pair ordering, one test per unordered pair: small-small and big-big
                // from the lower index; small-big always from the small side (a big
                // triangle never walks the fine hash — its 1 m range is huge).
                for (int level = bigI ? 1 : 0; level < 2; level++)
                {
                    CsrHash hash = hashes[level];
                    int[] entries = hash.Entries;
                    float size = cellSizes[level];
                    int x0 = FloorCell(emin.x, size), x1 = FloorCell(emax.x, size);
                    int y0 = FloorCell(emin.y, size), y1 = FloorCell(emax.y, size);
                    int z0 = FloorCell(emin.z, size), z1 = FloorCell(emax.z, size);
                    bool orderedLevel = level == 0 || bigI;
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    if (sign < 0 && !cullOff)
                    {
                        break;
                    }

                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var probe = new Vector3Int(
                            Mathf.Clamp(sign * nq.x + dx, -32, 32),
                            Mathf.Clamp(sign * nq.y + dy, -32, 32),
                            Mathf.Clamp(sign * nq.z + dz, -32, 32));
                        for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        for (int z = z0; z <= z1; z++)
                        {
                            if (!hash.Lookup(ZKey(probe, x, y, z), out int start, out int end))
                            {
                                continue;
                            }

                            for (int slot = start; slot < end; slot++)
                            {
                                int j = entries[slot];
                                if (j == i || (sign > 0 && orderedLevel && j < i))
                                {
                                    continue;
                                }

                                long pairKey = i < j
                                    ? ((long)i << 32) | (uint)j
                                    : ((long)j << 32) | (uint)i;
                                if (accepted.Contains(pairKey))
                                {
                                    continue;
                                }

                                tested++;
                                ref Tri tj = ref tris.Tris[j];
                                TriangleIndex.TriBounds(in tj, out Vector3 minJ, out Vector3 maxJ);
                                if (minJ.x > emax.x || maxJ.x < emin.x ||
                                    minJ.y > emax.y || maxJ.y < emin.y ||
                                    minJ.z > emax.z || maxJ.z < emin.z)
                                {
                                    continue;
                                }

                                float dot = Vector3.Dot(ti.N, tj.N);
                                bool anti;
                                if (sign > 0)
                                {
                                    if (dot < 0.999f)
                                    {
                                        continue;
                                    }

                                    anti = false;
                                }
                                else
                                {
                                    if (dot > -0.999f)
                                    {
                                        continue;
                                    }

                                    anti = true;
                                }

                                float sep = Math.Max(
                                    Math.Abs(Vector3.Dot(ti.N, tj.A) - ti.D),
                                    Math.Max(
                                        Math.Abs(Vector3.Dot(ti.N, tj.B) - ti.D),
                                        Math.Abs(Vector3.Dot(ti.N, tj.C) - ti.D)));
                                if (sep > ZPossible)
                                {
                                    continue;
                                }

                                PlaneBasis(ti.N, out Vector3 u, out Vector3 v);
                                clip[0] = new Vector2(Vector3.Dot(ti.A, u), Vector3.Dot(ti.A, v));
                                clip[1] = new Vector2(Vector3.Dot(ti.B, u), Vector3.Dot(ti.B, v));
                                clip[2] = new Vector2(Vector3.Dot(ti.C, u), Vector3.Dot(ti.C, v));
                                subject[0] = new Vector2(Vector3.Dot(tj.A, u), Vector3.Dot(tj.A, v));
                                subject[1] = new Vector2(Vector3.Dot(tj.B, u), Vector3.Dot(tj.B, v));
                                subject[2] = new Vector2(Vector3.Dot(tj.C, u), Vector3.Dot(tj.C, v));
                                float area = Geo.ClipArea(subject, clip, work1, work2, out Vector2 centroid);
                                if (area < MinOverlapArea)
                                {
                                    continue;
                                }

                                accepted.Add(pairKey);
                                acceptedCount++;
                                Vector3 point = u * centroid.x + v * centroid.y + ti.N * ti.D;
                                int ra = Math.Min(ti.Renderer, tj.Renderer);
                                int rb = Math.Max(ti.Renderer, tj.Renderer);
                                long clusterKey = (((long)ra << 32) | (uint)rb) * 2L + (anti ? 1L : 0L);
                                if (!clusters.TryGetValue(clusterKey, out ZCluster cluster))
                                {
                                    cluster = new ZCluster { RendererA = ra, RendererB = rb, Anti = anti };
                                    clusters.Add(clusterKey, cluster);
                                }

                                cluster.Pairs++;
                                cluster.Area += area;
                                cluster.SepMin = Math.Min(cluster.SepMin, sep);
                                cluster.Seps.Add(sep);
                                cluster.Box.Add(point.x, point.z);
                                if (area > cluster.LargestArea)
                                {
                                    cluster.LargestArea = area;
                                    cluster.Example = point;
                                }

                                InsertTopPair(topPairs, new ZPair
                                {
                                    Area = area,
                                    Sep = sep,
                                    Point = point,
                                    RendererA = ra,
                                    RendererB = rb
                                });
                            }
                        }
                    }
                }
                }
            }

            foreach (KeyValuePair<long, ZCluster> pair in clusters)
            {
                ZCluster cluster = pair.Value;
                RendererRecord a = context.Renderers.Records[cluster.RendererA];
                RendererRecord b = context.Renderers.Records[cluster.RendererB];
                string subtype = "zfight_cluster";
                string tag;
                string annotation = AnnotateZ(a, b, out tag);
                if (cluster.RendererA == cluster.RendererB)
                {
                    subtype = "intra_batch";
                    tag = "new";
                    annotation = "same renderer";
                }
                else if (cluster.Anti)
                {
                    subtype = "coplanar_double_sided";
                }

                cluster.Seps.Sort();
                float median = cluster.Seps[cluster.Seps.Count / 2];
                string tier = cluster.SepMin <= ZCertain ? "certain" : cluster.SepMin <= ZLikely ? "likely" : "possible";
                Finding finding = NewFinding("C", subtype, tag);
                finding.At(cluster.Example.x, cluster.Example.y, cluster.Example.z);
                finding.Metric("overlap_m2", cluster.Area);
                finding.Metric("pairs", cluster.Pairs);
                finding.Metric("sep_min", cluster.SepMin);
                finding.Metric("sep_median", median);
                finding.Metric("largest_pair_m2", cluster.LargestArea);
                finding.Metric("xMin", cluster.Box.XMin).Metric("xMax", cluster.Box.XMax);
                finding.Metric("zMin", cluster.Box.ZMin).Metric("zMax", cluster.Box.ZMax);
                finding.Object(a.Path).Object(b.Path);
                finding.Note = "tier=" + tier + "; " + annotation + "; " + a.Class + "/" + b.Class;
                Locate(context, finding);
                context.Output.Add(finding);
            }

            foreach (ZPair pair in topPairs)
            {
                RendererRecord a = context.Renderers.Records[pair.RendererA];
                RendererRecord b = context.Renderers.Records[pair.RendererB];
                string annotation = AnnotateZ(a, b, out string tag);
                Finding finding = NewFinding("C", "zfight_pair", tag);
                finding.At(pair.Point.x, pair.Point.y, pair.Point.z);
                finding.Metric("overlap_m2", pair.Area);
                finding.Metric("sep", pair.Sep);
                finding.Object(a.Path).Object(b.Path);
                finding.Note = annotation;
                Locate(context, finding);
                context.Output.Add(finding);
            }

            int duplicates = RunDuplicateTriangles(context, eligible);
            int swash = RunSwash(context);
            context.Output.Count("C_noDepthWrite", noDepth);
            context.Output.Count("C_droppedAboveMaxY", droppedHigh);
            context.Output.Count("C_pairsTested", tested);
            context.Output.Count("C_pairsAccepted", acceptedCount);
            context.Output.Count("C_clusters", clusters.Count);
            context.Output.Timing("C_zfight", timer.Elapsed.TotalMilliseconds);
            context.Log("C: " + tested + " pairs tested, " + acceptedCount + " accepted, " +
                        clusters.Count + " clusters, " + duplicates + " duplicate triangles, " +
                        swash + " buried swash meshes");
        }

        private static string AnnotateZ(RendererRecord a, RendererRecord b, out string tag)
        {
            SurfaceClass ca = a.Class;
            SurfaceClass cb = b.Class;
            bool puddleRoad = (ca == SurfaceClass.Puddle && (cb == SurfaceClass.Road || cb == SurfaceClass.Sidewalk)) ||
                              (cb == SurfaceClass.Puddle && (ca == SurfaceClass.Road || ca == SurfaceClass.Sidewalk));
            if (puddleRoad)
            {
                tag = "by_design";
                return "puddle over road (expected 5 mm)";
            }

            bool markingRoad = (ca == SurfaceClass.Marking && cb == SurfaceClass.Road) ||
                               (cb == SurfaceClass.Marking && ca == SurfaceClass.Road);
            if (markingRoad)
            {
                tag = "by_design";
                return "marking over road (expected 2.5 mm)";
            }

            bool publicGround = a.Name.StartsWith("Public Ground", StringComparison.Ordinal) ||
                                b.Name.StartsWith("Public Ground", StringComparison.Ordinal);
            if (publicGround && ca == SurfaceClass.Ground && cb == SurfaceClass.Ground)
            {
                tag = "suspect";
                return "poi pad rim";
            }

            tag = "new";
            return "unannotated";
        }

        private static void InsertTopPair(List<ZPair> top, ZPair pair)
        {
            const int cap = 50;
            if (top.Count >= cap && pair.Area <= top[top.Count - 1].Area)
            {
                return;
            }

            int slot = top.Count;
            while (slot > 0 && top[slot - 1].Area < pair.Area)
            {
                slot--;
            }

            top.Insert(slot, pair);
            if (top.Count > cap)
            {
                top.RemoveAt(top.Count - 1);
            }
        }

        private static int RunDuplicateTriangles(AuditContext context, bool[] eligible)
        {
            const float quantum = 0.0005f;
            TriangleIndex tris = context.Tris;
            var seen = new Dictionary<long, int>();
            int duplicates = 0;
            var keys = new VKey[3];
            for (int t = 0; t < tris.Count; t++)
            {
                if (!eligible[t])
                {
                    continue;
                }

                ref Tri tri = ref tris.Tris[t];
                keys[0] = VKey.Of(tri.A, quantum);
                keys[1] = VKey.Of(tri.B, quantum);
                keys[2] = VKey.Of(tri.C, quantum);
                Array.Sort(keys, CompareKeys);
                long hash = 1469598103934665603L;
                for (int k = 0; k < 3; k++)
                {
                    hash = (hash ^ keys[k].X) * 1099511628211L;
                    hash = (hash ^ keys[k].Y) * 1099511628211L;
                    hash = (hash ^ keys[k].Z) * 1099511628211L;
                }

                if (!seen.TryGetValue(hash, out int first))
                {
                    seen.Add(hash, t);
                    continue;
                }

                ref Tri original = ref tris.Tris[first];
                if (original.Renderer == tri.Renderer)
                {
                    continue;
                }

                duplicates++;
                Vector3 centroid = tri.Centroid;
                Finding finding = NewFinding("C", "duplicate_triangle", "new");
                finding.At(centroid.x, centroid.y, centroid.z);
                finding.Metric("area_m2", Vector3.Cross(tri.B - tri.A, tri.C - tri.A).magnitude * 0.5f);
                finding.Object(context.Renderers.Records[original.Renderer].Path);
                finding.Object(context.Renderers.Records[tri.Renderer].Path);
                Locate(context, finding);
                context.Output.Add(finding);
            }

            return duplicates;
        }

        private static int CompareKeys(VKey left, VKey right)
        {
            int result = left.X.CompareTo(right.X);
            if (result != 0)
            {
                return result;
            }

            result = left.Y.CompareTo(right.Y);
            return result != 0 ? result : left.Z.CompareTo(right.Z);
        }

        /// <summary>C3: swash vertices sunk under the drawn beach.</summary>
        private static int RunSwash(AuditContext context)
        {
            TriangleIndex tris = context.Tris;
            int emitted = 0;
            foreach (RendererRecord record in context.Renderers.Records)
            {
                if (!record.IsSwash || !record.Data.Readable)
                {
                    continue;
                }

                int buried = 0;
                int checkedVertices = 0;
                float maxDepth = 0f;
                Vector3 worst = Vector3.zero;
                Vector3[] vertices = record.Data.Vertices;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 p = record.LocalToWorld.MultiplyPoint3x4(vertices[index]);
                    tris.QueryPoint(p, new Vector3(0.001f, 5f, 0.001f), context.Scratch);
                    float beach = float.NaN;
                    for (int c = 0; c < context.Scratch.Count; c++)
                    {
                        ref Tri t = ref tris.Tris[context.Scratch[c]];
                        if (!context.Renderers.Records[t.Renderer].IsBeach)
                        {
                            continue;
                        }

                        if (Geo.ContainsXZ(in t.A, in t.B, in t.C, p.x, p.z, out float h) &&
                            (float.IsNaN(beach) || h > beach))
                        {
                            beach = h;
                        }
                    }

                    if (float.IsNaN(beach))
                    {
                        continue;
                    }

                    checkedVertices++;
                    if (p.y < beach + 0.002f)
                    {
                        buried++;
                        float depth = beach + 0.002f - p.y;
                        if (depth > maxDepth)
                        {
                            maxDepth = depth;
                            worst = p;
                        }
                    }
                }

                if (buried == 0)
                {
                    continue;
                }

                emitted++;
                Finding finding = NewFinding("C", "swash_buried", "new");
                finding.At(worst.x, worst.y, worst.z);
                finding.Metric("max_depth", maxDepth);
                finding.Metric("buried_vertices", buried);
                finding.Metric("checked_vertices", checkedVertices);
                finding.Object(record.Path);
                Locate(context, finding);
                context.Output.Add(finding);
            }

            return emitted;
        }

        // ------------------------------------------------------------------
        // D — placement.
        // ------------------------------------------------------------------

        private sealed class PropComponent
        {
            public int Renderer;
            public int Component;
            public Vector3 Min;
            public Vector3 Max;
            public readonly List<int> Tris = new List<int>();
            public float GroundRef = float.NaN;

            public float Height => Max.y - Min.y;
            public float Footprint => (Max.x - Min.x) * (Max.z - Min.z);
            public Vector3 Center => (Min + Max) * 0.5f;
        }

        private static void RunD(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            TriangleIndex tris = context.Tris;
            var byId = new Dictionary<int, PropComponent>();
            var components = new List<PropComponent>();
            foreach (RendererRecord record in context.Renderers.Records)
            {
                if (!IsPropClass(record.Class) || record.TriangleCount == 0)
                {
                    continue;
                }

                for (int t = record.TriStart; t < record.TriEnd; t++)
                {
                    ref Tri tri = ref tris.Tris[t];
                    if (!byId.TryGetValue(tri.Component, out PropComponent component))
                    {
                        component = new PropComponent
                        {
                            Renderer = record.Index,
                            Component = tri.Component,
                            Min = tri.A,
                            Max = tri.A
                        };
                        byId.Add(tri.Component, component);
                        components.Add(component);
                    }

                    component.Tris.Add(t);
                    TriangleIndex.TriBounds(in tri, out Vector3 min, out Vector3 max);
                    component.Min = Vector3.Min(component.Min, min);
                    component.Max = Vector3.Max(component.Max, max);
                }
            }

            var eligible = new List<PropComponent>();
            foreach (PropComponent component in components)
            {
                if (component.Height >= 0.05f && component.Footprint >= 0.01f)
                {
                    eligible.Add(component);
                }
            }

            List<Vector2[]> streetQuads = null;
            List<BoxXZ> streetBoxes = null;
            if (context.Streets != null)
            {
                streetQuads = new List<Vector2[]>();
                streetBoxes = new List<BoxXZ>();
                foreach (RuntimeOrientedBox box in context.Streets.StreetGeometry)
                {
                    Vector2[] corners = Geo.OrientedBoxCornersXZ(box);
                    var aabb = new BoxXZ();
                    foreach (Vector2 corner in corners)
                    {
                        aabb.Add(corner.x, corner.y);
                    }

                    streetQuads.Add(corners);
                    streetBoxes.Add(aabb);
                }
            }

            int floating = 0, sunk = 0, carriageway = 0, unknownGround = 0;
            var bottomPoints = new List<Vector3>(8);
            var pointKeys = new HashSet<VKey>();
            foreach (PropComponent component in eligible)
            {
                RendererRecord record = context.Renderers.Records[component.Renderer];
                CollectBottomPoints(tris, component, bottomPoints, pointKeys);
                float clearanceMin = float.PositiveInfinity;
                float groundMin = float.PositiveInfinity;
                bool allKnown = bottomPoints.Count > 0;
                bool sunkEverywhere = bottomPoints.Count > 0;
                foreach (Vector3 p in bottomPoints)
                {
                    float ground = GroundReference(context, p, record.Transform);
                    if (float.IsNaN(ground))
                    {
                        allKnown = false;
                        sunkEverywhere = false;
                        continue;
                    }

                    clearanceMin = Math.Min(clearanceMin, p.y - ground);
                    groundMin = Math.Min(groundMin, ground);
                    if (component.Max.y >= ground - Sunk)
                    {
                        sunkEverywhere = false;
                    }
                }

                component.GroundRef = float.IsPositiveInfinity(groundMin) ? float.NaN : groundMin;
                if (!allKnown)
                {
                    unknownGround++;
                }

                if (allKnown && clearanceMin > Floating && !SupportedBelow(context, component, bottomPoints))
                {
                    floating++;
                    Finding finding = NewFinding("D", "floating", "new");
                    Vector3 center = component.Center;
                    finding.At(center.x, component.Min.y, center.z);
                    finding.Metric("clearance_m", clearanceMin);
                    finding.Metric("height_m", component.Height);
                    finding.Metric("footprint_m2", component.Footprint);
                    finding.Object(record.Path);
                    finding.Note = "component " + Json.Int(component.Component);
                    Locate(context, finding);
                    context.Output.Add(finding);
                }

                if (sunkEverywhere)
                {
                    sunk++;
                    Finding finding = NewFinding("D", "sunk", "new");
                    Vector3 center = component.Center;
                    finding.At(center.x, component.Max.y, center.z);
                    finding.Metric("depth_m", groundMin - component.Max.y);
                    finding.Metric("height_m", component.Height);
                    finding.Metric("footprint_m2", component.Footprint);
                    finding.Object(record.Path);
                    finding.Note = "component " + Json.Int(component.Component);
                    Locate(context, finding);
                    context.Output.Add(finding);
                }

                if (streetQuads != null &&
                    record.Class != SurfaceClass.Road && record.Class != SurfaceClass.Marking &&
                    record.Class != SurfaceClass.Puddle && record.Class != SurfaceClass.Water)
                {
                    Vector3 center = component.Center;
                    float reference = float.IsNaN(component.GroundRef)
                        ? GroundAt(context, center.x, center.z)
                        : component.GroundRef;
                    if (!float.IsNaN(reference) && component.Min.y < reference + 1.2f)
                    {
                        var point = new Vector2(center.x, center.z);
                        for (int index = 0; index < streetQuads.Count; index++)
                        {
                            BoxXZ aabb = streetBoxes[index];
                            if (point.x < aabb.XMin || point.x > aabb.XMax ||
                                point.y < aabb.ZMin || point.y > aabb.ZMax)
                            {
                                continue;
                            }

                            if (Geo.PointInConvexXZ(point, streetQuads[index]))
                            {
                                carriageway++;
                                Finding finding = NewFinding("D", "on_carriageway", "new");
                                finding.At(center.x, component.Min.y, center.z);
                                finding.Metric("height_m", component.Height);
                                finding.Metric("footprint_m2", component.Footprint);
                                finding.Metric("above_ground_m", component.Min.y - reference);
                                finding.Object(record.Path);
                                finding.Note = "street box " + Json.Int(index);
                                Locate(context, finding);
                                context.Output.Add(finding);
                                break;
                            }
                        }
                    }
                }
            }

            int interpenetrations = RunInterpenetration(context, eligible);
            int blocks = RunBlocksAccess(context);
            context.Output.Count("D_components", components.Count);
            context.Output.Count("D_eligibleComponents", eligible.Count);
            context.Output.Count("D_unknownGround", unknownGround);
            context.Output.Timing("D_placement", timer.Elapsed.TotalMilliseconds);
            context.Log("D: " + eligible.Count + "/" + components.Count + " components, " + floating +
                        " floating, " + sunk + " sunk, " + carriageway + " on carriageway, " +
                        interpenetrations + " interpenetrations, " + blocks + " access blockers");
        }

        private static void CollectBottomPoints(
            TriangleIndex tris,
            PropComponent component,
            List<Vector3> points,
            HashSet<VKey> keys)
        {
            points.Clear();
            keys.Clear();
            var candidates = new List<Vector3>();
            float limit = component.Min.y + 0.02f;
            foreach (int t in component.Tris)
            {
                ref Tri tri = ref tris.Tris[t];
                AddBottomPoint(tri.A, limit, candidates, keys);
                AddBottomPoint(tri.B, limit, candidates, keys);
                AddBottomPoint(tri.C, limit, candidates, keys);
            }

            if (candidates.Count <= 8)
            {
                points.AddRange(candidates);
                return;
            }

            float stride = candidates.Count / 8f;
            for (int index = 0; index < 8; index++)
            {
                points.Add(candidates[(int)(index * stride)]);
            }
        }

        private static void AddBottomPoint(Vector3 p, float limit, List<Vector3> points, HashSet<VKey> keys)
        {
            if (p.y > limit)
            {
                return;
            }

            if (keys.Add(VKey.Of(p, 0.001f)))
            {
                points.Add(p);
            }
        }

        private static float GroundReference(AuditContext context, Vector3 p, Transform own)
        {
            float best = HighestAuditedHit(context, new Vector3(p.x, p.y + 0.05f, p.z), 6f, own);
            if (!float.IsNaN(best))
            {
                return best;
            }

            StandingGrid grid = context.Grid;
            if (grid.TryCell(p.x, p.z, out int ix, out int iz))
            {
                float expected = grid.ExpectedY[grid.Index(ix, iz)];
                if (!float.IsNaN(expected))
                {
                    return expected;
                }
            }

            return HighestAuditedHit(context, new Vector3(p.x, p.y + 40f, p.z), 80f, own);
        }

        private static float HighestAuditedHit(AuditContext context, Vector3 origin, float reach, Transform own)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                origin, Vector3.down, reach, StaticMask, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider collider = hits[index].collider;
                if (!context.IsAudited(collider, out _))
                {
                    continue;
                }

                if (own != null && IsUnder(collider.transform, own))
                {
                    continue;
                }

                float y = hits[index].point.y;
                if (y > origin.y)
                {
                    continue;
                }

                if (float.IsNaN(best) || y > best)
                {
                    best = y;
                }
            }

            return best;
        }

        private static bool SupportedBelow(AuditContext context, PropComponent component, List<Vector3> points)
        {
            TriangleIndex tris = context.Tris;
            foreach (Vector3 p in points)
            {
                tris.QueryBox(
                    new Vector3(p.x - 0.03f, p.y - 0.03f, p.z - 0.03f),
                    new Vector3(p.x + 0.03f, p.y + 0.001f, p.z + 0.03f),
                    context.Scratch);
                for (int index = 0; index < context.Scratch.Count; index++)
                {
                    ref Tri t = ref tris.Tris[context.Scratch[index]];
                    if (t.Renderer == component.Renderer && t.Component == component.Component)
                    {
                        continue;
                    }

                    if (Geo.PointTriDistanceSq(p, t.A, t.B, t.C) <= 0.03f * 0.03f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int RunInterpenetration(AuditContext context, List<PropComponent> components)
        {
            TriangleIndex tris = context.Tris;
            var buckets = new Dictionary<long, List<int>>();
            int skippedLarge = 0;
            int skippedSiblings = 0;
            for (int index = 0; index < components.Count; index++)
            {
                PropComponent component = components[index];
                int x0 = FloorCell(component.Min.x, 1f), x1 = FloorCell(component.Max.x, 1f);
                int z0 = FloorCell(component.Min.z, 1f), z1 = FloorCell(component.Max.z, 1f);
                // A shell or backdrop spanning the city is not a prop: it shares a
                // bucket with everything and its pairs alone exhaust memory.
                if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 400)
                {
                    skippedLarge++;
                    continue;
                }

                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = CellKey(x, 0, z);
                    if (!buckets.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        buckets.Add(key, list);
                    }

                    list.Add(index);
                }
            }

            var tested = new HashSet<long>();
            var trisA = new List<int>();
            var trisB = new List<int>();
            int found = 0;
            foreach (KeyValuePair<long, List<int>> bucket in buckets)
            {
                List<int> list = bucket.Value;
                for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    int a = Math.Min(list[i], list[j]);
                    int b = Math.Max(list[i], list[j]);
                    PropComponent ca = components[a];
                    PropComponent cb = components[b];
                    if (ca.Renderer == cb.Renderer)
                    {
                        continue;
                    }

                    // Sibling renderers are the material splits of ONE imported model
                    // (hull vs deck vs beam); their parts overlap by authoring, and
                    // reporting them buries the cross-object penetrations.
                    Transform nodeA = context.Renderers.Records[ca.Renderer].Transform;
                    Transform nodeB = context.Renderers.Records[cb.Renderer].Transform;
                    if (nodeA != null && nodeB != null &&
                        (nodeA.parent == nodeB.parent || nodeA.parent == nodeB || nodeB.parent == nodeA))
                    {
                        skippedSiblings++;
                        continue;
                    }

                    float ox = Math.Min(ca.Max.x, cb.Max.x) - Math.Max(ca.Min.x, cb.Min.x);
                    float oy = Math.Min(ca.Max.y, cb.Max.y) - Math.Max(ca.Min.y, cb.Min.y);
                    float oz = Math.Min(ca.Max.z, cb.Max.z) - Math.Max(ca.Min.z, cb.Min.z);
                    if (ox <= 0.05f || oy <= 0.05f || oz <= 0.05f)
                    {
                        continue;
                    }

                    // Remember only pairs that actually overlap: every pair sharing a
                    // bucket is far more than memory holds for a city of components.
                    long pairKey = ((long)a << 32) | (uint)b;
                    if (!tested.Add(pairKey))
                    {
                        continue;
                    }

                    Vector3 overlapMin = Vector3.Max(ca.Min, cb.Min);
                    Vector3 overlapMax = Vector3.Min(ca.Max, cb.Max);
                    CollectOverlapping(tris, ca, overlapMin, overlapMax, trisA);
                    CollectOverlapping(tris, cb, overlapMin, overlapMax, trisB);
                    int samples = 0;
                    bool hit = false;
                    Vector3 point = Vector3.zero;
                    for (int ia = 0; ia < trisA.Count && !hit && samples < 200; ia++)
                    {
                        ref Tri ta = ref tris.Tris[trisA[ia]];
                        TriangleIndex.TriBounds(in ta, out Vector3 amin, out Vector3 amax);
                        for (int ib = 0; ib < trisB.Count && samples < 200; ib++)
                        {
                            ref Tri tb = ref tris.Tris[trisB[ib]];
                            TriangleIndex.TriBounds(in tb, out Vector3 bmin, out Vector3 bmax);
                            if (amin.x > bmax.x || amax.x < bmin.x ||
                                amin.y > bmax.y || amax.y < bmin.y ||
                                amin.z > bmax.z || amax.z < bmin.z)
                            {
                                continue;
                            }

                            samples++;
                            if (Geo.TrianglesIntersect(ta.A, ta.B, ta.C, tb.A, tb.B, tb.C))
                            {
                                hit = true;
                                point = (ta.Centroid + tb.Centroid) * 0.5f;
                                break;
                            }
                        }
                    }

                    if (!hit)
                    {
                        continue;
                    }

                    found++;
                    Vector3 center = (overlapMin + overlapMax) * 0.5f;
                    Finding finding = NewFinding("D", "interpenetration", "new");
                    finding.At(center.x, center.y, center.z);
                    finding.Metric("overlap_m3", ox * oy * oz);
                    finding.Metric("overlap_x", ox).Metric("overlap_y", oy).Metric("overlap_z", oz);
                    finding.Metric("hit_x", point.x).Metric("hit_y", point.y).Metric("hit_z", point.z);
                    finding.Object(context.Renderers.Records[ca.Renderer].Path);
                    finding.Object(context.Renderers.Records[cb.Renderer].Path);
                    finding.Note = "components " + Json.Int(ca.Component) + "/" + Json.Int(cb.Component);
                    Locate(context, finding);
                    context.Output.Add(finding);
                }
            }

            context.Output.Count("D_interpenetrationSkippedLarge", skippedLarge);
            context.Output.Count("D_interpenetrationSkippedSiblings", skippedSiblings);
            context.Output.Count("D_interpenetrationPairs", tested.Count);
            return found;
        }

        private static void CollectOverlapping(
            TriangleIndex tris,
            PropComponent component,
            Vector3 min,
            Vector3 max,
            List<int> result)
        {
            result.Clear();
            foreach (int t in component.Tris)
            {
                TriangleIndex.TriBounds(in tris.Tris[t], out Vector3 tmin, out Vector3 tmax);
                if (tmin.x <= max.x && tmax.x >= min.x &&
                    tmin.y <= max.y && tmax.y >= min.y &&
                    tmin.z <= max.z && tmax.z >= min.z)
                {
                    result.Add(t);
                }
            }
        }

        private static int RunBlocksAccess(AuditContext context)
        {
            CityLayout layout = context.Layout;
            if (layout == null)
            {
                return 0;
            }

            var approaches = new List<KeyValuePair<string, Rect>>();
            for (int index = 0; index < layout.OpenAreaAccesses.Count; index++)
            {
                CityOpenAreaAccessDescriptor access = layout.OpenAreaAccesses[index];
                approaches.Add(new KeyValuePair<string, Rect>(access.Id, access.ApproachBounds));
            }

            for (int index = 0; index < layout.DistrictPointsOfInterest.Count; index++)
            {
                CityDistrictPointOfInterestDescriptor point = layout.DistrictPointsOfInterest[index];
                for (int a = 0; a < point.Accesses.Count; a++)
                {
                    approaches.Add(new KeyValuePair<string, Rect>(
                        point.Accesses[a].Id,
                        point.Accesses[a].ApproachBounds));
                }
            }

            int found = 0;
            foreach (ColliderRecord record in context.Colliders.Records)
            {
                if (record.IsTrigger ||
                    record.Name == CityDistrictPointOfInterestWorldBuilder.PublicGroundName)
                {
                    continue;
                }

                Rect footprint = Rect.MinMaxRect(
                    record.Bounds.min.x, record.Bounds.min.z,
                    record.Bounds.max.x, record.Bounds.max.z);
                foreach (KeyValuePair<string, Rect> approach in approaches)
                {
                    if (!footprint.Overlaps(approach.Value))
                    {
                        continue;
                    }

                    found++;
                    float xMin = Math.Max(footprint.xMin, approach.Value.xMin);
                    float xMax = Math.Min(footprint.xMax, approach.Value.xMax);
                    float zMin = Math.Max(footprint.yMin, approach.Value.yMin);
                    float zMax = Math.Min(footprint.yMax, approach.Value.yMax);
                    bool flat = IsFlatClass(record.Class);
                    Finding finding = NewFinding("D", "blocks_access", flat ? "by_design" : "new");
                    finding.At((xMin + xMax) * 0.5f, record.Bounds.max.y, (zMin + zMax) * 0.5f);
                    finding.Metric("overlap_m2", Math.Max(0f, xMax - xMin) * Math.Max(0f, zMax - zMin));
                    finding.Metric("collider_top_y", record.Bounds.max.y);
                    finding.Object(record.Path);
                    finding.Note = approach.Key + (flat ? "; ground collider" : string.Empty) +
                                   (record.KnownInvisible ? "; known_invisible" : string.Empty);
                    Locate(context, finding);
                    context.Output.Add(finding);
                }
            }

            return found;
        }

        // ------------------------------------------------------------------
        // E — existing defect lists, pedestrian graph, teleport lattice.
        // ------------------------------------------------------------------

        private static void RunE(AuditContext context)
        {
            var timer = Stopwatch.StartNew();
            CityLayout layout = context.Layout;
            if (layout == null)
            {
                context.Output.Notes.Add("E skipped: no layout in context " + context.Label);
                context.Output.Timing("E_existing_lists", timer.Elapsed.TotalMilliseconds);
                return;
            }

            var centerByCell = new Dictionary<Vector2Int, Vector2>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                centerByCell[surface.Cell] = surface.WorldBounds.center;
            }

            CityVerticalTraversalPlan vertical = CityVerticalTraversalAudit.Create(layout);
            foreach (CityVerticalTraversalDefectRecord defect in vertical.Defects)
            {
                Vector2 center = centerByCell.TryGetValue(defect.Cell, out Vector2 c)
                    ? c
                    : new Vector2(float.NaN, float.NaN);
                Finding finding = NewFinding("E", "vertical_defect", "new");
                finding.AtXZ(center.x, center.y);
                finding.Metric("delta", defect.Delta);
                finding.Note = defect.Type + "/" + defect.TransitionKind +
                               (defect.HasRelatedCell ? "; related " + defect.RelatedCell.x + "," + defect.RelatedCell.y : string.Empty) +
                               (defect.HasRoadEdge ? "; edge " + defect.RoadEdge.A + "-" + defect.RoadEdge.B : string.Empty);
                finding.Area = defect.AreaId;
                finding.Cell = defect.Cell.x.ToString(CultureInfo.InvariantCulture) + "," +
                               defect.Cell.y.ToString(CultureInfo.InvariantCulture);
                finding.Surface = defect.SurfaceKind.ToString();
                context.Output.Add(finding);
            }

            int unauthorized = 0;
            foreach (CityVerticalTraversalTransitionDescriptor transition in vertical.Transitions)
            {
                if (transition.IsAuthorized)
                {
                    continue;
                }

                unauthorized++;
                Vector2 mid = (transition.StartWorldXZ + transition.EndWorldXZ) * 0.5f;
                Finding finding = NewFinding("E", "vertical_transition", "new");
                finding.AtXZ(mid.x, mid.y);
                finding.Metric("max_step_delta", transition.MaximumStepDelta);
                finding.Metric("length_m", Vector2.Distance(transition.StartWorldXZ, transition.EndWorldXZ));
                finding.Note = transition.Classification + "/" + transition.Kind +
                               (transition.HasOtherCell ? "; other " + transition.OtherCell.x + "," + transition.OtherCell.y + " " + transition.OtherSurfaceKind : string.Empty);
                finding.Area = transition.AreaId;
                finding.Cell = transition.Cell.x.ToString(CultureInfo.InvariantCulture) + "," +
                               transition.Cell.y.ToString(CultureInfo.InvariantCulture);
                finding.Surface = transition.SurfaceKind.ToString();
                context.Output.Add(finding);
            }

            if (context.Boundaries != null)
            {
                EmitSpans(context, context.Boundaries.ProtectedDrops, "protected_drop");
                EmitSpans(context, context.Boundaries.SafeConnections, "safe_connection");
            }

            // Pedestrian graph.
            CityPedestrianPlan pedestrians = CityPedestrianPlanner.Create(
                layout,
                GameSessionState.DefaultCitySeed,
                context.Streets ?? CityStreetSurfacePlanner.Create(layout));
            int looseEnds = 0, islands = 0, offMask = 0;
            var union = new UnionFind(pedestrians.Nodes.Count);
            foreach (CityPedestrianLink link in pedestrians.Links)
            {
                union.Union(link.FirstNodeIndex, link.SecondNodeIndex);
            }

            var anchored = new HashSet<int>();
            foreach (CityPedestrianSpawnAnchor anchor in pedestrians.SpawnAnchors)
            {
                anchored.Add(union.Find(anchor.FirstNodeIndex));
                anchored.Add(union.Find(anchor.SecondNodeIndex));
            }

            var islandMembers = new Dictionary<int, List<int>>();
            for (int node = 0; node < pedestrians.Nodes.Count; node++)
            {
                Vector3 position = pedestrians.Nodes[node].Position;
                if (pedestrians.GetLinkIndices(node).Count == 1)
                {
                    looseEnds++;
                    Finding finding = NewFinding("E", "loose_end", "new");
                    finding.At(position.x, position.y, position.z);
                    finding.Metric("links", 1);
                    finding.Note = pedestrians.Nodes[node].Id;
                    Locate(context, finding);
                    context.Output.Add(finding);
                }

                if (!context.Mask.Contains(position, pedestrians.AgentRadius))
                {
                    offMask++;
                    Finding finding = NewFinding("E", "pedestrian_node_off_mask", "new");
                    finding.At(position.x, position.y, position.z);
                    Vector3 closest = context.Mask.ClosestPoint(position, pedestrians.AgentRadius);
                    finding.Metric("distance_to_mask", Vector2.Distance(
                        new Vector2(position.x, position.z), new Vector2(closest.x, closest.z)));
                    finding.Metric("agent_radius", pedestrians.AgentRadius);
                    finding.Note = pedestrians.Nodes[node].Id;
                    Locate(context, finding);
                    context.Output.Add(finding);
                }

                int root = union.Find(node);
                if (!anchored.Contains(root))
                {
                    if (!islandMembers.TryGetValue(root, out List<int> members))
                    {
                        members = new List<int>();
                        islandMembers.Add(root, members);
                    }

                    members.Add(node);
                }
            }

            foreach (KeyValuePair<int, List<int>> island in islandMembers)
            {
                islands++;
                var box = new BoxXZ();
                Vector3 sum = Vector3.zero;
                foreach (int node in island.Value)
                {
                    Vector3 position = pedestrians.Nodes[node].Position;
                    box.Add(position.x, position.z);
                    sum += position;
                }

                Vector3 centroid = sum / island.Value.Count;
                Finding finding = NewFinding("E", "island", "new");
                finding.At(centroid.x, centroid.y, centroid.z);
                finding.Metric("nodes", island.Value.Count);
                finding.Metric("xMin", box.XMin).Metric("xMax", box.XMax);
                finding.Metric("zMin", box.ZMin).Metric("zMax", box.ZMax);
                finding.Note = pedestrians.Nodes[island.Value[0]].Id;
                Locate(context, finding);
                context.Output.Add(finding);
            }

            // Teleport lattice, built exactly as the map does.
            int missingSquares = 0;
            if (context.World != null)
            {
                Rect chart = CityMapController.CreateDisplayWorldXZBounds(
                    layout.MapWorldXZBounds,
                    context.World.MountainBoundaryPlan);
                CityMapTeleportLattice lattice = CityMapTeleportLatticeBuilder.Create(
                    chart,
                    new Vector2(layout.WorldOrigin.x, layout.WorldOrigin.z),
                    Mathf.Max(0.5f, Mathf.Min(layout.NodeSpacing.x, layout.NodeSpacing.y)),
                    new CityMapCityTeleportGround(layout));
                StandingGrid grid = context.Grid;
                for (int cellZ = lattice.MinimumCell.y; cellZ <= lattice.MaximumCell.y; cellZ++)
                for (int cellX = lattice.MinimumCell.x; cellX <= lattice.MaximumCell.x; cellX++)
                {
                    var cell = new Vector2Int(cellX, cellZ);
                    if (lattice.TryGetSquare(cell, out _))
                    {
                        continue;
                    }

                    Rect bounds = lattice.GetCellWorldBounds(cell);
                    int inside = CountInsideCells(grid, bounds);
                    if (inside == 0)
                    {
                        continue;
                    }

                    missingSquares++;
                    int neighbours = 0;
                    if (lattice.TryGetSquare(cell + Vector2Int.left, out _)) neighbours++;
                    if (lattice.TryGetSquare(cell + Vector2Int.right, out _)) neighbours++;
                    if (lattice.TryGetSquare(cell + Vector2Int.up, out _)) neighbours++;
                    if (lattice.TryGetSquare(cell + Vector2Int.down, out _)) neighbours++;
                    Finding finding = NewFinding("E", "lattice_missing_square", "new");
                    finding.AtXZ(bounds.center.x, bounds.center.y);
                    finding.Metric("walkable_m2", inside * grid.CellArea);
                    finding.Metric("neighbours_with_squares", neighbours);
                    finding.Metric("all_neighbours_have_squares", neighbours == 4 ? 1 : 0);
                    finding.Note = "lattice cell " + cellX + "," + cellZ;
                    Locate(context, finding);
                    context.Output.Add(finding);
                }

                context.Output.Count("E_latticeSquares", lattice.Squares.Count);
            }

            // Mask drift: what the builders added beyond the layout mask.
            RoadWalkableArea layoutMask = RoadWalkableArea.FromLayout(layout);
            var driftCells = new List<int>();
            StandingGrid g = context.Grid;
            for (int index = 0; index < g.CellCount; index++)
            {
                if (!g.Has(index, StandingGrid.Inside))
                {
                    continue;
                }

                var p = new Vector3(g.CenterX(index % g.NX), 0f, g.CenterZ(index / g.NX));
                if (!layoutMask.Contains(p, HeroRadius))
                {
                    driftCells.Add(index);
                }
            }

            List<CellCluster> drift = ClusterCells(g, driftCells, null, false);
            foreach (CellCluster cluster in drift)
            {
                Finding finding = EmitCellCluster(context, "E", "lattice_mask_drift", "new", cluster, driftCells, null, -1);
                finding.Tag = "by_design";
                finding.Note = "walkable area extended by the world builders beyond RoadWalkableArea.FromLayout";
            }

            context.Output.Count("E_verticalDefects", vertical.Defects.Count);
            context.Output.Count("E_unauthorizedTransitions", unauthorized);
            context.Output.Count("E_pedestrianNodes", pedestrians.Nodes.Count);
            context.Output.Count("E_driftCells", driftCells.Count);
            context.Output.Timing("E_existing_lists", timer.Elapsed.TotalMilliseconds);
            context.Log("E: " + vertical.Defects.Count + " vertical defects, " + unauthorized +
                        " unauthorized transitions, " + looseEnds + " loose ends, " + islands +
                        " islands, " + offMask + " nodes off mask, " + missingSquares +
                        " missing lattice squares, " + drift.Count + " drift clusters");
        }

        private static void EmitSpans(
            AuditContext context,
            IReadOnlyList<CityRoadGroundBoundarySpan> spans,
            string subtype)
        {
            for (int index = 0; index < spans.Count; index++)
            {
                CityRoadGroundBoundarySpan span = spans[index];
                float mid = (span.MinimumCoordinate + span.MaximumCoordinate) * 0.5f;
                float x = span.IsHorizontal ? mid : span.FixedCoordinate;
                float z = span.IsHorizontal ? span.FixedCoordinate : mid;
                Finding finding = NewFinding("E", subtype, "by_design");
                finding.At(x, span.GroundTopY, z);
                finding.Dir = span.IsHorizontal ? "+z" : "+x";
                finding.Metric("length_m", span.Length);
                finding.Metric("ground_top_first", span.FirstGroundTopY);
                finding.Metric("ground_top_second", span.SecondGroundTopY);
                finding.Metric("travel_top_first", span.FirstTravelTopY);
                finding.Metric("travel_top_second", span.SecondTravelTopY);
                finding.Area = span.Surface.AreaId;
                finding.Cell = span.Surface.Cell.x.ToString(CultureInfo.InvariantCulture) + "," +
                               span.Surface.Cell.y.ToString(CultureInfo.InvariantCulture);
                finding.Surface = span.Surface.Kind.ToString();
                finding.Note = "edge " + span.Edge.A + "-" + span.Edge.B;
                context.Output.Add(finding);
            }
        }

        private static int CountInsideCells(StandingGrid grid, Rect bounds)
        {
            int count = 0;
            int x0 = Math.Max(0, (int)Math.Floor((bounds.xMin - grid.XMin) / grid.Step));
            int x1 = Math.Min(grid.NX - 1, (int)Math.Floor((bounds.xMax - grid.XMin) / grid.Step));
            int z0 = Math.Max(0, (int)Math.Floor((bounds.yMin - grid.ZMin) / grid.Step));
            int z1 = Math.Min(grid.NZ - 1, (int)Math.Floor((bounds.yMax - grid.ZMin) / grid.Step));
            for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
            {
                float x = grid.CenterX(ix);
                float z = grid.CenterZ(iz);
                if (x < bounds.xMin || x > bounds.xMax || z < bounds.yMin || z > bounds.yMax)
                {
                    continue;
                }

                if (grid.IsInside(ix, iz))
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------
        // The audit tests. They assert only that a check ran and flushed.
        // ------------------------------------------------------------------

        [Test]
        [Order(1)]
        public void A_InvisibleWalls()
        {
            AuditContext context = City;
            RunA(context);
            RunA2(context);
            RunA3(context);
            if (context.Planted)
            {
                AssertPlantFound(context, "A2", "invisible_obstacle_in_mask", 1.0f);
            }

            FlushAndAssert(context, "A");
        }

        [Test]
        [Order(2)]
        public void B_Gaps()
        {
            AuditContext context = City;
            RunB(context);
            FlushAndAssert(context, "B");
        }

        [Test]
        [Order(3)]
        public void C_ZFight()
        {
            AuditContext context = City;
            RunC(context);
            if (context.Planted)
            {
                AssertPlantFound(context, "C", "zfight_cluster", 1.0f);
            }

            FlushAndAssert(context, "C");
        }

        [Test]
        [Order(4)]
        public void D_Props()
        {
            AuditContext context = City;
            RunD(context);
            FlushAndAssert(context, "D");
        }

        [Test]
        [Order(5)]
        public void E_ExistingDefectLists()
        {
            AuditContext context = City;
            RunE(context);
            FlushAndAssert(context, "E");
            Debug.Log(context.Output.SummaryText());
        }

        /// <summary>
        /// Answers the two questions a finding list cannot: what actually
        /// carries the hero at a reported hole, and what actually stands
        /// across an authored approach. Prints; asserts nothing.
        /// </summary>
        [Test]
        [Order(6)]
        public void F_Diagnose()
        {
            AuditContext context = City;
            var points = new[]
            {
                new Vector3(-45.875f, 0f, -133.625f),
                new Vector3(-32.375f, 0f, -133.625f),
                new Vector3(-45.625f, 0f, 126.625f),
                new Vector3(136.375f, 0f, -100.625f),
                new Vector3(-39.125f, 0f, -126.125f),
                new Vector3(-45.875f, 0f, -132.5f),
                new Vector3(-45.875f, 0f, -135f),
                new Vector3(0f, 0f, 0f)
            };

            var report = new System.Text.StringBuilder();
            report.AppendLine("[CityAudit diagnose] columns");
            foreach (Vector3 p in points)
            {
                RaycastHit[] hits = Physics.RaycastAll(
                    new Vector3(p.x, 120f, p.z), Vector3.down, 400f, ~0,
                    QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                report.Append("  (").Append(F(p.x)).Append(", ").Append(F(p.z))
                      .Append(") hits=").Append(hits.Length);
                bool inside = context.Mask.Contains(new Vector3(p.x, 0f, p.z), HeroRadius);
                report.Append(" maskInside=").Append(inside ? "yes" : "no").AppendLine();
                for (int index = 0; index < hits.Length && index < 8; index++)
                {
                    RaycastHit hit = hits[index];
                    bool audited = context.IsAudited(hit.collider, out ColliderRecord record);
                    report.Append("      y=").Append(F(hit.point.y))
                          .Append(" trigger=").Append(hit.collider.isTrigger ? "1" : "0")
                          .Append(" layer=").Append(Json.Int(hit.collider.gameObject.layer))
                          .Append(" audited=").Append(audited ? "1" : "0")
                          .Append(' ')
                          .AppendLine(audited ? record.Path : hit.collider.name);
                }
            }

            report.AppendLine("[CityAudit diagnose] authored approaches");
            CityLayout layout = context.Layout;
            var approaches = new List<KeyValuePair<string, Rect>>();
            for (int index = 0; index < layout.OpenAreaAccesses.Count; index++)
            {
                CityOpenAreaAccessDescriptor access = layout.OpenAreaAccesses[index];
                approaches.Add(new KeyValuePair<string, Rect>(access.Id, access.ApproachBounds));
            }

            for (int index = 0; index < layout.DistrictPointsOfInterest.Count; index++)
            {
                CityDistrictPointOfInterestDescriptor poi = layout.DistrictPointsOfInterest[index];
                for (int access = 0; access < poi.Accesses.Count; access++)
                {
                    approaches.Add(new KeyValuePair<string, Rect>(
                        poi.Accesses[access].Id, poi.Accesses[access].ApproachBounds));
                }
            }

            foreach (KeyValuePair<string, Rect> approach in approaches)
            {
                Rect bounds = approach.Value;
                bool horizontal = bounds.width >= bounds.height;
                float span = horizontal ? bounds.width : bounds.height;
                int samples = Math.Max(4, (int)Math.Ceiling(span / 0.25f));
                int blocked = 0;
                int free = 0;
                int longestBlocked = 0;
                int run = 0;
                string worst = string.Empty;
                for (int sample = 0; sample <= samples; sample++)
                {
                    float t = sample / (float)samples;
                    float x = horizontal ? Mathf.Lerp(bounds.xMin, bounds.xMax, t) : bounds.center.x;
                    float z = horizontal ? bounds.center.y : Mathf.Lerp(bounds.yMin, bounds.yMax, t);
                    float ground = GroundAt(context, x, z);
                    if (float.IsNaN(ground))
                    {
                        continue;
                    }

                    int overlaps = Physics.OverlapCapsuleNonAlloc(
                        new Vector3(x, ground + StepMax + OverlapRadius, z),
                        new Vector3(x, ground + HeroHeight - OverlapRadius, z),
                        OverlapRadius, context.Overlaps, StaticMask,
                        QueryTriggerInteraction.Ignore);
                    ColliderRecord hitRecord = null;
                    for (int o = 0; o < overlaps; o++)
                    {
                        if (context.Overlaps[o] == null || context.Overlaps[o].isTrigger ||
                            !context.IsAudited(context.Overlaps[o], out ColliderRecord record) ||
                            IsFlatClass(record.Class))
                        {
                            continue;
                        }

                        hitRecord = record;
                        break;
                    }

                    if (hitRecord != null)
                    {
                        blocked++;
                        run++;
                        if (run > longestBlocked)
                        {
                            longestBlocked = run;
                            worst = hitRecord.Path;
                        }
                    }
                    else
                    {
                        free++;
                        run = 0;
                    }
                }

                report.Append("  ").Append(approach.Key)
                      .Append(" span=").Append(F(span))
                      .Append(" blocked=").Append(Json.Int(blocked))
                      .Append(" free=").Append(Json.Int(free))
                      .Append(" longestBlocked_m=").Append(F(longestBlocked * 0.25f));
                if (longestBlocked > 0)
                {
                    report.Append(' ').Append(worst);
                }

                report.AppendLine();
            }

            Debug.Log(report.ToString());
            Assert.That(context.Mask, Is.Not.Null);
        }

        private static void FlushAndAssert(AuditContext context, string check)
        {
            int flushed = context.Output.FlushCheck(check);
            context.Log(check + ": flushed " + flushed + " findings -> " + context.Output.Directory);
            context.Log(check + " counts: " + context.Output.CountsLine());
            Assert.That(context.Output.Ran(check), Is.True, "Check " + check + " did not record itself as run.");
            Assert.That(
                File.Exists(Path.Combine(context.Output.Directory, "findings.ndjson")),
                Is.True,
                "findings.ndjson was not written.");
            Assert.That(
                File.Exists(Path.Combine(context.Output.Directory, "run.json")),
                Is.True,
                "run.json was not written.");
        }

        private static void AssertPlantFound(AuditContext context, string category, string subtype, float radius)
        {
            bool found = false;
            foreach (Finding finding in PendingAndFlushed(context))
            {
                if (finding.Category != category || finding.Subtype != subtype)
                {
                    continue;
                }

                float dx = (float)finding.X - context.PlantPosition.x;
                float dz = (float)finding.Z - context.PlantPosition.z;
                if (dx * dx + dz * dz <= radius * radius)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "The in-situ plant did not produce " + category + "/" + subtype + ".");
        }

        private static List<Finding> PendingAndFlushed(AuditContext context)
        {
            var result = new List<Finding>(context.Output.All);
            result.AddRange(context.Output.PendingSnapshot());
            return result;
        }

        // ------------------------------------------------------------------
        // Control: every detector fires on a planted miniature world.
        // ------------------------------------------------------------------

        private static Material controlMaterial;

        private static Material ControlMaterial()
        {
            if (controlMaterial != null)
            {
                return controlMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            controlMaterial = shader != null ? new Material(shader) : null;
            return controlMaterial;
        }

        private static Mesh BuildQuadMesh(float x0, float x1, float z0, float z1, float y)
        {
            return BuildRectsMesh(new List<Rect> { Rect.MinMaxRect(x0, z0, x1, z1) }, y);
        }

        /// <summary>Top-facing (+Y) quads, one per rectangle, in world coordinates.</summary>
        private static Mesh BuildRectsMesh(List<Rect> rects, float y)
        {
            var vertices = new List<Vector3>(rects.Count * 4);
            var triangles = new List<int>(rects.Count * 6);
            foreach (Rect rect in rects)
            {
                int start = vertices.Count;
                vertices.Add(new Vector3(rect.xMin, y, rect.yMin));
                vertices.Add(new Vector3(rect.xMin, y, rect.yMax));
                vertices.Add(new Vector3(rect.xMax, y, rect.yMax));
                vertices.Add(new Vector3(rect.xMax, y, rect.yMin));
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }

            var mesh = new Mesh { name = "Audit Control Rects" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildBoxMesh(Vector3 min, Vector3 max)
        {
            var mesh = new Mesh { name = "Audit Control Box" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            AddFace(vertices, triangles, new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z));
            AddFace(vertices, triangles, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z));
            AddFace(vertices, triangles, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
            AddFace(vertices, triangles, new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z));
            AddFace(vertices, triangles, new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z));
            AddFace(vertices, triangles, new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z));
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFace(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static List<Rect> RectsMinus(Rect source, Rect hole)
        {
            var result = new List<Rect>();
            CityGroundTraversalPlanner.SubtractRectangle(source, hole, result);
            return result;
        }

        private static GameObject AddMeshObject(Transform parent, string name, Mesh mesh, bool collider, bool renderer)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, false);
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (renderer)
            {
                target.AddComponent<MeshRenderer>().sharedMaterial = ControlMaterial();
            }

            if (collider)
            {
                target.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            return target;
        }

        private static GameObject AddGround(Transform parent, string name, List<Rect> rects, float y)
        {
            return AddMeshObject(parent, name, BuildRectsMesh(rects, y), true, true);
        }

        private static GameObject AddCube(Transform parent, string name, Vector3 center, float size)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = center;
            cube.transform.localScale = new Vector3(size, size, size);
            return cube;
        }

        private static void AssertNear(
            AuditOutput output,
            string category,
            string subtype,
            float x,
            float z,
            float radius = 0.35f)
        {
            float best = float.PositiveInfinity;
            foreach (Finding finding in output.All)
            {
                if (finding.Category != category || finding.Subtype != subtype)
                {
                    continue;
                }

                float dx = (float)finding.X - x;
                float dz = (float)finding.Z - z;
                best = Math.Min(best, (float)Math.Sqrt(dx * dx + dz * dz));
            }

            Assert.That(
                best,
                Is.LessThanOrEqualTo(radius),
                category + "/" + subtype + " was not found within " + F(radius) + " m of (" +
                F(x) + ", " + F(z) + "); nearest " + F(best) + " m.");
        }

        [Test]
        public void Controls_EveryDetectorFires()
        {
            var host = new GameObject("City Audit Control Host");
            AuditOutput output = null;
            try
            {
                Transform root = host.transform;

                // Ground A: x[-6,-0.01] z[-6,3], a 1x1 hole at x[-4,-3] z[-2,-1].
                // The left strip is split in two so the planted seam - the
                // east edge, 20 mm short of Ground B - is the longest
                // uncovered edge of the mesh and therefore its sample point.
                var groundA = new List<Rect>
                {
                    Rect.MinMaxRect(-6f, -6f, -4f, -2f),
                    Rect.MinMaxRect(-6f, -2f, -4f, 3f),
                    Rect.MinMaxRect(-4f, -6f, -3f, -2f),
                    Rect.MinMaxRect(-4f, -1f, -3f, 3f),
                    Rect.MinMaxRect(-3f, -6f, -0.01f, 3f)
                };
                AddGround(root, "Active Land A", groundA, 0f);
                // Raised terrace with a continuous mask across the 0.5 m step.
                AddGround(root, "Active Land A Upper", new List<Rect> { Rect.MinMaxRect(-6f, 3f, -0.01f, 6f) }, 0.5f);
                // Ground B: x[0.01,6] z[-6,6]; hole at x[3,4] z[-2,-1] filled by a collider-only patch.
                AddGround(root, "Active Land B", RectsMinus(Rect.MinMaxRect(0.01f, -6f, 6f, 6f), Rect.MinMaxRect(3f, -2f, 4f, -1f)), 0f);
                AddMeshObject(root, "Planted Collider Patch", BuildQuadMesh(3f, 4f, -2f, -1f, 0f), true, false);
                // Ground C under the abutting mask rectangle, D beyond the mask with no fence,
                // F under the disconnected pocket rectangle.
                AddGround(root, "Active Land C", new List<Rect> { Rect.MinMaxRect(6f, -6f, 12f, 6f) }, 0f);
                AddGround(root, "Active Land D", new List<Rect> { Rect.MinMaxRect(12f, -6f, 16f, 6f) }, 0f);
                AddGround(root, "Active Land F", new List<Rect> { Rect.MinMaxRect(-6f, 8f, -2f, 12f) }, 0f);

                var invisible = new GameObject("Planted Invisible Box");
                invisible.transform.SetParent(root, false);
                invisible.transform.position = new Vector3(-3f, 0.5f, -3f);
                invisible.AddComponent<BoxCollider>().size = Vector3.one;

                GameObject proxy = AddMeshObject(
                    root,
                    "City Detail Chunk 0 0",
                    BuildBoxMesh(new Vector3(2f, 0f, 2.5f), new Vector3(3f, 1f, 3.5f)),
                    false,
                    true);
                BoxCollider proxyCollider = proxy.AddComponent<BoxCollider>();
                proxyCollider.center = new Vector3(3f, 0.5f, 3f);
                proxyCollider.size = new Vector3(2f, 1f, 1f);

                AddMeshObject(root, "Planted Z Quad", BuildQuadMesh(-3.7f, -3.3f, 1.3f, 1.7f, 0.0008f), false, true);

                var duplicate = new List<Vector3>
                {
                    new Vector3(-5f, 1.5f, -5f),
                    new Vector3(-4f, 1.5f, -4f),
                    new Vector3(-4f, 1.5f, -5f)
                };
                for (int copy = 0; copy < 2; copy++)
                {
                    var mesh = new Mesh { name = "Audit Control Triangle" };
                    mesh.SetVertices(duplicate);
                    mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    AddMeshObject(root, copy == 0 ? "Planted Tri A" : "Planted Tri B", mesh, false, true);
                }

                AddCube(root, "Planted Floating Cube", new Vector3(2f, 0.45f, -3f), 0.5f);
                AddCube(root, "Planted Sunk Cube", new Vector3(4f, -0.85f, -3f), 0.5f);
                // Two DIFFERENT assemblies: siblings of one imported model are
                // deliberately not compared (their parts overlap by authoring).
                var holderA = new GameObject("Planted Overlap Assembly A");
                holderA.transform.SetParent(root, false);
                var holderB = new GameObject("Planted Overlap Assembly B");
                holderB.transform.SetParent(root, false);
                AddCube(holderA.transform, "Planted Overlap Cube A", new Vector3(-2f, 0.25f, 0.5f), 0.5f);
                AddCube(holderB.transform, "Planted Overlap Cube B", new Vector3(-1.8f, 0.25f, 0.7f), 0.5f);
                Physics.SyncTransforms();

                var mask = new RoadWalkableArea();
                mask.Add(Rect.MinMaxRect(-6f, -6f, 6f, 6f));
                mask.Add(Rect.MinMaxRect(6f, -6f, 12f, 6f));
                mask.Add(Rect.MinMaxRect(-6f, 8f, -2f, 12f));

                output = new AuditOutput(ResolveOutputDirectory("controls"))
                {
                    Seed = 0,
                    Label = "controls"
                };
                RecordThresholds(output);
                var context = new AuditContext
                {
                    Label = "controls",
                    Host = root,
                    Mask = mask,
                    SeedPoint = new Vector3(-2f, 0f, 0f),
                    Output = output,
                    GridBounds = Rect.MinMaxRect(-8f, -8f, 18f, 14f)
                };
                Prepare(context);
                output.WriteRunJson();

                RunA(context);
                RunA2(context);
                RunA3(context);
                FlushAndAssert(context, "A");
                RunB(context);
                FlushAndAssert(context, "B");
                RunC(context);
                FlushAndAssert(context, "C");
                RunD(context);
                FlushAndAssert(context, "D");
                RunE(context);
                FlushAndAssert(context, "E");
                Debug.Log(output.SummaryText());

                AssertNear(output, "A", "mask_dead_band", 5.75f, 0f);
                AssertNear(output, "A", "mask_edge_no_barrier", 11.75f, 0f);
                AssertNear(output, "A2", "invisible_obstacle_in_mask", -3f, -3f);
                AssertNear(output, "A2", "collider_overhang", 3f, 3f);
                AssertNear(output, "A3", "unreachable_pocket", -4f, 10f);
                AssertNear(output, "A3", "physically_unreachable_pocket", -3f, 4.5f);
                AssertNear(output, "A3", "mask_step_barrier", -3f, 3f);
                AssertNear(output, "B", "hole", -3.5f, -1.5f);
                AssertNear(output, "B", "visual_hole", 3.5f, -1.5f);
                AssertNear(output, "B", "open_seam", 0f, -1.5f);
                AssertNear(output, "C", "zfight_cluster", -3.5f, 1.5f);
                AssertNear(output, "C", "duplicate_triangle", -4.333f, -4.667f);
                AssertNear(output, "D", "floating", 2f, -3f);
                AssertNear(output, "D", "sunk", 4f, -3f);
                AssertNear(output, "D", "interpenetration", -1.9f, 0.6f);
            }
            finally
            {
                if (output != null)
                {
                    output.Close();
                }

                Object.DestroyImmediate(host);
            }
        }
    }
}
