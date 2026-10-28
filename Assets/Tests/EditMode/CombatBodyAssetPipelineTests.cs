using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CombatBodyAssetPipelineTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void TorsoFrame_CoincidentCornersRetainMultiplicityAndRejectInvalidMetadata(bool corruptMetadata)
        {
            var source = new GameObject("CLO_Outer_cardiganBody");
            var model = new GameObject("Torso Frame Regression");
            var mesh = new Mesh();
            try
            {
                source.transform.localScale = model.transform.localScale = Vector3.one * 100f;
                SkinnedMeshRenderer original = source.AddComponent<SkinnedMeshRenderer>();
                var piece = new GameObject("Region4Patch2__" + source.name);
                piece.transform.SetParent(model.transform, false);
                piece.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                var vertices = new List<Vector3>();
                var labels = new List<Vector2>();
                var xy = new List<Vector2>();
                var z = new List<Vector2>();
                var indices = new List<int>();
                Vector3[] centres = { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
                foreach (Vector3 centre in centres)
                    AddCell(centre, centre + new Vector3(-.01f, 0f, 0f),
                        centre + new Vector3(0f, .01f, 0f), centre + new Vector3(.01f, -.01f, 0f));
                // Two imported corners coincide. Their multiplicity still belongs
                // to the authored triangle centroid, even after index welding.
                Vector3 a = new Vector3(.1f, .2f, .3f), b = new Vector3(.4f, .2f, .3f);
                Vector3 expected = (a + b + b) / 3f;
                AddCell(expected + (corruptMetadata ? Vector3.right * .02f : Vector3.zero), a, b, b);
                mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0);
                mesh.SetUVs(1, labels); mesh.SetUVs(6, xy); mesh.SetUVs(7, z);
                MethodInfo measure = Type.GetType("BarPromenade.Editor.CombatBodyAssetSetup, BarPromenade.Editor", true)
                    .GetMethod("MeasureTorsoFrames", BindingFlags.Static | BindingFlags.NonPublic);
                var originals = new Dictionary<string, SkinnedMeshRenderer> { { source.name, original } };
                if (corruptMetadata)
                {
                    TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() =>
                        measure.Invoke(null, new object[] { model, originals }));
                    Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                    Assert.That(failure.InnerException.Message, Does.Contain("lost its affine FBX basis"));
                }
                else
                {
                    var frames = (Dictionary<string, Matrix4x4>)measure.Invoke(null, new object[] { model, originals });
                    Assert.That(Vector3.Distance(frames[source.name].MultiplyPoint3x4(expected), expected * .01f),
                        Is.LessThan(.000001f));
                }

                void AddCell(Vector3 centre, params Vector3[] corners)
                {
                    int cell = labels.Count / 3;
                    foreach (Vector3 corner in corners)
                    {
                        indices.Add(vertices.Count); vertices.Add(corner * .01f);
                        labels.Add(new Vector2(cell, 1f));
                        xy.Add(new Vector2(centre.x, centre.y)); z.Add(new Vector2(centre.z, 0f));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void NpcBody_ReimportPreservesCardiganTorsoCellFrames()
        {
            const string path = "Assets/Resources/CombatGore/BodyNpc.fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.DontDownloadFromCacheServer);
            LogAssert.NoUnexpectedReceived();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/VillageLife/StationWorker.fbx");
            Assert.That(model, Is.Not.Null);
            Assert.That(source, Is.Not.Null);
            var originals = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                originals.Add(renderer.name, renderer.transform);
            MethodInfo validate = Type.GetType("BarPromenade.Editor.CombatBodyAssetSetup, BarPromenade.Editor", true)
                .GetMethod("ValidateTorsoCells", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(validate, Is.Not.Null);
            int cardiganPatches = 0;
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("Region", StringComparison.Ordinal) &&
                    !renderer.name.StartsWith("Flesh", StringComparison.Ordinal)) continue;
                var labels = new List<Vector2>();
                renderer.sharedMesh.GetUVs(1, labels);
                if (labels.Count == 0) continue;
                string name = renderer.name.Substring(renderer.name.IndexOf("__", StringComparison.Ordinal) + 2);
                Assert.That(renderer.sharedMesh.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.TexCoord4), Is.EqualTo(4), renderer.name);
                Assert.That(renderer.sharedMesh.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.TexCoord6), Is.EqualTo(4), renderer.name);
                validate.Invoke(null, new object[] { renderer, originals[name] });
                if (name == "CLO_Outer_cardiganBody") cardiganPatches++;
            }
            Assert.That(cardiganPatches, Is.GreaterThan(0));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
