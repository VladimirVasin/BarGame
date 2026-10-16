using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Real red entrance light is blocked by the next storey's floor while retaining its local wall wash.")]
        public IEnumerator CombatWarehouseIndicatorFloorOcclusion()
        {
            float previousDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            Camera camera = null;
            Vector3 previousCameraPosition = default;
            Quaternion previousCameraRotation = default;
            float previousFieldOfView = 60f;
            bool previousFollow = true;
            Vector3 previousHeroPosition = default, previousHeroFacing = Vector3.forward;
            bool heroPositionSaved = false;
            var lightStates = new Dictionary<Light, bool>();
            var rendererStates = new Dictionary<Renderer, bool>();
            Light indicator = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                AssertWarehouseIndicatorShadowBudget(root);

                WarehouseRefrigeratorPlacement fridge = root.WarehousePlan.Refrigerators[0];
                WarehouseRoomIndicator owner = FindFishRoomIndicator(root, fridge.SourceId);
                Assert.That(owner, Is.Not.Null);
                indicator = owner.Light;
                AssertWarehouseIndicatorShadowProfile(indicator);
                Assert.That(indicator.isActiveAndEnabled && indicator.intensity > 0f, Is.True);
                camera = root.CameraFollow.Camera;
                previousCameraPosition = camera.transform.position;
                previousCameraRotation = camera.transform.rotation;
                previousFieldOfView = camera.fieldOfView;
                previousFollow = root.CameraFollow.enabled;
                previousHeroPosition = root.Hero.transform.position;
                previousHeroFacing = root.Hero.transform.forward;
                heroPositionSaved = true;
                root.CameraFollow.enabled = false;
                foreach (Renderer renderer in root.Hero.GetComponentsInChildren<Renderer>(true))
                { rendererStates.Add(renderer, renderer.forceRenderingOff); renderer.forceRenderingOff = true; }
                foreach (Renderer renderer in root.Opponent.GetComponentsInChildren<Renderer>(true))
                { rendererStates.Add(renderer, renderer.forceRenderingOff); renderer.forceRenderingOff = true; }
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                { lightStates.Add(light, light.enabled); light.enabled = light == indicator; }

                // All diagnostic renders are synchronous: the production light
                // owners do not update between source isolation and restoration.
                // No receiver geometry or material is created by this test.
                Vector3 center = root.WarehousePlan.RoomCenters[fridge.RoomIndex];
                Quaternion roomRotation = Quaternion.Euler(0f, fridge.Yaw, 0f);
                Vector3 eye = center + roomRotation * new Vector3(5.25f, 4.65f, -.4f);
                Vector3 target = center + roomRotation * new Vector3(2.94f, 3.85f, 0f);
                // The production warehouse ambient follows the real hero,
                // not the diagnostic camera. Stand on the gallery being viewed.
                root.Hero.ResetActor(center + roomRotation * new Vector3(5.25f, WarehouseArenaPlan.LevelRise, -.4f) +
                    Vector3.up * PlayerFactory.GroundedRootOffset, roomRotation * Vector3.left);
                root.Hero.ApplyMotorConstraint();
                Physics.SyncTransforms();
                WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
                foreach (KeyValuePair<Renderer, bool> state in rendererStates) state.Key.forceRenderingOff = true;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 62f;
                Color32[] shadowed = ReadIndicatorFrame(camera);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Red indicator - Upstairs blocked");
                indicator.shadows = LightShadows.None;
                Color32[] unshadowed = ReadIndicatorFrame(camera);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Red indicator - Without shadows");
                indicator.shadows = LightShadows.Hard;
                indicator.enabled = false;
                Color32[] dark = ReadIndicatorFrame(camera);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Red indicator - Light off");
                indicator.enabled = true;
                AssertWarehouseIndicatorBlockedReceivers(camera, indicator, shadowed, unshadowed, dark);

                eye = center + roomRotation * new Vector3(5.25f, 1.7f, -.4f);
                root.Hero.ResetActor(center + roomRotation * new Vector3(5.25f, 0f, -.4f) +
                    Vector3.up * PlayerFactory.GroundedRootOffset, roomRotation * Vector3.left);
                root.Hero.ApplyMotorConstraint();
                Physics.SyncTransforms();
                WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
                foreach (KeyValuePair<Renderer, bool> state in rendererStates) state.Key.forceRenderingOff = true;
                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(indicator.transform.position - eye));
                camera.fieldOfView = 60f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Red indicator - Entry wash");
                AssertRoomIndicatorSpill(root, camera, indicator.transform.position);
            }
            finally
            {
                if (root != null && heroPositionSaved)
                {
                    root.Hero.ResetActor(previousHeroPosition, previousHeroFacing);
                    root.Hero.ApplyMotorConstraint();
                    Physics.SyncTransforms();
                    WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
                }
                if (indicator != null) indicator.shadows = LightShadows.Hard;
                foreach (KeyValuePair<Light, bool> state in lightStates)
                    if (state.Key != null) state.Key.enabled = state.Value;
                foreach (KeyValuePair<Renderer, bool> state in rendererStates)
                    if (state.Key != null) state.Key.forceRenderingOff = state.Value;
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(previousCameraPosition, previousCameraRotation);
                    camera.fieldOfView = previousFieldOfView;
                }
                if (root != null) root.CameraFollow.enabled = previousFollow;
                Time.captureDeltaTime = previousDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static void AssertWarehouseIndicatorShadowProfile(Light light)
        {
            Assert.That(light.type, Is.EqualTo(LightType.Point), "The bulb still washes its own wall and floor.");
            Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
            Assert.That(light.shadowStrength, Is.EqualTo(1f));
            Assert.That(light.range, Is.LessThanOrEqualTo(3.4f));
            Assert.That(light.intensity, Is.LessThanOrEqualTo(12f));
            UniversalAdditionalLightData data = light.GetComponent<UniversalAdditionalLightData>();
            Assert.That(data, Is.Not.Null);
            Assert.That(data.usePipelineSettings, Is.False);
            Assert.That(data.additionalLightsShadowResolutionTier,
                Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow));
        }

        private static void AssertWarehouseIndicatorShadowBudget(CombatTestRoot root)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.additionalLightsShadowmapResolution, Is.EqualTo(2048));
            Assert.That(pipeline.additionalLightsShadowResolutionTierLow, Is.EqualTo(256));
            Assert.That(pipeline.additionalLightsShadowResolutionTierMedium, Is.EqualTo(512));
            Assert.That(pipeline.additionalLightsShadowResolutionTierHigh, Is.EqualTo(1024));
            int indicators = 0, activeIndicators = 0;
            long texels = 0;
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.name == "Warehouse Room Indicator Light")
                {
                    indicators++;
                    AssertWarehouseIndicatorShadowProfile(light);
                    if (light.isActiveAndEnabled && light.intensity > 0f) activeIndicators++;
                }
                if (light.shadows == LightShadows.None) continue;
                UniversalAdditionalLightData data = light.GetComponent<UniversalAdditionalLightData>();
                Assert.That(data, Is.Not.Null);
                int tier = data.additionalLightsShadowResolutionTier;
                int resolution = tier == UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh ?
                    pipeline.additionalLightsShadowResolutionTierHigh :
                    tier == UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium ?
                    pipeline.additionalLightsShadowResolutionTierMedium : pipeline.additionalLightsShadowResolutionTierLow;
                // Include the conditional fish bulb even while it is off:
                // this bounds all six Point faces during any power/fade overlap.
                texels += (long)resolution * resolution * (light.type == LightType.Point ? 6 : 1);
            }
            Assert.That(indicators, Is.EqualTo(3));
            Assert.That(activeIndicators, Is.EqualTo(3));
            Assert.That(texels, Is.LessThanOrEqualTo(49L * 256 * 256));
            Assert.That(texels, Is.LessThanOrEqualTo((long)pipeline.additionalLightsShadowmapResolution *
                pipeline.additionalLightsShadowmapResolution), "The existing additional-light atlas must contain every source.");
        }

        private static void AssertWarehouseIndicatorBlockedReceivers(Camera camera, Light indicator,
            Color32[] shadowed, Color32[] unshadowed, Color32[] dark)
        {
            const int width = 160, height = 90;
            int receivers = 0;
            float leakedRed = 0f, blockedRed = 0f;
            Vector3 source = indicator.transform.position;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3((x + .5f) / width, (y + .5f) / height, 0f));
                    if (!Physics.Raycast(ray, out RaycastHit hit, 15f, ~0, QueryTriggerInteraction.Ignore) ||
                        hit.point.y < WarehouseArenaPlan.LevelRise + .08f ||
                        hit.collider.GetComponentInParent<CombatActor>() != null) continue;
                    Vector3 delta = hit.point - source;
                    float distance = delta.magnitude;
                    if (distance < .2f || distance > indicator.range * .95f) continue;
                    Vector3 direction = delta / distance;
                    if (!Physics.Raycast(source + direction * .04f, direction, out RaycastHit blocker,
                        distance - .09f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(blocker.normal.y) < .8f ||
                        blocker.point.y < 2.9f || blocker.point.y > WarehouseArenaPlan.LevelRise + .03f) continue;
                    int index = y * width + x;
                    float unblocked = IndicatorRedContribution(unshadowed[index], dark[index]);
                    if (unblocked < 6f) continue;
                    receivers++;
                    leakedRed += unblocked;
                    blockedRed += IndicatorRedContribution(shadowed[index], dark[index]);
                }
            Assert.That(receivers, Is.GreaterThan(30),
                "Removing shadows must visibly reproduce red light on real upstairs surfaces behind a floor/roof.");
            float leak = leakedRed / receivers, residual = blockedRed / receivers;
            Debug.Log($"Test warehouse red indicator: unshadowed red={leak:F2}, shadowed red={residual:F2}.");
            Assert.That(residual, Is.LessThan(leak * .18f + 1.5f),
                "The floor must block the real Point source; reducing its range or removing its local wash cannot satisfy this proof.");
        }

        private static float IndicatorRedContribution(Color32 lit, Color32 dark) =>
            Mathf.Max(0f, lit.r - dark.r - Mathf.Max(0f, Mathf.Max(lit.g - dark.g, lit.b - dark.b)));
    }
}
