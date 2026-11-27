using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class RidgeRavineWorld
    {
        internal static void Build(Transform parent, CityLayout layout, List<Collider> shelters = null, Rect? window = null)
        {
            var site = RidgeRavinePlan.ForLayout(layout);
            if (site == null) return;
            var grid = CityHillsideQuarterGroundGrid.ForLayout(layout);
            var root = new GameObject("Summit ravine repairs and fallen house").transform;
            root.SetParent(parent, false);
            if (!window.HasValue || window.Value.Overlaps(site.Bounds))
            {
                Place("RavineHouse", site.Origin, Quaternion.identity, Vector3.one);
                site.Families.Place(root, site.Origin, shelters != null, CityHillsideQuarterPlan.ForLayout(layout).Bridges[1]);
                if (shelters != null)
                {
                    foreach (var volume in site.CoveredRooms())
                    {
                        var covered = CityTerracePilotWorld.Box(root, "Surviving apartment ceilings",
                            volume.center, volume.size, Quaternion.identity);
                        covered.isTrigger = true;
                        covered.gameObject.layer = 2;
                        shelters.Add(covered);
                    }
                }
            }

            // Short shuttered repairs track the kink in the western escarpment.
            // Bed each course into the face; the cap remains just below the old rim.
            float[] stations = { -361.2f, -358.3f, -355.4f, -353.1f, -351.2f };
            for (int i = 1; i < stations.Length; i++)
            {
                Vector3 a = WestFace(stations[i - 1]), b = WestFace(stations[i]);
                Vector3 span = Vector3.ProjectOnPlane(b - a, Vector3.up);
                Vector3 outwards = Vector3.Cross(Vector3.up, span).normalized;
                Vector3 at = (a + b) * .5f - Vector3.up * (.11f + .13f * (i % 2));
                Place("RavineShoringBay", at, Quaternion.LookRotation(outwards),
                    new Vector3((span.magnitude - (i == 2 ? .14f : .035f)) / 3.8f, 1, 1));
            }
            // The fall runs from the severed rooms down the east benches and fans
            // northward onto the deeper gravel. Each piece beds into the local facet.
            Fallen("RavineKitchenFragment", -355.0f, .765f, 83f, -47f, -.10f);
            Fallen("RavinePipeWall",        -359.20f, .775f, 62f, -43f, -.09f);
            Fallen("RavineWindowWall",      -353.45f, .70f, -48f, -54f, -.09f);
            Fallen("RavineSplinteredFloor", -357.22f, .49f, -61f,  3f,  .10f);
            Fallen("RavineFallenFloor",     -353.85f, .47f,  14f, -5f,  .11f);
            Fallen("RavineFoldedRoof",      -351.12f, .52f, -28f,  8f,  .09f);
            Fallen("RavineMasonryFall",     -355.78f, .40f,  57f,  3f,  .03f);
            Fallen("RavineMasonryFall",     -349.28f, .56f, -43f, -4f,  .05f);
            Wedged("RavineEnamelBath", -352.85f, .855f, -73f, -14f);
            Wedged("RavineKitchenCabinet", -357.36f, .75f, -52f, 18f);
            Fallen("RavineApartmentDoor", -359.40f, .44f, -30f, 5f, .12f);
            CitySurfaceLibrary.ApplyHierarchy(root);

            Vector3 WestFace(float z)
            {
                Vector4 s = CityHillsideQuarterRavine.At(z);
                var lip = Surface(s.y, z);
                lip.x = Mathf.Lerp(s.y, s.z, .105f);
                return lip;
            }

            void Wedged(string model, float z, float across, float yaw, float roll)
            {
                // A fragment of reinforced floor cantilevers from the bank. Its rear is
                // embedded in the slope, with the fixtures still seated on its top.
                var section = CityHillsideQuarterRavine.At(z);
                var bed = Surface(Mathf.Lerp(section.y, section.z, across), z);
                Place(model, bed + Vector3.up * .28f, Quaternion.Euler(-6f,yaw,roll), Vector3.one);
            }

            void Fallen(string model, float z, float across, float yaw, float tilt, float embed)
            {
                Vector4 section = CityHillsideQuarterRavine.At(z);
                float x = Mathf.Lerp(section.y, section.z, across);
                Vector3 bed = Surface(x, z);
                const float radius = .22f;
                Vector3 east = Surface(x + radius, z) - Surface(x - radius, z);
                Vector3 north = Surface(x, z + radius) - Surface(x, z - radius);
                Vector3 normal = Vector3.Cross(north, east).normalized;
                var rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, normal), normal);
                Place(model, bed + normal * embed, rotation * Quaternion.Euler(tilt, yaw, 0f), Vector3.one);
            }

            Vector3 Surface(float x, float z)
            {
                if (!grid.TryHeight(new Vector2(x, z), out float y))
                    throw new InvalidOperationException("Ravine fabric outside surveyed ground at " + new Vector2(x,z));
                return new Vector3(x, y, z);
            }

            void Place(string model, Vector3 at, Quaternion rotation, Vector3 scale)
            {
                var template = Resources.Load<GameObject>("City/Hillside/" + model);
                if (template == null) throw new InvalidOperationException("Missing ravine fabric " + model);
                var placed = CityTerracePilotWorld.Model(template, root, model, at, rotation, scale);
                foreach (var mesh in placed.GetComponentsInChildren<MeshFilter>())
                {
                    string part = mesh.name;
                    var renderer = mesh.GetComponent<Renderer>();
                    if (part.StartsWith("COL_", StringComparison.Ordinal))
                    {
                        renderer.enabled = false;
                        if (shelters != null)
                        {
                            mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
                            FootstepGround.Stamp(mesh.gameObject, FootstepGroundKind.Concrete);
                        }
                        continue;
                    }
                    if (part.StartsWith("KitchenTiles__", StringComparison.Ordinal))
                        InteriorFinish(renderer, HomeSurfaceKind.BathroomTile, new Color(.56f,.65f,.58f));
                    else if (part.StartsWith("FadedWallpaper__", StringComparison.Ordinal))
                        InteriorFinish(renderer, HomeSurfaceKind.Wallpaper, new Color(.66f,.586f,.46f));
                    else if (part.StartsWith("ChippedEnamel__", StringComparison.Ordinal))
                        InteriorFinish(renderer, HomeSurfaceKind.Enamel, new Color(.84f,.85f,.77f), 1.5f);
                    else if (part.StartsWith("BrokenDoorLeaf__", StringComparison.Ordinal) ||
                        part.StartsWith("DrawerFront__", StringComparison.Ordinal))
                        InteriorFinish(renderer, HomeSurfaceKind.WornLaminate, new Color(.65f,.53f,.37f), 1f);
                    else if (part.StartsWith("OxideBricks__", StringComparison.Ordinal))
                        InteriorFinish(renderer, HomeSurfaceKind.Concrete, new Color(.51f,.32f,.25f));
                    // Floorboards and wallpaper closely follow solid backing. Thin exposed
                    // ties share the outline of the broken slab.
                    if (shelters != null && !part.StartsWith("FadedWallpaper__", StringComparison.Ordinal) &&
                        !part.StartsWith("CastStairTreads__", StringComparison.Ordinal) &&
                        !part.StartsWith("ExposedTies__", StringComparison.Ordinal) &&
                        !part.StartsWith("RoomFloorboards__", StringComparison.Ordinal))
                        mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
                }
            }
        }

        private static void InteriorFinish(Renderer renderer, HomeSurfaceKind kind, Color tint, float metresPerUv = 2f)
        {
            // Imported plaster faces use metre UVs divided by 2 in the shared export kit.
            HomeSurfaceAppearance.Apply(renderer, kind, SurfaceProjection.BoxXY, tint);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            float pitch = metresPerUv / HomeSurfaceAppearance.GetRecipe(kind).MetersPerTile;
            block.SetVector("_BaseMap_ST", new Vector4(pitch, pitch, 0f, 0f));
            renderer.SetPropertyBlock(block);
        }
    }
}
