using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// One real Home load proves the authored opening, reversible debug dressing
    /// and the shut room's interaction. The accompanying camera frames are
    /// visual review artifacts, not a substitute for looking at the room.
    /// </summary>
    public sealed class HomeAuthoredModelPlayModeTests
    {
        private const float TimeoutSeconds = 25f;
        private IDisposable capturePause;
        private HomeInteriorRoot home;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            capturePause?.Dispose();
            capturePause = null;
            if (home != null)
            {
                home.DebugWindow?.Close();
                Scene scene = home.gameObject.scene;
                Scene cleanup = SceneManager.CreateScene("Home Authored Model Cleanup");
                SceneManager.SetActiveScene(cleanup);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    while (unload != null && !unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            home = null;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredHome_PreviewsAllDaysAndKeepsLockedRoomClosed()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeArrival(HomeArrivalKind.OpeningSleep);
            Assert.That(Resources.Load<HomeInteriorModelLibrary>(
                HomeInteriorModelLibrary.ResourcePath), Is.Not.Null,
                "Import the Home Blender model and run HomeInteriorModelAssetSetup before this check.");

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(load.isDone, Is.True, "Home did not finish loading.");
            yield return WaitFor(() =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home != null && home.IsInitialized &&
                       !SceneTransitionService.IsTransitioning;
            }, "Home failed to build its authored runtime.");
            for (int frame = 0; frame < 3; frame++) yield return null;

            Assert.That(home.Arrival, Is.EqualTo(HomeArrivalKind.OpeningSleep));
            Assert.That(home.Opening, Is.Not.Null);
            Assert.That(home.ApartmentDays, Is.Not.Null);
            Assert.That(home.DebugWindow, Is.Not.Null);
            Assert.That(home.DebugCityMapShortcut.KeyboardShortcutEnabled, Is.False);
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            yield return CaptureAndCompleteOpening(camera);

            // Keep the comparison sequence in this same apartment instance,
            // with the hero at its ordinary arrival point after the real wake.
            home.Player.Motor.Teleport(home.Layout.PlayerSpawn);
            home.Player.GameObject.transform.rotation = Quaternion.identity;
            home.FixedCamera.ReapplyActiveShot();
            for (int frame = 0; frame < 3; frame++) yield return null;
            capturePause = GameTimeScaleRuntime.AcquirePause();
            double minutesUntilNoon = 720d - GameSessionState.GameTimeOfDayMinutes;
            Assert.That(minutesUntilNoon, Is.GreaterThan(0d));
            GameSessionState.AdvanceGameTime((float)minutesUntilNoon);
            home.DayNight.RefreshImmediate();
            HomeApartmentDressing dressing = home.Room.GetComponent<HomeApartmentDressing>();
            Assert.That(dressing, Is.Not.Null);
            HomeInteriorLayoutValidator.ValidateOrThrow(home.Layout);
            HomeBalconyLayoutValidator.ValidateOrThrow(home.Layout, home.BalconyLayout);
            dressing.ValidateOrThrow(home.Layout, home.BalconyLayout);

            HomeInteriorModelLibrary library = HomeInteriorModelLibrary.Load();
            Dictionary<Mesh, HomeAuthoredPart> imported = ValidateImportedGeometry(library);
            ValidateLiveGeometry(home, imported);
            ValidateBedSurfaces(home);
            ValidateLockedDoor(home);

            // One pause lease keeps every comparison and exported frame at
            // the same clock time without changing the session's running flag.
            Vector3 initialPosition = home.Player.GameObject.transform.position;
            Quaternion initialRotation = home.Player.GameObject.transform.rotation;
            double initialClock = GameSessionState.GameTimeOfDayMinutes;
            Dictionary<InventoryItemId, int> inventory = SnapshotInventory();
            int hunger = GameSessionState.HungerLevel;
            int fatigue = GameSessionState.FatigueLevel;
            int cash = GameSessionState.CashBalance;
            Assert.That(home.DebugWindow.Open(), Is.True);
            Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(1));
            HashSet<MeshFilter> firstDayParts = VisibleDecor(home, imported);
            int firstDayCount = dressing.VisiblePartCount;

            foreach (int day in new[] { 7, 3, 1 })
            {
                Assert.That(home.DebugWindow.TrySetGameDayNumber(day), Is.True);
                Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(day));
                Assert.That(dressing.AppliedDayNumber, Is.EqualTo(day));
                Assert.That(home.Player.GameObject.transform.position, Is.EqualTo(initialPosition));
                Assert.That(home.Player.GameObject.transform.rotation, Is.EqualTo(initialRotation));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(initialClock));
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Assert.That(GameSessionState.HungerLevel, Is.EqualTo(hunger));
                Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(fatigue));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                AssertInventory(inventory);
                dressing.ValidateOrThrow(home.Layout, home.BalconyLayout);
                if (day == 7)
                {
                    Assert.That(dressing.VisiblePartCount, Is.GreaterThan(firstDayCount),
                        "The last day's deterioration must exist in actual room geometry.");
                    Assert.That(VisibleDecor(home, imported).SetEquals(firstDayParts), Is.False);
                }
            }
            Assert.That(VisibleDecor(home, imported).SetEquals(firstDayParts), Is.True,
                "Returning to day one must restore its exact active dressing, not retain late-day clutter.");
            Assert.That(home.DebugWindow.Close(), Is.True);

            using (var captures = new HomeCaptures(camera))
            {
                for (int day = 1; day <= 7; day++)
                {
                    SelectDay(day);
                    home.Player.Motor.Teleport(initialPosition);
                    home.Player.GameObject.transform.rotation = initialRotation;
                    home.FixedCamera.ReapplyActiveShot();
                    home.DayNight.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                    captures.Write($"day-{day:00}-main");
                }

                foreach (int day in new[] { 1, 7 })
                {
                    SelectDay(day);
                    Rect bathroom = home.Layout.BathroomBounds;
                    home.Player.Motor.Teleport(new Vector3(
                        bathroom.xMin + 0.64f, PlayerFactory.GroundedRootOffset,
                        bathroom.yMin + 1.10f));
                    home.FixedCamera.ReapplyActiveShot();
                    home.DayNight.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Bathroom));
                    captures.Write($"day-{day:00}-bathroom");

                    Rect balcony = home.BalconyLayout.BalconyCameraActivationBounds;
                    home.Player.Motor.Teleport(new Vector3(
                        balcony.center.x, PlayerFactory.GroundedRootOffset, balcony.center.y));
                    home.FixedCamera.ReapplyActiveShot();
                    home.DayNight.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Balcony));
                    captures.Write($"day-{day:00}-balcony");
                }

                PlayerDoorActionTarget doorTarget = home.LockedRoomDoor.GetComponent<PlayerDoorActionTarget>();
                home.Player.Motor.Teleport(doorTarget.Plan.EntryRootPosition);
                home.Player.GameObject.transform.rotation = doorTarget.Plan.EntryRotation;
                home.FixedCamera.ReapplyActiveShot();
                home.DayNight.RefreshImmediate();
                yield return null;
                yield return null;
                Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                captures.Write("day-07-closed-room-approach");
            }

            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(initialClock));
            AssertInventory(inventory);
            capturePause.Dispose();
            capturePause = null;
            Physics.SyncTransforms();
            yield return WaitFor(() => ReferenceEquals(home.Player.Interactor.ActiveInteractable, home.LockedRoomDoor),
                "The player at the new doorway cannot reach its real interaction.");
            Assert.That(home.InteractionPrompt.TryInvokePrompt(), Is.True);
            PlayerDoorActionController doorAction = home.Player.GameObject.GetComponent<PlayerDoorActionController>();
            Assert.That(doorAction.IsPlaying, Is.True);
            Assert.That(home.DebugWindow.Open(), Is.False,
                "Changing the apartment must not interrupt a door gesture.");
            yield return WaitFor(() => home.InteractionPrompt.IsFeedbackVisibleAt(Time.unscaledTime),
                "The closed room did not answer the completed attempt with the missing-key thought.");
            Assert.That(home.InteractionPrompt.GetPromptKeyAt(Time.unscaledTime),
                Is.EqualTo("home.lockedRoomDoor.missingKey"));
            Assert.That(doorAction.IsPlaying, Is.False);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.HomeInterior));
            AssertInventory(inventory);
        }

        private IEnumerator CaptureAndCompleteOpening(Camera camera)
        {
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(home.AlarmClock.DisplayedTime, Is.EqualTo("05:59"));
            Assert.That(home.DebugWindow.Open(), Is.False,
                "The debug window must leave the sleeping prologue in control.");
            Assert.That(home.DebugWindow.TrySetGameDayNumber(7), Is.False);
            using (var captures = new HomeCaptures(camera))
            {
                yield return WaitFor(() => home.AlarmClock.DisplayVisible,
                    "The opening clock never showed its digits.");
                captures.Write("opening-01-alarm");
                yield return WaitFor(() => home.Opening.Phase == HomeOpeningPhase.AwaitingWake,
                    "The ordinary five-second opening did not reveal its wake action.");
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(home.DebugWindow.Open(), Is.False);
                Assert.That(home.Opening.TryWake(), Is.True);
                Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AlarmRinging));
                Assert.That(home.AlarmClock.IsRinging, Is.True);
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Assert.That(home.DebugWindow.Open(), Is.False);

                yield return WaitFor(() =>
                    home.Opening.Phase == HomeOpeningPhase.Waking &&
                    home.Opening.Timeline.PhaseElapsedSeconds >=
                        HomeOpeningTimeline.WakeCameraTransitionSeconds,
                    "The wake never reached its authored sleeper camera.");
                Assert.That(home.DebugWindow.Open(), Is.False,
                    "The debug window must wait for the entire wake animation.");
                captures.Write("opening-02-waking");

                yield return WaitFor(() =>
                    home.Opening.Phase == HomeOpeningPhase.Complete &&
                    !HomeApartmentDayController.IsHomeBusy(home),
                    "The authored wake did not return the hero to ordinary Home control.");
                Assert.That(home.AlarmClock.IsRinging, Is.False);
                Assert.That(home.Player.Motor.InputEnabled, Is.True);
                Assert.That(home.Player.Interactor.InputEnabled, Is.True);
                Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                Assert.That(home.DebugWindow.Open(), Is.True);
                Assert.That(home.DebugWindow.Close(), Is.True);
                captures.Write("opening-03-awake");
            }
        }

        private void SelectDay(int day)
        {
            Assert.That(home.DebugWindow.Open(), Is.True);
            if (GameSessionState.GameDayNumber != day)
                Assert.That(home.DebugWindow.TrySetGameDayNumber(day), Is.True);
            Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(day));
            Assert.That(home.DebugWindow.Close(), Is.True);
        }

        private static Dictionary<Mesh, HomeAuthoredPart> ValidateImportedGeometry(HomeInteriorModelLibrary library)
        {
            Assert.That(library.Parts, Is.Not.Empty);
            Assert.That(library.BuildSignature, Is.Not.Null.And.Not.Empty);
            var imported = new Dictionary<Mesh, HomeAuthoredPart>();
            foreach (HomeAuthoredPart part in library.Parts)
            {
                Assert.That(part.mesh, Is.Not.Null, $"Missing imported mesh: {part.name}");
                Assert.That(part.mesh.vertexCount, Is.GreaterThan(0), part.name);
                Assert.That(Vector3.Distance(part.mesh.bounds.min, HomeAuthoredPart.Vector(part.bounds_min)),
                    Is.LessThanOrEqualTo(0.012f), $"Wrong imported minimum/axis/scale: {part.name}");
                Assert.That(Vector3.Distance(part.mesh.bounds.max, HomeAuthoredPart.Vector(part.bounds_max)),
                    Is.LessThanOrEqualTo(0.012f), $"Wrong imported maximum/axis/scale: {part.name}");
                Assert.That(part.Size.x, Is.GreaterThan(0f), part.name);
                Assert.That(part.Size.y, Is.GreaterThan(0f), part.name);
                Assert.That(part.Size.z, Is.GreaterThan(0f), part.name);
                imported.Add(part.mesh, part);
            }
            return imported;
        }

        private static void ValidateLiveGeometry(HomeInteriorRoot root, Dictionary<Mesh, HomeAuthoredPart> imported)
        {
            int authoredRenderers = 0;
            int collisionPairs = 0;
            foreach (MeshFilter filter in root.Room.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !imported.ContainsKey(filter.sharedMesh)) continue;
                authoredRenderers++;
                Renderer renderer = filter.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null, filter.name);
                BoxCollider collision = filter.GetComponent<BoxCollider>();
                if (collision == null || collision.isTrigger || !filter.gameObject.activeInHierarchy) continue;
                collisionPairs++;
                Assert.That(renderer.bounds.Intersects(collision.bounds), Is.True,
                    $"Authored visual and plan-owned collision do not meet: {filter.name}");
            }
            Assert.That(authoredRenderers, Is.GreaterThan(20), "Home did not instantiate its authored environment.");
            Assert.That(collisionPairs, Is.GreaterThan(5), "No real authored structure was checked against collision.");
            Bounds leftWall = root.Room.Find("Home Left Wall").GetComponent<Renderer>().bounds;
            Bounds backWall = root.Room.Find("Home Back Wall").GetComponent<Renderer>().bounds;
            Bounds bed = root.Room.Find("Home Bed Frame").GetComponent<Renderer>().bounds;
            Bounds cupboard = root.Room.Find("Home Battered Cabinet").GetComponent<Renderer>().bounds;
            Assert.That(bed.min.x - leftWall.max.x, Is.EqualTo(.02f).Within(.01f),
                "The bed's head must meet the wall with a small fitting gap.");
            Assert.That(cupboard.min.x - leftWall.max.x, Is.EqualTo(.02f).Within(.01f));
            Assert.That(backWall.min.z - cupboard.max.z, Is.EqualTo(.02f).Within(.01f),
                "The kitchen cupboard belongs in the far corner, beside both walls.");
            Vector3 dock = HomeBedInteractionPlan.Create(root.Layout).EntryRootPosition;
            foreach (HomeFurnitureFootprint item in root.Layout.Furniture)
            {
                if (!item.BlocksMovement) continue;
                var closest = new Vector2(Mathf.Clamp(dock.x, item.Bounds.xMin, item.Bounds.xMax),
                    Mathf.Clamp(dock.z, item.Bounds.yMin, item.Bounds.yMax));
                Assert.That(Vector2.Distance(new Vector2(dock.x, dock.z), closest),
                    Is.GreaterThan(HomeInteriorLayoutValidator.PlayerClearanceRadius),
                    $"Moving the bed must keep its standing approach clear of {item.Id}.");
            }
        }

        private static void ValidateBedSurfaces(HomeInteriorRoot root)
        {
            Assert.That(root.BedSurface, Is.Not.Null, "The migration lost the bed's runtime deformation binding.");
            Assert.That(root.BedSurface.MattressModel, Is.Not.Null);
            Assert.That(root.BedSurface.PillowModel, Is.Not.Null);
            HomeBedDeformableSurface[] surfaces = root.Room.GetComponentsInChildren<HomeBedDeformableSurface>(true);
            Assert.That(surfaces, Has.Length.EqualTo(2));
            foreach (HomeBedDeformableSurface surface in surfaces)
            {
                Assert.That(surface.Mesh, Is.Not.Null);
                Assert.That(surface.Columns, Is.GreaterThan(1));
                Assert.That(surface.Rows, Is.GreaterThan(1));
                Assert.That(surface.TopVertexCount, Is.LessThanOrEqualTo(surface.VertexCount));
                Assert.That(surface.Mesh.isReadable, Is.True, surface.name);
            }
        }

        private static void ValidateLockedDoor(HomeInteriorRoot root)
        {
            LockedDoorInteraction door = root.LockedRoomDoor;
            Assert.That(door, Is.Not.Null);
            Assert.That(door.IsConfigured, Is.True);
            Assert.That(door.PromptKey, Is.EqualTo("home.lockedRoomDoor.prompt"));
            Assert.That(door.LockedKey, Is.EqualTo("home.lockedRoomDoor.missingKey"));
            Assert.That(LocalizationService.Get(door.LockedKey), Is.Not.EqualTo(door.LockedKey));
            Assert.That(door.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(door.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(door.GetComponent<PlayerDoorActionTarget>().IsConfigured, Is.True);
            Assert.That(door.GetComponent<HomeExit>(), Is.Null, "The shut room must not carry a destination exit.");
            Assert.That(door.GetComponent<HomeEntrance>(), Is.Null);
        }

        private static HashSet<MeshFilter> VisibleDecor(HomeInteriorRoot root, Dictionary<Mesh, HomeAuthoredPart> imported)
        {
            var result = new HashSet<MeshFilter>();
            foreach (MeshFilter filter in root.Room.GetComponentsInChildren<MeshFilter>(false))
                if (filter.sharedMesh != null && imported.TryGetValue(filter.sharedMesh, out HomeAuthoredPart part) &&
                    part.role == "decor" && filter.GetComponent<Renderer>().enabled)
                    result.Add(filter);
            return result;
        }

        private static Dictionary<InventoryItemId, int> SnapshotInventory()
        {
            var result = new Dictionary<InventoryItemId, int>();
            foreach (InventoryItemStack item in GameSessionState.InventoryItems)
                result.Add(item.ItemId, item.Count);
            return result;
        }

        private static void AssertInventory(Dictionary<InventoryItemId, int> expected)
        {
            Assert.That(GameSessionState.InventoryItems.Count, Is.EqualTo(expected.Count));
            foreach (KeyValuePair<InventoryItemId, int> item in expected)
                Assert.That(GameSessionState.GetInventoryItemCount(item.Key), Is.EqualTo(item.Value), item.Key.ToString());
        }

        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private sealed class HomeCaptures : IDisposable
        {
            private const int Width = 1280;
            private const int Height = 720;
            private readonly Camera camera;
            private readonly RenderTexture target = new RenderTexture(Width, Height, 24);
            private readonly Texture2D frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            private readonly string directory;

            public HomeCaptures(Camera sceneCamera)
            {
                camera = sceneCamera;
                directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HomeAuthoredModel");
                Directory.CreateDirectory(directory);
            }

            public void Write(string name)
            {
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                    frame.Apply();
                    string path = Path.Combine(directory, name + ".png");
                    File.WriteAllBytes(path, frame.EncodeToPNG());
                    Debug.Log($"Home authored capture wrote {path}");
                    Color32 first = frame.GetPixel(0, 0);
                    bool varied = false;
                    for (int y = 0; y < 16 && !varied; y++)
                        for (int x = 0; x < 16 && !varied; x++)
                        {
                            Color32 sample = frame.GetPixel(x * (Width - 1) / 15, y * (Height - 1) / 15);
                            varied = sample.r != first.r || sample.g != first.g || sample.b != first.b;
                        }
                    Assert.That(varied, Is.True, $"Home capture '{name}' is blank.");
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                }
            }

            public void Dispose()
            {
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
