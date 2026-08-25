using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The single Resources handle for the four bespoke staged prefabs.
    /// The prefabs themselves stay outside Resources and cannot leak into
    /// the ordinary city pedestrian catalogue.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MountainRoadCafeCastProvider",
        menuName = "Bar Promenade/Mountain Road Cafe Cast Provider")]
    public sealed class MountainRoadCafeCastProvider : ScriptableObject
    {
        public const string ResourcePath =
            "MountainRoad/MountainRoadCafeCastProvider";

        [SerializeField] private GameObject lonePatronPrefab;
        [SerializeField] private GameObject pairManPrefab;
        [SerializeField] private GameObject pairWomanPrefab;
        [SerializeField] private GameObject attendantPrefab;

        public GameObject LonePatronPrefab => lonePatronPrefab;
        public GameObject PairManPrefab => pairManPrefab;
        public GameObject PairWomanPrefab => pairWomanPrefab;
        public GameObject AttendantPrefab => attendantPrefab;

        public bool HasCompleteCast =>
            lonePatronPrefab != null &&
            pairManPrefab != null &&
            pairWomanPrefab != null &&
            attendantPrefab != null;

        public static MountainRoadCafeCastProvider Load()
        {
            return Resources.Load<MountainRoadCafeCastProvider>(
                ResourcePath);
        }

        public GameObject GetPrefab(MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.LonePatron:
                    return lonePatronPrefab;
                case MountainRoadCafeCastRole.PairMan:
                    return pairManPrefab;
                case MountainRoadCafeCastRole.PairWoman:
                    return pairWomanPrefab;
                case MountainRoadCafeCastRole.Attendant:
                    return attendantPrefab;
                default:
                    return null;
            }
        }

        public void Configure(
            GameObject configuredLonePatronPrefab,
            GameObject configuredPairManPrefab,
            GameObject configuredPairWomanPrefab,
            GameObject configuredAttendantPrefab)
        {
            lonePatronPrefab = configuredLonePatronPrefab;
            pairManPrefab = configuredPairManPrefab;
            pairWomanPrefab = configuredPairWomanPrefab;
            attendantPrefab = configuredAttendantPrefab;
        }
    }
}
