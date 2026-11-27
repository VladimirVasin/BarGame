using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // Local floor and wall loops are measured by the Blender generator alongside the FBX.
    internal sealed class RidgeRavinePlan
    {
        private const string Folder = "City/Hillside/";
        private static readonly ConditionalWeakTable<CityLayout, RidgeRavinePlan> Sites = new();
        [Serializable] private sealed class Loop { public Vector2[] points; }
        [Serializable] private sealed class Well { public float x, y, width, height; }
        [Serializable] private sealed class Survey
        {
            public string design;
            public Vector2[] outline;
            public Loop[] solids, edges;
            public float storey;
            public Well stairWell;
        }
        private readonly Survey survey;
        internal readonly RavineFamilyRooms Families = RavineFamilyRooms.Load();
        private readonly Rect well;
        internal float Storey => survey.storey;
        internal readonly Vector3 Origin;
        internal readonly Vector2[] Outline;
        internal readonly Rect Bounds;
        internal readonly List<Vector2[]> Floors = new();
        internal readonly List<Vector2[]> Solids = new();
        internal Vector3 Arrival => Origin + new Vector3(4.71f, PlayerFactory.GroundedRootOffset, 3.65f);

        internal static RidgeRavinePlan ForLayout(CityLayout layout) => layout.HasSummitHome
            ? Sites.GetValue(layout, value => new RidgeRavinePlan(CityHillsideQuarterPlan.ForLayout(value).Links[1])) : null;

        private RidgeRavinePlan(CityHillsideQuarterLink street)
        {
            Origin = new Vector3(2f, street.RoadTopAt(2f) - .07f, street.WestGateWorld.z - 11.8f);
            var asset = Resources.Load<TextAsset>(Folder + "RidgeRavineSurvey");
            survey = asset == null ? null : JsonUtility.FromJson<Survey>(asset.text);
            if (survey?.design != "ridge_ravine_v2" || survey.outline.Length < 4 || survey.solids.Length == 0 ||
                survey.edges?.Length != 4 || survey.stairWell == null || survey.storey <= 0)
                throw new InvalidOperationException("Missing measured ravine apartment floor and solid survey");
            var opening = survey.stairWell;
            well = new Rect(opening.x, opening.y, opening.width, opening.height);
            Outline = Placed(survey.outline);
            Bounds = CityRoadPolygon.Bounds(Outline);
            foreach (var loop in survey.solids) Solids.Add(CityRoadPolygon.CounterClockwise(Placed(loop.points)));
            // The lip is concave. Each pair of measured stations defines one convex strip.
            for (int i = 1; i < Outline.Length - 2; i++)
            {
                var back = Outline[i - 1];
                var front = Outline[i];
                Floors.Add(new[] { back, new Vector2(Bounds.xMax, back.y), new Vector2(Bounds.xMax, front.y), front });
            }
        }

        private Vector2[] Placed(Vector2[] local)
        {
            var result = new Vector2[local.Length];
            for (int i = 0; i < local.Length; i++) result[i] = local[i] + new Vector2(Origin.x, Origin.z);
            return result;
        }

        internal bool ContainsFloor(Vector2 point)
        {
            if (Bounds.Contains(point))
                foreach (var strip in Floors)
                    if (CityRoadPolygon.Contains(strip, point)) return true;
            return false;
        }

        // A chart arrival uses a level floor with capsule clearance. The
        // switchbacks and their undersides are reached by walking from it.
        internal bool TryArrivalTop(Vector2 world, float? rootY, float radius, out float top)
        {
            top = Origin.y;
            Vector2 p = world - new Vector2(Origin.x, Origin.z);
            if (p.x >= well.xMin-radius-.05f && p.y >= well.yMin-radius-.05f &&
                p.y <= well.yMax+radius+.05f) return false;
            int floor = rootY.HasValue ? Mathf.RoundToInt((rootY.Value - PlayerFactory.GroundedRootOffset - top)/Storey) : 0;
            if (floor < 0 || floor > 2 || Families.Occupied(p, floor, radius)) return false;
            if (floor == 0) return true;
            // The upper street-facing opening has a sill. The wardrobe and
            // its open door occupy the back corner of the third-floor flat.
            if (Mathf.Abs(p.y) > 4.36f-radius || (floor == 2 && p.y < -3.12f+radius &&
                p.x >= -.05f-radius && p.x <= 2.16f+radius)) return false;
            var edge = survey.edges[floor].points;
            for (int i = 1; i < edge.Length; i++)
            {
                Vector2 a = edge[i-1], b = edge[i];
                if (p.y >= a.y && p.y <= b.y &&
                    p.x < Mathf.Lerp(a.x,b.x,(p.y-a.y)/(b.y-a.y))) return false;
                Vector2 span = b-a;
                Vector2 closest = a + span*Mathf.Clamp01(Vector2.Dot(p-a,span)/span.sqrMagnitude);
                if ((p-closest).sqrMagnitude < (radius+.15f)*(radius+.15f)) return false;
            }
            top += floor*Storey;
            return true;
        }

        internal IEnumerable<Bounds> CoveredRooms()
        {
            for (int floor = 0; floor < 3; floor++)
            {
                var edge = survey.edges[floor+1].points;
                int middle = edge.Length/2;
                for (int half = 0; half < 2; half++)
                {
                    int first = half == 0 ? 0 : middle, last = half == 0 ? middle : edge.Length-1;
                    float left = edge[first].x;
                    for (int i = first+1; i <= last; i++) left = Mathf.Max(left,edge[i].x);
                    Vector3 min = Origin + new Vector3(left, floor*Storey, edge[first].y);
                    Vector3 max = Origin + new Vector3(well.xMax, (floor+1)*Storey, edge[last].y);
                    yield return new Bounds((min+max)*.5f, max-min);
                }
            }
        }
    }
}
