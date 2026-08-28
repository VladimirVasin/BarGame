using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainCablewayRideTests
    {
        private static MountainRoadCablewayPlan MountainCableway()
        {
            return MountainRoadPlanner
                .Create(GameSessionState.DefaultCitySeed)
                .Terminal
                .Cableway;
        }

        private static MountainRoadCablewayPlan VillageCableway()
        {
            return AlpineVillagePlanner
                .Create(GameSessionState.DefaultCitySeed)
                .Station
                .Cableway;
        }

        [Test]
        [Category("MountainRoad")]
        public void DriveRules_BrakeToZeroExactlyAtTheDock()
        {
            const float cruise = 2.05f;
            Assert.That(
                MountainCablewayDriveRules.EvaluateApproachSpeed(0f, cruise),
                Is.EqualTo(0f));
            Assert.That(
                MountainCablewayDriveRules.EvaluateApproachSpeed(
                    MountainCablewayDriveRules.BrakeDistance,
                    cruise),
                Is.EqualTo(cruise).Within(0.0001f));
            Assert.That(
                MountainCablewayDriveRules.EvaluateApproachSpeed(
                    MountainCablewayDriveRules.BrakeDistance * 4f,
                    cruise),
                Is.EqualTo(cruise).Within(0.0001f));

            // Monotone all the way in: the line never speeds up as it closes.
            float previous = 0f;
            for (float remaining = 0f;
                 remaining <= MountainCablewayDriveRules.BrakeDistance;
                 remaining += 0.25f)
            {
                float speed =
                    MountainCablewayDriveRules.EvaluateApproachSpeed(
                        remaining,
                        cruise);
                Assert.That(speed, Is.GreaterThanOrEqualTo(previous - 1e-4f));
                previous = speed;
            }
        }

        /// <summary>
        /// A cabin already sitting on the point is sent round rather than
        /// declared arrived. The offer is "the next one", and a line that
        /// stops the instant you ask reads as a cheat.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void DriveRules_SendAnAlreadyDockedCabinAllTheWayRound()
        {
            const float loop = 125f;
            Assert.That(
                MountainCablewayDriveRules.EvaluateApproachDistance(
                    0f,
                    0f,
                    loop),
                Is.EqualTo(loop).Within(0.0001f));
            Assert.That(
                MountainCablewayDriveRules.EvaluateApproachDistance(
                    10f,
                    40f,
                    loop),
                Is.EqualTo(30f).Within(0.0001f));

            // And it wraps rather than going negative.
            Assert.That(
                MountainCablewayDriveRules.EvaluateApproachDistance(
                    120f,
                    10f,
                    loop),
                Is.EqualTo(15f).Within(0.0001f));
        }

        /// <summary>
        /// The one that matters for boarding at all: the cabin has to come to
        /// rest ON the point. A dock further than the motor's two-centimetre
        /// vertical tolerance out of place is refused silently.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void Controller_DocksACabinExactlyOnTheBoardingPoint()
        {
            MountainRoadCablewayPlan plan = MountainCableway();
            var host = new GameObject("Cableway Dock Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(host.transform, plan);
                MountainCablewayController controller = result.Controller;

                Assert.That(controller.IsDocked, Is.False);
                Assert.That(
                    controller.RequestDockAt(plan.BoardingLoopDistance),
                    Is.True);
                Assert.That(controller.IsDocking, Is.True);

                // A fixed step, deliberately coarse: the last frame has to be
                // clamped to what remains or the dock is an asymptote.
                for (int frame = 0; frame < 4000 && !controller.IsDocked;
                     frame++)
                {
                    controller.Advance(1f / 30f);
                }

                Assert.That(
                    controller.IsDocked,
                    Is.True,
                    "The line never came to rest.");
                Assert.That(controller.CurrentSpeed, Is.EqualTo(0f));

                Transform cabin = controller.DockedCabin;
                Assert.That(cabin, Is.Not.Null);

                // Compared as a DISTANCE, never Is.EqualTo(Vector3).
                Assert.That(
                    Vector3.Distance(
                        cabin.position,
                        plan.BoardingCabinAttachment),
                    Is.LessThan(0.01f));

                // And it stays put until it is let go.
                controller.Advance(1f);
                Assert.That(
                    Vector3.Distance(
                        cabin.position,
                        plan.BoardingCabinAttachment),
                    Is.LessThan(0.01f));

                Assert.That(controller.Resume(), Is.True);
                controller.Advance(1f);
                Assert.That(controller.IsDocked, Is.False);
                Assert.That(
                    Vector3.Distance(
                        cabin.position,
                        plan.BoardingCabinAttachment),
                    Is.GreaterThan(0.05f),
                    "The line did not get under way again.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Boarding is a step at BOTH terminals, and it is measured against
        /// the cabin's standable floor - the top of the skirt - rather than
        /// the underside of the box, which is `0.40 m` lower and would put
        /// the platform back where the climb was.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [Category("MountainRoad")]
        public void Boarding_IsAStepAtBothTerminals(bool mountain)
        {
            MountainRoadCablewayPlan plan = mountain
                ? MountainCableway()
                : VillageCableway();

            float step = plan.CabinFloorY - plan.BoardingPlatformTopY;
            Assert.That(
                step,
                Is.EqualTo(MountainRoadCablewayPlan.BoardingStepHeight)
                    .Within(0.0001f));
            Assert.That(step, Is.LessThan(0.5f));

            // The floor is the skirt top, not the cabin's underside.
            Assert.That(
                plan.CabinFloorY -
                (plan.LowerCableCenter.y - plan.CabinAttachmentToBottom),
                Is.EqualTo(MountainRoadCablewayPlan.CabinSkirtHeight)
                    .Within(0.0001f));

            // The dock stands clear of the cabin's near face: contact is read
            // back as achieved movement, so a graze reads as a crawl.
            Vector3 dock = plan.BoardingDockPosition;
            Vector3 cabin = plan.BoardingCabinFloorCenter;
            float clearance = Vector2.Distance(
                                  new Vector2(dock.x, dock.z),
                                  new Vector2(cabin.x, cabin.z)) -
                              plan.CabinSize.x * 0.5f;
            Assert.That(
                clearance,
                Is.GreaterThan(0.3f).And.LessThan(1.5f));
        }

        /// <summary>
        /// The platform is taller than the hero's step offset, so the treads
        /// are not decoration: without them the boarding point is unreachable
        /// while looking perfectly correct.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void Platform_IsClimbableInStepsTheHeroCanTake()
        {
            MountainRoadCablewayPlan plan = MountainCableway();
            var host = new GameObject("Cableway Platform Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(host.transform, plan);
                Transform[] treads = result.StationRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "Physical Platform Tread")
                    .ToArray();

                float platformTop =
                    plan.BoardingPlatformTopY - plan.StationArea.Center.y;
                Assert.That(
                    platformTop,
                    Is.GreaterThan(PlayerFactory.StepOffset),
                    "No treads would be needed; this test is then vacuous.");
                Assert.That(treads, Is.Not.Empty);

                // Every riser, including the last one onto the strip itself.
                var tops = treads
                    .Select(t => t.localPosition.y +
                                 t.localScale.y * 0.5f)
                    .Concat(new[] { platformTop })
                    .OrderBy(y => y)
                    .ToArray();
                float previous = 0f;
                for (int index = 0; index < tops.Length; index++)
                {
                    Assert.That(
                        tops[index] - previous,
                        Is.LessThan(PlayerFactory.StepOffset),
                        $"Riser {index} is taller than a step.");
                    previous = tops[index];
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The village terminal is a RETURN station: it holds the rope tight
        /// and turns nothing under power, so it has no motor voice at all
        /// rather than a silent one hung off a gearbox it does not contain.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void ReturnStation_HasNoMotorAndStillRuns()
        {
            MountainRoadCablewayPlan plan = VillageCableway();
            var host = new GameObject("Return Station Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(
                        host.transform,
                        plan,
                        MountainCablewayStationKind.Return);

                Assert.That(
                    result.Controller.IsInitialized,
                    Is.True,
                    "A station with no reducer must still initialize.");
                Assert.That(
                    result.Root.GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name.Contains("Reducer")),
                    Is.False);
                Assert.That(
                    result.Root.GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name.Contains("Tension Weight Plate")),
                    Is.True);
                Assert.That(
                    result.Controller.AudioSources
                        .Any(source => source.loop),
                    Is.False,
                    "A return station must carry no motor loop.");

                // The line still turns from this end.
                result.Controller.Advance(1f);
                Assert.That(
                    result.Controller.TravelledDistance,
                    Is.GreaterThan(0f));

                Assert.That(
                    result.Cabins.All(cabin =>
                        cabin.GetComponentsInChildren<Collider>(true)
                            .Length == 0),
                    Is.True,
                    "Moving cabins must remain presentation-only.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Every cabin carries the pelvis anchor a ride binds to, and it sits
        /// on the bench rather than at the cabin's origin - which is up on the
        /// cable, three metres over the passenger's head.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void Cabin_CarriesASeatAnchorOnItsBench()
        {
            MountainRoadCablewayPlan plan = MountainCableway();
            var host = new GameObject("Cabin Seat Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(host.transform, plan);
                for (int index = 0; index < result.Cabins.Count; index++)
                {
                    Transform anchor = result.Cabins[index].Find(
                        MountainCablewayWorldBuilder.CabinSeatAnchorName);
                    Assert.That(
                        anchor,
                        Is.Not.Null,
                        "A cabin with no seat anchor cannot be ridden.");

                    float overFloor = anchor.localPosition.y +
                                      plan.CabinAttachmentToBottom -
                                      MountainRoadCablewayPlan
                                          .CabinSkirtHeight;
                    Assert.That(
                        overFloor,
                        Is.EqualTo(
                            MountainCablewayWorldBuilder.CabinBenchHeight)
                            .Within(0.001f));
                }

                // The doorway is an aperture on the OUTBOARD face, where the
                // platform is: the window there is simply not built, which is
                // what frees the boarding clips from any door timing. The
                // inboard face keeps its glass, because that side looks at
                // the other track and the pedestal between them.
                Assert.That(
                    result.Cabins[0]
                        .GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name == "Cabin Right Window"),
                    Is.False);
                Assert.That(
                    result.Cabins[0]
                        .GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name == "Cabin Left Window"),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The gate opens and the sign stays. Nobody took it down, and that is
        /// truer about the place than either the fence or the gate.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void BoardingZone_OpensAGateAndKeepsTheFadedSign()
        {
            MountainRoadCablewayPlan plan = MountainCableway();
            var host = new GameObject("Boarding Zone Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(host.transform, plan);
                Transform[] parts = result.StationRoot
                    .GetComponentsInChildren<Transform>(true);

                Assert.That(
                    parts.Any(t =>
                        t.name == "Faded Sign - Boarding Closed"),
                    Is.True,
                    "The sign is the tone of the place; it stays.");
                Assert.That(
                    parts.Any(t =>
                        t.name == "Boarding Gate Leaf Standing Open"),
                    Is.True);

                // Two rail wings with a gap, not one unbroken run.
                Transform[] rails = parts
                    .Where(t => t.name == "Physical Boarding Rail")
                    .ToArray();
                Assert.That(rails, Has.Length.EqualTo(4));

                // Nothing physical stands between the platform and the lane
                // the hero walks in along.
                Vector3 dock = plan.BoardingDockPosition;
                foreach (Transform rail in rails)
                {
                    Assert.That(
                        Vector3.Distance(rail.position, dock),
                        Is.GreaterThan(0.6f));
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The fade has to happen in the last span, where the snow ridge that
        /// hides the far turn actually is. Any earlier and the cabin vanishes
        /// in open air, which is a worse cut than no cut.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void RideFade_HappensInTheFinalSpanBehindTheRidge()
        {
            MountainRoadCablewayPlan plan = MountainCableway();
            float fadeAt = plan.LineLength -
                           AlpineCablewayRideController.FadeLeadMeters;

            float lastSupport = 0f;
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                if (plan.Nodes[index].Kind ==
                    MountainCablewayNodeKind.Support)
                {
                    lastSupport = Mathf.Max(
                        lastSupport,
                        plan.Nodes[index].Distance);
                }
            }

            Assert.That(
                fadeAt,
                Is.GreaterThan(lastSupport),
                "The screen goes out before the cabin clears the last tower.");
            Assert.That(fadeAt, Is.LessThan(plan.LineLength));

            // And it is genuinely near the top: the remaining run is a small
            // fraction of the line, not a third of it.
            Assert.That(
                (plan.LineLength - fadeAt) / plan.LineLength,
                Is.LessThan(0.25f));
        }
    }
}
