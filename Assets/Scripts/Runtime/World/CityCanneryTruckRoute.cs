using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCanneryTruckLeg
    { PortArrive, PortReverse, PortToFactory, FactoryReverse, FactoryToShop, ShopToFactory, FactoryToPort }

    /// <summary>Right-lane rear-axle paths over the existing street graph.
    /// Turns use the shared paved intersection aprons; factory/port maneuvers
    /// join their original parking poses continuously.</summary>
    public sealed class CityCanneryTruckRoute
    {
        private const float Radius = CityCanneryPlan.TurningRadius + .25f;
        private const float RightTurnRadius = CityBusPlanner.RightTurnRadius;
        private const float Step = .25f;
        private readonly CityLayout layout;
        private readonly CityCanneryPlan site;
        private readonly CityPortAccessPlan port;
        private readonly List<CityPortTruckPose>[] poses = new List<CityPortTruckPose>[7];
        private readonly float[][] distances = new float[7][];
        private readonly HashSet<RoadEdge> busRoads;
        private readonly HashSet<Vector2Int> turnNodes;
        private readonly List<Rect> paving = new List<Rect>();
        private readonly List<Rect> streetFurniture = new List<Rect>();
        private readonly Dictionary<(Vector2Int node,Vector2Int incoming,Vector2Int outgoing,bool centered),bool>
            turnClearance = new Dictionary<(Vector2Int,Vector2Int,Vector2Int,bool),bool>();
        private readonly HashSet<RoadEdge> streetEdges = new HashSet<RoadEdge>();
        // Street edges by endpoint, in layout order, so the search visits a
        // node's few neighbours instead of filtering the whole road graph per
        // expanded state. Equal-cost ties resolve exactly as the full scan did.
        private readonly Dictionary<Vector2Int,List<(Vector2Int node,RoadEdge edge)>> streetNeighbours =
            new Dictionary<Vector2Int,List<(Vector2Int,RoadEdge)>>();
        // The road profile lookup walks every edge of the city. Grounding the
        // axles re-asks the rear point each pose was placed at; same XZ, same answer.
        private readonly Dictionary<Vector2,float> streetHeights = new Dictionary<Vector2,float>();
        public IReadOnlyCollection<RoadEdge> StreetEdges => streetEdges;
        public int SharedBusStreetCount { get; private set; }
        public float LaneCenterOffset { get; }
        public CityPortTruckPose ShopPose { get; private set; }
        public Vector3 ShopDoorPoint { get; private set; }
        public Vector3 ShopDropPoint { get; private set; }
        public CityPortTruckPose PortLoadingPose => Sample(CityCanneryTruckLeg.PortReverse, 1f);
        public CityPortTruckPose FactoryWaitingPose => Sample(CityCanneryTruckLeg.FactoryReverse, 1f);
        /// <summary>The first truck is already on its way when the dock event
        /// starts. Only that first arrival uses a suffix of the ordinary road
        /// route; every following trip starts from the factory parking pose.</summary>
        public double InitialFactoryToPortDuration => CityFishSupplyCycle.FirstPortCrateStoredAtSeconds -
            Math.Max(2d, Length(CityCanneryTruckLeg.PortArrive) / 3d) -
            Math.Max(2d, Length(CityCanneryTruckLeg.PortReverse) / 1.1d);
        public float InitialFactoryToPortStartProgress => Mathf.Clamp01(1f -
            (float)(InitialFactoryToPortDuration * 3d) / Length(CityCanneryTruckLeg.FactoryToPort));
        public CityPortTruckPose InitialArrivalPose => SampleInitialFactoryToPort(0f);
        public CityPortTruckPose SampleInitialFactoryToPort(float progress) =>
            Sample(CityCanneryTruckLeg.FactoryToPort,
                Mathf.Lerp(InitialFactoryToPortStartProgress, 1f, Mathf.Clamp01(progress)));
        public float MaximumGrade { get; private set; }
        private CityCanneryTruckLeg maximumGradeLeg;
        private int maximumGradePose;

        private CityCanneryTruckRoute(CityLayout layout, CityCanneryPlan site, CityPortAccessPlan port)
        {
            this.layout = layout; this.site = site; this.port = port;
            CityBusPlan busPlan = layout.BlueprintId == CityBlueprintCatalog.DefaultBlueprintId
                ? CityBusPlanner.CreateRoadRouting(layout) : null;
            busRoads = CityBusPlanner.ServiceRoadEdges(busPlan);
            if (busPlan != null)
                foreach (CityBusStopDescriptor stop in busPlan.Stops)
                    streetFurniture.Add(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop));
            foreach (CityElevationStairDescriptor stair in layout.ElevationPlan.SignatureStairs)
            {
                CityElevationStairPlacement placement = CityElevationStairPlacementPlanner.Create(layout,stair);
                foreach (CityExteriorStairRailDescriptor rail in placement.ExteriorPlan.Rails) AddRailFootprint(rail);
                AddRailFootprint(placement.LowerInnerRail);
                AddRailFootprint(placement.UpperInnerRail);
            }
            LaneCenterOffset = Mathf.Max(0f, (layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth) * .25f);
            turnNodes = new HashSet<Vector2Int>(CityBusIntersectionSelector.Select(layout));
            CreatePaving();
            CreateStreetNeighbours();
            for (int i=0; i<poses.Length; i++) poses[i] = new List<CityPortTruckPose>();
            // The old reserved pose left only .4 m behind the truck. Keep 3.2 m
            // at the cold store for the 2.5 m lift, jack and standing operator.
            const float tailLiftClearance = 2.8f;
            float reverseEnd = 1f - tailLiftClearance / port.TruckRouteLength(CityPortTruckLeg.ReverseToStore);
            AddPort(poses[0], CityPortTruckLeg.Arrive, 0f, 1f);
            AddPort(poses[1], CityPortTruckLeg.ReverseToStore, 0f, reverseEnd);
            AddPort(poses[2], CityPortTruckLeg.Leave,
                tailLiftClearance / port.TruckRouteLength(CityPortTruckLeg.Leave), 1f);
            CityPortTruckPose portOut = poses[2][poses[2].Count-1];
            CityPortTruckPose reverseStart = new CityPortTruckPose(site.ReverseStart,
                site.Rotation * Quaternion.LookRotation(Vector3.left), true);
            AddStreet(poses[2], portOut, port.StreetEdge, reverseStart, site.FrontageEdge);
            AddFactoryReverse(poses[3]);
            BuildingLot shop = layout.Supermarket;
            if (shop == null || !shop.HasRoadFrontage)
                throw new InvalidOperationException("The cannery delivery needs the existing supermarket frontage.");
            Vector2Int shopSide = -shop.FrontageDirection;
            RoadEdge shopEdge = RoadEdge.ForCellFrontage(shop.Cell,shopSide);
            if (!layout.HasRoad(shopEdge) || layout.GetPathKind(shopEdge)!=CityPathKind.Street)
                throw new InvalidOperationException("Supermarket rear service door needs an existing rear street for cannery deliveries.");
            Quaternion shopRotation=Quaternion.LookRotation(new Vector3(shop.FrontageDirection.x,0,shop.FrontageDirection.y));
            // These fixed-metre coordinates belong to the existing authored
            // Closed Rear Service Door, not the customer storefront.
            Vector3 modelOrigin=shop.DoorPosition-shopRotation*new Vector3(0,0,7.75f);
            ShopDoorPoint=modelOrigin+shopRotation*new Vector3(2.8f,.08f,-7.715f);
            ShopDropPoint=modelOrigin+shopRotation*new Vector3(2.8f,.08f,-8.45f);
            Vector3 shopForward=shopRotation*Vector3.right;
            Vector3 shopPoint=modelOrigin+shopRotation*new Vector3(5.4f,0,-13f);
            shopPoint += Right(shopForward)*LaneCenterOffset;
            shopPoint.y = StreetHeight(shopPoint);
            ShopPose = new CityPortTruckPose(shopPoint,Quaternion.LookRotation(shopForward),false);
            AddFactoryExit(poses[4]);
            AddStreet(poses[4], poses[4][poses[4].Count-1], site.FrontageEdge, ShopPose, shopEdge);
            AddStreet(poses[5], ShopPose, shopEdge, reverseStart, site.FrontageEdge);
            CityPortTruckPose portIn = port.SampleTruck(CityPortTruckLeg.Arrive,0f);
            AddFactoryExit(poses[6]);
            AddStreet(poses[6], poses[6][poses[6].Count-1], site.FrontageEdge, portIn, port.StreetEdge);
            foreach (RoadEdge edge in streetEdges)
                if (busRoads.Contains(edge)) SharedBusStreetCount++;
            for(int leg=0;leg<poses.Length;leg++)
            for(int i=0;i<poses[leg].Count;i++)
                poses[leg][i]=GroundAxles(poses[leg][i]);
            // Both lookups only serve construction; the poses are final now.
            streetHeights.Clear();
            streetNeighbours.Clear();
            ShopPose=poses[4][poses[4].Count-1];
            for (int leg=0;leg<poses.Length;leg++)
            {
                distances[leg]=new float[poses[leg].Count];
                for(int i=1;i<poses[leg].Count;i++)
                {
                    Vector3 d=poses[leg][i].RearAxle-poses[leg][i-1].RearAxle;
                    distances[leg][i]=distances[leg][i-1]+d.magnitude;
                    float horizontal=new Vector2(d.x,d.z).magnitude;
                    float grade = horizontal > .001f ? Mathf.Abs(d.y) / horizontal :
                        Mathf.Abs(d.y) > .001f ? float.PositiveInfinity : 0f;
                    if (grade > MaximumGrade)
                    {
                        MaximumGrade = grade;
                        maximumGradeLeg = (CityCanneryTruckLeg)leg;
                        maximumGradePose = i;
                    }
                }
            }
        }

        public static CityCanneryTruckRoute Create(CityLayout layout, CityCanneryPlan site, CityPortAccessPlan port) =>
            layout == null || site == null || port == null ? null : new CityCanneryTruckRoute(layout,site,port);
        public float Length(CityCanneryTruckLeg leg) => distances[(int)leg][distances[(int)leg].Length-1];
        public CityPortTruckPose Sample(CityCanneryTruckLeg leg,float progress)
        {
            int l=(int)leg;
            float distance=Mathf.Clamp01(progress)*Length(leg);
            int i=Array.BinarySearch(distances[l],distance);
            if(i<0)i=~i;
            i=Mathf.Clamp(i,1,distances[l].Length-1);
            float t=Mathf.InverseLerp(distances[l][i-1],distances[l][i],distance);
            CityPortTruckPose a=poses[l][i-1], b=poses[l][i];
            return new CityPortTruckPose(Vector3.Lerp(a.RearAxle,b.RearAxle,t),
                Quaternion.Slerp(a.Rotation,b.Rotation,t),b.Reversing);
        }

        private void AddPort(List<CityPortTruckPose> target,CityPortTruckLeg leg,float from,float to)
        {
            int count=Mathf.CeilToInt(port.TruckRouteLength(leg)*(to-from)/Step);
            for(int i=0;i<=count;i++)Add(target,port.SampleTruck(leg,Mathf.Lerp(from,to,i/(float)count)));
        }
        private void AddFactoryReverse(List<CityPortTruckPose> target)
        {
            const float yardRadius=Radius;
            for(int i=0;i<=64;i++)
            {
                float a=Mathf.PI*.5f*i/64f;
                Vector3 p=new Vector3(-1.25f+yardRadius*Mathf.Sin(a),CityCanneryPlan.YardTop,6.75f+yardRadius*Mathf.Cos(a));
                Vector3 world=site.World(p);
                if(p.z>10f)world.y=StreetHeight(world);
                else world.y=site.Origin.y+CityCanneryPlan.YardTop;
                Vector3 forward=site.Rotation*new Vector3(-Mathf.Cos(a),0,Mathf.Sin(a));
                Add(target,new CityPortTruckPose(world,Quaternion.LookRotation(forward),true));
            }
            AddStraight(target,site.World(new Vector3(5,CityCanneryPlan.YardTop,6.75f)),
                site.TruckParkedRearAxle,site.Forward,true,false);
        }

        private void AddFactoryExit(List<CityPortTruckPose> target)
        {
            // Retrace the same yard curve forwards. Both departures and the
            // end-of-delivery reverse therefore meet the waiting pose exactly.
            List<CityPortTruckPose> reverse = poses[(int)CityCanneryTruckLeg.FactoryReverse];
            for (int i = reverse.Count - 1; i >= 0; i--)
            {
                CityPortTruckPose p = reverse[i];
                Add(target, new CityPortTruckPose(p.RearAxle, p.Rotation, false));
            }
        }

        private void AddStreet(List<CityPortTruckPose> target,CityPortTruckPose from,RoadEdge fromEdge,
            CityPortTruckPose to,RoadEdge toEdge)
        {
            Vector3 startForward=from.Rotation*Vector3.forward, endForward=to.Rotation*Vector3.forward;
            Vector2Int start=ForwardNode(fromEdge,from.RearAxle,startForward);
            Vector2Int goal=ForwardNode(toEdge,to.RearAxle,-endForward);
            Vector2Int incoming=Direction(startForward), outgoing=Direction(endForward);
            float startRoom = Vector3.Dot(layout.GetNodeWorldPosition(start)-from.RearAxle,startForward);
            float endRoom = Vector3.Dot(to.RearAxle-layout.GetNodeWorldPosition(goal),endForward);
            float startOffset = Vector3.Dot(from.RearAxle-layout.GetNodeWorldPosition(start),Right(startForward));
            float endOffset = Vector3.Dot(to.RearAxle-layout.GetNodeWorldPosition(goal),Right(endForward));
            bool factoryDeparture = fromEdge.Equals(site.FrontageEdge) &&
                Horizontal(from.RearAxle-site.ReverseStart).sqrMagnitude < .001f;
            List<Vector2Int> nodes=FindNodes(start,incoming,goal,outgoing,startRoom,endRoom,
                startOffset,endOffset,factoryDeparture);
            if (nodes == null)
                throw new InvalidOperationException($"No connected truck street route: from={from.RearAxle} on {fromEdge}, " +
                    $"to={to.RearAxle} on {toEdge}; start={start}/{incoming}, room={startRoom:F3}, lane={startOffset:F3}, " +
                    $"goal={goal}/{outgoing}, room={endRoom:F3}, lane={endOffset:F3}; " +
                    $"startApron={turnNodes.Contains(start)}, goalApron={turnNodes.Contains(goal)}, " +
                    $"factoryDeparture={factoryDeparture}, permittedAprons={turnNodes.Count}, " +
                    $"streetFixtures={streetFurniture.Count}, checkedTurns={turnClearance.Count}.");
            streetEdges.Add(fromEdge);
            streetEdges.Add(toEdge);
            for (int i=1;i<nodes.Count;i++) streetEdges.Add(new RoadEdge(nodes[i-1],nodes[i]));
            // The graph describes street centerlines. Project offset parking
            // anchors back onto that graph before choosing corner tangents;
            // physical lane anchors remain the endpoints of the smooth joins.
            var polyline=new List<Vector3>{from.RearAxle-Right(startForward)*startOffset};
            foreach(Vector2Int node in nodes)polyline.Add(layout.GetNodeWorldPosition(node));
            polyline.Add(to.RearAxle-Right(endForward)*endOffset);
            // Remove duplicate and collinear graph nodes before filleting.
            for(int i=polyline.Count-2;i>0;i--)
            {
                Vector3 before=Horizontal(polyline[i]-polyline[i-1]);
                Vector3 after=Horizontal(polyline[i+1]-polyline[i]);
                if(before.sqrMagnitude<.001f || after.sqrMagnitude<.001f || Vector3.Dot(before.normalized,after.normalized)>.9999f)
                    polyline.RemoveAt(i);
            }
            Vector3 cursor=from.RearAxle;
            for(int i=1;i<polyline.Count-1;i++)
            {
                Vector3 corner=polyline[i];
                Vector3 incomingDirection=Horizontal(corner-polyline[i-1]).normalized;
                Vector3 outgoingDirection=Horizontal(polyline[i+1]-corner).normalized;
                if(Vector3.Dot(incomingDirection,outgoingDirection)<-.01f)
                    throw new InvalidOperationException($"Cannery route U-turn outside a turning area at corner {i}: " +
                        $"{polyline[i-1]} -> {corner} -> {polyline[i+1]}; " +
                        $"from={from.RearAxle} on {fromEdge}, to={to.RearAxle} on {toEdge}.");
                bool rightTurn = Vector3.Dot(Right(incomingDirection), outgoingDirection) > .5f;
                // The factory's boundary corner has no exterior fourth pad.
                // Keep its existing clear centerline arc as the final part of
                // the yard departure, then merge into the right street lane.
                bool departureCorner = factoryDeparture && !turnNodes.Contains(start) &&
                    Horizontal(corner-layout.GetNodeWorldPosition(start)).sqrMagnitude < .001f;
                float lane = departureCorner ? 0f : LaneCenterOffset;
                float radius = rightTurn && !departureCorner ? RightTurnRadius : Radius;
                float tangent = radius + (rightTurn ? lane : -lane);
                Vector3 enter=corner-incomingDirection*tangent+Right(incomingDirection)*lane;
                Vector3 leave=corner+outgoingDirection*tangent+Right(outgoingDirection)*lane;
                if(Vector3.Dot(Horizontal(enter-cursor),incomingDirection)<-.02f ||
                   Horizontal(polyline[i+1]-corner).magnitude<tangent-.02f)
                    throw new InvalidOperationException($"Cannery street corner {i} lacks tangent room: " +
                        $"{polyline[i-1]} -> {corner} -> {polyline[i+1]}, cursor={cursor}, tangent={tangent:F3}.");
                AddStreetStraight(target,cursor,enter,incomingDirection);
                Vector3 center=enter+outgoingDirection*radius;
                Vector3 radial=(enter-center)/radius;
                for(int s=1;s<=48;s++)
                {
                    float angle=Mathf.PI*.5f*s/48f;
                    Vector3 p=center+radius*(radial*Mathf.Cos(angle)+incomingDirection*Mathf.Sin(angle));
                    p.y=StreetHeight(p);
                    Vector3 forward=-radial*Mathf.Sin(angle)+incomingDirection*Mathf.Cos(angle);
                    Add(target,new CityPortTruckPose(p,Quaternion.LookRotation(forward),false));
                }
                cursor=leave;
            }
            AddStreetStraight(target,cursor,to.RearAxle,endForward);
        }

        private List<Vector2Int> FindNodes(Vector2Int start,Vector2Int incoming,Vector2Int goal,Vector2Int outgoing,
            float startRoom,float endRoom,float startOffset,float endOffset,bool factoryDeparture)
        {
            var initial=(node:start,direction:incoming);
            var costs=new Dictionary<(Vector2Int node,Vector2Int direction),float>{{initial,0}};
            var previous=new Dictionary<(Vector2Int node,Vector2Int direction),(Vector2Int node,Vector2Int direction)>();
            var pending=new List<(Vector2Int node,Vector2Int direction)>{initial};
            var visited=new HashSet<(Vector2Int node,Vector2Int direction)>();
            while(pending.Count>0)
            {
                int best=0;
                for(int i=1;i<pending.Count;i++)if(costs[pending[i]]<costs[pending[best]])best=i;
                var state=pending[best]; pending.RemoveAt(best);
                if(!visited.Add(state))continue;
                if(state.node==goal && state.direction!=-outgoing &&
                    (state.direction==outgoing || turnNodes.Contains(state.node) &&
                        endRoom >= TurnTangent(state.direction,outgoing)+MinimumLaneJoinRun(LaneCenterOffset-endOffset) &&
                        TurnClearsFurniture(state.node,state.direction,outgoing,false)))
                {
                    var result=new List<Vector2Int>{state.node};
                    while(previous.TryGetValue(state,out var p)){state=p;result.Add(state.node);}
                    result.Reverse();return result;
                }
                if(!streetNeighbours.TryGetValue(state.node,out var neighbours))continue;
                foreach((Vector2Int next,RoadEdge edge) in neighbours)
                {
                    Vector2Int direction=next-state.node;
                    if(direction==-state.direction)continue;
                    bool departureCorner = state==initial && factoryDeparture && !turnNodes.Contains(state.node);
                    if(direction!=state.direction && !turnNodes.Contains(state.node) && !departureCorner)continue;
                    if(state==initial && direction!=state.direction && startRoom <
                        (departureCorner ? Radius+MinimumLaneJoinRun(-startOffset) :
                            TurnTangent(state.direction,direction)+MinimumLaneJoinRun(LaneCenterOffset-startOffset)))continue;
                    if(direction!=state.direction && !TurnClearsFurniture(state.node,state.direction,direction,departureCorner))continue;
                    var candidate=(node:next,direction:direction);
                    float rise=Mathf.Abs(layout.ElevationPlan.GetNodeElevation(edge.A)-layout.ElevationPlan.GetNodeElevation(edge.B));
                    float span=(edge.IsHorizontal?layout.NodeSpacing.x:layout.NodeSpacing.y)-layout.RoadWidth;
                    if(rise/Mathf.Max(span,1f)>.16f)continue;
                    float cost=costs[state]+(edge.IsHorizontal?layout.NodeSpacing.x:layout.NodeSpacing.y)+
                        (direction==state.direction?0f:12f)+rise*2f;
                    if(costs.TryGetValue(candidate,out float existing)&&existing<=cost)continue;
                    costs[candidate]=cost;previous[candidate]=state;pending.Add(candidate);
                }
            }
            return null;
        }

        private static float MinimumLaneJoinRun(float shift) => Mathf.Sqrt(6f*Mathf.Abs(shift)*RightTurnRadius);

        private bool TurnClearsFurniture(Vector2Int node, Vector2Int incoming, Vector2Int outgoing, bool centered)
        {
            var key = (node,incoming,outgoing,centered);
            if (turnClearance.TryGetValue(key,out bool clear)) return clear;
            Vector3 corner = layout.GetNodeWorldPosition(node);
            Vector3 forward = new Vector3(incoming.x,0,incoming.y);
            Vector3 after = new Vector3(outgoing.x,0,outgoing.y);
            bool right = Vector3.Dot(Right(forward),after)>.5f;
            float lane = centered ? 0f : LaneCenterOffset;
            float radius = right && !centered ? RightTurnRadius : Radius;
            float tangent = radius+(right?lane:-lane);
            Vector3 enter = corner-forward*tangent+Right(forward)*lane;
            Vector3 center = enter+after*radius;
            Vector3 radial = (enter-center)/radius;
            clear = true;
            foreach (Rect furniture in streetFurniture)
            {
                // Only fixtures in this corner's finite swept neighbourhood can
                // affect it. The cached result is shared by all route searches.
                Vector2 nearest = new Vector2(Mathf.Clamp(corner.x,furniture.xMin,furniture.xMax),
                    Mathf.Clamp(corner.z,furniture.yMin,furniture.yMax));
                if ((nearest-new Vector2(corner.x,corner.z)).sqrMagnitude>225f) continue;
                for (int i = 0; i <= 64; i++)
                {
                    float angle = Mathf.PI*.5f*i/64f;
                    Vector3 point = center+radius*(radial*Mathf.Cos(angle)+forward*Mathf.Sin(angle));
                    Vector3 heading = -radial*Mathf.Sin(angle)+forward*Mathf.Cos(angle);
                    if (!BodyOverlapsFurniture(point,heading,furniture)) continue;
                    clear = false;
                    break;
                }
                if (!clear) break;
            }
            turnClearance[key] = clear;
            return clear;
        }

        private void AddRailFootprint(CityExteriorStairRailDescriptor rail)
        {
            if (Vector3.Distance(rail.SurfaceStart,rail.SurfaceEnd)<=.001f) return;
            // Stair treads and both approach rails share the same placement
            // plan as their physical end posts. A paved turning apron does
            // not imply that a roadside rail standing on it can be crossed.
            float half = rail.Thickness*.5f;
            streetFurniture.Add(Rect.MinMaxRect(Mathf.Min(rail.SurfaceStart.x,rail.SurfaceEnd.x)-half,
                Mathf.Min(rail.SurfaceStart.z,rail.SurfaceEnd.z)-half,
                Mathf.Max(rail.SurfaceStart.x,rail.SurfaceEnd.x)+half,
                Mathf.Max(rail.SurfaceStart.z,rail.SurfaceEnd.z)+half));
        }

        private static bool BodyOverlapsFurniture(Vector3 rear, Vector3 forward, Rect furniture)
        {
            // Includes the runtime sensor margin and the horizontal reach
            // of its raised center when the grounded body pitches on a grade.
            const float padding = .18f;
            float halfWidth = CityCanneryTruckDimensions.HalfWidth+padding;
            float halfLength = CityCanneryTruckDimensions.Length*.5f+padding;
            Vector3 right = Right(forward);
            Vector3 center = rear+forward*((CityCanneryTruckDimensions.Rear+CityCanneryTruckDimensions.Front)*.5f);
            Vector3 delta = new Vector3(furniture.center.x-center.x,0,furniture.center.y-center.z);
            float x = furniture.width*.5f, z = furniture.height*.5f;
            return Mathf.Abs(delta.x)<=x+halfWidth*Mathf.Abs(right.x)+halfLength*Mathf.Abs(forward.x) &&
                Mathf.Abs(delta.z)<=z+halfWidth*Mathf.Abs(right.z)+halfLength*Mathf.Abs(forward.z) &&
                Mathf.Abs(Vector3.Dot(delta,right))<=halfWidth+x*Mathf.Abs(right.x)+z*Mathf.Abs(right.z) &&
                Mathf.Abs(Vector3.Dot(delta,forward))<=halfLength+x*Mathf.Abs(forward.x)+z*Mathf.Abs(forward.z);
        }

        private float TurnTangent(Vector2Int incoming,Vector2Int outgoing)
        {
            bool right = incoming.y*outgoing.x-incoming.x*outgoing.y > 0;
            return right ? RightTurnRadius+LaneCenterOffset : Radius-LaneCenterOffset;
        }

        private Vector2Int ForwardNode(RoadEdge edge,Vector3 position,Vector3 direction)
        {
            float a=Vector3.Dot(layout.GetNodeWorldPosition(edge.A)-position,direction);
            float b=Vector3.Dot(layout.GetNodeWorldPosition(edge.B)-position,direction);
            return a>b?edge.A:edge.B;
        }
        private static Vector2Int Direction(Vector3 v) => Mathf.Abs(v.x)>Mathf.Abs(v.z)
            ?new Vector2Int(v.x>0?1:-1,0):new Vector2Int(0,v.z>0?1:-1);
        private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x,0,v.z);
        private static Vector3 Right(Vector3 forward) => new Vector3(forward.z,0,-forward.x);
        private float StreetHeight(Vector3 p)
        {
            var key=new Vector2(p.x,p.z);
            if(streetHeights.TryGetValue(key,out float y))return y;
            if(!layout.ElevationPlan.TrySampleSurface(key,CitySurfaceRole.RoadTop,out y,out _))
                throw new InvalidOperationException($"Cannery street path leaves the road at {p}.");
            streetHeights[key]=y;
            return y;
        }
        private CityPortTruckPose GroundAxles(CityPortTruckPose pose)
        {
            Vector3 rear=pose.RearAxle;
            Vector3 direction=Horizontal(pose.Rotation*Vector3.forward).normalized;
            rear.y=TruckGround(rear);
            Vector3 front=rear+direction*CityCanneryTruckDimensions.Wheelbase;
            front.y=TruckGround(front);
            Quaternion rotation=Quaternion.LookRotation(front-rear);
            return new CityPortTruckPose(rear,rotation,pose.Reversing);
        }
        private float TruckGround(Vector3 p)
        {
            if(site.TrySampleYardTop(p,out float yardTop))return yardTop;
            if(port.TrySampleTop(new Vector2(p.x,p.z),out float top))return top;
            return StreetHeight(p);
        }
        private void AddStraight(List<CityPortTruckPose> target,Vector3 from,Vector3 to,Vector3 forward,bool reverse,bool street)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/Step));
            for(int i=0;i<=count;i++)
            {
                Vector3 p=Vector3.Lerp(from,to,i/(float)count);
                if(street)p.y=StreetHeight(p);
                Add(target,new CityPortTruckPose(p,Quaternion.LookRotation(forward),reverse));
            }
        }

        private void AddStreetStraight(List<CityPortTruckPose> target, Vector3 from, Vector3 to, Vector3 forward)
        {
            // Parking and civil-access anchors are on the old centerline.
            // A tangent S join reaches/leaves the right lane over the available
            // straight; ordinary street segments retain the full lane offset.
            forward = Horizontal(forward).normalized;
            Vector3 right = Right(forward), delta = Horizontal(to - from);
            float run = Vector3.Dot(delta, forward), shift = Vector3.Dot(delta, right);
            if (run < -.02f || Mathf.Abs(shift) > .01f && run*run < 6f*Mathf.Abs(shift)*RightTurnRadius-.01f)
                throw new InvalidOperationException("The truck lane join lacks a forward tangent.");
            int count = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / Step));
            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                float blend = t * t * (3f - 2f * t);
                Vector3 p = from + forward * (run * t) + right * (shift * blend);
                p.y = StreetHeight(p);
                Vector3 heading = forward * Mathf.Max(run, .001f) + right * (shift * 6f * t * (1f - t));
                Add(target, new CityPortTruckPose(p, Quaternion.LookRotation(heading), false));
            }
        }

        private void CreatePaving()
        {
            float halfRoad = layout.RoadWidth * .5f;
            float halfLanePair = halfRoad - CityStreetSurfacePlanner.SidewalkWidth;
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street) continue;
                Vector3 a = layout.GetNodeWorldPosition(edge.A), b = layout.GetNodeWorldPosition(edge.B);
                paving.Add(Rect.MinMaxRect(Mathf.Min(a.x,b.x)-halfLanePair, Mathf.Min(a.z,b.z)-halfLanePair,
                    Mathf.Max(a.x,b.x)+halfLanePair, Mathf.Max(a.z,b.z)+halfLanePair));
                AddApproachApron(edge, edge.A, halfRoad);
                AddApproachApron(edge, edge.B, halfRoad);
            }
            foreach (Vector2Int node in turnNodes)
            {
                Vector3 p = layout.GetNodeWorldPosition(node);
                paving.Add(Rect.MinMaxRect(p.x-halfRoad,p.z-halfRoad,p.x+halfRoad,p.z+halfRoad));
            }
        }

        private void CreateStreetNeighbours()
        {
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street) continue;
                Neighbours(edge.A).Add((edge.B, edge));
                Neighbours(edge.B).Add((edge.A, edge));
            }
        }

        private List<(Vector2Int node,RoadEdge edge)> Neighbours(Vector2Int node)
        {
            if (streetNeighbours.TryGetValue(node, out var list)) return list;
            list = new List<(Vector2Int,RoadEdge)>(4);
            streetNeighbours[node] = list;
            return list;
        }

        private void AddApproachApron(RoadEdge edge, Vector2Int node, float halfRoad)
        {
            if (!turnNodes.Contains(node)) return;
            Vector3 p = layout.GetNodeWorldPosition(node);
            Vector3 outward = Horizontal(layout.GetNodeWorldPosition(edge.Other(node)) - p).normalized;
            float length = CityStreetSurfacePlanner.BusApproachApronLength;
            Vector3 center = p + outward * (halfRoad + length * .5f);
            paving.Add(edge.IsHorizontal
                ? Rect.MinMaxRect(center.x-length*.5f,center.z-halfRoad,center.x+length*.5f,center.z+halfRoad)
                : Rect.MinMaxRect(center.x-halfRoad,center.z-length*.5f,center.x+halfRoad,center.z+length*.5f));
        }
        private static void Add(List<CityPortTruckPose> target,CityPortTruckPose pose)
        {
            if(target.Count>0&&Vector3.Distance(target[target.Count-1].RearAxle,pose.RearAxle)<.0001f)return;
            target.Add(pose);
        }

        /// <summary>Checks the whole body, including both overhangs. The only
        /// pavement exception is the authored dropped-curb factory entrance.</summary>
        public void ValidateOrThrow()
        {
            for(int l=0;l<poses.Length;l++)
            for(int i=0;i<poses[l].Count;i++)
            {
                CityPortTruckPose pose=poses[l][i];
                for(float x=-CityCanneryTruckDimensions.HalfWidth;x<=CityCanneryTruckDimensions.HalfWidth+.001f;
                    x+=CityCanneryTruckDimensions.HalfWidth)
                for(float z=CityCanneryTruckDimensions.Rear;z<=CityCanneryTruckDimensions.Front+.001f;z+=.5f)
                {
                    Vector3 p=pose.RearAxle+pose.Rotation*new Vector3(x,0,z);
                    Vector3 local=site.Local(p);
                    if(site.ProductionBounds.Contains(new Vector2(p.x,p.z)))
                        throw new InvalidOperationException("The delivery truck crosses the cannery production floor.");
                    if(local.x>=-.1f&&site.TrySampleYardTop(p,out _))continue;
                    if(port.TrySampleTop(new Vector2(p.x,p.z),out _))continue;
                    bool paved=false;
                    foreach(Rect road in paving)
                    {
                        if(p.x>=road.xMin-.02f&&p.x<=road.xMax+.02f&&
                           p.z>=road.yMin-.02f&&p.z<=road.yMax+.02f){paved=true;break;}
                    }
                    if(!paved)throw new InvalidOperationException($"Truck body leaves paving on {(CityCanneryTruckLeg)l} " +
                        $"pose {i}/{poses[l].Count} at world={p}, local={local}, axle={site.Local(pose.RearAxle)}, " +
                        $"site={site.Origin}, frontage={site.FrontageEdge}.");
                }
            }
            if(MaximumGrade>.17f)
            {
                List<CityPortTruckPose> leg = poses[(int)maximumGradeLeg];
                Vector3 before = leg[maximumGradePose-1].RearAxle;
                Vector3 after = leg[maximumGradePose].RearAxle;
                throw new InvalidOperationException($"Cannery truck route grade {MaximumGrade:P2} exceeds 17% " +
                    $"on {maximumGradeLeg} pose {maximumGradePose}/{leg.Count}: {before} -> {after}, " +
                    $"local {site.Local(before)} -> {site.Local(after)}, site={site.Origin}, frontage={site.FrontageEdge}.");
            }
        }
    }
}
