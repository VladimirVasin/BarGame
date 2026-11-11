using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideHousePresentation
    {
        private static GameObject bay, airer, clerestory;
        private static readonly GameObject[] galleries = new GameObject[5];
        private static readonly Vector2Int FishSignHouse = new(-23, -7);
        private const string FishSignResource = "City/RidgeFishSign/";

        internal static void Build(Transform root, CityHillsideBuilding house, CityBuildingAssetRegistry building)
        {
            Quaternion facing = house.Lot.FacadeRotation;
            Vector3 entry = house.ThresholdWorld;
            int variant = house.Lot.BuildingVariant;
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(house.Lot.District, variant);
            if (house.Lot.Cell == FishSignHouse)
                BuildFishSign(building, envelope);
            if (house.HasGuardedEntry)
            {
                var railModel = Resources.Load<GameObject>(CityTerracePilotWorld.ResourceFolder + "TerraceRail");
                var guard = new GameObject("East Ridge entry rail").transform;
                guard.SetParent(root, false);
                for (int i = 1; i < house.EntryRail.Count; i++)
                    CityTerracePilotWorld.BuildRailRun(guard, railModel, "Entry rail", house.EntryRail[i - 1], house.EntryRail[i]);
                CitySurfaceLibrary.ApplyHierarchy(guard);
            }
            if (house.Bank == CityTerraceTransportBank.East)
            {
                // Face the actual pair of balcony doors, including the house
                // whose vestibule is offset to clear the central passage.
                Vector3 front = building.transform.TransformPoint(new Vector3(0f, 0f, envelope.z * .5f - 1.2f));
                var gallery = Place(ref galleries[variant], "BalconyGallery" + variant, root, front, facing);
                foreach (var anchor in gallery.GetComponentsInChildren<Transform>())
                {
                    if (!anchor.name.StartsWith("ANCHOR_Pier_", StringComparison.Ordinal)) continue;
                    CityTerracePilotWorld.Box(root, "Gallery column", anchor.position,
                        new Vector3(.20f, 2.76f, .26f), facing);
                }
            }
            else
            {
                // Both roof levels sit on the open front bed. Its high point
                // is still below the original rear stairhead on this section.
                float roofY = CityBuildingAssetProvider.GetExpectedBodyTop(house.Lot.District, variant) + .18f;
                Vector3 roof = building.transform.TransformPoint(new Vector3(.85f, roofY, .65f));
                var raised = Place(ref clerestory, "SteppedClerestory", root, roof, facing);
                CityTerracePilotWorld.Box(raised, "High roof step", new Vector3(1.05f, .5f, 0f),
                    new Vector3(4.42f, 1f, 3.25f), Quaternion.identity);
                CityTerracePilotWorld.Box(raised, "Low roof step", new Vector3(-2.25f, .25f, 0f),
                    new Vector3(2.10f, .50f, 2.55f), Quaternion.identity);
            }
            if (house.HasGlazedBay)
            {
                var stairs = Place(ref bay, "GlazedStairBay", root, entry, facing);
                Transform ceiling = CityResidentialCourtyardAssetProvider.FindPart(stairs, "ANCHOR_EntranceLampCeiling");
                // The first landing projects over the doorway. Keep the whole
                // fixture below its authored slab, with its chassis flush to
                // the soffit instead of intersecting it as a wall-mounted lamp.
                Transform mount = new GameObject(CityResidentialEntranceLighting.CeilingMountName).transform;
                mount.SetParent(building.transform, false);
                mount.SetPositionAndRotation(ceiling.position, facing);
                foreach (int side in new[] { -1, 1 })
                    CityTerracePilotWorld.Box(stairs, "Bay pier", new Vector3(side * .98f, 1.32f, .53f),
                        new Vector3(.21f, 2.64f, 1.06f), Quaternion.identity);
            }
            else if (house.Lot.BuildingVariant == 3 || house.Lot.Cell.y % 3 == 0)
                CityResidentialCourtyardAssetProvider.Create(root, CityResidentialCourtyardPropKind.EntranceCanopy, entry, facing);
            if (house.HasAirer)
            {
                // The writing/reading of the city's window mask remains with the
                // original building. The additional module sits on a real balcony.
                Place(ref airer, "BalconyAirer", root, house.AirerOrigin, facing);
            }

            // Keep both the independent porch approach and the graph turnaround
            // free. Props occupy only the side band within the level doorstep.
            bool shared = house.Lot.Cell.y == -21;
            if (house.Lot.BuildingVariant != 3 && (shared || (house.Lot.Cell.y + house.Lot.Cell.x) % 4 == 0))
            {
                bool gableEntry = Mathf.Abs(house.Lot.FacadeForward.x) > .5f;
                var kind = gableEntry || house.Lot.Cell.x % 2 == 0
                    ? CityResidentialCourtyardPropKind.Planter : CityResidentialCourtyardPropKind.Bench;
                float depth = house.Bank == CityTerraceTransportBank.East ? 1.86f : 1.15f;
                Vector3 spot = entry + facing * new Vector3(gableEntry ? 1.85f : 2.25f, 0f, depth);
                GameObject prop = CityResidentialCourtyardAssetProvider.Create(root, kind, spot, facing);
                var contact = CityResidentialCourtyardAssetProvider.GetLocalBounds(kind);
                var collider = prop.AddComponent<BoxCollider>();
                collider.center = contact.center;
                collider.size = contact.size;
            }
            if (house.Lot.BuildingVariant == 3)
            {
                Bounds passage = CityBuildingAssetProvider.GetExpectedPassageBounds(house.Lot.District, 3);
                Vector3 centre = building.transform.TransformPoint(passage.center);
                BoxCollider volume = CityTerracePilotWorld.Box(root, "Covered household passage", centre,
                    passage.size, facing);
                volume.gameObject.layer = 2;
                volume.isTrigger = true;
                root.gameObject.AddComponent<CityTerraceWorldAnchors>().Initialize(volume);
            }
        }

        private static void BuildFishSign(CityBuildingAssetRegistry building, Vector3 envelope)
        {
            var model = Resources.Load<GameObject>(FishSignResource + "RidgeFishSign");
            var print = Resources.Load<Texture2D>(FishSignResource + "RidgeFishSignAlbedo");
            if (model == null || print == null)
                throw new InvalidOperationException("The west Ridge gable sign needs its imported frame and print.");

            Transform wall = building.transform;
            var sign = UnityEngine.Object.Instantiate(model, wall, false);
            sign.name = "Ridge fish cannery advertisement";
            sign.transform.localPosition = new Vector3(envelope.x * .5f, 8.55f, -.6f);
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

            foreach (string side in new[] { "Upper", "Lower" })
            {
                Transform lens = CityResidentialCourtyardAssetProvider.FindPart(sign.transform, "ANCHOR_Flood" + side);
                Transform aim = CityResidentialCourtyardAssetProvider.FindPart(sign.transform, "ANCHOR_Aim" + side);
                lens.name = CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeFishSign";
                lens.rotation = Quaternion.LookRotation(aim.position - lens.position, wall.up);
            }
        }

        private static Transform Place(ref GameObject template, string name, Transform parent, Vector3 position, Quaternion facing)
        {
            if (template == null) template = Resources.Load<GameObject>("City/Hillside/" + name);
            if (template == null) throw new InvalidOperationException("Missing domestic hillside model " + name);
            var frame = new GameObject(name).transform;
            frame.SetParent(parent, false);
            frame.SetPositionAndRotation(position, facing);
            var model = UnityEngine.Object.Instantiate(template, frame, false);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                string role = renderer.name.Substring(renderer.name.LastIndexOf("__", StringComparison.Ordinal) + 2);
                int suffix = role.IndexOf('.');
                if (suffix >= 0) role = role.Substring(0, suffix);
                if (role == "LandingGlass") renderer.sharedMaterial = HomeBalconyResources.GlassMaterial;
                else if (role == "Brick") BarExteriorSurfaceAppearance.Apply(renderer, BarExteriorSurfaceKind.Brick,
                    new Color(.47f, .38f, .33f));
                else renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(role);
            }
            return frame;
        }

        internal static void BuildApron(Transform parent, GameObject paving, CityHillsideBuilding house)
        {
            Slab(house.CourtBounds, "Porch");
            Slab(house.ApproachBounds, "Footpath");

            void Slab(Rect rect, string label)
            {
                // Trim the metre already owned by the street sidewalk.
                if (house.DoorWorld.z < house.StreetContactWorld.z) rect.yMax = Mathf.Min(rect.yMax, house.PavementEdgeZ);
                else rect.yMin = Mathf.Max(rect.yMin, house.PavementEdgeZ);
                if (rect.height < .01f) return;
                var p = rect.center;
                house.TrySampleApproach(p, out float y);
                var centre = new Vector3(p.x, y, p.y);
                CityTerracePilotWorld.Model(paving, parent, label, centre, Quaternion.identity,
                    new Vector3(rect.width / CityTerracePilotPlan.CourtSize, 1f, rect.height / CityTerracePilotPlan.CourtSize));
                var collider = CityTerracePilotWorld.Box(parent, label + " contact", centre - Vector3.up * .15f,
                    new Vector3(rect.width, .3f, rect.height), Quaternion.identity);
                FootstepGround.Stamp(collider.gameObject, FootstepGroundKind.Stone);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            bay = null;
            airer = null;
            clerestory = null;
            Array.Clear(galleries, 0, galleries.Length);
        }
    }
}
