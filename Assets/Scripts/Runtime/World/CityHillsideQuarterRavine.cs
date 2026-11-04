using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Surveyed cross-sections of the dry cut. Every change of bank direction and both
    /// edges of each road are stations, so chart, open ground and rock share the same lip.</summary>
    internal static class CityHillsideQuarterRavine
    {
        // Z, west lip X, east lip X, depth below the surrounding soil. The
        // narrowing blind ends stop before the foundations of the north/south rim.
        internal static readonly Vector4[] Sections =
        {
            new(-366f, -6.8f, -5.8f, 0f), new(-360f, -11.5f, -3.8f, 4.2f),
            new(-354f, -16.8f, -.8f, 10.6f), new(-351f, -22f, 0f, 15.4f),
            new(-346f, -23f, -1f, 16f), new(-341f, -22.5f, -3f, 15.1f),
            new(-334f, -21.7f, -3.3f, 13.2f), new(-328f, -20.8f, -4.3f, 14.3f),
            new(-321f, -20.5f, -3.8f, 12.7f), new(-315f, -20.2f, -3f, 13.1f),
            new(-312f, -18.9f, -1.6f, 12.9f), new(-307f, -18f, 0f, 12.2f),
            new(-302f, -17.4f, .2f, 11.8f), new(-296f, -15.4f, -2.1f, 7.3f),
            new(-291f, -9.7f, -2.2f, 3.8f), new(-286f, -5.6f, -4.4f, 0f)
        };

        // Normalised cross-section coordinate and depth. Unequal benches
        // on the rock faces interrupt each fall before the narrow stony floor.
        internal static readonly Vector2[] Profile =
        {
            new(0f, 0f), new(.075f, .09f), new(.15f, .53f), new(.215f, .55f),
            new(.32f, .89f), new(.425f, 1f), new(.50f, 1f), new(.61f, .97f),
            new(.72f, .82f), new(.79f, .78f), new(.84f, .47f), new(.94f, .21f), new(1f, 0f)
        };

        // A strip of regular ground either side stays fixed at these X values.
        // Inside, move entire mesh columns together to follow the surveyed edges.
        internal const float MeshWest = -26f, MeshEast = 3f, TemplateWest = -22f, TemplateEast = -1f;

        internal static Vector4 At(float z)
        {
            for (int i = 1; i < Sections.Length; i++)
                if (z <= Sections[i].x)
                    return Vector4.Lerp(Sections[i - 1], Sections[i], Mathf.InverseLerp(Sections[i - 1].x, Sections[i].x, z));
            return Sections[Sections.Length - 1];
        }

        internal static float Drop(Vector2 point)
        {
            if (point.x < MeshWest || point.x > MeshEast || point.y <= Sections[0].x ||
                point.y >= Sections[Sections.Length - 1].x) return 0f;
            var section = At(point.y);
            if (point.x <= section.y || point.x >= section.z) return 0f;
            float u = Mathf.InverseLerp(section.y, section.z, point.x);
            for (int i = 1; i < Profile.Length; i++)
            {
                if (u > Profile[i].x) continue;
                float depth = Mathf.Lerp(Profile[i - 1].y, Profile[i].y,
                    Mathf.InverseLerp(Profile[i - 1].x, Profile[i].x, u));
                return section.w * depth;
            }
            return 0f;
        }

        internal static float MeshX(float x, Vector4 section)
        {
            if (x <= MeshWest || x >= MeshEast) return x;
            if (x < TemplateWest)
                return Mathf.Lerp(MeshWest, section.y, Mathf.InverseLerp(MeshWest, TemplateWest, x));
            if (x <= TemplateEast)
                return Mathf.Lerp(section.y, section.z, Mathf.InverseLerp(TemplateWest, TemplateEast, x));
            return Mathf.Lerp(section.z, MeshEast, Mathf.InverseLerp(TemplateEast, MeshEast, x));
        }

        internal static bool IsOpenRockColumn(float x, float z) => x > TemplateWest && x < TemplateEast &&
            z > Sections[0].x && z < Sections[Sections.Length - 1].x;

        internal static IReadOnlyList<Vector2> Outline()
        {
            var result = new List<Vector2>(Sections.Length * 2);
            foreach (var s in Sections) result.Add(new Vector2(s.z, s.x));
            for (int i = Sections.Length - 1; i >= 0; i--) result.Add(new Vector2(Sections[i].y, Sections[i].x));
            return result.AsReadOnly();
        }

        internal static IReadOnlyList<Vector2[]> OpenPolygons(IReadOnlyList<CityHillsideQuarterBridge> bridges)
        {
            var result = new List<Vector2[]>();
            for (int i = 1; i < Sections.Length; i++)
            {
                var a = Sections[i - 1]; var b = Sections[i];
                float z = (a.x + b.x) * .5f;
                bool deck = false;
                foreach (var bridge in bridges)
                    if (z > bridge.DeckBounds.yMin && z < bridge.DeckBounds.yMax) { deck = true; break; }
                if (!deck)
                    result.Add(new[] { new Vector2(a.y, a.x), new Vector2(a.z, a.x),
                        new Vector2(b.z, b.x), new Vector2(b.y, b.x) });
            }
            return result.AsReadOnly();
        }
    }

    public sealed class CityHillsideQuarterBridge
    {
        public string Id => Link.Id;
        public CityHillsideQuarterLink Link { get; }
        public Rect DeckBounds { get; }
        public Vector3 DeckCenterWorld { get; }
        public Vector3 BottomWorld { get; }
        public Vector3 WestBearingWorld { get; }
        public Vector3 EastBearingWorld { get; }

        internal CityHillsideQuarterBridge(CityHillsideQuarterPlan quarter, CityHillsideQuarterLink link)
        {
            Link = link;
            float z = link.WestGateWorld.z;
            float west = float.PositiveInfinity, east = float.NegativeInfinity;
            // Both diagonal edges of the ravine bear on land even at the four corners.
            foreach (var s in CityHillsideQuarterRavine.Sections)
            {
                if (s.x < link.Bounds.yMin || s.x > link.Bounds.yMax) continue;
                west = Mathf.Min(west, s.y); east = Mathf.Max(east, s.z);
            }
            const float seating = 1.2f;
            if (float.IsInfinity(west)) throw new InvalidOperationException("Missing bridge rock profile at " + z);
            WestBearingWorld = new Vector3(west - seating, link.RoadTopAt(west - seating), z);
            EastBearingWorld = new Vector3(east + seating, link.RoadTopAt(east + seating), z);
            DeckBounds = Rect.MinMaxRect(WestBearingWorld.x, link.Bounds.yMin, EastBearingWorld.x, link.Bounds.yMax);
            var centre = DeckBounds.center;
            DeckCenterWorld = new Vector3(centre.x, link.RoadTopAt(centre.x), centre.y);
            BottomWorld = new Vector3(centre.x, quarter.SampleSoil(centre), centre.y);
        }
    }
}
