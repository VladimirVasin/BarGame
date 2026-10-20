using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private const float HudFadeInSeconds = .15f, HudFadeOutSeconds = .3f;
        private float ammoHudFade, reloadHudFade, healthHudFade, staminaHudFade, guardHudFade, chargeHudFade;
        private float healthHudHold, staminaHudHold, hudLastHealth;
        private float restraintHudProgress, restraintHudPulse;
        private CombatWeaponId hudWeapon;
        private int resultSelection;
        private GUIStyle ammoDigits, ammoReserve, hudCaption, resultTitle, restraintKey;
        private readonly HudCounterLabel gameplayRounds = new HudCounterLabel("");
        private readonly HudCounterLabel gameplayReserve = new HudCounterLabel("");
        private readonly GUIContent ammunitionMeasure = new GUIContent();
        private string measuredRounds, measuredReserve, measuredReload;
        private float roundsWidth, reserveWidth, reloadWidth;

        public bool DebugHudVisible { get; private set; }
        internal bool AutomaticHudPresentation { get; set; } = true;
        internal bool HudCanRender => isActiveAndEnabled && IsInitialized && pistolApplicationFocused &&
            !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning &&
            (Taunt == null || !Taunt.IsActive) && !BarMinigameModalLock.IsAnyLocked &&
            GameInput.CanRead(GameInputContext.Gameplay);
        public bool GameplayHudVisible => HudCanRender && !DebugHudVisible;
        public bool DefeatResultVisible => isActiveAndEnabled && IsInitialized && Hero != null &&
            Hero.State.IsDefeated && pistolApplicationFocused && !PauseMenuController.IsAnyPaused &&
            !GameTimeScaleRuntime.IsPaused &&
            !SceneTransitionService.IsTransitioning &&
            (GameplayHudVisible || DebugFreeCamera.IsInspecting(Player.GameObject));
        public float AmmunitionHudOpacity => HudOpacity(ammoHudFade);
        public float ReloadHudOpacity => HudOpacity(reloadHudFade);
        public float HealthHudOpacity => HudOpacity(healthHudFade);
        public float StaminaHudOpacity => HudOpacity(staminaHudFade);
        public float GuardHudOpacity => HudOpacity(guardHudFade);
        public float ChargeHudOpacity => HudOpacity(chargeHudFade);
        public bool RestraintHudVisible => GameplayHudVisible && Hero != null && Hero.CanEscapeRestraint;
        public float RestraintHudProgress => RestraintHudVisible ? Hero.RestraintEscapeProgress : 0f;

        private float HudOpacity(float fade) => GameplayHudVisible ? Mathf.SmoothStep(0f, 1f, fade) : 0f;
        private static float FadeHud(float fade, bool show, float seconds) =>
            Mathf.MoveTowards(fade, show ? 1f : 0f, seconds / (show ? HudFadeInSeconds : HudFadeOutSeconds));

        private void ResetHudPresentation(bool closeDebug = false, bool preserveHealthBaseline = false)
        {
            ammoHudFade = reloadHudFade = healthHudFade = staminaHudFade = guardHudFade = chargeHudFade = 0f;
            healthHudHold = staminaHudHold = 0f;
            restraintHudProgress = restraintHudPulse = 0f;
            if (!preserveHealthBaseline) hudLastHealth = Hero != null ? Hero.State.Health : 0f;
            hudWeapon = Hero != null ? Hero.WeaponId : CombatWeaponId.Crowbar;
            resultSelection = 0;
            if (closeDebug) DebugHudVisible = false;
        }

        private void UpdateHudInput()
        {
            if (!HudCanRender) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
            {
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    GameDiagnosticsSnapshot.TryOpenLogDirectory();
                else DebugHudVisible = !DebugHudVisible;
            }
            // A victory still permits the body interaction and free exploration.
            // Result navigation is explicit (Alt); defeat has no competing action.
            if (!GameplayHudVisible || !RoundFinished || !(Hero.State.IsDefeated || GameCursor.FreeCursorRequested)) return;
            int delta = GameInput.ReadMenuSelectionDelta(GameInputContext.Gameplay);
            if (delta != 0) resultSelection = 1 - resultSelection;
            bool submit = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            submit |= Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
            if (submit) ActivateHudResult(resultSelection);
        }

        /// <summary>One presentation clock per frame; IMGUI layout/repaint only reads it.</summary>
        internal void AdvanceHudPresentation(float seconds)
        {
            if (!IsInitialized || !isActiveAndEnabled || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return;
            if (!HudCanRender)
            {
                // The Room hit lands while its camera/input loan still hides the
                // HUD. Keep that health baseline so release reveals the real
                // damage once. Pause retains the exact fade; other handoffs reset.
                if (!PauseMenuController.IsAnyPaused)
                    ResetHudPresentation(preserveHealthBaseline: roomInput.IsLocked && RoomAttackCinematicActive);
                return;
            }
            if (hudWeapon != Hero.WeaponId || Hero.IsSwitchingWeapon)
            {
                ammoHudFade = reloadHudFade = guardHudFade = chargeHudFade = 0f;
                hudWeapon = Hero.WeaponId;
            }
            bool firearm = Hero.IsFirearm && Hero.Firearm != null && !Hero.IsSwitchingWeapon;
            ammoHudFade = FadeHud(ammoHudFade, firearm && Hero.PistolAimBodyAvailable &&
                Hero.Firearm.AimRequested && !Hero.State.IsDefeated, seconds);
            reloadHudFade = FadeHud(reloadHudFade, firearm && Hero.Firearm.IsReloading && !Hero.State.IsDefeated, seconds);
            healthHudHold = Mathf.Max(0f, healthHudHold - seconds);
            if (Hero.State.Health < hudLastHealth) healthHudHold = 3f;
            hudLastHealth = Hero.State.Health;
            bool critical = Hero.State.Health <= Hero.State.Settings.MaxHealth * .25f;
            healthHudFade = FadeHud(healthHudFade, healthHudHold > 0f || critical, seconds);
            bool spent = Hero.State.Stamina < Hero.State.Settings.MaxStamina - .01f;
            staminaHudHold = spent ? 1f : Mathf.Max(0f, staminaHudHold - seconds);
            staminaHudFade = FadeHud(staminaHudFade, spent || staminaHudHold > 0f, seconds);
            guardHudFade = FadeHud(guardHudFade, !Hero.IsFirearm && (Hero.GuardRequested || Hero.State.IsBlocking), seconds);
            chargeHudFade = FadeHud(chargeHudFade, !Hero.IsFirearm && Hero.State.IsCharging, seconds);
            float escapeProgress = Hero.IsRestrained ? Hero.RestraintEscapeProgress : 0f;
            // Pulse from accepted presses, never from held-device state or IMGUI events.
            restraintHudPulse = escapeProgress > restraintHudProgress ? 1f :
                Mathf.MoveTowards(restraintHudPulse, 0f, seconds / .14f);
            restraintHudProgress = escapeProgress;
        }

        private void EnsureHudStyles()
        {
            if (small != null) return;
            small = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleLeft, RetroUiTheme.Text);
            controls = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleCenter, RetroUiTheme.Muted);
            button = RetroUiTheme.CreateButtonStyle(9, TextAnchor.MiddleCenter, RetroUiTheme.Text, false);
            button.padding = new RectOffset(2, 2, 0, 0);
            ammoDigits = RetroUiTheme.CreateLabelStyle(23, TextAnchor.MiddleRight, RetroUiTheme.AccentPale);
            ammoReserve = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleRight, RetroUiTheme.Muted);
            hudCaption = RetroUiTheme.CreateLabelStyle(8, TextAnchor.MiddleLeft, RetroUiTheme.Text);
            resultTitle = RetroUiTheme.CreateLabelStyle(13, TextAnchor.MiddleCenter, RetroUiTheme.Text);
            restraintKey = RetroUiTheme.CreateLabelStyle(18, TextAnchor.MiddleCenter, RetroUiTheme.Text);
        }

        private void OnGUI()
        {
            bool inspectionDefeat = DefeatResultVisible && !HudCanRender;
            if (!HudCanRender && !inspectionDefeat) return;
            EnsureHudStyles();
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(canvas);
            int depth = GUI.depth;
            GUI.depth = -50; // Under shared speech, interaction prompts and modal menus.
            try
            {
                if (inspectionDefeat) DrawGameplayResult(canvas);
                else if (DebugHudVisible) DrawDebugHud(canvas);
                else DrawGameplayHud(canvas);
                if (IsLaboratory && !inspectionDefeat) DrawLaboratoryControls(canvas);
            }
            finally { GUI.depth = depth; RetroUiTheme.EndCanvas(matrix); }
        }

        private void DrawGameplayHud(RetroUiCanvas canvas)
        {
            DrawFirearmCrosshair(canvas);
            DrawFocusMarker(canvas);
            DrawGameplayMeter(new Rect(14, 302, 114, 19), "combat.health", Hero.State.Health,
                Hero.State.Settings.MaxHealth, HealthHudOpacity, true);
            DrawGameplayMeter(new Rect(14, 326, 114, 17), "combat.stamina", Hero.State.Stamina,
                Hero.State.Settings.MaxStamina, StaminaHudOpacity, false);
            DrawGameplayMeter(new Rect(14, 278, 114, 17), "combat.charge", Hero.State.Charge01,
                1f, ChargeHudOpacity, false, Hero.State.ChargeLimit01);
            if (GuardHudOpacity > 0f)
                DrawGuardReadiness(new Rect(138, 304, 15, 15), Hero, GuardHudOpacity);
            DrawGameplayAmmunition();
            DrawRestraintEscape();
            if (RoundFinished) DrawGameplayResult(canvas);
        }

        private void DrawRestraintEscape()
        {
            if (!RestraintHudVisible) return;
            // One silent instrument: the real key and its accumulated escape effort.
            // Fixed canvas coordinates share the other charcoal/bone HUD plates.
            var key = new Rect(304, 276, 32, 30);
            Color edge = Color.Lerp(RetroUiTheme.BorderMuted, RetroUiTheme.Text, restraintHudPulse);
            RetroUiTheme.DrawPanel(key, RetroUiTheme.Ink, edge, false, 0f, 1f, .9f);
            if (restraintHudPulse > 0f)
                RetroUiTheme.FillRect(new Rect(key.x + 2, key.y + 2, key.width - 4, key.height - 4),
                    RetroUiTheme.Fade(RetroUiTheme.Text, restraintHudPulse * .16f));
            GUI.Label(new Rect(key.x, key.y - 1, key.width, key.height), "E", restraintKey);
            var track = new Rect(260, 312, 120, 7);
            RetroUiTheme.FillRect(track, RetroUiTheme.Fade(RetroUiTheme.Ink, .9f));
            RetroUiTheme.FillRect(new Rect(track.x + 1, track.y + 1, track.width - 2, track.height - 2),
                RetroUiTheme.BorderMuted);
            RetroUiTheme.FillRect(new Rect(track.x + 1, track.y + 1,
                (track.width - 2) * RestraintHudProgress, track.height - 2), RetroUiTheme.Text);
        }

        private void DrawGameplayMeter(Rect rect, string key, float value, float max, float opacity,
            bool health, float limit = -1f)
        {
            if (opacity <= 0f) return;
            RetroUiTheme.FillRect(rect, RetroUiTheme.Fade(RetroUiTheme.Ink, .75f * opacity));
            DrawHudLabel(new Rect(rect.x + 4, rect.y - 1, rect.width - 8, 12), LocalizationService.Get(key), hudCaption, opacity);
            Rect track = new Rect(rect.x + 4, rect.yMax - 5, rect.width - 8, health ? 3 : 2);
            RetroUiTheme.FillRect(track, RetroUiTheme.Fade(RetroUiTheme.BorderMuted, opacity));
            RetroUiTheme.FillRect(new Rect(track.x, track.y, track.width * Mathf.Clamp01(value / max), track.height),
                RetroUiTheme.Fade(health ? RetroUiTheme.AccentPale : RetroUiTheme.Text, opacity));
            if (health && value <= max * .25f)
                RetroUiTheme.FillRect(new Rect(rect.x - 3, rect.y, 1, rect.height), RetroUiTheme.Fade(RetroUiTheme.Text, opacity));
            if (limit >= 0f)
                RetroUiTheme.FillRect(new Rect(track.x + (track.width - 1) * limit, track.y - 1, 1, track.height + 2),
                    RetroUiTheme.Fade(RetroUiTheme.AccentPale, opacity));
        }

        private void DrawGameplayAmmunition()
        {
            if (!Hero.IsFirearm || Hero.Firearm == null || Hero.IsSwitchingWeapon) return;
            float alpha = AmmunitionHudOpacity;
            if (alpha > 0f)
            {
                string rounds = gameplayRounds.Get("", Hero.Firearm.Rounds);
                string reserve = gameplayReserve.Get("/ ", Hero.Firearm.ReserveRounds);
                // Retain readable type; fit the plate to its contents instead of
                // reserving empty columns. Measure only when a counter changes.
                if (rounds != measuredRounds || reserve != measuredReserve)
                {
                    measuredRounds = rounds;
                    measuredReserve = reserve;
                    roundsWidth = MeasureAmmunitionText(rounds, ammoDigits);
                    reserveWidth = MeasureAmmunitionText(reserve, ammoReserve);
                }
                float width = roundsWidth + reserveWidth + 15f;
                var rect = new Rect(626f - width, 308, width, 30);
                RetroUiTheme.FillRect(rect, RetroUiTheme.Fade(RetroUiTheme.Ink, .75f * alpha));
                RetroUiTheme.FillRect(new Rect(rect.x, rect.y, rect.width, 1), RetroUiTheme.Fade(RetroUiTheme.BorderMuted, alpha));
                DrawHudLabel(new Rect(rect.x + 6, 307, roundsWidth, 29), rounds, ammoDigits, alpha);
                DrawHudLabel(new Rect(rect.x + 9 + roundsWidth, 316, reserveWidth, 17), reserve, ammoReserve, alpha);
            }
            float reload = ReloadHudOpacity;
            if (reload <= 0f) return;
            // Reload state is independent of aim, without exposing a reserve counter at rest.
            string caption = LocalizationService.Get("combat.pistol.reloading");
            if (caption != measuredReload)
            {
                measuredReload = caption;
                reloadWidth = MeasureAmmunitionText(caption, hudCaption) + 8f;
            }
            Rect row = new Rect(626f - reloadWidth, 340, reloadWidth, 15);
            RetroUiTheme.FillRect(row, RetroUiTheme.Fade(RetroUiTheme.Ink, .75f * reload));
            DrawHudLabel(new Rect(row.x + 4, 338, row.width - 8, 12), caption, hudCaption, reload);
            RetroUiTheme.FillRect(new Rect(row.x + 4, 352, row.width - 8, 1), RetroUiTheme.Fade(RetroUiTheme.BorderMuted, reload));
            RetroUiTheme.FillRect(new Rect(row.x + 4, 352, (row.width - 8) * Hero.Firearm.ReloadProgress, 1), RetroUiTheme.Fade(RetroUiTheme.Text, reload));
        }

        private float MeasureAmmunitionText(string text, GUIStyle style)
        {
            ammunitionMeasure.text = text;
            return Mathf.Ceil(style.CalcSize(ammunitionMeasure).x);
        }

        private static void DrawHudLabel(Rect rect, string text, GUIStyle style, float opacity)
        {
            Color previous = GUI.color;
            GUI.color = RetroUiTheme.Fade(previous, opacity);
            try { GUI.Label(rect, text, style); }
            finally { GUI.color = previous; }
        }

        private void DrawGameplayResult(RetroUiCanvas canvas)
        {
            var panel = new Rect(210, 20, 220, 63);
            RetroUiTheme.DrawPanel(panel, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .85f);
            GUI.Label(new Rect(220, 25, 200, 22), LocalizationService.Get(Hero.State.IsDefeated ?
                Mode == CombatTestMode.Endless ? "combat.endless.finished" : "combat.defeat" : "combat.victory"), resultTitle);
            DrawHudResultButton(canvas, new Rect(220, 53, 114, 20), 0,
                Mode == CombatTestMode.Endless ? "combat.new_run" : "combat.reset");
            DrawHudResultButton(canvas, new Rect(342, 53, 78, 20), 1, "combat.menu");
        }

        private void DrawHudResultButton(RetroUiCanvas canvas, Rect rect, int index, string key)
        {
            bool pointer = GameCursor.FreeCursorRequested;
            bool hover = pointer && rect.Contains(RetroUiTheme.LogicalMousePosition(canvas));
            bool navigation = Hero.State.IsDefeated || pointer;
            RetroUiTheme.DrawSelection(rect, hover || navigation && index == resultSelection);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && pointer;
            bool pressed = GUI.Button(rect, LocalizationService.Get(key), button);
            GUI.enabled = enabled;
            if (pressed) ActivateHudResult(index);
        }

        private void ActivateHudResult(int index)
        {
            if (!GameplayHudVisible || !RoundFinished) return;
            if (index == 0) ResetRound();
            else ReturnToMenu();
        }
    }
}
