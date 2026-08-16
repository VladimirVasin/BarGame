using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CanopyRagClothPlayModeTests
    {
        private const int SimulatedFixedSteps = 30;

        private RenderTexture renderTarget;

        [UnityTest]
        public IEnumerator HangingRag_MovesUnderWindWithoutNaN()
        {
            var parent = new GameObject("Cloth Simulation Test");
            try
            {
                GameObject rag = ClothPanelFactory.CreateHangingRag(
                    "Simulated Rag",
                    parent.transform,
                    new Vector3(0f, 3f, 0f),
                    0f,
                    0.6f,
                    1.2f,
                    Color.gray,
                    2);
                var cloth = rag.GetComponent<Cloth>();

                // Cloth pauses while its renderer is culled, so the
                // test needs a live camera that actually sees the
                // rag; in batch mode only a render-texture target
                // guarantees the camera really renders.
                var cameraObject = new GameObject("Cloth Test Camera");
                cameraObject.transform.SetParent(
                    parent.transform,
                    false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 2.6f, -4f);
                camera.transform.LookAt(new Vector3(0f, 2.4f, 0f));
                renderTarget = new RenderTexture(64, 64, 24);
                renderTarget.Create();
                camera.targetTexture = renderTarget;

                CityClothWindRegistry.Register(cloth);
                CityClothWindRegistry.SetWind(
                    new WindSample(90f, 1f));
                Assert.That(
                    cloth.externalAcceleration.magnitude,
                    Is.GreaterThan(0f),
                    "The registry must push registered cloth.");

                // cloth.vertices reports the authored particle rest
                // poses; the live deformation only exists on the
                // skinned output, so the test bakes the renderer.
                var renderer =
                    rag.GetComponent<SkinnedMeshRenderer>();
                Vector3[] rest = renderer.sharedMesh.vertices;
                var baked = new Mesh();
                for (int step = 0;
                     step < SimulatedFixedSteps;
                     step++)
                {
                    yield return new WaitForFixedUpdate();
                }

                renderer.BakeMesh(baked);
                Vector3[] moved = baked.vertices;
                Object.Destroy(baked);
                Assert.That(
                    moved.Length,
                    Is.EqualTo(rest.Length));
                float maxFreeDrift = 0f;
                for (int index = 0; index < moved.Length; index++)
                {
                    Assert.That(
                        float.IsNaN(moved[index].x) ||
                        float.IsNaN(moved[index].y) ||
                        float.IsNaN(moved[index].z),
                        Is.False,
                        "The simulation must stay finite.");
                    float drift = Vector3.Distance(
                        rest[index],
                        moved[index]);
                    bool pinned =
                        rest[index].y >
                        ClothPanelFactory.PinnedTopThreshold;
                    if (pinned)
                    {
                        Assert.That(
                            drift,
                            Is.LessThan(0.01f),
                            "Pinned particles must hold the anchor.");
                    }
                    else
                    {
                        maxFreeDrift = Mathf.Max(
                            maxFreeDrift,
                            drift);
                    }
                }

                Assert.That(
                    maxFreeDrift,
                    Is.GreaterThan(0.02f),
                    "Wind and gravity must move the free hem.");
            }
            finally
            {
                if (renderTarget != null)
                {
                    renderTarget.Release();
                    Object.Destroy(renderTarget);
                    renderTarget = null;
                }

                Object.Destroy(parent);
            }
        }
    }
}
