using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class ChurchRemodelAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.ChurchAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.ChurchGardenAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator CaptureChurchRemodelAcceptance()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousDelta = Time.captureDeltaTime;
            CityGameRoot city = null;
            ChurchInteriorRoot interior = null;
            ChurchAssetRegistry exterior = null;
            var hiddenInterior = new List<Renderer>();
            var pausedClocks = new List<GameTimeRuntime>();
            VolumeProfile grayscaleProfile = null;
            GameObject grayscaleOwner = null;
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                SetCityNativeEffects(true);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    PauseChurchRemodelClock(pausedClocks);
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
                    city.DayNight.enabled = false;
                    city.Weather.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    exterior = city.World.Church.transform.parent.GetComponentInChildren<ChurchAssetRegistry>();
                    Assert.That(exterior, Is.Not.Null);
                    Assert.That(exterior.Kind, Is.EqualTo(ChurchAssetKind.Exterior));
                    CityChurchWorldBuilder.ValidateExteriorEntranceAnchor(exterior, city.World.ChurchPlan);
                    Vector3 entranceAnchor = exterior.EntranceAnchor.position;
                    Vector3 doorGround = city.World.ChurchPlan.DoorGroundPosition;
                    Assert.That(Vector2.Distance(new Vector2(entranceAnchor.x, entranceAnchor.z),
                        new Vector2(doorGround.x, doorGround.z)), Is.LessThan(.01f));
                    Assert.That(entranceAnchor.y - doorGround.y,
                        Is.EqualTo(CityChurchPlanner.FoundationTopAboveGround).Within(.001f),
                        "The visual threshold uses the model's foundation datum; the door dock uses physical ground.");
                    Assert.That(city.World.Church.transform.parent.Find("Church Door"), Is.Null,
                        "The imported model owns the door; there is no substitute primitive.");
                    Assert.That(city.World.Church.transform.parent.Find("Church Foundation"), Is.Null);
                    bool visibleDoor = false;
                    foreach (ChurchRendererBinding binding in exterior.RendererBindings)
                        if (binding.SourceName == "EXT_WestDoors")
                        {
                            Assert.That(binding.Renderer.enabled, Is.True);
                            visibleDoor = true;
                        }
                    Assert.That(visibleDoor, Is.True, "The authored metre-size door stays visible.");
                    ValidateChurchPavingSurfaces(city);
                    CityChurchCourtyardPlanner.ValidateOrThrow(city.Layout, city.World.ChurchPlan,
                        city.World.ChurchCourtyardPlan);
                    ValidateChurchGardenLights(city, false);
                    AssertChurchGardenGroundDraws(city);
                    grayscaleOwner = new GameObject("Church Acceptance Grayscale");
                    Volume volume = grayscaleOwner.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 10000f;
                    volume.weight = 0f;
                    grayscaleProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    grayscaleProfile.Add<ColorAdjustments>(true).saturation.Override(-100f);
                    volume.sharedProfile = grayscaleProfile;
                    return ChurchRemodelCourtyardShots(city, volume);
                });
                AssertChurchGardenFramesHaveNoErrorPink();
                // Walk the production controller around the preserved garden loop.
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.LightBeer, 0);
                city.Player.Motor.Teleport(city.World.ChurchPlan.ReturnPosition);
                foreach (Vector3 waypoint in CityChurchCourtyardPlanner.GetLoopWaypoints(city.World.ChurchCourtyardPlan))
                    yield return WalkChurchGardenLeg(city.Player.Motor, waypoint, "remodel-loop");
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.LightBeer, 0);
                SetChurchRemodelTime(12 * 60);
                city.Rain.SetIntensity(0f);
                city.Camera.GetComponent<PlayerCameraFollow>().enabled = true;
                yield return BeginChurchRemodelDoor(city.Player, city.World.Church);
                yield return WaitForChurchRemodel(() =>
                {
                    interior = Object.FindAnyObjectByType<ChurchInteriorRoot>();
                    return interior != null && interior.IsInitialized && !SceneTransitionService.IsTransitioning;
                }, "The real church entrance did not reach its interior.");
                PauseChurchRemodelClock(pausedClocks);
                interior.CameraFollow.enabled = false;
                interior.DayNight.enabled = false;
                interior.Player.Motor.SetInputEnabled(false);
                AssertChurchRemodelMetricContract(interior);
                yield return WalkChurchRemodelInterior(interior);
                foreach (Renderer renderer in interior.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hiddenInterior.Add(renderer); }
                foreach (Shot shot in ChurchRemodelInteriorShots())
                    yield return CaptureChurchRemodelInteriorView(interior, shot, false);
                SetChurchRemodelTime(21 * 60);
                interior.DayNight.RefreshImmediate();
                foreach (Shot shot in new[]
                {
                    Shot.At("church-night-nave", new Vector3(0f, EyeHeight, -8.2f), new Vector3(0f, 2.4f, 8.8f), 68f),
                    Shot.At("church-night-candles", new Vector3(1f, EyeHeight, 4.9f), new Vector3(3.2f, 1.2f, 6.3f), 58f)
                }) yield return CaptureChurchRemodelInteriorView(interior, shot, false);
                yield return CaptureChurchRemodelInteriorView(interior,
                    Shot.At("church-night-grayscale", new Vector3(0f, EyeHeight, -8.2f), new Vector3(0f, 2.4f, 8.8f), 68f), true);
                GameSessionState.UpdateDrinkingProgress(100, DrinkId.LightBeer, 1);
                yield return CaptureChurchRemodelInteriorView(interior,
                    Shot.At("church-night-intoxication", new Vector3(0f, EyeHeight, -8.2f), new Vector3(0f, 2.4f, 8.8f), 68f), false, 90);
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.LightBeer, 0);
                AssertChurchRemodelLightingOwnership(interior);
                foreach (Renderer renderer in hiddenInterior) if (renderer != null) renderer.enabled = true;
                hiddenInterior.Clear();
                interior.CameraFollow.enabled = true;
                yield return BeginChurchRemodelDoor(interior.Player, interior.Exit);
                yield return WaitForChurchRemodel(() =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !SceneTransitionService.IsTransitioning;
                }, "The church exit did not return to City.");
                Vector3 expected = city.World.ChurchPlan.ReturnPosition;
                Vector3 actual = city.Player.GameObject.transform.position;
                Assert.That(Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(expected.x, expected.z)),
                    Is.LessThan(.06f), "Exit returns to the same western forecourt.");
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
                Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.Zero);
                Assert.That(Shader.GetGlobalInt("_MothersHouseAirSourceCount"), Is.Zero);
                Debug.Log("CHURCH REMODEL ACCEPTANCE OK: exact imported metre envelope, shared door/window frame, human furniture, clear garden/interior routes, actual door roundtrip, bounded shared lighting and day/night/weather/state views.");
            }
            finally
            {
                foreach (Renderer renderer in hiddenInterior) if (renderer != null) renderer.enabled = true;
                foreach (GameTimeRuntime clock in pausedClocks) if (clock != null) clock.enabled = true;
                if (grayscaleOwner != null) Object.DestroyImmediate(grayscaleOwner);
                if (grayscaleProfile != null) Object.DestroyImmediate(grayscaleProfile);
                if (city != null) { city.Player.Motor.SetInputEnabled(true); city.DayNight.enabled = true; city.Weather.enabled = true; }
                if (interior != null) { interior.Player.Motor.SetInputEnabled(true); interior.CameraFollow.enabled = true; interior.DayNight.enabled = true; }
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static void AssertChurchGardenGroundDraws(CityGameRoot city)
        {
            var properties = new MaterialPropertyBlock();
            int checkedRenderers = 0;
            foreach (Renderer renderer in city.World.ChurchCourtyardRoot.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null ||
                        material.shader.name != "Bar Promenade/Layered Ground") continue;
                    renderer.GetPropertyBlock(properties);
                    Assert.That(properties.isEmpty, Is.False,
                        renderer.name + " must use the supported per-renderer ground-weather path.");
                    Assert.That(properties.GetFloat("_GroundCityClimate"), Is.EqualTo(1f), renderer.name);
                    checkedRenderers++;
                    break;
                }
            Assert.That(checkedRenderers, Is.GreaterThan(0), "The actual garden ground-material donors must be covered.");
        }

        private static void AssertChurchGardenFramesHaveNoErrorPink()
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                foreach (string shot in new[] { "day-church-garden-00-overview", "day-church-garden-02-fountain",
                    "day-church-garden-04-statue", "night-church-garden" })
                {
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City, shot + ".png");
                    Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True, shot);
                    Assert.That(image.width, Is.EqualTo(Width));
                    Assert.That(image.height, Is.EqualTo(Height));
                    int errorPixels = 0;
                    // The fixed stone/clay/timber garden palette has no saturated
                    // magenta. A broad block survives fog/grade on a failed BRG
                    // draw; allow a few coloured-glass/AA pixels in the distance.
                    foreach (Color32 pixel in image.GetPixels32())
                        if (pixel.r > 160 && pixel.b > 160 && pixel.g < 140 &&
                            pixel.r - pixel.g > 65 && pixel.b - pixel.g > 65 &&
                            Mathf.Abs(pixel.r - pixel.b) < 32) errorPixels++;
                    Assert.That(errorPixels, Is.LessThan(128), shot + " contains a shader-error magenta block.");
                }
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static Shot[] ChurchRemodelCourtyardShots(CityGameRoot city, Volume gray)
        {
            var shots = new List<Shot>();
            Shot[] garden = ChurchGardenShots(city);
            foreach (Shot pose in garden) Add("day-" + pose.Name, pose, false);
            Vector3 door = city.World.ChurchPlan.DoorGroundPosition;
            Add("day-church-door-contact", Shot.At("door", door + new Vector3(-2.5f, 1.1f, -2f), door + Vector3.up * 1.2f, 56f), false);
            Add("day-church-upper-windows", Shot.At("upper-windows",
                door + city.World.ChurchPlan.EntranceOutwardDirection * 8f + Vector3.up * 4f,
                door + Vector3.up * 9.3f, 50f), false);
            Add("night-church-garden", garden[0], true);
            Add("night-church-door", garden[1], true);
            Add("night-church-rain", garden[1], true, rain: true);
            Add("night-church-grayscale", garden[0], true, grayscale: true);
            Add("night-church-intoxication", garden[1], true, intoxicated: true);
            return shots.ToArray();
            void Add(string name, Shot pose, bool night, bool rain = false, bool grayscale = false, bool intoxicated = false)
            {
                int frames = 0;
                shots.Add(Shot.At(name, pose.Position, pose.Target, pose.FieldOfView, readyWhen: () =>
                {
                    if (frames++ == 0)
                    {
                        if (night) { SetChurchRemodelTime(21 * 60); city.DayNight.ApplyCurrentTime(true); }
                        gray.weight = grayscale ? 1f : 0f;
                        city.Rain.SetIntensity(rain ? 1f : 0f);
                        CityWaterResources.SetRainIntensity(rain ? 1f : 0f);
                        CityWetSurfaceRegistry.SetImmediate(rain ? 1f : 0f);
                        GameSessionState.UpdateDrinkingProgress(intoxicated ? 100 : 0, DrinkId.LightBeer, intoxicated ? 1 : 0);
                        MoveCityLightingObserver(city, pose);
                    }
                    Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(12));
                    Assert.That(city.Lighting.BounceRegionCount, Is.LessThanOrEqualTo(4));
                    return frames > (intoxicated ? 90 : rain ? 30 : SettleFrames);
                }));
            }
        }

        private static Shot[] ChurchRemodelInteriorShots() => new[]
        {
            Shot.At("church-day-narthex-to-altar", new Vector3(0f, EyeHeight, -10.1f), new Vector3(0f, 2.5f, 8.8f), 68f),
            Shot.At("church-day-nave-vault", new Vector3(0f, EyeHeight, -5.2f), new Vector3(0f, 5.5f, 3f), 72f),
            Shot.At("church-day-north-aisle", new Vector3(-4.6f, EyeHeight, -6.6f), new Vector3(-4.8f, 2f, 6f), 64f),
            Shot.At("church-day-south-aisle", new Vector3(4.6f, EyeHeight, -6.6f), new Vector3(4.8f, 2f, 6f), 64f),
            Shot.At("church-day-confessional", new Vector3(2.8f, EyeHeight, 4.8f), new Vector3(4.95f, 1.2f, 6.55f), 60f),
            Shot.At("church-day-altar-rail", new Vector3(0f, EyeHeight, 6.7f), new Vector3(0f, 2.4f, 10f), 64f),
            Shot.At("church-day-organ-and-font", new Vector3(0f, EyeHeight, -6.9f), new Vector3(0f, 3f, -11.2f), 74f),
            Shot.At("church-day-west-rose-and-organ", new Vector3(2f, EyeHeight, -4f), new Vector3(0f, 4.6f, -11.8f), 68f),
            Shot.At("church-day-door-threshold", new Vector3(1.7f, 1.2f, -10.2f), new Vector3(0f, 1.1f, -12.1275f), 56f)
        };

        private static IEnumerator CaptureChurchRemodelInteriorView(ChurchInteriorRoot interior, Shot shot, bool gray, int frames = SettleFrames)
        {
            Camera camera = Camera.main;
            GameObject owner = null;
            VolumeProfile profile = null;
            try
            {
                if (gray)
                {
                    owner = new GameObject("Church Interior Grayscale");
                    Volume volume = owner.AddComponent<Volume>();
                    volume.isGlobal = true; volume.priority = 10000f;
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    profile.Add<ColorAdjustments>(true).saturation.Override(-100f);
                    volume.sharedProfile = profile;
                }
                camera.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.Target - shot.Position));
                camera.fieldOfView = shot.FieldOfView;
                for (int frame = 0; frame < frames; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.ChurchInterior, shot.Name);
            }
            finally { if (owner != null) Object.DestroyImmediate(owner); if (profile != null) Object.DestroyImmediate(profile); }
        }

        private static void AssertChurchRemodelMetricContract(ChurchInteriorRoot interior)
        {
            ChurchInteriorLayoutPlan plan = interior.Layout;
            ChurchInteriorLayoutValidator.ValidateOrThrow(plan);
            Assert.That(plan.RoomSize.x, Is.LessThan(CityChurchPlanner.ModelWidth));
            Assert.That(plan.RoomSize.y, Is.EqualTo(CityChurchPlanner.ModelLength).Within(.001f));
            Assert.That(interior.World.Model.localScale, Is.EqualTo(Vector3.one));
            Bounds measured = new Bounds();
            bool first = true;
            foreach (Renderer renderer in interior.World.Registry.Renderers)
            {
                if (first) { measured = renderer.bounds; first = false; }
                else measured.Encapsulate(renderer.bounds);
            }
            Assert.That(first, Is.False);
            Assert.That(Vector3.Distance(measured.min, plan.ModelLocalBounds.min), Is.LessThan(.04f));
            Assert.That(Vector3.Distance(measured.max, plan.ModelLocalBounds.max), Is.LessThan(.04f));
            Assert.That(plan.Contains(new Vector3(5f, 0f, -10f)), Is.False);
            // City may be dormant or unloaded after entry, so compare against
            // the prefab's metre frame instead of retaining a destroyed scene object.
            ChurchAssetRegistry facade = ChurchResources.LoadExteriorPrefab().GetComponent<ChurchAssetRegistry>();
            ChurchAssetRegistry room = interior.World.Registry;
            Transform door = ChurchRemodelAnchor(room, "ANCHOR_Interior.Door");
            Vector3 localDoor = room.transform.InverseTransformPoint(door.position);
            Vector3 sourceDoor = facade.transform.InverseTransformPoint(facade.EntranceAnchor.position) * CityChurchPlanner.ExteriorModelScale;
            Assert.That(Vector3.Distance(ChurchInteriorLayoutPlanner.ToExteriorMetricPosition(localDoor), sourceDoor), Is.LessThan(.01f));
            foreach (string side in new[] { "North", "South" })
                for (int index = 0; index < ChurchInteriorLayoutPlanner.WindowDepths.Length; index++)
                {
                    string suffix = "Window." + side + "." + index;
                    Vector3 inner = room.transform.InverseTransformPoint(ChurchRemodelAnchor(room, "ANCHOR_Interior." + suffix).position);
                    Vector3 outer = facade.transform.InverseTransformPoint(ChurchRemodelAnchor(facade, "ANCHOR_Exterior." + suffix).position) * CityChurchPlanner.ExteriorModelScale;
                    Assert.That(Vector3.Distance(ChurchInteriorLayoutPlanner.ToExteriorMetricPosition(inner), outer), Is.LessThan(.01f), suffix);
                    Assert.That(Mathf.Abs(inner.x), Is.EqualTo(ChurchInteriorLayoutPlanner.WindowApertureX).Within(.001f));
                    Assert.That(inner.y, Is.EqualTo(ChurchInteriorLayoutPlanner.WindowCenterY).Within(.001f));
                }
            Vector3 rose = room.transform.InverseTransformPoint(ChurchRemodelAnchor(room, "ANCHOR_Interior.Rose").position);
            Vector3 roseEdge = room.transform.InverseTransformPoint(ChurchRemodelAnchor(room, "ANCHOR_Interior.Rose.Radius").position);
            Vector3 exteriorRose = facade.transform.InverseTransformPoint(ChurchRemodelAnchor(facade, "ANCHOR_Exterior.Rose").position)
                * CityChurchPlanner.ExteriorModelScale;
            Vector3 exteriorRoseEdge = facade.transform.InverseTransformPoint(ChurchRemodelAnchor(facade, "ANCHOR_Exterior.Rose.Radius").position)
                * CityChurchPlanner.ExteriorModelScale;
            Assert.That(Vector3.Distance(rose, ChurchInteriorLayoutPlanner.RoseCenter), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(ChurchInteriorLayoutPlanner.ToExteriorMetricPosition(rose), exteriorRose), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(ChurchInteriorLayoutPlanner.ToExteriorMetricPosition(roseEdge), exteriorRoseEdge), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(rose, roseEdge) * 2f,
                Is.EqualTo(ChurchInteriorLayoutPlanner.RoseOpeningDiameter).Within(.001f));
            Assert.That(Vector3.Distance(exteriorRose, exteriorRoseEdge) * 2f,
                Is.EqualTo(ChurchInteriorLayoutPlanner.RoseOpeningDiameter).Within(.001f));
            Assert.That(facade.Dimensions.DoorWidth * CityChurchPlanner.ExteriorModelScale,
                Is.EqualTo(room.Dimensions.DoorWidth).Within(.001f));
            Assert.That(facade.Dimensions.DoorHeight * CityChurchPlanner.ExteriorModelScale,
                Is.EqualTo(room.Dimensions.DoorHeight).Within(.001f));
        }

        private static Transform ChurchRemodelAnchor(ChurchAssetRegistry registry, string name)
        {
            foreach (Transform transform in registry.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
            Assert.Fail("Missing church semantic anchor: " + name);
            return null;
        }

        private static IEnumerator WalkChurchRemodelInterior(ChurchInteriorRoot interior)
        {
            PlayerMotor motor = interior.Player.Motor;
            foreach (ChurchInteriorPathPlan path in interior.Layout.Paths)
            {
                bool alongZ = path.Bounds.height > path.Bounds.width;
                Vector3 first = new Vector3(alongZ ? path.Bounds.center.x : path.Bounds.xMin + .35f,
                    PlayerFactory.GroundedRootOffset, alongZ ? path.Bounds.yMin + .35f : path.Bounds.center.y);
                Vector3 last = new Vector3(alongZ ? path.Bounds.center.x : path.Bounds.xMax - .35f,
                    PlayerFactory.GroundedRootOffset, alongZ ? path.Bounds.yMax - .35f : path.Bounds.center.y);
                motor.Teleport(first);
                Physics.SyncTransforms();
                yield return WalkChurchGardenLeg(motor, last, path.Id);
            }
            motor.Teleport(new Vector3(0f, PlayerFactory.GroundedRootOffset, 7.4f));
            Physics.SyncTransforms();
            bool reachedSanctuary = false;
            for (int frame = 0; frame < 15; frame++)
            {
                reachedSanctuary |= motor.MoveTowardsApproachWaypoint(new Vector3(0f, 0f, 9f), .1f, .25f);
                yield return null;
            }
            motor.CancelInteractionPoseMove();
            Assert.That(reachedSanctuary, Is.False);
            Assert.That(motor.transform.position.z, Is.LessThan(interior.Layout.WalkableBounds.yMax));
            motor.TeleportPlanar(new Vector3(-5f, 0f, -10f), 0f);
            yield return null;
            Assert.That(interior.Layout.Contains(motor.transform.position, .2f), Is.True,
                "The narrower narthex remains the navigation boundary.");
            motor.Teleport(interior.Layout.PlayerSpawn);
        }

        private static void AssertChurchRemodelLightingOwnership(ChurchInteriorRoot interior)
        {
            ChurchInteriorAtmosphere atmosphere = interior.Atmosphere;
            Camera camera = Camera.main;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(atmosphere));
            Assert.That(atmosphere.BounceRegionCount, Is.LessThanOrEqualTo(4));
            Assert.That(atmosphere.InteriorVolumeSourceCount, Is.LessThanOrEqualTo(2));
            Assert.That(atmosphere.InteriorVolumeBlockerCount, Is.LessThanOrEqualTo(24));
            Assert.That(atmosphere.HalationSourceCount, Is.LessThanOrEqualTo(16));
            Assert.That(atmosphere.Reflection, Is.Not.Null);
            Assert.That(atmosphere.Reflection.ReceiverCount, Is.GreaterThan(0));
            Assert.That(atmosphere.Reflection.ReceiverCount,
                Is.LessThanOrEqualTo(interior.World.Registry.Renderers.Count));
            Assert.That(atmosphere.Reflection.CaptureCompletedCount,
                Is.LessThanOrEqualTo(atmosphere.Reflection.CaptureCount));
            GraphicsEffectsSettings.CleanBounceEnabled = false;
            atmosphere.PrepareForCamera(camera);
            Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
            GraphicsEffectsSettings.CleanBounceEnabled = true;
            atmosphere.PrepareForCamera(camera);
            atmosphere.enabled = false;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
            atmosphere.enabled = true;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(atmosphere));
        }

        private static IEnumerator BeginChurchRemodelDoor(PlayerRuntime player, Component component)
        {
            PlayerDoorActionTarget action = component.GetComponent<PlayerDoorActionTarget>();
            Assert.That(action, Is.Not.Null);
            player.Motor.Teleport(action.Plan.EntryRootPosition);
            player.GameObject.transform.rotation = action.Plan.EntryRotation;
            player.Motor.SetInputEnabled(true);
            Physics.SyncTransforms();
            if (component is ChurchEntrance entrance)
            {
                Assert.That(entrance.CanInteract(player.Interactor), Is.True);
                entrance.Interact(player.Interactor);
            }
            else
            {
                ChurchExit exit = (ChurchExit)component;
                Assert.That(exit.CanInteract(player.Interactor), Is.True);
                exit.Interact(player.Interactor);
            }
            yield return WaitForChurchRemodel(() => SceneTransitionService.IsTransitioning,
                "The church door action did not complete.");
        }

        private static IEnumerator WaitForChurchRemodel(Func<bool> ready, string failure)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, failure);
        }
        private static void PauseChurchRemodelClock(List<GameTimeRuntime> paused)
        {
            GameTimeRuntime clock = Object.FindAnyObjectByType<GameTimeRuntime>();
            if (clock != null && clock.enabled) { clock.enabled = false; paused.Add(clock); }
        }
        private static void SetChurchRemodelTime(int minute)
        {
            double delta = minute - GameSessionState.GameTimeOfDayMinutes;
            if (delta < 0) delta += 24 * 60;
            GameSessionState.AdvanceGameTime((float)(delta / GameTimeState.GameMinutesPerRealSecond));
        }
    }
}
