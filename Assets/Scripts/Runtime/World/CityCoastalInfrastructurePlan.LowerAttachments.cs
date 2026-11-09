using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCoastalInfrastructurePlan
    {
        // The inlet plate is .34 m wide and .12 m deep. Pin centres leave its
        // back 5 mm inside the fixed-metre shell on every facade orientation.
        private static CityElectricalAttachment[] LowerAttachments(BuildingLot lot)
        {
            var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
            Vector3 size = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, lot.BuildingVariant);
            float height = Mathf.Min(8.2f, CityBuildingAssetProvider.GetExpectedBodyTop(lot.District, lot.BuildingVariant) - .55f);
            float halfWidth = size.x * .5f, back = -size.z * .5f;
            bool industrial = lot.District == CityDistrictKind.Industrial;
            float front = size.z * .5f - (lot.District == CityDistrictKind.Residential ? 1.2f : 0f);
            // The industrial loading aprons have narrow full-height front piers,
            // even when their shutter is recessed by 1.18 m. Side contacts land
            // behind the apron on the main wall. Variant 3 in Blender (index 2)
            // has only its five-metre left wing at the rear.
            float frontCorner = industrial ? .34f : .53f;
            float sideFront = front - (industrial ? 1.75f : .53f);
            bool wing = lot.BuildingVariant == 2;
            float rearRight = wing ? -halfWidth + 5f : halfWidth;
            float rightBack = wing ? 0f : back;
            return new[]
            {
                Pin(-halfWidth + frontCorner, front, Vector3.forward),
                Pin(halfWidth - frontCorner, front, Vector3.forward),
                Pin(halfWidth, sideFront, Vector3.right),
                Pin(-halfWidth, sideFront, Vector3.left),
                Pin(halfWidth, rightBack + .53f, Vector3.right),
                Pin(-halfWidth, back + .53f, Vector3.left),
                Pin(-halfWidth + .53f, back, Vector3.back),
                Pin(rearRight - .53f, back, Vector3.back)
            };

            CityElectricalAttachment Pin(float x, float z, Vector3 normal)
            {
                Vector3 outward = pose.Rotation * normal;
                return new CityElectricalAttachment(pose.TransformPoint(new Vector3(x, height, z)) + outward * .055f, outward);
            }
        }
    }
}
