using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityBusNpcPassengerController
    {
        private const float ResidentRefreshInterval = .25f;
        private CityResidentJourneyDirector journeys;
        private int vehicleIndex;
        private float residentRefreshDelay;
        private int pendingVisibleStop = -1;

        public void ConfigureResidentJourneys(CityResidentJourneyDirector director, int index)
        {
            if (journeys != null || !IsInitialized || index != busDirector.VehicleIndex)
                throw new InvalidOperationException("Resident service requires one initialized fleet vehicle.");
            journeys = director ?? throw new ArgumentNullException(nameof(director));
            vehicleIndex = index;
            if (busDirector.Actor != null) busDirector.Actor.ServiceStarted += OnResidentServiceStarted;
        }

        private void OnResidentServiceStarted(int stop)
        {
            // The shared calendar driver supplies historical stop events while the
            // city is detached. Live physical service waits for the open doors.
            if (!isActiveAndEnabled) return;
            pendingVisibleStop = busDirector.Actor.IsSpawned ? stop : -1;
            if (pendingVisibleStop >= 0) return;
            journeys.Advance(GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes);
            journeys.ServiceHiddenStop(vehicleIndex, stop);
        }

        public void ReleaseForTimeJump()
        {
            if (journeys == null) return;
            ReleaseEveryPassenger();
            foreach (var waiter in waiters)
                if (pedestrians.ResidentIndex(waiter.Actor) == waiter.Resident)
                    pedestrians.ReleaseRouteBoundActor(waiter.Actor);
            waiters.Clear();
            busWasSpawned = false;
            // This visit began physically before the City slept. Its arrival
            // will not be replayed by the subsequent historical timetable.
            var actor = busDirector.Actor;
            if (pendingVisibleStop >= 0 && actor != null &&
                actor.CurrentStopIndex == pendingVisibleStop && !actor.HasPlayerPassenger)
                journeys.ServiceHiddenStop(vehicleIndex, pendingVisibleStop);
            pendingVisibleStop = -1;
            residentRefreshDelay = 0f;
        }

        public void WarmResidentWaiters()
        {
            if (journeys != null && busDirector.VehicleIndexOnLine == 0)
                RefreshResidentWaiters(true);
        }

        private void RefreshResidentService(CityBusActor actor, float deltaTime)
        {
            if (actor == null) return;
            if (pendingVisibleStop >= 0 && (!actor.IsSpawned || actor.CurrentStopIndex != pendingVisibleStop))
            {
                // A hidden bus can finish the same call when the hero has left
                // the area. At a visible departure everyone still waiting keeps
                // their place for the next vehicle.
                if (!actor.IsSpawned && actor.CurrentStopIndex == pendingVisibleStop)
                {
                    journeys.Advance(GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes);
                    journeys.ServiceHiddenStop(vehicleIndex, pendingVisibleStop);
                }
                pendingVisibleStop = -1;
            }
            RefreshCabinViews(actor, deltaTime);
            residentRefreshDelay -= deltaTime;
            if (residentRefreshDelay > 0f && (!actor.IsSpawned || busWasSpawned)) return;
            residentRefreshDelay = ResidentRefreshInterval;
            // Only one controller owns the line's queue, independent of which
            // of its two vehicles reaches that kerb first.
            if (busDirector.VehicleIndexOnLine == 0) RefreshResidentWaiters(false);
        }

        private void RefreshResidentWaiters(bool covered)
        {
            if (NetworkWaiters() >= MaximumWaiters) return;
            var actors = pedestrians.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                var walker = actors[i];
                int id = pedestrians.ResidentAtSlot(i);
                if (id < 0 || !walker.IsSpawned || walker.IsRouteBound ||
                    walker.IsYieldingToVehicle || walker.IsPersonalSpaceReacting) continue;
                var leg = journeys.GetState(id);
                if ((leg.Phase != CityResidentJourneyPhase.ApproachingStop && leg.Phase != CityResidentJourneyPhase.Waiting) ||
                    leg.Line != busDirector.LineIndex || !waitPlan.TryGetWaitPoint(leg.BoardStop, out var point) ||
                    !point.IsReachableFrom(walker.TargetNodeIndex) ||
                    point.NodeDistances[walker.TargetNodeIndex] > RecruitGraphDistance ||
                    !TryReserveSlot(point, out var slot) ||
                    !walker.BeginStopApproach(point.PedestrianNodeIndex, slot, point.Facing, point.NodeDistances)) continue;
                waiters.Add(new Waiter(walker, point.StopIndex, point.StopId, slot) { Resident = id });
                if (NetworkWaiters() >= MaximumWaiters) return;
            }

            for (int i = 0; i < waitPlan.WaitPoints.Count; i++)
            {
                var point = waitPlan.WaitPoints[i];
                if (point.WaitSlots.Count == 0) continue;
                float squared = PlanarSquaredDistance(point.WaitSlots[0], player.position);
                if (squared > CityPedestrianDirector.MaximumSpawnDistance * CityPedestrianDirector.MaximumSpawnDistance ||
                    (!covered && squared < RuntimeSceneSetup.CityFarClipPlane * RuntimeSceneSetup.CityFarClipPlane)) continue;
                while (NetworkWaiters() < MaximumWaiters && TryReserveSlot(point, out Vector3 slot))
                {
                    int id = journeys.NextWaiting(vehicleIndex, point.StopIndex, excludePresented: true);
                    if (id < 0) break;
                    var walker = pedestrians.TrySpawnStopWaiter(slot, point.Facing, point.PedestrianNodeIndex,
                        point.NodeDistances, unchecked((uint)id + 1u), id);
                    if (walker == null) break;
                    waiters.Add(new Waiter(walker, point.StopIndex, point.StopId, slot) { Resident = id });
                }
            }
        }

        private bool AdvanceResidentQueue(Waiter waiter)
        {
            var leg = journeys.GetState(waiter.Resident);
            if (leg.Line != busDirector.LineIndex || leg.BoardStop != waiter.StopIndex ||
                (leg.Phase != CityResidentJourneyPhase.Waiting && leg.Phase != CityResidentJourneyPhase.ApproachingStop))
            {
                waiter.Walker.CancelStopApproach();
                return false;
            }
            if (PlanarSquaredDistance(waiter.Walker.Position, player.position) >
                CityPedestrianDirector.DespawnDistance * CityPedestrianDirector.DespawnDistance)
            {
                pedestrians.ReleaseRouteBoundActor(waiter.Walker);
                return false;
            }
            if (waiter.Walker.MotionState != CityPedestrianMotionState.WaitingAtStop) return true;
            if (leg.Phase == CityResidentJourneyPhase.ApproachingStop && !journeys.MarkAtStop(waiter.Resident)) return true;
            if (lines == null || !IsFirstVisibleWaiter(waiter)) return true;
            for (int i = 0; i < lines.Count; i++)
            {
                var controller = lines[i];
                var actor = controller.busDirector.Actor;
                if (controller.busDirector.LineIndex != leg.Line || actor == null || !actor.IsSpawned ||
                    !actor.DoorsFullyOpen || actor.CurrentStopIndex != waiter.StopIndex) continue;
                controller.TryBeginBoarding(waiter, actor);
                if (waiter.Walker.MotionState != CityPedestrianMotionState.Boarding) continue;
                if (controller == this) return true;
                controller.waiters.Add(waiter);
                return false;
            }
            return true;
        }

        private bool IsFirstVisibleWaiter(Waiter candidate)
        {
            double readyAt = journeys.GetState(candidate.Resident).ReadyAt;
            foreach (var other in waiters)
            {
                if (other == candidate || other.StopIndex != candidate.StopIndex || other.Resident < 0 ||
                    other.Walker.MotionState != CityPedestrianMotionState.WaitingAtStop) continue;
                var leg = journeys.GetState(other.Resident);
                if (leg.Phase == CityResidentJourneyPhase.Waiting &&
                    (leg.ReadyAt < readyAt || (leg.ReadyAt == readyAt && other.Resident < candidate.Resident))) return false;
            }
            return true;
        }
    }
}
