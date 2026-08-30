using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized bridge to the bespoke cemetery raven prefab.
    /// The prefab itself lives outside Resources; only this
    /// ScriptableObject is addressable, and its single reference is
    /// what carries the prefab into a build — the stairwell cat's
    /// provider pattern, on the cemetery's own shelf.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CemeteryRavenProvider",
        menuName = "Bar Promenade/Cemetery Raven Provider")]
    public sealed class CemeteryRavenProvider : ScriptableObject
    {
        public const string ResourcePath =
            "Cemetery/CemeteryRavenProvider";
        public const string DesignId = "cemetery_raven_v1";

        [SerializeField] private GameObject ravenPrefab;

        public GameObject RavenPrefab => ravenPrefab;

        public static CemeteryRavenProvider Load()
        {
            return Resources.Load<CemeteryRavenProvider>(ResourcePath);
        }
    }
}
