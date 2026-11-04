using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityBusNpcPassengerController
    {
        private sealed class CabinView
        {
            internal int Resident = -1;
            internal CityPedestrianPresentation Body;
            internal CityBusRidePlan BoardingPlan;
            internal float Elapsed;
        }

        private readonly CabinView[] cabin = new CabinView[CityBusActor.MaximumNpcOccupants];

        // Used by diagnostics and by systems observing a particular commuter.
        // A riding resident can be visible without a street physics slot.
        public CityPedestrianPresentation GetCabinBody(int resident)
        {
            foreach (var view in cabin)
                if (view != null && view.Resident == resident) return view.Body;
            return null;
        }

        private bool MoveBoardedWalkerIntoCabin(Waiter waiter, CityBusActor actor)
        {
            int slot = CityBusActor.NpcSlotForPlace(waiter.SeatIndex);
            if (slot < 0) return false;
            return BindCabinView(actor, slot, waiter.Resident, waiter);
        }

        private bool BindCabinView(CityBusActor actor, int slot, int resident, Waiter boarding)
        {
            var view = cabin[slot] ??= new CabinView();
            int place = CityBusActor.NpcPlaceIndices[slot];
            if (view.Body != null || !TryGetCabinRootPose(actor, place, out var local, out var rotation)) return false;
            bool reserved = boarding == null ? actor.TryAttachNpcPassengerAtPlace(view, place) :
                actor.TransferNpcPlace(boarding, view, place);
            if (!reserved) return false;
            var body = pedestrians.TakeCabinBody(resident, boarding?.Walker);
            if (body == null)
            {
                if (boarding != null) actor.TransferNpcPlace(view, boarding, place);
                else actor.ReleasePassenger(view);
                return false;
            }
            // Parenting to the sprung cabin keeps both the rig and its contact
            // targets together even between reduced-rate animation samples.
            body.transform.SetParent(actor.transform, false);
            body.transform.SetLocalPositionAndRotation(local, rotation);
            body.transform.localScale = Vector3.one;
            body.transform.SetParent(actor.Presentation.SuspensionVisual, true);
            if (!TryBindCabinPose(body, actor, place))
            {
                pedestrians.ReturnCabinBody(resident, body);
                actor.ReleasePassenger(view);
                return false;
            }
            view.Body = body;
            view.Resident = resident;
            view.BoardingPlan = boarding?.Plan;
            view.Elapsed = 0f;
            return true;
        }

        private void RefreshCabinViews(CityBusActor actor, float deltaTime)
        {
            if (!actor.IsSpawned) return;
            float distance = actor.GetClosestPlanarBodyDistance(player.position);
            float interval = actor.HasPlayerPassenger || distance < 12f ? 0f :
                distance < 30f ? 1f / 12f : 1f / 5f;
            // A distant approaching service has several seconds to construct its
            // occupied places behind the fog. A warp/close reveal binds before render.
            int bindBudget = distance > CityBusDirector.FogHiddenDistance ? 1 : cabin.Length;
            bool canExit = deltaTime > 0f && actor.DoorsFullyOpen;
            for (int slot = 0; slot < cabin.Length; slot++)
            {
                int resident = journeys.RiderAt(vehicleIndex, slot);
                var view = cabin[slot];
                if (view != null && view.Body != null && view.Resident != resident)
                    ReleaseCabinView(actor, view);
                if (resident < 0) continue;
                if (view == null || view.Body == null)
                {
                    if (bindBudget <= 0 || journeys.IsStreetPresented(resident) ||
                        !BindCabinView(actor, slot, resident, null)) continue;
                    bindBudget--;
                    view = cabin[slot];
                }
                view.Elapsed += deltaTime;
                if (view.Elapsed > 0f && view.Elapsed >= interval)
                {
                    view.Body.Advance(view.Elapsed, false);
                    view.Elapsed = 0f;
                }
                if (canExit && actor.CurrentStopIndex == journeys.GetState(resident).ExitStop &&
                    BeginCabinExit(actor, slot, view)) canExit = false;
            }
        }

        private bool BeginCabinExit(CityBusActor actor, int slot, CabinView view)
        {
            int place = CityBusActor.NpcPlaceIndices[slot];
            var bus = actor.Presentation.Registry;
            if (!waitPlan.TryGetWaitPoint(actor.CurrentStopIndex, out var stop) || stop.WaitSlots.Count == 0)
                return false;
            // All riders land on this stop's validated pavement places. Only
            // their order of preference varies with the door. A busy aisle
            // or a full landing leaves the resident on the sprung cabin rig.
            Transform door = actor.CanUsePassengerDoor(view, CityBusPassengerDoor.Front, place)
                ? bus.FrontDoorEntryAnchor
                : actor.CanUsePassengerDoor(view, CityBusPassengerDoor.Rear, place) ? bus.RearDoorEntryAnchor : null;
            if (door == null || !TryChooseExit(stop, door.position, null, out _) ||
                !TryGetCabinRootPose(actor, place, out var local, out var rotation)) return false;
            var walker = pedestrians.TryStartCabinExit(view.Resident, view.Body, stop.PedestrianNodeIndex,
                actor.transform.TransformPoint(local), actor.Rotation * rotation);
            if (walker == null) return false;
            var waiter = new Waiter(walker, stop.StopIndex, stop.StopId, stop.WaitSlots[0])
            {
                Resident = view.Resident, SeatIndex = place, Plan = view.BoardingPlan,
                RideRootLocalPosition = local, RideRootLocalRotation = rotation,
                BoardedServiceOrdinal = actor.ServiceOrdinal - 1
            };
            view.Body = null;
            view.Resident = -1;
            view.BoardingPlan = null;
            if (!BeginPassengerRide(walker, actor, place, true) || !actor.TransferNpcPlace(view, waiter, place))
            {
                pedestrians.ReleaseRouteBoundActor(walker);
                actor.ReleasePassenger(view);
                return false;
            }
            waiters.Add(waiter);
            // Claim this exit before the queue on either vehicle attempts to
            // use the same stretch of aisle later in this service tick.
            AdvanceRide(waiter, actor);
            return true;
        }

        private void ReleaseCabinView(CityBusActor actor, CabinView view)
        {
            if (actor != null) actor.ReleasePassenger(view);
            if (pedestrians != null) pedestrians.ReturnCabinBody(view.Resident, view.Body);
            view.Body = null;
            view.Resident = -1;
            view.BoardingPlan = null;
        }

        private void ReleaseCabinViews(CityBusActor actor)
        {
            foreach (var view in cabin)
                if (view != null && view.Body != null) ReleaseCabinView(actor, view);
        }
    }
}
