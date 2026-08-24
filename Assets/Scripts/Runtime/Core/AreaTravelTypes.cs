using System;

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
        MountainRoad = 1
    }

    /// <summary>
    /// Describes why the destination was entered. A destination root may
    /// consume the token once and choose the matching spawn in a later pass.
    /// </summary>
    public enum AreaArrivalToken
    {
        Default = 0,
        MapTeleport = 1,
        Tunnel = 2
    }

    public readonly struct AreaTravelRequest : IEquatable<AreaTravelRequest>
    {
        public AreaTravelRequest(
            GameAreaId destinationArea,
            AreaArrivalToken arrivalToken = AreaArrivalToken.Default)
        {
            DestinationArea = destinationArea;
            ArrivalToken = arrivalToken;
        }

        public GameAreaId DestinationArea { get; }
        public AreaArrivalToken ArrivalToken { get; }

        public bool IsValid =>
            AreaSceneCatalog.IsSupported(DestinationArea) &&
            Enum.IsDefined(typeof(AreaArrivalToken), ArrivalToken);

        public bool Equals(AreaTravelRequest other)
        {
            return DestinationArea == other.DestinationArea &&
                   ArrivalToken == other.ArrivalToken;
        }

        public override bool Equals(object obj)
        {
            return obj is AreaTravelRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)DestinationArea * 397) ^
                       (int)ArrivalToken;
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
                   area == GameAreaId.MountainRoad;
        }

        public static string GetSceneName(GameAreaId area)
        {
            switch (area)
            {
                case GameAreaId.City:
                    return SceneIds.City;
                case GameAreaId.MountainRoad:
                    return SceneIds.MountainRoad;
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

            area = default;
            return false;
        }
    }
}
