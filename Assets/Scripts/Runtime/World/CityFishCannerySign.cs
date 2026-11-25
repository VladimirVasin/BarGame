using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityFishCannerySign
    {
        internal const string ClockName = "Clock quarter fish cannery advertisement";
        internal const string ClockPurpose = "ClockFishSign";
        private const string RidgePurpose = "RidgeFishSign";

        internal static void BuildClock(CityLayout layout, BuildingLot lot, CityBuildingAssetRegistry building)
        {
            if (!layout.SpatialPlan.IsCompactCoastal || layout.Seed != GameSessionState.DefaultCitySeed ||
                !layout.TryGetPrimaryLandmarkCell(CityDistrictKind.OldTown, out var clockCell) ||
                lot.Cell != clockCell) return;
            Vector3 size = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, lot.BuildingVariant);
            Attach(building.transform, new Vector3(size.x * .5f, 3.66f, -.28f),
                ClockPurpose, ClockName, "Left", "Right");
        }

        internal static void BuildRidge(CityBuildingAssetRegistry building, Vector3 envelope) =>
            Attach(building.transform, new Vector3(envelope.x * .5f, 8.55f, -.6f),
                RidgePurpose, "Ridge fish cannery advertisement", "Upper", "Lower");

        private static void Attach(Transform wall, Vector3 centre, string purpose, string name,
            string firstFixture, string secondFixture)
        {
            string path = "City/" + purpose + "/" + purpose;
            var model = Resources.Load<GameObject>(path);
            var print = Resources.Load<Texture2D>(path + "Albedo");
            if (!model || !print) throw new InvalidOperationException("Missing cannery billboard frame or print: " + path);
            var sign = UnityEngine.Object.Instantiate(model, wall, false);
            sign.name = name;
            sign.transform.localPosition = centre;
            sign.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            foreach (Renderer part in sign.GetComponentsInChildren<Renderer>())
            {
                switch (part.name)
                {
                    case "PrintedFace":
                        part.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                        var properties = new MaterialPropertyBlock();
                        properties.SetTexture("_BaseMap", print);
                        properties.SetColor("_BaseColor", Color.white);
                        properties.SetFloat("_Smoothness", .06f);
                        part.SetPropertyBlock(properties);
                        break;
                    case "FloodDiffusers":
                        part.sharedMaterial = CityNightResources.EmissiveMaterial;
                        CityNightGlowRegistry.Register(part, new Color(1.6f, 1.68f, 1.46f));
                        break;
                    default:
                        part.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial("SteelDark");
                        CitySurfaceLibrary.ApplyRenderer(part);
                        break;
                }
            }

            Aim(firstFixture);
            Aim(secondFixture);

            void Aim(string side)
            {
                Transform lens = CityResidentialCourtyardAssetProvider.FindPart(sign.transform, "ANCHOR_Flood" + side);
                Transform aim = CityResidentialCourtyardAssetProvider.FindPart(sign.transform, "ANCHOR_Aim" + side);
                lens.name = CityNightAtmosphere.IndustrialTaskLightPrefix + purpose;
                lens.rotation = Quaternion.LookRotation(aim.position - lens.position, wall.up);
            }
        }
    }
}
