using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The nine hand props of the pedestrian library as separate prefabs
    /// (the user's rule of 2026-09-05: nothing an NPC holds is part of
    /// a body). Every check that involves a transform is made in WORLD
    /// space on a real body instance: the Mount hides a 100x FBX bone
    /// scale, so a local scale of 0.01 or 100 is correct and a prop
    /// judged by its local values would pass at any size.
    /// </summary>
    public sealed class CityPedestrianHandPropTests
    {
        private const string ManifestPath =
            "Assets/Pedestrians/Props/CityPedestrianHandProps.json";
        private const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        private const string MountName = "Mount";
        private const string ResourcesPrefabFolder =
            "Assets/Resources/Pedestrians";
        private const string StagedPrefabFolder =
            "Assets/Pedestrians/Staged/Prefabs";

        /// <summary>A prop root may not be further than this from the
        /// socket it hangs on: the Mount was measured against the socket
        /// head, and anything else is a stale or mis-scaled Mount.</summary>
        private const float SocketSeatTolerance = 0.02f;

        /// <summary>The 100x trap: an in-hand prop of the wrong scale is
        /// either invisible or the size of a car.</summary>
        private const float SmallPropMaximumSize = 0.35f;
        private const float LargePropMaximumSize = 0.8f;
        private const float RodTipMinimumReach = 1.4f;
        private const float RodTipMaximumReach = 2.6f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// A colour that has been through a MaterialPropertyBlock is not
        /// bit-exact (`0.31f` reads back one ULP off), and NUnit's
        /// `Is.EqualTo` on a Color is bitwise, so colours are compared
        /// per channel within this tolerance.
        /// </summary>
        private const float ColorTolerance = 0.00001f;

        public static IEnumerable<CityPedestrianHandPropId> AllIds =>
            CityPedestrianHandProps.Ids;

        [Serializable]
        private sealed class Manifest
        {
            public string generator_version;
            public string library;
            public string material_asset;
            public int mesh_count;
            public int triangle_count;
            public string build_signature;
            public ManifestProp[] props;
        }

        [Serializable]
        private sealed class ManifestProp
        {
            public string id;
            public string prefab_name;
            public string socket;
            public string bone;
            public string reference_design;
            public string root;
            public float[] socket_head_m;
            public int mesh_count;
            public int triangle_count;
            public float[] bounds_min;
            public float[] bounds_max;
            public ManifestPart[] parts;
            public ManifestAnchor[] anchors;
        }

        [Serializable]
        private sealed class ManifestPart
        {
            public string name;
            public string role;
            public string palette_name;
            public float[] base_color;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class ManifestAnchor
        {
            public string name;
            public string kind;
            public string part;
            public string axis_from;
        }

        /// <summary>
        /// Every renderer name a prop prefab carries, for the body
        /// sweeps here and in the balcony test. Exact names: the sweep
        /// must never be prefix-based, because `ACC_PipeManifold` on the
        /// pipeback roller and the bartender's own `ACC_ServiceTowel`
        /// (a different generator, a different model) are not props.
        /// </summary>
        public static HashSet<string> CollectPropPartNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityPedestrianHandPropId id in CityPedestrianHandProps.Ids)
            {
                GameObject prefab = CityPedestrianHandProps.LoadPrefab(id);
                Assert.That(
                    prefab,
                    Is.Not.Null,
                    $"Hand prop prefab '{CityPedestrianHandProps.GetResourcePath(id)}' " +
                    "is missing; run Bar Promenade/City Pedestrian 3D/Build Hand Props.");
                var registry = prefab.GetComponent<CityPedestrianHandPropRegistry>();
                Assert.That(registry, Is.Not.Null, id.ToString());
                foreach (Renderer renderer in registry.Renderers)
                {
                    Assert.That(renderer, Is.Not.Null, id.ToString());
                    names.Add(renderer.name);
                }
            }

            Assert.That(names, Is.Not.Empty);
            return names;
        }

        [Test]
        public void Library_DeclaresTheNinePropsInEnumOrder()
        {
            Manifest manifest = LoadManifest();
            Assert.That(
                manifest.library,
                Is.EqualTo("CityPedestrianHandProps"));
            Assert.That(
                manifest.material_asset,
                Is.EqualTo(SharedMaterialPath),
                "Props render in the pedestrians' shared material.");
            Assert.That(
                manifest.props,
                Has.Length.EqualTo(CityPedestrianHandProps.Ids.Count));

            int triangles = 0;
            int meshes = 0;
            for (int index = 0; index < manifest.props.Length; index++)
            {
                ManifestProp prop = manifest.props[index];
                CityPedestrianHandPropId id = CityPedestrianHandProps.Ids[index];
                Assert.That(
                    prop.id,
                    Is.EqualTo(CityPedestrianHandProps.GetManifestId(id)),
                    "The manifest order is the enum order; the integer " +
                    "values are serialized on the prefabs.");
                Assert.That(
                    prop.prefab_name,
                    Is.EqualTo(CityPedestrianHandProps.GetPrefabName(id)));
                Assert.That(
                    prop.socket,
                    Is.EqualTo(CityPedestrianHandProps.GetSocketName(id)));
                Assert.That(prop.root, Is.EqualTo("PROP_" + prop.prefab_name));
                Assert.That(prop.parts, Has.Length.EqualTo(prop.mesh_count));
                Assert.That(
                    prop.parts.Sum(part => part.triangles),
                    Is.EqualTo(prop.triangle_count));
                triangles += prop.triangle_count;
                meshes += prop.mesh_count;
            }

            Assert.That(triangles, Is.EqualTo(manifest.triangle_count));
            Assert.That(meshes, Is.EqualTo(manifest.mesh_count));

            // The anchors the runtime reaches by name.
            Assert.That(
                FindProp(manifest, CityPedestrianHandPropId.FishingRod).anchors
                    .Select(anchor => anchor.name),
                Is.EquivalentTo(new[] { CityPedestrianHandProps.RodTipAnchorName }));
            Assert.That(
                FindProp(manifest, CityPedestrianHandPropId.SmokingPipe).anchors
                    .Select(anchor => anchor.name),
                Is.EquivalentTo(new[] { CityPedestrianHandProps.PipeEmberAnchorName }));
            Assert.That(
                FindProp(manifest, CityPedestrianHandPropId.CoffeePot).anchors
                    .Select(anchor => anchor.name),
                Is.EquivalentTo(new[] { CityPedestrianHandProps.CoffeePotSpoutAnchorName }));
        }

        [TestCaseSource(nameof(AllIds))]
        public void Prefab_MatchesTheManifest(CityPedestrianHandPropId id)
        {
            ManifestProp expected = FindProp(LoadManifest(), id);
            Manifest manifest = LoadManifest();
            GameObject prefab = CityPedestrianHandProps.LoadPrefab(id);
            Assert.That(
                prefab,
                Is.Not.Null,
                $"'{CityPedestrianHandProps.GetResourcePath(id)}' is missing " +
                "from Resources.");
            Assert.That(
                prefab.name,
                Is.EqualTo(CityPedestrianHandProps.GetPrefabName(id)));
            Assert.That(
                CityPedestrianHandProps.IsAvailable(id),
                Is.True);

            var registry = prefab.GetComponent<CityPedestrianHandPropRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Id, Is.EqualTo(id));
            Assert.That(registry.ManifestId, Is.EqualTo(expected.id));
            Assert.That(
                registry.SocketName,
                Is.EqualTo(CityPedestrianHandProps.GetSocketName(id)));
            Assert.That(
                registry.ReferenceDesignId,
                Is.EqualTo(expected.reference_design));
            Assert.That(
                registry.SourceTriangleCount,
                Is.EqualTo(expected.triangle_count),
                "The prefab must be built from the current manifest.");
            Assert.That(
                registry.BuildSignature,
                Is.EqualTo(manifest.build_signature));
            Assert.That(
                registry.SourceGeneratorVersion,
                Is.EqualTo(manifest.generator_version));

            // Hierarchy: root (identity) -> Mount -> parts and anchors.
            Assert.That(prefab.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(prefab.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Transform mount = registry.Mount;
            Assert.That(mount, Is.Not.Null);
            Assert.That(mount.name, Is.EqualTo(MountName));
            Assert.That(mount.parent, Is.SameAs(prefab.transform));
            Assert.That(
                mount.localPosition,
                Is.EqualTo(registry.MountLocalPosition),
                "The stored Mount pose must be what the prefab wears.");
            Assert.That(mount.localRotation, Is.EqualTo(registry.MountLocalRotation));
            Assert.That(mount.localScale, Is.EqualTo(registry.MountLocalScale));

            // Renderers: exactly the manifest parts, rigid meshes in the
            // shared material, each bound for the palette.
            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(sharedMaterial, Is.Not.Null);
            Assert.That(
                registry.Renderers.Select(renderer => renderer.name),
                Is.EquivalentTo(expected.parts.Select(part => part.name)),
                "The prefab's renderers are the manifest parts, no more, " +
                "no fewer.");
            int triangles = 0;
            foreach (Renderer renderer in registry.Renderers)
            {
                Assert.That(renderer, Is.InstanceOf<MeshRenderer>(), renderer.name);
                Assert.That(
                    renderer.transform.parent,
                    Is.SameAs(mount),
                    $"{renderer.name} must be a direct child of the Mount.");
                var filter = renderer.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null, renderer.name);
                Assert.That(filter.sharedMesh, Is.Not.Null, renderer.name);
                Assert.That(
                    renderer.sharedMaterials,
                    Has.Length.EqualTo(1),
                    renderer.name);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(sharedMaterial),
                    renderer.name);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On),
                    renderer.name);
                ManifestPart part = expected.parts.Single(candidate =>
                    string.Equals(candidate.name, renderer.name, StringComparison.Ordinal));
                int meshTriangles = 0;
                for (int subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                {
                    meshTriangles += (int)filter.sharedMesh.GetIndexCount(subMesh) / 3;
                }

                Assert.That(
                    meshTriangles,
                    Is.EqualTo(part.triangles),
                    $"{renderer.name} triangle count differs from the manifest.");
                triangles += meshTriangles;
            }

            Assert.That(triangles, Is.EqualTo(expected.triangle_count));
            Assert.That(
                registry.RendererBindings.Select(binding => binding.Renderer),
                Is.EquivalentTo(registry.Renderers),
                "Every renderer carries exactly one palette binding.");
            foreach (CityPedestrianRendererBinding binding in registry.RendererBindings)
            {
                ManifestPart part = expected.parts.Single(candidate =>
                    string.Equals(candidate.name, binding.RendererName, StringComparison.Ordinal));
                Assert.That(binding.PaletteName, Is.EqualTo(part.palette_name));
                Assert.That(binding.Role, Is.EqualTo(part.role));
            }

            // Anchors: exactly the manifest's, under the Mount.
            Assert.That(
                registry.Anchors.Select(anchor => anchor.Name),
                Is.EquivalentTo(expected.anchors.Select(anchor => anchor.name)));
            foreach (CityPedestrianHandPropAnchor anchor in registry.Anchors)
            {
                Assert.That(anchor.Transform, Is.Not.Null, anchor.Name);
                Assert.That(anchor.Transform.name, Is.EqualTo(anchor.Name));
                Assert.That(
                    anchor.Transform.IsChildOf(mount),
                    Is.True,
                    $"{anchor.Name} must ride the Mount.");
            }

            // Passive: a prop is geometry and nothing else.
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Animation>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(registry.Renderers.Count),
                "Every renderer in the prefab is registered.");
        }

        /// <summary>
        /// Attached to a real body, the prop root sits on its socket and
        /// the geometry is the authored size and reach: the manifest's
        /// bounds are in the reference body's frame with the socket head
        /// known, so the sorted extents and the farthest corner's
        /// distance from the socket are axis-independent expectations,
        /// and a stale or mis-scaled Mount misses them by metres.
        /// </summary>
        [TestCaseSource(nameof(AllIds))]
        public void Attach_SeatsThePropOnItsSocketAtHandScale(
            CityPedestrianHandPropId id)
        {
            ManifestProp expected = FindProp(LoadManifest(), id);
            var parent = new GameObject("Hand Prop Attach Test");
            try
            {
                CityPedestrianAssetRegistry body = InstantiateBodyFor(id, parent.transform);
                Transform socket = CityPedestrianHandProps.FindSocket(body.ModelRoot, id);
                Assert.That(
                    socket,
                    Is.Not.Null,
                    $"{body.DesignId} has no '{CityPedestrianHandProps.GetSocketName(id)}'.");

                CityPedestrianHandPropRegistry prop =
                    CityPedestrianHandProps.Attach(body, id);
                Assert.That(prop, Is.Not.Null);
                Assert.That(prop.Id, Is.EqualTo(id));
                Assert.That(prop.transform.parent, Is.SameAs(socket));
                Assert.That(
                    CityPedestrianHandProps.FindAttached(socket, id),
                    Is.SameAs(prop));
                Assert.That(
                    Vector3.Distance(prop.transform.position, socket.position),
                    Is.LessThan(SocketSeatTolerance),
                    "The prop root must sit on the socket.");
                Assert.That(
                    prop.gameObject.layer,
                    Is.EqualTo(socket.gameObject.layer));

                // The body stands in its bind pose, and the Mount was
                // measured with the reference FBX at identity — but the
                // pedestrian prefab turns its Model child by 180 degrees
                // about Y (`CityPedestrianAssetSetup` builds it so), so the
                // comparison is made in the model root's own frame, which
                // IS the FBX frame. A socket that has moved in that frame
                // since the build means a stale Mount.
                Vector3 socketInModelFrame =
                    body.ModelRoot.InverseTransformPoint(socket.position);
                Assert.That(
                    Vector3.Distance(
                        socketInModelFrame,
                        prop.ReferenceSocketRestPosition),
                    Is.LessThan(SocketSeatTolerance),
                    $"{body.DesignId}'s '{socket.name}' rests " +
                    $"{Vector3.Distance(socketInModelFrame, prop.ReferenceSocketRestPosition):F4} m " +
                    "from where the prop was measured.");

                MeasureWorldBounds(prop, out Vector3 min, out Vector3 max);
                Vector3 size = max - min;
                float largest = Mathf.Max(size.x, size.y, size.z);
                float cap = IsSmallProp(id) ? SmallPropMaximumSize : LargePropMaximumSize;
                if (id == CityPedestrianHandPropId.FishingRod)
                {
                    cap = RodTipMaximumReach;
                }

                Assert.That(
                    largest,
                    Is.LessThan(cap),
                    $"{id} spans {largest:F3} m in hand: the 100x trap.");

                float[] expectedSize = SortedExtents(expected.bounds_min, expected.bounds_max);
                float[] actualSize = SortedExtents(min, max);
                for (int axis = 0; axis < 3; axis++)
                {
                    Assert.That(
                        actualSize[axis],
                        Is.EqualTo(expectedSize[axis]).Within(0.03f),
                        $"{id} extent {axis} differs from the authored geometry.");
                }

                float expectedReach = FarthestCorner(
                    ToVector(expected.bounds_min),
                    ToVector(expected.bounds_max),
                    ToVector(expected.socket_head_m));
                float actualReach = FarthestCorner(min, max, socket.position);
                Assert.That(
                    actualReach,
                    Is.EqualTo(expectedReach).Within(0.05f),
                    $"{id} reaches {actualReach:F3} m from the socket; the " +
                    $"generator authored {expectedReach:F3} m.");

                if (id == CityPedestrianHandPropId.FishingRod)
                {
                    Transform tip = prop.RequireAnchor(CityPedestrianHandProps.RodTipAnchorName);
                    float reach = Vector3.Distance(tip.position, socket.position);
                    Assert.That(
                        reach,
                        Is.InRange(RodTipMinimumReach, RodTipMaximumReach),
                        $"The rod tip is {reach:F3} m from the grip.");
                    // The far end: the tip stands (almost) as far from the
                    // grip as the rod reaches at all. Judged on the anchor's
                    // own distance, not on AABB corners — a rod held at a
                    // diagonal has box corners farther from its tip than
                    // from its grip even when the tip is exactly right.
                    Assert.That(
                        reach,
                        Is.GreaterThan(0.8f * actualReach),
                        $"The tip anchor is {reach:F3} m from the grip but the rod " +
                        $"reaches {actualReach:F3} m: the anchor is not at the far end.");
                }

                if (id == CityPedestrianHandPropId.SmokingPipe)
                {
                    Transform ember = prop.RequireAnchor(CityPedestrianHandProps.PipeEmberAnchorName);
                    Assert.That(
                        Vector3.Distance(ember.position, socket.position),
                        Is.InRange(0.02f, SmallPropMaximumSize),
                        "The ember sits on the bowl, a pipe's length from the mouth.");
                    Assert.That(
                        prop.FindRenderer(SeacoastFishermanFactory.PipeEmberRendererName),
                        Is.Not.Null);
                }

                if (id == CityPedestrianHandPropId.CoffeePot)
                {
                    Transform spout = prop.RequireAnchor(CityPedestrianHandProps.CoffeePotSpoutAnchorName);
                    Renderer potBody = prop.FindRenderer("ACC_CoffeePotBody");
                    Assert.That(potBody, Is.Not.Null);
                    Vector3 bodyCentre = MeasureMeshCentre(potBody);
                    Vector3 outward = spout.position - bodyCentre;
                    Assert.That(
                        outward.magnitude,
                        Is.InRange(0.03f, 0.3f),
                        "The spout lip is the far end of the spout.");
                    Assert.That(
                        Vector3.Dot(spout.forward, outward.normalized),
                        Is.GreaterThan(0.99f),
                        "The spout anchor points out of the pot along the spout.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Attach_RefusesAnotherSocket()
        {
            var parent = new GameObject("Hand Prop Wrong Socket Test");
            try
            {
                CityPedestrianAssetRegistry body = InstantiateBodyFor(
                    CityPedestrianHandPropId.Cigarette,
                    parent.transform);
                Transform grip = CityPedestrianHandProps.FindSocket(
                    body.ModelRoot,
                    CityPedestrianHandProps.GripRightSocketName);
                Assert.That(grip, Is.Not.Null);
                Material material = body.Renderers[0].sharedMaterial;

                Assert.Throws<InvalidOperationException>(() =>
                    CityPedestrianHandProps.Attach(
                        grip,
                        CityPedestrianHandPropId.Cigarette,
                        material,
                        0));
                Assert.Throws<InvalidOperationException>(() =>
                    CityPedestrianHandProps.Attach(
                        body.ModelRoot,
                        CityPedestrianHandPropId.CarpetBeater,
                        material,
                        0));
                Assert.Throws<ArgumentNullException>(() =>
                    CityPedestrianHandProps.Attach(
                        null,
                        CityPedestrianHandPropId.Chalk,
                        material,
                        0));
                Assert.That(
                    CityPedestrianHandProps.FindAttached(
                        grip,
                        CityPedestrianHandPropId.Cigarette),
                    Is.Null,
                    "A refused attach leaves nothing behind.");
                Assert.That(
                    parent.GetComponentsInChildren<CityPedestrianHandPropRegistry>(true),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Detach_DestroysTheInstanceAndNullsTheReference()
        {
            var parent = new GameObject("Hand Prop Detach Test");
            try
            {
                CityPedestrianAssetRegistry body = InstantiateBodyFor(
                    CityPedestrianHandPropId.Chalk,
                    parent.transform);
                CityPedestrianHandPropRegistry chalk =
                    CityPedestrianHandProps.Attach(body, CityPedestrianHandPropId.Chalk);
                GameObject instance = chalk.gameObject;
                Transform socket = chalk.transform.parent;

                CityPedestrianHandProps.Detach(ref chalk);
                Assert.That(chalk, Is.Null);
                Assert.That(instance == null, Is.True, "The instance must be destroyed.");
                Assert.That(
                    CityPedestrianHandProps.FindAttached(socket, CityPedestrianHandPropId.Chalk),
                    Is.Null);
                Assert.That(
                    parent.GetComponentsInChildren<CityPedestrianHandPropRegistry>(true),
                    Is.Empty);

                // Null-safe and idempotent: a role that may or may not
                // hold something calls it on every release.
                CityPedestrianHandProps.Detach(ref chalk);
                Assert.That(chalk, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// Free-standing, the Mount is identity and the parts sit in the
        /// import frame in metres with the socket head at the root, so
        /// the farthest part corner is exactly as far from the root as
        /// the generator authored it from the socket head.
        /// </summary>
        [TestCaseSource(nameof(AllIds))]
        public void Place_KeepsThePartsWithinTheAuthoredReachOfTheRoot(
            CityPedestrianHandPropId id)
        {
            ManifestProp expected = FindProp(LoadManifest(), id);
            var parent = new GameObject("Hand Prop Place Test");
            parent.transform.SetPositionAndRotation(
                new Vector3(12f, 3f, -4f),
                Quaternion.Euler(0f, 61f, 0f));
            try
            {
                var localPosition = new Vector3(0.5f, 0.25f, -0.75f);
                Quaternion localRotation = Quaternion.Euler(0f, -30f, 0f);
                CityPedestrianHandPropRegistry prop = CityPedestrianHandProps.Place(
                    id,
                    parent.transform,
                    localPosition,
                    localRotation,
                    null,
                    1);
                Assert.That(prop, Is.Not.Null);
                Assert.That(prop.Id, Is.EqualTo(id));
                Assert.That(prop.transform.parent, Is.SameAs(parent.transform));
                Assert.That(
                    Vector3.Distance(prop.transform.localPosition, localPosition),
                    Is.LessThan(0.0001f));
                Assert.That(prop.Mount.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(prop.Mount.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(prop.Mount.localScale, Is.EqualTo(Vector3.one));
                Assert.That(prop.PaletteVariant, Is.EqualTo(1));
                Assert.That(prop.gameObject.layer, Is.EqualTo(parent.layer));

                MeasureWorldBounds(prop, out Vector3 min, out Vector3 max);
                float expectedReach = FarthestCorner(
                    ToVector(expected.bounds_min),
                    ToVector(expected.bounds_max),
                    ToVector(expected.socket_head_m));
                float actualReach = FarthestCorner(min, max, prop.transform.position);
                Assert.That(
                    actualReach,
                    Is.EqualTo(expectedReach).Within(0.05f),
                    $"{id} placed free-standing reaches {actualReach:F3} m " +
                    $"from its root; authored {expectedReach:F3} m.");
                float cap = id == CityPedestrianHandPropId.FishingRod
                    ? RodTipMaximumReach
                    : LargePropMaximumSize;
                Assert.That(actualReach, Is.LessThan(cap));

                // Restoring the Mount puts back the measured pose.
                prop.RestoreMountToSocketPose();
                Assert.That(prop.Mount.localPosition, Is.EqualTo(prop.MountLocalPosition));
                Assert.That(prop.Mount.localRotation, Is.EqualTo(prop.MountLocalRotation));
                Assert.That(prop.Mount.localScale, Is.EqualTo(prop.MountLocalScale));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The same four-variant tint the bodies wear: a binding on a
        /// variant palette changes colour with the variant, through the
        /// property block (no material is ever instantiated).
        /// </summary>
        [Test]
        public void ApplyPaletteVariant_TintsVariantPalettesThroughPropertyBlocks()
        {
            var parent = new GameObject("Hand Prop Palette Test");
            int varyingBindings = 0;
            try
            {
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
                var block = new MaterialPropertyBlock();
                foreach (CityPedestrianHandPropId id in CityPedestrianHandProps.Ids)
                {
                    CityPedestrianHandPropRegistry prop = CityPedestrianHandProps.Place(
                        id,
                        parent.transform,
                        Vector3.zero,
                        Quaternion.identity,
                        sharedMaterial,
                        0);
                    Assert.That(prop.PaletteVariant, Is.EqualTo(0));
                    foreach (CityPedestrianRendererBinding binding in prop.RendererBindings)
                    {
                        binding.Renderer.GetPropertyBlock(block);
                        AssertColorNear(
                            block.GetColor(BaseColorId),
                            binding.GetColor(0),
                            $"{id}/{binding.RendererName} at variant 0");
                    }

                    prop.ApplyPaletteVariant(1);
                    Assert.That(prop.PaletteVariant, Is.EqualTo(1));
                    foreach (CityPedestrianRendererBinding binding in prop.RendererBindings)
                    {
                        binding.Renderer.GetPropertyBlock(block);
                        Color tinted = block.GetColor(BaseColorId);
                        AssertColorNear(
                            tinted,
                            binding.GetColor(1),
                            $"{id}/{binding.RendererName} at variant 1");
                        if (binding.GetColor(1) != binding.GetColor(0))
                        {
                            varyingBindings++;
                            Assert.That(
                                ColorDistance(tinted, binding.GetColor(0)),
                                Is.GreaterThan(ColorTolerance),
                                $"{id}/{binding.RendererName} did not change.");
                        }

                        Assert.That(
                            binding.Renderer.sharedMaterial,
                            Is.SameAs(sharedMaterial),
                            "Tinting never instantiates a material.");
                    }

                    // Wraps like the bodies: variant 5 is variant 1.
                    prop.ApplyPaletteVariant(5);
                    Assert.That(prop.PaletteVariant, Is.EqualTo(1));
                    prop.ApplyPaletteVariant(-1);
                    Assert.That(prop.PaletteVariant, Is.EqualTo(3));
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }

            Assert.That(
                varyingBindings,
                Is.GreaterThan(0),
                "At least one prop part rides a variant palette (the " +
                "bouquet's blooms follow the visit's palette).");
        }

        [Test]
        public void Visibility_HidesAndShowsThePropAsAWhole()
        {
            var parent = new GameObject("Hand Prop Visibility Test");
            try
            {
                CityPedestrianHandPropRegistry pot = CityPedestrianHandProps.Place(
                    CityPedestrianHandPropId.CoffeePot,
                    parent.transform,
                    Vector3.zero,
                    Quaternion.identity,
                    null,
                    0);
                Assert.That(pot.IsVisible, Is.True);
                pot.SetVisible(false);
                Assert.That(pot.IsVisible, Is.False);
                Assert.That(pot.Renderers.All(renderer => !renderer.enabled), Is.True);
                pot.SetVisible(true);
                Assert.That(pot.IsVisible, Is.True);
                Assert.That(pot.Renderers.All(renderer => renderer.enabled), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The whole point of the library: no body ships a prop. Every
        /// pedestrian prefab (Resources and Staged, which includes the
        /// cafe cast) is swept for renderers named exactly like a prop
        /// part. Exact names only — never prefixes.
        /// </summary>
        [Test]
        public void Bodies_CarryNoRendererNamedLikeAPropPart()
        {
            HashSet<string> propParts = CollectPropPartNames();
            string[] prefabPaths = AssetDatabase
                .FindAssets("t:Prefab", new[] { ResourcesPrefabFolder, StagedPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    string.Equals(
                        Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        ResourcesPrefabFolder,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        StagedPrefabFolder,
                        StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(
                prefabPaths.Length,
                Is.GreaterThanOrEqualTo(20),
                "Both pedestrian prefab folders must be swept.");

            var offenders = new List<string>();
            foreach (string path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (propParts.Contains(renderer.name))
                    {
                        offenders.Add($"{path}: {renderer.name}");
                    }
                }
            }

            Assert.That(
                offenders,
                Is.Empty,
                "Bodies still carry hand-prop parts:\n" + string.Join("\n", offenders));
        }

        private static Manifest LoadManifest()
        {
            Assert.That(File.Exists(ManifestPath), Is.True, $"{ManifestPath} is missing.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.props, Is.Not.Null);
            return manifest;
        }

        private static ManifestProp FindProp(Manifest manifest, CityPedestrianHandPropId id)
        {
            string manifestId = CityPedestrianHandProps.GetManifestId(id);
            ManifestProp prop = manifest.props.SingleOrDefault(candidate =>
                string.Equals(candidate.id, manifestId, StringComparison.Ordinal));
            Assert.That(prop, Is.Not.Null, $"The manifest has no '{manifestId}'.");
            return prop;
        }

        private static bool IsSmallProp(CityPedestrianHandPropId id)
        {
            return id == CityPedestrianHandPropId.Cigarette ||
                   id == CityPedestrianHandPropId.CafeCigarette ||
                   id == CityPedestrianHandPropId.Chalk ||
                   id == CityPedestrianHandPropId.SmokingPipe;
        }

        /// <summary>
        /// A body that has the prop's socket: the fisherman's staged
        /// prefab for the rod and the pipe (his own props), the
        /// babushka from Resources for everything else — every design
        /// shares one skeleton, so her left grip serves the towel and
        /// her right grip the pot exactly as the cafe attendant's do.
        /// </summary>
        private static CityPedestrianAssetRegistry InstantiateBodyFor(
            CityPedestrianHandPropId id,
            Transform parent)
        {
            GameObject prefab;
            if (id == CityPedestrianHandPropId.FishingRod ||
                id == CityPedestrianHandPropId.SmokingPipe)
            {
                SeacoastFishermanProvider provider = SeacoastFishermanProvider.Load();
                Assert.That(provider, Is.Not.Null, "The fisherman provider is missing.");
                prefab = provider.StagedPrefab;
            }
            else
            {
                prefab = Resources.Load<GameObject>(
                    CityPedestrianResources.BabushkaPrefabResourcePath);
            }

            Assert.That(prefab, Is.Not.Null, $"No body prefab for {id}.");
            GameObject instance = Object.Instantiate(prefab, parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            var registry = instance.GetComponentInChildren<CityPedestrianAssetRegistry>(true);
            Assert.That(registry, Is.Not.Null, prefab.name);
            Assert.That(registry.ModelRoot, Is.Not.Null, prefab.name);
            Assert.That(registry.Renderers, Is.Not.Empty, prefab.name);
            return registry;
        }

        /// <summary>World AABB of every part from its mesh bounds through
        /// its transform. The prop meshes import non-readable; for a rigid
        /// part at the bind pose the box corners are the vertex AABB.</summary>
        private static void MeasureWorldBounds(
            CityPedestrianHandPropRegistry registry,
            out Vector3 min,
            out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            foreach (Renderer renderer in registry.Renderers)
            {
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Assert.That(mesh, Is.Not.Null, "A prop part has no mesh.");
                Bounds local = mesh.bounds;
                Matrix4x4 localToWorld = renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = localToWorld.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z));
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    any = true;
                }
            }

            Assert.That(any, Is.True, "The prop has no parts.");
        }

        private static Vector3 MeasureMeshCentre(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null, renderer.name);
            Assert.That(filter.sharedMesh, Is.Not.Null, renderer.name);
            return renderer.localToWorldMatrix.MultiplyPoint3x4(filter.sharedMesh.bounds.center);
        }

        private static float FarthestCorner(Vector3 min, Vector3 max, Vector3 origin)
        {
            float farthest = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                farthest = Mathf.Max(farthest, Vector3.Distance(point, origin));
            }

            return farthest;
        }

        /// <summary>Extents sorted ascending: the Blender-to-Unity axis
        /// conversion permutes and flips axes, and a sorted triple is
        /// invariant under both.</summary>
        private static float[] SortedExtents(float[] min, float[] max)
        {
            return SortedExtents(ToVector(min), ToVector(max));
        }

        private static float[] SortedExtents(Vector3 min, Vector3 max)
        {
            float[] extents = { max.x - min.x, max.y - min.y, max.z - min.z };
            Array.Sort(extents);
            return extents;
        }

        private static float ColorDistance(Color a, Color b)
        {
            return Mathf.Max(
                Mathf.Abs(a.r - b.r),
                Mathf.Abs(a.g - b.g),
                Mathf.Abs(a.b - b.b),
                Mathf.Abs(a.a - b.a));
        }

        private static void AssertColorNear(Color actual, Color expected, string message)
        {
            Assert.That(
                ColorDistance(actual, expected),
                Is.LessThanOrEqualTo(ColorTolerance),
                $"{message}: expected {expected}, was {actual}.");
        }

        private static Vector3 ToVector(float[] values)
        {
            Assert.That(values, Is.Not.Null);
            Assert.That(values, Has.Length.EqualTo(3));
            return new Vector3(values[0], values[1], values[2]);
        }
    }
}
