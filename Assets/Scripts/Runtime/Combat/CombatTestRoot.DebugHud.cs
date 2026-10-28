using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        // Retained authoring surface, selected explicitly with F8.
        private void DrawDebugHud(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(ToolbarRect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            if (DrawToolbarButton(canvas, new Rect(156, 12, 58, 18), "combat-weapons",
                LocalizationService.Get("combat.weapons"))) ReturnToWeapons();
            if (Opponent.IsZombie)
                GUI.Label(new Rect(224, 12, WarehousePlan != null ? 94 : 166, 18),
                    LocalizationService.Get(Opponent.IsCrane ? "combat.mode.crane" : Opponent.IsRoom ? "combat.mode.room" :
                        Opponent.IsImprint ? "combat.mode.imprint" : Opponent.IsLongExposure ? "combat.mode.long-exposure" : "combat.zombie"), small);
            else if (DrawToolbarButton(canvas, new Rect(220, 12, WarehousePlan != null ? 94 : 166, 18), "combat-style",
                LocalizationService.Get(OpponentStyleHudKeys[(int)OpponentStyle]),
                Mode != CombatTestMode.Endless || EndlessRunFinished))
                SetOpponentStyle((CombatOpponentStyle)(((int)OpponentStyle + 1) % 3));
            DrawWarehouseButton(canvas);
            if (Mode == CombatTestMode.Endless)
                GUI.Label(new Rect(392, 12, 102, 18), endlessKillsLabel.Get(LocalizationService.Get("combat.kills"), EndlessKills), small);
            else if (DrawToolbarButton(canvas, new Rect(392, 12, 102, 18), "combat-mode",
                ModeHudText())) SetSparring(!Sparring);
            if (DrawToolbarButton(canvas, new Rect(498, 12, 70, 18), "combat-reset",
                LocalizationService.Get(Mode == CombatTestMode.Endless ? "combat.new_run" : "combat.reset"))) ResetRound();
            if (DrawToolbarButton(canvas, new Rect(572, 12, 48, 18), "combat-menu",
                LocalizationService.Get("combat.menu"))) ReturnToMenu();
            DrawFighterHud(new Rect(14, 302, 138, 37), "combat.health", Hero);
            if (Opponent.gameObject.activeInHierarchy)
                DrawFighterHud(new Rect(488, 302, 138, 37), "combat.opponent", Opponent);
            DrawChargeMeter();
            DrawFirearmCrosshair(canvas);
            DrawLegacyPistolHud();
            DrawFocusMarker(canvas);
            DrawEndlessStatus();
            GUI.Label(new Rect(14, 342, 612, 14), LocalizationService.Get(Hero.IsShotgun ? "combat.controls.shotgun" :
                Hero.IsPistol ? "combat.controls.pistol" : "combat.controls"), controls);
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
