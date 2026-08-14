using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class GameDiagnosticsSnapshot
    {
        public static bool TryOpenLogDirectory()
        {
            string directory = GameLog.CurrentDirectoryPath;
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return false;
            }

            try
            {
                GameLog.Info(
                    "diagnostics",
                    "log_directory_open_requested",
                    GameLog.Field(
                        "platform",
                        Application.platform.ToString()));
                GameLog.Flush();
                string fullPath = Path.GetFullPath(directory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                Application.OpenURL(new Uri(fullPath).AbsoluteUri);
                return true;
            }
            catch (Exception exception)
            {
                GameLog.Warning(
                    "diagnostics",
                    "log_directory_open_failed",
                    GameLog.Field(
                        "exception_type",
                        exception.GetType().Name));
                GameLog.Flush();
                return false;
            }
        }

        public static bool Capture(string reason = "manual")
        {
            if (GameLog.Profile == GameLogProfile.Off)
            {
                return false;
            }

            var fields = new List<GameLogField>(35)
            {
                GameLog.Field(
                    "snapshot_id",
                    Guid.NewGuid().ToString("N")),
                GameLog.Field(
                    "reason",
                    string.IsNullOrWhiteSpace(reason)
                        ? "manual"
                        : reason),
                GameLog.Field(
                    "active_scene",
                    SceneManager.GetActiveScene().name),
                GameLog.Field(
                    "transitioning",
                    SceneTransitionService.IsTransitioning),
                GameLog.Field(
                    "transition_operation_id",
                    SceneTransitionService.CurrentOperationId),
                GameLog.Field(
                    "active_bar_id",
                    GameSessionState.ActiveBarId),
                GameLog.Field(
                    "active_activity",
                    GameSessionState.ActiveBarActivity.ToString()),
                GameLog.Field(
                    "returning_to_city",
                    GameSessionState.IsReturningToCity),
                GameLog.Field(
                    "return_kind",
                    GameSessionState.ReturnKind.ToString()),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "hunger",
                    GameSessionState.HungerLevel),
                GameLog.Field(
                    "stress",
                    GameSessionState.StressLevel),
                GameLog.Field(
                    "fatigue",
                    GameSessionState.FatigueLevel),
                GameLog.Field(
                    "last_drink",
                    GameSessionState.LastAlcoholicDrink.ToString()),
                GameLog.Field(
                    "drinks_consumed",
                    GameSessionState.DrinksConsumed),
                GameLog.Field(
                    "cash_balance",
                    GameSessionState.CashBalance),
                GameLog.Field(
                    "balance_delay_seconds",
                    GameSessionState.BalanceCheckDelayRemaining),
                GameLog.Field(
                    "next_balance_sequence",
                    GameSessionState.BalanceCheckSequence),
                GameLog.Field(
                    "planned_route",
                    string.Join(
                        ">",
                        GameSessionState.PlannedBarRoute))
            };

            CityGameRoot city =
                UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
            BarInteriorRoot bar =
                UnityEngine.Object.FindAnyObjectByType<BarInteriorRoot>();
            HomeInteriorRoot home =
                UnityEngine.Object.FindAnyObjectByType<HomeInteriorRoot>();
            if (city != null)
            {
                AddCityFields(fields, city);
                AddBalanceFields(fields, city.IntoxicationStatus);
                AddPlayerFields(fields, city.Player);
            }
            else if (bar != null)
            {
                AddBarFields(fields, bar);
                AddBalanceFields(fields, bar.IntoxicationStatus);
                AddPlayerFields(fields, bar.Player);
            }
            else if (home != null)
            {
                AddHomeFields(fields, home);
                AddBalanceFields(
                    fields,
                    home.IntoxicationStatus);
                AddPlayerFields(fields, home.Player);
            }
            else
            {
                fields.Add(GameLog.Field("root_kind", "none"));
                fields.Add(GameLog.Field("root_initialized", false));
            }

            GameLog.Info(
                "diagnostics",
                "snapshot",
                fields.ToArray());
            GameLog.Flush();
            return GameLog.Profile != GameLogProfile.Off;
        }

        private static void AddCityFields(
            ICollection<GameLogField> fields,
            CityGameRoot city)
        {
            fields.Add(GameLog.Field("root_kind", "city"));
            fields.Add(
                GameLog.Field(
                    "root_initialized",
                    city.IsInitialized));

            CityLayout layout = city.Layout;
            if (layout != null)
            {
                fields.Add(
                    GameLog.Field(
                        "road_count",
                        layout.RoadEdges.Count));
                fields.Add(
                    GameLog.Field(
                        "lot_count",
                        layout.BuildingLots.Count));
                fields.Add(
                    GameLog.Field(
                        "district_count",
                        layout.Districts.Count));
                fields.Add(
                    GameLog.Field(
                        "park_present",
                        layout.Park != null));
            }

            fields.Add(
                GameLog.Field(
                    "bar_count",
                    city.World?.Bars.Count ?? 0));
            fields.Add(
                GameLog.Field(
                    "player_home_present",
                    city.World?.PlayerHome != null));
            fields.Add(
                GameLog.Field(
                    "map_open",
                    city.Map != null && city.Map.IsOpen));
            fields.Add(
                GameLog.Field(
                    "debug_window_open",
                    city.DebugWindow != null &&
                    city.DebugWindow.IsOpen));
        }

        private static void AddBarFields(
            ICollection<GameLogField> fields,
            BarInteriorRoot bar)
        {
            fields.Add(GameLog.Field("root_kind", "bar"));
            fields.Add(
                GameLog.Field(
                    "root_initialized",
                    bar.IsInitialized));
            fields.Add(
                GameLog.Field(
                    "bar_activity",
                    bar.ActiveActivity.ToString()));
            fields.Add(
                GameLog.Field(
                    "layout_zone_count",
                    bar.Layout?.Zones.Count ?? 0));
            fields.Add(
                GameLog.Field(
                    "patron_count",
                    bar.Patrons?.Count ?? 0));
            fields.Add(
                GameLog.Field(
                    "drink_shop_open",
                    bar.DrinkShop != null &&
                    bar.DrinkShop.IsOpen));
            fields.Add(
                GameLog.Field(
                    "debug_window_open",
                    bar.DebugWindow != null &&
                    bar.DebugWindow.IsOpen));
        }

        private static void AddHomeFields(
            ICollection<GameLogField> fields,
            HomeInteriorRoot home)
        {
            fields.Add(GameLog.Field("root_kind", "home"));
            fields.Add(
                GameLog.Field(
                    "root_initialized",
                    home.IsInitialized));
            fields.Add(
                GameLog.Field(
                    "furniture_count",
                    home.Layout?.Furniture.Count ?? 0));
            fields.Add(
                GameLog.Field(
                    "home_exit_present",
                    home.Exit != null));
        }

        private static void AddBalanceFields(
            ICollection<GameLogField> fields,
            IntoxicationStatusController status)
        {
            fields.Add(
                GameLog.Field(
                    "balance_controller_present",
                    status != null));
            if (status == null)
            {
                return;
            }

            fields.Add(
                GameLog.Field(
                    "balance_state",
                    status.BalanceStateName));
            fields.Add(
                GameLog.Field(
                    "balance_delay_armed",
                    status.IsBalanceDelayArmed));
            fields.Add(
                GameLog.Field(
                    "scheduled_balance_sequence",
                    status.ScheduledBalanceSequence));
            fields.Add(
                GameLog.Field(
                    "balance_position",
                    status.BalancePosition));
            fields.Add(
                GameLog.Field(
                    "balance_risk",
                    status.BalanceRisk));
        }

        private static void AddPlayerFields(
            ICollection<GameLogField> fields,
            PlayerRuntime player)
        {
            if (player.GameObject == null)
            {
                fields.Add(GameLog.Field("player_present", false));
                return;
            }

            Vector3 position = player.GameObject.transform.position;
            fields.Add(GameLog.Field("player_present", true));
            fields.Add(GameLog.Field("player_x", position.x));
            fields.Add(GameLog.Field("player_y", position.y));
            fields.Add(GameLog.Field("player_z", position.z));
        }
    }
}
