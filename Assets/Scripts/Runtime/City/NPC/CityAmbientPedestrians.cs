using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>City-only background pedestrians. The calendar and transit systems never own these bodies.</summary>
    [DefaultExecutionOrder(320)]
    internal sealed class CityAmbientPedestrians : MonoBehaviour
    {
        internal const int MaximumActive = CityResidentLifeRules.MaximumAmbientStreetPopulation;
        internal const int MaximumStreetBodies = CityPedestrianDirector.MaximumActiveModels + MaximumActive;
        internal const int LocalStreetLimit = 4;
        internal const int LocalAmbientLimit = 2;
        internal const int WarmLimit = 3;
        private const float LocalRadius = 12f;
        private const int MaximumCachedBodies = 18;
        private const int MaximumEntryProbes = 8;
        private const float EntryRadius = 35f;
        internal const float ReleaseRadius = 46f;
        private const float BalanceRadius = 26f;
        private const float MinimumEntryDistance = 7f;
        private const float SpawnSpacing = 4f;
        private const int AdultLookCount = DefaultNpcPopulation.AmbientAppearancesPerGender * 2;
        internal const int MaximumActiveChildren = 2;
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("City.AmbientPedestrians");

        private readonly Walker[] walkers = new Walker[MaximumActive];
        private readonly Look[] looks = new Look[AdultLookCount + DefaultNpcPopulation.AmbientChildCount];
        private readonly List<CityAmbientWalkPlan.Entry> entries = new List<CityAmbientWalkPlan.Entry>(96);
        private readonly List<SpawnCandidate> candidates = new List<SpawnCandidate>(MaximumEntryProbes);
        private readonly CityAmbientWalking.Contact[] contacts = new CityAmbientWalking.Contact[MaximumStreetBodies + 1];
        private CityGameRoot city;
        private CityAmbientWalkPlan plan;
        private CityAmbientWalking movement;
        private Transform hero;
        private float heroRadius;
        private int[] lanePopulation;
        private int railVisits;
        private int cachedBodies;
        private int activeChildren;
        private int serial;
        private uint entrySearch;
        private float spawnCountdown, crowdCheckCountdown;

        internal int ActiveCount { get; private set; }
        internal CityAmbientWalkPlan WalkPlan => plan;

        private struct Look
        {
            public CityPedestrianPresentation Body;
            public int LastUse;
            public bool Leased;
        }

        private struct Walker
        {
            public int Encounter, Look;
            public CityWorldResult RailWorld;
            public bool AtRail => RailWorld != null;
            public CityAmbientWalking.Route Walk;
            public float Age, Speed, NextBlockedLog, NextPlaygroundProbe;
            public CityPlaygroundVisit Playground;
        }

        private readonly struct SpawnCandidate
        {
            public readonly CityAmbientWalkPlan.Entry Entry;
            public readonly int Direction;
            public readonly float Score;

            public SpawnCandidate(CityAmbientWalkPlan.Entry entry, int direction, float score)
            {
                Entry = entry;
                Direction = direction;
                Score = score;
            }
        }

        internal static CityAmbientPedestrians Create(CityGameRoot city, RoadWalkableArea pavement)
        {
            var root = new GameObject("City Ambient Pedestrians");
            root.transform.SetParent(city.transform, false);
            var ambient = root.AddComponent<CityAmbientPedestrians>();
            ambient.city = city;
            ambient.hero = city.Player.GameObject.transform;
            var playerBody = ambient.hero.GetComponent<CharacterController>();
            ambient.heroRadius = playerBody != null ? playerBody.radius : .32f;
            var childExclusions = new List<Rect>();
            if (HomeYardSitePlanner.Create(city.Layout) is HomeYardSitePlan barYard)
                childExclusions.Add(barYard.GroundBounds);
            foreach (CityFringeYardDescriptor yard in city.World.FringeYardPlan.Yards)
                if (yard.Kind == CityFringeYardKind.EastUtilityEdge)
                    childExclusions.Add(yard.AreaBounds);
            ambient.plan = new CityAmbientWalkPlan(city.PedestrianPlan, city.Layout, city.BusPlans, childExclusions,
                city.World.CourtyardPlayground?.Site.RoutePrefix, city.World.SlidePlayground?.Site.RoutePrefix,
                city.World.BridgePlayground?.Site.RoutePrefix, city.World.Seesaw?.Site.RoutePrefix);
            ambient.movement = new CityAmbientWalking(ambient.plan, pavement, CityStreetSupportPlan.ForLayout(city.Layout),
                city.Pedestrians.VehicleTraffic);
            ambient.lanePopulation = new int[city.PedestrianPlan.Links.Count];
            city.Pedestrians.AmbientStreet = ambient;
            return ambient;
        }

        internal bool WarmNext()
        {
            if (ActiveCount >= Mathf.Min(WarmLimit, Target()) || !TrySpawn(true)) return false;
            spawnCountdown = .9f;
            return true;
        }

        internal static bool OnSameStretch(Vector3 first, Vector3 second) =>
            Mathf.Abs(first.y - second.y) < 2f && (first - second).sqrMagnitude < LocalRadius * LocalRadius;

        internal int CountNear(Vector3 point, int exceptSlot = -1)
        {
            int count = 0;
            for (int i = 0; i < walkers.Length; i++)
                if (i != exceptSlot && walkers[i].Encounter != 0 &&
                    OnSameStretch(point, looks[walkers[i].Look].Body.transform.position)) count++;
            return count;
        }

        internal bool TryGetWalker(int index, out Vector3 position, out bool woman, out int encounter)
        {
            Walker walker = walkers[index];
            encounter = walker.Encounter;
            woman = IsWoman(walker.Look);
            position = encounter != 0 ? looks[walker.Look].Body.transform.position : default;
            return encounter != 0;
        }

        internal CityPedestrianPresentation GetWalkerBody(int index)
        {
            Walker walker = walkers[index];
            return walker.Encounter != 0 ? looks[walker.Look].Body : null;
        }

        internal CityPlaygroundVisit.Phase GetPlaygroundPhase(int index) => walkers[index].Playground.CurrentPhase;

        private int Target() => Mathf.Min(MaximumActive,
            CityResidentLifeRules.TargetAmbientPopulation(GameSessionState.GameTimeOfDayMinutes));

        private void LateUpdate() => Advance(Time.deltaTime);

        internal void Advance(float delta)
        {
            if (city == null || !city.IsInitialized || city.IsDormant || delta <= 0f) return;
            using var marker = TickMarker.Auto();
            int target = Target();
            bool daylight = GameTimeDayNightRules.IsDaylight(GameSessionState.GameTimeOfDayMinutes);
            float step = Mathf.Min(delta, .1f);
            CityPlaygroundSeesaw seesaw = city.World.Seesaw;
            if (seesaw != null) seesaw.Advance(step, daylight);
            int contactCount = CollectContacts();
            Vector3 heroHead = city.Pedestrians.ResolveHeroFocus();
            crowdCheckCountdown -= delta;
            bool checkCrowd = crowdCheckCountdown <= 0f;
            if (checkCrowd) crowdCheckCountdown = .7f;
            for (int index = 0; index < walkers.Length; index++)
            {
                ref Walker walker = ref walkers[index];
                if (walker.Encounter == 0) continue;
                CityPedestrianPresentation body = looks[walker.Look].Body;
                Vector3 position = body.transform.position;
                float distanceSquared = (position - hero.position).sqrMagnitude;
                walker.Age += delta;
                bool beyondLocalStreets = distanceSquared > ReleaseRadius * ReleaseRadius;
                bool atPlayground = walker.Playground.CurrentPhase != CityPlaygroundVisit.Phase.None;
                bool childLeaving = walker.Walk.IsChild && !daylight && !atPlayground;
                bool canRotateCast = distanceSquared > 15f * 15f &&
                    (walker.Age > 28f || ActiveCount > target || walker.Age > 12f && distanceSquared > 24f * 24f);
                if ((childLeaving || beyondLocalStreets || !atPlayground && (canRotateCast || checkCrowd && walker.Age > 5f)) &&
                    city.Visibility.CanChangeAmbientBodyAt(position) &&
                    (childLeaving || beyondLocalStreets || canRotateCast || !HasLocalRoom(position, index)))
                {
                    Release(index);
                    contacts[index] = default;
                    continue;
                }
                body.MinimumEvaluationInterval = city.Visibility.CanChangeAmbientBodyAt(position) ? 1f / 8f :
                    distanceSquared < 15f * 15f ? 0f : 1f / 15f;
                if (walker.Walk.IsChild && AdvancePlayground(index, ref walker, body, step, daylight))
                    continue;
                if (walker.AtRail)
                {
                    body.Advance(delta, false);
                    continue;
                }
                AdvanceWalker(index, ref walker, body, step, contactCount, heroHead);
            }

            spawnCountdown -= delta;
            if (ActiveCount >= target || spawnCountdown > 0f) return;
            spawnCountdown = TrySpawn(false) ? .9f : 1.2f;
        }

        private bool AdvancePlayground(int index, ref Walker walker, CityPedestrianPresentation body, float delta, bool daylight)
        {
            if (walker.Playground.CurrentPhase == CityPlaygroundVisit.Phase.None)
            {
                if (!daylight || walker.Age < walker.NextPlaygroundProbe || walker.Walk.Backing ||
                    walker.Walk.IsChild && plan.IsStair(walker.Walk.Link)) return false;
                walker.NextPlaygroundProbe = walker.Age + .8f;
                if (!walker.Playground.TryStart(city.World.PlaygroundSwings, walker.Encounter, body.transform.position,
                    city.World.CourtyardPlayground, city.World.SlidePlayground, city.World.BridgePlayground,
                    city.World.Seesaw, city.World.BalanceStations))
                    return false;
            }
            if (!walker.Playground.Advance(body, walker.Encounter, walker.Speed, delta, daylight))
            {
                walker.Age = 0f;
                walker.NextPlaygroundProbe = 45f;
                walker.Walk.NextProbe = 0f;
                return false;
            }
            contacts[index] = new CityAmbientWalking.Contact(-MaximumStreetBodies - index - 2,
                body.transform.position, CityAmbientWalking.Radius);
            return true;
        }

        private bool TrySpawn(bool warming)
        {
            if (ActiveCount >= MaximumActive || ActiveCount + city.Pedestrians.ActiveCount >= MaximumStreetBodies)
                return false;
            Vector3 player = hero.position;
            Vector3 interest = player + Vector3.ProjectOnPlane(hero.forward, Vector3.up) * 6f;
            entrySearch++;
            if ((warming || entrySearch % 3 == 1) && TrySpawnAtRail(player, warming)) return true;
            plan.CollectEntries(player, EntryRadius, entries);
            int childTarget = Mathf.Min(MaximumActiveChildren, Target() / 5);
            bool childSlot = !warming && activeChildren < childTarget &&
                GameTimeDayNightRules.IsDaylight(GameSessionState.GameTimeOfDayMinutes);
            candidates.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                CityAmbientWalkPlan.Entry entry = entries[i];
                if (plan.IsPlaygroundLink(entry.Link) && (!childSlot || !plan.AllowsChild(entry.Link))) continue;
                Vector3 offset = entry.Position - player;
                float distance = offset.magnitude;
                if (distance < MinimumEntryDistance || distance > EntryRadius || Mathf.Abs(offset.y) > 4f ||
                    lanePopulation[entry.Link] >= plan.LaneLimit(entry.Link) ||
                    !ClearOfPeople(entry.Position) || !HasLocalRoom(entry.Position))
                    continue;
                bool hidden = city.Visibility.CanChangeAmbientBodyAt(entry.Position);
                if (!warming && !hidden && distance > 23f) continue;
                float plus = ArrivalScore(entry, 1, interest);
                float minus = ArrivalScore(entry, -1, interest);
                if (float.IsPositiveInfinity(Mathf.Min(plus, minus))) continue;
                uint hash = CityPedestrianStableHash.Combine(entrySearch, unchecked((uint)i));
                float score = Mathf.Min(plus, minus) + Mathf.Abs(distance - (warming ? 13f : 19f)) * .5f +
                    lanePopulation[entry.Link] * 6f - (plan.IsBusy(entry.Link) ? 2f : 0f) + (hash & 255u) / 96f;
                if (plan.IsPlaygroundLink(entry.Link)) score -= 10f;
                score += warming ? FramingPenalty(entry.Position) : hidden ? 0f : 2f;
                int rank = 0;
                while (rank < candidates.Count && score >= candidates[rank].Score) rank++;
                if (rank == MaximumEntryProbes) continue;
                if (candidates.Count == MaximumEntryProbes) candidates.RemoveAt(MaximumEntryProbes - 1);
                candidates.Insert(rank, new SpawnCandidate(entry, plus <= minus ? 1 : -1, score));
            }

            foreach (SpawnCandidate candidate in candidates)
            {
                CityAmbientWalkPlan.Entry chosen = candidate.Entry;
                int direction = candidate.Direction;
                Vector3 ahead = plan.Sample(chosen.Link, chosen.Distance + direction * .7f, out _);
                if ((!warming && !city.Visibility.CanChangeAmbientBodyAt(chosen.Position) && !CoveredAtEntry(chosen.Position)) ||
                    !movement.HasClearance(chosen.Position) ||
                    !movement.HasClearance(ahead))
                    continue;
                bool child = childSlot && plan.AllowsChild(chosen.Link);
                if (Spawn(chosen, direction, child: child)) return true;
                if (child && !plan.IsPlaygroundLink(chosen.Link) && Spawn(chosen, direction)) return true;
            }
            return false;
        }

        internal void ReleaseRailRests(CityWorldResult world)
        {
            for (int i = 0; i < walkers.Length; i++)
                if (walkers[i].AtRail && walkers[i].RailWorld == world) Release(i);
        }

        private bool TrySpawnAtRail(Vector3 player, bool warming)
        {
            var railSpots = city.World.RailRests;
            // Shared street population and one occupied
            // rest point per local stretch. Ordinary walkers fill any gap.
            int nearest = -1;
            float best = EntryRadius * EntryRadius;
            for (int i = 0; i < railSpots.Count; i++)
            {
                CityRailRestSpot spot = railSpots[i];
                Vector3 offset = spot.Feet - player;
                float distance = offset.sqrMagnitude;
                if (distance >= best || distance < MinimumEntryDistance * MinimumEntryDistance ||
                    Mathf.Abs(offset.y) > 4f || !HasLocalRoom(spot.Feet) || !ClearOfPeople(spot.Feet)) continue;
                bool occupied = false;
                for (int j = 0; j < walkers.Length; j++)
                {
                    var other = GetWalkerBody(j);
                    if (other != null && other.IsRestingAtRail && OnSameStretch(other.transform.position, spot.Feet))
                    {
                        occupied = true;
                        break;
                    }
                }
                if (occupied || !warming && !city.Visibility.CanChangeAmbientBodyAt(spot.Feet) ||
                    !spot.HasClearApproach()) continue;
                best = distance;
                nearest = i;
            }
            return nearest >= 0 && Spawn(default, 0, railSpots[nearest]);
        }

        private float ArrivalScore(CityAmbientWalkPlan.Entry entry, int direction, Vector3 interest)
        {
            float available = direction > 0 ? plan.Length(entry.Link) - entry.Distance : entry.Distance;
            Vector3 future = plan.Sample(entry.Link, entry.Distance + direction * Mathf.Min(8f, available), out _);
            if (!HasLocalRoom(future)) return float.PositiveInfinity;
            float score = Vector3.Distance(future, interest) * .55f + FramingPenalty(future);
            if (available < 3f) score += 5f;
            return score;
        }

        private float FramingPenalty(Vector3 feet)
        {
            Vector3 view = city.Camera.WorldToViewportPoint(feet + Vector3.up);
            if (view.z < .3f || city.Visibility.CanChangeAmbientBodyAt(feet)) return 14f;
            return Mathf.Clamp01(Mathf.Max(Mathf.Abs(view.x - .5f), Mathf.Abs(view.y - .5f)) - .32f) * 20f;
        }

        private bool CoveredAtEntry(Vector3 feet)
        {
            Transform view = city.Camera.transform;
            Vector3 side = view.right * .45f;
            return Blocked(feet + Vector3.up * 1.85f) &&
                   Blocked(feet + Vector3.up * .85f + side) &&
                   Blocked(feet + Vector3.up * .85f - side) &&
                   Blocked(feet + Vector3.up * .15f);

            bool Blocked(Vector3 point) => Physics.Linecast(view.position, point, out RaycastHit hit,
                CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore) &&
                hit.rigidbody == null && !(hit.collider is CharacterController);
        }

        private bool Spawn(CityAmbientWalkPlan.Entry entry, int direction, CityRailRestSpot? rest = null, bool child = false)
        {
            if (child && !GameTimeDayNightRules.IsDaylight(GameSessionState.GameTimeOfDayMinutes)) return false;
            int slot = 0;
            while (slot < walkers.Length && walkers[slot].Encounter != 0) slot++;
            if (slot == walkers.Length) return false;
            int look = child
                ? AcquireLook(AdultLookCount, DefaultNpcPopulation.AmbientChildCount)
                : AcquireLook(PreferWoman() ? DefaultNpcPopulation.AmbientAppearancesPerGender : 0,
                    DefaultNpcPopulation.AmbientAppearancesPerGender);
            if (look < 0) return false;
            int encounter = ++serial;
            uint hash = CityPedestrianStableHash.Combine(city.PedestrianPlan.StableSeed, unchecked((uint)encounter));
            float phase = (hash & 255u) / 256f;
            float pace = Mathf.Lerp(.92f, 1.08f, phase);
            CityPedestrianPresentation body = looks[look].Body;
            Vector3 feet, facing;
            if (rest.HasValue)
            {
                feet = rest.Value.Feet;
                facing = rest.Value.Facing;
            }
            else
            {
                feet = entry.Position;
                plan.Sample(entry.Link, entry.Distance, out Vector3 tangent);
                facing = tangent * direction;
            }
            body.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(facing));
            body.gameObject.SetActive(true);
            NpcFootstepSources.Unregister(body.transform);
            body.ConfigureCycle(pace, phase);
            if (rest.HasValue)
            {
                bool smoking = ((railVisits++ + city.PedestrianPlan.StableSeed) & 1) != 0;
                body.RestAtRail(rest.Value, smoking, phase);
            }
            else
            {
                body.Advance(0f, true, true);
                lanePopulation[entry.Link]++;
            }
            looks[look].Leased = true;
            looks[look].LastUse = encounter;
            walkers[slot] = new Walker
            {
                Encounter = encounter, Look = look, RailWorld = rest.HasValue ? city.World : null,
                Walk = new CityAmbientWalking.Route(entry.Link, direction, entry.Distance, child),
                Speed = pace * CityResidentWorld.WalkingSpeed
            };
            ActiveCount++;
            if (child) activeChildren++;
            return true;
        }

        private bool PreferWoman()
        {
            const float rangeSquared = BalanceRadius * BalanceRadius;
            int count = 0;
            int women = 0;
            IReadOnlyList<CityPedestrianActor> actors = city.Pedestrians.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor actor = actors[i];
                if (!actor.IsSpawned || actor.IsAttachedToVehicle ||
                    (actor.Position - hero.position).sqrMagnitude > rangeSquared ||
                    city.Visibility.CanChangeAmbientBodyAt(actor.Position))
                    continue;
                int resident = city.Pedestrians.ResidentIndex(actor);
                if (resident < 0) continue;
                count++;
                if (DefaultNpcPopulation.IsWomanPedestrian(resident)) women++;
            }
            foreach (Walker walker in walkers)
            {
                if (walker.Encounter == 0 || walker.Walk.IsChild ||
                    (looks[walker.Look].Body.transform.position - hero.position).sqrMagnitude > EntryRadius * EntryRadius)
                    continue;
                // Count arrivals approaching from just outside the shot, too.
                count++;
                if (IsWoman(walker.Look)) women++;
            }
            return CityResidentLifeRules.PreferWomanForNextArrival(count, women);
        }

        private int AcquireLook(int first, int count)
        {
            int free = -1;
            int cached = -1;
            for (int i = first; i < first + count; i++)
            {
                if (looks[i].Leased) continue;
                if (looks[i].Body == null)
                {
                    if (free < 0 || looks[i].LastUse < looks[free].LastUse) free = i;
                }
                else if (cached < 0 || looks[i].LastUse < looks[cached].LastUse) cached = i;
            }
            if (cached >= 0)
            {
                if (free < 0) return cached;
                bool reuse = first == AdultLookCount
                    ? looks[cached].LastUse <= looks[free].LastUse
                    : cachedBodies >= MaximumCachedBodies || serial - looks[cached].LastUse >= 12;
                if (reuse) return cached;
            }
            if (free < 0) return -1;

            if (cachedBodies >= MaximumCachedBodies)
            {
                int oldest = -1;
                for (int i = 0; i < looks.Length; i++)
                    if (!looks[i].Leased && looks[i].Body != null &&
                        (oldest < 0 || looks[i].LastUse < looks[oldest].LastUse))
                        oldest = i;
                if (oldest < 0) return -1;
                CityPedestrianResources.DestroyObject(looks[oldest].Body.gameObject);
                looks[oldest].Body = null;
                cachedBodies--;
            }
            string id = first == AdultLookCount
                ? DefaultNpcPopulation.AmbientChildId(free - first)
                : DefaultNpcPopulation.AmbientPedestrianId(free - first, IsWoman(first));
            looks[free].Body = CityPedestrianFactory.CreateAmbientPresentation(transform, id);
            cachedBodies++;
            return free;
        }

        private int CollectContacts()
        {
            for (int i = 0; i < walkers.Length; i++)
            {
                Walker walker = walkers[i];
                var body = GetWalkerBody(i);
                // Stationary contacts have no walking link or right-of-way negotiation.
                contacts[i] = body == null ? default : new CityAmbientWalking.Contact(
                    walker.AtRail || walker.Playground.CurrentPhase != CityPlaygroundVisit.Phase.None
                        ? -MaximumStreetBodies - i - 2 : walker.Encounter,
                    body.transform.position, CityAmbientWalking.Radius, walker.Walk);
            }

            int count = walkers.Length;
            contacts[count++] = new CityAmbientWalking.Contact(-1, hero.position, heroRadius);
            var actors = city.Pedestrians.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor actor = actors[i];
                if (actor.IsSpawned && actor.CollisionEnabled && !actor.IsAttachedToVehicle)
                    contacts[count++] = new CityAmbientWalking.Contact(-i - 2, actor.Position, actor.AgentRadius);
            }
            return count;
        }

        private void AdvanceWalker(int index, ref Walker walker, CityPedestrianPresentation body,
            float delta, int contactCount, Vector3 heroHead)
        {
            int link = walker.Walk.Link;
            Vector3 previous = body.transform.position;
            bool moving = movement.Advance(ref walker.Walk, walker.Encounter, previous, walker.Speed,
                delta, contacts, contactCount, out Vector3 position, out Vector3 facing);
            if (walker.Walk.Link != link)
            {
                lanePopulation[link]--;
                lanePopulation[walker.Walk.Link]++;
            }
            contacts[index] = new CityAmbientWalking.Contact(walker.Encounter, position, CityAmbientWalking.Radius, walker.Walk);
            body.transform.SetPositionAndRotation(position, Quaternion.RotateTowards(body.transform.rotation,
                Quaternion.LookRotation(facing), 210f * delta));
            if (!walker.Walk.IsChild) body.UpdateStreetAttention(heroHead, city.Pedestrians);
            body.AdvanceTravelled(delta, moving, position - previous);

            if (walker.Walk.Stopped < 1.5f || !GameLog.IsVerbose || walker.Age < walker.NextBlockedLog) return;
            walker.NextBlockedLog = walker.Age + 15f;
            int blocker = walker.Walk.Blocker;
            string kind = blocker == 0 ? "surface" : blocker == -1 ? "hero" : blocker < 0 ? "resident" : "ambient";
            GameLog.Debug("pedestrian", "ambient_route_blocked", GameLog.Field("encounter", walker.Encounter),
                GameLog.Field("link", plan.LinkId(walker.Walk.Link)), GameLog.Field("blocker_kind", kind),
                GameLog.Field("blocked_s", walker.Walk.Stopped), GameLog.Field("x", position.x),
                GameLog.Field("y", position.y), GameLog.Field("z", position.z));
        }

        private bool HasLocalRoom(Vector3 point, int exceptSlot = -1)
        {
            int ambient = CountNear(point, exceptSlot);
            if (ambient >= LocalAmbientLimit) return false;
            int count = ambient;
            var actors = city.Pedestrians.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor actor = actors[i];
                if (actor.IsSpawned && !actor.IsAttachedToVehicle && OnSameStretch(point, actor.Position) &&
                    ++count >= LocalStreetLimit) return false;
            }
            return true;
        }

        internal bool ClearOfPeople(Vector3 point)
        {
            float squared = SpawnSpacing * SpawnSpacing;
            if ((point - hero.position).sqrMagnitude < squared) return false;
            IReadOnlyList<CityPedestrianActor> actors = city.Pedestrians.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor actor = actors[i];
                if (actor.IsSpawned && (point - actor.Position).sqrMagnitude < squared)
                    return false;
            }
            for (int i = 0; i < walkers.Length; i++)
            {
                if (walkers[i].Encounter == 0) continue;
                if ((point - looks[walkers[i].Look].Body.transform.position).sqrMagnitude < squared) return false;
            }
            return true;
        }

        private void Release(int index)
        {
            Walker walker = walkers[index];
            if (walker.Encounter == 0) return;
            var body = looks[walker.Look].Body;
            walker.Playground.Cancel(body, walker.Encounter);
            if (!walker.AtRail) lanePopulation[walker.Walk.Link]--;
            body.gameObject.SetActive(false);
            looks[walker.Look].Leased = false;
            walkers[index] = default;
            ActiveCount--;
            if (walker.Walk.IsChild) activeChildren--;
        }

        private static bool IsWoman(int look) => look >= DefaultNpcPopulation.AmbientAppearancesPerGender && look < AdultLookCount;

        private void OnDisable()
        {
            for (int i = 0; i < walkers.Length; i++) Release(i);
            spawnCountdown = 0f;
        }
    }
}
