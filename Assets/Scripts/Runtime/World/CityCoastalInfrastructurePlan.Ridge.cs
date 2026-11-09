using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityElectricalServiceConnection
    {
        internal CityElectricalServiceConnection(string id, Vector3 start, Vector3 end, Quaternion rotation, string suppliedById, bool existing = false, Quaternion sourceRotation = default)
        { StableId = id; Start = start; End = end; Rotation = rotation; SuppliedById = suppliedById; IsExisting = existing; SourceRotation = sourceRotation; }
        public string StableId { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public Quaternion Rotation { get; }
        public string SuppliedById { get; }
        public bool IsExisting { get; }
        public Quaternion SourceRotation { get; }
    }

    public sealed partial class CityCoastalInfrastructurePlan
    {
        // Secondary insulated feeders use one authored cable, not three bare conductors.
        // They share the city's source and mesh; there is no separate disconnected hill network.
        private static void ExtendToRidge(CityLayout layout, IList<CityCoastalFacility> facilities,
            ClearanceContext clearance, List<Vector3> poles, List<Vector3> forwards,
            List<CityCoastalCableSpan> spans, List<Rect> solids, List<Vector3> inlets,
            List<CityElectricalServiceConnection> services)
        {
            var consumers = CityElectricalConsumerPlan.ForLayout(layout).Consumers;
            if (consumers.Count == 0) return;
            var survey = new RidgeSurvey(layout, consumers, clearance);
            var nodes = new List<Vector3>(512);
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                Sample(road.Path);
            foreach (var link in CityHillsideQuarterPlan.ForLayout(layout).Links)
                Sample(new CityRoadPath(new[] { Xz(link.WestGateWorld), Xz(link.EastGateWorld) }));
            // One candidate next to each legal facade gives low pavilions a short,
            // same-side drop instead of forcing their feed across the roadway.
            foreach (var consumer in consumers)
            foreach (var attachment in consumer.Attachments)
            {
                for (int distance = 4; distance <= 12; distance += 4)
                    Candidate(Xz(attachment.Position + attachment.Outward * distance));
            }
            nodes.Sort((a,b) => { int z = b.z.CompareTo(a.z); return z != 0 ? z : a.x.CompareTo(b.x); });
            var visibility = new Dictionary<(Vector3, Vector3), bool>();
            var pending = new List<CityElectricalConsumer>(consumers.Count);
            foreach (var consumer in consumers) if (consumer.SuppliedById == null) pending.Add(consumer);
            // Bottom to top, with stable addresses for ties. A new local consumer
            // cannot reshuffle the source street sample or change lower-city poles.
            pending.Sort((a,b) => { int z = b.Bounds.center.y.CompareTo(a.Bounds.center.y);
                return z != 0 ? z : string.CompareOrdinal(a.StableId,b.StableId); });
            foreach (var consumer in pending)
            {
                if(NeighbourFeed(consumer))continue;
                int source = -1; CityElectricalAttachment selected = default;
                float best = float.PositiveInfinity;
                for (int p = 0; p < poles.Count; p++)
                foreach (var attachment in consumer.Attachments)
                {
                    float length = (poles[p] + Vector3.up * PoleCableHeight - attachment.Position).sqrMagnitude;
                    if (length < best && DropClear(poles[p], attachment, consumer))
                    { source = p; selected = attachment; best = length; }
                }
                if (source < 0) Route(consumer, out source, out selected);
                if (source < 0) throw new InvalidOperationException("No connected electrical route to " + consumer.StableId + $" (poles={poles.Count}, spans={spans.Count}, services={services.Count}, candidates={nodes.Count})");
                Vector3 start = poles[source] + Vector3.up * PoleCableHeight;
                spans.Add(new CityCoastalCableSpan(start, selected.Position, .22f, true));
                inlets.Add(selected.Position);
                services.Add(new CityElectricalServiceConnection(consumer.StableId, start, selected.Position,
                    Quaternion.LookRotation(selected.Outward), null));
                Budget();
            }
            foreach (var consumer in consumers)
            {
                if (consumer.SuppliedById == null) continue;
                bool supplied = services.Exists(s => s.StableId == consumer.SuppliedById);
                if (!supplied) throw new InvalidOperationException("Missing upstream electrical consumer " + consumer.SuppliedById);
                // This cable already belongs to the authored cobbler yard. Register
                // the real edge, rather than rendering a duplicate in this mesh.
                services.Add(new CityElectricalServiceConnection(consumer.StableId, consumer.ExistingSource,
                    consumer.ExistingEnd, Quaternion.LookRotation((consumer.ExistingSource-consumer.ExistingEnd).normalized),
                    consumer.SuppliedById, true));
            }

            Debug.Log($"CITY ELECTRICAL: poles={poles.Count}, spans={spans.Count}, services={services.Count}, ridge={consumers.Count}.");

            bool NeighbourFeed(CityElectricalConsumer receiver)
            {
                CityElectricalConsumer supplier=null;
                CityElectricalAttachment output=default,input=default;
                float best=32f*32f;
                foreach(var candidate in consumers)
                {
                    if(candidate.Lot==null || !services.Exists(service=>service.StableId==candidate.StableId))continue;
                    foreach(var from in candidate.Attachments)
                    foreach(var to in receiver.Attachments)
                    {
                        Vector3 delta=to.Position-from.Position;float distance=delta.sqrMagnitude;
                        if(distance>=best || Vector3.Dot(delta,from.Outward)<.05f || Vector3.Dot(-delta,to.Outward)<.05f)continue;
                        var span=new CityCoastalCableSpan(from.Position,to.Position,.15f,true);
                        if(!survey.Clear(span,receiver,candidate) || !ClearSpan(layout,facilities,span,clearance))continue;
                        best=distance;supplier=candidate;output=from;input=to;
                    }
                }
                if(supplier==null)return false;
                spans.Add(new CityCoastalCableSpan(output.Position,input.Position,.15f,true));
                inlets.Add(input.Position);
                services.Add(new CityElectricalServiceConnection(receiver.StableId,output.Position,input.Position,
                    Quaternion.LookRotation(input.Outward),supplier.StableId,false,Quaternion.LookRotation(output.Outward)));
                Budget();return true;
            }

            void Sample(CityRoadPath path)
            {
                int stations = Mathf.CeilToInt(path.Length / 12f);
                for (int n = 0; n <= stations; n++)
                {
                    CityRoadSample sample = path.SampleDistance(path.Length * n / stations);
                    for (int side = -1; side <= 1; side += 2)
                        Candidate(sample.Position + sample.Right * (6.25f * side));
                }
            }
            void Candidate(Vector2 point)
            {
                if (nodes.Count >= 768 || !survey.Pole(point, out Vector3 position)) return;
                foreach (Vector3 existing in nodes) if ((existing-position).sqrMagnitude < 9f) return;
                nodes.Add(position);
            }
            bool DropClear(Vector3 pole, CityElectricalAttachment attachment, CityElectricalConsumer receiver)
            {
                Vector3 start = pole + Vector3.up * PoleCableHeight;
                if ((start-attachment.Position).sqrMagnitude > 48f*48f ||
                    Vector3.Dot(start-attachment.Position,attachment.Outward) < .35f) return false;
                return survey.Clear(new CityCoastalCableSpan(start,attachment.Position,.22f,true), receiver) &&
                    ClearSpan(layout,facilities,new CityCoastalCableSpan(start,attachment.Position,.22f,true),clearance);
            }
            bool Link(Vector3 a, Vector3 b)
            {
                if ((a-b).sqrMagnitude > MaximumSpanLength*MaximumSpanLength || (a-b).sqrMagnitude < 9f) return false;
                var key = (a,b);
                if (visibility.TryGetValue(key,out bool clear)) return clear;
                var span = new CityCoastalCableSpan(a+Vector3.up*PoleCableHeight,b+Vector3.up*PoleCableHeight,.55f,false);
                clear = survey.Clear(span,null) && ClearSpan(layout,facilities,span,clearance);
                visibility.Add(key,clear); return clear;
            }
            void Route(CityElectricalConsumer consumer, out int source, out CityElectricalAttachment attachment)
            {
                source = -1; attachment = default;
                int count = nodes.Count;
                var costs = new float[count]; var parents = new int[count]; var visited = new bool[count];
                var goals = new bool[count]; var pins = new CityElectricalAttachment[count];
                for(int n=0;n<count;n++)
                foreach(var pin in consumer.Attachments)
                    if(DropClear(nodes[n],pin,consumer)) {goals[n]=true;pins[n]=pin;break;}
                int goal=-1;
                // Lazy Dijkstra: reject blocked edges only on a proposed complete path.
                // Checking every nearby cable in advance wastes most build-time geometry work.
                while(true)
                {
                    for(int n=0;n<count;n++) {costs[n]=float.PositiveInfinity;parents[n]=int.MinValue;visited[n]=false;}
                    for(int n=0;n<count;n++)
                    for(int p=0;p<poles.Count;p++)
                    {
                        float distance=Vector3.Distance(poles[p],nodes[n]);
                        if(distance>MaximumSpanLength || distance<3f || distance>=costs[n] ||
                            (visibility.TryGetValue((poles[p],nodes[n]),out bool known) && !known))continue;
                        costs[n]=distance;parents[n]=-p-1;
                    }
                    goal=-1;
                    for(int iteration=0;iteration<count;iteration++)
                    {
                        int current=-1;float minimum=float.PositiveInfinity;
                        for(int n=0;n<count;n++)if(!visited[n] && costs[n]<minimum){minimum=costs[n];current=n;}
                        if(current<0)break;
                        visited[current]=true;
                        if(goals[current]){goal=current;attachment=pins[current];break;}
                        for(int n=0;n<count;n++)
                        {
                            float distance=Vector3.Distance(nodes[current],nodes[n]);
                            float cost=minimum+distance+60f;
                            if(visited[n] || distance>MaximumSpanLength || distance<3f || cost>=costs[n] ||
                                (visibility.TryGetValue((nodes[current],nodes[n]),out bool known) && !known))continue;
                            costs[n]=cost;parents[n]=current;
                        }
                    }
                    if(goal<0)break;
                    bool valid=true;
                    for(int node=goal;node>=0;)
                    {
                        int predecessor=parents[node];
                        Vector3 start=predecessor<0?poles[-predecessor-1]:nodes[predecessor];
                        if(!Link(start,nodes[node]))valid=false;
                        node=predecessor;
                    }
                    if(valid)break;
                }
                if(goal<0)return;
                var path=new List<int>();
                int at=goal;
                while(at>=0) {path.Add(at);at=parents[at];}
                int parent=-at-1;
                for(int step=path.Count-1;step>=0;step--)
                {
                    Vector3 point=nodes[path[step]];
                    Vector3 forward=point-poles[parent];forward.y=0f;forward.Normalize();
                    spans.Add(new CityCoastalCableSpan(poles[parent]+Vector3.up*PoleCableHeight,
                        point+Vector3.up*PoleCableHeight,.55f,false));
                    parent=poles.Count;poles.Add(point);forwards.Add(forward);solids.Add(PoleFootprint(point));
                    Budget();
                }
                source=parent;
            }
            void Budget()
            {
                if(poles.Count>MaximumPoleCount || spans.Count>MaximumCableSpanCount)
                    throw new InvalidOperationException($"Electrical network exceeds its budget: poles={poles.Count}, spans={spans.Count}, services={services.Count}.");
            }
        }

        private static Vector2 Xz(Vector3 point) => new Vector2(point.x,point.z);

        private sealed class RidgeSurvey
        {
            private readonly CityLayout layout;
            private readonly IReadOnlyList<CityElectricalConsumer> consumers;
            private readonly ClearanceContext clearance;
            private readonly CityHillsideGroundPlan ground;
            private readonly CityStreetSupportPlan paving;
            private readonly List<CityStreetSupportPatch> nearby = new List<CityStreetSupportPatch>(64);
            private readonly IReadOnlyList<CityHillsideFurniturePlacement> furniture;
            internal RidgeSurvey(CityLayout layout,IReadOnlyList<CityElectricalConsumer> consumers,ClearanceContext clearance)
            {
                this.layout=layout;this.consumers=consumers;this.clearance=clearance;
                ground=CityHillsideGroundPlan.ForLayout(layout);paving=CityStreetSupportPlan.ForLayout(layout);
                furniture=CityHillsideFurniturePlan.ForLayout(layout).Placements;
            }
            internal bool Pole(Vector2 point,out Vector3 position)
            {
                position=default;
                if(!ground.TrySampleTop(point,out float y))return false;
                Rect footprint=new Rect(point.x-.23f,point.y-.23f,.46f,.46f);
                foreach(var consumer in consumers) if(Expanded(consumer.Bounds,.4f).Overlaps(footprint))return false;
                foreach(var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
                    if(Expanded(house.ApproachBounds,.35f).Overlaps(footprint) || house.CourtBounds.Overlaps(footprint))return false;
                foreach(var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                    foreach(var area in stair.WalkableBounds) if(Expanded(area,.3f).Overlaps(footprint))return false;
                if(RidgeKioskSitePlan.TryCreate(layout,out var kiosk) && kiosk.ReservedBounds.Overlaps(footprint))return false;
                if(CityGlassReturnSitePlan.LandReservation.Overlaps(footprint))return false;
                var loft=CityPigeonLoftPlan.ForLayout(layout);
                if(loft.IsEnabled && loft.Courtyard.Bounds.Overlaps(footprint))return false;
                foreach(var area in CityPawnshopDescentPlan.ForLayout(layout).Footprints)
                    if(CityRoadPolygon.Bounds(area).Overlaps(footprint))return false;
                foreach(var item in furniture)
                    if(Expanded(item.Bounds,.4f).Overlaps(footprint) || item.AccessBounds.Overlaps(footprint))return false;
                foreach(Rect reservation in clearance.Furniture)if(Expanded(reservation,.35f).Overlaps(footprint))return false;
                foreach(var site in CityHillsideSiteDetails.ForLayout(layout))
                    if(site.Bounds.Overlaps(footprint))return false;
                nearby.Clear();paving.GetNearby(footprint,nearby);
                foreach(var patch in nearby)if(patch.Bounds.Overlaps(footprint))return false;
                // Preserve actual contact on the finished triangles, not the analytic slope.
                for(int c=0;c<4;c++)
                {
                    Vector2 corner=new Vector2((c&1)==0?footprint.xMin:footprint.xMax,(c&2)==0?footprint.yMin:footprint.yMax);
                    if(!ground.TrySampleTop(corner,out float top) || Mathf.Abs(top-y)>.24f)return false;
                }
                position=new Vector3(point.x,y-.04f,point.y);
                // A hill pole needs a real service pocket. Narrow gaps beside glazed
                // bays and galleries are better supplied by a wall-to-wall lead.
                return clearance.Geometry.PoleClear(position,2f);
            }
            internal bool Clear(CityCoastalCableSpan span,CityElectricalConsumer receiver,CityElectricalConsumer supplier=null)
            {
                Rect bounds=Rect.MinMaxRect(Mathf.Min(span.Start.x,span.End.x)-.03f,Mathf.Min(span.Start.z,span.End.z)-.03f,
                    Mathf.Max(span.Start.x,span.End.x)+.03f,Mathf.Max(span.Start.z,span.End.z)+.03f);
                nearby.Clear();paving.GetNearby(bounds,nearby);
                int samples=Mathf.Max(4,Mathf.CeilToInt(Vector3.Distance(span.Start,span.End)/.75f));
                for(int s=1;s<samples;s++)
                {
                    float t=s/(float)samples;Vector3 p=span.Sample(t);Vector2 xz=Xz(p);
                    if(ground.TrySampleTop(xz,out float groundY) && p.y<groundY+2.7f && (receiver==null || Vector3.Distance(p,span.End)>2f))return false;
                    foreach(var patch in nearby)
                    {
                        if(patch.Kind!=CityStreetSupportKind.Asphalt || !patch.TrySampleTop(xz,out float top))continue;
                        if(p.y-top<MinimumVehicleWireHeight+.03f)return false;
                    }
                }
                return true;
            }
        }
    }
}
