using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A measured broad municipal stair joining the two existing pawnshop view benches.</summary>
    public sealed class CityPawnshopBenchTerracePlan
    {
        public const string LeftBenchId="ridge-West-ParkBench-11",RightBenchId="ridge-West-ParkBench-13";
        private static readonly ConditionalWeakTable<CityLayout,CityPawnshopBenchTerracePlan> Cached=new();
        public bool IsEnabled {get;}
        public float Width=>Flight.Width;
        public int StairCount=>Flight.StepCount;
        public float Rise=>Flight.StepRise;
        public float Tread=>Flight.TreadDepth;
        public CityExteriorStairFlightDescriptor Flight {get;}
        public Rect StairFootprint {get;}
        public CityTerracePilotFloor UpperLanding {get;}
        public Rect LowerLandingBounds {get;}
        public IReadOnlyList<Vector3> LowerLandingTopVertices {get;}
        public IReadOnlyList<Vector2[]> Footprints {get;}
        public IReadOnlyList<Vector3> RouteWorld {get;}
        public Rect Bounds {get;}
        public const float StatueScale=1.2f;
        public Vector3 StatuePosition=>new(Flight.Start.x,UpperLanding.Center.y,Flight.End.z-3f);
        public Rect StatueFootprint=>Rect.MinMaxRect(StatuePosition.x-.54f,StatuePosition.z-.54f,
            StatuePosition.x+.54f,StatuePosition.z+.54f);
        public IReadOnlyList<Vector3> FlowerbedPositions {get;}
        public IReadOnlyList<Vector2[]> DecorationSolidPolygons {get;}
        private Rect footwayOverlap;

        public static CityPawnshopBenchTerracePlan ForLayout(CityLayout layout)=>
            Cached.GetValue(layout,value=>new CityPawnshopBenchTerracePlan(value));

        private CityPawnshopBenchTerracePlan(CityLayout layout)
        {
            Footprints=Array.Empty<Vector2[]>();RouteWorld=Array.Empty<Vector3>();
            LowerLandingTopVertices=Array.Empty<Vector3>();
            FlowerbedPositions=Array.Empty<Vector3>();DecorationSolidPolygons=Array.Empty<Vector2[]>();
            if(!CityPawnshopPlan.TryCreate(layout,out var pawnshop)) return;
            CityHillsideStreetRoute westStreet=null;
            foreach(var street in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if(street.Bank==CityTerraceTransportBank.West) {westStreet=street;break;}
            if(westStreet==null) return;
            CityHillsideFurniturePlacement left=default,right=default;
            foreach(var bench in CityHillsideFurniturePlan.ForLayout(layout).Placements)
            {
                if(bench.Id==LeftBenchId) left=bench;
                if(bench.Id==RightBenchId) right=bench;
            }
            if(left.Id==null || right.Id==null || Mathf.Abs(left.Position.y-right.Position.y)>.01f ||
                (XZ(left.Position)-XZ(pawnshop.Origin)).sqrMagnitude>45f*45f ||
                (XZ(right.Position)-XZ(pawnshop.Origin)).sqrMagnitude>45f*45f) return;
            float top=Mathf.Max(left.Position.y,right.Position.y);
            float centerX=(left.Position.x+right.Position.x)*.5f;
            float topZ=(left.Position.z+right.Position.z)*.5f+2.70f;
            const float width=10.4f,rise=.16f,tread=.36f;
            const int steps=6;
            Flight=new CityExteriorStairFlightDescriptor("Pawnshop broad bench stair",
                new Vector3(centerX,top-steps*rise,topZ+steps*tread),Vector3.back,width,steps,rise,tread,
                CityExteriorStairDropSide.Both);
            StairFootprint=Rect.MinMaxRect(centerX-width*.5f,topZ,centerX+width*.5f,Flight.Start.z);
            float rear=(left.Position.z+right.Position.z)*.5f-2f;
            Rect upper=Rect.MinMaxRect(Mathf.Min(left.AccessBounds.xMin,right.AccessBounds.xMin),rear,
                Mathf.Max(left.AccessBounds.xMax,right.AccessBounds.xMax),topZ);
            UpperLanding=new CityTerracePilotFloor("Pawnshop shared bench landing",
                new Vector3(upper.center.x,top,upper.center.y),upper.size,true);
            FlowerbedPositions=new[] {StatuePosition+Vector3.left*1.7f,StatuePosition+Vector3.right*1.7f};
            var decorationSolids=new List<Vector2[]> {CityRoadPolygon.Rectangle(StatueFootprint)};
            foreach(Vector3 flowerbed in FlowerbedPositions)
                decorationSolids.Add(CityRoadPolygon.Rectangle(Rect.MinMaxRect(flowerbed.x-.65f,flowerbed.z-.35f,
                    flowerbed.x+.65f,flowerbed.z+.35f)));
            DecorationSolidPolygons=decorationSolids.AsReadOnly();
            // The north seam meets the true public sidewalk at its outer edge.
            // Across the width, retain its existing road grade rather than
            // inserting a level ten-metre kerb across the sloping street.
            float northZ=westStreet.GetLaneZ(4)-5f;
            LowerLandingBounds=Rect.MinMaxRect(StairFootprint.xMin,Flight.Start.z,StairFootprint.xMax,northZ);
            if(!TryStreet(new Vector2(LowerLandingBounds.xMin,northZ),out float lowLeft) ||
                !TryStreet(new Vector2(LowerLandingBounds.xMax,northZ),out float lowRight))
                throw new InvalidOperationException("The broad bench stair needs its actual public footway seam.");
            LowerLandingTopVertices=new[] {
                new Vector3(LowerLandingBounds.xMin,Flight.Start.y,Flight.Start.z),
                new Vector3(LowerLandingBounds.xMax,Flight.Start.y,Flight.Start.z),
                new Vector3(LowerLandingBounds.xMax,lowRight,northZ),
                new Vector3(LowerLandingBounds.xMin,lowLeft,northZ) };
            Footprints=new[] {CityRoadPolygon.Rectangle(upper),CityRoadPolygon.Rectangle(StairFootprint),
                CityRoadPolygon.Rectangle(LowerLandingBounds)};
            footwayOverlap=Rect.MinMaxRect(StairFootprint.xMin,northZ,StairFootprint.xMax,northZ+.5f);
            Bounds=Rect.MinMaxRect(upper.xMin,upper.yMin,upper.xMax,northZ+.5f);
            foreach(Rect protectedArea in CityBusNetworkFurniture.Reservations(CityDistrictBusPlanner.CreateRoadRouting(layout),layout))
                foreach(Vector2[] footprint in Footprints)
                    if(protectedArea.Overlaps(CityRoadPolygon.Bounds(footprint)))
                        throw new InvalidOperationException("The bench stair must retain the complete bus reservation.");
            IsEnabled=true;
            TrySampleTop(new Vector2(centerX,northZ),out float approachY);
            RouteWorld=new[] {new Vector3(centerX,approachY,northZ+.5f),new Vector3(centerX,approachY,northZ),Flight.Start,Flight.End,
                new Vector3(centerX,top,topZ-.8f)};

            bool TryStreet(Vector2 p,out float y)
            {
                return westStreet.TrySampleTop(p,out y,out var kind) && kind==CityStreetSupportKind.Sidewalk;
            }
        }

        /// <summary>The real continuous capsule ramp, with visible tread probes authored separately.</summary>
        public bool TrySampleTop(Vector2 p,out float top)
        {
            top=0f;
            if(!IsEnabled) return false;
            if(CityTerracePilotPlan.Inside(UpperLanding.Bounds,p.x,p.y)) {top=UpperLanding.Center.y;return true;}
            if(CityTerracePilotPlan.Inside(StairFootprint,p.x,p.y))
            {
                float run=Vector3.Dot(new Vector3(p.x,Flight.Start.y,p.y)-Flight.Start,Flight.Direction);
                top=Flight.Start.y+Mathf.Clamp01(run/Flight.RunLength)*Flight.TotalRise;return true;
            }
            if(CityTerracePilotPlan.Inside(footwayOverlap,p.x,p.y))
            {
                top=Mathf.Lerp(LowerLandingTopVertices[3].y,LowerLandingTopVertices[2].y,
                    Mathf.InverseLerp(footwayOverlap.xMin,footwayOverlap.xMax,p.x));return true;
            }
            return CityTerracePilotPlan.Inside(LowerLandingBounds,p.x,p.y) &&
                CityStreetSupportPatch.TrySampleTop(LowerLandingTopVertices,p,out top);
        }

        internal bool TryApproach(CityHillsideFurniturePlacement placement,Vector3 dock,out List<Vector3> path)
        {
            path=null;
            if(!IsEnabled || (placement.Id!=LeftBenchId && placement.Id!=RightBenchId)) return false;
            var corners=new List<Vector3>(RouteWorld) {dock};
            path=new List<Vector3>();
            for(int i=1;i<corners.Count;i++)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(XZ(corners[i-1]),XZ(corners[i]))/.35f));
                for(int j=i==1?0:1;j<=count;j++)
                {
                    Vector3 point=Vector3.Lerp(corners[i-1],corners[i],(float)j/count);
                    if(!TrySampleTop(XZ(point),out float y))
                        throw new InvalidOperationException("The bench terrace approach left its measured landing.");
                    path.Add(new Vector3(point.x,y,point.z));
                }
            }
            return true;
        }

        private static Vector2 XZ(Vector3 p)=>new(p.x,p.z);
    }
}
