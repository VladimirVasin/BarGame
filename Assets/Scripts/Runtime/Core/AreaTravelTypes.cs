using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A separately loaded, top-level part of the game world. Values are
    /// deliberately independent from build indices so future scenes can be
    /// appended without changing persisted or UI-facing area identities.
    /// </summary>
    public enum GameAreaId
    {
        City = 0,
        MountainRoad = 1,

        /// <summary>
        /// The village above the cableway. Reached only by the cabin - there is
        /// no road, no path and no second way in from either end.
        /// </summary>
        AlpineVillage = 2
    }

    /// <summary>
    /// Describes why the destination was entered. A destination root may
    /// consume the token once and choose the matching spawn in a later pass.
    /// </summary>
    public enum AreaArrivalToken
    {
        Default = 0,
        MapTeleport = 1,
        Tunnel = 2,

        /// <summary>
        /// The map was asked for a specific place on the other tab, so the
        /// request carries a coordinate and the destination root is expected
        /// to spawn on it instead of at its own front door.
        /// </summary>
        MapPoint = 3,

        /// <summary>
        /// The hero did not walk here: he is sitting in the Ferryman's car,
        /// which drove into the city's south tunnel and has to come out of the
        /// mountain's one still moving, with him still in the seat.
        ///
        /// This is the only token that arrives with the player already inside
        /// a contextual interaction, so <see cref="MountainRoadRoot"/> raises
        /// the car before it decides where to put him and then hands him
        /// straight back to it.
        /// </summary>
        Ferryman = 4,

        /// <summary>
        /// The hero is on the cableway cabin's bench and the line is still
        /// running. Like <see cref="Ferryman"/> this arrives with the player
        /// already inside a contextual interaction, so the destination root
        /// raises the cabin before it decides where to put him and hands him
        /// straight back to it.
        /// </summary>
        Cableway = 5
    }

    public readonly struct AreaTravelRequest : IEquatable<AreaTravelRequest>
    {
        public AreaTravelRequest(
            GameAreaId destinationArea,
            AreaArrivalToken arrivalToken = AreaArrivalToken.Default)
            : this(destinationArea, arrivalToken, Vector3.zero, false)
        {
        }

        private AreaTravelRequest(
            GameAreaId destinationArea,
            AreaArrivalToken arrivalToken,
            Vector3 arrivalPosition,
            bool hasArrivalPosition)
        {
            DestinationArea = destinationArea;
            ArrivalToken = arrivalToken;
            ArrivalPosition = arrivalPosition;
            HasArrivalPosition = hasArrivalPosition;
        }

        /// <summary>
        /// Travel that ends somewhere in particular.
        ///
        /// Picking a place on the other tab and being put down at that
        /// area's front door is not the same answer to the same question,
        /// and the chart already knows the coordinate. The destination root
        /// still clamps it to its own ground - the map draws places, it does
        /// not promise a capsule fits there.
        /// </summary>
        public static AreaTravelRequest ToPoint(
            GameAreaId destinationArea,
            Vector3 arrivalPosition)
        {
            return new AreaTravelRequest(
                destinationArea,
                AreaArrivalToken.MapPoint,
                arrivalPosition,
                true);
        }

        public GameAreaId DestinationArea { get; }
        public AreaArrivalToken ArrivalToken { get; }
        public Vector3 ArrivalPosition { get; }
        public bool HasArrivalPosition { get; }

        public bool IsValid =>
            AreaSceneCatalog.IsSupported(DestinationArea) &&
            Enum.IsDefined(typeof(AreaArrivalToken), ArrivalToken) &&
            (!HasArrivalPosition || IsFinite(ArrivalPosition));

        public bool Equals(AreaTravelRequest other)
        {
            return DestinationArea == other.DestinationArea &&
                   ArrivalToken == other.ArrivalToken &&
                   HasArrivalPosition == other.HasArrivalPosition &&
                   (!HasArrivalPosition ||
                    ArrivalPosition == other.ArrivalPosition);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public override bool Equals(object obj)
        {
            return obj is AreaTravelRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ((int)DestinationArea * 397) ^
                           (int)ArrivalToken;
                return HasArrivalPosition
                    ? (hash * 397) ^ ArrivalPosition.GetHashCode()
                    : hash;
            }
        }

        public static bool operator ==(
            AreaTravelRequest left,
            AreaTravelRequest right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            AreaTravelRequest left,
            AreaTravelRequest right)
        {
            return !left.Equals(right);
        }
    }

    public static class AreaSceneCatalog
    {
        public static bool IsSupported(GameAreaId area)
        {
            return area == GameAreaId.City ||
                   area == GameAreaId.MountainRoad ||
                   area == GameAreaId.AlpineVillage;
        }

        public static string GetSceneName(GameAreaId area)
        {
            switch (area)
            {
                case GameAreaId.City:
                    return SceneIds.City;
                case GameAreaId.MountainRoad:
                    return SceneIds.MountainRoad;
                case GameAreaId.AlpineVillage:
                    return SceneIds.AlpineVillage;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(area),
                        area,
                        "Unsupported separately loaded game area.");
            }
        }

        public static bool TryGetArea(
            string sceneName,
            out GameAreaId area)
        {
            if (string.Equals(
                    sceneName,
                    SceneIds.City,
                    StringComparison.Ordinal))
            {
                area = GameAreaId.City;
                return true;
            }

            if (string.Equals(
                    sceneName,
                    SceneIds.MountainRoad,
                    StringComparison.Ordinal))
            {
                area = GameAreaId.MountainRoad;
                return true;
            }

            if (string.Equals(
                    sceneName,
                    SceneIds.AlpineVillage,
                    StringComparison.Ordinal))
            {
                area = GameAreaId.AlpineVillage;
                return true;
            }

            area = default;
            return false;
        }
    }
}
