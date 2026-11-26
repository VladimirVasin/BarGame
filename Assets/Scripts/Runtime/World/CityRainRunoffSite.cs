using UnityEngine;

namespace BarPromenade
{
    // A passive anchor. The rain field owns the clock, queries and drawing for all sites.
    internal sealed class CityRainRunoffSite : MonoBehaviour
    {
        private Vector3 position, direction;

        internal Vector3 Position => transform.TransformPoint(position);
        internal Vector3 Direction => transform.TransformDirection(direction).normalized;
        internal float Width { get; private set; }
        internal float Depth { get; private set; }
        internal bool IsInlet => Depth > 0f;
        internal CityStreetSupportPlan StreetSupport { get; private set; }

        internal static void AddOutlet(Transform owner, Vector3 mouth, Vector3 outward, float boreRadius)
        {
            Add(owner, mouth, outward, boreRadius, 0f);
        }

        internal static void AddLocalOutlet(Transform owner, Vector3 mouth, Vector3 outward, float boreRadius) =>
            AddOutlet(owner, owner.TransformPoint(mouth), owner.TransformDirection(outward), boreRadius);

        internal static void AddInlet(Transform owner, Vector3 centre, Vector3 forward, float halfWidth, float halfDepth,
            CityStreetSupportPlan streetSupport = null)
        {
            Add(owner, centre, forward, halfWidth, halfDepth).StreetSupport = streetSupport;
        }

        private static CityRainRunoffSite Add(Transform owner, Vector3 point, Vector3 axis, float width, float depth)
        {
            var site = owner.gameObject.AddComponent<CityRainRunoffSite>();
            site.position = owner.InverseTransformPoint(point);
            site.direction = owner.InverseTransformDirection(axis);
            site.Width = width;
            site.Depth = depth;
            return site;
        }
    }
}
