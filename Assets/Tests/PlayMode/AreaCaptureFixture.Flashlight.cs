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
    public sealed class CombatFlashlightCaptureSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CombatFlashlightAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("One real warehouse flashlight, rendered light and shadows, rig motion and ownership cleanup.")]
        [PrebuildSetup(typeof(CombatFlashlightCaptureSetup))]
        public IEnumerator CombatWarehouseFlashlight()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            GameObject blocker = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root, Is.Not.Null);
                Assert.That(root.IsInitialized && root.WarehousePlan != null, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;

                CombatChestFlashlight flashlight = root.Flashlight;
                Assert.That(flashlight, Is.Not.Null);
                Light spot = flashlight.SpotLight;
                Assert.That(flashlight.ModelRoot && flashlight.Lens && spot, Is.True);
                Transform chest = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Chest;
                Assert.That(flashlight.ModelRoot.transform.IsChildOf(chest), Is.True,
                    "The actual imported flashlight must follow the production hero's chest bone.");
                Assert.That(flashlight.ModelRoot.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
                CombatFlashlightBindings bindings = Resources.Load<CombatFlashlightBindings>(
                    CombatFlashlightAssetProvider.ResourceFolder + "Bindings");
                Assert.That(bindings, Is.Not.Null);
                float leftChest = Vector3.Dot(flashlight.ModelRoot.transform.position - chest.position,
                    root.Hero.transform.right);
                Assert.That(leftChest, Is.InRange(-.32f, -.04f),
                    "In the standing pose the imported flashlight must sit on the hero's anatomical left chest.");
                AssertFlashlightMount(flashlight, chest, bindings);
                AssertWarehouseFlashlightAndPracticals(root, spot);
                Assert.That(spot.type, Is.EqualTo(LightType.Spot));
                Assert.That(spot.shadows, Is.Not.EqualTo(LightShadows.None));
                Assert.That(spot.shadowStrength, Is.EqualTo(1f).Within(.001f));
                Assert.That(spot.innerSpotAngle, Is.LessThan(spot.spotAngle));
                Assert.That(spot.range, Is.InRange(10f, 20f));
                Assert.That(spot.intensity, Is.InRange(CombatChestFlashlight.Intensity * .01f, CombatChestFlashlight.Intensity));
                Assert.That(RenderSettings.ambientLight.maxColorComponent, Is.GreaterThan(0f));
                Assert.That(RenderSettings.sun == null || !RenderSettings.sun.enabled, Is.True);

                Camera camera = root.CameraFollow.Camera;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test flashlight warehouse entry");
                root.CameraFollow.enabled = false;
                yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight left chest");

                // The south loading wall is three metres from the authored entrance.
                // Its real dark painted steel is a demanding readability receiver.
                root.Hero.ResetActor(root.WarehousePlan.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset,
                    Vector3.back);
                root.Opponent.ResetActor(new Vector3(-8.8f, PlayerFactory.GroundedRootOffset, -6.8f), Vector3.right);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(Physics.Raycast(spot.transform.position + spot.transform.forward * .08f,
                    spot.transform.forward, out RaycastHit wall, spot.range, ~0, QueryTriggerInteraction.Ignore), Is.True,
                    "The chest beam must reach the actual warehouse shell.");
                Assert.That(wall.collider.transform.IsChildOf(root.transform), Is.True);
                Assert.That(wall.distance, Is.InRange(1.5f, 5f));
                Vector3 receiver = wall.point;
                Vector3 cameraPosition = spot.transform.position + spot.transform.right * 3.8f +
                    spot.transform.forward * 1.1f + Vector3.up * .45f;
                camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(receiver - cameraPosition));
                camera.fieldOfView = 58f;
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test flashlight bright loading door beam");
                float lit = SampleFlashlightReceiver(camera, receiver);
                float dark;
                spot.enabled = false;
                try
                {
                    // Keep the receiver, camera and exposure identical for this diagnostic comparison.
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test flashlight dim warehouse");
                    dark = SampleFlashlightReceiver(camera, receiver);
                }
                finally { spot.enabled = true; }
                LogFlashlightAmbient(camera, root, wall, lit, dark);
                Assert.That(dark, Is.GreaterThan(.001f),
                    "The unlit warehouse surface must retain a readability floor after the PS1 composite.");
                Assert.That(lit, Is.GreaterThan(dark + .025f),
                    "The real spotlight must visibly brighten the actual wall after post-processing.");
                Assert.That(dark, Is.LessThan(lit * .5f),
                    "The warehouse outside the flashlight must remain substantially dimmer than its beam.");

                yield return CaptureFlashlightDistanceVisibility(root, camera, wall.point);

                // Use the same imported cargo as the real warehouse. The offset camera
                // sees the wall around it while the centre of the flashlight cannot.
                Vector3 middle = Vector3.Lerp(spot.transform.position, receiver, .5f);
                blocker = WarehouseArenaAssetProvider.Create("Cargo", root.transform,
                    middle - Vector3.up * .625f + spot.transform.right * .22f, Quaternion.identity);
                blocker.name = "Test flashlight cargo blocker";
                // The measured cargo is 1.25 m high. Its centre meets the ray;
                // the small lateral offset avoids the real seam between cartons.
                Physics.SyncTransforms();
                Assert.That(Physics.Raycast(spot.transform.position + spot.transform.forward * .08f,
                    spot.transform.forward, out RaycastHit blockedRay, wall.distance, ~0,
                    QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(blockedRay.collider.transform.IsChildOf(blocker.transform), Is.True,
                    "The imported cargo must physically intercept the real beam.");
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test flashlight cargo shadow");
                float shadow = SampleFlashlightReceiver(camera, receiver);
                Assert.That(shadow, Is.LessThan(dark + (lit - dark) * .5f),
                    "An actual cargo shadow must remove most direct light from the wall behind it.");
                blocker.SetActive(false);
                Object.Destroy(blocker);
                blocker = null;

                Vector3 previousBeam = spot.transform.forward;
                root.Hero.ResetActor(root.Hero.transform.position, Vector3.right);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(Vector3.Angle(previousBeam, spot.transform.forward), Is.GreaterThan(65f),
                    "Turning the torso must turn the world-space beam, independently of the camera.");
                AssertFlashlightMount(flashlight, chest, bindings);
                yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight torso turn");

                root.Hero.SetBlock(true);
                root.Hero.Step(.5f);
                yield return null;
                AssertFlashlightMount(flashlight, chest, bindings);
                yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight crowbar block");
                root.Hero.SetBlock(false);
                root.Hero.Step(.6f);
                Assert.That(root.Hero.TryAttack(), Is.True);
                root.Hero.Step(.25f);
                yield return null;
                AssertFlashlightMount(flashlight, chest, bindings);
                yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight crowbar strike");
                root.Hero.Step(1f);

                Assert.That(root.Hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                bool capturedPistolAccess = false;
                for (int frame = 0; frame < 150 && (root.Hero.IsSwitchingWeapon ||
                    !root.Hero.HasWeaponInHand || root.Hero.WeaponId != CombatWeaponId.Pistol); frame++)
                {
                    root.Tick(1f / 30f);
                    yield return null;
                    AssertFlashlightMount(flashlight, chest, bindings);
                    if (!capturedPistolAccess && root.Hero.WeaponCarryActionIsDraw &&
                        root.Hero.WeaponCarryActionProgress > .48f)
                    {
                        capturedPistolAccess = true;
                        yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight left chest pistol draw");
                    }
                }
                Assert.That(root.Hero.WeaponId, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
                root.Hero.SetPistolAim(true, root.Hero.transform.position + Vector3.right * 20f + Vector3.up * 1.3f);
                for (int frame = 0; frame < 24; frame++) { root.Tick(1f / 30f); yield return null; }
                Assert.That(root.Hero.Firearm.IsAiming, Is.True);
                AssertFlashlightMount(flashlight, chest, bindings);
                yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight pistol aim");
                root.Hero.SetPistolAim(false);
                root.Hero.Step(1f);
                Assert.That(root.Hero.TrySelectWeapon(CombatWeaponId.Shotgun), Is.True);
                bool capturedShotgunDraw = false;
                for (int frame = 0; frame < 150 && (root.Hero.IsSwitchingWeapon ||
                    !root.Hero.HasWeaponInHand || root.Hero.WeaponId != CombatWeaponId.Shotgun); frame++)
                {
                    root.Tick(1f / 30f);
                    yield return null;
                    AssertFlashlightMount(flashlight, chest, bindings);
                    if (!capturedShotgunDraw && root.Hero.WeaponCarryActionIsDraw &&
                        root.Hero.WeaponCarryActionProgress > .48f)
                    {
                        capturedShotgunDraw = true;
                        yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight left chest shotgun draw");
                    }
                }
                Assert.That(root.Hero.WeaponId, Is.EqualTo(CombatWeaponId.Shotgun));
                Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
                Assert.That(capturedPistolAccess && capturedShotgunDraw, Is.True);

                root.Hero.State.ReceiveProjectileHit(root.Hero.State.Settings.MaxHealth);
                root.Hero.Step(CombatAssetProvider.DefeatHandoffSeconds + .05f);
                Assert.That(root.Hero.IsRagdollActive, Is.True);
                for (int frame = 0; frame < 72; frame++) yield return new WaitForFixedUpdate();
                AssertFlashlightMount(flashlight, chest, bindings);
                yield return CaptureFlashlightMount(camera, root, flashlight, "Test flashlight fallen hero");
                Assert.That(spot.isActiveAndEnabled, Is.True,
                    "The chest flashlight stays attached and on through the physical fall.");

                root.ResetRound();
                yield return null;
                Assert.That(root.Flashlight, Is.SameAs(flashlight));
                AssertWarehouseFlashlightAndPracticals(root, spot);
                AssertFlashlightMount(flashlight, chest, bindings);
                Assert.That(root.NewWarehouseLayout(1), Is.True);
                yield return null;
                Assert.That(root.Flashlight, Is.SameAs(flashlight));
                AssertWarehouseFlashlightAndPracticals(root, spot);
                root.CameraFollow.enabled = true;
                root.CameraFollow.Snap();
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test flashlight new warehouse layout");

                root.enabled = false;
                yield return null;
                Assert.That(spot.isActiveAndEnabled, Is.False,
                    "Disabling the combat owner must release its scene lighting.");
                Assert.That(Shader.GetGlobalVector("_WarehouseAmbientOrigin").w, Is.Zero);
                root.enabled = true;
                yield return null;
                Assert.That(spot.isActiveAndEnabled, Is.True);
                flashlight.enabled = false;
                yield return null;
                Assert.That(spot.isActiveAndEnabled, Is.False);
                Assert.That(Shader.GetGlobalVector("_WarehouseAmbientOrigin").w, Is.Zero);
                flashlight.enabled = true;
                yield return null;
                Assert.That(spot.isActiveAndEnabled, Is.True);

                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(flashlight == null && spot == null, Is.True,
                    "Leaving CombatTest must destroy the one flashlight and its real light.");
                Assert.That(Object.FindObjectsByType<CombatChestFlashlight>().Length, Is.Zero);
                Assert.That(Shader.GetGlobalVector("_WarehouseAmbientOrigin").w, Is.Zero,
                    "Leaving the warehouse must release its shared shader visibility override.");
            }
            finally
            {
                if (blocker != null) Object.Destroy(blocker);
                if (root != null) root.AutomaticSimulation = false;
                Time.captureDeltaTime = previousCaptureDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static void AssertFlashlightMount(CombatChestFlashlight flashlight, Transform chest,
            CombatFlashlightBindings bindings)
        {
            Assert.That(flashlight.ModelRoot.transform.parent, Is.SameAs(chest));
            Assert.That(Vector3.Distance(flashlight.ModelRoot.transform.localPosition, bindings.BoneLocalPosition),
                Is.LessThan(.00001f), "Every pose must retain the measured imported chest binding.");
            Assert.That(Quaternion.Angle(flashlight.ModelRoot.transform.localRotation, bindings.BoneLocalRotation),
                Is.LessThan(.01f));
            Assert.That(Vector3.Distance(flashlight.Lens.position, flashlight.SpotLight.transform.position),
                Is.LessThan(.05f), "The light source must originate at the actual flashlight lens.");
            Assert.That(Vector3.Angle(flashlight.Lens.forward, flashlight.SpotLight.transform.forward),
                Is.LessThan(1f));
        }

        private static void AssertWarehouseFlashlightAndPracticals(CombatTestRoot root, Light spot)
        {
            Assert.That(root.GetComponentsInChildren<CombatChestFlashlight>(true).Length, Is.EqualTo(1));
            int active = 0, practical = 0;
            foreach (Light light in Object.FindObjectsByType<Light>())
            {
                if (!light.isActiveAndEnabled || light.intensity <= 0f) continue;
                active++;
                if (light == spot || light == root.GetComponentInChildren<WarehouseArenaMechanisms>().FanLight) continue;
                Assert.That(light.gameObject.name, Is.EqualTo("Warehouse Work Lamp"),
                    "Other sources must belong to the plan's rare practical lamps.");
                practical++;
            }
            Assert.That(practical, Is.EqualTo(root.WarehousePlan.Lamps.Count));
            Assert.That(active, Is.EqualTo(2 + root.WarehousePlan.Lamps.Count));
        }

        private static IEnumerator CaptureFlashlightDistanceVisibility(CombatTestRoot root,
            Camera camera, Vector3 receiver)
        {
            Light spot = root.Flashlight.SpotLight;
            Vector3 heroPosition = root.Hero.transform.position;
            Vector3 heroFacing = root.Hero.transform.forward;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            bool previousMotorEnabled = root.Player.Motor.enabled;
            // Hold diagnostic poses still across coroutine/late-update phases;
            // the vertical-only case must not gain an unrelated gravity offset.
            root.Player.Motor.enabled = false;
            float nearRadius = Mathf.Max(WarehouseArenaLighting.NearDistance * .7f,
                Mathf.Abs(receiver.y - PlayerFactory.GroundedRootOffset) + .3f);
            float middleRadius = (WarehouseArenaLighting.NearDistance + WarehouseArenaLighting.FarDistance) * .5f;
            float farRadius = WarehouseArenaLighting.FarDistance + .15f;
            Vector3 near = FlashlightReceiverHeroPosition(receiver, nearRadius);
            Vector3 middle = FlashlightReceiverHeroPosition(receiver, middleRadius);
            Vector3 far = FlashlightReceiverHeroPosition(receiver, farRadius);
            try
            {
                root.Hero.ResetActor(near, Vector3.back);
                Physics.SyncTransforms();
                yield return null;
                AssertFlashlightHeroOrigin(root);
                spot.enabled = false;
                float nearDark = SampleFlashlightReceiver(camera, receiver, "Test flashlight nearby dark warehouse");
                Assert.That(nearDark, Is.GreaterThan(.001f));

                Vector4 originBeforeOrbit = Shader.GetGlobalVector("_WarehouseAmbientOrigin");
                Vector3 orbit = cameraPosition + camera.transform.right * .8f;
                camera.transform.SetPositionAndRotation(orbit, Quaternion.LookRotation(receiver - orbit));
                yield return null;
                spot.enabled = false;
                Assert.That(Shader.GetGlobalVector("_WarehouseAmbientOrigin"), Is.EqualTo(originBeforeOrbit),
                    "Orbiting the camera must not move the hero-centred visibility field.");
                float orbitDark = SampleFlashlightReceiver(camera, receiver, "Test flashlight ambient camera orbit");
                Assert.That(orbitDark, Is.EqualTo(nearDark).Within(.006f + nearDark * .25f),
                    "The same surface remains readable when only the camera changes position.");
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);

                root.Hero.ResetActor(middle, Vector3.back);
                Physics.SyncTransforms();
                yield return null;
                spot.enabled = false;
                AssertFlashlightHeroOrigin(root);
                float middleDark = SampleFlashlightReceiver(camera, receiver, "Test flashlight fading warehouse distance");
                Assert.That(middleDark, Is.LessThan(nearDark * .9f));
                Assert.That(middleDark, Is.GreaterThan(.0002f));

                root.Hero.ResetActor(far, Vector3.back);
                Physics.SyncTransforms();
                yield return null;
                spot.enabled = false;
                AssertFlashlightHeroOrigin(root);
                float farDark = SampleFlashlightReceiver(camera, receiver, "Test flashlight distant warehouse darkness");
                Assert.That(farDark, Is.LessThanOrEqualTo(1f / 255f),
                    "Beyond the hero's visibility radius unlit geometry must fade to black, allowing one display step.");
                spot.enabled = true;
                yield return null;
                float farLit = SampleFlashlightReceiver(camera, receiver, "Test flashlight distant real beam");
                Assert.That(farLit, Is.GreaterThan(farDark + .005f),
                    "The real spotlight must remain visible where ambient readability has already faded out.");

                // Keep XZ near the receiver and change only vertical separation:
                // this distinguishes a real 3D radius from a ground-plane circle.
                root.Hero.ResetActor(new Vector3(near.x, receiver.y + farRadius, near.z), Vector3.back);
                Physics.SyncTransforms();
                yield return null;
                spot.enabled = false;
                AssertFlashlightHeroOrigin(root);
                float verticallyFar = SampleFlashlightReceiver(camera, receiver);
                Assert.That(verticallyFar, Is.LessThanOrEqualTo(1f / 255f),
                    "The ambient visibility radius must include vertical distance rather than only XZ distance.");
                Debug.Log($"Test flashlight distance visibility: near={nearDark:F6}, middle={middleDark:F6}, " +
                    $"far={farDark:F6}, farBeam={farLit:F6}, verticalFar={verticallyFar:F6}, orbit={orbitDark:F6}");
            }
            finally
            {
                root.Hero.ResetActor(heroPosition, heroFacing);
                Physics.SyncTransforms();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                spot.enabled = true;
                root.Player.Motor.enabled = previousMotorEnabled;
            }
            yield return null;
        }

        private static Vector3 FlashlightReceiverHeroPosition(Vector3 receiver, float radius)
        {
            float vertical = receiver.y - PlayerFactory.GroundedRootOffset;
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, radius * radius - vertical * vertical));
            return new Vector3(receiver.x, PlayerFactory.GroundedRootOffset, receiver.z + horizontal);
        }

        private static void AssertFlashlightHeroOrigin(CombatTestRoot root)
        {
            Vector4 origin = Shader.GetGlobalVector("_WarehouseAmbientOrigin");
            Vector3 actual = root.Hero.transform.position;
            Assert.That(origin.w, Is.EqualTo(1f));
            Assert.That(Vector3.Distance(new Vector3(origin.x, origin.y, origin.z), actual), Is.LessThan(.001f),
                "The shader visibility origin must follow the actual hero transform, independently of camera position.");
        }

        private static void LogFlashlightAmbient(Camera camera, CombatTestRoot root,
            RaycastHit receiver, float lit, float dark)
        {
            Vector3[] normals = { Vector3.up, Vector3.right, Vector3.forward, receiver.normal };
            var evaluated = new Color[normals.Length];
            var ambient = RenderSettings.ambientProbe;
            ambient.Evaluate(normals, evaluated);
            LightProbes.GetInterpolatedProbe(receiver.point, null, out var interpolated);
            var interpolatedColors = new Color[normals.Length];
            interpolated.Evaluate(normals, interpolatedColors);
            Debug.Log($"Test flashlight ambient: mode={RenderSettings.ambientMode}, color={RenderSettings.ambientLight:F6}, " +
                $"intensity={RenderSettings.ambientIntensity:F6}, skybox={RenderSettings.skybox}, " +
                $"probeUp={evaluated[0]:F6}, probeRight={evaluated[1]:F6}, probeForward={evaluated[2]:F6}, " +
                $"probeReceiver={evaluated[3]:F6}, interpolatedReceiver={interpolatedColors[3]:F6}, " +
                $"SHAr={Shader.GetGlobalVector("unity_SHAr"):F6}, SHAg={Shader.GetGlobalVector("unity_SHAg"):F6}, " +
                $"SHAb={Shader.GetGlobalVector("unity_SHAb"):F6}, lit={lit:F6}, dark={dark:F6}, " +
                $"receiver={receiver.collider.name}, position={receiver.point:F4}, normal={receiver.normal:F4}");
            // Command-buffer/per-draw values can differ from these CPU-visible shader globals.
            Vector3 view = receiver.point - camera.transform.position;
            if (Physics.Raycast(camera.transform.position, view.normalized, out RaycastHit visible,
                view.magnitude + .2f, ~0, QueryTriggerInteraction.Ignore))
                Debug.Log($"Test flashlight receiver view: collider={visible.collider.name}, distance={visible.distance:F4}, " +
                    $"expectedDistance={view.magnitude:F4}, point={visible.point:F4}");
            MeshRenderer nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || renderer.sharedMaterial == null) continue;
                float distance = renderer.bounds.SqrDistance(receiver.point);
                if (distance >= nearestDistance) continue;
                nearest = renderer;
                nearestDistance = distance;
            }
            if (nearest != null)
                Debug.Log($"Test flashlight receiver surface: renderer={nearest.name}, shader={nearest.sharedMaterial.shader.name}, " +
                    $"baseColor={nearest.sharedMaterial.GetColor("_BaseColor"):F6}, probes={nearest.lightProbeUsage}, " +
                    $"receiveGI={nearest.receiveGI}, lightmap={nearest.lightmapIndex}, " +
                    $"keywords={string.Join(",", nearest.sharedMaterial.shaderKeywords)}");
        }

        private static IEnumerator CaptureFlashlightMount(Camera camera, CombatTestRoot root,
            CombatChestFlashlight flashlight, string name)
        {
            Vector3 chest = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Chest.position;
            Vector3 position = chest + root.Hero.transform.forward * 1.9f + root.Hero.transform.right * 1.25f + Vector3.up * .35f;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(chest - position));
            camera.fieldOfView = 48f;
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
        }

        private static float SampleFlashlightReceiver(Camera camera, Vector3 worldPoint, string shotName = null)
        {
            var target = new RenderTexture(Width, Height, 24);
            var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                frame.Apply();
                if (!string.IsNullOrEmpty(shotName))
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, shotName + ".png");
                    File.WriteAllBytes(path, frame.EncodeToPNG());
                    // A deliberately fully dark frame is valid for the far ambient proof.
                    Debug.Log("Area capture wrote " + path);
                }
                Vector3 viewport = camera.WorldToViewportPoint(worldPoint);
                Assert.That(viewport.z, Is.GreaterThan(0f));
                Assert.That(viewport.x, Is.InRange(.1f, .9f));
                Assert.That(viewport.y, Is.InRange(.1f, .9f));
                int x = Mathf.RoundToInt(viewport.x * (Width - 1));
                int y = Mathf.RoundToInt(viewport.y * (Height - 1));
                float total = 0f;
                for (int dy = -6; dy <= 6; dy++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        Color color = frame.GetPixel(x + dx, y + dy);
                        total += color.r * .2126f + color.g * .7152f + color.b * .0722f;
                    }
                return total / 169f;
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
