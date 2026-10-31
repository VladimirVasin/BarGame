using UnityEngine;

namespace BarPromenade
{
    /// <summary>Edge-anchored combat instruments, shared by drawing and pointer routing.</summary>
    internal readonly struct CombatHudLayout
    {
        internal CombatHudLayout(RetroUiCanvas canvas, bool laboratory, bool debug)
        {
            Rect viewport = canvas.ViewportRect;
            float left = viewport.xMin + 8f, right = viewport.xMax - 8f;
            float top = viewport.yMin + 8f, bottom = viewport.yMax - 8f;
            Laboratory = new Rect(left, top, 304f, 46f);
            Toolbar = new Rect(right - 296f, top, 296f, 46f);
            Result = new Rect(right - 216f, top + (debug ? 52f : 0f), 216f, 46f);
            Help = new Rect(left, bottom - 14f, right - left, 14f);
            LaboratoryHelp = new Rect(left, bottom - (debug ? 28f : 14f), right - left, 14f);
            float metersBottom = bottom - (debug ? 18f : 0f) - (laboratory ? 16f : 0f);
            Hero = new Rect(left, metersBottom - 37f, 138f, 37f);
            Opponent = new Rect(right - 138f, metersBottom - 37f, 138f, 37f);
            LegacyAmmunition = new Rect(left, Hero.y - 29f, 170f, 25f);
            Health = new Rect(left, metersBottom - 39f, 114f, 19f);
            Stamina = new Rect(left, metersBottom - 17f, 114f, 17f);
            Guard = new Rect(left + 122f, Health.y + 2f, 15f, 15f);
            Ammunition = new Rect(right - 170f, metersBottom - 47f, 170f, 30f);
            Reload = new Rect(right - 170f, metersBottom - 15f, 170f, 15f);
            RestraintKey = new Rect(viewport.center.x - 16f, metersBottom - 42f, 32f, 30f);
            RestraintTrack = new Rect(viewport.center.x - 60f, metersBottom - 7f, 120f, 7f);
            Endless = new Rect(left, top, 230f, 46f);
        }

        internal Rect Laboratory { get; }
        internal Rect Toolbar { get; }
        internal Rect Result { get; }
        internal Rect Help { get; }
        internal Rect LaboratoryHelp { get; }
        internal Rect Hero { get; }
        internal Rect Opponent { get; }
        internal Rect LegacyAmmunition { get; }
        internal Rect Health { get; }
        internal Rect Stamina { get; }
        internal Rect Guard { get; }
        internal Rect Ammunition { get; }
        internal Rect Reload { get; }
        internal Rect RestraintKey { get; }
        internal Rect RestraintTrack { get; }
        internal Rect Endless { get; }

        internal bool ContainsControls(Vector2 point, bool laboratory, bool debug, bool result) =>
            laboratory && Laboratory.Contains(point) || debug && Toolbar.Contains(point) ||
            result && Result.Contains(point);

        internal static Rect Row(Rect panel, float x, float y, float width, float height = 18f) =>
            new Rect(panel.x + x, panel.y + y, width, height);
    }
}
