using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public enum CityPortTruckLeg { Arrive, ReverseToStore, Leave }

    public readonly struct CityPortTruckPose
    {
        public CityPortTruckPose(Vector3 rearAxle, Quaternion rotation, bool reversing)
        { RearAxle = rearAxle; Rotation = rotation; Reversing = reversing; }
        public Vector3 RearAxle { get; }
        public Quaternion Rotation { get; }
        public bool Reversing { get; }
        public Vector3 Center => RearAxle + Rotation * Vector3.forward * 1.4f;
    }

    /// <summary>Shared imported civil geometry and a connected, unoccupied rigid-truck route.
    /// No vehicle, clock or gameplay operation is created by this plan.</summary>
    public sealed class CityPortAccessPlan
    {
        [Serializable] public sealed class RoadSample
        {
            public float distance, halfWidth, crossfall;
            public Vector3 center, right;
        }
        [Serializable] private sealed class YardDefinition
        {
            public float x, z, width, depth;
            public Rect Bounds => new Rect(x, z, width, depth);
        }
        [Serializable] private sealed class Definition
        {
            public int version;
            public float carriagewayWidth, roadLength;
            public float truckLength, truckWidth, truckWheelbase, truckRearOverhang, turnRadius;
            public RoadSample[] roadSamples;
            public YardDefinition lowerYard, upperYard;
            public Vector3 gate, loadingRearAxle, turnCenter;
        }
        private readonly struct RoutePoint
        {
            public RoutePoint(Vector3 point, Vector3 forward) { Point = point; Rotation = Quaternion.LookRotation(forward); }
            public Vector3 Point { get; }
            public Quaternion Rotation { get; }
        }

        private static readonly ConditionalWeakTable<CityLayout, CityPortAccessPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityPortAccessPlan>();
        private static Definition definition;
        private readonly RoutePoint[][] truckRoutes;
        private readonly float[][] truckDistances;
        private readonly Definition data;
        private readonly CityLayout layout;
        // Local-frame box outside which FindSurface cannot find pavement: it
        // holds both yards and every road centreline point, grown by the widest
        // shoulder (halfWidth + .5 m) and one more metre for the .015 m
        // acceptance and float rounding. TrySampleTop answers false out there
        // without the scan; its top is then 0 rather than the nearest surface,
        // which no caller reads on a false answer.
        private readonly Vector2 pavedMin, pavedMax;
        public Vector3 Origin { get; }
        public RoadEdge StreetEdge { get; }
        public Vector3 StreetConnection => World(data.roadSamples[0].center);
        public Vector3 Gate => World(data.gate);
        public Vector3 StoreRearAxle => World(data.loadingRearAxle);
        public float CarriagewayWidth => data.carriagewayWidth;
        public float TruckLength => data.truckLength;
        public float TruckWidth => data.truckWidth;
        public float TruckWheelbase => data.truckWheelbase;
        public float MinimumTurningRadius => data.turnRadius;
        public IReadOnlyList<RoadSample> RoadSamples => data.roadSamples;
        public Rect LowerYard => World(data.lowerYard.Bounds);
        public Rect UpperYard => World(data.upperYard.Bounds);
        public Rect StreetOpening => World(Rect.MinMaxRect(48.8f, -43.05f, 59.2f, -41.85f));
        public Rect ReservedBounds => World(Rect.MinMaxRect(.5f, -46f, 59.5f, -7f));
        public Vector3 World(Vector3 local) => Origin + local;
        public Rect World(Rect local) => new Rect(local.position + new Vector2(Origin.x, Origin.z), local.size);

        private CityPortAccessPlan(CityLayout layout, CityPortPlan port, Definition source)
        {
            this.layout = layout;
            Origin = port.Origin;
            data = source;
            pavedMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            pavedMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            float shoulder = 0f;
            for (int yard=0; yard<2; yard++)
            {
                // Both rect edges, so a negative authored size still lands inside.
                Rect rect = yard==0 ? data.lowerYard.Bounds : data.upperYard.Bounds;
                pavedMin = Vector2.Min(pavedMin, new Vector2(Mathf.Min(rect.xMin,rect.xMax), Mathf.Min(rect.yMin,rect.yMax)));
                pavedMax = Vector2.Max(pavedMax, new Vector2(Mathf.Max(rect.xMin,rect.xMax), Mathf.Max(rect.yMin,rect.yMax)));
            }
            foreach (RoadSample sample in data.roadSamples)
            {
                pavedMin = Vector2.Min(pavedMin, new Vector2(sample.center.x, sample.center.z));
                pavedMax = Vector2.Max(pavedMax, new Vector2(sample.center.x, sample.center.z));
                shoulder = Mathf.Max(shoulder, sample.halfWidth);
            }
            shoulder += .5f + 1f;
            pavedMin -= new Vector2(shoulder, shoulder);
            pavedMax += new Vector2(shoulder, shoulder);
            bool found = false;
            foreach (CityOpenAreaAccessDescriptor access in layout.OpenAreaAccesses)
            {
                if (access.Feature != CityAreaFeatureKind.NorthWaterfront) continue;
                StreetEdge = access.FrontageEdge;
                found = true;
                break;
            }
            if (!found || !layout.HasRoad(StreetEdge) || layout.GetPathKind(StreetEdge) != CityPathKind.Street)
                throw new InvalidOperationException("Port access must join an existing city street.");
            Vector3 a = layout.GetNodeWorldPosition(StreetEdge.A), b = layout.GetNodeWorldPosition(StreetEdge.B);
            Vector3 point = StreetConnection;
            float amount = Vector3.Dot(new Vector3(point.x-a.x, 0, point.z-a.z),
                new Vector3(b.x-a.x, 0, b.z-a.z)) / new Vector2(b.x-a.x, b.z-a.z).sqrMagnitude;
            Vector3 projected = Vector3.Lerp(a, b, amount);
            if (amount < 0 || amount > 1 || Vector2.Distance(new Vector2(point.x, point.z),
                    new Vector2(projected.x, projected.z)) > .02f)
                throw new InvalidOperationException("The imported port approach misses its street frontage.");
            float streetTop = layout.ElevationPlan.SampleRoadDatum(StreetEdge, amount) + CityStreetSurfacePlanner.RoadTop;
            if (Mathf.Abs(point.y - streetTop) > .015f)
                throw new InvalidOperationException($"Port street joint height differs: imported={point.y:F4}, street={streetTop:F4}.");
            truckRoutes = CreateTruckRoutes();
            truckDistances = new float[truckRoutes.Length][];
            for (int leg=0; leg<truckRoutes.Length; leg++)
            {
                truckDistances[leg] = new float[truckRoutes[leg].Length];
                for (int i=1;i<truckRoutes[leg].Length;i++)
                    truckDistances[leg][i] = truckDistances[leg][i-1] +
                        Vector3.Distance(truckRoutes[leg][i-1].Point, truckRoutes[leg][i].Point);
            }
        }

        public static CityPortAccessPlan GetOrCreate(CityLayout layout, CityPortPlan port)
        {
            if (port == null || !Supports(layout))
                return null;
            if (!Plans.TryGetValue(layout, out CityPortAccessPlan plan))
            {
                if (definition == null)
                {
                    TextAsset asset = Resources.Load<TextAsset>("City/Port/PortAccessLayout");
                    if (asset == null) throw new InvalidOperationException("Missing authored port access contract.");
                    definition = JsonUtility.FromJson<Definition>(asset.text);
                }
                plan = new CityPortAccessPlan(layout, port, definition);
                Plans.Add(layout, plan);
            }
            if(Vector3.Distance(plan.Origin,port.Origin)>.01f)
                throw new InvalidOperationException("Port civil geometry and seacoast origin disagree.");
            port.Access = plan;
            return plan;
        }

        private static bool Supports(CityLayout layout) =>
            layout.BlueprintId==CityBlueprintCatalog.DefaultBlueprintId &&
            layout.BlockCount==new Vector2Int(17,14) &&
            Vector2.Distance(layout.NodeSpacing,new Vector2(26f,26f))<.01f && Mathf.Abs(layout.RoadWidth-8f)<.01f;

        internal static CityPortAccessPlan ForLayout(CityLayout layout)
        {
            if(!Supports(layout))return null;
            if(Plans.TryGetValue(layout,out CityPortAccessPlan result))return result;
            // Resolve the default frame directly from the authored frontage and
            // sea datum. Calling the seacoast planner here would recurse through
            // its sand sampler. Terrain therefore never depends on build order.
            foreach(CityOpenAreaAccessDescriptor access in layout.OpenAreaAccesses)
            {
                if(access.Feature!=CityAreaFeatureKind.NorthWaterfront)continue;
                foreach(CitySurfaceDescriptor surface in layout.Surfaces)
                    if(surface.Feature==CityAreaFeatureKind.NorthWaterfront && surface.Kind==CitySurfaceKind.Water)
                        return GetOrCreate(layout,new CityPortPlan(new Vector3(
                            access.Center.x-54f,surface.PhysicalTopY,access.Center.z+42f)));
            }
            return null;
        }

        public float TruckRouteLength(CityPortTruckLeg leg) => truckDistances[(int)leg][truckDistances[(int)leg].Length-1];

        public CityPortTruckPose SampleTruck(CityPortTruckLeg leg, float progress)
        {
            int l = (int)leg;
            float distance = Mathf.Clamp01(progress) * TruckRouteLength(leg);
            float[] distances = truckDistances[l];
            int i = 1;
            while (i < distances.Length-1 && distances[i] < distance) i++;
            float t = Mathf.InverseLerp(distances[i-1], distances[i], distance);
            RoutePoint a = truckRoutes[l][i-1], b = truckRoutes[l][i];
            return new CityPortTruckPose(World(Vector3.Lerp(a.Point,b.Point,t)),
                Quaternion.Slerp(a.Rotation,b.Rotation,t), leg == CityPortTruckLeg.ReverseToStore);
        }

        private RoutePoint[][] CreateTruckRoutes()
        {
            var arrive = new List<RoutePoint>();
            var reverse = new List<RoutePoint>();
            var leave = new List<RoutePoint>();
            // A real rear-axle turn joins the street's eastward travel direction
            // tangentially to the authored branch. The imported flare carries
            // the whole 8 m body, not merely a point following the road centre.
            const int join = 30;
            RoadSample entry = data.roadSamples[join];
            float heading = Mathf.Atan2(-entry.right.z,entry.right.x);
            float streetZ = data.roadSamples[0].center.z;
            float entryRadius = (entry.center.z-streetZ)/(1-Mathf.Sin(heading));
            if(entryRadius<data.turnRadius)throw new InvalidOperationException("Port street turn is too tight.");
            float streetX = entry.center.x-entryRadius*Mathf.Cos(heading);
            float angle = Mathf.PI*.5f-heading;
            var junction = new List<RoutePoint>();
            for(int i=0;i<=72;i++)
            {
                float a=angle*i/72;
                Vector3 p=new Vector3(streetX+entryRadius*Mathf.Sin(a),0,streetZ+entryRadius*(1-Mathf.Cos(a)));
                Vector2 world=new Vector2(p.x+Origin.x,p.z+Origin.z);
                if(TrySampleTop(world,out float top))p.y=top-Origin.y;
                else
                {
                    Vector3 first=layout.GetNodeWorldPosition(StreetEdge.A),last=layout.GetNodeWorldPosition(StreetEdge.B);
                    float amount=(world.x-first.x)/(last.x-first.x);
                    p.y=layout.ElevationPlan.SampleRoadDatum(StreetEdge,amount)+CityStreetSurfacePlanner.RoadTop-Origin.y;
                }
                junction.Add(new RoutePoint(p,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))));
            }
            arrive.AddRange(junction);
            for (int i=join+1;i<data.roadSamples.Length;i++)
            {
                RoadSample row=data.roadSamples[i];
                arrive.Add(new RoutePoint(row.center, new Vector3(-row.right.z,0,row.right.x)));
            }
            Vector3 c=data.turnCenter;
            float r=data.turnRadius;
            AddStraight(arrive, data.gate, c+Vector3.back*r, Vector3.left);
            for(int i=1;i<=96;i++)
            {
                float a=Mathf.PI*i/96;
                arrive.Add(new RoutePoint(c+new Vector3(-r*Mathf.Sin(a),0,-r*Mathf.Cos(a)),
                    new Vector3(-Mathf.Cos(a),0,Mathf.Sin(a))));
            }
            AddStraight(reverse,c+Vector3.forward*r,data.loadingRearAxle,Vector3.right);
            AddStraight(leave,data.loadingRearAxle,c+Vector3.forward*r,Vector3.right);
            for(int i=1;i<=48;i++)
            {
                float a=Mathf.PI*.5f*i/48;
                leave.Add(new RoutePoint(c+new Vector3(r*Mathf.Sin(a),0,r*Mathf.Cos(a)),
                    new Vector3(Mathf.Cos(a),0,-Mathf.Sin(a))));
            }
            for(int i=1;i<=48;i++)
            {
                float a=Mathf.PI*.5f*i/48;
                leave.Add(new RoutePoint(c+new Vector3(r+r*(1-Mathf.Cos(a)),0,-r*Mathf.Sin(a)),
                    new Vector3(Mathf.Sin(a),0,-Mathf.Cos(a))));
            }
            AddStraight(leave,c+new Vector3(r*2,0,-r),data.gate,Vector3.right);
            for(int i=data.roadSamples.Length-2;i>=join;i--)
            {
                RoadSample row=data.roadSamples[i];
                leave.Add(new RoutePoint(row.center,new Vector3(row.right.z,0,-row.right.x)));
            }
            for(int i=junction.Count-2;i>=0;i--)
                leave.Add(new RoutePoint(junction[i].Point,-(junction[i].Rotation*Vector3.forward)));
            return new[]{arrive.ToArray(),reverse.ToArray(),leave.ToArray()};
        }

        private static void AddStraight(List<RoutePoint> route,Vector3 from,Vector3 to,Vector3 forward)
        {
            int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/.35f));
            for(int i=route.Count==0?0:1;i<=n;i++) route.Add(new RoutePoint(Vector3.Lerp(from,to,i/(float)n),forward));
        }

        public bool TrySampleTop(Vector2 world, out float top)
        {
            // The same local point FindSurface derives, so the box test and
            // the scan judge one value.
            Vector2 p=world-new Vector2(Origin.x,Origin.z);
            if(p.x<pavedMin.x||p.x>pavedMax.x||p.y<pavedMin.y||p.y>pavedMax.y){top=0;return false;}
            FindSurface(world,out float distance,out top);
            return distance <= .015f;
        }

        public float ApplyGroundTop(Vector2 world,float natural)
        {
            Rect bounds=ReservedBounds;
            bounds=Rect.MinMaxRect(bounds.xMin-2,bounds.yMin-2,bounds.xMax+2,bounds.yMax+2);
            if(!bounds.Contains(world))return natural;
            FindSurface(world,out float distance,out float top);
            return Mathf.Lerp(top-.18f,natural,Mathf.SmoothStep(0,1,distance/2f));
        }

        private void FindSurface(Vector2 world,out float distance,out float top)
        {
            Vector2 p=world-new Vector2(Origin.x,Origin.z);
            distance=float.PositiveInfinity; top=0;
            for(int yard=0;yard<2;yard++)
            {
                Rect rect=yard==0?data.lowerYard.Bounds:data.upperYard.Bounds;
                float d=Vector2.Distance(p,new Vector2(Mathf.Clamp(p.x,rect.xMin,rect.xMax),Mathf.Clamp(p.y,rect.yMin,rect.yMax)));
                if(d<distance){distance=d;top=Origin.y+CityPortPlan.DeckHeight;}
            }
            float bestRoadOffset=float.PositiveInfinity;
            for(int i=1;i<data.roadSamples.Length;i++)
            {
                RoadSample a=data.roadSamples[i-1],b=data.roadSamples[i];
                float t=Project(p,a.center,b.center,out Vector2 point);
                Vector3 right=Vector3.Lerp(a.right,b.right,t).normalized;
                float lateral=Vector2.Dot(p-point,new Vector2(right.x,right.z));
                float offset=Vector2.Distance(p,point);
                // The authored half-metre shoulders are flush with the same
                // graded plane; the earthwork must continue beneath them.
                float d=Mathf.Max(0,offset-Mathf.Lerp(a.halfWidth,b.halfWidth,t)-.5f);
                if(d<distance || d<=distance && offset<bestRoadOffset)
                {distance=d;bestRoadOffset=offset;top=Origin.y+Mathf.Lerp(a.center.y,b.center.y,t)+lateral*Mathf.Lerp(a.crossfall,b.crossfall,t);}
            }
        }

        private static float Project(Vector2 p,Vector3 a,Vector3 b,out Vector2 point)
        {
            var first=new Vector2(a.x,a.z);var delta=new Vector2(b.x-a.x,b.z-a.z);
            float t=Mathf.Clamp01(Vector2.Dot(p-first,delta)/Mathf.Max(.00001f,delta.sqrMagnitude));
            point=first+delta*t;return t;
        }

        public void AppendWalkableFootprints(ICollection<Rect> rectangles)
        {
            rectangles.Add(LowerYard);rectangles.Add(UpperYard);
            float seam = UpperYard.yMin, reach = CityGroundTraversalPlanner.ConnectorReach;
            rectangles.Add(Rect.MinMaxRect(Mathf.Max(LowerYard.xMin, UpperYard.xMin), seam - reach,
                Mathf.Min(LowerYard.xMax, UpperYard.xMax), seam + reach));
            for(int i=1;i<data.roadSamples.Length;i++)
                AddWalkStrip(rectangles,data.roadSamples[i-1].center,data.roadSamples[i].center,2f);
        }
        private void AddWalkStrip(ICollection<Rect> rectangles,Vector3 a,Vector3 b,float half)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.5f));
            for(int i=0;i<=count;i++)
            {
                Vector3 p=World(Vector3.Lerp(a,b,i/(float)count));
                rectangles.Add(Rect.MinMaxRect(p.x-half,p.z-half,p.x+half,p.z+half));
            }
        }

        public void ValidateOrThrow(CityLayout layout)
        {
            if(!layout.HasRoad(StreetEdge))throw new InvalidOperationException("Disconnected port service graph.");
            for(int i=1;i<data.roadSamples.Length;i++)
            for(int side=-1;side<=1;side++)
            {
                RoadSample a=data.roadSamples[i-1],b=data.roadSamples[i];
                Vector3 first=a.center+a.right*a.halfWidth*side+Vector3.up*(a.crossfall*a.halfWidth*side);
                Vector3 second=b.center+b.right*b.halfWidth*side+Vector3.up*(b.crossfall*b.halfWidth*side);
                if(Mathf.Abs(second.y-first.y)/new Vector2(second.x-first.x,second.z-first.z).magnitude>.06f)
                    throw new InvalidOperationException("The actual port carriageway exceeds six percent grade.");
            }
            if(Mathf.Abs(TruckWheelbase/MinimumTurningRadius)>Mathf.Tan(36f*Mathf.Deg2Rad))
                throw new InvalidOperationException("Truck steering exceeds the reserved rigid-truck envelope.");
            CityPortTruckPose arrivalEnd=SampleTruck(CityPortTruckLeg.Arrive,1);
            CityPortTruckPose reverseStart=SampleTruck(CityPortTruckLeg.ReverseToStore,0);
            if(Vector3.Distance(arrivalEnd.RearAxle,reverseStart.RearAxle)>.01f||Quaternion.Angle(arrivalEnd.Rotation,reverseStart.Rotation)>.05f)
                throw new InvalidOperationException("Truck reverse begins with a discontinuity.");
        }
    }
}
