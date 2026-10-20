using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Timeout(600000)]
        [Explicit("Focused City surfaces and lighting: nine areas, map/binding budgets, synchronized pairs and resident exterior ownership.")]
        public IEnumerator CityNativeLighting()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousCaptureDelta = Time.captureDeltaTime;
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            bool clockWasEnabled = false;
            PlayerCameraFollow follow = null;
            bool followWasEnabled = false;
            var hiddenPlayer = new List<Renderer>();
            var reflections = new CityReflectionAcceptance();
            CitySurfaceAudit surfaces = null;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test city lighting");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                SetCityNativeEffects(true);
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    Assert.That(city.Lighting, Is.Not.Null);
                    Assert.That(city.Lighting.Reflection, Is.Not.Null);
                    AssertCityNativeShaders();
                    surfaces = AssertCitySurfaceContracts(city);
                    clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                    Assert.That(clock, Is.Not.Null);
                    clockWasEnabled = clock.enabled;
                    clock.enabled = false;
                    city.DayNight.enabled = false;
                    city.Weather.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    Assert.That(follow, Is.Not.Null);
                    followWasEnabled = follow.enabled;
                    follow.enabled = false;
                    return CityNativeLightingShots(city, reflections);
                });

                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hiddenPlayer.Add(renderer); }
                AssertCityLightingCanon(city);
                yield return VerifyCityLightingPool(city);
                yield return CaptureCityLightingEffectPairs(city, reflections);
                yield return CaptureCitySurfacePairs(city, surfaces, reflections);
                var effectDifferences = new Dictionary<string, int>();
                foreach (string effect in new[] { "Reflections", "Lit air", "Halation", "Metal highlights", "Bounce" })
                    effectDifferences.Add(effect, CountCityLightingDifference(effect + " baseline", effect + " enabled"));
                yield return VerifyCityLightingOwnership(city, reflections);
                surfaces.AssertRestored();

                SceneReflectionCapture[] zones = CityReflectionSlots(city);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(city == null, Is.True, "Single loading must destroy the City lighting owner.");
                foreach (SceneReflectionCapture zone in zones)
                    Assert.That(zone == null, Is.True, "Every reusable City capture belongs to the unloaded exterior.");
                Assert.That(SceneLightingPresentation.Resolve(Camera.main), Is.Null);
                AssertCityLightingGlobalsCleared();
                var invisibleEffects = new List<string>();
                foreach (KeyValuePair<string, int> effect in effectDifferences)
                    if (effect.Value <= 24) invisibleEffects.Add(effect.Key + "=" + effect.Value);
                Assert.That(invisibleEffects, Is.Empty,
                    "Every effect must visibly affect its real receiver (>24 pixels): " + string.Join(", ", invisibleEffects));
            }
            finally
            {
                surfaces?.SetPigment(true);
                surfaces?.SetResponse(true);
                foreach (Renderer renderer in hiddenPlayer) if (renderer != null) renderer.enabled = true;
                if (city != null)
                {
                    if (city.IsDormant) city.ResumeFromDormant();
                    city.DayNight.enabled = true;
                    city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                }
                if (follow != null) follow.enabled = followWasEnabled;
                if (clock != null) clock.enabled = clockWasEnabled;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test city lighting cleanup");
            }
        }

        private static Shot[] CityNativeLightingShots(CityGameRoot city, CityReflectionAcceptance reflections)
        {
            Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
            Assert.That(city.Lighting.Reflection.RegionCount, Is.GreaterThan(9),
                "City must share bounded practical environments across its nine working areas.");
            Assert.That(city.Lighting.Reflection.ZoneCount, Is.EqualTo(2));
            var poses = new List<Shot>();
            BuildingLot bar = null;
            foreach (BuildingLot lot in city.Layout.BuildingLots) if (lot.IsBar) { bar = lot; break; }
            Assert.That(bar, Is.Not.Null);
            poses.Add(FrameWindowFacade("Residential", bar));
            poses.Add(FrameWindowFacade("Old town", FindOrdinaryFrontage(city.Layout, CityDistrictKind.OldTown)));
            CityDecorationDescriptor cinema = default;
            bool cinemaFound = false;
            foreach (CityDecorationDescriptor decoration in city.World.DecorationPlan.Descriptors)
                if (decoration.Kind == CityDecorationKind.NightlifeCinema) { cinema = decoration; cinemaFound = true; break; }
            Assert.That(cinemaFound, Is.True, "Nightlife acceptance must see the real neon cinema frontage.");
            Vector3 cinemaRight = Vector3.Cross(Vector3.up, cinema.Forward);
            poses.Add(Shot.At("Nightlife", cinema.Position + cinema.Forward * 7f + cinemaRight * 2f + Vector3.up * EyeHeight,
                cinema.Position + Vector3.up * 2.4f, 72f));
            Assert.That(city.Cannery, Is.Not.Null);
            poses.Add(Shot.At("Industrial", city.Cannery.Plan.World(new Vector3(-1.1f, 1.9f, -2f)),
                city.Cannery.Plan.World(new Vector3(-5f, 1.4f, -.35f)), 70f));
            CityParkChessLampPlan chess = CityParkChessLampPlan.Create(city.Layout, city.World.DecorationPlan);
            Assert.That(chess.IsPresent, Is.True);
            poses.Add(Shot.At("Park", chess.Origin + chess.Forward * 5.5f + chess.Tangent * 2.5f + Vector3.up * EyeHeight,
                chess.Origin + Vector3.up * 1.25f, 68f));
            poses.Add(RenameCityPose("Church and cemetery", ChurchGardenShots(city)[5]));
            Assert.That(city.Layout.River.Promenades, Is.Not.Empty);
            CityRiverPromenadeDescriptor quay = city.Layout.River.Promenades[0];
            Transform quayLamp = null;
            float lampDistance = float.PositiveInfinity;
            float channelX = city.Layout.River.Segments[0].WaterBounds.center.x;
            foreach (Transform anchor in city.World.RiverQuayLampAnchors)
            {
                if ((anchor.position.x < channelX) != quay.WestBank) continue;
                float distance = Mathf.Abs(anchor.position.z - quay.Bounds.center.y);
                if (distance >= lampDistance) continue;
                lampDistance = distance; quayLamp = anchor;
            }
            Assert.That(quayLamp, Is.Not.Null, "River acceptance must use a real waterside lantern.");
            float quayY = Mathf.Lerp(quay.SouthY, quay.NorthY,
                Mathf.InverseLerp(quay.Bounds.yMin, quay.Bounds.yMax, quayLamp.position.z - 3.5f));
            poses.Add(Shot.At("River", new Vector3(quay.Bounds.center.x, quayY + EyeHeight, quayLamp.position.z - 3.5f),
                quayLamp.position + quayLamp.forward * 3f, 70f));
            CityPortPlan port = city.World.PortPlan;
            Assert.That(port, Is.Not.Null);
            poses.Add(Shot.At("Coast and port", port.World(new Vector3(18.5f, CityPortPlan.DeckHeight + EyeHeight, -5f)),
                port.World(new Vector3(0f, 3f, 3.2f)), 68f));
            Assert.That(city.World.MountainBoundaryPlan.HasTunnel, Is.True);
            CityMountainTunnelDescriptor tunnel = city.World.MountainBoundaryPlan.Tunnel;
            poses.Add(Shot.At("Fringe and tunnel", tunnel.PortalGroundCenter - tunnel.Axis * 12f + Vector3.up * EyeHeight,
                tunnel.PortalGroundCenter + Vector3.up * 2f, 66f));

            // These crossing views complement the nine local samples without
            // turning a lighting check into the city-wide audit lattice.
            var boundaries = new List<Shot>();
            Assert.That(city.Layout.Park.Gates, Is.Not.Empty);
            CityParkGateDescriptor gate = city.Layout.Park.Gates[0];
            boundaries.Add(Shot.At("Park street boundary", gate.Center - gate.OutwardNormal * 6f + Vector3.up * EyeHeight,
                gate.Center + gate.OutwardNormal * 8f + Vector3.up, 68f));
            CityParkRegionPlan darkPark = city.Layout.Park.Regions[0];
            foreach (CityParkRegionPlan region in city.Layout.Park.Regions)
                if ((region.Center - chess.Origin).sqrMagnitude > (darkPark.Center - chess.Origin).sqrMagnitude) darkPark = region;
            boundaries.Add(Shot.At("Park dark interior", darkPark.Center + Vector3.back * 4f + Vector3.up * EyeHeight,
                darkPark.Center + Vector3.forward * 8f + Vector3.up, 68f));
            CitySeacoastFrame coast = city.World.SeacoastPlan.Frame;
            Vector3 beach = new Vector3(coast.EastZone.center.x, coast.BeachEdgeTopY, coast.WaterlineZ - 3f);
            boundaries.Add(Shot.At("Dark beach boundary", beach + Vector3.up * EyeHeight,
                beach + Vector3.right * 12f + Vector3.forward * 4f, 68f));
            boundaries.Add(Shot.At("Tunnel threshold boundary", tunnel.PortalGroundCenter + tunnel.Axis * 2f + Vector3.up * EyeHeight,
                tunnel.PortalGroundCenter - tunnel.Axis * 10f + Vector3.up, 68f));

            var shots = new List<Shot>();
            bool nightApplied = false;
            foreach (CityDistrictKind district in new[] { CityDistrictKind.Residential, CityDistrictKind.OldTown,
                         CityDistrictKind.Nightlife, CityDistrictKind.Industrial })
            {
                BuildingLot closeLot = FindOrdinaryFrontage(city.Layout, district);
                Add("Day close " + district + " masonry", FrameBuildingSurface("Masonry close", closeLot,
                    FindBuiltPrototype(closeLot), true, 0f), false);
            }
            Vector3 barRight = Vector3.Cross(Vector3.up, bar.FacadeForward);
            Add("Day close bar unique facade", Shot.At("Bar close",
                bar.DoorPosition + bar.FacadeForward * 3.1f + barRight * 1.2f + Vector3.up * EyeHeight,
                bar.DoorPosition + barRight * .8f + Vector3.up * 1.1f, 62f), false);
            foreach (Shot pose in poses) Add("Day " + pose.Name, pose, false);
            foreach (Shot pose in poses) Add("Night " + pose.Name, pose, true);
            foreach (Shot pose in boundaries) Add("Night " + pose.Name, pose, true);
            return shots.ToArray();

            void Add(string description, Shot pose, bool night)
            {
                bool configured = false;
                bool awaitingNight = false;
                uint dayGeneration = 0;
                double deadline = 0d;
                shots.Add(Shot.At(CityLightingFrame(description), pose.Position, pose.Target, pose.FieldOfView, readyWhen: () =>
                {
                    if (!configured)
                    {
                        if (night && !nightApplied)
                        {
                            // Advance uses a float real-time delta. Its tiny
                            // rounding remainder must not request another
                            // lighting generation for every subsequent shot.
                            nightApplied = true;
                            dayGeneration = city.Lighting.Reflection.LightingGeneration;
                            GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                                GameTimeState.GameMinutesPerRealSecond));
                            city.DayNight.ApplyCurrentTime(true);
                            awaitingNight = true;
                        }
                        MoveCityLightingObserver(city, pose);
                        configured = true;
                    }
                    ApplyCityLightingPose(city.Camera, pose);
                    if (awaitingNight)
                    {
                        if (deadline == 0d) deadline = Time.realtimeSinceStartupAsDouble + 25d;
                        // Static environments debounce global lighting changes;
                        // even a fast daylight sheet must await the night generation.
                        city.Lighting.PrepareForCamera(city.Camera);
                        if (city.Lighting.Reflection.LightingGeneration == dayGeneration)
                        {
                            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline),
                                "Night must invalidate the local daytime environments.");
                            reflections.ObserveWait(city.Lighting.Reflection, city.Camera, description, "Night generation", false);
                            return false;
                        }
                        awaitingNight = false;
                    }
                    if (description.Contains("Dark beach"))
                    {
                        city.Lighting.PrepareForCamera(city.Camera);
                        for (int index = 0; index < city.Lighting.VolumeSourceCount; index++)
                            Assert.That(city.Lighting.IsInsideVolumeSupport(index, pose.Position), Is.False,
                                "The wild beach must stay outside every borrowed light volume.");
                    }
                    return CityReflectionsReady(city, ref deadline, reflections, description);
                }));
            }
        }

        private static IEnumerator VerifyCityLightingPool(CityGameRoot city)
        {
            CityNightAtmosphere atmosphere = city.Night.Atmosphere;
            var original = new List<Light>(atmosphere.StreetLightPool);
            Assert.That(original, Is.Not.Empty);
            foreach (Transform anchor in new[] { atmosphere.LampAnchors[0], atmosphere.LampAnchors[atmosphere.LampAnchors.Count - 1] })
            {
                city.Player.Motor.Teleport(anchor.position);
                atmosphere.RefreshImmediate();
                city.Camera.transform.position = anchor.position + Vector3.up * EyeHeight;
                city.Lighting.PrepareForCamera(city.Camera);
                Assert.That(atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                Assert.That(atmosphere.StreetLightPool, Is.EqualTo(original), "Reassignment must reuse the same real lights.");
                AssertCitySelectedSources(city);
                yield return null;
            }
            // A fixed physical lamp must retain the city-wide daytime floor.
            Light entrance = atmosphere.BarLights[0];
            atmosphere.SetNightFactor(1f, true);
            float nightPower = entrance.intensity;
            atmosphere.SetNightFactor(0f, true);
            Assert.That(entrance.enabled, Is.True);
            Assert.That(entrance.intensity, Is.GreaterThanOrEqualTo(nightPower * GameTimeDayNightRules.DayFixtureFloor - .0001f));
            atmosphere.SetNightFactor(1f, true);
        }

        private static void AssertCitySelectedSources(CityGameRoot city)
        {
            var lighting = city.Lighting;
            Assert.That(lighting.PracticalCount, Is.InRange(1, CityLightingPresentation.MaximumPracticals));
            Assert.That(lighting.EmitterCandidateCount, Is.InRange(1, CityLightingPresentation.MaximumEmitterCandidates));
            Assert.That(lighting.HalationSourceCount, Is.InRange(0, CityLightingPresentation.MaximumHalationSources));
            Assert.That(lighting.VolumeSourceCount, Is.InRange(0, CityLightingPresentation.MaximumVolumeSources));
            Assert.That(lighting.BounceRegionCount, Is.InRange(0, CityLightingPresentation.MaximumBounceRegions));
            var unique = new HashSet<Light>();
            for (int index = 0; index < lighting.VolumeSourceCount; index++)
            {
                Assert.That(lighting.TryGetVolumeSource(index, out SceneLocalLightSource source), Is.True);
                Assert.That(source.Light, Is.Not.Null);
                Assert.That(unique.Add(source.Light), Is.True, "A pooled source may appear only once in a camera snapshot.");
                Assert.That(lighting.IsVolumeAllowed(source.Light), Is.True);
                Assert.That(lighting.IsInsideVolumeSupport(index, source.Bounds.max + Vector3.one), Is.False,
                    "Scattering support must stop at the registered local bounds.");
                bool registered = false;
                for (int practical = 0; practical < lighting.PracticalCount; practical++)
                    if (lighting.TryGetPractical(practical, out Light actual) && actual == source.Light) registered = true;
                Assert.That(registered, Is.True, "Air follows an existing practical rather than a second light.");
            }
            for (int index = 0; index < lighting.BounceRegionCount; index++)
            {
                Assert.That(lighting.TryGetBounceRegion(index, out var region), Is.True);
                Assert.That(region.Source, Is.Not.Null);
                Assert.That(region.Visibility, Is.InRange(0f, 1f));
            }
        }

        private static void AssertCityLightingCanon(CityGameRoot city)
        {
            int forbiddenYardCount = 0;
            for (int index = 0; index < city.Lighting.PracticalCount; index++)
            {
                Assert.That(city.Lighting.TryGetPractical(index, out Light light), Is.True);
                if (light.name != "Home Yard Spotlight Light") continue;
                forbiddenYardCount++;
                Assert.That(city.Lighting.IsVolumeAllowed(light), Is.False, "The cold home/bar yard retains its no-volume exception.");
            }
            Assert.That(forbiddenYardCount, Is.GreaterThan(0), "The protected yard's real lamp must be covered by acceptance.");
            Assert.That(city.ParkChessLamp, Is.Not.Null);
            Light[] parkLights = city.World.ParkRoot.GetComponentsInChildren<Light>(true);
            foreach (Light light in parkLights)
                Assert.That(light.transform.IsChildOf(city.ParkChessLamp.transform), Is.True,
                    "The park keeps only its existing chess fixture.");
            Assert.That(city.ParkChessLamp.GetComponentsInChildren<Light>(true), Is.Not.Empty);
            // The presentation itself must never become another physical lamp.
            Assert.That(city.Lighting.GetComponents<Light>(), Is.Empty);
            Assert.That(city.Camera.farClipPlane, Is.EqualTo(48f).Within(.01f));
            Assert.That(RenderSettings.fog, Is.True);
        }

        private static IEnumerator CaptureCityLightingEffectPairs(CityGameRoot city, CityReflectionAcceptance reflections)
        {
            Light entrance = city.Night.Atmosphere.BarLights[0];
            Vector3 bulb = entrance.transform.position;
            Vector3 outward = city.Layout.BuildingLots[0].FacadeForward;
            foreach (BuildingLot lot in city.Layout.BuildingLots) if (lot.IsBar) { outward = lot.FacadeForward; break; }
            // The pool check deliberately ends at a distant fixture. Restore
            // the real observer before querying distance-owned factory lights.
            Shot machinery = Shot.At("Cannery machinery", city.Cannery.Plan.World(new Vector3(-1.1f, 1.9f, -2f)),
                city.Cannery.Plan.World(new Vector3(-5f, 1.4f, -.35f)), 64f);
            MoveCityLightingObserver(city, machinery);
            city.Cannery.RefreshPresentation();
            yield return null;
            Assert.That(city.Cannery.FactoryPresentationActive, Is.True);
            city.Lighting.PrepareForCamera(city.Camera);
            Shot metal = CityLitMetalPose(city, out Renderer metalReceiver, out Vector3 metalFace);
            yield return Pair("Reflections", metal, value => GraphicsEffectsSettings.CleanReflectionsEnabled = value);
            yield return Pair("Metal highlights", metal, value => GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = value);
            yield return Pair("Halation", Shot.At("Bar bulb", bulb + outward * 5f + Vector3.down * 1.5f, bulb, 64f),
                value => GraphicsEffectsSettings.CleanHalationEnabled = value);

            // Select near a normal street mast rather than the protected yard.
            Transform mast = city.Night.Atmosphere.LampAnchors[0];
            Shot air = Shot.At("Street air", mast.position + outward * 6f + Vector3.down * 2.5f,
                mast.position + Vector3.down * 1.5f, 64f);
            yield return Pair("Lit air", air, value => GraphicsEffectsSettings.CleanVolumeEnabled = value);
            Shot surface = Shot.At("Bar physical surfaces", bulb + outward * 3.5f + Vector3.down * 1.3f,
                bulb - outward * .25f + Vector3.down * 1.2f, 64f);
            MoveCityLightingObserver(city, surface);
            yield return null;
            city.Lighting.PrepareForCamera(city.Camera);
            Assert.That(city.Lighting.BounceRegionCount, Is.GreaterThan(0), "The bar's real practical must expose a receiver patch.");
            yield return Pair("Bounce", CityBounceReceiverPose(city, outward), value => GraphicsEffectsSettings.CleanBounceEnabled = value);

            IEnumerator Pair(string subject, Shot pose, Action<bool> apply)
            {
                MoveCityLightingObserver(city, pose);
                city.Cannery.RefreshPresentation();
                yield return null;
                double deadline = 0d;
                while (!CityReflectionsReady(city, ref deadline, reflections, "Effect " + subject)) yield return null;
                ApplyCityLightingPose(city.Camera, pose);
                city.Lighting.PrepareForCamera(city.Camera);
                AssertCitySelectedSources(city);
                Light[] lights = city.GetComponentsInChildren<Light>(true);
                var intensities = new float[lights.Length];
                for (int index = 0; index < lights.Length; index++) intensities[index] = lights[index].intensity;
                try
                {
                    // No yield between these renders: actor, rain, flicker and
                    // shader time are identical, so only this saved option differs.
                    apply(false);
                    if (subject == "Reflections" || subject == "Metal highlights")
                    {
                        city.Lighting.PrepareForCamera(city.Camera);
                        LogCityMaterialLighting(city, metalReceiver, metalFace, subject, false);
                    }
                    CaptureCurrentCamera(city.Camera, SceneIds.City, CityLightingFrame(subject + " baseline"));
                    apply(true);
                    if (subject == "Reflections" || subject == "Metal highlights")
                    {
                        city.Lighting.PrepareForCamera(city.Camera);
                        LogCityMaterialLighting(city, metalReceiver, metalFace, subject, true);
                    }
                    CaptureCurrentCamera(city.Camera, SceneIds.City, CityLightingFrame(subject + " enabled"));
                    for (int index = 0; index < lights.Length; index++)
                        Assert.That(lights[index].intensity, Is.EqualTo(intensities[index]),
                            "Presentation toggles preserve actual practical power: " + lights[index].name);
                }
                finally { apply(true); }
            }
        }

        private static Shot CityLitMetalPose(CityGameRoot city, out Renderer actualReceiver, out Vector3 actualFace)
        {
            Renderer selected = null;
            Light source = null;
            Vector3 target = default;
            float score = 0f;
            var scratch = new MaterialPropertyBlock();
            var lights = new List<Light>();
            for (int index = 0; index < city.Lighting.PracticalCount; index++)
                if (city.Lighting.TryGetPractical(index, out Light actual) && actual.isActiveAndEnabled &&
                    actual.name.StartsWith("Cannery ", StringComparison.Ordinal) && actual.intensity > .01f) lights.Add(actual);
            Vector3 reference = city.Cannery.Plan.World(new Vector3(-5f, 1.2f, -.35f));
            int receiverCount = 0, triangleCount = 0;
            foreach (Renderer renderer in city.Cannery.Equipment.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !CityMetalAppearance.TryGetResponse(renderer, scratch, out _)) continue;
                bool moving = false;
                for (Transform ancestor = renderer.transform; ancestor != city.Cannery.Equipment; ancestor = ancestor.parent)
                    if (ancestor.name.StartsWith("MOVE_", StringComparison.Ordinal)) { moving = true; break; }
                if (moving || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                receiverCount++;
                // Equipment is batched by material: its bounds span separate
                // machines and their empty gaps. Frame a measured real face,
                // rather than the empty centre of that combined mesh.
                PlayerScarfCollisionWorld.ReadMesh(filter.sharedMesh, out Vector3[] vertices, out int[] triangles);
                for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                {
                    Vector3 point = renderer.transform.TransformPoint((vertices[triangles[triangle]] +
                        vertices[triangles[triangle + 1]] + vertices[triangles[triangle + 2]]) / 3f);
                    float referenceDistance = (point - reference).sqrMagnitude;
                    if (referenceDistance > 9f || point.y < city.Cannery.Plan.Origin.y + .6f) continue;
                    triangleCount++;
                    foreach (Light light in lights)
                    {
                        Vector3 toReceiver = point - light.transform.position;
                        if (toReceiver.sqrMagnitude >= light.range * light.range || toReceiver.sqrMagnitude < .04f ||
                            light.type == LightType.Spot && Vector3.Dot(toReceiver.normalized, light.transform.forward) <
                            Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad)) continue;
                        float candidateScore = light.intensity / ((1f + toReceiver.sqrMagnitude) * (1f + referenceDistance * 4f));
                        if (candidateScore <= score) continue;
                        score = candidateScore; selected = renderer; source = light; target = point;
                    }
                }
            }
            Assert.That(selected, Is.Not.Null, "The effect comparison needs a real lit static machinery face: factoryVisible=" +
                city.Cannery.FactoryPresentationActive + "; activeCanneryLights=" + lights.Count +
                "; metalReceivers=" + receiverCount + "; nearbyFaces=" + triangleCount + ".");
            Vector3 eye = city.Cannery.Plan.World(new Vector3(-1.1f, 1.9f, -2f));
            Debug.Log("City metal comparison receiver: " + selected.name + "; source=" + source.name + "; actualFace=" + target);
            actualReceiver = selected;
            actualFace = target;
            return Shot.At("Lit metal receiver", eye, target, 48f);
        }

        private static void LogCityMaterialLighting(CityGameRoot city, Renderer receiver, Vector3 face, string effect, bool enabled)
        {
            var block = new MaterialPropertyBlock();
            receiver.GetPropertyBlock(block);
            string wide = "empty=" + block.isEmpty + ",mask=" + block.GetFloat("_ClearCoatMask") +
                ",contact=" + block.GetFloat("_ClearCoatSmoothness");
            block.Clear(); receiver.GetPropertyBlock(block, 0);
            string indexed = "empty=" + block.isEmpty + ",mask=" + block.GetFloat("_ClearCoatMask") +
                ",contact=" + block.GetFloat("_ClearCoatSmoothness");
            Material material = receiver.sharedMaterial;
            var reflection = city.Lighting.Reflection;
            string status = "City material pair '" + effect + "' enabled=" + enabled + "; receiver=" + receiver.name +
                "; rendererEnabled=" + receiver.enabled + "; forceOff=" + receiver.forceRenderingOff +
                "; active=" + receiver.gameObject.activeInHierarchy + "; parentAnimator=" + (receiver.GetComponentInParent<Animator>() != null) +
                "; wide[" + wide + "]; index0[" + indexed + "]; material=" + material.name +
                "; shader=" + material.shader.name + "; materialMask=" + material.GetFloat("_ClearCoatMask") +
                "; materialContact=" + material.GetFloat("_ClearCoatSmoothness") +
                "; metallic=" + material.GetFloat("_Metallic") + "; smoothness=" + material.GetFloat("_Smoothness") +
                "; environmentOff=" + material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF") +
                "; specularOff=" + material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF") +
                "; reflectionReceivers=" + reflection.ReceiverCount + "; selectedRegistered=" + reflection.IsRegisteredReceiver(receiver) +
                "; ready=" + reflection.AllReady +
                "; shaderReflectionCount=" + Shader.GetGlobalInt("_SceneSurfaceReflectionCount") + "; actualFace=" + face;
            for (int index = 0; index < reflection.ZoneCount; index++)
            {
                reflection.TryGetZone(index, out SceneReflectionCapture zone);
                status += "; zone " + index + " containsFace=" + zone.Bounds.Contains(face) +
                    ",gain=" + zone.CurrentGain + ",min=" + zone.Bounds.min + ",max=" + zone.Bounds.max;
            }
            Debug.Log(status);
        }

        private static Shot CityBounceReceiverPose(CityGameRoot city, Vector3 outward)
        {
            Renderer[] renderers = city.transform.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < city.Lighting.BounceRegionCount; index++)
            {
                Assert.That(city.Lighting.TryGetBounceRegion(index, out var patch), Is.True);
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        !CityMetalAppearance.Supports(renderer) || !renderer.bounds.Intersects(patch.Bounds) ||
                        renderer.bounds.max.y < patch.Center.y + .2f || renderer.bounds.size.sqrMagnitude > 100f) continue;
                    Vector3 target = renderer.bounds.ClosestPoint(patch.Center + Vector3.up * .6f);
                    if (!patch.Bounds.Contains(target) || target.y < patch.Center.y + .15f) continue;
                    Debug.Log("City bounce comparison receiver: " + renderer.name + "; source=" + patch.Source.name);
                    return Shot.At("Bar bounce receiver", target + outward * 2.2f + Vector3.up * .25f, target, 58f);
                }
            }
            Assert.Fail("A validated City bounce patch must reach a real raised wall or fixture; ground cannot reflect onto itself.");
            return default;
        }

        private static IEnumerator VerifyCityLightingOwnership(CityGameRoot city, CityReflectionAcceptance reflections)
        {
            Camera camera = city.Camera;
            var lighting = city.Lighting;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(lighting));
            var exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
            try
            {
                CaptureCurrentCamera(camera, SceneIds.City, CityLightingFrame("Excluded camera"));
                Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
                AssertCityLightingGlobalsCleared();
                Assert.That(lighting.Reflection.HasCaptureGeometry, Is.False,
                    "Excluding the owning camera must release its capture-layer lease immediately.");
                foreach (SceneReflectionCapture zone in CityReflectionSlots(city))
                    Assert.That(zone.IsReady, Is.False,
                        "Exclusion must invalidate the owned environments even when the renderer skips Prepare.");
            }
            finally { Object.DestroyImmediate(exclusion); }
            var pool = new List<Light>(city.Night.Atmosphere.StreetLightPool);
            Assert.That(city.EnterDormant(), Is.True);
            Assert.That(city.IsDormant, Is.True);
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null,
                "The resident exterior must stop presenting while its interior owns the view.");
            AssertCityLightingGlobalsCleared();
            foreach (SceneReflectionCapture zone in CityReflectionSlots(city))
                Assert.That(zone.IsReady, Is.False, "Dormancy must release the captured exterior environment.");
            yield return VerifyCityHomeBalcony(city, reflections);
            city.ResumeFromDormant();
            Assert.That(city.IsDormant, Is.False);
            Assert.That(city.Lighting, Is.SameAs(lighting));
            Assert.That(city.Night.Atmosphere.StreetLightPool, Is.EqualTo(pool));
            city.DayNight.enabled = false;
            city.Weather.enabled = false;
            city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
            RenderTexture previousTarget = city.Camera.targetTexture;
            var returnTarget = new RenderTexture(Width, Height, 24);
            try
            {
                city.Camera.targetTexture = returnTarget;
                // The main Capture helper has already restored its target.
                // Batch mode needs an explicit real-camera draw after waking
                // this exterior, just as it does for the additive balcony.
                for (int frame = 0; frame < 2; frame++)
                {
                    lighting.PrepareForCamera(city.Camera);
                    city.Camera.Render();
                    yield return null;
                }
                Assert.That(lighting.Reflection.CompletedEligibleCameraFrames, Is.EqualTo(2),
                    "The resumed City camera must complete its two real warmup frames.");
                double deadline = 0d;
                while (!CityReflectionsReady(city, ref deadline, reflections, "Resident return"))
                {
                    city.Camera.Render();
                    yield return null;
                }
                Assert.That(SceneLightingPresentation.Resolve(city.Camera), Is.SameAs(lighting));
                Assert.That(lighting.Reflection.CaptureFailureCount, Is.EqualTo(reflections.WarmupFailures),
                    "A resident return must not add failures after the initial camera warmup.");
                int captures = lighting.Reflection.CaptureCount;
                for (int frame = 0; frame < 12; frame++)
                {
                    lighting.PrepareForCamera(city.Camera);
                    city.Camera.Render();
                    yield return null;
                }
                Assert.That(lighting.Reflection.CaptureCount, Is.EqualTo(captures),
                    "Ordinary moving traffic must not invalidate a stationary static environment.");
                CaptureCurrentCamera(city.Camera, SceneIds.City, CityLightingFrame("Interior return"));
            }
            finally
            {
                city.Camera.targetTexture = previousTarget;
                returnTarget.Release();
                Object.DestroyImmediate(returnTarget);
            }
        }

        private static IEnumerator VerifyCityHomeBalcony(CityGameRoot city, CityReflectionAcceptance reflections)
        {
            GameSessionState.EnterHome();
            yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Additive);
            Scene interior = SceneManager.GetSceneByName(SceneIds.HomeInterior);
            Assert.That(interior.isLoaded, Is.True);
            Assert.That(SceneManager.SetActiveScene(interior), Is.True);
            // This is the same installer the resident-door transition calls
            // after making its additive target active.
            BarPromenadeRuntimeBootstrap.InstallForScene(interior);
            HomeInteriorRoot home = null;
            double deadline = Time.realtimeSinceStartupAsDouble + TimeoutSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized) break;
                yield return null;
            }
            Assert.That(home != null && home.IsInitialized, Is.True, "The real apartment must initialize behind the dormant City.");
            var homeSurfaces = new CitySurfaceAudit();
            var homeMaps = new Dictionary<Texture, CitySurfaceSheet>();
            foreach (CitySurfaceSheet sheet in CitySurfaceLibrary.Manifest.sheets)
                homeMaps.Add(Resources.Load<Texture2D>(sheet.albedo), sheet);
            CollectCitySurfaceBindings(home.ExteriorView.transform, homeMaps, homeSurfaces);
            Assert.That(homeSurfaces.Bindings, Is.Not.Empty, "The real balcony reconstruction must use refreshed City maps.");
            var exterior = home.ExteriorLighting;
            Assert.That(exterior, Is.Not.Null);
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            Assert.That(camera, Is.Not.Null);
            camera.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData);
            Debug.Log("City lighting Home camera: name=" + camera.name + "; scene=" + camera.gameObject.scene.name +
                "; enabled=" + camera.enabled + "; active=" + camera.gameObject.activeInHierarchy +
                "; instance=" + camera.GetEntityId() + "; display=" + camera.targetDisplay +
                "; type=" + camera.cameraType + "; renderType=" + (cameraData != null ? cameraData.renderType.ToString() : "none") +
                "; renderer=" + (cameraData != null && cameraData.scriptableRenderer != null ? cameraData.scriptableRenderer.GetType().Name : "none"));
            Assert.That(camera.cameraType, Is.EqualTo(CameraType.Game), "Home must adopt a real game camera rather than a dormant preview or reflection camera.");
            Assert.That(home.ExteriorAtmosphere.IsBalconyVisibilityActive, Is.False);
            Assert.That(exterior.enabled, Is.False, "The indoor camera must also release the exterior's capture ownership.");
            CaptureCurrentCamera(camera, SceneIds.HomeInterior, CityLightingFrame("Home indoor exclusion"));
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null,
                "City effects must stay absent from the apartment's indoor camera.");
            AssertCityLightingGlobalsCleared();

            Rect activation = home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(new Vector3(activation.center.x, home.Layout.PlayerSpawn.y, activation.center.y));
            yield return null;
            yield return null;
            Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Balcony));
            Assert.That(home.ExteriorAtmosphere.IsBalconyVisibilityActive, Is.True);
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(exterior));
            RenderTexture previousTarget = camera.targetTexture;
            var warmupTarget = new RenderTexture(Width, Height, 24);
            try
            {
                camera.targetTexture = warmupTarget;
                // Initial geometry upload belongs to scene warmup, before
                // timing the reflection requests that depend on those frames.
                for (int frame = 0; frame < 2; frame++)
                {
                    exterior.PrepareForCamera(camera);
                    camera.Render();
                    yield return null;
                }
                Assert.That(exterior.Reflection.CompletedEligibleCameraFrames, Is.EqualTo(2),
                    "The actual balcony camera must complete its two warmup frames.");
                deadline = Time.realtimeSinceStartupAsDouble + 25d;
                do
                {
                    exterior.PrepareForCamera(camera);
                    // The additive acceptance borrows the scene's actual
                    // camera without a window-driven frame. Render it across
                    // distinct engine frames to satisfy the real GPU warmup.
                    camera.Render();
                    reflections.ObserveWait(exterior.Reflection, camera, "Home balcony", "Reflection", exterior.Reflection.AllReady);
                    if (exterior.Reflection.AllReady) break;
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "The real balcony must acquire its own bounded City environment.");
                    yield return null;
                } while (true);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                warmupTarget.Release();
                Object.DestroyImmediate(warmupTarget);
            }
            string support = "Support=" + exterior.HasExteriorSupport + "; groundFaces=" + exterior.ExteriorGroundFaceCount +
                "; structures=" + exterior.ExteriorStructureCount + "; practicals=" + exterior.PracticalCount +
                "; selectedAir=" + exterior.VolumeSourceCount + "; selectedBounce=" + exterior.BounceRegionCount;
            Debug.Log("City lighting Home exterior: " + support);
            Assert.That(exterior.Reflection.CaptureCompletedCount, Is.GreaterThanOrEqualTo(exterior.Reflection.ZoneCount));
            Assert.That(exterior.Reflection.CaptureFailureCount, Is.Zero,
                "The warmed renderer must complete the balcony captures without a new failure.");
            Assert.That(home.ExteriorView.GetComponentsInChildren<Collider>(true), Is.Empty,
                "Balcony reflection geometry must preserve the collider-free exterior reconstruction.");
            Assert.That(exterior.VolumeSourceCount, Is.GreaterThan(0),
                "The real balcony must exercise exterior air sources before checking their indoor boundary. " + support);
            Assert.That(exterior.BounceRegionCount, Is.GreaterThan(0),
                "Collider-free reconstructed ground still supplies measured exterior bounce patches. " + support);
            float indoorX = PlayerHomeBalconyGeometry.HomeFacadeX - PlayerHomeBalconyGeometry.WallThickness - .15f;
            float indoorY = home.Layout.PlayerSpawn.y + 1f;
            Vector3[] indoorReceivers =
            {
                new Vector3(indoorX, indoorY, PlayerHomeBalconyGeometry.WindowCenterZ),
                new Vector3(indoorX, indoorY, PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector3(.5f, indoorY, -.5f)
            };
            for (int index = 0; index < exterior.VolumeSourceCount; index++)
            {
                Assert.That(exterior.TryGetVolumeSource(index, out SceneLocalLightSource source), Is.True);
                foreach (Vector3 receiver in indoorReceivers)
                    Assert.That(exterior.IsInsideVolumeSupport(index, receiver), Is.False,
                        "Exterior light air must stop before the real apartment's facade and indoor receivers: " + source.Light.name);
            }
            CaptureCurrentCamera(camera, SceneIds.HomeInterior, CityLightingFrame("Home balcony"));

            home.Player.Motor.Teleport(new Vector3(.5f, home.Layout.PlayerSpawn.y, -.5f));
            yield return null;
            yield return null;
            Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(exterior.enabled, Is.False);
            for (int index = 0; index < exterior.Reflection.ZoneCount; index++)
            {
                Assert.That(exterior.Reflection.TryGetZone(index, out SceneReflectionCapture released), Is.True);
                Assert.That(released.IsReady, Is.False, "Indoor return must release captured exterior resources.");
            }
            CaptureCurrentCamera(camera, SceneIds.HomeInterior, CityLightingFrame("Home indoor return"));
            homeSurfaces.AssertRestored();
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
            AssertCityLightingGlobalsCleared();
            Assert.That(SceneManager.SetActiveScene(city.Scene), Is.True);
            yield return SceneManager.UnloadSceneAsync(interior);
            yield return null;
            Assert.That(exterior == null, Is.True, "The apartment's optional City owner must unload independently of the resident City.");
        }

        private static bool CityReflectionsReady(CityGameRoot city, ref double deadline, CityReflectionAcceptance reflections, string subject)
        {
            if (deadline == 0d) deadline = Time.realtimeSinceStartupAsDouble + 25d;
            city.Lighting.PrepareForCamera(city.Camera);
            SceneReflectionCapture[] zones = CityReflectionSlots(city);
            reflections.ObserveWait(city.Lighting.Reflection, city.Camera, subject, "Reflection", city.Lighting.Reflection.AllReady);
            Assert.That(zones[0].CaptureInProgress && zones[1].CaptureInProgress, Is.False,
                "The two City buffers may submit only one GPU capture at once.");
            if (city.Lighting.Reflection.AllReady)
            {
                reflections.ObserveReady(city.Lighting.Reflection, zones);
                AssertCitySelectedSources(city);
                return true;
            }
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline),
                "The chosen local City environments must complete rather than silently accepting fallback.");
            return false;
        }

        private sealed class CityReflectionAcceptance
        {
            internal int WarmupFailures { get; private set; } = -1;
            private string waitSubject, waitPhase;
            private double waitStarted;
            private int waitCalls;
            private bool completionReported;

            internal void ObserveWait(CityReflectionController reflection, Camera camera, string subject, string phase, bool ready)
            {
                if (waitSubject != subject)
                {
                    waitSubject = subject; waitStarted = Time.realtimeSinceStartupAsDouble;
                    waitCalls = 0; completionReported = false; waitPhase = null;
                }
                waitCalls++;
                bool report = waitCalls == 1 || waitCalls == 120 || waitCalls == 600 || waitCalls == 900 ||
                    waitPhase != phase || ready && !completionReported;
                waitPhase = phase;
                if (!report) return;
                completionReported |= ready;
                string status = "City lighting wait '" + subject + "': phase=" + phase + "; ready=" + ready +
                    "; callbacks=" + waitCalls + "; elapsed=" + (Time.realtimeSinceStartupAsDouble - waitStarted).ToString("F2") +
                    "; frame=" + Time.frameCount + "; generation=" + reflection.LightingGeneration +
                    "; cameraFrames=" + reflection.CompletedEligibleCameraFrames + "; pending=" + reflection.SelectionPending +
                    "; geometry=" + reflection.HasCaptureGeometry + "; eye=" + camera.transform.position;
                for (int index = 0; index < reflection.ZoneCount; index++)
                {
                    reflection.TryGetZone(index, out SceneReflectionCapture zone);
                    status += "; slot " + index + " region=" + reflection.SelectedRegionIndex(index) +
                        ",ready=" + zone.IsReady + ",pending=" + zone.CaptureInProgress + ",submit=" + zone.CaptureCount +
                        ",complete=" + zone.CaptureCompletedCount + ",failure=" + zone.CaptureFailureCount +
                        ",timeout=" + zone.CaptureTimeoutCount + ",cancel=" + zone.CaptureCancelledCount;
                }
                Debug.Log(status);
            }

            internal void ObserveReady(CityReflectionController reflection, SceneReflectionCapture[] zones)
            {
                int timeouts = 0;
                foreach (SceneReflectionCapture zone in zones)
                {
                    Assert.That(zone.IsReady, Is.True, "Every selected City environment must be genuinely ready.");
                    Assert.That(zone.CaptureCompletedCount, Is.GreaterThan(0),
                        "A real filtered capture must complete for each reusable buffer.");
                    timeouts += zone.CaptureTimeoutCount;
                }
                if (WarmupFailures < 0)
                {
                    // Cold shader compilation and initial GPU uploads can
                    // exhaust one watchdog window before the renderer warms.
                    // Accept only that first recovered timeout, never a silent
                    // submission failure or an unready fallback environment.
                    Assert.That(reflection.CaptureFailureCount, Is.InRange(0, 1),
                        "Only one initial renderer warmup timeout may recover.");
                    Assert.That(reflection.CaptureFailureCount, Is.EqualTo(timeouts),
                        "The initial allowance covers a recovered timeout, not a failed submission.");
                    Assert.That(reflection.CaptureCompletedCount, Is.GreaterThanOrEqualTo(zones.Length));
                    Assert.That(reflection.CaptureCount, Is.GreaterThanOrEqualTo(reflection.CaptureCompletedCount + timeouts),
                        "A recovered timeout must be followed by a real retry and completion.");
                    WarmupFailures = reflection.CaptureFailureCount;
                    Debug.Log("City reflection warmup completed; recovered timeouts=" + WarmupFailures + ".");
                }
                Assert.That(reflection.CaptureFailureCount, Is.EqualTo(WarmupFailures),
                    "No capture failure may be added after the first complete City environment.");
            }
        }

        private static SceneReflectionCapture[] CityReflectionSlots(CityGameRoot city)
        {
            var zones = new SceneReflectionCapture[2];
            for (int index = 0; index < zones.Length; index++)
            {
                Assert.That(city.Lighting.Reflection.TryGetZone(index, out zones[index]), Is.True);
                Assert.That(zones[index], Is.Not.Null);
            }
            return zones;
        }

        private static void MoveCityLightingObserver(CityGameRoot city, Shot pose)
        {
            city.Player.Motor.Teleport(pose.Position - Vector3.up * EyeHeight);
            city.Night.Atmosphere.RefreshImmediate();
            ApplyCityLightingPose(city.Camera, pose);
            Physics.SyncTransforms();
        }

        private static void ApplyCityLightingPose(Camera camera, Shot pose)
        {
            camera.transform.SetPositionAndRotation(pose.Position,
                Quaternion.LookRotation((pose.Target - pose.Position).normalized, Vector3.up));
            camera.fieldOfView = pose.FieldOfView;
        }

        private static Shot RenameCityPose(string name, Shot shot) => Shot.At(name, shot.Position, shot.Target, shot.FieldOfView);
        private static string CityLightingFrame(string description) => "Test city lighting - " + description;

        private static void AssertCityLightingGlobalsCleared()
        {
            Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.Zero);
            Assert.That(Shader.GetGlobalInt("_AlpineLocalLightCount"), Is.Zero);
            Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
            Assert.That(Shader.GetGlobalVector("_WarehouseCleanEffects"), Is.EqualTo(Vector4.zero));
        }

        private static void SetCityNativeEffects(bool enabled)
        {
            GraphicsEffectsSettings.CleanReflectionsEnabled = enabled;
            GraphicsEffectsSettings.CleanVolumeEnabled = enabled;
            GraphicsEffectsSettings.CleanHalationEnabled = enabled;
            GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = enabled;
            GraphicsEffectsSettings.CleanBounceEnabled = enabled;
        }

        private static void AssertCityNativeShaders()
        {
            foreach (string name in new[] { "Bar Promenade/Lit", "Bar Promenade/City River Water",
                         "Hidden/BarPromenade/Alpine Village Local Lit Air", CitySurfaceLibrary.ShaderName, CitySurfaceLibrary.FoliageShaderName })
            {
                Shader shader = Shader.Find(name);
                Assert.That(shader, Is.Not.Null, name);
                Assert.That(shader.isSupported, Is.True, name);
#if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False, name);
#endif
            }
        }

        private static int CountCityLightingDifference(string before, string after)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
            var baseline = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var actual = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(baseline.LoadImage(File.ReadAllBytes(Path.Combine(folder, CityLightingFrame(before) + ".png"))), Is.True);
                Assert.That(actual.LoadImage(File.ReadAllBytes(Path.Combine(folder, CityLightingFrame(after) + ".png"))), Is.True);
                Color32[] a = baseline.GetPixels32(), b = actual.GetPixels32();
                int changed = 0;
                for (int index = 0; index < a.Length; index++)
                    if (Math.Abs(a[index].r - b[index].r) + Math.Abs(a[index].g - b[index].g) +
                        Math.Abs(a[index].b - b[index].b) >= 2) changed++;
                Debug.Log("City lighting pair '" + after + "': " + changed + " changed pixels.");
                return changed;
            }
            finally { Object.DestroyImmediate(baseline); Object.DestroyImmediate(actual); }
        }
    }
}
