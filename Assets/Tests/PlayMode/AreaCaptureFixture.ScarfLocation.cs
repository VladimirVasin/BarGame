using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Timeout(60000)]
        [Explicit("Focused scarf location policy on the production rig, with rendered gesture frames. Run alone.")]
        public IEnumerator ScarfLocationPostureWaitsForThirdPersonAndSurvivesTravel()
        {
            const float frameSeconds = 0.1f;
            float previousDelta = Time.captureDeltaTime;
            Scene previousScene = SceneManager.GetActiveScene();
            Scene houseScene = default;
            Scene villageScene = default;
            PlayerRuntime player = default;
            Camera camera = null;
            RenderTexture target = null;
            IDisposable pause = null;
            IDisposable ride = null;
            Player3DHeadVisibility hiddenHead = null;
            PlayerScarfController.MouthAccess mouth = null;
            try
            {
                GameSessionState.BeginNewGame();
                Time.captureDeltaTime = frameSeconds;
                houseScene = SceneManager.CreateScene(SceneIds.MothersHouseInterior);
                SceneManager.SetActiveScene(houseScene);
                camera = new GameObject("Scarf Location Inspection Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.16f, .18f, .21f);
                camera.nearClipPlane = .03f;
                camera.farClipPlane = 20f;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                target = new RenderTexture(640, 480, 24);
                target.Create();
                camera.targetTexture = target;

                player = CreateScarfLocationHero(camera);
                var scarf = player.GameObject.GetComponent<PlayerScarfController>();
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.Scarf), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true), Is.True);
                var visual = (Player3DCharacterPresentation)player.Visual;
                hiddenHead = Player3DHeadVisibility.Hide(visual.Registry);
                yield return ScarfLocationSeconds(PlayerScarfController.LocationDelaySeconds + 1f);
                AssertScarfLocationPosture(scarf, false,
                    "The delay must not elapse inside a first-person view.");

                hiddenHead.Restore();
                hiddenHead = null;
                AimScarfHero(camera, player, new Vector3(.65f, 1.5f, 1.2f));
                yield return ScarfLocationSeconds(2f);
                yield return CaptureScarfFrame(camera, "location-00-raised-before-delay");
                pause = GameTimeScaleRuntime.AcquirePause();
                yield return ScarfLocationSeconds(PlayerScarfController.LocationDelaySeconds + 1f);
                AssertScarfLocationPosture(scarf, false, "Pausing cannot spend the location delay.");
                pause.Dispose();
                pause = null;
                yield return ScarfLocationSeconds(2.2f);
                AssertScarfLocationPosture(scarf, false, "The ordinary view must last about five seconds first.");

                bool sawLoweringGesture = false;
                for (int frame = 0; frame < 20; frame++)
                {
                    yield return null;
                    if (scarf.Presentation.MouthLowered <= .1f || scarf.Presentation.MouthLowered >= .65f)
                        continue;
                    Assert.That(scarf.IsGestureActive, Is.True);
                    yield return CaptureScarfFrame(camera, "location-01-lowering-with-hand");
                    sawLoweringGesture = true;
                    break;
                }
                Assert.That(sawLoweringGesture, Is.True, "The scarf must lower through the existing hand gesture.");
                yield return ScarfLocationSeconds(PlayerScarfController.GestureSeconds + .3f);
                AssertScarfLocationPosture(scarf, true, "A house is outside the village location policy.");
                Assert.That(GameSessionState.ScarfRestingLowered, Is.True);
                yield return CaptureScarfFrame(camera, "location-02-resting-lowered");
                mouth = PlayerScarfController.RequireMouthAccess(player, camera);
                Assert.That(mouth.IsReady, Is.True, "An already lowered scarf needs no extra gesture to vomit or drink.");
                mouth.Dispose();
                mouth = null;
                yield return ScarfFrames(3);
                AssertScarfLocationPosture(scarf, true, "Mouth access release must preserve the location posture.");

                Object.Destroy(player.GameObject);
                player = default;
                yield return null;
                villageScene = SceneManager.CreateScene(SceneIds.AlpineVillage);
                SceneManager.MoveGameObjectToScene(camera.gameObject, villageScene);
                SceneManager.SetActiveScene(villageScene);
                player = CreateScarfLocationHero(camera);
                scarf = player.GameObject.GetComponent<PlayerScarfController>();
                AssertScarfLocationPosture(scarf, true, "Recreating the hero must carry the lowered scarf into the village.");
                player.Motor.SetInputEnabled(false);
                hiddenHead = Player3DHeadVisibility.Hide(((Player3DCharacterPresentation)player.Visual).Registry);
                ride = GameSessionState.AcquireCablewayRide();
                yield return ScarfLocationSeconds(PlayerScarfController.LocationDelaySeconds + 1f);
                AssertScarfLocationPosture(scarf, true, "The cableway ride cannot spend the arrival delay.");
                ride.Dispose();
                ride = null;
                hiddenHead.Restore();
                hiddenHead = null;
                yield return ScarfLocationSeconds(PlayerScarfController.LocationDelaySeconds + 1f);
                AssertScarfLocationPosture(scarf, true,
                    "A restored third-person camera must still wait for the disembarking input lock.");
                player.Motor.SetInputEnabled(true);
                yield return ScarfLocationSeconds(4.4f);
                AssertScarfLocationPosture(scarf, true, "Village arrival has its own five-second delay.");
                yield return ScarfLocationSeconds(1.1f);
                Assert.That(scarf.IsGestureActive, Is.True);
                yield return CaptureScarfFrame(camera, "location-03-raising-with-hand");
                yield return ScarfLocationSeconds(PlayerScarfController.GestureSeconds + .3f);
                AssertScarfLocationPosture(scarf, false, "The village raises the scarf after disembarking.");
                Assert.That(GameSessionState.ScarfRestingLowered, Is.False);
                yield return CaptureScarfFrame(camera, "location-04-resting-raised");
                mouth = PlayerScarfController.RequireMouthAccess(player, camera);
                Assert.That(mouth.IsReady, Is.False, "Mouth access waits for the raised scarf to lower.");
                yield return ScarfLocationSeconds(PlayerScarfController.GestureSeconds + .3f);
                Assert.That(GameSessionState.ScarfRestingLowered, Is.False);
                Assert.That(mouth.IsReady, Is.True);
                AssertScarfLocationPosture(scarf, true, "Mouth access wins over the village's raised resting posture.");
                mouth.Dispose();
                mouth = null;
                yield return ScarfLocationSeconds(PlayerScarfController.GestureSeconds + .3f);
                AssertScarfLocationPosture(scarf, false, "Released mouth access returns to the village posture.");

                Object.Destroy(player.GameObject);
                player = default;
                yield return null;
                SceneManager.MoveGameObjectToScene(camera.gameObject, houseScene);
                SceneManager.SetActiveScene(houseScene);
                player = CreateScarfLocationHero(camera);
                scarf = player.GameObject.GetComponent<PlayerScarfController>();
                yield return ScarfLocationSeconds(PlayerScarfController.LocationDelaySeconds +
                    PlayerScarfController.GestureSeconds + .4f);
                AssertScarfLocationPosture(scarf, true, "A later interior visit lowers the scarf again.");
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.ScarfRestingLowered, Is.False);
                Assert.That(scarf.Presentation.IsEquipped, Is.False);
                Assert.That(scarf.Presentation.MouthLowered, Is.Zero);
            }
            finally
            {
                mouth?.Dispose();
                hiddenHead?.Restore();
                ride?.Dispose();
                pause?.Dispose();
                if (player.GameObject != null) Object.Destroy(player.GameObject);
                if (camera != null)
                {
                    camera.targetTexture = null;
                    Object.Destroy(camera.gameObject);
                }
                if (target != null)
                {
                    target.Release();
                    Object.Destroy(target);
                }
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (houseScene.IsValid() && houseScene.isLoaded) SceneManager.UnloadSceneAsync(houseScene);
                if (villageScene.IsValid() && villageScene.isLoaded) SceneManager.UnloadSceneAsync(villageScene);
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static PlayerRuntime CreateScarfLocationHero(Camera camera)
        {
            PlayerRuntime value = PlayerFactory.Create(null, Vector3.zero, camera, null, null);
            value.Motor.enabled = false;
            return value;
        }

        private static IEnumerator ScarfLocationSeconds(float seconds) =>
            ScarfFrames(Mathf.CeilToInt(seconds / .1f));

        private static void AssertScarfLocationPosture(PlayerScarfController scarf, bool lowered, string reason)
        {
            Assert.That(scarf.Presentation.MouthLowered, Is.EqualTo(lowered ? 1f : 0f).Within(.001f), reason);
            Assert.That(scarf.IsGestureActive, Is.False,
                "A resting scarf must not indefinitely block contextual interactions. " + reason);
        }
    }
}
