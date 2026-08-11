using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBusRuntimeTests
    {
        private const float VisibleEncounterDistance = 24f;

        [Test]
        public void SamePlanAndAdvanceSequence_RepeatsBusLifecycle()
        {
            CityBusPlan plan = CreateDirectedApproachPlan(
                80.4f,
                true);
            RuntimeFixture first = null;
            RuntimeFixture second = null;
            try
            {
                first = RuntimeFixture.Create("First", plan);
                second = RuntimeFixture.Create("Second", plan);

                Assert.That(
                    first.Director.TimeUntilNextSpawn,
                    Is.EqualTo(second.Director.TimeUntilNextSpawn)
                        .Within(0.000001f));

                float initialDelay =
                    first.Director.TimeUntilNextSpawn + 0.01f;
                first.Director.Advance(initialDelay);
                second.Director.Advance(initialDelay);

                Assert.That(first.Director.ActiveCount, Is.EqualTo(1));
                Assert.That(second.Director.ActiveCount, Is.EqualTo(1));
                Assert.That(first.Actor.EngineAudioSource.clip, Is.Not.Null);
                Assert.That(first.Actor.EngineAudioSource.loop, Is.True);
                Assert.That(
                    first.Actor.EngineAudioSource.spatialBlend,
                    Is.EqualTo(1f));
                Assert.That(
                    first.Actor.EngineAudioSource.volume,
                    Is.EqualTo(CityBusActor.EngineIdleVolume)
                        .Within(0.0001f));
                Assert.That(
                    first.Actor.SpawnAnchorId,
                    Is.EqualTo(second.Actor.SpawnAnchorId));
                Assert.That(first.Director.ActiveCount, Is.LessThanOrEqualTo(1));

                bool observedOpenDoor = false;
                for (int step = 0; step < 120; step++)
                {
                    first.Director.Advance(0.25f);
                    second.Director.Advance(0.25f);

                    Assert.That(
                        first.Actor.MotionState,
                        Is.EqualTo(second.Actor.MotionState));
                    Assert.That(
                        first.Actor.DistanceAlongLink,
                        Is.EqualTo(second.Actor.DistanceAlongLink)
                            .Within(0.0001f));
                    Assert.That(
                        first.Actor.Speed,
                        Is.EqualTo(second.Actor.Speed).Within(0.0001f));
                    Assert.That(
                        first.Presentation.DoorOpenness,
                        Is.EqualTo(second.Presentation.DoorOpenness)
                            .Within(0.0001f));
                    Assert.That(
                        first.Actor.DwellCount,
                        Is.EqualTo(second.Actor.DwellCount));
                    observedOpenDoor |=
                        first.Presentation.DoorOpenness > 0.01f;
                }

                Assert.That(observedOpenDoor, Is.True);
                Assert.That(first.Actor.DwellCount, Is.EqualTo(1),
                    "The first partial loop must not serve the same stop " +
                    "twice before the route wraps.");
                first.Director.Shutdown();
                Assert.That(first.Director.ActiveCount, Is.Zero);
                Assert.That(first.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dormant));
                Assert.That(first.Actor.BodyCollider.enabled, Is.False);
                Assert.That(first.Presentation.DoorOpenness, Is.Zero);
                Assert.That(first.Presentation.NightFactor, Is.Zero);
                Assert.That(first.Presentation.BrakeFactor, Is.Zero);
                Assert.That(first.Actor.EngineAudioSource.isPlaying, Is.False);
                Assert.That(first.Actor.EngineAudioSource.volume, Is.Zero);
                Assert.That(
                    first.Actor.EngineAudioSource.pitch,
                    Is.EqualTo(CityBusActor.EngineIdlePitch)
                        .Within(0.0001f));
            }
            finally
            {
                first?.Destroy();
                second?.Destroy();
            }
        }

        [Test]
        public void RemoteCycleWithoutEncounter_DoesNotSpawnInvisibleBus()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Remote Cycle",
                    CreateCyclicPlan(false));

                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(
                    fixture.Director.ActiveCount,
                    Is.Zero,
                    "A fog-hidden anchor is not a valid candidate when " +
                    "its directed route never reaches the 24 m initial-" +
                    "approach area around the player.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void FallbackBand_SpawnsWhenPreferredBandHasNoAnchor()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Fallback Band",
                    CreateDirectedApproachPlan(
                        60.4f,
                        false,
                        70.4f));
                CityBusSpawnAnchor anchor =
                    fixture.Director.Plan.SpawnAnchors[0];
                Quaternion rotation = Quaternion.LookRotation(
                    anchor.Forward,
                    Vector3.up);
                float bodyDistance =
                    CityBusActor.GetClosestPlanarBodyDistance(
                        fixture.Player.position,
                        anchor.Position,
                        rotation,
                        fixture.Actor.LocalVisualBounds);
                Assert.That(
                    bodyDistance,
                    Is.GreaterThanOrEqualTo(
                        CityBusDirector.FogHiddenDistance));
                Assert.That(
                    bodyDistance,
                    Is.LessThan(CityBusDirector.MinimumSpawnDistance));

                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(
                    fixture.Director.ActiveCount,
                    Is.EqualTo(1),
                    "A connected fog-hidden anchor must be used as a " +
                    "fallback when the preferred 76-86 m band is empty.");
                Assert.That(
                    fixture.Actor.GetClosestPlanarBodyDistance(
                        fixture.Player.position),
                    Is.LessThan(CityBusDirector.MinimumSpawnDistance),
                    "The actual spawned pose must come from the fallback " +
                    "band, not another preferred-band sample on the route.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void JunctionOnlyStraightRing_DoesNotProvideSpawnPose()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Junction Only",
                    CreateDirectedApproachPlan(
                        80.4f,
                        false,
                        100.4f,
                        true));

                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(
                    fixture.Director.ActiveCount,
                    Is.Zero,
                    "Runtime sampling must not treat a straight junction " +
                    "maneuver as a road-segment spawn anchor.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void SpawnCandidate_LeavesNormalApproachBeforeNextStop()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Stop Approach",
                    CreateDirectedApproachPlan(
                        80.4f,
                        true,
                        100.4f,
                        false,
                        true));
                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(fixture.Director.ActiveCount, Is.EqualTo(1));
                CityBusStopDescriptor stop =
                    fixture.Director.Plan.Stops[0];
                float forwardStopDistance =
                    GetForwardLoopDistance(
                        fixture.Director.Plan,
                        fixture.Actor.CurrentLinkIndex,
                        fixture.Actor.DistanceAlongLink,
                        stop.LinkIndex,
                        stop.DistanceAlongLink);
                float longitudinalExtent =
                    Mathf.Abs(
                        fixture.Actor.LocalVisualBounds.center.z) +
                    fixture.Actor.LocalVisualBounds.extents.z;
                float minimumServiceApproach =
                    ((CityBusActor.CruiseSpeed *
                      CityBusActor.CruiseSpeed) /
                     (2f * CityBusActor.ServiceDeceleration)) +
                    CityBusActor.ObstacleStopPadding +
                    longitudinalExtent;

                Assert.That(
                    forwardStopDistance,
                    Is.GreaterThanOrEqualTo(
                        minimumServiceApproach - 0.001f),
                    "A runtime-sampled spawn must not make the bus dwell " +
                    "immediately at a hidden stop.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void DirectedAwayApproach_RemainsActiveUntilVisibleEncounter()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Directed Away",
                    CreateDirectedApproachPlan(80.4f, false));
                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);
                Assert.That(fixture.Director.ActiveCount, Is.EqualTo(1));

                bool travelledBeyondOrdinaryRecycleDistance = false;
                bool reachedVisibleEncounter = false;
                float minimumBodyDistance = float.PositiveInfinity;
                for (int step = 0; step < 360; step++)
                {
                    fixture.Director.Advance(0.25f);
                    Assert.That(
                        fixture.Director.ActiveCount,
                        Is.EqualTo(1),
                        "A directed approach must not recycle before its " +
                        $"first encounter; nearest body distance was " +
                        $"{minimumBodyDistance:F2} m.");

                    float bodyDistance =
                        fixture.Actor.GetClosestPlanarBodyDistance(
                            fixture.Player.position);
                    minimumBodyDistance = Mathf.Min(
                        minimumBodyDistance,
                        bodyDistance);
                    travelledBeyondOrdinaryRecycleDistance |=
                        bodyDistance >= CityBusDirector.RecycleDistance;
                    if (bodyDistance <= VisibleEncounterDistance)
                    {
                        reachedVisibleEncounter = true;
                        break;
                    }
                }

                Assert.That(
                    travelledBeyondOrdinaryRecycleDistance,
                    Is.True,
                    "The fixture must exercise the directed-away recycle " +
                    "boundary before turning toward the player.");
                Assert.That(
                    reachedVisibleEncounter,
                    Is.True,
                    $"The successful lifecycle stayed outside the " +
                    $"{VisibleEncounterDistance:F0} m visible encounter " +
                    $"range; nearest body distance was " +
                    $"{minimumBodyDistance:F2} m.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void FixedRing_RouteSequenceDoesNotDependOnBehaviorSeed()
        {
            RuntimeFixture first = null;
            RuntimeFixture second = null;
            try
            {
                CityBusPlan plan = CreateDirectedApproachPlan(
                    80.4f,
                    false);
                first = RuntimeFixture.Create("Route Seed A", plan);
                second = RuntimeFixture.Create("Route Seed B", plan);
                first.SpawnDirectly(0x11111111u);
                second.SpawnDirectly(0xEEEEEEEEu);

                for (int step = 0; step < 600; step++)
                {
                    first.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);
                    second.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);

                    Assert.That(
                        first.Actor.CurrentLinkIndex,
                        Is.EqualTo(second.Actor.CurrentLinkIndex));
                    Assert.That(
                        first.Actor.DistanceAlongLink,
                        Is.EqualTo(second.Actor.DistanceAlongLink)
                            .Within(0.0001f));
                    Assert.That(
                        first.Actor.Position,
                        Is.EqualTo(second.Actor.Position));
                }
            }
            finally
            {
                first?.Destroy();
                second?.Destroy();
            }
        }

        [Test]
        public void FixedRing_ServesStopAgainOnEveryCompletedLoop()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Repeated Loop Stop",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                for (int step = 0; step < 400; step++)
                {
                    fixture.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    fixture.Actor.DwellCount,
                    Is.GreaterThanOrEqualTo(2),
                    "A fixed service stop must be re-enabled after the " +
                    "ordered route wraps, rather than remaining served " +
                    "for the complete pooled spawn.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void Yielding_StopsBeforeFiniteClearance_AndResumesWithoutCamera()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Safety",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();
                Assert.That(fixture.Director.ActiveCount, Is.EqualTo(1));

                for (int step = 0; step < 8; step++)
                {
                    fixture.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(fixture.Actor.Speed, Is.GreaterThan(0f));
                float positionBeforeYield =
                    fixture.Actor.DistanceAlongLink;
                fixture.Actor.Advance(
                    0.5f,
                    new CityBusObstacleState(true, 0.20f),
                    0f);

                Assert.That(fixture.Actor.IsYielding, Is.True);
                Assert.That(fixture.Actor.Speed, Is.Zero);
                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.EqualTo(positionBeforeYield).Within(0.0001f),
                    "The body must not consume the safety padding.");

                fixture.Actor.Advance(
                    1f,
                    CityBusObstacleState.Clear,
                    0f);
                Assert.That(fixture.Actor.IsYielding, Is.False);
                Assert.That(fixture.Actor.Speed, Is.GreaterThan(0f));
                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.GreaterThan(positionBeforeYield));
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void ObstacleProbe_IgnoresBalconyHeightSeparation()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Vertical Safety",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();
                Vector3 streetTarget =
                    fixture.Actor.Position + (Vector3.right * 6f);

                Assert.That(
                    fixture.Actor.TryGetPathObstacleClearance(
                        streetTarget,
                        0.35f,
                        12f,
                        out _),
                    Is.True);
                Assert.That(
                    fixture.Actor.TryGetPathObstacleClearance(
                        streetTarget + (Vector3.up *
                            PlayerHomeBalconyGeometry
                                .ApartmentFloorElevation),
                        0.35f,
                        12f,
                        out _),
                    Is.False);
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void ObstacleBeyondOrderedLinkBoundary_BrakesBeforeSeam()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Boundary Safety",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();
                fixture.Actor.Advance(
                    10f,
                    CityBusObstacleState.Clear,
                    0f);
                fixture.Actor.Advance(
                    1.3f,
                    CityBusObstacleState.Clear,
                    0f);

                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.InRange(37f, 39.9f));
                Vector3 targetOnNextTraversal =
                    new Vector3(2f, 0.08f, 80.4f);
                Assert.That(
                    fixture.Actor.TryGetPathObstacleClearance(
                        targetOnNextTraversal,
                        0.35f,
                        8f,
                        out float clearance),
                    Is.True);
                Assert.That(clearance, Is.LessThan(8f));

                float distanceBeforeBraking =
                    fixture.Actor.DistanceAlongLink;
                fixture.Actor.Advance(
                    0.25f,
                    new CityBusObstacleState(true, clearance),
                    0f);

                Assert.That(fixture.Actor.IsBraking, Is.True);
                Assert.That(fixture.Actor.IsYielding, Is.True);
                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.EqualTo(distanceBeforeBraking).Within(0.0001f),
                    "The bus must brake on the current link instead of " +
                    "crossing its ordered seam into the obstacle.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void PlanWithoutSpawnAnchors_IsOperationallyEmpty()
        {
            CityBusPlan plan = CreateCyclicPlan(
                includeStop: false,
                includeAnchor: false);

            Assert.That(plan.Nodes, Is.Not.Empty);
            Assert.That(plan.Links, Is.Not.Empty);
            Assert.That(plan.SpawnAnchors, Is.Empty);
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void CollisionPolicy_KeepsPlayerAndPedestriansSolid()
        {
            Assert.That(
                LayerMask.NameToLayer(CityBusCollision.LayerName),
                Is.EqualTo(CityBusCollision.LayerIndex));

            CityBusCollision.EnsureRuntimePolicy();

            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityBusCollision.DefaultLayerIndex,
                    CityBusCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.LayerIndex,
                    CityBusCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityBusCollision.LayerIndex,
                    CityBusCollision.LayerIndex),
                Is.True);
        }

        private static CityBusPlan CreateCyclicPlan(
            bool includeStop = true,
            bool includeAnchor = true)
        {
            Vector3 start = new Vector3(0f, 0.08f, 80.4f);
            Vector3 cornerOne = start + (Vector3.right * 10f);
            Vector3 cornerTwo = cornerOne + (Vector3.back * 10f);
            Vector3 cornerThree = cornerTwo + (Vector3.left * 10f);
            RoadEdge edge = new RoadEdge(
                new Vector2Int(0, 0),
                new Vector2Int(0, 1));
            var samples = new List<CityBusPathSample>
            {
                new CityBusPathSample(start, Vector3.right, 0f),
                new CityBusPathSample(cornerOne, Vector3.back, 10f),
                new CityBusPathSample(cornerTwo, Vector3.left, 20f),
                new CityBusPathSample(cornerThree, Vector3.forward, 30f),
                new CityBusPathSample(start, Vector3.right, 40f)
            };
            CityBusClearanceResult clearance =
                new CityBusClearanceResult(
                    true,
                    CityBusClearanceFailureKind.None,
                    -1,
                    default,
                    CityBusDesignVehicle.Default.ClearanceMargin);
            var nodes = new List<CityBusRouteNode>
            {
                new CityBusRouteNode(
                    "node",
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
                    "link",
                    0,
                    0,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    samples,
                    float.PositiveInfinity,
                    clearance)
            };
            var anchors = new List<CityBusSpawnAnchor>();
            if (includeAnchor)
            {
                anchors.Add(new CityBusSpawnAnchor(
                    "anchor",
                    0,
                    0f,
                    start,
                    Vector3.right,
                    edge));
            }
            var stops = new List<CityBusStopDescriptor>();
            if (includeStop)
            {
                stops.Add(new CityBusStopDescriptor(
                    "stop",
                    "shelter",
                    start + (Vector3.right * 5f) +
                    (Vector3.forward * 3f),
                    0,
                    5f,
                    start + (Vector3.right * 5f),
                    Vector3.right,
                    edge));
            }
            return new CityBusPlan(
                11,
                17,
                0x42555331u,
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

        private static CityBusPlan CreateDirectedApproachPlan(
            float anchorZ,
            bool includeStop,
            float farZ = 100.4f,
            bool markEveryLinkAsJunction = false,
            bool placeStopAtSpawnBand = false)
        {
            Vector3 anchor = new Vector3(0f, 0.08f, anchorZ);
            Vector3 farNorth = new Vector3(0f, 0.08f, farZ);
            Vector3 farEast = new Vector3(20f, 0.08f, farZ);
            Vector3 nearEast = new Vector3(20f, 0.08f, 20f);
            Vector3 nearWest = new Vector3(0f, 0.08f, 20f);
            Vector3[] points =
            {
                anchor,
                farNorth,
                farEast,
                nearEast,
                nearWest
            };
            var nodeEdges = new RoadEdge[points.Length];
            for (int index = 0; index < nodeEdges.Length; index++)
            {
                int edgeColumn = markEveryLinkAsJunction
                    ? index
                    : index <= 1
                        ? 0
                        : index - 1;
                nodeEdges[index] = new RoadEdge(
                    new Vector2Int(edgeColumn, 0),
                    new Vector2Int(edgeColumn, 1));
            }

            RoadEdge edge = nodeEdges[0];
            CityBusClearanceResult clearance =
                new CityBusClearanceResult(
                    true,
                    CityBusClearanceFailureKind.None,
                    -1,
                    default,
                    CityBusDesignVehicle.Default.ClearanceMargin);
            var links = new List<CityBusRouteLink>(points.Length);
            var nodes = new List<CityBusRouteNode>(points.Length);
            for (int index = 0; index < points.Length; index++)
            {
                int next = (index + 1) % points.Length;
                Vector3 forward =
                    (points[next] - points[index]).normalized;
                float length = Vector3.Distance(
                    points[index],
                    points[next]);
                links.Add(new CityBusRouteLink(
                    "approach-link:" + index,
                    index,
                    next,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    new[]
                    {
                        new CityBusPathSample(
                            points[index],
                            forward,
                            0f),
                        new CityBusPathSample(
                            points[next],
                            forward,
                            length)
                    },
                    float.PositiveInfinity,
                    clearance));
                nodes.Add(new CityBusRouteNode(
                    "approach-node:" + index,
                    points[index],
                    forward,
                    nodeEdges[index],
                    nodeEdges[index].A,
                    nodeEdges[index].B,
                    new[] { index }));
            }

            var stops = new List<CityBusStopDescriptor>();
            if (includeStop)
            {
                const int stopLinkIndex = 2;
                float stopDistance = placeStopAtSpawnBand ? 24f : 40f;
                int stopTargetIndex =
                    (stopLinkIndex + 1) % points.Length;
                Vector3 stopForward =
                    (points[stopTargetIndex] -
                     points[stopLinkIndex]).normalized;
                Vector3 stopPosition =
                    points[stopLinkIndex] +
                    (stopForward * stopDistance);
                stops.Add(new CityBusStopDescriptor(
                    "approach-stop",
                    "shelter",
                    stopPosition + (Vector3.forward * 3f),
                    stopLinkIndex,
                    stopDistance,
                    stopPosition,
                    stopForward,
                    edge));
            }

            return new CityBusPlan(
                11,
                17,
                0x42555332u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                new List<CityBusSpawnAnchor>
                {
                    new CityBusSpawnAnchor(
                        "approach-anchor",
                        0,
                        0f,
                        anchor,
                        Vector3.forward,
                        edge)
                },
                stops,
                new List<CityBusClearanceFailure>(),
                points.Length,
                links.Count);
        }

        private static float GetForwardLoopDistance(
            CityBusPlan plan,
            int fromLinkIndex,
            float fromDistanceAlongLink,
            int toLinkIndex,
            float toDistanceAlongLink)
        {
            float from = float.NaN;
            float to = float.NaN;
            float loopDistance = 0f;
            for (int index = 0;
                 index < plan.OrderedLinkIndices.Count;
                 index++)
            {
                int linkIndex = plan.OrderedLinkIndices[index];
                if (linkIndex == fromLinkIndex)
                {
                    from = loopDistance + fromDistanceAlongLink;
                }

                if (linkIndex == toLinkIndex)
                {
                    to = loopDistance + toDistanceAlongLink;
                }

                loopDistance += plan.Links[linkIndex].Length;
            }

            Assert.That(float.IsNaN(from), Is.False);
            Assert.That(float.IsNaN(to), Is.False);
            float result = to - from;
            return result >= 0f ? result : result + plan.LoopLength;
        }

        private sealed class RuntimeFixture
        {
            private RuntimeFixture(
                GameObject root,
                Transform player,
                CityBusActor actor,
                CityBusPresentation presentation,
                CityBusDirector director)
            {
                Root = root;
                Player = player;
                Actor = actor;
                Presentation = presentation;
                Director = director;
            }

            public GameObject Root { get; }
            public Transform Player { get; }
            public CityBusActor Actor { get; }
            public CityBusPresentation Presentation { get; }
            public CityBusDirector Director { get; }

            public static RuntimeFixture Create(
                string name,
                CityBusPlan plan)
            {
                GameObject root = new GameObject(name);
                Transform player = new GameObject("Player").transform;
                player.SetParent(root.transform, false);
                Transform pool = new GameObject("Pool").transform;
                pool.SetParent(root.transform, false);

                CityBusAssetRegistry registry =
                    CreateRegistry(pool);
                CityBusPresentation presentation =
                    registry.gameObject.AddComponent<
                        CityBusPresentation>();
                presentation.Initialize(registry);
                presentation.gameObject.SetActive(false);

                GameObject actorObject = new GameObject("Actor");
                actorObject.layer = CityBusCollision.LayerIndex;
                actorObject.transform.SetParent(root.transform, false);
                CityBusActor actor =
                    actorObject.AddComponent<CityBusActor>();
                actor.Initialize(
                    registry.LocalBounds,
                    registry.Dimensions);

                CityBusDirector director =
                    root.AddComponent<CityBusDirector>();
                director.Initialize(
                    plan,
                    actor,
                    presentation,
                    player,
                    null,
                    pool,
                    () => 1f);
                return new RuntimeFixture(
                    root,
                    player,
                    actor,
                    presentation,
                    director);
            }

            public void Destroy()
            {
                if (Root != null)
                {
                    Object.DestroyImmediate(Root);
                }
            }

            public void SpawnDirectly(
                uint behaviorSeed = 0x42555354u)
            {
                CityBusSpawnAnchor anchor = Plan.SpawnAnchors[0];
                Actor.PrepareSpawn(
                    Plan,
                    anchor,
                    behaviorSeed);
                Actor.BindPresentation(Presentation);
                Physics.SyncTransforms();
            }

            private CityBusPlan Plan => Director.Plan;

            private static CityBusAssetRegistry CreateRegistry(
                Transform parent)
            {
                GameObject model = new GameObject("Model");
                model.transform.SetParent(parent, false);
                CityBusAssetRegistry registry =
                    model.AddComponent<CityBusAssetRegistry>();
                Transform body = CreateChild("Body", model.transform);
                Transform frontLeftSteering = CreateChild(
                    "Front Left Steering",
                    body);
                Transform frontRightSteering = CreateChild(
                    "Front Right Steering",
                    body);
                Transform frontLeftWheel = CreateChild(
                    "Front Left Wheel",
                    frontLeftSteering);
                Transform frontRightWheel = CreateChild(
                    "Front Right Wheel",
                    frontRightSteering);
                Transform rearLeftWheel = CreateChild(
                    "Rear Left Wheel",
                    body);
                Transform rearRightWheel = CreateChild(
                    "Rear Right Wheel",
                    body);
                registry.Configure(
                    model.transform,
                    body,
                    CreateChild("Front Door Forward Leaf", body),
                    CreateChild("Front Door Rearward Leaf", body),
                    CreateChild("Rear Door Forward Leaf", body),
                    CreateChild("Rear Door Rearward Leaf", body),
                    frontLeftWheel,
                    frontRightWheel,
                    rearLeftWheel,
                    rearRightWheel,
                    frontLeftSteering,
                    frontRightSteering,
                    CreateChild("Driver", body),
                    CreateChild("Front Entry", body),
                    CreateChild("Rear Entry", body),
                    new Transform[0],
                    new Renderer[0],
                    new CityBusRendererBinding[0],
                    new Renderer[0],
                    new Renderer[0],
                    new Renderer[0],
                    new Bounds(
                        new Vector3(0f, 1.475f, 0f),
                        new Vector3(2.72f, 2.95f, 8.366f)),
                    new CityBusDimensions(
                        8.25f,
                        2.38f,
                        2.95f,
                        4.5f,
                        0.43f),
                    1,
                    "test",
                    "test",
                    "test");
                return registry;
            }

            private static Transform CreateChild(
                string name,
                Transform parent)
            {
                Transform child = new GameObject(name).transform;
                child.SetParent(parent, false);
                return child;
            }
        }
    }
}
