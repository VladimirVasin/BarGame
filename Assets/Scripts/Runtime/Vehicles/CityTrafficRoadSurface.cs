using UnityEngine;

namespace BarPromenade
{
    /// <summary>Identifies a collider built exclusively from the drivable street surface.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(MeshCollider))]
    internal sealed class CityTrafficRoadSurface : MonoBehaviour { }
}
