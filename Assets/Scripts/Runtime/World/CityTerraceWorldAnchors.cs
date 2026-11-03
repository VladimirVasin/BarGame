using UnityEngine;

namespace BarPromenade
{
    /// <summary>Composition references; the shared City atmosphere owns all shelter behaviour.</summary>
    public sealed class CityTerraceWorldAnchors : MonoBehaviour
    {
        public Collider RainShelter { get; private set; }

        internal void Initialize(Collider rainShelter) => RainShelter = rainShelter;
    }
}
