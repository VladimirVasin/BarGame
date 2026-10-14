using System;
using System.Globalization;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private static readonly Rect WarehouseButtonRect = new Rect(318, 12, 68, 18);
        // Explicit fixture selection preserves the original weapon-contact laboratory.
        // Player starts and direct scene loads always use the warehouse.
        internal static bool UseLegacyArenaForTests;
        internal static int? WarehouseSeedForTests;
        private GameObject combatLocation;
        public WarehouseArenaPlan WarehousePlan { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarehouseSelection()
        {
            UseLegacyArenaForTests = false;
            WarehouseSeedForTests = null;
        }

        private void CreateCombatLocation()
        {
            if (UseLegacyArenaForTests)
            {
                combatLocation = CombatAssetProvider.CreateArena(transform);
                heroSpawn = CombatAssetProvider.FindAnchor(combatLocation, "HeroSpawn").position + Vector3.up * PlayerFactory.GroundedRootOffset;
                opponentSpawn = CombatAssetProvider.FindAnchor(combatLocation, "OpponentSpawn").position + Vector3.up * PlayerFactory.GroundedRootOffset;
                arenaBounds = new ArenaBounds();
                return;
            }
            int seed = WarehouseSeedForTests ?? ReadWarehouseSeed();
            WarehouseSeedForTests = null;
            WarehousePlan = WarehouseArenaPlanner.Create(seed);
            combatLocation = WarehouseArenaBuilder.Create(WarehousePlan, transform);
            arenaBounds = new WarehouseArenaArea(WarehousePlan);
            UpdateWarehouseSpawns();
        }

        private static int RandomWarehouseSeed() => Guid.NewGuid().GetHashCode();

        private static int ReadWarehouseSeed()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-bp-warehouse-seed" && int.TryParse(args[i + 1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int seed)) return seed;
            return RandomWarehouseSeed();
        }

        private void UpdateWarehouseSpawns()
        {
            heroSpawn = WarehousePlan.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset;
            opponentSpawn = WarehousePlan.OpponentSpawn + Vector3.up * PlayerFactory.GroundedRootOffset;
            GameLog.Info("combat", "warehouse_layout", GameLog.Field("seed", WarehousePlan.Seed),
                GameLog.Field("generator_version", WarehouseArenaPlan.GeneratorVersion),
                GameLog.Field("template", WarehousePlan.Template.ToString()));
        }

        /// <summary>Replace the layout only by an explicit action; ordinary resets retain it.</summary>
        public bool NewWarehouseLayout(int seed)
        {
            if (!IsInitialized || WarehousePlan == null || !GameInput.CanRead(GameInputContext.Gameplay) ||
                (Taunt != null && Taunt.IsActive)) return false;
            WarehouseArenaPlan nextPlan = WarehouseArenaPlanner.Create(seed);
            GameObject next = WarehouseArenaBuilder.Create(nextPlan, transform);
            EndJournalRound("layout_changed");
            GameObject previous = combatLocation;
            previous.SetActive(false);
            combatLocation = next;
            WarehousePlan = nextPlan;
            ((WarehouseArenaArea)arenaBounds).ReplacePlan(nextPlan);
            UpdateWarehouseSpawns();
            Destroy(previous);
            PlaceRound(focusOpponent: Mode == CombatTestMode.Standard || !Hero.IsFirearm);
            return true;
        }

        private void DrawWarehouseButton(RetroUiCanvas canvas)
        {
            if (WarehousePlan == null) return;
            if (DrawToolbarButton(canvas, WarehouseButtonRect, "warehouse-new",
                LocalizationService.Get("combat.warehouse.new"))) NewWarehouseLayout(RandomWarehouseSeed());
        }
    }
}
