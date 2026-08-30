using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The end-to-end proof that was missing while four separate defects each
    /// broke ambient boarding on their own: the pedestrian navigation area
    /// instead of the road-inclusive one, the hero-only "seat opposite the
    /// driver" invariant, an unreachable transfer timeout, and a waiter that
    /// stopped its own bus short of the stop. Every one of them left the
    /// planners, the occupancy rules and the asset contracts passing, because
    /// nothing walked a passenger from the pavement into a seat and back out.
    /// </summary>
    public sealed class CityBusNpcPassengerPlayModeTests
    {
        private const float Step = 0.05f;

        // Budgets are game seconds, never frame counts. Time.deltaTime in a
        // batch run has been observed anywhere from 0.006 s to the 6.7 s
        // ceiling depending on how fast the frames come, so a frame budget is
        // meaningless; the frame cap is only a runaway guard. One lap of the
        // test loop is roughly 37 s: 100 m at 6 m/s plus two 10 s dwells.
        private const float PreloadSeconds = 200f;
        private const float WaiterSeconds = 150f;
        private const float BoardSeconds = 90f;
        private const float AlightSeconds = 200f;
        private const int FrameCap = 40000;

        [UnityTest]
        public IEnumerator AmbientPassenger_BoardsRidesAndAlightsAtALaterStop()
        {

            float previousTimeScale = Time.timeScale;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            GameObject root = null;
            CityBusDirector director = null;
            CityPedestrianDirector pedestrians = null;
            CityBusNpcPassengerController passengers = null;
            try
            {
                // One fixed step for every subsystem. Batch-mode frames come
                // at whatever rate the machine allows — Time.deltaTime was
                // seen at 0.006 s on one run and pinned to the 6.7 s ceiling
                // on another — and the bus, the walkers and the transfer
                // budget must all be measured against the same clock or a
                // hold expires under a passenger who is still walking.
                Time.timeScale = 1f;
                Time.captureDeltaTime = Step;
                root = new GameObject("City Bus NPC Passenger Root");
                var walkableArea = new AlwaysWalkableArea();
                CreateGround(root.transform);

                GameObject cameraObject = new GameObject("NPC Ride Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                GameObject uiObject = new GameObject("NPC Ride UI");
                uiObject.transform.SetParent(root.transform, false);
                InteractionPromptView prompt =
                    uiObject.AddComponent<InteractionPromptView>();

                // The hero stands far away: ambient waiters may only be
                // activated outright where the stop is already fog-hidden, and
                // an aboard hero would change the recycle and obstacle rules.
                PlayerRuntime player = PlayerFactory.Create(
                    root.transform,
                    new Vector3(0f, PlayerFactory.GroundedRootOffset, 300f),
                    camera,
                    walkableArea,
                    prompt);

                CityBusPlan route = CreateTwoStopRoute();
                Transform pool =
                    new GameObject("NPC Ride Bus Pool").transform;
                pool.SetParent(root.transform, false);
                CityBusAssetRegistry registry =
                    CityBusResources.Instantiate(pool);
                Assert.That(
                    registry,
                    Is.Not.Null,
                    "The production bus prefab must be available.");
                CityBusPresentation presentation =
                    registry.GetComponent<CityBusPresentation>();
                if (presentation == null)
                {
                    presentation = registry.gameObject
                        .AddComponent<CityBusPresentation>();
                }

                presentation.Initialize(registry);
                presentation.gameObject.SetActive(false);

                GameObject actorObject = new GameObject("NPC Ride Bus Actor");
                actorObject.layer = CityBusCollision.LayerIndex;
                actorObject.transform.SetParent(root.transform, false);
                CityBusActor actor =
                    actorObject.AddComponent<CityBusActor>();
                actor.Initialize(registry.LocalBounds, registry.Dimensions);

                director = root.AddComponent<CityBusDirector>();
                director.Initialize(
                    route,
                    actor,
                    presentation,
                    player.GameObject.transform,
                    null,
                    pool,
                    () => 0f);
                director.enabled = false;

                actor.PrepareSpawn(route, route.SpawnAnchors[0], 0x42555350u);
                actor.BindPresentation(presentation);
                AdvanceActorUntil(
                    actor,
                    () => actor.ServiceOrdinal == 1 && actor.DoorsFullyOpen);
                Assert.That(
                    actor.DoorsFullyOpen,
                    Is.True,
                    "The bus must reach its first stop with open doors.");

                // Take the wait slot from the real door dock rather than
                // guessing a side, then build a pedestrian graph beside it.
                Assert.That(
                    CityBusRidePlan.TryCreate(
                        actor,
                        walkableArea,
                        new Vector3(
                            0f,
                            CityBusNpcPassengerController
                                .PassengerPelvisHeight,
                            0f),
                        CityPedestrianPlanner.AgentRadius,
                        CityBusPassengerDoor.Front,
                        null,
                        CityBusActor.NpcSeatIndices[0],
                        0f,
                        false,
                        out CityBusRidePlan probe),
                    Is.True,
                    "An ambient seat must produce a transfer plan; requiring " +
                    "the hero's opposite-driver side here rejects most of " +
                    "the cabin.");

                Vector3 waitSlot = probe.EntryPose.RootPosition;
                Vector3 along = actor.transform.forward;
                CityPedestrianPlan pedestrianPlan =
                    CreatePedestrianPlan(waitSlot, along);
                pedestrians = CityPedestrianFactory.Create(
                    root.transform,
                    pedestrianPlan,
                    player.GameObject.transform,
                    walkableArea,
                    CityPedestrianPopulationProfile.City,
                    () => false);

                var waitPlan = new CityBusStopWaitPlan(new[]
                {
                    new CityBusStopWaitPoint(
                        0,
                        route.Stops[0].Id,
                        0,
                        -along,
                        new[] { waitSlot },
                        new[] { 0f, 4f, 8f })
                });
                passengers = CityBusNpcPassengerController.Create(
                    director,
                    pedestrians,
                    waitPlan,
                    walkableArea,
                    null,
                    player.GameObject.transform,
                    0x42555350,
                    () => false);

                // Every subsystem runs on the same clock: the pedestrian and
                // passenger directors advance themselves from LateUpdate, and
                // only the bus actor needs driving because its own director is
                // switched off. Disabling either director would call its
                // OnDisable and shut it down for good.
                Assert.That(passengers.IsInitialized, Is.True);

                // The bus was already running when the controller appeared, so
                // it seats its spawn preload first. Let that clear, otherwise
                // a full cabin legitimately refuses the waiter under test.
                // One frame first, so the controller's own LateUpdate can
                // seat its spawn preload before the wait for it to clear.
                yield return null;
                float elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < PreloadSeconds &&
                     (actor.NpcOccupantCount != 0 ||
                      passengers.PassengerCount != 0);
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Assert.That(
                    actor.NpcOccupantCount,
                    Is.Zero,
                    "Preloaded passengers must leave at their own stop. " +
                    Describe(actor, passengers));

                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < WaiterSeconds &&
                     FindWaitingWalker(pedestrians) == null;
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                CityPedestrianActor walker = FindWaitingWalker(pedestrians);
                Assert.That(
                    walker,
                    Is.Not.Null,
                    "A stop beyond the fog band must receive a waiter. " +
                    Describe(actor, passengers));

                CityPedestrianArchetype archetype =
                    CityPedestrianDirector.GetActorArchetype(walker);
                Assert.That(archetype, Is.Not.Null);
                Assert.That(
                    archetype.CanRideBus,
                    Is.True,
                    "Only a design that declares a seated ride may wait.");

                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < BoardSeconds &&
                     walker.MotionState !=
                         CityPedestrianMotionState.Riding;
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Assert.That(
                    walker.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Riding),
                    "The waiter never reached a seat. Every ambient boarding " +
                    "defect so far has ended exactly here while the planners " +
                    "and asset contracts stayed green. " +
                    Describe(actor, passengers) +
                    $" walker={walker.MotionState}");
                Assert.That(actor.NpcOccupantCount, Is.EqualTo(1));
                Assert.That(actor.HasPlayerPassenger, Is.False);
                Assert.That(
                    actor.IsSeatOccupied(CityBusActor.PlayerSeatIndex),
                    Is.False,
                    "Seat 07 stays reserved for the hero.");
                Assert.That(
                    CountOccupiedAmbientSeats(actor),
                    Is.EqualTo(1),
                    "The passenger holds exactly one ambient seat.");
                Assert.That(
                    walker.Presentation.IsSeated,
                    Is.True,
                    "A riding walker plays its authored seated loop.");
                Assert.That(
                    IsInsideCabin(actor, registry, walker.Position),
                    Is.True,
                    "A seated passenger must be inside the body it rides in.");

                int boardedOrdinal = actor.ServiceOrdinal;
                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < AlightSeconds &&
                     (walker.IsAttachedToVehicle ||
                      actor.NpcOccupantCount != 0);
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.That(
                    walker.IsAttachedToVehicle,
                    Is.False,
                    "The passenger never got off again. " +
                    Describe(actor, passengers) +
                    $" walker={walker.MotionState}");
                Assert.That(
                    actor.ServiceOrdinal,
                    Is.GreaterThan(boardedOrdinal),
                    "A passenger may only leave at a later stop.");
                Assert.That(
                    walker.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking),
                    "An alighted walker rejoins ordinary roaming.");
                Assert.That(
                    walker.Presentation == null ||
                    !walker.Presentation.IsSeated,
                    Is.True);
                Assert.That(
                    IsInsideCabin(actor, registry, walker.Position),
                    Is.False,
                    "An alighted walker stands outside the bus.");
                Assert.That(
                    actor.HasServiceHold,
                    Is.False,
                    "A finished transfer hands its dwell hold back; a leaked " +
                    "one seals the doors at every later stop.");
            }
            finally
            {
                if (passengers != null)
                {
                    passengers.Shutdown();
                }

                if (pedestrians != null)
                {
                    pedestrians.Shutdown();
                }

                if (director != null)
                {
                    director.Shutdown();
                }

                if (root != null)
                {
                    Object.Destroy(root);
                }

                Time.captureDeltaTime = previousCaptureDeltaTime;
                Time.timeScale = previousTimeScale;
            }
        }

        private static string Describe(
            CityBusActor actor,
            CityBusNpcPassengerController passengers)
        {
            return $"[bus {actor.MotionState} stop={actor.CurrentStopIndex} " +
                   $"ordinal={actor.ServiceOrdinal} " +
                   $"doorsOpen={actor.DoorsFullyOpen} " +
                   $"hold={actor.HasServiceHold} " +
                   $"npc={actor.NpcOccupantCount} | tracked=" +
                   $"{passengers.TrackedCount} waiting={passengers.WaiterCount}" +
                   $" aboard={passengers.PassengerCount} dt={Time.deltaTime:F3}]";
        }

        private static CityPedestrianActor FindWaitingWalker(
            CityPedestrianDirector pedestrians)
        {
            IReadOnlyList<CityPedestrianActor> actors = pedestrians.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor candidate = actors[index];
                if (candidate.IsSpawned &&
                    (candidate.MotionState ==
                         CityPedestrianMotionState.WaitingAtStop ||
                     candidate.MotionState ==
                         CityPedestrianMotionState.ApproachingStop))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// The controller owns its passenger records privately, so occupancy
        /// is read back through the cabin itself rather than through a handle
        /// a test has no business holding.
        /// </summary>
        private static int CountOccupiedAmbientSeats(CityBusActor actor)
        {
            int count = 0;
            IReadOnlyList<int> seats = CityBusActor.NpcSeatIndices;
            for (int index = 0; index < seats.Count; index++)
            {
                if (actor.IsSeatOccupied(seats[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsInsideCabin(
            CityBusActor actor,
            CityBusAssetRegistry registry,
            Vector3 position)
        {
            Vector3 local = actor.transform.InverseTransformPoint(position);
            Bounds bounds = registry.LocalBounds;
            return Mathf.Abs(local.x) <= bounds.extents.x &&
                   Mathf.Abs(local.z) <= bounds.extents.z;
        }

        private static void AdvanceActorUntil(
            CityBusActor actor,
            System.Func<bool> predicate)
        {
            for (int guard = 0; guard < 4000 && !predicate(); guard++)
            {
                actor.Advance(Step, CityBusObstacleState.Clear, 0f);
            }
        }

        private static CityPedestrianPlan CreatePedestrianPlan(
            Vector3 slot,
            Vector3 along)
        {
            Vector3 planar = new Vector3(along.x, 0f, along.z).normalized;
            var nodes = new List<CityPedestrianNode>
            {
                new CityPedestrianNode("npc-stop-node", slot, false),
                new CityPedestrianNode(
                    "npc-lane-node",
                    slot + (planar * 6f),
                    false),
                new CityPedestrianNode(
                    "npc-far-node",
                    slot + (planar * 12f),
                    false)
            };
            var links = new List<CityPedestrianLink>
            {
                new CityPedestrianLink(
                    "npc-link-a",
                    0,
                    1,
                    CityPedestrianLinkKind.Sidewalk),
                new CityPedestrianLink(
                    "npc-link-b",
                    1,
                    2,
                    CityPedestrianLinkKind.Sidewalk)
            };
            var anchors = new List<CityPedestrianSpawnAnchor>
            {
                new CityPedestrianSpawnAnchor(
                    "npc-anchor",
                    slot + (planar * 6f),
                    0,
                    1)
            };
            var rectangles = new List<Rect>
            {
                Rect.MinMaxRect(
                    slot.x - 60f,
                    slot.z - 60f,
                    slot.x + 60f,
                    slot.z + 60f)
            };
            return new CityPedestrianPlan(
                91,
                37,
                0x50454431u,
                CityPedestrianPlanner.AgentRadius,
                nodes,
                links,
                anchors,
                rectangles);
        }

        private static CityBusPlan CreateTwoStopRoute()
        {
            Vector3 start = new Vector3(
                0f,
                CityStreetSurfacePlanner.RoadTop,
                0f);
            Vector3 east = start + (Vector3.right * 30f);
            Vector3 southEast = east + (Vector3.back * 20f);
            Vector3 southWest = southEast + (Vector3.left * 30f);
            var edge = new RoadEdge(
                new Vector2Int(0, 0),
                new Vector2Int(0, 1));
            var samples = new List<CityBusPathSample>
            {
                new CityBusPathSample(start, Vector3.right, 0f),
                new CityBusPathSample(east, Vector3.back, 30f),
                new CityBusPathSample(southEast, Vector3.left, 50f),
                new CityBusPathSample(southWest, Vector3.forward, 80f),
                new CityBusPathSample(start, Vector3.right, 100f)
            };
            var clearance = new CityBusClearanceResult(
                true,
                CityBusClearanceFailureKind.None,
                -1,
                default,
                CityBusDesignVehicle.Default.ClearanceMargin);
            var nodes = new List<CityBusRouteNode>
            {
                new CityBusRouteNode(
                    "npc-ride-node",
                    start,
                    Vector3.right,
                    edge,
                    edge.A,
                    edge.B,
                    new[] { 0 })
            };
            var links = new List<CityBusRouteLink>
            {
                new CityBusRouteLink(
                    "npc-ride-link",
                    0,
                    0,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    samples,
                    float.PositiveInfinity,
                    clearance)
            };
            var anchors = new List<CityBusSpawnAnchor>
            {
                new CityBusSpawnAnchor(
                    "npc-ride-anchor",
                    0,
                    0f,
                    start,
                    Vector3.right,
                    edge)
            };
            var stops = new List<CityBusStopDescriptor>
            {
                CreateStop("npc-ride-stop-a", start, 8f, edge),
                CreateStop("npc-ride-stop-b", start, 48f, edge)
            };
            return new CityBusPlan(
                91,
                37,
                0x42555332u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                anchors,
                stops,
                new List<CityBusClearanceFailure>(),
                1,
                1);
        }

        private static CityBusStopDescriptor CreateStop(
            string id,
            Vector3 start,
            float distance,
            RoadEdge edge)
        {
            Vector3 position = distance <= 30f
                ? start + (Vector3.right * distance)
                : start + (Vector3.right * 30f) +
                  (Vector3.back * (distance - 30f));
            Vector3 forward = distance <= 30f
                ? Vector3.right
                : Vector3.back;
            return new CityBusStopDescriptor(
                id,
                $"{id}-shelter",
                position + (Vector3.forward * 3f),
                0,
                distance,
                position,
                forward,
                edge);
        }

        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ground.name = "NPC Ride Test Ground";
            ground.transform.SetParent(parent, false);
            // Its TOP must be the height this scene actually lives at, and
            // that is the ROAD, not the sidewalk. Everything synthetic here
            // - the route, the bus, and therefore every door dock derived
            // from it - is built on CityStreetSurfacePlanner.RoadTop, while
            // this slab used to be raised to SidewalkTop. Six centimetres of
            // difference buried every dock, and the pedestrian director's
            // spawn-clearance capsule correctly refused to materialise a
            // waiter inside terrain: its lowest point sat 12 mm under the
            // slab. That single line is why this test "failed on any code"
            // for as long as it existed. In the real city the road and the
            // pavement are separate meshes at their own heights, so nothing
            // there was ever wrong.
            ground.transform.position = new Vector3(
                10f,
                CityStreetSurfacePlanner.RoadTop * 0.5f,
                -10f);
            ground.transform.localScale = new Vector3(
                160f,
                CityStreetSurfacePlanner.RoadTop,
                160f);
        }

        private sealed class AlwaysWalkableArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f)
            {
                return true;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                return desiredPosition;
            }

            public Vector3 ClosestPoint(
                Vector3 position,
                float radius = 0f)
            {
                return position;
            }
        }
    }
}
