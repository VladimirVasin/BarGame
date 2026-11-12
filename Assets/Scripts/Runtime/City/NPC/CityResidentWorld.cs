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
        private const int ActivityCount = (int)CityResidentActivity.Leisure + 1;
        private readonly CityPedestrianPlan plan;
        private readonly int[,] places = new int[CityResidentLifeRules.ResidentCount, ActivityCount];
        private readonly Route[,,] routes = new Route[CityResidentLifeRules.ResidentCount, ActivityCount, ActivityCount];
        private readonly Route[,] circuits = new Route[CityResidentLifeRules.ResidentCount, ActivityCount];
        private readonly Dictionary<int, float[]> distances = new Dictionary<int, float[]>();
        private readonly Route[] retainedRoutes = new Route[CityResidentLifeRules.ResidentCount];
        private readonly double[] retainedAt = new double[CityResidentLifeRules.ResidentCount];
        private readonly double[] retainedActivity = new double[CityResidentLifeRules.ResidentCount];
        private readonly Vector3[] retainedPosition = new Vector3[CityResidentLifeRules.ResidentCount];
        private readonly Dictionary<int, Address> homeAddresses = new Dictionary<int, Address>();
        private readonly Dictionary<int, Address> workAddresses = new Dictionary<int, Address>();
        private readonly HashSet<int> publicLeisureNodes = new HashSet<int>();
        private readonly int[] kioskPlaces = new int[CityResidentLifeRules.ResidentCount];
        private readonly int[] kioskWaiting = new int[RidgeKioskCourtPlan.Capacity];
        internal int KioskEntryNode { get; private set; } = -1;
        internal int KioskWindowNode { get; private set; } = -1;
        private int kioskGateNode = -1;
        internal int KioskGateNode => kioskGateNode;
        private readonly int[] components;
        internal bool HasPavement => plan.Nodes.Count >= 2;
        public CityResidentJourneyDirector Journeys { get; internal set; }
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
            var leisure = new List<int>(24);
            var hillHomes = new List<int>(8);
            var hillWalks = new List<int>(2);
            if (layout != null)
            {
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (lot.IsPlayerHome) continue;
                    if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential)
                    {
                        var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                        Vector3 entrance = pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant));
                        AddAddress(homes, homeAddresses, lot, lot.SidewalkArrivalPosition, entrance, 32);
                    }
                    else if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Industrial)
                        AddAddress(work, workAddresses, lot, lot.SidewalkArrivalPosition, lot.DoorPosition, 12);
                    else if (lot.IsSupermarket) AddPlace(shops, lot.SidewalkArrivalPosition, 4);
                    else if (lot.IsBar) AddPlace(walks, lot.SidewalkArrivalPosition, 4);
                }
                var quarter = CityHillsideNeighborhoodPlan.ForLayout(layout);
                foreach (var house in quarter.SharedCourtBuildings)
                    AddHillHome(house);
                // Draw a few addresses across the entire rise instead of filling
                // the home pool with consecutive houses on the first street.
                for (int i = 1; i <= 4 && quarter.Buildings.Count > 0; i++)
                    AddHillHome(quarter.Buildings[(quarter.Buildings.Count - 1) * i / 4]);
                var passage = CityHillsideAccessPlan.ForLayout(layout).SharedCourt;
                if (passage != null)
                {
                    AddPlace(hillWalks, passage.RouteWorld[0], 2);
                    AddPlace(hillWalks, passage.RouteWorld[passage.RouteWorld.Count - 1], 2);
                }
                foreach (CityPedestrianLink link in plan.Links)
                {
                    if (leisure.Count >= 24) break;
                    if (link.Kind != CityPedestrianLinkKind.Sidewalk) continue;
                    int node = link.FirstNodeIndex;
                    if (plan.Nodes[node].IsCrosswalkEntry || plan.GetLinkIndices(node).Count > 2) continue;
                    Vector2 grid = layout.GetWorldGridCoordinate(plan.Nodes[node].Position);
                    if (link.Id.StartsWith("river-promenade:", StringComparison.Ordinal) ||
                        link.Id.StartsWith("coast:", StringComparison.Ordinal) ||
                        (layout.TryGetArea(Vector2Int.FloorToInt(grid), out CityAreaDefinition area) &&
                         area.Feature == CityAreaFeatureKind.CentralPark))
                    {
                        AddUnique(leisure, node);
                        publicLeisureNodes.Add(node);
                    }
                }
            }
            if (layout?.PlayerHome != null && CityBusStopWaitPlanner.TryFindNearestNode(plan,
                layout.PlayerHome.SidewalkArrivalPosition, out int playerHomeNode))
            {
                homes.Remove(playerHomeNode);
                hillHomes.Remove(playerHomeNode);
            }
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
                // The same residents keep their normal schedules and presentation
                // budget. A stable subset lives in the houses on the new streets.
                bool livesUphill = hillHomes.Count > 0 && id % 11 == 0;
                int home = livesUphill ? hillHomes[(int)((Hash(0, seed) + (uint)(id / 11)) % (uint)hillHomes.Count)] :
                    homes[(int)(hash % (uint)homes.Count)];
                if (CityResidentLifeRules.HasWorkSchedule(id) && workAddresses.Count > 0 &&
                    !HasConnectedAddress(home, work))
                {
                    // An isolated residential component cannot supply a work
                    // commute. Choose a real home in a component with a job.
                    for (int offset = 0; offset < homes.Count; offset++)
                    {
                        int alternate = homes[(int)((hash + (uint)offset) % (uint)homes.Count)];
                        if (!HasConnectedAddress(alternate, work)) continue;
                        home = alternate;
                        livesUphill = false;
                        break;
                    }
                }
                places[id, (int)CityResidentActivity.Home] = home;
                places[id, (int)CityResidentActivity.ReturningHome] = home;
                places[id, (int)CityResidentActivity.Work] = SelectConnected(work, home, hash >> 5);
                places[id, (int)CityResidentActivity.Shopping] = SelectConnected(shops, home, hash >> 10);
                places[id, (int)CityResidentActivity.Walking] = SelectConnected(
                    livesUphill && hillWalks.Count > 0 ? hillWalks : walks, home, hash >> 15);
                places[id, (int)CityResidentActivity.Leisure] = SelectConnected(
                    HasConnectedAddress(home, leisure) ? leisure : walks, home, hash >> 19);
                retainedActivity[id] = double.NaN;
            }
            if (layout != null) AssignKioskLeisure();

            void AddHillHome(CityHillsideBuilding house)
            {
                if (house.Lot.IsPlayerHome) return;
                Vector2 court = house.CourtBounds.center;
                AddAddress(hillHomes, homeAddresses, house.Lot,
                    new Vector3(court.x, house.DoorWorld.y, court.y), house.ThresholdWorld, 8, hasEntranceRoute: true);
            }
        }

        private void AssignKioskLeisure()
        {
            var nodes = new int[RidgeKioskCourtPlan.Capacity];
            Array.Fill(nodes, -1);
            Array.Fill(kioskWaiting, -1);
            for (int node = 0; node < plan.Nodes.Count; node++)
            {
                string id = plan.Nodes[node].Id;
                if (!id.StartsWith(RidgeKioskCourtPlan.NodePrefix, StringComparison.Ordinal)) continue;
                if (id == RidgeKioskCourtPlan.NodePrefix + "entry") KioskEntryNode = node;
                else if (id == RidgeKioskCourtPlan.NodePrefix + "window") KioskWindowNode = node;
                else
                    for (int place = 0; place < nodes.Length; place++)
                    {
                        if (id == RidgeKioskCourtPlan.PlaceId(place)) { nodes[place] = node; break; }
                        if (id == RidgeKioskCourtPlan.WaitingId(place)) { kioskWaiting[place] = node; break; }
                    }
            }
            if (KioskEntryNode < 0 || KioskWindowNode < 0 ||
                Array.IndexOf(kioskWaiting, -1) >= 0 || Array.IndexOf(nodes, -1) >= 0) return;
            foreach (int index in plan.GetLinkIndices(KioskEntryNode))
            {
                var link = plan.Links[index];
                if (link.Kind != CityPedestrianLinkKind.HomeEntrance || link.SecondNodeIndex != KioskEntryNode) continue;
                kioskGateNode = link.FirstNodeIndex;
                break;
            }
            if (kioskGateNode < 0) return;
            IReadOnlyList<float> distance = DistancesTo(KioskEntryNode);
            var candidates = new List<(int id, float distance, uint rank)>();
            for (int id = 0; id < CityResidentLifeRules.ResidentCount; id++)
            {
                if (!CityResidentLifeRules.HasWorkSchedule(id) || DefaultNpcPopulation.IsWomanPedestrian(id)) continue;
                float home = distance[Destination(id, CityResidentActivity.Home)];
                float work = distance[Destination(id, CityResidentActivity.Work)];
                if (!float.IsFinite(home) || !float.IsFinite(work)) continue;
                candidates.Add((id, home * .7f + work * .3f, Hash(id, seed)));
            }
            candidates.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.rank.CompareTo(b.rank));
            for (int place = 0; place < Math.Min(nodes.Length, candidates.Count); place++)
            {
                int id = candidates[place].id;
                kioskPlaces[id] = place + 1;
                places[id, (int)CityResidentActivity.Leisure] = nodes[place];
                publicLeisureNodes.Add(nodes[place]);
            }
        }

        internal int KioskPlace(int resident) => kioskPlaces[resident] - 1;
        internal int KioskWaitingNode(int place) => kioskWaiting[place];

        internal bool HasEnteredKioskRoute(int node) => node >= 0 && node != kioskGateNode &&
            plan.Nodes[node].Id.StartsWith(RidgeKioskCourtPlan.NodePrefix, StringComparison.Ordinal);

        internal float KioskApproachLimit(Route route, Vector3? prefix, int destination)
        {
            const float visibleWalkInMetres = 28f;
            float prefixLength = prefix.HasValue ? PrefixLength(route, prefix.Value) : 0f;
            float courtLeg = DistancesTo(destination)[kioskGateNode];
            return Mathf.Max(0f, prefixLength + route.Length - courtLeg - visibleWalkInMetres);
        }

        public readonly struct Address
        {
            public readonly BuildingLot Building;
            public readonly Vector3 Entrance;
            public readonly int PavementNode;
            public readonly int FrontageNode;
            internal Address(BuildingLot building, Vector3 entrance, int node, int frontage = -1)
            {
                Building = building;
                Entrance = entrance;
                PavementNode = node;
                FrontageNode = frontage < 0 ? node : frontage;
            }
        }

        public Address Home(int resident) => AddressAt(homeAddresses, Destination(resident, CityResidentActivity.Home));
        internal int HomeFrontage(int resident) => Home(resident).FrontageNode;
        public Address Workplace(int resident) => AddressAt(workAddresses, Destination(resident, CityResidentActivity.Work));

        private Address AddressAt(Dictionary<int, Address> addresses, int node)
        {
            if (addresses.TryGetValue(node, out Address address)) return address;
            return HasPavement ? new Address(null, plan.Nodes[node].Position, node) : new Address(null, Vector3.zero, -1);
        }

        private void AddAddress(List<int> nodes, Dictionary<int, Address> addresses, BuildingLot lot,
            Vector3 approach, Vector3 entrance, int maximum, bool hasEntranceRoute = false)
        {
            if (nodes.Count >= maximum || !CityBusStopWaitPlanner.TryFindNearestNode(plan, approach, out int node)) return;
            AddUnique(nodes, node);
            if (!addresses.ContainsKey(node))
                addresses.Add(node, new Address(lot, entrance, node, hasEntranceRoute ? EntranceFrontage(node) : node));
        }

        private int EntranceFrontage(int node)
        {
            // Entrance links point from the public pavement toward their house.
            // A split may reuse an existing public node at the junction.
            for (int visited = 0; visited < plan.Nodes.Count; visited++)
            {
                int parent = node;
                foreach (int index in plan.GetLinkIndices(node))
                {
                    CityPedestrianLink link = plan.Links[index];
                    if (link.Kind == CityPedestrianLinkKind.HomeEntrance && link.SecondNodeIndex == node)
                    {
                        parent = link.FirstNodeIndex;
                        break;
                    }
                }
                if (parent == node) break;
                node = parent;
            }
            return node;
        }

        public CityResidentLifeSample Schedule(int resident, int day, double minute) => kioskPlaces[resident] == 0
            ? CityResidentLifeRules.Evaluate(resident, day, minute, seed)
            : CityResidentLifeRules.EvaluateKioskRegular(resident, KioskPlace(resident), day, minute, seed);

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
            if (Journeys != null) return Journeys.TrySample(resident, day * 1440d + minute, out sample);
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
                if (schedule.Activity == CityResidentActivity.Leisure)
                    return TrySampleLeisure(destination, schedule, out sample);
                if (!outdoor) return false;
                Route circuit = circuits[resident, (int)schedule.Activity];
                if (circuit == null)
                {
                    circuit = BuildRoute(HomeFrontage(resident), destination);
                    circuits[resident, (int)schedule.Activity] = circuit;
                }
                if (circuit.Length <= .001f) return false;
                // Finish the actual delayed leg first, then resume the full
                // public frontage circuit. Repeated recycling cannot shorten it.
                bool startsAtPublic = route.Nodes[route.Nodes.Length - 1] == destination;
                double phase = (travelled - initialLength + (startsAtPublic ? circuit.Length : 0d)) % (2d * circuit.Length);
                returning = phase >= circuit.Length;
                travelled = returning ? 2d * circuit.Length - phase : phase;
                route = circuit; prefix = 0f; retainedStart = null;
            }
            return SampleRoute(route, travelled, retainedStart, returning, schedule, out sample);
        }

        internal bool TrySampleLeisure(int node, CityResidentLifeSample schedule, out StreetSample sample)
        {
            if (publicLeisureNodes.Contains(node))
            {
                foreach (int index in plan.GetLinkIndices(node))
                {
                    CityPedestrianLink link = plan.Links[index];
                    if (link.Kind != CityPedestrianLinkKind.Sidewalk &&
                        !plan.Nodes[node].Id.StartsWith(RidgeKioskCourtPlan.NodePrefix, StringComparison.Ordinal)) continue;
                    sample = new StreetSample(plan.Nodes[node].Position, link.Other(node), node, node, schedule, atLeisureDestination: true);
                    return true;
                }
            }
            sample = default;
            return false;
        }

        internal bool SampleRoute(Route route, double travelled, Vector3? retainedStart, bool returning,
            CityResidentLifeSample schedule, out StreetSample sample)
        {
            sample = default;
            float prefix = retainedStart.HasValue ? PrefixLength(route, retainedStart.Value) : 0f;
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
            if (Journeys != null)
            {
                Journeys.RetainWalk(resident, actor, day * 1440d + minute);
                return;
            }
            CityResidentLifeSample schedule = Schedule(resident, day, minute);
            int goal = actor.ResidentDestinationNode >= 0 ? actor.ResidentDestinationNode : Destination(resident, schedule.Activity);
            Route remaining = BuildRoute(actor.TargetNodeIndex, goal);
            remaining.StartPrevious = actor.PreviousNodeIndex;
            retainedRoutes[resident] = remaining;
            retainedPosition[resident] = actor.Position;
            retainedAt[resident] = day * 1440d + minute;
            retainedActivity[resident] = schedule.StartedAt;
        }

        internal Route BuildRoute(int start, int goal) => BuildRoute(plan, start, goal, DistancesTo(goal));

        internal static Route BuildRoute(CityPedestrianPlan plan, int start, int goal, IReadOnlyList<float> distance)
        {
            var nodes = new List<int> { start };
            var links = new List<int>();
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
                    float next = LinkLength(plan, link) + distance[other];
                    if (next < cost) { cost = next; best = linkIndex; }
                }
                if (best < 0 || float.IsPositiveInfinity(cost)) break;
                CityPedestrianLink selected = plan.Links[best];
                current = selected.Other(current);
                nodes.Add(current); links.Add(best); total += LinkLength(plan, selected);
            }
            return new Route(nodes.ToArray(), links.ToArray(), total);
        }

        private CityPedestrianLink PrefixLink(Route route)
        {
            foreach (int index in plan.GetLinkIndices(route.Nodes[0]))
                if (plan.Links[index].Other(route.Nodes[0]) == route.StartPrevious) return plan.Links[index];
            return null;
        }
        internal float PrefixLength(Route route, Vector3 start)
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

        private bool HasConnectedAddress(int home, List<int> candidates)
        {
            foreach (int node in candidates)
                if (node != home && components[node] == components[home]) return true;
            return false;
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
        private float LinkLength(CityPedestrianLink link) => LinkLength(plan, link);
        private static float LinkLength(CityPedestrianPlan plan, CityPedestrianLink link) => link.Path?.Length ??
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
        internal sealed class Route
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
            internal readonly bool AtLeisureDestination;
            public StreetSample(Vector3 position, int from, int to, int destination, CityResidentLifeSample schedule,
                bool atLeisureDestination = false)
            {
                Position = position;
                FromNode = from;
                ToNode = to;
                Destination = destination;
                Schedule = schedule;
                AtLeisureDestination = atLeisureDestination;
            }
        }
    }
}
