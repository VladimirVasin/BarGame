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
        private CityPlaygroundLandscapePlan playgroundLandscape;
        private readonly Dictionary<(CityResidentialCourtyardSurfaceKind, bool), int> courtyardStyles =
            new Dictionary<(CityResidentialCourtyardSurfaceKind, bool), int>();
        private readonly Dictionary<CityPlaygroundLandscapePlan.Finish, int> playgroundStyles =
            new Dictionary<CityPlaygroundLandscapePlan.Finish, int>();
        internal int ResidentialCourtyardTriangleCount { get; private set; }

        internal void SetResidentialCourtyards(CityResidentialCourtyardPlan plan, HomeExteriorContextPlan home = null)
        {
            if (sealedSurface) throw new InvalidOperationException("Courtyard surfaces must precede ground completion.");
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            courtyardHome = home;
            courtyardMasks.Clear(); courtyardBuckets.Clear();
            foreach (CityResidentialCourtyardSurfaceDescriptor surface in plan.Surfaces)
                AddGroundCoating(surface.Polygon, surface.Kind, surface.IsSheltered, surface.IsCommonGround);
        }

        internal void SetPlaygroundLandscape(CityPlaygroundLandscapePlan plan)
        {
            if (sealedSurface) throw new InvalidOperationException("Playground landscape must precede ground completion.");
            playgroundLandscape = plan ?? throw new ArgumentNullException(nameof(plan));
            foreach (CityPlaygroundLandscapePlan.Site site in plan.Sites)
            foreach (CityPlaygroundLandscapePlan.Coat coat in site.Coats)
                AddGroundCoating(coat.Outline, CityResidentialCourtyardSurfaceKind.Concrete, false, false, coat);
        }

        private void AddGroundCoating(IReadOnlyList<Vector2> polygon, CityResidentialCourtyardSurfaceKind kind,
            bool sheltered, bool common, CityPlaygroundLandscapePlan.Coat coat = default)
        {
            var points = new Vector2[polygon.Count];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 p = polygon[i];
                Vector3 local = courtyardHome == null ? new Vector3(p.x, 0f, p.y) :
                    PlayerHomeBalconyGeometry.ToHomeLocal(courtyardHome.PlayerHome, new Vector3(p.x, 0f, p.y));
                points[i] = new Vector2(local.x, local.z);
            }
            Rect bounds = CityRoadPolygon.Bounds(points);
            int index = courtyardMasks.Count;
            courtyardMasks.Add(new CourtyardMask(new Footprint(points), bounds, kind, sheltered, common, coat));
            for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
            for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
            {
                var key = new Vector2Int(x, z);
                if (!courtyardBuckets.TryGetValue(key, out List<int> entries)) courtyardBuckets.Add(key, entries = new List<int>());
                entries.Add(index);
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
                    int style = mask.Coat.Surface == CityPlaygroundLandscapePlan.Finish.None
                        ? CourtyardStyle(mask.Kind, mask.Sheltered) : PlaygroundStyle(mask.Coat.Surface);
                    var outside = new List<Triangle>();
                    foreach (Triangle piece in remaining)
                    {
                        foreach (Triangle part in GroundSurfacePartitioner.Intersect(piece, mask.Footprint))
                        {
                            result.Add(new Triangle(CourtyardVertex(part.A, mask), CourtyardVertex(part.B, mask),
                                CourtyardVertex(part.C, mask), style));
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

        private int PlaygroundStyle(CityPlaygroundLandscapePlan.Finish finish)
        {
            if (playgroundStyles.TryGetValue(finish, out int style)) return style;
            bool gravel = IsPlaygroundGravel(finish);
            bool edge = finish is CityPlaygroundLandscapePlan.Finish.ConcreteEdge or CityPlaygroundLandscapePlan.Finish.GravelEdge;
            Texture2D texture = gravel ? CityParkSurfaceAppearance.GetTexture(CityParkSurfaceKind.Path) : CityExteriorAppearance.SidewalkTexture;
            Color tint = gravel ? CityExteriorAppearance.ParkPath : new Color(.54f, .56f, .53f);
            if (edge) tint *= gravel ? .88f : 1.26f;
            var properties = new MaterialPropertyBlock();
            properties.SetTexture(Shader.PropertyToID("_BaseMap"), texture);
            properties.SetTexture(Shader.PropertyToID("_GroundResponse"), GroundSurfaceAppearance.GetResponse(texture));
            properties.SetColor(Shader.PropertyToID("_BaseColor"), tint);
            properties.SetColor(Shader.PropertyToID("_Color"), tint);
            properties.SetFloat(Shader.PropertyToID("_Smoothness"), gravel ? .07f : .14f);
            properties.SetFloat(Shader.PropertyToID("_GroundKind"), (float)(gravel ? GroundSurfaceKind.Gravel : GroundSurfaceKind.Concrete));
            properties.SetFloat(Shader.PropertyToID("_GroundCityClimate"), 1f);
            if (!gravel && !edge)
                properties.SetFloat(Shader.PropertyToID("_GroundSidewalkMetres"), CityExteriorAppearance.SidewalkTextureTileSize);
            style = FindStyle(new Style(properties, gravel ? FootstepGroundKind.Soil : FootstepGroundKind.Concrete,
                gravel ? CityWetSurfaceKind.Ground : CityWetSurfaceKind.Sidewalk, tint, true));
            playgroundStyles.Add(finish, style);
            return style;
        }

        private static bool IsPlaygroundGravel(CityPlaygroundLandscapePlan.Finish finish) =>
            finish is CityPlaygroundLandscapePlan.Finish.Gravel or CityPlaygroundLandscapePlan.Finish.GravelEdge;

        private Vertex CourtyardVertex(Vertex source, in CourtyardMask mask)
        {
            Vector3 city = courtyardHome == null ? source.Position :
                PlayerHomeBalconyGeometry.ToCityWorld(courtyardHome.PlayerHome, source.Position);
            if (mask.Coat.Surface != CityPlaygroundLandscapePlan.Finish.None)
            {
                Vector2 offset = new Vector2(city.x, city.z) - mask.Coat.Origin;
                Vector2 right = mask.Coat.Right;
                float pitch = IsPlaygroundGravel(mask.Coat.Surface)
                    ? CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Path).MetersPerTile
                    : CityExteriorAppearance.SidewalkTextureTileSize;
                Vector2 uv = new Vector2(Vector2.Dot(offset, right),
                    Vector2.Dot(offset, new Vector2(-right.y, right.x))) / pitch;
                return new Vertex(source.Position, source.Normal, uv, Vector4.zero, Color.white);
            }
            CityResidentialCourtyardSurfaceKind kind = mask.Kind;
            float tile = kind == CityResidentialCourtyardSurfaceKind.Asphalt ? CityExteriorAppearance.RoadTextureTileSize :
                kind == CityResidentialCourtyardSurfaceKind.Concrete ? CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Plaza).MetersPerTile :
                CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn).MetersPerTile;
            return new Vertex(source.Position, source.Normal, new Vector2(city.x, city.z) / tile, Vector4.zero, Color.white);
        }

        private static int Bucket(float value) => Mathf.FloorToInt(value / CourtyardBucketSize);
        private readonly struct CourtyardMask
        {
            internal CourtyardMask(Footprint footprint, Rect bounds, CityResidentialCourtyardSurfaceKind kind,
                bool sheltered, bool commonGround, CityPlaygroundLandscapePlan.Coat coat)
            { Footprint = footprint; Bounds = bounds; Kind = kind; Sheltered = sheltered; IsCommonGround = commonGround; Coat = coat; }
            internal readonly Footprint Footprint;
            internal readonly Rect Bounds;
            internal readonly CityResidentialCourtyardSurfaceKind Kind;
            internal readonly bool Sheltered;
            internal readonly bool IsCommonGround;
            internal readonly CityPlaygroundLandscapePlan.Coat Coat;
        }
    }
}
