using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metre-space placement of the shared kiosk, with +Z towards its customer.</summary>
    public class CityKioskSitePlan
    {
        internal const float WallHalfWidth = 1.50f, WallHalfDepth = 1.25f;
        internal const float ApronFront = 3.40f, ApronBack = 1.23f;

        internal CityKioskSitePlan(string stableId, CityDistrictKind district, Vector3 origin,
            Quaternion rotation, string characterId, bool isRidge, RoadEdge? frontageEdge = null,
            Vector3 streetAnchor = default, float barRouteDistance = float.PositiveInfinity,
            float barPlanarDistance = float.PositiveInfinity, float actualSidewalkWidth = 2f)
        {
            StableId = stableId; District = district; Origin = origin; Rotation = rotation;
            CharacterId = characterId; IsRidge = isRidge; FrontageEdge = frontageEdge;
            StreetAnchor = streetAnchor; BarRouteDistance = barRouteDistance; BarPlanarDistance = barPlanarDistance;
            ActualSidewalkWidth = actualSidewalkWidth;
            Footprint = Polygon(-WallHalfWidth, -WallHalfDepth, WallHalfWidth, WallHalfDepth);
            Bounds = CityRoadPolygon.Bounds(Footprint);
            var apron = Polygon(-WallHalfWidth, ApronBack, WallHalfWidth, ApronFront);
            ApronPolygons = System.Array.AsReadOnly(new[] { apron });
            Apron = CityRoadPolygon.Bounds(apron);
            // Overlap the eroded public mask by a capsule diameter. The survey
            // verifies that this seam lies on existing pavement at the same top.
            WalkableApronPolygons = System.Array.AsReadOnly(new[] {
                Polygon(-WallHalfWidth, ApronBack, WallHalfWidth,
                    ApronFront + CityGroundTraversalPlanner.MaximumAgentRadius * 2f + .10f) });
            ReservedBounds = CityRoadPolygon.Bounds(Polygon(-1.95f, -1.69f, 1.92f, ApronFront));
            StructureBounds = CityRoadPolygon.Bounds(Polygon(-1.95f, -1.69f, 1.92f, 2.16f));
        }

        public string StableId { get; }
        public CityDistrictKind District { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation { get; }
        public Vector3 Approach => Origin + Rotation * (Vector3.forward * 2.22f);
        // Lower-city surveys validate these at the real pavement centre. Ridge
        // retains its established sidewalk recipe and is covered separately.
        public Vector3 PublicThroughCenter => Origin + Rotation *
            (Vector3.forward * (ApronFront - 1f + ActualSidewalkWidth * .5f));
        public Vector3 PublicThroughLeft => PublicThroughCenter - Rotation * (Vector3.right * 3f);
        public Vector3 PublicThroughRight => PublicThroughCenter + Rotation * (Vector3.right * 3f);
        public Vector2[] Footprint { get; }
        public IReadOnlyList<Vector2[]> ApronPolygons { get; }
        public IReadOnlyList<Vector2[]> WalkableApronPolygons { get; }
        public Rect Bounds { get; }
        public Rect Apron { get; }
        public Rect ReservedBounds { get; }
        public Rect StructureBounds { get; }
        public string CharacterId { get; }
        public bool IsRidge { get; }
        public bool HasDialogue => IsRidge;
        public RoadEdge? FrontageEdge { get; }
        public Vector3 StreetAnchor { get; }
        public float BarRouteDistance { get; }
        public float BarPlanarDistance { get; }
        public float ActualSidewalkWidth { get; }

        public bool TrySampleApronTop(Vector2 point, out float top)
        {
            top = Origin.y;
            foreach (var polygon in ApronPolygons)
                if (CityRoadPolygon.Contains(polygon, point)) return true;
            return false;
        }

        private Vector2[] Polygon(float left, float back, float right, float front)
        {
            Vector3 a = Origin + Rotation * new Vector3(left, 0f, back);
            Vector3 b = Origin + Rotation * new Vector3(right, 0f, back);
            Vector3 c = Origin + Rotation * new Vector3(right, 0f, front);
            Vector3 d = Origin + Rotation * new Vector3(left, 0f, front);
            return CityRoadPolygon.CounterClockwise(new[] { Xz(a), Xz(b), Xz(c), Xz(d) });
        }

        private static Vector2 Xz(Vector3 value) => new Vector2(value.x, value.z);
    }
}
