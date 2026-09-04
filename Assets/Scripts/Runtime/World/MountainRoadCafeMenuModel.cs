using System;
using System.Collections.Generic;

namespace BarPromenade.Runtime.World
{
    public enum MountainRoadCafeMenuState
    {
        Hidden = (int)CounterMenuState.Hidden,
        Delivering = (int)CounterMenuState.Delivering,
        Open = (int)CounterMenuState.Open,
        Confirmed = (int)CounterMenuState.Confirmed,
        Retrieving = (int)CounterMenuState.Retrieving,
        Closed = (int)CounterMenuState.Closed,
        Resting = (int)CounterMenuState.Resting
    }

    /// <summary>
    /// Stable identifiers in the order in which the cafe presents them.
    /// Player-visible names belong to the localized view, not to this model.
    /// </summary>
    public static class MountainRoadCafeMenuItemIds
    {
        public const string FriedEggs =
            "mountain.cafe.menu.item.fried_eggs";
        public const string CheeseSandwich =
            "mountain.cafe.menu.item.cheese_sandwich";
        public const string BlackCoffee =
            "mountain.cafe.menu.item.black_coffee";

        private static readonly IReadOnlyList<string> ordered =
            Array.AsReadOnly(new[]
            {
                FriedEggs,
                CheeseSandwich,
                BlackCoffee
            });

        public static IReadOnlyList<string> Ordered => ordered;
    }

    /// <summary>
    /// Mountain-cafe compatibility adapter over the common counter-menu
    /// model. It keeps the existing public API and domain enum intact.
    /// </summary>
    public sealed class MountainRoadCafeMenuModel
    {
        private readonly CounterMenuModel core =
            new CounterMenuModel(MountainRoadCafeMenuItemIds.Ordered);

        public MountainRoadCafeMenuState State =>
            (MountainRoadCafeMenuState)core.State;
        public int SelectedIndex => core.SelectedIndex;
        public string SelectedItemId => core.SelectedItemId;
        public string ConfirmedItemId => core.ConfirmedItemId;
        public CounterMenuModel Core => core;

        public void Reset()
        {
            core.Reset();
        }

        public bool BeginDelivery()
        {
            return core.BeginDelivery();
        }

        public bool Open()
        {
            return core.Open();
        }

        public bool MovePrevious()
        {
            return core.MovePrevious();
        }

        public bool MoveNext()
        {
            return core.MoveNext();
        }

        public bool Confirm()
        {
            return core.Confirm();
        }

        public bool RestOnCounter()
        {
            return core.RestOnCounter();
        }

        public bool Reopen()
        {
            return core.Reopen();
        }

        public bool BeginRetrieval()
        {
            return core.BeginRetrieval();
        }

        public bool CompleteRetrieval()
        {
            return core.CompleteRetrieval();
        }
    }
}
