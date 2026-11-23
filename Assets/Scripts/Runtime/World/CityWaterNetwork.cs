using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public enum CityWaterPipeKind { Supply, GravitySewer, ForceMain, CoastalDischarge, SpringFeed }
    public enum CityWaterKit { WallService, StopCover, SewerCover, PumpHeader, WaterBarrel, PipeSaddle, VentGrille, OutfallHead, OutfallStream,
        StreetBarrel, StreetBend, PipeCollar, WallClamp, GroundShoe, LineValve, StairBracket }

    public readonly struct CityWaterPipe
    {
        public readonly Vector3 From, To;
        public readonly CityWaterPipeKind Kind;
        internal CityWaterPipe(Vector3 from, Vector3 to, CityWaterPipeKind kind)
        { From = from; To = to; Kind = kind; }
    }

    public readonly struct CityWaterFixture
    {
        public readonly CityWaterKit Kind;
        public readonly Vector3 Position, Scale;
        public readonly Quaternion Rotation;
        internal CityWaterFixture(CityWaterKit kind, Vector3 point, Quaternion rotation, Vector3 scale)
        { Kind = kind; Position = point; Rotation = rotation; Scale = scale; }
    }

    public sealed class CityWaterService
    {
        public string Id { get; }
        public Vector3 Inlet { get; }
        public Vector3 Drain { get; }
        public bool OnRidge { get; }
        internal readonly int Tap, WaterTap;
        internal CityWaterService(string id, Vector3 inlet, Vector3 drain, int tap, int waterTap, bool onRidge)
        { Id = id; Inlet = inlet; Drain = drain; Tap = tap; WaterTap = waterTap; OnRidge = onRidge; }
    }

    /// <summary>Survey in City metres, shared by the additive district, balcony and editor.
    /// Supply uses street loops. Sewers drain along a directed tree to an isolated wet well.</summary>
    public sealed partial class CityWaterNetwork
    {
        private static readonly ConditionalWeakTable<CityLayout, CityWaterNetwork> Cache = new();
        private readonly List<CityWaterService> services = new();
        private readonly List<CityWaterPipe> pipes = new();
        private readonly List<CityWaterFixture> fixtures = new();
        private readonly List<Vector3> discharge = new();
        private readonly List<Vector3> lifts = new();
        public IReadOnlyList<Vector3> SewagePumpIntakes => lifts;
        private readonly List<Junction> junctions = new();
        private readonly List<Street> streets = new();
        private readonly HashSet<string> buildingIds = new(StringComparer.Ordinal);
        private int[] supplyParent, sewerParent;
        private int station;
        private readonly HashSet<Vector2Int> coveredStreets = new();
        public Vector3 Booster { get; private set; }
        public IReadOnlyList<CityWaterService> Services => services;
        public IReadOnlyList<CityWaterPipe> Pipes => pipes;
        public IReadOnlyList<CityWaterFixture> Fixtures => fixtures;
        public Vector3 Header { get; private set; }
        public Vector3 Outfall { get; private set; }
        public bool Enabled => services.Count != 0;

        private sealed class Junction
        {
            internal readonly Vector3 Surface;
            internal Vector3 Water, Sewer;
            internal readonly List<int> Links = new();
            internal Junction(Vector3 surface)
            {
                Surface = surface;
                Water = surface - Vector3.up * 1.15f;
                Sewer = surface + new Vector3(.52f, -2.3f, 0f);
            }
        }
        private sealed class Street
        {
            internal readonly int A, B;
            internal readonly bool Supply, Drain, Crossing, OnRidge;
            internal bool Active = true;
            internal Street(int a, int b, bool supply, bool drain, bool crossing, bool onRidge)
            { A = a; B = b; Supply = supply; Drain = drain; Crossing = crossing; OnRidge = onRidge; }
        }

        public static CityWaterNetwork ForLayout(CityLayout layout, CitySeacoastPlan coast = null,
            CityFringeYardPlan fringe = null) => Cache.GetValue(layout, l => new CityWaterNetwork(l, coast, fringe));

        private CityWaterNetwork(CityLayout layout, CitySeacoastPlan coast, CityFringeYardPlan fringe)
        {
            if (!layout.SpatialPlan.IsCompactCoastal) return;
            if (coast == null && CityWorldPlans.IsMemoised(layout))
            {
                var city = CityWorldPlans.GetOrCreate(layout);
                coast = city.Seacoast; fringe = city.FringeYard;
            }
            coast ??= CitySeacoastPlanner.Create(layout);
            var utilities = CityCoastalInfrastructurePlan.ForLayout(layout);
            if (coast == null || !utilities.IsEnabled) return;
            SurveyStreets(layout);
            CityCoastalFacility works = default;
            foreach (var facility in utilities.Facilities)
                if (facility.Assembly == "MunicipalWorkshop") works = facility;
            if (works.Assembly == null) throw new InvalidOperationException("Waterworks has no municipal workshop.");
            // Along the two gables, clear of the working doors at local +Z.
            var lateral = works.Rotation * Vector3.right;
            Header = works.Position + lateral * (works.Size.x * .5f + .20f);
            var headerRotation = works.Rotation * Quaternion.Euler(0, 90f, 0);
            Piece(CityWaterKit.PumpHeader, Header, headerRotation);
            int tap = TapStreet(Header);
            station = Node(Header - headerRotation * Vector3.right);
            junctions[station].Sewer = works.Position - lateral * (works.Size.x * .5f + .22f) - Vector3.up * 3.4f;
            junctions[station].Water = Header - headerRotation*Vector3.right + Vector3.up*.57f;
            Edge(station, tap, supply:false, crossing:true);
            SurveyWorksOutlet(works,tap);
            Piece(CityWaterKit.SewerCover, AtHeight(junctions[station].Sewer, works.Position.y + .055f), works.Rotation);
            SurveyBuildings(layout, coast, utilities, fringe);
            SurveyRetainingSupply(layout);
            BuildMains();
            PlaceAccessCovers();
            ConnectSourceAndSea(layout, coast, works);
        }

        private static Vector3 AtHeight(Vector3 v, float y) => new(v.x, y, v.z);
        private static Vector2 Xz(Vector3 p) => new(p.x, p.z);
        private int Node(Vector3 s) { junctions.Add(new Junction(s)); return junctions.Count - 1; }
        private void Edge(int a, int b, bool supply = true, bool drain = true, bool crossing = false, bool onRidge = false)
        {
            if (a == b) return;
            junctions[a].Links.Add(streets.Count);
            junctions[b].Links.Add(streets.Count);
            streets.Add(new Street(a, b, supply, drain, crossing, onRidge));
        }

        // Split the street at the actual perpendicular takeoff. Future connections can
        // share either resulting segment. Bridge spans and private service lines have no taps.
        private int TapStreet(Vector3 facade, bool onRidge = false)
        {
            int best = -1; float fraction = 0f, distance = float.PositiveInfinity;
            for (int i = 0; i < streets.Count; i++)
            {
                Street s = streets[i];
                if (!s.Active || s.Crossing || s.OnRidge != onRidge || !s.Drain || !s.Supply) continue;
                Vector3 a = junctions[s.A].Surface, d = junctions[s.B].Surface - a;
                float t = Mathf.Clamp01(Vector3.Dot(facade - a, d) / Mathf.Max(.001f, d.sqrMagnitude));
                Vector3 delta = facade - (a + t*d);
                float score = delta.sqrMagnitude + delta.y*delta.y*5f;
                if (score >= distance) continue;
                best = i; fraction = t; distance = score;
            }
            if (best < 0) throw new InvalidOperationException("No municipal main reachable from " + facade);
            Street link = streets[best];
            var left = junctions[link.A]; var right = junctions[link.B];
            float length = Vector3.Distance(left.Surface, right.Surface);
            if (length * fraction < .25f) return link.A;
            if (length * (1f-fraction) < .25f) return link.B;
            int tap = Node(Vector3.Lerp(left.Surface, right.Surface, fraction));
            junctions[tap].Water = Vector3.Lerp(left.Water, right.Water, fraction);
            junctions[tap].Sewer = Vector3.Lerp(left.Sewer, right.Sewer, fraction);
            link.Active = false;
            Edge(link.A, tap, onRidge:onRidge); Edge(tap, link.B, onRidge:onRidge);
            return tap;
        }

        private void PlaceAccessCovers()
        {
            // Lids are flush with the road crown, with enough space between nearby
            // service taps. Use the built road's slope at the station, including Ridge.
            foreach (var service in services)
            {
                var node = junctions[service.Tap];
                Vector3 p = node.Surface;
                if (!coveredStreets.Add(new Vector2Int(Mathf.FloorToInt(p.x/14f), Mathf.FloorToInt(p.z/14f)))) continue;
                Vector3 tangent = Vector3.forward;
                foreach (int index in node.Links)
                {
                    var edge = streets[index]; if (!edge.Active || edge.Crossing) continue;
                    Vector3 d = junctions[edge.A == service.Tap ? edge.B : edge.A].Surface - p;
                    if (d.sqrMagnitude > .04f) { tangent = d.normalized; break; }
                }
                Vector3 right = Vector3.Cross(Vector3.up,tangent).normalized;
                var grade = Quaternion.LookRotation(tangent, Vector3.Cross(tangent,right));
                Piece(CityWaterKit.SewerCover,p+Vector3.up*.013f+right*.52f,grade);
                Piece(CityWaterKit.StopCover,p+Vector3.up*.013f-right*.62f,grade);
            }
        }

        private void Piece(CityWaterKit kit, Vector3 position, Quaternion rotation, Vector3? scale = null) =>
            fixtures.Add(new CityWaterFixture(kit, position, rotation, scale ?? Vector3.one));

        private void BuildMains()
        {
            supplyParent = Routes(false, out _);
            sewerParent = Routes(true, out var order);
            // Work upstream to downstream. Deeper inverts absorb shallow rises; beyond
            // practical trench depth the reach becomes a named pressure main instead.
            for (int i = order.Count-1; i > 0; i--)
            {
                int n = order[i], parent = sewerParent[n];
                var from = junctions[n]; var to = junctions[parent];
                float fall = Mathf.Max(.013f, Vector2.Distance(Xz(from.Sewer), Xz(to.Sewer)) * .006f);
                float invert = Mathf.Min(to.Sewer.y, from.Sewer.y - fall);
                if (invert >= to.Surface.y - 5.5f) to.Sewer = AtHeight(to.Sewer, invert);
            }
            foreach (var s in streets)
                if (s.Active && s.Supply) Pipe(junctions[s.A].Water, junctions[s.B].Water, CityWaterPipeKind.Supply);
            foreach (int n in order)
                if (n != station) DrainPipe(junctions[n].Sewer, junctions[sewerParent[n]].Sewer);
            foreach (var service in services)
            {
                if (supplyParent[service.WaterTap] < 0 || sewerParent[service.Tap] < 0)
                    throw new InvalidOperationException("Disconnected water service: " + service.Id);
                Pipe(junctions[service.WaterTap].Water, service.Inlet, CityWaterPipeKind.Supply);
                DrainPipe(service.Drain, junctions[service.Tap].Sewer);
            }
        }

        private int[] Routes(bool sewer, out List<int> order)
        {
            order = new List<int>(junctions.Count);
            var parents = new int[junctions.Count]; Array.Fill(parents, -1);
            var costs = new float[junctions.Count]; Array.Fill(costs, float.PositiveInfinity);
            var pending = new SortedSet<(float cost, int node)> { (0f, station) };
            parents[station] = station; costs[station] = 0f;
            while (pending.Count != 0)
            {
                var entry = pending.Min; pending.Remove(entry); order.Add(entry.node);
                var node = junctions[entry.node];
                foreach (int i in node.Links)
                {
                    Street s = streets[i];
                    if (!s.Active || (sewer ? !s.Drain : !s.Supply)) continue;
                    int other = s.A == entry.node ? s.B : s.A;
                    Vector3 d = junctions[other].Surface - node.Surface;
                    float cost = entry.cost + Mathf.Max(.01f, d.magnitude) + (sewer ? Mathf.Max(0f, -d.y)*25f : 0f);
                    if (cost >= costs[other]) continue;
                    pending.Remove((costs[other], other)); pending.Add((cost, other));
                    costs[other] = cost; parents[other] = entry.node;
                }
            }
            return parents;
        }

        private void Pipe(Vector3 a, Vector3 b, CityWaterPipeKind kind) => pipes.Add(new CityWaterPipe(a,b,kind));
        private void DrainPipe(Vector3 a, Vector3 b)
        {
            bool pumped = a.y <= b.y;
            if (pumped) lifts.Add(a);
            Pipe(a, b, pumped ? CityWaterPipeKind.ForceMain : CityWaterPipeKind.GravitySewer);
        }

        public void Trace(CityWaterService service, List<Vector3> supply, List<Vector3> sewer)
        {
            supply.Clear(); sewer.Clear();
            for (int n = service.WaterTap; ; n = supplyParent[n])
            { supply.Add(junctions[n].Water); if (n == station) break; }
            supply.Reverse(); supply.Add(service.Inlet);
            sewer.Add(service.Drain);
            for (int n = service.Tap; ; n = sewerParent[n])
            { sewer.Add(junctions[n].Sewer); if (n == station) break; }
            sewer.AddRange(discharge);
        }
    }
}
