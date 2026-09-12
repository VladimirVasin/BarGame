using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>Starts a fresh session at a place, without performing a journey.</summary>
    public static class NewGameStartService
    {
        public const int MorningMinuteOfDay = (7 * 60) + 40;

        private static NewGameLocation pendingLocation = NewGameLocation.Count;
        private static string pendingOperationId = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pendingLocation = NewGameLocation.Count;
            pendingOperationId = string.Empty;
        }

        public static bool TryStart(NewGameLocation location)
        {
            if (!NewGameLocationCatalog.IsSupported(location) ||
                SceneTransitionService.IsTransitioning ||
                SceneManager.GetActiveScene().name != SceneIds.MainMenu)
                return false;

            string scene = NewGameLocationCatalog.SceneName(location);
            bool isArea = AreaSceneCatalog.TryGetArea(scene, out GameAreaId area);
            if (!Application.CanStreamedLevelBeLoaded(scene) ||
                (isArea && !Application.CanStreamedLevelBeLoaded(SceneIds.AreaLoading)))
                return false;

            // A real lot gives the bar its district/activity and an exterior
            // door to return to. The fallback interior ID has no city door.
            BuildingLot bar = location == NewGameLocation.Bar ? FindStartingBar() : null;
            if (location == NewGameLocation.Bar && bar == null)
                return false;

            ResetStatics();
            GameSessionState.BeginNewGame();
            PrepareInteriorContext(location, bar);
            GameSessionState.TryStartGameTimeAt(MorningMinuteOfDay);

            bool accepted = isArea
                ? AreaTravelService.Request(area)
                : SceneTransitionService.RequestLoad(scene);
            if (!accepted)
            {
                // Failed confirmation leaves the launch card with a stopped,
                // fresh session, including any entry-dependent quest state.
                GameSessionState.BeginNewGame();
                return false;
            }

            if (scene == SceneIds.City || location == NewGameLocation.Bar)
            {
                pendingLocation = location;
                pendingOperationId = SceneTransitionService.CurrentOperationId;
            }
            GameLog.Info("session", "new_game_location_selected",
                GameLog.Field("location", location.ToString()),
                GameLog.Field("scene", scene),
                GameLog.Field("operation_id", SceneTransitionService.CurrentOperationId));
            return true;
        }

        private static void PrepareInteriorContext(NewGameLocation location, BuildingLot bar)
        {
            switch (location)
            {
                case NewGameLocation.Home:
                    GameSessionState.EnterHome();
                    GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                    break;
                case NewGameLocation.Stairwell:
                    GameSessionState.EnterHome();
                    GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.StreetDoor);
                    break;
                case NewGameLocation.Bar:
                    GameSessionState.EnterBar(bar.BarId, bar.BarActivity, bar.District);
                    break;
                case NewGameLocation.Supermarket:
                    GameSessionState.EnterSupermarket();
                    break;
                case NewGameLocation.Church:
                    GameSessionState.EnterChurch();
                    break;
                case NewGameLocation.MothersHouse:
                    GameSessionState.EnterMothersHouse();
                    break;
            }
        }

        private static BuildingLot FindStartingBar()
        {
            // The same city the launch will compose, so the memo the City
            // root reads is already warm when the bar door opens onto it.
            CityLayout layout = CityLayoutCache.GetOrGenerate(
                CityBlueprintCatalog.Resolve(GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            BuildingLot nearest = null;
            float nearestDistance = float.PositiveInfinity;
            Vector3 origin = layout.PlayerHome?.SidewalkArrivalPosition ?? layout.SpawnWorldPosition;
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.IsBar || string.IsNullOrEmpty(lot.BarId)) continue;
                float distance = (lot.SidewalkArrivalPosition - origin).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = lot;
                nearestDistance = distance;
            }
            return nearest;
        }

        /// <summary>
        /// Resolve the launch position from the world just composed, before
        /// its default area-arrival branch can choose the south tunnel.
        /// Authored decks retain their actual floor height rather than a
        /// chart coordinate being resampled as surrounding terrain.
        /// </summary>
        internal static bool TryConsumeCityArrival(
            CityLayout layout, CityWorldResult world,
            out NewGameLocation location, out Vector3 position, out Vector3 forward)
        {
            location = pendingLocation;
            position = Vector3.zero;
            forward = Vector3.forward;
            if (location != NewGameLocation.City && location != NewGameLocation.Docks &&
                location != NewGameLocation.Cannery)
                return false;
            if (!TryConsumeArrival(location)) return false;

            switch (location)
            {
                case NewGameLocation.City:
                    if (world.PlayerHome != null && layout.PlayerHome != null)
                    {
                        position = world.PlayerHome.ReturnPosition;
                        Vector2Int facing = layout.PlayerHome.FrontageDirection;
                        forward = new Vector3(facing.x, 0f, facing.y);
                    }
                    else
                    {
                        position = layout.SpawnWorldPosition + Vector3.up *
                            (CityStreetSurfacePlanner.RoadTop + PlayerFactory.GroundedRootOffset);
                    }
                    break;
                case NewGameLocation.Docks:
                    CityPortPlan port = world.PortPlan ??
                        throw new InvalidOperationException("The new-game docks need the working port.");
                    // West of the crane/trolley lanes and north of the store.
                    position = port.World(new Vector3(-10f,
                        CityPortPlan.DeckHeight + PlayerFactory.GroundedRootOffset, -8f));
                    forward = new Vector3(10f, 0f, 5f).normalized;
                    break;
                case NewGameLocation.Cannery:
                    CityCanneryPlan cannery = CityCanneryPlan.Create(layout) ??
                        throw new InvalidOperationException("The new-game factory needs the cannery.");
                    // The public corridor beside the production floor is
                    // separate from both the truck and the worker routes.
                    position = cannery.World(new Vector3(-1.1f,
                        CityCanneryPlan.FloorTop + PlayerFactory.GroundedRootOffset, -1.3f));
                    forward = -cannery.Right;
                    break;
            }
            return true;
        }

        internal static bool TryConsumeArrival(NewGameLocation location)
        {
            if (pendingLocation != location || string.IsNullOrEmpty(pendingOperationId) ||
                !string.Equals(pendingOperationId, SceneTransitionService.CurrentOperationId,
                    StringComparison.Ordinal) ||
                SceneManager.GetActiveScene().name != NewGameLocationCatalog.SceneName(location))
                return false;
            ResetStatics();
            return true;
        }
    }
}
