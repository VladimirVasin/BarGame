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
        private const float CourtyardBucketSize = 8f;
        private readonly Dictionary<Vector2Int, List<int>> courtyardBuckets = new Dictionary<Vector2Int, List<int>>();
        private readonly List<CourtyardMask> courtyardMasks = new List<CourtyardMask>();
        private HomeExteriorContextPlan courtyardHome;
        private readonly Dictionary<(CityResidentialCourtyardSurfaceKind, bool), int> courtyardStyles =
            new Dictionary<(CityResidentialCourtyardSurfaceKind, bool), int>();
        internal int ResidentialCourtyardTriangleCount { get; private set; }

        internal void SetResidentialCourtyards(CityResidentialCourtyardPlan plan, HomeExteriorContextPlan home = null)
        {
            if (sealedSurface) throw new InvalidOperationException("Courtyard surfaces must precede ground completion.");
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            courtyardHome = home;
            courtyardMasks.Clear(); courtyardBuckets.Clear();
            foreach (CityResidentialCourtyardSurfaceDescriptor surface in plan.Surfaces)
            {
                var points = new Vector2[surface.Polygon.Count];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 p = surface.Polygon[i];
                    Vector3 local = home == null ? new Vector3(p.x, 0f, p.y) :
                        PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, new Vector3(p.x, 0f, p.y));
                    points[i] = new Vector2(local.x, local.z);
                }
                Rect bounds = CityRoadPolygon.Bounds(points);
                int index = courtyardMasks.Count;
                courtyardMasks.Add(new CourtyardMask(new Footprint(points), bounds, surface.Kind,
                    surface.IsSheltered, surface.IsCommonGround));
                for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
                for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!courtyardBuckets.TryGetValue(key, out List<int> entries)) courtyardBuckets.Add(key, entries = new List<int>());
                    entries.Add(index);
                }
            }
        }

        private List<Triangle> ApplyResidentialCourtyards(List<Triangle> surface)
        {
            if (courtyardMasks.Count == 0) return surface;
            var result = new List<Triangle>(surface.Count);
            var candidates = new List<int>();
            foreach (Triangle triangle in surface)
            {
                Style sourceStyle = styles[triangle.Surface];
                float groundKind = sourceStyle.Properties.GetFloat(Shader.PropertyToID("_GroundKind"));
                bool earth = groundKind == (float)GroundSurfaceKind.Soil || groundKind == (float)GroundSurfaceKind.Lawn;
                // Public paving, authored stone/sand/gravel and doorway routes
                // retain their exact surface and priority. A courtyard mask only
                // repaints its exposed substrate; it never adds another plane.
                if (sourceStyle.Priority >= 30 || (!earth && groundKind != (float)GroundSurfaceKind.Asphalt &&
                    groundKind != (float)GroundSurfaceKind.Concrete)) { result.Add(triangle); continue; }
                Rect bounds = Bounds(triangle);
                candidates.Clear();
                for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
                for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
                    if (courtyardBuckets.TryGetValue(new Vector2Int(x, z), out List<int> nearby))
                        foreach (int index in nearby)
                            if (bounds.Overlaps(courtyardMasks[index].Bounds) && !candidates.Contains(index)) candidates.Add(index);
                if (candidates.Count == 0) { result.Add(triangle); continue; }
                candidates.Sort((a, b) => b.CompareTo(a));
                var remaining = new List<Triangle> { triangle };
                // The last, more specific mask wins (sheltered pad inside apron).
                // Consume intersections in stable reverse order exactly once.
                foreach (int candidate in candidates)
                {
                    CourtyardMask mask = courtyardMasks[candidate];
                    if (mask.IsCommonGround && triangle.Surface == perimeterStyle) continue;
                    if (mask.Kind == CityResidentialCourtyardSurfaceKind.Soil && !earth) continue;
                    int style = CourtyardStyle(mask.Kind, mask.Sheltered);
                    var outside = new List<Triangle>();
                    foreach (Triangle piece in remaining)
                    {
                        foreach (Triangle part in GroundSurfacePartitioner.Intersect(piece, mask.Footprint))
                        {
                            result.Add(new Triangle(CourtyardVertex(part.A, mask.Kind), CourtyardVertex(part.B, mask.Kind),
                                CourtyardVertex(part.C, mask.Kind), style));
                            ResidentialCourtyardTriangleCount++;
                        }
                        outside.AddRange(GroundSurfacePartitioner.Subtract(piece, mask.Footprint));
                    }
                    remaining = outside;
                    if (remaining.Count == 0) break;
                }
                result.AddRange(remaining);
            }
            return result;
        }

        private int CourtyardStyle(CityResidentialCourtyardSurfaceKind kind, bool sheltered)
        {
            var key = (kind, sheltered);
            if (courtyardStyles.TryGetValue(key, out int style)) return style;
            bool asphalt = kind == CityResidentialCourtyardSurfaceKind.Asphalt;
            bool concrete = kind == CityResidentialCourtyardSurfaceKind.Concrete;
            // Soil is the plan's planting-band key. Its visible ground is
            // worn low lawn, so the edge reads as a planted boundary rather
            // than another brown repair patch in the common hardstand.
            HomeSurfaceRecipe lawn = CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn);
            HomeSurfaceRecipe plaza = CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Plaza);
            Texture2D texture = asphalt ? CityExteriorAppearance.RoadTexture : concrete ?
                CityParkSurfaceAppearance.GetTexture(CityParkSurfaceKind.Plaza) : CityParkSurfaceAppearance.GetTexture(CityParkSurfaceKind.Lawn);
            Color tint = asphalt ? new Color(.62f, .70f, .74f) : concrete ? SurfaceAppearanceCore.CreateDisplayTint(
                new Color(.38f, .40f, .36f), plaza.AlbedoCompensation) : SurfaceAppearanceCore.CreateDisplayTint(
                    new Color(.18f, .25f, .14f), lawn.AlbedoCompensation);
            var properties = new MaterialPropertyBlock();
            properties.SetTexture(Shader.PropertyToID("_BaseMap"), texture);
            properties.SetTexture(Shader.PropertyToID("_GroundResponse"), GroundSurfaceAppearance.GetResponse(texture));
            properties.SetColor(Shader.PropertyToID("_BaseColor"), tint);
            properties.SetColor(Shader.PropertyToID("_Color"), tint);
            properties.SetFloat(Shader.PropertyToID("_Smoothness"), asphalt ? .1f : concrete ? plaza.Smoothness : lawn.Smoothness);
            properties.SetFloat(Shader.PropertyToID("_GroundKind"), (float)(asphalt ? GroundSurfaceKind.Asphalt :
                concrete ? GroundSurfaceKind.Concrete : GroundSurfaceKind.Lawn));
            properties.SetFloat(Shader.PropertyToID("_GroundCityClimate"), sheltered ? 0f : 1f);
            style = FindStyle(new Style(properties, concrete || asphalt ? FootstepGroundKind.Concrete : FootstepGroundKind.Grass,
                asphalt ? CityWetSurfaceKind.Road : concrete ? CityWetSurfaceKind.Sidewalk : CityWetSurfaceKind.Ground,
                tint, !sheltered));
            courtyardStyles.Add(key, style); return style;
        }

        private Vertex CourtyardVertex(Vertex source, CityResidentialCourtyardSurfaceKind kind)
        {
            Vector3 city = courtyardHome == null ? source.Position :
                PlayerHomeBalconyGeometry.ToCityWorld(courtyardHome.PlayerHome, source.Position);
            float tile = kind == CityResidentialCourtyardSurfaceKind.Asphalt ? CityExteriorAppearance.RoadTextureTileSize :
                kind == CityResidentialCourtyardSurfaceKind.Concrete ? CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Plaza).MetersPerTile :
                CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn).MetersPerTile;
            return new Vertex(source.Position, source.Normal, new Vector2(city.x, city.z) / tile, Vector4.zero, Color.white);
        }

        private static int Bucket(float value) => Mathf.FloorToInt(value / CourtyardBucketSize);
        private readonly struct CourtyardMask
        {
            internal CourtyardMask(Footprint footprint, Rect bounds, CityResidentialCourtyardSurfaceKind kind, bool sheltered, bool commonGround)
            { Footprint = footprint; Bounds = bounds; Kind = kind; Sheltered = sheltered; IsCommonGround = commonGround; }
            internal readonly Footprint Footprint;
            internal readonly Rect Bounds;
            internal readonly CityResidentialCourtyardSurfaceKind Kind;
            internal readonly bool Sheltered;
            internal readonly bool IsCommonGround;
        }
    }
}
