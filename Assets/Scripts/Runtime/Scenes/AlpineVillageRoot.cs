using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// Runtime composition root for the separately loaded village above the
    /// cableway. Built line for line on <see cref="MountainRoadRoot"/>, which
    /// is the working shape for an outdoor area: it reconstructs only pure map
    /// data for the tabs it is not standing in, and keeps no other area's world
    /// alive behind it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public AlpineVillagePlan Plan { get; private set; }
        public AlpineVillageWorldResult World { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public AlpineVillageSoundscape Soundscape { get; private set; }

        /// <summary>The dense, wind-stretched snowfall through the full
        /// camera volume.</summary>
        public CityRainField Snow { get; private set; }

        /// <summary>Terrain-hugging spindrift that makes each gale readable
        /// against the lane instead of only against the sky.</summary>
        public AlpineVillageStormField BlowingSnow { get; private set; }

        /// <summary>The shared mountain-air bed, driven from the village's
        /// already-normalized gale rather than the road's tree sway.</summary>
        public MountainRoadWindSoundPlayer WindSound { get; private set; }

        /// <summary>The city's drifting fog sheets, unchanged. The village
        /// argues with the city in COLOUR - its Exp2 haze is the one warm one
        /// in the game - and not in whether the air has anything in it.
        /// </summary>
        public CityFogField Fog { get; private set; }

        public AlpineVillageWeatherShaper WeatherShaper { get; private set; }
        public CityWeatherController Weather { get; private set; }
        public InteractionPromptView InteractionPrompt { get; private set; }
        public IntoxicationHudView IntoxicationHud { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }

        public CityMapController Map { get; private set; }
        public MinigameDebugWindow DebugWindow { get; private set; }
        public InventoryController Inventory { get; private set; }
        public JournalController Journal { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }
        public AreaArrivalToken ArrivalToken { get; private set; }
        public bool HadAreaArrival { get; private set; }

        /// <summary>The offer to board the cabin back down.</summary>
        public AlpineCablewayCabinSeat CabinSeat { get; private set; }

        /// <summary>The descent, while it is being ridden - and, on a visit
        /// that arrived by cabin, the arrival that put him here.</summary>
        public AlpineCablewayRideController CablewayRide
        {
            get;
            private set;
        }

        /// <summary>
        /// How far the village has gone out, `0` warm and `1` an ordinary
        /// mountain village at dusk.
        ///
        /// Nothing drives this yet and it must stay at zero until the prologue
        /// exists. It is here now, wired through the lighting apply rather
        /// than written over it from outside, because that is the one shape
        /// that survives: the atmosphere re-applies the grade every game
        /// minute, and the mountain road already paid for learning that.
        /// </summary>
        public float WarmthGrade { get; private set; }

        /// <summary>
        /// How far the gale has closed the haze this frame, `0` the base
        /// density and `1` the storm peak. Smoothed from
        /// <see cref="StormWaveTarget"/> on `Time.deltaTime`.
        ///
        /// It is keyed on the RAW shared gust rhythm and not on the shaped
        /// gale pulse the spindrift reads, because the shaped strength
        /// saturates: in a thunderstorm slot the pulse's trough is `0.72` at
        /// the lane foot and pinned at `1` at the head for the whole ninety
        /// minutes, and a wave on it would close the lane and never reopen
        /// it. On the raw rhythm the trough returns to the base by
        /// construction, so the top house comes back every cycle.
        /// </summary>
        public float StormWave { get; private set; }

        /// <summary>Where the wave is heading: the raw gust through
        /// <see cref="AlpineVillageStormFieldRules.EvaluateStormWaveTarget"/>.
        /// </summary>
        public float StormWaveTarget { get; private set; }

        private Camera areaCamera;
        private int appliedAtmosphereDay = int.MinValue;
        private int appliedAtmosphereMinute = int.MinValue;
        private MaterialPropertyBlock warmthProperties;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            GameAudioMixer.ApplyProfile(GameAudioProfile.City);
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Stopwatch timer = Stopwatch.StartNew();
            areaCamera = RuntimeSceneSetup.EnsureAlpineVillage();
            Audio = RetroAudioService.EnsureInstalled();
            Plan = AlpineVillagePlanner.Create(GameSessionState.CitySeed);
            World = AlpineVillageWorldBuilder.Build(transform, Plan);

            // The loading service arms this before destination activation, so
            // consume it before any spawn decision or PlayerFactory call.
            HadAreaArrival = AreaTravelService.TryConsumeArrival(
                GameAreaId.AlpineVillage,
                out AreaArrivalToken token,
                out Vector3 arrivalPoint,
                out bool hasArrivalPoint);
            ArrivalToken = HadAreaArrival
                ? token
                : AreaArrivalToken.Default;

            var ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPrompt = ui.AddComponent<InteractionPromptView>();
            IntoxicationHud = ui.AddComponent<IntoxicationHudView>();

            Vector3 spawnPosition = Plan.SpawnPosition +
                                    Vector3.up *
                                    PlayerFactory.GroundedRootOffset;
            string spawnSource = "lane_foot";

            if (HadAreaArrival && ArrivalToken == AreaArrivalToken.Cableway)
            {
                // He arrives in the cabin and the ride seats him, but he has
                // to be built somewhere first - put him on the platform he is
                // about to step onto, so a seat that fails leaves him beside
                // the station rather than up the lane.
                spawnPosition = Plan.Station.BoardingDockPosition +
                                Vector3.up * PlayerFactory.GroundedRootOffset;
                spawnSource = "cableway_platform";
            }

            if (HadAreaArrival &&
                hasArrivalPoint &&
                new CityMapAlpineVillageTeleportGround(World.WalkableArea)
                    .TryClampArrival(
                        arrivalPoint,
                        out Vector3 pointSpawn))
            {
                // The map asked for a place, not for the area. A point it
                // cannot hold falls back to the station rather than dropping
                // the hero into the slope.
                spawnPosition = pointSpawn;
                spawnSource = "map_point";
            }

            GameLog.Info(
                "alpine_village",
                "spawn_selected",
                GameLog.Field("source", spawnSource),
                GameLog.Field("arrival", ArrivalToken.ToString()),
                GameLog.Field("x", spawnPosition.x),
                GameLog.Field("y", spawnPosition.y),
                GameLog.Field("z", spawnPosition.z));
            Player = PlayerFactory.Create(
                transform,
                spawnPosition,
                areaCamera,
                World.WalkableArea,
                InteractionPrompt);

            // Facing up the lane. The composition only works from the bottom
            // looking up, and that is the first thing an arrival should see.
            //
            // EXCEPT off the cabin, where he is standing on a `1.37 m` strip
            // with a `0.64 m` drop off both long sides. The lane bears
            // `19.9°` off the flight's own axis here, so pointing him up it
            // walks him off the side of his own staircase after two metres and
            // `0.48 m` down onto the pad - which he then cannot climb back.
            // From the cabin he is turned down the steps; the lane is what he
            // sees the moment he is off them.
            Vector3 facing = ArrivalToken == AreaArrivalToken.Cableway
                ? -Plan.Station.Cableway.LineForward
                : Plan.SpawnForward;
            Player.GameObject.transform.rotation = Quaternion.LookRotation(
                facing,
                Vector3.up);

            CameraFollow = areaCamera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow = areaCamera.gameObject
                    .AddComponent<PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                areaCamera,
                Player.GameObject.transform,
                false);
            BuildAtmosphere();
            BuildCableway();
            BuildCommonUi(ui);
            ApplyCurrentAtmosphere(true);
            ApplyVisibility();
            IsInitialized = true;

            timer.Stop();
            GameLog.Info(
                "alpine_village",
                "initialize_completed",
                GameLog.Field("duration_ms", timer.ElapsedMilliseconds),
                GameLog.Field("seed", Plan.Seed),
                GameLog.Field("lane_length", Plan.Lane.Length),
                GameLog.Field("lane_climb", Plan.Lane.ElevationGain),
                GameLog.Field("lane_grade", Plan.Lane.AverageGrade),
                GameLog.Field("plot_count", Plan.Plots.Count),
                GameLog.Field("arrival", ArrivalToken.ToString()));
        }

        /// <summary>
        /// Sets how far the village has gone out and re-applies the grade at
        /// once, so a caller never has to know that the atmosphere owns the
        /// clock.
        /// </summary>
        public void SetWarmthGrade(float grade)
        {
            WarmthGrade = Mathf.Clamp01(grade);
            Soundscape?.SetWarmthGrade(WarmthGrade);
            ApplyCurrentAtmosphere(true);
            ApplyVisibility();
        }

        /// <summary>
        /// The per-minute half of the atmosphere: lighting and the warmth
        /// presentation. Visibility is NOT here any more - it moves every
        /// frame with the storm wave and has its own writer,
        /// <see cref="ApplyVisibility"/>.
        /// </summary>
        private void ApplyCurrentAtmosphere(bool force = false)
        {
            if (areaCamera == null)
            {
                return;
            }

            int day = GameSessionState.GameDayIndex;
            int minute = GameSessionState.GameMinuteOfDay;
            if (!force &&
                day == appliedAtmosphereDay &&
                minute == appliedAtmosphereMinute)
            {
                return;
            }

            RuntimeSceneSetup.ApplyAlpineVillageLighting(
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes),
                WarmthGrade,
                force);
            ApplyVillageWarmthPresentation();
            appliedAtmosphereDay = day;
            appliedAtmosphereMinute = minute;
        }

        /// <summary>
        /// The one writer of the village's fog, background, far plane and
        /// the wall's own haze term, every frame. The wall is told what was
        /// just written to `RenderSettings` rather than re-deriving it, so
        /// the two can never disagree by a frame; and this stays on while
        /// the hero rides - the ride's fade covers whatever the wave does.
        /// </summary>
        private void ApplyVisibility()
        {
            if (areaCamera == null)
            {
                return;
            }

            RuntimeSceneSetup.ApplyAlpineVillageVisibility(
                areaCamera,
                WarmthGrade,
                StormWave);
            AlpineVillageRidgeAppearance.SetHaze(
                RenderSettings.fogColor,
                RenderSettings.fogDensity);
        }

        /// <summary>
        /// The prologue will drive one value, and this one pass turns it into
        /// the four quiet losses the bibles name: fewer cords, darker rooms,
        /// dirtier snow and weaker practicals. At the current playable
        /// baseline the value remains zero, so every window is still warm.
        /// </summary>
        private void ApplyVillageWarmthPresentation()
        {
            if (World == null || World.Root == null)
            {
                return;
            }

            if (warmthProperties == null)
            {
                warmthProperties = new MaterialPropertyBlock();
            }

            Renderer[] renderers = World.Root.GetComponentsInChildren<
                Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer.name == "Lit Window")
                {
                    float power = EvaluateWindowWarmth(renderer);
                    ApplyWarmthColor(
                        renderer,
                        ScaleRgb(
                            AlpineVillageWorldBuilder.WarmWindowTint,
                            power));
                    continue;
                }

                if (renderer.name.StartsWith("Garland Bulbs "))
                {
                    int span = ParseTrailingNumber(renderer.name);
                    float power = EvaluateGarlandWarmth(span);
                    ApplyWarmthColor(
                        renderer,
                        ScaleRgb(
                            AlpineVillageWorldBuilder.GarlandBulbTint,
                            power));
                    continue;
                }

                if (renderer.name.EndsWith(" Snow"))
                {
                    Color dirtySnow = new Color(
                        0.43f,
                        0.405f,
                        0.355f,
                        1f);
                    ApplyWarmthColor(
                        renderer,
                        Color.Lerp(
                            AlpineVillageWorldBuilder.CleanSnowTint,
                            dirtySnow,
                            WarmthGrade * 0.68f));
                }
            }

            // The ground carries two submeshes: the floor at index 0 and the
            // enclosing rise at index 1. Only the floor dirties as the place
            // goes out - a wall of snow in its own shadow does not - and a
            // renderer-wide block would leak this tint onto the rise, so
            // the write is indexed.
            Renderer terrain = World.TerrainRoot.GetComponent<Renderer>();
            if (terrain != null)
            {
                ApplyWarmthColor(
                    terrain,
                    Color.Lerp(
                        Color.white,
                        new Color(0.72f, 0.68f, 0.60f, 1f),
                        WarmthGrade * 0.55f),
                    AlpineVillageWorldBuilder.TerrainFloorMaterialIndex);
            }

            Light[] lights = World.Root.GetComponentsInChildren<Light>(true);
            for (int index = 0; index < lights.Length; index++)
            {
                Light light = lights[index];
                float baseIntensity;
                float power;
                if (light.name.StartsWith("Garland Lamp "))
                {
                    baseIntensity =
                        AlpineVillageWorldBuilder.GarlandLampIntensity;
                    power = EvaluateGarlandWarmth(
                        ParseTrailingNumber(light.name));
                }
                else if (light.name == "Summit Window Snow Pool")
                {
                    baseIntensity = AlpineVillageWorldBuilder
                        .SummitWindowSnowPoolIntensity;
                    power = Mathf.Lerp(1f, 0.18f, WarmthGrade);
                }
                else if (light.name == "Window Snow Pool")
                {
                    baseIntensity = AlpineVillageWorldBuilder
                        .WindowSnowPoolIntensity;
                    power = Mathf.Lerp(1f, 0.06f, WarmthGrade);
                }
                else
                {
                    continue;
                }

                light.intensity = baseIntensity * power;
                light.enabled = power > 0.025f;
            }
        }

        private float EvaluateWindowWarmth(Renderer renderer)
        {
            Transform owner = renderer.transform.parent;
            string key = owner.name + "/" +
                         renderer.transform.GetSiblingIndex();
            float unit = CitySoundStableHash.ToUnitFloat(
                CitySoundStableHash.SourceEvent(Plan.Seed, key, 0u));
            bool summit = owner.name ==
                          "Village Plot - village-mothers-house";
            float cutoff = summit
                ? Mathf.Lerp(0.62f, 1.10f, unit)
                : Mathf.Lerp(0.18f, 0.90f, unit);
            return Mathf.Lerp(
                0.04f,
                1f,
                EvaluateRemainingWarmth(WarmthGrade, cutoff, 0.10f));
        }

        private float EvaluateGarlandWarmth(int span)
        {
            // Authored out of spatial order so the village loses isolated
            // cords, not a visible wipe travelling up the street.
            float cutoff;
            switch (span)
            {
                case 0: cutoff = 0.22f; break;
                case 1: cutoff = 0.68f; break;
                case 2: cutoff = 0.36f; break;
                case 3: cutoff = 0.84f; break;
                case 4: cutoff = 0.48f; break;
                case 5: cutoff = 0.14f; break;
                case 6: cutoff = 0.74f; break;
                case 7: cutoff = 0.56f; break;
                default: cutoff = 0.92f; break;
            }

            return EvaluateRemainingWarmth(
                WarmthGrade,
                cutoff,
                0.075f);
        }

        private static float EvaluateRemainingWarmth(
            float grade,
            float cutoff,
            float feather)
        {
            float fade = Mathf.InverseLerp(
                cutoff - feather,
                cutoff + feather,
                Mathf.Clamp01(grade));
            return 1f - Mathf.SmoothStep(0f, 1f, fade);
        }

        private void ApplyWarmthColor(Renderer renderer, Color color)
        {
            warmthProperties.Clear();
            renderer.GetPropertyBlock(warmthProperties);
            warmthProperties.SetColor("_BaseColor", color);
            warmthProperties.SetColor("_Color", color);
            renderer.SetPropertyBlock(warmthProperties);
        }

        /// <summary>
        /// The same write, into one submesh's own block. A renderer-wide
        /// block would sit under every slot's block and tint them all.
        /// </summary>
        private void ApplyWarmthColor(
            Renderer renderer,
            Color color,
            int materialIndex)
        {
            warmthProperties.Clear();
            renderer.GetPropertyBlock(warmthProperties, materialIndex);
            warmthProperties.SetColor("_BaseColor", color);
            warmthProperties.SetColor("_Color", color);
            renderer.SetPropertyBlock(warmthProperties, materialIndex);
        }

        private static Color ScaleRgb(Color color, float power)
        {
            return new Color(
                color.r * power,
                color.g * power,
                color.b * power,
                color.a);
        }

        private static int ParseTrailingNumber(string value)
        {
            if (value.Length >= 2 &&
                int.TryParse(
                    value.Substring(value.Length - 2),
                    out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            // The wave first, on the same delta the game clock advanced on
            // this frame, so a pause freezes the rhythm and the haze
            // together; then the per-minute pass; then the one visibility
            // write that both feed.
            StormWaveTarget = AlpineVillageStormFieldRules
                .EvaluateStormWaveTarget(
                    GameWeatherRules.EvaluateCurrentGust());
            StormWave = AlpineVillageStormFieldRules.AdvanceStormWave(
                StormWave,
                StormWaveTarget,
                Time.deltaTime);
            ApplyCurrentAtmosphere();
            ApplyVisibility();
        }

        /// <summary>
        /// The village end of the line: the way back down, and the arrival if
        /// this visit came up in a cabin.
        /// </summary>
        private void BuildCableway()
        {
            bool arrivingByCabin =
                HadAreaArrival && ArrivalToken == AreaArrivalToken.Cableway;
            AlpineCablewayRideFactory.Installation installation =
                AlpineCablewayRideFactory.Install(
                    transform,
                    Player,
                    areaCamera,
                    World.Cableway,
                    Plan.Station.Cableway,
                    GameAreaId.MountainRoad,
                    arrivingByCabin);
            CabinSeat = installation.Seat;
            CablewayRide = installation.Ride;
        }

        private void BuildAtmosphere()
        {
            Soundscape = AlpineVillageSoundscape.Create(
                transform,
                AlpineVillageSoundscapePlanner.Create(Plan),
                World.SemanticObjects,
                WarmthGrade);

            // The city's schedule, read through a permanent alpine storm.
            // Nothing here re-rolls the weather: the slot and bearing stay
            // shared, while high local floors guarantee dense snow and gale
            // transport in every one of those slots.
            WeatherShaper = new AlpineVillageWeatherShaper(
                Player.GameObject.transform,
                Plan.Lane.Start.y,
                Plan.Lane.End.y);

            var snowObject = new GameObject("Village Snow Field");
            snowObject.transform.SetParent(transform, false);
            Snow = snowObject.AddComponent<CityRainField>();
            Snow.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Plan.Seed,
                WeatherShaper
                    .ShapePrecipitation(GameWeatherRules.EvaluateCurrent())
                    .RainIntensity,
                AlpineVillageWeatherRules.PrecipitationKind,
                IsSheltered());

            // The city's fog, verbatim - same component, same shared
            // atmosphere material, same 36-sheet cap. The village's own warm
            // haze keeps doing the distance; these do the metre in front of
            // the hero, which is what a bulb hanging over a lane needs in
            // order to have anything to glow into.
            var fogObject = new GameObject("Village Fog Field");
            fogObject.transform.SetParent(transform, false);
            Fog = fogObject.AddComponent<CityFogField>();
            Fog.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Plan.Seed);

            Weather = gameObject.AddComponent<CityWeatherController>();
            Weather.Initialize(
                Snow,
                null,
                null,
                null,
                areaCamera.transform,
                IsSheltered,
                WeatherShaper,
                Fog);

            // The garland meshes were built before the player and weather.
            // Bind them now to the one shaped wind sample already driving
            // snow and cloth; the builder cannot invent a second wind owner.
            AlpineVillageGarlandWind[] garlands =
                World.Root.GetComponentsInChildren<
                    AlpineVillageGarlandWind>(true);
            for (int index = 0; index < garlands.Length; index++)
            {
                garlands[index].BindWeather(Weather);
            }

            // A second, low layer is what makes wind legible against snow and
            // stone. It samples the real village terrain for every strip, so
            // the uphill half cannot float while the downhill half clips.
            var stormObject = new GameObject("Village Blowing Snow");
            stormObject.transform.SetParent(transform, false);
            WindSound = stormObject
                .AddComponent<MountainRoadWindSoundPlayer>();
            BlowingSnow = stormObject.AddComponent<AlpineVillageStormField>();
            BlowingSnow.Initialize(
                Player.GameObject.transform,
                Plan,
                Weather,
                CityNightResources.AtmosphereMaterial,
                Plan.Seed,
                IsSheltered,
                WindSound);
        }

        private void BuildCommonUi(GameObject ui)
        {
            BalanceCheckView balance = ui.AddComponent<BalanceCheckView>();
            balance.Initialize(Player.GameObject.transform, Camera.main);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud,
                balance);

            CityLayout cityMapLayout = GenerateCityMapLayout();
            CityMountainBoundaryPlan cityMountains =
                CityMountainBoundaryPlanner.Create(cityMapLayout);
            Map = ui.AddComponent<CityMapController>();
            Map.Initialize(
                cityMapLayout,
                Player,
                CameraFollow,
                IntoxicationHud,
                null,
                cityMountains,
                null);

            // Every tab charts pure data, so the two areas that are not loaded
            // cost a planner run each and no GameObject at all. The teleport
            // ground handed over is THIS scene's, because the lattice measures
            // against the mask of the place the player is actually standing in.
            Map.ConfigureAreas(
                GameAreaId.AlpineVillage,
                CityMapMountainRoadOverlayBuilder.Create(
                    MountainRoadPlanner.Create(GameSessionState.CitySeed)),
                request => AreaTravelService.Request(request),
                new CityMapAlpineVillageTeleportGround(World.WalkableArea),
                CityMapAlpineVillageOverlayBuilder.Create(Plan),
                Plan.Plots);

            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud,
                Map);

            Inventory = ui.AddComponent<InventoryController>();
            Inventory.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);
            Journal = ui.AddComponent<JournalController>();
            Journal.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);
        }

        private static CityLayout GenerateCityMapLayout()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Resolve(
                GameSessionState.CityBlueprintId);
            return CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.CitySeed);
        }

        /// <summary>
        /// The station canopy is the only roof the player can stand under out
        /// here. The houses are shut - their doors do not open yet - so a
        /// doorway is not shelter.
        /// </summary>
        private bool IsSheltered()
        {
            // The cabin is a closed local interior even while its root crosses
            // open village air. Treat the whole ride as shelter so neither
            // snowfall layer is born through its roof before the blackout.
            if (GameSessionState.IsRidingAVehicle)
            {
                return true;
            }

            if (Player.GameObject == null || Plan == null)
            {
                return false;
            }

            Vector3 position = Player.GameObject.transform.position;
            MountainRoadTerminalRect pad = Plan.Station.PadArea;
            return pad.ContainsXZ(position, 0.2f) &&
                   position.y >= pad.Center.y - 0.3f &&
                   position.y <= pad.Center.y + 5.4f;
        }
    }
}
