using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused lodge furnishings, two physical doors and interior wind capture.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageLodgeShelter()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () =>
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                return new[]
                {
                    Shot.At("lodge-00-interior-unlit", LodgePoint(root, -1.1f, 1.72f, -4.8f),
                        LodgePoint(root, 1f, 1.25f, 1.3f), 80f),
                    Shot.At("lodge-01-exterior-open", LodgePoint(root, -2f, 1.72f, -11f),
                        LodgePoint(root, 0f, 1.5f, -5.8f), 67f)
                };
            });

            LodgeShelterController lodge = root.LodgeShelter;
            Assert.That(lodge, Is.Not.Null);
            Assert.That(lodge.transform.Find("OpenDoorLeaves"), Is.Null, "The old combined model must be replaced.");
            Assert.That(lodge.Hinge(0), Is.Not.SameAs(lodge.Hinge(1)));
            Assert.That(lodge.LanternLight.enabled, Is.False);
            Assert.That(root.Music.IsPlaybackSuppressed, Is.True, "The initial open doors keep the village theme silent.");
            Assert.That(root.Music.Source.isPlaying, Is.False);
            Assert.That(root.Music.NormalizedGain, Is.Zero);
            AudioClip windClip = root.WindSound.ActiveClip;
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                Assert.That(listener.GetComponents<AudioLowPassFilter>().Any(filter => filter.enabled), Is.False,
                    "Closing the lodge must not filter the listener or local indoor sounds.");

            // All four combinations use the actual imported mesh colliders.
            // Neither door may change the other door's pose or session state.
            foreach (int mask in new[] { 3, 1, 2, 0 })
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                SetLodgeDoors(lodge, mask);
                yield return null;
                Assert.That(root.Music.IsPlaybackSuppressed, Is.EqualTo(mask != 0),
                    "The actual door combination gates the village theme, mask " + mask);
                if (mask != 0)
                {
                    Assert.That(root.Music.NormalizedGain, Is.Zero);
                    Assert.That(root.Music.Source.isPlaying, Is.False);
                }
                for (int index = 0; index < 2; index++)
                {
                    bool open = (mask & (1 << index)) != 0;
                    Assert.That(LodgeShelterSessionState.IsDoorOpen(index), Is.EqualTo(open));
                    float x = index == 0 ? -.654f : .654f;
                    var ray = new Ray(LodgePoint(root, x, 1.2f, -7f), lodge.transform.forward);
                    Assert.That(Physics.Raycast(ray, out RaycastHit hit, 2.1f,
                        PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore), Is.EqualTo(!open),
                        "Actual half-opening collision, mask " + mask + ", leaf " + index);
                    if (!open) Assert.That(hit.transform.IsChildOf(lodge.Hinge(index)), Is.True);
                    float angle = Quaternion.Angle(lodge.Hinge(index).localRotation, Quaternion.identity);
                    Assert.That(angle, Is.EqualTo(open ? 180f : 0f).Within(.01f));
                }
                root.Workroom.Environment.Advance(1f);
                root.InteriorAcoustics.Advance(1f);
                root.WindSound.SetNormalizedStrength(.9f);
                float expected = VillageInteriorAcoustics.EvaluateLodgeEnclosure(true, lodge.OpenDoorCount);
                Assert.That(root.InteriorAcoustics.Enclosure, Is.EqualTo(expected).Within(.001f));
                Assert.That(root.WindSound.Enclosure, Is.EqualTo(expected).Within(.001f),
                    "The workroom must not overwrite the lodge enclosure.");
                float gain = Mathf.Lerp(1f, VillageInteriorAcoustics.LodgeClosedVolumeMultiplier, expected);
                float cutoff = Mathf.Lerp(1f, VillageInteriorAcoustics.LodgeClosedCutoffMultiplier, expected);
                Assert.That(root.WindSound.Source.volume,
                    Is.EqualTo(MountainRoadWindSoundPlayer.MaximumVolume * Mathf.Pow(.9f, .85f) * gain).Within(.0001f));
                Assert.That(root.WindSound.ToneFilter.cutoffFrequency,
                    Is.EqualTo(Mathf.Lerp(700f, 2600f, .9f) * cutoff).Within(1f));
                Assert.That(root.WindSound.ActiveClip, Is.SameAs(windClip));
            }
            yield return VerifyLodgeMusicTransitions(root);

            SetLodgeDoors(lodge, 3);
            foreach (Vector3 occupied in new[] { new Vector3(-.65f, .02f, -5.985f), new Vector3(-.16f, .02f, -6.43f) })
            {
                PlaceLodgeHero(root, occupied);
                Assert.That(lodge.TrySetDoorOpen(0, false), Is.False,
                    "Neither the panel nor its projecting grip may appear through the hero at " + occupied);
                Assert.That(LodgeShelterSessionState.LeftDoorOpen, Is.True);
            }
            PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));

            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                // The same shared E path works from inside and outside. In
                // particular, the guest can close both leaves after entering.
                foreach (bool inside in new[] { true, false })
                for (int index = 0; index < 2; index++)
                {
                    LodgeShelterInteraction door = lodge.Door(index);
                    Vector3 dock = door.InteractionPosition + lodge.transform.forward * (inside ? .88f : -.88f);
                    root.Player.Motor.Teleport(dock);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(door),
                        "Door E target, inside=" + inside + ", leaf=" + index);
                    Assert.That(root.InteractionPrompt.PromptKey, Is.EqualTo(door.PromptKey));
                    bool wasOpen = LodgeShelterSessionState.IsDoorOpen(index);
                    bool otherOpen = LodgeShelterSessionState.IsDoorOpen(1 - index);
                    yield return PressLodgeUse(input, keyboard);
                    Assert.That(LodgeShelterSessionState.IsDoorOpen(index), Is.EqualTo(!wasOpen));
                    Assert.That(LodgeShelterSessionState.IsDoorOpen(1 - index), Is.EqualTo(otherOpen));
                }

                foreach (LodgeShelterInteraction stub in new[] { lodge.Cot, lodge.Kettle })
                {
                    root.Player.Motor.Teleport(stub.InteractionPosition);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(stub));
                    InventoryItemStack[] before = GameSessionState.InventoryItems.ToArray();
                    double minutes = GameSessionState.GameTimeOfDayMinutes;
                    stub.Interact(root.Player.Interactor);
                    Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(minutes));
                    Assert.That(GameSessionState.InventoryItems, Is.EqualTo(before));
                    Assert.That(root.InteractionPrompt.IsFeedbackVisible, Is.True);
                    Assert.That(root.InteractionPrompt.IsSpeaking, Is.False);
                    Assert.That(root.InteractionPrompt.GetBottomPromptKeyAt(Time.unscaledTime),
                        Is.EqualTo(stub == lodge.Cot ? "lodge.cot.inspect" : "lodge.kettle.inspect"));
                    root.InteractionPrompt.ClearFeedback();
                    yield return null;
                }

                root.Player.Motor.Teleport(lodge.Lantern.InteractionPosition);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(lodge.Lantern));
                yield return PressLodgeUse(input, keyboard);
                Assert.That(lodge.LanternLight.enabled && LodgeShelterSessionState.LanternLit, Is.True);
                yield return PressLodgeUse(input, keyboard);
                Assert.That(lodge.LanternLight.enabled || LodgeShelterSessionState.LanternLit, Is.False);
            }
            finally { input.TearDown(); }

            PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
            SetLodgeDoors(lodge, 0);
            Assert.That(lodge.SetLanternLit(true), Is.True);
            root.InteriorAcoustics.Advance(1f);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Assert.That(lodge.TrySetDoorOpen(0, true), Is.False);
                Assert.That(lodge.SetLanternLit(false), Is.False);
                Assert.That(lodge.Cot.CanInteract(root.Player.Interactor), Is.False);
                float enclosure = root.InteriorAcoustics.Enclosure;
                root.InteriorAcoustics.Advance(2f);
                Assert.That(root.InteriorAcoustics.Enclosure, Is.EqualTo(enclosure));
            }
            lodge.enabled = false;
            Assert.That(lodge.LanternLight.enabled, Is.False);
            lodge.enabled = true;
            lodge.Hinge(0).localRotation = Quaternion.Euler(0f, 73f, 0f);
            lodge.RestoreState();
            Assert.That(lodge.OpenDoorCount, Is.Zero);
            Assert.That(Quaternion.Angle(lodge.Hinge(0).localRotation, Quaternion.identity), Is.LessThan(.01f));
            Assert.That(lodge.LanternLight.enabled, Is.True, "Disable/restore retains the current session's shelter choices.");
            VerifyLodgeWeatherBoundary(root);

            LodgeFrame(root, "lodge-02-interior-lit", new Vector3(-1.1f, 1.72f, -4.8f), new Vector3(1f, 1.25f, 1.3f), 80f);
            LodgeFrame(root, "lodge-03-cot", new Vector3(-.9f, 1.65f, -1.1f), new Vector3(-3.2f, .55f, 1f), 60f);
            LodgeFrame(root, "lodge-04-tea-and-lantern", new Vector3(3.45f, 1.7f, -1.35f), new Vector3(4.1f, 1.3f, 1f), 53f);
            LodgeFrame(root, "lodge-05-exterior-closed", new Vector3(-2f, 1.72f, -11f), new Vector3(0f, 1.5f, -5.8f), 67f);
            yield return CaptureLodgeWind(root);

            PlaceLodgeHero(root, new Vector3(0f, .02f, -8f));
            root.Workroom.Environment.Advance(1f);
            root.InteriorAcoustics.Advance(VillageInteriorAcoustics.TransitionSeconds * .5f);
            Assert.That(root.WindSound.Enclosure, Is.InRange(.45f, .55f), "The acoustic boundary fades instead of snapping.");
            root.InteriorAcoustics.Advance(VillageInteriorAcoustics.TransitionSeconds * .5f);
            Assert.That(root.WindSound.Enclosure, Is.Zero);
            Assert.That(root.WindSound.ActiveClip, Is.SameAs(windClip));
            Assert.That(root.WindSound.Source.isPlaying, Is.True);
            yield return VerifyLodgeWarmth(root);
            GameSessionState.BeginNewGame();
            lodge.RestoreState();
            Assert.That(lodge.OpenDoorCount, Is.EqualTo(2));
            Assert.That(lodge.LanternLight.enabled || LodgeShelterSessionState.LanternLit, Is.False);
            Assert.That(LodgeStoveSessionState.IsBurning, Is.False);
            Assert.That(root.Stove.ProvidesWarmth(root.Stove.Plan.EntryPose.RootPosition), Is.False);
        }

        private static Vector3 LodgePoint(AlpineVillageRoot root, float x, float y, float z) =>
            root.LodgeShelter.transform.TransformPoint(new Vector3(x, y, z));

        private static void PlaceLodgeHero(AlpineVillageRoot root, Vector3 localGround) =>
            root.Player.Motor.Teleport(root.LodgeShelter.transform.TransformPoint(localGround) +
                Vector3.up * PlayerFactory.GroundedRootOffset);

        private static void SetLodgeDoors(LodgeShelterController lodge, int mask)
        {
            for (int index = 0; index < 2; index++)
                Assert.That(lodge.TrySetDoorOpen(index, (mask & (1 << index)) != 0), Is.True);
            Physics.SyncTransforms();
        }

        private static IEnumerator PressLodgeUse(InputTestFixture input, Keyboard keyboard)
        {
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
        }

        private static IEnumerator VerifyLodgeMusicTransitions(AlpineVillageRoot root)
        {
            AlpineVillageMusicPlayer music = root.Music;
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((music.ActiveClip == null || music.ActiveClip.loadState != AudioDataLoadState.Loaded ||
                    music.PlaybackState == SceneMusicPlaybackState.Loading || music.IsFadeInDeferred) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(music.ActiveClip, Is.Not.Null, "The closed lodge must load the existing village theme.");
            Assert.That(music.ActiveClip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            Assert.That(music.IsFadeInDeferred, Is.False);
            AudioClip clip = music.ActiveClip;
            bool enabled = music.enabled;
            try
            {
                // The real Update processes each door change. Between those
                // frames, advance only the shared fade clock for bounded proof.
                music.enabled = false;
                music.AdvanceFade(.5f);
                Assert.That(music.NormalizedGain, Is.InRange(.45f, .95f), "Closing both doors fades in instead of jumping to full gain.");
                music.AdvanceFade(.5f);
                Assert.That(music.NormalizedGain, Is.EqualTo(1f));
                Assert.That(music.Source.volume, Is.EqualTo(1f).Within(.0001f), "The full village theme uses the requested louder source gain.");
                Assert.That(music.Source.isPlaying, Is.True);
                music.Source.timeSamples = Mathf.Min(clip.samples / 2, clip.frequency * 5);

                foreach (int mask in new[] { 1, 2 })
                {
                    SetLodgeDoors(root.LodgeShelter, mask);
                    music.enabled = true;
                    yield return null;
                    music.enabled = false;
                    Assert.That(music.IsPlaybackSuppressed, Is.True);
                    music.AdvanceFade(mask == 1 ? 1f : 1.9f);
                    Assert.That(music.NormalizedGain, mask == 1 ? Is.InRange(.65f, .9f) : Is.InRange(.4f, .6f),
                        "Either open leaf starts the shared four-second tail.");
                    if (mask == 2)
                    {
                        music.AdvanceFade(2.1f);
                        Assert.That(music.NormalizedGain, Is.Zero);
                        Assert.That(music.IsPaused, Is.True);
                        Assert.That(music.Source.isPlaying, Is.False);
                        int pausedSample = music.Source.timeSamples;
                        yield return null;
                        Assert.That(music.Source.timeSamples, Is.EqualTo(pausedSample));
                    }
                    float priorGain = music.NormalizedGain;
                    int priorSample = music.Source.timeSamples;
                    SetLodgeDoors(root.LodgeShelter, 0);
                    music.enabled = true;
                    yield return null;
                    music.enabled = false;
                    Assert.That(music.IsPlaybackSuppressed, Is.False);
                    Assert.That(music.ActiveClip, Is.SameAs(clip));
                    Assert.That(music.NormalizedGain, Is.InRange(priorGain - .001f, priorGain + .1f),
                        "A quick reversal or paused resume fades from the existing gain.");
                    Assert.That(music.Source.timeSamples, Is.GreaterThanOrEqualTo(priorSample - 2048),
                        "Neither quick re-close nor repeated close restarts the theme.");
                    music.AdvanceFade(1f);
                    Assert.That(music.NormalizedGain, Is.EqualTo(1f));
                }

                PlaceLodgeHero(root, new Vector3(0f, .02f, -8f));
                music.enabled = true;
                yield return null;
                Assert.That(music.IsPlaybackSuppressed, Is.False,
                    "The requested music gate depends on the doors, not the hero's position.");
            }
            finally
            {
                music.enabled = enabled;
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
            }
        }

        private static IEnumerator VerifyLodgeWarmth(AlpineVillageRoot root)
        {
            LodgeStoveInteraction stove = root.Stove;
            AlpineColdExposureDriver cold = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
            var hero = root.Player.Visual as Player3DCharacterPresentation;
            Assert.That(cold, Is.Not.Null);
            Assert.That(hero, Is.Not.Null);
            Vector3 far = new Vector3(-5.5f, .02f, 2.5f);
            PlaceLodgeHero(root, far);
            SetLodgeDoors(root.LodgeShelter, 0);
            cold.ResetSession();
            cold.Model.Step(12f, false);
            try
            {
                Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.False,
                    "Shut doors without a fire do not create heat.");
                Assert.That(cold.IsSheltered, Is.False);
                yield return null;
                float unheatedExposure = cold.Model.ExposureSeconds;
                yield return null;
                Assert.That(cold.Model.ExposureSeconds, Is.GreaterThan(unheatedExposure));
                Assert.That(hero.ColdBodyWeight, Is.GreaterThan(0f));

                // Commit existing fuel/ignition rules directly: the separate
                // ignition capture already owns the three-strike presentation.
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.FirewoodLog), Is.True);
                Assert.That(LodgeStoveSessionState.TryPlaceLog(), Is.True);
                Assert.That(LodgeStoveSessionState.TryIgnite(), Is.True);
                foreach (Vector3 point in new[] { new Vector3(-8.1f, .02f, -5.2f), new Vector3(8.1f, .02f, -5.2f),
                    new Vector3(-8.1f, .02f, 5.2f), new Vector3(8.1f, .02f, 5.2f),
                    new Vector3(0f, .02f, -5.3f), far })
                {
                    PlaceLodgeHero(root, point);
                    Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.True, "Heated lodge at " + point);
                    Assert.That(cold.IsSheltered, Is.True, "The live frost driver reads the whole room.");
                    float exposure = cold.Model.ExposureSeconds;
                    yield return null;
                    yield return null;
                    Assert.That(cold.Model.ExposureSeconds, Is.LessThanOrEqualTo(exposure));
                    Assert.That(hero.ColdBodyWeight, Is.Zero);
                    Assert.That(hero.ColdArmWeight, Is.Zero);
                    Assert.That(hero.ColdBreath.IsEmissionEnabled, Is.False);
                }
                Assert.That(cold.Model.ExposureSeconds, Is.LessThan(unheatedExposure), "The lit closed room thaws existing frost.");
                PlaceLodgeHero(root, far);
                foreach (int mask in new[] { 1, 2, 3 })
                {
                    SetLodgeDoors(root.LodgeShelter, mask);
                    Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.False);
                    Assert.That(stove.ProvidesWarmth(stove.Plan.EntryPose.RootPosition), Is.True,
                        "An open entrance preserves the original nearby stove warmth.");
                    Assert.That(cold.IsSheltered, Is.False);
                }
                SetLodgeDoors(root.LodgeShelter, 0);
                PlaceLodgeHero(root, new Vector3(0f, .02f, -8f));
                Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.False);
                Assert.That(cold.IsSheltered, Is.False, "A heated lodge cannot warm the outdoor listener.");
            }
            finally { LodgeStoveSessionState.ResetForNewSession(); cold.ResetSession(); }
        }

        private static void LodgeFrame(AlpineVillageRoot root, string name, Vector3 from, Vector3 to, float fov)
        {
            Camera camera = Camera.main;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            Renderer[] hero = root.Player.GameObject.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            try
            {
                foreach (Renderer renderer in hero) renderer.enabled = false;
                Vector3 world = root.LodgeShelter.transform.TransformPoint(from);
                camera.transform.SetPositionAndRotation(world,
                    Quaternion.LookRotation(root.LodgeShelter.transform.TransformPoint(to) - world));
                camera.fieldOfView = fov;
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, name);
            }
            finally
            {
                foreach (Renderer renderer in hero) if (renderer != null) renderer.enabled = true;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = previousFov;
            }
        }

        private static void VerifyLodgeWeatherBoundary(AlpineVillageRoot root)
        {
            foreach (ParticleSystem field in new[] { root.Snow.Particles, root.Fog.Particles,
                         root.BlowingSnow.Particles, root.PeripheralBlizzard.Particles })
            {
                var saved = new ParticleSystem.Particle[field.particleCount];
                int count = field.GetParticles(saved);
                Vector3 Local(Vector3 world)
                {
                    var main = field.main;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Local) return field.transform.InverseTransformPoint(world);
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
                        return main.customSimulationSpace.InverseTransformPoint(world);
                    return world;
                }
                Vector3 inside = Local(LodgePoint(root, 1.1f, 1.5f, -2f));
                Vector3 outside = Local(LodgePoint(root, 1.1f, 1.5f, -8f));
                var probes = new[]
                {
                    new ParticleSystem.Particle { position = inside, remainingLifetime = 2f, startLifetime = 2f, startSize = .1f },
                    new ParticleSystem.Particle { position = outside, remainingLifetime = 2f, startLifetime = 2f, startSize = .1f }
                };
                try
                {
                    field.SetParticles(probes, probes.Length);
                    root.Workroom.Environment.CullInteriorWeather();
                    int kept = field.GetParticles(probes);
                    Assert.That(kept, Is.EqualTo(1), field.name + ": only the interior particle is culled.");
                    Assert.That(Vector3.Distance(probes[0].position, outside), Is.LessThan(.001f));
                }
                finally { field.SetParticles(saved, count); }
            }
        }

        private static IEnumerator CaptureLodgeWind(AlpineVillageRoot root)
        {
            AudioSource wind = root.WindSound.Source;
            AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
            bool[] muted = sources.Select(source => source.mute).ToArray();
            float volume = AudioListener.volume;
            bool paused = AudioListener.pause;
            bool capturing = false;
            int rate = AudioSettings.outputSampleRate;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.AlpineVillage);
            try
            {
                foreach (AudioSource source in sources) source.mute = source != wind;
                AudioListener.pause = false;
                AudioListener.volume = 0f;
                Assert.That(AudioRenderer.Start(), Is.True, "Offline audio warmup must start before unmuting its capture.");
                capturing = true;
                AudioListener.volume = 1f;
                yield return PumpFrostHearthAudio(rate / 5, null, 30, false);
                AudioListener.volume = 0f;
                AudioRenderer.Stop();
                capturing = false;
                yield return null;
                Assert.That(AudioRenderer.Start(), Is.True, "Offline audio capture must start before unmuting its render.");
                capturing = true;
                AudioListener.volume = 1f;
                double openRms = 0d;
                foreach (int mask in new[] { 3, 0 })
                {
                    SetLodgeDoors(root.LodgeShelter, mask);
                    root.Workroom.Environment.Advance(1f);
                    root.InteriorAcoustics.Advance(1f);
                    yield return PumpFrostHearthAudio(rate, null);
                    var samples = new List<float>();
                    yield return PumpFrostHearthAudio(rate * 5, samples);
                    float[] pcm = samples.ToArray();
                    double rms = FrostHearthRms(pcm, rate, false);
                    string name = mask == 3 ? "lodge-wind-open" : "lodge-wind-closed";
                    WritePcmWave(Path.Combine(folder, name + ".wav"), pcm, rate, 2, 1f);
                    TestContext.Out.WriteLine($"{name}: DSP RMS {rms:F6}, cutoff {root.WindSound.ToneFilter.cutoffFrequency:F1} Hz.");
                    Assert.That(rms, Is.GreaterThan(.000001d), "The actual wind remains audible through the walls.");
                    if (mask == 3) openRms = rms;
                    else Assert.That(rms, Is.LessThan(openRms * .10d), "The closed lodge leaves only a near-inaudible exterior wind trace.");
                }
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                for (int i = 0; i < sources.Length; i++) if (sources[i] != null) sources[i].mute = muted[i];
                AudioListener.pause = paused;
                AudioListener.volume = volume;
            }
        }
    }
}
