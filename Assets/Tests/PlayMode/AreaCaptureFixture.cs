using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Photographs the game.
    ///
    /// This exists because numbers cannot see. In one session three
    /// defects passed `1710` green tests and were caught only by looking
    /// at a rendered frame: a jukebox standing in the middle of the room,
    /// pendant lamps six millimetres across, and an entire room at a
    /// hundredth of its size. Every one of them kept correct anchors,
    /// correct collision and a correct manifest, because none of those
    /// numbers come from the meshes.
    ///
    /// Frames are taken in PLAY MODE through the scene's own main camera,
    /// so what is photographed is the real thing: the real lighting, the
    /// real post-processing stack, and a world built by the scene's own
    /// root rather than by a mock-up of it.
    ///
    /// Every capture is `[Explicit]`. They are not tests - they assert
    /// almost nothing - and they must not join an ordinary PlayMode
    /// sweep: this project has already hit
    /// `Too many instant steps in test execution mode: ExitPlayModeTask`
    /// from running heavy scene-loading fixtures together. Run one area at
    /// a time:
    ///
    /// <code>
    /// Unity.exe -batchmode -projectPath &lt;p&gt; -runTests
    ///   -testPlatform PlayMode
    ///   -testFilter "AreaCaptureFixture.Bar"
    ///   -testResults &lt;xml&gt; -logFile &lt;log&gt;
    /// </code>
    ///
    /// Frames land in `Captures/&lt;area&gt;/`, which is gitignored. Look at
    /// them; that is the whole point.
    /// </summary>
    public sealed class AreaCaptureFixture
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const float TimeoutSeconds = 60f;

        //  Long enough for a root to finish composing its world, for
        //  atmosphere to light it and for a first physics step to settle
        //  anything that drops.
        private const int SettleFrames = 12;

        /// <summary>
        /// One frame. Either at an absolute place in the world, or - far
        /// more useful in a scene whose geometry you have not measured -
        /// at an offset from the hero, who by definition stands somewhere
        /// worth photographing.
        ///
        /// Invented absolute coordinates are how you get a folder of
        /// pictures of the inside of a wall: the first City capture here
        /// produced exactly that.
        /// </summary>
        private readonly struct Shot
        {
            private Shot(
                string name,
                Vector3 position,
                Vector3 target,
                float fieldOfView,
                bool relativeToHero)
            {
                Name = name;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
                RelativeToHero = relativeToHero;
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Target { get; }
            public float FieldOfView { get; }
            public bool RelativeToHero { get; }

            /// <summary>A fixed place, for a room whose layout is known.</summary>
            public static Shot At(
                string name,
                Vector3 position,
                Vector3 target,
                float fieldOfView = 60f)
            {
                return new Shot(
                    name, position, target, fieldOfView, false);
            }

            /// <summary>
            /// An offset in the hero's own frame: `+Z` is where he faces.
            /// </summary>
            public static Shot NearHero(
                string name,
                Vector3 offset,
                Vector3 lookOffset,
                float fieldOfView = 60f)
            {
                return new Shot(
                    name, offset, lookOffset, fieldOfView, true);
            }
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Bar()
        {
            GameSessionState.EnterBar("bar-capture");
            yield return Capture(
                SceneIds.BarInterior,
                Root<BarInteriorRoot>(),
                new[]
                {
                    Shot.NearHero(
                        "00-over-the-shoulder",
                        new Vector3(0f, 1.5f, -3.2f),
                        new Vector3(0f, 1.2f, 9f), 62f),
                    Shot.At(
                        "01-from-the-door",
                        new Vector3(0f, 1.72f, -7.2f),
                        new Vector3(0f, 1.5f, 5.5f), 62f),
                    Shot.At(
                        "02-counter",
                        new Vector3(-1.6f, 1.65f, 1.4f),
                        new Vector3(-0.6f, 1.35f, 6.2f), 55f),
                    Shot.At(
                        "03-booths-and-stage",
                        new Vector3(-2.5f, 1.8f, -2.2f),
                        new Vector3(-9.5f, 1.2f, 2.0f), 62f),
                    Shot.At(
                        "04-activity-bay",
                        new Vector3(1.5f, 1.75f, -4.2f),
                        new Vector3(7.0f, 1.1f, 0.4f), 58f),
                    Shot.At(
                        "05-pendants-and-ceiling",
                        new Vector3(0f, 1.55f, 0.5f),
                        new Vector3(-0.5f, 3.9f, 5.6f), 62f),
                    Shot.At(
                        "06-overview",
                        new Vector3(-9.4f, 4.2f, -7.0f),
                        new Vector3(1.5f, 0.9f, 4.0f), 72f),
                    Shot.At(
                        "07-jukebox-and-entrance",
                        new Vector3(1.0f, 1.6f, -2.6f),
                        new Vector3(6.6f, 1.1f, -6.9f), 58f),
                });
        }

        //  Every area below is photographed from the hero, because his
        //  position is the one place in a scene guaranteed to be worth
        //  looking at and the one this fixture can find without measuring
        //  the world. The bar keeps hand-placed shots as well, because
        //  its room HAS been measured - and even there the first frame is
        //  the one over his shoulder.
        private static Shot[] HeroShots()
        {
            return new[]
            {
                Shot.NearHero(
                    "01-over-the-shoulder",
                    new Vector3(0f, 1.5f, -3.6f),
                    new Vector3(0f, 1.2f, 9f), 62f),
                Shot.NearHero(
                    "02-what-he-faces",
                    new Vector3(0f, 1.65f, 0.4f),
                    new Vector3(0f, 1.5f, 14f), 68f),
                Shot.NearHero(
                    "03-from-above",
                    new Vector3(0f, 11f, -11f),
                    new Vector3(0f, 0f, 6f), 60f),
            };
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator City()
        {
            yield return Capture(
                SceneIds.City, Root<CityGameRoot>(), HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CitySpecialBuildings()
        {
            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CitySpecialBuildingShots(cityRoot));
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator MountainRoad()
        {
            yield return Capture(
                SceneIds.MountainRoad, Root<MountainRoadRoot>(), HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Home()
        {
            yield return Capture(
                SceneIds.HomeInterior, Root<HomeInteriorRoot>(), HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Stairwell()
        {
            yield return Capture(
                SceneIds.StairwellInterior,
                Root<StairwellInteriorRoot>(),
                HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Supermarket()
        {
            yield return Capture(
                SceneIds.SupermarketInterior,
                Root<SupermarketInteriorRoot>(),
                HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Church()
        {
            yield return Capture(
                SceneIds.ChurchInterior,
                Root<ChurchInteriorRoot>(),
                HeroShots());
        }

        // ------------------------------------------------------------

        private static Func<Component> Root<T>() where T : Component
        {
            return () => Object.FindAnyObjectByType<T>();
        }

        private static Shot[] CitySpecialBuildingShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Layout, Is.Not.Null);

            BuildingLot bar = null;
            BuildingLot supermarket = null;
            BuildingLot playerHome = null;
            foreach (BuildingLot lot in root.Layout.BuildingLots)
            {
                if (lot.IsBar)
                {
                    bar = lot;
                }
                else if (lot.IsSupermarket)
                {
                    supermarket = lot;
                }
                else if (lot.IsPlayerHome)
                {
                    playerHome = lot;
                }
            }

            Assert.That(bar, Is.Not.Null);
            Assert.That(supermarket, Is.Not.Null);
            Assert.That(playerHome, Is.Not.Null);
            return new[]
            {
                FrameSpecialBuilding("00-bar", bar),
                FrameSpecialEntrance("00-bar-entrance", bar, 2.35f),
                FrameSpecialEntrance(
                    "00-bar-entrance-opposite",
                    bar,
                    -2.35f),
                FrameSpecialFrontageEdge(
                    "00-bar-edge-left",
                    bar,
                    -1f),
                FrameSpecialFrontageEdge(
                    "00-bar-edge-right",
                    bar,
                    1f),
                FrameSpecialFoundation("00-bar-foundation", bar),
                FrameSpecialBuilding("01-supermarket", supermarket),
                FrameSpecialBuilding("02-player-home", playerHome)
            };
        }

        private static Shot FrameSpecialEntrance(
            string name,
            BuildingLot lot,
            float lateralOffset)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = lot.DoorPosition + Vector3.up * 1.35f;
            Vector3 position = lot.DoorPosition +
                forward * 4.2f +
                right * lateralOffset +
                Vector3.up * 1.72f;
            return Shot.At(name, position, target, 54f);
        }

        private static Shot FrameSpecialFrontageEdge(
            string name,
            BuildingLot lot,
            float side)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 edge = lot.DoorPosition + right * (side * 5.28f);
            Vector3 target = edge + Vector3.up * 1.55f;
            Vector3 position = edge +
                forward * 3.15f -
                right * (side * 1.60f) +
                Vector3.up * 1.75f;
            return Shot.At(name, position, target, 50f);
        }

        private static Shot FrameSpecialFoundation(
            string name,
            BuildingLot lot)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = lot.DoorPosition -
                forward * 2.15f -
                right * 6f +
                Vector3.up * 0.10f;
            Vector3 position = lot.DoorPosition +
                forward * 0.45f -
                right * 8.65f +
                Vector3.up * 0.78f;
            return Shot.At(name, position, target, 42f);
        }

        private static Shot FrameSpecialBuilding(
            string name,
            BuildingLot lot)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            bool frontsAlongX = lot.FrontageDirection.x != 0;
            float depth = frontsAlongX ? lot.Size.x : lot.Size.y;
            float frontage = frontsAlongX ? lot.Size.y : lot.Size.x;
            Vector3 target = lot.Center +
                (Vector3.up * (lot.Height * 0.42f));
            Vector3 position = lot.Center +
                (forward * ((depth * 0.5f) + 8f)) +
                (right * (frontage * 0.42f)) +
                (Vector3.up * ((lot.Height * 0.58f) + 1f));
            return Shot.At(name, position, target, 60f);
        }

        private static IEnumerator Capture(
            string sceneName,
            Func<Component> findRoot,
            Shot[] shots)
        {
            return Capture(sceneName, findRoot, () => shots);
        }

        private static IEnumerator Capture(
            string sceneName,
            Func<Component> findRoot,
            Func<Shot[]> resolveShots)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                operation.isDone,
                Is.True,
                $"Scene '{sceneName}' did not load.");

            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (findRoot() == null &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                findRoot(),
                Is.Not.Null,
                $"Scene '{sceneName}' never built its root.");

            Shot[] shots = resolveShots();
            Assert.That(shots, Is.Not.Null.And.Not.Empty);

            for (int frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }

            Camera camera = Camera.main;
            Assert.That(
                camera,
                Is.Not.Null,
                $"Scene '{sceneName}' has no main camera to shoot with.");

            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                sceneName);
            Directory.CreateDirectory(folder);

            //  The scene's OWN camera is borrowed rather than a fresh one
            //  added, so the frames carry the real post-processing stack,
            //  culling mask and volume weights. Its state is restored
            //  afterwards in a finally, because a test that leaves the
            //  main camera pointing at a render texture breaks every test
            //  that runs after it.
            RenderTexture target = new RenderTexture(Width, Height, 24);
            Texture2D frameBuffer =
                new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFieldOfView = camera.fieldOfView;

            //  The main camera is the PLAYER's camera, so the hero stands
            //  in front of it and his head fills the middle of every shot.
            //  These frames are for judging the world; the hero has his
            //  own art spec and his own tests. Hidden by renderer, not by
            //  deactivating him, so nothing he drives stops running.
            var hiddenPlayer = new System.Collections.Generic.List<Renderer>();
            PlayerMotor player = Object.FindAnyObjectByType<PlayerMotor>();
            if (player != null)
            {
                foreach (Renderer renderer in
                         player.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hiddenPlayer.Add(renderer);
                    }
                }
            }

            try
            {
                camera.targetTexture = target;
                foreach (Shot shot in shots)
                {
                    //  Positioned and rendered without yielding, so a
                    //  camera-follow script cannot move it back in
                    //  between.
                    Vector3 from = shot.Position;
                    Vector3 to = shot.Target;
                    if (shot.RelativeToHero)
                    {
                        Assert.That(
                            player,
                            Is.Not.Null,
                            $"'{sceneName}/{shot.Name}' is framed on the " +
                            "hero, and this scene has none.");
                        from = player.transform.TransformPoint(from);
                        to = player.transform.TransformPoint(to);
                    }

                    camera.transform.SetPositionAndRotation(
                        from,
                        Quaternion.LookRotation(
                            (to - from).normalized,
                            Vector3.up));
                    camera.fieldOfView = shot.FieldOfView;
                    camera.Render();

                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = target;
                    frameBuffer.ReadPixels(
                        new Rect(0f, 0f, Width, Height), 0, 0);
                    frameBuffer.Apply();
                    RenderTexture.active = previousActive;

                    string path = Path.Combine(
                        folder,
                        $"{shot.Name}.png");
                    File.WriteAllBytes(path, frameBuffer.EncodeToPNG());
                    Debug.Log($"Area capture wrote {path}");

                    Assert.That(
                        IsBlank(frameBuffer),
                        Is.False,
                        $"'{sceneName}/{shot.Name}' came out a single flat " +
                        "colour: the camera saw nothing. Wrong place, wrong " +
                        "culling mask, or a world that never built.");
                }
            }
            finally
            {
                foreach (Renderer renderer in hiddenPlayer)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                    }
                }

                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(
                    previousPosition,
                    previousRotation);
                camera.fieldOfView = previousFieldOfView;
                Object.DestroyImmediate(frameBuffer);
                target.Release();
                Object.DestroyImmediate(target);
            }

        }

        /// <summary>
        /// True when every sampled pixel is the same colour.
        ///
        /// A folder full of flat rectangles looks like success from the
        /// outside - the files exist, the run is green, the log says it
        /// wrote them - which is precisely the failure this whole fixture
        /// is meant to stop.
        /// </summary>
        private static bool IsBlank(Texture2D frame)
        {
            const int Steps = 24;
            Color32 first = frame.GetPixel(0, 0);
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    Color32 sample = frame.GetPixel(
                        x * (frame.width - 1) / (Steps - 1),
                        y * (frame.height - 1) / (Steps - 1));
                    if (sample.r != first.r ||
                        sample.g != first.g ||
                        sample.b != first.b)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
