using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class WarehouseArenaBuilder
    {
        public static GameObject Create(WarehouseArenaPlan plan, Transform parent)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var host = new GameObject("Warehouse Duel"); host.transform.SetParent(parent,false);
            WarehouseArenaAssetProvider.Create("Shell",host.transform,Vector3.zero,Quaternion.identity);
            foreach (Vector3 platform in plan.Platforms)
                WarehouseArenaAssetProvider.Create("Platform",host.transform,platform,Quaternion.identity);
            foreach (WarehouseArenaStair stair in plan.Stairs)
                WarehouseArenaAssetProvider.Create("Stair",host.transform,stair.Foot,Quaternion.identity);
            foreach (WarehouseArenaObstacle obstacle in plan.Obstacles)
                if (obstacle.Kind != WarehouseArenaObstacleKind.Support)
                    WarehouseArenaAssetProvider.Create(obstacle.Kind.ToString(),host.transform,obstacle.Position,Quaternion.Euler(0f,obstacle.Yaw,0f));
            BuildRails(plan,host.transform);
            float side = unchecked((uint)plan.Seed) % 2u == 0u ? -1f : 1f;
            GameObject pipes = WarehouseArenaAssetProvider.Create("Pipes",host.transform,
                new Vector3(side * 11.75f,0f,-.7f),Quaternion.Euler(0f,side < 0f ? 90f : -90f,0f));
            // The wall-local service box owns its low mechanical sound; generation and reset
            // cannot leave detached voices, lights or runtime clips in the scene.
            host.AddComponent<WarehouseArenaAmbience>().Initialize(pipes.transform);
            BuildLampFixtures(plan,host.transform);
            return host;
        }

        private static void BuildLampFixtures(WarehouseArenaPlan plan, Transform parent)
        {
            Vector2[] lamps = {
                new Vector2(-7f,-5f), new Vector2(0f,-5f), new Vector2(7f,-5f),
                new Vector2(plan.Stairs[0].Foot.x,2f), new Vector2(0f,1f), new Vector2(plan.Stairs[1].Foot.x,2f),
                new Vector2(-5f,7f), new Vector2(5f,7f)
            };
            for (int i = 0; i < lamps.Length; i++)
            {
                // Retain the authored fittings, but the hero's chest lamp is the
                // warehouse's only direct light. No hidden fill lights are created.
                WarehouseArenaAssetProvider.Create("Lamp",parent,
                    new Vector3(lamps[i].x,5.55f,lamps[i].y),Quaternion.identity);
            }
        }

        private static void BuildRails(WarehouseArenaPlan plan, Transform parent)
        {
            foreach (Vector3 platform in plan.Platforms)
            {
                foreach (Vector3 side in new[] { Vector3.left,Vector3.right,Vector3.back,Vector3.forward })
                {
                    bool joined = false;
                    foreach (Vector3 neighbour in plan.Platforms)
                        if ((neighbour - (platform + side * 3f)).sqrMagnitude < .01f) { joined = true; break; }
                    if (joined) continue;
                    Vector3 edge = platform + side * 1.5f;
                    // The actual shell holds the two outside edges. Every exposed drop gets a rail.
                    if (Mathf.Abs(edge.x) > 11.99f) continue;
                    bool alongX = Mathf.Abs(side.z) > .5f;
                    float from = alongX ? edge.x - 1.5f : edge.z - 1.5f;
                    float to = from + 3f;
                    var spans = new List<Vector2> { new Vector2(from,to) };
                    if (alongX && Mathf.Abs(edge.z - 5.5f) < .01f)
                        foreach (WarehouseArenaStair stair in plan.Stairs)
                            CutOpening(spans,stair.Foot.x - 1.1f,stair.Foot.x + 1.1f);
                    foreach (Vector2 span in spans)
                        for (float start = span.x; start < span.y - .001f; start += 1f)
                        {
                            float length = Mathf.Min(1f,span.y - start);
                            Vector3 position = alongX ? new Vector3(start + length*.5f,2.6f,edge.z) :
                                new Vector3(edge.x,2.6f,start + length*.5f);
                            GameObject rail = WarehouseArenaAssetProvider.Create("Rail",parent,position,
                                Quaternion.Euler(0f,alongX ? 0f : 90f,0f));
                            // Only the metre wrapper of a shortened end piece changes span. The imported
                            // model keeps its unit factor, and rail height/depth always remain authored.
                            rail.transform.localScale = new Vector3(length,1f,1f);
                        }
                }
            }
        }

        private static void CutOpening(List<Vector2> spans, float from, float to)
        {
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                Vector2 span = spans[i];
                if (from >= span.y || to <= span.x) continue;
                spans.RemoveAt(i);
                if (from > span.x) spans.Add(new Vector2(span.x,from));
                if (to < span.y) spans.Add(new Vector2(to,span.y));
            }
        }
    }
}
