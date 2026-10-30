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
            Transform apartmentDoor,
            Collider descentWall = null,
            Collider upperLanding = null,
            Transform catRelief = null,
            Transform warehouseDoor = null)
        {
            Root = root;
            StairColliders = stairColliders;
            UpperBlocker = upperBlocker;
            StreetDoor = streetDoor;
            ApartmentDoor = apartmentDoor;
            DescentWall = descentWall;
            UpperLanding = upperLanding;
            CatRelief = catRelief;
            WarehouseDoor = warehouseDoor;
            CatReliefBounds = catRelief != null ? catRelief.GetComponent<Renderer>().bounds : default;
        }

        public Transform Root { get; }
        public IReadOnlyList<Collider> StairColliders { get; }
        public Collider UpperBlocker { get; }
        public Transform StreetDoor { get; }
        public Transform ApartmentDoor { get; }
        public Collider DescentWall { get; }
        public Collider UpperLanding { get; }
        public Transform CatRelief { get; }
        public Transform WarehouseDoor { get; }
        public Bounds CatReliefBounds { get; }
    }
}
