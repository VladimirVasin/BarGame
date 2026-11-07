using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Walk-in D-shaped household yard on the earth inside the west hairpin.
    /// Every placement, including both ends of each fence panel, uses built terrain.</summary>
    public sealed class CityPigeonCourtyardPlan
    {
        private static readonly Vector2 BendCentre = new(-146f, -333f);
        private const float Radius = 6.7f;
        private const float EastEdge = -139.13f;
        // Raw furniture surveys run before the ground grid exists. Reserve the domestic
        // parcel there without recursing into this finished-terrain plan.
        internal static Rect FurnitureReservation => Rect.MinMaxRect(
            BendCentre.x - Radius, BendCentre.y - Radius, EastEdge, BendCentre.y + Radius);

        public IReadOnlyList<Vector3> FencePosts { get; }
        public Vector3 GateCenter { get; }
        public Vector3 GateLeafEnd { get; }
        public Vector3 Arrival { get; }
        public Vector3 Workbench { get; }
        public Vector3 GrainStore { get; }
        public Vector3 Bath { get; }
        public Rect WorkbenchBounds { get; }
        public Rect GrainStoreBounds { get; }
        public Rect BathBounds { get; }
        public Rect Bounds { get; }
        public IReadOnlyList<Vector3> WalkPath { get; }
        public IReadOnlyList<Vector2[]> SolidPolygons { get; }

        internal CityPigeonCourtyardPlan(CityHillsideGroundPlan ground)
        {
            const float north = -333f + Radius, south = -333f - Radius;
            var posts = new List<Vector3>(25);
            // Start at the hinge. Walk clockwise around the enclosure, ending at
            // the strike post. The missing last span is a real, unobstructed opening.
            foreach (float angle in new[] { 193f, 174f, 153f, 132f, 111f, 90f })
                posts.Add(Foot(Arc(angle)));
            for (int i = 1; i <= 3; i++) posts.Add(Foot(new Vector2(Mathf.Lerp(BendCentre.x, EastEdge, i / 3f), north)));
            for (int i = 1; i <= 6; i++) posts.Add(Foot(new Vector2(EastEdge, Mathf.Lerp(north, south, i / 6f))));
            for (int i = 1; i <= 3; i++) posts.Add(Foot(new Vector2(Mathf.Lerp(EastEdge, BendCentre.x, i / 3f), south)));
            foreach (float angle in new[] { 249f, 228f, 207f }) posts.Add(Foot(Arc(angle)));
            FencePosts = posts.AsReadOnly();
            Vector2 hinge = XZ(posts[0]), strike = XZ(posts[posts.Count - 1]);
            Vector2 gate = (hinge + strike) * .5f;
            Vector2 inward = (BendCentre - gate).normalized;
            GateCenter = Foot(gate);
            // The opened leaf folds against the inside of the adjacent span.
            GateLeafEnd = Foot(hinge + inward * 1.55f);
            Arrival = Foot(gate + inward * 1.15f);
            Workbench = Foot(new Vector2(-146.0f, -327.60f));
            GrainStore = Foot(new Vector2(-142.85f, -334.40f));
            Bath = Foot(new Vector2(-147.25f, -336.65f));
            // Imported mesh bounds at the workbench's south-facing and store's
            // west-facing orientations. The bath's stepping stones sit off centre.
            WorkbenchBounds = Footprint(Workbench, -.96f, -.475f, .96f, .511f);
            GrainStoreBounds = Footprint(GrainStore, -.43f, -.56f, .38f, .56f);
            BathBounds = Footprint(Bath, -.66f, -.63f, .565f, .505f);
            Rect outline = FurnitureReservation;
            Bounds = Rect.MinMaxRect(outline.xMin - .05f, outline.yMin - .05f, outline.xMax + .05f, outline.yMax + .05f);

            WalkPath = Array.AsReadOnly(new[] {
                Foot(gate - inward * 1.27f), GateCenter, Arrival, Foot(new Vector2(-148.10f, -333.35f)),
                Foot(new Vector2(-145.15f, -331.60f)), Foot(new Vector2(-142.10f, -331.06f)),
                Foot(new Vector2(-140.32f, -331.14f)), Foot(new Vector2(-139.82f, -331.78f)),
                Foot(new Vector2(-139.80f, -332.73f))
            });
            if (!ground.Contains(XZ(Arrival), .4f)) throw new InvalidOperationException("The pigeon yard arrival lost its footing.");

            var solids = new List<Vector2[]>(posts.Count + 3);
            for (int i = 1; i < posts.Count; i++)
                solids.Add(ThinSpan(XZ(posts[i - 1]), XZ(posts[i])));
            solids.Add(ThinSpan(hinge, XZ(GateLeafEnd)));
            solids.Add(CityRoadPolygon.Rectangle(WorkbenchBounds));
            solids.Add(CityRoadPolygon.Rectangle(GrainStoreBounds));
            solids.Add(CityRoadPolygon.Rectangle(BathBounds));
            SolidPolygons = solids.AsReadOnly();

            Vector3 Foot(Vector2 p)
            {
                if (!ground.TrySampleTop(p, out float y))
                    throw new InvalidOperationException("Missing pigeon courtyard ground at " + p);
                return new Vector3(p.x, y, p.y);
            }
        }

        private static Vector2 Arc(float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return BendCentre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
        }

        private static Vector2[] ThinSpan(Vector2 a, Vector2 b)
        {
            // Include each 80 mm post and its corners at bends in the map obstacle.
            Vector2 along = (b - a).normalized * .06f;
            Vector2 across = new(-along.y, along.x);
            return CityRoadPolygon.CounterClockwise(new[] { a - along - across, b + along - across,
                b + along + across, a - along + across });
        }

        private static Rect Footprint(Vector3 foot, float left, float back, float right, float front) =>
            Rect.MinMaxRect(foot.x + left, foot.z + back, foot.x + right, foot.z + front);

        private static Vector2 XZ(Vector3 p) => new(p.x, p.z);
    }
}
