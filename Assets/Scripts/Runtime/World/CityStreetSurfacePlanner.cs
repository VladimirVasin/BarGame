using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public static class CityStreetSurfacePlanner
    {
        /// <summary>
        /// One plan per layout: the result is read-only copies all the way
        /// down, and the bus grounding, the road batches, the pedestrian
        /// planner and the cannery trolleys each used to plan it again
        /// (18-54 ms apiece) for the same answer.
        /// </summary>
        private static readonly ConditionalWeakTable<CityLayout, CityStreetSurfacePlan>
            Plans = new ConditionalWeakTable<CityLayout, CityStreetSurfacePlan>();
        private static readonly ConditionalWeakTable<CityLayout, CityStreetSurfacePlan>
            .CreateValueCallback CreateUncachedCallback = CreateUncached;
        private static readonly ConditionalWeakTable<CityLayout, CityStreetSurfacePlan>
            CourtyardPavingPlans = new ConditionalWeakTable<CityLayout, CityStreetSurfacePlan>();
        private static readonly ConditionalWeakTable<CityLayout, CityStreetSurfacePlan>
            .CreateValueCallback CreateCourtyardPavingCallback = CreateCourtyardPavingUncached;
        private static readonly ConditionalWeakTable<CityLayout, PublicPavementContext> PublicPavements =
            new ConditionalWeakTable<CityLayout, PublicPavementContext>();
        private static readonly ConditionalWeakTable<CityLayout,List<CityEntranceForecourtPlan>> EntranceHeightPlans=
            new ConditionalWeakTable<CityLayout,List<CityEntranceForecourtPlan>>();

        // Kept as the inset inside the eight-metre traffic corridor. Vehicle
        // lanes, stop docks and existing pedestrian lane IDs depend on it.
        public const float SidewalkWidth = 1f;
        public const float PublicSidewalkWidth = 2f;
        public const float RoadTop = 0.08f;
        public const float SidewalkTop = 0.14f;
        public const float CrosswalkDepth = 2.4f;
        public const float BusApproachApronLength = 4.5f;
        public const int CrosswalkStripeCount = 4;
        public const int MaximumCrosswalkIntersections =
            CityStreetIntersectionSelector.MaximumIntersectionCount;

        private const float RoadSurfaceHeight = RoadTop * 2f;
        private const float SidewalkHeight = SidewalkTop - RoadTop;
        private const float PublicPavementThickness = .30f;
        private const float CenterDashWidth = 0.13f;
        private const float MaximumCenterDashLength = 2.1f;
        private const float MarkingHeight = 0.025f;
        private const float MarkingCenterAboveRoadBase = 0.095f;
        private const float CrosswalkStripeDepth = 0.36f;
        // Deep enough to outlast the graded apron's own fall across the
        // crossing: a pavement thickness plus the crossfall over half the
        // opening, with room to spare. It is buried; only its outer face
        // is ever seen, through the kerb line it closes.
        private const float PortOpeningSkirtDepth = 0.6f;
        private const float GeometryTolerance = 0.0001f;

        public static float GetPublicSidewalkOuterDistance(CityLayout layout) =>
            layout.RoadWidth * .5f +
            (layout.SpatialPlan.IsCompactCoastal ? PublicSidewalkWidth - SidewalkWidth : 0f);

        private static float PavementThickness(float width) =>
            width > SidewalkWidth ? PublicPavementThickness : SidewalkHeight;

        public static CityEntranceForecourtPlan CreateEntranceForecourt(CityLayout layout, BuildingLot lot, float legacyWidth)
            =>CreateEntranceForecourtCore(layout,lot,legacyWidth,true);

        private static CityEntranceForecourtPlan CreateEntranceForecourtCore(CityLayout layout,BuildingLot lot,float legacyWidth,bool sampleSoil)
        {
            Vector3 direction = new Vector3(lot.FrontageDirection.x, 0f, lot.FrontageDirection.y);
            bool alongX = lot.FrontageDirection.x != 0;
            bool compact = layout.SpatialPlan.IsCompactCoastal;
            float width = compact ? (alongX ? lot.Size.y : lot.Size.x) : legacyWidth;
            Vector3 tangent=new Vector3(-direction.z,0f,direction.x);
            if(!compact)
            {
                Vector3 center=(lot.DoorPosition+lot.SidewalkArrivalPosition)*.5f+Vector3.up*(SidewalkTop-SidewalkHeight*.5f);
                float length=Vector3.Distance(lot.DoorPosition,lot.SidewalkArrivalPosition);
                var bounds=new Bounds(center,alongX?new Vector3(length,SidewalkHeight,width):new Vector3(width,SidewalkHeight,length));
                Vector3 near=center-direction*length*.5f,far=center+direction*length*.5f;
                var top=new[]{near-tangent*width*.5f,far-tangent*width*.5f,
                    near+tangent*width*.5f,far+tangent*width*.5f};
                var bottom=new Vector3[4];
                for(int i=0;i<4;i++){top[i].y=bounds.max.y;bottom[i]=top[i]-Vector3.up*SidewalkHeight;}
                return new CityEntranceForecourtPlan(top,bottom,2,2,false);
            }
            RoadEdge edge=RoadEdge.ForCellFrontage(lot.Cell,lot.FrontageDirection);
            CityRoadPath path=layout.RoadGeometry.Get(edge);
            int rightSign=edge.IsHorizontal?lot.FrontageDirection.y:-lot.FrontageDirection.x;
            float outerDistance=layout.RoadWidth*.5f-SidewalkWidth+GetPublicPavement(layout).Width(edge,rightSign);
            CityStreetSurfacePlan paving=CreateCourtyardPaving(layout);
            float doorRoad=layout.ElevationPlan.SampleRoadDatum(edge,
                path.Project(new Vector2(lot.DoorPosition.x,lot.DoorPosition.z)).DistanceAlong/path.Length);
            float thresholdCorrection=lot.DoorPosition.y-doorRoad;
            int rows=Mathf.Clamp(Mathf.CeilToInt(width/.65f)+1,2,33);
            float run=Vector2.Distance(new Vector2(lot.DoorPosition.x,lot.DoorPosition.z),
                path.Project(new Vector2(lot.DoorPosition.x,lot.DoorPosition.z)).Position)-outerDistance;
            int columns=Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(run)/.6f)+1,2,13);
            var tops=new Vector3[rows*columns];var bottoms=new Vector3[tops.Length];
            for(int row=0;row<rows;row++)
            {
                Vector3 inner=lot.DoorPosition-direction*.08f+tangent*Mathf.Lerp(-width*.5f,width*.5f,row/(float)(rows-1));
                CityRoadProjection projection=path.Project(new Vector2(inner.x,inner.z));
                Vector3 outer=new Vector3(projection.Position.x,0f,projection.Position.y)-direction*(outerDistance-.04f);
                float road=layout.ElevationPlan.SampleRoadDatum(edge,projection.DistanceAlong/path.Length);
                float innerTop=road+SidewalkTop+thresholdCorrection;
                float outerTop=TrySamplePublicSidewalkTop(layout,paving,new Vector2(outer.x,outer.z),out float pavementTop)
                    ?pavementTop:road+SidewalkTop;
                for(int column=0;column<columns;column++)
                {
                    float amount=column/(float)(columns-1);
                    Vector3 point=Vector3.Lerp(inner,outer,amount);
                    float top=Mathf.Lerp(innerTop,outerTop,amount);
                    float soil=0f;
                    bool supported=sampleSoil && CityTerrainSurfacePlan.TrySampleGroundTop(layout,new Vector2(point.x,point.z),out soil,out _);
                    point.y=top;int index=row*columns+column;tops[index]=point;
                    bottoms[index]=new Vector3(point.x,supported?Mathf.Min(top-PublicPavementThickness,soil-.12f):
                        Mathf.Min(top-PublicPavementThickness,lot.Center.y-.4f),point.z);
                }
            }
            return new CityEntranceForecourtPlan(tops,bottoms,rows,columns,true);
        }

        public static bool TrySampleEntranceForecourtTop(CityLayout layout,Vector2 point,out float top)
        {
            top=float.NegativeInfinity;
            if(!layout.SpatialPlan.IsCompactCoastal)return false;
            // Height-only plans never sample terrain/cannery/bus routing.
            // Fixtures can ask during early routing without a dependency cycle.
            List<CityEntranceForecourtPlan> plans=EntranceHeightPlans.GetValue(layout,value=>
            {
                var result=new List<CityEntranceForecourtPlan>(4);
                foreach(BuildingLot lot in value.BuildingLots)
                    if(lot.IsBar || lot.IsPlayerHome || lot.IsSupermarket)
                        result.Add(CreateEntranceForecourtCore(value,lot,3f,false));
                return result;
            });
            bool found=false;
            foreach(CityEntranceForecourtPlan plan in plans)
                if(plan.Footprint.Contains(point))
                {top=Mathf.Max(top,plan.SampleTop(new Vector3(point.x,0f,point.y)));found=true;}
            return found;
        }

        public static bool TrySamplePublicSidewalkTop(CityLayout layout,Vector2 point,out float top) =>
            TrySamplePublicSidewalkTop(layout,CreateCourtyardPaving(layout),point,out top);

        private static bool TrySamplePublicSidewalkTop(CityLayout layout,CityStreetSurfacePlan paving,Vector2 point,out float top)
        {
            top=float.NegativeInfinity;bool found=false;
            foreach(RuntimeOrientedBox box in paving.SidewalkGeometry)
            {
                Vector3 normal=box.Rotation*Vector3.up,plane=box.Center+normal*box.Size.y*.5f;
                if(normal.y<.1f)continue;
                float height=plane.y-(normal.x*(point.x-plane.x)+normal.z*(point.y-plane.z))/normal.y;
                Vector3 local=Quaternion.Inverse(box.Rotation)*(new Vector3(point.x,height,point.y)-box.Center);
                if(Mathf.Abs(local.x)>box.Size.x*.5f+.001f || Mathf.Abs(local.z)>box.Size.z*.5f+.001f)continue;
                top=Mathf.Max(top,height);found=true;
            }
            foreach(CityStreetRibbonDescriptor ribbon in paving.CurvedSidewalkRibbons)
            foreach(Vector2[] polygon in ribbon.Polygons)
            {
                if(!CityRoadPolygon.Contains(polygon,point))continue;
                CityRoadPath path=layout.RoadGeometry.Get(ribbon.Edge);
                float height=(ribbon.FlatNode.HasValue?layout.ElevationPlan.GetNodeElevation(ribbon.FlatNode.Value):
                    layout.ElevationPlan.SampleRoadDatum(ribbon.Edge,path.Project(point).DistanceAlong/path.Length))+ribbon.TopOffset;
                top=Mathf.Max(top,height);found=true;
            }
            return found;
        }

        public static CityStreetSurfacePlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return Plans.GetValue(layout, CreateUncachedCallback);
        }

        // Ordinary courts use their own cell ground and frontage-side strip;
        // connections join ordinary cells only across missing road arms.
        // The cannery's driveway cut belongs to the protected POI's opposite
        // strip and cannot intersect that walking ground. Omit its service
        // selection here: like crosswalk selection, it consumes court routes.
        internal static CityStreetSurfacePlan CreateCourtyardPaving(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return CourtyardPavingPlans.GetValue(layout, CreateCourtyardPavingCallback);
        }

        private static CityStreetSurfacePlan CreateUncached(CityLayout layout) => CreateCore(layout, true);
        private static CityStreetSurfacePlan CreateCourtyardPavingUncached(CityLayout layout) => CreateCore(layout, false);

        private static CityStreetSurfacePlan CreateCore(CityLayout layout, bool includeCrosswalks)
        {
            float carriagewayWidth =
                layout.RoadWidth - (SidewalkWidth * 2f);
            if (!IsFinite(layout.RoadWidth) ||
                carriagewayWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    layout.RoadWidth,
                    "Road width must leave positive carriageway space " +
                    "between both sidewalks.");
            }

            var streetSurfaces = new List<Bounds>();
            var parkPaths = new List<Bounds>();
            var sidewalks = new List<Bounds>();
            var centerMarkings = new List<Bounds>();
            var crosswalkMarkings = new List<Bounds>();
            var streetGeometry = new List<RuntimeOrientedBox>();
            var parkPathGeometry = new List<RuntimeOrientedBox>();
            var sidewalkGeometry = new List<RuntimeOrientedBox>();
            var centerMarkingGeometry = new List<RuntimeOrientedBox>();
            var crosswalkMarkingGeometry =
                new List<RuntimeOrientedBox>();
            var sidewalkWalkableRectangles = new List<Rect>();
            var crosswalkWalkableRectangles = new List<Rect>();
            var crosswalks = new List<CityCrosswalkDescriptor>();
            var markingExclusions = new List<Rect>();
            CityEastExitPlan eastExit = CityEastExitPlanner.Create(layout);
            if (eastExit.IsEnabled) markingExclusions.Add(eastExit.StreetOpening);
            var edgesWithSidewalks = new HashSet<RoadEdge>();

            Dictionary<Vector2Int, NodeConnections> connections =
                CreateNodeConnections(layout);
            PublicPavementContext publicPavement = GetPublicPavement(layout);
            var busIntersections = new HashSet<Vector2Int>(
                CityBusIntersectionSelector.Select(layout));
            List<RoadEdge> sortedEdges = CreateSortedEdges(layout);
            CreateBaseSurfaces(
                layout,
                sortedEdges,
                streetSurfaces,
                parkPaths,
                streetGeometry,
                parkPathGeometry);
            CreateSidewalkStrips(
                layout,
                sortedEdges,
                connections,
                busIntersections,
                sidewalks,
                sidewalkGeometry,
                sidewalkWalkableRectangles,
                edgesWithSidewalks,
                includeCrosswalks,
                publicPavement);
            CreateIntersectionSidewalks(
                layout,
                connections,
                busIntersections,
                sidewalks,
                sidewalkGeometry,
                sidewalkWalkableRectangles,
                markingExclusions,
                publicPavement);

            IReadOnlyList<Vector2Int> selectedNodes = Array.Empty<Vector2Int>();
            if (includeCrosswalks)
            {
                selectedNodes = CityStreetIntersectionSelector.Select(layout, MaximumCrosswalkIntersections);
                CreateCrosswalks(
                    layout,
                    selectedNodes,
                    sortedEdges,
                    edgesWithSidewalks,
                    carriagewayWidth,
                    crosswalkMarkings,
                    crosswalkMarkingGeometry,
                    crosswalkWalkableRectangles,
                    crosswalks,
                    markingExclusions);
                CreateCenterMarkings(
                    layout,
                    sortedEdges,
                    markingExclusions,
                    centerMarkings,
                    centerMarkingGeometry);
            }

            var curvedStreets = new List<CityStreetRibbonDescriptor>();
            var curvedSidewalks = new List<CityStreetRibbonDescriptor>();
            var curvedMarkings = new List<CityStreetRibbonDescriptor>();
            CreateCurvedRibbons(layout, connections, busIntersections, markingExclusions,
                curvedStreets, curvedSidewalks, curvedMarkings, includeCrosswalks,
                publicPavement);

            return new CityStreetSurfacePlan(
                carriagewayWidth,
                streetSurfaces,
                parkPaths,
                sidewalks,
                centerMarkings,
                crosswalkMarkings,
                streetGeometry,
                parkPathGeometry,
                sidewalkGeometry,
                centerMarkingGeometry,
                crosswalkMarkingGeometry,
                sidewalkWalkableRectangles,
                crosswalkWalkableRectangles,
                new List<Vector2Int>(selectedNodes),
                crosswalks,
                curvedStreets, curvedSidewalks, curvedMarkings,
                layout.SpatialPlan.IsCompactCoastal ? PublicSidewalkWidth : SidewalkWidth);
        }

        private static void CreateBaseSurfaces(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            ICollection<Bounds> streetSurfaces,
            ICollection<Bounds> parkPaths,
            ICollection<RuntimeOrientedBox> streetGeometry,
            ICollection<RuntimeOrientedBox> parkPathGeometry)
        {
            var streetNodes = new HashSet<Vector2Int>();
            var parkNodes = new HashSet<Vector2Int>();
            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                if (layout.RoadGeometry.IsCurved(edge))
                {
                    CityRoadPath path = layout.RoadGeometry.Get(edge);
                    Rect bounds = path.Bounds;
                    bounds.xMin -= layout.RoadWidth * .5f; bounds.xMax += layout.RoadWidth * .5f;
                    bounds.yMin -= layout.RoadWidth * .5f; bounds.yMax += layout.RoadWidth * .5f;
                    streetSurfaces.Add(new Bounds(new Vector3(bounds.center.x,
                        (layout.GetNodeWorldPosition(edge.A).y + layout.GetNodeWorldPosition(edge.B).y) * .5f,
                        bounds.center.y), new Vector3(bounds.width, RoadSurfaceHeight, bounds.height)));
                    streetNodes.Add(edge.A); streetNodes.Add(edge.B);
                    continue;
                }
                float surfaceWidth = layout.GetTravelWidth(edge);
                float halfSurface = surfaceWidth * 0.5f;
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Vector3 delta = end - start;
                float planarLength = new Vector2(
                    delta.x,
                    delta.z).magnitude;
                float endInset = layout.TryGetBridge(
                        edge,
                        out CityRiverBridgeDescriptor bridge)
                    ? ResolveBridgeSpanInset(edge, start, end, bridge)
                    : halfSurface;
                float insetAmount = planarLength > GeometryTolerance
                    ? Mathf.Clamp01(endInset / planarLength)
                    : 0f;
                float startAmount = layout.RoadGeometry.ObliqueJunction?.Node == edge.A
                    ? CityRoadJunction.MouthInset / planarLength : insetAmount;
                float endAmount = layout.RoadGeometry.ObliqueJunction?.Node == edge.B
                    ? CityRoadJunction.MouthInset / planarLength : insetAmount;
                Vector3 segmentStart = Vector3.Lerp(
                    start,
                    end,
                    startAmount);
                Vector3 segmentEnd = Vector3.Lerp(
                    start,
                    end,
                    1f - endAmount);
                segmentStart.y = start.y;
                segmentEnd.y = end.y;
                bool isStreet = layout.GetPathKind(edge) ==
                                CityPathKind.Street;
                bool hasSignatureStair = isStreet &&
                    layout.ElevationPlan.TryGetSignatureStair(
                        edge,
                        out _);
                float renderedWidth = hasSignatureStair
                    ? layout.RoadWidth - (SidewalkWidth * 2f)
                    : surfaceWidth;
                RuntimeOrientedBox geometry = CreateSurfaceBox(
                    segmentStart + Vector3.up * RoadTop,
                    segmentEnd + Vector3.up * RoadTop,
                    renderedWidth,
                    RoadSurfaceHeight);
                Vector3 size = edge.IsHorizontal
                    ? new Vector3(
                        Mathf.Abs(delta.x) + surfaceWidth,
                        RoadSurfaceHeight,
                        renderedWidth)
                    : new Vector3(
                        renderedWidth,
                        RoadSurfaceHeight,
                        Mathf.Abs(delta.z) + surfaceWidth);
                var surface = new Bounds((start + end) * 0.5f, size);
                if (!isStreet)
                {
                    parkPaths.Add(surface);
                    parkPathGeometry.Add(geometry);
                    parkNodes.Add(edge.A);
                    parkNodes.Add(edge.B);
                }
                else
                {
                    streetSurfaces.Add(surface);
                    streetGeometry.Add(geometry);
                    streetNodes.Add(edge.A);
                    streetNodes.Add(edge.B);
                    if (hasSignatureStair)
                    {
                        AddStairEdgeKerbBands(
                            layout,
                            edge,
                            segmentStart,
                            segmentEnd,
                            streetGeometry);
                    }
                }
            }

            foreach (Vector2Int node in streetNodes)
            {
                if (layout.RoadGeometry.ObliqueJunction?.Node == node) continue;
                Vector3 center = layout.GetNodeWorldPosition(node);
                streetGeometry.Add(new RuntimeOrientedBox(
                    center,
                    Quaternion.identity,
                    new Vector3(
                        layout.RoadWidth,
                        RoadSurfaceHeight,
                        layout.RoadWidth)));
            }

            foreach (Vector2Int node in parkNodes)
            {
                if (streetNodes.Contains(node))
                {
                    continue;
                }

                Vector3 center = layout.GetNodeWorldPosition(node);
                float nodeWidth = layout.RoadWidth;
                parkPathGeometry.Add(new RuntimeOrientedBox(
                    center,
                    Quaternion.identity,
                    new Vector3(
                        nodeWidth,
                        RoadSurfaceHeight,
                        nodeWidth)));
            }
        }

        // A signature-stair edge narrows its slab to the carriageway so the
        // flight is never paved over, and at a bus node the pavement starts
        // 8.5 m out instead of 4. District ground stops at the road band by
        // contract, so between the two the metre-wide kerb strip had NO
        // surface at all: four 4.5 x 1 m holes the hero fell through, one at
        // each stair whose edge ends on a bus node. The band is floored here
        // at road level, under whatever pavement exists above it, and cut
        // around the stair's own ground cut so the flight stays clear.
        private static void AddStairEdgeKerbBands(
            CityLayout layout,
            RoadEdge edge,
            Vector3 segmentStart,
            Vector3 segmentEnd,
            ICollection<RuntimeOrientedBox> streetGeometry)
        {
            Vector3 delta = segmentEnd - segmentStart;
            var tangent = new Vector3(delta.x, 0f, delta.z);
            if (tangent.sqrMagnitude <= GeometryTolerance)
            {
                return;
            }

            tangent.Normalize();
            var left = new Vector3(-tangent.z, 0f, tangent.x);
            float sideOffset = (layout.RoadWidth * 0.5f) -
                               (SidewalkWidth * 0.5f);
            Rect cut = default;
            bool hasCut = layout.ElevationPlan.TryGetSignatureStair(
                edge,
                out CityElevationStairDescriptor stair);
            if (hasCut)
            {
                cut = CityElevationStairPlacementPlanner
                    .Create(layout, stair)
                    .GroundCutFootprint;
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 offset = left * (sideOffset * side);
                AddKerbBandOutsideCut(
                    segmentStart + offset + (Vector3.up * RoadTop),
                    segmentEnd + offset + (Vector3.up * RoadTop),
                    hasCut,
                    cut,
                    streetGeometry);
            }
        }

        private static void AddKerbBandOutsideCut(
            Vector3 start,
            Vector3 end,
            bool hasCut,
            Rect cut,
            ICollection<RuntimeOrientedBox> streetGeometry)
        {
            if (hasCut)
            {
                bool horizontal = Mathf.Abs(end.z - start.z) <
                                  GeometryTolerance;
                bool vertical = Mathf.Abs(end.x - start.x) <
                                GeometryTolerance;
                float fixedValue = horizontal ? start.z : start.x;
                float fixedMin = horizontal ? cut.yMin : cut.xMin;
                float fixedMax = horizontal ? cut.yMax : cut.xMax;
                float from = horizontal ? start.x : start.z;
                float to = horizontal ? end.x : end.z;
                float minimum = horizontal ? cut.xMin : cut.yMin;
                float maximum = horizontal ? cut.xMax : cut.yMax;
                if ((horizontal || vertical) &&
                    fixedValue >= fixedMin &&
                    fixedValue <= fixedMax &&
                    Mathf.Max(from, to) > minimum &&
                    Mathf.Min(from, to) < maximum &&
                    Mathf.Abs(to - from) > GeometryTolerance)
                {
                    float first = (minimum - from) / (to - from);
                    float second = (maximum - from) / (to - from);
                    float low = Mathf.Clamp01(Mathf.Min(first, second));
                    float high = Mathf.Clamp01(Mathf.Max(first, second));
                    if (low > 0.001f)
                    {
                        streetGeometry.Add(CreateSurfaceBox(
                            start,
                            Vector3.Lerp(start, end, low),
                            SidewalkWidth,
                            RoadSurfaceHeight));
                    }

                    if (high < 0.999f)
                    {
                        streetGeometry.Add(CreateSurfaceBox(
                            Vector3.Lerp(start, end, high),
                            end,
                            SidewalkWidth,
                            RoadSurfaceHeight));
                    }

                    return;
                }
            }

            streetGeometry.Add(CreateSurfaceBox(
                start,
                end,
                SidewalkWidth,
                RoadSurfaceHeight));
        }

        // A crossing edge runs bank node to bank node, but the eight metres
        // behind each bank node are the embankment, not the bridge. The span
        // is the carriageway - beyond it the granite promenade is the
        // surface, so the strip stops on the quay seat the span rests on.
        private static float ResolveBridgeSpanInset(
            RoadEdge edge,
            Vector3 start,
            Vector3 end,
            CityRiverBridgeDescriptor bridge)
        {
            Rect span = bridge.SpanBounds;
            float first = edge.IsHorizontal ? start.x : start.z;
            float second = edge.IsHorizontal ? end.x : end.z;
            float spanMinimum = edge.IsHorizontal ? span.xMin : span.yMin;
            float spanMaximum = edge.IsHorizontal ? span.xMax : span.yMax;
            return Mathf.Max(
                spanMinimum - Mathf.Min(first, second),
                Mathf.Max(first, second) - spanMaximum);
        }

        private static void CreateSidewalkStrips(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ISet<Vector2Int> busIntersections,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles,
            ISet<RoadEdge> edgesWithSidewalks, bool includeCanneryOpening,
            PublicPavementContext publicPavement)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            // Both precinct plans are pure functions of the layout (and
            // memoised on it); resolved once here instead of under every
            // pavement strip.
            CityCanneryPlan cannery = includeCanneryOpening ? CityCanneryPlan.Create(layout) : null;
            CityPortAccessPlan port = CityPortAccessPlan.ForLayout(layout);
            CityEastExitPlan eastExit = CityEastExitPlanner.Create(layout);
            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                if (layout.RoadGeometry.IsCurved(edge))
                {
                    edgesWithSidewalks.Add(edge);
                    continue;
                }

                Vector3 roadStart = layout.GetNodeWorldPosition(edge.A);
                Vector3 roadEnd = layout.GetNodeWorldPosition(edge.B);
                Vector3 delta = roadEnd - roadStart;
                float planarLength = new Vector2(
                    delta.x,
                    delta.z).magnitude;
                Vector3 tangent = new Vector3(
                    delta.x,
                    0f,
                    delta.z).normalized;
                float startInset = ResolveEndpointInset(
                    connections[edge.A],
                    halfRoad) +
                    (busIntersections.Contains(edge.A)
                        ? BusApproachApronLength
                        : 0f);
                float endInset = ResolveEndpointInset(
                    connections[edge.B],
                    halfRoad) +
                    (busIntersections.Contains(edge.B)
                        ? BusApproachApronLength
                        : 0f);
                if (layout.RoadGeometry.ObliqueJunction?.Node == edge.A) startInset = CityRoadJunction.MouthInset;
                if (layout.RoadGeometry.ObliqueJunction?.Node == edge.B) endInset = CityRoadJunction.MouthInset;
                // A NEGATIVE inset (a dead end) extends the sidewalk
                // PAST the node so the road cap gets its wrap - clamping
                // it to zero used to stop the pavement at the node
                // centre while the road ran half a road further. The
                // elevation sample stays clamped: the extension is flat
                // at the cap's own datum.
                float startRatio = startInset < 0f
                    ? startInset / planarLength
                    : Mathf.Clamp01(startInset / planarLength);
                float endRatio = endInset < 0f
                    ? 1f - (endInset / planarLength)
                    : 1f - Mathf.Clamp01(endInset / planarLength);
                Vector3 start = Vector3.LerpUnclamped(
                    roadStart,
                    roadEnd,
                    startRatio);
                Vector3 end = Vector3.LerpUnclamped(
                    roadStart,
                    roadEnd,
                    endRatio);
                start.y = layout.ElevationPlan.SampleRoadDatum(
                    edge,
                    Mathf.Clamp01(startRatio));
                end.y = layout.ElevationPlan.SampleRoadDatum(
                    edge,
                    Mathf.Clamp01(endRatio));
                float length = Vector3.Distance(start, end);
                if (length <= GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Street edge {edge} is too short for sidewalks.");
                }

                Vector3 left = new Vector3(
                    -tangent.z,
                    0f,
                    tangent.x);
                bool hasStair = layout.ElevationPlan.TryGetSignatureStair(
                    edge,
                    out CityElevationStairDescriptor stair);
                bool stairOnLeft = false;
                CityElevationStairPlacement stairPlacement = null;
                if (hasStair)
                {
                    stairPlacement =
                        CityElevationStairPlacementPlanner.Create(
                            layout,
                            stair);
                    stairOnLeft = Vector3.Dot(
                        stairPlacement.SideDirection,
                        left) > 0f;
                }

                if (!hasStair || !stairOnLeft)
                {
                    float width = publicPavement.Width(edge, -1);
                    float sideOffset = halfRoad - SidewalkWidth + width * .5f;
                    AddSidewalkWithPortOpening(cannery, port, eastExit,
                            start + left * sideOffset + Vector3.up * SidewalkTop,
                            end + left * sideOffset + Vector3.up * SidewalkTop,
                        sidewalks,
                        sidewalkGeometry,
                        walkableRectangles, width);
                    AddPublicJunctionAprons(layout, edge, left * sideOffset,
                        width, startInset, endInset, busIntersections,
                        sidewalks, sidewalkGeometry, walkableRectangles);
                }

                if (!hasStair || stairOnLeft)
                {
                    float width = publicPavement.Width(edge, 1);
                    float sideOffset = halfRoad - SidewalkWidth + width * .5f;
                    AddSidewalkWithPortOpening(cannery, port, eastExit,
                            start - left * sideOffset + Vector3.up * SidewalkTop,
                            end - left * sideOffset + Vector3.up * SidewalkTop,
                        sidewalks,
                        sidewalkGeometry,
                        walkableRectangles, width);
                    AddPublicJunctionAprons(layout, edge, -left * sideOffset,
                        width, startInset, endInset, busIntersections,
                        sidewalks, sidewalkGeometry, walkableRectangles);
                }

                if (hasStair)
                {
                    AddSignatureStairApproaches(
                        stair,
                        stairPlacement,
                        sidewalks,
                        sidewalkGeometry,
                        walkableRectangles);
                }
                edgesWithSidewalks.Add(edge);
            }
        }

        private static void AddSidewalkWithPortOpening(CityCanneryPlan cannery, CityPortAccessPlan port,
            CityEastExitPlan eastExit,
            Vector3 start, Vector3 end,
            ICollection<Bounds> sidewalks, ICollection<RuntimeOrientedBox> geometry, ICollection<Rect> walkable,
            float width)
        {
            // The eastern road joins the outside asphalt edge. Only its
            // near-side pavement is opened; the opposite pavement continues.
            if (eastExit.IsEnabled && Mathf.Abs(end.x - start.x) < .01f &&
                start.x + width * .5f > eastExit.ApproachStart.x - SidewalkWidth &&
                start.x - width * .5f < eastExit.ApproachStart.x + GeometryTolerance)
            {
                Rect opening = eastExit.StreetOpening;
                if (Mathf.Max(start.z, end.z) > opening.yMin &&
                    Mathf.Min(start.z, end.z) < opening.yMax)
                {
                    float a = (opening.yMin - start.z) / (end.z - start.z);
                    float b = (opening.yMax - start.z) / (end.z - start.z);
                    float low = Mathf.Clamp01(Mathf.Min(a, b));
                    float high = Mathf.Clamp01(Mathf.Max(a, b));
                    if (low > .001f) AddSidewalk(CreateSurfaceBox(start,
                        Vector3.Lerp(start, end, low), width, PavementThickness(width)), sidewalks, geometry, walkable);
                    if (high < .999f) AddSidewalk(CreateSurfaceBox(Vector3.Lerp(start, end, high),
                        end, width, PavementThickness(width)), sidewalks, geometry, walkable);
                    walkable.Add(Rect.MinMaxRect(start.x - width * .5f,
                        opening.yMin, start.x + width * .5f, opening.yMax));
                    return;
                }
            }
            if(cannery!=null)
            {
                Rect opening=cannery.StreetOpening;
                bool horizontal=Mathf.Abs(end.z-start.z)<.01f;
                bool vertical=Mathf.Abs(end.x-start.x)<.01f;
                float fixedValue=horizontal?start.z:start.x;
                float fixedMin=horizontal?opening.yMin:opening.xMin;
                float fixedMax=horizontal?opening.yMax:opening.xMax;
                float from=horizontal?start.x:start.z, to=horizontal?end.x:end.z;
                float minimum=horizontal?opening.xMin:opening.yMin, maximum=horizontal?opening.xMax:opening.yMax;
                if((horizontal||vertical)&&fixedValue+width*.5f>fixedMin&&fixedValue-width*.5f<fixedMax&&
                    Mathf.Max(from,to)>minimum&&Mathf.Min(from,to)<maximum)
                {
                    float a=(minimum-from)/(to-from),b=(maximum-from)/(to-from);
                    float low=Mathf.Clamp01(Mathf.Min(a,b)),high=Mathf.Clamp01(Mathf.Max(a,b));
                    if(low>.001f)AddSidewalk(CreateSurfaceBox(start,Vector3.Lerp(start,end,low),width,PavementThickness(width)),sidewalks,geometry,walkable);
                    if(high<.999f)AddSidewalk(CreateSurfaceBox(Vector3.Lerp(start,end,high),end,width,PavementThickness(width)),sidewalks,geometry,walkable);
                    // The imported apron supplies the dropped physical surface;
                    // pedestrian lanes continue over this one vehicle crossing.
                    walkable.Add(opening);
                    return;
                }
            }
            if(port!=null && Mathf.Abs(end.z-start.z)<.01f && Mathf.Abs(end.x-start.x)>.01f)
            {
                Rect opening=port.StreetOpening;
                if(start.z+width*.5f>opening.yMin && start.z-width*.5f<opening.yMax &&
                    Mathf.Max(start.x,end.x)>opening.xMin && Mathf.Min(start.x,end.x)<opening.xMax)
                {
                    float a=(opening.xMin-start.x)/(end.x-start.x),b=(opening.xMax-start.x)/(end.x-start.x);
                    float low=Mathf.Clamp01(Mathf.Min(a,b)),high=Mathf.Clamp01(Mathf.Max(a,b));
                    if(low>.001f)AddSidewalk(CreateSurfaceBox(start,Vector3.Lerp(start,end,low),width,PavementThickness(width)),sidewalks,geometry,walkable);
                    if(high<.999f)AddSidewalk(CreateSurfaceBox(Vector3.Lerp(start,end,high),end,width,PavementThickness(width)),sidewalks,geometry,walkable);
                    AddPortOpeningSkirt(
                        Vector3.Lerp(start, end, low),
                        Vector3.Lerp(start, end, high),
                        geometry, width);
                    return;
                }
            }
            AddSidewalk(CreateSurfaceBox(start,end,width,PavementThickness(width)),sidewalks,geometry,walkable);
        }

        // The port crossing drops the kerb so a truck can cross, and the
        // graded apron behind it is laid a pavement thickness below the
        // street datum - lower still across the crossfall. The road slab's
        // own underside sits exactly on nominal ground, so along that one
        // stretch its side face stood over open sky and the player saw a
        // fog-coloured slit under the kerb. This buried skirt closes the
        // line without touching the port's own height contract.
        private static void AddPortOpeningSkirt(
            Vector3 start,
            Vector3 end,
            ICollection<RuntimeOrientedBox> geometry, float width)
        {
            if ((end - start).sqrMagnitude <= GeometryTolerance)
            {
                return;
            }

            Vector3 drop = Vector3.up * (SidewalkTop + RoadTop);
            geometry.Add(CreateSurfaceBox(
                start - drop,
                end - drop,
                width,
                PortOpeningSkirtDepth));
        }

        private static void AddSignatureStairApproaches(
            CityElevationStairDescriptor stair,
            CityElevationStairPlacement placement,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            AddApproachIfLongEnough(
                placement.LowerApproachStart,
                placement.LowerApproachEnd,
                stair.Width,
                sidewalks,
                sidewalkGeometry,
                walkableRectangles);
            AddApproachIfLongEnough(
                placement.UpperApproachStart,
                placement.UpperApproachEnd,
                stair.Width,
                sidewalks,
                sidewalkGeometry,
                walkableRectangles);
            walkableRectangles.Add(placement.Footprint);
        }

        private static void AddPublicJunctionAprons(CityLayout layout, RoadEdge edge,
            Vector3 sideOffset, float width,
            float startInset, float endInset, ISet<Vector2Int> busIntersections,
            ICollection<Bounds> sidewalks, ICollection<RuntimeOrientedBox> geometry,
            ICollection<Rect> walkable)
        {
            if (width <= SidewalkWidth) return;
            Vector3 first = layout.GetNodeWorldPosition(edge.A);
            Vector3 last = layout.GetNodeWorldPosition(edge.B);
            float length = Vector2.Distance(new Vector2(first.x, first.z), new Vector2(last.x, last.z));
            float halfRoad = layout.RoadWidth * .5f;
            const float connectionOverlap = .8f;
            if (busIntersections.Contains(edge.A) && startInset > halfRoad)
                AddApron(halfRoad, startInset + connectionOverlap);
            if (busIntersections.Contains(edge.B) && endInset > halfRoad)
                AddApron(length - endInset - connectionOverlap, length - halfRoad);

            void AddApron(float from, float to)
            {
                Vector3 a = Vector3.Lerp(first, last, from / length) + sideOffset;
                Vector3 b = Vector3.Lerp(first, last, to / length) + sideOffset;
                a.y = layout.ElevationPlan.SampleRoadDatum(edge, from / length) + RoadTop;
                b.y = layout.ElevationPlan.SampleRoadDatum(edge, to / length) + RoadTop;
                AddSidewalk(CreateSurfaceBox(a, b, width, PublicPavementThickness), sidewalks, geometry, walkable);
            }
        }

        private static void AddApproachIfLongEnough(
            Vector3 first,
            Vector3 second,
            float width,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            if (Vector3.Distance(first, second) <= GeometryTolerance)
            {
                return;
            }

            AddSidewalk(
                CreateSurfaceBox(
                    first,
                    second,
                    width,
                    SidewalkHeight),
                sidewalks,
                sidewalkGeometry,
                walkableRectangles);
        }

        private static void CreateIntersectionSidewalks(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ISet<Vector2Int> busIntersections,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles,
            ICollection<Rect> markingExclusions,
            PublicPavementContext publicPavement)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            float carriagewayWidth =
                layout.RoadWidth - (SidewalkWidth * 2f);
            float halfCarriageway = carriagewayWidth * 0.5f;
            float sideOffset =
                halfCarriageway + (SidewalkWidth * 0.5f);
            List<Vector2Int> nodes = CreateSortedNodes(layout);
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector2Int node = nodes[index];
                NodeConnections nodeConnections = connections[node];
                if (layout.RoadGeometry.ObliqueJunction?.Node == node)
                {
                    foreach (Vector2[] polygon in layout.RoadGeometry.ObliqueJunction.RoadPolygons)
                        markingExclusions.Add(CityRoadPolygon.Bounds(polygon));
                    continue;
                }
                if (!nodeConnections.IsIntersectionCore)
                {
                    continue;
                }

                Vector3 nodePosition = layout.GetNodeWorldPosition(node);
                markingExclusions.Add(Rect.MinMaxRect(
                    nodePosition.x - halfRoad,
                    nodePosition.z - halfRoad,
                    nodePosition.x + halfRoad,
                    nodePosition.z + halfRoad));
                if (nodeConnections.StreetCount == 0)
                {
                    continue;
                }

                float cornerOffset = busIntersections.Contains(node)
                    ? CityBusIntersectionSelector
                        .GetCornerCenterOffset(layout)
                    : sideOffset;

                for (int xSign = -1; xSign <= 1; xSign += 2)
                {
                    for (int zSign = -1; zSign <= 1; zSign += 2)
                    {
                        float cornerWidth = publicPavement.CornerWidth(node, xSign, zSign, cornerOffset);
                        float expandedOffset = cornerOffset + (cornerWidth - SidewalkWidth) * .5f;
                        float thickness = PavementThickness(cornerWidth);
                        Vector3 center = nodePosition + new Vector3(
                            xSign * expandedOffset,
                            SidewalkTop - thickness * .5f,
                            zSign * expandedOffset);
                        AddSidewalk(
                            new Bounds(
                                center,
                                new Vector3(
                                    cornerWidth,
                                    thickness,
                                    cornerWidth)),
                            sidewalks,
                            sidewalkGeometry,
                            walkableRectangles);
                    }
                }

                AddClosedIntersectionMouths(
                    nodePosition,
                    nodeConnections,
                    cornerOffset,
                    sidewalks,
                    sidewalkGeometry,
                    walkableRectangles, node, publicPavement);
            }
        }

        private static void AddClosedIntersectionMouths(
            Vector3 nodePosition,
            NodeConnections connections,
            float cornerOffset,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles,
            Vector2Int node, PublicPavementContext publicPavement)
        {
            float mouthSpan = (cornerOffset * 2f) - SidewalkWidth;
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Vector2Int direction = directions[index];
                if (connections.Contains(direction))
                {
                    continue;
                }

                float width = direction.x != 0
                    ? Mathf.Min(publicPavement.CornerWidth(node, direction.x, -1, cornerOffset),
                        publicPavement.CornerWidth(node, direction.x, 1, cornerOffset))
                    : Mathf.Min(publicPavement.CornerWidth(node, -1, direction.y, cornerOffset),
                        publicPavement.CornerWidth(node, 1, direction.y, cornerOffset));
                float offset = cornerOffset + (width - SidewalkWidth) * .5f;
                float thickness = PavementThickness(width);
                Vector3 center = nodePosition + new Vector3(
                    direction.x * offset,
                    SidewalkTop - thickness * .5f,
                    direction.y * offset);
                Vector3 size = direction.x != 0
                    ? new Vector3(
                        width,
                        thickness,
                        mouthSpan)
                    : new Vector3(
                        mouthSpan,
                        thickness,
                        width);
                AddSidewalk(
                    new Bounds(center, size),
                    sidewalks,
                    sidewalkGeometry,
                    walkableRectangles);
            }
        }

        private static void CreateCrosswalks(
            CityLayout layout,
            IReadOnlyList<Vector2Int> selectedNodes,
            IReadOnlyList<RoadEdge> sortedEdges,
            ISet<RoadEdge> edgesWithSidewalks,
            float carriagewayWidth,
            ICollection<Bounds> crosswalkMarkings,
            ICollection<RuntimeOrientedBox> crosswalkMarkingGeometry,
            ICollection<Rect> walkableRectangles,
            ICollection<CityCrosswalkDescriptor> crosswalks,
            ICollection<Rect> markingExclusions)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            Dictionary<Vector2Int, List<RoadEdge>> approachesByNode =
                IndexCrosswalkApproaches(
                    layout,
                    sortedEdges,
                    edgesWithSidewalks);
            for (int nodeIndex = 0;
                 nodeIndex < selectedNodes.Count;
                 nodeIndex++)
            {
                Vector2Int node = selectedNodes[nodeIndex];
                if (!approachesByNode.TryGetValue(
                        node,
                        out List<RoadEdge> approaches))
                {
                    continue;
                }

                Vector3 nodePosition = layout.GetNodeWorldPosition(node);
                for (int edgeIndex = 0;
                     edgeIndex < approaches.Count;
                     edgeIndex++)
                {
                    RoadEdge edge = approaches[edgeIndex];
                    Vector2Int other = edge.Other(node);
                    Vector3 otherPosition =
                        layout.GetNodeWorldPosition(other);
                    Vector3 outward = otherPosition - nodePosition;
                    outward.y = 0f;
                    outward.Normalize();
                    Vector3 crosswalkCenter = nodePosition +
                        (outward *
                         (halfRoad + (CrosswalkDepth * 0.5f)));
                    TrySetRoadDatum(layout, ref crosswalkCenter);
                    Rect walkable = CreateOrientedRect(
                        crosswalkCenter,
                        outward,
                        CrosswalkDepth,
                        carriagewayWidth);
                    walkableRectangles.Add(walkable);
                    crosswalks.Add(new CityCrosswalkDescriptor(
                        node,
                        edge,
                        walkable,
                        crosswalkCenter,
                        outward,
                        new Vector3(-outward.z, 0f, outward.x)));
                    markingExclusions.Add(walkable);

                    for (int stripeIndex = 0;
                         stripeIndex < CrosswalkStripeCount;
                         stripeIndex++)
                    {
                        float distance = halfRoad +
                            (((stripeIndex + 0.5f) /
                              CrosswalkStripeCount) * CrosswalkDepth);
                        Vector3 center = nodePosition +
                            (outward * distance);
                        TrySetRoadDatum(layout, ref center);
                        center.y += MarkingCenterAboveRoadBase;
                        Vector3 size = Mathf.Abs(outward.x) > 0.5f
                            ? new Vector3(
                                CrosswalkStripeDepth,
                                MarkingHeight,
                                carriagewayWidth)
                            : new Vector3(
                                carriagewayWidth,
                                MarkingHeight,
                                CrosswalkStripeDepth);
                        var marking = new Bounds(center, size);
                        crosswalkMarkings.Add(marking);
                        crosswalkMarkingGeometry.Add(
                            new RuntimeOrientedBox(
                                marking.center,
                                Quaternion.identity,
                                marking.size));
                    }
                }
            }
        }

        /// <summary>
        /// The street edges with pavements, listed under each of their two
        /// nodes in sorted-edge order. A crossing node reads its list
        /// instead of filtering the whole sorted edge list; the list is
        /// that filter's own subsequence, so the crossings come out in the
        /// same order.
        /// </summary>
        private static Dictionary<Vector2Int, List<RoadEdge>>
            IndexCrosswalkApproaches(
                CityLayout layout,
                IReadOnlyList<RoadEdge> sortedEdges,
                ISet<RoadEdge> edgesWithSidewalks)
        {
            var result = new Dictionary<Vector2Int, List<RoadEdge>>();
            for (int edgeIndex = 0;
                 edgeIndex < sortedEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = sortedEdges[edgeIndex];
                if (layout.GetPathKind(edge) != CityPathKind.Street ||
                    !edgesWithSidewalks.Contains(edge))
                {
                    continue;
                }

                AddApproach(result, edge.A, edge);
                AddApproach(result, edge.B, edge);
            }

            return result;
        }

        private static void AddApproach(
            IDictionary<Vector2Int, List<RoadEdge>> approachesByNode,
            Vector2Int node,
            RoadEdge edge)
        {
            if (!approachesByNode.TryGetValue(
                    node,
                    out List<RoadEdge> approaches))
            {
                approaches = new List<RoadEdge>(4);
                approachesByNode.Add(node, approaches);
            }

            approaches.Add(edge);
        }

        private static void CreateCenterMarkings(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            IReadOnlyList<Rect> markingExclusions,
            ICollection<Bounds> centerMarkings,
            ICollection<RuntimeOrientedBox> centerMarkingGeometry)
        {
            for (int edgeIndex = 0;
                 edgeIndex < sortedEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = sortedEdges[edgeIndex];
                if (layout.RoadGeometry.IsCurved(edge)) continue;
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                float planarLength = new Vector2(
                    end.x - start.x,
                    end.z - start.z).magnitude;
                float length = Vector3.Distance(start, end);
                int dashCount = Mathf.Max(
                    2,
                    Mathf.FloorToInt(length / 5f));
                for (int dashIndex = 0;
                     dashIndex < dashCount;
                     dashIndex++)
                {
                    float t = (dashIndex + 0.5f) / dashCount;
                    float dashLength = Mathf.Min(
                        MaximumCenterDashLength,
                        (length / dashCount) * 0.48f);
                    float halfAmount = planarLength > GeometryTolerance
                        ? dashLength * 0.5f / planarLength
                        : 0f;
                    Vector3 dashStart = Vector3.Lerp(
                        start,
                        end,
                        Mathf.Clamp01(t - halfAmount));
                    Vector3 dashEnd = Vector3.Lerp(
                        start,
                        end,
                        Mathf.Clamp01(t + halfAmount));
                    dashStart.y = layout.ElevationPlan.SampleRoadDatum(
                        edge,
                        Mathf.Clamp01(t - halfAmount));
                    dashEnd.y = layout.ElevationPlan.SampleRoadDatum(
                        edge,
                        Mathf.Clamp01(t + halfAmount));
                    float markingTop =
                        MarkingCenterAboveRoadBase +
                        MarkingHeight * 0.5f;
                    RuntimeOrientedBox geometry = CreateSurfaceBox(
                        dashStart + Vector3.up * markingTop,
                        dashEnd + Vector3.up * markingTop,
                        CenterDashWidth,
                        MarkingHeight);
                    Bounds dash = CalculateBounds(geometry);
                    if (!OverlapsAny(
                            CreateRect(dash),
                            markingExclusions))
                    {
                        centerMarkings.Add(dash);
                        centerMarkingGeometry.Add(geometry);
                    }
                }
            }
        }

        private static void CreateCurvedRibbons(CityLayout layout,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ISet<Vector2Int> busIntersections, IReadOnlyList<Rect> exclusions,
            ICollection<CityStreetRibbonDescriptor> streets,
            ICollection<CityStreetRibbonDescriptor> sidewalks,
            ICollection<CityStreetRibbonDescriptor> markings, bool includeMarkings,
            PublicPavementContext publicPavement)
        {
            float halfRoad = layout.RoadWidth * .5f;
            foreach (RoadEdge edge in layout.RoadGeometry.CurvedEdges)
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                float roadStart = layout.RoadGeometry.GetEndpointInset(edge, edge.A);
                float roadEnd = layout.RoadGeometry.GetEndpointInset(edge, edge.B);
                streets.Add(new CityStreetRibbonDescriptor(edge,
                    SlicePath(path, roadStart, path.Length - roadEnd).Ribbon(layout.RoadWidth), RoadTop, RoadSurfaceHeight));
                float startInset = layout.RoadGeometry.ObliqueJunction?.Node == edge.A ? roadStart :
                    ResolveEndpointInset(connections[edge.A], halfRoad) +
                    (busIntersections.Contains(edge.A) ? BusApproachApronLength : 0f);
                float endInset = layout.RoadGeometry.ObliqueJunction?.Node == edge.B ? roadEnd :
                    ResolveEndpointInset(connections[edge.B], halfRoad) +
                    (busIntersections.Contains(edge.B) ? BusApproachApronLength : 0f);
                CityRoadPath pavementPath = SlicePath(path, startInset, path.Length - endInset);
                for (int side = -1; side <= 1; side += 2)
                {
                    float width = publicPavement.Width(edge, side);
                    sidewalks.Add(new CityStreetRibbonDescriptor(edge,
                        pavementPath.Ribbon(width, side * (halfRoad - SidewalkWidth + width * .5f)),
                        SidewalkTop, PavementThickness(width)));
                    if (width > SidewalkWidth)
                    {
                        // Turning space stays flush: the wider walking route
                        // continues in paving without rebuilding a raised kerb
                        // across the bus's swept envelope.
                        if (busIntersections.Contains(edge.A))
                            sidewalks.Add(new CityStreetRibbonDescriptor(edge,
                                SlicePath(path, halfRoad, startInset + .15f).Ribbon(width,
                                    side * (halfRoad - SidewalkWidth + width * .5f)), RoadTop, PublicPavementThickness));
                        if (busIntersections.Contains(edge.B))
                            sidewalks.Add(new CityStreetRibbonDescriptor(edge,
                                SlicePath(path, path.Length - endInset - .15f, path.Length - halfRoad).Ribbon(width,
                                    side * (halfRoad - SidewalkWidth + width * .5f)), RoadTop, PublicPavementThickness));
                    }
                }
                if (!includeMarkings) continue;
                int dashCount = Mathf.Max(2, Mathf.FloorToInt(path.Length / 5f));
                for (int dash = 0; dash < dashCount; dash++)
                {
                    float distance = (dash + .5f) * path.Length / dashCount;
                    float halfLength = Mathf.Min(MaximumCenterDashLength, path.Length / dashCount * .48f) * .5f;
                    CityRoadPath marking = SlicePath(path, distance - halfLength, distance + halfLength);
                    bool excluded = false;
                    foreach (Rect exclusion in exclusions)
                        if (marking.Bounds.Overlaps(exclusion)) { excluded = true; break; }
                    if (!excluded) markings.Add(new CityStreetRibbonDescriptor(edge,
                        marking.Ribbon(CenterDashWidth), MarkingCenterAboveRoadBase + MarkingHeight * .5f,
                        MarkingHeight));
                }
            }
            CityRoadJunction junction = layout.RoadGeometry.ObliqueJunction;
            if (junction != null)
            {
                streets.Add(new CityStreetRibbonDescriptor(junction.OwnerEdge, junction.RoadPolygons,
                    RoadTop, RoadSurfaceHeight, junction.Node));
                sidewalks.Add(new CityStreetRibbonDescriptor(junction.OwnerEdge, junction.SidewalkPolygons,
                    SidewalkTop, SidewalkHeight, junction.Node));
            }
        }

        private static CityRoadPath SlicePath(CityRoadPath path, float start, float end)
        {
            CityRoadSample first = path.SampleDistance(start), last = path.SampleDistance(end);
            var vertices = new List<Vector2> {
                first.Position + first.Tangent * Mathf.Min(0f, start)
            };
            foreach (Vector2 vertex in path.Vertices)
            {
                float distance = path.Project(vertex).DistanceAlong;
                if (distance > start + .001f && distance < end - .001f) vertices.Add(vertex);
            }
            Vector2 finish = last.Position + last.Tangent * Mathf.Max(0f, end - path.Length);
            if ((finish - vertices[vertices.Count - 1]).sqrMagnitude > .000001f) vertices.Add(finish);
            return new CityRoadPath(vertices);
        }

        private static void AddSidewalk(
            Bounds sidewalk,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            sidewalks.Add(sidewalk);
            sidewalkGeometry.Add(new RuntimeOrientedBox(
                sidewalk.center,
                Quaternion.identity,
                sidewalk.size));
            walkableRectangles.Add(CreateRect(sidewalk));
        }

        private static void AddSidewalk(
            RuntimeOrientedBox sidewalk,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            Bounds bounds = CalculateBounds(sidewalk);
            sidewalks.Add(bounds);
            sidewalkGeometry.Add(sidewalk);
            walkableRectangles.Add(CreateRect(bounds));
        }

        private static RuntimeOrientedBox CreateSurfaceBox(
            Vector3 topStart,
            Vector3 topEnd,
            float width,
            float thickness)
        {
            Vector3 slope = topEnd - topStart;
            if (slope.sqrMagnitude <= GeometryTolerance)
            {
                throw new InvalidOperationException(
                    "A graded surface requires a positive run length.");
            }

            Quaternion rotation = Quaternion.LookRotation(
                slope.normalized,
                Vector3.up);
            Vector3 normal = rotation * Vector3.up;
            return new RuntimeOrientedBox(
                (topStart + topEnd) * 0.5f -
                normal * (thickness * 0.5f),
                rotation,
                new Vector3(width, thickness, slope.magnitude));
        }

        private static void TrySetRoadDatum(
            CityLayout layout,
            ref Vector3 position)
        {
            if (layout.ElevationPlan.TrySampleSurface(
                    new Vector2(position.x, position.z),
                    CitySurfaceRole.RoadDatum,
                    out float height,
                    out _))
            {
                position.y = height;
            }
        }

        private static Bounds CalculateBounds(RuntimeOrientedBox box)
        {
            Vector3 half = box.Size * 0.5f;
            Vector3 right = box.Rotation * Vector3.right;
            Vector3 up = box.Rotation * Vector3.up;
            Vector3 forward = box.Rotation * Vector3.forward;
            Vector3 extents = Abs(right) * half.x +
                              Abs(up) * half.y +
                              Abs(forward) * half.z;
            return new Bounds(box.Center, extents * 2f);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static Rect CreateOrientedRect(
            Vector3 center,
            Vector3 direction,
            float alongLength,
            float acrossLength)
        {
            return Mathf.Abs(direction.x) > 0.5f
                ? new Rect(
                    center.x - (alongLength * 0.5f),
                    center.z - (acrossLength * 0.5f),
                    alongLength,
                    acrossLength)
                : new Rect(
                    center.x - (acrossLength * 0.5f),
                    center.z - (alongLength * 0.5f),
                    acrossLength,
                    alongLength);
        }

        private static Rect CreateRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static float ResolveEndpointInset(
            NodeConnections connections,
            float halfRoad)
        {
            if (connections.IsIntersectionCore)
            {
                return halfRoad;
            }

            return connections.Count == 1 ? -halfRoad : 0f;
        }

        private static bool OverlapsAny(
            Rect rectangle,
            IReadOnlyList<Rect> others)
        {
            for (int index = 0; index < others.Count; index++)
            {
                Rect other = others[index];
                if (Mathf.Min(rectangle.xMax, other.xMax) -
                        Mathf.Max(rectangle.xMin, other.xMin) >
                    GeometryTolerance &&
                    Mathf.Min(rectangle.yMax, other.yMax) -
                        Mathf.Max(rectangle.yMin, other.yMin) >
                    GeometryTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<Vector2Int, NodeConnections>
            CreateNodeConnections(CityLayout layout)
        {
            var connections =
                new Dictionary<Vector2Int, NodeConnections>(
                    layout.Nodes.Count);
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                connections.Add(
                    layout.Nodes[index],
                    new NodeConnections());
            }

            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                bool isStreet =
                    layout.GetPathKind(edge) == CityPathKind.Street;
                connections[edge.A].Add(edge.B - edge.A, isStreet);
                connections[edge.B].Add(edge.A - edge.B, isStreet);
            }

            return connections;
        }

        private static List<RoadEdge> CreateSortedEdges(CityLayout layout)
        {
            var edges = new List<RoadEdge>(layout.RoadEdges);
            edges.Sort(RoadEdge.Compare);
            return edges;
        }

        private static List<Vector2Int> CreateSortedNodes(CityLayout layout)
        {
            var nodes = new List<Vector2Int>(layout.Nodes);
            nodes.Sort(CompareNodes);
            return nodes;
        }

        private static int CompareNodes(Vector2Int left, Vector2Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0
                ? xComparison
                : left.y.CompareTo(right.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static PublicPavementContext GetPublicPavement(CityLayout layout) =>
            PublicPavements.GetValue(layout, value => new PublicPavementContext(value));

        private sealed class PublicPavementContext
        {
            private readonly CityLayout layout;
            private readonly HashSet<(RoadEdge edge, int rightSign)> expanded =
                new HashSet<(RoadEdge, int)>();
            private readonly HashSet<(Vector2Int node, Vector2Int cell)> protectedCorners =
                new HashSet<(Vector2Int, Vector2Int)>();

            internal PublicPavementContext(CityLayout source)
            {
                layout = source;
                if (!source.SpatialPlan.IsCompactCoastal) return;
                foreach (RoadEdge edge in source.RoadEdges)
                {
                    if (source.GetPathKind(edge) != CityPathKind.Street) continue;
                    bool reserved = source.TryGetBridge(edge, out _) ||
                        source.ElevationPlan.TryGetSignatureStair(edge, out _);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector2Int cell = edge.IsHorizontal
                            ? edge.A + (sign > 0 ? Vector2Int.down : Vector2Int.zero)
                            : edge.A + (sign < 0 ? Vector2Int.left : Vector2Int.zero);
                        // Existing retaining rails and park hedges keep their
                        // physical line. Widen only the reserved urban frontage;
                        // stairs, bridges and protected drops own their landings.
                        if (!IsUrban(cell)) continue;
                        if (!reserved && HasLevelFrontage(edge, cell)) expanded.Add((edge, sign));
                        else
                        {
                            protectedCorners.Add((edge.A, cell));
                            protectedCorners.Add((edge.B, cell));
                        }
                    }
                }
            }

            internal float Width(RoadEdge edge, int rightSign) =>
                expanded.Contains((edge, rightSign)) ? PublicSidewalkWidth : SidewalkWidth;

            private bool HasLevelFrontage(RoadEdge edge, Vector2Int cell)
            {
                // This early paving decision must not instantiate terrain,
                // cannery routing or courtyard plans. Their final compositor
                // consumes paving later. Retain narrow protected boundaries
                // unless the whole urban frontage has a safe datum step.
                float ground = layout.ElevationPlan.GetCellElevation(cell) + CityElevationPlan.GroundTopOffset;
                for (int sample = 0; sample <= 8; sample++)
                    if (Mathf.Abs(ground - layout.ElevationPlan.SampleRoadDatum(edge, sample / 8f) - SidewalkTop) >
                        CityRoadGroundBoundaryPlanner.MaximumSafeStep) return false;
                return true;
            }

            internal float CornerWidth(Vector2Int node, int xSign, int zSign, float oldOffset)
            {
                if (!layout.SpatialPlan.IsCompactCoastal) return SidewalkWidth;
                var cell = new Vector2Int(node.x + (xSign < 0 ? -1 : 0),
                    node.y + (zSign < 0 ? -1 : 0));
                if (!IsUrban(cell) || protectedCorners.Contains((node, cell))) return SidewalkWidth;
                Vector3 center = layout.GetNodeWorldPosition(node);
                float offset = oldOffset + (PublicSidewalkWidth - SidewalkWidth) * .5f;
                var corner = new Rect(center.x + xSign * offset - PublicSidewalkWidth * .5f,
                    center.z + zSign * offset - PublicSidewalkWidth * .5f,
                    PublicSidewalkWidth, PublicSidewalkWidth);
                // Rear wings are allocated after street paving. Primary lots
                // own the frontage reserve; reading BuildingMasses here would
                // recurse through courtyard allocation and paving again.
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (!lot.HasBuilding) continue;
                    Bounds bounds = lot.WorldBounds;
                    Rect solid = Rect.MinMaxRect(bounds.min.x - .1f, bounds.min.z - .1f,
                        bounds.max.x + .1f, bounds.max.z + .1f);
                    if (corner.Overlaps(solid)) return SidewalkWidth;
                }
                return PublicSidewalkWidth;
            }

            private bool IsUrban(Vector2Int cell) =>
                layout.Blueprint.TryGetArea(cell, out CityAreaDefinition area) && area.SupportsOrdinaryBuildings;
        }

        private sealed class NodeConnections
        {
            private readonly List<Vector2Int> directions =
                new List<Vector2Int>(4);

            public int Count => directions.Count;
            public int StreetCount { get; private set; }

            public bool IsIntersectionCore
            {
                get
                {
                    if (directions.Count >= 3)
                    {
                        return true;
                    }

                    return directions.Count == 2 &&
                           directions[0] + directions[1] !=
                           Vector2Int.zero;
                }
            }

            public void Add(Vector2Int direction, bool isStreet)
            {
                directions.Add(direction);
                if (isStreet)
                {
                    StreetCount++;
                }
            }

            public bool Contains(Vector2Int direction)
            {
                return directions.Contains(direction);
            }
        }
    }
}
