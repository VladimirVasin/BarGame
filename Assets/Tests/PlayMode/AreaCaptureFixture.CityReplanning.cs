using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Replanned district streets through the production camera, light and fog.")]
        public IEnumerator CityReplanning()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            var issues = new List<string>();
            System.Action restoreWalker = null;
            try
            {
                yield return Capture(SceneIds.City,
                    () =>
                    {
                        city = Object.FindAnyObjectByType<CityGameRoot>();
                        return city != null && city.Layout != null && city.World != null &&
                            city.BusPlan != null ? city : null;
                    }, () => CityReplanningShots(city, issues, out restoreWalker));
            }
            finally { restoreWalker?.Invoke(); }
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        private static Shot[] CityReplanningShots(CityGameRoot city, List<string> issues, out System.Action restoreWalker)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.SpatialPlan.IsUniform, Is.False);
            LogReplanningRouteLengths(city);
            var shots = new List<Shot>();
            CityStreetSurfacePlan streetPlan = CityStreetSurfacePlanner.Create(layout);
            RoadWalkableArea pedestrianArea = CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan);
            CityRoadJunction oblique = layout.RoadGeometry.ObliqueJunction;
            Assert.That(oblique, Is.Not.Null);
            foreach (CityRoadPath pavement in oblique.SidewalkPaths)
                for (float distance = .1f; distance < pavement.Length; distance += .5f)
                {
                    Vector2 point = pavement.SampleDistance(distance).Position;
                    float top = layout.ElevationPlan.GetNodeElevation(oblique.Node) + CityStreetSurfacePlanner.SidewalkTop;
                    Vector3 probe = new Vector3(point.x, top + .5f, point.y);
                    if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(hit.point.y - top) > .025f)
                        issues.Add($"Oblique junction pavement at {point:F4}, expected={top:F4}");
                    Vector3 standing = new Vector3(point.x, top, point.y);
                    if (!city.World.WalkableArea.Contains(standing, .35f)) issues.Add($"Oblique hero navigation at {point:F4}");
                    if (!pedestrianArea.Contains(standing, .35f)) issues.Add($"Oblique pedestrian navigation at {point:F4}");
                }
            for (int index = 0; index < layout.RoadGeometry.CurvedEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadGeometry.CurvedEdges[index];
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                for (float s = 6.5f; s < path.Length - 6f; s += 1f)
                {
                    CityRoadSample sample = path.SampleDistance(s);
                    float datum = layout.ElevationPlan.SampleRoadDatum(edge, s / path.Length);
                    foreach (float offset in new[] { 0f, -3.5f, 3.5f })
                    {
                        Vector2 point = sample.Position + sample.Right * offset;
                        if (offset != 0 && streetPlan.CurvedSidewalkRibbons.Any(ribbon =>
                            ribbon.Edge.Equals(edge) && NearPavementEnd(ribbon, point))) continue;
                        Vector3 probe = new Vector3(point.x, datum + 2f, point.y);
                        Assert.That(Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4f,
                            ~0, QueryTriggerInteraction.Ignore), Is.True, "Curved paving must have a physical surface.");
                        bool pavement = offset != 0 && streetPlan.CurvedSidewalkPolygons.Any(polygon =>
                            CityRoadPolygon.Contains(polygon, point));
                        float top = datum + (pavement ? CityStreetSurfacePlanner.SidewalkTop : CityStreetSurfacePlanner.RoadTop);
                        if (Mathf.Abs(hit.point.y - top) > .025f)
                            issues.Add($"Paving {edge.A}->{edge.B} s={s:F2} offset={offset:F1} point={point:F4} pavement={pavement} expected={top:F4} actual={hit.point.y:F4} collider={hit.collider.name}");
                        if (!city.World.WalkableArea.Contains(hit.point, .35f))
                            issues.Add($"Hero navigation at {point:F4}");
                        if (pavement && !pedestrianArea.Contains(hit.point, .35f))
                            issues.Add($"Pedestrian navigation at {point:F4}");
                    }
                }
                CityRoadSample eyeSample = path.SampleDistance(path.Length * .3f);
                Vector3 eye = ReplanningStreetEye(layout, new Vector3(eyeSample.Position.x, 0, eyeSample.Position.y));
                CityRoadSample targetSample = path.SampleDistance(Mathf.Min(path.Length - 2f, path.Length * .3f + 16f));
                shots.Add(Shot.At($"replanning-shortcuts-street-{index + 1:00}-curve", eye,
                    new Vector3(targetSample.Position.x, eye.y - .7f, targetSample.Position.y), 78f));
            }
            foreach (CityPedestrianLink link in city.PedestrianPlan.Links.Where(link => link.Path != null))
                for (float distance = .5f; distance < link.Path.Length; distance += 1f)
                {
                    Vector2 point = link.Path.SampleDistance(distance).Position;
                    float top = link.PathHeightSampler(point);
                    Vector3 probe = new Vector3(point.x, top + .5f, point.y);
                    RaycastHit[] hits = Physics.RaycastAll(probe, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore);
                    if (!hits.Any(hit => hit.collider is MeshCollider && Mathf.Abs(hit.point.y - top) < .025f))
                        issues.Add($"Pedestrian height {link.Id} at {point:F4}, expected={top:F4}, hits={string.Join(",", hits.Select(hit => hit.collider.name + ":" + hit.point.y.ToString("F4")))}");
                }
            RoadEdge branch = new RoadEdge(new Vector2Int(1, 8), new Vector2Int(2, 8));
            CityRoadPath branchPath = layout.RoadGeometry.Get(branch);
            CityRoadSample middle = branchPath.SampleDistance(branchPath.Length * .5f);
            Vector2 released = middle.Position - middle.Right * 5.2f;
            float branchDatum = layout.ElevationPlan.SampleRoadDatum(branch, .5f);
            Vector3 releasedProbe = new Vector3(released.x, branchDatum + 2f, released.y);
            Assert.That(Physics.Raycast(releasedProbe, Vector3.down, out RaycastHit releasedGround, 4f,
                ~0, QueryTriggerInteraction.Ignore), Is.True, "Released straight-road strip must be filled with ground.");
            Assert.That(releasedGround.point.y, Is.LessThan(branchDatum + .04f), "The obsolete straight road must not survive below the new bend.");
            Assert.That(city.World.WalkableArea.Contains(releasedGround.point, .35f), Is.True);
            Vector3 junction = layout.GetNodeWorldPosition(new Vector2Int(1, 8));
            Vector3 junctionEye = ReplanningStreetEye(layout, junction + Vector3.right * 5f);
            shots.Add(Shot.At("replanning-shortcuts-street-04-t-junction", junctionEye,
                junction + Vector3.left * 6f + Vector3.up * (EyeHeight - .6f), 102f));
            CityRoadSample branchEye = branchPath.SampleDistance(11f);
            Vector3 obliqueEye = ReplanningStreetEye(layout,
                new Vector3(branchEye.Position.x, 0f, branchEye.Position.y));
            shots.Add(Shot.At("replanning-shortcuts-street-05-approach", obliqueEye,
                junction + Vector3.forward * 4f + Vector3.up * (EyeHeight - .6f), 90f));
            VerifyReplanningCourtyards(city, streetPlan, shots, issues);
            var walkerDemo = new ReplanningCourtyardWalkerDemo(city, streetPlan, issues);
            walkerDemo.AddShots(shots);
            restoreWalker = walkerDemo.Restore;
            Debug.Log($"OldTown pilot: {layout.RoadGeometry.CurvedEdges.Count} shared road paths; physical probe issues={issues.Count}.");
            return shots.ToArray();
        }

        private static void VerifyReplanningCourtyards(CityGameRoot city,
            CityStreetSurfacePlan streetPlan, List<Shot> shots, List<string> issues)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.CourtyardBlocks.Count, Is.EqualTo(4));
            Assert.That(layout.BuildingMasses.Count, Is.EqualTo(146));
            var mapGround = new CityMapCityTeleportGround(layout);
            RoadWalkableArea pedestrianArea = CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan);
            Physics.SyncTransforms();
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                VerifyReplanningWalkingPath($"Courtyard {block.Cell}", block.Route, city, streetPlan, pedestrianArea, issues);
                foreach (Vector3 arrival in new[] { block.CourtCenter, block.PassageCenter })
                {
                    Vector2 point = new Vector2(arrival.x, arrival.z);
                    if (!mapGround.TryResolveStandingPosition(point, out Vector3 standing) ||
                        (new Vector2(standing.x, standing.z) - point).sqrMagnitude > .0001f ||
                        !TryReplanningSurfaceTop(layout, streetPlan, point, out float top) ||
                        Mathf.Abs(standing.y - top - PlayerFactory.GroundedRootOffset) > .025f)
                        issues.Add($"Courtyard {block.Cell} map arrival moved or missed the actual ground at {point:F4}.");
                }
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(block.Primary);
                Vector3 courtEye = ReplanningCourtyardEye(layout, streetPlan,
                    new Vector2(block.CourtCenter.x, block.CourtCenter.z));
                Vector3 courtTarget = pose.TransformPoint(new Vector3(9f, 1.15f, -2.5f));
                if (block.RearBuilding != null)
                {
                    Vector3 flank = pose.TransformPoint(new Vector3(8.1f, 0f, -1.5f));
                    courtEye = ReplanningCourtyardEye(layout, streetPlan, new Vector2(flank.x, flank.z));
                    courtTarget = block.CourtCenter + Vector3.up * 1.15f;
                }
                shots.Add(Shot.At($"replanning-shortcuts-courtyard-{block.Cell.x}-{block.Cell.y}-court", courtEye,
                    courtTarget, 88f));
                if (block.RearBuilding != null)
                {
                    float passageDistance = block.Route.Project(new Vector2(block.PassageCenter.x,
                        block.PassageCenter.z)).DistanceAlong;
                    CityRoadSample eye = block.Route.SampleDistance(Mathf.Max(0f, passageDistance - 2f));
                    CityRoadSample target = block.Route.SampleDistance(Mathf.Min(block.Route.Length, passageDistance + 4f));
                    Vector3 passageEye = ReplanningCourtyardEye(layout, streetPlan, eye.Position);
                    shots.Add(Shot.At($"replanning-shortcuts-courtyard-{block.Cell.x}-{block.Cell.y}-passage", passageEye,
                        new Vector3(target.Position.x, passageEye.y - .65f, target.Position.y), 78f));
                }
                if (block.Cell == new Vector2Int(0, 8))
                {
                    Vector3 entranceEye = ReplanningCourtyardEye(layout, streetPlan, block.Route.Vertices[0]);
                    CityRoadSample target = block.Route.SampleDistance(6f);
                    shots.Add(Shot.At("replanning-shortcuts-courtyard-0-8-entrance", entranceEye,
                        new Vector3(target.Position.x, entranceEye.y - .55f, target.Position.y), 82f));
                }
            }
            foreach (CityCourtyardConnection connection in layout.CourtyardConnections)
            {
                VerifyReplanningWalkingPath($"Courtyard connection {connection.FirstCell}->{connection.SecondCell}",
                    connection.Path, city, streetPlan, pedestrianArea, issues);
                Vector2 middle = connection.Path.SampleDistance(connection.Path.Length * .5f).Position;
                if (!mapGround.TryResolveStandingPosition(middle, out Vector3 standing) ||
                    (new Vector2(standing.x, standing.z) - middle).sqrMagnitude > .0001f ||
                    !TryReplanningSurfaceTop(layout, streetPlan, middle, out float top) ||
                    Mathf.Abs(standing.y - top - PlayerFactory.GroundedRootOffset) > .025f)
                    issues.Add($"Courtyard connection map arrival moved or missed the ground at {middle:F4}.");
                foreach (bool reverse in new[] { false, true })
                {
                    float station = connection.Path.Length * (reverse ? .72f : .28f);
                    CityRoadSample eye = connection.Path.SampleDistance(station);
                    CityRoadSample target = connection.Path.SampleDistance(station + (reverse ? -9f : 9f));
                    Vector3 camera = ReplanningCourtyardEye(layout, streetPlan, eye.Position);
                    shots.Add(Shot.At($"replanning-shortcuts-courtyard-connection-{(reverse ? "reverse" : "forward")}", camera,
                        new Vector3(target.Position.x, camera.y - .65f, target.Position.y), 86f));
                }
            }
        }

        private static void VerifyReplanningWalkingPath(string label, CityRoadPath path, CityGameRoot city,
            CityStreetSurfacePlan streetPlan, RoadWalkableArea pedestrianArea, List<string> issues)
        {
            CharacterController hero = city.Player.GameObject.GetComponent<CharacterController>();
            for (float distance = 0f; distance < path.Length + .5f; distance += .5f)
            {
                Vector2 point = path.SampleDistance(distance).Position;
                if (!TryReplanningSurfaceTop(city.Layout, streetPlan, point, out float top))
                { issues.Add($"{label} has no walking surface at {point:F4}."); continue; }
                Vector3 floor = new Vector3(point.x, top, point.y);
                if (!Physics.Raycast(floor + Vector3.up * 1.5f, Vector3.down,
                    out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    Mathf.Abs(hit.point.y - top) > .025f || hit.normal.y < .7f)
                    issues.Add($"{label} physical floor at {point:F4}, expected={top:F4}, actual={hit.point.y:F4}, collider={hit.collider?.name}.");
                if (!city.World.WalkableArea.Contains(floor, .35f)) issues.Add($"{label} hero capsule leaves navigation at {point:F4}.");
                if (!pedestrianArea.Contains(floor, .35f)) issues.Add($"{label} pedestrian capsule leaves navigation at {point:F4}.");
                // Full radius; the production step offset admits the existing curb.
                foreach (Collider obstacle in Physics.OverlapCapsule(
                    floor + Vector3.up * (hero.stepOffset + .35f + PlayerFactory.GroundedRootOffset),
                    floor + Vector3.up * (hero.height - .35f + PlayerFactory.GroundedRootOffset), .35f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (obstacle.transform.IsChildOf(city.Player.GameObject.transform) ||
                        obstacle.GetComponentInParent<DefaultNpcAppearance>() != null ||
                        obstacle.GetComponentInParent<CityPedestrianActor>() != null) continue;
                    issues.Add($"{label} route capsule blocked at {point:F4} by {obstacle.name}.");
                }
            }
        }

        private sealed class ReplanningCourtyardWalkerDemo
        {
            private const float Tick = .05f;
            private readonly CityGameRoot city;
            private readonly CityStreetSurfacePlan surfaces;
            private readonly List<string> issues;
            private readonly CityRoadPath path;
            private readonly RoadWalkableArea area;
            private readonly int first, second;
            private readonly float[] toFirst, toSecond;
            private readonly List<KeyValuePair<Behaviour, bool>> paused = new List<KeyValuePair<Behaviour, bool>>();
            private CityPedestrianActor walker;
            private Vector3 originalPosition;
            private Quaternion originalRotation;
            private int originalTarget, ticks, originalDetours;
            private bool originalCollision, reverseStarted, forwardCompleted;
            private bool originalAutomaticUpdatesSuspended, hasManualControl;
            private string originalDesign;
            private float travelled;

            public ReplanningCourtyardWalkerDemo(CityGameRoot city, CityStreetSurfacePlan surfaces, List<string> issues)
            {
                this.city = city; this.surfaces = surfaces; this.issues = issues;
                CityCourtyardConnection connection = city.Layout.CourtyardConnections.Single();
                path = connection.Path;
                CityPedestrianPlan plan = city.PedestrianPlan;
                first = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id ==
                    $"courtyard:{connection.FirstCell.x}:{connection.FirstCell.y}:court");
                second = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id ==
                    $"courtyard:{connection.SecondCell.x}:{connection.SecondCell.y}:court");
                toFirst = CityBusStopWaitPlanner.CreateNodeDistances(plan, first);
                toSecond = CityBusStopWaitPlanner.CreateNodeDistances(plan, second);
                Assert.That(toSecond[first], Is.EqualTo(path.Length).Within(.01f), "The courtyard shortcut must be the actual shortest graph route.");
                area = CityPedestrianPlanner.CreateWalkableArea(plan);
            }

            public void AddShots(List<Shot> shots)
            {
                // Frame the walker on the longest real straight leg so a
                // courtyard corner cannot hide the body from the camera.
                float longest = 0f, along = 0f, middle = 0f;
                for (int i = 1; i < path.Vertices.Count; i++)
                {
                    float span = Vector2.Distance(path.Vertices[i - 1], path.Vertices[i]);
                    if (span > longest) { longest = span; middle = along + span * .5f; }
                    along += span;
                }
                foreach (bool reverse in new[] { false, true })
                    foreach (bool arrival in new[] { false, true })
                    {
                        float station = arrival ? (reverse ? 0f : path.Length) : middle;
                        float cameraStation = arrival ? station + (reverse ? 2f : -2f)
                            : station + (reverse ? 1f : -1f) * Mathf.Min(3f, longest * .25f);
                        Vector3 eye = ReplanningCourtyardEye(city.Layout, surfaces, path.SampleDistance(cameraStation).Position);
                        Vector2 target = path.SampleDistance(station).Position;
                        TryReplanningSurfaceTop(city.Layout, surfaces, target, out float top);
                        shots.Add(Shot.At($"replanning-shortcuts-courtyard-walker-{(reverse ? "reverse" : "forward")}-{(arrival ? "arrival" : "passage")}",
                            eye, new Vector3(target.x, top + .85f, target.y), 72f,
                            readyWhen: () => ReachCheckpoint(reverse, station, arrival)));
                    }
            }

            private bool ReachCheckpoint(bool reverse, float station, bool arrival)
            {
                if (walker == null)
                {
                    walker = city.Pedestrians.Actors.FirstOrDefault(actor => actor.IsSpawned &&
                        actor.MotionState == CityPedestrianMotionState.Walking && !actor.IsPersonalSpaceReacting);
                    if (walker == null) return false; // Let the existing population spawn normally.
                    originalPosition = walker.Position; originalRotation = walker.transform.rotation;
                    originalTarget = walker.TargetNodeIndex; originalCollision = walker.CollisionEnabled;
                    originalDesign = walker.DesignId;
                    originalAutomaticUpdatesSuspended = city.Pedestrians.AutomaticUpdatesSuspended;
                    city.Pedestrians.AutomaticUpdatesSuspended = true;
                    hasManualControl = true;
                    Pause(city.BusPassengers); Pause(city.BenchRests);
                    Assert.That(walker.IsSpawned, Is.True, "Manual capture must retain the existing walker presentation.");
                    Assert.That(walker.AgentRadius, Is.EqualTo(.35f).Within(.001f));
                    walker.CharacterController.enabled = false;
                    // Initial fixture placement only. Both measured legs then
                    // use ordinary graph guidance and CharacterController.Move.
                    walker.transform.position = city.PedestrianPlan.Nodes[first].Position;
                    walker.ResumeRoaming(first); walker.SetAvoidance(1f, 1f);
                    originalDetours = walker.DetourCount;
                    Physics.SyncTransforms();
                }
                if (reverse && !reverseStarted)
                {
                    Assert.That(forwardCompleted, Is.True);
                    walker.ResumeRoaming(second);
                    walker.SetAvoidance(1f, -1f);
                    reverseStarted = true; travelled = 0f; ticks = 0;
                }
                int goal = reverse ? first : second;
                Vector3 destination = city.PedestrianPlan.Nodes[goal].Position;
                float[] distances = reverse ? toFirst : toSecond;
                for (int batch = 0; batch < 40; batch++)
                {
                    Vector2 current = new Vector2(walker.Position.x, walker.Position.z);
                    float progress = path.Project(current).DistanceAlong;
                    bool reached = arrival ? Vector2.Distance(current, new Vector2(destination.x, destination.z)) < .06f
                        : reverse ? progress <= station : progress >= station;
                    if (reached)
                    {
                        if (arrival)
                        {
                            Assert.That(travelled, Is.GreaterThan(path.Length * .95f), "The walker must physically traverse the complete shortcut.");
                            Assert.That(walker.DesignId, Is.EqualTo(originalDesign));
                            if (!reverse) forwardCompleted = true;
                            Debug.Log($"Courtyard walker {originalDesign}, {(reverse ? "reverse" : "forward")}: measured travel={travelled:F3} m.");
                        }
                        return true;
                    }
                    CityPedestrianPlan plan = city.PedestrianPlan;
                    int previousNode = walker.PreviousNodeIndex, targetNode = walker.TargetNodeIndex;
                    string previousId = previousNode >= 0 ? plan.Nodes[previousNode].Id : "<none>";
                    string targetId = targetNode >= 0 ? plan.Nodes[targetNode].Id : "<none>";
                    Vector3 targetPosition = targetNode >= 0 ? plan.Nodes[targetNode].Position : walker.Position;
                    Assert.That(++ticks, Is.LessThan((path.Length / walker.MovementSpeed + 20f) / Tick),
                        $"The production pedestrian stalled on the courtyard shortcut: reverse={reverse}, arrival={arrival}, " +
                        $"current={walker.Position:F4}, target={targetPosition:F4}, destination={destination:F4}, " +
                        $"previousNode={previousNode}:{previousId}, targetNode={targetNode}:{targetId}, " +
                        $"progress={progress:F4}/{path.Length:F4}, distanceGoal={Vector2.Distance(current, new Vector2(destination.x, destination.z)):F4}, " +
                        $"lastDisplacement={walker.LastDisplacement:F5}, motionState={walker.MotionState}.");
                    Vector3 previous = walker.Position;
                    walker.Advance(Tick, initialApproachTarget: destination, initialApproachNodeDistances: distances);
                    travelled += Vector2.Distance(new Vector2(previous.x, previous.z), new Vector2(walker.Position.x, walker.Position.z));
                    Assert.That(walker.CollisionEnabled, Is.True, "The demo must retain the real pedestrian capsule.");
                    Assert.That(walker.DetourCount, Is.EqualTo(originalDetours), "The shortcut must be traversable without a blocked escape.");
                    current = new Vector2(walker.Position.x, walker.Position.z);
                    float maximumOffset = CityPedestrianActor.MaximumLateralOffset + .02f;
                    Assert.That(path.Project(current).DistanceSquared, Is.LessThan(maximumOffset * maximumOffset),
                        "The pedestrian must stay on its authored connection with the production shoulder shift.");
                    Assert.That(area.Contains(walker.Position, .35f), Is.True);
                    Assert.That(city.World.WalkableArea.Contains(walker.Position, .35f), Is.True);
                    bool hasGroundTop = TryReplanningSurfaceTop(city.Layout, surfaces, current, out float top);
                    float verticalGap = walker.Position.y - top;
                    float skinWidth = walker.CharacterController.skinWidth;
                    if (!hasGroundTop || verticalGap < -.002f || verticalGap > skinWidth + .002f)
                        issues.Add($"Courtyard walker root outside controller ground-contact band at {current:F4}, " +
                            $"root={walker.Position.y:F4}, ground={top:F4}, gap={verticalGap:F5}, skinWidth={skinWidth:F4}.");
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(current.x, top + .5f, current.y), Vector3.down,
                        1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (!hits.Any(hit => hit.collider is MeshCollider && Mathf.Abs(hit.point.y - top) < .025f))
                        issues.Add($"Courtyard walker has no physical ground at {current:F4}, expected={top:F4}.");
                }
                return false;
            }

            private void Pause(Behaviour behaviour)
            {
                if (behaviour == null) return;
                paused.Add(new KeyValuePair<Behaviour, bool>(behaviour, behaviour.enabled));
                behaviour.enabled = false;
            }

            public void Restore()
            {
                if (walker != null && walker.IsSpawned)
                {
                    walker.CharacterController.enabled = false;
                    walker.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    walker.ResumeRoaming(originalTarget);
                    walker.SetAvoidance(1f, 0f);
                    walker.CharacterController.enabled = originalCollision;
                }
                foreach (KeyValuePair<Behaviour, bool> entry in paused)
                    if (entry.Key != null) entry.Key.enabled = entry.Value;
                if (hasManualControl && city != null && city.Pedestrians != null)
                    city.Pedestrians.AutomaticUpdatesSuspended = originalAutomaticUpdatesSuspended;
            }
        }

        private static bool TryReplanningSurfaceTop(CityLayout layout, CityStreetSurfacePlan streetPlan,
            Vector2 point, out float top)
        {
            if (streetPlan.CurvedSidewalkPolygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)) ||
                (layout.RoadGeometry.ObliqueJunction != null && layout.RoadGeometry.ObliqueJunction.SidewalkPolygons
                    .Any(polygon => CityRoadPolygon.Contains(polygon, point))))
                return layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.SidewalkTop, out top, out _);
            var position = new Vector3(point.x, 0f, point.y);
            foreach (RuntimeOrientedBox sidewalk in streetPlan.SidewalkGeometry)
                if (sidewalk.TrySampleTop(position, out top)) return true;
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out top, out _)) return true;
            return layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop, out top, out _);
        }

        private static Vector3 ReplanningCourtyardEye(CityLayout layout, CityStreetSurfacePlan streetPlan, Vector2 point)
        {
            Assert.That(TryReplanningSurfaceTop(layout, streetPlan, point, out float top), Is.True);
            return new Vector3(point.x, top + EyeHeight, point.y);
        }

        private static bool NearPavementEnd(CityStreetRibbonDescriptor ribbon, Vector2 point)
        {
            foreach (Vector2[] polygon in ribbon.Polygons)
                foreach (int side in new[] { 0, 2 })
                {
                    // Cross-sections are legitimate material/height boundaries;
                    // raycasts on their millimetre tolerance band are ambiguous.
                    Vector2 a = polygon[side], delta = polygon[(side + 1) % polygon.Length] - a;
                    Vector2 nearest = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                    if ((point - nearest).sqrMagnitude < .000004f) return true;
                }
            return false;
        }

        private static void LogReplanningRouteLengths(CityGameRoot city)
        {
            Assert.That(city.World.FringeYardPlan.HasTunnelForecourt, Is.True);
            Assert.That(city.World.MountainBoundaryPlan.HasTunnel, Is.True);
            LastRouteCarPlan car = LastRouteCarPlan.Create(city.Layout);
            Assert.That(car.IsPresent, Is.True);
            CityTunnelTravelPlan tunnel = CityTunnelTravelPlanner.Create(
                city.World.MountainBoundaryPlan.Tunnel);
            CityTunnelForecourtDescriptor forecourt = city.World.FringeYardPlan.TunnelForecourt;
            LastRouteCarDrivePath departure = LastRouteCityDrivePlanner.CreateDeparture(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            LastRouteCarDrivePath arrival = LastRouteCityDrivePlanner.CreateReturn(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            float blackout = LastRouteCityDrivePlanner.TunnelBlackoutDepth;
            Debug.Log($"City replanning routes: departureTotal={departure.Length:F3} m; " +
                $"cityToPortal={departure.Length - blackout:F3} m; tunnel={blackout:F3} m; " +
                $"returnTotal={arrival.Length:F3} m; busLoop={city.BusPlan.LoopLength:F3} m; " +
                $"busStops={city.BusPlan.Stops.Count}.");
        }

        private static Vector3 ReplanningStreetEye(CityLayout layout, Vector3 position)
        {
            if (layout.ElevationPlan.TrySampleSurface(new Vector2(position.x, position.z),
                CitySurfaceRole.RoadTop, out float top, out _)) position.y = top;
            position.y += EyeHeight;
            return position;
        }
    }
}
