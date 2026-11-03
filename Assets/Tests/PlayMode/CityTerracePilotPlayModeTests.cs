using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityTerracePilotPlayModeTests
    {
        private const float FrameSeconds = .05f;
        private const int MaximumWaypointFrames = 160;
        private CityTerracePilotRoot root;
        private CityGameRoot city;
        private Scene previousScene, pilotScene;
        private readonly List<GameObject> hiddenRoots = new List<GameObject>();
        private readonly Dictionary<Collider, bool> ambientCollisionStates = new Dictionary<Collider, bool>();
        private CharacterController isolatedHeroCapsule;
        private GameSessionState.CheckpointSnapshot previousSession;
        private CheckpointWorldState previousWorld;
        private float previousCaptureDelta, previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            previousTimeScale = Time.timeScale;
            previousSession = GameSessionState.CaptureCheckpoint();
            previousWorld = CheckpointWorldState.Capture();
            previousScene = SceneManager.GetActiveScene();
            // Preserve the test runner's scene and exclude any prior camera or
            // collider from the same physics world without loading the City.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (GameObject candidate in SceneManager.GetSceneAt(i).GetRootGameObjects())
            {
                if (!candidate.activeSelf || candidate.GetComponentInChildren<Camera>() == null &&
                    candidate.GetComponentInChildren<Collider>() == null) continue;
                hiddenRoots.Add(candidate);
                candidate.SetActive(false);
            }
            pilotScene = SceneManager.CreateScene("CityTerracePilotVerification");
            SceneManager.SetActiveScene(pilotScene);
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = FrameSeconds;
            Time.timeScale = 1f;
            yield return null;
        }

        private IEnumerator CreateIsolatedPilot()
        {
            root = new GameObject("City Terrace Pilot Verification").AddComponent<CityTerracePilotRoot>();
            yield return null;
            Assert.That(root.IsReady, Is.True, "The isolated pilot must compose without loading the production City.");
            root.Player.Motor.SetInputEnabled(false);
            root.Follow.SetOrbitInputEnabled(false);
            for (int frame = 0; frame < 6; frame++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (isolatedHeroCapsule != null)
                foreach (KeyValuePair<Collider, bool> pair in ambientCollisionStates)
                    if (pair.Key != null) Physics.IgnoreCollision(isolatedHeroCapsule, pair.Key, pair.Value);
            ambientCollisionStates.Clear();
            isolatedHeroCapsule = null;
            if (root != null) Object.Destroy(root.gameObject);
            // Live City owners use scene-unload lifecycle guards. Unloading
            // below must own their destruction, rather than an earlier root
            // Destroy while the scene still reports itself as loaded.
            yield return null;
            Scene cleanup = previousScene.IsValid() && previousScene.isLoaded
                ? previousScene : SceneManager.CreateScene("Terrace Verification Cleanup");
            SceneManager.SetActiveScene(cleanup);
            if (pilotScene.IsValid() && pilotScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(pilotScene);
                if (unload != null) yield return unload;
            }
            previousWorld?.Restore();
            if (previousSession != null) GameSessionState.RestoreCheckpoint(previousSession);
            foreach (GameObject candidate in hiddenRoots) if (candidate != null) candidate.SetActive(true);
            hiddenRoots.Clear();
            Time.captureDeltaTime = previousCaptureDelta;
            Time.timeScale = previousTimeScale;
            root = null;
            city = null;
        }

        [UnityTest]
        public IEnumerator ProductionHero_TraversesBothTerraceRoutesWithMeasuredContactsAndCityFrames()
        {
            yield return CreateIsolatedPilot();
            Assert.That(root.Player.Visual, Is.InstanceOf<Player3DCharacterPresentation>());
            Assert.That(root.Camera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(.090f).Within(.0001f));
            Assert.That(root.Camera.farClipPlane, Is.EqualTo(48f).Within(.001f));
            Assert.That(root.Plan.TotalRise, Is.EqualTo(7.68f).Within(.001f));
            AssertAuthoredSurfaces();
            AssertBoundaryGuards();

            Capture("01-lower-court", new Vector3(0f, 2.1f, -4f), new Vector3(0f, 5f, 15f));
            yield return WalkRoute(root.Plan.WestRoute, false, "west-up", true);
            Assert.That(root.Player.GameObject.transform.position.y, Is.EqualTo(7.72f).Within(.15f));
            Capture("03-upper-court", new Vector3(-6f, 10.8f, 24f), new Vector3(-14f, 3.8f, 10f));
            yield return WalkRoute(root.Plan.WestRoute, true, "west-down", false);
            yield return WalkRoute(root.Plan.EastRoute, false, "east-up", false);
            yield return WalkRoute(root.Plan.EastRoute, true, "east-down", false);
            Assert.That(Vector3.Distance(root.Player.GameObject.transform.position, root.Plan.SpawnPosition),
                Is.LessThan(.15f), "Both round trips return by physical walking to the lower court.");
        }

        [UnityTest]
        public IEnumerator LiveCity_StreetEntryAndBothTerraceRoutesKeepContactsArrivalsAndMap()
        {
            Debug.Log("Live terrace verification: loading production City.");
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            pilotScene = SceneManager.GetActiveScene();
            float deadline = Time.realtimeSinceStartup + 90f;
            float nextProgress = Time.realtimeSinceStartup + 5f;
            while (city == null || !city.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Production City did not become ready.");
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (Time.realtimeSinceStartup >= nextProgress)
                {
                    Debug.Log("Live terrace initialization: world=" + (city != null && city.World != null) +
                        ", hero=" + (city != null && city.Player.GameObject != null));
                    nextProgress = Time.realtimeSinceStartup + 5f;
                }
                yield return null;
            }
            CityTerraceDistrictPlan plan = CityTerraceDistrictPlan.ForLayout(city.Layout);
            Assert.That(plan.IsEnabled, Is.True);
            Transform world = city.World.Root.transform.Find("Terraced Courtyards");
            Assert.That(world, Is.Not.Null, "The production builder must own the courtyard; a separate pilot root is insufficient.");
            Assert.That(Vector3.Distance(world.position, plan.Origin), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(world.rotation, plan.Rotation), Is.LessThan(.01f));
            Assert.That(plan.EntryGradePercent, Is.LessThanOrEqualTo(8.3f));
            Assert.That(plan.LowerPassGradePercent, Is.LessThanOrEqualTo(8.3f));
            int openLots = 0, houses = 0;
            foreach (BuildingLot lot in city.Layout.BuildingLots)
            {
                if (!plan.IsReservedCell(lot.Cell)) continue;
                if (lot.IsOpenGround) openLots++;
                if (lot.IsOrdinaryBuilding) houses++;
            }
            Assert.That(openLots, Is.EqualTo(2));
            Assert.That(houses, Is.EqualTo(2));
            Assert.That(city.Layout.BuildingLots.Count, Is.EqualTo(144), "Reservation retains the existing lot identities.");
#if UNITY_EDITOR
            // Produce the geometry reference in the same live-world invocation,
            // before walking, so the authored map can be redrawn immediately.
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            AssertDistrictSurfaces(plan, world);
            AssertPedestrianLayerContacts(plan);
            // This fixture follows exact authored centre-lines rather than
            // steering around an ambient walker. Keep the population running
            // and its world contacts; isolate only hero/walker collision pairs.
            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            IsolateAmbientCollisionPairs();
            AssertDistrictMap(plan);
            AssertStackedCrossingBoundaries(plan);
            AssertCrossingRainShelter(plan);
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            PlayerCameraFollow follow = city.Camera.GetComponent<PlayerCameraFollow>();
            follow.SetOrbitInputEnabled(false);
            Vector3 street = plan.EntryStartWorld - Vector3.right * .5f;
            Assert.That(city.Layout.ElevationPlan.TrySampleSurface(new Vector2(street.x, street.z),
                CitySurfaceRole.RoadTop, out float streetTop, out _), Is.True);
            street.y = streetTop;
            // The only teleport selects the existing street before the actual
            // ingress. Every courtyard and house contact below is walked.
            city.Player.Motor.Teleport(street + Vector3.up * PlayerFactory.GroundedRootOffset);
            for (int frame = 0; frame < 5; frame++) yield return null;
            CaptureCity("live-01-street-entry", plan.World(new Vector3(0f, 2.2f, -15f)),
                plan.World(new Vector3(0f, 4f, 7f)));
            yield return WalkCityRoute(new[] { street, plan.EntryStartWorld, plan.EntryEndWorld, plan.LowerArrivalWorld },
                false, "street-entry", plan);
            Vector3 eastStreet = plan.LowerPassEndWorld + Vector3.right * .5f;
            Assert.That(city.Layout.ElevationPlan.TrySampleSurface(new Vector2(eastStreet.x, eastStreet.z),
                CitySurfaceRole.RoadTop, out float eastStreetTop, out _), Is.True);
            eastStreet.y = eastStreetTop;
            IReadOnlyList<Vector3> lowerPass = new[] { plan.LowerArrivalWorld, plan.LowerPassStartWorld,
                plan.LowerCrossingWorld, plan.LowerPassEndWorld, eastStreet };
            yield return WalkCityRoute(lowerPass, false, "lower-pass-out", plan);
            yield return WalkCityRoute(lowerPass, true, "lower-pass-return", plan);
            IReadOnlyList<Vector3> west = DistrictRoute(plan, plan.Pilot.WestRoute);
            IReadOnlyList<Vector3> east = DistrictRoute(plan, plan.Pilot.EastRoute);
            yield return WalkCityRoute(west, false, "west-up", plan, -1);
            CaptureCity("live-02-upper-court", plan.World(new Vector3(-6f, 10.8f, 24f)),
                plan.World(new Vector3(-13f, 3.84f, 2f)));
            // Crossing from one stair route to the other traverses the whole
            // upper deck. The return loop crosses it in the opposite direction.
            yield return WalkCityRoute(east, true, "east-down", plan);
            yield return WalkCityRoute(east, false, "east-up", plan, 1);
            yield return WalkCityRoute(west, true, "west-down", plan);
            yield return WalkCityRoute(new[] { plan.LowerArrivalWorld, plan.EntryEndWorld, plan.EntryStartWorld, street },
                false, "street-return", plan);
            CaptureCity("live-03-residential-section", plan.World(new Vector3(17f, 6.5f, 9f)),
                plan.World(new Vector3(13.04f, 5.4f, -5f)));
            Debug.Log("Live terrace verification completed: street, lower passage, both upper loops, layered contacts, camera, rain and map arrivals.");
        }

        private IEnumerator WalkCityRoute(IReadOnlyList<Vector3> route, bool reverse, string name,
            CityTerraceDistrictPlan plan, int residenceSide = 0)
        {
            PlayerMotor motor = city.Player.Motor;
            CharacterController capsule = motor.GetComponent<CharacterController>();
            var presentation = (Player3DCharacterPresentation)city.Player.Visual;
            Debug.Log("Live terrace walking: " + name);
            for (int corner = 0; corner < route.Count; corner++)
            {
                int index = reverse ? route.Count - 1 - corner : corner;
                Vector3 target = route[index] + Vector3.up * PlayerFactory.GroundedRootOffset;
                bool arrived = false;
                int limit = Mathf.Max(MaximumWaypointFrames,
                    Mathf.CeilToInt(Vector3.Distance(motor.transform.position, target) / (2.6f * FrameSeconds)) + 60);
                for (int frame = 0; frame < limit && !arrived; frame++)
                {
                    IsolateAmbientCollisionPairs();
                    Vector3 before = motor.transform.position;
                    arrived = motor.MoveTowardsApproachWaypoint(target, .035f, FrameSeconds);
                    yield return null;
                    Vector3 position = motor.transform.position;
                    Assert.That(Vector3.Distance(before, position), Is.LessThan(.35f), name + " teleported.");
                    Assert.That(city.World.WalkableArea.Contains(position, capsule.radius), Is.True,
                        name + " left its supported corridor at " + position);
                    if (plan.ContainsFootprint(new Vector2(position.x, position.z)))
                    {
                        Assert.That(plan.TryGetSurfaceHeight(position, out float top), Is.True);
                        Assert.That(position.y - top, Is.InRange(-.06f, .30f), name + " lost physical floor contact.");
                    }
                    Assert.That(presentation.LeftFootGround.HasSurface || presentation.RightFootGround.HasSurface,
                        Is.True, name + " lost both foot probes.");
                    AssertFootNearActor(presentation.LeftFootGround, position.y, name + " left foot");
                    AssertFootNearActor(presentation.RightFootGround, position.y, name + " right foot");
                }
                if (!arrived) LogBlockedRoute(name, target, capsule, plan);
                Assert.That(arrived, Is.True, name + " blocked by navigation or an old fence/prop at " +
                    motor.transform.position + " toward " + target);
                Assert.That(motor.transform.position.y, Is.EqualTo(target.y).Within(.20f));
                if (name == "lower-pass-out" && index == 2)
                {
                    yield return AssertCrossingContactAndCamera(plan, false);
                    CaptureCity("live-05-underpass", plan.LowerCrossingWorld + new Vector3(-3.5f, 1.65f, .35f),
                        plan.UpperCrossingWorld + Vector3.up * .1f);
                }
                if (name == "west-up" && index == route.Count - 1)
                {
                    yield return AssertCrossingContactAndCamera(plan, true);
                    CaptureCity("live-06-upper-crossing", plan.UpperCrossingWorld + new Vector3(.4f, 2.1f, -4.5f),
                        plan.LowerCrossingWorld + new Vector3(3f, 1f, 0f));
                }
                if (residenceSide != 0 && index == 7)
                {
                    if (residenceSide < 0)
                        CaptureCity("live-04-stair-contact", plan.World(new Vector3(-12.7f, 5.5f, 12.6f)),
                            plan.World(new Vector3(-15f, 5.65f, 17.7f)));
                    Vector3 court = plan.World(new Vector3(residenceSide * 13.04f,
                        CityTerracePilotPlan.FlightRise * 2f, .04f));
                    Vector3 entry = plan.World(new Vector3(residenceSide * 13.04f,
                        CityTerracePilotPlan.FlightRise * 2f, -4.96f));
                    Vector3 threshold = default;
                    float closest = float.PositiveInfinity;
                    foreach (Vector3 candidate in plan.HouseEntryWorld)
                    {
                        float distance = Vector3.Distance(candidate, entry);
                        if (distance < closest) { threshold = candidate; closest = distance; }
                    }
                    Assert.That(closest, Is.LessThan(2f), "The retained house has no matching entrance contact.");
                    yield return WalkCityRoute(new[] { route[index], court, entry, threshold,
                        threshold - Vector3.right * .5f, threshold, entry, court, route[index] },
                        false, name + "-house-contact", plan);
                }
                if ((corner & 3) == 3) Debug.Log("Live terrace " + name + " corner " + index);
            }
            motor.CancelInteractionPoseMove();
        }

        private void IsolateAmbientCollisionPairs()
        {
            if (isolatedHeroCapsule == null || city == null || city.Pedestrians == null) return;
            foreach (CityPedestrianActor actor in city.Pedestrians.Actors)
            {
                Collider body = actor != null ? actor.CharacterController : null;
                if (body == null) continue;
                if (!ambientCollisionStates.ContainsKey(body))
                    ambientCollisionStates.Add(body, Physics.GetIgnoreCollision(isolatedHeroCapsule, body));
                // Pooled bodies can be enabled later. Refresh the scoped pair
                // immediately before the fixture moves, without stopping NPCs.
                if (body.enabled && body.gameObject.activeInHierarchy &&
                    !Physics.GetIgnoreCollision(isolatedHeroCapsule, body))
                    Physics.IgnoreCollision(isolatedHeroCapsule, body, true);
            }
        }

        private void LogBlockedRoute(string route, Vector3 target, CharacterController capsule, CityTerraceDistrictPlan plan)
        {
            Vector3 current = capsule.transform.position;
            Vector3 direction = target - current; direction.y = 0f;
            Vector3 desired = current + direction.normalized * .15f;
            Vector3 constrained = city.World.WalkableArea.Constrain(current, desired, capsule.radius);
            var text = new System.Text.StringBuilder("Terrace blocked diagnostics: ");
            text.Append(route).Append(" current=").Append(current.ToString("F3"))
                .Append(" desired=").Append(desired.ToString("F3"))
                .Append(" constrained=").Append(constrained.ToString("F3"))
                .Append(" flags=").Append(capsule.collisionFlags);
            if (plan.TryResolveStandingSurface(new Vector2(current.x, current.z), current.y,
                capsule.radius, out CityTerraceSurfaceContact contact))
                text.Append(" surface=").Append(contact.SurfaceId).Append('@').Append(contact.Height.ToString("F3"));
            else text.Append(" surface=none");
            Vector3 center = capsule.transform.TransformPoint(capsule.center);
            float halfLine = Mathf.Max(0f, capsule.height * .5f - capsule.radius) * Mathf.Abs(capsule.transform.lossyScale.y);
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Abs(capsule.transform.lossyScale.z));
            var seen = new HashSet<Collider>();
            foreach (Vector3 offset in new[] { Vector3.zero, desired - current })
                foreach (Collider hit in Physics.OverlapCapsule(center + offset + Vector3.up * halfLine,
                    center + offset - Vector3.up * halfLine, radius + .08f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit == null || hit.transform.IsChildOf(capsule.transform) || !seen.Add(hit) || seen.Count > 24) continue;
                    text.Append(" | ").Append(hit.name).Append('(').Append(hit.GetType().Name).Append(") parent=")
                        .Append(hit.transform.parent != null ? hit.transform.parent.name : "<root>")
                        .Append(" bounds=").Append(hit.bounds.ToString("F3"))
                        .Append(" ignored=").Append(Physics.GetIgnoreCollision(capsule, hit));
                }
            Debug.Log(text.ToString());
        }

        private static void AssertFootNearActor(FootGroundSample sample, float rootY, string context)
        {
            if (!sample.HasSurface) return;
            Assert.That(sample.HeelY - rootY, Is.InRange(-.40f, .33f), context + " chose another storey.");
            if (sample.HasToeSurface)
                Assert.That(sample.ToeY - rootY, Is.InRange(-.40f, .33f), context + " toe chose another storey.");
        }

        private void AssertStackedCrossingBoundaries(CityTerraceDistrictPlan plan)
        {
            Vector3 lower = plan.LowerCrossingWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 upper = plan.UpperCrossingWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
            Assert.That(new Vector2(lower.x, lower.z), Is.EqualTo(new Vector2(upper.x, upper.z)));
            Assert.That(upper.y - lower.y, Is.GreaterThan(3f), "The lower alley needs a real clearance beneath the crossing.");
            IWalkableArea area = city.World.WalkableArea;
            float radius = city.Player.Motor.GetComponent<CharacterController>().radius;
            Vector2 crossing = new Vector2(lower.x, lower.z);
            Assert.That(plan.TryResolveStandingSurface(crossing, lower.y, radius, out CityTerraceSurfaceContact lowerContact), Is.True);
            Assert.That(plan.TryResolveStandingSurface(crossing, upper.y, radius, out CityTerraceSurfaceContact upperContact), Is.True);
            Assert.That(lowerContact.SurfaceId, Is.EqualTo("lower-pass"));
            Assert.That(upperContact.SurfaceId, Is.EqualTo("upper-bridge"));
            Assert.That(lowerContact.Height, Is.EqualTo(plan.LowerCrossingWorld.y).Within(.005f));
            Assert.That(upperContact.Height, Is.EqualTo(plan.UpperCrossingWorld.y).Within(.005f));
            Assert.That(area.Contains(lower, radius), Is.True, "The lower floor is independently navigable.");
            Assert.That(area.Contains(upper, radius), Is.True, "The upper floor is independently navigable.");
            Assert.That(area.Contains((lower + upper) * .5f, radius), Is.False, "Air between storeys is not a third floor.");
            Assert.That(plan.TryGetSurfaceHeight(lower, out float lowerTop), Is.True);
            Assert.That(plan.TryGetSurfaceHeight(upper, out float upperTop), Is.True);
            Assert.That(lowerTop, Is.EqualTo(plan.LowerCrossingWorld.y).Within(.005f));
            Assert.That(upperTop, Is.EqualTo(plan.UpperCrossingWorld.y).Within(.005f));

            // The lower alley runs along world X; the upper bridge crosses it
            // along world Z. A long move cannot borrow the other floor's mask.
            Vector3 acrossLowerWall = area.Constrain(lower, lower + Vector3.forward * 4f, radius);
            Assert.That(acrossLowerWall.z - lower.z, Is.LessThan(1.6f), "Lower motion escaped through an upper platform.");
            Vector3 overUpperEdge = area.Constrain(upper, upper + Vector3.right * 4f, radius);
            Assert.That(overUpperEdge.x - upper.x, Is.LessThan(1.3f), "Upper motion borrowed the alley below the bridge edge.");
        }

        private void AssertCrossingRainShelter(CityTerraceDistrictPlan plan)
        {
            Collider shelter = null;
            Vector3 below = plan.LowerCrossingWorld + Vector3.up * 1.6f;
            Vector3 above = plan.UpperCrossingWorld + Vector3.up * .1f;
            foreach (Collider candidate in city.Rain.LocalShelters)
            {
                if (candidate == null) continue;
                Assert.That(candidate.bounds.Contains(above), Is.False, "The exposed upper path must retain rain.");
                if (candidate.bounds.Contains(below)) shelter = candidate;
            }
            Assert.That(shelter, Is.Not.Null, "The shared rain field needs a bounded volume under the deck.");
            Assert.That(shelter.isTrigger, Is.True);
            Assert.That(shelter.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
            Assert.That(shelter.bounds.max.y, Is.LessThanOrEqualTo(plan.UpperCrossingWorld.y + .001f));
            Assert.That(shelter.bounds.Contains(plan.LowerPassStartWorld + Vector3.up * 1.6f), Is.False,
                "The entrance and exposed alley must not become globally sheltered.");
        }

        private IEnumerator AssertCrossingContactAndCamera(CityTerraceDistrictPlan plan, bool upper)
        {
            for (int frame = 0; frame < 4; frame++) yield return null;
            float expectedTop = upper ? plan.UpperCrossingWorld.y : plan.LowerCrossingWorld.y;
            var presentation = (Player3DCharacterPresentation)city.Player.Visual;
            foreach (FootGroundSample sample in new[] { presentation.LeftFootGround, presentation.RightFootGround })
            {
                Assert.That(sample.HasSurface, Is.True, "Both feet need support at the layered crossing.");
                Assert.That(sample.HeelY, Is.EqualTo(expectedTop).Within(.12f));
                if (sample.HasToeSurface) Assert.That(sample.ToeY, Is.EqualTo(expectedTop).Within(.12f));
            }
            Vector3 rootPosition = city.Player.Motor.transform.position;
            Assert.That(Physics.Raycast(rootPosition + Vector3.up * .3f, Vector3.down, out RaycastHit ground,
                .6f, ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(ground.point.y, Is.EqualTo(expectedTop).Within(.03f));

            PlayerCameraFollow follow = city.Camera.GetComponent<PlayerCameraFollow>();
            float previousPitch = follow.TargetOrbitPitch;
            bool cinematic = follow.CinematicMotionEnabled;
            float yawChange = Mathf.DeltaAngle(city.Camera.transform.eulerAngles.y, 90f);
            try
            {
                follow.SetCinematicMotionEnabled(false);
                follow.RotateYaw(yawChange);
                follow.Snap();
                yield return null;
                AssertCameraClearance(follow);
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityTerracePilot",
                    upper ? "live-08-upper-game-camera" : "live-07-underpass-game-camera", 1280, 720);
                if (!upper)
                {
                    Assert.That(Physics.Raycast(rootPosition + Vector3.up * 1.6f, Vector3.up, out RaycastHit roof,
                        plan.UpperCrossingWorld.y - rootPosition.y,
                        ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore), Is.True,
                        "The traversable lower floor must have a physical deck above it.");
                    Assert.That(roof.normal.y, Is.LessThan(-.9f));
                    follow.RotatePitch(follow.MaximumOrbitPitch - follow.TargetOrbitPitch);
                    follow.Snap();
                    yield return null;
                    AssertCameraClearance(follow);
                    Assert.That(city.Camera.transform.position.y + .2f, Is.LessThan(roof.point.y - .01f),
                        "The standard camera sphere must stay under the bridge while looking down.");
                }
            }
            finally
            {
                follow.RotateYaw(-yawChange);
                follow.RotatePitch(previousPitch - follow.TargetOrbitPitch);
                follow.SetCinematicMotionEnabled(cinematic);
                follow.Snap();
            }
        }

        private void AssertCameraClearance(PlayerCameraFollow follow)
        {
            Vector3 lens = city.Camera.transform.position;
            foreach (Collider hit in Physics.OverlapSphere(lens, .19f, follow.CollisionLayerMask, QueryTriggerInteraction.Ignore))
                Assert.That(hit.transform.IsChildOf(city.Player.GameObject.transform), Is.True,
                    "The ordinary camera entered " + hit.name + " at " + lens);
        }

        private void AssertDistrictSurfaces(CityTerraceDistrictPlan plan, Transform world)
        {
            foreach (CityTerracePilotFloor floor in plan.Floors)
            {
                Transform model = world.Find(floor.Id);
                Assert.That(model, Is.Not.Null, floor.Id);
                Bounds visible = LocalRendererBounds(model);
                Assert.That(visible.size.x, Is.EqualTo(floor.Size.x).Within(.02f));
                Assert.That(visible.size.z, Is.EqualTo(floor.Size.y).Within(.02f));
                Vector3 point = plan.World(floor.Center);
                Assert.That(Physics.Raycast(point + Vector3.up, Vector3.down, out RaycastHit hit,
                    1.2f, ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore), Is.True, floor.Id);
                Assert.That(hit.point.y, Is.EqualTo(point.y).Within(.01f), floor.Id + " physical top");
            }
            foreach (CityExteriorStairFlightDescriptor flight in plan.Flights)
            {
                Transform model = world.Find(flight.Id);
                Assert.That(model, Is.Not.Null);
                Assert.That(LocalRendererBounds(model).size.z, Is.EqualTo(flight.RunLength).Within(.02f));
                for (int step = 0; step < flight.StepCount; step += flight.StepCount - 1)
                {
                    Vector3 local = flight.Start + flight.Direction * ((step + .5f) * flight.TreadDepth) +
                        Vector3.up * ((step + 1) * flight.StepRise);
                    Vector3 point = plan.World(local);
                    Assert.That(Physics.Raycast(point + Vector3.up * .5f, Vector3.down, out RaycastHit hit,
                        .7f, 1 << FootProbeSurface.LayerIndex, QueryTriggerInteraction.Collide), Is.True);
                    Assert.That(hit.point.y, Is.EqualTo(point.y).Within(.005f), flight.Id + " world tread");
                }
            }
            foreach (CityResidentialCourtyardPropDescriptor prop in city.World.ResidentialCourtyards.Props)
            {
                var point = new Vector2(prop.Position.x, prop.Position.z);
                if (plan.ContainsFootprint(point) && plan.TrySampleStandingTop(point, 0f, out float top))
                    Assert.That(prop.Position.y, Is.GreaterThanOrEqualTo(top - .1f), prop.StableId + " remains buried in a terrace support.");
            }
        }

        private void AssertDistrictMap(CityTerraceDistrictPlan plan)
        {
            var ground = new CityMapCityTeleportGround(city.Layout);
            Vector3 upper = plan.UpperArrivalWorld;
            Assert.That(ground.TryClampArrival(upper + Vector3.up * PlayerFactory.GroundedRootOffset,
                out Vector3 arrival), Is.True);
            Assert.That(Vector3.Distance(arrival, upper + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.01f));
            foreach (Vector3 point in new[] { plan.LowerCrossingWorld, plan.UpperCrossingWorld })
            {
                Vector3 requested = point + Vector3.up * PlayerFactory.GroundedRootOffset;
                Assert.That(ground.TryClampArrival(requested + Vector3.up * .1f, out Vector3 clamped), Is.True);
                Assert.That(Vector3.Distance(clamped, requested), Is.LessThan(.01f),
                    "An authored arrival at a shared XZ must retain its own supported level.");
            }
            bool inspection = city.Map.MapPointInspectionEnabled;
            try
            {
                city.Map.SetMapPointInspectionEnabled(true);
                foreach (var expected in new[] {
                    (Id: "city:poi:terrace-courts", Position: arrival),
                    (Id: "city:poi:terrace-underpass", Position: plan.LowerCrossingWorld + Vector3.up * PlayerFactory.GroundedRootOffset) })
                {
                    int marker = -1;
                    for (int i = 0; i < city.Map.ActiveMapPoints.Count; i++)
                        if (city.Map.ActiveMapPoints[i].StableId == expected.Id) marker = i;
                    Assert.That(marker, Is.GreaterThanOrEqualTo(0), expected.Id);
                    Assert.That(city.Map.SelectMapPoint(marker), Is.True);
                    Assert.That(city.Map.TryGetSelectedMapPoint(out CityMapPointDescriptor _, out Vector3 selected), Is.True);
                    Assert.That(Vector3.Distance(selected, expected.Position), Is.LessThan(.01f), expected.Id);
                }
            }
            finally { city.Map.SetMapPointInspectionEnabled(inspection); }
        }

        private void AssertPedestrianLayerContacts(CityTerraceDistrictPlan terrace)
        {
            CityPedestrianPlan pedestrians = city.PedestrianPlan;
            Assert.That(pedestrians.Links.Count, Is.GreaterThan(0), "The neighbouring population graph must remain active.");
            int lowerNode = -1, upperNode = -1;
            Vector2 crossing = new Vector2(terrace.UpperCrossingWorld.x, terrace.UpperCrossingWorld.z);
            foreach (CityPedestrianLink link in pedestrians.Links)
            {
                Vector3 first = pedestrians.Nodes[link.FirstNodeIndex].Position;
                Vector3 last = pedestrians.Nodes[link.SecondNodeIndex].Position;
                if (link.Path == null && new Vector2(first.x - last.x, first.z - last.z).sqrMagnitude < .0001f)
                    Assert.That(Mathf.Abs(first.y - last.y), Is.LessThan(.28f), link.Id + " directly joins different storeys.");
                int segments = link.Path != null ? link.Path.Vertices.Count - 1 : 1;
                for (int segment = 0; segment < segments; segment++)
                {
                    Vector2 a = link.Path != null ? link.Path.Vertices[segment] : new Vector2(first.x, first.z);
                    Vector2 b = link.Path != null ? link.Path.Vertices[segment + 1] : new Vector2(last.x, last.z);
                    Rect span = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                        Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    if (!span.Overlaps(terrace.ReservedBounds) && !span.Overlaps(terrace.LowerPassBounds)) continue;
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / .1f));
                    float previousY = 0f;
                    bool previousOwned = false;
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        Vector2 point = Vector2.Lerp(a, b, sample / (float)samples);
                        if (!terrace.ContainsFootprint(point)) { previousOwned = false; continue; }
                        float amount = link.Path != null ? link.Path.Project(point).DistanceAlong / link.Path.Length
                            : sample / (float)samples;
                        float height = link.PathHeightSampler != null ? link.PathHeightSampler(point) : Mathf.Lerp(first.y, last.y, amount);
                        Vector3 standing = new Vector3(point.x, height, point.y);
                        Assert.That(terrace.TryGetSurfaceHeight(standing, out float top), Is.True,
                            link.Id + " selects no supported terrace layer at " + standing);
                        Assert.That(height, Is.EqualTo(top).Within(.08f), link.Id + " remains on replaced ground.");
                        if (previousOwned) Assert.That(Mathf.Abs(height - previousY), Is.LessThanOrEqualTo(.28f),
                            link.Id + " jumps between floors.");
                        previousY = height; previousOwned = true;
                        if (Vector2.Distance(point, crossing) > .15f) continue;
                        if (Mathf.Abs(height - terrace.LowerCrossingWorld.y) < .08f) lowerNode = link.FirstNodeIndex;
                        if (Mathf.Abs(height - terrace.UpperCrossingWorld.y) < .08f) upperNode = link.FirstNodeIndex;
                    }
                }
            }
            Assert.That(lowerNode, Is.GreaterThanOrEqualTo(0), "The NPC graph needs a supported lower route.");
            Assert.That(upperNode, Is.GreaterThanOrEqualTo(0), "The NPC graph needs a separate supported upper route.");
            Assert.That(upperNode, Is.Not.EqualTo(lowerNode), "Coincident XZ must not merge the two floors' nodes.");
            var pending = new Queue<int>();
            var visited = new HashSet<int> { lowerNode };
            pending.Enqueue(lowerNode);
            while (pending.Count > 0)
            {
                int node = pending.Dequeue();
                foreach (int linkIndex in pedestrians.GetLinkIndices(node))
                {
                    int other = pedestrians.Links[linkIndex].Other(node);
                    if (visited.Add(other)) pending.Enqueue(other);
                }
            }
            Assert.That(visited.Contains(upperNode), Is.True, "The two NPC levels must connect through the physical stair loop.");
        }

        private static IReadOnlyList<Vector3> DistrictRoute(CityTerraceDistrictPlan plan, IReadOnlyList<Vector3> local)
        {
            var route = new Vector3[local.Count];
            for (int i = 0; i < route.Length; i++) route[i] = plan.World(local[i]);
            return route;
        }

        private void CaptureCity(string name, Vector3 position, Vector3 target)
        {
            Camera camera = city.Camera;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool enabled = follow.enabled;
            Pose previous = new Pose(camera.transform.position, camera.transform.rotation);
            float previousFov = camera.fieldOfView;
            try
            {
                follow.enabled = false;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                camera.fieldOfView = 65f;
                AreaCaptureFixture.CaptureCurrentCamera(camera, "CityTerracePilot", name, 1280, 720);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previous.position, previous.rotation);
                camera.fieldOfView = previousFov;
                follow.enabled = enabled;
            }
        }

        private IEnumerator WalkRoute(IReadOnlyList<Vector3> route, bool reverse, string name, bool captureMiddle)
        {
            Debug.Log("Terrace pilot traversal started: " + name);
            PlayerMotor motor = root.Player.Motor;
            CharacterController capsule = root.Player.GameObject.GetComponent<CharacterController>();
            var presentation = (Player3DCharacterPresentation)root.Player.Visual;
            for (int waypoint = 0; waypoint < route.Count; waypoint++)
            {
                int index = reverse ? route.Count - 1 - waypoint : waypoint;
                Vector3 target = route[index] + Vector3.up * PlayerFactory.GroundedRootOffset;
                bool arrived = false;
                for (int frame = 0; frame < MaximumWaypointFrames && !arrived; frame++)
                {
                    Vector3 before = motor.transform.position;
                    // Keep Motor.Update enabled: its contextual-motion branch
                    // applies production gravity during the descent. Calling
                    // after Update leaves the gait sample for late rig solving.
                    arrived = motor.MoveTowardsApproachWaypoint(target, .035f, FrameSeconds);
                    yield return null;
                    Vector3 position = motor.transform.position;
                    Assert.That(Vector3.Distance(before, position), Is.LessThan(.35f),
                        name + " must not teleport across a stair or courtyard seam.");
                    Assert.That(root.WalkableArea.Contains(position, capsule.radius), Is.True,
                        name + " left the supported capsule corridor at " + position);
                    Assert.That(root.Plan.TryGetSurfaceHeight(position, out float ground), Is.True);
                    Assert.That(position.y - ground, Is.InRange(-.06f, .30f),
                        name + " lost its ramp/floor contact at " + position);
                    Assert.That(presentation.LeftFootGround.HasSurface || presentation.RightFootGround.HasSurface,
                        Is.True, name + " lost both boot ground probes.");
                }
                Assert.That(arrived, Is.True, name + " blocked at waypoint " + index +
                    ": " + motor.transform.position + " toward " + target);
                Assert.That(motor.transform.position.y, Is.EqualTo(target.y).Within(.20f),
                    name + " reached the XZ corner on the wrong surface.");
                if (captureMiddle && index == 7)
                    Capture("02-middle-court", new Vector3(-13.04f, 5.5f, 7.04f), new Vector3(-15f, 8f, 20f));
                if ((waypoint & 3) == 3) Debug.Log("Terrace pilot " + name + " reached corner " + index);
            }
            motor.CancelInteractionPoseMove();
            Debug.Log("Terrace pilot traversal completed: " + name);
        }

        private void AssertAuthoredSurfaces()
        {
            Transform world = root.World.transform;
            foreach (CityTerracePilotFloor floor in root.Plan.Floors)
            {
                Transform model = world.Find(floor.Id);
                Assert.That(model, Is.Not.Null, floor.Id);
                Bounds bounds = LocalRendererBounds(model);
                Assert.That(bounds.size.x, Is.EqualTo(floor.Size.x).Within(.02f), floor.Id + " imported X metres");
                Assert.That(bounds.size.z, Is.EqualTo(floor.Size.y).Within(.02f), floor.Id + " imported Z metres");
                Assert.That(bounds.max.y, Is.EqualTo(0f).Within(.01f), floor.Id + " visible top datum");
                Assert.That(Physics.Raycast(floor.Center + Vector3.up, Vector3.down, out RaycastHit hit,
                    1.2f, ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore), Is.True, floor.Id);
                Assert.That(hit.point.y, Is.EqualTo(floor.Center.y).Within(.01f), floor.Id + " physical top datum");
            }
            foreach (CityExteriorStairFlightDescriptor flight in root.Plan.Flights)
            {
                Transform model = world.Find(flight.Id);
                Assert.That(model, Is.Not.Null, flight.Id);
                Bounds bounds = LocalRendererBounds(model);
                Assert.That(bounds.size.x, Is.EqualTo(2.1f).Within(.02f), flight.Id + " imported stair/rail width");
                Assert.That(bounds.min.y, Is.EqualTo(-.25f).Within(.02f), flight.Id + " imported base");
                Assert.That(bounds.max.y, Is.EqualTo(3.03736f).Within(.025f), flight.Id + " visible upper rail");
                Assert.That(bounds.min.z, Is.EqualTo(0f).Within(.01f));
                Assert.That(bounds.max.z, Is.EqualTo(flight.RunLength).Within(.02f), flight.Id + " imported run");
                bool lowerAnchorFound = false, upperAnchorFound = false;
                foreach (Transform anchor in model.GetComponentsInChildren<Transform>())
                {
                    if (anchor.name == "ANCHOR_Lower")
                    {
                        lowerAnchorFound = true;
                        Assert.That(Vector3.Distance(anchor.position, flight.Start), Is.LessThan(.005f));
                    }
                    if (anchor.name == "ANCHOR_Upper")
                    {
                        upperAnchorFound = true;
                        Assert.That(Vector3.Distance(anchor.position, flight.End), Is.LessThan(.005f));
                    }
                }
                Assert.That(lowerAnchorFound && upperAnchorFound, Is.True, flight.Id + " authored endpoints are required.");
                for (int step = 0; step < flight.StepCount; step++)
                {
                    Vector3 tread = flight.Start + flight.Direction * ((step + .5f) * flight.TreadDepth) +
                        Vector3.up * ((step + 1) * flight.StepRise);
                    Assert.That(Physics.Raycast(tread + Vector3.up * .5f, Vector3.down, out RaycastHit probe,
                        .7f, 1 << FootProbeSurface.LayerIndex, QueryTriggerInteraction.Collide), Is.True,
                        flight.Id + " missing foot probe " + step);
                    Assert.That(probe.collider.isTrigger, Is.True);
                    Assert.That(probe.collider.transform.IsChildOf(world), Is.True);
                    Assert.That(probe.point.y, Is.EqualTo(tread.y).Within(.005f), flight.Id + " tread contact");
                    Assert.That(Physics.Raycast(tread + Vector3.up * .5f, Vector3.down, out RaycastHit ramp,
                        1f, ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(ramp.point.y, Is.EqualTo(tread.y - flight.StepRise * .5f).Within(.01f),
                        flight.Id + " continuous capsule ramp");
                }
            }
        }

        private void AssertBoundaryGuards()
        {
            IWalkableArea area = root.WalkableArea;
            Assert.That(area.Contains(new Vector3(float.NaN, 0f, 0f)), Is.False);
            Assert.That(area.Contains(new Vector3(0f, 2f, 8f), .32f), Is.False, "The two lower flights do not fill the central void.");
            Vector3 blocked = area.Constrain(new Vector3(-3f, 1f, 7f), new Vector3(3f, 1f, 7f), .32f);
            Assert.That(blocked.x, Is.LessThan(-2.4f), "A long move cannot hop between disconnected flights.");
            Vector3 smallGap = area.Constrain(new Vector3(9f, 3.84f, 15f), new Vector3(9f, 7.68f, 15.12f));
            Assert.That(smallGap.z, Is.LessThanOrEqualTo(15.0402f), "Point queries cannot cross the narrow inter-storey gap.");
            foreach (CityExteriorStairFlightDescriptor flight in root.Plan.Flights)
            {
                Assert.That(area.Contains(flight.Start, .32f), Is.True, flight.Id + " open lower seam");
                Assert.That(area.Contains(flight.End, .32f), Is.True, flight.Id + " open upper seam");
            }
            Assert.That(Physics.Raycast(new Vector3(4f, .6f, 0f), Vector3.back, out RaycastHit guard,
                5.2f, ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore), Is.True,
                "The lower court's exposed edge must also have a physical guard.");
            Assert.That(guard.collider.transform.IsChildOf(root.World.transform), Is.True);
            Assert.That(guard.point.z, Is.LessThan(-4.8f));
        }

        private void Capture(string name, Vector3 position, Vector3 target)
        {
            Camera camera = root.Camera;
            bool followEnabled = root.Follow.enabled;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                root.Follow.enabled = false;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
                camera.fieldOfView = 65f;
                AreaCaptureFixture.CaptureCurrentCamera(camera, "CityTerracePilot", name, 1280, 720);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                root.Follow.enabled = followEnabled;
            }
        }

        private static Bounds LocalRendererBounds(Transform rootTransform)
        {
            Renderer[] renderers = rootTransform.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0), rootTransform.name + " has no visible imported meshes.");
            Bounds result = default;
            bool first = true;
            foreach (Renderer renderer in renderers)
            {
                Bounds source = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = source.center + Vector3.Scale(source.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = rootTransform.InverseTransformPoint(renderer.transform.TransformPoint(local));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }
    }
}
