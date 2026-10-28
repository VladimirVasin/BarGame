using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityWorldAuditAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityMiscAssetSetup, BarPromenade.Editor", true)
                .GetMethod("RunBatch", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        // This is the ordinary City capture's itinerary, not another suite.
        // Every eye is derived from the production plan or an actual receiver.
        private static Shot[] CityLocationAuditShots(CityGameRoot city)
        {
            Assert.That(city.World, Is.Not.Null);
            city.Player.Motor.SetInputEnabled(false);
            GameTimeRuntime clock = UnityEngine.Object.FindAnyObjectByType<GameTimeRuntime>();
            if (clock != null) clock.enabled = false;
            PlayerCameraFollow follow = city.Camera.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
            var shots = new List<Shot>();
            foreach (CityDistrictKind district in new[] { CityDistrictKind.Residential, CityDistrictKind.Industrial })
                shots.Add(FrameWindowFacade("audit-street-" + district, FindOrdinaryFrontage(city.Layout, district)));
            foreach (CityDistrictPointOfInterestDescriptor poi in city.Layout.DistrictPointsOfInterest)
            {
                Assert.That(poi.Accesses, Is.Not.Empty, poi.Id);
                var access = poi.Accesses[0];
                shots.Add(Shot.At("audit-poi-" + poi.Id,
                    access.Center - access.OutwardNormal * 2f + Vector3.up * EyeHeight,
                    poi.Center + Vector3.up * 1.2f, 72f));
            }
            foreach (CityDecorationDescriptor part in city.World.DecorationPlan.Descriptors)
            {
                bool pocket = part.Kind == CityDecorationKind.ResidentialCourtyardPocket;
                bool park = part.AnchorKind == CityDecorationAnchorKind.ParkFeature ||
                            part.AnchorKind == CityDecorationAnchorKind.ParkLandmark;
                if (!part.IsLandmark && !pocket && !park) continue;
                Vector3 forward = Vector3.ProjectOnPlane(part.Forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .5f) forward = Vector3.back;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float distance = pocket ? 4.2f : 8f;
                shots.Add(Shot.At("audit-detail-" + part.StableId,
                    part.Position + forward * distance + right * 1.2f + Vector3.up * EyeHeight,
                    part.Position + Vector3.up * (pocket ? .8f : 1.6f), 72f));
            }

            CityCemeteryPlan cemetery = city.World.CemeteryPlan;
            Assert.That(cemetery, Is.Not.Null);
            CemeteryWatchmanPlan watchman = CemeteryWatchmanPlan.Create(cemetery);
            Assert.That(watchman.IsPresent, Is.True);
            CityCemeteryPartDescriptor step = cemetery.Parts.First(part => part.StableId == "cemetery-lodge-step");
            CityCemeteryPartDescriptor rear = cemetery.Parts.First(part => part.StableId == "cemetery-lodge-wall-rear");
            Vector3 outward = Vector3.ProjectOnPlane(step.Center - rear.Center, Vector3.up).normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward);
            shots.Add(Shot.At("audit-cemetery-lodge", step.Center + outward * 5f + tangent * 1.4f + Vector3.up * EyeHeight,
                step.Center + Vector3.up * 1.3f, 68f));
            CityCemeteryPartDescriptor lodgeBase = cemetery.Parts.First(part => part.StableId == "cemetery-lodge-base");
            CityCemeteryPartDescriptor windowBoard = cemetery.Parts.First(part => part.StableId == "cemetery-lodge-window-board");
            CityCemeteryPartDescriptor windowHead = cemetery.Parts.First(part => part.StableId == "cemetery-lodge-window-head");
            Vector3 windowOutward = Vector3.ProjectOnPlane(windowBoard.Center - lodgeBase.Center, Vector3.up).normalized;
            Vector3 windowAim = windowBoard.Center;
            windowAim.y = (windowBoard.Center.y + windowBoard.Size.y * .5f +
                           windowHead.Center.y - windowHead.Size.y * .5f) * .5f;
            shots.Add(SiteCloseView("audit-cemetery-watch-window",
                windowAim + windowOutward * 1.8f + Vector3.Cross(Vector3.up, windowOutward) * .2f,
                windowAim, cemetery.GroundTopY, windowOutward));
            CityCemeteryPartDescriptor gate = cemetery.Parts.First(part => part.Kind == CityCemeteryPartKind.GateArch);
            Vector3 gateInward = Vector3.ProjectOnPlane(step.Center - gate.Center, Vector3.up).normalized;
            if (gateInward.sqrMagnitude < .5f) gateInward = Vector3.right;
            Vector3 gateGround = new Vector3(gate.Center.x, cemetery.GroundTopY, gate.Center.z);
            shots.Add(Shot.At("audit-cemetery-gate", gateGround - gateInward * 7f + Vector3.up * EyeHeight,
                gateGround + Vector3.up * 1.8f, 72f));
            Vector3 grave = cemetery.Parts.First(part => part.Kind == CityCemeteryPartKind.GraveMonument).Center;
            shots.Add(Shot.At("audit-cemetery-rows", grave + new Vector3(-4f, 1.4f, -4f), grave, 70f));

            CitySeacoastPlan coast = city.World.SeacoastPlan;
            Assert.That(coast, Is.Not.Null);
            foreach (CitySeacoastPartKind kind in new[] { CitySeacoastPartKind.Hut, CitySeacoastPartKind.PierDeck,
                         CitySeacoastPartKind.EsplanadeSlab, CitySeacoastPartKind.Barge })
            {
                var group = coast.Parts.Where(part => part.Kind == kind).ToArray();
                Assert.That(group, Is.Not.Empty, kind.ToString());
                Bounds bounds = new Bounds(group[0].Center, group[0].Size);
                foreach (CitySeacoastPartDescriptor part in group) bounds.Encapsulate(new Bounds(part.Center, part.Size));
                Vector3 aim = bounds.center;
                float distance = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * .55f, 5f, 12f);
                shots.Add(Shot.At("audit-coast-" + kind,
                    new Vector3(aim.x + distance * .7f, bounds.max.y + 1.3f, aim.z - distance), aim, 74f));
            }
            CitySeacoastPartDescriptor hutFloor = coast.Parts.First(part => part.StableId == "seacoast-hut-floor");
            CitySeacoastPartDescriptor hutDoor = coast.Parts.First(part => part.StableId == CitySeacoastPlanner.HutDoorId);
            CitySeacoastPartDescriptor hutLintel = coast.Parts.First(part => part.StableId == "seacoast-hut-lintel");
            CitySeacoastPartDescriptor hutJamb = coast.Parts.First(part => part.StableId == "seacoast-hut-jamb-0");
            Vector3 hutOutward = Vector3.ProjectOnPlane(hutLintel.Center - hutFloor.Center, Vector3.up).normalized;
            Vector3 hutTangent = Vector3.Cross(Vector3.up, hutOutward);
            Vector3 hatchAim = hutJamb.Center;
            hatchAim.y = coast.Parts.Where(part => part.StableId.StartsWith("seacoast-hut-boarding-", StringComparison.Ordinal))
                .Average(part => part.Center.y);
            Vector3 frontAim = (hutDoor.Center + hatchAim) * .5f;
            AddHutCloseView("audit-coast-hut-front-door-and-hatch", frontAim + hutOutward * 3.2f + hutTangent * .35f, frontAim);
            AddHutCloseView("audit-coast-hut-boarded-hatch", hatchAim + hutOutward * 1.5f - hutTangent * .2f, hatchAim);

            void AddHutCloseView(string name, Vector3 candidate, Vector3 aim)
            {
                float ground = CitySeacoastPlanner.SampleShoreWalkTop(city.Layout, coast.Frame, candidate.x, candidate.z);
                shots.Add(SiteCloseView(name, candidate, aim, ground, hutOutward));
            }

            CitySeacoastPartDescriptor slab = coast.Parts.Where(part => part.Kind == CitySeacoastPartKind.EsplanadeSlab)
                .OrderBy(part => (part.Center - hutFloor.Center).sqrMagnitude).First();
            Vector3 slabTop = slab.Center + slab.Rotation * Vector3.up * (slab.Size.y * .5f);
            CitySeacoastPartDescriptor coping = coast.Parts.Where(part => part.Kind == CitySeacoastPartKind.EsplanadeParapet)
                .OrderBy(part => (part.Center - slabTop).sqrMagnitude).First();
            Vector3 copingTop = coping.Center + Vector3.up * (coping.Size.y * .5f);
            shots.Add(SiteCloseView("audit-coast-esplanade-slabs-and-coping", slabTop,
                Vector3.Lerp(slabTop, copingTop, .6f), slabTop.y));
            CityRiverPromenadeDescriptor eastQuay = city.Layout.River.Promenades.First(part => !part.WestBank);
            CitySeacoastPartDescriptor[] quaySteps = coast.Parts.Where(part => part.Kind == CitySeacoastPartKind.EsplanadeStair &&
                part.StableId.StartsWith(CitySeacoastPlanner.PromenadeStairEastId, StringComparison.Ordinal)).ToArray();
            Assert.That(quaySteps, Is.Not.Empty);
            Vector3 stepsAim = quaySteps.Aggregate(Vector3.zero, (sum, part) => sum + part.Center + Vector3.up * (part.Size.y * .5f)) / quaySteps.Length;
            shots.Add(SiteCloseView("audit-coast-esplanade-quay-steps",
                new Vector3(eastQuay.Bounds.center.x, eastQuay.NorthY, eastQuay.Bounds.yMax - 2f), stepsAim, eastQuay.NorthY));

            LastRouteCarAssetRegistry car = city.LastRouteCar;
            Assert.That(car != null && car.IsBound, Is.True, "Use the actual parked LastRoute model.");
            LastRouteCarSeatPlan passenger = LastRouteCarSeatPlan.Create(car, car.transform.position.y);
            LastRouteFerrymanBoardingPlan driver = LastRouteFerrymanBoardingPlan.Create(car, car.transform.position.y);
            Assert.That(passenger.IsPresent && driver.IsPresent, Is.True);
            Bounds carBounds = car.Renderers[0].bounds;
            foreach (Renderer renderer in car.Renderers) carBounds.Encapsulate(renderer.bounds);
            CityStreetSurfacePlan carStreets = CityStreetSurfacePlanner.Create(city.Layout);
            Renderer[] carOccluders = CaptureOpaqueWorldMeshes(city).Where(renderer => !renderer.transform.IsChildOf(car.transform)).ToArray();
            var carOcclusionGeometry = new Dictionary<MeshFilter, (Vector3[] vertices, int[] indices)>();
            AddCarDockView("audit-last-route-passenger-side-and-dock", car.PassengerDoorEntryAnchor,
                passenger.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset);
            AddCarDockView("audit-last-route-driver-side-and-dock", car.DriverDoorEntryAnchor, driver.DoorDockPosition);

            void AddCarDockView(string name, Transform door, Vector3 dock)
            {
                // Read both sides from the prefab root and the real entry anchors;
                // the imported Body's axes are not the car's driving frame.
                Vector3 right = Vector3.ProjectOnPlane(car.transform.right, Vector3.up).normalized;
                Vector3 side = Vector3.Dot(door.position - car.transform.position, right) >= 0f ? right : -right;
                Vector3 dockEye = SiteCloseView(name + "-standing-dock", dock, door.position, dock.y).Position;
                Assert.That(Mathf.Abs(dockEye.y - EyeHeight - dock.y), Is.LessThan(.05f), name + " dock surface drift.");
                bool driverDetail = door == car.DriverDoorEntryAnchor;
                Transform leaf = driverDetail ? car.DriverDoorLeaf : car.PassengerDoorLeaf;
                Renderer panel = car.Bindings.Single(binding => binding.Role == "door_panel" && binding.Renderer.transform.IsChildOf(leaf)).Renderer;
                Renderer handle = car.Bindings.Single(binding => binding.Role == "door_handle" && binding.Renderer.transform.IsChildOf(leaf)).Renderer;
                Bounds focusBounds = driverDetail ? panel.bounds : carBounds;
                Vector3[] visibleDetails = { handle.bounds.center, panel.bounds.center + Vector3.up * .18f,
                    panel.bounds.center - Vector3.up * .18f, new Vector3(panel.bounds.center.x, panel.bounds.min.y + .04f, panel.bounds.center.z) };
                Vector3 aim = driverDetail ? Vector3.Lerp(panel.bounds.center, dock + Vector3.up * .3f, .35f) :
                    Vector3.Lerp(carBounds.center, dock + Vector3.up * (carBounds.size.y * .45f), .25f);
                Vector3 tangent = Vector3.Cross(Vector3.up, side);
                var candidates = new List<Vector3>(54);
                float outer = Mathf.Max(6.5f, carBounds.size.magnitude * 1.25f);
                float dockStandoff = Vector3.Dot(dock - carBounds.center, side);
                foreach (float standoff in new[] { outer, 5.5f, 4.5f, 3.5f, 2.5f, dockStandoff })
                    foreach (float along in new[] { 0f, -1.5f, 1.5f, -3f, 3f, -4.5f, 4.5f, -6f, 6f })
                    {
                        Vector3 point = carBounds.center + side * standoff + tangent * along;
                        // The driver's flank faces the waterfront edge: a fixed
                        // broadside offset can be over the sea. Use only actual
                        // planned street/ground, then retain the physical checks.
                        if (!TryReplanningSurfaceTop(city.Layout, carStreets, new Vector2(point.x, point.z), out float top)) continue;
                        point.y = top;
                        candidates.Add(point);
                    }
                Assert.That(candidates, Is.Not.Empty, name + " lacks planned standing ground on this side.");
                if (driverDetail) candidates.Sort((a, b) => (a - dock).sqrMagnitude.CompareTo((b - dock).sqrMagnitude));
                Shot shot = SiteCloseView(name, candidates[0], aim, dock.y, side, candidates, FramesWholeCarAndDock);
                shots.Add(shot);
                Debug.Log("LASTROUTE SIDE VIEW: " + name + ", actual dock=" + dock + ", supported eye=" + shot.Position);

                bool FramesWholeCarAndDock(Shot pose)
                {
                    Quaternion view = Quaternion.Inverse(Quaternion.LookRotation(pose.Target - pose.Position));
                    float tanY = Mathf.Tan(pose.FieldOfView * Mathf.Deg2Rad * .5f) * .9f;
                    float tanX = tanY * Mathf.Min(city.Camera.aspect, 4f / 3f);
                    for (int corner = 0; corner < 8; corner++)
                        if (!InFrame(new Vector3((corner & 1) == 0 ? focusBounds.min.x : focusBounds.max.x,
                            (corner & 2) == 0 ? focusBounds.min.y : focusBounds.max.y,
                            (corner & 4) == 0 ? focusBounds.min.z : focusBounds.max.z))) return false;
                    if (!InFrame(dock) || visibleDetails.Any(point => !InFrame(point))) return false;
                    // The actual island wall and furniture may hide a perfectly
                    // framed AABB. Every door/handle/body sample needs real LOS.
                    foreach (Vector3 point in visibleDetails)
                    {
                        Vector3 delta = point - pose.Position;
                        Ray ray = new Ray(pose.Position, delta.normalized);
                        if (carOccluders.Any(renderer => CaptureOpaqueMeshBlocks(renderer, ray, delta.magnitude, carOcclusionGeometry))) return false;
                    }
                    return true;

                    bool InFrame(Vector3 world)
                    {
                        Vector3 local = view * (world - pose.Position);
                        return local.z > 0f && Mathf.Abs(local.x) < local.z * tanX && Mathf.Abs(local.y) < local.z * tanY;
                    }
                }
            }

            foreach (CityFringeYardDescriptor yard in city.World.FringeYardPlan.Yards)
                shots.Add(Shot.At("audit-fringe-" + yard.StableId,
                    yard.Access.Center - yard.Access.OutwardNormal * 2f + Vector3.up * EyeHeight,
                    new Vector3(yard.TraversalBounds.center.x, yard.Access.Center.y + 1f, yard.TraversalBounds.center.y), 76f));

            var yardParts = city.World.OpenAreaDecorationPlan.Descriptors;
            Assert.That(yardParts, Is.Not.Empty);
            Bounds yardBounds = yardParts[0].Bounds;
            foreach (CityOpenAreaDecorationDescriptor part in yardParts) yardBounds.Encapsulate(part.Bounds);
            shots.Add(Shot.At("audit-bar-side-yard", yardBounds.center + new Vector3(0f, 2f, -8f),
                yardBounds.center, 76f));
            foreach (Shot shot in CityArchShelterShots(city))
                shots.Add(Shot.At("audit-arch-" + shot.Name, shot.Position, shot.Target, shot.FieldOfView));
            foreach (Shot shot in ChurchGardenShots(city))
                shots.Add(Shot.At("audit-church-" + shot.Name, shot.Position, shot.Target, shot.FieldOfView));

            CityFairPlan fair = city.World.Fair.Plan;
            Assert.That(fair.IsEnabled, Is.True);
            foreach (CityFairStall stall in fair.Stalls)
                shots.Add(Shot.At("audit-fair-" + stall.Id,
                    stall.Position + stall.Facing * 4.5f + Vector3.up * EyeHeight,
                    stall.Position + Vector3.up * 1.15f, 72f));
            CityEastExitPlan east = CityEastExitPlanner.Create(city.Layout);
            shots.Add(Shot.At("audit-east-post", east.CheckpointPosition + new Vector3(-9f, EyeHeight, 1.7f),
                east.CheckpointPosition + Vector3.up * EyeHeight, 70f));
            shots.Add(Shot.At("audit-east-valley", east.CheckpointPosition + new Vector3(-2f, EyeHeight, 0f),
                east.RoadEnd + new Vector3(12000f, 90f, 0f), 65f));
            var staged = new List<Shot>();
            foreach (Shot pose in shots) staged.Add(Observe(pose, false));
            foreach (CityDecorationDescriptor part in city.World.DecorationPlan.Descriptors)
                if (part.Kind == CityDecorationKind.ParkBandstand || part.Kind == CityDecorationKind.OldTownClockTower)
                {
                    Shot pose = shots.Single(shot => shot.Name == "audit-detail-" + part.StableId);
                    staged.Add(Observe(Shot.At(pose.Name + "-night", pose.Position, pose.Target, pose.FieldOfView), true));
                }
            Assert.That(staged.Select(shot => shot.Name).Distinct().Count(), Is.EqualTo(staged.Count));
            return staged.ToArray();

            Shot SiteCloseView(string name, Vector3 candidate, Vector3 aim, float plannedTop, Vector3? outwardNormal = null,
                IReadOnlyList<Vector3> plannedCandidates = null, Func<Shot, bool> acceptFrame = null)
            {
                if (outwardNormal.HasValue)
                {
                    Assert.That(outwardNormal.Value.sqrMagnitude, Is.GreaterThan(.9f), name + " lost its facade frame.");
                    if (plannedCandidates == null)
                        Assert.That(Vector3.Dot(candidate - aim, outwardNormal.Value), Is.GreaterThan(1f), name + " must remain outside the facade.");
                }
                CharacterController body = city.Player.Motor.GetComponent<CharacterController>();
                Assert.That(body, Is.Not.Null);
                Assert.That(follow, Is.Not.Null, "Use the ordinary gameplay lens for the close views.");
                Physics.SyncTransforms();
                bool IsActor(Collider collider) => collider.transform.IsChildOf(city.Player.GameObject.transform) ||
                    collider.GetComponentInParent<CityPedestrianActor>() != null || collider.GetComponentInParent<DefaultNpcAppearance>() != null;
                // Stay on the same facade side. A bench may occupy the first
                // viewpoint; it is an obstacle, never a standing-floor shortcut.
                Vector3 tangent = outwardNormal.HasValue ? Vector3.Cross(Vector3.up, outwardNormal.Value) : Vector3.zero;
                string rejected = string.Empty;
                for (int index = 0; index < (plannedCandidates?.Count ?? (outwardNormal.HasValue ? 7 : 1)); index++)
                {
                    float lateral = index == 0 ? 0f : (index % 2 == 1 ? 1f : -1f) * ((index + 1) / 2) * .65f;
                    Vector3 trial = plannedCandidates != null ? plannedCandidates[index] : candidate + tangent * lateral;
                    float expected = plannedCandidates != null ? trial.y : plannedTop;
                    if (plannedCandidates == null && outwardNormal.HasValue && !CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout,
                            new Vector2(trial.x, trial.z), out expected, out _))
                    { rejected = "no planned ground at " + trial; continue; }
                    foreach (CitySeacoastPartDescriptor part in city.World.SeacoastPlan.Parts)
                    {
                        bool walkTop = part.Kind == CitySeacoastPartKind.EsplanadeSlab || part.Kind == CitySeacoastPartKind.EsplanadeStair ||
                            part.Kind == CitySeacoastPartKind.PierDeck || part.Kind == CitySeacoastPartKind.FootbridgeDeck ||
                            part.Kind == CitySeacoastPartKind.BluffStair || part.Kind == CitySeacoastPartKind.Slipway ||
                            part.Kind == CitySeacoastPartKind.MolDeck || part.Kind == CitySeacoastPartKind.MolStair;
                        if (walkTop && new RuntimeOrientedBox(part.Center, part.Rotation, part.Size).TrySampleTop(trial, out float top))
                            expected = Mathf.Max(expected, top);
                    }
                    RaycastHit[] supports = Physics.RaycastAll(new Vector3(trial.x, expected + 1f, trial.z),
                        Vector3.down, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    var floors = supports.Where(hit => hit.normal.y > .7f && Mathf.Abs(hit.point.y - expected) < .25f &&
                        !IsActor(hit.collider)).OrderByDescending(hit => hit.point.y).ToArray();
                    if (floors.Length == 0) { rejected = "no physical floor near " + expected + " at " + trial; continue; }
                    Vector3 foot = floors[0].point;
                    if (!city.World.WalkableArea.Contains(foot, body.radius))
                    { rejected = "outside walking mask at " + foot; continue; }
                    Collider obstacle = Physics.OverlapCapsule(foot + Vector3.up * (body.radius + .34f),
                        foot + Vector3.up * 1.45f, body.radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .FirstOrDefault(collider => !IsActor(collider));
                    if (obstacle != null) { rejected = "body column crosses " + obstacle.name + " at " + foot; continue; }
                    Shot pose = Shot.At(name, foot + Vector3.up * EyeHeight, aim, follow.FollowFieldOfView);
                    if (acceptFrame != null && !acceptFrame(pose))
                    { rejected = "ordinary lens crops the actual car/dock from " + foot; continue; }
                    return pose;
                }
                Assert.Fail(name + " has no supported clear exterior standing view: " + rejected);
                return default;
            }

            Shot Observe(Shot pose, bool night)
            {
                bool configured = false;
                int settled = 0;
                return Shot.At(pose.Name, pose.Position, pose.Target, pose.FieldOfView, readyWhen: () =>
                {
                    if (!configured)
                    {
                        if (night && GameSessionState.GameTimeOfDayMinutes < 20 * 60)
                        {
                            GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                                GameTimeState.GameMinutesPerRealSecond));
                            city.DayNight.ApplyCurrentTime(true);
                        }
                        MoveCityLightingObserver(city, pose);
                        configured = true;
                    }
                    return ++settled >= 4;
                });
            }
        }
    }
}
