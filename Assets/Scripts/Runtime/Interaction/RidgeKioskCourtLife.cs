using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Visits use the resident's one street body and its usual graph, including the walk home.</summary>
    [DefaultExecutionOrder(325)]
    internal sealed partial class RidgeKioskCourtLife : MonoBehaviour
    {
        private enum Phase { Unbound, NeedDrink, ToWindow, Handover, ToTable, ReturningToHold, AtTable, Throwing, Leaving, Departed }
        private sealed class Visit
        {
            internal CityPedestrianActor Actor;
            internal Transform Head;
            internal RidgeKioskVisitorHands Hands;
            internal Phase Phase;
            internal double Until;
            internal float Elapsed, ThrowProgress, RetryDelay;
            internal int Cash, Orders, Sips, HoldNode, TableNode;
            internal long Request;
            internal bool FinalRestExtended;
            internal VodkaBottleView Container;
            internal BarPatronDrinkProp Drink;
            internal BarPatronDrinkingArmPose Drinking;
            internal BarPatronDrinkTimeline Timeline;
            internal bool IsAtTable => Actor && At(Actor, TableNode) && Phase is Phase.AtTable or Phase.NeedDrink or Phase.Throwing;
            internal void ConsumeSip()
            {
                if (!Container || Sips <= 0) return;
                Container.SetRemaining(--Sips / (float)Container.Profile.SipCount);
            }
        }

        private const float HandoverPickupSeconds = .72f;
        private const float HandoverSeconds = 1.20f;
        private const float WindowApproachTimeout = 45f;
        internal const float MinimumRefillWaitSeconds = 45f, MaximumRefillWaitSeconds = 75f;
        private readonly Visit[] visits = new Visit[RidgeKioskCourtPlan.Capacity];
        private RidgeKioskCourtWorld court;
        private RidgeKioskStation station;
        private CityResidentWorld residents;
        private CityResidentJourneyDirector journeys;
        private Transform hero, drops;
        private Collider heroBody;
        private AnimationClip throwClip;
        private RidgeKioskHeroHand heroHand;
        private long nextRequest;
        private Visit throwing;
        // The aisle stays owned after payment frees the counter.
        private Visit aisleTrip;
        private Vector3 handover;
        private int completedPurchases, repeatPurchases, releasedEmpties;
        internal int CompletedPurchases => completedPurchases;
        internal int RepeatPurchases => repeatPurchases;
        internal int ReleasedEmpties => releasedEmpties;
        internal CityPedestrianActor AisleTripActor => aisleTrip?.Actor;
        internal (CityPedestrianActor Actor, VodkaBottleView Container, float Progress) HeldThrow =>
            throwing == null ? default : (throwing.Actor, throwing.Container, throwing.ThrowProgress);
        internal bool IsCourtFloor(Vector3 position) => court.Plan.Slab.Contains(new Vector2(position.x, position.z));
        internal bool IsNear(Vector3 heroPosition) => (court.Plan.Origin - heroPosition).sqrMagnitude < 54f * 54f;
        internal int OccupiedPlaces
        {
            get { int count = 0; foreach (Visit visit in visits) if (visit != null && visit.IsAtTable) count++; return count; }
        }

        internal bool HasVisit(int id, int day, double minute)
        {
            int place = residents.KioskPlace(id);
            return place >= 0 && (visits[place].Actor || residents.Schedule(id, day, minute).Activity == CityResidentActivity.Leisure);
        }

        internal void Initialize(RidgeKioskCourtWorld world, RidgeKioskStation window, CityResidentWorld people,
            CityResidentJourneyDirector travel, PlayerRuntime player, Camera camera)
        {
            court = world;
            station = window;
            residents = people;
            journeys = travel;
            hero = player.GameObject.transform;
            heroBody = hero.GetComponent<CharacterController>();
            var heroRig = hero.GetComponentInChildren<Player3DAssetRegistry>();
            station.BindHero(player.Interactor, heroRig.Anchors.Head);
            heroHand = new RidgeKioskHeroHand();
            throwClip = PlayerVodkaActions.LoadThrowClip();
            if (!throwClip) throw new InvalidOperationException("Kiosk visitors need the shared empty-bottle throw clip.");
            drops = new GameObject("Kiosk discarded containers").transform;
            drops.SetParent(transform, false);
            handover = station.transform.TransformPoint(new Vector3(.56f, 1.04f, 1.46f));
            for (int i = 0; i < visits.Length; i++) visits[i] = new Visit();
            InitializeSpeech(camera);
        }

        internal void PrepareVisitorPresentation(CityPedestrianPresentation body) => heroHand.Fit(body);

        private void OnDestroy() => heroHand?.Dispose();

        internal void RestoreVisitorAtTable(CityPedestrianActor actor, int id, int day, CityResidentLifeSample schedule)
        {
            int place = residents.KioskPlace(id);
            Visit visit = visits[place];
            BindVisitor(visit, actor, place, residents.Destination(id, CityResidentActivity.Leisure), schedule);
            actor.transform.rotation = Quaternion.LookRotation(-RidgeKioskCourtPlan.PlaceDirection(place), Vector3.up);

            uint seed = CityPedestrianStableHash.Combine(unchecked((uint)GameSessionState.CitySeed),
                CityPedestrianStableHash.Combine((uint)id, (uint)day));
            InventoryItemId kind = (seed & 1u) == 0 ? InventoryItemId.BeerCan : InventoryItemId.VodkaBottle;
            CreateDrink(visit, kind);
            int full = visit.Container.Profile.SipCount;
            visit.Sips = 1 + (int)(CityPedestrianStableHash.Combine(seed, 1u) % (uint)full);
            visit.Container.SetRemaining(visit.Sips / (float)full);
            if (TryPrice(visit, kind, out var sale)) visit.Cash = sale.CashAfter;
            visit.Orders = 1;
            float progress = CityPedestrianStableHash.ToUnitFloat(CityPedestrianStableHash.Combine(seed, 2u));
            var timeline = visit.Sips == 1
                ? new BarPatronDrinkTimeline(unchecked((int)seed))
                : BarPatronDrinkTimeline.InProgress(unchecked((int)seed), progress);
            OccupyTable(visit, place, timeline);
        }

        internal bool AdvanceResident(CityPedestrianActor actor, int id, float step, int day, double minute)
        {
            int place = residents.KioskPlace(id);
            if (place < 0) return false;
            Visit visit = visits[place];
            var schedule = residents.Schedule(id, day, minute);
            var journey = journeys.GetState(id);
            int tableNode = residents.Destination(id, CityResidentActivity.Leisure);
            double now = day * 1440d + minute;
            if (schedule.Activity != CityResidentActivity.Leisure ||
                visit.Actor && visit.Phase != Phase.Departed && visit.Until <= now)
            {
                if (visit.Actor == actor && visit.Phase != Phase.Departed)
                {
                    if (visit.Phase == Phase.Throwing) return true;
                    if (visit.Phase != Phase.Leaving)
                    {
                        if (aisleTrip != null && aisleTrip != visit) return true;
                        aisleTrip = visit;
                        Leave(visit);
                    }
                    if (!At(actor, residents.KioskGateNode))
                    {
                        Go(actor, residents.KioskGateNode);
                        return true;
                    }
                    FinishAisleTrip(visit);
                    Set(visit, Phase.Departed);
                }
                // A visitor still walking to his table when the slot expires
                // must not complete a fictitious stop on the way home.
                if (journey.Activity == CityResidentActivity.Leisure)
                    journeys.LeaveKiosk(id, actor.TargetNodeIndex, schedule);
                return false;
            }

            if (actor.MotionState == CityPedestrianMotionState.ApproachingBench)
                actor.CancelBenchActivity();

            if (!visit.Actor || visit.Phase == Phase.Departed)
            {
                if (journey.DestinationNode != tableNode || journey.Activity != CityResidentActivity.Leisure ||
                    actor.IsRouteBound || actor.IsYieldingToVehicle || actor.IsPersonalSpaceReacting ||
                    !At(actor, tableNode) && actor.TargetNodeIndex != residents.KioskEntryNode &&
                    actor.PreviousNodeIndex != residents.KioskEntryNode && !IsCourtFloor(actor.Position)) return false;
                BindVisitor(visit, actor, place, tableNode, schedule);
                RequestDrink(visit);
            }

            bool takeOrders = minute < CityResidentLifeRules.KioskLastOrderMinute;
            visit.Elapsed += step;
            switch (visit.Phase)
            {
                case Phase.NeedDrink:
                    if (takeOrders && ReadyForWindow(visit) && TryReservePurchase(visit))
                        WalkToWindow(visit);
                    else if (!At(actor, visit.HoldNode)) Go(actor, visit.HoldNode);
                    else if (visit.Orders > 0) FaceTable(actor, place, step);
                    break;
                case Phase.ToWindow:
                    bool atWindow = At(actor, residents.KioskWindowNode);
                    if (!takeOrders || (!atWindow && visit.Elapsed > WindowApproachTimeout) ||
                        station.HeroHasWindow || !station.OwnsNpcWindow(visit.Head))
                        ReturnToHold(visit, 2f);
                    else if (atWindow && Face(actor, station.Vendor.Actor.Head.position - actor.Position, step) && station.Vendor.AtCounter)
                        BeginSale(visit, place, day);
                    break;
                case Phase.Handover:
                    // The release at the end of the visual handoff owns the only grant.
                    Face(actor, station.Vendor.Actor.Head.position - actor.Position, step);
                    break;
                case Phase.ToTable:
                    Go(actor, tableNode);
                    if (At(actor, tableNode) && FaceTable(actor, place, step))
                    {
                        journeys.MarkWalkArrived(id);
                        OccupyTable(visit, place, new BarPatronDrinkTimeline(
                            unchecked(GameSessionState.CitySeed ^ (id + 1) * 197 ^ visit.Orders * 839)));
                    }
                    break;
                case Phase.ReturningToHold:
                    if (At(actor, visit.HoldNode))
                    {
                        FinishAisleTrip(visit);
                        RequestDrink(visit, visit.RetryDelay);
                    }
                    break;
                case Phase.AtTable:
                    AdvanceTable(visit, place, step, now, takeOrders);
                    break;
            }
            return true;
        }

        private void AdvanceTable(Visit visit, int place, float step, double now, bool takeOrders)
        {
            if (!visit.Drinking)
            {
                FaceTable(visit.Actor, place, step);
                return;
            }

            visit.Drinking.SampleContacts = (visit.Actor.Position - hero.position).sqrMagnitude < 30f * 30f;
            visit.Drinking.HoldNewSips = visit.Sips == 0 || visit.Until - now < 2d;
            if (visit.Sips == 1 && visit.Drinking.CanConverse && !visit.FinalRestExtended)
            {
                uint seed = CityPedestrianStableHash.Combine(unchecked((uint)GameSessionState.CitySeed),
                    CityPedestrianStableHash.Combine((uint)visit.TableNode, (uint)visit.Orders));
                float pause = Mathf.Lerp(MinimumRefillWaitSeconds, MaximumRefillWaitSeconds,
                    CityPedestrianStableHash.ToUnitFloat(seed));
                visit.Timeline.ExtendCurrentRest(pause);
                visit.FinalRestExtended = true;
            }

            if (aisleTrip == visit && (!takeOrders || station.HeroHasWindow || !station.OwnsNpcWindow(visit.Head)))
                ReleasePurchaseReservation(visit);
            if (ReadyToFinish(visit))
                TryFinishDrink(visit, place, step, takeOrders);
            if (visit.Timeline != null)
                visit.Timeline.HoldAtEndOfRest = visit.Sips == 1 && takeOrders && aisleTrip != visit;
        }

        private bool ReadyToFinish(Visit visit) => visit.Phase == Phase.AtTable && visit.Drinking &&
            visit.Drinking.CanConverse && !SpeakingWith(visit) &&
            (visit.Sips == 0 || visit.Sips == 1 && visit.FinalRestExtended && visit.Timeline.RestFinished);

        private void TryFinishDrink(Visit visit, int place, float step, bool takeOrders)
        {
            if (visit.Request == 0) visit.Request = ++nextRequest;
            if (throwing != null || takeOrders && aisleTrip != null && aisleTrip != visit) return;
            if (!TryFindThrowDirection(visit, place, out Vector3 facing))
            {
                if (aisleTrip == visit) ReleasePurchaseReservation(visit);
                return;
            }

            // Reserve the round trip before the final sip. If the window is
            // busy, the guest keeps the drink in hand at the table.
            if (takeOrders && aisleTrip != visit && !TryReservePurchase(visit)) return;
            if (visit.Sips > 0 || !Face(visit.Actor, facing, step)) return;
            StopDrinking(visit);
            throwing = visit;
            visit.ThrowProgress = 0f;
            Set(visit, Phase.Throwing);
        }

        private void BindVisitor(Visit visit, CityPedestrianActor actor, int place, int tableNode, CityResidentLifeSample schedule)
        {
            if (visit.Actor) Release(visit);
            visit.Actor = actor;
            visit.Head = actor.Presentation.Registry.HeadAnchor;
            visit.Hands = new RidgeKioskVisitorHands(actor);
            visit.Until = schedule.EndsAt;
            visit.HoldNode = residents.KioskWaitingNode(place);
            visit.TableNode = tableNode;
            visit.Cash = 256;
            visit.Orders = 0;
            visit.Sips = 0;
        }

        private void OccupyTable(Visit visit, int place, BarPatronDrinkTimeline timeline)
        {
            FinishAisleTrip(visit);
            visit.HoldNode = visit.TableNode;
            visit.Request = 0;
            visit.FinalRestExtended = false;
            visit.Timeline = timeline;
            Set(visit, Phase.AtTable);
            visit.Container.SetOpened(true);
            visit.Drinking = visit.Hands.BindTable(visit.Drink,
                court.TableTop(place / RidgeKioskCourtPlan.PlacesPerTable).position, place, timeline);
            visit.Drinking.SipCompleted = visit.ConsumeSip;
        }

        private void CreateDrink(Visit visit, InventoryItemId kind)
        {
            visit.Container = VodkaBottleView.Create(transform, kind);
            visit.Drink = BarPatronDrinkProp.FromContainer(visit.Container);
        }

        private void BeginSale(Visit visit, int place, int day)
        {
            InventoryItemId kind = (place + day + visit.Orders) % 3 == 0 ? InventoryItemId.VodkaBottle : InventoryItemId.BeerCan;
            if (!TryPrice(visit, kind, out _) && !TryPrice(visit, kind = InventoryItemId.BeerCan, out _))
            {
                ReturnToHold(visit, 15f);
                return;
            }
            CreateDrink(visit, kind);
            visit.Container.SetOpened(false);
            visit.Container.transform.position = handover;
            Set(visit, Phase.Handover);
        }

        private bool TryPrice(Visit visit, InventoryItemId kind, out SupermarketPurchaseResult result)
        {
            foreach (RidgeKioskPurchase offer in station.Offers)
            {
                if (offer.ItemId != kind) continue;
                result = SupermarketPurchaseRules.Evaluate(offer.SourceId, kind, visit.Cash, 0, RetailStock.RidgeKiosk);
                return result.Succeeded;
            }
            result = default;
            return false;
        }

        private void LateUpdate()
        {
            if (!court || GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) return;
            foreach (Visit visit in visits)
            {
                if (!visit.Actor || !visit.Actor.IsSpawned) continue;
                if (visit.Phase == Phase.Throwing)
                {
                    PresentThrow(visit);
                }
                else if (visit.Phase == Phase.Handover && visit.Container)
                {
                    float reach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, HandoverPickupSeconds, visit.Elapsed));
                    float carry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HandoverPickupSeconds, HandoverSeconds, visit.Elapsed));
                    visit.Hands.Receive(visit.Drink, handover, reach, carry, visit.Elapsed >= HandoverPickupSeconds);
                    station.Vendor.SetHandover(handover + Vector3.up * .10f,
                        Mathf.Sin(Mathf.Clamp01(visit.Elapsed / HandoverSeconds) * Mathf.PI));
                    if (visit.Elapsed < HandoverSeconds) continue;
                    if (station.OwnsNpcWindow(visit.Head) && TryPrice(visit, visit.Container.ItemId, out var sale))
                    {
                        visit.Cash = sale.CashAfter;
                        visit.Sips = visit.Container.Profile.SipCount;
                        if (visit.Orders > 0) repeatPurchases++;
                        visit.Orders++;
                        completedPurchases++;
                        station.ReleaseNpc(visit.Head);
                        Set(visit, Phase.ToTable);
                        Go(visit.Actor, visit.TableNode);
                    }
                    else
                        ReturnToHold(visit, 2f);
                    station.Vendor.SetHandover(null, 0f);
                }
                else if (visit.Container && !visit.Drinking) visit.Hands.Carry(visit.Drink);
            }
        }

        private void PresentThrow(Visit visit)
        {
            // Sample the actual windup and release frames even when a large
            // frame step crosses both markers. Walking ticks cannot skip them.
            float progress = Mathf.Clamp01(visit.ThrowProgress + Time.deltaTime / PlayerVodkaActions.ThrowSeconds);
            if (visit.ThrowProgress < PlayerVodkaActions.ThrowWindupProgress)
                progress = Mathf.Min(progress, PlayerVodkaActions.ThrowWindupProgress);
            else if (visit.Container)
                progress = Mathf.Min(progress, PlayerVodkaActions.ThrowReleaseProgress);
            visit.ThrowProgress = progress;
            visit.Hands.ThrowPose(court.HeroActions, throwClip, progress, visit.Drink);

            if (visit.Container && progress >= PlayerVodkaActions.ThrowReleaseProgress)
            {
                Transform actor = visit.Actor.transform;
                var (velocity, spin) = PlayerVodkaActions.ContainerThrowMotion(actor.right, actor.forward);
                if (ThrownVodkaBottle.ReleaseAmbient(visit.Container, velocity, spin, actor, heroBody, drops))
                {
                    visit.Container = null;
                    visit.Drink = null;
                    visit.Hands.ReleaseGrip();
                    releasedEmpties++;
                }
            }

            if (progress < 1f) return;
            visit.Hands.EndThrow();
            throwing = null;
            bool takeOrders = GameSessionState.GameTimeOfDayMinutes < CityResidentLifeRules.KioskLastOrderMinute;
            if (takeOrders && aisleTrip == visit && station.OwnsNpcWindow(visit.Head) && !station.HeroHasWindow)
            {
                WalkToWindow(visit);
                return;
            }
            ReleasePurchaseReservation(visit);
            if (takeOrders) RequestDrink(visit);
            else Set(visit, Phase.AtTable);
        }

        private bool TryFindThrowDirection(Visit visit, int place, out Vector3 facing)
        {
            Vector3 outward = RidgeKioskCourtPlan.PlaceDirection(place);
            for (int turn = 0; turn < 3; turn++)
            {
                float yaw = turn == 0 ? 0f : turn == 1 ? 50f : -50f;
                Vector3 side = Quaternion.AngleAxis(yaw, Vector3.up) * outward;
                facing = Vector3.Cross(side, Vector3.up);
                Vector3 velocity = PlayerVodkaActions.ContainerThrowMotion(side, facing).Velocity;
                Vector3 direction = Vector3.ProjectOnPlane(velocity, Vector3.up).normalized;
                if (Blocks(hero.position, direction)) continue;
                bool clear = true;
                foreach (Visit other in visits)
                    if (other != visit && other.Actor && Blocks(other.Actor.Position, direction)) { clear = false; break; }
                if (clear) return true;
            }
            facing = default;
            return false;

            bool Blocks(Vector3 point, Vector3 direction)
            {
                Vector3 delta = point - visit.Actor.Position;
                if (Mathf.Abs(delta.y) > 1.6f) return false;
                delta.y = 0f;
                float distance = Vector3.Dot(delta, direction);
                return distance > 0f && distance < 4.2f && (delta - direction * distance).sqrMagnitude < .65f * .65f;
            }
        }

        private bool IsFirstInLine(Visit visitor)
        {
            for (int place = 0; place < visits.Length; place++)
            {
                Visit other = visits[place];
                if (other == visitor || !other.Actor) continue;
                // Let first-time arrivals finish entering their pockets before
                // sending somebody back up the same aisle toward the window.
                if (other.Phase == Phase.NeedDrink && other.Orders == 0 && other.Actor.ResidentDestinationNode == other.HoldNode &&
                    !At(other.Actor, other.HoldNode)) return false;
                if (other.Request == 0 || other.Request >= visitor.Request) continue;
                if (other.Phase == Phase.NeedDrink && ReadyForWindow(other) ||
                    ReadyToFinish(other) && TryFindThrowDirection(other, place, out _)) return false;
            }
            return true;
        }

        private static bool ReadyForWindow(Visit visit) => visit.Elapsed >= 0f &&
            (At(visit.Actor, visit.HoldNode) || visit.Orders == 0 && visit.Actor.ResidentDestinationNode != visit.HoldNode);

        private bool TryReservePurchase(Visit visit)
        {
            if (aisleTrip != null || !IsFirstInLine(visit) || !station.TryClaimNpc(visit.Head)) return false;
            aisleTrip = visit;
            return true;
        }

        private void WalkToWindow(Visit visit)
        {
            Set(visit, Phase.ToWindow);
            Go(visit.Actor, residents.KioskWindowNode);
        }

        private void ReleasePurchaseReservation(Visit visit)
        {
            station.ReleaseNpc(visit.Head);
            FinishAisleTrip(visit);
        }

        private void ReturnToHold(Visit visit, float retryDelay)
        {
            RemoveContainer(visit);
            station.ReleaseNpc(visit.Head);
            Set(visit, Phase.ReturningToHold);
            visit.RetryDelay = retryDelay;
            Go(visit.Actor, visit.HoldNode);
        }

        private void FinishAisleTrip(Visit visit)
        {
            if (aisleTrip == visit) aisleTrip = null;
        }

        private void RequestDrink(Visit visit, float delay = 0f)
        {
            visit.Request = ++nextRequest;
            Set(visit, Phase.NeedDrink);
            visit.Elapsed = -delay;
        }

        private static void Set(Visit visit, Phase phase) { visit.Phase = phase; visit.Elapsed = 0f; }
        private static bool At(CityPedestrianActor actor, int node) => actor.IsAtResidentDestination && actor.ResidentDestinationNode == node;
        private void Go(CityPedestrianActor actor, int node)
        {
            if (actor.ResidentDestinationNode != node) actor.SetResidentDestination(node, residents.DistancesTo(node));
        }

        private bool FaceTable(CityPedestrianActor actor, int place, float step) => Face(actor,
            court.Plan.Table(place / RidgeKioskCourtPlan.PlacesPerTable) - actor.Position, step);

        private static bool Face(CityPedestrianActor actor, Vector3 direction, float step)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f) return true;
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            actor.transform.rotation = Quaternion.RotateTowards(actor.transform.rotation, target, 330f * step);
            return Quaternion.Angle(actor.transform.rotation, target) < 4f;
        }
        private void Leave(Visit visit)
        {
            EndSpeechFor(visit);
            StopDrinking(visit);
            if (visit.Phase == Phase.Handover) RemoveContainer(visit);
            visit.Hands.EndThrow();
            if (throwing == visit) throwing = null;
            if (station.OwnsNpcWindow(visit.Head)) station.Vendor.SetHandover(null, 0f);
            station.ReleaseNpc(visit.Head);
            Set(visit, Phase.Leaving);
        }

        internal void ReleaseResident(int id, CityPedestrianActor actor)
        {
            int place = residents.KioskPlace(id);
            if (place >= 0 && visits[place].Actor == actor) Release(visits[place]);
        }

        private void Release(Visit visit)
        {
            if (!visit.Actor) return;
            FinishAisleTrip(visit);
            EndSpeechFor(visit);
            StopDrinking(visit);
            if (throwing == visit) throwing = null;
            if (station.OwnsNpcWindow(visit.Head)) station.Vendor.SetHandover(null, 0f);
            station.ReleaseNpc(visit.Head);
            visit.Hands.EndThrow();
            visit.Hands.ReleaseGrip();
            RemoveContainer(visit);
            visit.Head = null;
            visit.Actor = null;
            visit.Hands = null;
            Set(visit, Phase.Unbound);
        }

        private static void StopDrinking(Visit visit)
        {
            visit.Timeline = null;
            if (!visit.Drinking) return;
            visit.Drinking.SipCompleted = null;
            visit.Drinking.enabled = false;
            Destroy(visit.Drinking);
            visit.Drinking = null;
        }

        private static void RemoveContainer(Visit visit)
        {
            if (visit.Container)
            {
                visit.Hands?.ReleaseGrip();
                visit.Hands?.RestoreContactCulling();
                visit.Container.gameObject.SetActive(false);
                Destroy(visit.Container.gameObject);
            }
            visit.Container = null;
            visit.Drink = null;
        }

        private void OnDisable()
        {
            if (!court) return;
            CancelExchange();
            foreach (Visit visit in visits) Release(visit);
            foreach (var drop in drops.GetComponentsInChildren<ThrownVodkaBottle>(true)) Destroy(drop.gameObject);
        }
    }
}
