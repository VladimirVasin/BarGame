using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

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

                // The line is built docked, so let it go first: what is
                // measured here is the BRAKING, not the initial pose.
                Assert.That(controller.Resume(), Is.True);
                controller.Advance(2f);
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
        /// The line stands at the platform and does not move until somebody
        /// is in it.
        ///
        /// Boarding used to be: press, then wait about nineteen seconds of
        /// silence while a cabin came round. A cabin that is simply THERE
        /// costs no wait, no `waitingForCabin` flag, no poll and no answer to
        /// "what tells the player the call landed" - and it is what a freight
        /// line in its last years actually does.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [Category("MountainRoad")]
        public void Controller_StartsStandingWithACabinOnThePoint(
            bool mountain)
        {
            MountainRoadCablewayPlan plan = mountain
                ? MountainCableway()
                : VillageCableway();
            var host = new GameObject("Cableway Start Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(
                        host.transform,
                        plan,
                        mountain
                            ? MountainCablewayStationKind.Drive
                            : MountainCablewayStationKind.Return);
                MountainCablewayController controller = result.Controller;

                Assert.That(
                    controller.IsDocked,
                    Is.True,
                    "The line must be built standing at the platform.");
                Assert.That(controller.IsDocking, Is.False);
                Assert.That(controller.CurrentSpeed, Is.EqualTo(0f));

                Transform cabin = controller.DockedCabin;
                Assert.That(cabin, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        cabin.position,
                        plan.BoardingCabinAttachment),
                    Is.LessThan(0.001f),
                    "The docked cabin is on the point by construction, so " +
                    "this is exact rather than braked-to.");

                // And it stays there: nothing turns until it is let go.
                controller.Advance(5f);
                Assert.That(controller.TravelledDistance, Is.EqualTo(0f));
                Assert.That(
                    Vector3.Distance(
                        cabin.position,
                        plan.BoardingCabinAttachment),
                    Is.LessThan(0.001f));

                // A drive station is silent while it stands. An idle hum from
                // a gearbox that is not turning would be the loudest wrong
                // thing on the summit.
                foreach (AudioSource source in controller.AudioSources)
                {
                    if (source.loop)
                    {
                        Assert.That(
                            source.volume,
                            Is.EqualTo(0f).Within(0.0001f));
                    }
                }
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
        /// Something actually lights the place he boards.
        ///
        /// The station's two canopy fixtures both hang on the yard side and
        /// throw BACKWARDS: measured against the dock they are `92.7` and
        /// `52.5` degrees off axis, against half-angles of `50` and `39`. The
        /// one square metre of this station a passenger has to find was the
        /// darkest ground on it, and nothing said so - a light either covers a
        /// point or it does not, and no test had ever asked.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [Category("MountainRoad")]
        public void BoardingDock_IsInsideSomeStationLightCone(bool mountain)
        {
            MountainRoadCablewayPlan plan = mountain
                ? MountainCableway()
                : VillageCableway();
            var host = new GameObject("Dock Lighting Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(
                        host.transform,
                        plan,
                        mountain
                            ? MountainCablewayStationKind.Drive
                            : MountainCablewayStationKind.Return);

                // Aim at a standing chest, which is what the fixture is aimed
                // at and what the player actually sees lit.
                Vector3 target = plan.BoardingDockPosition + Vector3.up * 1.1f;
                Light covering = null;
                float bestAngle = 180f;
                foreach (Light light in
                         result.Root.GetComponentsInChildren<Light>(true))
                {
                    Vector3 delta = target - light.transform.position;
                    float distance = delta.magnitude;
                    float angle = Vector3.Angle(
                        light.transform.forward,
                        delta);
                    if (angle < bestAngle)
                    {
                        bestAngle = angle;
                    }

                    if (distance <= light.range &&
                        angle <= light.spotAngle * 0.5f)
                    {
                        covering = light;
                    }
                }

                Assert.That(
                    covering,
                    Is.Not.Null,
                    "No station light reaches the boarding dock; the " +
                    $"closest is {bestAngle:0.0} degrees off axis.");

                // And it delivers a POOL, not a hint. The station practical
                // puts about `0.42` on the pad; the marked spot has to beat
                // that or it is not a marker.
                float throwDistance = Vector3.Distance(
                    covering.transform.position,
                    plan.BoardingDockPosition);
                float illumination =
                    covering.intensity / (throwDistance * throwDistance);
                Assert.That(
                    illumination,
                    Is.GreaterThan(0.45f),
                    $"Only {illumination:0.00} delivered at the dock from " +
                    $"{throwDistance:0.00} m.");

                // House rules for a mountain fixture: a spot, inside the
                // area's own band, shadowless, and born under something you
                // can see.
                Assert.That(covering.type, Is.EqualTo(LightType.Spot));
                Assert.That(covering.intensity, Is.InRange(1.5f, 18f));
                Assert.That(
                    covering.shadows,
                    Is.EqualTo(LightShadows.None));
                Assert.That(
                    covering.GetComponentInParent<Renderer>(),
                    Is.Not.Null,
                    "A light with no visible fixture reads as a bug here.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The boarding side is solved the same way at BOTH terminals.
        ///
        /// It was not. The strip was ordered off a fence line authored at a
        /// fixed `1.56`, and the two stations do not hang their cable in the
        /// same place - the summit `4.50 m` in front of the pad centre, the
        /// village `1.90`. The village strip came out as `2.77` to `1.03`:
        /// **`1.74 m` long in the wrong direction**, with its own steps past
        /// its far end. Nothing caught it, because the only test that measured
        /// the strip built the summit.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [Category("MountainRoad")]
        public void BoardingSide_IsOrderedFromTheDockAtBothTerminals(
            bool mountain)
        {
            MountainRoadCablewayPlan plan = mountain
                ? MountainCableway()
                : VillageCableway();

            Assert.That(
                plan.BoardingPlatformLength,
                Is.GreaterThan(2f),
                "The strip has no length; it is inside out.");
            Assert.That(
                plan.BoardingPlatformNearForward,
                Is.LessThan(plan.BoardingDockForwardOffset));
            Assert.That(
                plan.BoardingPlatformFarForward,
                Is.GreaterThan(plan.BoardingDockForwardOffset),
                "The dock must stand ON the strip.");

            // Barrier, then the flight, then the strip - in that order, with
            // no overlap anywhere.
            Assert.That(
                plan.BoardingFenceForward,
                Is.LessThan(plan.BoardingStepsNearForward));
            Assert.That(
                plan.BoardingStepsFarForward,
                Is.EqualTo(plan.BoardingPlatformNearForward)
                    .Within(0.0001f));
            Assert.That(
                plan.BoardingStepsFarForward -
                plan.BoardingStepsNearForward,
                Is.EqualTo(
                    MountainRoadCablewayPlan.BoardingTreadDepth *
                    MountainRoadCablewayPlan.BoardingTreadCount)
                    .Within(0.0001f));

            // And the whole of it stands on ground: pad, or the apron that
            // carries the part running off the front of it.
            Assert.That(
                plan.BoardingFenceForward,
                Is.GreaterThan(-plan.StationArea.Size.y * 0.5f),
                "The barrier has fallen off the back of the pad.");
            Assert.That(
                plan.BoardingApronFarForward,
                Is.GreaterThan(plan.BoardingPlatformFarForward));
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

                // The line still turns from this end once it is let go. It is
                // built standing, like the drive end.
                Assert.That(result.Controller.Resume(), Is.True);
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
        /// The windows are glass, and the hero can see out.
        ///
        /// They were built with the LAMP LENS material - `CityNoirEmission`,
        /// `RenderType: Opaque`, `_Blend 0` - so the alpha authored on the
        /// tint was thrown away and the three panes were glowing plates. The
        /// passenger rides this box in first person for a whole climb.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [Category("MountainRoad")]
        public void Cabin_WindowsAreGlassAndNotLampLenses(bool mountain)
        {
            MountainRoadCablewayPlan plan = mountain
                ? MountainCableway()
                : VillageCableway();
            string[] paneNames =
            {
                "Cabin Front Window",
                "Cabin Rear Window",
                "Cabin Left Window"
            };
            var host = new GameObject("Cabin Glass Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(
                        host.transform,
                        plan,
                        mountain
                            ? MountainCablewayStationKind.Drive
                            : MountainCablewayStationKind.Return);
                var block = new MaterialPropertyBlock();
                int baseColor = Shader.PropertyToID("_BaseColor");

                Assert.That(result.Cabins, Is.Not.Empty);
                foreach (Transform cabin in result.Cabins)
                {
                    foreach (string paneName in paneNames)
                    {
                        Transform pane = cabin.Find(paneName);
                        Assert.That(
                            pane,
                            Is.Not.Null,
                            $"'{paneName}' is missing.");
                        var renderer = pane.GetComponent<Renderer>();
                        Assert.That(renderer, Is.Not.Null);

                        Material material = renderer.sharedMaterial;
                        Assert.That(
                            material.shader.name,
                            Is.EqualTo("Bar Promenade/Home Window Glass"),
                            "A pane the hero rides behind must be glazing, " +
                            "not the lamp lens material.");
                        Assert.That(
                            material.GetTag("RenderType", false),
                            Is.EqualTo("Transparent"));
                        Assert.That(
                            material.renderQueue,
                            Is.GreaterThanOrEqualTo(3000),
                            "Glass has to draw after the opaque world.");

                        // The tint rides the PER-RENDERER block. Writing it on
                        // the material would repaint the cafe's window walls
                        // and the hero's own balcony, which wear the same one.
                        renderer.GetPropertyBlock(block);
                        Assert.That(
                            block.GetColor(baseColor).a,
                            Is.EqualTo(0.24f).Within(0.001f),
                            "The pane is opaque again.");
                        Assert.That(
                            renderer.shadowCastingMode,
                            Is.EqualTo(ShadowCastingMode.Off));

                        // A closed box, never a quad. The shader is `Cull
                        // Back`: flattened to a plane this would look right
                        // from the platform and be INVISIBLE from the bench,
                        // which is the church vault's lesson in a smaller
                        // room.
                        Assert.That(
                            pane.GetComponent<MeshFilter>()
                                .sharedMesh.vertexCount,
                            Is.EqualTo(24),
                            "The pane must stay a closed box.");
                    }

                    // The outboard face is the doorway, by omission.
                    Assert.That(
                        cabin.Find("Cabin Right Window"),
                        Is.Null);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The glazing is a SHARED runtime singleton. Retuning the cabin by
        /// writing to it would silently repaint the cafe's three window walls,
        /// its boiler sight glass, and the hero's own balcony.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void CabinGlass_DoesNotMutateTheSharedMaterial()
        {
            Color before =
                HomeBalconyResources.GlassMaterial.GetColor("_BaseColor");
            var host = new GameObject("Cabin Glass Sharing Test Host");
            try
            {
                MountainCablewayWorldBuilder.Build(
                    host.transform,
                    MountainCableway());
                MountainCablewayWorldBuilder.Build(
                    host.transform,
                    VillageCableway(),
                    MountainCablewayStationKind.Return);

                Color after =
                    HomeBalconyResources.GlassMaterial.GetColor("_BaseColor");
                Assert.That(after.r, Is.EqualTo(before.r).Within(0.001f));
                Assert.That(after.g, Is.EqualTo(before.g).Within(0.001f));
                Assert.That(after.b, Is.EqualTo(before.b).Within(0.001f));
                Assert.That(
                    after.a,
                    Is.EqualTo(before.a).Within(0.001f),
                    "The cabin retuned the shared glazing.");
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

                // Three continuous bays now. The old opening was on the
                // station's CENTRE line, four metres from anywhere a person
                // boards; leaving it there would be a second way through the
                // fence, leading to the drive.
                Transform[] rails = parts
                    .Where(t => t.name == "Physical Boarding Rail")
                    .ToArray();
                Assert.That(rails, Has.Length.EqualTo(6));

                // Nothing solid stands in the boarding lane. Measured as an
                // OFFSET IN THE STATION FRAME rather than as a distance to
                // the dock: the fence is four metres back from the dock along
                // the line, so a distance test passes however far into the
                // lane a rail reaches.
                float inner = plan.BoardingPlatformInnerOffset;
                float outer = plan.BoardingPlatformOuterOffset;
                foreach (Transform part in parts)
                {
                    if (part.name != "Physical Boarding Rail" &&
                        part.name != "Physical Boarding Post")
                    {
                        continue;
                    }

                    float railInner =
                        part.localPosition.x - part.localScale.x * 0.5f;
                    float railOuter =
                        part.localPosition.x + part.localScale.x * 0.5f;
                    Assert.That(
                        railInner >= outer || railOuter <= inner,
                        Is.True,
                        $"'{part.name}' reaches into the boarding lane.");
                }

                // The sign hangs on the fence, not across the way in.
                Transform sign = parts.First(t =>
                    t.name == "Faded Sign - Boarding Closed");
                Assert.That(
                    sign.localPosition.x + sign.localScale.x * 0.5f,
                    Is.LessThanOrEqualTo(inner),
                    "A board at chest height across the only way in is one " +
                    "the hero walks through.");
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
