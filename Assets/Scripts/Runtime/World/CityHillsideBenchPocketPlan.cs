using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Upkeep around an existing view bench. The soil and the shared sit plan retain all contacts.</summary>
    public sealed class CityHillsideBenchPocket
    {
        public CityHillsideFurniturePlacement Placement { get; }
        public IReadOnlyList<Vector3> ApproachPath { get; }
        public IReadOnlyList<Vector2[]> SurfacePolygons { get; }
        public Vector3 LampFoot { get; }
        public Vector3 LampForward { get; }
        public Vector3 LampSource => LampFoot + Vector3.up * 3.2f + LampForward * .6848f;
        public Vector3 LampTarget { get; }
        public Rect LampFootprint => Rect.MinMaxRect(LampFoot.x - .12f, LampFoot.z - .12f,
            LampFoot.x + .12f, LampFoot.z + .12f);

        internal CityHillsideBenchPocket(CityHillsideFurniturePlacement placement, List<Vector3> path,
            List<Vector2[]> surfaces, Vector3 foot, Vector3 forward, Vector3 target)
        { Placement = placement; ApproachPath = path.AsReadOnly(); SurfacePolygons = surfaces.AsReadOnly();
            LampFoot = foot; LampForward = forward; LampTarget = target; }
    }

    /// <summary>Finite terrain-supported paths, with no new earth grading or furniture relocation.</summary>
    public sealed class CityHillsideBenchPocketPlan
    {
        public const float PathWidth = 1.6f;
        public const float PawnshopNeighborhoodRadius = 45f;
        public const string LightAnchorName = "IndustrialTaskLight_RidgeBench";
        private const float ProbeStep = .4f, MaximumApproachLength = 36f;
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideBenchPocketPlan> Cached = new();
        public IReadOnlyList<CityHillsideBenchPocket> Pockets { get; }
        public IReadOnlyList<string> UnconnectedBenchIds { get; }
        public IReadOnlyList<Vector2[]> SolidPolygons { get; }
        public double BuildMilliseconds { get; }
        public int SearchExpandedNodes { get; private set; }
        private static readonly List<Vector3> NoApproach = new();
        private readonly CityHillsideGroundPlan ground;
        private readonly IReadOnlyList<CityHillsideStreetRoute> roads;
        private readonly CityHillsideQuarterPlan quarter;
        private readonly CityPawnshopBenchTerracePlan terrace;
        private readonly RoadWalkableArea walk = new();
        private readonly List<Rect> protectedAreas;

        public static CityHillsideBenchPocketPlan ForLayout(CityLayout layout) =>
            Cached.GetValue(layout, value => new CityHillsideBenchPocketPlan(value));

        private CityHillsideBenchPocketPlan(CityLayout layout)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            ground = CityHillsideGroundPlan.ForLayout(layout);
            roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
            quarter = CityHillsideQuarterPlan.ForLayout(layout);
            var furniture = CityHillsideFurniturePlan.ForLayout(layout);
            terrace=CityPawnshopBenchTerracePlan.ForLayout(layout);
            protectedAreas = new List<Rect>(CityBusNetworkFurniture.Reservations(
                CityDistrictBusPlanner.CreateRoadRouting(layout), layout));
            walk.AddPolygons(ground.WalkablePolygons);
            foreach (var road in roads) walk.AddPolygons(road.WalkablePolygons);
            foreach (var link in quarter.Links) walk.Add(link.Bounds);
            if(terrace.IsEnabled) walk.AddPolygons(terrace.Footprints);
            walk.ExcludePolygons(furniture.SolidPolygons);
            if (RidgeKioskSitePlan.TryCreate(layout, out var kiosk))
            { protectedAreas.Add(kiosk.Bounds); protectedAreas.Add(kiosk.Apron); }
            bool hasPawnshop = CityPawnshopPlan.TryCreate(layout, out var pawnshop);
            if (hasPawnshop)
                protectedAreas.Add(CityRoadPolygon.Bounds(pawnshop.Footprint));
            foreach (var area in protectedAreas) walk.Exclude(area);
            var pockets = new List<CityHillsideBenchPocket>();
            var failures = new List<string>();
            var solids = new List<Vector2[]>();
            foreach (var placement in furniture.Placements)
            {
                if (placement.Kind != CityMiscKind.ParkBench) continue;
                // This work serves the reported pawnshop neighborhood. Other
                // hillside viewpoints retain their existing scenery and paths.
                if (!hasPawnshop || (XZ(placement.Position)-XZ(pawnshop.Origin)).sqrMagnitude >
                    PawnshopNeighborhoodRadius*PawnshopNeighborhoodRadius) continue;
                CityBenchSeat seat = default;
                foreach (var candidate in furniture.Seats) if (candidate.Id == placement.Id) { seat = candidate; break; }
                Vector3 dock = placement.Position + CityBenchSitPlan.GetDockOffset(seat);
                var path = terrace.TryApproach(placement,dock,out var stairPath)?stairPath:Connect(placement, dock);
                if (path.Count == 0) failures.Add(placement.Id);
                var surfaces = new List<Vector2[]> { CityRoadPolygon.Rectangle(placement.AccessBounds) };
                var corners = new List<Vector3>();
                for (int i = 0; i < path.Count; i++)
                {
                    if (i > 0 && i + 1 < path.Count)
                    {
                        Vector2 incoming=(XZ(path[i])-XZ(path[i-1])).normalized;
                        Vector2 outgoing=(XZ(path[i+1])-XZ(path[i])).normalized;
                        if (Vector2.Dot(incoming,outgoing)>.999f &&
                            Mathf.Abs(CityRoadPolygon.Cross(incoming,outgoing))<.002f) continue;
                    }
                    corners.Add(path[i]);
                }
                for (int i = 1; i < corners.Count; i++)
                {
                    Vector2 a = XZ(corners[i - 1]), b = XZ(corners[i]);
                    Vector2 right = new Vector2((b-a).y, -(b-a).x).normalized * (PathWidth * .5f);
                    if ((b-a).sqrMagnitude < .0001f) continue;
                    // The overlapping footprints keep the full walking width at bends.
                    surfaces.Add(CityRoadPolygon.CounterClockwise(new[] { a-right, b-right, b+right, a+right }));
                    // Square joints cover the whole walking width at a dogleg.
                    if (i + 1 < corners.Count) surfaces.Add(CityRoadPolygon.Rectangle(
                        Rect.MinMaxRect(b.x-.8f,b.y-.8f,b.x+.8f,b.y+.8f)));
                }
                Vector3 side = Vector3.Cross(Vector3.up, placement.Forward);
                Vector3 foot = placement.Position;
                bool lampFound = false;
                // A corner behind the plank leaves the view, entry lane and the complete path open.
                foreach (float along in new[] { -.42f, -.75f, .3f })
                {
                foreach (float across in placement.Id==CityPawnshopBenchTerracePlan.LeftBenchId?
                    new[] { -1.72f, 1.72f }:new[] { 1.72f, -1.72f })
                {
                    Vector3 candidate = placement.Position + side * across + placement.Forward * along;
                    if (!TryTop(XZ(candidate), out float y) || !walk.Contains(candidate, .2f)) continue;
                    bool clear = true;
                    for (int i = 1; i < path.Count; i++)
                        if (DistanceSquared(XZ(candidate), XZ(path[i-1]), XZ(path[i])) < 1.1f*1.1f) { clear=false; break; }
                    if (!clear) continue;
                    foot = new Vector3(candidate.x,y,candidate.z); lampFound=true; break;
                }
                if (lampFound) break;
                }
                if (!lampFound) throw new InvalidOperationException("No clear side light for " + placement.Id);
                Vector3 target = dock;
                Vector3 forward = Vector3.ProjectOnPlane(target-foot, Vector3.up).normalized;
                var pocket = new CityHillsideBenchPocket(placement,path,surfaces,foot,forward,target);
                pockets.Add(pocket); solids.Add(CityRoadPolygon.Rectangle(pocket.LampFootprint));
            }
            Pockets=pockets.AsReadOnly(); UnconnectedBenchIds=failures.AsReadOnly(); SolidPolygons=solids.AsReadOnly();
            BuildMilliseconds=timer.Elapsed.TotalMilliseconds;
        }

        private List<Vector3> Connect(CityHillsideFurniturePlacement bench, Vector3 dock)
        {
            var candidates = new List<Vector3>();
            foreach (var road in roads) { Consider(road.SidewalkPath); Consider(road.OppositeSidewalkPath); }
            foreach (var link in quarter.Links) { Consider(link.NorthSidewalk); Consider(link.SouthSidewalk); }
            candidates.Sort((a,b) => (a-dock).sqrMagnitude.CompareTo((b-dock).sqrMagnitude));
            List<Vector3> best = null;
            float shortest = float.PositiveInfinity;
            Vector3 side=Vector3.Cross(Vector3.up,bench.Forward);
            foreach (Vector3 source in candidates)
            {
                if (Vector2.Distance(XZ(source),XZ(dock)) > MaximumApproachLength) continue;
                Try(new[] {source,dock});
                foreach (float sign in new[] {-1f,1f})
                {
                    Vector3 corner = bench.Position+side*(sign*2.25f)+bench.Forward*1.45f;
                    Try(new[] {source,corner,dock});
                    Vector3 behind=bench.Position+side*(sign*2.25f)-bench.Forward*1.7f;
                    Try(new[] {source,behind,corner,dock});
                }
            }
            return best ?? FindTerrainRoute(candidates,dock);

            void Consider(IReadOnlyList<Vector3> points)
            {
                for(int i=1;i<points.Count;i++)
                {
                    Vector2 a=XZ(points[i-1]),b=XZ(points[i]), delta=b-a;
                    float t=delta.sqrMagnitude>.0001f?Mathf.Clamp01(Vector2.Dot(XZ(dock)-a,delta)/delta.sqrMagnitude):0f;
                    foreach(float shift in new[] {0f,-3f,3f,-6f,6f})
                    {
                        float at=Mathf.Clamp01(t+shift/Mathf.Max(.01f,delta.magnitude));
                        Vector3 p=Vector3.Lerp(points[i-1],points[i],at);
                        if((XZ(p)-XZ(dock)).sqrMagnitude<=MaximumApproachLength*MaximumApproachLength &&
                            !candidates.Exists(other=>(other-p).sqrMagnitude<.5f)) candidates.Add(p);
                    }
                }
            }
            void Try(IReadOnlyList<Vector3> corners)
            {
                float length=0f;
                for(int i=1;i<corners.Count;i++) length+=Vector2.Distance(XZ(corners[i-1]),XZ(corners[i]));
                if(length>=shortest || length>MaximumApproachLength) return;
                var sampled=SampleApproach(corners);
                if(sampled.Count==0) return;
                best=sampled; shortest=length;
            }
        }

        private List<Vector3> SampleApproach(IReadOnlyList<Vector3> corners)
        {
            var sampled=new List<Vector3>();
            for(int i=1;i<corners.Count;i++)
                if(!CheckSegment(corners[i-1],corners[i],sampled,i==1)) return NoApproach;
            return sampled;
        }

        // Search probes carry no arrays, lists, closures or interface enumerators.
        // Materialize only the selected route, once its contact checks succeed.
        private bool CheckSegment(Vector3 from,Vector3 to,List<Vector3> output=null,bool includeFirst=true)
        {
            Vector2 a=XZ(from),b=XZ(to),delta=b-a;
            int steps=Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/ProbeStep));
            Vector2 right=new Vector2(delta.y,-delta.x).normalized*(PathWidth*.5f);
            float slope=Mathf.Tan(40f*Mathf.Deg2Rad);
            Vector3 previous=default;
            for(int j=0;j<=steps;j++)
            {
                Vector2 p=Vector2.Lerp(a,b,(float)j/steps);
                if(!IsPathPoint(p,out float y) || !TryTop(p-right,out float left) ||
                    !TryTop(p+right,out float rightY) || Mathf.Abs(rightY-left)>PathWidth*slope) return false;
                var next=new Vector3(p.x,y,p.y);
                if(j>0 && Mathf.Abs(previous.y-y)>Vector2.Distance(XZ(previous),p)*slope+.025f) return false;
                previous=next;
                if(output!=null && (includeFirst || j>0)) output.Add(next);
            }
            return true;
        }

        private bool IsPathPoint(Vector2 p,out float y)
        {
            if(!TryTop(p,out y) || !walk.Contains(new Vector3(p.x,y,p.y),.34f)) return false;
            for(int i=0;i<protectedAreas.Count;i++)
            {
                Rect area=protectedAreas[i];
                if(Rect.MinMaxRect(area.xMin-.81f,area.yMin-.81f,area.xMax+.81f,area.yMax+.81f).Contains(p)) return false;
            }
            return true;
        }

        // A flat pad may have a steep downhill fade, while a comfortable route
        // follows its side. Search only when the inexpensive measured doglegs
        // fail. This finite graph is baked once per layout, never in Update.
        private List<Vector3> FindTerrainRoute(List<Vector3> candidates,Vector3 dock)
        {
            const float cell=.75f;
            const int half=40,width=half*2+1,maximumExpanded=2048;
            var goals=new List<Vector3>(32);
            foreach(Vector3 candidate in candidates)
            {
                if(IsPathPoint(XZ(candidate),out _) && !goals.Exists(p=>(p-candidate).sqrMagnitude<1f)) goals.Add(candidate);
                if(goals.Count==32) break;
            }
            if(goals.Count==0) return NoApproach;
            int count=width*width,start=half*width+half;
            var costs=new float[count];var parents=new int[count];var heights=new float[count];
            var validity=new byte[count];var closed=new bool[count];
            for(int i=0;i<count;i++) {costs[i]=float.PositiveInfinity;parents[i]=-1;}
            costs[start]=0f;heights[start]=dock.y;validity[start]=1;
            var open=new RouteHeap();open.Push(start,Heuristic(dock));
            int expanded=0;
            while(open.Count>0 && expanded<maximumExpanded)
            {
                int current=open.Pop();
                if(closed[current]) continue;
                expanded++;SearchExpandedNodes++;
                closed[current]=true;
                Vector3 from=Point(current);
                for(int goalIndex=0;goalIndex<goals.Count;goalIndex++)
                {
                    Vector3 goal=goals[goalIndex];
                    if((XZ(from)-XZ(goal)).sqrMagnitude>2.25f) continue;
                    if(!CheckSegment(goal,from)) continue;
                    var path=new List<Vector3> {goal};
                    for(int at=current;at>=0;at=parents[at]) path.Add(Point(at));
                    // Visibility simplification gives a small, clean footpath
                    // rather than presenting the search grid in the scenery.
                    var corners=new List<Vector3> {path[0]};
                    for(int at=0;at+1<path.Count;)
                    {
                        int next=path.Count-1;
                        while(next>at+1 && !CheckSegment(path[at],path[next])) next--;
                        corners.Add(path[next]);at=next;
                    }
                    float length=0f;
                    for(int i=1;i<corners.Count;i++) length+=Vector2.Distance(XZ(corners[i-1]),XZ(corners[i]));
                    if(length<=MaximumApproachLength) return SampleApproach(corners);
                }
                int x=current%width,z=current/width;
                for(int dz=-1;dz<=1;dz++)
                for(int dx=-1;dx<=1;dx++)
                {
                    if(dx==0 && dz==0) continue;
                    int nx=x+dx,nz=z+dz;
                    if(nx<0||nz<0||nx>=width||nz>=width) continue;
                    int next=nz*width+nx;
                    if(closed[next]) continue;
                    Vector2 p=XZ(dock)+new Vector2(nx-half,nz-half)*cell;
                    if((p-XZ(dock)).sqrMagnitude>MaximumApproachLength*MaximumApproachLength) continue;
                    if(validity[next]==0) validity[next]=IsPathPoint(p,out heights[next])?(byte)1:(byte)2;
                    if(validity[next]!=1) continue;
                    Vector3 destination=Point(next);
                    float move=Vector2.Distance(XZ(from),p);
                    float cost=costs[current]+move+Mathf.Abs(destination.y-from.y)*.6f;
                    if(cost>=costs[next] || cost>MaximumApproachLength*1.8f) continue;
                    if(!CheckSegment(from,destination)) continue;
                    costs[next]=cost;parents[next]=current;
                    open.Push(next,cost+Heuristic(destination));
                }
            }
            return NoApproach;

            Vector3 Point(int index)=>new(dock.x+(index%width-half)*cell,heights[index],dock.z+(index/width-half)*cell);
            float Heuristic(Vector3 p)
            {
                float result=float.PositiveInfinity;
                for(int i=0;i<goals.Count;i++) result=Mathf.Min(result,Vector2.Distance(XZ(p),XZ(goals[i])));
                return result;
            }
        }

        private sealed class RouteHeap
        {
            private readonly List<(int Index,float Score)> entries=new();
            internal int Count=>entries.Count;
            internal void Push(int index,float score)
            {
                int at=entries.Count;entries.Add((index,score));
                while(at>0)
                {
                    int parent=(at-1)/2;
                    if(entries[parent].Score<=score) break;
                    entries[at]=entries[parent];at=parent;
                }
                entries[at]=(index,score);
            }
            internal int Pop()
            {
                int result=entries[0].Index,last=entries.Count-1;
                var value=entries[last];entries.RemoveAt(last);
                if(last==0) return result;
                int at=0;
                while(at*2+1<entries.Count)
                {
                    int child=at*2+1;
                    if(child+1<entries.Count && entries[child+1].Score<entries[child].Score) child++;
                    if(entries[child].Score>=value.Score) break;
                    entries[at]=entries[child];at=child;
                }
                entries[at]=value;return result;
            }
        }

        private bool TryTop(Vector2 p,out float top)
        {
            if(terrace.TrySampleTop(p,out top)) return true;
            for(int i=0;i<roads.Count;i++)
                if(roads[i].TrySampleTop(p,out top,out var kind)) return kind==CityStreetSupportKind.Sidewalk;
            for(int i=0;i<quarter.Links.Count;i++)
                if(quarter.Links[i].TrySampleTop(p,out top,out var kind)) return kind==CityStreetSupportKind.Sidewalk;
            return ground.TrySampleTop(p,out top);
        }

        private static Vector2 XZ(Vector3 p)=>new(p.x,p.z);
        private static float DistanceSquared(Vector2 p,Vector2 a,Vector2 b)
        {
            Vector2 delta=b-a;
            return (p-Vector2.Lerp(a,b,delta.sqrMagnitude<.0001f?0f:Mathf.Clamp01(Vector2.Dot(p-a,delta)/delta.sqrMagnitude))).sqrMagnitude;
        }
    }
}
