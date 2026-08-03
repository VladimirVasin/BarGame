using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class SupermarketInteriorWorldResult
    {
        internal SupermarketInteriorWorldResult(
            Transform root,
            IList<SupermarketShelfView> shelves,
            Transform checkoutRoot,
            BarNpcDirector cashierDirector)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Shelves = new ReadOnlyCollection<SupermarketShelfView>(
                new List<SupermarketShelfView>(
                    shelves ?? throw new ArgumentNullException(
                        nameof(shelves))));
            CheckoutRoot = checkoutRoot ?? throw new ArgumentNullException(
                nameof(checkoutRoot));
            CashierDirector = cashierDirector ??
                throw new ArgumentNullException(nameof(cashierDirector));
        }

        public Transform Root { get; }
        public IReadOnlyList<SupermarketShelfView> Shelves { get; }
        public Transform CheckoutRoot { get; }
        public BarNpcDirector CashierDirector { get; }

        public bool TryGetShelf(
            string shelfId,
            out SupermarketShelfView shelf)
        {
            for (int index = 0; index < Shelves.Count; index++)
            {
                if (string.Equals(
                        Shelves[index].Id,
                        shelfId,
                        StringComparison.Ordinal))
                {
                    shelf = Shelves[index];
                    return true;
                }
            }

            shelf = null;
            return false;
        }
    }
}
