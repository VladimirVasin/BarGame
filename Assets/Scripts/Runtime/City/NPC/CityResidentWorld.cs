using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded, object-free residents on the actual pavement graph.
    /// Calendar samples can jump directly across days; routes are cached only
    /// by the finite resident/activity set, never by date or frame.</summary>
    public sealed class CityResidentWorld
    {
        public const float WalkingSpeed = .82f;
        private const double MetresPerGameMinute = WalkingSpeed / GameTimeState.GameMinutesPerRealSecond;
        private readonly CityPedestrianPlan plan;
        private readonly int[,] places = new int[CityResidentLifeRules.ResidentCount, 5];
        private readonly Route[,,] routes = new Route[CityResidentLifeRules.ResidentCount, 5, 5];
        private readonly Route[,] circuits = new Route[CityResidentLifeRules.ResidentCount, 5];
        private readonly Dictionary<int, float[]> distances = new Dictionary<int, float[]>();
        private readonly Route[] retainedRoutes = new Route[CityResidentLifeRules.ResidentCount];
        private readonly double[] retainedAt = new double[CityResidentLifeRules.ResidentCount];
        private readonly double[] retainedActivity = new double[CityResidentLifeRules.ResidentCount];
        private readonly Vector3[] retainedPosition = new Vector3[CityResidentLifeRules.ResidentCount];
        private readonly int[] components;
        private readonly int seed;

        public CityResidentWorld(CityPedestrianPlan pedestrianPlan, CityLayout layout = null)
        {
            plan = pedestrianPlan ?? throw new ArgumentNullException(nameof(pedestrianPlan));
            seed = plan.PopulationSeed;
            components = CreateComponents();
            var homes = new List<int>();
            var work = new List<int>();
            var shops = new List<int>();
            var walks = new List<int>();
            if (layout != null)
            {
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (lot.IsPlayerHome) continue;
                    if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential)
                        AddPlace(homes, lot.SidewalkArrivalPosition, 32);
                    else if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Industrial)
                        AddPlace(work, lot.SidewalkArrivalPosition, 12);
                    else if (lot.IsSupermarket) AddPlace(shops, lot.SidewalkArrivalPosition, 4);
                    else if (lot.IsBar) AddPlace(walks, lot.SidewalkArrivalPosition, 4);
                }
            }
            if (layout?.PlayerHome != null && CityBusStopWaitPlanner.TryFindNearestNode(plan,
                layout.PlayerHome.SidewalkArrivalPosition, out int playerHomeNode)) homes.Remove(playerHomeNode);
            // Graph-only fixtures and nonstandard layouts retain a valid fallback.
            if (homes.Count == 0)
                for (int i = 0; i < plan.SpawnAnchors.Count && homes.Count < 32; i++)
                    AddUnique(homes, plan.SpawnAnchors[i].FirstNodeIndex);
            if (homes.Count == 0 && plan.Nodes.Count > 0) homes.Add(0);
            if (work.Count == 0) work.AddRange(homes);
            if (shops.Count == 0) shops.AddRange(homes);
            foreach (int home in homes) AddUnique(walks, home);
            for (int id = 0; id < CityResidentLifeRules.ResidentCount && homes.Count > 0; id++)
            {
                uint hash = Hash(id, seed);
                int home = homes[(int)(hash % (uint)homes.Count)];
                places[id, (int)CityResidentActivity.Home] = home;
                places[id, (int)CityResidentActivity.ReturningHome] = home;
                places[id, (int)CityResidentActivity.Work] = SelectConnected(work, home, hash >> 5);
                places[id, (int)CityResidentActivity.Shopping] = SelectConnected(shops, home, hash >> 10);
                places[id, (int)CityResidentActivity.Walking] = SelectConnected(walks, home, hash >> 15);
                retainedActivity[id] = double.NaN;
            }
        }

        public CityResidentLifeSample Schedule(int resident, int day, double minute) =>
            CityResidentLifeRules.Evaluate(resident, day, minute, seed);

        public int Destination(int resident, CityResidentActivity activity) => places[resident, (int)activity];
        public IReadOnlyList<float> DistancesTo(int node)
        {
            if (!distances.TryGetValue(node, out float[] result))
            {
                result = CityBusStopWaitPlanner.CreateNodeDistances(plan, node);
                distances.Add(node, result);
            }
            return result;
        }

        public bool TrySample(int resident, int day, double minute, out StreetSample sample)
        {
            sample = default;
            if (plan.Nodes.Count < 2) return false;
            CityResidentLifeSample schedule = Schedule(resident, day, minute);
            int destination = Destination(resident, schedule.Activity);
            double now = day * 1440d + minute;
            Route route;
            double elapsed;
            Vector3? retainedStart = null;
            if (retainedRoutes[resident] != null && retainedActivity[resident] == schedule.StartedAt && now >= retainedAt[resident])
            {
                route = retainedRoutes[resident];
                elapsed = now - retainedAt[resident];
                retainedStart = retainedPosition[resident];
            }
            else
            {
                route = routes[resident, (int)schedule.Activity, (int)schedule.PreviousActivity];
                if (route == null)
                {
                    route = BuildRoute(Destination(resident, schedule.PreviousActivity), destination);
                    routes[resident, (int)schedule.Activity, (int)schedule.PreviousActivity] = route;
                }
                elapsed = Math.Max(0d, now - schedule.StartedAt);
            }
            bool outdoor = schedule.Activity == CityResidentActivity.Walking || schedule.Activity == CityResidentActivity.Shopping;
            double travelled = elapsed * MetresPerGameMinute;
            float prefix = retainedStart.HasValue ? PrefixLength(route, retainedStart.Value) : 0f;
            double initialLength = prefix + route.Length;
            bool returning = false;
            if (travelled >= initialLength)
            {
                if (!outdoor) return false;
                Route circuit = circuits[resident, (int)schedule.Activity];
                if (circuit == null)
                {
                    circuit = BuildRoute(Destination(resident, CityResidentActivity.Home), destination);
                    circuits[resident, (int)schedule.Activity] = circuit;
                }
                if (circuit.Length <= .001f) return false;
                // Finish the actual delayed leg first, then resume the full
                // home/public circuit. Repeated recycling cannot shorten it.
                bool startsAtPublic = route.Nodes[route.Nodes.Length - 1] == destination;
                double phase = (travelled - initialLength + (startsAtPublic ? circuit.Length : 0d)) % (2d * circuit.Length);
                returning = phase >= circuit.Length;
                travelled = returning ? 2d * circuit.Length - phase : phase;
                route = circuit; prefix = 0f; retainedStart = null;
            }
            if (prefix > .001f && travelled < prefix)
            {
                Vector3 position = SamplePrefix(route, retainedStart.Value, (float)travelled, prefix);
                sample = new StreetSample(position, route.StartPrevious, route.Nodes[0], route.Nodes[route.Nodes.Length - 1], schedule);
                return true;
            }
            if (route.Nodes.Length < 2) return false;
            travelled = Math.Max(0d, travelled - prefix);
            for (int i = 0; i < route.Links.Length; i++)
            {
                CityPedestrianLink link = plan.Links[route.Links[i]];
                float lengthOfLink = LinkLength(link);
                if (travelled > lengthOfLink && i < route.Links.Length - 1) { travelled -= lengthOfLink; continue; }
                int from = route.Nodes[i], to = route.Nodes[i + 1];
                float fraction = Mathf.Clamp01((float)(travelled / Math.Max(.001f, lengthOfLink)));
                Vector3 position = Vector3.Lerp(plan.Nodes[from].Position, plan.Nodes[to].Position, fraction);
                if (link.Path != null)
                {
                    bool forward = from == link.FirstNodeIndex;
                    Vector2 point = link.Path.SampleDistance((forward ? fraction : 1f - fraction) * link.Path.Length).Position;
                    position.x = point.x; position.z = point.y;
                    if (link.PathHeightSampler != null) position.y = link.PathHeightSampler(point);
                }
                sample = new StreetSample(position, returning ? to : from, returning ? from : to,
                    returning ? route.Nodes[0] : route.Nodes[route.Nodes.Length - 1], schedule);
                return true;
            }
            return false;
        }

        /// <summary>Continue from the real actor after a bus, a rest or a
        /// collision delay; the background never rewinds to its planned start.</summary>
        public void Retain(int resident, CityPedestrianActor actor, int day, double minute)
        {
            if (actor.TargetNodeIndex < 0 || actor.PreviousNodeIndex < 0 || actor.IsAttachedToVehicle) return;
            CityResidentLifeSample schedule = Schedule(resident, day, minute);
            int goal = actor.ResidentDestinationNode >= 0 ? actor.ResidentDestinationNode : Destination(resident, schedule.Activity);
            Route remaining = BuildRoute(actor.TargetNodeIndex, goal);
            remaining.StartPrevious = actor.PreviousNodeIndex;
            retainedRoutes[resident] = remaining;
            retainedPosition[resident] = actor.Position;
            retainedAt[resident] = day * 1440d + minute;
            retainedActivity[resident] = schedule.StartedAt;
        }

        private Route BuildRoute(int start, int goal)
        {
            var nodes = new List<int> { start };
            var links = new List<int>();
            IReadOnlyList<float> distance = DistancesTo(goal);
            int current = start;
            float total = 0f;
            for (int guard = 0; current != goal && guard < plan.Nodes.Count; guard++)
            {
                int best = -1;
                float cost = float.PositiveInfinity;
                foreach (int linkIndex in plan.GetLinkIndices(current))
                {
                    CityPedestrianLink link = plan.Links[linkIndex];
                    int other = link.Other(current);
                    float next = LinkLength(link) + distance[other];
                    if (next < cost) { cost = next; best = linkIndex; }
                }
                if (best < 0 || float.IsPositiveInfinity(cost)) break;
                CityPedestrianLink selected = plan.Links[best];
                current = selected.Other(current);
                nodes.Add(current); links.Add(best); total += LinkLength(selected);
            }
            return new Route(nodes.ToArray(), links.ToArray(), total);
        }

        private CityPedestrianLink PrefixLink(Route route)
        {
            foreach (int index in plan.GetLinkIndices(route.Nodes[0]))
                if (plan.Links[index].Other(route.Nodes[0]) == route.StartPrevious) return plan.Links[index];
            return null;
        }
        private float PrefixLength(Route route, Vector3 start)
        {
            CityPedestrianLink link = PrefixLink(route);
            if (link?.Path == null) return Vector3.Distance(start, plan.Nodes[route.Nodes[0]].Position);
            float along = link.Path.Project(new Vector2(start.x, start.z)).DistanceAlong;
            return route.Nodes[0] == link.SecondNodeIndex ? link.Path.Length - along : along;
        }
        private Vector3 SamplePrefix(Route route, Vector3 start, float travelled, float length)
        {
            CityPedestrianLink link = PrefixLink(route);
            if (link?.Path == null) return Vector3.Lerp(start, plan.Nodes[route.Nodes[0]].Position, travelled / length);
            float along = link.Path.Project(new Vector2(start.x, start.z)).DistanceAlong;
            bool forward = route.Nodes[0] == link.SecondNodeIndex;
            Vector2 point = link.Path.SampleDistance(along + (forward ? travelled : -travelled)).Position;
            float height = link.PathHeightSampler != null ? link.PathHeightSampler(point) : Mathf.Lerp(start.y, plan.Nodes[route.Nodes[0]].Position.y, travelled / length);
            return new Vector3(point.x, height, point.y);
        }

        private int SelectConnected(List<int> candidates, int home, uint hash)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                int candidate = candidates[(int)((hash + (uint)i) % (uint)candidates.Count)];
                if (components[candidate] == components[home] && candidate != home) return candidate;
            }
            foreach (int link in plan.GetLinkIndices(home)) return plan.Links[link].Other(home);
            return home;
        }
        private void AddPlace(List<int> list, Vector3 position, int maximum)
        {
            if (list.Count >= maximum) return;
            if (CityBusStopWaitPlanner.TryFindNearestNode(plan, position, out int node)) AddUnique(list, node);
        }
        private static void AddUnique(List<int> list, int node) { if (!list.Contains(node)) list.Add(node); }
        private float LinkLength(CityPedestrianLink link) => link.Path?.Length ??
            Vector3.ProjectOnPlane(plan.Nodes[link.FirstNodeIndex].Position - plan.Nodes[link.SecondNodeIndex].Position, Vector3.up).magnitude;
        private int[] CreateComponents()
        {
            var result = new int[plan.Nodes.Count];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            var queue = new Queue<int>();
            int component = 0;
            for (int root = 0; root < result.Length; root++)
            {
                if (result[root] >= 0) continue;
                result[root] = component++; queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    foreach (int index in plan.GetLinkIndices(node))
                    {
                        int other = plan.Links[index].Other(node);
                        if (result[other] >= 0) continue;
                        result[other] = result[node]; queue.Enqueue(other);
                    }
                }
            }
            return result;
        }
        private static uint Hash(int id, int seed) => CityPedestrianStableHash.Combine(unchecked((uint)seed), unchecked((uint)id + 0xC171u));
        private sealed class Route
        {
            public readonly int[] Nodes, Links;
            public readonly float Length;
            public int StartPrevious;
            public Route(int[] nodes, int[] links, float length) { Nodes = nodes; Links = links; Length = length; StartPrevious = nodes[0]; }
        }
        public readonly struct StreetSample
        {
            public readonly Vector3 Position;
            public readonly int FromNode, ToNode, Destination;
            public readonly CityResidentLifeSample Schedule;
            public StreetSample(Vector3 position, int from, int to, int destination, CityResidentLifeSample schedule)
            { Position = position; FromNode = from; ToNode = to; Destination = destination; Schedule = schedule; }
        }
    }
}
