using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Photographs the hand props IN HANDS: the babushka's carpet beater
    /// and cigarette, the mourner's bouquet, the fisherman's rod and pipe,
    /// and the bouquet laid free-standing on a slab.
    ///
    /// This exists because the numbers cannot see a prop rendered a
    /// hundred times too big, floating a hand's width from the fingers,
    /// or pointing back up the forearm: every one of those keeps a
    /// correct socket, a correct triangle count and a correct manifest.
    /// The bodies are bare prefab instances with one authored clip
    /// sampled onto them (no director, no presentation), so a frame here
    /// shows exactly what <see cref="CityPedestrianHandProps.Attach"/>
    /// does and nothing else.
    ///
    /// `[Explicit]`: a capture, not a regression. Frames land in
    /// `Captures/HandProps/` (gitignored). Look at them.
    /// </summary>
    public sealed class CityPedestrianHandPropCapturePlayModeTests
    {
        private const int Width = 960;
        private const int Height = 720;

        /// <summary>Well above any loaded world; the camera's 30 m far
        /// plane never reaches back down to it.</summary>
        private static readonly Vector3 StageOrigin = new Vector3(0f, 4000f, 0f);

        [UnityTest]
        [Explicit("Capture, not a test. Look at Captures/HandProps/.")]
        public IEnumerator HandProps_SitInTheHandsOnCamera()
        {
            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                "HandProps");
            Directory.CreateDirectory(folder);

            // The stage stands far above anything the previous test in a
            // sweep may have left loaded (the whole City, say), so the
            // frames hold the props and nothing else. Unloading the other
            // scenes instead is not an option: when this test runs first
            // the only other scene is the test runner's own, and pulling
            // it away hangs the run.
            var stage = new GameObject("Hand Prop Capture Stage");
            stage.transform.position = StageOrigin;
            var lightObject = new GameObject("Hand Prop Capture Light");
            var cameraObject = new GameObject("Hand Prop Capture Camera");
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            bool previousFog = RenderSettings.fog;
            AmbientMode previousMode = RenderSettings.ambientMode;
            Color previousAmbient = RenderSettings.ambientLight;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.15f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 30f;
                camera.targetTexture = target;

                // A floor, so a prop that fell to the ground reads as such.
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";
                floor.transform.SetParent(stage.transform, false);
                floor.transform.localScale = new Vector3(3f, 1f, 3f);

                // Babushka with the beater (right grip) and cigarette
                // (right cigarette socket), the roles the drying yard and
                // the courtyard give her.
                CityPedestrianAssetRegistry babushka = InstantiateBody(
                    Resources.Load<GameObject>("Pedestrians/YardBabushka3D"),
                    stage.transform,
                    new Vector3(0f, 0f, 0f));
                CityPedestrianHandPropRegistry beater =
                    CityPedestrianHandProps.Attach(
                        babushka, CityPedestrianHandPropId.CarpetBeater);
                CityPedestrianHandPropRegistry cigarette =
                    CityPedestrianHandProps.Attach(
                        babushka, CityPedestrianHandPropId.Cigarette);

                // Mourner with her bouquet, standing as the street clips
                // leave her (hanging arms).
                CityPedestrianAssetRegistry mourner = InstantiateBody(
                    Resources.Load<GameObject>("Pedestrians/CemeteryMourner3D"),
                    stage.transform,
                    new Vector3(1.6f, 0f, 0f));
                CityPedestrianHandPropRegistry bouquet =
                    CityPedestrianHandProps.Attach(
                        mourner, CityPedestrianHandPropId.FuneralBouquet);

                // Fisherman with rod and pipe.
                SeacoastFishermanProvider provider = SeacoastFishermanProvider.Load();
                Assert.That(provider, Is.Not.Null);
                CityPedestrianAssetRegistry fisherman = InstantiateBody(
                    provider.StagedPrefab,
                    stage.transform,
                    new Vector3(-1.6f, 0f, 0f));
                CityPedestrianHandPropRegistry rod =
                    CityPedestrianHandProps.Attach(
                        fisherman, CityPedestrianHandPropId.FishingRod);
                CityPedestrianHandPropRegistry pipe =
                    CityPedestrianHandProps.Attach(
                        fisherman, CityPedestrianHandPropId.SmokingPipe);

                // The cafe pair: the woman's cigarette on her cigarette
                // socket, the attendant's towel (left grip) and coffee pot
                // (right grip) — attached through the socket overload the
                // cafe factory uses, in the bind pose the Mount was
                // measured in.
                MountainRoadCafeCastProvider cafe = MountainRoadCafeCastProvider.Load();
                Assert.That(cafe, Is.Not.Null, "The cafe cast provider is missing.");
                MountainRoadCafeCastAssetRegistry cafeWoman = InstantiateCafeBody(
                    cafe.PairWomanPrefab,
                    stage.transform,
                    new Vector3(-3.4f, 0f, 0f));
                CityPedestrianHandPropRegistry cafeCigarette = AttachCafeProp(
                    cafeWoman, CityPedestrianHandPropId.CafeCigarette);
                MountainRoadCafeCastAssetRegistry attendant = InstantiateCafeBody(
                    cafe.AttendantPrefab,
                    stage.transform,
                    new Vector3(-5.2f, 0f, 0f));
                CityPedestrianHandPropRegistry towel = AttachCafeProp(
                    attendant, CityPedestrianHandPropId.ServiceTowel);
                CityPedestrianHandPropRegistry pot = AttachCafeProp(
                    attendant, CityPedestrianHandPropId.CoffeePot);

                // The same bouquet laid on a slab, as the grave receives it.
                GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Slab";
                slab.transform.SetParent(stage.transform, false);
                slab.transform.localPosition = new Vector3(3.2f, 0.06f, 0f);
                slab.transform.localScale = new Vector3(0.9f, 0.12f, 2.0f);
                Material bodyMaterial = mourner.Renderers[0].sharedMaterial;
                CityPedestrianHandPropRegistry laid = CemeteryLaidBouquet.Place(
                    stage.transform,
                    StageOrigin + new Vector3(3.2f, 0.12f, -0.2f),
                    Quaternion.identity,
                    bodyMaterial,
                    mourner.PaletteVariant);

                yield return null;

                // Pose every body with its own idle so the hands are where
                // the game holds them, not in the bind A-pose.
                SamplePose(babushka, 0.4f);
                SamplePose(mourner, 0.4f);
                SamplePose(fisherman, 0.4f);
                yield return null;

                // The first render of a session has no shadow maps yet.
                Shoot(camera, pixels, target, null,
                    StageOrigin + new Vector3(0f, 1.6f, -3f),
                    StageOrigin + new Vector3(0f, 1f, 0f), 50f);

                Transform babushkaHand = beater.transform.parent;
                Shoot(camera, pixels, target, Path.Combine(folder, "00-line-up.png"),
                    StageOrigin + new Vector3(0.6f, 1.5f, -4.6f),
                    StageOrigin + new Vector3(0.6f, 0.95f, 0f), 60f);
                Shoot(camera, pixels, target,
                    Path.Combine(folder, "01-babushka-beater-and-cigarette.png"),
                    babushkaHand.position + new Vector3(-0.9f, 0.35f, -0.9f),
                    babushkaHand.position, 40f);
                Transform mournerHand = bouquet.transform.parent;
                Shoot(camera, pixels, target, Path.Combine(folder, "02-mourner-bouquet.png"),
                    mournerHand.position + new Vector3(-0.8f, 0.4f, -1.0f),
                    mournerHand.position, 40f);
                Transform fishermanHand = rod.transform.parent;
                Shoot(camera, pixels, target,
                    Path.Combine(folder, "03-fisherman-rod-and-pipe.png"),
                    fishermanHand.position + new Vector3(1.2f, 0.9f, -2.2f),
                    fishermanHand.position + new Vector3(0f, 0.3f, 0.4f), 55f);
                Transform fishermanMouth = pipe.transform.parent;
                Shoot(camera, pixels, target,
                    Path.Combine(folder, "05-fisherman-pipe-close.png"),
                    fishermanMouth.position + new Vector3(-0.45f, 0.1f, -0.55f),
                    fishermanMouth.position, 30f);
                Shoot(camera, pixels, target, Path.Combine(folder, "04-laid-bouquet-on-slab.png"),
                    StageOrigin + new Vector3(3.2f, 1.1f, -1.6f),
                    StageOrigin + new Vector3(3.2f, 0.12f, -0.2f), 45f);

                Transform womanSocket = cafeCigarette.transform.parent;
                Shoot(camera, pixels, target,
                    Path.Combine(folder, "06-cafe-woman-cigarette.png"),
                    womanSocket.position + new Vector3(-0.5f, 0.25f, -0.6f),
                    womanSocket.position, 32f);
                Transform attendantGrip = pot.transform.parent;
                Shoot(camera, pixels, target,
                    Path.Combine(folder, "07-cafe-attendant-pot-and-towel.png"),
                    attendantGrip.position + new Vector3(0.7f, 0.45f, -1.6f),
                    attendantGrip.position + new Vector3(0.7f, 0f, 0f), 45f);

                Assert.That(
                    beater.IsVisible && cigarette.IsVisible && bouquet.IsVisible &&
                    rod.IsVisible && pipe.IsVisible && laid.IsVisible &&
                    cafeCigarette.IsVisible && towel.IsVisible && pot.IsVisible,
                    Is.True);
                Debug.Log($"Hand prop captures wrote {folder}");
            }
            finally
            {
                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.Destroy(stage);
                Object.Destroy(lightObject);
                Object.Destroy(cameraObject);
                Object.Destroy(pixels);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static CityPedestrianAssetRegistry InstantiateBody(
            GameObject prefab,
            Transform parent,
            Vector3 position)
        {
            Assert.That(prefab, Is.Not.Null, "A body prefab is missing.");
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.transform.localPosition = position;
            // Face the camera, which stands on -Z.
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var registry = instance.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            return registry;
        }

        private static MountainRoadCafeCastAssetRegistry InstantiateCafeBody(
            GameObject prefab,
            Transform parent,
            Vector3 position)
        {
            Assert.That(prefab, Is.Not.Null, "A cafe body prefab is missing.");
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var registry = instance.GetComponent<MountainRoadCafeCastAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            return registry;
        }

        private static CityPedestrianHandPropRegistry AttachCafeProp(
            MountainRoadCafeCastAssetRegistry body,
            CityPedestrianHandPropId id)
        {
            Transform socket = CityPedestrianHandProps.FindSocket(body.ModelRoot, id);
            Assert.That(socket, Is.Not.Null, $"{body.name} has no socket for {id}.");
            Renderer any = body.GetComponentInChildren<Renderer>(true);
            Assert.That(any, Is.Not.Null);
            return CityPedestrianHandProps.Attach(socket, id, any.sharedMaterial, 0);
        }

        private static void SamplePose(CityPedestrianAssetRegistry body, float time)
        {
            AnimationClip clip = body.IdleClip != null
                ? body.IdleClip
                : body.AmbientIdleClip;
            if (clip == null || body.Animator == null)
            {
                return;
            }

            clip.SampleAnimation(body.Animator.gameObject, time);
        }

        private static void Shoot(
            Camera camera,
            Texture2D pixels,
            RenderTexture target,
            string path,
            Vector3 position,
            Vector3 lookAt,
            float fieldOfView)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(
                lookAt - position,
                Vector3.up);
            camera.fieldOfView = fieldOfView;
            camera.Render();
            if (path == null)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                pixels.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }

            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
    }
}
