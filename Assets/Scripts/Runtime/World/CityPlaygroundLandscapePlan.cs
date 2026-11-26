using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Flush finishes and planted edges share the fitted equipment and its path connections.</summary>
    internal sealed class CityPlaygroundLandscapePlan
    {
        internal enum Finish { None, Concrete, ConcreteEdge, Gravel, GravelEdge }

        internal readonly struct Coat
        {
            internal Coat(Vector2[] outline, Vector2 origin, Vector2 right, Finish finish)
            { Outline = outline; Origin = origin; Right = right; Surface = finish; }
            internal Vector2[] Outline { get; }
            internal Vector2 Origin { get; }
            internal Vector2 Right { get; }
            internal Finish Surface { get; }
        }

        internal sealed class Site
        {
            internal CityCourtyardPlaygroundSite Court { get; }
            internal bool IsPark => Court == null;
            internal Vector2[] Pad { get; }
            internal Vector2[] Connector { get; }
            internal Vector2[] BalancePad { get; }
            internal Vector2[] BalanceConnector { get; }
            internal CityPlaygroundBalancePlan Balance { get; }
            internal Vector2 Approach { get; }
            internal Vector2 Join { get; }
            internal float Height { get; }
            internal IReadOnlyList<Coat> Coats { get; }

            internal Site(CityCourtyardPlaygroundSite court, Vector3 origin, Vector2 right, Vector2 forward,
                Rect localBounds, Vector2 join, Vector2 approach, CityPlaygroundBalancePlan balance)
            {
                Court = court;
                Height = origin.y;
                Join = join;
                Approach = approach;
                Balance = balance;
                Vector2 center = XZ(origin);
                bool park = court == null;
                Finish field = park ? Finish.Gravel : Finish.Concrete;
                Finish rim = park ? Finish.GravelEdge : Finish.ConcreteEdge;
                const float edge = .19f;
                Pad = Chamfered(localBounds, .23f);
                var inset = Rect.MinMaxRect(localBounds.xMin + edge, localBounds.yMin + edge,
                    localBounds.xMax - edge, localBounds.yMax - edge);
                var coats = new List<Coat>(4)
                {
                    new Coat(Pad, center, right, rim),
                    new Coat(Chamfered(inset, .15f), center, right, field),
                };
                if ((approach - join).sqrMagnitude > .01f)
                {
                    Connector = Access(.63f, .82f);
                    coats.Add(new Coat(Connector, center, right, rim));
                    coats.Add(new Coat(Access(.49f, .68f), center, right, field));
                }
                else Connector = Array.Empty<Vector2>();
                if (balance != null)
                {
                    BalancePad = CityRoadPolygon.Rectangle(balance.Footprint);
                    BalanceConnector = AccessSegment(XZ(balance.Branch), XZ(balance.Position), .53f, .55f);
                    coats.Add(new Coat(BalancePad, center, right, field));
                    coats.Add(new Coat(BalanceConnector, center, right, rim));
                    coats.Add(new Coat(AccessSegment(XZ(balance.Branch), XZ(balance.Position), .40f, .42f), center, right, field));
                }
                else BalancePad = BalanceConnector = Array.Empty<Vector2>();
                Coats = coats.AsReadOnly();

                Vector2[] Chamfered(Rect box, float corner) => CityRoadPolygon.CounterClockwise(new[]
                {
                    Point(box.xMin + corner, box.yMin), Point(box.xMax - corner, box.yMin),
                    Point(box.xMax, box.yMin + corner), Point(box.xMax, box.yMax - corner),
                    Point(box.xMax - corner, box.yMax), Point(box.xMin + corner, box.yMax),
                    Point(box.xMin, box.yMax - corner), Point(box.xMin, box.yMin + corner),
                });
                Vector2 Point(float x, float z) => center + right * x + forward * z;

                Vector2[] Access(float outer, float inner)
                {
                    Vector2 along = (approach - join).normalized;
                    return AccessSegment(join, approach + along * .32f, outer, inner);
                }
            }
        }

        private static Vector2[] AccessSegment(Vector2 first, Vector2 last, float entryHalfWidth, float endHalfWidth)
        {
            Vector2 along = (last - first).normalized;
            Vector2 across = new Vector2(-along.y, along.x);
            return CityRoadPolygon.CounterClockwise(new[]
            {
                first - across * entryHalfWidth, first + across * entryHalfWidth,
                last + across * endHalfWidth, last - across * endHalfWidth,
            });
        }

        private CityPlaygroundLandscapePlan(List<Site> sites) => Sites = sites.AsReadOnly();

        internal IReadOnlyList<Site> Sites { get; }

        internal static CityPlaygroundLandscapePlan Create(CityLayout layout, CityResidentialCourtyardPlan courts,
            IReadOnlyList<CityDecorationDescriptor> decorations)
        {
            var sites = new List<Site>(5);
            AddCourt(courts.Playground);
            AddCourt(courts.SlidePlayground);
            AddCourt(courts.BridgePlayground);
            AddCourt(courts.SeesawPlayground);
            foreach (CityDecorationDescriptor descriptor in decorations)
            {
                if (descriptor.Kind != CityDecorationKind.ParkPlayground) continue;
                CityDecorationWorldBuilder.GetDecorationFrame(layout, descriptor,
                    out Vector3 origin, out Vector3 tangent, out Vector3 forward);
                Rect bounds = CityPlaygroundGeometry.ParkGroundBounds;
                float halfWidth = bounds.xMax;
                Vector2 center = XZ(origin), right = XZ(tangent), axis = XZ(forward);
                var mouths = new[]
                {
                    center - right * (halfWidth - .34f), center + right * (halfWidth - .34f),
                    center + axis * (bounds.yMin + .34f) + right * 1.9f, center + axis * (bounds.yMax - .34f),
                };
                FindParkAccess(layout, center, mouths, out Vector2 join, out Vector2 approach);
                Rect equipment = CityRoadPolygon.Bounds(CityPlaygroundGeometry.ParkGroundFootprint(layout, descriptor));
                CityPlaygroundBalancePlan balance = FindParkBalance(layout, origin.y, join, approach, equipment);
                sites.Add(new Site(null, origin, right, axis, bounds, join, approach, balance));
            }
            return new CityPlaygroundLandscapePlan(sites);

            void AddCourt(CityCourtyardPlaygroundSite court)
            {
                if (court == null) return;
                Vector2 center = XZ(court.Position);
                Vector2 right = XZ(court.Rotation * Vector3.right), axis = XZ(court.Rotation * Vector3.forward);
                Vector2 first = Local(court.Footprint.min), last = Local(court.Footprint.max);
                Vector2 low = Vector2.Min(first, last), high = Vector2.Max(first, last);
                var bounds = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
                Vector3 dock = court.HasSeesaw ? CityCourtyardPlaygroundSite.SeesawApproach :
                    court.HasSlide ? court.SideEntry : CityCourtyardPlaygroundSite.BarsOrigin;
                sites.Add(new Site(court, court.Position, right, axis, bounds,
                    XZ(court.RouteJoin), XZ(court.Position + court.Rotation * dock), court.Balance));

                Vector2 Local(Vector2 point)
                {
                    Vector2 offset = point - center;
                    return new Vector2(Vector2.Dot(offset, right), Vector2.Dot(offset, axis));
                }
            }
        }

        private static CityPlaygroundBalancePlan FindParkBalance(CityLayout layout, float height,
            Vector2 join, Vector2 approach, Rect equipment)
        {
            Vector2 along = (approach - join).normalized;
            Vector2 right = new Vector2(-along.y, along.x);
            uint mix = CityGrassPlan.Hash(layout.Seed, Mathf.RoundToInt(join.x), Mathf.RoundToInt(join.y), 3229);
            for (int point = 4; point >= 1; point--)
            for (int side = 0; side < 2; side++)
            {
                Vector2 branch = Vector2.Lerp(join, approach, point / 5f);
                Vector2 axis = right * (((mix + (uint)side) & 1u) == 0 ? 1f : -1f);
                Vector2 dock = branch + axis * .94f;
                Vector2 tip = dock + axis * .79f;
                var pad = Rect.MinMaxRect(Mathf.Min(dock.x, tip.x) - .49f, Mathf.Min(dock.y, tip.y) - .49f,
                    Mathf.Max(dock.x, tip.x) + .49f, Mathf.Max(dock.y, tip.y) + .49f);
                if (pad.Overlaps(equipment) || !ClearParkAccess(layout, branch, tip) ||
                    !CityTerrainSurfacePlan.TrySampleGroundTop(layout, dock, out float top, out _) ||
                    !CityTerrainSurfacePlan.TrySampleGroundTop(layout, tip, out float end, out _) ||
                    Mathf.Abs(top - end) > .075f) continue;
                var position = new Vector3(dock.x, top, dock.y);
                return new CityPlaygroundBalancePlan(position, Quaternion.LookRotation(new Vector3(axis.x, 0, axis.y)),
                    new Vector3(join.x, height, join.y), new Vector3(branch.x, top, branch.y), pad, (mix & 4u) != 0);
            }
            return null;
        }

        private static void FindParkAccess(CityLayout layout, Vector2 center, IReadOnlyList<Vector2> mouths,
            out Vector2 join, out Vector2 approach)
        {
            join = approach = center;
            float best = float.PositiveInfinity;
            float nearest = float.PositiveInfinity;
            Vector2 nearestJoin = center, nearestMouth = center;
            Rect region = layout.Park.WalkableBounds;
            foreach (CityParkRegionPlan section in layout.Park.Regions)
                if (section.WalkableBounds.Contains(center)) { region = section.WalkableBounds; break; }
            region = Rect.MinMaxRect(region.xMin - 2f, region.yMin - 2f, region.xMax + 2f, region.yMax + 2f);
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.ParkPath || layout.TryGetBridge(edge, out _)) continue;
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                foreach (Vector2 mouth in mouths)
                {
                    float distance = path.Project(mouth).DistanceAlong;
                    for (int offset = -2; offset <= 2; offset++)
                    {
                        Vector2 point = path.SampleDistance(distance + offset * 1.6f).Position;
                        if (!region.Contains(point)) continue;
                        float squared = (point - mouth).sqrMagnitude;
                        if (squared < nearest) { nearest = squared; nearestJoin = point; nearestMouth = mouth; }
                        if (squared >= best || !ClearParkAccess(layout, mouth, point)) continue;
                        best = squared;
                        join = point;
                        approach = mouth;
                    }
                }
            }
            if (float.IsPositiveInfinity(best)) { join = nearestJoin; approach = nearestMouth; }
        }

        private static bool ClearParkAccess(CityLayout layout, Vector2 first, Vector2 last)
        {
            Vector2 span = last - first;
            float lengthSquared = span.sqrMagnitude;
            if (lengthSquared < .01f) return true;
            foreach (Vector3 tree in layout.Park.TreePositions)
                if (Distance(XZ(tree)) < 1.10f) return false;
            foreach (CityParkBenchDescriptor bench in layout.Park.Benches)
                if (Distance(XZ(bench.Position)) < 1.15f) return false;
            int steps = Mathf.CeilToInt(Mathf.Sqrt(lengthSquared) / .5f);
            for (int step = 0; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(first, last, step / (float)steps);
                if (layout.IsWater(new Vector3(point.x, 0f, point.y))) return false;
            }
            return true;

            float Distance(Vector2 point) => Vector2.Distance(point,
                first + span * Mathf.Clamp01(Vector2.Dot(point - first, span) / lengthSquared));
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
    }
}
