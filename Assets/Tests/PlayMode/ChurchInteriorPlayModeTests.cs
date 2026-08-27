using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class ChurchInteriorPlayModeTests
    {
        private const string ChurchRootName =
            "[Bar Promenade] Church Interior Runtime";
        private const string DoorRootName =
            "[Bar Promenade] Door Transition Runtime";
        private const string CityRootName =
            "[Bar Promenade] City Runtime";
        private const int RoomColliderCount = 6;
        private const float TimeoutSeconds = 45f;

        [UnityTest]
        public IEnumerator ChurchInterior_BootsAndCompletesDoorRoundTrip()
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.ChurchInterior),
                Is.True);
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.DoorTransition),
                Is.True);
            Assert.That(
                Application.CanStreamedLevelBeLoaded(SceneIds.City),
                Is.True);

            GameSessionState.BeginNewGame();
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.ChurchInterior,
                DoorTransitionDirection.EnterChurch,
                out string enterOperationId);
            Assert.That(accepted, Is.True, enterOperationId);
            GameSessionState.EnterChurch();

            DoorTransitionRoot enteringDoor = null;
            yield return WaitForLoadedRoot(
                SceneIds.DoorTransition,
                DoorRootName,
                (DoorTransitionRoot root) => enteringDoor = root);
            yield return WaitUntil(
                () => enteringDoor.IsInitialized,
                "Entering church door did not initialize.");
            Assert.That(
                enteringDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterChurch));

            ChurchInteriorRoot interior = null;
            yield return WaitForLoadedRoot(
                SceneIds.ChurchInterior,
                ChurchRootName,
                (ChurchInteriorRoot root) => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "ChurchInterior did not finish booting.");

            AssertInteriorContract(interior);
            PlacePlayerAtDoor(interior.Player, interior.Exit);
            Assert.That(
                interior.Exit.CanInteract(interior.Player.Interactor),
                Is.True);
            interior.Exit.Interact(interior.Player.Interactor);
            PlayerDoorActionController action =
                interior.Player.GameObject.GetComponent<
                    PlayerDoorActionController>();
            Assert.That(action, Is.Not.Null);
            Assert.That(action.IsPlaying, Is.True);
            yield return WaitUntil(
                () => SceneTransitionService.IsTransitioning,
                "Church exit DoorUse did not complete.");

            DoorTransitionRoot exitingDoor = null;
            yield return WaitForLoadedRoot(
                SceneIds.DoorTransition,
                DoorRootName,
                (DoorTransitionRoot root) => exitingDoor = root);
            yield return WaitUntil(
                () => exitingDoor.IsInitialized,
                "Exiting church door did not initialize.");
            Assert.That(
                exitingDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.ExitChurch));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.Church));

            CityGameRoot city = null;
            yield return WaitForLoadedRoot(
                SceneIds.City,
                CityRootName,
                (CityGameRoot root) => city = root);
            yield return WaitUntil(
                () => city.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "Church return to City did not settle.");

            Assert.That(city.World.ChurchPlan, Is.Not.Null);
            Vector3 expectedReturn = city.World.ChurchPlan.ReturnPosition;
            Vector3 actualReturn = city.Player.GameObject.transform.position;
            Assert.That(
                Vector2.Distance(
                    new Vector2(actualReturn.x, actualReturn.z),
                    new Vector2(expectedReturn.x, expectedReturn.z)),
                Is.LessThan(0.05f));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            var teleportGround = new CityMapCityTeleportGround(city.Layout);
            Assert.That(
                teleportGround.TryResolveStandingPosition(
                    city.World.ChurchPlan.ModelFootprint.center,
                    out _),
                Is.False,
                "Map teleport must not land inside the church model.");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The City half of the round trip, which the interior test
        /// above never touches: walk in off the street and open the
        /// door. The dock used to be computed from the top of the
        /// forecourt paving, which carries no collider - so it stood
        /// four centimetres above any height the hero could reach, the
        /// door action refused every attempt, and the prompt appeared
        /// and then did nothing at all when it was pressed.
        /// </summary>
        [UnityTest]
        public IEnumerator CityChurchEntrance_OpensForAHeroWalkingInOffTheStreet()
        {
            GameSessionState.BeginNewGame();
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.City,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return WaitUntil(
                () => load.isDone,
                "City did not load.");

            CityGameRoot city = null;
            yield return WaitForLoadedRoot(
                SceneIds.City,
                CityRootName,
                (CityGameRoot root) => city = root);
            yield return WaitUntil(
                () => city.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "City did not finish booting.");

            CityChurchPlan plan = city.World.ChurchPlan;
            Assert.That(plan, Is.Not.Null);
            Assert.That(city.World.Church, Is.Not.Null);

            PlayerRuntime player = city.Player;
            Transform hero = player.GameObject.transform;
            CharacterController controller =
                player.GameObject.GetComponent<CharacterController>();
            float previousCapture = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                player.Motor.Teleport(plan.ReturnPosition);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 20; frame++)
                {
                    controller.Move(Vector3.down * 0.02f);
                    yield return null;
                }

                Assert.That(
                    hero.position.y,
                    Is.EqualTo(plan.ReturnPosition.y).Within(
                        PlayerMotor.InteractionVerticalTolerance),
                    "The City return must settle on the church ground.");

                // Walk the forecourt rather than teleporting to the
                // dock: the approach itself has to be traversable.
                Vector3 doorward = plan.DoorGroundPosition - hero.position;
                doorward.y = 0f;
                doorward.Normalize();
                float remaining = float.PositiveInfinity;
                for (int frame = 0; frame < 600 && remaining > 0.05f; frame++)
                {
                    Vector3 toDock = plan.DoorDockPosition - hero.position;
                    toDock.y = 0f;
                    remaining = toDock.magnitude;
                    controller.Move(
                        (doorward * (3f / 60f)) + (Vector3.down * 0.02f));
                    yield return null;
                }

                Assert.That(
                    remaining,
                    Is.LessThan(0.6f),
                    $"The hero stalled at {hero.position} short of the " +
                    $"church door dock {plan.DoorDockPosition}.");
                Assert.That(
                    player.Interactor.ActiveInteractable,
                    Is.Not.Null,
                    "No interactable at the church door.");
                Assert.That(
                    player.Interactor.ActiveInteractable.PromptKey,
                    Is.EqualTo("interaction.enter_church"));

                ChurchEntrance entrance = city.World.Church;
                Assert.That(
                    entrance.CanInteract(player.Interactor),
                    Is.True);
                entrance.Interact(player.Interactor);

                PlayerDoorActionController action =
                    player.GameObject
                        .GetComponent<PlayerDoorActionController>();
                Assert.That(action, Is.Not.Null);
                Assert.That(
                    action.IsPlaying,
                    Is.True,
                    "Pressing the church door did nothing: the door " +
                    "action refused the dock it was handed.");

                yield return WaitUntil(
                    () => SceneTransitionService.IsTransitioning,
                    "Church enter DoorUse never reached the transition.");

                ChurchInteriorRoot interior = null;
                yield return WaitForLoadedRoot(
                    SceneIds.ChurchInterior,
                    ChurchRootName,
                    (ChurchInteriorRoot root) => interior = root);
                yield return WaitUntil(
                    () => interior.IsInitialized &&
                          !SceneTransitionService.IsTransitioning,
                    "ChurchInterior did not boot after entering.");
            }
            finally
            {
                Time.captureDeltaTime = previousCapture;
            }
        }

        /// <summary>
        /// The church was lit by six lights for a hall of 23 x 44 x 14 m
        /// and the narthex the hero arrives in had none of them, so the
        /// scene opened on a black screen. It now carries a warm candle
        /// layer and a cold daylight layer that trade places on the
        /// clock.
        ///
        /// The intensity assertion is the important one. URP falls off
        /// with the SQUARE of the distance, so the number a fixture
        /// needs is set by how far it has to throw: the two shafts this
        /// replaced were authored at 1.15 for a nine-metre crossing and
        /// delivered about a hundredth of that to the floor, which is
        /// the whole reason the church read as unlit.
        /// </summary>
        [UnityTest]
        public IEnumerator ChurchInterior_LightsBothLayersOnTheClock()
        {
            GameSessionState.BeginNewGame();
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.ChurchInterior,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return WaitUntil(
                () => load.isDone,
                "ChurchInterior did not load.");

            ChurchInteriorRoot interior = null;
            yield return WaitForLoadedRoot(
                SceneIds.ChurchInterior,
                ChurchRootName,
                (ChurchInteriorRoot root) => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized,
                "ChurchInterior did not boot.");

            ChurchInteriorAtmosphere atmosphere = interior.Atmosphere;
            Assert.That(
                atmosphere.WarmLights,
                Has.Length.EqualTo(
                    ChurchInteriorAtmosphere.WarmPracticalCount));
            Assert.That(
                atmosphere.Practicals,
                Has.Length.EqualTo(
                    ChurchInteriorAtmosphere.PracticalLightCount));
            Assert.That(
                atmosphere.WarmLights.All(
                    light =>
                        light != null &&
                        light.type == LightType.Point),
                Is.True);
            Assert.That(
                atmosphere.DaylightGlows,
                Has.Length.EqualTo(
                    ChurchInteriorAtmosphere.DaylightGlowCount));
            Assert.That(
                atmosphere.DaylightGlows.All(
                    light =>
                        light != null &&
                        light.type == LightType.Point),
                Is.True,
                "The glass lights its own reveal, which is a point.");

            // The daylight cones are gone entirely. Ten spots stood in
            // for a sun that could not get into the building; the sun
            // gets in now, and being a parallel source it delivers the
            // same light at three metres and at thirteen, which a point
            // source sized for its pool cannot. What is left is the
            // visible column of air, one per lancet.
            Assert.That(
                atmosphere.LightShafts,
                Has.Length.EqualTo(
                    ChurchInteriorAtmosphere.DaylightShaftCount));
            foreach (ChurchLightShaft beam in atmosphere.LightShafts)
            {
                Assert.That(beam, Is.Not.Null);
                Assert.That(
                    Mathf.Abs(beam.transform.localPosition.x),
                    Is.EqualTo(
                        ChurchInteriorAtmosphere.ShaftApertureX)
                        .Within(0.01f),
                    "A column of light must stand in the opening it " +
                    "comes through.");
            }

            foreach (float depth in
                     ChurchInteriorAtmosphere.WindowDepths)
            {
                Assert.That(
                    atmosphere.LightShafts.Count(
                        beam =>
                            Mathf.Abs(
                                beam.transform.localPosition.z -
                                depth) < 0.01f),
                    Is.EqualTo(2),
                    $"Both aisles need a lancet shaft at z={depth}.");
            }

            Assert.That(
                atmosphere.StainedGlass,
                Has.Length.EqualTo(2),
                "The glazing must be addressable per aisle.");

            // Every warm fixture burns: the flames the model owns
            // flicker in light alone, the ones this scene builds move
            // their geometry too.
            Assert.That(
                atmosphere.CandleFlames,
                Has.Length.EqualTo(atmosphere.WarmLights.Length));
            Assert.That(
                atmosphere.CandleFlames.All(flame => flame != null),
                Is.True);
            Assert.That(
                atmosphere.CandleFlames.Count(
                    flame => flame.FlameCount > 0),
                Is.GreaterThanOrEqualTo(12),
                "The sconces, coronas and votive stands must all " +
                "animate real flames.");

            // The votive stands are the first two warm fixtures and
            // carry a full ring each. Their flames are built here
            // rather than authored, because a merged mesh of
            // thirty-two of them cannot move.
            for (int index = 0;
                 index < ChurchInteriorAtmosphere.VotiveStandCentres.Length;
                 index++)
            {
                Assert.That(
                    atmosphere.CandleFlames[index].FlameCount,
                    Is.EqualTo(
                        ChurchInteriorAtmosphere.VotiveCandleCount));
            }

            // A corona is one object, not a heap of parts near each
            // other. Its hoop bars must lie ALONG the circle: laid
            // radially they read as the spokes of a starburst, which
            // is what shipped first. Measured, because the difference
            // is a quarter turn that looks like nothing in the source.
            Transform corona = atmosphere.transform.Find(
                "Nave Corona West Fixture");
            Assert.That(corona, Is.Not.Null);
            var hoopCentre = new Vector3(0f, 0f, -12f);
            int hoopBars = 0;
            int arms = 0;
            int hubs = 0;
            for (int index = 0; index < corona.childCount; index++)
            {
                Transform child = corona.GetChild(index);
                if (child.name == "Corona Arm")
                {
                    arms++;
                    continue;
                }

                if (child.name == "Corona Hub")
                {
                    hubs++;
                    continue;
                }

                if (child.name != "Corona Ring")
                {
                    continue;
                }

                hoopBars++;
                Vector3 outward = child.localPosition - hoopCentre;
                outward.y = 0f;
                Vector3 along = child.localRotation * Vector3.right;
                Assert.That(
                    Mathf.Abs(
                        Vector3.Dot(
                            outward.normalized,
                            along.normalized)),
                    Is.LessThan(0.2f),
                    "A hoop bar is pointing along its own radius, so " +
                    "the corona is a starburst rather than a ring.");
            }

            Assert.That(hoopBars, Is.GreaterThanOrEqualTo(8));
            // The chain has to land on something and that something has
            // to reach the hoop, or the parts only float near together.
            Assert.That(hubs, Is.EqualTo(1));
            Assert.That(arms, Is.GreaterThanOrEqualTo(4));

            ChurchInteriorDayNightController dayNight =
                interior.DayNight;
            Assert.That(dayNight, Is.Not.Null);
            Assert.That(dayNight.IsInitialized, Is.True);

            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(6f * 60f);
            dayNight.RefreshImmediate();
            yield return null;

            Assert.That(dayNight.DayFactor, Is.GreaterThan(0.99f));

            // Solar noon: the sun stands due south, so the south aisle
            // is the sun wall and the north aisle takes nothing at all.
            // The version of this test that this replaced read one
            // light out of ten and could not tell the two sides apart -
            // which is exactly how ten mirrored cones firing at equal
            // strength in both aisles at every hour went unnoticed.
            Assert.That(
                LitShaftCount(atmosphere, -1f),
                Is.Zero,
                "The north aisle cannot take direct sun at noon, or " +
                "at any other hour, in this hemisphere.");
            Assert.That(
                LitShaftCount(atmosphere, 1f),
                Is.EqualTo(
                    ChurchInteriorAtmosphere.WindowDepths.Length),
                "Every south lancet must be throwing a shaft at noon.");

            // The shaded aisle is NOT dark. Its windows take no sun but
            // they pass sky all day, and each is a small diffuse source
            // lifting its own reveal - a north aisle of black slots in
            // lit masonry is what this replaced. It must still lose to
            // the sun wall, clearly, or the two read as one.
            float daySouth = SideIntensity(atmosphere, 1f);
            float dayNorth = SideIntensity(atmosphere, -1f);
            Assert.That(
                dayNorth,
                Is.GreaterThan(daySouth * 0.25f),
                "The shaded aisle must still have daylight of its own.");
            Assert.That(
                daySouth,
                Is.GreaterThan(dayNorth * 1.8f),
                "The sun wall must outshine the shaded one.");

            // And the glazing on that wall has to read as lit glass
            // rather than a hole: above the church grade's own bloom
            // threshold, and well under the wall the sun is on.
            Assert.That(
                PaneColor(atmosphere, 0).maxColorComponent,
                Is.GreaterThan(0.62f),
                "A shaded pane must still look like a window.");

            // The glazing lives on the same clock. A sunlit pane is
            // the brightest thing in a church; a shaded one is not.
            Color daySouthGlass = PaneColor(atmosphere, 1);
            Color dayNorthGlass = PaneColor(atmosphere, 0);
            // Twice as bright, not four times: the shaded pane was
            // deliberately lifted so it reads as a window rather than
            // a hole, and the gap narrowed with it.
            Assert.That(
                daySouthGlass.grayscale,
                Is.GreaterThan(dayNorthGlass.grayscale * 1.7f),
                "The sunlit glass must outshine the shaded glass.");

            float dayWarm = atmosphere.CandleFlames[0].BaseIntensity;
            Color dayAmbient = RenderSettings.ambientLight;

            GameSessionState.AdvanceGameTime(12f * 60f);
            dayNight.RefreshImmediate();
            yield return null;

            Assert.That(dayNight.DayFactor, Is.LessThan(0.01f));
            float nightWarm = atmosphere.CandleFlames[0].BaseIntensity;
            Color nightAmbient = RenderSettings.ambientLight;
            Assert.That(
                SideIntensity(atmosphere, 1f),
                Is.LessThan(daySouth * 0.2f),
                "The daylight must die with the day.");
            Assert.That(
                LitShaftCount(atmosphere, 1f),
                Is.Zero,
                "No column of sunlight stands in a church at night.");
            Assert.That(
                PaneColor(atmosphere, 1).grayscale,
                Is.LessThan(daySouthGlass.grayscale * 0.2f),
                "A window at three in the morning is a dark hole.");
            Assert.That(
                nightWarm,
                Is.GreaterThan(dayWarm),
                "The candles take the room back after dark.");
            Assert.That(
                nightAmbient.grayscale,
                Is.LessThan(dayAmbient.grayscale));

            // Dark, but never the black screen this replaced: the warm
            // layer and the ambient floor still have to add up to a
            // room the hero can walk through.
            Assert.That(nightWarm, Is.GreaterThan(0.5f));
            Assert.That(nightAmbient.grayscale, Is.GreaterThan(0.1f));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The brightest daylight cone on one side of the nave.
        /// </summary>
        /// <summary>
        /// How much daylight one aisle is taking, read off the glow
        /// that lights its own reveals. The glows are the only lights
        /// left in the daylight layer, and they are driven by the same
        /// shared WindowWeight as the beams and the glazing, so any one
        /// of the three answers the question.
        /// </summary>
        private static float SideIntensity(
            ChurchInteriorAtmosphere atmosphere,
            float side)
        {
            return atmosphere.DaylightGlows
                .Where(light =>
                    light != null &&
                    Mathf.Sign(light.transform.localPosition.x) == side)
                .Select(light => light.intensity)
                .DefaultIfEmpty(0f)
                .Max();
        }

        /// <summary>
        /// How many visible columns of light are standing on one side.
        /// </summary>
        private static int LitShaftCount(
            ChurchInteriorAtmosphere atmosphere,
            float side)
        {
            return atmosphere.LightShafts.Count(
                beam =>
                    beam != null &&
                    beam.IsLit &&
                    Mathf.Approximately(beam.WallSide, side));
        }

        /// <summary>
        /// The colour actually written onto a glazing renderer. It goes
        /// through a MaterialPropertyBlock, so it has to be read back
        /// the same way rather than off the shared material.
        /// </summary>
        private static Color PaneColor(
            ChurchInteriorAtmosphere atmosphere,
            int index)
        {
            var block = new MaterialPropertyBlock();
            atmosphere.StainedGlass[index].GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
        }

        private static void AssertInteriorContract(
            ChurchInteriorRoot interior)
        {
            Assert.That(interior, Is.Not.Null);
            Assert.That(interior.World, Is.Not.Null);
            Assert.That(interior.World.Registry, Is.Not.Null);
            Assert.That(
                interior.World.Registry.Kind,
                Is.EqualTo(ChurchAssetKind.Interior));
            Assert.That(
                interior.World.Registry.BuildSignature,
                Is.Not.Empty);
            Assert.That(interior.Player.GameObject, Is.Not.Null);
            Assert.That(interior.Exit, Is.Not.Null);
            Assert.That(interior.Inventory, Is.Not.Null);
            Assert.That(interior.Journal, Is.Not.Null);
            Assert.That(interior.PauseMenu, Is.Not.Null);

            int blockingFixtureCount = interior.Layout.Fixtures.Count(
                fixture => fixture.BlocksMovement);
            Assert.That(
                interior.World.GameplayColliders,
                Has.Count.EqualTo(
                    RoomColliderCount + blockingFixtureCount));
            Assert.That(
                interior.World.GameplayColliders.All(
                    collider =>
                        collider != null &&
                        collider.enabled &&
                        collider.transform.IsChildOf(
                            interior.World.CollisionRoot)),
                Is.True);
            Assert.That(
                interior.World.Registry.GetComponentsInChildren<Collider>(
                    true),
                Is.Empty,
                "The imported church prefab must remain passive.");
        }

        private static void PlacePlayerAtDoor(
            PlayerRuntime player,
            Component door)
        {
            PlayerDoorActionTarget action =
                door.GetComponent<PlayerDoorActionTarget>();
            Assert.That(action, Is.Not.Null, door.name);
            Assert.That(action.IsConfigured, Is.True, door.name);
            player.Motor.Teleport(action.Plan.EntryRootPosition);
            player.GameObject.transform.rotation =
                action.Plan.EntryRotation;
            Physics.SyncTransforms();
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static IEnumerator WaitForLoadedRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                T root = FindExactRoot<T>(scene, exactRootName);
                if (scene.name == sceneName && root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' did not create root " +
                $"'{exactRootName}'.");
        }

        private static T FindExactRoot<T>(
            Scene scene,
            string exactRootName)
            where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    return roots[index].GetComponent<T>();
                }
            }

            return null;
        }
    }
}
