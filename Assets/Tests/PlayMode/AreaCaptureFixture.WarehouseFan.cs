using System.Collections;
using System.IO;
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
        [Explicit("Real overhead fan light, moving floor shadows, pause and scene ownership.")]
        public IEnumerator CombatWarehouseFanShadows()
        {
            float previousDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
#if UNITY_EDITOR
                System.Type.GetType("BarPromenade.Editor.CombatWarehouseAssetValidation, BarPromenade.Editor", true)
                    .GetMethod("ValidateOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                AssertWarehouseLightAndMechanisms(root);
                WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                Light key = machines.FanLight;
                Assert.That(Vector3.Distance(key.transform.position,new Vector3(0f,11.8f,0f)), Is.LessThan(.001f));
                Assert.That(Vector3.Dot(key.transform.forward,Vector3.down), Is.GreaterThan(.999f));
                Assert.That(key.transform.position.y - machines.FanRotor.position.y, Is.GreaterThan(2f));
                Assert.That(key.type, Is.EqualTo(LightType.Spot));
                Assert.That(key.cookie, Is.Null, "The imported rotating blades supply the pattern.");
                Assert.That(key.spotAngle, Is.InRange(120f,130f));
                Assert.That(key.range, Is.GreaterThanOrEqualTo(28f));
                UniversalAdditionalLightData keyData = key.GetComponent<UniversalAdditionalLightData>();
                Assert.That(keyData.usePipelineSettings, Is.False);
                Assert.That(keyData.additionalLightsShadowResolutionTier,
                    Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh));
                Assert.That(root.Flashlight.SpotLight.GetComponent<UniversalAdditionalLightData>().additionalLightsShadowResolutionTier,
                    Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium));

                Camera camera = root.CameraFollow.Camera;
                CaptureCurrentCamera(camera,SceneIds.CombatTest,"Test warehouse fan - Entrance lighting");
                Assert.That(root.PauseMenu.Open(), Is.True);
                Quaternion pausedPose = machines.FanRotor.localRotation;
                double pausedTime = machines.ElapsedSeconds;
                float power = key.intensity;
                machines.Advance(3f);
                Assert.That(machines.ElapsedSeconds, Is.EqualTo(pausedTime));
                Assert.That(Quaternion.Angle(pausedPose,machines.FanRotor.localRotation), Is.LessThan(.001f));
                Assert.That(key.enabled && key.intensity == power, Is.True, "Pause freezes blades while the lamp stays steady.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return null;

                root.CameraFollow.enabled = false;
                machines.enabled = false;
                foreach (Renderer renderer in root.Hero.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
                foreach (Renderer renderer in root.Opponent.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
                camera.transform.SetPositionAndRotation(new Vector3(0f,1.7f,-5.2f),
                    Quaternion.LookRotation(new Vector3(0f,8.2f,5.2f)));
                camera.fieldOfView = 78f;
                CaptureCurrentCamera(camera,SceneIds.CombatTest,"Test warehouse fan - Ceiling source");
                camera.transform.SetPositionAndRotation(new Vector3(0f,7.9f,-5.6f),
                    Quaternion.LookRotation(new Vector3(0f,-7.9f,5.6f)));
                camera.fieldOfView = 78f;
                CaptureCurrentCamera(camera,SceneIds.CombatTest,"Test warehouse fan - Open gallery lighting");

                // Isolate this real source only for the shadow diagnostic. The
                // production photographs above retain all eight sources.
                foreach (Light light in root.GetComponentsInChildren<Light>()) light.enabled = light == key;
                Color32[] first = CaptureWarehouseFanPixels(camera,"Test warehouse fan - First blade position");
                Quaternion firstPose = machines.FanRotor.localRotation;
                machines.Advance(3f);
                Assert.That(Quaternion.Angle(firstPose,machines.FanRotor.localRotation), Is.InRange(26.9f,27.1f));
                Color32[] second = CaptureWarehouseFanPixels(camera,"Test warehouse fan - Second blade position");
                key.shadows = LightShadows.None;
                Color32[] unshadowed = CaptureWarehouseFanPixels(camera,"Test warehouse fan - Shadow diagnostic");
                key.shadows = LightShadows.Hard;
                key.enabled = false;
                Color32[] dark = CaptureWarehouseFanPixels(camera,"Test warehouse fan - Source off");
                key.enabled = true;
                AssertWarehouseFanFloorShadows(camera,first,second,unshadowed,dark);

                foreach (Light light in root.GetComponentsInChildren<Light>()) light.enabled = true;
                foreach (Renderer renderer in root.Hero.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = false;
                foreach (Renderer renderer in root.Opponent.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = false;
                machines.enabled = true;
                root.CameraFollow.enabled = true;
                Light previous = key;
                Assert.That(root.NewWarehouseLayout(1), Is.True);
                yield return null;
                Assert.That(previous == null, Is.True, "Replacing the layout must destroy its old light.");
                AssertWarehouseLightAndMechanisms(root);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu,LoadSceneMode.Single);
                foreach (Light light in Object.FindObjectsByType<Light>())
                    Assert.That(light.name, Is.Not.EqualTo(WarehouseArenaLighting.FanLightName));
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static Color32[] CaptureWarehouseFanPixels(Camera camera, string name)
        {
            CaptureCurrentCamera(camera,SceneIds.CombatTest,name);
            var frame = new Texture2D(2,2,TextureFormat.RGB24,false);
            try
            {
                frame.LoadImage(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"Captures",SceneIds.CombatTest,name + ".png")));
                return frame.GetPixels32();
            }
            finally { Object.DestroyImmediate(frame); }
        }

        private static void AssertWarehouseFanFloorShadows(Camera camera, Color32[] first,
            Color32[] second, Color32[] unshadowed, Color32[] dark)
        {
            int samples = 0, moved = 0;
            float shadowedLight = 0f, fullLight = 0f, darkLight = 0f;
            // Only actual exposed horizontal floor receivers count. Visible
            // blades, moving machines, cargo and gallery edges cannot pass this proof.
            for (int y = 80; y < Height - 80; y += 8)
                for (int x = 100; x < Width - 100; x += 8)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3((x + .5f) / Width,(y + .5f) / Height,0f));
                    if (!Physics.Raycast(ray,out RaycastHit hit,30f,~0,QueryTriggerInteraction.Ignore) ||
                        hit.normal.y < .95f || Mathf.Abs(hit.point.y) > .04f ||
                        Mathf.Abs(hit.point.x) > 5.8f || Mathf.Abs(hit.point.z) > 5.8f ||
                        new Vector2(hit.point.x,hit.point.z).magnitude < 3.8f) continue;
                    int index = y * Width + x;
                    float a = FanPixelLight(first[index]), b = FanPixelLight(second[index]);
                    if (Mathf.Abs(a - b) > .025f) moved++;
                    shadowedLight += b;
                    fullLight += FanPixelLight(unshadowed[index]);
                    darkLight += FanPixelLight(dark[index]);
                    samples++;
                }
            Assert.That(samples, Is.GreaterThan(100));
            Debug.Log($"Test warehouse fan floor: moved={(float)moved / samples:F3}, shadowed={shadowedLight / samples:F3}, " +
                $"unshadowed={fullLight / samples:F3}, off={darkLight / samples:F3}");
            Assert.That((float)moved / samples, Is.GreaterThan(.03f), "Turning the blades must move real floor shadows.");
            Assert.That(fullLight / samples, Is.GreaterThan(shadowedLight / samples + .006f),
                "Removing geometry shadows must visibly brighten the same floor.");
            Assert.That(fullLight / samples, Is.GreaterThan(darkLight / samples + .025f),
                "The overhead source must really illuminate the warehouse after final presentation.");
        }

        private static float FanPixelLight(Color32 color) =>
            (color.r * .2126f + color.g * .7152f + color.b * .0722f) / 255f;
    }
}
