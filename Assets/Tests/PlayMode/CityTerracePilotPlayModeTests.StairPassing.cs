using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_SharedStairPassesPlayerAndOpposingResidents()
        {
            yield return LoadProductionCity();
            var director = city.Pedestrians;
            var graph = city.PedestrianPlan;
            var stair = CityHillsideAccessPlan.ForLayout(city.Layout).SharedCourt;
            var flight = stair.Flights[0];
            var centreline = new CityRoadPath(stair.RouteWorld.Select(p => new Vector2(p.x, p.z)).ToArray());
            int firstWaypoint = stair.RouteWorld.ToList().IndexOf(flight.End);
            int finalWaypoint = stair.RouteWorld.ToList().IndexOf(stair.Flights[2].End);
            Assert.That(finalWaypoint, Is.GreaterThan(firstWaypoint));
            var link = graph.Links.Single(l =>
                Vector3.Distance(graph.Nodes[l.FirstNodeIndex].Position, flight.Start) < .02f &&
                Vector3.Distance(graph.Nodes[l.SecondNodeIndex].Position, flight.End) < .02f);
            var motor = city.Player.Motor;
            director.AutomaticUpdatesSuspended = true;
            try
            {
                yield return MeetOnFlight(true);
                yield return MeetOnFlight(false);
            }
            finally
            {
                director.AutomaticUpdatesSuspended = false;
            }

            IEnumerator MeetOnFlight(bool playerClimbs)
            {
                // Release any streamed cast and reserve known live director slots.
                // The scene retains its rails, stair collider and normal body collision policy.
                director.enabled = false;
                director.enabled = true;
                motor.Teleport((playerClimbs ? flight.Start : stair.RouteWorld[0]) +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                motor.transform.rotation = Quaternion.LookRotation(flight.Direction);
                var down = Spawn(0, link.SecondNodeIndex, link.FirstNodeIndex);
                var up = playerClimbs ? null : Spawn(1, link.FirstNodeIndex, link.SecondNodeIndex);
                Physics.SyncTransforms();

                float minAcross = float.PositiveInfinity;
                bool passed = false, upperArrived = false;
                // Continue beyond the next landing. A resident may legitimately wait there
                // until the player has actually passed before returning down the stairs.
                int waypoint = firstWaypoint;
                for (int frame = 0; frame < 500 && !(upperArrived && down.IsAtResidentDestination); frame++)
                {
                    director.Advance(FrameSeconds);
                    if (playerClimbs && waypoint <= finalWaypoint && motor.MoveTowardsApproachWaypoint(
                        stair.RouteWorld[waypoint] + Vector3.up * PlayerFactory.GroundedRootOffset, .05f, FrameSeconds))
                        waypoint++;
                    upperArrived = playerClimbs ? waypoint > finalWaypoint : up.IsAtResidentDestination;
                    Vector3 climbing = playerClimbs ? motor.transform.position : up.Position;
                    var upOnStair = centreline.Project(new Vector2(climbing.x, climbing.z));
                    var downOnStair = centreline.Project(new Vector2(down.Position.x, down.Position.z));
                    float along = upOnStair.DistanceAlong - downOnStair.DistanceAlong;
                    if (Mathf.Abs(along) < .25f)
                        minAcross = Mathf.Min(minAcross, Mathf.Abs(upOnStair.SignedLateral - downOnStair.SignedLateral));
                    passed |= along > .3f;
                    CheckSupport(climbing);
                    CheckSupport(down.Position);
                    yield return null;
                }
                string pair = playerClimbs ? "player and resident" : "opposing residents";
                Assert.That(upperArrived && down.IsAtResidentDestination, Is.True,
                    $"{pair} must reach their original destinations. Upper actor at " +
                    $"{(playerClimbs ? motor.transform.position : up.Position):F2}, descending resident at {down.Position:F2}.");
                Assert.That(passed && float.IsFinite(minAcross), Is.True, $"{pair} never crossed on the flight.");
                Assert.That(minAcross, Is.GreaterThan(.42f), $"{pair} must visibly pass side by side.");
            }

            CityPedestrianActor Spawn(int slot, int from, int to)
            {
                var actor = director.Actors[slot];
                var model = CityPedestrianDefaultNpcBody.Create(actor.transform, DefaultNpcPopulation.PedestrianId(slot),
                    CityPedestrianResources.LoadStreetClipDonor());
                var visual = model.gameObject.AddComponent<CityPedestrianPresentation>();
                visual.Initialize(model);
                actor.PrepareSpawn(graph, new CityPedestrianSpawnAnchor("opposing stair route", graph.Nodes[from].Position,
                    from, to), to, CityResidentWorld.WalkingSpeed, 1f, 0f, 0, (uint)(31 + slot));
                actor.BindPresentation(visual);
                actor.SetResidentDestination(to, director.Residents.DistancesTo(to));
                return actor;
            }

            void CheckSupport(Vector3 feet)
            {
                Assert.That(stair.TrySampleTop(new Vector2(feet.x, feet.z), out float height), Is.True,
                    $"Passing put feet outside the stair at {feet:F2}.");
                Assert.That(feet.y, Is.EqualTo(height).Within(.22f), $"Passing lost stair contact at {feet:F2}.");
            }
        }

        [UnityTest]
        public IEnumerator SingleFileResidentRetreatsIntoBayForPlayerAndResumesHisRoute()
        {
            // A one-metre passage with a recess on the west, behind the northbound resident.
            // The player walks straight through, continuing beyond the recess before stopping.
            var passage = Rect.MinMaxRect(-.50f, -6f, .50f, 6f);
            var bay = Rect.MinMaxRect(-1.70f, -4.25f, .50f, -1.75f);
            var bounds = new[] { passage, bay };
            var area = new RoadWalkableArea(bounds);
            var host = new GameObject("Single file passing");
            Wall("Floor", new Vector3(0f, -.15f, 0f), new Vector3(4f, .30f, 12f));
            Wall("East wall", new Vector3(.60f, .85f, 0f), new Vector3(.20f, 1.7f, 12f));
            WestWall(-6f, bay.yMin);
            WestWall(bay.yMax, 6f);
            var camera = new GameObject("Passage camera").AddComponent<Camera>();
            camera.transform.SetParent(host.transform, false);
            camera.enabled = false;
            var prompt = new GameObject("Passage prompt").AddComponent<InteractionPromptView>();
            prompt.transform.SetParent(host.transform, false);
            var player = PlayerFactory.Create(host.transform, new Vector3(0f, PlayerFactory.GroundedRootOffset, 2.5f),
                camera, area, prompt);
            player.Motor.transform.rotation = Quaternion.LookRotation(Vector3.back);

            var anchor = new CityPedestrianSpawnAnchor("Northbound", Vector3.zero, 0, 1);
            var plan = new CityPedestrianPlan(7, 11, 3u, CityPedestrianPlanner.AgentRadius,
                new[] { new CityPedestrianNode("South", Vector3.back * 5f, false),
                    new CityPedestrianNode("North", Vector3.forward * 5f, false) },
                new[] { new CityPedestrianLink("Passage", 0, 1, CityPedestrianLinkKind.Sidewalk) },
                new[] { anchor }, bounds);
            var population = new CityPedestrianPopulationProfile("single file", 1, 1, 1, 1, 0f, 1, 0, false);
            var director = CityPedestrianFactory.Create(host.transform, plan, player.Motor.transform, area, population);
            director.AutomaticUpdatesSuspended = true;
            var resident = director.Actors.Single();
            resident.PrepareSpawn(plan, anchor, 1, CityResidentWorld.WalkingSpeed, 1f, 0f, 0, 31u);
            resident.BindPresentation(director.GetComponentInChildren<CityPedestrianPresentation>(true));
            resident.SetResidentDestination(1, new[] { 10f, 0f });
            Physics.SyncTransforms();

            Assert.That(player.Motor.transform.position.z, Is.EqualTo(2.5f).Within(.05f));
            bool usedBay = false, playerPassedInBay = false, playerArrived = false, beganRetreat = false;
            Vector3 atMeeting = Vector3.zero, heroAtMeeting = Vector3.zero, farthestAside = Vector3.zero;
            float closestAlong = float.PositiveInfinity;
            Vector3 destination = new Vector3(0f, PlayerFactory.GroundedRootOffset, -5.5f);
            for (int i = 0; i < 520 && !(playerArrived && resident.IsAtResidentDestination); i++)
            {
                director.Advance(FrameSeconds);
                playerArrived = player.Motor.MoveTowardsApproachWaypoint(destination, .04f, FrameSeconds);
                var visitor = player.Motor.transform.position;
                var walker = resident.Position;
                bool inRecess = walker.x < passage.xMin && bay.Contains(new Vector2(walker.x, walker.z));
                if (Mathf.Abs(walker.x) > Mathf.Abs(farthestAside.x)) farthestAside = walker;
                if (Mathf.Abs(visitor.z - walker.z) < closestAlong)
                {
                    closestAlong = Mathf.Abs(visitor.z - walker.z);
                    atMeeting = walker;
                    heroAtMeeting = visitor;
                }
                beganRetreat |= resident.IsPassingRetreat;
                usedBay |= inRecess;
                // At the crossing the resident must already be beyond the wall line
                // and the capsule centres must be a full body-width apart (less skin).
                float requiredGap = resident.AgentRadius + .32f - PlayerFactory.GroundedRootOffset;
                playerPassedInBay |= inRecess && Mathf.Abs(visitor.z - walker.z) < .20f &&
                    visitor.x - walker.x >= requiredGap && visitor.x >= passage.xMin + .32f;
                Assert.That(area.Contains(walker, resident.AgentRadius), Is.True, $"Resident left the passage at {walker:F2}.");
                yield return null;
            }
            Assert.That(beganRetreat && usedBay && playerPassedInBay, Is.True,
                $"Expected a pass in the bay. Retreat {beganRetreat}, widest step {farthestAside:F2}, closest pass resident {atMeeting:F2}, hero {heroAtMeeting:F2}.");
            Assert.That(playerArrived && resident.IsAtResidentDestination, Is.True,
                $"Both must finish after the encounter. Resident {resident.Position:F2}, hero {player.Motor.transform.position:F2}.");
            Assert.That(resident.ResidentDestinationNode, Is.EqualTo(1));
            Assert.That(Vector3.Distance(resident.Position, plan.Nodes[1].Position), Is.LessThan(.25f));

            void WestWall(float start, float end) => Wall("West wall", new Vector3(-.60f, .85f, (start + end) * .5f),
                new Vector3(.20f, 1.7f, end - start));
            void Wall(string name, Vector3 centre, Vector3 size)
            {
                var part = new GameObject(name);
                part.transform.SetParent(host.transform, false);
                part.transform.localPosition = centre;
                part.AddComponent<BoxCollider>().size = size;
            }
        }
    }
}
