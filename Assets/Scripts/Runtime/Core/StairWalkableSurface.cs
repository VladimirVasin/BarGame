using UnityEngine;

namespace BarPromenade
{
    /// <summary>Physical stair support, including the hidden ramp under visible treads.</summary>
    [DisallowMultipleComponent]
    public sealed class StairWalkableSurface : MonoBehaviour
    {
        public static void Stamp(Collider collider)
        {
            if (collider != null && !collider.TryGetComponent<StairWalkableSurface>(out _))
            {
                collider.gameObject.AddComponent<StairWalkableSurface>();
            }
        }
    }
}
