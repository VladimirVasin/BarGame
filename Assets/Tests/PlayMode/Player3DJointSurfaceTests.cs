using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    [PrebuildSetup(typeof(JointSurfaceAssetsSetup))]
    public sealed partial class Player3DJointSurfaceTests
    {
        private const string ManifestPath = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json";

        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(90f)]
        [TestCase(135f)]
        public void ProductionJointSurfaces_RemainConnectedAndKeepTheirCrossSection(float bendDegrees)
        {
            var owner = new GameObject("Test hero joint rig");
            try
            {
                Player3DAssetRegistry registry = Player3DResources.Instantiate(owner.transform);
                registry.Animator.enabled = false;
                Manifest manifest = LoadManifest();
                Assert.That(manifest.joint_surfaces?.contract, Is.EqualTo("character_joint_surfaces_v1"));
                Assert.That(manifest.joint_surfaces.seams, Is.Not.Empty);
                Seam[] trouserSeams = manifest.joint_surfaces.seams.Where(seam =>
                    seam.bone.StartsWith("shin.", StringComparison.Ordinal) &&
                    seam.renderers.All(name => name.StartsWith("CLO_Trousers", StringComparison.Ordinal))).ToArray();
                Assert.That(trouserSeams.Select(seam => seam.corrective_shape),
                    Is.EquivalentTo(new[] { "TrouserKneeFold.L", "TrouserKneeFold.R" }),
                    "Trousers must bend as their own fabric shell over the corrected body knees.");
                Assert.That(registry.AnatomicalParts.Count, Is.EqualTo(16), "Joint topology must retain independent gameplay regions.");
                var pose = registry.GetComponentsInChildren<Transform>(true).ToDictionary(bone => bone, bone => bone.localRotation);
                var probes = manifest.joint_surfaces.seams.Select(seam => new SeamProbe(registry, seam)).ToArray();
                PlayerJacketCloth jacket = registry.GetComponent<PlayerJacketCloth>();
                CharacterJointDeformation deformation = registry.GetComponent<CharacterJointDeformation>();
                Assert.That(deformation, Is.Not.Null, "The live rig must drive its authored body-volume corrections.");
                Assert.That(deformation.HasBindings, Is.True);
                var checkedHips = new HashSet<string>(StringComparer.Ordinal);
                var contactFailures = new List<string>();
                foreach (SeamProbe probe in probes)
                {
                    foreach (var saved in pose) saved.Key.localRotation = saved.Value;
                    SetBend(registry, probe.Seam.bone, bendDegrees);
                    deformation.ApplyPose();
                    jacket.RequestReset();
                    jacket.ApplyAt(0d, false, false, new WindSample(0f, 0f));
                    probe.AssertConnected(bendDegrees);
                    if (probe.Seam.bone.StartsWith("thigh.", StringComparison.Ordinal) && checkedHips.Add(probe.Seam.bone))
                    {
                        try { AssertJacketHemClear(registry, probe.Seam.bone + " at " + bendDegrees); }
                        catch (AssertionException failure)
                        {
                            contactFailures.Add(failure.Message);
                            WriteContactReplay(registry, probe.Seam.bone, bendDegrees);
                        }
                    }
                }
                Assert.That(contactFailures, Is.Empty, string.Join("\n", contactFailures));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        internal static void WriteContactReplay(Player3DAssetRegistry registry, string bone, float angle)
        {
            // A failure keeps the actual cached skin/contact state so geometry
            // can be reproduced offline without another Editor launch.
            object Field(object owner, string name) => owner.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic).GetValue(owner);
            var jacket = registry.GetComponent<PlayerJacketCloth>();
            object contacts = Field(jacket, "contacts");
            var envelopes = new List<ContactEnvelopeReplay>();
            foreach (object proxy in (Array)Field(contacts, "proxies"))
            {
                var planes = (Plane[])Field(proxy, "WorldPlanes");
                if (!(bool)Field(proxy, "Active") || planes == null) continue;
                envelopes.Add(new ContactEnvelopeReplay
                {
                    name = Field(proxy, "Part").ToString(),
                    minimum = (Vector3)Field(proxy, "WorldMinimum"), maximum = (Vector3)Field(proxy, "WorldMaximum"),
                    normals = planes.Select(plane => plane.normal).ToArray(),
                    distances = planes.Select(plane => plane.distance).ToArray()
                });
            }
            var surfaces = new List<ContactSurfaceReplay>();
            foreach (object surface in (Array)Field(jacket, "surfaces"))
            {
                var binding = (PlayerJacketCloth.SurfaceBinding)Field(surface, "binding");
                if (binding.Region != 0) continue;
                var local = (Vector3[])Field(surface, "posedOriginal");
                var skins = (Matrix4x4[])Field(surface, "skins");
                var vertexSkins = (int[])Field(surface, "vertexSkins");
                surfaces.Add(new ContactSurfaceReplay
                {
                    name = binding.Renderer.name,
                    raw = Enumerable.Range(0, local.Length).Select(index => skins[vertexSkins[index]].MultiplyPoint3x4(local[index])).ToArray(),
                    preContact = (Vector3[])Field(Field(surface, "panelShape"), "Raw"),
                    solved = (Vector3[])Field(surface, "world"), freedom = (float[])Field(surface, "contactFreedom"),
                    canonical = (int[])Field(surface, "contactVertices"), triangles = (int[])Field(surface, "contactTriangles")
                });
            }
            ContactSurfaceReplay[] trousers = registry.MeshBindings.Where(binding => binding.MeshName.StartsWith("CLO_Trousers", StringComparison.Ordinal))
                .Select(binding =>
                {
                    var posed = new PosedSurface((SkinnedMeshRenderer)binding.Renderer);
                    return new ContactSurfaceReplay { name = posed.Name, raw = posed.Points, triangles = posed.Triangles };
                }).ToArray();
            var replay = new ContactReplay { bone = bone, angle = angle, envelopes = envelopes.ToArray(), surfaces = surfaces.ToArray(), trousers = trousers };
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "player-3d-jacket-contacts-" + bone + "-" + angle.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ".json"),
                JsonUtility.ToJson(replay));
        }

        [Serializable] private sealed class ContactReplay
        {
            public string bone; public float angle;
            public ContactEnvelopeReplay[] envelopes;
            public ContactSurfaceReplay[] surfaces, trousers;
        }
        [Serializable] private sealed class ContactEnvelopeReplay
        {
            public string name; public Vector3 minimum, maximum;
            public Vector3[] normals; public float[] distances;
        }
        [Serializable] private sealed class ContactSurfaceReplay
        {
            public string name; public Vector3[] raw, preContact, solved;
            public float[] freedom; public int[] canonical, triangles;
        }

        [Test]
        public void JacketCopies_KeepJointContinuityForMirrorAndCameraLocalArmPoses()
        {
            var owner = new GameObject("Test jacket copies");
            Player3DFirstPersonSubset arm = null;
            try
            {
                Player3DAssetRegistry source = Player3DResources.Instantiate(owner.transform);
                Player3DAssetRegistry mirror = Player3DResources.Instantiate(owner.transform);
                source.Animator.enabled = mirror.Animator.enabled = false;
                PlayerWardrobe wardrobe = source.GetComponent<PlayerWardrobe>();
                PlayerWardrobe.GarmentBinding belt = wardrobe.Garments.Single(item => item.Slot == "belt");
                PlayerWardrobe.OutfitSnapshot outfit = wardrobe.CaptureOutfit();
                Assert.That(belt.Id, Is.EqualTo("hero_belt"));
                Assert.That(belt.CoveredBodyRenderers, Is.Empty);
                wardrobe.SetSlot("belt", null);
                Assert.That(belt.Renderers.All(renderer => !renderer.enabled), Is.True);
                Assert.That(wardrobe.GetEquippedItem("trousers"), Is.EqualTo("hero_trousers"),
                    "Removing the independent belt must keep the trousers equipped.");
                wardrobe.RestoreOutfit(outfit);
                Assert.That(belt.Renderers.All(renderer => renderer.enabled), Is.True);
                Manifest manifest = LoadManifest();
                Seam[] jacketSeams = manifest.joint_surfaces.seams.Where(seam => seam.renderers.All(name => name.StartsWith("CLO_Jacket", StringComparison.Ordinal))).ToArray();
                Assert.That(jacketSeams, Is.Not.Empty, "A loose sleeve still requires a shared elbow surface.");
                var sourceProbes = jacketSeams.Select(seam => new SeamProbe(source, seam)).ToArray();
                var mirrorProbes = jacketSeams.Select(seam => new SeamProbe(mirror, seam)).ToArray();
                SetBend(source, "forearm.R", 135f);
                SetBend(source, "forearm.L", 90f);
                PlayerJacketCloth sourceCloth = source.GetComponent<PlayerJacketCloth>();
                source.GetComponent<CharacterJointDeformation>().ApplyPose();
                sourceCloth.ApplyAt(0d, false, false, new WindSample(0f, 0f));
                var sourceBones = source.GetComponentsInChildren<Transform>(true).GroupBy(bone => bone.name).ToDictionary(group => group.Key, group => group.First());
                foreach (Transform bone in mirror.GetComponentsInChildren<Transform>(true))
                    if (sourceBones.TryGetValue(bone.name, out Transform original))
                    { bone.localPosition = original.localPosition; bone.localRotation = original.localRotation; bone.localScale = original.localScale; }
                PlayerJacketCloth mirrorCloth = mirror.GetComponent<PlayerJacketCloth>();
                mirror.GetComponent<CharacterJointDeformation>().ApplyPose();
                sourceCloth.CopyPoseTo(mirrorCloth);
                Assert.That(mirrorCloth.IsRuntimeDriven, Is.False);
                for (int index = 0; index < sourceCloth.SurfaceCount; index++)
                {
                    Assert.That(mirrorCloth.DeformedMesh(index).vertices.Zip(sourceCloth.DeformedMesh(index).vertices,
                        (a, b) => Vector3.Distance(a, b)).Max(), Is.LessThan(.000001f), "The mirror must display the same completed jacket surface.");
                    Assert.That(sourceCloth.SourceMesh(index), Is.Not.SameAs(sourceCloth.DeformedMesh(index)), "Imported jacket geometry must remain immutable.");
                }
                foreach (SeamProbe probe in sourceProbes) probe.AssertConnected(135f);
                foreach (SeamProbe probe in mirrorProbes) probe.AssertConnected(135f);

                arm = Player3DFirstPersonSubset.Create(owner.transform, Player3DFirstPersonSide.Right, 0, "Test camera-local arm", source);
                Seam[] armSeams = jacketSeams.Where(seam => seam.bone.EndsWith(".R", StringComparison.Ordinal)).ToArray();
                var armProbes = armSeams.Select(seam => new SeamProbe(arm.Registry, seam)).ToArray();
                SetBend(arm.Registry, "forearm.R", 45f);
                arm.Registry.GetComponent<CharacterJointDeformation>().ApplyPose();
                arm.RefreshAppearance();
                PlayerJacketCloth armCloth = arm.Registry.GetComponent<PlayerJacketCloth>();
                Assert.That(armCloth.IsRuntimeDriven, Is.False);
                foreach (SeamProbe probe in armProbes) probe.AssertConnected(45f);
                Assert.That(arm.VisibleRenderers.Any(renderer => renderer.name == "CLO_JacketSleeve.R"), Is.True);
                Assert.That(arm.VisibleRenderers.Any(renderer => renderer.name == "CLO_JacketForearm.R"), Is.True);
                var sourceSleeve = (SkinnedMeshRenderer)source.MeshBindings.Single(binding => binding.MeshName == "CLO_JacketSleeve.R").Renderer;
                int sleeve = Enumerable.Range(0, sourceCloth.SurfaceCount).Single(index => sourceCloth.DeformedMesh(index) == sourceSleeve.sharedMesh);
                Assert.That(armCloth.DeformedMesh(sleeve).vertices.Zip(sourceCloth.DeformedMesh(sleeve).vertices,
                    (a, b) => Vector3.Distance(a, b)).Max(), Is.GreaterThan(.00001f),
                    "A camera-local elbow at 45 degrees must recompute its fold instead of retaining the world arm's deep-flex shape.");
            }
            finally { arm?.Dispose(); UnityEngine.Object.DestroyImmediate(owner); }
        }

        internal static void SetBend(Player3DAssetRegistry registry, string boneName, float angle)
        {
            Transform joint = registry.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == boneName);
            string childName = boneName.StartsWith("forearm", StringComparison.Ordinal) ? boneName.Replace("forearm", "hand") :
                boneName.StartsWith("shin", StringComparison.Ordinal) ? boneName.Replace("shin", "foot") :
                boneName.StartsWith("upper_arm", StringComparison.Ordinal) ? boneName.Replace("upper_arm", "forearm") :
                boneName.StartsWith("thigh", StringComparison.Ordinal) ? boneName.Replace("thigh", "shin") : null;
            Vector3 incoming = (joint.position - joint.parent.position).normalized;
            if (childName != null)
            {
                Transform child = registry.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == childName);
                if (boneName.StartsWith("thigh", StringComparison.Ordinal))
                    incoming = (child.position - joint.position).normalized;
                joint.rotation = Quaternion.FromToRotation(child.position - joint.position, incoming) * joint.rotation;
            }
            Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward);
            if (boneName.StartsWith("shin", StringComparison.Ordinal)) forward = -forward;
            Vector3 axis = Vector3.Cross(incoming, forward).normalized;
            Assert.That(axis.sqrMagnitude, Is.GreaterThan(.5f), "The joint must retain a measurable bend plane.");
            joint.rotation = Quaternion.AngleAxis(angle, axis) * joint.rotation;
        }

        internal static void AssertJacketHemClear(Player3DAssetRegistry registry, string pose)
        {
            PlayerJacketCloth cloth = registry.GetComponent<PlayerJacketCloth>();
            var trousers = registry.MeshBindings.Where(binding => binding.MeshName.StartsWith("CLO_Trousers", StringComparison.Ordinal))
                .Select(binding => new PosedSurface((SkinnedMeshRenderer)binding.Renderer)).ToArray();
            float top = registry.Anchors.Pelvis.position.y + .22f;
            foreach (Player3DMeshBinding binding in registry.MeshBindings.Where(binding =>
                binding.MeshName.StartsWith("CLO_Jacket", StringComparison.Ordinal) &&
                !binding.MeshName.Contains("Sleeve") && !binding.MeshName.Contains("Forearm") && !binding.MeshName.Contains("Cuff")))
            {
                var renderer = (SkinnedMeshRenderer)binding.Renderer;
                var coat = new PosedSurface(renderer);
                int surface = Enumerable.Range(0, cloth.SurfaceCount).Where(index => cloth.DeformedMesh(index) == renderer.sharedMesh)
                    .DefaultIfEmpty(-1).Single();
                Vector3[] original = OriginalSkinPoints(renderer, surface < 0 ? renderer.sharedMesh : cloth.SourceMesh(surface));
                for (int vertex = 0; vertex < coat.Points.Length; vertex++)
                    if (coat.Points[vertex].y < top || original[vertex].y < top) Check(coat.Points[vertex], "vertex " + vertex);
                for (int triangle = 0; triangle < coat.Triangles.Length; triangle += 3)
                {
                    Vector3 a = coat.Points[coat.Triangles[triangle]], b = coat.Points[coat.Triangles[triangle + 1]],
                        c = coat.Points[coat.Triangles[triangle + 2]];
                    float originalTop = Mathf.Max(original[coat.Triangles[triangle]].y,
                        Mathf.Max(original[coat.Triangles[triangle + 1]].y, original[coat.Triangles[triangle + 2]].y));
                    if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) >= top && originalTop >= top) continue;
                    string face = "triangle " + triangle / 3 + " [" + coat.Triangles[triangle] + "," +
                        coat.Triangles[triangle + 1] + "," + coat.Triangles[triangle + 2] + "] ";
                    Check((a + b + c) / 3f, face + "centre"); Check((a + b) * .5f, face + "edge AB");
                    Check((b + c) * .5f, face + "edge BC"); Check((c + a) * .5f, face + "edge CA");
                    CheckStretch(coat.Triangles[triangle], coat.Triangles[triangle + 1]);
                    CheckStretch(coat.Triangles[triangle + 1], coat.Triangles[triangle + 2]);
                    CheckStretch(coat.Triangles[triangle + 2], coat.Triangles[triangle]);
                }
                void CheckStretch(int a, int b)
                {
                    float length = Vector3.Distance(original[a], original[b]);
                    // Ignore thin extrusion edges and UV splits. A short panel
                    // edge must not become the long rigid wing of a point escape.
                    if (length < .015f) return;
                    Assert.That(Vector3.Distance(coat.Points[a], coat.Points[b]), Is.LessThan(length * 2f),
                        binding.MeshName + " lower fabric overstretches edge [" + a + "," + b + "] in " + pose);
                }
                void Check(Vector3 point, string sample)
                {
                    foreach (PosedSurface body in trousers)
                        Assert.That(body.Penetration(point), Is.LessThan(.001f),
                            binding.MeshName + " lower fabric penetrates " + body.Name + " in " + pose +
                            " at " + point.ToString("F6") + " (" + sample + ")");
                }
            }
        }

        private static Vector3[] OriginalSkinPoints(SkinnedMeshRenderer renderer, Mesh source)
        {
            Matrix4x4[] skin = source.bindposes.Select((bind, index) => renderer.bones[index].localToWorldMatrix * bind).ToArray();
            Vector3[] original = source.vertices;
            BoneWeight[] weights = source.boneWeights;
            var world = new Vector3[original.Length];
            for (int i = 0; i < world.Length; i++)
            {
                BoneWeight weight = weights[i];
                world[i] = skin[weight.boneIndex0].MultiplyPoint3x4(original[i]) * weight.weight0;
                if (weight.weight1 > 0f) world[i] += skin[weight.boneIndex1].MultiplyPoint3x4(original[i]) * weight.weight1;
                if (weight.weight2 > 0f) world[i] += skin[weight.boneIndex2].MultiplyPoint3x4(original[i]) * weight.weight2;
                if (weight.weight3 > 0f) world[i] += skin[weight.boneIndex3].MultiplyPoint3x4(original[i]) * weight.weight3;
            }
            return world;
        }

        private sealed class PosedSurface
        {
            internal readonly string Name;
            internal readonly Vector3[] Points;
            internal readonly int[] Triangles;
            private readonly Bounds bounds;
            internal PosedSurface(SkinnedMeshRenderer renderer)
            {
                Name = renderer.name;
                var mesh = new Mesh();
                try
                {
                    renderer.BakeMesh(mesh, true);
                    Points = mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();
                    Triangles = mesh.triangles;
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
                bounds = new Bounds(Points[0], Vector3.zero);
                foreach (Vector3 point in Points) bounds.Encapsulate(point);
            }
            internal float Penetration(Vector3 point)
            {
                if (!bounds.Contains(point)) return 0f;
                double angle = 0d;
                float distance = float.PositiveInfinity;
                for (int triangle = 0; triangle < Triangles.Length; triangle += 3)
                {
                    Vector3 pa = Points[Triangles[triangle]], pb = Points[Triangles[triangle + 1]], pc = Points[Triangles[triangle + 2]];
                    Vector3 a = pa - point, b = pb - point, c = pc - point;
                    double denominator = a.magnitude * b.magnitude * c.magnitude +
                        Vector3.Dot(a, b) * c.magnitude + Vector3.Dot(b, c) * a.magnitude + Vector3.Dot(c, a) * b.magnitude;
                    angle += 2d * Math.Atan2(Vector3.Dot(a, Vector3.Cross(b, c)), denominator);
                    distance = Mathf.Min(distance, Vector3.Distance(point, PlayerScarfContactSolver.ClosestPoint(point, pa, pb, pc)));
                }
                return Math.Abs(angle) > Math.PI * 2d ? distance : 0f;
            }
        }

        private static Manifest LoadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ManifestPath))));

        private sealed class SeamProbe
        {
            internal readonly Seam Seam;
            private readonly SkinnedMeshRenderer first, second;
            private readonly int[] firstIndices, secondIndices;
            private readonly float restArea;

            internal SeamProbe(Player3DAssetRegistry registry, Seam seam)
            {
                Seam = seam;
                first = (SkinnedMeshRenderer)registry.MeshBindings.Single(binding => binding.MeshName == seam.renderers[0]).Renderer;
                second = (SkinnedMeshRenderer)registry.MeshBindings.Single(binding => binding.MeshName == seam.renderers[1]).Renderer;
                Mesh sourceA = BindMesh(registry, first), sourceB = BindMesh(registry, second);
                Vector3[] a = sourceA.vertices, b = sourceB.vertices;
                Vector3[] na = sourceA.normals, nb = sourceB.normals;
                firstIndices = new int[seam.points_blender.Length]; secondIndices = new int[firstIndices.Length];
                var restRing = new Vector3[firstIndices.Length];
                for (int point = 0; point < firstIndices.Length; point++)
                {
                    Vector3 authored = seam.points_blender[point];
                    Vector3 expected = registry.transform.TransformPoint(new Vector3(-authored.x, authored.z, -authored.y));
                    int[] candidatesA = Enumerable.Range(0, a.Length).Where(index => Vector3.Distance(first.transform.TransformPoint(a[index]), expected) < .00005f).ToArray();
                    int[] candidatesB = Enumerable.Range(0, b.Length).Where(index => Vector3.Distance(second.transform.TransformPoint(b[index]), expected) < .00005f).ToArray();
                    Assert.That(candidatesA, Is.Not.Empty, seam.id + " first imported seam vertex");
                    Assert.That(candidatesB, Is.Not.Empty, seam.id + " second imported seam vertex");
                    float best = -2f;
                    foreach (int ia in candidatesA)
                        foreach (int ib in candidatesB)
                        {
                            float dot = Vector3.Dot(first.transform.TransformDirection(na[ia]).normalized, second.transform.TransformDirection(nb[ib]).normalized);
                            if (dot > best) { best = dot; firstIndices[point] = ia; secondIndices[point] = ib; }
                        }
                    restRing[point] = expected;
                }
                restArea = RingArea(restRing);
                Assert.That(restArea, Is.GreaterThan(.00001f), seam.id + " authored cross section");
            }

            private static Mesh BindMesh(Player3DAssetRegistry registry, SkinnedMeshRenderer renderer)
            {
                PlayerJacketCloth cloth = registry.GetComponent<PlayerJacketCloth>();
                if (cloth != null)
                    for (int surface = 0; surface < cloth.SurfaceCount; surface++)
                        if (cloth.DeformedMesh(surface) == renderer.sharedMesh) return cloth.SourceMesh(surface);
                return renderer.sharedMesh;
            }

            internal void AssertConnected(float angle)
            {
                var bakedA = new Mesh(); var bakedB = new Mesh();
                try
                {
                    first.BakeMesh(bakedA, true); second.BakeMesh(bakedB, true);
                    Vector3[] a = bakedA.vertices, b = bakedB.vertices, na = bakedA.normals, nb = bakedB.normals;
                    var ring = new Vector3[firstIndices.Length];
                    for (int point = 0; point < firstIndices.Length; point++)
                    {
                        Vector3 worldA = first.transform.TransformPoint(a[firstIndices[point]]);
                        Vector3 worldB = second.transform.TransformPoint(b[secondIndices[point]]);
                        Assert.That(Vector3.Distance(worldA, worldB), Is.LessThan(.0001f), Seam.id + " at " + angle + " degrees has a visible boundary gap.");
                        Vector3 normalA = first.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(na[firstIndices[point]]).normalized;
                        Vector3 normalB = second.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(nb[secondIndices[point]]).normalized;
                        Assert.That(Vector3.Angle(normalA, normalB), Is.LessThan(2f), Seam.id + " at " + angle + " degrees lost continuous lighting.");
                        ring[point] = worldA;
                    }
                    float minimumArea = string.IsNullOrEmpty(Seam.corrective_shape) ? .15f : .65f;
                    Assert.That(RingArea(ring) / restArea, Is.GreaterThan(minimumArea), Seam.id + " at " + angle + " degrees collapsed into a pinched joint.");
                }
                finally { UnityEngine.Object.DestroyImmediate(bakedA); UnityEngine.Object.DestroyImmediate(bakedB); }
            }

            private static float RingArea(Vector3[] points)
            {
                Vector3 sum = Vector3.zero;
                Vector3 origin = points[0];
                for (int index = 0; index < points.Length; index++)
                    sum += Vector3.Cross(points[index] - origin, points[(index + 1) % points.Length] - origin);
                return sum.magnitude * .5f;
            }
        }

        [Serializable] private sealed class Manifest { public JointSurfaces joint_surfaces; }
        [Serializable] private sealed class JointSurfaces { public string contract; public Seam[] seams; }
        [Serializable] private sealed class Seam
        {
            public string id, bone, corrective_shape;
            public string[] renderers;
            public Vector3[] points_blender;
        }
    }
}
