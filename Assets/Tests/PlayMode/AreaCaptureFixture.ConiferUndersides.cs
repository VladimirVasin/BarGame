using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused conifer underside regression and ground-level captures. Run alone.")]
        public IEnumerator ConiferUndersides()
        {
            var stage = new GameObject("Conifer underside capture");
            stage.transform.position = new Vector3(0f, 4000f, 0f);
            bool fog = RenderSettings.fog;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;
            Vector4 wind = Shader.GetGlobalVector("_MountainWindParams");
            Mesh crown = null;
            GameObject treeRoot = null;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.55f, .55f, .55f);
                Shader.SetGlobalVector("_MountainWindParams", Vector4.zero);
                var camera = new GameObject("Conifer ground camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.50f, .53f, .56f);
                camera.nearClipPlane = .03f;
                camera.farClipPlane = 45f;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
                var light = new GameObject("Conifer daylight").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.type = LightType.Directional;
                light.intensity = .8f;
                light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);

                for (int variant = 0; variant < 3; variant++)
                for (int scale = 1; scale <= 2; scale++)
                {
                    var tree = new MountainRoadForestDescriptor("underside", MountainRoadForestLayer.Mid,
                        Vector3.zero, 14f * scale, 2.4f * scale, 17f, variant, true, .384f * scale);
                    crown = MountainRoadSceneryMeshFactory.CreateConiferCrowns("Underside crown", new[] { tree });
                    AssertClosedConiferUnderside(crown, tree);
                    treeRoot = new GameObject("Tree");
                    treeRoot.transform.SetParent(stage.transform, false);
                    treeRoot.AddComponent<MeshFilter>().sharedMesh = crown;
                    var renderer = treeRoot.AddComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    MountainRoadSurfaceAppearance.ApplyCombined(renderer, MountainRoadSurfaceKind.ConiferNeedles,
                        new Color(.095f, .14f, .115f), MountainRoadSurfaceAppearance.FoliageMaterial);
                    float trunkHeight = tree.Height * .32f;
                    GameObject trunk = RuntimePrimitiveFactory.CreateCombinedOrientedBoxes("Trunk", treeRoot.transform,
                        new[] { new RuntimeOrientedBox(Vector3.up * (trunkHeight * .5f),
                            Quaternion.Euler(0f, tree.YawDegrees, 0f),
                            new Vector3(tree.TrunkRadius * 2f, trunkHeight, tree.TrunkRadius * 2f)) },
                        new Color(.19f, .165f, .135f), false,
                        MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.BarkAndDeadwood).MetersPerTile,
                        RuntimeWorldUvMode.BoxProjected);
                    MountainRoadSurfaceAppearance.ApplyCombined(trunk.GetComponent<Renderer>(),
                        MountainRoadSurfaceKind.BarkAndDeadwood, new Color(.19f, .165f, .135f));
                    yield return null;

                    float lowerBase = float.PositiveInfinity;
                    foreach (Vector3 vertex in crown.vertices) lowerBase = Mathf.Min(lowerBase, vertex.y);
                    Vector3 origin = stage.transform.position;
                    camera.fieldOfView = 68f;
                    camera.transform.position = origin + new Vector3(tree.CrownRadius * .8f, EyeHeight,
                        -tree.CrownRadius * 1.35f);
                    camera.transform.LookAt(origin + Vector3.up * (lowerBase + tree.CrownRadius * .4f));
                    CaptureCurrentCamera(camera, "ConiferUndersides", $"v{variant}-x{scale}-edge");
                    Object.DestroyImmediate(treeRoot);
                    treeRoot = null;
                    Object.DestroyImmediate(crown);
                    crown = null;
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
                if (crown != null) Object.DestroyImmediate(crown);
                Shader.SetGlobalVector("_MountainWindParams", wind);
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
            }
        }

        private static void AssertClosedConiferUnderside(Mesh mesh, MountainRoadForestDescriptor tree)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            float tile = MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.ConiferNeedles).MetersPerTile;
            var welded = new Dictionary<Vector3Int, int>();
            var ids = new int[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = vertices[index] - tree.Position;
                var key = Vector3Int.RoundToInt(point * 10000f);
                if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded.Add(key, id); }
                ids[index] = id;
                Assert.That(new Vector2(point.x, point.z).magnitude, Is.LessThanOrEqualTo(tree.CrownRadius + .0001f));
                Assert.That(float.IsFinite(uv[index].x) && float.IsFinite(uv[index].y), Is.True);
                Assert.That(uv[index].y * tile, Is.EqualTo(point.y).Within(.0001f),
                    "The cap and skirt must infer the same tree foot and wind displacement.");
            }

            var edges = new Dictionary<(int, int), (int count, int direction)>();
            float volume = 0f, downwardArea = 0f;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Assert.That(normal.sqrMagnitude, Is.GreaterThan(.000001f), "A closing face collapsed.");
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
                if (normal.y < 0f) downwardArea -= normal.y * .5f;
                for (int edge = 0; edge < 3; edge++)
                {
                    int from = ids[triangles[index + edge]], to = ids[triangles[index + (edge + 1) % 3]];
                    var key = (Mathf.Min(from, to), Mathf.Max(from, to));
                    edges.TryGetValue(key, out var usage);
                    edges[key] = (usage.count + 1, usage.direction + (from < to ? 1 : -1));
                }
            }
            foreach (var edge in edges.Values)
                Assert.That(edge, Is.EqualTo((2, 0)), "A crown edge is open or the closing face points inward.");
            Assert.That(volume, Is.GreaterThan(0f), "Closed crowns must face outward.");
            Assert.That(downwardArea, Is.GreaterThan(tree.CrownRadius * tree.CrownRadius * 2f),
                "The view from below must be covered by downward-facing crown surface.");
        }
    }
}
