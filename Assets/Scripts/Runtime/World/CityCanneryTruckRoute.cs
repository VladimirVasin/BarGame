using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCanneryTruckLeg { PortArrive, PortReverse, PortToFactory, FactoryReverse, FactoryToShop, ShopToPort }

    /// <summary>Rear-axle paths over the existing street graph. Every street corner
    /// is a tangent six metre arc; the factory reverse is its own low-speed leg.</summary>
    public sealed class CityCanneryTruckRoute
    {
        // The minimum axle radius is six metres. Street fillets include the
        // long front overhang and keep the outer cab corner inside asphalt.
        private const float Radius = CityCanneryPlan.TurningRadius + .25f;
        private const float Step = .25f;
        private readonly CityLayout layout;
        private readonly CityCanneryPlan site;
        private readonly CityPortAccessPlan port;
        private readonly List<CityPortTruckPose>[] poses = new List<CityPortTruckPose>[6];
        private readonly float[][] distances = new float[6][];
        private readonly HashSet<RoadEdge> busRoads;
        private readonly HashSet<RoadEdge> streetEdges = new HashSet<RoadEdge>();
        public IReadOnlyCollection<RoadEdge> StreetEdges => streetEdges;
        public int SharedBusStreetCount { get; private set; }
        public CityPortTruckPose ShopPose { get; private set; }
        public Vector3 ShopDoorPoint { get; private set; }
        public Vector3 ShopDropPoint { get; private set; }
        public CityPortTruckPose PortLoadingPose => Sample(CityCanneryTruckLeg.PortReverse, 1f);
        public float MaximumGrade { get; private set; }
        private CityCanneryTruckLeg maximumGradeLeg;
        private int maximumGradePose;

        private CityCanneryTruckRoute(CityLayout layout, CityCanneryPlan site, CityPortAccessPlan port)
        {
            this.layout = layout; this.site = site; this.port = port;
            busRoads = layout.BlueprintId == CityBlueprintCatalog.DefaultBlueprintId
                ? CityBusPlanner.ServiceRoadEdges(CityBusPlanner.CreateRoadRouting(layout))
                : new HashSet<RoadEdge>();
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
            shopPoint.y = StreetHeight(shopPoint);
            ShopPose = new CityPortTruckPose(shopPoint,Quaternion.LookRotation(shopForward),false);
            for (int i=poses[3].Count-1;i>=0;i--)
            {
                CityPortTruckPose p = poses[3][i];
                Add(poses[4],new CityPortTruckPose(p.RearAxle,p.Rotation,false));
            }
            AddStreet(poses[4], poses[4][poses[4].Count-1], site.FrontageEdge, ShopPose, shopEdge);
            CityPortTruckPose portIn = port.SampleTruck(CityPortTruckLeg.Arrive,0f);
            AddStreet(poses[5],ShopPose,shopEdge,portIn,port.StreetEdge);
            foreach (RoadEdge edge in streetEdges)
                if (busRoads.Contains(edge)) SharedBusStreetCount++;
            for(int leg=0;leg<poses.Length;leg++)
            for(int i=0;i<poses[leg].Count;i++)
                poses[leg][i]=GroundAxles(poses[leg][i]);
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

        private void AddStreet(List<CityPortTruckPose> target,CityPortTruckPose from,RoadEdge fromEdge,
            CityPortTruckPose to,RoadEdge toEdge)
        {
            Vector3 startForward=from.Rotation*Vector3.forward, endForward=to.Rotation*Vector3.forward;
            Vector2Int start=ForwardNode(fromEdge,from.RearAxle,startForward);
            Vector2Int goal=ForwardNode(toEdge,to.RearAxle,-endForward);
            Vector2Int incoming=Direction(startForward), outgoing=Direction(endForward);
            List<Vector2Int> nodes=FindNodes(start,incoming,goal,outgoing,true) ??
                FindNodes(start,incoming,goal,outgoing,false);
            if (nodes == null)
                throw new InvalidOperationException("No connected truck street route reaches the cannery delivery point.");
            streetEdges.Add(fromEdge);
            streetEdges.Add(toEdge);
            for (int i=1;i<nodes.Count;i++) streetEdges.Add(new RoadEdge(nodes[i-1],nodes[i]));
            var polyline=new List<Vector3>{from.RearAxle};
            foreach(Vector2Int node in nodes)polyline.Add(layout.GetNodeWorldPosition(node));
            polyline.Add(to.RearAxle);
            // Remove duplicate and collinear graph nodes before filleting.
            for(int i=polyline.Count-2;i>0;i--)
            {
                Vector3 before=Horizontal(polyline[i]-polyline[i-1]);
                Vector3 after=Horizontal(polyline[i+1]-polyline[i]);
                if(before.sqrMagnitude<.001f || after.sqrMagnitude<.001f || Vector3.Dot(before.normalized,after.normalized)>.9999f)
                    polyline.RemoveAt(i);
            }
            Vector3 cursor=polyline[0];
            for(int i=1;i<polyline.Count-1;i++)
            {
                Vector3 corner=polyline[i];
                Vector3 incomingDirection=Horizontal(corner-polyline[i-1]).normalized;
                Vector3 outgoingDirection=Horizontal(polyline[i+1]-corner).normalized;
                if(Vector3.Dot(incomingDirection,outgoingDirection)<-.01f)
                    throw new InvalidOperationException("Cannery route contains a U-turn outside a turning area.");
                Vector3 enter=corner-incomingDirection*Radius;
                Vector3 leave=corner+outgoingDirection*Radius;
                if(Vector3.Dot(Horizontal(enter-cursor),incomingDirection)<-.02f ||
                   Horizontal(polyline[i+1]-corner).magnitude<Radius-.02f)
                    throw new InvalidOperationException("Cannery street corner lacks six metres of tangent room.");
                AddStraight(target,cursor,enter,incomingDirection,false,true);
                Vector3 center=enter+outgoingDirection*Radius;
                Vector3 radial=(enter-center)/Radius;
                for(int s=1;s<=48;s++)
                {
                    float angle=Mathf.PI*.5f*s/48f;
                    Vector3 p=center+Radius*(radial*Mathf.Cos(angle)+incomingDirection*Mathf.Sin(angle));
                    p.y=StreetHeight(p);
                    Vector3 forward=-radial*Mathf.Sin(angle)+incomingDirection*Mathf.Cos(angle);
                    Add(target,new CityPortTruckPose(p,Quaternion.LookRotation(forward),false));
                }
                cursor=leave;
            }
            AddStraight(target,cursor,to.RearAxle,endForward,false,true);
        }

        private List<Vector2Int> FindNodes(Vector2Int start,Vector2Int incoming,Vector2Int goal,Vector2Int outgoing,
            bool avoidBusRoads)
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
                if(state.node==goal && state.direction!=-outgoing)
                {
                    var result=new List<Vector2Int>{state.node};
                    while(previous.TryGetValue(state,out var p)){state=p;result.Add(state.node);}
                    result.Reverse();return result;
                }
                foreach(RoadEdge edge in layout.RoadEdges)
                {
                    if(layout.GetPathKind(edge)!=CityPathKind.Street)continue;
                    if(avoidBusRoads && busRoads.Contains(edge))continue;
                    Vector2Int next;
                    if(edge.A==state.node)next=edge.B;
                    else if(edge.B==state.node)next=edge.A;
                    else continue;
                    Vector2Int direction=next-state.node;
                    if(direction==-state.direction)continue;
                    var candidate=(node:next,direction:direction);
                    float rise=Mathf.Abs(layout.ElevationPlan.GetNodeElevation(edge.A)-layout.ElevationPlan.GetNodeElevation(edge.B));
                    float span=(edge.IsHorizontal?layout.NodeSpacing.x:layout.NodeSpacing.y)-layout.RoadWidth;
                    if(rise/Mathf.Max(span,1f)>.16f)continue;
                    float cost=costs[state]+(edge.IsHorizontal?layout.NodeSpacing.x:layout.NodeSpacing.y)+
                        (direction==state.direction?0f:12f)+rise*2f+(busRoads.Contains(edge)?10000f:0f);
                    if(costs.TryGetValue(candidate,out float existing)&&existing<=cost)continue;
                    costs[candidate]=cost;previous[candidate]=state;pending.Add(candidate);
                }
            }
            return null;
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
        private float StreetHeight(Vector3 p)
        {
            if(!layout.ElevationPlan.TrySampleSurface(new Vector2(p.x,p.z),CitySurfaceRole.RoadTop,out float y,out _))
                throw new InvalidOperationException($"Cannery street path leaves the road at {p}.");
            return y;
        }
        private CityPortTruckPose GroundAxles(CityPortTruckPose pose)
        {
            Vector3 rear=pose.RearAxle;
            Vector3 direction=Horizontal(pose.Rotation*Vector3.forward).normalized;
            rear.y=TruckGround(rear);
            Vector3 front=rear+direction*4.2f;
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
                for(float x=-1.25f;x<=1.251f;x+=1.25f)
                for(float z=-2.6f;z<=5.401f;z+=.5f)
                {
                    Vector3 p=pose.RearAxle+pose.Rotation*new Vector3(x,0,z);
                    Vector3 local=site.Local(p);
                    if(site.ProductionBounds.Contains(new Vector2(p.x,p.z)))
                        throw new InvalidOperationException("The delivery truck crosses the cannery production floor.");
                    if(local.x>=-.1f&&site.TrySampleYardTop(p,out _))continue;
                    if(port.TrySampleTop(new Vector2(p.x,p.z),out _))continue;
                    bool paved=false;
                    foreach(RoadEdge edge in layout.RoadEdges)
                    {
                        if(layout.GetPathKind(edge)!=CityPathKind.Street)continue;
                        Vector3 a=layout.GetNodeWorldPosition(edge.A), b=layout.GetNodeWorldPosition(edge.B);
                        float half=layout.RoadWidth*.5f-CityStreetSurfacePlanner.SidewalkWidth;
                        if(p.x>=Mathf.Min(a.x,b.x)-half-.02f&&p.x<=Mathf.Max(a.x,b.x)+half+.02f&&
                           p.z>=Mathf.Min(a.z,b.z)-half-.02f&&p.z<=Mathf.Max(a.z,b.z)+half+.02f){paved=true;break;}
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
