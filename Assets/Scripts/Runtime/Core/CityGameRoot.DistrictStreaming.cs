using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public sealed partial class CityGameRoot
    {
        private static readonly CityNightFixturePlan EmptyNightPlan = new(
            new List<StreetLampDescriptor>(), new List<TrafficSignalDescriptor>());

        private CityDistrictStreamingPlan districtPlan;
        private DistrictWalkableArea districtWalkableArea;
        private CityDistrict lowerDistrict, ridgeDistrict, activeDistrict, buildingDistrict;
        private RuntimeComposition districtBuild;
        private Action<CompositionStep> reportDistrictStep;
        private string districtBuildPhase;
        internal string SlowestDistrictBuildPhase { get; private set; }
        internal double SlowestDistrictBuildMilliseconds { get; private set; }
        private CityStreetSurfacePlan streetSurface;
        private CityMapCityTeleportGround arrivalGround;
        private PlayerCameraFollow cityFollow;
        private InteractionPromptView cityPrompt;
        private float nextDistrictCheck;
        private CityFogField cityFog;
        private IBoardGameMatch savedChess, savedDraughts;
        private IBoardGameMatch RestoreParkMatch(CityBoardGameKind kind) =>
            kind == CityBoardGameKind.Chess ? savedChess : savedDraughts;

        private GameObject RidgeScope
        {
            get
            {
                Transform root = ridgeDistrict?.Root ?? (buildingDistrict?.Ridge == true ? buildingDistrict.Root : null);
                return root != null ? root.gameObject : lowerDistrict?.World?.RidgeRoot;
            }
        }

        private IWalkableArea PlayerWalkableArea => districtWalkableArea != null
            ? districtWalkableArea : World.WalkableArea;

        internal bool LowerDistrictReady => lowerDistrict?.Lighting != null;
        internal bool RidgeDistrictReady => ridgeDistrict?.Lighting != null;

        private sealed class CityDistrict
        {
            internal readonly bool Ridge;
            internal readonly Transform Root;
            internal CityWorldResult World;
            internal CityNightWorldResult Night;
            internal CityLightingPresentation Lighting;
            internal GameObject Stops;
            internal IReadOnlyList<CityBenchSitInteraction> BenchSits;
            internal CityBenchNpcRestController BenchRests;
            internal IReadOnlyList<CityStreetUtilityInteraction> StreetUtilities;
            internal IReadOnlyList<Collider> Shelters;
            internal CitySoundscapePlan Sounds;

            internal CityDistrict(bool ridge, Transform root)
            {
                Ridge = ridge;
                Root = root != null ? root : new GameObject(ridge ? "Ridge district" : "Lower city district").transform;
            }
        }

        private bool WantsRidgeArrival()
        {
            if (SessionCheckpointService.IsRestoringScene(SceneName))
                return districtPlan.IsRidge(SessionCheckpointService.ResolvePlayerSpawn(SceneName, Layout.StandingSpawnPosition));
            if (GameSessionState.TryGetCityReturnKind(out CityReturnKind kind))
                return kind == CityReturnKind.PlayerHome || kind == CityReturnKind.Pawnshop;
            if (NewGameStartService.PendingCityLocation is { } location)
                return location == NewGameLocation.Ridge;
            if (AreaTravelService.PendingRequest is { } request)
                return request.HasArrivalPosition && districtPlan.IsRidge(request.ArrivalPosition);
            return districtPlan.IsRidge(Layout.StandingSpawnPosition);
        }

        private IEnumerator BuildDistrictSteps(CityDistrict district)
        {
            if (!district.Ridge && district.Root != transform) district.Root.SetParent(transform, false);
            if (districtPlan != null && !district.Ridge)
            {
                CityBlueprint blueprint = CityBlueprintCatalog.Resolve(Layout.BlueprintId);
                CityGenerationSettings settings = CityGenerationSettings.Default;
                var ready = CityLayoutCache.WaitForPrimedPlans(blueprint, settings, Layout.Seed);
                if (ready != null) yield return ready;
                CityLayoutCache.GetOrGenerate(blueprint, settings, Layout.Seed);
            }
            CityNightFixturePlan lamps = district.Ridge ? EmptyNightPlan : CityLayoutCache.GetOrCreateNightPlan(Layout);
            yield return CityWorldBuilder.BuildSteps(district.Root, Layout, CityGenerationSettings.Default,
                lamps, value => district.World = value, separateRidge: districtPlan == null,
                partition: districtPlan, ridgeOnly: district.Ridge);
            district.Night = CityNightWorldBuilder.Build(district.Root, lamps, district.World.Bars,
                eastExit: district.World.EastExitPlan,
                cityLayout: districtPlan == null || district.Ridge ? Layout : null);
        }

        private IEnumerator AttachRidgeScene(CityDistrict district, bool useSceneAsset)
        {
            if (useSceneAsset)
            {
                var loaded = new TaskCompletionSource<bool>();
                ridgeLoad = SceneManager.LoadSceneAsync(SceneIds.CityRidge, LoadSceneMode.Additive)
                    ?? throw new InvalidOperationException("CityRidge is missing from Build Settings.");
                ridgeLoad.completed += _ => loaded.TrySetResult(true);
                yield return loaded.Task;
                ridgeLoad = null;
                ridgeScene = SceneManager.GetSceneByName(SceneIds.CityRidge);
            }
            else
            {
                ridgeScene = SceneManager.CreateScene(SceneIds.CityRidge);
            }

            if (!ridgeScene.isLoaded) throw new InvalidOperationException("CityRidge did not load.");
            GameObject scope = district.Ridge ? district.Root.gameObject : district.World.RidgeRoot;
            scope.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(scope, ridgeScene);
        }

        private void UpdateDistrictStreaming()
        {
            if (SceneTransitionService.IsTransitioning || GameTimeScaleRuntime.IsPaused) return;
            if (districtBuild != null)
                AdvanceDistrictBuild(RuntimeComposition.FrameBudgetMilliseconds);

            if (Time.unscaledTime < nextDistrictCheck) return;
            nextDistrictCheck = Time.unscaledTime + .1f;
            Vector3 position = Player.GameObject.transform.position;
            float distance = districtPlan.SignedDistance(position);
            bool onRidge = distance < 0f;
            var here = onRidge ? ridgeDistrict : lowerDistrict;
            var opposite = onRidge ? lowerDistrict : ridgeDistrict;
            if (here == null)
            {
                EnsureDistrictAt(position);
                return;
            }

            bool aboard = BusRide != null && BusRide.IsPassengerAboard;
            float preload = aboard ? CityDistrictStreamingPlan.PassengerPreloadDistance : CityDistrictStreamingPlan.PreloadDistance;
            float release = aboard ? CityDistrictStreamingPlan.PassengerReleaseDistance : CityDistrictStreamingPlan.ReleaseDistance;
            if (Mathf.Abs(distance) <= preload && opposite == null && districtBuild == null)
                StartDistrictBuild(!onRidge);
            if (districtBuild != null && opposite == null && districtPlan.AtStreetSeam(position))
                AdvanceDistrictBuild(double.PositiveInfinity);

            SelectDistrict(here);
            if (Mathf.Abs(distance) > release && opposite != null)
                ReleaseDistrict(opposite);
        }

        private void EnsureDistrictAt(Vector3 position) => EnsureDistrict(districtPlan.IsRidge(position));

        private void EnsureDistrict(bool ridge)
        {
            if ((ridge ? ridgeDistrict : lowerDistrict) != null) return;
            if (districtBuild == null) StartDistrictBuild(ridge);
            AdvanceDistrictBuild(double.PositiveInfinity);
        }

        private void StartDistrictBuild(bool ridge)
        {
            buildingDistrict = new CityDistrict(ridge, null);
            districtBuild = new RuntimeComposition(BuildAndBindDistrict(buildingDistrict));
            reportDistrictStep ??= step => districtBuildPhase = step.Phase;
            districtBuildPhase = "district_start";
            SlowestDistrictBuildPhase = null;
            SlowestDistrictBuildMilliseconds = 0d;
        }

        private IEnumerator BuildAndBindDistrict(CityDistrict district)
        {
            if (district.Ridge)
            {
                yield return AttachRidgeScene(district, false);
                yield return new CompositionStep("ridge_scene", .01f);
            }
            yield return BuildDistrictSteps(district);
            yield return BindDistrictSteps(district);
            if (district.Ridge) ridgeDistrict = district;
            else lowerDistrict = district;
            RefreshDistrictRain();
        }

        private void AdvanceDistrictBuild(double budget)
        {
            long started = Stopwatch.GetTimestamp();
            bool pending = districtBuild.AdvanceFrame(reportDistrictStep, budget);
            double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            if (milliseconds > SlowestDistrictBuildMilliseconds)
            {
                SlowestDistrictBuildMilliseconds = milliseconds;
                SlowestDistrictBuildPhase = districtBuildPhase;
            }

            if (pending) return;
            districtBuild.Dispose();
            districtBuild = null;
            buildingDistrict = null;
        }

        private void SelectDistrict(CityDistrict district)
        {
            if (district == null || activeDistrict == district) return;
            activeDistrict?.Lighting?.Reflection?.Rearm();
            activeDistrict = district;
            World = district.World;
            Night = district.Night;
            Lighting = district.Lighting;
            BusStops = district.Stops;
            BenchSits = district.BenchSits;
            BenchRests = district.BenchRests;
            StreetUtilities = district.StreetUtilities;
            district.World.Root.GetComponent<CityWindField>()?.BindWeather(Weather);
        }

        private void RefreshDistrictRain()
        {
            var shelters = new List<Collider>();
            if (lowerDistrict?.Shelters != null) shelters.AddRange(lowerDistrict.Shelters);
            if (ridgeDistrict?.Shelters != null) shelters.AddRange(ridgeDistrict.Shelters);
            Rain.SetLocalShelters(shelters);
            RefreshDistrictSounds();
            GameObject ridge = RidgeScope;
            Rain.Contacts?.BindDistrict(ridge != null ? ridge.transform : null, Cannery?.Truck);
        }

        private void RefreshDistrictSounds()
        {
            var low = lowerDistrict?.Sounds;
            var high = ridgeDistrict?.Sounds;
            if (low == null && high == null) return;
            if (low != null && high != null)
            {
                var sources = new List<CitySoundSourceDescriptor>(low.Sources);
                sources.AddRange(high.Sources);
                SoundscapePlan = CitySoundscapePlanner.Create(Layout.Seed, sources);
            }
            else SoundscapePlan = low ?? high;
            var swings = lowerDistrict?.World?.PlaygroundSwings;
            if (Soundscape == null)
                Soundscape = CitySoundscapeDirector.Create(transform, SoundscapePlan, Camera.transform, Layout,
                    DryingYardBabushkas, WeighbridgeNeedle, swings, () => Night.NightFactor);
            else
                Soundscape.SetLoadedDistricts(SoundscapePlan, DryingYardBabushkas, WeighbridgeNeedle, swings);
        }

        private void ReleaseDistrict(CityDistrict district)
        {
            AmbientPedestrians?.ReleaseRailRests(district.World);
            DayNight.SetDistrictNight(district.Ridge, null);
            if (!district.Ridge) Cannery?.RestorePresentation();
            district.Root.gameObject.SetActive(false);
            if (district.Ridge)
            {
                Pedestrians.ConfigureKioskCourt(null);
                ridgeDistrict = null;
                UnloadRidge(ridgeScene);
                ridgeScene = default;
            }
            else
            {
                Pedestrians.ConfigureDistrictCourt(null, true);
                Pedestrians.ConfigureDistrictCourt(null, false);
                Pedestrians.ConfigureVehicleTraffic(Bus, null, null, AmbientTraffic);
                AmbientTraffic.BindFerryman(null);
                Rain.Contacts?.BindFerryman(null);
                DebugWindow.BindCannery(null);
                Player.Motor.SetFootstepSurface(null);
                YardWheelchair = null;
                DryingYardBabushkas = null;
                CourtyardResidents = null;
                ArchShelterPresentation = null;
                ArchShelterConversations = null;
                CemeteryMourner = null;
                CemeteryWatchman = null;
                CemeteryRavens = null;
                CityRavenRoosts = null;
                EastGuards = null;
                SeacoastFisherman = null;
                ParkChessPlayer = null;
                ParkCheckersPlayer = null;
                ParkChessSetMen = null;
                ParkChessLamp = null;
                ParkQuarrel = null;
                ChurchGardenPot = null;
                SurfSound = null;
                Cannery = null;
                LastRouteCar = null;
                LastRouteFerryman = null;
                Ride = null;
                TunnelTravel = null;
                TunnelLighting = null;
                TunnelShelter = null;
                Gravedigging = null;
                GraveWork = null;
                savedChess = BoardGames?.GetMatch(CityBoardGameKind.Chess);
                savedDraughts = BoardGames?.GetMatch(CityBoardGameKind.Draughts);
                BoardGames = null;
                CafeMenu = null;
                CafeSeatView = null;
                CafeConversation = null;
                Destroy(district.Root.gameObject);
                lowerDistrict = null;
            }
            RefreshDistrictRain();
            activeDistrict?.World.Root.GetComponent<CityWindField>()?.BindWeather(Weather);
            AreaAssetWarmup.NoteEviction();
        }

        private sealed class DistrictWalkableArea : IWalkableArea
        {
            private readonly CityGameRoot city;

            internal DistrictWalkableArea(CityGameRoot city) => this.city = city;

            public bool Contains(Vector3 position, float radius = 0f) =>
                GetArea(position)?.Contains(position, radius) == true;

            public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f) =>
                GetArea(desiredPosition)?.Constrain(currentPosition, desiredPosition, radius) ?? currentPosition;

            private RoadWalkableArea GetArea(Vector3 point) => (city.districtPlan.IsRidge(point)
                ? city.ridgeDistrict : city.lowerDistrict)?.World.WalkableArea;
        }
    }
}
