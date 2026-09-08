using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MothersHouseLivingRoomAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType(
                "BarPromenade.Editor.MothersHouseInteriorAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    /// <summary>The real sofa interaction, lamp passage and living-room presentation.</summary>
    public sealed class MothersHouseSofaSitPlayModeTests
    {
        private const string InteriorRootName =
            "[Bar Promenade] Mother's House Interior Runtime";
        private const float TimeoutSeconds = 60f;

        /// <summary>
        /// The walk plus the enter clip: 36 frames at 12 fps is 3.0 s, the
        /// guided walk is 2.394 m, and the clock is pinned at 1/60. A FRAME
        /// budget and not a realtime deadline - batch mode runs frames as
        /// fast as it can, so "wait two seconds" means nothing here.
        /// </summary>
        private const int SitFrameBudget = 900;

        private const int StandFrameBudget = 480;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        [PrebuildSetup(typeof(MothersHouseLivingRoomAssetsSetup))]
        public IEnumerator HeroSitsOnTheSofaAndStandsBackUp()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot<MothersHouseInteriorRoot>(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                found => interior = found);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The mother's house never finished initializing.");

            // Every authored seat constant in this feature is world space,
            // which is only true while the room's own root sits at identity.
            Assert.That(
                interior.Room.position,
                Is.EqualTo(Vector3.zero));

            CityBenchSitInteraction sofa = interior.Sofa;
            Assert.That(sofa, Is.Not.Null, "The room built no sofa seat.");
            Assert.That(interior.Seats, Has.Count.EqualTo(1));
            Assert.That(
                sofa.PromptKey,
                Is.EqualTo("interaction.sit_sofa"));

            CityBenchSitPlan plan = sofa.Plan;
            PlayerInteractor interactor = interior.Player.Interactor;

            // NOT teleported to the dock: the guided walk over the real
            // 2.394 m is the thing under test.
            Assert.That(
                sofa.CanInteract(interactor),
                Is.True,
                "The sofa must be offered from the room's own spawn.");

            bool previousShadow =
                interior.Player.ContactShadow != null &&
                interior.Player.ContactShadow.enabled;

            sofa.Interact(interactor);

            PlayerAnimatedInteractionController controller = sofa.Controller;
            int frames = 0;
            while (controller.Phase !=
                   PlayerAnimatedInteractionPhase.Looping &&
                   frames < SitFrameBudget)
            {
                frames++;
                yield return null;
            }

            Assert.That(
                controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Looping),
                $"The hero never settled onto the sofa in {frames} frames " +
                $"(phase {controller.Phase}). A blocked walk aborts with a " +
                "warning; a bad dock height stalls in silence.");

            Assert.That(sofa.IsSeated, Is.True);
            Assert.That(
                sofa.PromptKey,
                Is.EqualTo(CityBenchSitInteraction.StandPromptKey));

            Transform root = interior.Player.GameObject.transform;
            Assert.That(
                Vector3.Distance(root.position, plan.EntryRootPosition),
                Is.LessThan(PlayerMotor.InteractionPositionTolerance),
                "The capsule stays on the dock for the whole interaction; " +
                "only the drawn body is carried onto the cushion.");

            var presentation =
                interior.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Vector3 pelvis =
                presentation.Registry.Anchors.Pelvis.position;
            Assert.That(
                Vector3.Distance(pelvis, plan.ActionHipPosition),
                Is.LessThan(0.02f),
                $"The seated pelvis landed at {pelvis} against the " +
                $"authored {plan.ActionHipPosition}.");

            // The visible half of "the capsule never moved": without this
            // every seated bench in the game paints an oval on the floor
            // three quarters of a metre in front of the sitter.
            Assert.That(
                interior.Player.ContactShadow.enabled,
                Is.False,
                "The contact shadow must stop drawing while he is seated.");

            // Clause 6: no cut. The room keeps its own fixed shot.
            Assert.That(
                interior.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            Assert.That(interior.Player.Motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.True,
                "The stand-up press has to stay reachable.");

            // The pose, asserted rather than logged - `LogAssert
            // .NoUnexpectedReceived()` below fails on any Debug.Log, even
            // an informative one.
            //
            // MEASURED, not guessed: pelvis (-2.260, 0.600, -0.600) exactly
            // on the authored point, feet at y 0.168 and 0.146. The feet
            // hang because these are the BUS passenger clips, which every
            // seat in the game wears - the mountain brink bench, the park
            // bench and the discarded couch all sit the same way. The band
            // is stated so a re-authored clip or a moved cushion fails here
            // instead of shipping a hero perched in the air.
            float leftFootY =
                presentation.Registry.Anchors.LeftFoot.position.y;
            float rightFootY =
                presentation.Registry.Anchors.RightFoot.position.y;
            Assert.That(
                leftFootY,
                Is.InRange(0.05f, 0.30f),
                $"The left foot sits at y {leftFootY:0.###}.");
            Assert.That(
                rightFootY,
                Is.InRange(0.05f, 0.30f),
                $"The right foot sits at y {rightFootY:0.###}.");
            Assert.That(
                pelvis.y - Mathf.Max(leftFootY, rightFootY),
                Is.GreaterThan(0.25f),
                "He must be sitting on the cushion, not standing on it.");

            yield return CaptureLivingRoomPresentation(interior);

            // Stand up. A FIXED window, never `while (sofa.IsSeated)`:
            // the seat leaves Looping on the same frame the exit is
            // requested, so that loop never runs and every assertion inside
            // it passes against nothing.
            Assert.That(sofa.CanInteract(interactor), Is.True);
            sofa.Interact(interactor);

            frames = 0;
            while (controller.Phase !=
                   PlayerAnimatedInteractionPhase.Idle &&
                   frames < StandFrameBudget)
            {
                frames++;
                yield return null;
            }

            Assert.That(
                controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle),
                $"The hero never stood back up in {frames} frames.");
            Assert.That(sofa.IsSeated, Is.False);
            Assert.That(
                Vector3.Distance(root.position, plan.EntryRootPosition),
                Is.LessThan(PlayerMotor.InteractionPositionTolerance));
            Assert.That(interior.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                interior.Player.ContactShadow.enabled,
                Is.EqualTo(previousShadow),
                "The contact shadow must come back exactly as it was.");
            Assert.That(
                interior.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            // Walk the actual gap between the lamp, sofa and chair in
            // both directions. Its old full-height lamp collider blocked it.
            var body = interior.Player.GameObject.GetComponent<CharacterController>();
            yield return WalkLivingRoom(body, new Vector2(-1.55f, 0.65f));
            yield return WalkLivingRoom(body, new Vector2(-1.55f, 2.20f));
            yield return WalkLivingRoom(body, new Vector2(-1.55f, 1.35f));
            AssertDrawnHeroShadow(interior, presentation);
            yield return WalkLivingRoom(body, new Vector2(-1.55f, -0.60f));

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The specific defect this feature was designed around: behind the
        /// sofa there is 0.425 m against a 0.64 m capsule, so a walk routed
        /// there can only stall. The offer must not reach those pockets.
        /// </summary>
        [UnityTest]
        public IEnumerator SofaIsNotOfferedFromBehindOrBesideTheStair()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot<MothersHouseInteriorRoot>(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                found => interior = found);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The mother's house never finished initializing.");

            CityBenchSitInteraction sofa = interior.Sofa;
            Assert.That(sofa, Is.Not.Null);

            Vector3[] pockets =
            {
                new Vector3(-2.8f, 0.04f, 1.4f),
                new Vector3(-2.8f, 0.04f, -1.8f)
            };

            for (int index = 0; index < pockets.Length; index++)
            {
                interior.Player.Motor.Teleport(pockets[index]);
                Physics.SyncTransforms();
                yield return null;

                Assert.That(
                    sofa.CanInteract(interior.Player.Interactor),
                    Is.False,
                    $"The sofa is offered from {pockets[index]}, where the " +
                    "walk to it can only stall against the stair ramp.");
            }

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Clause 8: a cancel restores everything it took. `InteractionCompleted`
        /// does not fire on one, so the restore has to hang off the phase.
        /// </summary>
        [UnityTest]
        public IEnumerator CancellingMidEnterRestoresTheHero()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot<MothersHouseInteriorRoot>(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                found => interior = found);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The mother's house never finished initializing.");

            CityBenchSitInteraction sofa = interior.Sofa;
            bool previousShadow = interior.Player.ContactShadow.enabled;
            sofa.Interact(interior.Player.Interactor);

            int frames = 0;
            while (sofa.Controller.Phase !=
                   PlayerAnimatedInteractionPhase.Entering &&
                   frames < SitFrameBudget)
            {
                frames++;
                yield return null;
            }

            Assert.That(
                sofa.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Entering));

            sofa.enabled = false;
            yield return null;

            Assert.That(
                sofa.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(sofa.IsSeated, Is.False);
            Assert.That(
                interior.Player.ContactShadow.enabled,
                Is.EqualTo(previousShadow));
            Assert.That(interior.Player.Motor.InputEnabled, Is.True);

            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator CaptureLivingRoomPresentation(MothersHouseInteriorRoot interior)
        {
            MothersHouseInteriorAtmosphere atmosphere = interior.Atmosphere;
            Assert.That(atmosphere.FloorLampLight.type, Is.EqualTo(LightType.Point));
            foreach (Renderer flame in atmosphere.FireFlicker.Flames)
            {
                Assert.That(flame.sharedMaterial.shader.name,
                    Is.EqualTo("BarPromenade/MothersHouseFlame"));
                Assert.That(flame.sharedMaterial.shader.isSupported, Is.True);
            }
            foreach (Light light in atmosphere.LampLights.Concat(atmosphere.WindowLights)
                         .Append(atmosphere.FireLight))
            {
                Assert.That(light.enabled, Is.True, light.name);
                Assert.That(light.lightmapBakeType, Is.EqualTo(LightmapBakeType.Realtime), light.name);
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft), light.name);
            }
            Assert.That(Vector3.Distance(atmosphere.FloorLampLight.transform.position,
                interior.World.FloorLampLightAnchor.position), Is.LessThan(0.005f));
            AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>()
                .Where(value => value.isActiveAndEnabled).ToArray();
            Assert.That(listeners, Has.Length.EqualTo(1));
            Assert.That(listeners[0].GetComponent<Camera>(), Is.SameAs(Camera.main));
            foreach (AudioSource source in new[] { atmosphere.FireCrackleSource,
                         interior.Soundscape.MuffledWindSource, interior.Soundscape.ClockSource })
            {
                Assert.That(source.enabled && !source.mute && source.isPlaying, Is.True,
                    source.name + " must be playing into the restored listener.");
                Assert.That(source.outputAudioMixerGroup, Is.Not.Null);
            }
            Assert.That(atmosphere.FireCrackleSource.volume, Is.GreaterThanOrEqualTo(0.2f));
            float[] audio = new float[atmosphere.FireCrackleSource.clip.samples];
            Assert.That(atmosphere.FireCrackleSource.clip.GetData(audio, 0), Is.True);
            Assert.That(Math.Sqrt(audio.Average(value => (double)value * value)),
                Is.GreaterThan(0.02), "The hearth clip must contain audible signal.");

            interior.Mother.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var hero = (Player3DCharacterPresentation)interior.Player.Visual;
            hero.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Vector3 initialPosition = atmosphere.FireLight.transform.localPosition;
            float initialIntensity = atmosphere.FireLight.intensity;
            float largestMove = 0f;
            float largestChange = 0f;
            for (int frame = 0; frame < 48; frame++)
            {
                for (int tick = 0; tick < 5; tick++)
                {
                    yield return null;
                }
                largestMove = Mathf.Max(largestMove, Vector3.Distance(initialPosition,
                    atmosphere.FireLight.transform.localPosition));
                largestChange = Mathf.Max(largestChange,
                    Mathf.Abs(initialIntensity - atmosphere.FireLight.intensity));
                Assert.That(Vector3.Distance(atmosphere.FireLight.transform.position,
                    interior.World.FireLightAnchor.position), Is.LessThan(0.10f));
                CaptureLivingRoomFrame($"living-room-motion/frame-{frame:000}");
            }
            Assert.That(largestMove, Is.GreaterThan(0.002f), "The flame's light centre must wander.");
            Assert.That(largestChange, Is.GreaterThan(0.15f), "The hearth must vary without strobing.");
            Color32[] seatedFrame = CaptureLivingRoomFrame("living-room-seated");
            Renderer heroFace = hero.Registry.FaceAtlas.Renderer;
            Renderer motherFace = interior.Mother.Registry.FaceAtlas.Renderer;
            float heroBrightness = AssertReadableFace(seatedFrame, heroFace, "hero");
            // The user accepted the mother remaining in the hearth's
            // half-shadow on 2026-09-08. Do not force a frontal fill onto her.
            TestContext.Out.WriteLine($"Mother's accepted half-shadow: {FaceLuminance(seatedFrame, motherFace):0.000}.");
            try
            {
                atmosphere.FloorLampLight.enabled = false;
                atmosphere.FireFlicker.Advance(1f / 60f);
                Color32[] lampOff = CaptureLivingRoomFrame("living-room-seated-lamp-off");
                Assert.That(heroBrightness - FaceLuminance(lampOff, heroFace), Is.GreaterThan(0.04f),
                    "The real lamp must materially illuminate the hero's seated face.");
            }
            finally
            {
                atmosphere.FloorLampLight.enabled = true;
                atmosphere.FireFlicker.Advance(1f / 60f);
            }
            Vector3 beforePause = atmosphere.FireLight.transform.localPosition;
            float intensityBeforePause = atmosphere.FireLight.intensity;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                yield return null;
                Assert.That(atmosphere.FireLight.transform.localPosition, Is.EqualTo(beforePause));
                Assert.That(atmosphere.FireLight.intensity, Is.EqualTo(intensityBeforePause));
            }
        }

        private static IEnumerator WalkLivingRoom(CharacterController body, Vector2 target)
        {
            for (int frame = 0; frame < 360; frame++)
            {
                Vector3 position = body.transform.position;
                Vector2 remaining = target - new Vector2(position.x, position.z);
                if (remaining.magnitude < 0.07f)
                {
                    yield break;
                }
                Vector2 step = Vector2.ClampMagnitude(remaining, 0.04f);
                body.Move(new Vector3(step.x, -0.1f, step.y));
                yield return null;
            }
            Assert.Fail($"The lamp passage is blocked before {target}: {body.transform.position}.");
        }

        private static float AssertReadableFace(Color32[] frame, Renderer face, string actor)
        {
            float luminance = FaceLuminance(frame, face);
            TestContext.Out.WriteLine($"{actor} face {face.bounds.center}: luminance {luminance:0.000}.");
            Assert.That(luminance, Is.GreaterThan(0.16f),
                $"The seated {actor}'s face must remain readable in the actual gameplay frame.");
            return luminance;
        }

        private static float FaceLuminance(Color32[] frame, Renderer face)
        {
            Vector3 viewport = Camera.main.WorldToViewportPoint(face.bounds.center);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            int centreX = Mathf.RoundToInt(viewport.x * 1280);
            int centreY = Mathf.RoundToInt(viewport.y * 720);
            float luminance = 0f;
            for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++)
            {
                Color32 sample = frame[Mathf.Clamp(centreY + y, 0, 719) * 1280 +
                    Mathf.Clamp(centreX + x, 0, 1279)];
                luminance += (sample.r * 0.2126f + sample.g * 0.7152f + sample.b * 0.0722f) / 255f;
            }
            return luminance / 49f;
        }

        private static void AssertDrawnHeroShadow(MothersHouseInteriorRoot interior,
            Player3DCharacterPresentation hero)
        {
            Renderer[] renderers = hero.Registry.Renderers
                .Where(value => value != null && value.enabled && value.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers.All(value => value.shadowCastingMode == ShadowCastingMode.On),
                Is.True, "The real character meshes must cast shadows, including while seated.");
            ShadowCastingMode[] original = renderers.Select(value => value.shadowCastingMode).ToArray();
            Color32[] withShadow = CaptureLivingRoomFrame("living-room-standing");
            Color32[] withoutShadow;
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                withoutShadow = CaptureLivingRoomFrame(null);
            }
            finally
            {
                for (int index = 0; index < renderers.Length; index++)
                {
                    renderers[index].shadowCastingMode = original[index];
                }
            }
            int changed = 0;
            for (int pixel = 0; pixel < withShadow.Length; pixel++)
            {
                Color32 before = withShadow[pixel];
                Color32 after = withoutShadow[pixel];
                if (after.r + after.g + after.b - before.r - before.g - before.b > 18)
                {
                    changed++;
                }
            }
            Assert.That(changed, Is.GreaterThan(120),
                "Turning off only the hero's shadow caster must visibly lighten the rendered room.");
        }

        private static Color32[] CaptureLivingRoomFrame(string name)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            try
            {
                camera.aspect = 16f / 9f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                if (!string.IsNullOrEmpty(name))
                {
                    string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../Captures/MothersHouseInterior", name + ".png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                }
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(sceneName),
                Is.True,
                $"Scene '{sceneName}' must be enabled in Build Settings.");
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return WaitUntil(
                () => operation.isDone,
                $"Scene '{sceneName}' did not load.");

            T found = null;
            yield return WaitUntil(
                () =>
                {
                    Scene scene = SceneManager.GetActiveScene();
                    found = FindExactRoot<T>(scene, exactRootName);
                    return scene.name == sceneName && found != null;
                },
                $"Scene '{sceneName}' did not create root " +
                $"'{exactRootName}'.");
            capture(found);
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static T FindExactRoot<T>(
            Scene scene,
            string exactRootName)
            where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    return roots[index].GetComponent<T>();
                }
            }

            return null;
        }
    }
}
