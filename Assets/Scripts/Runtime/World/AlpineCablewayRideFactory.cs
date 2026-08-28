using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Installs the boarding offer and the ride at one terminal.
    ///
    /// Both ends of the line go through here on purpose. The mountain station
    /// and the village station are the same problem seen from opposite sides,
    /// and the one thing that must never drift between them is which of the
    /// two legs is armed: departing arms nothing until the hero sits down,
    /// arriving arms immediately and waits under the black screen.
    /// </summary>
    public static class AlpineCablewayRideFactory
    {
        public const string RootName = "Cableway Boarding";

        /// <summary>
        /// Result of installing one terminal. The ride is null on a visit
        /// where nobody is boarding and nobody has arrived - the offer still
        /// stands, it simply has not been taken.
        /// </summary>
        public readonly struct Installation
        {
            internal Installation(
                AlpineCablewayCabinSeat seat,
                AlpineCablewayRideController ride)
            {
                Seat = seat;
                Ride = ride;
            }

            public AlpineCablewayCabinSeat Seat { get; }
            public AlpineCablewayRideController Ride { get; }
        }

        public static Installation Install(
            Transform parent,
            PlayerRuntime player,
            Camera camera,
            MountainCablewayWorldResult cableway,
            MountainRoadCablewayPlan cablewayPlan,
            GameAreaId destinationArea,
            bool arrivingByCabin)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (cableway == null)
            {
                throw new ArgumentNullException(nameof(cableway));
            }

            if (cablewayPlan == null)
            {
                throw new ArgumentNullException(nameof(cablewayPlan));
            }

            if (player.GameObject == null)
            {
                GameLog.Warning("cableway", "boarding_no_player");
                return default;
            }

            var controller = player.GameObject
                .GetComponent<PlayerAnimatedInteractionController>();
            if (controller == null)
            {
                controller = player.GameObject
                    .AddComponent<PlayerAnimatedInteractionController>();
            }

            if (!controller.IsInitialized)
            {
                controller.Initialize(player, camera);
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            AlpineCablewayCabinSeat seat =
                root.AddComponent<AlpineCablewayCabinSeat>();
            seat.Initialize(
                player,
                controller,
                cableway.Controller,
                AlpineCablewayCabinSeatPlan.Create(cablewayPlan),
                camera);

            AlpineCablewayRideController ride = arrivingByCabin
                ? AlpineCablewayRideController.CreateForArrival(
                    parent,
                    seat,
                    cableway.Controller,
                    () => cablewayPlan,
                    destinationArea)
                : AlpineCablewayRideController.CreateForDeparture(
                    parent,
                    seat,
                    cableway.Controller,
                    cablewayPlan,
                    destinationArea);
            return new Installation(seat, ride);
        }
    }
}
