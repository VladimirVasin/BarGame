using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The first live eastern transport alignment. Stable grid identities and
    /// existing terrace heights remain authoritative; the forty-metre ascent
    /// is still the separate transport survey, not an active elevation field.
    /// </summary>
    public static class CityTerraceInlandJunctionPlan
    {
        public const float ApproachInset = 4f;
        public const float StraightMouthLength = 6.5f;
        public static IReadOnlyList<Vector2Int> Nodes { get; } =
            new ReadOnlyCollection<Vector2Int>(new[] {
                new Vector2Int(12, 8), new Vector2Int(13, 8) });

        internal static bool Supports(CityGenerationSettings settings) =>
            settings.Blueprint?.Id == CityBlueprintCatalog.DefaultBlueprintId &&
            settings.SpatialPlan?.IsCompactCoastal == true;

        public static bool Contains(Vector2Int node) =>
            node == Nodes[0] || node == Nodes[1];

        public static bool Contains(RoadEdge edge) => Contains(edge.A) || Contains(edge.B);

        internal static Vector2 Offset(Vector2Int node) => node == Nodes[0]
            ? new Vector2(-.15f, .25f) : node == Nodes[1]
            ? new Vector2(.5f, .25f) : Vector2.zero;

        internal static CityRoadPath CreateApproach(RoadEdge edge, Vector2 start, Vector2 end)
        {
            // The authored corner centers are (155.85,52.25)/(182.5,52.25).
            // Cardinal mouths keep every old side street attached; the extra
            // straight run keeps the vehicle lane miter beyond its turn socket.
            // only the middle span blends laterally to its unchanged far end.
            Vector2 tangent = edge.IsHorizontal ? Vector2.right : Vector2.up;
            Vector2 first = start + tangent * StraightMouthLength;
            Vector2 last = end - tangent * StraightMouthLength;
            Vector2 span = last - first;
            float along = Vector2.Dot(span, tangent);
            Vector2 lateral = span - tangent * along;
            int steps = Mathf.Max(2, Mathf.CeilToInt(span.magnitude / 1.5f));
            var vertices = new List<Vector2>(steps + 5) {
                start, start + tangent * ApproachInset, first };
            for (int index = 1; index < steps; index++)
            {
                float amount = (float)index / steps;
                float blend = amount * amount * (3f - 2f * amount);
                vertices.Add(first + tangent * (along * amount) + lateral * blend);
            }
            vertices.Add(last);
            vertices.Add(end - tangent * ApproachInset);
            vertices.Add(end);
            return new CityRoadPath(vertices);
        }
    }
}
