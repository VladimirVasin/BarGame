using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The southern line shares the checked city grid at its foot. Outside
    /// the grid it follows the actual offset lanes and height profiles of both hills.</summary>
    internal sealed class CityHillsideBusPlanner
    {
        private const float LaneOffset = CityHillsideStreetRoute.RoadHalfWidth * .5f;
        private const float CornerRadius = 7f;
        private const float TurnLaneOffset = .6f;
        private const float MouthApproach = 20f;
        private const float TangentInset = CornerRadius - TurnLaneOffset;
        private readonly CityLayout layout;
        private readonly CityStreetSupportPlan paving;
        private readonly string routeId;
        private readonly List<CityBusRouteLink> links = new();
        private readonly List<CityBusRouteNode> nodes = new();
        private readonly List<CityBusStopDescriptor> stops = new();
        private readonly List<CityBusSpawnAnchor> anchors = new();
        private readonly List<int> order = new();
        private readonly List<float> starts = new();
        private readonly Dictionary<int, (float distance, Vector3 pole, RoadEdge edge)> streetPlatforms = new();
        private readonly CityBusDesignVehicle vehicle = CityBusDesignVehicle.Default;
        private float length;

        private CityHillsideBusPlanner(CityLayout layout)
        {
            this.layout = layout;
            paving = CityStreetSupportPlan.ForLayout(layout);
            routeId = "bus-route:" + layout.BlueprintId + ":route-03";
        }

        internal static CityBusPlan Create(CityLayout layout, IReadOnlyList<CityBusRouteLink> southReturn,
            IReadOnlyList<CityBusRouteNode> southNodes) =>
            new CityHillsideBusPlanner(layout).Build(southReturn, southNodes);

        private CityBusPlan Build(IReadOnlyList<CityBusRouteLink> grid, IReadOnlyList<CityBusRouteNode> gridNodes)
        {
            var hills = CityHillsideStreetPlan.ForLayout(layout).Routes;
            var summit = CityHillsideQuarterPlan.ForLayout(layout).Links[1];
            var west = hills[0];
            var east = hills[1];
            for (int i = 0; i < grid.Count; i++)
            {
                var original = grid[i];
                // Reserve the approach for the long vehicle to ease towards
                // the centre of the road before it sweeps across the mouth.
                float trim = MouthApproach - layout.RoadWidth * .5f;
                var samples = Clip(original, i == 0 ? trim : 0f, i == grid.Count - 1 ? trim : 0f);
                AddLink(original.Kind, original.JunctionNode, samples,
                    original.MinimumTurnRadius, original.Clearance, gridNodes[i].RoadEdge,
                    gridNodes[i].FromGridNode, gridNodes[i].ToGridNode, false);
            }

            // East to west along the Works bridge. Preserve a clear pavement dock at
            // each end, and ordinary stops on the streets between the two interchanges.
            int last = grid.Count - 1;
            int eastLink = FindGridStop(east, 0, last, 1);
            int westLink = FindGridStop(west, last, 0, -1);
            float previous = 0f;
            if (eastLink >= 0)
            {
                AddStreetStop(eastLink, "transfer:east-slope", "map.bus.transfer.east_slope");
                previous = stops[stops.Count - 1].DistanceAlongLoop;
            }
            int westBoundary = westLink < 0 ? last : westLink;
            for (int i = eastLink + 1; i < westBoundary; i++)
            {
                if (starts[i] + links[i].Length * .5f - previous < 180f ||
                    starts[westBoundary] - starts[i] < 135f || !CanServeStreet(i)) continue;
                AddStreetStop(i, "south-street-" + i, "map.bus.stop.south_street");
                previous = stops[stops.Count - 1].DistanceAlongLoop;
            }
            if (westLink >= 0) AddStreetStop(westLink, "transfer:west-slope", "map.bus.transfer.west_slope");

            var westLane = HillLane(west, true);
            var eastLane = HillLane(east, false);
            AddMouth(west, true, links[last].Samples[links[last].Samples.Count - 1].Position, westLane[0]);
            AddHill(west, westLane, westLink < 0);
            var bridge = new List<Vector3>();
            foreach (Vector3 point in summit.Centreline)
                bridge.Add(point + Vector3.back * LaneOffset);
            AddHillLink(bridge, float.PositiveInfinity, CityBusRouteLinkKind.Straight);
            AddHill(east, eastLane, eastLink < 0);
            AddMouth(east, false, eastLane[eastLane.Count - 1], links[0].Samples[0].Position, true);
            return new CityBusPlan(layout.Seed, layout.Seed, unchecked((uint)layout.Seed ^ 0x42555333u),
                routeId, order, length, LaneOffset, vehicle, nodes, links, anchors, stops,
                Array.Empty<CityBusClearanceFailure>(), nodes.Count, links.Count);
        }

        private static List<CityBusPathSample> Clip(CityBusRouteLink link, float start, float end)
        {
            var result = new List<CityBusPathSample>(link.Samples.Count);
            float finish = link.Length - end;
            for (int i = 1; i < link.Samples.Count; i++)
            {
                var a = link.Samples[i - 1];
                var b = link.Samples[i];
                if (b.Distance <= start || a.Distance >= finish) continue;
                if (result.Count == 0) Append(start);
                Append(Mathf.Min(b.Distance, finish));

                void Append(float distance)
                {
                    float t = Mathf.InverseLerp(a.Distance, b.Distance, distance);
                    result.Add(new CityBusPathSample(Vector3.Lerp(a.Position, b.Position, t),
                        Vector3.Lerp(a.Forward, b.Forward, t).normalized, distance - start));
                }
            }
            return result;
        }

        private static List<Vector3> HillLane(CityHillsideStreetRoute hill, bool climbing)
        {
            var lane = hill.Path.Offset(climbing ? LaneOffset : -LaneOffset);
            var result = new List<Vector3>(lane.Vertices.Count);
            float cut = MouthApproach - CityHillsideStreetRoute.RoadHalfWidth;
            var tangent = lane.SampleDistance(cut);
            result.Add(new Vector3(tangent.Position.x, hill.Profile.SampleDistance(cut).Height, tangent.Position.y));
            for (int i = 1; i < lane.Vertices.Count; i++)
            {
                if (hill.Profile.Stations[i].HorizontalDistance <= cut + .001f) continue;
                Vector2 p = lane.Vertices[i];
                result.Add(new Vector3(p.x, hill.Profile.Stations[i].Height, p.y));
            }
            if (!climbing) result.Reverse();
            return result;
        }

        private void AddMouth(CityHillsideStreetRoute hill, bool west, Vector3 start, Vector3 end, bool closes = false)
        {
            Vector3 mouth = layout.GetNodeWorldPosition(hill.MouthNode);
            mouth.y += CityStreetSurfacePlanner.RoadTop;
            Vector3 incoming = west ? Vector3.left : Vector3.forward;
            Vector3 outgoing = west ? Vector3.back : Vector3.left;
            Vector3 inRight = Vector3.Cross(Vector3.up, incoming);
            Vector3 outRight = Vector3.Cross(Vector3.up, outgoing);
            Vector3 centre = mouth - incoming * TangentInset - inRight * (CornerRadius - TurnLaneOffset);
            var points = new List<Vector3> { start };
            const int steps = 36;
            Approach(incoming, inRight, true);
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.PI * .5f * i / steps;
                Add(centre + (inRight * Mathf.Cos(a) + incoming * Mathf.Sin(a)) * CornerRadius);
            }
            Approach(outgoing, outRight, false);
            points[points.Count - 1] = end;
            AddHillLink(points, CornerRadius, CityBusRouteLinkKind.LeftTurn, closes);

            void Approach(Vector3 travel, Vector3 right, bool entering)
            {
                for (int i = 1; i <= steps; i++)
                {
                    float t = entering ? i / (float)steps : 1f - i / (float)steps;
                    float blend = t * t * t * (4f - 3f * t);
                    float lateral = Mathf.Lerp(LaneOffset, TurnLaneOffset, blend);
                    float station = Mathf.Lerp(MouthApproach, TangentInset, t);
                    Add(mouth + travel * (entering ? -station : station) + right * lateral);
                }
            }
            void Add(Vector3 point)
            {
                if (paving.TrySampleTop(new Vector2(point.x, point.z), FootstepGroundKind.Concrete, out float top))
                    point.y = top;
                points.Add(point);
            }
        }

        private void AddHill(CityHillsideStreetRoute hill, List<Vector3> lane, bool transferAtFoot)
        {
            int index = links.Count;
            AddHillLink(lane, 9f - LaneOffset, CityBusRouteLinkKind.Straight);
            bool west = hill.Bank == CityTerraceTransportBank.West;
            for (int n = 0; n < CityHillsideStreetRoute.LaneCount; n++)
            {
                int shelf = west ? n : CityHillsideStreetRoute.LaneCount - n - 1;
                // Each platform serves neighbouring terraces by their public stairs.
                // The shared crest is served once, just off the bridge on the west.
                if (shelf % 2 != 0 || !west && shelf == CityHillsideStreetRoute.LaneCount - 1) continue;
                string target = (west ? "west-" : "east-") + "terrace-" + (shelf + 1);
                string key = shelf == CityHillsideStreetRoute.LaneCount - 1 && west
                    ? "map.bus.stop.summit" : (west ? "map.bus.stop.west_terrace" : "map.bus.stop.east_terrace") +
                    (shelf == 0 ? ".lower" : shelf == 2 ? ".middle" : ".upper");
                bool transfer = transferAtFoot && shelf == 0;
                if (transfer)
                {
                    target = west ? "transfer:west-slope" : "transfer:east-slope";
                    key = west ? "map.bus.transfer.west_slope" : "map.bus.transfer.east_slope";
                }
                bool crest = shelf == CityHillsideStreetRoute.LaneCount - 1;
                float idealX = transfer ? layout.RoadGeometry.GetNodePosition(hill.MouthNode).x :
                    crest ? hill.LaneMaxX - 8f : (hill.LaneMinX + hill.LaneMaxX) * .5f;
                var samples = links[index].Samples;
                float best = float.PositiveInfinity;
                CityBusPathSample chosen = default;
                Vector3 chosenPole = default;
                for (int i = 0; i < samples.Count; i++)
                {
                    var sample = samples[i];
                    if (Mathf.Abs(sample.Position.z - hill.GetLaneZ(shelf)) > LaneOffset + .02f ||
                        Mathf.Abs(sample.Forward.x) < .995f) continue;
                    float cost = Mathf.Abs(sample.Position.x - idealX);
                    if (cost >= best || !TryPlaceHillStop(hill, sample, out var pole)) continue;
                    best = cost;
                    chosen = sample;
                    chosenPole = pole;
                }
                if (float.IsInfinity(best))
                    throw new CityDistrictServiceUnavailable("No hillside pavement dock for " + target);
                AddStop(index, chosen, target, key, chosenPole, home: crest && layout.HasSummitHome ? layout.PlayerHome : null);
            }
        }

        private bool TryPlaceHillStop(CityHillsideStreetRoute hill, CityBusPathSample sample, out Vector3 pole)
        {
            var clearance = CityBusPlatformClearance.ForLayout(layout);
            Vector3 forward = Vector3.ProjectOnPlane(sample.Forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // Hill walkers use the middle of the two-metre pavement. Reserve the
            // full capsule width through both ends of the shelter, including the
            // final curves of the entrance. Keep the roof behind this walking strip.
            const float walkingHalfWidth = .52f;
            float pavementMiddle = (CityHillsideStreetRoute.RoadHalfWidth + CityHillsideStreetRoute.OuterHalfWidth) * .5f;
            for (float setback = pavementMiddle + walkingHalfWidth + .79f; setback <= 6.7f; setback += .25f)
            {
                pole = sample.Position + right * (setback - LaneOffset);
                if (!clearance.TryHeight(pole, out float height)) continue;
                pole.y = height;
                if (!clearance.Allows(sample.Position, forward, pole, -right)) continue;
                Rect roof = CityBusStopWorldBuilder.DescribeFurnitureFootprint(pole, forward, -right);
                roof = Rect.MinMaxRect(roof.xMin - walkingHalfWidth, roof.yMin - walkingHalfWidth,
                    roof.xMax + walkingHalfWidth, roof.yMax + walkingHalfWidth);
                var walk = hill.Bank == CityTerraceTransportBank.West ? hill.SidewalkPath : hill.OppositeSidewalkPath;
                bool crossing = false;
                for (int i = 1; i < walk.Count && !crossing; i++)
                {
                    Vector3 a = walk[i - 1], b = walk[i];
                    Rect span = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                        Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
                    crossing = span.xMax >= roof.xMin && span.xMin <= roof.xMax &&
                        span.yMax >= roof.yMin && span.yMin <= roof.yMax;
                }
                if (!crossing) return true;
            }
            pole = default;
            return false;
        }

        private int FindGridStop(CityHillsideStreetRoute hill, int start, int end, int step)
        {
            Vector3 mouth = layout.GetNodeWorldPosition(hill.MouthNode);
            for (int i = start; step > 0 ? i <= end : i >= end; i += step)
            {
                if ((nodes[i].Position - mouth).sqrMagnitude <= 45f * 45f && CanServeStreet(i))
                    return i;
            }
            // Where the city-side kerb is occupied, the first straight on the hill
            // serves the interchange. It replaces the ordinary stop on that terrace.
            return -1;
        }

        private bool CanServeStreet(int index)
        {
            if (streetPlatforms.ContainsKey(index)) return true;
            var edge = nodes[index].RoadEdge;
            var link = links[index];
            float distance = link.Length * .5f;
            Vector3 pole;
            if (link.Kind == CityBusRouteLinkKind.Straight)
            {
                if (layout.IsBusFurnitureExcluded(edge) || layout.River.TryGetBridge(edge, out _) ||
                    !CityBusPlanner.TryPlaceDistrictStreetStop(layout, link, ref distance, out pole)) return false;
            }
            else if (index + 1 >= nodes.Count ||
                !CityBusPlanner.TryMacroPlatform(layout, link, nodes[index], nodes[index + 1],
                    distance, out distance, out pole, out edge)) return false;
            streetPlatforms.Add(index, (distance, pole, edge));
            return true;
        }

        private void AddStreetStop(int index, string target, string key)
        {
            var site = streetPlatforms[index];
            CityBusActor.EvaluateLink(links[index], site.distance, out var position, out var forward);
            AddStop(index, new CityBusPathSample(position, forward, site.distance), target, key, site.pole, site.edge);
        }

        private void AddStop(int index, CityBusPathSample sample, string target, string key, Vector3 pole, RoadEdge? roadEdge = null,
            BuildingLot home = null)
        {
            Vector3 right = Vector3.Cross(Vector3.up, sample.Forward).normalized;
            if (CityBusPlatformClearance.ForLayout(layout).TryHeight(pole, out float ground)) pole.y = ground;
            var node = nodes[index];
            var kind = target.StartsWith("transfer:", StringComparison.Ordinal)
                ? CityBusStopTargetKind.Transfer : CityBusStopTargetKind.LoopSpacing;
            string platformId = routeId + ":" + target;
            if (home != null)
            {
                kind = CityBusStopTargetKind.PlayerHome;
                key = "bus.stop." + layout.BlueprintId.Replace('-', '_') + ".home";
                target = "player-home:" + layout.BlueprintId + ":" + home.Cell.x + ":" + home.Cell.y;
            }
            stops.Add(new CityBusStopDescriptor(platformId, "", key, stops.Count,
                starts[index] + sample.Distance, CityDistrictKind.Residential, pole, -right, index,
                sample.Distance, sample.Position, sample.Forward, roadEdge ?? node.RoadEdge, kind, target, home?.Cell ?? node.ToGridNode));
        }

        private void AddHillLink(List<Vector3> points, float radius, CityBusRouteLinkKind kind, bool closes = false)
        {
            var samples = new List<CityBusPathSample>();
            Vector3 previous = points[0];
            float distance = 0;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                if ((b - a).sqrMagnitude < .00001f) continue;
                Vector3 tangent = (b - a).normalized;
                Vector3 from = i == 0 ? tangent : ((a - points[i - 1]).normalized + tangent).normalized;
                Vector3 to = i + 2 == points.Count ? tangent : ((points[i + 2] - b).normalized + tangent).normalized;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .5f));
                for (int j = i == 0 ? 0 : 1; j <= steps; j++)
                {
                    float t = j / (float)steps;
                    Vector3 position = Vector3.Lerp(a, b, t);
                    Vector3 forward = Vector3.Slerp(from, to, t);
                    // Long straights stay parallel to the kerb right up to their
                    // bend. Only blend tangents within one sample of a vertex.
                    if (steps > 4 && j > 0 && j < steps) forward = tangent;
                    distance += Vector3.Distance(previous, position);
                    samples.Add(new CityBusPathSample(position, forward, distance));
                    previous = position;
                }
            }
            VerifyEnvelope(samples);
            var clear = new CityBusClearanceResult(true, CityBusClearanceFailureKind.None, -1, default, vehicle.ClearanceMargin);
            AddLink(kind, default, samples, radius, clear, default, default, default, closes);
        }

        private void VerifyEnvelope(List<CityBusPathSample> samples)
        {
            foreach (var pose in samples)
            {
                Vector2 center = new Vector2(pose.Position.x, pose.Position.z);
                Vector2 f = new Vector2(pose.Forward.x, pose.Forward.z).normalized;
                Vector2 right = new Vector2(f.y, -f.x);
                for (int end = -1; end <= 1; end += 2)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 corner = center + end * vehicle.InflatedLength * .5f * f +
                        side * vehicle.InflatedWidth * .5f * right;
                    if (!paving.TrySampleTop(corner, FootstepGroundKind.Concrete, out float road) ||
                        paving.TrySampleTop(corner, FootstepGroundKind.Stone, out float curb) && curb > road + .02f)
                        throw new CityDistrictServiceUnavailable($"Hillside bus at {center} leaves carriageway at {corner}.");
                }
            }
        }

        private void AddLink(CityBusRouteLinkKind kind, Vector2Int junction, List<CityBusPathSample> samples,
            float radius, CityBusClearanceResult clearance, RoadEdge edge, Vector2Int from, Vector2Int to, bool closes)
        {
            int i = links.Count;
            var first = samples[0];
            string id = routeId + ":link:" + i;
            nodes.Add(new CityBusRouteNode(id, first.Position, first.Forward, edge, from, to, new[] { i }));
            var link = new CityBusRouteLink(id, i, closes ? 0 : i + 1, kind, junction, samples, radius, clearance);
            order.Add(i);
            starts.Add(length);
            links.Add(link);
            length += link.Length;
            if (kind != CityBusRouteLinkKind.Straight) return;
            float nextAnchor = 0f;
            for (int j = 0; j < samples.Count; j++)
            {
                var pose = samples[j];
                if (pose.Distance < nextAnchor) continue;
                anchors.Add(new CityBusSpawnAnchor(id + ":spawn:" + j, i, pose.Distance, pose.Position, pose.Forward, edge));
                nextAnchor = pose.Distance + 12f;
            }
        }
    }
}
