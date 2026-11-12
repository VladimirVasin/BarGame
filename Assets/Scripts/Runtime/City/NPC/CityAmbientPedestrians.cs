using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>City-only visual walkers. The calendar and transit systems never own these bodies.</summary>
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
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("City.AmbientPedestrians");

        private readonly Walker[] walkers = new Walker[MaximumActive];
        private readonly Look[] looks = new Look[DefaultNpcPopulation.AmbientAppearancesPerGender * 2];
        private readonly List<CityAmbientWalkPlan.Entry> entries = new List<CityAmbientWalkPlan.Entry>(96);
        private readonly List<SpawnCandidate> candidates = new List<SpawnCandidate>(MaximumEntryProbes);
        private readonly CityAmbientWalking.Contact[] contacts = new CityAmbientWalking.Contact[MaximumStreetBodies + 1];
        private CityGameRoot city;
        private CityAmbientWalkPlan plan;
        private CityAmbientWalking movement;
        private Transform hero;
        private float heroRadius;
        private int[] lanePopulation;
        private int cachedBodies;
        private int serial;
        private uint entrySearch;
        private float spawnCountdown, crowdCheckCountdown;

        internal int ActiveCount { get; private set; }

        private struct Look
        {
            public CityPedestrianPresentation Body;
            public int LastUse;
            public bool Leased;
        }

        private struct Walker
        {
            public int Encounter, Look;
            public CityAmbientWalking.Route Walk;
            public float Age, Speed, NextBlockedLog;
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
            ambient.plan = new CityAmbientWalkPlan(city.PedestrianPlan, city.Layout, city.BusPlans);
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

        private int Target() => Mathf.Min(MaximumActive,
            CityResidentLifeRules.TargetAmbientPopulation(GameSessionState.GameTimeOfDayMinutes));

        private void LateUpdate()
        {
            float delta = Time.deltaTime;
            if (city == null || !city.IsInitialized || city.IsDormant || delta <= 0f) return;
            using var marker = TickMarker.Auto();
            int target = Target();
            int contactCount = CollectContacts();
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
                bool canRotateCast = distanceSquared > 15f * 15f &&
                    (walker.Age > 28f || ActiveCount > target || walker.Age > 12f && distanceSquared > 24f * 24f);
                if ((beyondLocalStreets || canRotateCast || checkCrowd && walker.Age > 5f) &&
                    city.Visibility.CanChangeAmbientBodyAt(position) &&
                    (beyondLocalStreets || canRotateCast || !HasLocalRoom(position, index)))
                {
                    Release(index);
                    contacts[index] = default;
                    continue;
                }
                AdvanceWalker(index, ref walker, body, Mathf.Min(delta, .1f), distanceSquared, contactCount);
            }

            spawnCountdown -= delta;
            if (ActiveCount >= target || spawnCountdown > 0f) return;
            spawnCountdown = TrySpawn(false) ? .9f : 1.2f;
        }

        private bool TrySpawn(bool warming)
        {
            if (ActiveCount >= MaximumActive || ActiveCount + city.Pedestrians.ActiveCount >= MaximumStreetBodies)
                return false;
            Vector3 player = hero.position;
            Vector3 interest = player + Vector3.ProjectOnPlane(hero.forward, Vector3.up) * 6f;
            plan.CollectEntries(player, EntryRadius, entries);
            entrySearch++;
            candidates.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                CityAmbientWalkPlan.Entry entry = entries[i];
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
                return Spawn(chosen, direction);
            }
            return false;
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

        private bool Spawn(CityAmbientWalkPlan.Entry entry, int direction)
        {
            int slot = 0;
            while (slot < walkers.Length && walkers[slot].Encounter != 0) slot++;
            if (slot == walkers.Length) return false;
            int look = AcquireLook(PreferWoman());
            if (look < 0) return false;
            int encounter = ++serial;
            uint hash = CityPedestrianStableHash.Combine(city.PedestrianPlan.StableSeed, unchecked((uint)encounter));
            float phase = (hash & 255u) / 256f;
            float pace = Mathf.Lerp(.92f, 1.08f, phase);
            CityPedestrianPresentation body = looks[look].Body;
            plan.Sample(entry.Link, entry.Distance, out Vector3 tangent);
            body.transform.SetPositionAndRotation(entry.Position, Quaternion.LookRotation(tangent * direction));
            body.gameObject.SetActive(true);
            NpcFootstepSources.Unregister(body.transform);
            body.ConfigureCycle(pace, phase);
            body.Advance(0f, true, true);
            looks[look].Leased = true;
            looks[look].LastUse = encounter;
            walkers[slot] = new Walker
            {
                Encounter = encounter, Look = look,
                Walk = new CityAmbientWalking.Route(entry.Link, direction, entry.Distance),
                Speed = pace * CityResidentWorld.WalkingSpeed
            };
            lanePopulation[entry.Link]++;
            ActiveCount++;
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
                if (walker.Encounter == 0 ||
                    (looks[walker.Look].Body.transform.position - hero.position).sqrMagnitude > EntryRadius * EntryRadius)
                    continue;
                // Count arrivals approaching from just outside the shot, too.
                count++;
                if (IsWoman(walker.Look)) women++;
            }
            return CityResidentLifeRules.PreferWomanForNextArrival(count, women);
        }

        private int AcquireLook(bool woman)
        {
            int first = woman ? DefaultNpcPopulation.AmbientAppearancesPerGender : 0;
            int free = -1;
            int cached = -1;
            for (int i = first; i < first + DefaultNpcPopulation.AmbientAppearancesPerGender; i++)
            {
                if (looks[i].Leased) continue;
                if (looks[i].Body == null)
                {
                    if (free < 0 || looks[i].LastUse < looks[free].LastUse) free = i;
                }
                else if (cached < 0 || looks[i].LastUse < looks[cached].LastUse) cached = i;
            }
            if (cached >= 0 && (cachedBodies >= MaximumCachedBodies || free < 0 ||
                serial - looks[cached].LastUse >= 12))
                return cached;
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
            string id = DefaultNpcPopulation.AmbientPedestrianId(free - first, woman);
            looks[free].Body = CityPedestrianFactory.CreateAmbientPresentation(transform, id);
            cachedBodies++;
            return free;
        }

        private int CollectContacts()
        {
            for (int i = 0; i < walkers.Length; i++)
            {
                Walker walker = walkers[i];
                contacts[i] = walker.Encounter != 0
                    ? new CityAmbientWalking.Contact(walker.Encounter, looks[walker.Look].Body.transform.position,
                        CityAmbientWalking.Radius, walker.Walk)
                    : default;
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
            float delta, float distanceSquared, int contactCount)
        {
            int link = walker.Walk.Link;
            bool moving = movement.Advance(ref walker.Walk, walker.Encounter, body.transform.position, walker.Speed,
                delta, contacts, contactCount, out Vector3 position, out Vector3 facing);
            if (walker.Walk.Link != link)
            {
                lanePopulation[link]--;
                lanePopulation[walker.Walk.Link]++;
            }
            contacts[index] = new CityAmbientWalking.Contact(walker.Encounter, position, CityAmbientWalking.Radius, walker.Walk);
            body.transform.SetPositionAndRotation(position, Quaternion.RotateTowards(body.transform.rotation,
                Quaternion.LookRotation(facing), 210f * delta));
            body.MinimumEvaluationInterval = city.Visibility.CanChangeAmbientBodyAt(position) ? 1f / 8f :
                distanceSquared < 15f * 15f ? 0f : 1f / 15f;
            body.Advance(delta, moving);

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

        private bool ClearOfPeople(Vector3 point)
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
            looks[walker.Look].Body.gameObject.SetActive(false);
            looks[walker.Look].Leased = false;
            lanePopulation[walker.Walk.Link]--;
            walkers[index] = default;
            ActiveCount--;
        }

        private static bool IsWoman(int look) => look >= DefaultNpcPopulation.AmbientAppearancesPerGender;

        private void OnDisable()
        {
            for (int i = 0; i < walkers.Length; i++) Release(i);
            spawnCountdown = 0f;
        }
    }
}
