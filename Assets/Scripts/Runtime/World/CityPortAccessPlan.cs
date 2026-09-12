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
        private SurfaceIndex surfaceIndex;
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
        private static readonly Rect ReservedLocal = Rect.MinMaxRect(.5f, -46f, 59.5f, -7f);
        public Rect ReservedBounds => World(ReservedLocal);
        /// <summary>The box outside which <see cref="ApplyGroundTop"/> leaves natural
        /// ground untouched: the reservation and its two-metre earthwork blend. The
        /// terrain builder keeps its fine collider pitch inside it.</summary>
        public Rect GradedBounds
        {
            get
            {
                Rect bounds=ReservedBounds;
                return Rect.MinMaxRect(bounds.xMin-2,bounds.yMin-2,bounds.xMax+2,bounds.yMax+2);
            }
        }
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
            if(!GradedBounds.Contains(world))return natural;
            FindSurface(world,out float distance,out float top);
            return Mathf.Lerp(top-.18f,natural,Mathf.SmoothStep(0,1,distance/2f));
        }

        // The nearest paved surface, judged by a running best over the two
        // yards and then every road segment in authored order. A candidate
        // replaces the best by `d<distance`, or for a road by
        // `d<=distance && offset<bestRoadOffset`, so the first of equals wins.
        //
        // Every sample used to walk all 96 segments, normalising a lerped
        // "right" for each. The plan is immutable, so the segment constants
        // are resolved once (SurfaceIndex), the normalise only runs for a
        // candidate that actually takes the lead, and a uniform grid hands
        // each query only the candidates that can still win in its cell.
        //
        // Bit-identity: let m be the least d over all candidates for the
        // query point. Everything before the first candidate with d==m has
        // d>m, so that candidate passes `d<distance` and overwrites all three
        // state values; from then on distance==m and no candidate with d>m
        // can pass either test. Dropping candidates whose d is provably >m
        // for every point of the cell therefore changes neither the winner
        // nor the state it leaves, provided the survivors keep their order
        // and their arithmetic, which they do. A cell keeps a candidate when
        // a lower bound of its d over the cell is at most an upper bound of
        // m over the cell; both bounds carry a 2 cm margin, three orders
        // above float rounding at these tens of metres. Queries outside the
        // grid, or non-finite ones, take the full scan.
        private void FindSurface(Vector2 world,out float distance,out float top)
        {
            Vector2 p=world-new Vector2(Origin.x,Origin.z);
            distance=float.PositiveInfinity; top=0;
            float bestRoadOffset=float.PositiveInfinity;
            SurfaceIndex index=surfaceIndex??(surfaceIndex=new SurfaceIndex(data,IndexCover()));
            int[] candidates=index.Candidates(p);
            if(candidates==null)
            {
                for(int id=0;id<index.CandidateCount;id++)
                    TestCandidate(index,id,p,ref distance,ref bestRoadOffset,ref top);
                return;
            }
            for(int k=0;k<candidates.Length;k++)
                TestCandidate(index,candidates[k],p,ref distance,ref bestRoadOffset,ref top);
        }

        private void TestCandidate(SurfaceIndex index,int id,Vector2 p,ref float distance,ref float bestRoadOffset,ref float top)
        {
            if(id<2)
            {
                Rect rect=id==0?data.lowerYard.Bounds:data.upperYard.Bounds;
                float yardDistance=Vector2.Distance(p,new Vector2(Mathf.Clamp(p.x,rect.xMin,rect.xMax),Mathf.Clamp(p.y,rect.yMin,rect.yMax)));
                if(yardDistance<distance){distance=yardDistance;top=Origin.y+CityPortPlan.DeckHeight;}
                return;
            }
            int s=id-2;
            RoadSample a=data.roadSamples[s],b=data.roadSamples[s+1];
            float t=Mathf.Clamp01(Vector2.Dot(p-index.First[s],index.Delta[s])/index.Denominator[s]);
            Vector2 point=index.First[s]+index.Delta[s]*t;
            float offset=Vector2.Distance(p,point);
            // The authored half-metre shoulders are flush with the same
            // graded plane; the earthwork must continue beneath them.
            float d=Mathf.Max(0,offset-Mathf.Lerp(a.halfWidth,b.halfWidth,t)-.5f);
            if(d<distance || d<=distance && offset<bestRoadOffset)
            {
                Vector3 right=Vector3.Lerp(a.right,b.right,t).normalized;
                float lateral=Vector2.Dot(p-point,new Vector2(right.x,right.z));
                distance=d;bestRoadOffset=offset;top=Origin.y+Mathf.Lerp(a.center.y,b.center.y,t)+lateral*Mathf.Lerp(a.crossfall,b.crossfall,t);
            }
        }

        // Local-frame box the grid must answer: every point TrySampleTop
        // lets through and every point ApplyGroundTop grades, plus a metre
        // for the world-to-local rounding of either box test.
        private Rect IndexCover()
        {
            Rect graded=Rect.MinMaxRect(ReservedLocal.xMin-2f,ReservedLocal.yMin-2f,ReservedLocal.xMax+2f,ReservedLocal.yMax+2f);
            return Rect.MinMaxRect(
                Mathf.Min(graded.xMin,pavedMin.x)-1f,Mathf.Min(graded.yMin,pavedMin.y)-1f,
                Mathf.Max(graded.xMax,pavedMax.x)+1f,Mathf.Max(graded.yMax,pavedMax.y)+1f);
        }

        /// <summary>Per-segment constants of the authored road, resolved once,
        /// and a uniform grid of the candidates that can still win in each
        /// cell. Candidate ids: 0 and 1 are the yards, 2+s is the segment
        /// from road sample s to s+1, in the scan order FindSurface uses.</summary>
        private sealed class SurfaceIndex
        {
            private const float CellSize=2f;
            private const float Margin=.02f;
            public readonly Vector2[] First,Delta;
            public readonly float[] Denominator;
            public readonly int CandidateCount;
            private readonly Vector2 min,max;
            private readonly int columns,rows;
            private readonly int[][] cells;

            public SurfaceIndex(Definition data,Rect cover)
            {
                int segments=data.roadSamples.Length-1;
                First=new Vector2[segments];Delta=new Vector2[segments];Denominator=new float[segments];
                var segmentMin=new Vector2[segments];var segmentMax=new Vector2[segments];
                var narrowest=new float[segments];var widest=new float[segments];
                for(int s=0;s<segments;s++)
                {
                    // Exactly what Project computed per sample before.
                    RoadSample a=data.roadSamples[s],b=data.roadSamples[s+1];
                    First[s]=new Vector2(a.center.x,a.center.z);
                    Delta[s]=new Vector2(b.center.x-a.center.x,b.center.z-a.center.z);
                    Denominator[s]=Mathf.Max(.00001f,Delta[s].sqrMagnitude);
                    Vector2 end=First[s]+Delta[s];
                    segmentMin[s]=Vector2.Min(First[s],end);segmentMax[s]=Vector2.Max(First[s],end);
                    narrowest[s]=Mathf.Min(a.halfWidth,b.halfWidth);widest[s]=Mathf.Max(a.halfWidth,b.halfWidth);
                }
                CandidateCount=2+segments;
                var yards=new[]{Normalized(data.lowerYard.Bounds),Normalized(data.upperYard.Bounds)};
                min=new Vector2(cover.xMin,cover.yMin);
                columns=Mathf.Max(1,Mathf.CeilToInt(cover.width/CellSize));
                rows=Mathf.Max(1,Mathf.CeilToInt(cover.height/CellSize));
                max=min+new Vector2(columns,rows)*CellSize;
                cells=new int[columns*rows][];
                float halfDiagonal=CellSize*Mathf.Sqrt(2f)*.5f;
                var retained=new List<int>(CandidateCount);
                for(int row=0;row<rows;row++)
                for(int column=0;column<columns;column++)
                {
                    Vector2 cellMin=min+new Vector2(column,row)*CellSize,cellMax=cellMin+new Vector2(CellSize,CellSize);
                    Vector2 centre=(cellMin+cellMax)*.5f;
                    // d is 1-Lipschitz in the query, so any candidate's d at
                    // the centre plus the half diagonal bounds the least d
                    // anywhere in the cell from above.
                    float bound=float.PositiveInfinity;
                    for(int id=0;id<CandidateCount;id++)
                    {
                        float upper=id<2
                            ? DistanceToRect(centre,yards[id])+halfDiagonal
                            : Mathf.Max(0f,DistanceToSegment(centre,First[id-2],Delta[id-2],Denominator[id-2])+halfDiagonal-narrowest[id-2]-.5f);
                        bound=Mathf.Min(bound,upper+Margin);
                    }
                    retained.Clear();
                    for(int id=0;id<CandidateCount;id++)
                    {
                        float lower=id<2
                            ? DistanceBetweenRects(cellMin,cellMax,new Vector2(yards[id].xMin,yards[id].yMin),new Vector2(yards[id].xMax,yards[id].yMax))
                            : Mathf.Max(0f,DistanceBetweenRects(cellMin,cellMax,segmentMin[id-2],segmentMax[id-2])-widest[id-2]-.5f);
                        if(lower-Margin<=bound)retained.Add(id);
                    }
                    cells[row*columns+column]=retained.ToArray();
                }
            }

            /// <summary>The cell's candidates in scan order, or null when the
            /// point is outside the grid or not a number, which the full scan
            /// answers.</summary>
            public int[] Candidates(Vector2 p)
            {
                if(!(p.x>=min.x&&p.x<max.x&&p.y>=min.y&&p.y<max.y))return null;
                int column=Mathf.Min(columns-1,(int)((p.x-min.x)/CellSize));
                int row=Mathf.Min(rows-1,(int)((p.y-min.y)/CellSize));
                return cells[row*columns+column];
            }

            private static Rect Normalized(Rect rect)=>Rect.MinMaxRect(
                Mathf.Min(rect.xMin,rect.xMax),Mathf.Min(rect.yMin,rect.yMax),
                Mathf.Max(rect.xMin,rect.xMax),Mathf.Max(rect.yMin,rect.yMax));

            private static float DistanceToRect(Vector2 p,Rect rect)=>
                Vector2.Distance(p,new Vector2(Mathf.Clamp(p.x,rect.xMin,rect.xMax),Mathf.Clamp(p.y,rect.yMin,rect.yMax)));

            private static float DistanceToSegment(Vector2 p,Vector2 first,Vector2 delta,float denominator)
            {
                float t=Mathf.Clamp01(Vector2.Dot(p-first,delta)/denominator);
                return Vector2.Distance(p,first+delta*t);
            }

            private static float DistanceBetweenRects(Vector2 aMin,Vector2 aMax,Vector2 bMin,Vector2 bMax)
            {
                float dx=Mathf.Max(0f,Mathf.Max(bMin.x-aMax.x,aMin.x-bMax.x));
                float dy=Mathf.Max(0f,Mathf.Max(bMin.y-aMax.y,aMin.y-bMax.y));
                return Mathf.Sqrt(dx*dx+dy*dy);
            }
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
