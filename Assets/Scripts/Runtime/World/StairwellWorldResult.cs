using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed class StairwellWorldResult
    {
        public StairwellWorldResult(
            Transform root,
            IReadOnlyList<Collider> stairColliders,
            Collider upperBlocker,
            Transform streetDoor,
            Transform apartmentDoor)
        {
            Root = root;
            StairColliders = stairColliders;
            UpperBlocker = upperBlocker;
            StreetDoor = streetDoor;
            ApartmentDoor = apartmentDoor;
        }

        public Transform Root { get; }
        public IReadOnlyList<Collider> StairColliders { get; }
        public Collider UpperBlocker { get; }
        public Transform StreetDoor { get; }
        public Transform ApartmentDoor { get; }
    }
}
