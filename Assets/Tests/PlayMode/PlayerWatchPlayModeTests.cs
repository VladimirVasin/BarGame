using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerWatchPlayModeTests
    {
        [UnityTest]
        public IEnumerator CheckWatch_UsesRealRigAndClockAndRestoresChaseAndFixedCameras()
        {
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                var mouse = InputSystem.AddDevice<Mouse>();
                GameInput.HandleApplicationFocus(true);
                GameSessionState.BeginNewGame("Wristwatch PlayMode");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60 + 34), Is.True);

                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                yield return WaitFor(() =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && city.Player.Motor.IsGrounded &&
                           GameInput.CanRead(GameInputContext.Gameplay);
                }, "City playable");
                yield return Check(input, keyboard, mouse, city.Player, Camera.main, false);

                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                HomeInteriorRoot home = null;
                yield return WaitFor(() =>
                {
                    home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                    return home != null && home.IsInitialized && home.Player.Motor.IsGrounded &&
                           GameInput.CanRead(GameInputContext.Gameplay);
                }, "Home playable");
                yield return Check(input, keyboard, mouse, home.Player, Camera.main, true);
            }
            finally
            {
                input.TearDown();
            }
        }

        private static IEnumerator Check(InputTestFixture input, Keyboard keyboard, Mouse mouse,
            PlayerRuntime player, Camera camera, bool fixedView)
        {
            var watch = player.GameObject.GetComponent<PlayerWatchController>();
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var capture = player.GameObject.AddComponent<WatchLateCapture>();
            camera.aspect = 16f / 9f;
            Assert.That(follow.FixedPoseActive, Is.EqualTo(fixedView));
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();
            yield return null;
            Vector3 start = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float lens = camera.fieldOfView, nearPlane = camera.nearClipPlane;
            Vector3 feet = player.GameObject.transform.position;

            input.Press(keyboard.hKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.hKey, queueEventOnly: true);
            yield return WaitFor(() => watch.Progress >= .60f, "Watch pull");
            Assert.That(watch.Progress, Is.LessThan(1f));
            Assert.That(watch.CuffAmount, Is.GreaterThan(0f));
            capture.Pending = () => Capture(camera, fixedView ? "home-pull" : "city-pull");
            Assert.That(watch.PinchError, Is.LessThan(.025f), "The right pinch must reach the left cuff.");

            yield return WaitFor(() => watch.IsOpen, "Eye view");
            Vector3 dial = default;
            float dialFacing = 0f, wristDistance = 0f;
            var registry = ((Player3DCharacterPresentation)player.Visual).Registry;
            Assert.That(registry.TryGetPart(Player3DAnatomicalPart.LeftHand, out var leftHand), Is.True);
            Assert.That(registry.TryGetPart(Player3DAnatomicalPart.LeftForearm, out var skin), Is.True);
            capture.Pending = () =>
            {
                Capture(camera, fixedView ? "home-dial" : "city-dial");
                dial = camera.WorldToViewportPoint(watch.Dial.position);
                dialFacing = Vector3.Dot(watch.Dial.forward, (camera.transform.position - watch.Dial.position).normalized);
                wristDistance = Vector3.Distance(watch.Dial.position, leftHand.Bone.position);
            };
            yield return WaitFor(() => capture.Pending == null, "Rendered eye view");
            Assert.That(dial.x, Is.InRange(.24f, .55f), $"Dial viewport {dial}");
            Assert.That(dial.y, Is.InRange(.12f, .43f));
            Assert.That(dial.z, Is.InRange(.23f, .60f));
            Assert.That(dialFacing, Is.GreaterThan(.75f));
            Assert.That(wristDistance, Is.InRange(.04f, .075f));
            Assert.That(skin.Renderer.enabled, Is.True);
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(watch.DisplayedMinute, Is.EqualTo(GameSessionState.GameMinuteOfDay));
            GameSessionState.AdvanceGameTime(4f);
            yield return null;
            yield return null;
            Assert.That(watch.DisplayedMinute, Is.EqualTo(GameSessionState.GameMinuteOfDay));
            if (fixedView)
            {
                yield return CheckColon(registry, capture, camera);
                Assert.That(watch.IsOpen, Is.True, "Pausing holds the wristwatch pose.");
            }

            Quaternion firstPerson = camera.transform.rotation;
            input.Set(mouse.delta, new Vector2(90f, 60f), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(firstPerson, camera.transform.rotation), Is.GreaterThan(5f));
            Assert.That(Vector3.Distance(feet, player.GameObject.transform.position), Is.LessThan(.05f));

            input.Press(keyboard.hKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.hKey, queueEventOnly: true);
            yield return WaitFor(() => player.Motor.InputEnabled, "Watch close");
            Assert.That(Vector3.Distance(start, camera.transform.position), Is.LessThan(.04f));
            Assert.That(Quaternion.Angle(rotation, camera.transform.rotation), Is.LessThan(1f));
            Assert.That(camera.fieldOfView, Is.EqualTo(lens).Within(.5f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(nearPlane));
            Assert.That(player.Interactor.InputEnabled, Is.True);
            Assert.That(follow.FixedPoseActive, Is.EqualTo(fixedView));
            Assert.That(watch.CuffAmount, Is.Zero);

            input.Press(keyboard.hKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.hKey, queueEventOnly: true);
            yield return WaitFor(() => watch.Progress > .15f, "Partial watch opening");
            input.Press(keyboard.hKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.hKey, queueEventOnly: true);
            yield return WaitFor(() => player.Motor.InputEnabled, "Reversed opening");
            Assert.That(watch.Progress, Is.Zero);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        private static IEnumerator CheckColon(Player3DAssetRegistry registry, WatchLateCapture capture, Camera camera)
        {
            var lcd = Array.Find(registry.GetComponentsInChildren<MeshRenderer>(),
                renderer => renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.shader.name == "BarPromenade/Watch LCD");
            Assert.That(lcd, Is.Not.Null);
            int colon = Shader.PropertyToID("_ColonVisible");
            int digits = Shader.PropertyToID("_Segments");
            var block = new MaterialPropertyBlock();
            const float step = (float)(1d / (60d * GameTimeState.GameMinutesPerRealSecond));

            using (GameTimeScaleRuntime.AcquirePause())
            {
                // Centre on an even second and advance calendar time explicitly.
                double now = GameSessionState.GameTimeOfDayMinutes;
                double start = Math.Floor(now) + 1d + 10.5d / 60d;
                GameSessionState.AdvanceGameTime((float)((start - now) / GameTimeState.GameMinutesPerRealSecond));
                yield return null;
                lcd.GetPropertyBlock(block);
                Vector4 segmentMasks = block.GetVector(digits);
                Assert.That(block.GetFloat(colon), Is.EqualTo(1f));

                GameSessionState.AdvanceGameTime(step);
                capture.Pending = () => Capture(camera, "home-colon-off");
                yield return null;
                lcd.GetPropertyBlock(block);
                Assert.That(block.GetFloat(colon), Is.Zero);
                Assert.That(block.GetVector(digits), Is.EqualTo(segmentMasks));
                yield return null;
                lcd.GetPropertyBlock(block);
                Assert.That(block.GetFloat(colon), Is.Zero, "A paused calendar holds the blink phase.");

                GameSessionState.AdvanceGameTime(step);
                yield return null;
                lcd.GetPropertyBlock(block);
                Assert.That(block.GetFloat(colon), Is.EqualTo(1f));
                Assert.That(block.GetVector(digits), Is.EqualTo(segmentMasks));
            }
        }

        private static void Capture(Camera camera, string name)
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                AreaCaptureFixture.CaptureCurrentCamera(camera, "PlayerWatch", name, 960, 540);
        }

        private static IEnumerator WaitFor(Func<bool> condition, string phase)
        {
            // First rendering of the interior can compile shaders in a single frame.
            float deadline = Time.realtimeSinceStartup + 90f;
            while (!condition())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), phase);
                yield return null;
            }
        }
    }

    [DefaultExecutionOrder(20000)]
    internal sealed class WatchLateCapture : MonoBehaviour
    {
        internal Action Pending;

        private void LateUpdate()
        {
            Action action = Pending;
            Pending = null;
            action?.Invoke();
        }
    }
}
