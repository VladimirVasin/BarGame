using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_RidgeKioskRearCourtTradesAndEmptiesByEleven()
        {
            const int evening = 21 * 60 + 10;
            Assert.That(GameSessionState.TryStartGameTimeAt(evening), Is.True);
            yield return LoadProductionCity();
            Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var site), Is.True);
            RidgeKioskCourtPlan court = site.Court;
            Assert.That(court, Is.Not.Null, "The Ridge shop needs its reserved rear court.");
            Assert.That(court.Slab.yMax, Is.LessThanOrEqualTo(site.Bounds.yMin));
            Assert.That(court.PhoneBounds.yMax, Is.LessThan(court.Slab.yMax));
            bool survey = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_REAR_COURT_SURVEY") == "1";
            bool review = survey || System.Environment.GetEnvironmentVariable("BAR_PROMENADE_REAR_COURT_CAPTURE") == "1";
#if UNITY_EDITOR
            if (survey)
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            var live = city.World.Root.GetComponentInChildren<RidgeKioskCourtLife>();
            Assert.That(live, Is.Not.Null);
            RidgeKioskStation station = live.GetComponentInParent<RidgeKioskStation>();
            var dock = CityHillsideFurniturePlan.ForLayout(city.Layout).UtilityDocks.Single(item =>
                item.Kind == CityStreetUtilityKind.PhoneBooth && court.Slab.Contains(new Vector2(item.StandPosition.x, item.StandPosition.z)));
            Assert.That(city.World.WalkableArea.Contains(dock.StandPosition, CityGroundTraversalPlanner.MaximumAgentRadius), Is.True);
            for (float z = RidgeKioskCourtPlan.SlabFront - .55f; z <= CityKioskSitePlan.ApronFront + .60f; z += .10f)
                Assert.That(city.World.WalkableArea.Contains(court.Point(court.SideAisle, z), CityGroundTraversalPlanner.MaximumAgentRadius),
                    Is.True, "The side path must join the slab and the sidewalk at " + z);
            var ground = new CityMapCityTeleportGround(city.Layout);
            for (int place = 0; place < RidgeKioskCourtPlan.Capacity; place++)
            {
                Vector3 foot = court.Place(place);
                Assert.That(city.World.WalkableArea.Contains(foot, CityGroundTraversalPlanner.MaximumAgentRadius), Is.True, "standing place " + place);
                Assert.That(ground.TryResolveStandingPosition(new Vector2(foot.x, foot.z), out Vector3 arrival), Is.True);
                Assert.That(arrival.y, Is.EqualTo(foot.y + PlayerFactory.GroundedRootOffset).Within(.01f));
            }
            int[] regulars = Enumerable.Range(0, CityResidentLifeRules.ResidentCount)
                .Where(id => city.Pedestrians.Residents.KioskPlace(id) >= 0).ToArray();
            Assert.That(regulars, Has.Length.EqualTo(6));
            Assert.That(regulars.GroupBy(id => city.Pedestrians.Residents.KioskPlace(id) / RidgeKioskCourtPlan.PlacesPerTable)
                .Select(table => table.Count()), Is.EquivalentTo(new[] { 2, 2, 2 }));
            Assert.That(regulars.All(id => !DefaultNpcPopulation.IsWomanPedestrian(id)), Is.True);
            Assert.That(regulars.All(id => city.Pedestrians.Residents.Schedule(id, 0, CityResidentLifeRules.KioskCourtEmptyMinute).Activity != CityResidentActivity.Leisure), Is.True);
            VerifyCourtGraph();
            VerifyCourtPavingAndEdges();
            int startupPlaces = live.OccupiedPlaces;
            var startupDrinks = city.Pedestrians.GetComponentsInChildren<VodkaBottleView>();
            string startupState = $"Minute {GameSessionState.GameTimeOfDayMinutes:F2}, hero {city.Player.GameObject.transform.position}, " +
                $"street bodies {city.Pedestrians.ActiveCount}. " + string.Join(" | ", regulars.Select(id =>
                {
                    var state = city.ResidentJourneys.GetState(id);
                    return $"{id} {state.Activity} {state.Phase}";
                }));
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(evening).Within(1d),
                "Synchronous City composition must not advance the calendar.");
            Assert.That(startupPlaces, Is.EqualTo(RidgeKioskCourtPlan.Capacity), startupState);
            Assert.That(startupPlaces, Is.EqualTo(regulars.Select(city.ResidentJourneys.GetState).Count(state =>
                state.Activity == CityResidentActivity.Leisure && state.Phase == CityResidentJourneyPhase.AtDestination)));
            Assert.That(startupDrinks, Has.Length.EqualTo(startupPlaces));
            Assert.That(startupDrinks.All(drink => drink.IsOpened && drink.Remaining > 0f), Is.True);
            Assert.That(startupDrinks.Select(drink => drink.ItemId).Distinct(), Is.EquivalentTo(new[] { InventoryItemId.BeerCan, InventoryItemId.VodkaBottle }));
            Assert.That(startupDrinks.Any(drink => drink.Remaining < 1f), Is.True);
            var startupPoses = city.Pedestrians.GetComponentsInChildren<BarPatronDrinkingArmPose>();
            Assert.That(startupPoses, Has.Length.EqualTo(startupPlaces));
            Assert.That(startupPoses.Any(pose => !pose.CanConverse), Is.True,
                "The first visible frame must already include a drinking action.");
            foreach (var pose in startupPoses)
            {
                Assert.That(pose.BottleGripError, Is.LessThan(.01f), "The actual vessel grip must be inside the curled hand.");
                Assert.That(pose.HandPose.RightGripWeight, Is.GreaterThan(.98f));
                Assert.That(pose.HandPose.Hands.Single(hand => !hand.IsLeft).Renderers.All(renderer =>
                    CharacterJointDeformation.FindShape(renderer.sharedMesh, "BottleGrip") >= 0), Is.True,
                    "Every right-hand part uses the hero's authored bottle shape.");
            }
            Assert.That(startupPoses.Any(pose => pose.GripKind == NpcHandGripKind.Bottle), Is.True);
            VodkaBottleView beer = startupDrinks.First(drink => drink.ItemId == InventoryItemId.BeerCan &&
                HoldingActor(drink)?.Presentation.GetComponent<BarPatronDrinkingArmPose>()?.CanConverse == true);
            var beerHolder = HoldingActor(beer);
            var beerPose = beerHolder.Presentation.GetComponent<BarPatronDrinkingArmPose>();
            Assert.That(beerPose.GripKind, Is.EqualTo(beer.Profile.GripKind));
            var heroHands = city.Player.GameObject.GetComponentInChildren<NpcHandPose>(true);
            var heroCanFrame = heroHands.GetAnatomicalFrame(false, beer.Profile.GripKind);
            var rightHand = beerPose.HandPose.Hands.Single(hand => !hand.IsLeft);
            Assert.That(Vector3.Dot(rightHand.Hand.rotation * heroCanFrame.ThumbDirectionInHand, beer.transform.up),
                Is.GreaterThan(.98f), "The thumb on the hero mesh must point toward the lid.");
            Assert.That(Vector3.Dot(rightHand.Hand.rotation * heroCanFrame.PalmNormalInHand, -beer.transform.forward),
                Is.GreaterThan(.98f), "The palm on the hero mesh must face the can.");
            Transform shoulder = CityPedestrianHandProps.FindSocket(beerHolder.Presentation.transform, BarPatronWorldBuilder.RightUpperArmBoneName);
            Transform elbow = CityPedestrianHandProps.FindSocket(beerHolder.Presentation.transform, BarPatronWorldBuilder.RightForearmBoneName);
            Transform wrist = CityPedestrianHandProps.FindSocket(beerHolder.Presentation.transform, BarPatronWorldBuilder.RightHandBoneName);
            Assert.That(Vector3.Angle(shoulder.position - elbow.position, wrist.position - elbow.position), Is.LessThan(155f),
                "The drinker must have a bent elbow at rest.");
            Assert.That(live.CompletedPurchases, Is.Zero, "Existing customers already own their drinks at startup.");
            Debug.Log("Ridge kiosk startup filled " + startupPlaces + " places with drinks and staggered phases.");

            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var previousInterval = UnityEngine.Rendering.OnDemandRendering.renderFrameInterval;
            bool previousFocus = GameCursor.ApplicationFocused;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 120;
            Vector3 listener = court.Point(-3.7f, -2.0f) + Vector3.up * PlayerFactory.GroundedRootOffset;
            city.Player.Motor.Teleport(listener);
            const int pedestrianTicksPerFrame = 8;
            const float pedestrianStep = .15f;
            try
            {
                GameCursor.SetApplicationFocused(true);
                city.Pedestrians.AutomaticUpdatesSuspended = true;
                Capture("00-enclosed-court-evening", court.Point(7.5f, -13.0f) + Vector3.up * 5.3f,
                    court.Point(0f, -5.5f) + Vector3.up * .5f);
                Capture("01-startup-drinkers", court.Point(4.6f, -9.9f) + Vector3.up * 2.2f, court.Point(-.4f, -4.7f) + Vector3.up * 1.2f);
                Transform subject = beerHolder.transform;
                Capture("02-beer-grip", beerHolder.Position + subject.right * 1.05f + subject.forward * .88f + Vector3.up * 1.48f,
                    beer.Grip.position + Vector3.up * .10f);

                var barLines = new HashSet<string>(Enumerable.Range(0, BarConversationCatalog.CounterCount).SelectMany(index =>
                {
                    var pair = BarConversationCatalog.Get(BarConversationKind.Counter, index);
                    return new[] { LocalizationService.Get(pair.FirstKey), LocalizationService.Get(pair.SecondKey) };
                }));
                var bubbles = live.GetComponent<NpcSpeechBubbleView>();
                bool spokeBarLine = false;
                for (int frame = 0; frame < 48 && !spokeBarLine; frame++)
                {
                    city.Pedestrians.Advance(.10f);
                    foreach (var actor in city.Pedestrians.Actors)
                        if (actor.IsSpawned && bubbles.TryGetSpeechFaceSample(actor.Presentation, out var line) && barLines.Contains(line.Text))
                            spokeBarLine = true;
                    yield return null;
                }
                Assert.That(spokeBarLine, Is.True, "A court visitor must present a line from the bar's shared conversation bank.");

                VodkaBottleView woundUp = null;
                ThrownVodkaBottle flight = null;
                Vector3 initialGrip = default, windupGrip = default, launch = default;
                var throwStops = new[]
                {
                    (.15f, "04a-throw-entry"),
                    (PlayerVodkaActions.ThrowWindupProgress, "04b-throw-windup"),
                    (PlayerVodkaActions.ThrowReleaseProgress - .06f, "04c-throw-before-release")
                };
                int throwStage = 0;
                bool flew = false;
                var courtBodies = city.Pedestrians.Actors.Where(actor => actor.IsSpawned && live.IsCourtFloor(actor.Position)).ToArray();
                Assert.That(courtBodies, Has.Length.EqualTo(RidgeKioskCourtPlan.Capacity));
                var lastRestAt = new Dictionary<CityPedestrianActor, float>();
                var repeatRoundTrips = new HashSet<CityPedestrianActor>();
                Transform windowBlock = city.Player.GameObject.transform;
                Assert.That(station.TryClaimNpc(windowBlock), Is.True);
                bool queuedTwo = false, returnFrame = false;
                CityPedestrianActor lastThrower = null, shopper = null;
                int purchasesAtStart = 0;
                float elapsed = 0f, closestPeer = float.PositiveInfinity, closestTable = float.PositiveInfinity;
                for (int frame = 0; frame < 600 && (!flew || repeatRoundTrips.Count < courtBodies.Length); frame++)
                {
                    for (int tick = 0; tick < pedestrianTicksPerFrame; tick++)
                    {
                        city.Pedestrians.Advance(pedestrianStep);
                        elapsed += pedestrianStep;
                        ObserveRepeatRoutes();
                    }
                    yield return null;
                    foreach (var visitor in courtBodies)
                    {
                        var pose = visitor.Presentation.GetComponent<BarPatronDrinkingArmPose>();
                        if (!pose || !pose.CanConverse) continue;
                        var drink = visitor.Presentation.GetComponentInChildren<VodkaBottleView>();
                        if (drink && HasLastSip(drink)) lastRestAt.TryAdd(visitor, elapsed);
                    }
                    CityPedestrianActor throwingActor = live.HeldThrow.Actor;
                    if (lastThrower && throwingActor != lastThrower)
                    {
                        Assert.That(live.AisleTripActor, Is.SameAs(lastThrower));
                        Assert.That(lastThrower.ResidentDestinationNode, Is.EqualTo(city.Pedestrians.Residents.KioskWindowNode),
                            "Finishing the throw must start the walk to the window immediately.");
                    }
                    lastThrower = throwingActor;
                    if (!queuedTwo)
                    {
                        Assert.That(live.ReleasedEmpties, Is.Zero, "A blocked window must leave the last sip in the guest's hand.");
                        var waiting = lastRestAt.Where(entry => elapsed - entry.Value >=
                            RidgeKioskCourtLife.MaximumRefillWaitSeconds + BarPatronDrinkTimeline.MaximumRestSeconds).Take(2).ToArray();
                        if (waiting.Length == 2)
                        {
                            foreach (var entry in waiting)
                            {
                                var drink = entry.Key.Presentation.GetComponentInChildren<VodkaBottleView>();
                                Assert.That(drink && HasLastSip(drink), Is.True);
                            }
                            var guest = waiting[0].Key;
                            Capture("04-last-sip-wait", guest.Position + guest.transform.forward * 1.45f +
                                guest.transform.right * .75f + Vector3.up * 1.55f, guest.Position + Vector3.up * 1.25f);
                            queuedTwo = true;
                            station.ReleaseNpc(windowBlock);
                        }
                    }
                    if (!returnFrame && shopper && live.RepeatPurchases > purchasesAtStart &&
                        live.IsCourtFloor(shopper.Position) && shopper.MotionState == CityPedestrianMotionState.Walking)
                    {
                        returnFrame = true;
                        Capture("05a-refill-return", court.Point(1.5f, -9.9f) + Vector3.up * 2.2f,
                            shopper.Position + Vector3.up);
                    }
                    if (flew) continue;
                    if (flight)
                    {
                        flew = Vector3.Distance(flight.transform.position, launch) > .10f;
                        continue;
                    }
                    var (actor, held, progress) = live.HeldThrow;
                    if (!woundUp)
                    {
                        if (!held) continue;
                        Assert.That(held.Remaining, Is.Zero);
                        woundUp = held;
                        initialGrip = held.Grip.position;
                    }
                    if (held == woundUp)
                    {
                        Assert.That(Vector3.Dot(held.transform.up, Vector3.up), Is.GreaterThan(.98f),
                            "The empty stays upright in the hand during the hero's throwing motion.");
                        if (throwStage >= throwStops.Length || progress < throwStops[throwStage].Item1) continue;
                        AssertVisibleGrip(actor, held);
                        if (throwStage == 1)
                        {
                            windupGrip = held.Grip.position;
                            Assert.That(Vector3.Distance(windupGrip, initialGrip), Is.GreaterThan(.15f),
                                "The held empty must visibly move into the windup.");
                        }
                        else if (throwStage == 2)
                            Assert.That(Vector3.Dot(held.Grip.position - windupGrip, actor.transform.right), Is.GreaterThan(.12f),
                                "The held empty must sweep toward the release direction.");
                        float side = throwStage == 0 ? .55f : .95f;
                        Capture(throwStops[throwStage++].Item2,
                            actor.Position + actor.transform.forward * 1.35f + actor.transform.right * side + Vector3.up * 1.62f,
                            Vector3.Lerp(actor.Position + Vector3.up * 1.27f, held.Grip.position, .35f));
                    }
                    else if ((flight = woundUp.GetComponent<ThrownVodkaBottle>()))
                    {
                        Assert.That(flight.transform.IsChildOf(live.transform), Is.True, "Release must transfer the same prop to the court.");
                        Assert.That(flight.Body.isKinematic, Is.False);
                        Assert.That(flight.Body.linearVelocity.magnitude, Is.GreaterThan(4f), "The same held empty must launch with real velocity.");
                        launch = flight.transform.position;
                    }
                }
                string tradeState = TradeState();
                Assert.That(throwStage, Is.EqualTo(throwStops.Length), "The same empty must stay in the hand from entry to release.");
                Assert.That(flew, Is.True, "The empty must travel after its release from the visible windup. " + tradeState);
                Assert.That(live.CompletedPurchases, Is.GreaterThan(0), tradeState);
                Assert.That(live.ReleasedEmpties, Is.GreaterThan(0), tradeState);
                Assert.That(queuedTwo && returnFrame, Is.True, "Two visitors must wait for the same window and visibly return with drinks.");
                Assert.That(repeatRoundTrips, Has.Count.EqualTo(courtBodies.Length), tradeState);
                Assert.That(closestPeer, Is.GreaterThan(.66f), "Visitors must not overlap during a refill. " + tradeState);
                Assert.That(closestTable, Is.GreaterThan(RidgeKioskCourtPlan.TableRadius + city.Pedestrians.Plan.AgentRadius - .025f),
                    "A walking capsule must clear the tabletop rim. " + tradeState);
                Assert.That(live.OccupiedPlaces, Is.InRange(RidgeKioskCourtPlan.Capacity - 1, RidgeKioskCourtPlan.Capacity));
                Debug.Log("Ridge kiosk repeat round trips verified. " + tradeState);
                Assert.That(ThrownVodkaBottle.AmbientActiveCount, Is.LessThanOrEqualTo(ThrownVodkaBottle.MaximumAmbientContainers));
                Capture("05-evening-drinkers", court.Point(4.6f, -9.9f) + Vector3.up * 2.2f, court.Point(-.4f, -4.7f) + Vector3.up * 1.2f);
                while (GameSessionState.GameTimeOfDayMinutes < CityResidentLifeRules.KioskCourtEmptyMinute)
                {
                    AdvanceWalkingAndCalendar();
                    yield return null;
                }
                Assert.That(live.OccupiedPlaces, Is.Zero);
                Assert.That(city.Pedestrians.Actors.All(actor => !actor.IsSpawned || !live.IsCourtFloor(actor.Position)), Is.True,
                    "All visitors must have walked off the rear slab by 23:00. " + TradeState());
                for (int frame = 0; frame < 530 && !station.Vendor.IsSmoking; frame++)
                {
                    GameSessionState.AdvanceGameTime(.40f);
                    yield return null;
                }
                Assert.That(station.Vendor.IsSmoking, Is.True, "The vendor must use his door and reach the smoking point.");
                Capture("06-seller-smoking", court.Point(-3.4f, -4.3f) + Vector3.up * 1.65f, court.SmokePoint + Vector3.up * 1.25f);
                city.Player.Motor.Teleport(site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                follow.SetFixedPose(site.Approach + new Vector3(-.6f, 1.6f, 2.5f),
                    Quaternion.LookRotation(station.InteractionPosition - (site.Approach + new Vector3(-.6f, 1.6f, 2.5f))), 55f);
                Physics.SyncTransforms();
                yield return null;
                station.Interact(city.Player.Interactor);
                for (int frame = 0; frame < 385 && !station.Menu.IsOpen; frame++) yield return null;
                Assert.That(station.Vendor.AtCounter && station.Menu.IsOpen, Is.True, "A buyer at the window ends the break and opens the existing shop.");
                station.Menu.Close();

                city.Player.Motor.Teleport(listener);
                AdvanceCalendarTo(15 * 60 + 59);
                yield return null;
                yield return null;
                Assert.That(live.OccupiedPlaces, Is.Zero);
                Vector3 observer = court.Point(4.9f, -1.7f) + Vector3.up * 2.1f;
                follow.SetFixedPose(observer, Quaternion.LookRotation(court.Point(-.4f, -6f) + Vector3.up - observer), 62f);
                var firstPositions = new Dictionary<int, Vector3>();
                bool walkedInWithDrink = false;
                while (!walkedInWithDrink && GameSessionState.GameTimeOfDayMinutes < 20 * 60)
                {
                    AdvanceWalkingAndCalendar(advanceServices: true);
                    yield return null;
                    foreach (CityPedestrianActor actor in city.Pedestrians.Actors)
                    {
                        if (!actor.IsSpawned) continue;
                        int id = city.Pedestrians.ResidentIndex(actor);
                        if (id < 0 || city.Pedestrians.Residents.KioskPlace(id) < 0) continue;
                        if (city.ResidentJourneys.GetState(id).Activity != CityResidentActivity.Leisure) continue;
                        bool onSlab = live.IsCourtFloor(actor.Position);
                        if (!firstPositions.TryGetValue(id, out Vector3 first))
                        {
                            Assert.That(onSlab, Is.False, "A new arrival must first be seen on the street.");
                            firstPositions.Add(id, actor.Position);
                        }
                        else if (onSlab && actor.MotionState == CityPedestrianMotionState.Walking &&
                            actor.ResidentDestinationNode == city.Pedestrians.Residents.Destination(id, CityResidentActivity.Leisure))
                        {
                            Assert.That(Vector3.Distance(actor.Position, first), Is.GreaterThan(3f));
                            var drink = actor.Presentation.GetComponentInChildren<VodkaBottleView>();
                            Assert.That(drink, Is.Not.Null, "The purchased container must be in the customer's hand before entering the slab. " +
                                $"Resident {id}. " + TradeState());
                            Assert.That(drink.Remaining, Is.EqualTo(1f));
                            AssertVisibleGrip(actor, drink);
                            walkedInWithDrink = true;
                            Capture("03-carry-to-table", actor.Position + actor.transform.right * .96f + actor.transform.forward * 1.1f + Vector3.up * 1.60f,
                                drink.Grip.position + Vector3.up * .12f);
                            break;
                        }
                    }
                }
                Assert.That(walkedInWithDrink, Is.True, "An arriving resident must walk from the street and carry the purchase to the court. " + TradeState());
                Debug.Log("Ridge kiosk walk-in reached the court with the purchased container in hand.");
                if (review)
                {
                    AdvanceCalendarTo(12 * 60);
                    Capture("07-paved-street-entrance", court.Point(6f, 5.7f) + Vector3.up * 2.4f,
                        court.Point(2.3f, -.6f) + Vector3.up * .5f);
                    Capture("08-enclosed-court-day", court.Point(7.5f, -13.0f) + Vector3.up * 5.3f,
                        court.Point(0f, -5.5f) + Vector3.up * .5f);
                }

                void ObserveRepeatRoutes()
                {
                    CityPedestrianActor current = live.AisleTripActor;
                    if (current != shopper)
                    {
                        if (shopper && live.RepeatPurchases > purchasesAtStart)
                        {
                            Assert.That(shopper.IsAtResidentDestination && XzDistance(shopper.Position, Spot(shopper)) < .07f, Is.True,
                                "The next trip must wait until this buyer reaches his own standing place.");
                            repeatRoundTrips.Add(shopper);
                            lastRestAt.Remove(shopper);
                        }
                        if (current)
                        {
                            Assert.That(XzDistance(current.Position, Spot(current)), Is.LessThan(.22f));
                            var drink = current.Presentation.GetComponentInChildren<VodkaBottleView>();
                            Assert.That(drink && HasLastSip(drink), Is.True, "The refill must reserve the aisle before the guest drinks his last sip.");
                            Assert.That(lastRestAt.ContainsKey(current), Is.True);
                            Assert.That(elapsed - lastRestAt[current] + pedestrianStep * pedestrianTicksPerFrame,
                                Is.GreaterThanOrEqualTo(RidgeKioskCourtLife.MinimumRefillWaitSeconds));
                        }
                        shopper = current;
                        purchasesAtStart = live.RepeatPurchases;
                    }
                    for (int i = 0; i < courtBodies.Length; i++)
                    {
                        var actor = courtBodies[i];
                        if (actor != current)
                            Assert.That(XzDistance(actor.Position, Spot(actor)), Is.LessThan(.07f),
                                "Other regulars must wait at their own standing places.");
                        for (int j = 0; j < i; j++)
                            closestPeer = Mathf.Min(closestPeer, XzDistance(actor.Position, courtBodies[j].Position));
                        if (!live.IsCourtFloor(actor.Position)) continue;
                        for (int table = 0; table < RidgeKioskCourtPlan.TableCount; table++)
                            closestTable = Mathf.Min(closestTable, XzDistance(actor.Position, court.Table(table)));
                    }
                }

                Vector3 Spot(CityPedestrianActor actor) =>
                    court.Place(city.Pedestrians.Residents.KioskPlace(city.Pedestrians.ResidentIndex(actor)));

                static bool HasLastSip(VodkaBottleView drink) =>
                    Mathf.Abs(drink.Remaining - 1f / drink.Profile.SipCount) < .001f;
            }
            finally
            {
                GameCursor.SetApplicationFocused(previousFocus);
                city.Pedestrians.AutomaticUpdatesSuspended = false;
                follow.ClearFixedPose();
                UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = previousInterval;
            }

            void AdvanceCalendarTo(int minute) => GameSessionState.AdvanceGameTime(
                (float)(((1440 + minute - GameSessionState.GameTimeOfDayMinutes) % 1440d) / GameTimeState.GameMinutesPerRealSecond));

            void VerifyCourtGraph()
            {
                var plan = city.Pedestrians.Plan;
                var nav = CityPedestrianPlanner.CreateWalkableArea(plan);
                var stops = Enumerable.Range(0, RidgeKioskCourtPlan.Capacity).SelectMany(slot => new[] { court.Place(slot), court.Wait(slot) }).ToArray();
                var links = plan.Links.Where(link => link.Id.StartsWith(RidgeKioskCourtPlan.NodePrefix, System.StringComparison.Ordinal)).ToArray();
                Assert.That(links, Is.Not.Empty);
                foreach (var link in links)
                {
                    Vector3 a = plan.Nodes[link.FirstNodeIndex].Position, b = plan.Nodes[link.SecondNodeIndex].Position;
                    float length = XzDistance(a, b);
                    int samples = Mathf.CeilToInt(length / .05f);
                    for (int i = 0; i <= samples; i++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, i / (float)samples);
                        Assert.That(nav.Contains(p, plan.AgentRadius), Is.True, $"{link.Id} is blocked at {p - court.Origin}");
                        foreach (Vector3 stop in stops)
                        {
                            if (XzDistance(stop, a) < .04f || XzDistance(stop, b) < .04f) continue;
                            Assert.That(XzDistance(p, stop), Is.GreaterThan(plan.AgentRadius * 2f + .025f),
                                $"{link.Id} passes too close to a reserved stop at {stop - court.Origin}");
                        }
                    }
                }
            }

            void VerifyCourtPavingAndEdges()
            {
                Physics.SyncTransforms();
                var grass = city.World.Root.GetComponentInChildren<CityGrassField>().Surface;
                foreach (float x in new[] { court.SideAisle, -1.95f })
                {
                    float front = x == court.SideAisle ? CityKioskSitePlan.ApronFront - .02f : .70f;
                    for (float z = RidgeKioskCourtPlan.SlabFront + .05f; z <= front; z += .08f)
                    {
                        Vector3 foot = court.Point(x, z);
                        var point = new Vector2(foot.x, foot.z);
                        foot.y = court.PathTop(point);
                        Assert.That(Physics.Raycast(foot + Vector3.up * .25f, Vector3.down, out RaycastHit hit,
                            .5f, ~0, QueryTriggerInteraction.Ignore), Is.True, "Paving needs a physical top at " + point);
                        Assert.That(hit.collider.name, Does.Contain("KioskCourtPath"), "The path must be laid over the earth.");
                        Assert.That(hit.point.y, Is.EqualTo(foot.y).Within(.02f), "Paving must follow the planned grade.");
                        Assert.That(hit.collider.GetComponent<FootstepGround>().Kind, Is.EqualTo(FootstepGroundKind.Concrete));
                        Assert.That(grass.TrySample(point, out _, out _, out _), Is.False, "No grass through the laid path.");
                    }
                }
                var nav = CityPedestrianPlanner.CreateWalkableArea(city.Pedestrians.Plan);
                foreach (Rect edge in court.EdgeBounds)
                {
                    Vector3 p = new(edge.center.x, court.Origin.y, edge.center.y);
                    Assert.That(nav.Contains(p, city.Pedestrians.Plan.AgentRadius), Is.False,
                        "Fence/planter footprints must exclude NPC paths.");
                    Assert.That(city.World.WalkableArea.Contains(p, CityGroundTraversalPlanner.MaximumAgentRadius), Is.False,
                        "The hero must respect the same fence and planter footprints.");
                }
                Debug.Log("Ridge kiosk paved approach: concrete contacts, grade, grass exclusion and fence clearance verified.");
            }

            static float XzDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

            CityPedestrianActor HoldingActor(VodkaBottleView drink)
            {
                foreach (var actor in city.Pedestrians.Actors)
                {
                    if (!actor.IsSpawned || !actor.Presentation) continue;
                    var hands = actor.Presentation.Registry.GetComponentInChildren<NpcHandPose>();
                    if (hands && hands.RightGripWeight > .98f &&
                        Vector3.Distance(hands.AnatomicalCylinderCentre(false), drink.Grip.position) < .01f) return actor;
                }
                return null;
            }

            void AssertVisibleGrip(CityPedestrianActor actor, VodkaBottleView drink)
            {
                var hands = actor.Presentation.Registry.GetComponentInChildren<NpcHandPose>();
                var right = hands.Hands.Single(hand => !hand.IsLeft);
                Assert.That(drink.transform.parent, Is.SameAs(right.Hand), "The held prop must follow the actual wrist.");
                var anatomy = hands.GetAnatomicalFrame(false, drink.Profile.GripKind);
                Assert.That(Vector3.Dot(right.Hand.rotation * anatomy.ThumbDirectionInHand, drink.transform.up), Is.GreaterThan(.98f),
                    "The thumb must point toward the opening of the held container.");
                var scratch = new Mesh();
                try
                {
                    float closest = float.PositiveInfinity;
                    foreach (var renderer in right.Renderers)
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        renderer.BakeMesh(scratch, true);
                        foreach (Vector3 point in scratch.vertices)
                            closest = Mathf.Min(closest, Vector3.Distance(renderer.transform.TransformPoint(point), drink.Grip.position));
                    }
                    Assert.That(closest, Is.LessThan(hands.GripRadius(drink.Profile.GripKind) + .02f),
                        "The visible fingers and palm must reach the vessel, including while the legs animate.");
                }
                finally
                {
                    Object.Destroy(scratch);
                }
            }

            void AdvanceWalkingAndCalendar(bool advanceServices = false)
            {
                GameSessionState.AdvanceGameTime(pedestrianTicksPerFrame * pedestrianStep - Time.captureDeltaTime);
                for (int tick = 0; tick < pedestrianTicksPerFrame; tick++)
                {
                    if (advanceServices)
                    {
                        float extraStep = pedestrianStep - (tick == 0 ? Time.captureDeltaTime : 0f);
                        foreach (var vehicle in city.BusVehicles) vehicle.Advance(extraStep);
                        foreach (var passengers in city.VehiclePassengers) passengers.Advance(extraStep);
                        station.Vendor.AdvanceCourtVendor(extraStep);
                    }
                    city.Pedestrians.Advance(pedestrianStep);
                }
            }

            string TradeState()
            {
                var people = city.Pedestrians;
                var active = people.Actors.Where(actor => actor.IsSpawned && people.ResidentIndex(actor) >= 0 &&
                    people.Residents.KioskPlace(people.ResidentIndex(actor)) >= 0);
                return $"Court occupancy {live.OccupiedPlaces}, purchases {live.CompletedPurchases}, " +
                    $"discarded {live.ReleasedEmpties}, repurchases {live.RepeatPurchases}, vendor at counter {station.Vendor.AtCounter}. " +
                    string.Join(" | ", active.Select(actor => $"{people.ResidentIndex(actor)} {actor.MotionState} " +
                        $"local {actor.Position - court.Origin} moving to {Node(actor.TargetNodeIndex)}, goal {Node(actor.ResidentDestinationNode)}"));

                string Node(int index) => index < 0 ? "none" : people.Plan.Nodes[index].Id;
            }

            void Capture(string shot, Vector3 eye, Vector3 target)
            {
                if (!review || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 62f);
                // As in bar service captures, manual Camera.Render must skin
                // the current sampled bones, rather than reuse the last frame.
                var skins = city.Pedestrians.GetComponentsInChildren<SkinnedMeshRenderer>();
                var previous = new bool[skins.Length];
                try
                {
                    for (int i = 0; i < skins.Length; i++)
                    {
                        previous[i] = skins[i].forceMatrixRecalculationPerRender;
                        skins[i].forceMatrixRecalculationPerRender = true;
                    }
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKioskRearCourt", shot, 1280, 720);
                }
                finally
                {
                    for (int i = 0; i < skins.Length; i++)
                        if (skins[i]) skins[i].forceMatrixRecalculationPerRender = previous[i];
                }
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_RidgeKioskTradesAndTalksOffTheSummitSidewalk()
        {
            yield return LoadProductionCity();
            Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var site), Is.True,
                "The default summit needs a site along the home and bus stop sidewalk.");
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_RIDGE_KIOSK") == "1";
#if UNITY_EDITOR
            if (capture)
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            Transform kiosk = city.World.Root.transform.Find("Ridge Kiosk");
            Assert.That(kiosk, Is.Not.Null);
            Assert.That(kiosk.position, Is.EqualTo(site.Origin));
            var vendor = kiosk.GetComponentInChildren<RidgeKioskVendor>();
            var station = kiosk.GetComponentInChildren<RidgeKioskStation>();
            Assert.That(station, Is.Not.Null);
            var products = kiosk.GetComponentsInChildren<RidgeKioskPurchase>();
            Assert.That(products.Select(p => p.ItemId), Is.EquivalentTo(new[] {
                InventoryItemId.BeerCan, InventoryItemId.VodkaBottle, InventoryItemId.PotatoPirozhok }));
            var modelRenderers = kiosk.GetComponentsInChildren<Renderer>(true);
            Assert.That(modelRenderers.Any(r => r.name == "RidgeKiosk_RidgeKioskSurface" &&
                r.bounds.size.x > 3f && r.bounds.size.y > 3f), Is.True, "Imported shell retains its authored metres.");
            Assert.That(Vector3.Distance(vendor.Actor.transform.position,
                site.Origin + new Vector3(0f, .08f, .45f)), Is.LessThan(.01f));

            var point = city.Map.PointsOfInterest.Single(p => p.StableId == "ridge-kiosk");
            Vector3 standing = site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset;
            var ground = new CityMapCityTeleportGround(city.Layout);
            Assert.That(ground.TryClampArrival(point.WorldPosition, out var safe), Is.True);
            Assert.That(Vector3.Distance(safe, standing), Is.LessThan(.005f));
            Assert.That(point.Footprint, Is.EqualTo(site.Bounds));
            Assert.That(city.World.WalkableArea.Contains(site.Origin, .30f), Is.False);

            capture &= SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var hero = city.Player.Interactor;
            var motor = city.Player.Motor;
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var receipt = WorldItemFoundScreen.For(hero);
            var angles = kiosk.GetComponentsInChildren<Transform>();
            var approachAnchor = angles.Single(t => t.name == "KioskApproach");
            Assert.That(Vector3.Distance(approachAnchor.position, site.Approach), Is.LessThan(.01f));
            // Direct City loading skips the apartment wake that normally starts time.
            if (!GameSessionState.IsGameTimeRunning) GameSessionState.TryStartGameTimeAt(12 * 60);
            double toNoon = (2160d - GameSessionState.GameTimeOfDayMinutes) % 1440d;
            GameSessionState.AdvanceGameTime((float)(toNoon / GameTimeState.GameMinutesPerRealSecond));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(720d).Within(.01d));
            city.DayNight.ApplyCurrentTime(true);

            try
            {
                Vector3 sidewalk = new Vector3(standing.x, standing.y, site.Apron.yMax + 1f);
                motor.Teleport(sidewalk + Vector3.left * 2.5f);
                Physics.SyncTransforms();
                yield return WalkTo(sidewalk);
                yield return WalkTo(standing);
                if (capture)
                {
                    Vector3 at = kiosk.position + new Vector3(-4.8f, 2f, 5.9f);
                    follow.SetFixedPose(at, Quaternion.LookRotation(kiosk.position + Vector3.up * 1.25f - at), 58f);
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKiosk", "01-home-and-window", 1280, 720);
                    // Looking back along the player's approach from the house, the
                    // serving hatch, goods and waiting strip share the same frame.
                    at = kiosk.position + new Vector3(2.55f, 1.68f, 4.45f);
                    follow.SetFixedPose(at, Quaternion.LookRotation(vendor.InteractionPosition - at), 55f);
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKiosk", "02-window-from-home", 1280, 720);
                }

                for (int index = 0; index < station.Offers.Count; index++)
                {
                    var product = station.Offers[index];
                    yield return OpenWindow();
                    var menu = station.Menu;
                    Assert.That(menu.IsOpen, Is.True);
                    Assert.That(menu.Select(index + 1), Is.True);
                    Assert.That(menu.SelectedItemId, Is.EqualTo(product.ItemId));
                    int cash = GameSessionState.CashBalance;
                    int count = GameSessionState.GetInventoryItemCount(product.ItemId);
                    menu.Exit();
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash), "Cancelling must not charge.");
                    Assert.That(GameSessionState.GetInventoryItemCount(product.ItemId), Is.EqualTo(count));
                    yield return OpenWindow();
                    Assert.That(menu.Select(index + 1), Is.True);
                    Assert.That(menu.ConfirmSelection(), Is.True);
                    Assert.That(receipt.IsPresenting, Is.True);
                    Assert.That(SupermarketProductCatalog.TryGetOffer(RetailStock.RidgeKiosk, product.ItemId,
                        out var offer), Is.True);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - offer.Price));
                    Assert.That(GameSessionState.GetInventoryItemCount(product.ItemId), Is.EqualTo(count + 1));
                    Assert.That(menu.ConfirmSelection(), Is.False, "One confirmation owns exactly one grant.");
                    if (product.ItemId == InventoryItemId.PotatoPirozhok)
                    {
                        for (int i = 0; i < 100 && !receipt.IsShowing; i++) yield return null;
                        Assert.That(receipt.IsShowing, Is.True, "The full-size pie is held on the receipt.");
                        Assert.That(receipt.ActiveItemId, Is.EqualTo(product.ItemId));
                        Assert.That(receipt.Dismiss(), Is.True);
                        for (int i = 0; i < 100 && receipt.IsPresenting; i++) yield return null;
                        Assert.That(receipt.IsPresenting, Is.False);
                        yield return null;
                        Assert.That(menu.IsOpen && menu.SelectedItemId == product.ItemId, Is.True);
                        menu.Exit();
                        GameSessionState.UpdateNeeds(56, GameSessionState.StressLevel);
                        Assert.That(city.Inventory.Open(), Is.True);
                        int inventoryIndex = GameSessionState.InventoryItems.ToList().FindIndex(s => s.ItemId == product.ItemId);
                        Assert.That(inventoryIndex, Is.GreaterThanOrEqualTo(0));
                        city.Inventory.SelectItem(inventoryIndex);
                        Assert.That(city.Inventory.SelectedStack.ItemId, Is.EqualTo(product.ItemId));
                        Assert.That(city.Inventory.UseSelected(), Is.True);
                        Assert.That(GameSessionState.HungerLevel, Is.EqualTo(32));
                        Assert.That(GameSessionState.GetInventoryItemCount(product.ItemId), Is.EqualTo(count));
                        Assert.That(city.Inventory.Close(), Is.True);
                    }
                    else
                    {
                        Assert.That(receipt.Abandon(), Is.True);
                        Assert.That(menu.IsOpen, Is.True);
                        menu.Exit();
                    }
                    yield return null;
                }

                int beforeTalk = GameSessionState.CashBalance;
                yield return OpenWindow();
                Assert.That(station.Menu.SelectedIndex, Is.Zero);
                Assert.That(station.Menu.ConfirmSelection(), Is.True);
                Assert.That(station.Menu.IsOpen, Is.False);
                Assert.That(vendor.Session.IsActive, Is.True);
                for (int i = 0; i < 220 && !vendor.Session.IsChoosing && vendor.Session.IsActive; i++) yield return null;
                Assert.That(vendor.Session.IsChoosing, Is.True,
                    $"Dialogue phase={vendor.Session.Phase}, clock={vendor.Session.SpeechClock:F2}, " +
                    $"rig={hero.GetComponent<PlayerAnimatedInteractionController>().Phase}, hero={hero.transform.position:F2}, " +
                    $"vendor={vendor.Actor.transform.position:F2}, game minute={GameSessionState.GameMinuteOfDay}, " +
                    $"camera={vendor.Session.CameraDirector.LastRejectedShotReason}");
                yield return null;
                vendor.Session.SelectChoice(0);
                Assert.That(vendor.Session.Confirm(), Is.True);
                bool heardHero = false, heardSeller = false;
                for (int i = 0; i < 450 && vendor.Session.IsActive; i++)
                {
                    if (vendor.Session.Phase == DialoguePhase.Speaking)
                    {
                        if (vendor.Session.CurrentNode.Speaker == DialogueSpeaker.Hero) heardHero = true;
                        if (vendor.Session.CurrentNode.Speaker == DialogueSpeaker.Npc)
                        {
                            if (capture && !heardSeller)
                                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKiosk", "03-seller-at-window", 1280, 720);
                            heardSeller = true;
                        }
                    }
                    yield return null;
                }
                Assert.That(heardHero && heardSeller, Is.True, "A complete exchange needs both speakers.");
                Assert.That(vendor.Session.IsActive || BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(beforeTalk));
                Assert.That(hero.InputEnabled && motor.InputEnabled, Is.True);
                yield return WalkTo(sidewalk);
                yield return WalkTo(sidewalk + Vector3.right * 2.5f);
            }
            finally
            {
                if (receipt.IsPresenting) receipt.Abandon();
                station.Menu?.Close();
                if (city.Inventory.IsOpen) city.Inventory.Close();
                if (vendor.Session.IsActive) vendor.Session.Cancel();
                follow.ClearFixedPose();
            }

            IEnumerator OpenWindow()
            {
                Vector3 eye = hero.transform.position + new Vector3(.37f, 1.53f, .65f);
                follow.SetFixedPose(eye, Quaternion.LookRotation(station.InteractionPosition - eye), 59f);
                Physics.SyncTransforms();
                yield return null;
                yield return null;
                Assert.That(hero.ActiveInteractable, Is.SameAs(station));
                station.Interact(hero);
                // The press opening the window cannot also select its first action.
                yield return null;
                yield return null;
            }

            IEnumerator WalkTo(Vector3 destination)
            {
                bool arrived = false;
                for (int i = 0; i < 96 && !arrived; i++)
                {
                    arrived = motor.MoveTowardsApproachWaypoint(destination, .045f, Time.captureDeltaTime);
                    yield return null;
                }
                Assert.That(arrived, Is.True, $"Hero must physically reach {destination:F2}, stopped at {motor.transform.position:F2}.");
                Assert.That(motor.transform.position.y, Is.EqualTo(destination.y).Within(.17f));
            }
        }
    }
}
