using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        // The quay cannot move all four corner pavements outwards: one quadrant is water.
        // Prove individual left/right approaches over the *existing* carriageway. Each
        // macro occupies both streets, exactly like the ordinary two-edge wide-right turn.
        private static void AddDistrictQuayTurns(CityLayout layout, IReadOnlyList<TemporaryNode> nodes,
            List<TemporaryLink> links)
        {
            var near = new List<TemporaryLink>();
            foreach (var link in links)
                if (link.IsRoadSegment && !layout.RoadGeometry.IsCurved(link.RoadEdge) &&
                    link.RoadEdge.A.x >= 3 && link.RoadEdge.B.x <= 10 && link.RoadEdge.A.y >= 8) near.Add(link);
            var support = CityStreetSupportPlan.ForLayout(layout);
            var signals = new HashSet<Vector2Int>(CityStreetIntersectionSelector.Select(layout));
            var vehicle = CityBusDesignVehicle.Default;
            foreach (var incoming in near)
            foreach (var outgoing in near)
            {
                var a = nodes[incoming.FromNodeIndex];
                var b = nodes[outgoing.FromNodeIndex];
                if (a.ToGridNode != b.FromGridNode || a.ToGridNode.y < 9 ||
                    a.ToGridNode.x < 4 || a.ToGridNode.x > 9) continue;
                Vector3 first = incoming.Samples[0].Forward;
                Vector3 next = outgoing.Samples[0].Forward;
                first.y = next.y = 0;
                first.Normalize(); next.Normalize();
                if (Mathf.Abs(Vector3.Dot(first, next)) > .001f) continue;
                bool existing = links.Exists(link =>
                    link.FromNodeIndex == incoming.ToNodeIndex && link.ToNodeIndex == outgoing.FromNodeIndex ||
                    link.FromNodeIndex == incoming.FromNodeIndex && link.ToNodeIndex == outgoing.ToNodeIndex);
                if (existing) continue;
                Vector3 centre = layout.GetNodeWorldPosition(a.ToGridNode);
                Vector3 start = incoming.Samples[0].Position;
                Vector3 end = outgoing.Samples[outgoing.Samples.Count - 1].Position;
                float before = Vector3.Dot(centre - start, first), after = Vector3.Dot(end - centre, next);
                if (before < 19f || after < 19f) continue;
                bool rightTurn = Vector3.Dot(Vector3.Cross(Vector3.up, first), next) > 0;
                float sign = rightTurn ? 1f : -1f;
                var laneRight = Vector3.Cross(Vector3.up, first);
                var exitRight = Vector3.Cross(Vector3.up, next);
                const float radius = 7f, innerLane = .6f, inset = radius - innerLane;
                float offset = -sign * innerLane;
                Vector3 pivot = centre - first * inset + laneRight * (offset + sign * radius);
                var samples = new List<CityBusPathSample>(180);
                bool clear = true;
                Approach(first, laneRight, before, true);
                for (int j = 1; clear && j <= 48; j++)
                {
                    float angle = Mathf.PI * .5f * j / 48;
                    Add(pivot + radius * (-sign * laneRight * Mathf.Cos(angle) + first * Mathf.Sin(angle)),
                        first * Mathf.Cos(angle) + sign * laneRight * Mathf.Sin(angle));
                }
                Approach(next, exitRight, after, false);
                if (!clear) continue;
                var proof = ValidateStaticIntersectionFixtures(layout, vehicle, a.ToGridNode, signals.Contains(a.ToGridNode),
                    samples, CreateClearResult(vehicle));
                if (!proof.IsClear) continue;
                links.Add(new TemporaryLink("bus:quay:" + incoming.Id + ":" + outgoing.Id,
                    incoming.FromNodeIndex, outgoing.ToNodeIndex,
                    rightTurn ? CityBusRouteLinkKind.RightTurn : CityBusRouteLinkKind.LeftTurn,
                    a.ToGridNode, samples, radius, proof, false, incoming.RoadEdge, true, true, outgoing.RoadEdge));

                void Approach(Vector3 travel, Vector3 side, float extent, bool entering)
                {
                    int count = Mathf.CeilToInt((extent - inset) / .25f);
                    for (int j = entering ? 0 : 1; clear && j <= count; j++)
                    {
                        float t = entering ? j / (float)count : 1f - j / (float)count;
                        float blend = t*t*t*(4f-3f*t);
                        float derivative = 12f*t*t*(1f-t);
                        float shift = Mathf.Lerp(1.5f, offset, blend);
                        float station = Mathf.Lerp(extent, inset, t);
                        float slope = (entering ? 1f : -1f) * (offset - 1.5f) * derivative / (extent - inset);
                        Add(centre + travel * (entering ? -station : station) + side * shift,
                            (travel + side * slope).normalized);
                    }
                }

                void Add(Vector3 point, Vector3 forward)
                {
                    Vector2 center = new Vector2(point.x, point.z);
                    Vector2 along = new Vector2(forward.x, forward.z);
                    Vector2 lateral = new Vector2(along.y, -along.x);
                    if (!support.TrySampleTop(center, FootstepGroundKind.Concrete, out float height)) { clear = false; return; }
                    for (int lengthSign = -1; lengthSign <= 1; lengthSign += 2)
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                    {
                        var corner = center + along * (lengthSign * vehicle.InflatedLength * .5f) +
                                     lateral * (sideSign * vehicle.InflatedWidth * .5f);
                        if (!support.TrySampleTop(corner, FootstepGroundKind.Concrete, out float road) ||
                            support.TrySampleTop(corner, FootstepGroundKind.Stone, out float kerb) && kerb > road + .02f)
                        { clear = false; return; }
                    }
                    point.y = height;
                    if (samples.Count > 0)
                    {
                        var previous = samples[samples.Count-1].Position;
                        float run = Vector2.Distance(center, new Vector2(previous.x, previous.z));
                        if (run > .001f) forward.y = (height - previous.y) / run;
                    }
                    AppendPoint(samples, point, forward.normalized);
                }
            }
        }
    }
}
