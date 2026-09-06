using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The geometric mirror in the live apartment: off and plugged in the
    /// main room, on behind the bathroom shot with the twin at the hero's
    /// reflection wearing his pose and his face, the reflection keeping
    /// its head while a first-person view takes the real one off, and
    /// everything back once the shot changes.
    /// </summary>
    [PrebuildSetup(typeof(HomeBrushingAssetsSetup))]
    public sealed class HomeBathroomMirrorPlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private const float FastTimeScale = 6f;

        private HomeInteriorRoot home;
        private Vector4 neutralFaceCell;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.EnterHome();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene cleanupScene = SceneManager.CreateScene(
                "BathroomMirrorCleanup" + UnityEngine.Random.Range(0, 100000));
            SceneManager.SetActiveScene(cleanupScene);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(SceneIds.HomeInterior);
            while (unload != null && !unload.isDone)
            {
                yield return null;
            }

            home = null;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Mirror_ShowsOnlyBehindTheBathroomShotAndReflectsTheHero()
        {
            yield return LoadHome();
            HomeBathroomMirrorWorld mirror = home.BathroomMirror;
            Assert.That(mirror, Is.Not.Null, "The home builds its bathroom mirror.");
            Assert.That(mirror.IsInitialized, Is.True);
            string cloned = string.Join(" | ", mirror.ClonedSourceNames);
            Assert.That(mirror.StaticCloneCount, Is.GreaterThanOrEqualTo(45), "The bathroom is copied: " + cloned);
            foreach (string required in new[]
                     {
                         "Home Bathroom West Wall", "Home Bathroom Front Wall Left", "Home Bathroom Door Ajar",
                         "Home Bathroom Tile Floor", "Home Bathroom Right Tile", "Home Bathroom Toilet Bowl",
                         "Home Bathroom Toilet Lid", "Home Bathroom Shower Tray", "Home Bathroom Shower Curtain",
                         "Home Bathroom Shower Head", "Home Bathroom Sink Basin", "Home Bathroom Sink Tap",
                         "Home Bathroom Exposed Pipe", "Home Bathroom Floor Drain",
                     })
            {
                Assert.That(mirror.ClonedSourceNames, Contains.Item(required), required + " is missing from the reflection: " + cloned);
            }

            // Nothing from the rest of the flat: the selection box alone also
            // holds the near corner of the locked room's front wall.
            foreach (string name in mirror.ClonedSourceNames)
            {
                Assert.That(
                    HomeBathroomMirrorWorld.IsSelectable(name),
                    Is.True,
                    "'" + name + "' does not belong in the mirrored bathroom.");
            }

            Assert.That(mirror.ClonedSourceNames, Has.None.Contains("Locked Room"));
            Assert.That(mirror.ClonedSourceNames, Has.None.Contains("Cracked Mirror"));
            Assert.That(mirror.ClonedSourceNames, Has.None.Contains("Back Tile"), "The tile behind the plane would fight the opening's own pieces.");
            Assert.That(mirror.HasTwin, Is.True, "The production hero is reflected by a second instance.");
            Assert.That(mirror.TwinUnpairedBoneCount, Is.Zero, "Every bone of the twin pairs with the hero's.");
            Assert.That(mirror.TwinPairedBoneCount, Is.GreaterThan(30));
            yield return null;
            yield return null;
            Assert.That(home.FixedCamera.ActiveShotKind, Is.Not.EqualTo(HomeCameraShotKind.Bathroom), "He wakes in the main room.");
            Assert.That(mirror.IsActive, Is.False, "No mirrored room outside the bathroom shot.");
            Assert.That(mirror.Content.gameObject.activeInHierarchy, Is.False);
            Assert.That(mirror.Opening.Plate.enabled, Is.True, "The plate plugs the hole.");

            var presentation = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Player3DAssetRegistry hero = presentation.Registry;
            Assert.That(mirror.TwinUnpairedRendererCount, Is.Zero, "Every renderer of the hero has one in the reflection.");
            Assert.That(mirror.TwinPairedRendererCount, Is.EqualTo(hero.Renderers.Count));
            Transform heroRoot = hero.transform;
            Camera camera = home.CameraFollow.GetComponent<Camera>();

            home.Player.Motor.Teleport(new Vector3(2.075f, 0.12f, 2.78f));
            yield return WaitUntil(
                () => home.FixedCamera.ActiveShotKind == HomeCameraShotKind.Bathroom && mirror.IsActive,
                "The bathroom shot never took over, or the mirror never woke.");
            yield return AtPresentation(() =>
            {
                Assert.That(mirror.Content.gameObject.activeInHierarchy, Is.True);
                Assert.That(mirror.Opening.Plate.enabled, Is.False, "The hole is open behind the bathroom shot.");
                Transform twinRoot = mirror.TwinRoot;
                Assert.That(twinRoot.gameObject.activeInHierarchy, Is.True);
                float heroDepth = HomeBathroomMirrorPlane.DepthInFront(home.Room.InverseTransformPoint(heroRoot.position));
                float twinDepth = HomeBathroomMirrorPlane.DepthInFront(home.Room.InverseTransformPoint(twinRoot.position));
                Assert.That(heroDepth, Is.GreaterThan(0.5f), "The hero stands in front of the mirror.");
                Assert.That(twinDepth, Is.EqualTo(-heroDepth).Within(0.002f), "The twin stands as far behind the plane as the hero stands before it.");
                Assert.That(twinRoot.position.x, Is.EqualTo(heroRoot.position.x).Within(0.002f));
                Assert.That(twinRoot.position.y, Is.EqualTo(heroRoot.position.y).Within(0.002f));
                Assert.That(twinRoot.lossyScale.z, Is.LessThan(0f), "The reflection is a flipped copy, not a turned one.");
                foreach (Player3DAnatomicalPart part in new[]
                         {
                             Player3DAnatomicalPart.Pelvis, Player3DAnatomicalPart.Torso, Player3DAnatomicalPart.Head,
                             Player3DAnatomicalPart.RightHand, Player3DAnatomicalPart.LeftShin
                         })
                {
                    Assert.That(hero.TryGetPart(part, out Player3DAnatomicalPartBinding heroPart), Is.True, part.ToString());
                    Assert.That(mirror.Twin.TryGetPart(part, out Player3DAnatomicalPartBinding twinPart), Is.True, part.ToString());
                    Assert.That(
                        Quaternion.Angle(heroPart.Bone.localRotation, twinPart.Bone.localRotation),
                        Is.LessThan(0.01f),
                        part + " wears the hero's pose.");
                    Assert.That(twinPart.Renderer.enabled, Is.True, part + " is drawn in the reflection.");
                }

                // The bones, not just the root: the head's world position must
                // be the reflection of the real head's, which no amount of
                // parenting luck produces by itself.
                Assert.That(hero.TryGetPart(Player3DAnatomicalPart.Head, out Player3DAnatomicalPartBinding heroHead), Is.True);
                Assert.That(mirror.Twin.TryGetPart(Player3DAnatomicalPart.Head, out Player3DAnatomicalPartBinding twinHead), Is.True);
                Vector3 heroHeadLocal = home.Room.InverseTransformPoint(heroHead.Bone.position);
                Vector3 twinHeadLocal = home.Room.InverseTransformPoint(twinHead.Bone.position);
                Vector3 expectedHead = HomeBathroomMirrorPlane.Reflect(heroHeadLocal);
                Assert.That(twinHeadLocal.x, Is.EqualTo(expectedHead.x).Within(0.003f), "The reflected head is off sideways.");
                Assert.That(twinHeadLocal.y, Is.EqualTo(expectedHead.y).Within(0.003f), "The reflected head is off in height.");
                Assert.That(twinHeadLocal.z, Is.EqualTo(expectedHead.z).Within(0.003f), "The reflected head is at the wrong depth.");

                Assert.That(hero.HasFaceAtlas && mirror.Twin.HasFaceAtlas, Is.True);
                var heroBlock = new MaterialPropertyBlock();
                var twinBlock = new MaterialPropertyBlock();
                hero.FaceAtlas.Renderer.GetPropertyBlock(heroBlock);
                mirror.Twin.FaceAtlas.Renderer.GetPropertyBlock(twinBlock);
                Assert.That(twinBlock.GetTexture("_BaseMap"), Is.EqualTo(heroBlock.GetTexture("_BaseMap")), "The reflection wears his face atlas.");
                Assert.That(twinBlock.GetVector("_BaseMap_ST"), Is.EqualTo(heroBlock.GetVector("_BaseMap_ST")), "...at the same expression cell.");
                neutralFaceCell = heroBlock.GetVector("_BaseMap_ST");
                AssertTheHoleShowsSomethingElse(mirror, camera);
                CaptureFrame("00-bathroom-shot", camera);
                // Over his head, so the lens is not inside him: the plate, the
                // wall and tile around it, and his reflection through the hole.
                CaptureWitness(
                    "01-witness-mirror",
                    new Vector3(2.075f, 2.05f, 3.06f),
                    new Vector3(2.075f, 1.68f, HomeBathroomMirrorPlane.PlaneZ),
                    62f);
                // Along the wall: the only angle that would show the opening as
                // a tunnel, or the replacement pieces as a seam.
                CaptureWitness(
                    "02-witness-edge",
                    new Vector3(2.95f, 1.74f, 3.16f),
                    new Vector3(2.075f, 1.72f, HomeBathroomMirrorPlane.PlaneZ),
                    50f);
            });

            // A live face, not a coincidence: soiling his mouth moves the hero
            // to a different atlas cell, and the reflection must follow it.
            GameSessionState.SetHeroMouthSoiled(true, "mirror_face_test");
            yield return null;
            yield return AtPresentation(() =>
            {
                var heroBlock = new MaterialPropertyBlock();
                var twinBlock = new MaterialPropertyBlock();
                hero.FaceAtlas.Renderer.GetPropertyBlock(heroBlock);
                mirror.Twin.FaceAtlas.Renderer.GetPropertyBlock(twinBlock);
                Vector4 soiledCell = heroBlock.GetVector("_BaseMap_ST");
                Assert.That(soiledCell, Is.Not.EqualTo(neutralFaceCell), "A soiled mouth must move him to another atlas cell.");
                Assert.That(twinBlock.GetVector("_BaseMap_ST"), Is.EqualTo(soiledCell), "The reflection followed him to the new cell.");
            });
            GameSessionState.SetHeroMouthSoiled(false, "mirror_face_test");
            yield return null;

            // A first-person view takes the real head off; the reflection keeps it.
            HomeShowerInteraction shower = home.ShowerScene;
            yield return WalkToAndActivate(shower, new Vector3(3.30f, 0.12f, 2.35f));
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(() => shower.IsUndressed && shower.View.IsHeadHidden, "The shower never hid the head.");
            yield return AtPresentation(() =>
            {
                Assert.That(mirror.IsActive, Is.True, "The bathroom scenes stay inside the bathroom shot.");
                Assert.That(Player3DHeadVisibility.IsHeadDrawn(hero), Is.False);
                Assert.That(Player3DHeadVisibility.IsHeadDrawn(mirror.Twin), Is.True, "The reflection keeps its head while the lens is inside the real one.");
                Player3DMeshBinding jacket = Find(mirror.Twin, "CLO_JacketBody");
                Assert.That(jacket.Renderer.enabled, Is.False, "The reflection undresses with him.");
                Player3DMeshBinding torso = Find(mirror.Twin, "GEO_Torso");
                Player3DMeshBinding heroTorso = Find(hero, "GEO_Torso");
                Assert.That(ReferenceEquals(torso.Renderer.sharedMaterial, heroTorso.Renderer.sharedMaterial), Is.True, "The reflection borrows the same skin material.");
                shower.View.ApplyLookDelta(-75f, 10f);
            });
            yield return AtPresentation(() =>
            {
                CaptureFrame("03-shower-look-left", camera);
                CaptureWitness(
                    "04-witness-shower-reflection",
                    new Vector3(2.075f, 2.05f, 3.06f),
                    new Vector3(2.075f, 1.68f, HomeBathroomMirrorPlane.PlaneZ),
                    62f);
            });
            shower.enabled = false; // cancel: owned cleanup
            yield return null;
            shower.enabled = true;
            Time.timeScale = 1f;
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The shower never released the player.");
            Assert.That(Player3DHeadVisibility.IsHeadDrawn(hero), Is.True);

            home.Player.Motor.Teleport(new Vector3(-2.0f, 0.12f, -2.0f));
            yield return WaitUntil(() => home.FixedCamera.ActiveShotKind != HomeCameraShotKind.Bathroom, "The main room shot never came back.");
            yield return null;
            yield return null;
            Assert.That(mirror.IsActive, Is.False, "The mirrored room switches off with the shot.");
            Assert.That(mirror.Content.gameObject.activeInHierarchy, Is.False);
            Assert.That(mirror.Opening.Plate.enabled, Is.True, "The plate plugs the hole again.");
        }

        private IEnumerator LoadHome()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
            while (load != null && !load.isDone)
            {
                yield return null;
            }

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("HomeInterior never finished initializing.");
        }

        private IEnumerator WalkToAndActivate(HomeBathroomSceneInteraction scene, Vector3 approachPosition)
        {
            home.Player.Motor.Teleport(approachPosition);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (ReferenceEquals(home.Player.Interactor.ActiveInteractable, scene))
                {
                    scene.Interact(home.Player.Interactor);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"{scene.GetType().Name} was never discovered by the interactor.");
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(failureMessage);
        }

        private IEnumerator AtPresentation(System.Action sample)
        {
            // Sampled after every LateUpdate, the mirror's own included.
            var probe = home.GetComponent<HomeBathroomPresentationProbe>() ??
                home.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            bool completed = false;
            System.Exception failure = null;
            probe.Sample = () =>
            {
                try { sample(); }
                catch (System.Exception exception) { failure = exception; }
                finally { completed = true; }
            };
            while (!completed) yield return null;
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static Player3DMeshBinding Find(Player3DAssetRegistry registry, string meshName)
        {
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer != null && binding.MeshName == meshName) return binding;
            }

            Assert.Fail("The rig no longer has '" + meshName + "'.");
            return null;
        }

        private void CaptureWitness(string shot, Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            Camera main = home.CameraFollow.GetComponent<Camera>();
            var witness = new GameObject("Mirror Witness Camera");
            Camera camera = witness.AddComponent<Camera>();
            try
            {
                camera.CopyFrom(main);
                camera.transform.SetPositionAndRotation(
                    home.Room.TransformPoint(position),
                    Quaternion.LookRotation(home.Room.TransformPoint(lookAt) - home.Room.TransformPoint(position), Vector3.up));
                camera.fieldOfView = fieldOfView;
                camera.enabled = false;
                CaptureFrame(shot, camera);
            }
            finally
            {
                Object.DestroyImmediate(witness);
            }
        }

        /// <summary>
        /// The proof that the trick actually renders: the same frame with the
        /// hole open and with the plate plugging it must differ inside the
        /// plate's rectangle and nowhere else. Object counts and transforms
        /// cannot show this; only pixels can.
        /// </summary>
        private void AssertTheHoleShowsSomethingElse(HomeBathroomMirrorWorld mirror, Camera camera)
        {
            Assert.That(mirror.Opening.IsMirrorActive, Is.True);
            Color32[] open = Render(camera, out int width, out int height);
            mirror.Opening.SetMirrorActive(false);
            Color32[] plugged = Render(camera, out _, out _);
            mirror.Opening.SetMirrorActive(true);

            Rect hole = HomeBathroomMirrorPlane.OpeningXY;
            var inside = new RectInt(width, height, 0, 0);
            double insideSum = 0, outsideSum = 0;
            long insideCount = 0, outsideCount = 0;
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 world = home.Room.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? hole.xMin : hole.xMax,
                    (corner & 2) == 0 ? hole.yMin : hole.yMax,
                    HomeBathroomMirrorPlane.PlaneZ));
                Vector3 viewport = camera.WorldToViewportPoint(world);
                Assert.That(viewport.z, Is.GreaterThan(0f), "The mirror must be in front of the lens.");
                int x = Mathf.Clamp(Mathf.RoundToInt(viewport.x * width), 0, width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(viewport.y * height), 0, height - 1);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }

            inside = new RectInt(minX, minY, maxX - minX, maxY - minY);
            Assert.That(inside.width, Is.GreaterThan(8), "The mirror covers too few pixels to judge.");
            Assert.That(inside.height, Is.GreaterThan(8));
            // Two pixels of margin: the plate's own edge sits on the boundary.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    double difference =
                        Mathf.Abs(open[index].r - plugged[index].r) +
                        Mathf.Abs(open[index].g - plugged[index].g) +
                        Mathf.Abs(open[index].b - plugged[index].b);
                    bool near =
                        x >= inside.xMin - 3 && x <= inside.xMax + 3 &&
                        y >= inside.yMin - 3 && y <= inside.yMax + 3;
                    if (x > inside.xMin + 2 && x < inside.xMax - 2 &&
                        y > inside.yMin + 2 && y < inside.yMax - 2)
                    {
                        insideSum += difference;
                        insideCount++;
                    }
                    else if (!near)
                    {
                        outsideSum += difference;
                        outsideCount++;
                    }
                }
            }

            Assert.That(insideCount, Is.GreaterThan(0));
            double insideMean = insideSum / insideCount;
            double outsideMean = outsideSum / Mathf.Max(1, outsideCount);
            Assert.That(
                insideMean,
                Is.GreaterThan(12.0),
                $"Opening the hole changed almost nothing inside the mirror ({insideMean:F2}/765): the reflection is not being drawn.");
            Assert.That(
                outsideMean,
                Is.LessThan(1.5),
                $"Opening the hole changed the rest of the frame too ({outsideMean:F2}/765).");
        }

        private static Color32[] Render(Camera camera, out int width, out int height)
        {
            width = 640;
            height = 360;
            var target = new RenderTexture(width, height, 24);
            var frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                frame.Apply();
                return frame.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static void CaptureFrame(string shot, Camera camera)
        {
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", "HomeMirror");
            System.IO.Directory.CreateDirectory(folder);
            var target = new RenderTexture(1280, 720, 24);
            var frame = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0);
                frame.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, shot + ".png"), frame.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
