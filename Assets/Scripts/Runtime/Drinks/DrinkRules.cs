using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public static class DrinkRules
    {
        private static readonly DrinkDefinition[] definitions =
        {
            new DrinkDefinition(DrinkId.LightBeer, DrinkFamily.Beer, 8),
            new DrinkDefinition(DrinkId.DarkBeer, DrinkFamily.Beer, 10),
            new DrinkDefinition(DrinkId.WhiteWine, DrinkFamily.Wine, 11),
            new DrinkDefinition(DrinkId.RedWine, DrinkFamily.Wine, 13),
            new DrinkDefinition(DrinkId.Vodka, DrinkFamily.Vodka, 18),
            new DrinkDefinition(DrinkId.PepperVodka, DrinkFamily.Vodka, 20),
            new DrinkDefinition(DrinkId.CognacVs, DrinkFamily.Cognac, 16),
            new DrinkDefinition(DrinkId.CognacVsop, DrinkFamily.Cognac, 18),
            new DrinkDefinition(DrinkId.Water, DrinkFamily.Water, 0)
        };

        private static readonly IReadOnlyList<DrinkDefinition> readOnlyDefinitions =
            Array.AsReadOnly(definitions);

        public static IReadOnlyList<DrinkDefinition> Definitions =>
            readOnlyDefinitions;

        public static DrinkDefinition GetDefinition(DrinkId id)
        {
            switch (id)
            {
                case DrinkId.None:
                    return new DrinkDefinition(
                        DrinkId.None,
                        DrinkFamily.None,
                        0);
                case DrinkId.LightBeer:
                    return definitions[0];
                case DrinkId.DarkBeer:
                    return definitions[1];
                case DrinkId.WhiteWine:
                    return definitions[2];
                case DrinkId.RedWine:
                    return definitions[3];
                case DrinkId.Vodka:
                    return definitions[4];
                case DrinkId.PepperVodka:
                    return definitions[5];
                case DrinkId.CognacVs:
                    return definitions[6];
                case DrinkId.CognacVsop:
                    return definitions[7];
                case DrinkId.Water:
                    return definitions[8];
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        public static DrinkFamily GetFamily(DrinkId id)
        {
            return GetDefinition(id).Family;
        }

        public static int GetIntoxicationGain(DrinkId id)
        {
            return GetDefinition(id).IntoxicationGain;
        }

        public static bool IsAlcoholic(DrinkId id)
        {
            DrinkFamily family = GetFamily(id);
            return family != DrinkFamily.None && family != DrinkFamily.Water;
        }

        public static bool AreCompatible(DrinkId first, DrinkId second)
        {
            DrinkFamily firstFamily = GetFamily(first);
            DrinkFamily secondFamily = GetFamily(second);
            if (firstFamily == DrinkFamily.None ||
                secondFamily == DrinkFamily.None ||
                firstFamily == DrinkFamily.Water ||
                secondFamily == DrinkFamily.Water)
            {
                return true;
            }

            if (firstFamily == secondFamily)
            {
                return true;
            }

            return
                (firstFamily == DrinkFamily.Wine &&
                 secondFamily == DrinkFamily.Cognac) ||
                (firstFamily == DrinkFamily.Cognac &&
                 secondFamily == DrinkFamily.Wine);
        }
    }
}
