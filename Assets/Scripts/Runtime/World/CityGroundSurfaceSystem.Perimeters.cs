using System;
using System.Collections.Generic;
using UnityEngine;
using Triangle = BarPromenade.GroundSurfacePartitioner.Triangle;
using Vertex = BarPromenade.GroundSurfacePartitioner.Vertex;
using Footprint = BarPromenade.GroundSurfacePartitioner.Footprint;

namespace BarPromenade
{
    internal sealed partial class CityGroundSurfaceSystem
    {
        private CityBuildingPerimeterPlan buildingPerimeters;
        private HomeExteriorContextPlan perimeterHome;
        private Footprint[] perimeterOuter, perimeterBodies;
        private int perimeterStyle = -1;
        internal int BuildingPerimeterTriangleCount { get; private set; }

        internal void SetBuildingPerimeters(CityBuildingPerimeterPlan plan, HomeExteriorContextPlan home = null)
        {
            if (sealedSurface) throw new InvalidOperationException("Building perimeters must precede ground completion.");
            buildingPerimeters = plan ?? throw new ArgumentNullException(nameof(plan));
            perimeterHome = home;
            perimeterOuter = new Footprint[plan.Footprints.Count];
            perimeterBodies = new Footprint[perimeterOuter.Length];
            for (int i = 0; i < perimeterOuter.Length; i++)
            {
                var footprint = buildingPerimeters.Footprints[i];
                perimeterOuter[i] = home == null ? footprint.OuterFootprint : ToLocal(footprint.Outer);
                perimeterBodies[i] = home == null ? footprint.BodyFootprint : ToLocal(footprint.Body);
            }
        }

        private List<Triangle> ApplyBuildingPerimeters(List<Triangle> surface)
        {
            if (buildingPerimeters == null) return surface;
            if (perimeterStyle < 0)
                perimeterStyle = FindStyle(new Style(CityExteriorAppearance.CreateBuildingPerimeterProperties()));
            var result = new List<Triangle>(surface.Count);
            var candidates = new List<int>();
            foreach (Triangle triangle in surface)
            {
                Style style = styles[triangle.Surface];
                float kind = style.Properties.GetFloat(Shader.PropertyToID("_GroundKind"));
                // Existing stone, gravel, sand and authored paths own their
                // surfaces. Only the exposed soil/lawn beside walls is paved.
                if ((style.Kind != FootstepGroundKind.Soil && style.Kind != FootstepGroundKind.Grass) ||
                    (kind != (float)GroundSurfaceKind.Soil && kind != (float)GroundSurfaceKind.Lawn))
                { result.Add(triangle); continue; }
                Vector3 a = ToCity(triangle.A.Position), b = ToCity(triangle.B.Position), c = ToCity(triangle.C.Position);
                buildingPerimeters.Query(Rect.MinMaxRect(Mathf.Min(a.x, Mathf.Min(b.x, c.x)),
                    Mathf.Min(a.z, Mathf.Min(b.z, c.z)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                    Mathf.Max(a.z, Mathf.Max(b.z, c.z))), candidates);
                if (candidates.Count == 0) { result.Add(triangle); continue; }

                var remaining = new List<Triangle> { triangle };
                var covered = new List<Triangle>();
                // Consume the union once, including overlapping aprons in a
                // narrow alley, before removing every adjoining building body.
                foreach (int candidate in candidates)
                {
                    var outside = new List<Triangle>();
                    foreach (Triangle part in remaining)
                    {
                        covered.AddRange(GroundSurfacePartitioner.Intersect(part, perimeterOuter[candidate]));
                        outside.AddRange(GroundSurfacePartitioner.Subtract(part, perimeterOuter[candidate]));
                    }
                    remaining = outside;
                    if (remaining.Count == 0) break;
                }
                foreach (int candidate in candidates)
                {
                    var outside = new List<Triangle>();
                    foreach (Triangle part in covered)
                    {
                        remaining.AddRange(GroundSurfacePartitioner.Intersect(part, perimeterBodies[candidate]));
                        outside.AddRange(GroundSurfacePartitioner.Subtract(part, perimeterBodies[candidate]));
                    }
                    covered = outside;
                    if (covered.Count == 0) break;
                }
                result.AddRange(remaining);
                foreach (Triangle part in covered)
                    result.Add(new Triangle(AsphaltVertex(part.A), AsphaltVertex(part.B), AsphaltVertex(part.C), perimeterStyle));
                if (!sealedSurface) BuildingPerimeterTriangleCount += covered.Count;
            }
            return result;
        }

        private Footprint ToLocal(IReadOnlyList<Vector2> polygon)
        {
            var points = new Vector2[polygon.Count];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = PlayerHomeBalconyGeometry.ToHomeLocal(perimeterHome.PlayerHome,
                    new Vector3(polygon[i].x, 0f, polygon[i].y));
                points[i] = new Vector2(point.x, point.z);
            }
            return new Footprint(points);
        }

        private Vector3 ToCity(Vector3 point) => perimeterHome == null ? point :
            PlayerHomeBalconyGeometry.ToCityWorld(perimeterHome.PlayerHome, point);

        private Vertex AsphaltVertex(Vertex source)
        {
            Vector3 city = ToCity(source.Position);
            return new Vertex(source.Position, source.Normal,
                new Vector2(city.x, city.z) / CityExteriorAppearance.RoadTextureTileSize, Vector4.zero, Color.white);
        }
    }
}
