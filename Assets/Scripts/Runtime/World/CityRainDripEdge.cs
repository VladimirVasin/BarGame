using UnityEngine;

namespace BarPromenade
{
    /// <summary>An exposed lip on an existing canopy, authored in the canopy's frame.</summary>
    internal sealed class CityRainDripEdge : MonoBehaviour
    {
        private Vector3 first, last;

        internal static void Add(Transform parent, Vector3 first, Vector3 last)
        {
            var edge = parent.gameObject.AddComponent<CityRainDripEdge>();
            edge.first = first;
            edge.last = last;
        }

        internal Vector3 Sample(float fraction) => transform.TransformPoint(Vector3.Lerp(first, last, fraction));
    }
}
