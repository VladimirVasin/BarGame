using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class HomeBalconyLayoutPlan
    {
        internal HomeBalconyLayoutPlan(
            Rect interiorAccessPath,
            Rect doorwayBridge,
            Rect balconyBounds,
            Vector3 doorCenter,
            Vector3 doorSize,
            Vector3 windowCenter,
            Vector3 windowSize,
            IList<Rect> walkableRectangles)
        {
            InteriorAccessPath = interiorAccessPath;
            DoorwayBridge = doorwayBridge;
            BalconyBounds = balconyBounds;
            DoorCenter = doorCenter;
            DoorSize = doorSize;
            WindowCenter = windowCenter;
            WindowSize = windowSize;
            WalkableRectangles =
                new ReadOnlyCollection<Rect>(
                    new List<Rect>(walkableRectangles));
        }

        public Rect InteriorAccessPath { get; }
        public Rect DoorwayBridge { get; }
        public Rect BalconyBounds { get; }
        public Vector3 DoorCenter { get; }
        public Vector3 DoorSize { get; }
        public Vector3 WindowCenter { get; }
        public Vector3 WindowSize { get; }
        public IReadOnlyList<Rect> WalkableRectangles { get; }
        public float StreetGroundY =>
            -PlayerHomeBalconyGeometry
                .ApartmentFloorElevation;

        public Rect BalconyCameraActivationBounds =>
            Rect.MinMaxRect(
                PlayerHomeBalconyGeometry.HomeFacadeX + 0.18f,
                BalconyBounds.yMin + 0.12f,
                BalconyBounds.xMax - 0.18f,
                BalconyBounds.yMax - 0.12f);
    }
}
