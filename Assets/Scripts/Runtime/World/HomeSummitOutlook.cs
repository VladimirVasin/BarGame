using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal static class HomeSummitOutlook
    {
        // At the upper street the first lower-city rooflines are nearly 200 m away.
        internal const float FarPlane = 410f;
        internal const float FogDensity = .0048f;

        // Authored from the approved 16:9 gameplay frame, in apartment space.
        // The balcony uses this fixed lens and pose each time the hero steps outside.
        internal static readonly Vector3 CameraPosition = new Vector3(5.58f, 2.02f, .22f);
        internal static readonly Quaternion CameraRotation = Quaternion.Euler(26.61676f, 128.17090f, 0f);

        // Static, collider-free distant silhouettes at their surveyed positions. Closer
        // structures keep their imported models. Distant lots retain their actual
        // stepped slab and wing footprints; there is no second active city.
        internal static void Build(Transform cityFrame, HomeExteriorContextPlan context, Rect fullDetail)
        {
            var layout = context.Layout;
            var boxes = new List<RuntimeOrientedBox>();
            Vector3 eye = context.PlayerHome.DoorPosition;
            var prototypes = CityBuildingAssetProvider.LoadOrThrow();
            foreach (var lot in layout.BuildingMasses) Add(lot);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
                if (!house.FoundationBounds.Overlaps(fullDetail) && !house.ApproachBounds.Overlaps(fullDetail)) Add(house.Lot);
            if (boxes.Count > 0)
            {
                var shell = RuntimePrimitiveFactory.CreateCombinedOrientedBoxes("Lower city roofs", cityFrame, boxes,
                    new Color(.38f, .39f, .37f));
                shell.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            var banks = CityHillsideGroundGrid.ForLayout(layout);
            var street = CityStreetSupportPlan.ForLayout(layout);
            var roadVertices = new List<Vector3>();
            var roadTriangles = new List<int>();
            foreach (var patch in street.Patches)
            {
                if (!patch.IsSupport || patch.Kind != CityStreetSupportKind.Asphalt || fullDetail.Overlaps(patch.Bounds)) continue;
                var centre = patch.Bounds.center;
                if (centre.y < eye.z || (centre - new Vector2(eye.x, eye.z)).sqrMagnitude > FarPlane * FarPlane) continue;
                int start = roadVertices.Count;
                foreach (var p in patch.TopVertices) roadVertices.Add(p + Vector3.up * .06f);
                for (int j = 2; j < patch.TopVertices.Count; j++)
                {
                    bool flip = Vector3.Cross(roadVertices[start + j - 1] - roadVertices[start],
                        roadVertices[start + j] - roadVertices[start]).y < 0f;
                    roadTriangles.Add(start);
                    roadTriangles.Add(start + (flip ? j : j - 1));
                    roadTriangles.Add(start + (flip ? j - 1 : j));
                }
            }
            if (roadTriangles.Count > 0)
            {
                var roadMesh = new Mesh { name = "Descending streets", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                roadMesh.SetVertices(roadVertices);
                roadMesh.SetTriangles(roadTriangles, 0);
                roadMesh.RecalculateNormals();
                var lanes = new GameObject(roadMesh.name);
                lanes.transform.SetParent(cityFrame, false);
                lanes.AddComponent<MeshFilter>().sharedMesh = roadMesh;
                lanes.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(roadMesh);
                var roadRenderer = lanes.AddComponent<MeshRenderer>();
                roadRenderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                roadRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", new Color(.24f, .26f, .25f));
                roadRenderer.SetPropertyBlock(properties);
            }

            const float step = 8f;
            int half = Mathf.CeilToInt(FarPlane / step);
            int across = 2 * half + 1;
            var vertices = new Vector3[across * (half + 1)];
            var valid = new bool[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var indices = new List<int>();
            for (int row = 0; row <= half; row++)
            for (int col = 0; col < across; col++)
            {
                var p = new Vector2(eye.x + (col - half) * step, eye.z + row * step);
                int i = row * across + col;
                if ((p - new Vector2(eye.x, eye.z)).sqrMagnitude > FarPlane * FarPlane) continue;
                bool found = false;
                float y = 0f;
                if (quarter.TerrainBounds.Contains(p)) { y = quarter.SampleSoil(p); found = true; }
                else foreach (var bank in banks)
                    if (bank.TryHeight(p, out y)) { found = true; break; }
                if (!found && !CityTerrainSurfacePlan.TrySampleGroundTop(layout, p, out y, out _) &&
                    !street.TrySampleTop(p, out y, out _)) continue;
                valid[i] = true;
                vertices[i] = new Vector3(p.x, y - .18f, p.y);
                uv[i] = p * .12f;
                if (row == 0 || col == 0 || fullDetail.Contains(p - Vector2.one * (step * .5f))) continue;
                int a = i - across - 1;
                if (valid[a] && valid[i - 1] && valid[i - across])
                {
                    indices.Add(a); indices.Add(i - 1); indices.Add(i - across);
                    indices.Add(i - across); indices.Add(i - 1); indices.Add(i);
                }
            }
            var mesh = new Mesh { name = "Distant city grade", vertices = vertices, uv = uv, triangles = indices.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var land = new GameObject(mesh.name);
            land.transform.SetParent(cityFrame, false);
            land.AddComponent<MeshFilter>().sharedMesh = mesh;
            land.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var renderer = land.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            CityExteriorAppearance.ApplyGroundSurface(renderer, new Color(.45f, .46f, .40f));

            void Add(BuildingLot lot)
            {
                var delta = lot.Center - eye;
                if (!lot.HasBuilding || lot.IsPlayerHome || delta.z < 0f || delta.sqrMagnitude > FarPlane * FarPlane) return;
                if (lot.IsOrdinaryBuilding)
                {
                    var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                    // Roof meshes stay GPU-resident and preserve the imported parapets,
                    // pitches and stairheads, even on non-readable player-build assets.
                    var source = prototypes.GetPrefabOrThrow(lot.District, lot.BuildingVariant).GetComponent<CityBuildingAssetRegistry>();
                    if (source.TryGetRenderer(CityBuildingMeshRole.Roof, out var roof))
                    {
                        var part = new GameObject("Distant roof " + lot.Cell);
                        part.transform.SetParent(cityFrame, false);
                        var t = roof.transform;
                        part.transform.localPosition = pose.TransformPoint(source.transform.InverseTransformPoint(t.position));
                        part.transform.localRotation = pose.Rotation * Quaternion.Inverse(source.transform.rotation) * t.rotation;
                        part.transform.localScale = (source.transform.worldToLocalMatrix * t.localToWorldMatrix).lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh = roof.GetComponent<MeshFilter>().sharedMesh;
                        var cap = part.AddComponent<MeshRenderer>();
                        cap.sharedMaterial = roof.sharedMaterial;
                        cap.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    foreach (var mass in CityBuildingAssetProvider.GetExpectedCollisionBounds(lot.District, lot.BuildingVariant))
                        boxes.Add(new RuntimeOrientedBox(pose.TransformPoint(mass.center), pose.Rotation, mass.size));
                }
                else boxes.Add(new RuntimeOrientedBox(lot.Center + Vector3.up * (lot.Height * .5f), Quaternion.identity,
                    new Vector3(lot.Size.x, lot.Height, lot.Size.y)));
            }
        }
    }
}
