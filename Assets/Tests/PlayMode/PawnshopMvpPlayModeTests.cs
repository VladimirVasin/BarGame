using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PawnshopMvpAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.PawnshopAssetSetup, BarPromenade.Editor", true)
                .GetMethod("EnsureAssetsReady", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.CityBenchAccessStairAssetSetup, BarPromenade.Editor", true)
                .GetMethod("EnsureAssetsReady", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.ProjectSceneSetup, BarPromenade.Editor", true)
                .GetMethod("ConfigurePawnshopScene", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
            if (Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_PAWNSHOP") == "1")
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("FocusGameViewForCapture", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    [PrebuildSetup(typeof(PawnshopMvpAssetsSetup))]
    public sealed partial class PawnshopMvpPlayModeTests
    {
        private const float Step = 1f / 30f;
        private const float Timeout = 45f;

        [UnityTest]
        public IEnumerator CityPawnshop_EnterOpenBothStubsAndReturn()
        {
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            bool previousFocus = GameInput.MovementFocused;
            float previousStep = Time.captureDeltaTime;
            float previousScale = Time.timeScale;
            object cursor = new object();
            PlayerCameraFollow follow = null;
            PlayerCameraFollow exteriorFollow = null;
            PawnshopInteriorRoot interior = null;
            bool capture = Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_PAWNSHOP") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            try
            {
                GameInput.HandleApplicationFocus(true);
                GameCursor.Capture(cursor, CursorLockMode.Locked, false, 0);
                Time.captureDeltaTime = Step;
                Time.timeScale = 1f;
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                yield return WaitFor(() =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !SceneTransitionService.IsTransitioning;
                }, "City did not finish preparing the pawnshop approach.");

                Assert.That(CityPawnshopPlan.TryCreate(city.Layout, out var site), Is.True);
                Transform exterior = city.World.Root.transform.Find("Pawnshop Exterior");
                Assert.That(exterior, Is.Not.Null);
                Assert.That(Vector3.Distance(exterior.position, site.Origin), Is.LessThan(.005f));
                Assert.That(Quaternion.Angle(exterior.rotation, site.Rotation), Is.LessThan(.01f));
                var entrance = exterior.GetComponentInChildren<PawnshopEntrance>();
                Assert.That(entrance, Is.Not.Null);
                var entryAction = entrance.GetComponent<PlayerDoorActionTarget>();
                Assert.That(entryAction != null && entryAction.IsConfigured, Is.True);
                Assert.That(city.World.WalkableArea.Contains(site.Origin, .32f), Is.False,
                    "Neither pedestrian navigation nor map arrival may put the hero inside the exterior shell.");
                if (new CityMapCityTeleportGround(city.Layout)
                    .TryResolveStandingPosition(new Vector2(site.Origin.x, site.Origin.z), out Vector3 safeArrival))
                    Assert.That(city.World.WalkableArea.Contains(safeArrival, .32f), Is.True,
                        "A clamped map request must land outside the actual rotated building with body clearance.");

                PlayerRuntime player = city.Player;
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                exteriorFollow = follow;
                var crossings = CityPawnshopCrosswalkPlan.ForLayout(city.Layout).Crossings;
                Assert.That(crossings.Count, Is.EqualTo(2), "Both adjacent roads need a zebra onto the island.");
                Assert.That(crossings.Select(crossing => crossing.Lane), Is.EquivalentTo(new[] { 4, 5 }));
                foreach (var crossing in crossings)
                {
                    Assert.That(crossing.Width, Is.EqualTo(2.4f));
                    Assert.That(crossing.Stripes.Count, Is.EqualTo(6));
                    foreach (RuntimeOrientedBox stripe in crossing.Stripes)
                        Assert.That((stripe.Rotation * Vector3.up).y, Is.GreaterThan(.8f),
                            "The zebra's imported paint face must point upward on the real road.");
                    Assert.That(crossing.ApproachPath.Count, Is.GreaterThanOrEqualTo(crossing.WalkPath.Count));
                    player.Motor.Teleport(crossing.ApproachPath[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    for (int i = 0; i < 5; i++) yield return null;
                    if (capture)
                    {
                        Vector3 away = crossing.WalkPath[0] - crossing.Center;
                        away.y = 0f;
                        Vector3 eye = crossing.WalkPath[0] + away.normalized * 2f + Vector3.up * 2.1f;
                        Vector3 target = Vector3.Lerp(crossing.Center, site.DoorWorld, .35f) + Vector3.up * .35f;
                        follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 62f);
                        yield return null;
                        yield return CaptureExteriorGameView(city, 0, "00-crossing-" + crossing.Lane + "-night");
                    }
                    foreach (Vector3 point in crossing.ApproachPath)
                        yield return WalkTo(player, point + Vector3.up * PlayerFactory.GroundedRootOffset);
                    for (int point = crossing.ApproachPath.Count - 1; point >= 0; point--)
                        yield return WalkTo(player, crossing.ApproachPath[point] + Vector3.up * PlayerFactory.GroundedRootOffset);
                }
                player.Motor.Teleport(site.ReturnWorld);
                Physics.SyncTransforms();
                for (int i = 0; i < 5; i++) yield return null;
                float[] nightIntensities = null;
                void CheckNightLights() => nightIntensities = AssertExteriorLighting(city, exterior);
                void CheckDayLights()
                {
                    float[] day = AssertExteriorLighting(city, exterior);
                    Assert.That(nightIntensities, Is.Not.Null);
                    for (int index = 0; index < day.Length; index++)
                        Assert.That(day[index], Is.GreaterThanOrEqualTo(nightIntensities[index] * (2f / 3f) - .01f),
                            "The practicals retain the shared daylight fixture floor.");
                }
                if (capture)
                {
                    Vector3 eye = new Vector3(-45.5f, site.Origin.y + 1.72f, -301.3f);
                    follow.SetFixedPose(eye, Quaternion.LookRotation(site.Origin + Vector3.up * 1.65f - eye), 65f);
                    yield return null;
                    yield return CaptureExteriorGameView(city, 0, "01-facade-from-hero-night", CheckNightLights);
                    eye = site.ReturnWorld + site.DoorOutward * 4f + site.Rotation * Vector3.right * 5f + Vector3.up * 2.2f;
                    follow.SetFixedPose(eye, Quaternion.LookRotation(site.Origin + Vector3.up * 1.5f - eye), 60f);
                    yield return null;
                    yield return CaptureExteriorGameView(city, 0, "02-facade-and-street-night");
                    yield return CaptureExteriorGameView(city, 12 * 60, "02-facade-and-street-day", CheckDayLights);
                }
                else
                {
                    yield return CaptureExteriorGameView(city, 0, null, CheckNightLights);
                    yield return CaptureExteriorGameView(city, 12 * 60, null, CheckDayLights);
                }

                yield return WalkTo(player, entryAction.Plan.EntryRootPosition);
                yield return AimAt(player, follow, entrance.InteractionPosition);
                Assert.That(player.Interactor.ActiveInteractable, Is.SameAs(entrance),
                    "The ordinary City door prompt must be discovered from the real approach.");
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.PawnshopInterior,
                    "E at the exterior door did not load the pawnshop.");
                yield return WaitFor(() =>
                {
                    interior = Object.FindAnyObjectByType<PawnshopInteriorRoot>();
                    return interior != null && interior.IsInitialized && !SceneTransitionService.IsTransitioning;
                }, "PawnshopInterior did not initialize.");
                Assert.That(city != null && city.IsDormant, Is.True,
                    "The same City stays dormant behind its new interior.");
                Assert.That(interior.Player.Visual, Is.InstanceOf<Player3DCharacterPresentation>());
                Assert.That(interior.Clerk, Is.Not.Null);
                Assert.That(Vector3.Distance(interior.Clerk.transform.position, new Vector3(0f, 0f, 2f)),
                    Is.LessThan(.08f), "The clerk stands in the separate secure room.");
                Bounds room = BoundsOf(interior.Model);
                Assert.That(room.size.x, Is.InRange(7.4f, 8.3f), "Imported meshes retain authored metres.");
                Assert.That(room.size.z, Is.InRange(8.4f, 9.3f));
                Assert.That(room.size.y, Is.InRange(3f, 4.1f));
                Assert.That(interior.Model.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "The imported model remains passive; the validated room owns collision.");
                Assert.That(interior.Lights.Any(light => light != null && light.enabled && light.intensity > 0f), Is.True);
                Assert.That(interior.LightingPresentation.BounceRegionCount, Is.EqualTo(2));
                if (capture && GraphicsEffectsSettings.CleanReflectionsEnabled)
                    yield return WaitFor(() => interior.LightingPresentation.Reflection.IsReady,
                        "The bounded room reflection must be ready before reviewing its glass and metal.");
                Physics.SyncTransforms();
                BoxCollider barrier = interior.World.SolidColliders.Single(collider =>
                    collider.name.EndsWith("Secure staff barrier", StringComparison.Ordinal));
                foreach (float x in new[] { -3.2f, 0f, 3.2f })
                    Assert.That(barrier.Raycast(new Ray(new Vector3(x, 1.5f, -.2f), Vector3.forward), out _, 2f), Is.True,
                        "The anti-theft partition must close the full room, including beside the counter.");

                player = interior.Player;
                follow = interior.CameraFollow;
                if (capture)
                {
                    // Stand the camera just ahead of the arrival hero so his
                    // near-plane head cannot hide the room being reviewed.
                    follow.SetFixedPose(new Vector3(0f, 1.64f, -2.95f),
                        Quaternion.LookRotation(new Vector3(0f, 1.25f, 1.1f) - new Vector3(0f, 1.64f, -2.95f)), 65f);
                    yield return null;
                    yield return CaptureGameView("03-entrance-to-counter");
                    follow.SetFixedPose(new Vector3(.45f, 1.58f, -1.05f),
                        Quaternion.LookRotation(interior.Clerk.Head.position - new Vector3(.45f, 1.58f, -1.05f)), 57f);
                    yield return null;
                    yield return CaptureGameView("04-clerk-behind-grille");
                }

                Vector3 tradePosition = interior.Trade.InteractionPosition;
                tradePosition.y = player.GameObject.transform.position.y;
                yield return WalkTo(player, tradePosition);
                Vector3 beforeBarrier = player.GameObject.transform.position;
                for (int i = 0; i < 45; i++)
                {
                    player.Motor.MoveTowardsApproachWaypoint(new Vector3(0f, beforeBarrier.y, 2f), .04f, Step);
                    yield return null;
                }
                Assert.That(player.GameObject.transform.position.z, Is.LessThan(.3f),
                    "The full counter/partition must physically prevent reaching the clerk room.");
                player.Motor.Teleport(beforeBarrier);
                yield return AimAt(player, follow, interior.Trade.InteractionPosition);
                Assert.That(player.Interactor.ActiveInteractable, Is.SameAs(interior.Trade));
                int cash = GameSessionState.CashBalance;
                string[] inventory = InventorySnapshot();
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                Assert.That(interior.Trade.IsOpen, Is.True,
                    "The opening E must leave the trade panel open.");
                Assert.That(interior.Trade.SelectedBranch, Is.EqualTo(PawnshopTradeBranch.None));
                Assert.That(interior.Trade.SelectBuy(), Is.True);
                Assert.That(interior.Trade.SelectedBranch, Is.EqualTo(PawnshopTradeBranch.Buy));
                Assert.That(interior.Trade.FeedbackKey, Is.Not.Empty);
                AssertNoTransaction(cash, inventory);
                if (capture) yield return CaptureGameView("05-buy-placeholder");
                Assert.That(interior.Trade.SelectSell(), Is.True);
                Assert.That(interior.Trade.SelectedBranch, Is.EqualTo(PawnshopTradeBranch.Sell));
                Assert.That(interior.Trade.FeedbackKey, Is.Not.Empty);
                AssertNoTransaction(cash, inventory);
                if (capture) yield return CaptureGameView("06-sell-placeholder");
                Assert.That(interior.PauseMenu.Open(), Is.False,
                    "A full trade modal keeps one input owner.");
                input.Press(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                Assert.That(interior.Trade.IsOpen, Is.False);
                Assert.That(player.Interactor.InputEnabled && player.Motor.InputEnabled, Is.True);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);

                Assert.That(interior.Trade.Open(player.Interactor), Is.True);
                interior.Trade.enabled = false;
                yield return null;
                Assert.That(interior.Trade.IsOpen || BarMinigameModalLock.IsAnyLocked, Is.False,
                    "Disabling the owner releases its panel, cursor and input lock.");
                Assert.That(player.Interactor.InputEnabled && player.Motor.InputEnabled, Is.True);
                interior.Trade.enabled = true;
                Assert.That(interior.PauseMenu.Open(), Is.True);
                Assert.That(interior.PauseMenu.Cancel(), Is.True);
                yield return null;
                AssertNoTransaction(cash, inventory);

                var exitAction = interior.Exit.GetComponent<PlayerDoorActionTarget>();
                Assert.That(exitAction != null && exitAction.IsConfigured, Is.True);
                yield return WalkTo(player, exitAction.Plan.EntryRootPosition);
                yield return AimAt(player, follow, interior.Exit.InteractionPosition);
                Assert.That(player.Interactor.ActiveInteractable, Is.SameAs(interior.Exit));
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.City &&
                    city != null && !city.IsDormant && !SceneTransitionService.IsTransitioning,
                    "The pawnshop exit did not restore its resident City.");
                Assert.That(Vector3.Distance(city.Player.GameObject.transform.position, site.ReturnWorld), Is.LessThan(.15f),
                    "Return keeps the measured height and the clear outward doorway position.");
                Assert.That(GameSessionState.IsReturningToCity, Is.False);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                AssertNoTransaction(cash, inventory);
            }
            finally
            {
                if (interior != null && interior.Trade != null) interior.Trade.Close();
                if (follow != null) follow.ClearFixedPose();
                if (exteriorFollow != null) exteriorFollow.ClearFixedPose();
                input.TearDown();
                GameCursor.Release(cursor);
                GameInput.HandleApplicationFocus(previousFocus);
                Time.captureDeltaTime = previousStep;
                Time.timeScale = previousScale;
            }
        }

        private static Bounds BoundsOf(Transform model)
        {
            Renderer[] meshes = model.GetComponentsInChildren<Renderer>(true);
            Assert.That(meshes, Is.Not.Empty);
            Bounds bounds = meshes[0].bounds;
            foreach (Renderer mesh in meshes) bounds.Encapsulate(mesh.bounds);
            return bounds;
        }

        private static string[] InventorySnapshot() => GameSessionState.InventoryItems
            .Select(stack => stack.ItemId + ":" + stack.Count).OrderBy(value => value).ToArray();

        private static void AssertNoTransaction(int cash, string[] inventory)
        {
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
            Assert.That(InventorySnapshot(), Is.EqualTo(inventory), "Neither placeholder may grant or consume an item.");
        }

        private static IEnumerator WalkTo(PlayerRuntime player, Vector3 target)
        {
            bool arrived = false;
            for (int frame = 0; frame < 180 && !arrived; frame++)
            {
                arrived = player.Motor.MoveTowardsApproachWaypoint(target, .045f, Step);
                yield return null;
            }
            Assert.That(arrived, Is.True, $"The real approach stalled at {player.GameObject.transform.position:F2}, target {target:F2}.");
        }

        private static IEnumerator AimAt(PlayerRuntime player, PlayerCameraFollow follow, Vector3 target)
        {
            Vector3 away = player.GameObject.transform.position - target;
            away.y = 0f;
            Vector3 eye = player.GameObject.transform.position + away.normalized * .7f + Vector3.up * 1.5f;
            follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 60f);
            Physics.SyncTransforms();
            yield return null;
            yield return null;
        }

        private static IEnumerator CaptureGameView(string shot)
        {
            string path = Path.GetFullPath(Path.Combine("Captures", "Pawnshop", shot + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return WaitFor(() => File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous,
                "The actual Game View must finish writing the requested pawnshop frame.", 5f);
        }

        private static IEnumerator CaptureExteriorGameView(CityGameRoot city, int minuteOfDay, string shot, Action check = null)
        {
            // Direct City loading leaves the fresh clock stopped. Prepare its
            // presentation hour without advancing needs or starting traffic, then
            // restore that same stopped time before the physical entry scenario.
            int previousMinute = GameSessionState.GameMinuteOfDay;
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(GameSessionState.TryPrepareGameTimeAt(minuteOfDay), Is.True);
            try
            {
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                yield return null;
                yield return null;
                check?.Invoke();
                if (shot != null) yield return CaptureGameView(shot);
            }
            finally
            {
                Assert.That(GameSessionState.TryPrepareGameTimeAt(previousMinute), Is.True);
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
            }
        }

        private static float[] AssertExteriorLighting(CityGameRoot city, Transform exterior)
        {
            string[] fixtures = { "PawnshopEntrance", "PawnshopSignLeft", "PawnshopSignRight", "PawnshopFacadeLeft", "PawnshopFacadeRight", "PawnshopFacadeRear" };
            Transform[] parts = exterior.GetComponentsInChildren<Transform>(true);
            var pool = city.Night.Atmosphere.StreetLightPool;
            Assert.That(pool.Count, Is.InRange(fixtures.Length, 12),
                "The facade borrows the existing bounded pool, including any reserved practical lease.");
            Assert.That(exterior.GetComponentsInChildren<Light>(true), Is.Empty);
            var intensities = new float[fixtures.Length];
            for (int index = 0; index < fixtures.Length; index++)
            {
                string name = CityNightAtmosphere.IndustrialTaskLightPrefix + fixtures[index];
                Transform source = parts.Single(part => part.name == name);
                Light light = pool.SingleOrDefault(candidate => candidate.enabled &&
                    Vector3.Distance(candidate.transform.position, source.position) < .01f);
                Assert.That(light, Is.Not.Null, "The actual facade fitting must lease pooled light: " + name);
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(light.range, Is.GreaterThanOrEqualTo(index <= 2 ? 7.9f : 14f));
                Assert.That(light.spotAngle, Is.GreaterThanOrEqualTo(145f),
                    "Broad facade spill and the sheltered entrance retain their own lenses.");
                Assert.That(light.color.g, Is.GreaterThanOrEqualTo(light.color.r), "The working facade light stays neutral.");
                Assert.That((uint)light.GetUniversalAdditionalLightData().renderingLayers, Is.EqualTo(1u));
                Assert.That(light.intensity, Is.GreaterThan(0f));
                intensities[index] = light.intensity;
            }
            return intensities;
        }

        private static IEnumerator WaitFor(Func<bool> ready, string message, float timeout = Timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, message);
        }
    }
}
