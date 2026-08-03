using System;

namespace BarPromenade
{
    public enum SupermarketPurchaseStatus
    {
        Success = 0,
        InvalidSource,
        NotOffered,
        AlreadyPurchased,
        InsufficientFunds,
        InventoryFull
    }

    public readonly struct SupermarketPurchaseResult
    {
        internal SupermarketPurchaseResult(
            SupermarketPurchaseStatus status,
            string sourceId,
            InventoryItemId requestedItemId,
            SupermarketProductOffer offer,
            int cashBefore,
            int cashAfter,
            int itemCountBefore,
            int itemCountAfter)
        {
            Status = status;
            SourceId = sourceId ?? string.Empty;
            RequestedItemId = requestedItemId;
            Offer = offer;
            CashBefore = cashBefore;
            CashAfter = cashAfter;
            ItemCountBefore = itemCountBefore;
            ItemCountAfter = itemCountAfter;
        }

        public bool Succeeded => Status == SupermarketPurchaseStatus.Success;
        public SupermarketPurchaseStatus Status { get; }
        public string SourceId { get; }
        public InventoryItemId RequestedItemId { get; }
        public SupermarketProductOffer Offer { get; }
        public int CashBefore { get; }
        public int CashAfter { get; }
        public int ItemCountBefore { get; }
        public int ItemCountAfter { get; }
    }

    public static class SupermarketPurchaseRules
    {
        public static SupermarketPurchaseResult Evaluate(
            string sourceId,
            InventoryItemId itemId,
            bool sourceAlreadyPurchased,
            int cashBalance,
            int currentItemCount)
        {
            if (cashBalance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cashBalance));
            }

            if (currentItemCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentItemCount));
            }

            string normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
                ? string.Empty
                : sourceId.Trim();
            if (normalizedSourceId.Length == 0)
            {
                return CreateFailure(
                    SupermarketPurchaseStatus.InvalidSource,
                    normalizedSourceId,
                    itemId,
                    default,
                    cashBalance,
                    currentItemCount);
            }

            if (!SupermarketProductCatalog.TryGetOffer(
                    itemId,
                    out SupermarketProductOffer offer))
            {
                return CreateFailure(
                    SupermarketPurchaseStatus.NotOffered,
                    normalizedSourceId,
                    itemId,
                    default,
                    cashBalance,
                    currentItemCount);
            }

            InventoryItemDefinition definition =
                InventoryItemCatalog.Get(itemId);
            if (currentItemCount > definition.MaximumStack)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentItemCount),
                    currentItemCount,
                    "The current count exceeds the catalog stack limit.");
            }

            if (sourceAlreadyPurchased)
            {
                return CreateFailure(
                    SupermarketPurchaseStatus.AlreadyPurchased,
                    normalizedSourceId,
                    itemId,
                    offer,
                    cashBalance,
                    currentItemCount);
            }

            if (cashBalance < offer.Price)
            {
                return CreateFailure(
                    SupermarketPurchaseStatus.InsufficientFunds,
                    normalizedSourceId,
                    itemId,
                    offer,
                    cashBalance,
                    currentItemCount);
            }

            if (currentItemCount >= definition.MaximumStack)
            {
                return CreateFailure(
                    SupermarketPurchaseStatus.InventoryFull,
                    normalizedSourceId,
                    itemId,
                    offer,
                    cashBalance,
                    currentItemCount);
            }

            return new SupermarketPurchaseResult(
                SupermarketPurchaseStatus.Success,
                normalizedSourceId,
                itemId,
                offer,
                cashBalance,
                cashBalance - offer.Price,
                currentItemCount,
                currentItemCount + 1);
        }

        private static SupermarketPurchaseResult CreateFailure(
            SupermarketPurchaseStatus status,
            string sourceId,
            InventoryItemId itemId,
            SupermarketProductOffer offer,
            int cashBalance,
            int currentItemCount)
        {
            return new SupermarketPurchaseResult(
                status,
                sourceId,
                itemId,
                offer,
                cashBalance,
                cashBalance,
                currentItemCount,
                currentItemCount);
        }
    }
}
