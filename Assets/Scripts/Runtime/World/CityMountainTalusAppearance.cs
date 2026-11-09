using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Registers the soil/rock mixture on the surveyed talus surface.
    /// UV5 is private to this surface. Existing road, verge and lightmap data
    /// retain their channels, and all renderers reuse the shared materials.</summary>
    internal static class CityMountainTalusAppearance
    {
        internal const int DataChannel = 4;
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();

        internal static void BakeGround(Mesh mesh, CityLayout layout)
        {
            var plan = CityMountainTalusPlan.ForLayout(layout);
            Vector3[] vertices = mesh.vertices;
            var data = new List<Vector2>(vertices.Length);
            foreach (Vector3 vertex in vertices)
                data.Add(new Vector2(plan.Influence(new Vector2(vertex.x, vertex.z)), 0f));
            mesh.SetUVs(DataChannel, data);
        }

        internal static void ApplyGround(Renderer renderer, int slot = 0)
        {
            GroundSurfaceAppearance.ReadProperties(renderer, Properties, slot);
            // A single-material terrain uses the renderer-wide block. An indexed
            // block overrides that whole block, so copy its existing pigment first.
            if (Properties.isEmpty && slot >= 0) renderer.GetPropertyBlock(Properties);
            Properties.SetFloat(Shader.PropertyToID("_GroundMountainTalus"), 1f);
            Texture2D rock = CityMountainSurfaceAppearance.GetTexture();
            Properties.SetTexture(Shader.PropertyToID("_GroundTalusMap"), rock);
            Properties.SetTexture(Shader.PropertyToID("_GroundTalusResponse"), GroundSurfaceAppearance.GetResponse(rock));
            Properties.SetColor(Shader.PropertyToID("_GroundTalusColor"),
                CityMountainSurfaceAppearance.CreateDisplayTint(CityMountainBoundaryWorldBuilder.ForeRock));
            GroundSurfaceAppearance.WriteProperties(renderer, Properties, slot);
        }

        internal static void BakeRidge(Mesh mesh, IReadOnlyList<CityMountainRidgeStation> stations,
            int first, int last)
        {
            Vector3[] vertices = mesh.vertices;
            var data = new List<Vector2>(vertices.Length);
            foreach (Vector3 vertex in vertices)
            {
                var point = new Vector2(vertex.x, vertex.z);
                float nearest = float.PositiveInfinity;
                float toeHeight = stations[first].BaseY;
                for (int index = first; index < last; index++)
                {
                    Vector2 a = stations[index].WorldXZ, b = stations[index + 1].WorldXZ;
                    Vector2 delta = b - a;
                    float t = delta.sqrMagnitude > .0001f
                        ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0f;
                    float distance = (point - Vector2.Lerp(a, b, t)).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    toeHeight = Mathf.Lerp(stations[index].BaseY, stations[index + 1].BaseY, t);
                }
                data.Add(new Vector2(0f, vertex.y - toeHeight));
            }
            mesh.SetUVs(DataChannel, data);
        }

        internal static void ApplyRidge(Renderer renderer)
        {
            GroundSurfaceAppearance.ReadProperties(renderer, Properties, -1);
            Properties.SetFloat(Shader.PropertyToID("_MountainTalusSoil"), 1f);
            Properties.SetTexture(Shader.PropertyToID("_MountainTalusSoilMap"), CityExteriorAppearance.GroundTexture);
            Properties.SetColor(Shader.PropertyToID("_MountainTalusSoilColor"), CityHillsidePerimeterSurface.ResidentialSoil);
            GroundSurfaceAppearance.WriteProperties(renderer, Properties, -1);
        }
    }
}
