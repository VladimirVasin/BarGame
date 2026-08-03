using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared visual recipe for the generated City and its bounded Home view.
    /// Keeping these values together prevents the balcony proxy from drifting
    /// away from the same seeded location rendered in City.
    /// </summary>
    internal static class CityExteriorAppearance
    {
        public static readonly Color Asphalt =
            new Color(0.175f, 0.195f, 0.195f);
        public static readonly Color ParkPath =
            new Color(0.39f, 0.34f, 0.24f);
        public static readonly Color Ground =
            new Color(0.170f, 0.205f, 0.185f);
        public static readonly Color WindowOff =
            new Color(0.025f, 0.035f, 0.040f);
        public static readonly Color ColdWindow =
            new Color(0.24f, 0.43f, 0.56f);
        public static readonly Color WarmWindow =
            new Color(0.88f, 0.48f, 0.20f);
        public static readonly Color BarWindow =
            new Color(1.35f, 0.72f, 0.28f);
        public static readonly Color HomeWindow =
            new Color(0.82f, 1.10f, 1.22f);
        public static readonly Color SupermarketWindow =
            new Color(0.50f, 0.82f, 0.66f);

        public static Color CreateNightFacadeColor(
            BuildingLot lot)
        {
            if (lot.IsBar)
            {
                return new Color(
                    lot.Color.r * 0.70f,
                    lot.Color.g * 0.65f,
                    lot.Color.b * 0.68f,
                    1f);
            }

            if (lot.IsPlayerHome)
            {
                return new Color(
                    lot.Color.r * 0.72f,
                    lot.Color.g * 0.78f,
                    lot.Color.b * 0.80f,
                    1f);
            }

            if (lot.IsSupermarket)
            {
                return new Color(
                    lot.Color.r * 0.68f,
                    lot.Color.g * 0.74f,
                    lot.Color.b * 0.62f,
                    1f);
            }

            float value =
                (lot.Color.r +
                 lot.Color.g +
                 lot.Color.b) /
                3f;
            Color tintedValue = Color.Lerp(
                new Color(value, value, value, 1f),
                lot.Color,
                0.32f);
            return new Color(
                tintedValue.r * 0.68f,
                tintedValue.g * 0.73f,
                tintedValue.b * 0.70f,
                1f);
        }

        public static Color ResolveWindowColor(
            BuildingLot lot,
            int citySeed,
            int floor,
            int pane,
            int side,
            out bool emissive)
        {
            if (lot.IsBar)
            {
                emissive = true;
                return BarWindow;
            }

            if (lot.IsPlayerHome)
            {
                emissive = true;
                return HomeWindow;
            }

            if (lot.IsSupermarket)
            {
                emissive = true;
                return SupermarketWindow;
            }

            uint hash = StableHash(
                citySeed,
                lot.Cell.x,
                lot.Cell.y,
                floor,
                pane,
                side);
            int selection = (int)(hash % 100u);
            if (selection < 65)
            {
                emissive = false;
                return WindowOff;
            }

            emissive = true;
            return selection < 90
                ? ColdWindow
                : WarmWindow;
        }

        public static Color Darken(
            Color color,
            float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
        }

        private static uint StableHash(
            int seed,
            int x,
            int z,
            int floor,
            int pane,
            int side)
        {
            uint hash =
                unchecked((uint)seed) ^
                0x9E3779B9u;
            hash = Mix(hash, unchecked((uint)x));
            hash = Mix(hash, unchecked((uint)z));
            hash = Mix(hash, unchecked((uint)floor));
            hash = Mix(hash, unchecked((uint)pane));
            return Mix(hash, unchecked((uint)side));
        }

        private static uint Mix(
            uint first,
            uint second)
        {
            uint hash = first;
            hash ^=
                second +
                0x85EBCA6Bu +
                (hash << 6) +
                (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u
                ? 0xA341316Cu
                : hash;
        }
    }
}
