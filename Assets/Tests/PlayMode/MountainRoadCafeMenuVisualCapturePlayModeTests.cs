using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using BarPromenade.Runtime.World;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Frames of the cafe menu close-up, for looking at, not for asserting.
    /// It writes what the seated hero actually sees plus an overhead
    /// reference of the same booklet, and logs each line's clearance above
    /// the authored page plane so a buried line can be read as a number.
    /// </summary>
    public sealed class MountainRoadCafeMenuVisualCapturePlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 30f;
        private const float LoadTimeoutSeconds = 60f;
        private const int MaximumSeatFrames = 180;
        private const int Width = 1280;
        private const int Height = 720;

        private RenderTexture renderTarget;
        private Texture2D frameBuffer;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (renderTarget != null)
            {
                renderTarget.Release();
                Object.Destroy(renderTarget);
                renderTarget = null;
            }

            if (frameBuffer != null)
            {
                Object.Destroy(frameBuffer);
                frameBuffer = null;
            }

            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
            Scene road = SceneManager.GetSceneByName(SceneIds.MountainRoad);
            if (!road.isLoaded)
            {
                yield break;
            }

            Scene blank = SceneManager.CreateScene(
                "Cafe Menu Capture Teardown");
            SceneManager.SetActiveScene(blank);
            yield return SceneManager.UnloadSceneAsync(road);
        }

        [UnityTest]
        [Explicit("Capture, not a test. Look at Captures/CafeMenu/.")]
        public IEnumerator CafeMenu_PhotographsTheOpenedBooklet()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return null;

            CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                string.Equals(
                    candidate.Plan.Id,
                    root.Plan.Terminal.Site.CounterSeat.StableId,
                    StringComparison.Ordinal));
            TeleportPlayer(
                root.Player,
                seat.Plan.EntryRootPosition,
                seat.Plan.EntryRotation);
            yield return null;

            seat.Interact(root.Player.Interactor);
            int seatFrames = 0;
            while (!seat.IsSeated && seatFrames++ < MaximumSeatFrames)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.True);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            MountainRoadCafeMenuController menu = root.CafeMenu;
            cast.Advance(
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds +
                MountainRoadCafeServiceTimeline.NoticeSeconds);
            int menuFrames = 0;
            while (menu.State != MountainRoadCafeMenuState.Open &&
                   menuFrames++ < 30)
            {
                yield return null;
            }

            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Open));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!root.CafeSeatView.IsMenuFocusComplete &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(root.CafeSeatView.IsMenuFocusComplete, Is.True);
            yield return null;

            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                "CafeMenu");
            Directory.CreateDirectory(folder);
            renderTarget = new RenderTexture(
                Width,
                Height,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = "Cafe Menu Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            frameBuffer = new Texture2D(
                Width,
                Height,
                TextureFormat.RGB24,
                false);

            Camera gameCamera = root.CameraFollow.GetComponent<Camera>();
            ReportGeometry(root, menu, gameCamera);

            yield return CaptureFrom(gameCamera, folder, "menu-focus.png");

            // Overhead reference: the same booklet seen square on, so the
            // page layout can be judged apart from the seated framing.
            MountainRoadCafeMenuPresentation presentation = menu.Presentation;
            Transform book = presentation.PropRoot;
            var probeObject = new GameObject("Cafe Menu Probe Camera");
            try
            {
                Camera probe = probeObject.AddComponent<Camera>();
                probe.CopyFrom(gameCamera);
                probe.targetTexture = null;
                probe.fieldOfView = 40f;
                probe.nearClipPlane = 0.02f;
                probe.transform.position = book.position + (Vector3.up * 0.6f);
                probe.transform.rotation = Quaternion.LookRotation(
                    Vector3.down,
                    book.forward);
                yield return CaptureFrom(probe, folder, "menu-overhead.png");

                probe.transform.position = book.position +
                    (Vector3.up * 0.42f) - (book.forward * 0.30f);
                probe.transform.rotation = Quaternion.LookRotation(
                    book.position - probe.transform.position,
                    Vector3.up);
                yield return CaptureFrom(
                    probe,
                    folder,
                    "menu-three-quarter.png");
            }
            finally
            {
                Object.Destroy(probeObject);
            }
        }

        private IEnumerator CaptureFrom(
            Camera camera,
            string folder,
            string fileName)
        {
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = renderTarget;
            camera.Render();
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTarget;
            frameBuffer.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            frameBuffer.Apply();
            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, frameBuffer.EncodeToPNG());
            Debug.Log("Cafe menu capture wrote " + path);
            yield return null;
        }

        /// <summary>
        /// Logs the authored page basis and, for every glyph corner, its
        /// signed height above the plane the three text anchors define.
        /// A negative number is a glyph sunk into the paper.
        /// </summary>
        private static void ReportGeometry(
            MountainRoadRoot root,
            MountainRoadCafeMenuController menu,
            Camera camera)
        {
            MountainRoadCafeAssetRegistry model = root.World.Cafe.Model;
            MountainRoadCafeMenuPresentation presentation = menu.Presentation;
            model.TryGetAnchor("MenuText.Item.00", out Transform item00);
            model.TryGetAnchor("MenuText.Item.01", out Transform item01);
            model.TryGetAnchor("MenuText.Selection", out Transform selection);
            model.TryGetAnchorBinding(
                "MenuText.Item.00",
                out MountainRoadCafeAnchorBinding binding);
            Vector3 authoredNormal = binding.WorldForward(model.ModelRoot);
            Vector3 alongLine =
                (item00.position - selection.position).normalized;
            Vector3 alongColumn =
                (item00.position - item01.position).normalized;
            Vector3 planeNormal =
                Vector3.Cross(alongColumn, alongLine).normalized;
            if (Vector3.Dot(planeNormal, Vector3.up) < 0f)
            {
                planeNormal = -planeNormal;
            }

            var report = new StringBuilder();
            report.AppendLine("CAFE MENU GEOMETRY");
            report.AppendLine(
                "  model root scale   " + model.ModelRoot.lossyScale);
            report.AppendLine("  authored normal    " + authoredNormal);
            report.AppendLine("  measured normal    " + planeNormal);
            report.AppendLine(
                "  authored vs measured deg " +
                Vector3.Angle(authoredNormal, planeNormal).ToString("0.00"));
            report.AppendLine(
                "  camera position    " + camera.transform.position);
            report.AppendLine(
                "  camera forward     " + camera.transform.forward);
            report.AppendLine(
                "  camera fov         " + camera.fieldOfView.ToString("0.0"));
            report.AppendLine(
                "  camera to book m   " +
                Vector3.Distance(
                    camera.transform.position,
                    presentation.PropRoot.position).ToString("0.000"));
            report.AppendLine(
                "  grazing angle deg  " +
                (90f - Vector3.Angle(
                    camera.transform.forward,
                    -planeNormal)).ToString("0.0"));

            Vector3 planePoint = item00.position;
            foreach (TMP_Text text in presentation.ItemLines
                         .Concat(new[] { presentation.SelectionMarker }))
            {
                if (text == null)
                {
                    continue;
                }

                text.ForceMeshUpdate();
                report.AppendLine("  \"" + text.text + "\"");
                report.AppendLine(
                    "    font size " + text.fontSize.ToString("0.000") +
                    "  chars " + text.textInfo.characterCount +
                    "  lines " + text.textInfo.lineCount +
                    "  overflowing " + text.isTextOverflowing);
                float lowest = float.MaxValue;
                float highest = float.MinValue;
                for (int index = 0;
                     index < text.textInfo.characterCount;
                     index++)
                {
                    TMP_CharacterInfo character =
                        text.textInfo.characterInfo[index];
                    if (!character.isVisible)
                    {
                        continue;
                    }

                    foreach (Vector3 corner in new[]
                             {
                                 character.bottomLeft,
                                 character.topLeft,
                                 character.topRight,
                                 character.bottomRight
                             })
                    {
                        float height = Vector3.Dot(
                            text.transform.TransformPoint(corner) - planePoint,
                            planeNormal);
                        lowest = Mathf.Min(lowest, height);
                        highest = Mathf.Max(highest, height);
                    }
                }

                report.AppendLine(
                    "    clearance above page plane  min " +
                    (lowest * 1000f).ToString("0.00") + " mm  max " +
                    (highest * 1000f).ToString("0.00") + " mm");
                Vector3 screen =
                    camera.WorldToScreenPoint(text.transform.position);
                report.AppendLine(
                    "    anchor on screen  " + screen.ToString("F1") +
                    "  world " + text.transform.position.ToString("F4"));
                Bounds bounds = text.GetComponent<Renderer>().bounds;
                report.AppendLine(
                    "    renderer bounds size " + bounds.size.ToString("F4"));
            }

            Debug.Log(report.ToString());
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<MountainRoadRoot> capture)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.MountainRoad,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            MountainRoadRoot root = null;
            while (root == null && Time.realtimeSinceStartup < deadline)
            {
                root = Object.FindAnyObjectByType<MountainRoadRoot>();
                if (root == null)
                {
                    yield return null;
                }
            }

            Assert.That(root, Is.Not.Null);
            capture(root);
        }

        private static void TeleportPlayer(
            PlayerRuntime player,
            Vector3 position,
            Quaternion rotation)
        {
            CharacterController controller =
                player.GameObject.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }

            player.GameObject.transform.SetPositionAndRotation(
                position,
                rotation);
            if (controller != null)
            {
                controller.enabled = wasEnabled;
            }

            Physics.SyncTransforms();
        }
    }
}
