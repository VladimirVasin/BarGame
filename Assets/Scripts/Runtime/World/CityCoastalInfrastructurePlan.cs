using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityCoastalFacility
    {
        internal CityCoastalFacility(string id, string assembly, Vector3 position, Vector3 size, Quaternion rotation)
        { StableId = id; Assembly = assembly; Position = position; Size = size; Rotation = rotation; }
        public string StableId { get; }
        public string Id => StableId;
        public string Assembly { get; }
        public Vector3 Position { get; }
        public Vector3 Size { get; }
        public Quaternion Rotation { get; }
        public Rect Footprint
        {
            get
            {
                // Measured render envelope, including asymmetric door/pipe
                // and roof relief. The wall collider retains its logical size.
                Vector2 low,high;
                if(Assembly=="Substation") {low=new Vector2(-2.56f,-2.06f);high=new Vector2(2.64f,2.12f);}
                else if(Assembly=="MunicipalWorkshop") {low=new Vector2(-3.76f,-2.16f);high=new Vector2(3.76f,2.21f);}
                else {low=new Vector2(-3.06f,-2.26f);high=new Vector2(3.06f,2.31f);}
                Vector3 center = Position + Rotation * new Vector3((low.x+high.x)*.5f,0f,(low.y+high.y)*.5f);
                Vector3 right = Rotation * Vector3.right, forward = Rotation * Vector3.forward;
                float halfX = Mathf.Abs(right.x)*(high.x-low.x)*.5f + Mathf.Abs(forward.x)*(high.y-low.y)*.5f;
                float halfZ = Mathf.Abs(right.z)*(high.x-low.x)*.5f + Mathf.Abs(forward.z)*(high.y-low.y)*.5f;
                return Rect.MinMaxRect(center.x-halfX-.08f,center.z-halfZ-.08f,
                    center.x+halfX+.08f,center.z+halfZ+.08f);
            }
        }
    }

    public readonly struct CityCoastalCableSpan
    {
        internal CityCoastalCableSpan(Vector3 start, Vector3 end, float sag, bool service)
        { Start = start; End = end; Sag = sag; IsServiceDrop = service; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public float Sag { get; }
        public bool IsServiceDrop { get; }
        public Vector3 Sample(float t) => Vector3.Lerp(Start, End, t) -
            Vector3.up * (Sag * 4f * t * (1f - t));
    }

    /// <summary>One immutable bounded electrical graph and three utility sites per layout.
    /// Pure placement never asks a navigation/world-composition owner for its own inputs.</summary>
    public sealed class CityCoastalInfrastructurePlan
    {
        public const int ExpectedFacilityCount = 3;
        public const int MaximumPoleCount = 64;
        public const int MaximumCableSpanCount = 192;
        public const float PoleCableHeight = 8.14f;
        public const float MinimumVehicleWireHeight = 7f;
        // The .38 m ground collar clears the public pavement's 5 m outer
        // edge, while the .65 m crossarm tips fit the recessed street wall.
        private const float PoleSideOffset = 5.35f;
        private const float MaximumSpanLength = 64f;
        private const int MaximumPoleCandidateCount = MaximumPoleCount * 4;
        private static readonly ConditionalWeakTable<CityLayout, CityCoastalInfrastructurePlan> Cache =
            new ConditionalWeakTable<CityLayout, CityCoastalInfrastructurePlan>();

        private CityCoastalInfrastructurePlan(IList<CityCoastalFacility> facilities,
            IList<Vector3> poles, IList<CityCoastalCableSpan> spans, IList<Rect> solids,
            IList<Vector3> inlets, IList<Vector3> forwards)
        {
            Facilities = new ReadOnlyCollection<CityCoastalFacility>(facilities);
            Poles = new ReadOnlyCollection<Vector3>(poles);
            CableSpans = new ReadOnlyCollection<CityCoastalCableSpan>(spans);
            SolidFootprints = new ReadOnlyCollection<Rect>(solids);
            FacadeInlets = new ReadOnlyCollection<Vector3>(inlets);
            PoleForwards = new ReadOnlyCollection<Vector3>(forwards);
        }
        public IReadOnlyList<CityCoastalFacility> Facilities { get; }
        public IReadOnlyList<Vector3> Poles { get; }
        public IReadOnlyList<CityCoastalCableSpan> CableSpans { get; }
        public IReadOnlyList<Rect> SolidFootprints { get; }
        public IReadOnlyList<Vector3> FacadeInlets { get; }
        public IReadOnlyList<Vector3> PoleForwards { get; }
        public bool IsEnabled => Facilities.Count != 0;

        public static CityCoastalInfrastructurePlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Cache.GetValue(layout, value => CreateUncached(value,
                value.SpatialPlan.IsCompactCoastal
                    ? CityFringeYardPlanner.Create(value, CityMountainBoundaryPlanner.Create(value))
                    : CityFringeYardPlan.Empty));
        }
        public static CityCoastalInfrastructurePlan Create(CityLayout layout, CityFringeYardPlan fringe)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (fringe == null) throw new ArgumentNullException(nameof(fringe));
            return Cache.GetValue(layout, value => CreateUncached(value, fringe));
        }

        private static CityCoastalInfrastructurePlan CreateUncached(CityLayout layout, CityFringeYardPlan fringe)
        {
            var facilities = new List<CityCoastalFacility>(ExpectedFacilityCount);
            var poles = new List<Vector3>(MaximumPoleCount);
            var spans = new List<CityCoastalCableSpan>(MaximumCableSpanCount);
            var solids = new List<Rect>(MaximumPoleCount + ExpectedFacilityCount);
            var inlets = new List<Vector3>(24);
            var forwards = new List<Vector3>(MaximumPoleCount);
            var connections = new List<Vector2Int>(MaximumPoleCount);
            if (!fringe.IsEnabled || !layout.SpatialPlan.IsCompactCoastal)
                return new CityCoastalInfrastructurePlan(facilities, poles, spans, solids, inlets, forwards);

            var clearance = new ClearanceContext(layout);
            AddFacility(layout, fringe, facilities, solids, clearance, "coastal-substation", "Substation",
                new Vector3(5f, 3.7f, 4f), CityFringeYardKind.WestIndustrialBelt, .65f);
            AddFacility(layout, fringe, facilities, solids, clearance, "coastal-municipal-workshop", "MunicipalWorkshop",
                new Vector3(7.4f, 4.4f, 4.2f), CityFringeYardKind.WestIndustrialBelt, .20f);
            AddFacility(layout, fringe, facilities, solids, clearance, "coastal-port-workshop", "PortWorkshop",
                new Vector3(6f, 4.2f, 4.4f), CityFringeYardKind.WestStoneTerraces, .75f);

            var visibility = new Dictionary<(Vector3, Vector3, Vector3), bool>();
            var candidates = new List<Vector3>(MaximumPoleCandidateCount);
            CityCoastalFacility station = facilities[0];
            Vector3 feedAnchor = FeedAnchor(station);
            Vector2 feedXZ = new Vector2(feedAnchor.x,feedAnchor.z);
            // Road-side poles are deliberately between junctions. They stand
            // beyond the full public sidewalk, never at the old traffic inset.
            // Reserve a bounded source stratum before the whole-town sample;
            // an index stride must not omit the one edge beside the feed yard.
            var nearEdges = new List<int>(layout.RoadEdges.Count);
            for(int e=0;e<layout.RoadEdges.Count;e++)nearEdges.Add(e);
            nearEdges.Sort((a,b)=>
            {
                float first=layout.RoadGeometry.Get(layout.RoadEdges[a]).Project(feedXZ).DistanceSquared;
                float second=layout.RoadGeometry.Get(layout.RoadEdges[b]).Project(feedXZ).DistanceSquared;
                int order=first.CompareTo(second);return order==0?a.CompareTo(b):order;
            });
            for(int n=0;n<Mathf.Min(12,nearEdges.Count) && candidates.Count<32;n++)
                AddRoadCandidates(layout,fringe,facilities,candidates,clearance,layout.RoadEdges[nearEdges[n]],32);
            int edgeCount=Mathf.Min(80,layout.RoadEdges.Count);
            for(int e=0;e<edgeCount && candidates.Count<MaximumPoleCandidateCount;e++)
            {
                int index=edgeCount==1?0:Mathf.RoundToInt(e*(layout.RoadEdges.Count-1)/(float)(edgeCount-1));
                AddRoadCandidates(layout,fringe,facilities,candidates,clearance,layout.RoadEdges[index],MaximumPoleCandidateCount);
            }
            // A small feed mast is part of the substation yard. It makes the
            // common graph start at a physical source rather than at a random pole.
            if (!TryGround(layout, feedXZ, out float feedY))
                throw new InvalidOperationException("Coastal substation feed lacks terrain support.");
            Vector3 feed = new Vector3(feedXZ.x, feedY - .04f, feedXZ.y);
            poles.Add(feed); forwards.Add(Vector3.zero);
            solids.Add(PoleFootprint(feed));
            spans.Add(new CityCoastalCableSpan(feed + Vector3.up * PoleCableHeight,
                station.Position + station.Rotation * new Vector3(2.46f, 2.12f, -.5f), .18f, true));
            int initialCandidates=candidates.Count,sourceLinks=0;
            var sourceRejected=new int[4];
            Vector3 nearestCandidate=default;float nearestSquared=float.PositiveInfinity;
            for(int c=0;c<candidates.Count;c++)
                if((candidates[c]-feed).sqrMagnitude<nearestSquared)
                {nearestSquared=(candidates[c]-feed).sqrMagnitude;nearestCandidate=candidates[c];}

            // Bounded Prim growth over visible, obstacle-free overhead links.
            // An unusable candidate is omitted together with its pole; no fake
            // isolated wire network is placed through a house or park frontage.
            while (poles.Count < MaximumPoleCount && candidates.Count > 0)
            {
                int bestCandidate = -1, bestParent = -1;
                float bestDistance = MaximumSpanLength * MaximumSpanLength;
                for (int c = 0; c < candidates.Count; c++)
                for (int p = 0; p < poles.Count; p++)
                {
                    float distance = (candidates[c] - poles[p]).sqrMagnitude;
                    if (distance >= bestDistance || distance < 16f) continue;
                    Vector3 direction = candidates[c] - poles[p];
                    direction.y=0f;direction.Normalize();
                    Vector3 parentForward = forwards[p] == Vector3.zero ? direction : forwards[p];
                    var key = (poles[p], candidates[c], parentForward);
                    if (!visibility.TryGetValue(key, out bool clear))
                    {
                        clear = ClearBundle(layout,facilities,clearance,poles[p],candidates[c],parentForward,direction,out CableRejection rejection);
                        visibility.Add(key, clear);
                        if(poles.Count==1){sourceLinks++;if(!clear)sourceRejected[(int)rejection]++;}
                    }
                    if (!clear) continue;
                    bestDistance = distance; bestCandidate = c; bestParent = p;
                }
                if (bestCandidate < 0 || connections.Count * 3 + 4 > MaximumCableSpanCount - 24) break;
                Vector3 next = candidates[bestCandidate];
                connections.Add(new Vector2Int(bestParent,poles.Count));
                Vector3 forward = next-poles[bestParent];forward.y=0f;forward.Normalize();
                if(forwards[bestParent]==Vector3.zero)forwards[bestParent]=forward;
                forwards.Add(forward);
                poles.Add(next); solids.Add(PoleFootprint(next)); candidates.RemoveAt(bestCandidate);
                // Street stations are placement alternatives, not a mandate
                // for several poles along one small block.
                for(int c=candidates.Count-1;c>=0;c--)
                    if((new Vector2(candidates[c].x-next.x,candidates[c].z-next.z)).sqrMagnitude<144f)
                        candidates.RemoveAt(c);
            }
            if (poles.Count < 2)
                throw new InvalidOperationException($"Coastal electrical graph cannot leave its substation: candidates={initialCandidates}, sourceLinks={sourceLinks}, building={sourceRejected[1]}, facility={sourceRejected[2]}, vehicle={sourceRejected[3]}, feed={feed}, nearest={nearestCandidate}, distance={Mathf.Sqrt(nearestSquared):F2}.");

            for (int c = 0; c < connections.Count; c++)
            {
                Vector2Int link = connections[c];
                Vector3 a = forwards[link.x], b = forwards[link.y];
                Vector3 rightA = new Vector3(a.z,0f,-a.x), rightB = new Vector3(b.z,0f,-b.x);
                // Each end is attached to its own pole's ceramic crossarm.
                // Branching may change orientation; ends never hang in air.
                if (Vector3.Dot(rightA,rightB)<0f) rightB = -rightB;
                for (int conductor = -1; conductor <= 1; conductor++)
                    spans.Add(new CityCoastalCableSpan(poles[link.x] + Vector3.up * PoleCableHeight + rightA*(conductor*.65f),
                        poles[link.y] + Vector3.up * PoleCableHeight + rightB*(conductor*.65f),.65f,false));
            }

            // Service drops are finite and visible. A source only supplies a
            // facade when the whole cable stays outside authoritative solids.
            for (int i = 0; i < layout.BuildingLots.Count && inlets.Count < 24; i++)
            {
                BuildingLot lot = layout.BuildingLots[i];
                if (!lot.IsOrdinaryBuilding || !lot.HasRoadFrontage) continue;
                Vector3 end = lot.DoorPosition + lot.FacadeForward * .18f + Vector3.up * Mathf.Min(6.25f,lot.Height-1f);
                int nearest = -1; float distance = 19f * 19f;
                for (int p = 0; p < poles.Count; p++)
                {
                    float squared = (poles[p] - end).sqrMagnitude;
                    if (squared >= distance) continue;
                    var drop = new CityCoastalCableSpan(poles[p] + Vector3.up * PoleCableHeight, end, .25f, true);
                    if (!ClearSpan(layout, facilities, drop, clearance, lot)) continue;
                    nearest = p; distance = squared;
                }
                if (nearest < 0) continue;
                spans.Add(new CityCoastalCableSpan(poles[nearest] + Vector3.up * PoleCableHeight, end, .25f, true));
                inlets.Add(end);
            }
            return new CityCoastalInfrastructurePlan(facilities, poles, spans, solids, inlets, forwards);
        }

        private static void AddFacility(CityLayout layout, CityFringeYardPlan fringe,
            IList<CityCoastalFacility> facilities, IList<Rect> solids, ClearanceContext clearance, string id, string assembly,
            Vector3 size, CityFringeYardKind preferred, float along)
        {
            bool found = false; Vector3 best = default; float bestScore = float.PositiveInfinity;
            Quaternion bestRotation = Quaternion.identity;
            int tested=0,blockedRoad=0,blockedSolid=0,unsupported=0;
            for (int y = 0; y < fringe.Yards.Count; y++)
            {
                CityFringeYardDescriptor yard = fringe.Yards[y];
                if (yard.Kind != preferred) continue;
                Rect area = yard.AreaBounds;
                // Keep the long wall along the service street, with its doors
                // facing roadward. The west sheet has only eight usable metres.
                Quaternion rotation = Quaternion.Euler(0f,90f,0f);
                Rect local = new CityCoastalFacility(id,assembly,Vector3.zero,size,rotation).Footprint;
                const float maintenanceGap=.35f;
                float xMin=area.xMin+maintenanceGap-local.xMin;
                // AreaBounds already omits the half-road. Preserve the same
                // 2.2 m public-road exclusion when deriving the candidate range.
                float xMax=area.xMax-2.2f-local.xMax;
                float zMin=area.yMin+maintenanceGap-local.yMin;
                float zMax=area.yMax-maintenanceGap-local.yMax;
                if(xMax<xMin || zMax<zMin)continue;
                Vector2 desired = new Vector2(area.center.x, Mathf.Lerp(area.yMin, area.yMax, along));
                int zCount=Mathf.Clamp(Mathf.CeilToInt((zMax-zMin)/1.25f)+1,2,128);
                // Include both exact admissible edges. A fixed grid phase can
                // otherwise skip the entire narrow interval beside a wall.
                for (int zi=0;zi<zCount;zi++)
                for (int xi=0;xi<6;xi++)
                {
                    tested++;
                    float x=Mathf.Lerp(xMin,xMax,xi/5f),z=Mathf.Lerp(zMin,zMax,zi/(float)(zCount-1));
                    Rect footprint = new CityCoastalFacility(id,assembly,new Vector3(x,0f,z),size,rotation).Footprint;
                    if(NearRoad(layout,footprint,2.2f)){blockedRoad++;continue;}
                    if (footprint.Overlaps(Expanded(yard.TraversalBounds, .65f)) ||
                        OverlapsAny(solids, Expanded(footprint, 1.2f)) ||
                        OverlapsBuildings(clearance, footprint) || OverlapsAny(clearance.Furniture,Expanded(footprint,.4f)))
                    {blockedSolid++;continue;}
                    bool blocked = false;
                    for (int p = 0; p < yard.Parts.Count; p++)
                    {
                        CityFringeYardPartDescriptor part = yard.Parts[p];
                        if (!part.BlocksMovement) continue;
                        // Footprint includes the authored part's rotation;
                        // its raw Size axes are not a world-space rectangle.
                        if (footprint.Overlaps(Expanded(part.Footprint,.30f))) { blocked = true; break; }
                    }
                    if(blocked){blockedSolid++;continue;}
                    if(!TryFoundation(layout,footprint,out float datum)){unsupported++;continue;}
                    if(assembly=="Substation")
                    {
                        var station = new CityCoastalFacility(id,assembly,new Vector3(x,datum-.05f,z),size,rotation);
                        Rect feed = PoleFootprint(FeedAnchor(station));
                        if(feed.xMin<area.xMin || feed.xMax>area.xMax || feed.yMin<area.yMin || feed.yMax>area.yMax ||
                            feed.Overlaps(Expanded(yard.TraversalBounds,.65f)) || NearRoad(layout,feed,2.2f) ||
                            OverlapsBuildings(clearance,feed) || OverlapsAny(clearance.Furniture,Expanded(feed,.4f)))
                        {blockedSolid++;continue;}
                        for(int p=0;p<yard.Parts.Count;p++)
                            if(yard.Parts[p].BlocksMovement && feed.Overlaps(Expanded(yard.Parts[p].Footprint,.30f)))
                            {blocked=true;break;}
                        if(blocked){blockedSolid++;continue;}
                        if(!TryFoundation(layout,feed,out _)){unsupported++;continue;}
                    }
                    float score = (new Vector2(x,z)-desired).sqrMagnitude;
                    if (score >= bestScore) continue;
                    found = true; bestScore = score; best = new Vector3(x,datum-.05f,z);bestRotation=rotation;
                }
            }
            if (!found) throw new InvalidOperationException($"No supported coastal utility site for {id}: tested={tested}, road={blockedRoad}, solid={blockedSolid}, unsupported={unsupported}.");
            var facility = new CityCoastalFacility(id, assembly, best, size,bestRotation);
            facilities.Add(facility); solids.Add(facility.Footprint);
        }

        private static void AddRoadCandidates(CityLayout layout,CityFringeYardPlan fringe,
            IList<CityCoastalFacility> facilities,IList<Vector3> candidates,ClearanceContext clearance,RoadEdge edge,int maximum)
        {
            if(layout.TryGetBridge(edge,out _) || layout.GetPathKind(edge)!=CityPathKind.Street)return;
            CityRoadPath path=layout.RoadGeometry.Get(edge);
            if(path.Length<18f)return;
            int count=Mathf.Clamp(Mathf.CeilToInt(path.Length/13f)+1,3,6);
            for(int i=0;i<count && candidates.Count<maximum;i++)
            {
                // Three distinct stations on a 26 m street are 9, 13 and 17 m.
                // The centre alone coincides with its facade's door approach.
                float along=Mathf.Lerp(9f,path.Length-9f,i/(float)(count-1));
                CityRoadSample sample=path.SampleDistance(along);
                for(int side=-1;side<=1 && candidates.Count<maximum;side+=2)
                {
                    Vector2 xz=sample.Position+sample.Right*(PoleSideOffset*side);
                    if(TryPole(layout,fringe,facilities,candidates,clearance,xz,out Vector3 pole))candidates.Add(pole);
                }
            }
        }

        private static bool TryPole(CityLayout layout, CityFringeYardPlan fringe,
            IList<CityCoastalFacility> facilities, IList<Vector3> candidates, ClearanceContext clearance, Vector2 xz, out Vector3 pole)
        {
            pole = default;
            if (!CityTerrainSurfacePlan.TrySampleGroundTop(layout,xz,out float top,out CitySurfaceDescriptor surface))return false;
            CityFringeYardDescriptor yard=null;
            if(surface.Kind==CitySurfaceKind.OpenGround)
                for(int y=0;y<fringe.Yards.Count;y++)
                    if(fringe.Yards[y].AreaId==surface.AreaId){yard=fringe.Yards[y];break;}
            if(surface.Kind!=CitySurfaceKind.BuildableGround && yard==null)return false;
            // The authored ground collar is .38 m wide. Building solids
            // already include the same .35 m separation used by the wires.
            Rect footprint = PoleFootprint(new Vector3(xz.x,0f,xz.y));
            if (OverlapsBuildings(clearance,footprint) ||
                OverlapsAny(clearance.Furniture,Expanded(footprint,.4f))) return false;
            for (int f=0;f<facilities.Count;f++) if (footprint.Overlaps(Expanded(facilities[f].Footprint,.7f))) return false;
            for (int p=0;p<candidates.Count;p++)
                if ((new Vector2(candidates[p].x,candidates[p].z)-xz).sqrMagnitude<16f) return false;
            for (int l=0;l<layout.BuildingLots.Count;l++)
                if ((new Vector2(layout.BuildingLots[l].SidewalkArrivalPosition.x,
                    layout.BuildingLots[l].SidewalkArrivalPosition.z)-xz).sqrMagnitude<6.25f) return false;
            for (int n=0;n<layout.Nodes.Count;n++)
            {
                Vector3 node=layout.GetNodeWorldPosition(layout.Nodes[n]);
                if ((new Vector2(node.x,node.z)-xz).sqrMagnitude<81f) return false;
            }
            for(int y=0;y<fringe.Yards.Count;y++)
                if (footprint.Overlaps(Expanded(fringe.Yards[y].TraversalBounds,.30f))) return false;
            if(yard!=null)
            {
                if(footprint.xMin<yard.AreaBounds.xMin || footprint.xMax>yard.AreaBounds.xMax ||
                    footprint.yMin<yard.AreaBounds.yMin || footprint.yMax>yard.AreaBounds.yMax)return false;
                for(int p=0;p<yard.Parts.Count;p++)
                    if(yard.Parts[p].BlocksMovement && footprint.Overlaps(Expanded(yard.Parts[p].Footprint,.30f)))return false;
            }
            if(!TryFoundation(layout,footprint,out top))return false;
            if(CityStreetSurfacePlanner.TrySampleEntranceForecourtTop(layout,xz,out float forecourtTop))top=forecourtTop;
            pole=new Vector3(xz.x,top-.04f,xz.y);return true;
        }

        private enum CableRejection {None,Building,Facility,Vehicle}
        private static bool ClearBundle(CityLayout layout,IList<CityCoastalFacility> facilities,
            ClearanceContext context,Vector3 start,Vector3 end,Vector3 forwardA,Vector3 forwardB,out CableRejection rejection)
        {
            rejection=CableRejection.None;
            Vector3 rightA=new Vector3(forwardA.z,0f,-forwardA.x);
            Vector3 rightB=new Vector3(forwardB.z,0f,-forwardB.x);
            if(Vector3.Dot(rightA,rightB)<0f)rightB=-rightB;
            for(int conductor=-1;conductor<=1;conductor++)
            {
                var span=new CityCoastalCableSpan(start+Vector3.up*PoleCableHeight+rightA*(conductor*.65f),
                    end+Vector3.up*PoleCableHeight+rightB*(conductor*.65f),.65f,false);
                if(!ClearSpan(layout,facilities,span,context,out rejection))return false;
            }
            return true;
        }

        private static bool ClearSpan(CityLayout layout, IList<CityCoastalFacility> facilities,
            CityCoastalCableSpan span, ClearanceContext context, BuildingLot receiver = null)
            =>ClearSpan(layout,facilities,span,context,out _,receiver);

        private static bool ClearSpan(CityLayout layout,IList<CityCoastalFacility> facilities,
            CityCoastalCableSpan span,ClearanceContext context,out CableRejection rejection,BuildingLot receiver=null)
        {
            rejection=CableRejection.None;
            int samples=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(span.Start,span.End)/1.5f),4,48);
            for(int i=1;i<samples;i++)
            {
                Vector3 point=span.Sample(i/(float)samples);
                Vector2 xz=new Vector2(point.x,point.z);
                for(int b=0;b<context.Solids.Count;b++)
                {
                    SolidVolume solid = context.Solids[b];
                    if (solid.Lot == receiver && i >= samples - 1) continue;
                    if (point.y <= solid.Top && solid.Footprint.Contains(xz)){rejection=CableRejection.Building;return false;}
                }
                for(int f=0;f<facilities.Count;f++)
                    if(point.y<facilities[f].Position.y+facilities[f].Size.y+.35f &&
                        facilities[f].Footprint.Contains(xz)){rejection=CableRejection.Facility;return false;}
                for(int e=0;e<layout.RoadEdges.Count;e++)
                {
                    RoadEdge edge=layout.RoadEdges[e];
                    CityRoadPath path=layout.RoadGeometry.Get(edge);
                    if (!Expanded(path.Bounds, layout.RoadWidth*.5f+.1f).Contains(xz)) continue;
                    CityRoadProjection projection=path.Project(xz);
                    float width = layout.RoadWidth*.5f+.1f;
                    if(projection.DistanceSquared>width*width) continue;
                    float datum=layout.ElevationPlan.SampleRoadDatum(edge,projection.DistanceAlong/path.Length);
                    if(point.y-datum<MinimumVehicleWireHeight){rejection=CableRejection.Vehicle;return false;}
                }
            }
            return true;
        }

        private readonly struct SolidVolume
        {
            internal SolidVolume(BuildingLot lot, Rect footprint)
            { Lot = lot; Footprint = Expanded(footprint, .35f); Top = lot.Center.y + lot.Height + .35f; }
            internal BuildingLot Lot { get; }
            internal Rect Footprint { get; }
            internal float Top { get; }
        }
        private sealed class ClearanceContext
        {
            internal readonly List<SolidVolume> Solids = new List<SolidVolume>();
            internal readonly List<Rect> Furniture = new List<Rect>();
            internal ClearanceContext(CityLayout layout)
            {
                CityBusPlan bus=CityBusPlanner.CreateRoadRouting(layout);
                for(int stop=0;stop<bus.Stops.Count;stop++)
                    Furniture.Add(CityBusStopWorldBuilder.DescribeFurnitureFootprint(bus.Stops[stop]));
                for (int i = 0; i < layout.BuildingMasses.Count; i++)
                {
                    BuildingLot lot = layout.BuildingMasses[i];
                    IReadOnlyList<Rect> footprints = lot.CreateCollisionFootprints();
                    for (int b = 0; b < footprints.Count; b++) Solids.Add(new SolidVolume(lot, footprints[b]));
                }
            }
        }

        private static bool TryFoundation(CityLayout layout, Rect rect, out float datum)
        {
            datum=float.PositiveInfinity;float highest=float.NegativeInfinity;
            for(int i=0;i<5;i++)
            {
                Vector2 point=i==4?rect.center:new Vector2((i&1)==0?rect.xMin:rect.xMax,(i&2)==0?rect.yMin:rect.yMax);
                if(!TryGround(layout,point,out float y))return false;
                datum=Mathf.Min(datum,y);highest=Mathf.Max(highest,y);
            }
            return highest-datum<=.24f;
        }
        private static bool TryGround(CityLayout layout, Vector2 point, out float y) =>
            CityTerrainSurfacePlan.TrySampleGroundTop(layout,point,out y,out _);
        private static bool OverlapsBuildings(ClearanceContext context,Rect rect)
        {
            for(int i=0;i<context.Solids.Count;i++)
                if(rect.Overlaps(context.Solids[i].Footprint))return true;
            return false;
        }
        private static bool NearRoad(CityLayout layout,Rect rect,float margin)
        {
            for(int e=0;e<layout.RoadEdges.Count;e++)
            {
                Rect road=layout.RoadGeometry.Get(layout.RoadEdges[e]).Bounds;
                if(rect.Overlaps(Expanded(road,layout.RoadWidth*.5f+margin)))return true;
            }
            return false;
        }
        private static bool OverlapsAny(IList<Rect> bounds,Rect rect)
        {for(int i=0;i<bounds.Count;i++)if(rect.Overlaps(bounds[i]))return true;return false;}
        private static Rect PoleFootprint(Vector3 p)=>new Rect(p.x-.2f,p.z-.2f,.4f,.4f);
        private static Vector3 FeedAnchor(CityCoastalFacility station)=>station.Position+
            station.Rotation*new Vector3(station.Size.x*.5f+.85f,0f,-.5f);
        private static Rect Expanded(Rect value,float amount)=>Rect.MinMaxRect(value.xMin-amount,value.yMin-amount,
            value.xMax+amount,value.yMax+amount);
    }
}
