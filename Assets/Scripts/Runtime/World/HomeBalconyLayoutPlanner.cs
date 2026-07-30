using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeBalconyLayoutPlanner
    {
        public static HomeBalconyLayoutPlan Generate(
            HomeInteriorLayoutPlan interior)
        {
            if (interior == null)
            {
                throw new ArgumentNullException(nameof(interior));
            }

            float facadeX =
                PlayerHomeBalconyGeometry.HomeFacadeX;
            float doorHalf =
                PlayerHomeBalconyGeometry.DoorWidth * 0.5f;
            float balconyHalf =
                PlayerHomeBalconyGeometry.BalconyWidth * 0.5f;

            Rect interiorAccess = Rect.MinMaxRect(
                2.55f,
                PlayerHomeBalconyGeometry.DoorCenterZ -
                doorHalf -
                0.16f,
                interior.WalkableBounds.xMax,
                PlayerHomeBalconyGeometry.DoorCenterZ +
                doorHalf +
                0.16f);
            Rect doorwayBridge = Rect.MinMaxRect(
                interior.WalkableBounds.xMax - 0.90f,
                PlayerHomeBalconyGeometry.DoorCenterZ -
                doorHalf -
                0.12f,
                facadeX + 0.82f,
                PlayerHomeBalconyGeometry.DoorCenterZ +
                doorHalf +
                0.12f);
            Rect balconyBounds = Rect.MinMaxRect(
                facadeX -
                PlayerHomeBalconyGeometry.WallThickness * 0.5f,
                PlayerHomeBalconyGeometry.BalconyCenterZ -
                balconyHalf,
                facadeX +
                PlayerHomeBalconyGeometry.BalconyDepth,
                PlayerHomeBalconyGeometry.BalconyCenterZ +
                balconyHalf);

            var walkable = new List<Rect>
            {
                interior.WalkableBounds,
                doorwayBridge,
                balconyBounds
            };
            var plan = new HomeBalconyLayoutPlan(
                interiorAccess,
                doorwayBridge,
                balconyBounds,
                new Vector3(
                    facadeX,
                    PlayerHomeBalconyGeometry.DoorHeight * 0.5f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector3(
                    PlayerHomeBalconyGeometry.WallThickness +
                    0.04f,
                    PlayerHomeBalconyGeometry.DoorHeight,
                    PlayerHomeBalconyGeometry.DoorWidth),
                new Vector3(
                    facadeX,
                    PlayerHomeBalconyGeometry.WindowCenterY,
                    PlayerHomeBalconyGeometry.WindowCenterZ),
                new Vector3(
                    PlayerHomeBalconyGeometry.WallThickness +
                    0.04f,
                    PlayerHomeBalconyGeometry.WindowHeight,
                    PlayerHomeBalconyGeometry.WindowWidth),
                walkable);
            HomeBalconyLayoutValidator.ValidateOrThrow(
                interior,
                plan);
            return plan;
        }
    }
}
