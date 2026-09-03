using System;
using System.Collections.Generic;

namespace BarPromenade.Runtime.World
{
    /// <summary>
    /// Domain-neutral lifecycle shared by physical counter menus. Delivery,
    /// input and prop motion stay in scene adapters; this class owns only the
    /// ordered selection and the one-shot confirmation contract.
    /// </summary>
    public enum CounterMenuState
    {
        Hidden = 0,
        Delivering = 1,
        Open = 2,
        Confirmed = 3,
        Retrieving = 4,
        Closed = 5
    }

    public sealed class CounterMenuModel
    {
        private readonly IReadOnlyList<string> itemIds;

        public CounterMenuModel(IReadOnlyList<string> orderedItemIds)
        {
            if (orderedItemIds == null || orderedItemIds.Count == 0)
            {
                throw new ArgumentException(
                    "A counter menu requires at least one item.",
                    nameof(orderedItemIds));
            }

            var copy = new string[orderedItemIds.Count];
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < orderedItemIds.Count; index++)
            {
                string itemId = orderedItemIds[index];
                if (string.IsNullOrWhiteSpace(itemId) ||
                    !unique.Add(itemId))
                {
                    throw new ArgumentException(
                        "Counter-menu item identifiers must be non-empty " +
                        "and unique.",
                        nameof(orderedItemIds));
                }

                copy[index] = itemId;
            }

            itemIds = Array.AsReadOnly(copy);
            Reset();
        }

        public IReadOnlyList<string> ItemIds => itemIds;
        public CounterMenuState State { get; private set; }
        public int SelectedIndex { get; private set; }
        public string SelectedItemId => itemIds[SelectedIndex];
        public string ConfirmedItemId { get; private set; }

        public void Reset()
        {
            State = CounterMenuState.Hidden;
            SelectedIndex = 0;
            ConfirmedItemId = null;
        }

        public bool BeginDelivery()
        {
            if (State != CounterMenuState.Hidden)
            {
                return false;
            }

            State = CounterMenuState.Delivering;
            return true;
        }

        public bool Open()
        {
            if (State != CounterMenuState.Delivering)
            {
                return false;
            }

            State = CounterMenuState.Open;
            return true;
        }

        public bool MovePrevious()
        {
            return Move(-1);
        }

        public bool MoveNext()
        {
            return Move(1);
        }

        public bool Select(int index)
        {
            if (State != CounterMenuState.Open ||
                index < 0 || index >= itemIds.Count)
            {
                return false;
            }

            SelectedIndex = index;
            return true;
        }

        public bool Confirm()
        {
            if (State != CounterMenuState.Open)
            {
                return false;
            }

            ConfirmedItemId = SelectedItemId;
            State = CounterMenuState.Confirmed;
            return true;
        }

        public bool BeginRetrieval()
        {
            if (State != CounterMenuState.Delivering &&
                State != CounterMenuState.Open &&
                State != CounterMenuState.Confirmed)
            {
                return false;
            }

            State = CounterMenuState.Retrieving;
            return true;
        }

        public bool CompleteRetrieval()
        {
            if (State != CounterMenuState.Retrieving)
            {
                return false;
            }

            State = CounterMenuState.Closed;
            return true;
        }

        private bool Move(int direction)
        {
            if (State != CounterMenuState.Open)
            {
                return false;
            }

            int count = itemIds.Count;
            SelectedIndex = (SelectedIndex + direction + count) % count;
            return true;
        }
    }
}
