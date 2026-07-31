using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum BarDrinkBottleStyle
    {
        None = 0,
        WaterBottle = 1,
        BeerLongneck = 2,
        WineBottle = 3,
        VodkaBottle = 4,
        CognacBottle = 5
    }

    public enum BarDrinkVesselKind
    {
        None = 0,
        Tumbler = 1,
        Pint = 2,
        WineGlass = 3,
        ShotGlass = 4,
        Snifter = 5
    }

    /// <summary>
    /// Immutable visual contract for one retail drink. DrinkId is the stable
    /// identity shared with the purchase rules; StableId is suitable for
    /// deterministic world-object names and diagnostics.
    /// </summary>
    public readonly struct BarDrinkPresentation
    {
        internal BarDrinkPresentation(
            DrinkId drinkId,
            string stableId,
            BarDrinkBottleStyle bottleStyle,
            BarDrinkVesselKind vesselKind,
            Color32 bottleColor,
            Color32 liquidColor,
            Color32 labelColor,
            float targetFill)
        {
            DrinkId = drinkId;
            StableId = stableId;
            BottleStyle = bottleStyle;
            VesselKind = vesselKind;
            BottleColor = bottleColor;
            LiquidColor = liquidColor;
            LabelColor = labelColor;
            TargetFill = targetFill;
        }

        public DrinkId DrinkId { get; }
        public string StableId { get; }
        public BarDrinkBottleStyle BottleStyle { get; }
        public BarDrinkVesselKind VesselKind { get; }
        public Color32 BottleColor { get; }
        public Color32 LiquidColor { get; }
        public Color32 LabelColor { get; }
        public float TargetFill { get; }
    }

    public static class BarDrinkPresentationCatalog
    {
        private static readonly BarDrinkPresentation[] presentations =
        {
            Define(
                DrinkId.Water,
                "water",
                BarDrinkBottleStyle.WaterBottle,
                BarDrinkVesselKind.Tumbler,
                Rgb(151, 188, 194),
                Rgb(190, 224, 229),
                Rgb(218, 224, 196),
                0.74f),
            Define(
                DrinkId.LightBeer,
                "light-beer",
                BarDrinkBottleStyle.BeerLongneck,
                BarDrinkVesselKind.Pint,
                Rgb(91, 58, 25),
                Rgb(220, 158, 43),
                Rgb(226, 202, 132),
                0.82f),
            Define(
                DrinkId.DarkBeer,
                "dark-beer",
                BarDrinkBottleStyle.BeerLongneck,
                BarDrinkVesselKind.Pint,
                Rgb(57, 37, 24),
                Rgb(75, 38, 20),
                Rgb(174, 93, 55),
                0.82f),
            Define(
                DrinkId.WhiteWine,
                "white-wine",
                BarDrinkBottleStyle.WineBottle,
                BarDrinkVesselKind.WineGlass,
                Rgb(64, 93, 62),
                Rgb(224, 204, 117),
                Rgb(220, 211, 176),
                0.56f),
            Define(
                DrinkId.RedWine,
                "red-wine",
                BarDrinkBottleStyle.WineBottle,
                BarDrinkVesselKind.WineGlass,
                Rgb(47, 72, 50),
                Rgb(111, 24, 38),
                Rgb(190, 159, 112),
                0.56f),
            Define(
                DrinkId.Vodka,
                "vodka",
                BarDrinkBottleStyle.VodkaBottle,
                BarDrinkVesselKind.ShotGlass,
                Rgb(173, 199, 202),
                Rgb(205, 226, 228),
                Rgb(215, 218, 207),
                0.72f),
            Define(
                DrinkId.PepperVodka,
                "pepper-vodka",
                BarDrinkBottleStyle.VodkaBottle,
                BarDrinkVesselKind.ShotGlass,
                Rgb(169, 190, 187),
                Rgb(214, 147, 89),
                Rgb(166, 47, 35),
                0.72f),
            Define(
                DrinkId.CognacVs,
                "cognac-vs",
                BarDrinkBottleStyle.CognacBottle,
                BarDrinkVesselKind.Snifter,
                Rgb(115, 68, 35),
                Rgb(182, 91, 31),
                Rgb(202, 164, 91),
                0.44f),
            Define(
                DrinkId.CognacVsop,
                "cognac-vsop",
                BarDrinkBottleStyle.CognacBottle,
                BarDrinkVesselKind.Snifter,
                Rgb(91, 50, 30),
                Rgb(145, 62, 27),
                Rgb(177, 127, 56),
                0.44f)
        };

        private static readonly IReadOnlyList<BarDrinkPresentation>
            presentationsView = Array.AsReadOnly(presentations);

        public static IReadOnlyList<BarDrinkPresentation> Presentations =>
            presentationsView;

        public static bool TryGet(
            DrinkId drinkId,
            out BarDrinkPresentation presentation)
        {
            for (int index = 0; index < presentations.Length; index++)
            {
                if (presentations[index].DrinkId == drinkId)
                {
                    presentation = presentations[index];
                    return true;
                }
            }

            presentation = default;
            return false;
        }

        public static BarDrinkPresentation Get(DrinkId drinkId)
        {
            if (TryGet(drinkId, out BarDrinkPresentation presentation))
            {
                return presentation;
            }

            throw new ArgumentOutOfRangeException(
                nameof(drinkId),
                drinkId,
                "Only retail bar drinks have a presentation.");
        }

        private static BarDrinkPresentation Define(
            DrinkId drinkId,
            string stableId,
            BarDrinkBottleStyle bottleStyle,
            BarDrinkVesselKind vesselKind,
            Color32 bottleColor,
            Color32 liquidColor,
            Color32 labelColor,
            float targetFill)
        {
            return new BarDrinkPresentation(
                drinkId,
                stableId,
                bottleStyle,
                vesselKind,
                bottleColor,
                liquidColor,
                labelColor,
                targetFill);
        }

        private static Color32 Rgb(byte red, byte green, byte blue)
        {
            return new Color32(red, green, blue, byte.MaxValue);
        }
    }
}
