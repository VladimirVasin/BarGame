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
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Four seconds of the real hearth shader at 24 fps, plus its gameplay frame.")]
        public IEnumerator MothersHouseFireMotion()
        {
            const int FrameCount = 96;
            const float FrameSeconds = 1f / 24f;
            float previousCaptureDelta = Time.captureDeltaTime;
            MothersHouseInteriorRoot interior = null;
            Camera camera = null;
            Vector3 cameraPosition = default;
            Quaternion cameraRotation = default;
            float cameraFieldOfView = 60f;
            float cameraAspect = 1f;
            bool cameraFollowEnabled = false;
            bool fixedCameraEnabled = false;
            bool cameraStateCaptured = false;
            IDisposable pause = null;
            try
            {
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                Time.captureDeltaTime = FrameSeconds;
                AsyncOperation load = SceneManager.LoadSceneAsync(
                    SceneIds.MothersHouseInterior, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    interior = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                    if (load.isDone && interior != null && interior.IsInitialized &&
                        !SceneTransitionService.IsTransitioning)
                        break;
                    yield return null;
                }

                Assert.That(load.isDone, Is.True);
                Assert.That(interior, Is.Not.Null);
                Assert.That(interior.IsInitialized, Is.True);
                Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                for (int frame = 0; frame < SettleFrames; frame++)
                    yield return null;

                MothersHouseFireFlicker fire = interior.Atmosphere.FireFlicker;
                Assert.That(fire, Is.Not.Null);
                Assert.That(fire.isActiveAndEnabled, Is.True);
                Assert.That(fire.Flames.Count, Is.EqualTo(2));
                foreach (Renderer flame in fire.Flames)
                {
                    Assert.That(flame.sharedMaterial, Is.Not.Null);
                    Assert.That(flame.sharedMaterial.shader.name,
                        Is.EqualTo("BarPromenade/MothersHouseFlame"));
                    Assert.That(flame.sharedMaterial.shader.isSupported, Is.True);
                }

                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                cameraPosition = camera.transform.position;
                cameraRotation = camera.transform.rotation;
                cameraFieldOfView = camera.fieldOfView;
                cameraAspect = camera.aspect;
                cameraFollowEnabled = interior.CameraFollow.enabled;
                fixedCameraEnabled = interior.FixedCamera.enabled;
                cameraStateCaptured = true;
                camera.aspect = (float)Width / Height;
                // Photograph the ordinary player camera before borrowing it.
                CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior,
                    "fire-motion-gameplay");
                interior.FixedCamera.enabled = false;
                interior.CameraFollow.enabled = false;
                Vector3 position = interior.Room.TransformPoint(
                    new Vector3(-0.25f, 0.91f, 2.18f));
                Vector3 target = interior.Room.TransformPoint(
                    new Vector3(0f, 0.69f, 3.53f));
                camera.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation(target - position, interior.Room.up));
                camera.fieldOfView = 43f;

                // The shader consumes the same owned clock as the light.
                // Pausing must freeze that clock, not let GPU _Time run on.
                pause = GameTimeScaleRuntime.AcquirePause();
                yield return null;
                float pausedClock = fire.ElapsedSeconds;
                var properties = new MaterialPropertyBlock();
                fire.Flames[0].GetPropertyBlock(properties);
                float pausedShaderTime = properties.GetFloat("_FireTime");
                yield return null;
                yield return null;
                Assert.That(fire.ElapsedSeconds, Is.EqualTo(pausedClock));
                foreach (Renderer flame in fire.Flames)
                {
                    flame.GetPropertyBlock(properties);
                    Assert.That(properties.GetFloat("_FireTime"), Is.EqualTo(pausedShaderTime));
                }
                pause.Dispose();
                pause = null;

                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(),
                    "Captures", SceneIds.MothersHouseInterior, "fire-motion"));
                float startClock = fire.ElapsedSeconds;
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    // Ordinary Update advances the live fire once per frame;
                    // no seeking, light changes or relocated flame meshes.
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior,
                        $"fire-motion/{frame:000}");
                }
                Assert.That(fire.ElapsedSeconds - startClock,
                    Is.EqualTo(FrameCount * FrameSeconds).Within(0.01f));
            }
            finally
            {
                pause?.Dispose();
                Time.captureDeltaTime = previousCaptureDelta;
                if (cameraStateCaptured)
                {
                    if (interior != null)
                    {
                        interior.FixedCamera.enabled = fixedCameraEnabled;
                        interior.CameraFollow.enabled = cameraFollowEnabled;
                    }
                    if (camera != null)
                    {
                        camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                        camera.fieldOfView = cameraFieldOfView;
                        camera.aspect = cameraAspect;
                    }
                }
                GameSessionState.BeginNewGame();
            }
        }
    }
}
