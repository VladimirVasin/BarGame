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
        private CityResidentJourneyDirector journeys;
        private RidgeKioskCourtLife kioskCourt;
        private CityBusDirector residentFleet;
        internal CityAltitudeVisibility StreetVisibility { get; set; }
        internal CityAmbientPedestrians AmbientStreet { get; set; }

        internal bool CanChangeStreetBodyAt(Vector3 feet) =>
            StreetVisibility == null || StreetVisibility.CanChangeBodyAt(feet);

        public void ConfigureResidentJourneys(CityResidentJourneyDirector director)
        {
            journeys = director ?? throw new ArgumentNullException(nameof(director));
        }

        internal void ConfigureKioskCourt(RidgeKioskCourtLife court) => kioskCourt = court;
        internal bool IsNearKioskCourt => kioskCourt != null && kioskCourt.IsNear(player.position);

        public void ReleaseResidentPresentationsForTimeJump()
        {
            personalSpace?.Reset();
            ReleaseAllActors();
        }

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

        internal bool WarmKioskPopulation()
        {
            if (!HasResidentLife || !IsNearKioskCourt || ActiveCount >= CurrentActiveLimit) return false;
            for (int retry = 0; retry < MaximumSpawnProbes; retry++)
                if (TrySpawnResident(allowNear: true, settledKioskOnly: true)) return true;
            return false;
        }

        public int ResidentIndex(CityPedestrianActor actor)
        {
            for (int i = 0; i < actors.Count; i++) if (actors[i] == actor) return HasResidentLife ? actorResidents[i] : -1;
            return -1;
        }

        internal int ResidentAtSlot(int slot) => HasResidentLife ? actorResidents[slot] : -1;

        internal bool HasKioskVisit(int slot)
        {
            int resident = ResidentAtSlot(slot);
            return resident >= 0 && kioskCourt != null &&
                kioskCourt.HasVisit(resident, GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes);
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
                bool followsResidentSchedule = resident >= 0 && !actor.IsRouteBound && !actor.IsYieldingToVehicle;
                bool visitingKiosk = followsResidentSchedule && kioskCourt != null &&
                    kioskCourt.AdvanceResident(actor, resident, deltaTime, day, minute);
                if (followsResidentSchedule && !visitingKiosk)
                {
                    CityResidentLifeSample schedule = residentWorld.Schedule(resident, day, minute);
                    // Change goals at schedule boundaries. Public out-and-back
                    // walks reverse only after physically reaching their end.
                    if (journeys != null)
                    {
                        var leg = journeys.GetState(resident);
                        if (leg.Phase == CityResidentJourneyPhase.Walking && actor.IsAtResidentDestination &&
                            actor.ResidentDestinationNode == leg.DestinationNode)
                        {
                            journeys.MarkWalkArrived(resident);
                            leg = journeys.GetState(resident);
                        }
                        if (leg.DestinationNode >= 0 && actor.ResidentDestinationNode != leg.DestinationNode)
                            actor.SetResidentDestination(leg.DestinationNode, residentWorld.DistancesTo(leg.DestinationNode));
                    }
                    else if (actorActivityStarts[i] != schedule.StartedAt)
                    {
                        actorActivityStarts[i] = schedule.StartedAt;
                        int goal = residentWorld.Destination(resident, schedule.Activity);
                        actor.SetResidentDestination(goal, residentWorld.DistancesTo(goal));
                    }
                    else if (actor.IsAtResidentDestination && IsPublicResidentActivity(schedule.Activity))
                    {
                        int home = residentWorld.HomeFrontage(resident);
                        int publicGoal = residentWorld.Destination(resident, schedule.Activity);
                        int goal = actor.ResidentDestinationNode == home ? publicGoal : home;
                        if (goal != actor.ResidentDestinationNode)
                            actor.SetResidentDestination(goal, residentWorld.DistancesTo(goal));
                    }
                }
                float squaredDistance = PlanarSquaredDistance(actor.Position, player.position);
                bool ordinaryWalk = (actor.MotionState == CityPedestrianMotionState.Walking ||
                    actor.MotionState == CityPedestrianMotionState.WaitingAtDestination) && !actor.IsPersonalSpaceReacting &&
                    !actor.IsYieldingToVehicle;
                actor.Presentation.MinimumEvaluationInterval = !ordinaryWalk || squaredDistance < 24f * 24f
                    ? 0f : squaredDistance < 48f * 48f ? 1f / 15f : 1f / 8f;
                bool holdsKioskPosition = visitingKiosk && actor.IsAtResidentDestination;
                if (!holdsKioskPosition) SetEncounterMovement(actor, i);
                actor.Advance(deltaTime, attentionCandidate: heroHead, vehicleTraffic: vehicleTraffic,
                    holdResidentDestination: holdsKioskPosition);
                pedestrianIndex.Update(i, actor);
                squaredDistance = PlanarSquaredDistance(actor.Position, player.position);
                // Arrival never makes somebody vanish within view. Distant
                // excess/finished visits retire under the existing fog contract.
                bool hidden = squaredDistance >= MinimumConnectedSpawnDistance * MinimumConnectedSpawnDistance;
                bool indoors = resident >= 0 && actor.IsAtResidentDestination &&
                    (journeys != null ? IsIndoorDestination(journeys.GetState(resident)) :
                        !IsPublicResidentActivity(residentWorld.Schedule(resident, day, minute).Activity) &&
                        residentWorld.Schedule(resident, day, minute).Activity != CityResidentActivity.Leisure);
                bool activityOwned = actor.IsRouteBound || actor.IsPersonalSpaceReacting || actor.IsYieldingToVehicle ||
                    (actor.MotionState != CityPedestrianMotionState.Walking &&
                     actor.MotionState != CityPedestrianMotionState.WaitingAtDestination &&
                     actor.MotionState != CityPedestrianMotionState.RouteEnded);
                if (!activityOwned && (squaredDistance > DespawnDistance * DespawnDistance ||
                    hidden && (indoors || ActiveCount > CurrentActiveLimit)) && CanChangeStreetBodyAt(actor.Position))
                    ReleaseActor(i);
            }
            kioskCourt?.AdvanceSpeech(deltaTime, day, minute);
            spawnCooldown = Mathf.Max(0f, spawnCooldown - deltaTime);
            if (ActiveCount < GetEffectivePopulationLimit() && spawnCooldown <= 0f)
            {
                bool spawned = TrySpawnResident(false);
                spawnCooldown = spawned ? .4f : 1f;
            }
        }

        private static bool IsPublicResidentActivity(CityResidentActivity activity) =>
            activity == CityResidentActivity.Walking || activity == CityResidentActivity.Shopping;

        private static bool IsIndoorDestination(CityResidentJourneyState state) =>
            state.Phase == CityResidentJourneyPhase.AtDestination && state.Activity != CityResidentActivity.Leisure;

        private bool TrySpawnResident(bool allowNear, bool settledKioskOnly = false)
        {
            if (!CanUseStreetSlot()) return false;
            int slot = FindAvailableActorIndex();
            if (slot < 0) return false;
            int best = -1;
            uint bestPriority = uint.MaxValue;
            CityResidentWorld.StreetSample chosen = default;
            float minimumSquared = allowNear ? 4f * 4f : MinimumConnectedSpawnDistance * MinimumConnectedSpawnDistance;
            int day = GameSessionState.GameDayIndex;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            bool nearCourt = IsNearKioskCourt;
            bool chosenAtCourt = false;
            for (int offset = 0; offset < CityResidentLifeRules.ResidentCount; offset++)
            {
                int id = (residentCursor + offset) % CityResidentLifeRules.ResidentCount;
                bool kioskRegular = kioskCourt != null && residentWorld.KioskPlace(id) >= 0;
                if (settledKioskOnly && !kioskRegular) continue;
                if (blockedResidentsUntil[id] > residentSpawnTime || IsResidentPresented(id) || !residentWorld.TrySample(id, day, minute, out CityResidentWorld.StreetSample sample)) continue;
                float distance = PlanarSquaredDistance(sample.Position, player.position);
                bool kioskVisitor = kioskRegular && sample.Schedule.Activity == CityResidentActivity.Leisure;
                bool atCourt = kioskVisitor && sample.AtLeisureDestination;
                if (settledKioskOnly && !atCourt) continue;
                if (kioskRegular && !atCourt && kioskCourt.IsCourtFloor(sample.Position)) continue;
                float minimum = atCourt && allowNear ? 0f : kioskVisitor ? 4f * 4f : minimumSquared;
                if (distance < minimum || distance > MaximumSpawnDistance * MaximumSpawnDistance) continue;
                if (!allowNear)
                {
                    bool closeVisit = kioskVisitor && distance < minimumSquared && StreetVisibility != null;
                    if (closeVisit ? !StreetVisibility.CanChangeAmbientBodyAt(sample.Position) : !CanChangeStreetBodyAt(sample.Position)) continue;
                }
                if (!HasResidentClearance(sample, atCourt)) continue;
                uint priority = CityResidentLifeRules.StreetPriority(id, day, plan.PopulationSeed);
                if (nearCourt) priority = priority / 4u + (atCourt ? 0u : kioskVisitor ? 0x40000000u : 0x80000000u);
                if (best >= 0 && priority >= bestPriority) continue;
                best = id;
                chosen = sample;
                bestPriority = priority;
                chosenAtCourt = atCourt;
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
            CityPedestrianActor actor = actors[slot];
            actor.SetResidentDestination(chosen.Destination, residentWorld.DistancesTo(chosen.Destination));
            if (chosenAtCourt)
            {
                actor.SettleAtSpawnedDestination();
                kioskCourt.RestoreVisitorAtTable(actor, best, day, chosen.Schedule);
            }
            residentCursor = (best + 1) % CityResidentLifeRules.ResidentCount;
            return true;
        }

        // Only residents who will actually step down near the hero need a street
        // slot. Riders staying on the vehicle have a cabin presentation of their own.
        private bool CanUseStreetSlot()
        {
            if (journeys == null || residentFleet == null) return true;
            int free = actors.Count - ActiveCount;
            if (free <= 0) return false;
            int needed = 0;
            for (int i = 0; i < residentFleet.VehicleCount; i++)
            {
                var bus = residentFleet.GetVehicle(i);
                if (bus.Actor == null || !bus.Actor.HasRoutePhase) continue;
                for (int slot = 0; slot < CityBusActor.MaximumNpcOccupants; slot++)
                {
                    int id = journeys.RiderAt(i, slot);
                    if (id < 0) continue;
                    int stop = journeys.GetState(id).ExitStop;
                    if (stop < 0 || PlanarSquaredDistance(player.position, bus.Plan.Stops[stop].Position) >
                        CityBusDirector.MaximumSpawnDistance * CityBusDirector.MaximumSpawnDistance) continue;
                    if (bus.EstimatedSecondsToStop(stop) <= 30d && ++needed >= free) return false;
                }
            }
            return true;
        }

        private int ReclaimHiddenStreetSlot()
        {
            float farthest = CityBusDirector.FogHiddenDistance * CityBusDirector.FogHiddenDistance;
            int chosen = -1;
            for (int i = 0; i < actors.Count; i++)
            {
                var walker = actors[i];
                if (!walker.IsSpawned || walker.IsPersonalSpaceReacting || walker.IsYieldingToVehicle ||
                    (walker.MotionState != CityPedestrianMotionState.Walking &&
                     walker.MotionState != CityPedestrianMotionState.WaitingAtDestination)) continue;
                float distance = PlanarSquaredDistance(walker.Position, player.position);
                if (distance <= farthest || !CanChangeStreetBodyAt(walker.Position)) continue;
                farthest = distance;
                chosen = i;
            }
            if (chosen >= 0) ReleaseActor(chosen);
            return chosen;
        }

        private bool HasResidentClearance(CityResidentWorld.StreetSample sample, bool atCourt)
        {
            // Authored standing places only need capsule clearance. Street traffic
            // still gets room to walk and pass before it reaches the tables.
            float separation = atCourt ? 2f * plan.AgentRadius + CollisionActivationPadding : profile.MinimumPeerSeparation;
            int laneCount = 0, destinationCount = 0;
            int localCount = AmbientStreet != null ? AmbientStreet.CountNear(sample.Position) : 0;
            for (int i = 0; i < actors.Count; i++)
            {
                CityPedestrianActor other = actors[i];
                if (!other.IsSpawned || other.IsAttachedToVehicle) continue;
                if (!atCourt && CityAmbientPedestrians.OnSameStretch(other.Position, sample.Position) &&
                    ++localCount >= CityAmbientPedestrians.LocalStreetLimit) return false;
                if (other.ResidentDestinationNode == sample.Destination && ++destinationCount >= 3) return false;
                if (PlanarSquaredDistance(other.Position, sample.Position) < separation * separation) return false;
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
            if (!MakeRoomInBodyCache()) return null;
            CityPedestrianPresentation result = createResidentPresentation(id);
            try
            {
                if (kioskCourt != null && residentWorld.KioskPlace(id) >= 0) kioskCourt.PrepareVisitorPresentation(result);
            }
            catch
            {
                CityPedestrianResources.DestroyObject(result.gameObject);
                throw;
            }
            result.transform.SetParent(poolRoot, false);
            presentationPool.Add(result); residentIds.Add(result, id);
            return result;
        }

        private bool MakeRoomInBodyCache()
        {
            if (presentationPool.Count < profile.PoolSize) return true;
            for (int i = 0; i < presentationPool.Count; i++)
            {
                var spare = presentationPool[i];
                if (IsPresentationInUse(spare)) continue;
                presentationPool.RemoveAt(i);
                residentIds.Remove(spare);
                spare.Shutdown();
                CityPedestrianResources.DestroyObject(spare.gameObject);
                return true;
            }
            return false;
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
            journeys?.SetStreetPresented(id, true);
            CityResidentLifeSample schedule = residentWorld.Schedule(id, GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes);
            actorActivityStarts[slot] = schedule.StartedAt;
            int goal = journeys != null ? journeys.GetState(id).DestinationNode : residentWorld.Destination(id, schedule.Activity);
            if (goal >= 0) actors[slot].SetResidentDestination(goal, residentWorld.DistancesTo(goal));
        }

        internal void RetainPresentedJourneys()
        {
            if (journeys == null) return;
            int day = GameSessionState.GameDayIndex;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            for (int i = 0; i < actors.Count; i++)
                if (actorResidents[i] >= 0 && actors[i] != null && actors[i].IsSpawned)
                    residentWorld.Retain(actorResidents[i], actors[i], day, minute);
        }

        private void RetainResident(int slot)
        {
            if (!HasResidentLife || actorResidents[slot] < 0) return;
            kioskCourt?.ReleaseResident(actorResidents[slot], actors[slot]);
            residentWorld.Retain(actorResidents[slot], actors[slot], GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes);
            journeys?.SetStreetPresented(actorResidents[slot], false);
            actorResidents[slot] = -1;
        }
    }
}
