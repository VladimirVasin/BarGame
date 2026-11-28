using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public sealed partial class CityGameRoot
    {
        private IEnumerator BindDistrictSteps(CityDistrict district, bool hasAreaArrival = false,
            AreaArrivalToken areaArrivalToken = default)
        {
            var world = district.World;
            Transform owner = district.Root;
            // Several existing local binders resolve this district's authored dock through World.
            CityWorldResult before = World;
            World = world;
            CityLaundryController laundry = world.Root.GetComponentInChildren<CityLaundryController>();
            if (laundry != null) laundry.PresentationObserver = Player.GameObject.transform;
            CityDogParkController dogPark = world.Root.GetComponentInChildren<CityDogParkController>();
            if (dogPark != null) dogPark.PresentationObserver = Player.GameObject.transform;
            CityResidentialCourtyardLife courtyardLife = world.ResidentialCourtyardRoot != null
                ? world.ResidentialCourtyardRoot.GetComponent<CityResidentialCourtyardLife>() : null;
            if (courtyardLife != null) courtyardLife.PresentationObserver = Player.GameObject.transform;
            world.Root.GetComponentInChildren<CityOffshoreBoatController>()
                ?.AttachHero(Player.GameObject.transform);
            if (world.SeacoastPlan != null)
                world.Root.GetComponentInChildren<CityLighthouseLanternController>()
                    ?.BindPier(Player.GameObject.transform, world.SeacoastPlan.Frame);
            CitySandTreading sand = world.Root.GetComponentInChildren<CitySandTreading>();
            if (sand != null)
            {
                sand.AttachWalker(Player.GameObject.transform);
                Player.Motor.SetFootstepSurface(sand);
            }
            var kioskCourt = world.Root.GetComponentInChildren<RidgeKioskCourtWorld>();
            if (kioskCourt != null && Pedestrians.Residents.KioskWindowNode >= 0)
            {
                var kioskLife = kioskCourt.gameObject.AddComponent<RidgeKioskCourtLife>();
                kioskLife.Initialize(kioskCourt, kioskCourt.GetComponentInParent<RidgeKioskStation>(),
                    Pedestrians.Residents, ResidentJourneys, Player, Camera);
                Pedestrians.ConfigureKioskCourt(kioskLife);
            }
            foreach (var districtCourt in world.Root.GetComponentsInChildren<DistrictKioskCourtWorld>())
            {
                var life = districtCourt.gameObject.AddComponent<DistrictKioskCourtLife>();
                life.Initialize(districtCourt, Pedestrians.Residents, ResidentJourneys, Player);
                Pedestrians.ConfigureDistrictCourt(life, districtCourt.Plan.Residential);
            }

            if (!district.Ridge)
            {
                bool hasTunnelTravel = CityTunnelTravelPlanner.TryCreate(
                    world.MountainBoundaryPlan,
                    out CityTunnelTravelPlan tunnelTravelPlan);
                if (hasTunnelTravel)
                {
                    TunnelTravel = CityTunnelTravelController.Create(
                        owner,
                        tunnelTravelPlan,
                        Player,
                        cityPrompt);
                }
                // The yard rider is authored, not ambient: one staged NPC on
                // the invisible circuit immediately left of the selected bar,
                // outside the pedestrian pool and its spawn bands.
                YardWheelchair = YardWheelchairFactory.Create(
                    owner,
                    YardWheelchairPlan.Create(
                        world.OpenAreaDecorationPlan,
                        Layout));
                // The drying yard's three authored grandmothers: two beat
                // the hung carpets at the rack, one smokes apart watching.
                // Staged NPCs outside the pedestrian pool, like the rider.
                DryingYardBabushkas = DryingYardBabushkaFactory.Create(
                    owner,
                    DryingYardBabushkaPlan.Create(Layout));
                // Small silent tableaux at selected residential pockets. Their
                // bodies reuse only unlimited generic pedestrian archetypes and
                // remain outside both the roaming pool and the unique staged-NPC
                // providers; the fringe yards carry no resident layer.
                CourtyardResidents = CityCourtyardResidentFactory.Create(
                    owner,
                    CityCourtyardResidentPlan.Create(
                        Layout,
                        world.DecorationPlan));
                if (world.ArchShelterPlan.IsEnabled)
                {
                    ArchShelterPresentation = world.ArchShelter.Root
                        .AddComponent<CityArchShelterPresentation>();
                    ArchShelterPresentation.Initialize(Layout.Seed);
                }

                // The public industrial lot now carries a working cannery. Its
                // production controller owns the crew; the retired human-weighing
                // tableau and its player-sensitive scale are never instantiated.
                WeighbridgeAttendants = System.Array.Empty<WeighbridgeAttendantPresentation>();
                WeighbridgeNeedle = null;
                Cannery = CityCanneryController.Build(owner, Layout,
                    world.Root.GetComponentInChildren<CityPortController>(), Player.GameObject.transform);
                World = before;
                yield return new CompositionStep("district_population", .89f);
                World = world;
                // The cemetery's one scripted visitor: while the hero is
                // near the grounds a mourner spawns out of sight, walks
                // through the gate to a deterministic random grave, lays
                // her bouquet, cries for thirty seconds, wipes her eyes
                // and leaves. Spawned per visit, not staged forever.
                CemeteryMourner = CityCemeteryMournerController.Create(
                    owner,
                    Layout,
                    world.CemeteryPlan,
                    Player.GameObject.transform,
                    Camera,
                    GameSessionState.CitySeed);
                // The gate lodge is attended: one snide old watchman at
                // his window post, eyes on the arch, answering every
                // "поговорить" with the next line of his repertoire.
                CemeteryWatchmanPlan watchmanPlan =
                    CemeteryWatchmanPlan.Create(world.CemeteryPlan);
                CemeteryWatchman = CemeteryWatchmanFactory.Create(
                    owner,
                    watchmanPlan,
                    GameSessionState.CitySeed,
                    Player.GameObject.transform);
                // The hero works this yard for a living, and the old man
                // at the gate has the only work going: vacant plots near
                // his own post, handed over one at a time and marked out
                // on the ground the moment they are taken. The register
                // stands every grave he has already sent the hero to back
                // up as well — a hole left half dug and a stone standing
                // over a finished one both survive a trip indoors.
                Gravedigging = CemeteryGravediggingRegister.Create(
                    owner,
                    world.CemeteryPlan,
                    watchmanPlan,
                    world.CemeteryGroundExcavation);
                if (CemeteryWatchman != null &&
                    CemeteryWatchman.Talk != null)
                {
                    CemeteryWatchman.Talk.AttachGravedigging(Gravedigging);
                }
                // The boat station is not abandoned by everybody: one man
                // sits on the end of the мостки with his back to the shore
                // and answers about the water whether or not he was asked.
                SeacoastFisherman = SeacoastFishermanFactory.Create(
                    owner,
                    SeacoastFishermanPlan.Create(world.SeacoastPlan),
                    GameSessionState.CitySeed);
                bool arrivingByCar = IsReturningByCar(hasAreaArrival, areaArrivalToken);
                BuildLastRouteCar(Camera, arrivingByCar, hasTunnelTravel, tunnelTravelPlan, owner);
                BuildLastRouteRide(arrivingByCar, hasTunnelTravel, tunnelTravelPlan, owner);
                World = before;
                yield return new CompositionStep("district_population", .89f);
                World = world;
                // The park kept a place for company and two men still keep
                // it: an old player at each of the two chess tables, on
                // seats that are each other's rotated 180 degrees about the
                // middle of the set, so the pair is turned toward one
                // another with the whole set between them. One plays chess
                // and one plays draughts, both have their heads in their
                // hands, and neither has anybody across his own board — the
                // two remaining planks stay unclaimed and sittable, which
                // is the point of the place rather than an oversight.
                //
                // Both are raised before the bench interaction and the rest
                // controller below, because both of those offer seats and
                // these two are claimed the moment they sit down.
                ParkChessLamp = CityParkChessLampWorldBuilder.Build(
                    owner,
                    CityParkChessLampPlan.Create(Layout, world.DecorationPlan));
                // Both boards are set up and neither game has been started.
                // Fifty-six men, four combined meshes, nothing that moves.
                ParkChessSetMen = CityChessSetWorldBuilder.Build(
                    owner,
                    Layout,
                    world.DecorationPlan,
                    GameSessionState.CitySeed);
                ParkChessPlayer = ParkChessPlayerFactory.Create(
                    owner,
                    ParkChessPlayerPlan.Create(Layout, world.DecorationPlan));
                ParkCheckersPlayer = ParkCheckersPlayerFactory.Create(
                    owner,
                    ParkCheckersPlayerPlan.Create(
                        Layout,
                        world.DecorationPlan));
                if (world.MountainBoundaryPlan.HasTunnel)
                {
                    var tunnelShelterObject =
                        new GameObject("City Tunnel Shelter");
                    tunnelShelterObject.transform.SetParent(owner, false);
                    TunnelShelter = tunnelShelterObject.AddComponent<
                        CityTunnelShelterController>();
                    TunnelShelter.Initialize(
                        Player.GameObject.transform,
                        world.MountainBoundaryPlan.Tunnel,
                        cityFog);
                }
                // The surf bed sits under everything near the north
                // shore: driven by the hero's distance to the waterline
                // and the deterministic wind, silent when the layout has
                // no coast.
                GameObject surfSoundObject =
                    new GameObject("City Surf Sound");
                surfSoundObject.transform.SetParent(owner, false);
                SurfSound =
                    surfSoundObject.AddComponent<CitySurfSoundPlayer>();
                surfSoundObject
                    .AddComponent<CitySurfSoundController>()
                    .Initialize(
                        SurfSound,
                        Camera.transform,
                        world.SeacoastPlan,
                        Layout.BuildingMasses);
            }
            district.Sounds = district.Ridge ? CitySoundscapeAnchorPlanner.CreateRidge(Layout)
                : CitySoundscapeAnchorPlanner.Create(Layout, world.DecorationPlan, districtPlan == null);

            World = before;
            yield return new CompositionStep("district_interactions", .92f);
            World = world;
            // Every authored seat is sittable in the bus ride's seated
            // pose: the bar-side yard bench faces the dead tree, ordinary
            // park benches face their own paths, point-of-interest and
            // street-decoration seats face their centres and every bus
            // stop shelter bench faces its road.
            List<CityBenchSitPlan> benchPlans =
                CityBenchSitPlan.CreateAll(
                    Layout,
                    world.OpenAreaDecorationPlan,
                    world.CemeteryPlan,
                    BusPlans,
                    world.DecorationPlan,
                    streetSurface,
                    world.SeacoastPlan,
                    world.ChurchCourtyardPlan);
            if (world.Fair != null)
            {
                foreach (CityBenchSeat seat in world.Fair.Plan.Benches)
                    benchPlans.Add(new CityBenchSitPlan(seat));
                world.Fair.InstallInteractions(Player, Camera);
            }
            if (world.CafeSite != null)
                benchPlans.Add(new CityBenchSitPlan(world.CafeSite.CounterSeat.ToBenchSeat()));
            var cobblerSite = CityCobblerSitePlan.ForLayout(Layout);
            if (cobblerSite.IsEnabled && (districtPlan == null || district.Ridge)) benchPlans.Add(new CityBenchSitPlan(cobblerSite.Yard.Seat));
            if (districtPlan != null) benchPlans.RemoveAll(bench => districtPlan.IsRidge(bench.FloorPosition) != district.Ridge);
            if (world.ResidentialCourtyards != null)
                benchPlans.AddRange(CityBenchRestPlanner.CreateCourtyardSeats(world.ResidentialCourtyards));
            district.BenchSits = CityBenchSitWorldBuilder.Build(
                owner,
                benchPlans,
                Player,
                Camera);
            if (!district.Ridge) InstallChurchGardenPot();
            // Life simulation: every now and then a walker near a free
            // bench sits down for a while and moves on.
            // Fair seats stay available to the hero; background walkers
            // have no authored approach through the fair's props.
            var restBenchPlans = new List<CityBenchSitPlan>(benchPlans);
            restBenchPlans.RemoveAll(bench => bench.Id == CityCobblerYardPlan.SeatId);
            if (world.CafeSite != null)
                restBenchPlans.RemoveAll(bench => bench.Id == world.CafeSite.CounterSeat.StableId);
            if (world.Fair != null)
                foreach (CityBenchSeat fairSeat in world.Fair.Plan.Benches)
                    restBenchPlans.RemoveAll(bench => bench.Id == fairSeat.Id);
            CityBenchRestPlan restPlan;
            if (districtPlan == null) restPlan = CityBenchRestPlanner.Create(restBenchPlans, PedestrianPlan, world.ResidentialCourtyards);
            else
            {
                var planning = System.Threading.Tasks.Task.Run(() =>
                    CityBenchRestPlanner.Create(restBenchPlans, PedestrianPlan, world.ResidentialCourtyards));
                World = before;
                yield return planning;
                World = world;
                restPlan = planning.GetAwaiter().GetResult();
            }
            district.BenchRests = CityBenchNpcRestController.Create(owner, restPlan, Pedestrians, GameSessionState.CitySeed);
            var docks = new List<CityStreetUtilityDock>(CityStreetUtilityDock.CreateAll(Layout, world.DecorationPlan));
            if (districtPlan != null) docks.RemoveAll(dock => districtPlan.IsRidge(dock.StandPosition) != district.Ridge);
            district.StreetUtilities = CityStreetUtilityWorldBuilder.Build(owner, docks, this);

            if (!district.Ridge)
            {
                BenchSits = district.BenchSits;
                BindCafe(owner, Camera, cityFollow, owner);
                EastGuards = CityEastGuardWorldBuilder.Build(owner, world.EastExitPlan,
                    Player.GameObject.transform, Camera, Layout.Seed);
                // The two men at the barrel and the sleeper under the arch talk
                // among themselves over the same shared bubbles; raised here for
                // the same reason as the guards.
                if (world.ArchShelterPlan.IsEnabled)
                {
                    ArchShelterConversations = CityArchShelterConversationController.Create(
                        world.ArchShelter, Player.GameObject.transform, Camera, Layout.Seed);
                }
                // Every act of the gravedigger's job is now a piece of
                // work rather than a press. Raised here rather than beside
                // the jobs themselves because it takes the Camera down onto
                // the hole and leases the hero out of sight while he digs,
                // and neither of those exists until this far down. One
                // session serves the whole yard: it binds to whichever
                // grave the hero is standing over, and to the board of
                // whichever finished stone he stops to read — reading is a
                // Camera move rather than a panel, because the words are
                // real letters on the brass.
                GraveWork = CemeteryGraveWorkController.Create(
                    owner,
                    Gravedigging,
                    Player,
                    cityFollow,
                    Camera,
                    intoxicationHud,
                    owner);
                // Two ordinary wintering ravens that hold to the first
                // grave the hero ever closes with a stone: one on its
                // mound, one on clear ground a few steps off. Raised
                // right after the work session because their director
                // must know when a session owns the Camera — nothing
                // about the birds may be observable inside somebody
                // else's shot.
                CemeteryRavens = CityCemeteryRavenController.Create(
                    owner,
                    world.CemeteryPlan,
                    Gravedigging,
                    GraveWork,
                    Player.GameObject.transform,
                    Camera,
                    GameSessionState.CitySeed);
                // And the rest of the species: up to eight triggerless
                // pairs on the open city's planned roosts, always already
                // perched — nothing about them is an event. Raised right
                // after the grave pair because they share its session
                // gate: while a grave-work act owns the Camera, no bird
                // may do anything observable inside that shot. The
                // closure reads GraveWork lazily per poll, the same
                // null-guarded idiom the cemetery pair applies to a yard
                // with no work controller at all. The ground is the one the
                // arrival already built: it is a function of the layout, and
                // the map takes the same instance below.
                CityRavenRoosts = RavenRoostController.Create(
                    owner,
                    CityRavenRoostPlanner.Create(
                        Layout,
                        World,
                        arrivalGround,
                        GameSessionState.CitySeed),
                    RavenRoostSettings.City,
                    Player.GameObject.transform,
                    () => GraveWork != null && GraveWork.IsActive,
                    GameSessionState.CitySeed);
                // The argument at the chess set. Null when the layout grew
                // no park: there is then nobody to have it with.
                ParkQuarrel = CityParkQuarrelController.Create(
                    owner,
                    ParkChessPlayer,
                    ParkCheckersPlayer,
                    Player.GameObject.transform,
                    SpeechBubbles,
                    GameSessionState.CitySeed);
                // And the games themselves. Nothing on either board moves
                // until the hero takes one of the two free planks; from
                // that moment that table is a real match against a real,
                // deliberately mediocre engine, and it keeps its position
                // for as long as this City lives. Raised last because it
                // needs the seats, the Camera and the bubbles all at once.
                BoardGames = CityBoardGameController.Create(
                    owner,
                    CityBoardGamePlan.Create(Layout, world.DecorationPlan),
                    district.BenchSits,
                    Player,
                    Camera,
                    cityFollow,
                    ParkChessSetMen != null
                        ? ParkChessSetMen.GetComponent<CityChessSetMen>()
                        : null,
                    SpeechBubbles,
                    ParkChessPlayer,
                    ParkCheckersPlayer,
                    ParkQuarrel,
                    GameSessionState.CitySeed, RestoreParkMatch);
                DebugWindow.BindCannery(Cannery);
            }
            var pigeonKeeper = world.Root.GetComponentInChildren<CityPigeonKeeper>(true);
            if (pigeonKeeper != null)
                pigeonKeeper.gameObject.AddComponent<CityPigeonKeeperSpeech>().Initialize(pigeonKeeper, SpeechBubbles);

            BindRidgeInteractions(world.RidgeRoot ?? world.Root);
            world.Root.GetComponentInChildren<CityCoastalGrassWind>(true)?.Initialize(Weather, Player.GameObject.transform);
            world.Root.GetComponent<CityWindField>()?.BindWeather(Weather);
            world.Root.GetComponentInChildren<CityGrassField>(true)?.Initialize(Player.GameObject.transform, Pedestrians, Camera);
            var rainShelters = new List<Collider>();
            if (world.ArchShelter != null) rainShelters.AddRange(world.ArchShelter.RainShelterColliders);
            if (world.Fair != null) rainShelters.AddRange(world.Fair.RainShelters);
            if (world.Gazebo != null) rainShelters.AddRange(world.Gazebo.RainShelters);
            if (laundry != null) rainShelters.AddRange(laundry.RainShelters);
            if (world.CafeRainShelter != null) rainShelters.Add(world.CafeRainShelter);
            if (world.EastExit?.RainShelter != null) rainShelters.Add(world.EastExit.RainShelter);
            rainShelters.AddRange(world.KioskRainShelters);
            foreach (var terraceAnchors in world.Root.GetComponentsInChildren<CityTerraceWorldAnchors>())
                if (terraceAnchors.RainShelter != null) rainShelters.Add(terraceAnchors.RainShelter);

            district.Shelters = rainShelters;
            if (!district.Ridge)
            {
                var ferryman = LastRouteCar != null ? LastRouteCar.GetComponentInParent<LastRouteCarDriver>() : null;
                AmbientTraffic.BindFerryman(ferryman);
                Cannery?.Traffic?.BindAmbient(AmbientTraffic);
                Pedestrians.ConfigureVehicleTraffic(Bus, Cannery, ferryman, AmbientTraffic);
                Rain.Contacts?.BindFerryman(LastRouteCar != null ? LastRouteCar.transform : null);
            }
            World = before;
            yield return new CompositionStep("district_local_services", .94f);
            district.Stops = districtPlan == null ? BusStops :
                CityBusStopWorldBuilder.Build(owner, BusPlans, stop => districtPlan.IsRidge(stop.ShelterPosition) == district.Ridge);
            yield return new CompositionStep("district_stop_models", .95f);
            var lowLampAnchors = new List<Transform>(world.RiverQuayLampAnchors);
            if (world.Fair != null) lowLampAnchors.AddRange(world.Fair.LampAnchors);
            district.Night.ShareFog(cityFog);
            district.Night.BusStopPoles = district.Stops.transform;
            district.Night.InitializeLighting(
                Player.GameObject.transform,
                Layout.Seed,
                world.FringePracticalAnchors,
                lowLampAnchors, taskRoot: owner, initializeFog: false);
            if (!district.Ridge && world.MountainBoundaryRoot != null && world.MountainBoundaryPlan.HasTunnel)
            {
                TunnelLighting = CityTunnelLightingController.Create(
                    world.MountainBoundaryRoot.transform,
                    world.MountainBoundaryPlan.Tunnel,
                    district.Night.Atmosphere,
                    world.FringePracticalAnchors);
            }

            yield return new CompositionStep("district_lamp_fixtures", .97f);
            var presentation = new GameObject("City Lighting");
            presentation.transform.SetParent(owner, false);
            district.Lighting = presentation.AddComponent<CityLightingPresentation>();
            yield return district.Lighting.InitializeSteps(this, world, district.Night, owner,
                () => districtPlan == null || activeDistrict == district, districtPlan != null);
            if (districtPlan != null) DayNight.SetDistrictNight(district.Ridge, district.Night);
            else DayNight.ApplyCurrentTime(true);
            World = before;
        }
    }
}
