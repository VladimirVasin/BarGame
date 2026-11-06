using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Order(-100), Timeout(360000)]
        public IEnumerator LiveCity_RidgeGroundAndViewBenchesKeepSupportedWalksAndArrivals()
        {
            GameSessionState.TryStartGameTimeAt(12 * 60);
            yield return LoadProductionCity();
            var layout = city.Layout;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var furniture = CityHillsideFurniturePlan.ForLayout(layout);
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            var mapGround = new CityMapCityTeleportGround(layout);
            var motor = city.Player.Motor;
            var capsule = motor.GetComponent<CharacterController>();
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var hero = (Player3DCharacterPresentation)city.Player.Visual;
            var home = layout.SummitHome;
            Assert.That(ground.WalkablePolygons, Is.Not.Empty, "The hillside must offer land beyond the street ribbons.");
            Assert.That(furniture.Seats.Count, Is.GreaterThan(2), "Both slopes need places to sit, besides the home view.");
            Assert.That(furniture.Placements.Any(p => p.Kind == CityMiscKind.RoadsidePhoneBooth), Is.True);
            Assert.That(furniture.Placements.Any(p => p.Kind == CityMiscKind.RoadsideDumpsterAndUtility), Is.True);
            Assert.That(furniture.UtilityDocks.Count, Is.GreaterThan(0));
            Debug.Log($"Ridge furniture: {furniture.Seats.Count} view benches, " +
                $"{furniture.Placements.Count(p => p.Kind == CityMiscKind.RoadsideDumpsterAndUtility)} dumpsters, " +
                $"{furniture.Placements.Count(p => p.Kind == CityMiscKind.RoadsidePhoneBooth)} phone booths.");

            isolatedHeroCapsule = capsule;
            motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            follow.SetOrbitInputEnabled(false);
            int previousInterval = OnDemandRendering.renderFrameInterval;
            bool previousFocus = GameInput.MovementFocused;
            var input = new InputTestFixture();
            bool inputReady = false;
            var cursorOwner = new object();
            CityBenchSitInteraction activeSeat = null;
            try
            {
                Time.captureDeltaTime = .1f;
                OnDemandRendering.renderFrameInterval = 12;
                Physics.SyncTransforms();
                var homeMass = city.World.Root.transform
                    .Find("Player Home/" + CityBuildingPrototypeWorldBuilder.LogicalCollisionObjectName)?.GetComponent<BoxCollider>();
                Assert.That(homeMass, Is.Not.Null, "Home must own its surveyed building-mass collider.");
                Assert.That(homeMass.bounds.size.x, Is.EqualTo(home.FoundationBounds.width).Within(.01f),
                    "Home collision must retain its own 13 m width, rather than borrow a generic residential prototype.");
                Assert.That(homeMass.bounds.size.z, Is.EqualTo(home.FoundationBounds.height).Within(.01f));
                Assert.That(homeMass.bounds.center.x, Is.EqualTo(home.FoundationBounds.center.x).Within(.01f));
                Assert.That(homeMass.bounds.center.z, Is.EqualTo(home.FoundationBounds.center.y).Within(.01f));
                Debug.Log("Ridge ground verification: walking both bank courtyards.");
                foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                {
                    var excursion = RidgeBankExcursion(road, ground, capsule.radius);
                    foreach (Vector3 soil in excursion.Skip(2))
                    {
                        AssertHillsideCollider(new Vector2(soil.x, soil.z), soil.y);
                        Assert.That(mapGround.TryResolveStandingPosition(new Vector2(soil.x, soil.z), out var arrival), Is.True);
                        Assert.That(Vector3.Distance(arrival, soil + Vector3.up * PlayerFactory.GroundedRootOffset),
                            Is.LessThan(.04f), road.Bank + " map arrival must use the actual off-road triangle.");
                    }
                    yield return RidgeStand(excursion[0]);
                    yield return RidgeWalk(excursion, road.Bank + " off-road courtyard");
                    yield return CapturePlayerView("ridge-ground-" + road.Bank,
                        excursion[0] + Vector3.up * .5f);
                    yield return RidgeWalk(excursion.Reverse().ToArray(), road.Bank + " return to street");
                }

                // An actual perimeter loop proves usable space behind and beside
                // the house. Its front-east corner stays tight to the real wall:
                // the dry ravine turns towards this house immediately beyond it.
                Rect bounds = home.FoundationBounds;
                const float outerClearance = 1.2f, ravineSideClearance = .45f;
                var retaining = CityHillsideSiteDetails.ForLayout(layout)
                    .Where(site => site.Part == CityHillsideSiteDetails.Kind.RetainingEnd &&
                        site.Bounds.xMax <= bounds.xMin + .1f &&
                        site.Bounds.yMin < bounds.yMax + 2f && site.Bounds.yMax > bounds.yMax - 4f)
                    .OrderBy(site => Vector3.Distance(site.Origin, home.ThresholdWorld)).First();
                float wallNorth = retaining.Bounds.yMax + .55f;
                float wallWest = retaining.Bounds.xMin - .55f;
                float wallSouth = retaining.Bounds.yMin - .55f;
                Vector2[] corners = {
                    new Vector2(home.StreetContactWorld.x, bounds.yMax + ravineSideClearance),
                    new Vector2(home.StreetContactWorld.x, wallNorth),
                    new Vector2(wallWest, wallNorth),
                    new Vector2(wallWest, wallSouth),
                    new Vector2(bounds.xMin - outerClearance, wallSouth),
                    new Vector2(bounds.xMin - outerClearance, bounds.yMin - outerClearance),
                    new Vector2(bounds.xMax + ravineSideClearance, bounds.yMin - outerClearance),
                    new Vector2(bounds.xMax + ravineSideClearance, bounds.yMax + ravineSideClearance),
                    new Vector2(home.StreetContactWorld.x, bounds.yMax + ravineSideClearance)
                };
                var homeLoop = new List<Vector3> { home.StreetContactWorld };
                foreach (Vector2 corner in corners)
                {
                    Assert.That(ground.Contains(corner, capsule.radius), Is.True,
                        "The home needs an open perimeter, including its rear: " + corner);
                    homeLoop.Add(RidgeGroundAt(corner, ground));
                }
                homeLoop.Add(home.StreetContactWorld);
                Debug.Log("Ridge ground verification: walking the home perimeter around the real retaining-wall end.");
                yield return RidgeStand(homeLoop[0]);
                yield return RidgeWalk(homeLoop, "home courtyard perimeter");
                yield return CapturePlayerView("ridge-home-continuous-ground", home.Lot.Center + Vector3.up * .5f);

                foreach (Vector3 probe in homeLoop.Skip(1).Take(homeLoop.Count - 2))
                {
                    Assert.That(mapGround.TryResolveStandingPosition(new Vector2(probe.x, probe.z), out var arrival),
                        Is.True, "New ground must accept a chart arrival.");
                    Assert.That(Vector3.Distance(arrival, probe + Vector3.up * PlayerFactory.GroundedRootOffset),
                        Is.LessThan(.04f), "The map must retain the clicked ground and exact triangle height.");
                    AssertHillsideCollider(new Vector2(probe.x, probe.z), probe.y);
                }
                Assert.That(city.World.WalkableArea.Contains(home.Lot.Center + Vector3.up * 1f, capsule.radius), Is.False,
                    "Opening the courtyard must not admit the house footprint.");
                foreach (var bridge in quarter.Bridges)
                {
                    var beside = new Vector2(bridge.DeckCenterWorld.x,
                        bridge.DeckCenterWorld.z + (bridge.Id == "court" ? -1f : 1f) *
                        (bridge.DeckBounds.height * .5f + 1.5f));
                    Assert.That(ground.Contains(beside, capsule.radius), Is.False, "A view does not open the dry cut.");
                    Assert.That(mapGround.TryResolveStandingPosition(beside, out _), Is.False,
                        "A map arrival cannot borrow the rock floor beneath a bridge.");
                }

                var tree = CityHillsideSiteDetails.ForLayout(layout)
                    .Where(site => site.Part == CityHillsideSiteDetails.Kind.YardTree)
                    .OrderBy(site => Vector3.Distance(site.Origin, home.ThresholdWorld)).First();
                var trunk = new Vector2(tree.Origin.x, tree.Origin.z);
                Assert.That(ground.Contains(trunk, capsule.radius), Is.False,
                    "Newly accessible land must still exclude the physical tree trunk.");
                if (mapGround.TryResolveStandingPosition(trunk, out var treeArrival))
                    Assert.That(Vector2.Distance(trunk, new Vector2(treeArrival.x, treeArrival.z)),
                        Is.GreaterThanOrEqualTo(.34f * tree.Scale + CityGroundTraversalPlanner.MaximumAgentRadius - .015f),
                        "A map click on a tree must fail or move the full hero capsule beyond its actual trunk.");

                Debug.Log("Ridge ground verification: checking furniture contacts and scenery directions.");
                foreach (var seat in furniture.Seats)
                {
                    var plan = new CityBenchSitPlan(seat);
                    Assert.That(city.World.WalkableArea.Contains(plan.EntryRootPosition, capsule.radius), Is.True,
                        seat.Id + " needs a full capsule on its approach/exit.");
                    AssertHillsideCollider(new Vector2(plan.EntryRootPosition.x, plan.EntryRootPosition.z), seat.GroundY);
                    var placement = furniture.Placements.Single(p => p.Id == seat.Id);
                    Assert.That(Vector3.Distance(placement.ViewTarget, seat.SeatTopCenter), Is.GreaterThan(10f),
                        seat.Id + " must face a city reveal beyond its own furniture pocket.");
                    Vector3 sight = Vector3.ProjectOnPlane(placement.ViewTarget - seat.SeatTopCenter, Vector3.up).normalized;
                    Assert.That(Vector3.Dot(seat.FaceDirection, sight), Is.GreaterThan(.8f), seat.Id + " faces away from its view.");
                }
                foreach (var dock in furniture.UtilityDocks)
                {
                    Assert.That(city.World.WalkableArea.Contains(dock.StandPosition, capsule.radius), Is.True,
                        dock.Id + " needs a clear street-side approach.");
                    AssertHillsideCollider(new Vector2(dock.StandPosition.x, dock.StandPosition.z), dock.StandPosition.y);
                }
                var homePlacement = furniture.Placements.Single(p => p.Id == CityHillsideFurniturePlan.HomeBenchId);
                var homeSeat = furniture.Seats.Single(p => p.Id == CityHillsideFurniturePlan.HomeBenchId);
                Rect bridgeBounds = quarter.Bridges.Single(bridge => bridge.Id == "summit").DeckBounds;
                Vector2 benchXZ = new Vector2(homePlacement.Position.x, homePlacement.Position.z);
                Vector2 nearestBridge = new Vector2(Mathf.Clamp(benchXZ.x, bridgeBounds.xMin, bridgeBounds.xMax),
                    Mathf.Clamp(benchXZ.y, bridgeBounds.yMin, bridgeBounds.yMax));
                Assert.That(Vector2.Distance(benchXZ, nearestBridge), Is.LessThanOrEqualTo(15f),
                    "The home view bench must stand beside the upper bridge.");
                Assert.That(Vector3.Distance(homeSeat.SeatTopCenter, home.ThresholdWorld), Is.LessThan(35f),
                    "The mandatory view bench belongs to the hero's home.");
                Vector3 towardView = Vector3.ProjectOnPlane(homePlacement.ViewTarget - homeSeat.SeatTopCenter, Vector3.up).normalized;
                Assert.That(Vector3.Dot(homeSeat.FaceDirection, towardView), Is.GreaterThan(.8f),
                    "The sitter must face the composed city view.");
                Assert.That(towardView.z, Is.GreaterThan(.35f), "The home bench faces north towards the descending city.");
                Assert.That(homePlacement.ViewTarget.y, Is.LessThan(homeSeat.GroundY - .5f),
                    "The composed home view must reveal the lower city below the crest.");
                activeSeat = city.GetComponentsInChildren<CityBenchSitInteraction>()
                    .Single(seat => seat.Plan.Id == homeSeat.Id);
                // Walk continuously from the home loop's final street contact,
                // across the road west of the bridge and around the bench's
                // western end. The east end is beside the real retaining face.
                Vector3 seatFloor = new Vector3(homeSeat.SeatTopCenter.x, homeSeat.GroundY, homeSeat.SeatTopCenter.z);
                Vector3 benchRight = Vector3.Cross(Vector3.up, homeSeat.FaceDirection);
                float endClearance = homeSeat.SeatWidth * .5f + .85f;
                float frontClearance = homeSeat.SeatDepth * .5f + .65f;
                Vector3 behindEnd = seatFloor - benchRight * endClearance - homeSeat.FaceDirection * frontClearance;
                Vector3 aheadEnd = seatFloor - benchRight * endClearance + homeSeat.FaceDirection * frontClearance;
                float crossingX = Mathf.Min(behindEnd.x, bridgeBounds.xMin - 4f);
                var summit = quarter.Links.Single(link => link.Id == "summit");
                var approachXZ = new[] {
                    new Vector2(crossingX, home.StreetContactWorld.z),
                    new Vector2(crossingX, summit.WestGateWorld.z + 4f),
                    new Vector2(behindEnd.x, behindEnd.z),
                    new Vector2(aheadEnd.x, aheadEnd.z)
                };
                var benchApproach = new List<Vector3>();
                var streetSupport = CityStreetSupportPlan.ForLayout(layout);
                foreach (Vector2 point in approachXZ)
                {
                    bool supported = streetSupport.TrySampleTop(point, out float top, out _) ||
                        ground.TrySampleTop(point, out top);
                    Assert.That(supported, Is.True, "The home-to-bench route needs real ground: " + point);
                    var foot = new Vector3(point.x, top, point.y);
                    Assert.That(city.World.WalkableArea.Contains(foot + Vector3.up * PlayerFactory.GroundedRootOffset,
                        capsule.radius), Is.True, "The home-to-bench route needs a full capsule: " + point);
                    benchApproach.Add(foot);
                }
                benchApproach.Add(activeSeat.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset);
                Debug.Log("Ridge ground verification: physically walking from Home across the street to the view bench.");
                yield return RidgeWalk(benchApproach, "home to bridge-view bench");
                Debug.Log("Ridge ground verification: using E to sit on the home bridge-view bench.");
                motor.SetInputEnabled(true);
                city.Player.Interactor.SetInputEnabled(true);
                input.Setup();
                inputReady = true;
                var keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                GameInput.HandleApplicationFocus(true);
                GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
                Vector3 eye = motor.transform.position + Vector3.up * 1.5f - homeSeat.FaceDirection * .15f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(activeSeat.InteractionPosition - eye), 59f);
                yield return null;
                yield return null;
                Assert.That(city.Player.Interactor.ActiveInteractable, Is.SameAs(activeSeat),
                    "Looking at the real bench must expose the ordinary E offer.");
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                for (int frame = 0; frame < 120 && !activeSeat.IsSeated; frame++) yield return null;
                Assert.That(activeSeat.IsSeated, Is.True, "E must seat the production hero on the home bench.");
                hero.ReapplyLatePresentationPose();
                activeSeat.Controller.RefreshActiveClipAlignment();
                Assert.That(Vector3.Distance(hero.Registry.Anchors.Pelvis.position, activeSeat.Plan.ActionHipPosition),
                    Is.LessThan(.04f), "The seated pelvis rests on the authored timber.");
                follow.ClearFixedPose();
                yield return CapturePlayerView("ridge-home-bench-seated-day", homePlacement.ViewTarget,
                    _ => Assert.That(activeSeat.IsSeated && !follow.FixedPoseActive, Is.True,
                        "The daytime view must use the ordinary camera while the hero is actually seated."));
                AdvanceRidgeClock(22 * 60);
                yield return null;
                yield return CapturePlayerView("ridge-home-bench-seated-night", homePlacement.ViewTarget,
                    _ => Assert.That(activeSeat.IsSeated && !follow.FixedPoseActive, Is.True,
                        "The night view must use the ordinary camera while the hero is actually seated."));
                // E remains owned by the same bench while seated, independent
                // of a cinematic view or a private test-only RequestExit call.
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                for (int frame = 0; frame < 120 && activeSeat.OwnsActiveInteraction; frame++) yield return null;
                Assert.That(activeSeat.OwnsActiveInteraction, Is.False, "The second E must return to the independent exit dock.");
                Assert.That(Vector3.Distance(motor.transform.position, activeSeat.Plan.EntryRootPosition), Is.LessThan(.09f));
                Assert.That(motor.InputEnabled && city.Player.Interactor.InputEnabled, Is.True);
                AdvanceRidgeClock(12 * 60);
#if UNITY_EDITOR
                var exporter = System.Type.GetType(
                    "BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true);
                Assert.That(exporter, Is.Not.Null, "The final map survey exporter must be loaded.");
                exporter.GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            }
            finally
            {
                if (activeSeat != null && activeSeat.OwnsActiveInteraction) activeSeat.Controller.CancelActiveInteraction();
                follow.ClearFixedPose();
                motor.CancelInteractionPoseMove();
                if (inputReady) input.TearDown();
                GameCursor.Release(cursorOwner);
                GameInput.HandleApplicationFocus(previousFocus);
                OnDemandRendering.renderFrameInterval = previousInterval;
            }
        }

        private IReadOnlyList<Vector3> RidgeBankExcursion(CityHillsideStreetRoute road,
            CityHillsideGroundPlan ground, float radius)
        {
            // Search finite public frontage stations. The final target must be
            // several metres beyond any road, door apron or small entrance court.
            foreach (int lane in new[] { 3, 5, 1 })
            foreach (float fraction in new[] { .52f, .68f, .35f, .22f, .80f })
            foreach (float side in new[] { -1f, 1f })
            {
                float x = Mathf.Lerp(road.LaneMinX, road.LaneMaxX, fraction);
                float z = road.GetLaneZ(lane);
                var points = new[] { new Vector2(x, z + side * 4f), new Vector2(x, z + side * 6.8f),
                    new Vector2(x, z + side * 10f), new Vector2(x + 3f, z + side * 10f) };
                var route = new List<Vector3>();
                bool valid = true;
                for (int i = 0; i < points.Length; i++)
                {
                    if (i == 0 && road.TrySampleTop(points[i], out float top, out _))
                        route.Add(new Vector3(points[i].x, top, points[i].y));
                    else if (ground.Contains(points[i], radius) && ground.TrySampleTop(points[i], out top))
                        route.Add(new Vector3(points[i].x, top, points[i].y));
                    else { valid = false; break; }
                    Vector3 standing = route[i] + Vector3.up * PlayerFactory.GroundedRootOffset;
                    if (!city.World.WalkableArea.Contains(standing, radius)) { valid = false; break; }
                }
                if (!valid) continue;
                foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings)
                    if (house.ApproachBounds.Contains(points[2]) || house.CourtBounds.Contains(points[2])) valid = false;
                for (int i = 1; i < route.Count && valid; i++)
                    for (int j = 0; j <= 16; j++)
                    {
                        Vector3 at = Vector3.Lerp(route[i - 1], route[i], j / 16f);
                        if (!city.World.WalkableArea.Contains(at, radius)) { valid = false; break; }
                    }
                if (valid) return route;
            }
            Assert.Fail(road.Bank + " has no broad reachable courtyard outside its roads and entrance aprons.");
            return System.Array.Empty<Vector3>();
        }

        private static Vector3 RidgeGroundAt(Vector2 point, CityHillsideGroundPlan ground)
        {
            Assert.That(ground.TrySampleTop(point, out float height), Is.True, "Missing exact hillside ground " + point);
            return new Vector3(point.x, height, point.y);
        }

        private IEnumerator RidgeStand(Vector3 ground)
        {
            city.Player.Motor.Teleport(ground + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            for (int i = 0; i < 4; i++) yield return null;
        }

        private IEnumerator RidgeWalk(IReadOnlyList<Vector3> route, string stage)
        {
            var motor = city.Player.Motor;
            var capsule = motor.GetComponent<CharacterController>();
            var visual = (Player3DCharacterPresentation)city.Player.Visual;
            foreach (Vector3 floor in route)
            {
                Vector3 destination = floor + Vector3.up * PlayerFactory.GroundedRootOffset;
                int limit = Mathf.CeilToInt(Vector3.Distance(motor.transform.position, destination) / .20f) + 70;
                bool arrived = false;
                for (int frame = 0; frame < limit && !arrived; frame++)
                {
                    IsolateAmbientCollisionPairs();
                    Vector3 before = motor.transform.position;
                    arrived = motor.MoveTowardsApproachWaypoint(destination, .04f, Time.captureDeltaTime);
                    yield return null;
                    Assert.That(Vector3.Distance(before, motor.transform.position), Is.LessThan(.36f), stage + " teleported.");
                    Assert.That(city.World.WalkableArea.Contains(motor.transform.position, capsule.radius), Is.True,
                        stage + " left the supported footprint at " + motor.transform.position);
                    Assert.That(visual.LeftFootGround.HasSurface || visual.RightFootGround.HasSurface, Is.True,
                        stage + " lost both boot contacts.");
                }
                if (!arrived)
                {
                    Vector3 center = capsule.transform.TransformPoint(capsule.center);
                    float radius = capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x),
                        Mathf.Abs(capsule.transform.lossyScale.z));
                    float halfAxis = Mathf.Max(0f, capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) * .5f - radius);
                    Vector3 direction = Vector3.ProjectOnPlane(destination - motor.transform.position, Vector3.up).normalized;
                    var blockers = Physics.CapsuleCastAll(center - Vector3.up * halfAxis, center + Vector3.up * halfAxis,
                        radius, direction, .8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .Where(hit => !hit.collider.transform.IsChildOf(motor.transform)).OrderBy(hit => hit.distance);
                    Debug.LogWarning(stage + " blocked capsule cast: " + string.Join("; ", blockers.Select(hit =>
                        $"{hit.collider.transform.parent?.name}/{hit.collider.name} at {hit.distance:F3}, bounds {hit.collider.bounds}").Take(8)));
                }
                Assert.That(arrived, Is.True, stage + " blocked at " + motor.transform.position + " toward " + destination);
                Assert.That(motor.transform.position.y, Is.EqualTo(destination.y).Within(.20f), stage + " wrong floor.");
            }
            motor.CancelInteractionPoseMove();
        }

        private void AdvanceRidgeClock(int minute)
        {
            if (!GameSessionState.IsGameTimeRunning) GameSessionState.TryStartGameTimeAt(minute);
            GameSessionState.AdvanceGameTime((float)((minute - GameSessionState.GameTimeOfDayMinutes + 1440d) % 1440d /
                GameTimeState.GameMinutesPerRealSecond));
            city.DayNight.ApplyCurrentTime(true);
        }
    }
}
