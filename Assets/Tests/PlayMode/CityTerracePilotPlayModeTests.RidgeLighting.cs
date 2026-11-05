using System.Collections;
using System.Collections.Generic;
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
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_RidgeKioskDialogueRetainsFacesAndClothesWithBrightStreet()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return LoadProductionCity();
            Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
            city.Weather.enabled = false;
            Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var site), Is.True);
            Transform kiosk = city.World.Root.transform.Find("Ridge Kiosk");
            var vendor = kiosk.GetComponentInChildren<RidgeKioskVendor>();
            var hero = city.Player.Interactor;
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var atmosphere = city.Night.Atmosphere;
            Transform outgoingAnchor = kiosk.Find("IndustrialTaskLight_RidgeKiosk");
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_RIDGE_LIGHTING") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            int priorInterval = OnDemandRendering.renderFrameInterval;
            try
            {
                OnDemandRendering.renderFrameInterval = 24;
                foreach (int minute in new[] { 12 * 60, 0 })
                {
                    double advance = (minute - GameSessionState.GameTimeOfDayMinutes + 1440d) % 1440d;
                    GameSessionState.AdvanceGameTime((float)(advance / GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    city.Player.Motor.Teleport(site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                    city.Player.Motor.transform.rotation = Quaternion.LookRotation(-kiosk.forward);
                    Vector3 eye = hero.transform.position + new Vector3(.37f, 1.53f, .65f);
                    follow.SetFixedPose(eye, Quaternion.LookRotation(vendor.Actor.Head.position - eye), 59f);
                    Physics.SyncTransforms();
                    atmosphere.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(hero.ActiveInteractable, Is.SameAs(vendor));
                    vendor.Interact(hero);
                    Assert.That(vendor.Session.IsActive, Is.True, "Use the production conversation and staging.");
                    for (int frame = 0; frame < 220 && !vendor.Session.IsChoosing && vendor.Session.IsActive; frame++)
                        yield return null;
                    Assert.That(vendor.Session.IsChoosing, Is.True, vendor.Session.CameraDirector.LastRejectedShotReason);
                    yield return null;
                    Assert.That(vendor.Session.SelectChoice(0), Is.True);
                    Assert.That(vendor.Session.Confirm(), Is.True);
                    bool sawHero = false, sawSeller = false;
                    for (int frame = 0; frame < 450 && vendor.Session.IsActive; frame++)
                    {
                        if (vendor.Session.Phase == DialoguePhase.Speaking && vendor.Session.CameraDirector.IsSettled)
                        {
                            bool speakingHero = vendor.Session.CurrentNode.Speaker == DialogueSpeaker.Hero;
                            if (vendor.Session.CameraDirector.CurrentSpeakerIsHero == speakingHero &&
                                (speakingHero ? !sawHero : !sawSeller))
                            {
                                atmosphere.RefreshImmediate();
                                Light outgoing = FindKioskOutgoing();
                                Assert.That(atmosphere.RealtimeLightCount,
                                    Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                                AssertStreetBeamExcludesActor(outgoing, city.Player.GameObject.transform);
                                AssertStreetBeamExcludesActor(outgoing, vendor.Actor.transform);
                                if (capture)
                                    CaptureDialogueExposure(outgoing, speakingHero ? vendor.Session.HeroHead : vendor.Actor.Head,
                                        $"{minute:0000}-dialogue-{(speakingHero ? "hero" : "seller")}");
                                if (speakingHero) sawHero = true; else sawSeller = true;
                            }
                        }
                        yield return null;
                    }
                    Assert.That(sawHero && sawSeller, Is.True, "Both real dialogue cameras must retain character exposure.");
                    Assert.That(vendor.Session.IsActive, Is.False, "The conversation restores ordinary play.");
                    Assert.That(hero.InputEnabled && city.Player.Motor.InputEnabled, Is.True);
                }
                var receivers = kiosk.GetComponent<RidgeKioskStreetLightReceivers>();
                Assert.That(receivers, Is.Not.Null);
                Renderer street = city.World.Root.GetComponentsInChildren<MeshRenderer>(true)
                    .First(renderer => !renderer.transform.IsChildOf(kiosk) &&
                        (renderer.renderingLayerMask & RidgeKioskStreetLightReceivers.StreetLayer) != 0);
                receivers.enabled = false;
                Assert.That(street.renderingLayerMask & RidgeKioskStreetLightReceivers.StreetLayer, Is.Zero,
                    "Disabling the scope restores its environment receiver layer.");
                receivers.enabled = true;
                Assert.That(street.renderingLayerMask & RidgeKioskStreetLightReceivers.StreetLayer, Is.Not.Zero,
                    "Re-enabling restores the scoped street light.");
                Light priorLease = FindKioskOutgoing();
                city.Player.Motor.Teleport(Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                atmosphere.RefreshImmediate();
                var reassigned = priorLease.GetUniversalAdditionalLightData();
                Assert.That((uint)reassigned.renderingLayers, Is.EqualTo(1u), "A retasked pool light must restore ordinary receivers.");
                Assert.That(reassigned.customShadowLayers, Is.False, "The next pooled fixture must retain ordinary shadow ownership.");
                if (capture) yield return PhotographStreet();
            }
            finally
            {
                if (vendor.Session.IsActive) vendor.Session.Cancel();
                follow.ClearFixedPose();
                OnDemandRendering.renderFrameInterval = priorInterval;
            }

            Light FindKioskOutgoing() => atmosphere.StreetLightPool.Single(light => light.isActiveAndEnabled &&
                Vector3.Distance(light.transform.position, outgoingAnchor.position) < .01f &&
                Vector3.Angle(light.transform.forward, outgoingAnchor.forward) < .1f);

            void CaptureDialogueExposure(Light outgoing, Transform head, string name)
            {
                float priorAspect = city.Camera.aspect;
                bool priorEnabled = outgoing.enabled;
                float priorIntensity = outgoing.intensity;
                try
                {
                    city.Camera.aspect = 1280f / 720f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", name + "-on", 1280, 720);
                    outgoing.enabled = false;
                    outgoing.intensity = 0f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", name + "-off", 1280, 720);
                    foreach (var patch in new[] {
                        (label: "face", point: head.position, halfSize: .055f, maximumClipped: .40f),
                        (label: "clothes", point: head.position - Vector3.up * .33f, halfSize: .065f, maximumClipped: .12f) })
                    {
                        Vector3 viewport = city.Camera.WorldToViewportPoint(patch.point);
                        Assert.That(viewport.z, Is.GreaterThan(0f), name + " " + patch.label);
                        Assert.That(viewport.x, Is.InRange(.05f, .95f), name + " " + patch.label);
                        Assert.That(viewport.y, Is.InRange(.05f, .95f), name + " " + patch.label);
                        Vector3 radius = city.Camera.WorldToViewportPoint(patch.point + city.Camera.transform.right * patch.halfSize) - viewport;
                        var lit = RidgeDialoguePatchExposure(name + "-on", viewport, Mathf.Abs(radius.x));
                        var unlit = RidgeDialoguePatchExposure(name + "-off", viewport, Mathf.Abs(radius.x));
                        Debug.Log($"Kiosk dialogue {name}/{patch.label}: on={lit.mean:F5}, off={unlit.mean:F5}, clipped={lit.clipped:F3}.");
                        Assert.That(lit.mean, Is.LessThanOrEqualTo(unlit.mean * 1.2f + .01f),
                            name + " " + patch.label + ": the strong street beam must not brighten the character.");
                        Assert.That(lit.clipped, Is.LessThan(patch.maximumClipped),
                            name + " " + patch.label + ": broad near-white clipping loses skin or cloth detail.");
                    }
                }
                finally
                {
                    outgoing.intensity = priorIntensity;
                    outgoing.enabled = priorEnabled;
                    city.Camera.aspect = priorAspect;
                }
            }

            IEnumerator PhotographStreet()
            {
                Vector3 eye = kiosk.TransformPoint(new Vector3(4.5f, 1.72f, 7.3f));
                Vector3 target = kiosk.TransformPoint(new Vector3(0f, .8f, 2.55f));
                city.Player.Motor.Teleport(kiosk.TransformPoint(new Vector3(4.5f, 0f, 7.3f)) +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 59f);
                Physics.SyncTransforms();
                atmosphere.RefreshImmediate();
                yield return null;
                yield return null;
                var renderers = city.Player.GameObject.GetComponentsInChildren<Renderer>();
                var enabled = renderers.Select(renderer => renderer.enabled).ToArray();
                Light outgoing = FindKioskOutgoing();
                bool priorEnabled = outgoing.enabled;
                float priorIntensity = outgoing.intensity, priorAspect = city.Camera.aspect;
                const string name = "0000-dialogue-street-spill";
                try
                {
                    foreach (Renderer renderer in renderers) renderer.enabled = false;
                    city.Camera.aspect = 1280f / 720f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", name + "-on", 1280, 720);
                    outgoing.enabled = false;
                    outgoing.intensity = 0f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", name + "-off", 1280, 720);
                    Vector3 viewport = city.Camera.WorldToViewportPoint(kiosk.TransformPoint(new Vector3(0f, .10f, 6f)));
                    float lit = RidgeKioskStreetPatchLuminance(name + "-on", viewport);
                    float unlit = RidgeKioskStreetPatchLuminance(name + "-off", viewport);
                    Debug.Log($"Kiosk dialogue street luminance: on={lit:F5}, off={unlit:F5}.");
                    Assert.That(lit, Is.GreaterThan(unlit * 1.5f + .002f), "The character correction must preserve the warm road pool.");
                }
                finally
                {
                    outgoing.intensity = priorIntensity;
                    outgoing.enabled = priorEnabled;
                    city.Camera.aspect = priorAspect;
                    for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = enabled[i];
                }
            }
        }

        private static void AssertStreetBeamExcludesActor(Light source, Transform actor)
        {
            var data = source.GetUniversalAdditionalLightData();
            uint lightLayers = data.renderingLayers;
            Assert.That(lightLayers, Is.EqualTo(RidgeKioskStreetLightReceivers.StreetLayer));
            Assert.That(data.customShadowLayers, Is.True);
            Assert.That((uint)data.shadowRenderingLayers, Is.EqualTo(1u), "Ordinary actors still block the strong beam.");
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    Assert.That(renderer.renderingLayerMask & lightLayers, Is.Zero,
                        "The street-only beam must exclude " + renderer.name + " even during dialogue.");
        }

        private static (float mean, float clipped) RidgeDialoguePatchExposure(string shot, Vector3 viewport, float radius)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine("Captures", "RidgeLighting", shot + ".png"))), Is.True);
                int cx = Mathf.RoundToInt(viewport.x * (image.width - 1));
                int cy = Mathf.RoundToInt(viewport.y * (image.height - 1));
                int half = Mathf.Clamp(Mathf.RoundToInt(radius * image.width), 2, 24);
                float sum = 0f;
                int clipped = 0, count = 0;
                for (int dy = -half; dy <= half; dy++)
                for (int dx = -half; dx <= half; dx++)
                {
                    Color pixel = image.GetPixel(cx + dx, cy + dy);
                    Color linear = pixel.linear;
                    float luminance = .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
                    sum += luminance;
                    if (pixel.maxColorComponent >= .96f && luminance >= .72f) clipped++;
                    count++;
                }
                return (sum / count, (float)clipped / count);
            }
            finally { Object.DestroyImmediate(image); }
        }

        [UnityTest]
        public IEnumerator LiveCity_RidgeLightingKeepsDoorStairsAndBridgeLitAcrossTheClock()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return LoadProductionCity();
            Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
            city.Weather.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            follow.SetOrbitInputEnabled(false);
            var atmosphere = city.Night.Atmosphere;
            Transform homeLens = city.World.Root.GetComponentsInChildren<Transform>()
                .Single(t => t.name == "Home Entrance Lamp");
            Transform homeAnchor = city.World.Root.GetComponentsInChildren<Transform>()
                .Single(t => t.name == "IndustrialTaskLight_HomeEntrance");
            Assert.That(Vector3.Distance(homeAnchor.position, homeLens.position), Is.LessThan(.45f),
                "The real source belongs to the existing entrance lens.");
            var probes = RidgeLightingProbes();
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_RIDGE_LIGHTING") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            float dayHomeIntensity = 0f, nightHomeIntensity = 0f;
            var kioskDayIntensities = new float[2];
            var kioskNightIntensities = new float[2];
            var coverageFailures = new HashSet<string>();
            bool priorVolume = GraphicsEffectsSettings.CleanVolumeEnabled;
            bool priorHalation = GraphicsEffectsSettings.CleanHalationEnabled;
            bool priorBounce = GraphicsEffectsSettings.CleanBounceEnabled;
            bool priorDof = GraphicsEffectsSettings.DepthOfFieldEnabled;
            int priorRenderingInterval = OnDemandRendering.renderFrameInterval;
            try
            {
                OnDemandRendering.renderFrameInterval = 24;
                GraphicsEffectsSettings.DepthOfFieldEnabled = true;
                foreach (int minute in new[] { 12 * 60, 18 * 60 + 30, 0 })
                {
                    double advance = (minute - GameSessionState.GameTimeOfDayMinutes + 1440d) % 1440d;
                    GameSessionState.AdvanceGameTime((float)(advance / GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    Debug.Log($"Ridge lighting: minute {minute}, {probes.Count} route probes, both directions.");
                    yield return CheckKioskAtCurrentTime(minute);
                    for (int reverse = 0; reverse < 2; reverse++)
                    for (int i = 0; i < probes.Count; i++)
                    {
                        var probe = probes[reverse == 0 ? i : probes.Count - 1 - i];
                        yield return Observe(probe, reverse != 0);
                        CheckRidgeDirectLight(probe.Name, probe.Point, capture, coverageFailures);
                        Assert.That(atmosphere.RealtimeLightCount,
                            Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                        if (probe.Name == "home-threshold")
                        {
                            var source = atmosphere.StreetLightPool.FirstOrDefault(light => light.isActiveAndEnabled &&
                                Vector3.Distance(light.transform.position, homeAnchor.position) < .01f);
                            Assert.That(source, Is.Not.Null, "The visible home lens must lease a real light at its bulb.");
                            Assert.That(RidgeLightHasClearPath(source, probe.Point + Vector3.up * .10f), Is.True,
                                "The home canopy and wall must not occlude their own doorstep source.");
                            if (minute == 720) dayHomeIntensity = source.intensity;
                            if (minute == 0) nightHomeIntensity = source.intensity;
                        }
                        if (capture && probe.Photograph && (reverse == 0 || minute == 0))
                            AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting",
                                $"{minute:0000}-{probe.Name}-{(reverse == 0 ? "out" : "back")}", 1280, 720);
                    }
                }
                Assert.That(dayHomeIntensity, Is.GreaterThanOrEqualTo(nightHomeIntensity *
                    GameTimeDayNightRules.DayFixtureFloor - .001f), "Noon must retain the fixture's daylight floor.");
                for (int i = 0; i < kioskDayIntensities.Length; i++)
                    Assert.That(kioskDayIntensities[i], Is.GreaterThanOrEqualTo(kioskNightIntensities[i] *
                        GameTimeDayNightRules.DayFixtureFloor - .001f), "Both kiosk beams retain their daylight floor.");

                // The direct-light contract must survive the saved switches for
                // atmospheric polish; an emissive lens or halo cannot satisfy it.
                GraphicsEffectsSettings.CleanVolumeEnabled = false;
                GraphicsEffectsSettings.CleanHalationEnabled = false;
                GraphicsEffectsSettings.CleanBounceEnabled = false;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                foreach (var probe in probes.Where(p => p.Photograph))
                {
                    yield return Observe(probe, false);
                    CheckRidgeDirectLight(probe.Name, probe.Point, capture, coverageFailures);
                    if (capture)
                        AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting",
                            "0000-" + probe.Name + "-direct", 1280, 720);
                }
                Assert.That(coverageFailures, Is.Empty, string.Join("\n", coverageFailures));
            }
            finally
            {
                GraphicsEffectsSettings.CleanVolumeEnabled = priorVolume;
                GraphicsEffectsSettings.CleanHalationEnabled = priorHalation;
                GraphicsEffectsSettings.CleanBounceEnabled = priorBounce;
                GraphicsEffectsSettings.DepthOfFieldEnabled = priorDof;
                OnDemandRendering.renderFrameInterval = priorRenderingInterval;
                follow.ClearFixedPose();
            }

            IEnumerator Observe(RidgeLightingProbe probe, bool reverse)
            {
                city.Player.Motor.Teleport(probe.Point + Vector3.up * PlayerFactory.GroundedRootOffset);
                Vector3 direction = reverse ? -probe.Forward : probe.Forward;
                city.Player.Motor.transform.rotation = Quaternion.LookRotation(direction);
                follow.ClearFixedPose();
                follow.RotateYaw(Mathf.DeltaAngle(city.Camera.transform.eulerAngles.y,
                    Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg));
                follow.Snap();
                Physics.SyncTransforms();
                atmosphere.RefreshImmediate();
                yield return null;
                yield return null;
            }

            IEnumerator CheckKioskAtCurrentTime(int minute)
            {
                Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var site), Is.True);
                Transform kiosk = city.World.Root.transform.Find("Ridge Kiosk");
                var vendor = kiosk.GetComponentInChildren<RidgeKioskVendor>();
                Renderer lens = kiosk.GetComponentsInChildren<Renderer>()
                    .Single(renderer => renderer.name == "RidgeKiosk_KioskLampGlass");
                Assert.That(lens.sharedMaterial, Is.SameAs(CityNightResources.EmissiveMaterial));
                Assert.That(lens.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off),
                    "The luminous diffuser must not cast a shadow over its own beams.");
                var anchors = new[] {
                    kiosk.Find("IndustrialTaskLight_RidgeKiosk"),
                    kiosk.Find("IndustrialTaskLight_RidgeKioskInterior") };
                Assert.That(anchors.All(anchor => anchor != null), Is.True,
                    "The kiosk's existing lamp must provide both an inward and an outward beam.");
                city.Player.Motor.Teleport(site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                city.Player.Motor.transform.rotation = site.Rotation * Quaternion.LookRotation(Vector3.back);
                follow.ClearFixedPose();
                follow.Snap();
                Physics.SyncTransforms();
                atmosphere.RefreshImmediate();
                yield return null;
                yield return null;
                Assert.That(atmosphere.RealtimeLightCount,
                    Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                var sources = new Light[anchors.Length];
                for (int i = 0; i < anchors.Length; i++)
                {
                    sources[i] = atmosphere.StreetLightPool.FirstOrDefault(light => light.isActiveAndEnabled &&
                        Vector3.Distance(light.transform.position, anchors[i].position) < .01f &&
                        Vector3.Angle(light.transform.forward, anchors[i].forward) < .1f);
                    if (sources[i] == null)
                    {
                        coverageFailures.Add("kiosk: no live pool lease for " + anchors[i].name);
                        continue;
                    }
                    Assert.That(sources[i].shadows, Is.Not.EqualTo(LightShadows.None),
                        "Kiosk light must respect the window aperture and the opaque shell.");
                    Assert.That(sources[i].color.r, Is.GreaterThan(sources[i].color.g));
                    Assert.That(sources[i].color.g, Is.GreaterThan(sources[i].color.b));
                    if (minute == 720) kioskDayIntensities[i] = sources[i].intensity;
                    if (minute == 0) kioskNightIntensities[i] = sources[i].intensity;
                }
                foreach (var probe in new[] {
                    (name: "seller-chest", local: new Vector3(0f, 1.20f, .64f), source: 1, seller: true),
                    (name: "seller-face", local: new Vector3(0f, 1.70f, .64f), source: 1, seller: true),
                    (name: "rear-shelf", local: new Vector3(.50f, 1.75f, -.65f), source: 1, seller: false),
                    (name: "rear-wall", local: new Vector3(.10f, 1.25f, -1.10f), source: 1, seller: false),
                    (name: "visitor-apron", local: new Vector3(0f, .10f, 2.22f), source: 0, seller: false),
                    (name: "sidewalk-spill", local: new Vector3(0f, .10f, 4f), source: 0, seller: false),
                    (name: "street-spill", local: new Vector3(0f, .10f, 5f), source: 0, seller: false),
                    (name: "road-edge-spill", local: new Vector3(0f, .10f, 6f), source: 0, seller: false),
                    (name: "far-street-spill", local: new Vector3(0f, .10f, 8f), source: 0, seller: false),
                    (name: "left-street-spill", local: new Vector3(-2f, .10f, 6f), source: 0, seller: false),
                    (name: "right-street-spill", local: new Vector3(2f, .10f, 6f), source: 0, seller: false) })
                {
                    Vector3 target = kiosk.TransformPoint(probe.local);
                    Light source = sources[probe.source];
                    if (source == null) continue;
                    bool clear = RidgeSourceReachesPoint(source, target,
                        probe.seller ? vendor.Actor.transform : null);
                    if (!clear) coverageFailures.Add($"kiosk-{probe.name}: beam does not reach {target:F2} through the shell; " +
                        $"source={source.transform.position:F2}, aim={source.transform.forward:F2}, " +
                        $"range={source.range:F1}, angle={Vector3.Angle(target-source.transform.position,source.transform.forward):F1}.");
                }
                if (capture && (minute == 720 || minute == 0))
                {
                    yield return PhotographKiosk(new Vector3(0f, 1.65f, 4.55f),
                        new Vector3(0f, 1.35f, .45f), $"{minute:0000}-kiosk-front");
                    if (minute == 0)
                        yield return PhotographKiosk(new Vector3(4.5f, 1.72f, 7.3f),
                            new Vector3(0f, .8f, 2.55f), "0000-kiosk-street-spill");
                }
            }

            IEnumerator PhotographKiosk(Vector3 localEye, Vector3 localTarget, string name)
            {
                Transform kiosk = city.World.Root.transform.Find("Ridge Kiosk");
                Vector3 eye = kiosk.TransformPoint(localEye), target = kiosk.TransformPoint(localTarget);
                // Keep the real pool focus here, but exclude the body enclosing
                // this observational lens. Restore every renderer afterwards.
                city.Player.Motor.Teleport(kiosk.TransformPoint(new Vector3(localEye.x, 0f, localEye.z)) +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                atmosphere.RefreshImmediate();
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 59f);
                yield return null;
                yield return null;
                var renderers = city.Player.GameObject.GetComponentsInChildren<Renderer>();
                var enabled = renderers.Select(renderer => renderer.enabled).ToArray();
                float priorAspect = city.Camera.aspect;
                try
                {
                    foreach (var renderer in renderers) renderer.enabled = false;
                    city.Camera.aspect = 1280f / 720f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", name, 1280, 720);
                    if (name == "0000-kiosk-street-spill")
                    {
                        Transform anchor = kiosk.Find("IndustrialTaskLight_RidgeKiosk");
                        Light outgoing = atmosphere.StreetLightPool.Single(light => light.isActiveAndEnabled &&
                            Vector3.Distance(light.transform.position, anchor.position) < .01f &&
                            Vector3.Angle(light.transform.forward, anchor.forward) < .1f);
                        bool priorEnabled = outgoing.enabled;
                        float priorIntensity = outgoing.intensity;
                        const string offName = "0000-kiosk-street-spill-off";
                        try
                        {
                            // Synchronous A/B: neither the nearest-source pool
                            // nor world time can advance between the two renders.
                            outgoing.enabled = false;
                            outgoing.intensity = 0f;
                            AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", offName, 1280, 720);
                        }
                        finally
                        {
                            outgoing.intensity = priorIntensity;
                            outgoing.enabled = priorEnabled;
                        }
                        Vector3 sample = city.Camera.WorldToViewportPoint(kiosk.TransformPoint(new Vector3(0f, .10f, 6f)));
                        Assert.That(sample.z, Is.GreaterThan(0f));
                        Assert.That(sample.x, Is.InRange(.02f, .98f));
                        Assert.That(sample.y, Is.InRange(.02f, .98f));
                        float lit = RidgeKioskStreetPatchLuminance(name, sample);
                        float unlit = RidgeKioskStreetPatchLuminance(offName, sample);
                        Debug.Log($"Kiosk street rendered linear luminance: on={lit:F5}, off={unlit:F5}, " +
                            $"ratio={lit/Mathf.Max(unlit,.00001f):F2}, sample={sample:F3}.");
                        Assert.That(lit, Is.GreaterThan(unlit * 1.5f + .002f),
                            "The kiosk must materially brighten the real rendered street, beyond geometric cone coverage.");
                    }
                }
                finally
                {
                    for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = enabled[i];
                    city.Camera.aspect = priorAspect;
                    follow.ClearFixedPose();
                }
            }
        }

        private static float RidgeKioskStreetPatchLuminance(string shot, Vector3 viewport)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine("Captures", "RidgeLighting", shot + ".png"))), Is.True);
                int x = Mathf.RoundToInt(viewport.x * (image.width - 1));
                int y = Mathf.RoundToInt(viewport.y * (image.height - 1));
                float sum = 0f;
                for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    Color linear = image.GetPixel(x + dx, y + dy).linear;
                    sum += .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
                }
                return sum / 81f;
            }
            finally { Object.DestroyImmediate(image); }
        }

        private List<RidgeLightingProbe> RidgeLightingProbes()
        {
            var result = new List<RidgeLightingProbe>();
            var home = city.Layout.SummitHome;
            result.Add(new RidgeLightingProbe("home-threshold", home.ThresholdWorld + home.Lot.FacadeForward * .60f,
                -home.Lot.FacadeForward, true));
            result.Add(new RidgeLightingProbe("home-approach", Vector3.Lerp(home.DoorWorld, home.StreetContactWorld, .5f),
                home.Lot.FacadeForward, false));
            var bus = city.BusPlans.Single(line => line.Number == 3);
            var stop = bus.Stops.Single(s => s.SequenceIndex == 3);
            result.Add(new RidgeLightingProbe("03-04", CityBusStopWaitPlanner.StandingPosition(bus, stop, city.Layout),
                Vector3.right, true));
            Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var kiosk), Is.True);
            result.Add(new RidgeLightingProbe("kiosk", kiosk.Approach, Vector3.back, false));
            var bridge = CityHillsideQuarterPlan.ForLayout(city.Layout).Bridges.Single(b => b.Id == "summit");
            float startX = CityBusStopWaitPlanner.StandingPosition(bus, stop, city.Layout).x;
            float endX = bridge.DeckBounds.xMax + 1.4f;
            int spans = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(endX - startX) / 4f));
            foreach (int side in new[] { -1, 1 })
            for (int station = 0; station <= spans; station++)
            {
                float x = Mathf.Lerp(startX, endX, (float)station / spans);
                var xz = new Vector2(x, bridge.DeckCenterWorld.z + side * 4f);
                bool found = bridge.Link.TrySampleTop(xz, out float top, out var kind);
                if (!found)
                    foreach (var street in CityHillsideStreetPlan.ForLayout(city.Layout).Routes)
                        if (street.TrySampleTop(xz, out top, out kind)) { found = true; break; }
                Assert.That(found && kind == CityStreetSupportKind.Sidewalk, Is.True,
                    $"The Ridge route sample must use the public footway at {xz:F2}.");
                result.Add(new RidgeLightingProbe($"street-{side}-{station}", new Vector3(x, top, xz.y),
                    Vector3.right, false));
            }
            foreach (int side in new[] { -1, 1 })
            foreach (float fraction in new[] { .08f, .5f, .92f })
            {
                float x = Mathf.Lerp(bridge.DeckBounds.xMin, bridge.DeckBounds.xMax, fraction);
                result.Add(new RidgeLightingProbe($"bridge-{side}-{fraction:F2}",
                    new Vector3(x, bridge.Link.PavementTopAt(x), bridge.DeckCenterWorld.z + side * 4f),
                    Vector3.right, side == 1 && fraction == .5f));
            }
            var shared = CityHillsideAccessPlan.ForLayout(city.Layout).SharedCourt;
            foreach (var floor in shared.Floors)
                result.Add(new RidgeLightingProbe("court-" + floor.Id, floor.Center, Vector3.back,
                    floor.Id == shared.Floors[0].Id || Mathf.Abs(floor.Size.x - floor.Size.y) < .01f));
            foreach (var flight in shared.Flights)
            foreach (float fraction in new[] { .25f, .75f })
                result.Add(new RidgeLightingProbe(flight.Id + $"-{fraction:F2}", Vector3.Lerp(flight.Start, flight.End, fraction),
                    flight.Direction, false));
            return result;
        }

        private void CheckRidgeDirectLight(string name, Vector3 floor, bool captureFailure, HashSet<string> failures)
        {
            Vector3 point = floor + Vector3.up * .10f;
            var pool = city.Night.Atmosphere.StreetLightPool;
            bool reached = pool.Any(light =>
                RidgeSourceReachesPoint(light, point));
            if (!reached && captureFailure && !failures.Any(f => f.StartsWith(name + ":")))
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", "failure-" + name, 1280, 720);
            if (!reached) failures.Add($"{name}: no assigned direct light covers the walking surface at {point:F2}. " +
                string.Join("; ", pool.Where(l => l.isActiveAndEnabled).OrderBy(l => Vector3.Distance(l.transform.position, point))
                    .Take(4).Select(l => $"{l.name}@{l.transform.position:F2} range={l.range:F1}, " +
                        $"angle={Vector3.Angle(point-l.transform.position,l.transform.forward):F1}/{l.spotAngle*.5f:F1}")));
        }

        private bool RidgeSourceReachesPoint(Light light, Vector3 point, Transform ignoredActor = null)
        {
            if (!light.isActiveAndEnabled || light.intensity <= .001f) return false;
            Vector3 delta = point - light.transform.position;
            return delta.sqrMagnitude < light.range * light.range * .95f * .95f &&
                (light.type == LightType.Point || light.type == LightType.Spot &&
                Vector3.Angle(delta, light.transform.forward) < light.spotAngle * .5f - 2f) &&
                (light.shadows == LightShadows.None || RidgeLightHasClearPath(light, point, ignoredActor));
        }

        private bool RidgeLightHasClearPath(Light light, Vector3 target, Transform ignoredActor = null)
        {
            Vector3 delta = target - light.transform.position;
            return !Physics.RaycastAll(light.transform.position + delta.normalized * .08f, delta.normalized,
                    delta.magnitude - .12f, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => !hit.transform.IsChildOf(city.Player.GameObject.transform) &&
                    (ignoredActor == null || !hit.transform.IsChildOf(ignoredActor)));
        }

        private readonly struct RidgeLightingProbe
        {
            internal readonly string Name;
            internal readonly Vector3 Point, Forward;
            internal readonly bool Photograph;
            internal RidgeLightingProbe(string name, Vector3 point, Vector3 forward, bool photograph)
            { Name = name; Point = point; Forward = forward; Photograph = photograph; }
        }
    }
}
