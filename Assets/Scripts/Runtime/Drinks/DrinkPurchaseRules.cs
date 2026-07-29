using System;

namespace BarPromenade
{
    public enum DrinkPurchaseStatus
    {
        Success = 0,
        NotOffered,
        InsufficientFunds,
        MaximumIntoxication
    }

    public readonly struct DrinkPurchaseResult
    {
        internal DrinkPurchaseResult(
            DrinkPurchaseStatus status,
            DrinkId requestedDrink,
            BarDrinkOffer offer,
            int cashBefore,
            int cashAfter,
            int intoxicationBefore,
            int intoxicationAfter,
            int actualIntoxicationDelta,
            DrinkId lastAlcoholicDrinkBefore,
            DrinkId lastAlcoholicDrinkAfter,
            int drinksConsumedBefore,
            int drinksConsumedAfter)
        {
            Status = status;
            RequestedDrink = requestedDrink;
            Offer = offer;
            CashBefore = cashBefore;
            CashAfter = cashAfter;
            IntoxicationBefore = intoxicationBefore;
            IntoxicationAfter = intoxicationAfter;
            ActualIntoxicationDelta = actualIntoxicationDelta;
            LastAlcoholicDrinkBefore = lastAlcoholicDrinkBefore;
            LastAlcoholicDrinkAfter = lastAlcoholicDrinkAfter;
            DrinksConsumedBefore = drinksConsumedBefore;
            DrinksConsumedAfter = drinksConsumedAfter;
        }

        public bool Succeeded => Status == DrinkPurchaseStatus.Success;
        public DrinkPurchaseStatus Status { get; }
        public DrinkId RequestedDrink { get; }
        public BarDrinkOffer Offer { get; }
        public int CashBefore { get; }
        public int CashAfter { get; }
        public int IntoxicationBefore { get; }
        public int IntoxicationAfter { get; }
        public int ActualIntoxicationDelta { get; }
        public DrinkId LastAlcoholicDrinkBefore { get; }
        public DrinkId LastAlcoholicDrinkAfter { get; }
        public int DrinksConsumedBefore { get; }
        public int DrinksConsumedAfter { get; }
    }

    public static class DrinkPurchaseRules
    {
        public static DrinkPurchaseResult Evaluate(
            DrinkId drinkId,
            int cashBalance,
            int intoxication,
            DrinkId lastAlcoholicDrink,
            int drinksConsumed)
        {
            ValidateState(
                cashBalance,
                intoxication,
                lastAlcoholicDrink,
                drinksConsumed);

            if (!BarDrinkCatalog.TryGetOffer(
                    drinkId,
                    out BarDrinkOffer offer))
            {
                return CreateFailure(
                    DrinkPurchaseStatus.NotOffered,
                    drinkId,
                    default,
                    cashBalance,
                    intoxication,
                    lastAlcoholicDrink,
                    drinksConsumed);
            }

            bool isAlcoholic = DrinkRules.IsAlcoholic(drinkId);
            if (isAlcoholic &&
                intoxication >= IntoxicationStageRules.MaximumLevel)
            {
                return CreateFailure(
                    DrinkPurchaseStatus.MaximumIntoxication,
                    drinkId,
                    offer,
                    cashBalance,
                    intoxication,
                    lastAlcoholicDrink,
                    drinksConsumed);
            }

            if (cashBalance < offer.Price)
            {
                return CreateFailure(
                    DrinkPurchaseStatus.InsufficientFunds,
                    drinkId,
                    offer,
                    cashBalance,
                    intoxication,
                    lastAlcoholicDrink,
                    drinksConsumed);
            }

            int intoxicationAfter = intoxication;
            DrinkId lastAlcoholicDrinkAfter = lastAlcoholicDrink;
            if (isAlcoholic)
            {
                intoxicationAfter = Math.Min(
                    IntoxicationStageRules.MaximumLevel,
                    intoxication +
                    DrinkRules.GetIntoxicationGain(drinkId));
                lastAlcoholicDrinkAfter = drinkId;
            }

            return new DrinkPurchaseResult(
                DrinkPurchaseStatus.Success,
                drinkId,
                offer,
                cashBalance,
                cashBalance - offer.Price,
                intoxication,
                intoxicationAfter,
                intoxicationAfter - intoxication,
                lastAlcoholicDrink,
                lastAlcoholicDrinkAfter,
                drinksConsumed,
                drinksConsumed + 1);
        }

        private static DrinkPurchaseResult CreateFailure(
            DrinkPurchaseStatus status,
            DrinkId requestedDrink,
            BarDrinkOffer offer,
            int cashBalance,
            int intoxication,
            DrinkId lastAlcoholicDrink,
            int drinksConsumed)
        {
            return new DrinkPurchaseResult(
                status,
                requestedDrink,
                offer,
                cashBalance,
                cashBalance,
                intoxication,
                intoxication,
                0,
                lastAlcoholicDrink,
                lastAlcoholicDrink,
                drinksConsumed,
                drinksConsumed);
        }

        private static void ValidateState(
            int cashBalance,
            int intoxication,
            DrinkId lastAlcoholicDrink,
            int drinksConsumed)
        {
            if (cashBalance < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cashBalance));
            }

            if (intoxication < 0 ||
                intoxication > IntoxicationStageRules.MaximumLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intoxication));
            }

            if (lastAlcoholicDrink != DrinkId.None &&
                !DrinkRules.IsAlcoholic(lastAlcoholicDrink))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lastAlcoholicDrink));
            }

            if (drinksConsumed < 0 ||
                drinksConsumed == int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(drinksConsumed));
            }
        }
    }
}
