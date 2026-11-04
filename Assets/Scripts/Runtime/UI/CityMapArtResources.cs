using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A measured ImageGen sheet is usable only with the geography it was drawn from.</summary>
    public static class CityMapArtResources
    {
        public const string ResourcePath = "UI/Maps/CityMapImage";
        public const int SchemaVersion = 2;
        [Serializable]
        public sealed class Metadata
        {
            public int schemaVersion;
            public string blueprintId;
            public int citySeed;
            public string fingerprint;
            public Rect worldBounds;
            public int imageWidth, imageHeight;
        }
        [Serializable] private sealed class Catalog { public Entry[] entries; }
        [Serializable] private sealed class Entry { public string key, value; }
        private sealed class Artwork
        {
            public Texture2D Texture;
            public Rect Bounds;
        }
        private sealed class Fingerprint { public string Value; }
        private static ConditionalWeakTable<CityMapController, Artwork> artwork = new ConditionalWeakTable<CityMapController, Artwork>();
        private static ConditionalWeakTable<CityMapController, Fingerprint> fingerprints = new ConditionalWeakTable<CityMapController, Fingerprint>();
        private static bool resourcesLoaded, staleWarning;
        private static Texture2D texture;
        private static Metadata metadata;

        public static bool TryGet(CityMapController controller, out Texture2D image, out Rect worldBounds)
        {
            if (controller == null || controller.Layout == null)
            { image = null; worldBounds = default; return false; }
            Artwork bound = artwork.GetValue(controller, Bind);
            image = bound.Texture; worldBounds = bound.Bounds;
            return image != null;
        }

        internal static void ResetBinding(CityMapController controller)
        {
            if (controller == null) return;
            artwork.Remove(controller); fingerprints.Remove(controller);
        }

        private static Artwork Bind(CityMapController controller)
        {
            var result = new Artwork();
            CityLayout layout = controller.Layout;
            if (layout.Seed != GameSessionState.DefaultCitySeed ||
                layout.BlueprintId != CityBlueprintCatalog.Default.Id) return result;
            if (!resourcesLoaded)
            {
                resourcesLoaded = true;
                TextAsset source = Resources.Load<TextAsset>(ResourcePath);
                metadata = source == null ? null : JsonUtility.FromJson<Metadata>(source.text);
                // The JSON and PNG intentionally share a Resources stem;
                // typed loads keep their two purposes unambiguous.
                texture = Resources.Load<Texture2D>(ResourcePath);
            }
            if (metadata == null || texture == null) return result;
            Rect bounds = controller.DisplayWorldXZBounds;
            bool matches = metadata.schemaVersion == SchemaVersion && metadata.citySeed == layout.Seed &&
                metadata.blueprintId == layout.BlueprintId && metadata.imageWidth == texture.width &&
                metadata.imageHeight == texture.height && SameBounds(metadata.worldBounds, bounds) &&
                string.Equals(metadata.fingerprint, CreateFingerprint(controller), StringComparison.Ordinal);
            if (!matches)
            {
                if (!staleWarning)
                {
                    staleWarning = true;
                    GameLog.Warning("map", "illustration_outdated_redraw_with_imagegen");
                }
                return result;
            }
            result.Texture = texture; result.Bounds = bounds;
            return result;
        }

        private static bool SameBounds(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < .001f && Mathf.Abs(a.y - b.y) < .001f &&
            Mathf.Abs(a.width - b.width) < .001f && Mathf.Abs(a.height - b.height) < .001f;

        public static string CreateFingerprint(CityMapController controller)
        {
            if (controller == null || controller.Layout == null) throw new ArgumentNullException(nameof(controller));
            return fingerprints.GetValue(controller, c => new Fingerprint { Value = HashGeography(c) }).Value;
        }

        private static string HashGeography(CityMapController controller)
        {
            CityLayout layout = controller.Layout;
            using (var stream = new MemoryStream(65536))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("city_map_image_v2"); writer.Write(layout.BlueprintId); writer.Write(layout.Seed);
                RectValue(writer, controller.DisplayWorldXZBounds); Scalar(writer, layout.RoadWidth);
                writer.Write(layout.Surfaces.Count);
                foreach (CitySurfaceDescriptor surface in layout.Surfaces)
                {
                    writer.Write(surface.AreaId); writer.Write((int)surface.Kind); RectValue(writer, surface.WorldBounds);
                }
                writer.Write(layout.BuildingMasses.Count);
                foreach (BuildingLot lot in layout.BuildingMasses)
                {
                    writer.Write(lot.Cell.x); writer.Write(lot.Cell.y); writer.Write((int)lot.District);
                    writer.Write(lot.HasBuilding); writer.Write(lot.IsBar); writer.Write(lot.IsPlayerHome); writer.Write(lot.IsSupermarket);
                    writer.Write(lot.BarId ?? string.Empty); Point(writer, lot.Center); Scalar(writer, lot.Size.x); Scalar(writer, lot.Size.y);
                    var polygons = lot.CreateCollisionPolygons(); writer.Write(polygons.Count);
                    foreach (Vector2[] polygon in polygons)
                    { writer.Write(polygon.Length); foreach (Vector2 point in polygon) Point(writer, point); }
                }
                // A road can be regraded without moving its XZ footprint or
                // nearby doors. That still invalidates the drawn geography.
                writer.Write(layout.Nodes.Count);
                foreach (Vector2Int node in layout.Nodes)
                {
                    writer.Write(node.x); writer.Write(node.y);
                    Scalar(writer, layout.ElevationPlan.GetNodeElevation(node));
                }
                writer.Write(layout.RoadEdges.Count);
                foreach (RoadEdge edge in layout.RoadEdges)
                {
                    writer.Write((int)layout.GetPathKind(edge));
                    CityRoadPath path = layout.RoadGeometry.Get(edge);
                    writer.Write(path.Vertices.Count);
                    float distance = 0f;
                    for (int index = 0; index < path.Vertices.Count; index++)
                    {
                        if (index > 0) distance += Vector2.Distance(path.Vertices[index - 1], path.Vertices[index]);
                        RoadPoint(writer, layout, edge, path.Vertices[index], distance / path.Length);
                    }
                    // Street surfaces break at the two level-junction insets
                    // as well as path vertices; preserve their positions and Y.
                    float start = layout.RoadGeometry.GetEndpointInset(edge, edge.A);
                    float end = path.Length - layout.RoadGeometry.GetEndpointInset(edge, edge.B);
                    RoadPoint(writer, layout, edge, path.SampleDistance(start).Position, start / path.Length);
                    RoadPoint(writer, layout, edge, path.SampleDistance(end).Position, end / path.Length);
                }
                foreach (CityRoadPath path in layout.CourtyardPaths)
                { writer.Write(path.Vertices.Count); foreach (Vector2 point in path.Vertices) Point(writer, point); }
                writer.Write(-1);
                var entrances = CityResidentialEntrancePlan.ForLayout(layout).Entrances;
                writer.Write(entrances.Count);
                foreach (var entrance in entrances)
                {
                    Point(writer, entrance.Threshold);
                    writer.Write(entrance.Points.Count);
                    foreach (Vector3 point in entrance.Points) Point(writer, point);
                }
                CityTerraceDistrictPlan terraces = CityTerraceDistrictPlan.ForLayout(layout);
                writer.Write(terraces.IsEnabled);
                if (terraces.IsEnabled)
                {
                    writer.Write(terraces.Floors.Count);
                    foreach (CityTerracePilotFloor floor in terraces.Floors)
                    {
                        writer.Write(floor.Id); writer.Write(floor.IsCourt);
                        Point(writer, terraces.World(floor.Center)); Point(writer, floor.Size);
                    }
                    writer.Write(terraces.Flights.Count);
                    foreach (CityExteriorStairFlightDescriptor flight in terraces.Flights)
                    {
                        writer.Write(flight.Id); Point(writer, terraces.World(flight.Start));
                        Point(writer, terraces.World(flight.End)); Scalar(writer, flight.Width);
                        writer.Write(flight.StepCount); Scalar(writer, flight.StepRise); Scalar(writer, flight.TreadDepth);
                    }
                    writer.Write(terraces.GroundCuts.Count);
                    foreach (Rect cut in terraces.GroundCuts) RectValue(writer, cut);
                    Point(writer, terraces.EntryStartWorld); Point(writer, terraces.EntryEndWorld);
                    RectValue(writer, terraces.EntryBounds);
                    Point(writer, terraces.LowerArrivalWorld); Point(writer, terraces.UpperArrivalWorld);
                    Point(writer, terraces.LowerPassStartWorld); Point(writer, terraces.LowerPassRampEndWorld);
                    Point(writer, terraces.LowerPassEndWorld);
                    RectValue(writer, terraces.LowerPassBounds); RectValue(writer, terraces.BridgeWorldBounds);
                    Point(writer, terraces.LowerCrossingWorld); Point(writer, terraces.UpperCrossingWorld);
                    foreach (Vector3 point in terraces.ShortcutRouteWorld) Point(writer, point);
                    if (terraces.HasCourtyardLink)
                    {
                        writer.Write(terraces.CourtyardLinkRouteWorld.Count);
                        foreach (Vector3 point in terraces.CourtyardLinkRouteWorld) Point(writer, point);
                        RectValue(writer, terraces.CourtyardLinkRampBounds);
                        Point(writer, terraces.CourtyardLinkArrivalWorld);
                    }
                }
                foreach (CityHillsideStreetRoute road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                {
                    writer.Write((int)road.Bank);
                    writer.Write(road.Path.Vertices.Count);
                    foreach (var paving in road.WalkablePolygons)
                        foreach (Vector2 point in paving) Point(writer, point);
                    for (int i = 0; i < road.Path.Vertices.Count; i++)
                    {
                        Point(writer, road.Path.Vertices[i]);
                        Scalar(writer, road.Profile.Stations[i].Height);
                    }
                }
                var quarter = CityHillsideQuarterPlan.ForLayout(layout);
                RectValue(writer, quarter.TerrainBounds);
                writer.Write(quarter.RavineOutline.Count);
                foreach (Vector2 edge in quarter.RavineOutline) Point(writer, edge);
                if (quarter.Bridges.Count > 0)
                {
                    // Moving an intermediate rock ledge changes the surveyed
                    // elevation even if the two bridge centres stay put.
                    foreach (var section in CityHillsideQuarterRavine.Sections)
                        foreach (var station in CityHillsideQuarterRavine.Profile)
                        {
                            var at = new Vector2(Mathf.Lerp(section.y,section.z,station.x), section.x);
                            Point(writer, new Vector3(at.x,quarter.SampleSoil(at),at.y));
                        }
                }
                writer.Write(quarter.Bridges.Count);
                foreach (var bridge in quarter.Bridges)
                {
                    writer.Write(bridge.Id);
                    RectValue(writer, bridge.DeckBounds);
                    Point(writer, bridge.WestBearingWorld); Point(writer, bridge.EastBearingWorld);
                    Point(writer, bridge.DeckCenterWorld); Point(writer, bridge.BottomWorld);
                }
                foreach (var link in quarter.Links)
                {
                    writer.Write(link.Id); Scalar(writer, CityHillsideQuarterLink.CarriagewayWidth);
                    foreach (var point in link.Centreline) Point(writer, point);
                    foreach (var point in link.NorthSidewalk) Point(writer, point);
                    foreach (var point in link.SouthSidewalk) Point(writer, point);
                }
                var paintedCrossings = CityStreetSurfacePlanner.Create(layout).CrosswalkMarkingGeometry;
                writer.Write(paintedCrossings.Count);
                foreach (var stripe in paintedCrossings)
                {
                    CrosswalkStroke(stripe, out var a, out var b, out float width);
                    Point(writer, a); Point(writer, b); Scalar(writer, width);
                }
                foreach (CityHillsideShortcut stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                {
                    writer.Write(stair.Id); writer.Write(stair.WalkableBounds.Count);
                    foreach (Rect footprint in stair.WalkableBounds) RectValue(writer, footprint);
                    foreach (Vector3 point in stair.RouteWorld) Point(writer, point);
                    foreach (var flight in stair.Flights) { writer.Write(flight.StepCount); Scalar(writer, flight.Width); }
                }
                foreach (CityHillsideBuilding building in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
                {
                    writer.Write(building.Lot.IsPlayerHome);
                    writer.Write(building.Lot.BuildingVariant); Scalar(writer, building.Lot.Height);
                    foreach (Vector2[] body in building.Lot.CreateCollisionPolygons())
                        foreach (Vector2 vertex in body) Point(writer, vertex);
                    RectValue(writer, building.FoundationBounds); RectValue(writer, building.CourtBounds);
                    RectValue(writer, building.ApproachBounds); Point(writer, building.DoorWorld);
                    Point(writer, building.StreetContactWorld);
                }
                CityPortAccessPlan portAccess = CityPortAccessPlan.ForLayout(layout);
                writer.Write(portAccess != null);
                if (portAccess != null)
                {
                    Scalar(writer, portAccess.CarriagewayWidth); writer.Write(portAccess.RoadSamples.Count);
                    foreach (CityPortAccessPlan.RoadSample sample in portAccess.RoadSamples) Point(writer, portAccess.World(sample.center));
                }
                CityEastExitPlan eastExit = CityEastExitPlanner.Create(layout);
                writer.Write(eastExit.IsEnabled);
                if (eastExit.IsEnabled)
                {
                    Point(writer, eastExit.ApproachStart); Point(writer, eastExit.CheckpointPosition);
                    Point(writer, eastExit.RoadEnd); RectValue(writer, eastExit.BoothBounds);
                }
                RectValue(writer, layout.Park.WalkableBounds); writer.Write(layout.Park.TreePositions.Count);
                foreach (Vector3 tree in layout.Park.TreePositions) Point(writer, tree);
                writer.Write(layout.River.Segments.Count);
                foreach (CityRiverSegmentDescriptor segment in layout.River.Segments) RectValue(writer, segment.WaterBounds);
                writer.Write(layout.River.Bridges.Count);
                foreach (CityRiverBridgeDescriptor bridge in layout.River.Bridges) RectValue(writer, bridge.DeckBounds);
                writer.Write(controller.PointsOfInterest.Count);
                foreach (CityMapPointOfInterest point in controller.PointsOfInterest)
                {
                    writer.Write(point.StableId); writer.Write((int)point.Kind); Point(writer, point.WorldPosition);
                    writer.Write(point.HasFootprint); RectValue(writer, point.Footprint);
                }
                CityLaundryPlan laundry = CityLaundryPlan.ForLayout(layout);
                writer.Write(laundry.IsEnabled);
                if (laundry.IsEnabled) foreach (Rect body in laundry.BodyFootprints) RectValue(writer, body);
                CityDogParkPlan park = CityDogParkPlan.ForLayout(layout);
                writer.Write(park.IsEnabled);
                if (park.IsEnabled) RectValue(writer, park.Footprint);
                writer.Write(controller.BusOverlay.Lines.Count);
                foreach (CityMapBusLine line in controller.BusOverlay.Lines)
                {
                    writer.Write(line.RouteId); writer.Write(line.Number); writer.Write(line.Points.Count);
                    foreach (Vector3 point in line.Points) Point(writer, point);
                }
                writer.Write(controller.BusOverlay.Stops.Count);
                foreach (CityMapBusStopMarker stop in controller.BusOverlay.Stops)
                {
                    writer.Write(stop.StableId); writer.Write(stop.Ordinal); Point(writer, stop.WorldPosition);
                    writer.Write(stop.LabelLocalizationKey); writer.Write(stop.RouteNumber); writer.Write(stop.TransferNumber);
                    writer.Write(stop.TransferId); Point(writer, stop.StandingPosition);
                }
                CitySeacoastPlan coast = controller.SeacoastPlan ?? CitySeacoastPlanner.Create(layout);
                writer.Write(coast != null);
                if (coast != null)
                {
                    writer.Write(coast.Parts.Count);
                    foreach (CitySeacoastPartDescriptor part in coast.Parts)
                    {
                        writer.Write(part.StableId); writer.Write((int)part.Kind); Point(writer, part.Center); Point(writer, part.Size);
                        Scalar(writer, part.Rotation.x); Scalar(writer, part.Rotation.y);
                        Scalar(writer, part.Rotation.z); Scalar(writer, part.Rotation.w);
                    }
                    bool hasLantern = CityLighthouseIslandPlanner.TryResolveLanternPosition(coast, out Vector3 lantern);
                    writer.Write(hasLantern); if (hasLantern) Point(writer, lantern);
                }
                CityMountainBoundaryPlan mountains = controller.MountainBoundaryPlan;
                writer.Write(mountains.IsEnabled);
                if (mountains.IsEnabled)
                {
                    writer.Write(mountains.Ridges.Count);
                    foreach (CityMountainRidgeDescriptor ridge in mountains.Ridges)
                    {
                        writer.Write(ridge.StableId); writer.Write(ridge.Stations.Count);
                        foreach (CityMountainRidgeStation station in ridge.Stations)
                        { Point(writer, station.Toe); Point(writer, station.OuterFoot); }
                    }
                    writer.Write(mountains.HasTunnel);
                    if (mountains.HasTunnel) { RectValue(writer, mountains.Tunnel.PortalBounds); RectValue(writer, mountains.Tunnel.ApproachBounds); }
                    writer.Write(mountains.HasRiverCave);
                    if (mountains.HasRiverCave) RectValue(writer, mountains.RiverCave.ApproachBounds);
                }
                // Labels belong to the Russian authored catalogue; the OS's
                // optional existing English overlay must not change geography.
                TextAsset russian = Resources.Load<TextAsset>("Localization/ru");
                Catalog catalog = russian == null ? null : JsonUtility.FromJson<Catalog>(russian.text);
                if (catalog?.entries != null)
                    foreach (Entry entry in catalog.entries)
                        if (entry.key != null && entry.key.StartsWith("map.", StringComparison.Ordinal) &&
                            !entry.key.StartsWith("map.alpine_village.", StringComparison.Ordinal))
                        { writer.Write(entry.key); writer.Write(entry.value ?? string.Empty); }
                writer.Flush();
                using (SHA256 hash = SHA256.Create())
                {
                    stream.Position = 0;
                    byte[] digest = hash.ComputeHash(stream);
                    var text = new StringBuilder(digest.Length * 2);
                    foreach (byte value in digest) text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    return text.ToString();
                }
            }
        }

        internal static void CrosswalkStroke(RuntimeOrientedBox stripe, out Vector3 a, out Vector3 b, out float width)
        {
            bool alongX = stripe.Size.x > stripe.Size.z;
            Vector3 half = stripe.Rotation * (alongX ? Vector3.right * stripe.Size.x : Vector3.forward * stripe.Size.z) * .5f;
            a = stripe.Center - half; b = stripe.Center + half;
            width = Mathf.Min(stripe.Size.x, stripe.Size.z);
        }

        private static void RectValue(BinaryWriter writer, Rect rect)
        { Scalar(writer, rect.x); Scalar(writer, rect.y); Scalar(writer, rect.width); Scalar(writer, rect.height); }
        private static void RoadPoint(BinaryWriter writer, CityLayout layout, RoadEdge edge, Vector2 point, float amount)
        { Point(writer, new Vector3(point.x, layout.ElevationPlan.SampleRoadDatum(edge, amount), point.y)); }
        private static void Point(BinaryWriter writer, Vector2 point) { Scalar(writer, point.x); Scalar(writer, point.y); }
        private static void Point(BinaryWriter writer, Vector3 point) { Scalar(writer, point.x); Scalar(writer, point.y); Scalar(writer, point.z); }
        // A map pixel spans much more than a millimetre. Quantization avoids
        // making OS floating-point noise look like an authored geography edit.
        private static void Scalar(BinaryWriter writer, float value) =>
            writer.Write((long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            artwork = new ConditionalWeakTable<CityMapController, Artwork>();
            fingerprints = new ConditionalWeakTable<CityMapController, Fingerprint>();
            resourcesLoaded = staleWarning = false; texture = null; metadata = null;
        }
    }
}
