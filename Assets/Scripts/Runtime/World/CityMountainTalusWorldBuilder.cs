using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    // Reuses the authored old-collapse models for the upper rim, beyond the
    // four lower service yards. Low, buried blocks break the foot silhouette.
    internal static class CityMountainTalusWorldBuilder
    {
        internal static void Build(Transform parent, CityLayout layout, CityMountainBoundaryPlan boundary)
        {
            var talus = CityMountainTalusPlan.ForLayout(layout);
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var batches = new Dictionary<Vector2Int, List<RuntimeMeshPlacement>>();
            int variant = 0;
            foreach (var ridge in boundary.Ridges)
            {
                if (ridge.StableId == "mountain-west-north" || ridge.StableId == "mountain-west-south" ||
                    ridge.StableId == "mountain-south-west-join" || ridge.StableId == "mountain-south-west-after-tunnel") continue;
                for (int i = 1; i < ridge.Stations.Count; i++)
                {
                    var a = ridge.Stations[i - 1];
                    var b = ridge.Stations[i];
                    float length = Vector2.Distance(a.WorldXZ, b.WorldXZ);
                    int count = Mathf.FloorToInt(length / 5.5f);
                    for (int j = 0; j < count; j++)
                    {
                        float fraction = (j + .45f) / count;
                        Vector3 outward = Vector3.Lerp(a.OutwardNormal, b.OutwardNormal, fraction).normalized;
                        Vector2 toe = Vector2.Lerp(a.WorldXZ, b.WorldXZ, fraction);
                        Vector2 point = toe - new Vector2(outward.x, outward.z) * .1f;
                        if (talus.Influence(point) < .65f || !ground.TrySampleTop(point, out float top)) continue;
                        float phase = (a.Seed & 255) * .03f + j * 1.7f;
                        var size = new Vector3(3.3f + .8f * Mathf.Sin(phase), 1.3f + .3f * Mathf.Cos(phase), 1.6f);
                        Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                        // Lowest sampled corner owns burial on a sloping bank.
                        float bottom = top;
                        bool clear = true;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 corner = new Vector3(point.x, top, point.y) + tangent * (side * size.x * .5f) - outward * .8f;
                            var cornerXZ = new Vector2(corner.x, corner.z);
                            if (talus.Influence(cornerXZ) < .12f || !ground.TrySampleTop(cornerXZ, out float cornerTop))
                            {
                                clear = false;
                                break;
                            }
                            bottom = Mathf.Min(bottom, cornerTop);
                        }
                        if (!clear) continue;
                        Vector3 center = new Vector3(point.x, bottom + size.y * .5f - .46f, point.y);
                        var cell = new Vector2Int(Mathf.FloorToInt(center.x / 48f), Mathf.FloorToInt(center.z / 48f));
                        if (!batches.TryGetValue(cell, out var placements))
                        {
                            placements = new List<RuntimeMeshPlacement>();
                            batches.Add(cell, placements);
                        }
                        placements.Add(new RuntimeMeshPlacement(CityRockfallAssetProvider.GetMesh(variant++ % CityRockfallAssetProvider.VariantCount),
                            center, Quaternion.LookRotation(outward), size));
                    }
                }
            }
            foreach (var batch in batches)
            {
                var rock = RuntimePrimitiveFactory.CreateCombinedMeshes($"Upper rim buried rockfall {batch.Key.x} {batch.Key.y}",
                    parent, batch.Value, new Color(.235f, .26f, .235f), true,
                    CityMountainSurfaceAppearance.MetersPerTile, RuntimeWorldUvMode.BoxProjected);
                var renderer = rock.GetComponent<Renderer>();
                CityMountainSurfaceAppearance.ApplyPhysicalRidge(renderer, new Color(.235f, .26f, .235f));
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                FootstepGround.Stamp(rock, FootstepGroundKind.Stone);
            }
        }
    }
}
