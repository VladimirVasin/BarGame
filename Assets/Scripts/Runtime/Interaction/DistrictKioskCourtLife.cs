using UnityEngine;

namespace BarPromenade
{
    // The city owns each resident's journey and pooled body. The court leases
    // the already-present body just for the window, table and return dock.
    [DefaultExecutionOrder(150)]
    internal sealed class DistrictKioskCourtLife : MonoBehaviour
    {
        private enum Phase { Waiting, Window, Handover, Table, Sitting, Drinking, Rising, Return, Deposit, Exit }

        private sealed class Visit
        {
            internal int Resident, TableNode, Sips;
            internal CityResidentWorld.DistrictVisit Plan;
            internal CityPedestrianActor Actor;
            internal Transform Head;
            internal RidgeKioskVisitorHands Hands;
            internal VodkaBottleView Cup;
            internal BarPatronDrinkProp Drink;
            internal BarPatronDrinkingArmPose Pose;
            internal Phase Phase;
            internal float Time;
            internal bool Seated;
        }

        private DistrictKioskCourtWorld court;
        private RidgeKioskStation station;
        private CityResidentWorld residents;
        private CityResidentJourneyDirector journeys;
        private Transform hero;
        private Visit[] visits;
        private Visit atWindow;
        private RidgeKioskHeroHand bottleHands;
        private Vector3 handover;
        internal bool IsNear(Vector3 point) => (point - transform.position).sqrMagnitude < 34f * 34f;
        internal bool OnFloor(Vector3 point) => court.Plan.Contains(new Vector2(point.x, point.z));

        internal void Initialize(DistrictKioskCourtWorld world, CityResidentWorld people,
            CityResidentJourneyDirector travel, PlayerRuntime player)
        {
            court = world;
            if (!world.Plan.Residential) bottleHands = new RidgeKioskHeroHand();
            station = GetComponentInParent<RidgeKioskStation>();
            residents = people;
            journeys = travel;
            hero = player.GameObject.transform;
            station.BindHero(player.Interactor, hero.GetComponentInChildren<Player3DAssetRegistry>().Anchors.Head);
            handover = station.transform.TransformPoint(new Vector3(0f, 1.04f, 1.46f));
            visits = new Visit[court.Plan.Capacity];
            for (int i = 0; i < visits.Length; i++) visits[i] = new Visit();
        }

        internal void PrepareVisitorPresentation(CityPedestrianPresentation body) => bottleHands?.Fit(body);

        private void OnDestroy() => bottleHands?.Dispose();

        internal void Restore(CityPedestrianActor actor, int id, CityResidentWorld.DistrictVisit placement,
            double now, CityResidentLifeSample schedule)
        {
            Visit visit = Bind(actor, id, placement);
            if (now % 1440 >= CityResidentLifeRules.KioskLastOrderMinute && now - schedule.StartedAt < 15d)
            {
                Set(visit, Phase.Exit);
                Go(visit, placement.Gate);
                return;
            }
            Face(visit, Table(visit).position, 1f);
            CreateCup(visit);
            double age = now - schedule.StartedAt;
            int full = visit.Sips;
            visit.Sips = Mathf.Max(1, full - (int)(age / (court.Plan.Residential ? 35d : 15d)));
            visit.Cup.SetRemaining(visit.Sips / (float)full);
            Settle(visit, alreadySeated: true);
        }

        internal bool AdvanceVisitor(CityPedestrianActor actor, int id, CityResidentWorld.DistrictVisit placement,
            float step, int day, double minute)
        {
            Visit visit = visits[placement.Slot];
            CityResidentLifeSample schedule = residents.Schedule(id, day, minute);
            int table = residents.Destination(id, CityResidentActivity.Leisure);
            if (!visit.Actor)
            {
                var leg = journeys.GetState(id);
                if (schedule.Activity != CityResidentActivity.Leisure || leg.DestinationNode != table ||
                    actor.PreviousNodeIndex != placement.Gate && actor.TargetNodeIndex != placement.Gate && !OnFloor(actor.Position))
                    return false;
                visit = Bind(actor, id, placement);
                if (minute < CityResidentLifeRules.KioskLastOrderMinute) TrySendToWindow(visit);
            }

            bool leaving = schedule.Activity != CityResidentActivity.Leisure;
            bool canOrder = minute < CityResidentLifeRules.KioskLastOrderMinute;
            visit.Time += step;
            if ((leaving) &&
                visit.Phase is not (Phase.Rising or Phase.Return or Phase.Deposit or Phase.Exit))
            {
                if (visit.Phase == Phase.Drinking && visit.Pose && !visit.Pose.CanConverse &&
                    minute < CityResidentLifeRules.KioskCourtEmptyMinute - 3)
                    return true;
                StartLeaving(visit);
            }

            switch (visit.Phase)
            {
                case Phase.Waiting:
                    if (!At(visit, visit.TableNode)) { Go(visit, visit.TableNode); break; }
                    if (canOrder) TrySendToWindow(visit);
                    break;
                case Phase.Window:
                    if (!canOrder || visit.Time > 13f)
                    {
                        ReleaseWindow(visit);
                        Set(visit, Phase.Waiting);
                        Go(visit, visit.TableNode);
                    }
                    else if (At(visit, placement.Window) && station.Vendor.AtCounter && Face(visit, handover, step))
                    {
                        CreateCup(visit);
                        Set(visit, Phase.Handover);
                    }
                    break;
                case Phase.Table:
                    if (At(visit, visit.TableNode) && Face(visit, Table(visit).position, step))
                    {
                        journeys.MarkWalkArrived(id);
                        Settle(visit, alreadySeated: false);
                    }
                    break;
                case Phase.Sitting:
                    if (visit.Time >= .72f) StartDrinking(visit);
                    break;
                case Phase.Drinking:
                    if (visit.Pose)
                    {
                        visit.Pose.SampleContacts = IsNear(hero.position);
                        visit.Pose.HoldNewSips = visit.Sips <= 0;
                    }
                    break;
                case Phase.Rising:
                    if (visit.Time >= .64f) GoToExit(visit);
                    break;
                case Phase.Return:
                    if (At(visit, placement.Return) && Face(visit, court.BottleDock, step))
                        Set(visit, Phase.Deposit);
                    break;
                case Phase.Deposit:
                    // LateUpdate owns grip release at the dock, once.
                    break;
                case Phase.Exit:
                    if (At(visit, placement.Gate))
                    {
                        journeys.LeaveKiosk(id, placement.Gate, schedule);
                        Release(visit);
                        return false;
                    }
                    break;
            }
            return true;
        }

        private Visit Bind(CityPedestrianActor actor, int id, CityResidentWorld.DistrictVisit placement)
        {
            Visit visit = visits[placement.Slot];
            visit.Resident = id;
            visit.Plan = placement;
            visit.TableNode = residents.Destination(id, CityResidentActivity.Leisure);
            visit.Actor = actor;
            visit.Head = actor.Presentation.Registry.HeadAnchor;
            visit.Hands = new RidgeKioskVisitorHands(actor);
            Set(visit, Phase.Waiting);
            return visit;
        }

        private void Settle(Visit visit, bool alreadySeated)
        {
            var presentation = visit.Actor.Presentation;
            var seat = court.Plan.Seated(visit.Plan.Slot) ? court.Seat(visit.Plan.Slot) : null;
            var ride = seat ? CityPedestrianDirector.GetActorArchetype(visit.Actor)?.SeatedRide : null;
            if (ride != null)
            {
                if (!alreadySeated) presentation.BeginGroundSeatTransition(.72f);
                visit.Seated = presentation.TrySeat(seat, ride, Vector3.up, visit.Actor.Position, visit.Actor.transform.forward);
                if (!visit.Seated) presentation.CancelGroundSeatTransition();
            }
            if (visit.Seated && !alreadySeated) Set(visit, Phase.Sitting);
            else StartDrinking(visit);
        }

        private void StartDrinking(Visit visit)
        {
            visit.Cup.SetOpened(true);
            Transform table = Table(visit);
            Vector3 outward = court.transform.forward;
            Vector3 top = table.position;
            if (court.Plan.Table(visit.Plan.Slot) == 1 || !court.Plan.Residential)
                top += Vector3.Project(visit.Actor.Position - top, court.transform.right) * .68f;
            var timeline = new BarPatronDrinkTimeline(unchecked(GameSessionState.CitySeed ^ (visit.Resident + 1) * 613));
            if (court.Plan.Residential && visit.Seated) timeline.ExtendCurrentRest(9f + visit.Plan.Slot * 7f);
            visit.Pose = visit.Hands.BindTable(visit.Drink, top, outward,
                court.Plan.Residential ? .27f : .28f, timeline,
                keepInHand: !visit.Seated && (court.Plan.Residential || visit.Plan.Slot != 1), seated: visit.Seated);
            visit.Pose.SipCompleted = () =>
            {
                if (visit.Sips <= 0 || !visit.Cup) return;
                visit.Cup.SetRemaining(--visit.Sips / (float)visit.Cup.Profile.SipCount);
            };
            Set(visit, Phase.Drinking);
        }

        private void CreateCup(Visit visit)
        {
            InventoryItemId item = court.Plan.Residential || visit.Plan.Slot == 2
                ? InventoryItemId.BeerCan : InventoryItemId.VodkaBottle;
            visit.Cup = VodkaBottleView.Create(transform, item);
            visit.Cup.SetOpened(false);
            visit.Cup.transform.position = handover;
            visit.Drink = BarPatronDrinkProp.FromContainer(visit.Cup);
            visit.Sips = visit.Cup.Profile.SipCount;
        }

        private void StartLeaving(Visit visit)
        {
            bool occupied = visit.Phase is Phase.Drinking or Phase.Sitting;
            if (occupied) court.MarkDeparture(visit.Plan.Slot);
            if (visit.Phase == Phase.Handover) RemoveCup(visit);
            StopPose(visit);
            ReleaseWindow(visit);
            if (visit.Seated)
            {
                visit.Actor.Presentation.BeginGroundSeatTransition(.64f);
                visit.Actor.Presentation.ClearSeat();
                visit.Seated = false;
                Set(visit, Phase.Rising);
            }
            else GoToExit(visit);
        }

        private void GoToExit(Visit visit)
        {
            bool returnBottle = !court.Plan.Residential && visit.Cup &&
                visit.Cup.ItemId == InventoryItemId.VodkaBottle && visit.Sips == 0;
            Set(visit, returnBottle ? Phase.Return : Phase.Exit);
            Go(visit, returnBottle ? visit.Plan.Return : visit.Plan.Gate);
        }

        private void LateUpdate()
        {
            if (visits == null || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            foreach (Visit visit in visits)
            {
                if (!visit.Actor || !visit.Cup || visit.Pose) continue;
                if (visit.Phase == Phase.Handover)
                {
                    float reach = Mathf.SmoothStep(0f, 1f, visit.Time / .68f);
                    float carry = Mathf.SmoothStep(0f, 1f, (visit.Time - .68f) / .43f);
                    visit.Hands.Receive(visit.Drink, handover, reach, carry, visit.Time >= .68f);
                    station.Vendor.SetHandover(handover + Vector3.up * .10f, Mathf.Sin(reach * Mathf.PI * .5f) * (1f - carry));
                    if (carry < 1f) continue;
                    ReleaseWindow(visit);
                    Set(visit, Phase.Table);
                    Go(visit, visit.TableNode);
                }
                else if (visit.Phase == Phase.Deposit)
                {
                    float release = Mathf.SmoothStep(0f, 1f, visit.Time / .84f);
                    visit.Hands.Receive(visit.Drink, court.BottleDock, 1f, 1f - release, true);
                    if (release < 1f) continue;
                    if (court.ReturnBottle(visit.Cup))
                    {
                        visit.Cup = null;
                        visit.Drink = null;
                        visit.Hands.RestoreContactCulling();
                    }
                    visit.Hands.ReleaseGrip();
                    Set(visit, Phase.Exit);
                    Go(visit, visit.Plan.Gate);
                }
                else visit.Hands.Carry(visit.Drink);
            }
        }

        internal void ReleaseResident(CityPedestrianActor actor, CityResidentWorld.DistrictVisit placement)
        {
            if (visits != null && visits[placement.Slot].Actor == actor) Release(visits[placement.Slot]);
        }

        private void Release(Visit visit)
        {
            ReleaseWindow(visit);
            StopPose(visit);
            if (visit.Seated) visit.Actor.Presentation?.ClearSeat();
            visit.Actor.Presentation?.CancelGroundSeatTransition();
            visit.Seated = false;
            RemoveCup(visit);
            visit.Hands.RestoreContactCulling();
            visit.Hands = null;
            visit.Actor = null;
            visit.Head = null;
        }

        private void TrySendToWindow(Visit visit)
        {
            if (atWindow != null || !station.TryClaimNpc(visit.Head)) return;
            atWindow = visit;
            Set(visit, Phase.Window);
            Go(visit, visit.Plan.Window);
        }

        private void ReleaseWindow(Visit visit)
        {
            if (atWindow != visit) return;
            station.Vendor.SetHandover(null, 0f);
            station.ReleaseNpc(visit.Head);
            atWindow = null;
        }

        private static void StopPose(Visit visit)
        {
            if (!visit.Pose) return;
            visit.Pose.ReleaseTableBinding();
            visit.Pose = null;
        }

        private static void RemoveCup(Visit visit)
        {
            if (visit.Cup)
            {
                visit.Cup.gameObject.SetActive(false);
                Destroy(visit.Cup.gameObject);
            }
            visit.Cup = null;
            visit.Drink = null;
            visit.Hands.ReleaseGrip();
        }

        private Transform Table(Visit visit) => court.Table(court.Plan.Table(visit.Plan.Slot));
        private static bool At(Visit visit, int node) => visit.Actor.IsAtResidentDestination && visit.Actor.ResidentDestinationNode == node;
        private void Go(Visit visit, int node)
        {
            if (visit.Actor.ResidentDestinationNode != node)
                visit.Actor.SetResidentDestination(node, residents.DistancesTo(node));
        }

        private static void Set(Visit visit, Phase phase) { visit.Phase = phase; visit.Time = 0f; }

        private static bool Face(Visit visit, Vector3 point, float step)
        {
            Transform body = visit.Actor.transform;
            Vector3 toward = Vector3.ProjectOnPlane(point - body.position, Vector3.up);
            if (toward.sqrMagnitude < .001f) return true;
            Quaternion target = Quaternion.LookRotation(toward, Vector3.up);
            body.rotation = Quaternion.RotateTowards(body.rotation, target, 310f * step);
            return Quaternion.Angle(body.rotation, target) < 4f;
        }

        private void OnDisable()
        {
            if (visits == null) return;
            foreach (Visit visit in visits)
                if (visit.Actor) Release(visit);
        }
    }
}
