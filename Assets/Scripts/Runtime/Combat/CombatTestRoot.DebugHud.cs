using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        // Retained authoring surface, selected explicitly with F8.
        private void DrawDebugHud(RetroUiCanvas canvas, CombatHudLayout layout)
        {
            Rect panel = layout.Toolbar;
            RetroUiTheme.DrawPanel(panel, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 6, 3, 52), "combat-weapons",
                LocalizationService.Get("combat.weapons"), !IsStoryContinuation)) ReturnToWeapons();
            if (Opponent.IsZombie)
                GUI.Label(CombatHudLayout.Row(panel, 62, 3, WarehousePlan != null ? 128 : 228),
                    LocalizationService.Get(Opponent.IsCrane ? "combat.mode.crane" : Opponent.IsRoom ? "combat.mode.room" :
                        Opponent.IsImprint ? "combat.mode.imprint" : Opponent.IsLongExposure ? "combat.mode.long-exposure" : "combat.zombie"), small);
            else if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 62, 3, WarehousePlan != null ? 128 : 228), "combat-style",
                LocalizationService.Get(OpponentStyleHudKeys[(int)OpponentStyle]),
                Mode != CombatTestMode.Endless || EndlessRunFinished))
                SetOpponentStyle((CombatOpponentStyle)(((int)OpponentStyle + 1) % 3));
            DrawWarehouseButton(canvas, CombatHudLayout.Row(panel, 194, 3, 96));
            if (Mode == CombatTestMode.Endless)
                GUI.Label(CombatHudLayout.Row(panel, 6, 25, 126), endlessKillsLabel.Get(LocalizationService.Get("combat.kills"), EndlessKills), small);
            else if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 6, 25, 126), "combat-mode",
                ModeHudText())) SetSparring(!Sparring);
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 136, 25, 90), "combat-reset",
                LocalizationService.Get(Mode == CombatTestMode.Endless ? "combat.new_run" : "combat.reset"))) ResetRound();
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 230, 25, 60), "combat-menu",
                LocalizationService.Get("combat.menu"))) ReturnToMenu();
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 6, 47, 284), "combat-coordinates",
                (PlayerCoordinatesHud.CoordinatesEnabled ? "[X] " : "[ ] ") +
                LocalizationService.Get("debug.player_coordinates")))
                PlayerCoordinatesHud.CoordinatesEnabled = !PlayerCoordinatesHud.CoordinatesEnabled;
            DrawFighterHud(layout.Hero, "combat.health", Hero);
            if (Opponent.gameObject.activeInHierarchy)
                DrawFighterHud(layout.Opponent, "combat.opponent", Opponent);
            DrawFirearmCrosshair(canvas);
            DrawLegacyPistolHud(layout.LegacyAmmunition);
            DrawFocusMarker(canvas);
            DrawEndlessStatus(layout.Endless);
            DrawHudHelp(layout.Help, LocalizationService.Get(Hero.IsShotgun ? "combat.controls.shotgun" :
                Hero.IsPistol ? "combat.controls.pistol" : "combat.controls"), ref measuredHelp, ref helpWidth);
        }

        private bool DrawToolbarButton(RetroUiCanvas canvas, Rect rect, string control, string text, bool enabled = true)
        {
            bool pointerAvailable = GameCursor.FreeCursorRequested;
            RetroUiTheme.DrawSelection(rect, enabled && pointerAvailable &&
                (rect.Contains(RetroUiTheme.LogicalMousePosition(canvas)) || GUI.GetNameOfFocusedControl() == control));
            GUI.SetNextControlName(control);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled && pointerAvailable;
            bool pressed = RetroUiInput.Button(rect, text, button);
            GUI.enabled = previousEnabled;
            return pressed;
        }

        private void DrawFighterHud(Rect rect, string healthKey, CombatActor actor)
        {
            if (actor.IsZombie) rect.yMin += 16f; // No empty stamina row under a single enemy meter.
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            DrawMeter(new Rect(rect.x + 6f, rect.y + 3f, rect.width - (actor.IsHero ? 32f : 12f), 14f), healthKey,
                actor.State.Health, actor.State.Settings.MaxHealth, RetroUiTheme.AccentPale, 3f,
                actor.IsHero ? heroHealthLabel : opponentHealthLabel);
            if (!actor.IsZombie) DrawMeter(new Rect(rect.x + 6f, rect.y + 19f, rect.width - 12f, 13f), "combat.stamina",
                actor.State.Stamina, actor.State.Settings.MaxStamina, RetroUiTheme.Muted, 2f,
                actor.IsHero ? heroStaminaLabel : opponentStaminaLabel);
            if (actor.IsHero) DrawGuardReadiness(new Rect(rect.xMax - 22f, rect.y + 2f, 15f, 15f), actor);
        }

        private static void DrawGuardReadiness(Rect rect, CombatActor actor, float opacity = 1f)
        {
            // A closed shield means the stance can guard; its centre lights only
            // while the raised two-hand guard actually protects the actor.
            bool ready = actor.GuardReady;
            bool requested = actor.GuardRequested && !ready;
            Color color = ready ? RetroUiTheme.Text : requested ? RetroUiTheme.Accent : RetroUiTheme.BorderMuted;
            color = RetroUiTheme.Fade(color, opacity);
            RetroUiTheme.FillRect(new Rect(rect.x, rect.y, 2f, 9f), color);
            RetroUiTheme.FillRect(new Rect(rect.xMax - 2f, rect.y, 2f, 9f), color);
            if (ready) RetroUiTheme.FillRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            else if (requested)
            {
                // Two separated shoulders show a held request; the shield stays
                // open and empty until both hands actually protect the fighter.
                RetroUiTheme.FillRect(new Rect(rect.x + 2f, rect.y, 3f, 1f), color);
                RetroUiTheme.FillRect(new Rect(rect.xMax - 5f, rect.y, 3f, 1f), color);
            }
            RetroUiTheme.FillRect(new Rect(rect.x + 2f, rect.y + 9f, rect.width - 4f, 2f), color);
            RetroUiTheme.FillRect(new Rect(rect.x + 4f, rect.y + 11f, rect.width - 8f, 2f), color);
            RetroUiTheme.FillRect(new Rect(rect.x + 6f, rect.y + 13f, rect.width - 12f, 2f), color);
            if (actor.State.IsBlocking && ready)
                RetroUiTheme.FillRect(new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, 5f), RetroUiTheme.Fade(RetroUiTheme.AccentPale, opacity));
        }

        private void DrawMeter(Rect rect, string key, float value, float max, Color color, float thickness, HudCounterLabel label)
        {
            var track = new Rect(rect.x, rect.yMax - thickness, rect.width, thickness);
            RetroUiTheme.FillRect(track, RetroUiTheme.Shadow);
            RetroUiTheme.FillRect(new Rect(track.x, track.y, track.width * Mathf.Clamp01(value / max), thickness), color);
            GUI.Label(new Rect(rect.x, rect.y - 2f, rect.width, 12f),
                label.Get(LocalizationService.Get(key), Mathf.CeilToInt(value)), small);
        }
    }
}
