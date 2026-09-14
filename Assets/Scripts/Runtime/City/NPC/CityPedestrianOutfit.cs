using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One garment of an outfit: the palette name the model's manifest
    /// gives a part, and the colour that palette wears on this body.
    /// </summary>
    public readonly struct CityPedestrianOutfitGarment
    {
        public CityPedestrianOutfitGarment(string paletteName, Color color)
        {
            PaletteName = paletteName ?? string.Empty;
            Color = color;
        }

        public string PaletteName { get; }
        public Color Color { get; }
    }

    /// <summary>
    /// A named set of garments for one body, keyed by the palette names
    /// its model declares. A staged site hands one to
    /// <see cref="CityPedestrianAssetRegistry.ApplyOutfit"/>; parts whose
    /// palette the outfit does not name keep the design's own colour, so
    /// skin, soles and eyes are never touched by dressing someone.
    ///
    /// This exists because the palette variant is a multiplier around
    /// one, which is enough to tell a crowd apart and not enough to tell
    /// two placed neighbours apart. The cannery crew solved the same
    /// problem with per-slot colour families; this is that idea for a
    /// body the pedestrian library dresses.
    /// </summary>
    public sealed class CityPedestrianOutfit
    {
        private readonly CityPedestrianOutfitGarment[] garments;

        public CityPedestrianOutfit(
            params CityPedestrianOutfitGarment[] configuredGarments)
        {
            garments = configuredGarments ??
                Array.Empty<CityPedestrianOutfitGarment>();
        }

        public IReadOnlyList<CityPedestrianOutfitGarment> Garments => garments;

        public bool TryGetColor(string paletteName, out Color color)
        {
            for (int index = 0; index < garments.Length; index++)
            {
                if (string.Equals(
                        garments[index].PaletteName,
                        paletteName,
                        StringComparison.Ordinal))
                {
                    color = garments[index].Color;
                    return true;
                }
            }

            color = default;
            return false;
        }
    }
}
