using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideQuarterBridgeWorld
    {
        public static void Build(Transform parent, CityHillsideQuarterPlan quarter, Rect? retained = null)
        {
            if (quarter.Bridges.Count == 0) return;
            GameObject deck = Template("TerraceBridge");
            GameObject concrete = Template("TerraceSupport");
            GameObject rail = Template("TerraceRail");
            GameObject truss = Fitting("CourtBridgeTruss");
            GameObject head = Fitting("UpperBridgeParapetHead");
            foreach (var bridge in quarter.Bridges)
            {
                if (retained.HasValue && !retained.Value.Overlaps(bridge.DeckBounds)) continue;
                var root = new GameObject(bridge.Id == "summit" ? "Upper ravine bridge" : "Court ravine bridge").transform;
                root.SetParent(parent, false);
                Vector3 start = bridge.WestBearingWorld, end = bridge.EastBearingWorld;
                Vector3 direction = (end - start).normalized;
                Quaternion grade = Quaternion.FromToRotation(Vector3.right, direction);
                Vector3 up = grade * Vector3.up;
                Quaternion railRotation = Quaternion.LookRotation(direction, up);
                bool upper = bridge.Id == "summit";
                float length = Vector3.Distance(start, end), width = bridge.DeckBounds.height;
                float girderDepth = upper ? 1.48f : .90f;
                float slabDepth = upper ? .58f : .44f;
                Vector3 mid = (start + end) * .5f;

                // The continuous street compositor supplies the road and both walking tops.
                // These slabs start under its skirts; no second surface or collider at the feet.
                int slabs = Mathf.CeilToInt(length / 4.8f);
                for (int i = 0; i < slabs; i++)
                {
                    Vector3 at = Vector3.Lerp(start, end, (i + .5f) / slabs) - up * .18f;
                    CityTerracePilotWorld.Model(deck, root, "Deck slab " + i, at, grade,
                        new Vector3(length / slabs / 4.8f, slabDepth / .35f, width / 2.4f));
                }
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 girder = mid + Vector3.forward * (side * (width * .5f - 1.3f)) - up * (.16f + slabDepth);
                    Solid("Bearing beam", girder, new Vector3(length, girderDepth, .76f), grade);
                    Vector3 railStart = start + Vector3.forward * (side * (width * .5f - .09f)) +
                        Vector3.up * (bridge.Link.PavementTopAt(start.x) - start.y);
                    CityTerracePilotWorld.Box(root, "Pedestrian guard", railStart + direction * (length * .5f) + up * .525f,
                        new Vector3(length, 1.05f, .16f), grade);
                    if (upper)
                    {
                        int bays = Mathf.CeilToInt(length / 2.4f);
                        float pitch = length / bays;
                        for (int bay = 0; bay < bays; bay++)
                            CityTerracePilotWorld.Model(rail, root, "Rail bay " + bay,
                                railStart + direction * (pitch * bay), railRotation, new Vector3(1f, 1f, pitch / 2.4f));
                        foreach (float fraction in new[] { .04f, .25f, .75f, .96f })
                        {
                            Quaternion noseToBank = fraction < .5f ? grade : grade * Quaternion.Euler(0f, 180f, 0f);
                            Fit(head, "Sloped concrete parapet head", railStart + direction * (fraction * length),
                                noseToBank, Vector3.one);
                        }
                    }
                    else
                    {
                        // The rolled top chord climbs at the bank ends. Its six
                        // open panels are read from the pavement, above eye level.
                        bool reverse = side > 0;
                        Fit(truss, "Riveted six-panel side truss", reverse ? railStart + direction * length : railStart,
                            reverse ? grade * Quaternion.Euler(0f, 180f, 0f) : grade, new Vector3(length / 21.5f, 1f, 1f));
                    }
                }
                foreach (var bearing in new[] { start, end })
                {
                    float inward = bearing.x == start.x ? 1f : -1f;
                    Vector3 seat = bearing + direction * (inward * .70f) - up * (.18f + slabDepth);
                    Solid("Bank abutment", seat, new Vector3(1.8f, upper ? 4.2f : 3.5f, width - .22f), grade);
                }
                CitySurfaceLibrary.ApplyHierarchy(root);

                void Fit(GameObject model, string name, Vector3 position, Quaternion rotation, Vector3 scale)
                {
                    var part = CityTerracePilotWorld.Model(model, root, name, position, rotation, scale);
                    // The upper chord, braces and battered concrete heads project
                    // above the waist-high guard and retain their actual open spaces.
                    foreach (var mesh in part.GetComponentsInChildren<MeshFilter>())
                        mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
                }

                void Solid(string name, Vector3 top, Vector3 size, Quaternion rotation)
                {
                    CityTerracePilotWorld.Model(concrete, root, name, top, rotation,
                        new Vector3(size.x / 2.4f, size.y / 1.92f, size.z / 2.4f));
                    CityTerracePilotWorld.Box(root, name + " body", top - rotation * Vector3.up * (size.y * .5f), size, rotation);
                }
            }
        }

        private static GameObject Fitting(string name) =>
            Resources.Load<GameObject>("City/Hillside/" + name) ??
                throw new InvalidOperationException("Missing authored hillside bridge fitting " + name);

        private static GameObject Template(string name)
        {
            var model = Resources.Load<GameObject>(CityTerracePilotWorld.ResourceFolder + name);
            if (model == null) throw new InvalidOperationException("Missing residential bridge model " + name);
            return model;
        }
    }
}
