using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianDirector
    {
        private static readonly Unity.Profiling.ProfilerMarker ResidentSimulationMarker = new Unity.Profiling.ProfilerMarker("City.ResidentLife");
        private readonly CityPedestrianSpatialIndex pedestrianIndex = new CityPedestrianSpatialIndex();
        private readonly Dictionary<CityPedestrianPresentation, int> residentIds = new Dictionary<CityPedestrianPresentation, int>();
        private Func<int, CityPedestrianPresentation> createResidentPresentation;
        private int[] actorResidents = Array.Empty<int>();
        private double[] actorActivityStarts = Array.Empty<double>();
        private CityResidentWorld residentWorld;
        private int residentCursor;
        private float residentSpawnTime;
        private readonly float[] blockedResidentsUntil = new float[CityResidentLifeRules.ResidentCount];
        public bool HasResidentLife => createResidentPresentation != null;
        public CityResidentWorld Residents => residentWorld;
        public int LogicalResidentCount => HasResidentLife ? CityResidentLifeRules.ResidentCount : 0;

        private void InitializeResidentLife(Func<int, CityPedestrianPresentation> factory)
        {
            createResidentPresentation = factory;
            if (factory == null) return;
            actorResidents = new int[actors.Count];
            actorActivityStarts = new double[actors.Count];
            for (int i = 0; i < actorResidents.Length; i++) actorResidents[i] = -1;
            residentWorld = new CityResidentWorld(plan);
            spawnCooldown = .1f;
        }

        public void ConfigureResidentPlaces(CityLayout layout)
        {
            if (!HasResidentLife) return;
            if (ActiveCount != 0) throw new InvalidOperationException("Resident places must be configured before presentation starts.");
            residentWorld = new CityResidentWorld(plan, layout);
        }

        /// <summary>Called only beneath the composition overlay, one body per
        /// yielded construction step. Near residents then exist before reveal.</summary>
        public bool WarmResidentPopulation()
        {
            if (!HasResidentLife || ActiveCount >= CurrentActiveLimit) return false;
            for (int retry = 0; retry < MaximumSpawnProbes; retry++)
                if (TrySpawnResident(true)) return true;
            return false;
        }

        public int ResidentIndex(CityPedestrianActor actor)
        {
            for (int i = 0; i < actors.Count; i++) if (actors[i] == actor) return HasResidentLife ? actorResidents[i] : -1;
            return -1;
        }

        private void AdvanceResidentLife(float deltaTime, bool warming)
        {
            if (deltaTime <= 0f && !warming) return;
            residentSpawnTime += deltaTime;
            RefreshPlayerTravel(deltaTime);
            personalSpace.Advance(deltaTime);
            pedestrianIndex.Rebuild(actors);
            Vector3 heroHead = ResolveHeroFocus();
            int day = GameSessionState.GameDayIndex;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor actor = actors[i];
                if (!actor.IsSpawned) continue;
                int resident = actorResidents[i];
                if (resident >= 0 && !actor.IsRouteBound)
                {
                    CityResidentLifeSample schedule = residentWorld.Schedule(resident, day, minute);
                    // Change goals at schedule boundaries. Public out-and-back
                    // walks reverse only after physically reaching their end.
                    if (actorActivityStarts[i] != schedule.StartedAt)
                    {
                        actorActivityStarts[i] = schedule.StartedAt;
                        int goal = residentWorld.Destination(resident, schedule.Activity);
                        actor.SetResidentDestination(goal, residentWorld.DistancesTo(goal));
                    }
                    else if (actor.IsAtResidentDestination && IsPublicResidentActivity(schedule.Activity))
                    {
                        int home = residentWorld.Destination(resident, CityResidentActivity.Home);
                        int publicGoal = residentWorld.Destination(resident, schedule.Activity);
                        int goal = actor.ResidentDestinationNode == home ? publicGoal : home;
                        if (goal != actor.ResidentDestinationNode)
                            actor.SetResidentDestination(goal, residentWorld.DistancesTo(goal));
                    }
                }
                float squaredDistance = PlanarSquaredDistance(actor.Position, player.position);
                bool ordinaryWalk = (actor.MotionState == CityPedestrianMotionState.Walking ||
                    actor.MotionState == CityPedestrianMotionState.WaitingAtDestination) && !actor.IsPersonalSpaceReacting;
                actor.Presentation.MinimumEvaluationInterval = !ordinaryWalk || squaredDistance < 24f * 24f
                    ? 0f : squaredDistance < 48f * 48f ? 1f / 15f : 1f / 8f;
                actor.Advance(deltaTime, ResolveAvoidance(actor, i), null, null, heroHead);
                pedestrianIndex.Update(i, actor);
                squaredDistance = PlanarSquaredDistance(actor.Position, player.position);
                // Arrival never makes somebody vanish within view. Distant
                // excess/finished visits retire under the existing fog contract.
                bool hidden = squaredDistance >= MinimumConnectedSpawnDistance * MinimumConnectedSpawnDistance;
                bool indoors = resident >= 0 && actor.IsAtResidentDestination &&
                    !IsPublicResidentActivity(residentWorld.Schedule(resident, day, minute).Activity);
                bool activityOwned = actor.IsRouteBound || actor.IsPersonalSpaceReacting ||
                    (actor.MotionState != CityPedestrianMotionState.Walking &&
                     actor.MotionState != CityPedestrianMotionState.WaitingAtDestination &&
                     actor.MotionState != CityPedestrianMotionState.RouteEnded);
                if (!activityOwned && (squaredDistance > DespawnDistance * DespawnDistance ||
                    hidden && (indoors || ActiveCount > CurrentActiveLimit))) ReleaseActor(i);
            }
            spawnCooldown = Mathf.Max(0f, spawnCooldown - deltaTime);
            if (ActiveCount < GetEffectivePopulationLimit() && spawnCooldown <= 0f)
            {
                bool spawned = TrySpawnResident(false);
                spawnCooldown = spawned ? .4f : 1f;
            }
        }

        private static bool IsPublicResidentActivity(CityResidentActivity activity) =>
            activity == CityResidentActivity.Walking || activity == CityResidentActivity.Shopping;

        private bool TrySpawnResident(bool allowNear)
        {
            int slot = FindAvailableActorIndex();
            if (slot < 0) return false;
            int best = -1;
            uint bestPriority = uint.MaxValue;
            CityResidentWorld.StreetSample chosen = default;
            float minimumSquared = allowNear ? 4f * 4f : MinimumConnectedSpawnDistance * MinimumConnectedSpawnDistance;
            int day = GameSessionState.GameDayIndex;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            for (int offset = 0; offset < CityResidentLifeRules.ResidentCount; offset++)
            {
                int id = (residentCursor + offset) % CityResidentLifeRules.ResidentCount;
                if (blockedResidentsUntil[id] > residentSpawnTime || IsResidentPresented(id) || !residentWorld.TrySample(id, day, minute, out CityResidentWorld.StreetSample sample)) continue;
                float distance = PlanarSquaredDistance(sample.Position, player.position);
                if (distance < minimumSquared || distance > MaximumSpawnDistance * MaximumSpawnDistance) continue;
                if (!HasResidentClearance(sample)) continue;
                uint priority = CityResidentLifeRules.StreetPriority(id, day, plan.PopulationSeed);
                if (best < 0 || priority < bestPriority) { best = id; chosen = sample; bestPriority = priority; }
            }
            if (best < 0) return false;
            Physics.SyncTransforms();
            if (!IsCollisionActivationSafe(chosen.Position, plan.AgentRadius, actors[slot]))
            { blockedResidentsUntil[best] = residentSpawnTime + 5f; residentCursor = (best + 1) % CityResidentLifeRules.ResidentCount; return false; }
            CityPedestrianPresentation body = AcquireResidentPresentation(best);
            if (body == null) return false;
            var anchor = new CityPedestrianSpawnAnchor("resident:" + best, chosen.Position, chosen.FromNode, chosen.ToNode);
            initialApproachCompleted[slot] = true;
            if (!TryBindSpawn(slot, anchor, chosen.ToNode, unchecked((uint)best + plan.StableSeed), body)) return false;
            actorResidents[slot] = best;
            actorActivityStarts[slot] = chosen.Schedule.StartedAt;
            actors[slot].SetResidentDestination(chosen.Destination, residentWorld.DistancesTo(chosen.Destination));
            residentCursor = (best + 1) % CityResidentLifeRules.ResidentCount;
            return true;
        }

        private bool HasResidentClearance(CityResidentWorld.StreetSample sample)
        {
            int laneCount = 0, destinationCount = 0;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor other = actors[i];
                if (!other.IsSpawned || other.IsAttachedToVehicle) continue;
                if (other.ResidentDestinationNode == sample.Destination && ++destinationCount >= 3) return false;
                if (PlanarSquaredDistance(other.Position, sample.Position) < profile.MinimumPeerSeparation * profile.MinimumPeerSeparation) return false;
                if ((other.PreviousNodeIndex == sample.FromNode && other.TargetNodeIndex == sample.ToNode) ||
                    (other.PreviousNodeIndex == sample.ToNode && other.TargetNodeIndex == sample.FromNode)) laneCount++;
            }
            return laneCount < profile.MaximumWalkersPerStreetLane;
        }

        private bool IsResidentPresented(int id)
        {
            for (int i = 0; i < actorResidents.Length; i++)
                if (actorResidents[i] == id && actors[i].IsSpawned) return true;
            return false;
        }

        private CityPedestrianPresentation AcquireResidentPresentation(int id)
        {
            foreach (KeyValuePair<CityPedestrianPresentation, int> entry in residentIds)
                if (entry.Value == id) return IsPresentationInUse(entry.Key) ? null : entry.Key;
            if (presentationPool.Count >= profile.PoolSize)
            {
                int free = -1;
                for (int i = 0; i < presentationPool.Count; i++)
                    if (!IsPresentationInUse(presentationPool[i])) { free = i; break; }
                if (free < 0) return null;
                CityPedestrianPresentation removed = presentationPool[free];
                residentIds.Remove(removed); presentationPool.RemoveAt(free);
                removed.Shutdown(); CityPedestrianResources.DestroyObject(removed.gameObject);
            }
            CityPedestrianPresentation result = createResidentPresentation(id);
            result.transform.SetParent(poolRoot, false);
            presentationPool.Add(result); residentIds.Add(result, id);
            return result;
        }

        private CityPedestrianPresentation AcquireTransitResident(Vector3 position, uint hash)
        {
            int day = GameSessionState.GameDayIndex;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            for (int offset = 0; offset < CityResidentLifeRules.ResidentCount; offset++)
            {
                int id = (int)((hash + (uint)offset) % CityResidentLifeRules.ResidentCount);
                if (IsResidentPresented(id) || !residentWorld.TrySample(id, day, minute, out CityResidentWorld.StreetSample sample)) continue;
                if (PlanarSquaredDistance(position, sample.Position) > 14f * 14f) continue;
                CityPedestrianPresentation body = AcquireResidentPresentation(id);
                if (body != null && CanRideBus(body)) return body;
            }
            return null;
        }

        private void BindResidentIdentity(int slot, CityPedestrianPresentation body)
        {
            if (!HasResidentLife || !residentIds.TryGetValue(body, out int id)) return;
            actorResidents[slot] = id;
            CityResidentLifeSample schedule = residentWorld.Schedule(id, GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes);
            actorActivityStarts[slot] = schedule.StartedAt;
            int goal = residentWorld.Destination(id, schedule.Activity);
            actors[slot].SetResidentDestination(goal, residentWorld.DistancesTo(goal));
        }

        private void RetainResident(int slot)
        {
            if (!HasResidentLife || actorResidents[slot] < 0) return;
            residentWorld.Retain(actorResidents[slot], actors[slot], GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes);
            actorResidents[slot] = -1;
        }
    }
}
