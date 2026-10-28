using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
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
        // A single explicit selection owns this plan; release it with the scene's
        // borrowed state rather than rebuilding street ribbons for every contact sample.
        private static CityStreetSurfacePlan localAcceptanceStreets;

        [UnityTest]
        [Explicit("Only the waterworks and drying court: real entrances, physical sound owners, live cloth/hand props and smoking stroll. Run alone.")]
        [PrebuildSetup(typeof(ParkAssetsSetup))]
        public IEnumerator CityDomesticCourts() => CaptureCityLocalAcceptance(ReviewDomesticCourts);

        [UnityTest]
        [Explicit("Only the bar-side yard: one complete existing wheelchair circuit, real tyre support/pivots and four addressed views. Run alone.")]
        [PrebuildSetup(typeof(ParkAssetsSetup))]
        public IEnumerator CityBarYardCircuit() => CaptureCityLocalAcceptance(ReviewBarYardCircuit);

        [UnityTest]
        [Explicit("Only the four urban landmarks from supported public streets, at the ordinary lens, in colour and grayscale. Run alone.")]
        [PrebuildSetup(typeof(ParkAssetsSetup))]
        public IEnumerator CityLandmarksFromStreets() => CaptureCityLocalAcceptance(ReviewLandmarksFromStreets);

        [UnityTest]
        [Explicit("Only the four west/south service approaches: actual motor in/out, rock/closed walking boundary and addressed construction details. Run alone.")]
        [PrebuildSetup(typeof(ParkAssetsSetup))]
        public IEnumerator CityFringeApproaches() => CaptureCityLocalAcceptance(ReviewFringeApproaches);

        [UnityTest]
        [Explicit("Three public City observers: 15 s each of autonomous physical stereo, music muted only. Listen to the unnormalized WAVs for district contrast. Run alone.")]
        [PrebuildSetup(typeof(ParkAssetsSetup))]
        public IEnumerator CityDistrictAcousticContrast()
        {
            float previousVolume = AudioListener.volume, previousDelta = Time.captureDeltaTime;
            bool previousPause = AudioListener.pause, capturing = false, stateCaptured = false;
            CityStreetSurfacePlan previousStreets = localAcceptanceStreets;
            var musicMutes = new Dictionary<AudioSource, bool>();
            CityGameRoot city = null;
            PlayerCameraFollow follow = null;
            Pose cameraPose = default, heroPose = default;
            bool followEnabled = false, motorInput = false;
            try
            {
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                Time.captureDeltaTime = .1f;
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized && load.isDone, Is.True, "District audio did not finish City composition.");
                Assert.That(city.Camera.GetComponent<AudioListener>(), Is.Not.Null);
                Assert.That(city.Camera.GetComponent<AudioListener>().isActiveAndEnabled, Is.True);
                Assert.That(AudioSettings.GetConfiguration().speakerMode, Is.EqualTo(AudioSpeakerMode.Stereo),
                    "District audio needs the actual stereo output configuration.");
                int rate = AudioSettings.outputSampleRate;
                Assert.That(rate, Is.InRange(8000, 192000), "Bound the offline PCM allocation to a supported output rate.");
                cameraPose = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
                heroPose = new Pose(city.Player.GameObject.transform.position, city.Player.GameObject.transform.rotation);
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow != null && follow.enabled;
                motorInput = city.Player.Motor.InputEnabled;
                stateCaptured = true;
                if (follow != null) follow.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                localAcceptanceStreets = CityStreetSurfacePlanner.Create(city.Layout);
                // Preserve location handovers, fades, playheads, weather and all
                // physical owners. Only their music AudioSources borrow mute.
                MuteLocalAcceptanceMusic(city, musicMutes);
                AudioListener.pause = false;
                Assert.That(AudioRenderer.Start(), Is.True,
                    "Unity AudioRenderer.Start is unavailable; this gate needs Editor PlayMode with audio enabled.");
                capturing = true;
                AudioListener.volume = 1f;
                // Unity's first offline session may return no samples. Reuse the
                // established throwaway-session bootstrap, without a whitelist.
                yield return PumpDistrictAcoustics(city, rate, null, null, 30, false);
                AudioListener.volume = 0f;
                AudioRenderer.Stop(); capturing = false;
                yield return null;
                Assert.That(AudioRenderer.Start(), Is.True, "Unity could not restart offline stereo after bootstrap.");
                capturing = true;
                AudioListener.volume = 1f;

                string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", "CityDistrictAcousticContrast");
                System.IO.Directory.CreateDirectory(folder);
                // One exact-length PCM array is reused for all three places.
                // Each native frame buffer is disposed before the next yield.
                var pcm = new float[checked(rate * 15 * 2)];
                CitySourceSoundId[] cues = { CitySourceSoundId.DryingYardClothLoop,
                    CitySourceSoundId.WaterworksPipeLoop, CitySourceSoundId.ParkFountainLoop };
                string[] names = { "01-residential-drying-yard", "02-industrial-waterworks", "03-park-fountain" };
                Vector3? previousEye = null;
                for (int zone = 0; zone < cues.Length; zone++)
                {
                    CitySoundSourceDescriptor anchor = city.SoundscapePlan.LoopingSources.Single(source => source.Cue == cues[zone]);
                    Vector3 preferred = Vector3.back;
                    if (zone < 2)
                    {
                        CityDistrictPointOfInterestKind kind = zone == 0 ? CityDistrictPointOfInterestKind.ResidentialDryingYard
                            : CityDistrictPointOfInterestKind.OldTownWaterworksCourt;
                        CityDistrictPointOfInterestDescriptor poi = city.Layout.DistrictPointsOfInterest.Single(point => point.Kind == kind);
                        Assert.That(poi.Accesses, Is.Not.Empty);
                        preferred = poi.Accesses[0].Center - anchor.WorldPosition;
                    }
                    Vector3 eye = FindLocalDetailEye(city, anchor.WorldPosition, preferred, 4f);
                    if (previousEye.HasValue) Assert.That(Vector3.Distance(eye, previousEye.Value), Is.GreaterThan(1f));
                    previousEye = eye;
                    Vector3 floor = eye - Vector3.up * EyeHeight;
                    AssertLocalClearColumn(city, floor, .32f, names[zone] + " public observer");
                    city.Player.Motor.Teleport(floor + Vector3.up * PlayerFactory.GroundedRootOffset);
                    city.Player.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(anchor.WorldPosition - eye, Vector3.up));
                    city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(anchor.WorldPosition - eye));
                    Physics.SyncTransforms();
                    MuteLocalAcceptanceMusic(city, musicMutes);
                    // Let the real distance gates, fades and occlusion react to
                    // the moved hero/listener. No seek or manual sound tick.
                    yield return PumpDistrictAcoustics(city, rate, null, null, 30, true);
                    AssertLocalLoopOwner(city, cues[zone]);
                    int voiceIndex = Enumerable.Range(0, city.SoundscapePlan.LoopingSources.Count)
                        .Single(index => city.SoundscapePlan.LoopingSources[index].Cue == cues[zone]);
                    AudioSource localVoice = city.Soundscape.GetLoopSources()[voiceIndex];
                    Assert.That(localVoice.isPlaying && !localVoice.mute && localVoice.volume > 0f, Is.True,
                        names[zone] + " must retain its autonomous physical loop at the public observer.");
                    Assert.That(Vector3.Distance(eye, localVoice.transform.position), Is.LessThan(localVoice.maxDistance));
                    var report = new List<string>(2048)
                    {
                        names[zone] + ": 15 s stereo; gain=1; no normalization; all non-music sources retain their state.",
                        "LIVE samples active voices every second; EVENT is only the latest provenance sampled each frame and may predate this window. Scheduled silence is not forced.",
                        "observer=" + city.Player.GameObject.transform.position.ToString("F3") + "; listener=" + city.Camera.transform.position.ToString("F3") +
                            "; forward=" + city.Camera.transform.forward.ToString("F3") + "; target=" + anchor.StableId + "; physicalOwner=" + anchor.PhysicalOwner,
                        "gameTimeStart=" + GameSessionState.GameTimeOfDayMinutes + "; sampleRate=" + rate +
                            "; eventsBefore=" + city.Soundscape.PlayedEventCount
                    };
                    foreach (CitySoundSourceDescriptor source in city.SoundscapePlan.Sources)
                        report.Add("PLAN " + source.StableId + "; owner=" + source.PhysicalOwner + "; cue=" + source.Cue +
                            "; position=" + source.WorldPosition.ToString("F3") + "; ownerBounds=" + source.PhysicalOwnerBounds +
                            "; radius=" + source.AudibleRadius + "; playback=" + source.Playback);
                    AppendDistrictSourceInventory(city, report, "start", musicMutes);
                    try
                    {
                        yield return PumpDistrictAcoustics(city, rate, pcm, report, 180, true);
                        double rms = ProductionAudioRms(pcm), left = ProductionAudioRms(pcm, 0), right = ProductionAudioRms(pcm, 1);
                        float peak = 0f;
                        foreach (float sample in pcm) peak = Mathf.Max(peak, Mathf.Abs(sample));
                        WritePcmWave(System.IO.Path.Combine(folder, names[zone] + ".wav"), pcm, rate, 2, 1f);
                        report.Add(FormattableString.Invariant($"OUTPUT seconds=15 RMS={rms:F8} left={left:F8} right={right:F8} peak={peak:F8}; gameTimeEnd={GameSessionState.GameTimeOfDayMinutes:F3}; eventsAfter={city.Soundscape.PlayedEventCount}"));
                        Assert.That(rms, Is.GreaterThan(1e-7d), "Offline output is silent; no district contrast proof exists.");
                    }
                    finally
                    {
                        AppendDistrictSourceInventory(city, report, "end", musicMutes);
                        System.IO.File.WriteAllLines(System.IO.Path.Combine(folder, names[zone] + ".txt"), report);
                    }
                    TestContext.Out.WriteLine(report.FindLast(line => line.StartsWith("OUTPUT ", StringComparison.Ordinal)));
                }
                TestContext.Out.WriteLine("CITY DISTRICT ACOUSTIC CAPTURE READY: listen to three unnormalized 15 s WAVs; numeric energy does not prove artistic district contrast.");
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                foreach (var pair in musicMutes) if (pair.Key != null) pair.Key.mute = pair.Value;
                if (stateCaptured && city != null)
                {
                    city.Player.Motor.Teleport(heroPose.position);
                    city.Player.GameObject.transform.rotation = heroPose.rotation;
                    city.Player.Motor.SetInputEnabled(motorInput);
                    city.Camera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
                    if (follow != null) follow.enabled = followEnabled;
                }
                localAcceptanceStreets = previousStreets;
                Time.captureDeltaTime = previousDelta;
                AudioListener.pause = previousPause;
                AudioListener.volume = previousVolume;
                GameSessionState.BeginNewGame();
            }
        }

        private static void MuteLocalAcceptanceMusic(CityGameRoot city, Dictionary<AudioSource, bool> original)
        {
            void Mute(SceneMusicPlayer music)
            {
                if (music == null || music.Source == null) return;
                if (!original.ContainsKey(music.Source)) original.Add(music.Source, music.Source.mute);
                music.Source.mute = true;
            }
            foreach (SceneMusicPlayer music in Object.FindObjectsByType<SceneMusicPlayer>(FindObjectsInactive.Include)) Mute(music);
            Mute(city.Music);
            foreach (CityLocationMusicSlot slot in city.LocationMusic.Slots) Mute(slot.Theme);
            Assert.That(original.Count, Is.InRange(1, 32), "Only the bounded scene/location music owners may borrow mute.");
        }

        private static IEnumerator PumpDistrictAcoustics(CityGameRoot city, int rate, float[] pcm,
            List<string> report, int frameBudget, bool requireSamples)
        {
            int written = 0, target = pcm != null ? pcm.Length : rate * 2;
            CitySoundProvenanceEntry? last = null;
            IReadOnlyList<AudioSource> loops = report != null ? city.Soundscape.GetLoopSources() : null;
            IReadOnlyList<AudioSource> details = report != null ? city.Soundscape.GetDetailSources() : null;
            for (int frame = 0; frame < frameBudget && written < target; frame++)
            {
                yield return null;
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                Assert.That(count, Is.InRange(0, rate / 2), "Unexpected offline frame size; refuse an unbounded native allocation.");
                using (var buffer = new NativeArray<float>(count * 2, Allocator.Temp))
                {
                    bool success = AudioRenderer.Render(buffer);
                    if (count > 0)
                    {
                        Assert.That(success, Is.True, "Unity did not render the requested stereo frame.");
                        int copy = Math.Min(buffer.Length, target - written);
                        if (pcm != null) NativeArray<float>.Copy(buffer, 0, pcm, written, copy);
                        written += copy;
                    }
                }
                if (report == null) continue;
                if (frame % 10 == 0)
                    foreach (AudioSource source in loops.Concat(details))
                        report.Add("LIVE frame=" + frame + "; " + source.name + "; clip=" + (source.clip != null ? source.clip.name : "none") +
                            "; playing=" + source.isPlaying + "; mute=" + source.mute + "; gain=" + source.volume + "; position=" + source.transform.position.ToString("F3"));
                if (city.Soundscape.TryGetLastSound(out CitySoundProvenanceEntry entry) && (!last.HasValue ||
                    entry.AbsoluteGameTime != last.Value.AbsoluteGameTime || entry.EmitterStableId != last.Value.EmitterStableId ||
                    entry.VoiceIndex != last.Value.VoiceIndex || entry.Reason != last.Value.Reason))
                {
                    report.Add("EVENT latest-at-frame=" + frame + "; " + entry.EmitterStableId + "; owner=" + entry.PhysicalOwner +
                        "; cue=" + entry.Cue + "; reason=" + entry.Reason + "; position=" + entry.WorldPosition.ToString("F3") +
                        "; gameTime=" + entry.AbsoluteGameTime + "; distance=" + entry.ListenerDistance + "; blockers=" + entry.OcclusionBlockers);
                    last = entry;
                }
                Assert.That(report.Count, Is.LessThanOrEqualTo(4096), "Bound one zone's diagnostic report.");
            }
            if (requireSamples) Assert.That(written, Is.EqualTo(target),
                "Unity offline audio produced no complete recording within the bounded frame budget; inspect audio/backend support.");
        }

        private static void AppendDistrictSourceInventory(CityGameRoot city, List<string> report, string phase,
            Dictionary<AudioSource, bool> musicMutes)
        {
            AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
            Assert.That(sources.Length, Is.LessThanOrEqualTo(1024), "Bound the existing source inventory, rather than creating a capture mix.");
            foreach (AudioSource source in sources)
            {
                string path = source.name;
                for (Transform parent = source.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
                string owners = string.Join(",", source.GetComponentsInParent<MonoBehaviour>(true).Where(owner => owner != null).Take(4).Select(owner => owner.GetType().Name));
                report.Add(phase + " SOURCE " + path + "; ownerComponents=" + owners + "; musicMuted=" + musicMutes.ContainsKey(source) +
                    "; clip=" + (source.clip != null ? source.clip.name : "none") + "; playing=" + source.isPlaying + "; enabled=" + source.enabled +
                    "; mute=" + source.mute + "; spatial=" + source.spatialBlend + "; gain=" + source.volume + "; position=" + source.transform.position.ToString("F3") +
                    "; listenerDistance=" + Vector3.Distance(city.Camera.transform.position, source.transform.position) + "; min=" + source.minDistance + "; max=" + source.maxDistance +
                    "; bus=" + (source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "none"));
            }
        }

        private static IEnumerator CaptureCityLocalAcceptance(Func<CityGameRoot, IEnumerator> review)
        {
            float previousDelta = Time.captureDeltaTime;
            CityStreetSurfacePlan previousStreets = localAcceptanceStreets;
            CityLocalAcceptanceState state = null;
            try
            {
                Time.captureDeltaTime = .1f;
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized && load.isDone, Is.True, "Local City acceptance did not finish composition.");
                state = new CityLocalAcceptanceState(city);
                localAcceptanceStreets = CityStreetSurfacePlanner.Create(city.Layout);
                Physics.SyncTransforms();
                yield return review(city);
            }
            finally
            {
                try { state?.Dispose(); }
                finally
                {
                    localAcceptanceStreets = previousStreets;
                    Time.captureDeltaTime = previousDelta;
                    GameSessionState.BeginNewGame();
                }
            }
        }

        private static IEnumerator ReviewDomesticCourts(CityGameRoot city)
        {
            foreach (CityDistrictPointOfInterestKind kind in new[] {
                CityDistrictPointOfInterestKind.OldTownWaterworksCourt,
                CityDistrictPointOfInterestKind.ResidentialDryingYard })
            {
                CityDistrictPointOfInterestDescriptor poi = city.Layout.DistrictPointsOfInterest.Single(p => p.Kind == kind);
                Transform site = city.World.Root.transform.Find(CityDistrictPointOfInterestWorldBuilder.CityRootName)
                    .Find(CityDistrictPointOfInterestWorldBuilder.GetSiteName(poi.Id));
                Assert.That(site, Is.Not.Null, poi.Id);
                Transform recipe = site.Find(CityDistrictPointOfInterestWorldBuilder.GetRecipeName(kind));
                Assert.That(recipe, Is.Not.Null, poi.Id);
                Assert.That(poi.Accesses.Count, Is.InRange(1, 4), "Bound this selection to the authored court entrances.");
                for (int index = 0; index < poi.Accesses.Count; index++)
                {
                    CityDistrictPointOfInterestAccessDescriptor access = poi.Accesses[index];
                    Vector3 start = LocalAcceptanceFloor(city, access.Center - access.OutwardNormal * 1.3f, access.Id + " street");
                    Vector3 inside = LocalAcceptanceFloor(city, access.Center + access.OutwardNormal * 2.2f, access.Id + " court");
                    city.Player.Motor.Teleport(start + Vector3.up * PlayerFactory.GroundedRootOffset);
                    yield return null;
                    yield return WalkChurchGardenLeg(city.Player.Motor, inside, access.Id + " into court");
                    yield return WalkChurchGardenLeg(city.Player.Motor, start, access.Id + " back to street");
                    yield return LocalAcceptanceFrame(city, "domestic-" + kind + "-entry-" + index,
                        start + Vector3.up * EyeHeight, inside + Vector3.up * 1.2f);
                }
                if (kind == CityDistrictPointOfInterestKind.OldTownWaterworksCourt)
                {
                    Transform water = recipe.Find("Dark Water");
                    Assert.That(water, Is.Not.Null);
                    Assert.That(water.GetComponent<Renderer>().enabled, Is.True);
                    Assert.That(recipe.Find("Working Lamp"), Is.Not.Null);
                    AssertLocalLoopOwner(city, CitySourceSoundId.WaterworksPipeLoop);
                    Vector3 eye = FindLocalDetailEye(city, water.position, recipe.forward);
                    yield return LocalAcceptanceFrame(city, "domestic-waterworks-basin-pipes-repair", eye, water.position + Vector3.up * 1.2f, 62f);
                    // Stand close enough to see down into the basin from
                    // the ordinary eye; lowering the camera hides it behind the rim.
                    Vector3 waterEye = FindLocalDetailEye(city, water.position, recipe.forward, 1.55f);
                    Bounds waterBounds = water.GetComponent<Renderer>().bounds;
                    yield return LocalAcceptanceFrame(city, "domestic-waterworks-water-and-bucket-edge", waterEye,
                        new Vector3(waterBounds.center.x, waterBounds.max.y, waterBounds.center.z),
                        city.Camera.GetComponent<PlayerCameraFollow>().FollowFieldOfView);
                }
            }

            Assert.That(city.DryingYardBabushkas.Count, Is.EqualTo(3));
            DryingYardBabushkaPlan plan = DryingYardBabushkaPlan.Create(city.Layout);
            var originalCulling = new List<KeyValuePair<Animator, AnimatorCullingMode>>();
            try
            {
                foreach (DryingYardBabushkaPresentation woman in city.DryingYardBabushkas)
                {
                    Animator animator = woman.GetComponent<CityPedestrianAssetRegistry>().Animator;
                    originalCulling.Add(new KeyValuePair<Animator, AnimatorCullingMode>(animator, animator.cullingMode));
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                int[] strikes = city.DryingYardBabushkas.Select(w => w.StrikeCount).ToArray();
                var captured = new bool[3];
                int smokerIndex = Enumerable.Range(0, 3).Single(i => plan.Stances[i].Strolls);
                DryingYardBabushkaStance stroll = plan.Stances[smokerIndex];
                Vector3 axis = stroll.PathEnd - stroll.Position;
                float length = axis.magnitude;
                Assert.That(length, Is.GreaterThan(.1f));
                axis /= length;
                float minAlong = float.PositiveInfinity, maxAlong = float.NegativeInfinity;
                // Observe the live LateUpdate owners; no reinitialization or timestamp seek.
                int maximumFrames = Mathf.CeilToInt((2f * length / stroll.MoveSpeed + 3f) / .1f);
                Assert.That(maximumFrames, Is.LessThanOrEqualTo(450), "The authored stroll must fit this focused observation.");
                for (int frame = 0; frame < maximumFrames; frame++)
                {
                    yield return null;
                    for (int index = 0; index < 3; index++)
                    {
                        DryingYardBabushkaPresentation woman = city.DryingYardBabushkas[index];
                        Assert.That(woman.IsInitialized, Is.True);
                        CityPedestrianAssetRegistry registry = woman.GetComponent<CityPedestrianAssetRegistry>();
                        Transform socket = CityPedestrianHandProps.FindSocket(registry.ModelRoot,
                            woman.Role == DryingYardBabushkaRole.CarpetBeater ? CityPedestrianHandPropId.CarpetBeater : CityPedestrianHandPropId.Cigarette);
                        Assert.That(woman.HeldProp, Is.Not.Null);
                        Assert.That(woman.HeldProp.transform.parent, Is.SameAs(socket));
                        if (frame % 10 == 0)
                        {
                            Vector3 floor = LocalAcceptanceFloor(city, woman.transform.position, "drying woman " + index);
                            Assert.That(woman.transform.position.y, Is.EqualTo(floor.y).Within(.08f), "The living woman must remain on the court ground.");
                        }
                        if (woman.Role == DryingYardBabushkaRole.CarpetBeater)
                        {
                            Assert.That(woman.Carpet, Is.Not.Null);
                            // A previous screenshot may have spanned a strike. Wait for an
                            // actual live pulse rather than interpreting its old counter as contact.
                            if (woman.StrikeCount > strikes[index] && !captured[index] && woman.Carpet.externalAcceleration.sqrMagnitude > 0f)
                            {
                                Bounds carpet = woman.Carpet.GetComponent<Renderer>().bounds;
                                Vector3 paddle = woman.HeldProp.transform.position;
                                Vector3 aim = (paddle + carpet.ClosestPoint(paddle)) * .5f;
                                // Look along the cloth edge from the beater's
                                // side, oblique enough to clear the rack post.
                                Vector3 profile = woman.transform.right - woman.transform.forward * .45f;
                                Vector3 eye = FindLocalDetailEye(city, aim, profile, 2.5f);
                                PoseLocalAcceptanceCamera(city, eye, aim,
                                    city.Camera.GetComponent<PlayerCameraFollow>().FollowFieldOfView);
                                CaptureCurrentCamera(city.Camera, SceneIds.City, "domestic-drying-beater-" + index + "-side-strike");
                                captured[index] = true;
                            }
                        }
                    }
                    Vector3 smoker = city.DryingYardBabushkas[smokerIndex].transform.position;
                    float along = Vector3.Dot(smoker - stroll.Position, axis);
                    minAlong = Mathf.Min(minAlong, along); maxAlong = Mathf.Max(maxAlong, along);
                    if (!captured[smokerIndex] && along > length * .8f)
                    {
                        Vector3 eye = FindLocalDetailEye(city, smoker, -axis);
                        yield return LocalAcceptanceFrame(city, "domestic-drying-smoker-corridor", eye, smoker + Vector3.up * 1.1f, 62f);
                        captured[smokerIndex] = true;
                    }
                }
                Assert.That(captured.All(value => value), Is.True, "Observe both live strikes and the smoker's actual corridor.");
                Assert.That(maxAlong - minAlong, Is.GreaterThan(length * .85f), "The smoker must actually traverse her authored span.");
                AssertLocalLoopOwner(city, CitySourceSoundId.DryingYardClothLoop);
                Debug.Log("DOMESTIC COURTS ACCEPTANCE OK: actual motor enters/exits every authored opening, water/pipe sound has its physical owner, both live strikes reach registered Cloth and the smoking woman crosses her corridor. Review strike frames for paddle/textile geometry; an event is not a measured surface contact.");
            }
            finally
            {
                foreach (var original in originalCulling) if (original.Key != null) original.Key.cullingMode = original.Value;
            }
        }

        private static IEnumerator ReviewBarYardCircuit(CityGameRoot city)
        {
            YardWheelchairActor actor = city.YardWheelchair;
            Assert.That(actor != null && actor.IsInitialized, Is.True);
            CityWheelchairNpcAssetRegistry registry = actor.GetComponentInChildren<CityWheelchairNpcAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Animator animator = registry.PedestrianRegistry.Animator;
            AnimatorCullingMode oldCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            try
            {
                var tyres = new[] { "ACC_WheelTyre.L", "ACC_WheelTyre.R" }.Select(name =>
                    actor.GetComponentsInChildren<MeshFilter>(true).Single(mesh => mesh.name == name)).ToArray();
                Vector3[][] vertices = tyres.Select(mesh => ReadLocalAcceptanceVertices(mesh.sharedMesh)).ToArray();
                float start = actor.Distance;
                Quaternion originalWheel = registry.LeftWheelPivot.localRotation;
                bool wheelTurned = false;
                int captured = 0;
                for (int frame = 0; frame < 900 && actor.Distance - start < actor.Plan.LapLength; frame++)
                {
                    yield return null;
                    wheelTurned |= Quaternion.Angle(originalWheel, registry.LeftWheelPivot.localRotation) > 5f;
                    if (frame % 5 == 0)
                    {
                        for (int tyre = 0; tyre < tyres.Length; tyre++)
                        {
                            Vector3 bottom = new Vector3(0f, float.PositiveInfinity, 0f);
                            foreach (Vector3 vertex in vertices[tyre])
                            {
                                Vector3 world = tyres[tyre].transform.TransformPoint(vertex);
                                if (world.y < bottom.y) bottom = world;
                            }
                            Vector3 floor = LocalAcceptanceFloor(city, bottom, "yard tyre " + tyre, false);
                            Assert.That(bottom.y - floor.y, Is.InRange(-.03f, .06f),
                                "The actual rotating tyre must stay against the circuit ground: tyre=" + tyre + ", bottom=" + bottom + ", floor=" + floor);
                        }
                        AssertLocalClearColumn(city, actor.transform.position, .36f, "wheelchair circuit");
                    }
                    if (captured < 4 && actor.Distance - start >= captured * actor.Plan.LapLength / 4f)
                    {
                        Vector3 eye = FindLocalDetailEye(city, actor.transform.position, actor.transform.right);
                        PoseLocalAcceptanceCamera(city, eye, actor.transform.position + Vector3.up * .95f, 68f);
                        CaptureCurrentCamera(city.Camera, SceneIds.City, "bar-yard-live-quarter-" + captured);
                        captured++;
                    }
                }
                Assert.That(actor.Distance - start, Is.GreaterThanOrEqualTo(actor.Plan.LapLength), "The existing live actor must finish a complete lap.");
                Assert.That(wheelTurned, Is.True, "Actual wheel geometry must rotate during the lap.");
                Assert.That(captured, Is.EqualTo(4));
                Debug.Log("BAR YARD CIRCUIT ACCEPTANCE OK: one full live lap, actual tyre vertices against physical ground, rotating wheel pivots and clear central body column. Four views retain the tree, yard furniture and new facades for inspection.");
            }
            finally { if (animator != null) animator.cullingMode = oldCulling; }
        }

        private static IEnumerator ReviewLandmarksFromStreets(CityGameRoot city)
        {
            CityDecorationKind[] kinds = { CityDecorationKind.OldTownClockTower, CityDecorationKind.ResidentialRooftopGreenhouse,
                CityDecorationKind.NightlifeCinema, CityDecorationKind.IndustrialGantry };
            var grayOwner = new GameObject("Local Landmark Grayscale Review");
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                profile.Add<ColorAdjustments>(true).saturation.Override(-100f);
                Volume gray = grayOwner.AddComponent<Volume>();
                gray.isGlobal = true; gray.priority = 10000f; gray.sharedProfile = profile; gray.weight = 0f;
                float lens = city.Camera.GetComponent<PlayerCameraFollow>().FollowFieldOfView;
                foreach (CityDecorationKind kind in kinds)
                {
                    CityDecorationDescriptor landmark = city.World.DecorationPlan.Descriptors.Single(p => p.Kind == kind);
                    Assert.That(landmark.TryResolveLot(city.Layout, out BuildingLot lot), Is.True);
                    Assert.That(lot.HasRoadFrontage, Is.True, kind + " must be reviewed from its public street.");
                    Vector3 street = LocalAcceptanceFloor(city, lot.ReturnPosition, kind + " public street");
                    Vector3 arrival = LocalAcceptanceFloor(city, lot.SidewalkArrivalPosition, kind + " sidewalk");
                    city.Player.Motor.Teleport(arrival + Vector3.up * PlayerFactory.GroundedRootOffset);
                    yield return null;
                    yield return WalkChurchGardenLeg(city.Player.Motor, street, kind + " sidewalk to street");
                    if (kind == CityDecorationKind.OldTownClockTower)
                        yield return LocalAcceptanceFrame(city, "landmark-clock-old-near-context", street + Vector3.up * EyeHeight,
                            landmark.Position + Vector3.up * 2f, lens);
                    var view = FindVisibleStreetLandmarkView(city, landmark, lens);
                    Vector3 eye = view.eye, target = view.aim;
                    city.Player.Motor.Teleport(eye - Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset));
                    city.Night.Atmosphere.RefreshImmediate();
                    yield return LocalAcceptanceFrame(city, "landmark-" + kind + "-street-colour", eye, target, lens, view.diagnostic);
                    gray.weight = 1f;
                    yield return LocalAcceptanceFrame(city, "landmark-" + kind + "-street-grayscale", eye, target, lens);
                    gray.weight = 0f;
                }
                Debug.Log("LANDMARK STREET VIEWS READY: four actual public road/sidewalk approaches, ordinary lens, original fog/effects and paired colour/grayscale. Recognition and occlusion remain a visual review, not an automated assertion.");
            }
            finally { Object.DestroyImmediate(grayOwner); Object.DestroyImmediate(profile); }
        }

        private static (Vector3 eye, Vector3 aim, Action diagnostic) FindVisibleStreetLandmarkView(
            CityGameRoot city, CityDecorationDescriptor landmark, float lens)
        {
            Assert.That(landmark.TryResolveLot(city.Layout, out BuildingLot lot), Is.True);
            CityDecorationWorldBuilder.GetDecorationFrame(city.Layout, landmark, out Vector3 origin, out _, out Vector3 forward);
            float width = Mathf.Abs(forward.x) > .5f ? lot.Size.y : lot.Size.x;
            float depth = Mathf.Abs(forward.x) > .5f ? lot.Size.x : lot.Size.y;
            CityMiscKind kind;
            int variant = 0;
            Vector3 scale;
            // These are the four existing imported placement contracts. Match
            // their source vertices to the actual batches rather than admitting
            // every unrelated balcony inside the old recipe's enclosing AABB.
            switch (landmark.Kind)
            {
                case CityDecorationKind.OldTownClockTower:
                    kind = CityMiscKind.OldTownClockTower;
                    float tower = Mathf.Clamp(width * .28f, 2.8f, 4f) / 4f;
                    scale = new Vector3(tower, 1f, tower); break;
                case CityDecorationKind.ResidentialRooftopGreenhouse:
                    kind = CityMiscKind.ResidentialRooftopGreenhouse;
                    scale = new Vector3(Mathf.Clamp(width * .46f, 3.4f, 5.4f) / 5.4f, 1f,
                        Mathf.Clamp(depth * .34f, 2.6f, 4f) / 4f); break;
                case CityDecorationKind.NightlifeCinema:
                    kind = CityMiscKind.NightlifeCinema; variant = landmark.Variant & 1;
                    scale = new Vector3(Mathf.Clamp(width * .72f, 6f, 9.5f) / 9.5f, 1f, 1f); break;
                case CityDecorationKind.IndustrialGantry:
                    kind = CityMiscKind.IndustrialGantry; variant = landmark.Variant & 1;
                    scale = new Vector3(Mathf.Clamp(width * .60f, 6f, 9f) / 9f, 1f,
                        Mathf.Clamp(depth * .42f, 3.2f, 5f) / 5f); break;
                default: throw new ArgumentOutOfRangeException(nameof(landmark));
            }
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            var expected = new Dictionary<Vector3Int, List<Vector3>>();
            Vector3Int Key(Vector3 point) => new Vector3Int(Mathf.FloorToInt(point.x * 100f),
                Mathf.FloorToInt(point.y * 100f), Mathf.FloorToInt(point.z * 100f));
            Bounds envelope = default;
            bool initialized = false;
            var featureVertices = new List<Vector3>();
            for (int part = 0; part < CityMiscAssetProvider.GetPartCount(kind); part++)
            {
                CityMiscMeshPart source = provider.GetPartOrThrow(kind, variant, part);
                bool feature = kind == CityMiscKind.OldTownClockTower && source.Role == CityMiscMeshRole.Residential ||
                    kind == CityMiscKind.ResidentialRooftopGreenhouse && source.Component == "Frame_Residential";
                foreach (Vector3 local in ReadLocalAcceptanceVertices(source.Mesh))
                {
                    Vector3 point = origin + rotation * Vector3.Scale(local, scale);
                    if (feature) featureVertices.Add(point);
                    if (!initialized) { envelope = new Bounds(point, Vector3.zero); initialized = true; }
                    else envelope.Encapsulate(point);
                    Vector3Int key = Key(point);
                    if (!expected.TryGetValue(key, out List<Vector3> bucket)) expected.Add(key, bucket = new List<Vector3>());
                    bucket.Add(point);
                }
            }
            envelope.Expand(.01f);
            var matchedSource = new HashSet<Vector3Int>();
            bool IsSourceVertex(Vector3 point)
            {
                Vector3Int key = Key(point);
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (expected.TryGetValue(key + new Vector3Int(x, y, z), out List<Vector3> bucket) &&
                        bucket.Any(value => (value - point).sqrMagnitude < .000004f))
                    { matchedSource.Add(key + new Vector3Int(x, y, z)); return true; }
                return false;
            }
            var geometry = new List<(Vector3 point, Transform chunk)>();
            foreach (MeshFilter filter in city.World.DecorationRoot.GetComponentsInChildren<MeshFilter>())
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (!filter.name.StartsWith("Imported ", StringComparison.Ordinal) || renderer == null || !renderer.enabled ||
                    !renderer.bounds.Intersects(envelope)) continue;
                foreach (Vector3 local in ReadLocalAcceptanceVertices(filter.sharedMesh))
                {
                    Vector3 point = filter.transform.TransformPoint(local);
                    if (envelope.Contains(point) && IsSourceVertex(point)) geometry.Add((point, filter.transform.parent));
                }
            }
            Assert.That(geometry.Count, Is.GreaterThan(10), landmark.Kind + " lost its exact rendered source geometry.");
            Assert.That(matchedSource.Count, Is.GreaterThanOrEqualTo(expected.Count * .98f),
                landmark.Kind + " must match the whole exact imported source, not one coincident nearby part.");
            Bounds form = new Bounds(geometry[0].point, Vector3.zero);
            foreach (var vertex in geometry) form.Encapsulate(vertex.point);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float formWidth = Vector3.Dot(new Vector3(Mathf.Abs(right.x), 0f, Mathf.Abs(right.z)), form.size);
            float formDepth = Vector3.Dot(new Vector3(Mathf.Abs(forward.x), 0f, Mathf.Abs(forward.z)), form.size);
            Vector3 core = new Vector3(form.center.x, form.min.y + form.size.y * .62f, form.center.z) + forward * (formDepth * .4f);
            Vector3[] desiredSamples = { core, core - right * formWidth * .28f, core + right * formWidth * .28f,
                core + Vector3.up * form.size.y * .16f, core - Vector3.up * form.size.y * .16f };
            if (kind == CityMiscKind.NightlifeCinema)
            {
                // The cinema's upper signature is its single vertical mast,
                // not five points scattered over the empty facade beside it.
                // This assembly's declared legacy_recipe_reflected_x profile
                // already reflects recipe X in its imported local geometry.
                float mastX = (variant == 0 ? -1f : 1f) * 9.5f * .36f;
                desiredSamples = new[] { new Vector3(mastX, 4.55f, .305f), new Vector3(mastX, 6f, .305f),
                    new Vector3(mastX, 7.45f, .305f), new Vector3(mastX, 8.8f, .26f),
                    new Vector3(-Mathf.Sign(mastX) * 9.5f * .44f, 3.14f, 1.42f) }
                    .Select(local => origin + rotation * Vector3.Scale(local, scale)).ToArray();
            }
            var samples = new List<(Vector3 point, Transform chunk)>();
            foreach (Vector3 desired in desiredSamples)
            {
                var sample = geometry.Where(vertex => samples.All(prior => (prior.point - vertex.point).sqrMagnitude > .0025f))
                    .OrderBy(vertex => (vertex.point - desired).sqrMagnitude).First();
                if (kind == CityMiscKind.NightlifeCinema)
                    Assert.That(Vector3.Distance(sample.point, desired), Is.LessThan(.65f),
                        "The cinema target must actually sample its named mast/canopy feature.");
                samples.Add(sample);
            }
            Vector3 aim = samples.Aggregate(Vector3.zero, (sum, sample) => sum + sample.point) / samples.Count;
            CityBuildingAssetRegistry[] buildingModels = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>();
            Renderer[] opaqueBuildings = CaptureOpaqueWorldMeshes(city);
            var featureGeometry = geometry.Where(vertex => featureVertices.Any(source =>
                (source - vertex.point).sqrMagnitude < .000004f)).ToArray();
            List<(Vector3 point, Transform chunk)> FunctionalSamples(Vector3 eye)
            {
                if (kind != CityMiscKind.OldTownClockTower && kind != CityMiscKind.ResidentialRooftopGreenhouse) return samples;
                Assert.That(featureGeometry, Is.Not.Empty, "Inspect the actual dial/frame, not its roof or antenna.");
                var result = new List<(Vector3 point, Transform chunk)>(4);
                if (kind == CityMiscKind.OldTownClockTower)
                {
                    Vector3 localEye = Quaternion.Inverse(rotation) * (eye - origin);
                    // The authored hands exist only on the two Z faces.
                    Vector3 normal = Vector3.forward * Mathf.Sign(localEye.z);
                    Vector3 across = Vector3.Cross(Vector3.up, normal);
                    Vector3 Local(Vector3 world)
                    {
                        Vector3 local = Quaternion.Inverse(rotation) * (world - origin);
                        return new Vector3(local.x / scale.x, local.y, local.z / scale.z);
                    }
                    var dial = featureGeometry.Where(vertex => Vector3.Dot(Local(vertex.point), normal) > 1.72f).ToArray();
                    Assert.That(dial.Length, Is.GreaterThanOrEqualTo(4), "The facing clock dial must exist in rendered geometry.");
                    foreach (float x in new[] { -.3f, .3f }) foreach (float y in new[] { -.3f, .3f })
                    {
                        Vector3 desired = origin + rotation * Vector3.Scale(normal * 1.74f + across * x + Vector3.up * (4.36f + y), scale);
                        result.Add(dial.Where(vertex => result.All(prior => (prior.point - vertex.point).sqrMagnitude > .0025f))
                            .OrderBy(vertex => (vertex.point - desired).sqrMagnitude).First());
                    }
                }
                else
                {
                    // Body samples lie on the actual two near vertical frame
                    // segments. Roof/antenna visibility alone cannot pass.
                    var posts = new List<Vector3>(4);
                    foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
                        posts.Add(origin + rotation * Vector3.Scale(new Vector3(x * 5.4f * .47f, .24f, z * 4f * .47f), scale));
                    foreach (Vector3 post in posts.OrderBy(point => Vector3.ProjectOnPlane(point - eye, Vector3.up).sqrMagnitude).Take(2))
                    {
                        var bottom = featureGeometry.Where(vertex => Mathf.Abs(vertex.point.y - post.y) < .03f)
                            .OrderBy(vertex => Vector3.ProjectOnPlane(vertex.point - post, Vector3.up).sqrMagnitude).First();
                        Assert.That(Vector3.ProjectOnPlane(bottom.point - post, Vector3.up).magnitude, Is.LessThan(.08f));
                        foreach (float height in new[] { 1.1f, 1.7f })
                            result.Add((bottom.point + Vector3.up * (height - .24f), bottom.chunk));
                    }
                }
                return result;
            }
            // Logical Building Mass is a gameplay envelope, not drawn geometry.
            // In residential prototype01 it reaches frontZ=5.801 while the
            // authored facade is at 4.601: the cinema sits in that 1.2 m void.
            // Only exact sibling proxies of a registered rendered model qualify;
            // all its actual opaque facade/roof triangles still test occlusion.
            var visualMassProxies = new HashSet<Collider>();
            foreach (CityBuildingAssetRegistry building in buildingModels)
                foreach (Transform child in building.transform.parent)
                {
                    string mass = CityBuildingPrototypeWorldBuilder.LogicalCollisionObjectName;
                    if (child.name != mass && !child.name.StartsWith(mass + " ", StringComparison.Ordinal)) continue;
                    Assert.That(child.GetComponent<Renderer>(), Is.Null, "Only collision-only Building Mass may defer to its model mesh.");
                    Assert.That(child.GetComponent<MeshFilter>(), Is.Null);
                    foreach (Collider collider in child.GetComponents<Collider>()) visualMassProxies.Add(collider);
                }
            var roofGeometry = new Dictionary<MeshFilter, (Vector3[] vertices, int[] indices)>();
            var candidates = new HashSet<Vector2>();
            Vector2 center = new Vector2(form.center.x, form.center.z);
            foreach (RoadEdge edge in city.Layout.RoadEdges)
            {
                CityRoadPath path = city.Layout.RoadGeometry.Get(edge);
                CityRoadProjection nearest = path.Project(center);
                if (nearest.DistanceSquared > 36f * 36f) continue;
                for (int along = -24; along <= 24; along += 6)
                    for (int side = -1; side <= 1; side++)
                    {
                        CityRoadSample sample = path.SampleDistance(nearest.DistanceAlong + along);
                        Vector2 point = sample.Position + sample.Right * (side * localAcceptanceStreets.CarriagewayWidth * .28f);
                        float distance = Vector2.Distance(point, center);
                        if (distance >= 9f && distance <= 34f) candidates.Add(point);
                    }
            }
            bool IsActor(Collider collider) => collider.transform.IsChildOf(city.Player.GameObject.transform) ||
                collider.GetComponentInParent<CityPedestrianActor>() != null || collider.GetComponentInParent<DefaultNpcAppearance>() != null;
            int bestVisible = 0;
            float bestScore = float.PositiveInfinity;
            Vector3 bestEye = default, bestAim = aim;
            Vector3[] bestFeatures = Array.Empty<Vector3>();
            int chosenVisible = 0, chosenSampleCount = samples.Count;
            bool verifiedMeshReader = false;
            string bestBlockers = string.Empty;
            var diverse = candidates.GroupBy(point => Mathf.FloorToInt((Mathf.Atan2(point.y - center.y, point.x - center.x) +
                Mathf.PI) / (Mathf.PI * .25f))).OrderBy(group => group.Key)
                .SelectMany(group => group.OrderBy(point => Mathf.Abs(Vector2.Distance(point, center) - 21f))
                    .ThenBy(point => point.x).ThenBy(point => point.y).Take(8)).Take(64);
            foreach (Vector2 point in diverse)
            {
                if (!TryReplanningSurfaceTop(city.Layout, localAcceptanceStreets, point, out float top)) continue;
                Vector3 foot = new Vector3(point.x, top, point.y);
                if (!city.World.WalkableArea.Contains(foot, .36f)) continue;
                RaycastHit[] floors = Physics.RaycastAll(foot + Vector3.up * 2f, Vector3.down, 4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.normal.y > .7f && Mathf.Abs(hit.point.y - top) < .4f && !IsActor(hit.collider))
                    .OrderBy(hit => Mathf.Abs(hit.point.y - top)).ToArray();
                if (floors.Length == 0) continue;
                foot = floors[0].point;
                if (Physics.OverlapCapsule(foot + Vector3.up * .7f, foot + Vector3.up * 1.45f, .36f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(collider => !IsActor(collider))) continue;
                Vector3 eye = foot + Vector3.up * EyeHeight;
                if (Vector3.Distance(eye, form.center) > 36f ||
                    kind == CityMiscKind.NightlifeCinema && Vector3.Dot(eye - origin, forward) < 3f) continue;
                if (kind == CityMiscKind.OldTownClockTower)
                {
                    Vector3 localEye = Quaternion.Inverse(rotation) * (eye - origin);
                    if (Mathf.Abs(localEye.z) < Mathf.Abs(localEye.x)) continue;
                }
                List<(Vector3 point, Transform chunk)> poseSamples = FunctionalSamples(eye);
                Vector3 poseAim = poseSamples.Aggregate(Vector3.zero, (sum, sample) => sum + sample.point) / poseSamples.Count;
                if (!verifiedMeshReader)
                {
                    CityBuildingAssetRegistry host = buildingModels.Single(building => building.transform.parent.name ==
                        "Building " + landmark.LotCell.x + "-" + landmark.LotCell.y);
                    Assert.That(host.TryGetRenderer(CityBuildingMeshRole.FacadePrimary, out Renderer wall), Is.True);
                    MeshFilter wallFilter = wall.GetComponent<MeshFilter>();
                    Assert.That(wallFilter, Is.Not.Null);
                    Vector3 probe = wall.bounds.center - eye;
                    // Populate the same read-only triangle cache, then aim at a
                    // real face centroid; a window opening is not a broken reader.
                    CaptureOpaqueMeshBlocks(wall, new Ray(eye, probe.normalized),
                        probe.magnitude + wall.bounds.size.magnitude, roofGeometry);
                    Assert.That(roofGeometry.TryGetValue(wallFilter, out var wallMesh), Is.True);
                    bool witness = false;
                    for (int index = 0; index < wallMesh.indices.Length; index += 3)
                    {
                        Vector3 a = wallMesh.vertices[wallMesh.indices[index]], b = wallMesh.vertices[wallMesh.indices[index + 1]],
                            c = wallMesh.vertices[wallMesh.indices[index + 2]], centerOfFace = (a + b + c) / 3f;
                        Vector3 normal = Vector3.Cross(b - a, c - a);
                        if (normal.sqrMagnitude < .000001f || Mathf.Abs(Vector3.Dot(normal.normalized, eye - centerOfFace)) < .001f) continue;
                        Vector3 delta = centerOfFace - eye;
                        witness = CaptureOpaqueMeshBlocks(wall, new Ray(eye, delta.normalized), delta.magnitude + .1f, roofGeometry);
                        break;
                    }
                    Assert.That(opaqueBuildings.Contains(wall) && witness, Is.True,
                        "The visual mesh reader must positively detect a real facade triangle before claiming clear landmark rays.");
                    verifiedMeshReader = true;
                }
                Vector3 facing = (poseAim - eye).normalized, across = Vector3.Cross(Vector3.up, facing).normalized;
                Vector3 vertical = Vector3.Cross(facing, across);
                float half = Mathf.Tan(lens * .5f * Mathf.Deg2Rad), aspect = Mathf.Min(city.Camera.aspect, 4f / 3f);
                bool Fits(Vector3 target)
                {
                    Vector3 delta = target - eye;
                    float reach = Vector3.Dot(delta, facing);
                    return reach > .1f && Mathf.Abs(Vector3.Dot(delta, across)) < reach * half * aspect * .9f &&
                        Mathf.Abs(Vector3.Dot(delta, vertical)) < reach * half * .9f;
                }
                bool framed = true;
                for (int corner = 0; corner < 8; corner++)
                    framed &= Fits(form.center + Vector3.Scale(form.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                if (!framed) continue;
                int visible = 0;
                var blockers = new List<string>(5);
                foreach (var sample in poseSamples)
                {
                    Vector3 delta = sample.point - eye;
                    Ray ray = new Ray(eye, delta.normalized);
                    RaycastHit[] contacts = Physics.RaycastAll(ray, delta.magnitude - .02f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore).Where(hit => !IsActor(hit.collider) &&
                            !visualMassProxies.Contains(hit.collider)).OrderBy(hit => hit.distance).ToArray();
                    Renderer blockedMesh = opaqueBuildings.FirstOrDefault(renderer => CaptureOpaqueMeshBlocks(renderer, ray, delta.magnitude, roofGeometry));
                    if (contacts.Length != 0 || blockedMesh != null)
                    {
                        blockers.Add(sample.point + " by collider=" + (contacts.Length != 0 ? contacts[0].collider.name : "<clear>") +
                            ", actual opaque mesh=" + (blockedMesh != null ? blockedMesh.name : "<clear>"));
                        continue;
                    }
                    visible++;
                }
                if (visible > bestVisible || string.IsNullOrEmpty(bestBlockers)) { bestVisible = visible; bestBlockers = " eye=" + eye + "; " + string.Join("; ", blockers); }
                int required = kind == CityMiscKind.OldTownClockTower || kind == CityMiscKind.ResidentialRooftopGreenhouse ? 4 : 3;
                if (visible < required) continue;
                // Compare all bounded candidates. The first passing sector was
                // sometimes a distant, barely readable view through native fog.
                float score = Vector3.Distance(eye, form.center) + (poseSamples.Count - visible) * 2f;
                if (score < bestScore)
                {
                    bestScore = score; bestEye = eye; bestAim = poseAim;
                    bestFeatures = poseSamples.Select(sample => sample.point).ToArray();
                    chosenVisible = visible; chosenSampleCount = poseSamples.Count;
                }
            }
            if (!float.IsPositiveInfinity(bestScore))
            {
                Debug.Log("LANDMARK VISIBLE STREET: " + landmark.Kind + ", exact source form=" + form +
                    ", chosen visible upper samples=" + chosenVisible + "/" + chosenSampleCount + ", distance=" + Vector3.Distance(bestEye, form.center).ToString("F2") + ", eye=" + bestEye);
                Action diagnostic = null;
                if (kind == CityMiscKind.OldTownClockTower || kind == CityMiscKind.ResidentialRooftopGreenhouse)
                {
                    CityBuildingAssetRegistry host = buildingModels.Single(building => building.transform.parent.name ==
                        "Building " + landmark.LotCell.x + "-" + landmark.LotCell.y);
                    diagnostic = () =>
                    {
                        Renderer[] currentOpaque = CaptureOpaqueWorldMeshes(city);
                        Debug.Log("LANDMARK RENDER FEATURES: " + landmark.Kind + ", aim=" + bestAim.ToString("F3") +
                            ", samples=" + string.Join("; ", bestFeatures.Select(point => point.ToString("F3") +
                                " viewport=" + city.Camera.WorldToViewportPoint(point).ToString("F3"))));
                        foreach (MeshRenderer renderer in host.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            MeshFilter filter = renderer.GetComponent<MeshFilter>();
                            if (filter == null || filter.sharedMesh == null) continue;
                            var rays = new List<string>(bestFeatures.Length);
                            foreach (Vector3 feature in bestFeatures)
                            {
                                Vector3 delta = feature - city.Camera.transform.position;
                                Ray ray = new Ray(city.Camera.transform.position, delta.normalized);
                                bool broad = renderer.bounds.IntersectRay(ray, out float entry) && entry < delta.magnitude - .05f;
                                bool guarded = CaptureOpaqueMeshBlocks(renderer, ray, delta.magnitude, roofGeometry);
                                bool direct = CaptureOpaqueMeshBlocks(renderer, ray, delta.magnitude, roofGeometry, false);
                                rays.Add("bounds=" + broad + "/guarded=" + guarded + "/direct=" + direct);
                            }
                            var mesh = roofGeometry[filter];
                            Bounds actualBounds = new Bounds(mesh.vertices[0], Vector3.zero);
                            foreach (Vector3 vertex in mesh.vertices) actualBounds.Encapsulate(vertex);
                            Debug.Log("LANDMARK HOST MESH: " + landmark.Kind + ", mesh=" + renderer.name +
                                ", snapshot=" + opaqueBuildings.Contains(renderer) + ", current=" + currentOpaque.Contains(renderer) +
                                ", enabled=" + renderer.enabled + ", active=" + renderer.gameObject.activeInHierarchy +
                                ", forceOff=" + renderer.forceRenderingOff + ", cameraLayer=" +
                                ((city.Camera.cullingMask & (1 << renderer.gameObject.layer)) != 0) +
                                ", material=" + string.Join(",", renderer.sharedMaterials.Select(material => material == null ? "<null>" :
                                    material.name + ":" + material.renderQueue + ":transparent=" + material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))) +
                                ", renderer bounds=" + renderer.bounds + ", triangle bounds=" + actualBounds +
                                ", rays=" + string.Join("; ", rays));
                        }
                    };
                }
                return (bestEye, bestAim, diagnostic);
            }
            Assert.Fail(landmark.Kind + " has no framed, capsule-clear street view within the native fog range; best exact-source upper samples=" +
                bestVisible + "/5; source form=" + form + bestBlockers + ". Do not accept the old under-eave frame.");
            return default;
        }

        private static Renderer[] CaptureOpaqueWorldMeshes(CityGameRoot city) => city.World.Root.GetComponentsInChildren<MeshRenderer>()
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy &&
                renderer.GetComponentInParent<PlayerMotor>() == null && renderer.GetComponentInParent<DefaultNpcAppearance>() == null &&
                renderer.GetComponentInParent<CityPedestrianActor>() == null && renderer.sharedMaterials.Any(material =>
                    material != null && material.renderQueue < 2500 && !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")))
            .Cast<Renderer>().ToArray();

        private static bool CaptureOpaqueMeshBlocks(Renderer roof, Ray ray, float reach,
            IDictionary<MeshFilter, (Vector3[] vertices, int[] indices)> cache, bool checkBounds = true)
        {
            if (checkBounds && (!roof.bounds.IntersectRay(ray, out float entry) || entry >= reach - .05f)) return false;
            MeshFilter filter = roof.GetComponent<MeshFilter>();
            if (filter == null) return false;
            if (!cache.TryGetValue(filter, out var geometry))
            {
                Vector3[] vertices = ReadLocalAcceptanceVertices(filter.sharedMesh);
                for (int index = 0; index < vertices.Length; index++) vertices[index] = filter.transform.TransformPoint(vertices[index]);
                int[] indices;
#if UNITY_EDITOR
                using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                {
                    var all = new List<int>();
                    for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                        using (var triangles = new NativeArray<int>(data[0].GetSubMesh(submesh).indexCount, Allocator.Temp))
                        { data[0].GetIndices(triangles, submesh, true); all.AddRange(triangles.ToArray()); }
                    indices = all.ToArray();
                }
#else
                indices = filter.sharedMesh.triangles;
#endif
                geometry = (vertices, indices); cache.Add(filter, geometry);
            }
            for (int index = 0; index < geometry.indices.Length; index += 3)
            {
                Vector3 a = geometry.vertices[geometry.indices[index]], e1 = geometry.vertices[geometry.indices[index + 1]] - a;
                Vector3 e2 = geometry.vertices[geometry.indices[index + 2]] - a, p = Vector3.Cross(ray.direction, e2);
                float determinant = Vector3.Dot(e1, p);
                if (Mathf.Abs(determinant) < .000001f) continue;
                Vector3 offset = ray.origin - a;
                float u = Vector3.Dot(offset, p) / determinant;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(offset, e1);
                float v = Vector3.Dot(ray.direction, q) / determinant, distance = Vector3.Dot(e2, q) / determinant;
                if (v >= 0f && u + v <= 1f && distance > .03f && distance < reach - .05f) return true;
            }
            return false;
        }

        private static IEnumerator ReviewFringeApproaches(CityGameRoot city)
        {
            var yards = city.World.FringeYardPlan.Yards.Where(yard => yard.Kind != CityFringeYardKind.EastUtilityEdge).ToArray();
            Assert.That(yards.Length, Is.EqualTo(4));
            CharacterController body = city.Player.Motor.GetComponent<CharacterController>();
            foreach (CityFringeYardDescriptor yard in yards)
            {
                Vector3 outward = yard.Access.OutwardNormal;
                Vector3 street = LocalAcceptanceFloor(city, yard.Access.Center - outward * 1.2f, yard.StableId + " street");
                Vector3 toe = yard.Access.Center;
                float inset = body.radius + .15f;
                if (outward.x < -.5f) toe.x = yard.AreaBounds.xMin + inset;
                else if (outward.x > .5f) toe.x = yard.AreaBounds.xMax - inset;
                else if (outward.z < -.5f) toe.z = yard.AreaBounds.yMin + inset;
                else toe.z = yard.AreaBounds.yMax - inset;
                toe = LocalAcceptanceFloor(city, toe, yard.StableId + " toe");
                Vector3 middle = LocalAcceptanceFloor(city, Vector3.Lerp(yard.Access.Center, toe, .5f), yard.StableId + " middle");
                city.Player.Motor.Teleport(street + Vector3.up * PlayerFactory.GroundedRootOffset);
                yield return null;
                yield return WalkChurchGardenLeg(city.Player.Motor, middle, yard.StableId + " actual approach");
                yield return LocalAcceptanceFrame(city, "fringe-" + yard.StableId + "-approach", middle + Vector3.up * EyeHeight, toe + Vector3.up * 1.4f);
                yield return WalkChurchGardenLeg(city.Player.Motor, toe, yard.StableId + " actual toe");
                // Ordinary rock yards stop at their toe. The open tunnel has
                // its own permitted corridor; the forecourt edge is not a gate.
                Vector3 beyondBoundary = toe + outward * 2f;
                CityMountainTunnelDescriptor tunnel = city.World.MountainBoundaryPlan.Tunnel;
                bool isTunnel = yard.Kind == CityFringeYardKind.SouthTunnelForecourt;
                if (isTunnel)
                {
                    Assert.That(tunnel.HasPhysicalGate, Is.False);
                    Assert.That(tunnel.TravelAvailable, Is.False);
                    CityMountainTunnelPathSample inside = tunnel.SamplePath(tunnel.WalkableDepth - body.radius - .15f);
                    RaycastHit[] supports = Physics.RaycastAll(inside.Position + Vector3.up, Vector3.down, 2f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Where(hit => hit.normal.y > .7f &&
                        Mathf.Abs(hit.point.y - inside.Position.y) < .25f &&
                        !hit.collider.transform.IsChildOf(city.Player.GameObject.transform) &&
                        hit.collider.GetComponentInParent<DefaultNpcAppearance>() == null).OrderBy(hit => hit.distance).ToArray();
                    Assert.That(supports, Is.Not.Empty, "The allowed tunnel corridor must have real physical floor.");
                    Vector3 innerFloor = supports[0].point;
                    Assert.That(city.World.WalkableArea.Contains(innerFloor, body.radius), Is.True);
                    AssertLocalClearColumn(city, innerFloor, body.radius, "inside the permitted tunnel corridor");
                    yield return WalkChurchGardenLeg(city.Player.Motor, innerFloor, yard.StableId + " permitted tunnel depth");
                    beyondBoundary = tunnel.SamplePath(tunnel.WalkableDepth + 2f).Position;
                }
                // Input remains borrowed by this fixture, so the independent
                // travel-turnback interaction is not what this walking-mask probe tests.
                for (int frame = 0; frame < 45; frame++)
                {
                    city.Player.Motor.MoveTowardsApproachWaypoint(beyondBoundary, .08f, .1f);
                    yield return null;
                }
                city.Player.Motor.CancelInteractionPoseMove();
                Vector3 stopped = city.Player.Motor.transform.position;
                if (isTunnel)
                {
                    float depth = Vector3.Dot(stopped - tunnel.PortalGroundCenter, tunnel.Axis.normalized);
                    Assert.That(depth, Is.InRange(tunnel.WalkableDepth - body.radius - .2f, tunnel.WalkableDepth + .03f),
                        "Walking must reach and remain inside the actual tunnel walking limit.");
                    Assert.That(city.World.WalkableArea.Contains(stopped, body.radius), Is.True);
                    Assert.That(city.World.WalkableArea.Contains(beyondBoundary, body.radius), Is.False,
                        "The probe target must be beyond the actual permitted corridor.");
                    Debug.Log("FRINGE TUNNEL MASK LIMIT: actual depth=" + depth.ToString("F2") +
                        ", planned walkable depth=" + tunnel.WalkableDepth + "; physical floor retained, travel-turnback not invoked.");
                }
                else
                {
                    Assert.That(Vector3.Dot(stopped - toe, outward), Is.LessThan(.8f),
                        "Walking must not bypass the physical rock toe: " + yard.StableId);
                    Assert.That(Physics.Raycast(stopped + Vector3.up * .85f, outward, out RaycastHit rock, 4f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True,
                        yard.StableId + " must stop beside a physical visible rock edge, not an unexplained empty clamp.");
                    Renderer visible = rock.collider.GetComponent<Renderer>();
                    if (visible == null)
                    {
                        // Ridge visuals and their exact toe-band collider are
                        // siblings, paired by the factory's stable chunk index.
                        Transform ridge = rock.collider.transform.parent;
                        Assert.That(ridge, Is.Not.Null);
                        Assert.That(city.World.MountainBoundaryPlan.Ridges.Any(part => part.StableId == ridge.name), Is.True,
                            "The stopping collider must belong to a planned physical ridge: " + rock.collider.name);
                        Assert.That(rock.collider.name.StartsWith(ridge.name + " Toe Collider ", StringComparison.Ordinal), Is.True);
                        Transform chunk = ridge.Find(rock.collider.name.Replace(" Toe Collider ", " Chunk "));
                        Assert.That(chunk, Is.Not.Null, "The exact toe collider lost its matching visible ridge chunk.");
                        visible = chunk.GetComponent<Renderer>();
                    }
                    Assert.That(visible, Is.Not.Null, "Inspect the actual visible boundary owner.");
                    Assert.That(visible.enabled && visible.gameObject.activeInHierarchy, Is.True);
                    Bounds visibleBounds = visible.bounds;
                    visibleBounds.Expand(.1f);
                    Assert.That(visibleBounds.Contains(rock.point), Is.True, "The physical stopping hit must remain in its matching visible rock chunk.");
                }
                yield return LocalAcceptanceFrame(city, "fringe-" + yard.StableId + "-walking-limit", stopped + Vector3.up * EyeHeight,
                    stopped + outward * 6f + Vector3.up * EyeHeight);
                yield return WalkChurchGardenLeg(city.Player.Motor, middle, yard.StableId + " return from toe");
                yield return WalkChurchGardenLeg(city.Player.Motor, street, yard.StableId + " return to street");
                CityFringeYardPartKind[] details = { CityFringeYardPartKind.MasonCart, CityFringeYardPartKind.RepairStack,
                    CityFringeYardPartKind.Gabion, CityFringeYardPartKind.TunnelCheek, CityFringeYardPartKind.DrainChannel };
                CityFringeYardPartDescriptor[] selected = details.Select(kind => yard.Parts.FirstOrDefault(p => p.Kind == kind))
                    .Where(part => !string.IsNullOrEmpty(part.StableId)).Take(2).ToArray();
                foreach (CityFringeYardPartDescriptor part in selected)
                {
                    Vector3 eye = FindLocalDetailEye(city, part.Center, -outward);
                    yield return LocalAcceptanceFrame(city, "fringe-detail-" + part.StableId, eye, part.Center, 62f);
                }
                Debug.Log("FRINGE ACTUAL MOTOR OK: " + yard.StableId + "; street -> middle -> toe -> stopped boundary -> street.");
            }
        }

        private static Vector3 LocalAcceptanceFloor(CityGameRoot city, Vector3 point, string label, bool requireWalkable = true)
        {
            Assert.That(TryReplanningSurfaceTop(city.Layout, localAcceptanceStreets, new Vector2(point.x, point.z), out float expected),
                Is.True, label + " lacks planned ground.");
            RaycastHit? floor = null;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, expected + 2f, point.z), Vector3.down, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < .7f || hit.collider.transform.IsChildOf(city.Player.GameObject.transform) ||
                    hit.collider.GetComponentInParent<CityPedestrianActor>() != null ||
                    hit.collider.GetComponentInParent<DefaultNpcAppearance>() != null || Mathf.Abs(hit.point.y - expected) > .4f) continue;
                if (!floor.HasValue || Mathf.Abs(hit.point.y - expected) < Mathf.Abs(floor.Value.point.y - expected)) floor = hit;
            }
            Assert.That(floor.HasValue, Is.True, label + " lacks physical ground near the production datum at " + point);
            Vector3 result = floor.Value.point;
            if (requireWalkable)
                Assert.That(city.World.WalkableArea.Contains(result, city.Player.Motor.GetComponent<CharacterController>().radius),
                    Is.True, label + " is outside the actual walking mask.");
            return result;
        }

        private static void AssertLocalClearColumn(CityGameRoot city, Vector3 floor, float radius, string label)
        {
            foreach (Collider obstacle in Physics.OverlapCapsule(floor + Vector3.up * (radius + .34f), floor + Vector3.up * 1.45f,
                radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!obstacle.transform.IsChildOf(city.Player.GameObject.transform) && obstacle.GetComponentInParent<CityPedestrianActor>() == null &&
                    obstacle.GetComponentInParent<DefaultNpcAppearance>() == null)
                    Assert.Fail(label + " body column crosses " + obstacle.name + " at " + floor);
        }

        private static Vector3 FindLocalDetailEye(CityGameRoot city, Vector3 target, Vector3 preferred, float distance = 3.2f)
        {
            // Candidates are supported walking ground, never an arbitrary floating/ridge camera.
            Vector3 forward = Vector3.ProjectOnPlane(preferred, Vector3.up).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.back;
            for (int index = 0; index < 12; index++)
            {
                Vector3 candidate = target + Quaternion.Euler(0f, index * 30f, 0f) * forward * distance;
                if (!city.World.WalkableArea.Contains(candidate, .36f) || !TryReplanningSurfaceTop(city.Layout, localAcceptanceStreets,
                    new Vector2(candidate.x, candidate.z), out float top)) continue;
                Vector3 point = new Vector3(candidate.x, top, candidate.z);
                bool blocked = Physics.OverlapCapsule(point + Vector3.up * .7f, point + Vector3.up * 1.45f, .36f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(obstacle =>
                    !obstacle.transform.IsChildOf(city.Player.GameObject.transform) && obstacle.GetComponentInParent<CityPedestrianActor>() == null &&
                    obstacle.GetComponentInParent<DefaultNpcAppearance>() == null);
                if (blocked) continue;
                return LocalAcceptanceFloor(city, point, "detail eye") + Vector3.up * EyeHeight;
            }
            Assert.Fail("No supported close view around actual detail at " + target + "; add a measured camera rather than accepting an empty frame.");
            return default;
        }

        private static void AssertLocalLoopOwner(CityGameRoot city, CitySourceSoundId cue)
        {
            Assert.That(city.Soundscape, Is.Not.Null);
            IReadOnlyList<AudioSource> voices = city.Soundscape.GetLoopSources();
            for (int index = 0; index < city.SoundscapePlan.LoopingSources.Count; index++)
            {
                var descriptor = city.Soundscape.GetLoopDescriptor(index);
                if (descriptor.Cue != cue) continue;
                AudioSource voice = voices[index];
                Assert.That(voice.clip, Is.Not.Null);
                Assert.That(voice.spatialBlend, Is.EqualTo(1f));
                Assert.That(Vector3.Distance(voice.transform.position, descriptor.WorldPosition), Is.LessThan(.01f));
                Assert.That(descriptor.PhysicalOwnerBounds.Contains(voice.transform.position), Is.True);
                return;
            }
            Assert.Fail("Missing physically owned loop: " + cue);
        }

        private static Vector3[] ReadLocalAcceptanceVertices(Mesh mesh)
        {
#if UNITY_EDITOR
            // The staged NPC FBX deliberately drops its runtime CPU copy.
            // The editor's read-only API inspects it without changing importer policy.
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(positions);
                return positions.ToArray();
            }
#else
            Assert.That(mesh.isReadable, Is.True, "Run this addressed geometry acceptance in the Editor.");
            return mesh.vertices;
#endif
        }

        private static void PoseLocalAcceptanceCamera(CityGameRoot city, Vector3 eye, Vector3 target, float fieldOfView)
        {
            city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            city.Camera.fieldOfView = fieldOfView;
        }

        private static IEnumerator LocalAcceptanceFrame(CityGameRoot city, string name, Vector3 eye, Vector3 target,
            float fieldOfView = 72f, Action diagnostic = null)
        {
            PoseLocalAcceptanceCamera(city, eye, target, fieldOfView);
            for (int frame = 0; frame < SettleFrames; frame++) yield return null;
            if (diagnostic == null) { CaptureCurrentCamera(city.Camera, SceneIds.City, name); yield break; }
            Quaternion expected = Quaternion.LookRotation(target - eye, Vector3.up);
            void VerifyRenderPose(string phase)
            {
                Debug.Log("LANDMARK CAMERA " + phase + ": " + name + ", requested=" + eye.ToString("F3") +
                    ", actual=" + city.Camera.transform.position.ToString("F3") + ", forward=" + city.Camera.transform.forward.ToString("F3") +
                    ", target=" + target.ToString("F3") + ", FOV=" + city.Camera.fieldOfView + ", aspect=" + city.Camera.aspect);
                Assert.That(Vector3.Distance(city.Camera.transform.position, eye), Is.LessThan(.001f), name + " camera moved after SettleFrames.");
                Assert.That(Quaternion.Angle(city.Camera.transform.rotation, expected), Is.LessThan(.01f), name + " camera rotated after SettleFrames.");
                Assert.That(Mathf.Abs(city.Camera.fieldOfView - fieldOfView), Is.LessThan(.001f), name + " FOV changed after SettleFrames.");
                diagnostic();
            }
            void BeforeCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
            {
                if (renderingCamera == city.Camera) VerifyRenderPose("begin-render");
            }
            VerifyRenderPose("before-render");
            RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
            try { CaptureCurrentCamera(city.Camera, SceneIds.City, name); }
            finally { RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering; }
        }

        private sealed class CityLocalAcceptanceState : IDisposable
        {
            private readonly CityGameRoot city;
            private readonly Pose camera, player;
            private readonly float lens, aspect, rain;
            private readonly PlayerCameraFollow follow;
            private readonly GameTimeRuntime clock;
            private readonly bool followEnabled, clockEnabled, inputEnabled, weatherEnabled, dayNightEnabled;
            private readonly List<Renderer> hidden = new List<Renderer>();

            public CityLocalAcceptanceState(CityGameRoot root)
            {
                city = root;
                camera = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
                player = new Pose(city.Player.GameObject.transform.position, city.Player.GameObject.transform.rotation);
                lens = city.Camera.fieldOfView; aspect = city.Camera.aspect;
                follow = city.Camera.GetComponent<PlayerCameraFollow>(); followEnabled = follow != null && follow.enabled;
                clock = Object.FindAnyObjectByType<GameTimeRuntime>(); clockEnabled = clock != null && clock.enabled;
                inputEnabled = city.Player.Motor.InputEnabled;
                weatherEnabled = city.Weather.enabled; dayNightEnabled = city.DayNight.enabled; rain = city.Rain.Intensity;
                if (follow != null) follow.enabled = false;
                if (clock != null) clock.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                city.DayNight.ApplyCurrentTime(true); city.DayNight.enabled = false;
                city.Weather.enabled = false; city.Rain.SetIntensity(0f);
                city.Camera.aspect = (float)Width / Height;
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
            }

            public void Dispose()
            {
                if (city == null) return;
                city.Player.Motor.CancelInteractionPoseMove();
                city.Player.Motor.Teleport(player.position); city.Player.GameObject.transform.rotation = player.rotation;
                city.Player.Motor.SetInputEnabled(inputEnabled);
                city.Camera.transform.SetPositionAndRotation(camera.position, camera.rotation);
                city.Camera.fieldOfView = lens; city.Camera.aspect = aspect;
                city.Weather.enabled = weatherEnabled; city.DayNight.enabled = dayNightEnabled; city.Rain.SetIntensity(rain);
                if (follow != null) follow.enabled = followEnabled;
                if (clock != null) clock.enabled = clockEnabled;
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
            }
        }
    }
}
