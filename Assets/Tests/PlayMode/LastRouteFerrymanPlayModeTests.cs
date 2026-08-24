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

        private const int BoardingSteps = 240;

        /// <summary>
        /// Long enough to cover the whole four-second wait loop at the
        /// pinned rate below, so the throw is guaranteed to fall inside
        /// the sampled window rather than usually falling inside it.
        /// </summary>
        private const int CoinSteps = 300;

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

                // On the bumper his boots were authored for, not floating
                // beside it.
                Assert.That(
                    Vector3.Distance(
                        ferryman.transform.position,
                        harness.Car.PerchSolesAnchor.position),
                    Is.LessThan(0.001f),
                    "His root is his soles and they belong on the bumper.");

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
                    Is.EqualTo(LastRouteFerrymanPhase.Boarding),
                    "Saying yes must get him off the bonnet immediately.");

                for (int step = 0;
                     step < BoardingSteps && !harness.Ferryman.IsDriving;
                     step++)
                {
                    yield return null;
                }

                Assert.That(
                    harness.Ferryman.IsDriving,
                    Is.True,
                    "He never arrived behind the wheel.");

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
            public LastRouteCarAssetRegistry Car;
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

            var harness = new Harness
            {
                Player = player,
                TargetInteraction = targetInteraction,
                Car = registry
            };

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
