using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The Ferryman under a running frame loop, which is where the three
    /// things that cannot be proved on paper live: the coin's world pose
    /// against the hand that is throwing it, the cloth coat existing at all,
    /// and the whole point of him - that saying yes puts him behind the
    /// wheel of the car he has been sitting on.
    /// </summary>
    public sealed class LastRouteFerrymanPlayModeTests
    {
        /// <summary>The coin is written every frame from the palm, so a
        /// millimetre is generous rather than tight.</summary>
        private const float CoinTolerance = 0.001f;

        /// <summary>How close his pelvis has to land to the drawn driver
        /// seat. Centimetres, because the solve is exact and anything
        /// larger would be hiding a real offset.</summary>
        private const float SeatTolerance = 0.02f;

        /// <summary>
        /// Ten seconds at the pinned rate. Getting into the car is no longer
        /// a three-quarter-second cut: it is a one-second drop, a walk of
        /// four or so round the nose, and two and a half seconds of door,
        /// seat and door again.
        /// </summary>
        private const int BoardingSteps = 600;

        /// <summary>
        /// Long enough to cover the whole four-second wait loop at the
        /// pinned rate below, so the throw is guaranteed to fall inside
        /// the sampled window rather than usually falling inside it.
        /// </summary>
        private const int CoinSteps = 300;

        /// <summary>
        /// And long enough to cover the wait loop even at the slowest
        /// playback speed his stance may be seeded with - the kicks are
        /// two events in four authored seconds, so a window that only
        /// usually contains them would be a flaky test rather than a
        /// contract.
        /// </summary>
        private const int IdleSteps = 520;

        /// <summary>
        /// Batch mode runs frames as fast as it can, which makes
        /// `Time.deltaTime` a millisecond or two and turns "wait 240
        /// frames" into "wait most of a second". Everything about this
        /// character is timed in seconds - a four-second wait loop, a
        /// three-quarter-second board - so the clock is pinned and the
        /// counts above mean what they say.
        /// </summary>
        private const float PinnedFrameSeconds = 1f / 60f;

        [SetUp]
        public void PinTheClock()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
        }

        [TearDown]
        public void ReleaseTheClock()
        {
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator Ferryman_PerchesOnTheBonnetWithHisCoinAndHisCoat()
        {
            var root = new GameObject("Ferryman Perch Test");
            try
            {
                Harness harness = BuildHarness(root.transform);
                LastRouteFerrymanPresentation ferryman = harness.Ferryman;

                Assert.That(
                    ferryman.Phase,
                    Is.EqualTo(LastRouteFerrymanPhase.Waiting));

                // Sitting ON the bonnet, proved against the car's OWN seat
                // anchor rather than against the placement maths.
                //
                // The model origin is the sole plane of the bind pose, and
                // the perch is not the bind pose - his knees are up on a
                // car - so putting the root on the soles anchor left him
                // hanging in the air with his coat draped on nothing. The
                // independent check is his pelvis: the drawn pose keeps the
                // underside of his hips 0.5077 m over his soles and the car
                // draws its bonnet 0.505 m over its bumper, so if the boots
                // are down where they belong the backside lands on the
                // metal. A few centimetres of tolerance for the pelvis
                // bone riding just inside the hips.
                var registry = ferryman
                    .GetComponentInChildren<CityPedestrianAssetRegistry>(
                        true);
                Assert.That(registry, Is.Not.Null);
                Assert.That(
                    Mathf.Abs(
                        registry.Pelvis.position.y -
                        harness.Car.PerchSeatAnchor.position.y),
                    Is.LessThan(0.06f),
                    $"He is perched at {registry.Pelvis.position.y:0.###} " +
                    $"while the bonnet is at " +
                    $"{harness.Car.PerchSeatAnchor.position.y:0.###}.");

                // And his boots are on the bumper, not above it.
                Assert.That(
                    Mathf.Abs(
                        Mathf.Min(
                            registry.LeftFoot.position.y,
                            registry.RightFoot.position.y) -
                        harness.Car.PerchSolesAnchor.position.y),
                    Is.LessThan(0.20f),
                    "His ankles must sit a boot's height over the bumper.");

                // Facing out over the nose, which is the side a player
                // walks up on.
                Vector3 outward =
                    harness.Car.PerchSolesAnchor.position -
                    harness.Car.PerchSeatAnchor.position;
                outward.y = 0f;
                Assert.That(
                    Vector3.Dot(
                        ferryman.transform.forward,
                        outward.normalized),
                    Is.GreaterThan(0.95f),
                    "He is meant to be looking at whoever walks up.");

                var coin = root.GetComponentInChildren<
                    LastRouteFerrymanCoin>(true);
                Assert.That(coin, Is.Not.Null);
                Assert.That(coin.IsInitialized, Is.True);

                var coat = root.GetComponentInChildren<
                    LastRouteFerrymanCoat>(true);
                Assert.That(coat, Is.Not.Null);
                Assert.That(coat.IsInitialized, Is.True);
                Assert.That(
                    coat.LeftFlap.GetComponent<Cloth>(),
                    Is.Not.Null,
                    "The coat is meant to be real cloth.");
                Assert.That(
                    coat.RightFlap.GetComponent<Cloth>(),
                    Is.Not.Null,
                    "The coat is meant to be real cloth.");

                // Two flaps, one either side of him. A single sheet in
                // front was tried and rendered as a signboard propped
                // against his shins - so the thing that matters is that
                // these are apart, and apart across him rather than along.
                Vector3 across =
                    coat.RightFlap.position - coat.LeftFlap.position;
                Assert.That(
                    across.magnitude,
                    Is.GreaterThan(0.2f),
                    "The two coat flaps have collapsed into one slab.");
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        across.normalized,
                        ferryman.transform.forward)),
                    Is.LessThan(0.25f),
                    "The flaps must be beside him, not one behind the " +
                    "other.");

                // The rigid stub it replaced must be hidden, or the two are
                // drawn one inside the other.
                var anchors = root.GetComponentInChildren<
                    LastRouteFerrymanRigAnchors>(true);
                Assert.That(anchors, Is.Not.Null);
                Assert.That(
                    anchors.CoatHemRenderer.enabled,
                    Is.False,
                    "The drawn hem stub must give way to the cloth.");

                yield return null;
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator Coin_StaysExactlyWhereTheArcSaysItIs()
        {
            var root = new GameObject("Ferryman Coin Test");
            try
            {
                Harness harness = BuildHarness(root.transform);
                var coin = root.GetComponentInChildren<
                    LastRouteFerrymanCoin>(true);
                Transform coinTransform =
                    coin.transform.GetChild(0);

                bool sawAirborne = false;
                bool sawInHand = false;
                for (int step = 0; step < CoinSteps; step++)
                {
                    yield return null;

                    float normalizedTime = harness.Ferryman.NormalizedTime;
                    Vector3 expected =
                        coin.ResolveWorldPosition(normalizedTime);
                    Assert.That(
                        Vector3.Distance(coinTransform.position, expected),
                        Is.LessThan(CoinTolerance),
                        $"The coin drifted off the arc at " +
                        $"{normalizedTime:0.####}.");

                    if (coin.IsAirborne)
                    {
                        sawAirborne = true;
                    }
                    else
                    {
                        sawInHand = true;
                    }
                }

                // A coin that is always in the hand, or never in it, would
                // pass every assertion above and still be wrong.
                Assert.That(
                    sawAirborne,
                    Is.True,
                    "He never threw it.");
                Assert.That(
                    sawInHand,
                    Is.True,
                    "He never caught it.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator SayingYes_PutsHimBehindHisOwnWheel()
        {
            var root = new GameObject("Ferryman Boarding Test");
            try
            {
                Harness harness = BuildHarness(root.transform);
                LastRouteFerrymanInteraction interaction =
                    root.GetComponentInChildren<
                        LastRouteFerrymanInteraction>(true);
                Assert.That(interaction, Is.Not.Null);
                Assert.That(interaction.IsInitialized, Is.True);

                // The cat's menu, with the cat's requirement removed: he
                // asks for an answer, and an answer is not carried.
                Assert.That(
                    interaction.Definition.HasRequirement,
                    Is.False,
                    "Nothing in the inventory may gate leaving the city.");

                Assert.That(
                    interaction.TryOpen(harness.Player.Interactor),
                    Is.True,
                    "The Ferryman would not open his menu.");
                Assert.That(
                    harness.TargetInteraction.State,
                    Is.EqualTo(InventoryTargetInteractionState.Choice));

                // Взаимодействовать -> "Уехать из города?" -> Да.
                harness.TargetInteraction.SelectChoice(
                    InventoryTargetInteractionChoice.Interact);
                harness.TargetInteraction.Confirm();
                Assert.That(
                    harness.TargetInteraction.State,
                    Is.EqualTo(
                        InventoryTargetInteractionState.Confirmation),
                    "Choosing to interact must ask before it acts.");

                harness.TargetInteraction.SelectConfirmation(true);
                harness.TargetInteraction.Confirm();

                Assert.That(
                    harness.Ferryman.Phase,
                    Is.EqualTo(LastRouteFerrymanPhase.Dismounting),
                    "Saying yes must get him off the bonnet immediately.");

                // Everything the beat is made of, watched as it happens:
                // the four phases in order, his boots reaching the ground,
                // the car answering on its springs, and the driver's door
                // actually opening and actually shutting again.
                var seen = new System.Collections.Generic.List<
                    LastRouteFerrymanPhase>
                {
                    LastRouteFerrymanPhase.Dismounting
                };
                float lowestRoot = float.MaxValue;
                float peakRock = 0f;
                float widestDoor = 0f;
                float groundY = harness.Car.transform.position.y;
                for (int step = 0;
                     step < BoardingSteps && !harness.Ferryman.IsDriving;
                     step++)
                {
                    yield return null;
                    LastRouteFerrymanPhase phase = harness.Ferryman.Phase;
                    if (seen[seen.Count - 1] != phase)
                    {
                        seen.Add(phase);
                    }

                    // Only while he is on his own two feet. His seated root
                    // is the BIND pose's sole plane and a seated pose has
                    // no soles on it, so the driving root legitimately sits
                    // 0.18 m under the floor pan - sampling it here would
                    // measure the cabin rather than the lot.
                    if (phase == LastRouteFerrymanPhase.Dismounting ||
                        phase == LastRouteFerrymanPhase.WalkingToDoor)
                    {
                        lowestRoot = Mathf.Min(
                            lowestRoot,
                            harness.Ferryman.transform.position.y);
                    }

                    peakRock = Mathf.Max(
                        peakRock,
                        Mathf.Abs(
                            harness.Suspension.Model.PitchDegrees));
                    widestDoor = Mathf.Max(
                        widestDoor,
                        harness.Doors.DriverOpenness);
                }

                Assert.That(
                    harness.Ferryman.IsDriving,
                    Is.True,
                    "He never arrived behind the wheel.");
                Assert.That(
                    seen,
                    Is.EqualTo(new[]
                    {
                        LastRouteFerrymanPhase.Dismounting,
                        LastRouteFerrymanPhase.WalkingToDoor,
                        LastRouteFerrymanPhase.Boarding,
                        LastRouteFerrymanPhase.Driving
                    }),
                    "He drops onto the lot, walks round to his own door and " +
                    "gets in - in that order and without skipping any of it.");

                // He was on the ground, not gliding over it. The stand pose
                // sits its soles on the root plane, so his root reaching the
                // lot IS his boots reaching it.
                Assert.That(
                    lowestRoot,
                    Is.EqualTo(groundY).Within(0.05f),
                    "He never actually touched the ground between the " +
                    "bonnet and the seat.");
                Assert.That(
                    peakRock,
                    Is.GreaterThan(0.2f),
                    "The car has to answer when his weight leaves the " +
                    "bonnet and again when it lands in the seat.");
                Assert.That(
                    harness.Suspension.Model.IsSettled ||
                    Mathf.Abs(harness.Suspension.Model.PitchDegrees) <
                        peakRock,
                    Is.True,
                    "and it has to stop rocking afterwards.");
                Assert.That(
                    widestDoor,
                    Is.GreaterThan(0.99f),
                    "He opens the driver's door rather than passing " +
                    "through it.");
                Assert.That(
                    harness.Doors.DriverOpenness,
                    Is.LessThan(0.01f),
                    "and shuts it behind him.");

                // And he arrived ON the seat rather than beside it. This is
                // the assertion the whole pelvis solve exists for.
                var registry = harness.Ferryman
                    .GetComponentInChildren<CityPedestrianAssetRegistry>(
                        true);
                Assert.That(
                    Vector3.Distance(
                        registry.Pelvis.position,
                        harness.Car.DriverSeatAnchor.position),
                    Is.LessThan(SeatTolerance),
                    "His pelvis is not on the drawn driver's seat.");

                // He can no longer be talked to: there is nobody on the
                // bonnet.
                Assert.That(
                    interaction.CanInteract(harness.Player.Interactor),
                    Is.False);

                // And the coin has gone with him rather than being left
                // hanging in the air where his hand used to be.
                var coin = root.GetComponentInChildren<
                    LastRouteFerrymanCoin>(true);
                Assert.That(coin.IsAirborne, Is.False);
                Assert.That(
                    coin.transform.GetChild(0).gameObject.activeSelf,
                    Is.False);
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator PassengerSeat_OpensOnlyOnceHeIsBehindTheWheel()
        {
            var root = new GameObject("Ferryman Passenger Seat Test");
            try
            {
                Harness harness = BuildHarness(root.transform);
                // Fetched before it is added, exactly as the car's own
                // factory does it: the controller disallows duplicates, so
                // a blind AddComponent on a hero who already has one hands
                // back null rather than a second component.
                //
                // The camera comes from the harness by hand, too. Its
                // Camera component is disabled so the test draws nothing,
                // and a scene search for one therefore finds nothing.
                var controller = harness.Player.GameObject
                    .GetComponent<PlayerAnimatedInteractionController>();
                if (controller == null)
                {
                    controller = harness.Player.GameObject
                        .AddComponent<PlayerAnimatedInteractionController>();
                }

                controller.Initialize(harness.Player, harness.Camera);

                LastRouteCarSeatPlan seat = LastRouteCarSeatPlan.Create(
                    harness.Car,
                    harness.Car.transform.position.y);
                Assert.That(seat.IsPresent, Is.True);

                var seatObject = new GameObject("Passenger Seat");
                seatObject.transform.SetParent(root.transform, false);
                var interaction = seatObject
                    .AddComponent<LastRouteCarSeatInteraction>();
                interaction.Initialize(
                    harness.Player,
                    controller,
                    seat,
                    harness.Car);
                interaction.AttachFerryman(harness.Ferryman);

                // The hero stands on the dock, so height is never the thing
                // refusing him.
                harness.Player.Motor.Teleport(seat.EntryRootPosition);
                harness.Player.GameObject.transform.rotation =
                    seat.EntryRotation;
                yield return null;

                Assert.That(
                    interaction.IsInvited,
                    Is.False,
                    "The man who owns the car is still sitting on it.");
                Assert.That(
                    interaction.CanInteract(harness.Player.Interactor),
                    Is.False,
                    "The passenger seat is his to offer, and he has not " +
                    "offered it.");

                // He says yes, and the whole beat plays out.
                Assert.That(
                    harness.Ferryman.TryBeginBoarding(),
                    Is.True);
                for (int step = 0;
                     step < BoardingSteps && !harness.Ferryman.IsDriving;
                     step++)
                {
                    yield return null;
                }

                Assert.That(harness.Ferryman.IsDriving, Is.True);
                Assert.That(interaction.IsInvited, Is.True);
                Assert.That(
                    interaction.CanInteract(harness.Player.Interactor),
                    Is.True,
                    "Once he is behind the wheel the seat beside him is " +
                    "real.");

                // And getting in opens the door he gets in through.
                interaction.Interact(harness.Player.Interactor);
                float widest = 0f;
                for (int step = 0; step < BoardingSteps; step++)
                {
                    yield return null;
                    widest = Mathf.Max(widest, harness.Doors.PassengerOpenness);
                    if (interaction.IsSeated &&
                        harness.Doors.PassengerOpenness <= 0f)
                    {
                        break;
                    }
                }

                Assert.That(
                    interaction.IsSeated,
                    Is.True,
                    "The hero never settled into the passenger seat.");
                Assert.That(
                    widest,
                    Is.GreaterThan(0.99f),
                    "The passenger door has to open for him the way the " +
                    "driver's opens for the Ferryman.");
                Assert.That(
                    harness.Doors.PassengerOpenness,
                    Is.LessThan(0.01f),
                    "and be shut once he is sitting in the car.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator PassengerSeat_FacesTheWayTheCarPoints()
        {
            var root = new GameObject("Ferryman Seat Facing Test");
            try
            {
                Harness harness = BuildHarness(
                    root.transform,
                    buildFerryman: false);

                LastRouteCarSeatPlan seat = LastRouteCarSeatPlan.Create(
                    harness.Car,
                    harness.Car.transform.position.y);
                Assert.That(seat.IsPresent, Is.True);

                // The regression this exists for was silent. The seat used
                // to take its facing from the imported Body node, whose
                // forward is very nearly vertical; flattening it produced a
                // zero vector, LookRotation warned into the log and returned
                // IDENTITY, and the hero rode a car with transparent glass
                // while facing world +Z. Asserting "not identity" alone
                // would not have caught it either, so this asserts the
                // actual direction against the drawn cabin.
                Vector3 cabinForward =
                    harness.Car.SteeringWheelPivot.position -
                    harness.Car.DriverSeatAnchor.position;
                cabinForward.y = 0f;
                cabinForward.Normalize();

                Vector3 seatForward = seat.EntryRotation * Vector3.forward;
                Assert.That(
                    Vector3.Dot(seatForward, cabinForward),
                    Is.GreaterThan(0.99f),
                    $"A seated passenger looks {seatForward} while the car " +
                    $"points {cabinForward}.");
                Assert.That(
                    Mathf.Abs(seatForward.y),
                    Is.LessThan(0.01f),
                    "A seated passenger must not look at the sky or the " +
                    "floor pan.");

                yield return null;
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator HeSwingsOneLegAtATimeUnderHisOwnLamp()
        {
            var root = new GameObject("Ferryman Idle Test");
            try
            {
                Harness harness = BuildHarness(root.transform);
                var registry = harness.Ferryman
                    .GetComponentInChildren<CityPedestrianAssetRegistry>(
                        true);
                float bumper = harness.Car.PerchSolesAnchor.position.y;

                // Every staged pedestrian ships as CullUpdateTransforms,
                // which is right in the city and useless here: batch mode
                // draws nothing, so the Animator declines to write a
                // single bone and the whole rig reads back in its bind
                // pose. That is not a harmless quirk to work around - it
                // is the reason a pose assertion can look green while
                // proving nothing, because the bind ankles happen to sit
                // at exactly equal height. Off for this instance only.
                registry.Animator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;

                // The lamp. He is the darkest thing in the game sitting on
                // an unlit lot, so without a fixture of his own he reads as
                // a hole rather than as a man - and it has to hang OUTSIDE
                // the staged art, which is validated to carry no lights at
                // all.
                Transform ferrymanRoot = harness.Ferryman.transform.parent;
                Assert.That(ferrymanRoot, Is.Not.Null);
                Light[] lights =
                    ferrymanRoot.GetComponentsInChildren<Light>(true);
                Assert.That(
                    lights.Length,
                    Is.EqualTo(1),
                    "The Ferryman is meant to have exactly one lamp.");
                Light lamp = lights[0];
                Assert.That(lamp.type, Is.EqualTo(LightType.Point));
                Assert.That(lamp.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(
                    harness.Ferryman
                        .GetComponentInChildren<Light>(true),
                    Is.Null,
                    "The staged art must stay passive; the lamp belongs " +
                    "to the runtime root beside it.");

                // Above his cap rather than under it: the design draws no
                // eyes and leans on the cap brim's own shadow, and a lamp
                // from below is the one angle that would argue with it.
                Assert.That(
                    lamp.transform.position.y,
                    Is.GreaterThan(registry.Head.position.y),
                    "The lamp has to rake down over the brim.");

                // And the legs. He kicks one boot off the bumper at a
                // time, which is what lets the perch stay measured against
                // the other one - so across a whole wait loop the LOWER of
                // the two boots must never rise off the metal, while the
                // higher one must, repeatedly.
                float worstPlanted = 0f;
                float bestSwing = 0f;
                for (int step = 0; step < IdleSteps; step++)
                {
                    yield return null;

                    float left = registry.LeftFoot.position.y - bumper;
                    float right = registry.RightFoot.position.y - bumper;
                    float planted = Mathf.Min(left, right);
                    float swung = Mathf.Max(left, right);
                    worstPlanted = Mathf.Max(worstPlanted, planted);
                    bestSwing = Mathf.Max(bestSwing, swung - planted);
                }

                // Ankles, not soles, so the tolerance is a boot's height
                // plus a centimetre of slack rather than zero.
                Assert.That(
                    worstPlanted,
                    Is.LessThan(0.20f),
                    $"His lower boot rose {worstPlanted:0.###} m over the " +
                    "bumper: both legs are swinging at once.");
                Assert.That(
                    bestSwing,
                    Is.GreaterThan(0.03f),
                    $"The legs only ever parted by {bestSwing:0.###} m - " +
                    "he has stopped swinging them.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator PassiveGuard_RejectsASmuggledCollider()
        {
            var root = new GameObject("Ferryman Passive Guard Test");
            try
            {
                LastRouteFerrymanProvider provider =
                    LastRouteFerrymanProvider.Load();
                Assert.That(
                    provider,
                    Is.Not.Null.And.Property("StagedPrefab").Not.Null);

                // A copy of the staged prefab with exactly one thing wrong
                // with it.
                GameObject tainted = Object.Instantiate(
                    provider.StagedPrefab,
                    root.transform);
                tainted.AddComponent<BoxCollider>();

                // A stand-in provider pointing at it. The field is written
                // by reflection rather than through SerializedObject
                // because this assembly builds for every platform and must
                // not reference UnityEditor; the provider has no setter
                // because in production only the asset build ever fills it.
                var taintedProvider = ScriptableObject
                    .CreateInstance<LastRouteFerrymanProvider>();
                typeof(LastRouteFerrymanProvider)
                    .GetField(
                        "stagedPrefab",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(taintedProvider, tainted);

                Harness harness = BuildHarness(
                    root.transform,
                    buildFerryman: false);

                Assert.That(
                    () => LastRouteFerrymanFactory.Create(
                        root.transform,
                        LastRouteFerrymanPlan.Create(harness.Car),
                        harness.Car,
                        harness.TargetInteraction,
                        1,
                        taintedProvider),
                    Throws.InvalidOperationException,
                    "A staged prefab carrying physics must not spawn.");

                yield return null;
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        // -------------------------------------------------------- harness

        private sealed class Harness
        {
            public PlayerRuntime Player;
            public InventoryTargetInteractionController TargetInteraction;
            public Camera Camera;
            public LastRouteCarAssetRegistry Car;
            public LastRouteCarDoors Doors;
            public LastRouteCarSuspension Suspension;
            public LastRouteFerrymanPresentation Ferryman;
        }

        private static Harness BuildHarness(
            Transform parent,
            bool buildFerryman = true)
        {
            CreateGround(parent);

            var cameraObject = new GameObject("Ferryman Test Camera");
            cameraObject.transform.SetParent(parent, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            var uiObject = new GameObject("Ferryman Test UI");
            uiObject.transform.SetParent(parent, false);
            InteractionPromptView prompt =
                uiObject.AddComponent<InteractionPromptView>();

            PlayerRuntime player = PlayerFactory.Create(
                parent,
                new Vector3(0f, PlayerFactory.GroundedRootOffset, -6f),
                camera,
                new AlwaysWalkableArea(),
                prompt);
            PlayerCameraFollow follow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(
                camera,
                player.GameObject.transform,
                interior: false);

            IntoxicationHudView hud =
                uiObject.AddComponent<IntoxicationHudView>();
            InventoryTargetInteractionController targetInteraction =
                uiObject.AddComponent<
                    InventoryTargetInteractionController>();
            targetInteraction.Initialize(player, follow, hud);

            // The car straight from its prefab rather than through its own
            // placement: where it parks is that feature's problem, and this
            // one is about the man on it.
            GameObject carPrefab = LastRouteCarAssetRegistry.LoadPrefab();
            Assert.That(
                carPrefab,
                Is.Not.Null,
                "The Last Route car prefab must be available.");
            GameObject car = Object.Instantiate(carPrefab, parent);
            car.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            var registry =
                car.GetComponentInChildren<LastRouteCarAssetRegistry>(true);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.IsBound, Is.True);

            // The doors and the springs, raised the way the factory raises
            // them. Skipping them would let every door assertion below pass
            // against a car that has none.
            LastRouteCarFactory.InstallMechanisms(car.transform, registry);

            var harness = new Harness
            {
                Player = player,
                Camera = camera,
                TargetInteraction = targetInteraction,
                Car = registry,
                Doors = car.GetComponent<LastRouteCarDoors>(),
                Suspension = car.GetComponent<LastRouteCarSuspension>()
            };
            Assert.That(harness.Doors, Is.Not.Null);
            Assert.That(harness.Doors.IsInitialized, Is.True);
            Assert.That(harness.Suspension, Is.Not.Null);
            Assert.That(harness.Suspension.IsInitialized, Is.True);

            if (!buildFerryman)
            {
                return harness;
            }

            LastRouteFerrymanPlan plan =
                LastRouteFerrymanPlan.Create(registry);
            Assert.That(
                plan.IsPresent,
                Is.True,
                "A parked car must always carry a Ferryman.");

            harness.Ferryman = LastRouteFerrymanFactory.Create(
                parent,
                plan,
                registry,
                targetInteraction,
                GameSessionState.DefaultCitySeed);
            Assert.That(
                harness.Ferryman,
                Is.Not.Null,
                "The staged Ferryman failed to spawn.");
            return harness;
        }

        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ground.name = "Ferryman Test Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(80f, 1f, 80f);
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
