using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Public court on the south side of the summit street. Both banks meet under its west end.</summary>
    internal sealed class RidgeMotelPlan
    {
        internal const string Id = "ridge-motel";
        internal const float Storey = 3.06f;
        internal readonly Vector3 Origin;
        internal readonly Rect Court, Shell, Office, Stair, Gallery;
        internal Vector3 Arrival => Origin + new Vector3(-2f, 0f, 14.8f);
        private static readonly ConditionalWeakTable<CityLayout, RidgeMotelPlan> Sites = new();

        internal static RidgeMotelPlan ForLayout(CityLayout layout) =>
            layout.HasSummitHome && layout.Seed == GameSessionState.DefaultCitySeed
                ? Sites.GetValue(layout, Create) : null;

        private static RidgeMotelPlan Create(CityLayout layout)
        {
            var street = CityHillsideQuarterPlan.ForLayout(layout).Links[1];
            return new RidgeMotelPlan(new Vector3(56f, street.PavementTopAt(56f), street.WestGateWorld.z - 21f));
        }

        private RidgeMotelPlan(Vector3 origin)
        {
            Origin = origin;
            Court = Part(-16.6f, -5.6f, 15.1f, 16f);
            Shell = Part(-13f, -5.6f, 15f, .02f);
            Office = Part(9f, 2.32f, 15f, 10.12f);
            Stair = Part(-16.39f, 0f, -13f, 6.4f);
            Gallery = Part(-13f, .02f, 15f, 1.92f);
        }

        private Rect Part(float left, float back, float right, float front) =>
            Rect.MinMaxRect(Origin.x + left, Origin.z + back, Origin.x + right, Origin.z + front);

        // A map click defaults to the courtyard. A height-bearing request on a
        // public gallery may select that floor. Stairs have overlapping flights.
        internal bool TryMapTop(Vector2 p, float? rootY, float radius, out float top)
        {
            top = Origin.y;
            if (Near(Shell, p, radius) || Near(Office, p, radius) || Near(Stair, p, radius))
                return false;
            // Each table includes the sweep of its loose chairs. The apron from the street stays clear.
            if (Near(Part(3.86f, 6.18f, 8.32f, 7.96f), p, radius) ||
                Near(Part(9.66f, 11.25f, 12.02f, 12.91f), p, radius) ||
                Near(Part(5.39f, 12.27f, 7.92f, 13.82f), p, radius) ||
                Near(Part(-11.18f, 2.30f, -10.42f, 2.82f), p, radius)) return false;
            if (!Near(Gallery, p, radius)) return true;
            if (rootY.HasValue && rootY.Value > Origin.y + Storey * .5f)
            {
                if (!Near(Gallery, p, -radius)) return false;
                top += Mathf.Clamp(Mathf.RoundToInt((rootY.Value - Origin.y) / Storey), 1, 2) * Storey;
            }
            if (Mathf.Abs(top - Origin.y - Storey) < .01f &&
                Near(Part(14.295f, .175f, 14.805f, .605f), p, radius)) return false;
            // Guard posts are physical supports on all three levels.
            float x = p.x - (Origin.x - 13f);
            float pitch = 28f / 6f;
            return Mathf.Abs(p.y - Origin.z - 1.77f) > radius + .07f ||
                   Mathf.Abs(x - Mathf.Round(x / pitch) * pitch) > radius + .07f;
        }

        private static bool Near(Rect r, Vector2 p, float pad) =>
            p.x >= r.xMin-pad && p.x <= r.xMax+pad && p.y >= r.yMin-pad && p.y <= r.yMax+pad;
    }
}
