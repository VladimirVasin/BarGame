using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// A deliberately isolated visual proof for the Residential balcony
    /// contract. It uses the packaged Blender prototype and the production
    /// balcony-smoker factory, then photographs one selected authored slot.
    /// The two 1280x720 frames should be viewed at half size to judge the
    /// intended 640x360 presentation.
    /// </summary>
    public sealed class CityResidentialBalconyVisualCapturePlayModeTests
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const float PositionTolerance = 0.004f;
        private const string CaptureFolder = "ResidentialBalconies";

        private GameObject root;
        private GameObject cameraObject;
        private GameObject lightObject;
        private CityBalconySmokerRuntime smokerRuntime;
        private RenderTexture renderTarget;
        private Texture2D frameBuffer;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private Light previousSun;
        private bool previousFog;
        private bool renderSettingsCaptured;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (smokerRuntime != null)
            {
                smokerRuntime.Shutdown();
                smokerRuntime = null;
            }

            if (renderSettingsCaptured)
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.sun = previousSun;
                RenderSettings.fog = previousFog;
                renderSettingsCaptured = false;
            }

            if (cameraObject != null)
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                if (camera != null)
                {
                    camera.targetTexture = null;
                }

                Object.Destroy(cameraObject);
            }

            if (renderTarget != null)
            {
                renderTarget.Release();
                Object.Destroy(renderTarget);
            }

            if (frameBuffer != null)
            {
                Object.Destroy(frameBuffer);
            }

            if (root != null)
            {
                Object.Destroy(root);
            }

            if (lightObject != null)
            {
                Object.Destroy(lightObject);
            }

            yield return null;
        }

        [UnityTest]
        [Explicit(
            "Capture, not a suite test. Look at " +
            "Captures/ResidentialBalconies/.")]
        public IEnumerator
            ResidentialPrototype_ShowsDoorDeckRailsAndSmokingResident()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityBalconySmokerPlan plan =
                CityBalconySmokerPlan.Create(layout);
            Assert.That(
                plan.IsPresent,
                Is.True,
                "The default City needs a balcony smoker to photograph.");

            CityBalconySmokerDescriptor descriptor = plan.Smokers[0];
            BuildingLot lot = layout.BuildingLots.Single(candidate =>
                candidate.Cell == descriptor.LotCell);
            Assert.That(lot.District, Is.EqualTo(
                CityDistrictKind.Residential));
            Assert.That(lot.IsOrdinaryBuilding, Is.True);

            root = new GameObject("Residential Balcony Capture Root");
            Transform building = new GameObject(
                "Selected Residential Building").transform;
            building.SetParent(root.transform, false);
            CityBuildingAssetRegistry buildingRegistry =
                CityBuildingPrototypeWorldBuilder.BuildCity(
                    building,
                    lot,
                    layout.Seed,
                    CityWorldBuilder.ResolveBuildingFoundationDepth(
                        layout,
                        lot));

            smokerRuntime = CityBalconySmokerFactory.Create(
                root.transform,
                plan);
            Assert.That(smokerRuntime.IsPresent, Is.True);
            Assert.That(smokerRuntime.IsVisible, Is.True);
            Assert.That(smokerRuntime.Count, Is.EqualTo(plan.Count));
            CityBalconySmokerPresentation presentation =
                smokerRuntime.Presentations.Single(candidate =>
                    string.Equals(
                        candidate.Descriptor.StableId,
                        descriptor.StableId,
                        StringComparison.Ordinal));
            Assert.That(presentation.IsInitialized, Is.True);
            Assert.That(
                presentation.Registry.DesignId,
                Is.EqualTo(descriptor.ArchetypeDesignId));
            Assert.That(
                CityBalconySmokerArchetypeCatalog.IsEligible(
                    descriptor.ArchetypeDesignId),
                Is.True,
                "Only a compatible current street-roaming archetype may " +
                "be reused on a balcony.");

            CityBuildingBalconySlot balcony =
                buildingRegistry.BalconySlots.Single(candidate =>
                    string.Equals(
                        candidate.StableId,
                        descriptor.BalconySlotStableId,
                        StringComparison.Ordinal));
            CityBuildingWindowSlot door =
                buildingRegistry.WindowSlots.Single(candidate =>
                    candidate.SlotId == balcony.DoorSlotId);

            AssertBalconyDoorContract(balcony, door);
            AssertBalconyPose(
                buildingRegistry,
                balcony,
                descriptor,
                presentation);
            Assert.That(
                buildingRegistry.Parts.Single(binding =>
                    binding.Role == CityBuildingMeshRole.Metal)
                    .Renderer.enabled,
                Is.True,
                "The Blender-authored rail/threshold/handle surface must " +
                "be visible in the capture.");
            AssertHeroSmokeLoop(presentation);
            AssertSmokingProps(presentation);
            AssertEveryEligibleArchetypeReachesInhaleContact(root.transform);

            int burstCount = presentation.SmokeBurstCount;
            for (int step = 0;
                 step < 300 &&
                 presentation.SmokeBurstCount == burstCount;
                 step++)
            {
                presentation.Advance(
                    CityBalconySmokerPresentation.MaximumStepSeconds);
            }

            Assert.That(
                presentation.SmokeBurstCount,
                Is.GreaterThan(burstCount),
                "The literal SmokeLoop must reach the authored exhale " +
                "event before capture.");
            Assert.That(
                presentation.ExhaleEffect.Particles.particleCount,
                Is.GreaterThan(0));
            AssertBalconyExhaleParticleMotion(
                presentation.ExhaleEffect,
                descriptor.Facing);

            AdvanceToReadableInhale(presentation);
            Assert.That(
                AssertPoseMatchesLiteralDriver(
                    presentation,
                    descriptor.ArchetypeDesignId),
                Is.LessThan(0.20f),
                "The reused SmokeLoop must bring this resident's cigarette " +
                "within the same readable inhale reach as Hero V2.");
            Assert.That(
                presentation.ExhaleEffect.EmitManualBurst(),
                Is.True,
                "The capture adds one plume to the readable inhale pose; " +
                "automatic exhale timing was asserted above.");
            presentation.ExhaleEffect.Particles.Simulate(
                0.32f,
                withChildren: true,
                restart: false,
                fixedTimeStep: false);

            ConfigureRenderState(descriptor.Facing);
            Camera camera = CreateCamera();
            CreateBuffers(camera);
            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                CaptureFolder);
            Directory.CreateDirectory(folder);

            // Give the freshly emitted plume one rendered frame while
            // retaining the readable exhale pose selected above.
            yield return null;

            Vector3 outward = descriptor.Facing.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 focus = descriptor.Position + Vector3.up * 0.88f;
            Capture(
                camera,
                folder,
                "residential-balcony-context.png",
                focus + outward * 7.4f + right * 3.2f +
                Vector3.up * 1.65f,
                focus,
                50f);
            Capture(
                camera,
                folder,
                "residential-balcony-detail.png",
                focus + outward * 4.2f + right * 1.65f +
                Vector3.up * 0.85f,
                focus,
                46f);
        }

        private void ConfigureRenderState(Vector3 balconyOutward)
        {
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            renderSettingsCaptured = true;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.46f, 0.50f);
            RenderSettings.fog = false;

            lightObject = new GameObject(
                "Residential Balcony Capture Key Light");
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.45f;
            key.color = new Color(1f, 0.88f, 0.76f);
            key.shadows = LightShadows.Hard;
            Vector3 lightDirection =
                (-balconyOutward.normalized + Vector3.down * 0.72f)
                .normalized;
            key.transform.rotation = Quaternion.LookRotation(
                lightDirection,
                Vector3.up);
            RenderSettings.sun = key;
        }

        private Camera CreateCamera()
        {
            cameraObject = new GameObject(
                "Residential Balcony Capture Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 64f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.20f, 0.24f);
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;
            return camera;
        }

        private void CreateBuffers(Camera camera)
        {
            renderTarget = new RenderTexture(
                Width,
                Height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = "Residential Balcony Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;
            frameBuffer = new Texture2D(
                Width,
                Height,
                TextureFormat.RGB24,
                false);
        }

        private void Capture(
            Camera camera,
            string folder,
            string fileName,
            Vector3 position,
            Vector3 target,
            float fieldOfView)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(
                target - position,
                Vector3.up);
            camera.fieldOfView = fieldOfView;
            camera.Render();

            RenderTexture previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTarget;
                frameBuffer.ReadPixels(
                    new Rect(0f, 0f, Width, Height),
                    0,
                    0);
                frameBuffer.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
            }

            Assert.That(
                IsBlank(frameBuffer),
                Is.False,
                $"'{fileName}' came out as one flat colour.");
            Assert.That(
                CountPixelsDifferentFromCorner(frameBuffer),
                Is.GreaterThan(Width * Height / 100),
                $"'{fileName}' contains too little visible geometry.");

            byte[] png = frameBuffer.EncodeToPNG();
            Assert.That(png.Length, Is.GreaterThan(1024));
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, png);
            Debug.Log($"Residential balcony capture wrote {path}");
        }

        private static void AssertBalconyDoorContract(
            CityBuildingBalconySlot balcony,
            CityBuildingWindowSlot door)
        {
            Bounds deck = balcony.LocalDeckBounds;
            Assert.That(
                door.OpeningKind,
                Is.EqualTo(CityBuildingOpeningKind.BalconyDoor));
            Assert.That(door.Floor, Is.EqualTo(balcony.Floor));
            Assert.That(door.Side, Is.EqualTo(balcony.Side));
            Assert.That(
                door.SizeMeters.y,
                Is.GreaterThan(1.9f),
                "A balcony entrance must be a person-height door, not a " +
                "window behind the slab.");
            Assert.That(
                door.LocalCenter.y - door.SizeMeters.y * 0.5f,
                Is.EqualTo(deck.max.y).Within(PositionTolerance));
            Assert.That(
                door.LocalCenter.x,
                Is.InRange(deck.min.x, deck.max.x));
            Assert.That(
                door.LocalCenter.z,
                Is.InRange(deck.min.z, deck.max.z));

            Vector2 doorHorizontal = new Vector2(
                door.LocalCenter.x,
                door.LocalCenter.z);
            Vector2 dockHorizontal = new Vector2(
                balcony.LocalNpcDock.x,
                balcony.LocalNpcDock.z);
            Assert.That(
                Vector2.Distance(doorHorizontal, dockHorizontal),
                Is.LessThan(1.5f),
                "The smoker dock must remain beside its apartment door.");
        }

        private static void AssertBalconyPose(
            CityBuildingAssetRegistry registry,
            CityBuildingBalconySlot balcony,
            CityBalconySmokerDescriptor descriptor,
            CityBalconySmokerPresentation presentation)
        {
            Vector3 expectedDock = registry.transform.TransformPoint(
                balcony.LocalNpcDock);
            Assert.That(
                Vector3.Distance(descriptor.Position, expectedDock),
                Is.LessThan(PositionTolerance));
            Assert.That(
                Vector3.Distance(
                    presentation.transform.position,
                    expectedDock),
                Is.LessThan(PositionTolerance));

            Vector3 localPresentation =
                registry.transform.InverseTransformPoint(
                    presentation.transform.position);
            Bounds deck = balcony.LocalDeckBounds;
            Assert.That(
                localPresentation.x,
                Is.InRange(deck.min.x, deck.max.x));
            Assert.That(
                localPresentation.z,
                Is.InRange(deck.min.z, deck.max.z));
            Assert.That(
                localPresentation.y,
                Is.EqualTo(deck.max.y).Within(PositionTolerance));

            Vector3 expectedFacing = registry.transform.TransformDirection(
                balcony.LocalOutward).normalized;
            Assert.That(
                Vector3.Dot(descriptor.Facing, expectedFacing),
                Is.GreaterThan(0.999f));
            Assert.That(
                Vector3.Dot(presentation.transform.forward, expectedFacing),
                Is.GreaterThan(0.999f));
        }

        private static void AssertHeroSmokeLoop(
            CityBalconySmokerPresentation presentation)
        {
            GameObject heroPrefab = Player3DResources.LoadPrefab();
            Assert.That(heroPrefab, Is.Not.Null);
            Player3DAssetRegistry hero =
                heroPrefab.GetComponent<Player3DAssetRegistry>();
            Assert.That(hero, Is.Not.Null);
            Assert.That(
                hero.TryGetAnimation(
                    CityBalconySmokerPresentation.SmokeLoopClipName,
                    out Player3DAnimationBinding heroSmoke),
                Is.True);
            Assert.That(heroSmoke, Is.Not.Null);
            Assert.That(
                presentation.ActiveClip,
                Is.SameAs(heroSmoke.Clip),
                "The resident must sample the literal production Hero V2 " +
                "SmokeLoop asset.");
            Assert.That(
                presentation.Registry.Animator.avatar,
                Is.SameAs(hero.Animator.avatar));
            Assert.That(
                presentation.AnimationDefinition.LoopClipName,
                Is.EqualTo(
                    CityBalconySmokerPresentation.SmokeLoopClipName));
            Assert.That(
                presentation.Registry.Animator.runtimeAnimatorController,
                Is.Null,
                "The passive smoker should be driven by its manual " +
                "SmokeLoop playable, not a replacement controller.");
        }

        private static void AssertSmokingProps(
            CityBalconySmokerPresentation presentation)
        {
            // The cigarette is a hand prop on SOCKET_Cigarette.R since
            // 2026-09-05: rigid MeshRenderers under the socket, never a
            // skin borrowed from the babushka.
            CityPedestrianHandPropRegistry held = presentation.HeldCigarette;
            Assert.That(held, Is.Not.Null, "No cigarette hand prop attached.");
            Assert.That(
                held.Id,
                Is.EqualTo(CityPedestrianHandPropId.Cigarette));
            Assert.That(held.transform.parent, Is.Not.Null);
            Assert.That(
                held.transform.parent.name,
                Is.EqualTo(CityBalconySmokerPresentation.CigaretteSocketName));
            Assert.That(
                held.transform.parent.IsChildOf(
                    presentation.Registry.ModelRoot),
                Is.True);
            Assert.That(presentation.CigaretteRenderers.Count, Is.EqualTo(2));
            Renderer cigarette = presentation.CigaretteRenderers.Single(
                renderer => string.Equals(
                    renderer.name,
                    "ACC_Cigarette",
                    StringComparison.Ordinal));
            Renderer ember = presentation.CigaretteRenderers.Single(
                renderer => string.Equals(
                    renderer.name,
                    "ACC_CigaretteEmber",
                    StringComparison.Ordinal));
            Assert.That(cigarette, Is.InstanceOf<MeshRenderer>());
            Assert.That(ember, Is.InstanceOf<MeshRenderer>());
            Assert.That(cigarette.enabled, Is.True);
            Assert.That(ember.enabled, Is.True);
        }

        /// <summary>The world centre of a rigid part's mesh through its
        /// own transform — exact for one mesh and independent of whether
        /// the renderer has been through a frame.</summary>
        private static Vector3 MeasureMeshCentre(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Assert.That(mesh, Is.Not.Null, $"{renderer.name} has no mesh.");
            return renderer.localToWorldMatrix.MultiplyPoint3x4(
                mesh.bounds.center);
        }

        private static void AssertBalconyExhaleParticleMotion(
            HomeBalconySmokingExhaleEffect exhale,
            Vector3 expectedOutward)
        {
            Assert.That(exhale, Is.Not.Null);
            Assert.That(exhale.MouthAnchor, Is.Not.Null);
            Vector3 mouthOutward = exhale.MouthAnchor.up.normalized;
            Vector3 expectedEmitterPosition =
                exhale.MouthAnchor.position +
                mouthOutward *
                HomeBalconySmokingExhaleEffect.MouthForwardOffset;
            Assert.That(
                Vector3.Distance(
                    exhale.Particles.transform.position,
                    expectedEmitterPosition),
                Is.LessThan(0.035f),
                "Balcony smoke must originate at the animated mouth, not " +
                "at the cigarette or actor root.");
            Assert.That(
                Vector3.Dot(
                    exhale.Particles.transform.forward,
                    mouthOutward),
                Is.GreaterThan(0.99f));

            var particles = new ParticleSystem.Particle[
                HomeBalconySmokingExhaleEffect.MaximumParticles];
            int count = exhale.Particles.GetParticles(particles);
            Assert.That(count, Is.GreaterThan(0));
            Vector3 averageVelocity = Vector3.zero;
            for (int index = 0; index < count; index++)
            {
                averageVelocity += particles[index].velocity;
            }

            averageVelocity /= count;
            Assert.That(
                Vector3.Dot(
                    averageVelocity,
                    expectedOutward.normalized),
                Is.GreaterThan(0.10f),
                "The exhaled balcony smoke must travel away from the " +
                "apartment facade.");
        }

        private static void AssertEveryEligibleArchetypeReachesInhaleContact(
            Transform parent)
        {
            GameObject heroPrefab = Player3DResources.LoadPrefab();
            Player3DAssetRegistry hero = heroPrefab != null
                ? heroPrefab.GetComponent<Player3DAssetRegistry>()
                : null;
            Assert.That(hero, Is.Not.Null);
            Assert.That(
                hero.TryGetAnimation(
                    CityBalconySmokerPresentation.SmokeLoopClipName,
                    out Player3DAnimationBinding smokeBinding),
                Is.True);
            PlayerAnimatedInteractionDefinition definition =
                CityBalconySmokerPresentation.CreateAnimationDefinition();
            var compatibilityRoot = new GameObject(
                "Balcony Smoker Archetype Compatibility");
            compatibilityRoot.transform.SetParent(parent, false);
            compatibilityRoot.transform.position =
                new Vector3(1000f, 0f, 1000f);

            var observed = CityBalconySmokerArchetypeCatalog
                .EligibleDesignIds;
            Assert.That(observed.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                observed.Count,
                Is.EqualTo(CityPedestrianResources.Archetypes.Count),
                "Every current roaming archetype should remain compatible " +
                "with the shared smoking rig.");
            for (int index = 0; index < observed.Count; index++)
            {
                string designId = observed[index];
                Assert.That(
                    CityPedestrianResources.TryGetArchetype(
                        designId,
                        out CityPedestrianArchetype archetype),
                    Is.True,
                    designId);
                Assert.That(
                    CityPedestrianResources.TryInstantiate(
                        CityPedestrianResources.LoadPrefab(archetype),
                        compatibilityRoot.transform,
                        out CityPedestrianAssetRegistry registry),
                    Is.True,
                    designId);
                var descriptor = new CityBalconySmokerDescriptor(
                    $"capture-compatibility/{designId}",
                    new Vector2Int(index, 0),
                    "capture-balcony",
                    designId,
                    new Vector3(index * 3f, 0f, 0f),
                    Vector3.forward,
                    new Vector3(index * 3f, 0f, 0f),
                    Vector3.forward,
                    index,
                    0.39f);
                var candidate = registry.gameObject.AddComponent<
                    CityBalconySmokerPresentation>();
                candidate.Initialize(
                    registry,
                    descriptor,
                    smokeBinding.Clip,
                    hero.Animator.avatar,
                    definition,
                    poseIsParentLocal: true);
                AdvanceToReadableInhale(candidate);

                Assert.That(
                    AssertPoseMatchesLiteralDriver(candidate, designId),
                    Is.LessThan(0.20f),
                    $"{designId} must retain Hero V2's readable inhale " +
                    "reach.");
                AssertSmokingProps(candidate);
                candidate.Shutdown();
                registry.gameObject.SetActive(false);
            }
        }

        private static float AssertPoseMatchesLiteralDriver(
            CityBalconySmokerPresentation presentation,
            string designId)
        {
            Transform mouth = FindDescendant(
                presentation.Registry.ModelRoot,
                CityBalconySmokerPresentation.MouthSocketName);
            Transform cigarette = FindDescendant(
                presentation.Registry.ModelRoot,
                CityBalconySmokerPresentation.CigaretteSocketName);
            Player3DAssetRegistry driver = presentation
                .GetComponentInChildren<Player3DAssetRegistry>(true);
            Assert.That(mouth, Is.Not.Null, designId);
            Assert.That(cigarette, Is.Not.Null, designId);
            Assert.That(driver, Is.Not.Null, designId);
            Transform driverMouth = FindDescendant(
                driver.ModelRoot,
                CityBalconySmokerPresentation.MouthSocketName);
            Transform driverCigarette = FindDescendant(
                driver.ModelRoot,
                CityBalconySmokerPresentation.CigaretteSocketName);
            Assert.That(driverMouth, Is.Not.Null, designId);
            Assert.That(driverCigarette, Is.Not.Null, designId);

            float residentDistance = Vector3.Distance(
                mouth.position,
                cigarette.position);
            float driverDistance = Vector3.Distance(
                driverMouth.position,
                driverCigarette.position);
            Assert.That(
                residentDistance,
                Is.EqualTo(driverDistance).Within(0.001f),
                $"{designId} must receive the literal Hero V2 bone pose.");

            // The attached prop rides the socket the pose transfer
            // moved: its ember must be within a cigarette's length of
            // the socket and no farther from the mouth than that. A prop
            // left at the 100x scale or on a stale Mount fails here, not
            // in a capture someone has to look at.
            Assert.That(presentation.HeldCigarette, Is.Not.Null, designId);
            Renderer emberRenderer = presentation.HeldCigarette.FindRenderer(
                "ACC_CigaretteEmber");
            Assert.That(emberRenderer, Is.Not.Null, designId);
            Vector3 emberCentre = MeasureMeshCentre(emberRenderer);
            float emberToSocket = Vector3.Distance(
                emberCentre,
                cigarette.position);
            Assert.That(
                emberToSocket,
                Is.InRange(0.02f, 0.15f),
                $"{designId}: the ember sits {emberToSocket:F3} m from " +
                "SOCKET_Cigarette.R.");
            float emberToMouth = Vector3.Distance(
                emberCentre,
                mouth.position);
            Assert.That(
                emberToMouth,
                Is.LessThanOrEqualTo(residentDistance + 0.15f),
                $"{designId}: the ember is {emberToMouth:F3} m from " +
                "SOCKET_Mouth at the held inhale.");
            return residentDistance;
        }

        private static void AdvanceToReadableInhale(
            CityBalconySmokerPresentation presentation)
        {
            const float readableInhaleProgress = 0.495f;
            for (int step = 0;
                 step < 300 &&
                 (presentation.CurrentLoopFrame !=
                      HomeBalconySmokingPlan.InhaleHoldLoopFrame ||
                  presentation.CurrentClipProgress01 <
                      readableInhaleProgress);
                 step++)
            {
                presentation.Advance(0.05f);
            }

            Assert.That(
                presentation.CurrentLoopFrame,
                Is.EqualTo(HomeBalconySmokingPlan.InhaleHoldLoopFrame),
                "The capture could not reach the held inhale pose.");
            Assert.That(
                presentation.CurrentClipProgress01,
                Is.GreaterThanOrEqualTo(readableInhaleProgress));
        }

        private static Transform FindDescendant(
            Transform rootTransform,
            string name)
        {
            if (rootTransform == null)
            {
                return null;
            }

            if (string.Equals(
                    rootTransform.name,
                    name,
                    StringComparison.Ordinal))
            {
                return rootTransform;
            }

            for (int index = 0;
                 index < rootTransform.childCount;
                 index++)
            {
                Transform found = FindDescendant(
                    rootTransform.GetChild(index),
                    name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool IsBlank(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            if (pixels.Length == 0)
            {
                return true;
            }

            Color32 first = pixels[0];
            for (int index = 1; index < pixels.Length; index++)
            {
                if (!SameRgb(first, pixels[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountPixelsDifferentFromCorner(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            if (pixels.Length == 0)
            {
                return 0;
            }

            Color32 corner = pixels[0];
            int count = 0;
            for (int index = 1; index < pixels.Length; index++)
            {
                if (!SameRgb(corner, pixels[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool SameRgb(Color32 left, Color32 right)
        {
            return left.r == right.r &&
                   left.g == right.g &&
                   left.b == right.b;
        }
    }
}
