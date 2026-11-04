using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using Triangle = BarPromenade.GroundSurfacePartitioner.Triangle;
using Vertex = BarPromenade.GroundSurfacePartitioner.Vertex;

namespace BarPromenade
{
    internal static class CityHillsidePerimeterSurface
    {
        private enum Finish { Soil, Asphalt, Turf, Gravel }
        private readonly struct Plot
        {
            internal readonly Finish Finish;
            internal readonly GroundSurfacePartitioner.Footprint Shape;
            internal Plot(Finish finish, params Vector2[] vertices)
            {
                Finish = finish;
                Shape = new GroundSurfacePartitioner.Footprint(vertices);
            }
        }
        private static readonly ConditionalWeakTable<CityLayout, List<Plot>> Plots = new();
        internal static Color ResidentialSoil => CityExteriorAppearance.ResolveDistrictGroundTint(
            CityDistrictPresentationPlanner.GetProfile(CityDistrictKind.Residential).Wear);

        private static List<Plot> CreatePlots(CityLayout layout)
        {
            var plots = new List<Plot>();
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            if (quarter.Bridges.Count == 0) return plots;
            var bridge = quarter.Bridges[quarter.Bridges.Count - 1];
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
            foreach (var house in housing.SharedCourtBuildings)
            {
                if (!house.OnSummitStreet) continue;
                if (house.Bank == CityTerraceTransportBank.West)
                {
                    // Two leftover parcels flank the street between its last switchback and the
                    // bridge. Grass withdraws at the house approach and on the dry rock shoulder.
                    float west = bridge.Link.WestGateWorld.x + 2f;
                    Verge(west, house.FoundationBounds.xMin - CityBuildingPerimeterPlan.Width,
                        bridge.Link.Bounds.yMin, -1f, 6.1f);
                    Verge(west + 1.8f, bridge.WestBearingWorld.x - 3.5f,
                        bridge.Link.Bounds.yMax, 1f, 8f);
                }
                else Gallery(house);
            }
            foreach (var house in housing.Buildings)
                if (house.Bank == CityTerraceTransportBank.East && house.Lot.BuildingVariant == 2 &&
                    (house.Lot.Cell.y == -6 || house.Lot.Cell.y == -8)) Gallery(house);
            return plots;

            void Gallery(CityHillsideBuilding house)
            {
                float left = house.FoundationBounds.xMax + CityBuildingPerimeterPlan.Width;
                Verge(left, left + 7.5f, house.PavementEdgeZ,
                    Mathf.Sign(house.DoorWorld.z - house.PavementEdgeZ), 5.3f);
            }

            void Verge(float start, float end, float pavement, float side, float depth)
            {
                float run = end - start;
                if (run < 4f) return;
                Vector2 P(float fraction, float offset) => new Vector2(start + run * fraction, pavement + side * offset);
                // Worn, unequal ends leave plain earth where people cut the corner. A tongue of
                // crushed stone follows the pavement and does not encircle the parcel like a bed.
                plots.Add(new Plot(Finish.Gravel, P(.05f, .02f), P(.82f, .02f), P(.75f, .55f), P(.23f, 1.15f)));
                plots.Add(new Plot(Finish.Turf, P(0f, 2.1f), P(.23f, 1.15f), P(.75f, .55f),
                    P(1f, 2.0f), P(.92f, depth * .88f), P(.55f, depth), P(.12f, depth * .74f)));
            }
        }

        internal static void ApplyVerges(Renderer renderer, int turf, int gravel)
        {
            Bind(turf, GroundSurfaceKind.Lawn, CityParkSurfaceAppearance.GetTexture(CityParkSurfaceKind.Lawn),
                CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn), new Color(.255f, .273f, .205f));
            Bind(gravel, GroundSurfaceKind.Gravel, CityCemeterySurfaceAppearance.GetTexture(CityCemeterySurfaceKind.Gravel),
                CityCemeterySurfaceAppearance.GetRecipe(CityCemeterySurfaceKind.Gravel), new Color(.265f, .253f, .214f));

            void Bind(int slot, GroundSurfaceKind kind, Texture2D cover, HomeSurfaceRecipe recipe, Color tint)
            {
                // Register the same soil/weather as the neighbouring slot. The transparent part
                // of the pigment mix then samples precisely its underlying wet soil at every hour.
                CityExteriorAppearance.ApplyGroundSurface(renderer, ResidentialSoil, slot);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties, slot);
                properties.SetFloat("_GroundKind", (float)kind);
                properties.SetFloat("_GroundVergeTileRatio", CityExteriorAppearance.GroundTextureTileSize / recipe.MetersPerTile);
                properties.SetColor("_GroundSubstrateColor", SurfaceAppearanceCore.CreateDisplayTint(tint, recipe.AlbedoCompensation));
                CitySurfaceLibrary.BindGroundCover(properties, cover);
                renderer.SetPropertyBlock(properties, slot);
            }
        }

        internal static void SetTilePitch(Renderer ground, int slot, float sourceMetres, float targetMetres)
        {
            float scale = sourceMetres / targetMetres;
            var properties = new MaterialPropertyBlock();
            ground.GetPropertyBlock(properties, slot);
            properties.SetVector("_BaseMap_ST", new Vector4(scale, scale, 0f, 0f));
            ground.SetPropertyBlock(properties, slot);
        }

        // Pave part of the original soil plane. A separate flat slab would float on the
        // downhill side, cover stairs, or change the height used by bus-side clearances.
        internal static Mesh Build(CityLayout layout, string name, Vector3[] points, Vector2[] coordinates,
            params List<int>[] layers)
        {
            var mesh = new Mesh { name = name };
            if (points.Length > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.subMeshCount = layers.Length;
            mesh.vertices = points;
            for (int slot = 0; slot < layers.Length; slot++) mesh.SetTriangles(layers[slot], slot, false);
            mesh.RecalculateNormals();
            var vertices = new List<Vector3>(points);
            var normals = new List<Vector3>();
            mesh.GetNormals(normals);
            var uv = new List<Vector2>(coordinates);
            // Distance to a straight line is affine across a triangle. Pack up to eight edge
            // distances, without tessellating the terrain just to interpolate the finish.
            var edgeDistances = new List<Vector4>(new Vector4[points.Length]);
            var otherEdges = new List<Color32>(new Color32[points.Length]);
            var splitIndices = new Dictionary<(Vector3, Finish), int>();
            var soil = new List<int>(layers[0].Count);
            var finishes = new[] { soil, new List<int>(), new List<int>(), new List<int>() };
            var candidates = new List<int>();
            var remaining = new List<Triangle>();
            var next = new List<Triangle>();
            var plan = CityBuildingPerimeterPlan.ForLayout(layout);
            var plots = Plots.GetValue(layout, CreatePlots);
            for (int i = 0; i < layers[0].Count; i += 3)
            {
                int a = layers[0][i], b = layers[0][i + 1], c = layers[0][i + 2];
                Vector3 min = Vector3.Min(points[a], Vector3.Min(points[b], points[c]));
                Vector3 max = Vector3.Max(points[a], Vector3.Max(points[b], points[c]));
                Rect bounds = Rect.MinMaxRect(min.x, min.z, max.x, max.z);
                plan.Query(bounds, candidates);
                bool cut = false;
                foreach (int candidate in candidates)
                {
                    var rings = plan.Bands(candidate);
                    for (int j = 0; j < rings.Count; j++)
                    {
                        Cover(rings[j], Finish.Asphalt);
                    }
                }
                foreach (var plot in plots) Cover(plot.Shape, plot.Finish);
                if (cut)
                    foreach (var part in remaining) Emit(part, Finish.Soil);
                else { soil.Add(a); soil.Add(b); soil.Add(c); }

                void Cover(GroundSurfacePartitioner.Footprint shape, Finish finish)
                {
                    if (!shape.Bounds.Overlaps(bounds)) return;
                    if (!cut)
                    {
                        remaining.Clear();
                        remaining.Add(new Triangle(At(a), At(b), At(c), 0));
                    }
                    next.Clear();
                    foreach (var original in remaining)
                    {
                        var covered = GroundSurfacePartitioner.Intersect(original, shape);
                        if (covered.Count == 0) { next.Add(original); continue; }
                        cut = true;
                        foreach (var part in covered) Emit(part, finish, shape);
                        next.AddRange(GroundSurfacePartitioner.Subtract(original, shape));
                    }
                    (remaining, next) = (next, remaining);
                }
            }

            if (vertices.Count > ushort.MaxValue && mesh.indexFormat != IndexFormat.UInt32)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.subMeshCount = layers.Length + 3;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            if (finishes[(int)Finish.Turf].Count != 0 || finishes[(int)Finish.Gravel].Count != 0)
            {
                mesh.SetUVs(GroundSurfaceCoordinates.Channel, edgeDistances);
                mesh.SetColors(otherEdges);
            }
            mesh.SetTriangles(soil, 0, false);
            for (int slot = 1; slot < layers.Length; slot++) mesh.SetTriangles(layers[slot], slot, false);
            for (int i = 1; i < finishes.Length; i++) mesh.SetTriangles(finishes[i], layers.Length + i - 1, false);
            mesh.RecalculateBounds();
            return mesh;

            Vertex At(int index) => new Vertex(points[index], normals[index], coordinates[index], Vector4.zero, Color.white);

            void Emit(Triangle part, Finish finish, GroundSurfacePartitioner.Footprint shape = null)
            {
                var indices = finishes[(int)finish];
                indices.Add(Index(part.A, finish, shape));
                indices.Add(Index(part.B, finish, shape));
                indices.Add(Index(part.C, finish, shape));
            }

            int Index(Vertex v, Finish finish, GroundSurfacePartitioner.Footprint shape)
            {
                var key = (v.Position, finish);
                if (splitIndices.TryGetValue(key, out int index)) return index;
                index = vertices.Count;
                vertices.Add(v.Position);
                normals.Add(v.Normal.normalized);
                var city = new Vector2(v.Position.x, v.Position.z);
                uv.Add(finish == Finish.Asphalt ? city / CityExteriorAppearance.RoadTextureTileSize : v.Uv0);
                var first = Vector4.one * 16f;
                var last = Vector4.one;
                if (finish == Finish.Turf || finish == Finish.Gravel)
                    for (int i = 0; i < shape.Points.Length; i++)
                    {
                        Vector2 from = shape.Points[i];
                        Vector2 axis = (shape.Points[(i + 1) % shape.Points.Length] - from).normalized;
                        Vector2 offset = city - from;
                        float distance = Mathf.Max(0f, axis.x * offset.y - axis.y * offset.x);
                        if (i < 4) first[i] = distance;
                        else last[i - 4] = Mathf.Clamp01(distance / 16f);
                    }
                edgeDistances.Add(first);
                otherEdges.Add(new Color(last.x, last.y, last.z, last.w));
                splitIndices.Add(key, index);
                return index;
            }
        }

        internal static void Stamp(GameObject ground, Mesh mesh, params FootstepGroundKind[] layers)
        {
            var kinds = new List<FootstepGroundKind>();
            for (int slot = 0; slot < layers.Length; slot++)
            {
                int triangles = (int)mesh.GetIndexCount(slot) / 3;
                for (int i = 0; i < triangles; i++) kinds.Add(layers[slot]);
            }
            FootstepGround.Stamp(ground, FootstepGroundKind.Soil, kinds);
        }
    }
}
