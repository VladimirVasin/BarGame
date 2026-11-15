using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reuse surveyed public lanes, never the Ferryman's island/tunnel access.
    /// Three copies let the existing speed planner look through the loop seam.</summary>
    public sealed class CityAmbientCarPlan
    {
        public readonly LastRouteCarDrivePath Path;
        public readonly float Length;
        public readonly string RouteId;
        private readonly Vector2[] junctions;

        public CityAmbientCarPlan(CityBusPlan route, CityStreetSupportPlan paving)
        {
            if (route == null || route.IsEmpty) throw new ArgumentException("Traffic needs a surveyed road loop.");
            if (paving == null) throw new ArgumentNullException(nameof(paving));
            RouteId = route.RouteId;
            var survey = new List<(CityBusPathSample sample, bool turn)>();
            int cut = -1;
            foreach (int index in route.OrderedLinkIndices)
            {
                var link = route.Links[index];
                if (!link.Clearance.IsClear) throw new ArgumentException("Unvalidated traffic corridor: " + link.Id);
                // Put the lap seam within a road, away from a junction's endpoint
                // corrections. Those corrections can retrace a short piece of lane.
                if (cut < 0 && link.Kind == CityBusRouteLinkKind.Straight && link.Length > 12f)
                    cut = survey.Count + link.Samples.Count / 2;
                foreach (var sample in link.Samples)
                    survey.Add((sample, link.Kind != CityBusRouteLinkKind.Straight));
            }
            if (cut < 0) throw new ArgumentException("Traffic needs a continuous road: " + RouteId);
            var loop = new List<Vector3>();
            var crossings = new List<Vector2>();
            double distance = 0d;
            float crossingStart = -1f;
            for (int offset = 0; offset <= survey.Count; offset++)
            {
                var station = survey[(cut + offset) % survey.Count];
                Vector3 point = station.sample.Position;
                if (paving.TrySampleTop(new Vector2(point.x, point.z), FootstepGroundKind.Concrete, out float top))
                    point.y = top;
                if (loop.Count > 0)
                {
                    Vector3 delta = point - loop[loop.Count - 1];
                    Vector3 planar = new Vector3(delta.x, 0f, delta.z);
                    if (planar.sqrMagnitude < .0001f || Vector3.Dot(planar, station.sample.Forward) <= 0f) continue;
                    if (delta.magnitude > 8f) throw new ArgumentException("Disconnected traffic lane: " + RouteId);
                    distance += delta.magnitude;
                }
                if (station.turn && crossingStart < 0f) crossingStart = (float)distance;
                if (!station.turn && crossingStart >= 0f)
                { crossings.Add(new Vector2(crossingStart, (float)distance)); crossingStart = -1f; }
                loop.Add(point);
            }
            if (loop.Count < 3 || Vector3.Distance(loop[0], loop[loop.Count - 1]) > .25f)
                throw new ArgumentException("Traffic lane is not closed: " + RouteId);
            loop[loop.Count - 1] = loop[0];
            Length = new LastRouteCarDrivePath(loop).Length;
            var repeated = new List<Vector3>(loop.Count * 3);
            for (int lap = 0; lap < 3; lap++)
                for (int i = lap == 0 ? 0 : 1; i < loop.Count; i++) repeated.Add(loop[i]);
            Path = new LastRouteCarDrivePath(repeated);
            junctions = crossings.ToArray();
        }

        public float KeepJunctionExitClear(float distance, float hold)
        {
            if (float.IsPositiveInfinity(hold)) return hold;
            float lap = Mathf.Floor(distance / Length) * Length;
            for (int copy = 0; copy < 2; copy++)
            foreach (Vector2 crossing in junctions)
            {
                float stop = lap + copy * Length + crossing.x - 4f;
                float end = lap + copy * Length + crossing.y + 7f;
                if (distance <= stop && hold > stop && hold < end) hold = stop;
            }
            return hold;
        }
    }
}
