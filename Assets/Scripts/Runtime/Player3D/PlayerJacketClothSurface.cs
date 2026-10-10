using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns a deformable mesh; imported geometry, materials and skin weights stay shared and immutable.</summary>
    internal sealed class PlayerJacketClothSurface : IDisposable
    {
        private readonly Matrix4x4[] bindposes;
        private readonly Matrix4x4[] matrices;
        private readonly Transform[] bones;
        private readonly BoneWeight[] skinWeights;
        private readonly int[] vertexSkins;
        private readonly Matrix4x4[] skins;
        private readonly Matrix4x4[] inverseSkins;
        private readonly bool[] deformSkins;
        private readonly Vector3[] original;
        private readonly Vector3[] sourceNormals;
        private readonly Vector3[] output;
        private readonly Vector3[] world;
        private readonly Vector3[] foldDelta;
        private readonly Vector3[] posedOriginal;
        private readonly float[] contactFreedom;
        private readonly int[] contactVertices;
        private readonly int[] contactTriangles;
        private readonly Vector3[] freeSurfaceOriginal, freeSurfaceCandidate, freeSurfaceBest;
        private readonly int[] freeSurfaceLayers;
        private readonly float[] freeSurfaceDepths;
        private readonly PanelShape panelShape;
        private readonly Transform shoulder, elbow, wrist;
        private readonly int foldShape;
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly bool normalMapped;
        private readonly PlayerJacketCloth.SurfaceBinding binding;
        public Mesh Mesh { get; }
        public Mesh Source => binding.Source;
        public SkinnedMeshRenderer Renderer => binding.Renderer;
        internal uint GeometryVersion { get; private set; }
        public int VertexCount => original.Length;
        internal bool IsHemPanel => panelShape != null && panelShape.HasPins;
        public Vector3 Original(int index) => original[index];
        public Vector3 World(int index) => world[index];
        public Vector3 WorldNormal(int index) => Skin(index).inverse.transpose.MultiplyVector(normals[index]).normalized;
        public Vector3 BindWorldNormal(int index) => Skin(index).inverse.transpose.MultiplyVector(sourceNormals[index]).normalized;

        public PlayerJacketClothSurface(PlayerJacketCloth.SurfaceBinding source,
            Transform shoulder = null, Transform elbow = null, Transform wrist = null, Transform actor = null)
        {
            binding = source;
            this.shoulder = shoulder; this.elbow = elbow; this.wrist = wrist;
            Material material = source.Renderer.sharedMaterial;
            normalMapped = material != null && material.IsKeywordEnabled("_NORMALMAP");
            original = source.Source.vertices;
            sourceNormals = source.Source.normals;
            posedOriginal = (Vector3[])original.Clone();
            foldShape = CharacterJointDeformation.FindShape(source.Source, source.Region == 1 ? "JacketElbowFold.L" :
                source.Region == 2 ? "JacketElbowFold.R" : "");
            if (foldShape >= 0)
            {
                if (shoulder == null || elbow == null || wrist == null)
                    throw new InvalidOperationException("An authored sleeve correction requires all three arm anchors.");
                foldDelta = new Vector3[original.Length];
                int lastFrame = source.Source.GetBlendShapeFrameCount(foldShape) - 1;
                source.Source.GetBlendShapeFrameVertices(foldShape, lastFrame, foldDelta, null, null);
            }
            output = (Vector3[])original.Clone();
            contactFreedom = (float[])source.Freedom.Clone();
            if (foldDelta != null)
                for (int i = 0; i < contactFreedom.Length; i++)
                    if (foldDelta[i].sqrMagnitude > 1e-14f) contactFreedom[i] = 1f;
            world = new Vector3[original.Length];
            // Identical authored weights share exactly the same posed skin matrix.
            // Cache their inverse too: every free vertex used to rebuild and invert it.
            BoneWeight[] weights = source.Source.boneWeights;
            vertexSkins = new int[weights.Length];
            var weightIndices = new Dictionary<BoneWeight, int>(ExactWeights.Instance);
            var uniqueWeights = new List<BoneWeight>();
            for (int i = 0; i < weights.Length; i++)
            {
                if (!weightIndices.TryGetValue(weights[i], out int skin))
                {
                    skin = uniqueWeights.Count;
                    weightIndices.Add(weights[i], skin);
                    uniqueWeights.Add(weights[i]);
                }
                vertexSkins[i] = skin;
            }
            skinWeights = uniqueWeights.ToArray();
            if (source.Region == 0)
            {
                // Imported UV splits share one physical cloth point. Cache
                // that correspondence once, so finite-area contacts cannot
                // pull the corners of adjacent UV charts apart.
                contactVertices = new int[original.Length];
                var coincident = new Dictionary<(int, Vector3), int>();
                for (int i = 0; i < original.Length; i++)
                {
                    var key = (vertexSkins[i], original[i]);
                    if (!coincident.TryGetValue(key, out int vertex)) coincident.Add(key, vertex = i);
                    contactVertices[i] = vertex;
                }
                var triangles = new List<int>();
                int[] sourceTriangles = source.Source.triangles;
                for (int i = 0; i < sourceTriangles.Length; i += 3)
                {
                    int a = contactVertices[sourceTriangles[i]], b = contactVertices[sourceTriangles[i + 1]],
                        c = contactVertices[sourceTriangles[i + 2]];
                    if (a == b || b == c || c == a ||
                        (contactFreedom[a] <= 0f && contactFreedom[b] <= 0f && contactFreedom[c] <= 0f)) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                }
                contactTriangles = triangles.ToArray();
                bool fullyFree = original.Length > 0;
                foreach (float freedom in contactFreedom) fullyFree &= freedom > 0f;
                if (fullyFree)
                {
                    freeSurfaceOriginal = new Vector3[original.Length];
                    freeSurfaceCandidate = new Vector3[original.Length];
                    freeSurfaceBest = new Vector3[original.Length];
                    freeSurfaceDepths = new float[original.Length];
                    freeSurfaceLayers = new int[original.Length];
                    // The thin constructed panels extrude along bind Z.
                    // Move their front/back pair together to keep thickness,
                    // including UV-split copies of either layer.
                    var layers = new Dictionary<(int, Vector2), int>();
                    for (int i = 0; i < original.Length; i++)
                    {
                        var key = (vertexSkins[i], new Vector2(original[i].x, original[i].y));
                        if (!layers.TryGetValue(key, out int vertex)) layers.Add(key, vertex = i);
                        freeSurfaceLayers[i] = vertex;
                    }
                }
            }
            skins = new Matrix4x4[skinWeights.Length];
            inverseSkins = new Matrix4x4[skinWeights.Length];
            deformSkins = new bool[skinWeights.Length];
            for (int i = 0; i < weights.Length; i++)
                if (contactFreedom[i] > 0f) deformSkins[vertexSkins[i]] = true;
            bindposes = source.Source.bindposes;
            bones = source.Renderer.bones;
            matrices = new Matrix4x4[bindposes.Length];
            Mesh = UnityEngine.Object.Instantiate(source.Source);
            Mesh.name = source.Source.name + " Jacket Motion";
            Mesh.hideFlags = HideFlags.HideAndDontSave;
            Mesh.MarkDynamic();
            source.Renderer.sharedMesh = Mesh;
            Mesh.GetNormals(normals);
            UpdatePose();
            if (source.Region == 0 && actor != null)
                panelShape = new PanelShape(world, original, source.Renderer.localToWorldMatrix, actor,
                    contactFreedom, contactVertices, contactTriangles, vertexSkins);
        }

        public void UpdatePose()
        {
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            for (int i = 0; i < skins.Length; i++)
            {
                skins[i] = BuildSkin(skinWeights[i]);
                if (deformSkins[i]) inverseSkins[i] = skins[i].inverse;
            }
            float fold = 0f;
            if (foldDelta != null)
            {
                float angle = Vector3.Angle(elbow.position - shoulder.position, wrist.position - elbow.position);
                float minimumCosine = Mathf.Cos(135f * Mathf.Deg2Rad * .5f);
                float cosine = Mathf.Max(minimumCosine, Mathf.Cos(angle * Mathf.Deg2Rad * .5f));
                fold = Mathf.Clamp01((1f / cosine - 1f) / (1f / minimumCosine - 1f));
                // The composed vertices already contain this shape; renderer skinning must not add it twice.
                Renderer.SetBlendShapeWeight(foldShape, 0f);
            }
            for (int i = 0; i < original.Length; i++)
            {
                posedOriginal[i] = original[i] + (foldDelta == null ? Vector3.zero : foldDelta[i] * fold);
                world[i] = Skin(i).MultiplyPoint3x4(posedOriginal[i]);
            }
            panelShape?.CapturePose(world);
        }

        public Matrix4x4 Skin(int vertex) => skins[vertexSkins[vertex]];

        private Matrix4x4 BuildSkin(BoneWeight weight)
        {
            Matrix4x4 result = Scale(matrices[weight.boneIndex0], weight.weight0);
            Add(ref result, weight.boneIndex1, weight.weight1);
            Add(ref result, weight.boneIndex2, weight.weight2);
            Add(ref result, weight.boneIndex3, weight.weight3);
            return result;
        }

        private void Add(ref Matrix4x4 result, int bone, float weight)
        {
            if (weight <= 0f) return;
            Matrix4x4 value = matrices[bone];
            for (int i = 0; i < 4; i++) result.SetColumn(i, result.GetColumn(i) + value.GetColumn(i) * weight);
        }

        private static Matrix4x4 Scale(Matrix4x4 value, float weight)
        {
            for (int i = 0; i < 4; i++) value.SetColumn(i, value.GetColumn(i) * weight);
            return value;
        }

        public int Deform(Vector3[] displacements, PlayerScarfBodyContacts contacts)
        {
            for (int i = 0; i < world.Length; i++)
            {
                float freedom = binding.Freedom[i];
                if (freedom <= 0f) continue;
                world[i] += Vector3.Lerp(displacements[binding.FirstNode[i]], displacements[binding.SecondNode[i]],
                    binding.SecondWeight[i]) * freedom;
            }
            if (freeSurfaceOriginal != null) Array.Copy(world, freeSurfaceOriginal, world.Length);
            panelShape?.CaptureRaw(world);
            contacts.Resolve(world, contactFreedom);
            int contactCount = contacts.LastContactCount;
            contactCount += ResolvePanelContacts(contacts);
            if (freeSurfaceOriginal != null && contacts.ResolveFreeSurface(freeSurfaceOriginal, world, contactTriangles,
                freeSurfaceLayers, freeSurfaceDepths, freeSurfaceCandidate, freeSurfaceBest)) contactCount += world.Length;
            for (int i = 0; i < output.Length; i++)
                output[i] = contactFreedom[i] <= 0f ? posedOriginal[i] : inverseSkins[vertexSkins[i]].MultiplyPoint3x4(world[i]);
            Write();
            return contactCount;
        }

        internal bool TryHemFold(PlayerScarfBodyContacts contacts, Transform actor,
            PlayerJacketClothSurface[] panels, out HemFoldSet folds)
        {
            folds = default;
            if (panelShape == null || !panelShape.HasPins || !panelShape.Stretched(world) ||
                !contacts.TryHemObstacle(panelShape.Raw, contactFreedom, actor, out Bounds obstacle, out float side)) return false;
            // The lifted row sits over the raised thigh; lower rows retain
            // the hanging hem. A little lateral room chooses one coherent
            // escape side instead of adjacent corners passing opposite ways.
            // A horizontal thigh can obstruct a lower row while the upper
            // row already clears it. Try a small extra outward reserve before
            // lifting clear upper fabric; both candidates obey the same gates.
            bool hasOther = contacts.TryHemObstacle(panelShape.Raw, contactFreedom, actor, out Bounds other,
                out float otherSide, -side);
            for (int motion = 0; motion < 2; motion++)
            {
                for (int candidate = 0; candidate < 2; candidate++)
                {
                    folds = new HemFoldSet(Create(obstacle, side, candidate, motion == 0 ? 1f : 0f));
                    if (Accept(folds)) return true;
                }
                // Both thighs can obstruct the hem in a crouch. Compose the
                // disjoint halves from the same raw fabric state before
                // testing; sequential application would restore one half.
                if (hasOther)
                    for (int candidate = 0; candidate < 2; candidate++)
                    {
                        float secondary = motion == 0 ? 1f : 0f;
                        folds = new HemFoldSet(Create(obstacle, side, candidate, secondary),
                            Create(other, otherSide, candidate, secondary));
                        if (Accept(folds)) return true;
                    }
                // Secondary hem motion can stretch an otherwise feasible
                // deep fold. Retry from the skinned fabric pose only after
                // the full-motion one- and two-thigh candidates fail.
            }
            return false;

            bool Accept(HemFoldSet candidate)
            {
                foreach (PlayerJacketClothSurface panel in panels)
                    if (!panel.AcceptsHemFold(candidate, contacts, actor)) return false;
                return true;
            }

            HemFold Create(Bounds bounds, float half, int reserve, float secondary)
            {
                float rise = Mathf.Ceil(Mathf.Max(0f, bounds.max.y - panelShape.Peak) * .8f / .02f) * .02f;
                float outside = half < 0f ? bounds.min.x - panelShape.HalfWidth * .195f :
                    bounds.max.x + panelShape.HalfWidth * .195f;
                return new HemFold(half, outside + half * reserve * .02f, rise, panelShape.Start,
                    panelShape.Middle, panelShape.Peak, panelShape.Pin, secondary);
            }
        }

        private bool AcceptsHemFold(HemFoldSet folds, PlayerScarfBodyContacts contacts, Transform actor)
        {
            if (panelShape == null) return true;
            panelShape.Apply(folds, world, actor);
            return panelShape.Clear(contacts) && !panelShape.Stretched(panelShape.Candidate);
        }

        internal bool ApplyHemFold(HemFoldSet folds, PlayerScarfBodyContacts contacts, Transform actor)
        {
            if (panelShape == null) return false;
            panelShape.Apply(folds, world, actor);
            if (!panelShape.Clear(contacts) || panelShape.Stretched(panelShape.Candidate)) return false;
            Array.Copy(panelShape.Candidate, world, world.Length);
            for (int i = 0; i < output.Length; i++)
                output[i] = contactFreedom[i] <= 0f ? posedOriginal[i] : inverseSkins[vertexSkins[i]].MultiplyPoint3x4(world[i]);
            Write();
            return true;
        }

        internal readonly struct HemFold
        {
            public readonly float Side, Lateral, Lift, Start, Middle, Peak, Pin, SecondaryMotion;
            public HemFold(float side, float lateral, float lift, float start, float middle, float peak, float pin,
                float secondaryMotion = 1f)
            { Side = side; Lateral = lateral; Lift = lift; Start = start; Middle = middle; Peak = peak; Pin = pin;
                SecondaryMotion = secondaryMotion; }
        }

        internal readonly struct HemFoldSet
        {
            public readonly HemFold First, Second;
            public HemFoldSet(HemFold first, HemFold second = default) { First = first; Second = second; }
        }

        // Cached cloth particles join UV splits and the two extrusion layers.
        // This is a contact fallback for overstretched panels, rather than a
        // second per-frame mesh/physics simulation.
        private sealed class PanelShape
        {
            public readonly Vector3[] Raw, Candidate;
            public readonly bool HasPins;
            public readonly float HalfWidth, Start, Middle, Peak, Pin;
            private readonly Vector3[] bind, pose;
            private readonly int[] canonical, triangles, layers;
            private readonly (int A, int B)[] edges;
            private readonly bool[] pinned;
            private readonly float[] freedom;

            public PanelShape(Vector3[] source, Vector3[] authored, Matrix4x4 authoredToWorld, Transform actor,
                float[] freedom, int[] canonical, int[] triangles, int[] skins)
            {
                this.freedom = freedom; this.canonical = canonical; this.triangles = triangles;
                int count = source.Length;
                Raw = new Vector3[count]; Candidate = new Vector3[count]; pose = (Vector3[])source.Clone();
                bind = new Vector3[count]; layers = new int[count]; pinned = new bool[count];
                var rows = new List<float>();
                float peak = float.NegativeInfinity, pin = float.PositiveInfinity, width = 0f;
                for (int i = 0; i < count; i++)
                {
                    bind[i] = actor.InverseTransformPoint(authoredToWorld.MultiplyPoint3x4(authored[i]));
                    layers[i] = canonical[i];
                    pinned[i] = freedom[i] < 1e-7f;
                    HasPins |= pinned[i];
                    if (pinned[i]) continue;
                    width = Mathf.Max(width, Mathf.Abs(bind[i].x));
                    peak = Mathf.Max(peak, bind[i].y);
                    bool seen = false;
                    foreach (float row in rows) if (Mathf.Abs(row - bind[i].y) < .0001f) { seen = true; break; }
                    if (!seen) rows.Add(bind[i].y);
                }
                rows.Sort(); HalfWidth = width; Peak = peak;
                Start = rows.Count > 1 ? rows[1] : peak;
                Middle = rows.Count > 2 ? rows[rows.Count - 2] : peak;
                for (int i = 0; i < count; i++)
                    if (pinned[i] && bind[i].y > peak) pin = Mathf.Min(pin, bind[i].y);
                Pin = pin;
                // Mutual nearest matching rest-weight points are the authored
                // 4 mm inner/outer wall. Circumferential neighbors are much
                // farther away. Pair once; never search geometry at runtime.
                var nearest = new int[count];
                for (int i = 0; i < count; i++)
                {
                    nearest[i] = -1;
                    if (canonical[i] != i) continue;
                    float distance = .006f * .006f;
                    for (int j = 0; j < count; j++)
                    {
                        if (j == i || canonical[j] != j || skins[j] != skins[i]) continue;
                        float square = (bind[j] - bind[i]).sqrMagnitude;
                        if (square < distance) { distance = square; nearest[i] = j; }
                    }
                }
                for (int i = 0; i < count; i++)
                    if (canonical[i] == i && nearest[i] >= 0 && nearest[nearest[i]] == i)
                        layers[i] = Mathf.Min(i, nearest[i]);
                var sums = new Vector3[count]; var sizes = new int[count];
                for (int i = 0; i < count; i++)
                {
                    if (canonical[i] != i) continue;
                    int layer = layers[i]; sums[layer] += bind[i]; sizes[layer]++;
                    pinned[layer] |= pinned[i];
                }
                for (int i = 0; i < count; i++)
                {
                    layers[i] = layers[canonical[i]];
                    int layer = layers[i]; bind[i] = sums[layer] / sizes[layer]; pinned[i] = pinned[layer];
                }
                var unique = new HashSet<(int, int)>();
                for (int i = 0; i < triangles.Length; i += 3)
                { Add(triangles[i], triangles[i + 1]); Add(triangles[i + 1], triangles[i + 2]); Add(triangles[i + 2], triangles[i]); }
                edges = new (int, int)[unique.Count]; unique.CopyTo(edges);

                void Add(int a, int b) { unique.Add(a < b ? (a, b) : (b, a)); }
            }

            public void CapturePose(Vector3[] source) => Array.Copy(source, pose, source.Length);
            public void CaptureRaw(Vector3[] source) => Array.Copy(source, Raw, source.Length);
            public bool Stretched(Vector3[] source)
            {
                foreach ((int a, int b) in edges)
                {
                    if (freedom[a] < 1e-7f && freedom[b] < 1e-7f) continue;
                    float rest = (pose[b] - pose[a]).sqrMagnitude;
                    if (rest >= .015f * .015f && (source[b] - source[a]).sqrMagnitude >= rest * 4f) return true;
                }
                return false;
            }

            public void Apply(HemFoldSet folds, Vector3[] solved, Transform actor)
            {
                Array.Copy(solved, Candidate, solved.Length);
                for (int i = 0; i < Candidate.Length; i++)
                {
                    Vector3 point = bind[i];
                    if (pinned[i]) continue;
                    HemFold fold = point.x * folds.First.Side > 0f ? folds.First : folds.Second;
                    if (point.x * fold.Side <= 0f) continue;
                    float front = Mathf.Clamp01((point.z + .02f) / .20f);
                    if (front <= 0f) continue;
                    float rise = point.y < fold.Middle ? .63f * Mathf.InverseLerp(fold.Start, fold.Middle, point.y) :
                        point.y < fold.Peak ? Mathf.Lerp(.63f, 1f, Mathf.InverseLerp(fold.Middle, fold.Peak, point.y)) :
                        1f - Mathf.InverseLerp(fold.Peak, fold.Pin, point.y);
                    float lateral = fold.Side < 0f ? Mathf.Min(point.x, fold.Lateral) : Mathf.Max(point.x, fold.Lateral);
                    Candidate[i] = Vector3.LerpUnclamped(pose[i], Raw[i], fold.SecondaryMotion) +
                        actor.TransformVector(new Vector3((lateral - point.x) * front,
                        fold.Lift * rise * front, 0f));
                }
            }

            public bool Clear(PlayerScarfBodyContacts contacts)
            {
                for (int i = 0; i < Candidate.Length; i++)
                    if (canonical[i] == i && freedom[i] >= 1e-7f && !contacts.OutsideSourceSupport(Candidate[i])) return false;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (freedom[a] < 1e-7f && freedom[b] < 1e-7f && freedom[c] < 1e-7f) continue;
                    Vector3 x = Candidate[a], y = Candidate[b], z = Candidate[c];
                    if (!contacts.OutsideSourceSupport((x + y + z) / 3f) || !contacts.OutsideSourceSupport((x + y) * .5f) ||
                        !contacts.OutsideSourceSupport((y + z) * .5f) || !contacts.OutsideSourceSupport((z + x) * .5f)) return false;
                }
                return true;
            }
        }

        private int ResolvePanelContacts(PlayerScarfBodyContacts contacts)
        {
            if (contactTriangles == null || contactTriangles.Length == 0) return 0;
            int contactCount = 0;
            // A clear vertex does not imply a clear cloth triangle: a raised
            // thigh can pass through an edge or the middle of a broad panel.
            // Bounded area constraints reuse the posed support envelopes;
            // there is no live body baking or collision-mesh allocation.
            for (int pass = 0; pass < 4; pass++)
            {
                int corrected = 0;
                for (int triangle = 0; triangle < contactTriangles.Length; triangle += 3)
                {
                    int a = contactTriangles[triangle], b = contactTriangles[triangle + 1], c = contactTriangles[triangle + 2];
                    corrected += Distribute(a, b, c, new Vector3(1f / 3f, 1f / 3f, 1f / 3f));
                    corrected += Distribute(a, b, c, new Vector3(.5f, .5f, 0f));
                    corrected += Distribute(a, b, c, new Vector3(0f, .5f, .5f));
                    corrected += Distribute(a, b, c, new Vector3(.5f, 0f, .5f));
                }
                if (corrected == 0) break;
                contactCount += corrected;
                for (int i = 0; i < world.Length; i++)
                    world[i] = world[contactVertices[i]];
                // Area corrections also keep every free corner outside.
                contacts.Resolve(world, contactFreedom);
                contactCount += contacts.LastContactCount;
            }
            return contactCount;

            int Distribute(int a, int b, int c, Vector3 weights)
            {
                Vector3 point = world[a] * weights.x + world[b] * weights.y + world[c] * weights.z;
                Vector3 resolved = point;
                if (!contacts.ResolvePoint(ref resolved)) return 0;
                float denominator = contactFreedom[a] * weights.x * weights.x +
                    contactFreedom[b] * weights.y * weights.y + contactFreedom[c] * weights.z * weights.z;
                if (denominator <= 1e-12f) return 0;
                Vector3 correction = (resolved - point) / denominator;
                Add(a, weights.x); Add(b, weights.y); Add(c, weights.z);
                return 1;

                void Add(int vertex, float amount)
                {
                    if (amount <= 0f || contactFreedom[vertex] <= 0f) return;
                    world[vertex] += correction * (contactFreedom[vertex] * amount);
                }
            }
        }

        // Partition boundaries are one surface even though wardrobe keeps their renderers addressable.
        public void SetWorldNormal(int index, Vector3 normal)
        {
            Vector3 local = Skin(index).transpose.MultiplyVector(normal).normalized;
            normals[index] = local;
            if (tangents.Count == original.Length)
            {
                Vector4 tangent = tangents[index];
                Vector3 direction = Vector3.ProjectOnPlane(new Vector3(tangent.x, tangent.y, tangent.z), local).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
        }

        public void CommitSharedNormals()
        {
            Mesh.SetNormals(normals);
            if (tangents.Count == original.Length) Mesh.SetTangents(tangents);
        }

        private sealed class ExactWeights : IEqualityComparer<BoneWeight>
        {
            internal static readonly ExactWeights Instance = new ExactWeights();
            public bool Equals(BoneWeight a, BoneWeight b) =>
                a.boneIndex0 == b.boneIndex0 && a.boneIndex1 == b.boneIndex1 &&
                a.boneIndex2 == b.boneIndex2 && a.boneIndex3 == b.boneIndex3 &&
                a.weight0.Equals(b.weight0) && a.weight1.Equals(b.weight1) &&
                a.weight2.Equals(b.weight2) && a.weight3.Equals(b.weight3);

            public int GetHashCode(BoneWeight weight)
            {
                unchecked
                {
                    int hash = weight.boneIndex0;
                    hash = hash * 397 ^ weight.boneIndex1;
                    hash = hash * 397 ^ weight.boneIndex2;
                    hash = hash * 397 ^ weight.boneIndex3;
                    hash = hash * 397 ^ weight.weight0.GetHashCode();
                    hash = hash * 397 ^ weight.weight1.GetHashCode();
                    hash = hash * 397 ^ weight.weight2.GetHashCode();
                    return hash * 397 ^ weight.weight3.GetHashCode();
                }
            }
        }

        public void Restore()
        {
            Array.Copy(original, output, original.Length);
            Array.Copy(original, posedOriginal, original.Length);
            if (foldShape >= 0) Renderer.SetBlendShapeWeight(foldShape, 0f);
            Write();
        }

        public void CopyTo(PlayerJacketClothSurface target)
        {
            if (target == null || target.output.Length != output.Length) return;
            // A camera-local arm may have its own elbow pose. Copy only secondary motion;
            // derive its authored sleeve correction from that instance's actual bones.
            target.UpdatePose();
            for (int i = 0; i < output.Length; i++)
                target.output[i] = target.posedOriginal[i] + output[i] - posedOriginal[i];
            if (target.foldShape >= 0) target.Renderer.SetBlendShapeWeight(target.foldShape, 0f);
            target.Write();
        }

        private void Write()
        {
            Mesh.vertices = output;
            Mesh.RecalculateNormals();
            // Keep the lighting basis attached to the deformed cuff/hem, also
            // when these vertices are copied into the mirror or first person.
            if (normalMapped) Mesh.RecalculateTangents();
            Mesh.GetNormals(normals);
            if (normalMapped) Mesh.GetTangents(tangents);
            Mesh.RecalculateBounds();
            // Consumers can reuse this geometry between writes. A manual
            // ApplyAt or a passive copy can write several poses in one frame.
            unchecked { GeometryVersion++; }
            Renderer.sharedMesh = Mesh;
            // Skinned bounds are renderer-local; retain the authored animated
            // bounds and add the maximum free-cloth excursion on every axis.
            Bounds bounds = binding.AuthoredBounds;
            Vector3 scale = Renderer.transform.lossyScale;
            bounds.Expand(new Vector3(.18f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                .18f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), .18f / Mathf.Max(.0001f, Mathf.Abs(scale.z))));
            Renderer.localBounds = bounds;
        }

        public void Dispose()
        {
            if (Renderer != null && Renderer.sharedMesh == Mesh)
            { Renderer.sharedMesh = Source; Renderer.localBounds = binding.AuthoredBounds; }
            PlayerScarfResources.DestroyOwned(Mesh);
        }
    }
}
