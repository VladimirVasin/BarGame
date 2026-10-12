using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class Player3DVisualCapturePlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(JointSurfaceAssetsSetup))]
        public IEnumerator ProductionRig_WalkingKeepsJacketHemLength()
        {
            Player3DAssetRegistry registry = CreateJointCaptureRig();
            registry.Animator.enabled = false;
            foreach (MonoBehaviour driver in registry.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(driver is Player3DAssetRegistry) && !(driver is PlayerWardrobe)) driver.enabled = false;
            PlayerJacketCloth cloth = registry.GetComponent<PlayerJacketCloth>();
            CharacterJointDeformation joints = registry.GetComponent<CharacterJointDeformation>();
            Assert.That(cloth, Is.Not.Null); Assert.That(joints, Is.Not.Null);
            Camera camera = cameraObject.GetComponent<Camera>();
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(directory);
            var reports = new List<JacketWalkResult>();
            var bindings = (PlayerJacketCloth.SurfaceBinding[])typeof(PlayerJacketCloth)
                .GetField("bindings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cloth);
            var panels = bindings.Where(binding => binding.Region == 0).Select(binding => new JacketWalkPanel(binding)).ToArray();
            Assert.That(panels, Is.Not.Empty);
            try
            {
                // The production forward clip is called Walk; WalkFwd is not an asset ID.
                foreach (string clipName in new[] { "Walk", "WalkBack" })
                {
                    Assert.That(registry.TryGetAnimation(clipName, out Player3DAnimationBinding animation), Is.True);
                    Assert.That(animation.Clip, Is.Not.Null);
                    Assert.That(animation.AuthoredDuration, Is.GreaterThan(0f));
                    var result = new JacketWalkResult { clip = clipName, worstFrame = -1, finite = true };
                    reports.Add(result);
                    Texture2D worstFront = null, worstSide = null;
                    var worstPoints = new List<Vector3>();
                    object[] worstTrousers = null;
                    Type posedSurface = typeof(Player3DJointSurfaceTests).GetNestedType("PosedSurface", BindingFlags.NonPublic);
                    ConstructorInfo poseMesh = posedSurface.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(SkinnedMeshRenderer) }, null);
                    MethodInfo penetration = posedSurface.GetMethod("Penetration", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(poseMesh, Is.Not.Null); Assert.That(penetration, Is.Not.Null);
                    registry.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    cloth.RequestReset();
                    int frames = Mathf.CeilToInt(animation.AuthoredDuration * 2f * 60f);
                    var clothMilliseconds = new double[frames];
                    var clothClock = new System.Diagnostics.Stopwatch();
                    try
                    {
                        for (int frame = 0; frame < frames; frame++)
                        {
                            float seconds = frame / 60f;
                            registry.transform.rotation = Quaternion.Euler(0f, 28f * frame / Mathf.Max(1, frames - 1), 0f);
                            if (frame > 0)
                            {
                                float speed = clipName == "Walk" ? Player3DCharacterPresentation.FullWalkSpeed :
                                    -Player3DCharacterPresentation.FullWalkBackSpeed;
                                registry.transform.position += registry.transform.TransformDirection(registry.Metrics.LocalForward) * (speed / 60f);
                            }
                            animation.Clip.SampleAnimation(registry.Animator.gameObject, Mathf.Repeat(seconds, animation.AuthoredDuration));
                            joints.ApplyPose();
                            clothClock.Restart();
                            cloth.ApplyAt(seconds, false, false, new WindSample(0f, 0f));
                            clothClock.Stop();
                            clothMilliseconds[frame] = clothClock.Elapsed.TotalMilliseconds;
                            Assert.That(cloth.LastMaximumSurfaceGeometryWrites, Is.LessThanOrEqualTo(1),
                                "A rendered cloth solve must upload each surface only after the final fold is selected.");
                            Assert.That(cloth.LastHemCandidateReuses, Is.EqualTo(cloth.LastHemFoldSurfaces),
                                "A successful fold must consume its already accepted candidate in the same pose.");
                            result.maximumSurfaceGeometryWrites = Mathf.Max(result.maximumSurfaceGeometryWrites,
                                cloth.LastMaximumSurfaceGeometryWrites);
                            result.hemFoldSurfaces += cloth.LastHemFoldSurfaces;
                            result.hemCandidateReuses += cloth.LastHemCandidateReuses;
                            float top = registry.Anchors.Pelvis.position.y + .22f;
                            float stretch = 1f;
                            string edge = "none";
                            foreach (JacketWalkPanel panel in panels)
                            {
                                panel.Measure(top, result, ref stretch, ref edge);
                            }
                            if (result.worstFrame < 0 || stretch > result.maximumStretch)
                            {
                                result.maximumStretch = stretch; result.worstFrame = frame;
                                result.worstSeconds = seconds; result.worstEdge = edge;
                                worstPoints.Clear();
                                foreach (JacketWalkPanel panel in panels) panel.AddLowerSamples(top, worstPoints);
                                worstTrousers = registry.MeshBindings.Where(binding => binding.MeshName.StartsWith("CLO_Trousers", StringComparison.Ordinal))
                                    .Select(binding => poseMesh.Invoke(new object[] { (SkinnedMeshRenderer)binding.Renderer })).ToArray();
                                Assert.That(worstTrousers, Is.Not.Empty);
                                if (worstFront != null) Object.Destroy(worstFront);
                                if (worstSide != null) Object.Destroy(worstSide);
                                worstFront = CaptureJacketWalk(camera, registry, false);
                                worstSide = CaptureJacketWalk(camera, registry, true);
                                // Preserve the contact state now, not a fresh cloth solve at the selected pose.
                                Player3DJointSurfaceTests.WriteContactReplay(registry, clipName, frame);
                                string replay = Path.Combine(directory, "player-3d-jacket-contacts-" + clipName + "-" + frame + ".json");
                                string selected = Path.Combine(directory, "player-3d-jacket-walk-" + clipName + "-contacts.json");
                                File.Copy(replay, selected, true);
                                File.Delete(replay);
                            }
                            yield return null;
                        }
                        foreach (Vector3 point in worstPoints)
                            foreach (object trousers in worstTrousers)
                                result.maximumPenetration = Mathf.Max(result.maximumPenetration,
                                    (float)penetration.Invoke(trousers, new object[] { point }));
                        File.WriteAllBytes(Path.Combine(directory, "player-3d-jacket-walk-" + clipName + "-front.png"), worstFront.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(directory, "player-3d-jacket-walk-" + clipName + "-side.png"), worstSide.EncodeToPNG());
                    }
                    finally
                    {
                        Array.Sort(clothMilliseconds);
                        TestContext.Out.WriteLine("{0}: jacket update p95={1:F3}ms, maximum={2:F3}ms",
                            clipName, clothMilliseconds[Mathf.CeilToInt(frames * .95f) - 1], clothMilliseconds[frames - 1]);
                        // Evidence survives even an unexpected capture/measurement failure.
                        if (worstFront != null)
                        {
                            File.WriteAllBytes(Path.Combine(directory, "player-3d-jacket-walk-" + clipName + "-front.png"), worstFront.EncodeToPNG());
                            Object.Destroy(worstFront);
                        }
                        if (worstSide != null)
                        {
                            File.WriteAllBytes(Path.Combine(directory, "player-3d-jacket-walk-" + clipName + "-side.png"), worstSide.EncodeToPNG());
                            Object.Destroy(worstSide);
                        }
                        File.WriteAllText(Path.Combine(directory, "player-3d-jacket-walk.json"),
                            JsonUtility.ToJson(new JacketWalkReport { gait = reports.ToArray() }, true));
                    }
                }
                foreach (JacketWalkResult result in reports)
                {
                    string detail = string.Format(CultureInfo.InvariantCulture,
                        "{0}: worst frame={1}, seconds={2:F4}, stretch={3:F4}, edge={4}, vertical up/down={5:F4}/{6:F4}m, " +
                        "pinned={7:F6}m, actual trouser penetration={8:F6}m, maximum surface writes={9}, accepted fold reuse={10}/{11}",
                        result.clip, result.worstFrame, result.worstSeconds, result.maximumStretch, result.worstEdge,
                        result.maximumUpwardDisplacement, result.maximumDownwardDisplacement, result.maximumPinnedDisplacement,
                        result.maximumPenetration, result.maximumSurfaceGeometryWrites, result.hemCandidateReuses,
                        result.hemFoldSurfaces);
                    TestContext.Out.WriteLine(detail);
                    Assert.That(result.finite, Is.True, detail);
                    Assert.That(result.maximumStretch, Is.LessThanOrEqualTo(1.2f), "Walking fabric must preserve material edge length. " + detail);
                    Assert.That(result.maximumPinnedDisplacement, Is.LessThanOrEqualTo(.0001f), "Pinned chest must keep its posed source. " + detail);
                    Assert.That(result.maximumPenetration, Is.LessThan(.001f), "The saved worst hem must clear actual trousers. " + detail);
                }
            }
            finally { foreach (JacketWalkPanel panel in panels) panel.Dispose(); }
        }

        private Texture2D CaptureJacketWalk(Camera camera, Player3DAssetRegistry registry, bool side)
        {
            Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 direction = side ? (right + forward * .12f).normalized : (forward + right * .12f).normalized;
            Vector3 target = registry.Anchors.Pelvis.position + Vector3.up * .025f;
            camera.transform.SetPositionAndRotation(target + direction * 3f, Quaternion.LookRotation(-direction, Vector3.up));
            camera.orthographicSize = .46f;
            lightObject.transform.forward = camera.transform.forward;
            return CaptureJoint(camera);
        }

        [Serializable] private sealed class JacketWalkReport { public JacketWalkResult[] gait; }
        [Serializable] private sealed class JacketWalkResult
        {
            public string clip, worstEdge;
            public int worstFrame, maximumSurfaceGeometryWrites, hemFoldSurfaces, hemCandidateReuses;
            public bool finite;
            public float worstSeconds, maximumStretch, maximumUpwardDisplacement, maximumDownwardDisplacement,
                maximumPinnedDisplacement, maximumPenetration;
        }

        private sealed class JacketWalkPanel : IDisposable
        {
            private readonly SkinnedMeshRenderer renderer;
            private readonly Mesh source, originalBake, deformedBake;
            private readonly float[] freedom;
            private readonly int[] triangles;
            private readonly (int A, int B)[] edges;
            private Vector3[] original, deformed;

            public JacketWalkPanel(PlayerJacketCloth.SurfaceBinding binding)
            {
                renderer = binding.Renderer; source = binding.Source;
                freedom = (float[])typeof(PlayerJacketCloth.SurfaceBinding).GetField("freedom",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binding);
                triangles = source.triangles;
                var unique = new HashSet<(int A, int B)>();
                for (int i = 0; i < triangles.Length; i += 3)
                { Add(triangles[i], triangles[i + 1]); Add(triangles[i + 1], triangles[i + 2]); Add(triangles[i + 2], triangles[i]); }
                edges = unique.OrderBy(edge => edge.A).ThenBy(edge => edge.B).ToArray();
                originalBake = new Mesh { name = "Test posed jacket source" };
                deformedBake = new Mesh { name = "Test posed walking jacket" };
                void Add(int a, int b) => unique.Add(a < b ? (a, b) : (b, a));
            }

            public void Measure(float top, JacketWalkResult result, ref float stretch, ref string edge)
            {
                Mesh live = renderer.sharedMesh;
                renderer.BakeMesh(deformedBake, true);
                try { renderer.sharedMesh = source; renderer.BakeMesh(originalBake, true); }
                finally { renderer.sharedMesh = live; }
                Matrix4x4 toWorld = renderer.localToWorldMatrix;
                original = originalBake.vertices; deformed = deformedBake.vertices;
                for (int i = 0; i < original.Length; i++)
                {
                    original[i] = toWorld.MultiplyPoint3x4(original[i]);
                    deformed[i] = toWorld.MultiplyPoint3x4(deformed[i]);
                    Vector3 delta = deformed[i] - original[i];
                    result.finite &= Finite(delta.x) && Finite(delta.y) && Finite(delta.z);
                    if (freedom[i] <= 1e-7f) result.maximumPinnedDisplacement = Mathf.Max(result.maximumPinnedDisplacement, delta.magnitude);
                    if (original[i].y >= top && deformed[i].y >= top) continue;
                    result.maximumUpwardDisplacement = Mathf.Max(result.maximumUpwardDisplacement, delta.y);
                    result.maximumDownwardDisplacement = Mathf.Max(result.maximumDownwardDisplacement, -delta.y);
                }
                foreach ((int a, int b) in edges)
                {
                    if ((freedom[a] <= 1e-7f && freedom[b] <= 1e-7f) ||
                        (original[a].y >= top && original[b].y >= top)) continue;
                    float rest = Vector3.Distance(original[a], original[b]);
                    if (rest < .015f) continue; // Ignore thin extrusion edges and coincident UV splits.
                    float ratio = Vector3.Distance(deformed[a], deformed[b]) / rest;
                    if (!Finite(ratio)) { result.finite = false; ratio = float.MaxValue; }
                    if (ratio > stretch) { stretch = ratio; edge = renderer.name + " [" + a + "," + b + "]"; }
                }
            }

            public void AddLowerSamples(float top, List<Vector3> points)
            {
                for (int i = 0; i < original.Length; i++)
                    if (original[i].y < top || deformed[i].y < top) points.Add(deformed[i]);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (Mathf.Max(original[a].y, Mathf.Max(original[b].y, original[c].y)) >= top &&
                        Mathf.Max(deformed[a].y, Mathf.Max(deformed[b].y, deformed[c].y)) >= top) continue;
                    points.Add((deformed[a] + deformed[b] + deformed[c]) / 3f);
                    points.Add((deformed[a] + deformed[b]) * .5f);
                    points.Add((deformed[b] + deformed[c]) * .5f);
                    points.Add((deformed[c] + deformed[a]) * .5f);
                }
            }

            private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
            public void Dispose() { Object.DestroyImmediate(originalBake); Object.DestroyImmediate(deformedBake); }
        }
    }
}
