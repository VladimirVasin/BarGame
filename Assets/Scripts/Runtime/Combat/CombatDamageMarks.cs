using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Bounded authored overlays retain wounds on the original combat/ragdoll skin.</summary>
    public sealed class CombatDamageMarks
    {
        public const int MaximumProjectileWounds = 64;
        internal const float ProjectileWoundDrySeconds = 24f;
        private const int HolesPerLayer = 8;
        private static Material sharedProjectileMaterial;
        private static readonly int BulletHolesId = Shader.PropertyToID("_BulletHoles");
        private static readonly int BulletShapesId = Shader.PropertyToID("_BulletShapes");
        private static readonly int BulletStatesId = Shader.PropertyToID("_BulletStates");
        private static readonly int BulletHoleCountId = Shader.PropertyToID("_BulletHoleCount");

        internal enum ProjectileWoundSurface { Skin, Fabric, Glove }

        internal readonly struct ProjectileWoundPresentation
        {
            public ProjectileWoundSurface Surface { get; }
            public Vector2 DirectionUv { get; }
            public float Stretch { get; }
            public float AgeSeconds { get; }
            public float Wetness { get; }
            public float Spread { get; }
            public int HitCount { get; }

            internal ProjectileWoundPresentation(ProjectileWoundSurface surface, Vector2 directionUv,
                float stretch, float ageSeconds, float wetness, float spread, int hitCount)
            {
                Surface = surface; DirectionUv = directionUv; Stretch = stretch; AgeSeconds = ageSeconds;
                Wetness = wetness; Spread = spread; HitCount = hitCount;
            }
        }
        private sealed class BlendShape
        {
            public int SourceIndex;
            public float Weight = float.NaN;
            public float[] Frames;
            public Vector3[][] Vertices, Normals;
        }
        private sealed class Patch
        {
            public SkinnedMeshRenderer Renderer, Source;
            public int Centre;
            public Matrix4x4[] BindPoses;
            public Transform[] Bones;
            public Vector3[] Vertices, Normals, SkinnedVertices;
            public Bounds SkinnedBounds;
            public Vector3[] DeformedVertices, DeformedNormals;
            public BlendShape[] BlendShapes = Array.Empty<BlendShape>();
            public Vector2[] Uv;
            public BoneWeight[] Weights;
            public int[] Triangles;
            public bool Active;
            public int Grade;
            public BodyDamageRegion Region;
            public int BodyPatch = -1;
            public bool Classified;
            public ProjectileWound Projection;
            private Matrix4x4[] posedBones;
            private int[] usedBones;
            private bool poseCaptured, skinnedVerticesReady;

            public Vector3 Position { get { RefreshBlendShapes(); return SkinVertex(Centre, false); } }
            public Vector3 Direction { get { RefreshBlendShapes(); return SkinVertex(Centre, true).normalized; } }
            public Vector3 SkinVertex(int index, bool normal) => Skin(DeformedVertices[index], DeformedNormals[index], Weights[index], normal);

            public void InitializeSkinning()
            {
                posedBones = new Matrix4x4[Bones.Length];
                var used = new SortedSet<int>();
                foreach (BoneWeight weight in Weights)
                {
                    if (weight.weight0 > 0f) used.Add(weight.boneIndex0);
                    if (weight.weight1 > 0f) used.Add(weight.boneIndex1);
                    if (weight.weight2 > 0f) used.Add(weight.boneIndex2);
                    if (weight.weight3 > 0f) used.Add(weight.boneIndex3);
                }
                usedBones = new int[used.Count]; used.CopyTo(usedBones);
            }

            public void CapturePose(Dictionary<Transform, Matrix4x4> worldBones)
            {
                foreach (int index in usedBones)
                {
                    Transform bone = Bones[index];
                    if (!worldBones.TryGetValue(bone, out Matrix4x4 world))
                    { world = bone.localToWorldMatrix; worldBones.Add(bone, world); }
                    Matrix4x4 matrix = world * BindPoses[index];
                    if (!posedBones[index].Equals(matrix)) skinnedVerticesReady = false;
                    posedBones[index] = matrix;
                }
                poseCaptured = true;
            }

            public void ReleasePose() => poseCaptured = false;

            public void RefreshSkinnedVertices()
            {
                if (skinnedVerticesReady) return;
                for (int i = 0; i < Vertices.Length; i++)
                {
                    Vector3 point = SkinnedVertices[i] = SkinVertex(i, false);
                    if (i == 0) SkinnedBounds = new Bounds(point, Vector3.zero);
                    else SkinnedBounds.Encapsulate(point);
                }
                skinnedVerticesReady = true;
            }

            public void RefreshBlendShapes()
            {
                if (Source == null || Renderer == null) return;
                bool changed = false;
                foreach (BlendShape shape in BlendShapes)
                {
                    float weight = Source.GetBlendShapeWeight(shape.SourceIndex);
                    if (weight == shape.Weight) continue;
                    shape.Weight = weight; changed = true;
                }
                if (!changed) return;
                skinnedVerticesReady = false;
                Array.Copy(Vertices, DeformedVertices, Vertices.Length);
                Array.Copy(Normals, DeformedNormals, Normals.Length);
                foreach (BlendShape shape in BlendShapes)
                {
                    if (Mathf.Abs(shape.Weight) < .00001f) continue;
                    int high = 0;
                    while (high < shape.Frames.Length - 1 && shape.Weight > shape.Frames[high]) high++;
                    int low = high - 1;
                    float lowerWeight = low < 0 ? 0f : shape.Frames[low];
                    float fraction = (shape.Weight - lowerWeight) / Mathf.Max(.0001f, shape.Frames[high] - lowerWeight);
                    for (int vertex = 0; vertex < Vertices.Length; vertex++)
                    {
                        Vector3 lowerVertex = low < 0 ? Vector3.zero : shape.Vertices[low][vertex];
                        Vector3 lowerNormal = low < 0 ? Vector3.zero : shape.Normals[low][vertex];
                        DeformedVertices[vertex] += Vector3.LerpUnclamped(lowerVertex, shape.Vertices[high][vertex], fraction);
                        DeformedNormals[vertex] += Vector3.LerpUnclamped(lowerNormal, shape.Normals[high][vertex], fraction);
                    }
                }
                SyncBlendShapeWeights(Renderer);
            }

            public void SyncBlendShapeWeights(SkinnedMeshRenderer target)
            {
                if (Source == null || target == null) return;
                for (int shape = 0; shape < BlendShapes.Length; shape++)
                    target.SetBlendShapeWeight(shape, Source.GetBlendShapeWeight(BlendShapes[shape].SourceIndex));
            }
            private Vector3 Skin(Vector3 vertex, Vector3 vertexNormal, BoneWeight weights, bool normal)
            {
                Vector3 result = Vector3.zero;
                Add(weights.boneIndex0, weights.weight0); Add(weights.boneIndex1, weights.weight1);
                Add(weights.boneIndex2, weights.weight2); Add(weights.boneIndex3, weights.weight3);
                return result;
                void Add(int index, float weight)
                {
                    if (weight <= 0f) return;
                    Matrix4x4 matrix = poseCaptured ? posedBones[index] : Bones[index].localToWorldMatrix * BindPoses[index];
                    result += (normal ? matrix.MultiplyVector(vertexNormal) : matrix.MultiplyPoint3x4(vertex)) * weight;
                }
            }
        }

        private sealed class ProjectileWound
        {
            public Patch Patch;
            public int A, B, C;
            public Vector3 Barycentric;
            public Vector2 Uv;
            public Vector2 DirectionUv;
            public float Radius, Stretch, Seed, AgeSeconds, Spread = 1f;
            public float RayDistance = float.PositiveInfinity;
            public int HitCount = 1, Slot;
            public BodyDamageRegion Region;
            public int BodyPatch = -1;
            public bool Retained = true;
            public ProjectileWoundSurface Surface;
            public ProjectileLayer Layer;
            public float Wetness => Mathf.Pow(Mathf.Clamp01(1f - AgeSeconds / ProjectileWoundDrySeconds), .75f);
            public float TargetSpread => 1f + (Surface == ProjectileWoundSurface.Fabric ? 1.05f :
                Surface == ProjectileWoundSurface.Skin ? .48f : .30f) + (HitCount - 1) * .10f;
            public Vector3 Position => Sample(false);
            public Vector3 Direction => Sample(true).normalized;
            public ProjectileWound CopyForHistory() => (ProjectileWound)MemberwiseClone();
            private Vector3 Sample(bool normal)
            {
                Patch.RefreshBlendShapes();
                return Patch.SkinVertex(A, normal) * Barycentric.x + Patch.SkinVertex(B, normal) * Barycentric.y +
                    Patch.SkinVertex(C, normal) * Barycentric.z;
            }
        }

        private sealed class ProjectileLayer
        {
            public Patch Patch;
            public SkinnedMeshRenderer Renderer;
            public readonly Vector4[] Holes = new Vector4[HolesPerLayer];
            public readonly Vector4[] Shapes = new Vector4[HolesPerLayer];
            public readonly Vector4[] States = new Vector4[HolesPerLayer];
            public int Count;
            public bool Dirty;
        }

        private readonly List<Patch> patches = new List<Patch>();
        private readonly List<ProjectileWound> projectileWounds = new List<ProjectileWound>(MaximumProjectileWounds);
        private readonly List<ProjectileLayer> projectileLayers = new List<ProjectileLayer>();
        private readonly Dictionary<Transform, Matrix4x4> projectileWorldBones = new Dictionary<Transform, Matrix4x4>();
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Material bloodMaterial;
        private readonly CombatActor actor;
        private readonly float projectileSurfaceReach;
        private Patch last;
        private ProjectileWound lastProjectile;
        public int Count { get; private set; }
        public int ProjectileCount => projectileWounds.Count;
        public Vector3 BleedPosition => TryGetBleed(out Vector3 point, out _) ? point : Vector3.zero;
        public Vector3 BleedDirection => TryGetBleed(out _, out Vector3 direction) ? direction : Vector3.up;

        public bool TryGetBleed(out Vector3 position, out Vector3 direction)
        {
            position = Vector3.zero; direction = Vector3.up;
            if (lastProjectile != null && Retains(lastProjectile))
            { position = lastProjectile.Position; direction = lastProjectile.Direction; return true; }
            if (last != null && last.Active && Retains(last))
            { position = last.Position; direction = last.Direction; return true; }
            for (int i = projectileWounds.Count - 1; i >= 0; i--)
                if (TryGetProjectileBleed(i, out position, out direction)) return true;
            for (int i = patches.Count - 1; i >= 0; i--)
                if (patches[i].Active && Retains(patches[i]))
                { position = patches[i].Position; direction = patches[i].Direction; return true; }
            return false;
        }

        public bool TryGetProjectileBleed(int index, out Vector3 position, out Vector3 direction)
        {
            position = Vector3.zero; direction = Vector3.up;
            if (index < 0 || index >= projectileWounds.Count) return false;
            ProjectileWound wound = projectileWounds[index];
            if (!Retains(wound)) return false;
            wound.Patch.RefreshBlendShapes();
            wound.Patch.SyncBlendShapeWeights(wound.Layer.Renderer);
            position = wound.Position; direction = wound.Direction;
            return true;
        }

        internal bool TryGetProjectilePresentation(int index, out ProjectileWoundPresentation presentation)
        {
            presentation = default;
            if (index < 0 || index >= projectileWounds.Count) return false;
            ProjectileWound wound = projectileWounds[index];
            presentation = new ProjectileWoundPresentation(wound.Surface, wound.DirectionUv, wound.Stretch,
                wound.AgeSeconds, wound.Wetness, wound.Spread, wound.HitCount);
            return true;
        }

        public CombatDamageMarks(CombatActor actor, Material material)
        {
            this.actor = actor;
            bloodMaterial = material;
            // A tilted torso's coarse collider can meet the ray well before
            // its clothing. Bound the same projection window for every patch
            // by the real actor dimensions, rather than a fixed skin offset.
            projectileSurfaceReach = Mathf.Clamp(actor.Body != null ? actor.Body.height * .25f : .4f, .18f, .4f);
            Transform rig = actor.DamageRigRoot;
            if (rig == null) throw new InvalidOperationException("Blood requires the actor's original rendered rig.");
            GameObject model = Resources.Load<GameObject>("CombatBlood/Wounds" + (actor.IsHero ? "Hero" : "Npc"));
            if (model == null) throw new InvalidOperationException("Missing authored combat wound model.");
            var sources = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                sources[renderer.name] = renderer;
            foreach (SkinnedMeshRenderer template in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string[] name = template.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (name.Length != 3 || !sources.TryGetValue(name[1], out SkinnedMeshRenderer source))
                    throw new InvalidOperationException("Wound surface missing from live rig: " + template.name);
                var host = new GameObject(template.name);
                host.transform.SetParent(source.transform, false);
                SkinnedMeshRenderer renderer = host.AddComponent<SkinnedMeshRenderer>();
                Mesh mesh = template.sharedMesh;
                Transform[] sourceBones = source.bones;
                renderer.sharedMesh = mesh; renderer.bones = sourceBones; renderer.rootBone = source.rootBone;
                renderer.sharedMaterial = material; renderer.localBounds = source.localBounds;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.updateWhenOffscreen = true; renderer.enabled = false;
                Vector2[] uv = mesh.uv;
                int centre = 0;
                for (int i = 1; i < uv.Length; i++)
                    if ((uv[i] - Vector2.one * .5f).sqrMagnitude < (uv[centre] - Vector2.one * .5f).sqrMagnitude) centre = i;
                Vector3[] meshVertices = mesh.vertices, meshNormals = mesh.normals;
                var shapes = new BlendShape[mesh.blendShapeCount];
                for (int shape = 0; shape < shapes.Length; shape++)
                {
                    int sourceIndex = source.sharedMesh.GetBlendShapeIndex(mesh.GetBlendShapeName(shape));
                    if (sourceIndex < 0) throw new InvalidOperationException("The wound lost its original blend shape: " + renderer.name);
                    int frames = mesh.GetBlendShapeFrameCount(shape);
                    var blend = new BlendShape { SourceIndex = sourceIndex, Frames = new float[frames],
                        Vertices = new Vector3[frames][], Normals = new Vector3[frames][] };
                    for (int frame = 0; frame < frames; frame++)
                    {
                        blend.Frames[frame] = mesh.GetBlendShapeFrameWeight(shape, frame);
                        blend.Vertices[frame] = new Vector3[mesh.vertexCount]; blend.Normals[frame] = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(shape, frame, blend.Vertices[frame], blend.Normals[frame], null);
                    }
                    shapes[shape] = blend;
                }
                var patch = new Patch { Renderer = renderer, Source = source, Centre = centre,
                    BindPoses = mesh.bindposes, Bones = sourceBones,
                    Vertices = mesh.vertices, Normals = mesh.normals, Uv = uv, Weights = mesh.boneWeights,
                    DeformedVertices = (Vector3[])meshVertices.Clone(), DeformedNormals = (Vector3[])meshNormals.Clone(),
                    BlendShapes = shapes, Triangles = mesh.triangles, SkinnedVertices = new Vector3[mesh.vertexCount] };
                patch.InitializeSkinning();
                patch.Projection = new ProjectileWound { Patch = patch };
                patches.Add(patch);
            }
        }

        public void Add(Vector3 point, Vector3 incoming, bool projectile = false, bool head = false,
            Player3DAnatomicalPart? part = null)
        {
            Patch nearest = null;
            ProjectileWound nearestWound = null;
            float best = float.PositiveInfinity;
            projectileWorldBones.Clear();
            foreach (Patch patch in patches)
            {
                if (!CombatBodyDestruction.SourceAvailable(actor, patch.Source)) continue;
                if (projectile && head && patch.Source.name != "GEO_Head" && patch.Source.name != "GEO_FaceSurface") continue;
                if (projectile && part.HasValue && !MatchesProjectileEndpoint(patch.Source.name, part.Value)) continue;
                ProjectileWound candidate = projectile ? LocateProjectile(patch, point, incoming, projectileSurfaceReach,
                    projectileWorldBones, best, nearestWound != null && float.IsFinite(nearestWound.RayDistance)) : null;
                if (projectile && candidate == null) continue;
                Vector3 surfacePoint = candidate != null ? candidate.Position : patch.Position;
                if (candidate != null)
                {
                    CombatBodyDestruction.TryClassifySurface(actor, patch.Source, surfacePoint,
                        out candidate.Region, out candidate.BodyPatch);
                    if (!Retains(candidate)) continue;
                }
                else
                {
                    Classify(patch, surfacePoint);
                    if (!Retains(patch)) continue;
                }
                Vector3 surfaceNormal = candidate != null ? candidate.Direction : patch.Direction;
                float distance = (surfacePoint - point).sqrMagnitude;
                // Exact surface distance wins. A large facing penalty can move a
                // wrist contact onto a sleeve several centimetres from its glove.
                float score = distance + Mathf.Max(0f, Vector3.Dot(surfaceNormal, incoming)) * (projectile ? .0004f : .15f);
                bool rayContact = candidate != null && float.IsFinite(candidate.RayDistance);
                bool previousRayContact = nearestWound != null && float.IsFinite(nearestWound.RayDistance);
                if (previousRayContact && !rayContact) continue;
                if (rayContact)
                {
                    // An actual entry on the incoming line outranks every
                    // nearest-point fallback, including on a different patch.
                    if (!previousRayContact) best = float.PositiveInfinity;
                    score = candidate.RayDistance;
                }
                if (patch.Active && !projectile) score += .045f;
                if (score >= best) continue;
                nearest = patch; nearestWound = candidate; best = score;
            }
            if (nearest == null) return;
            if (projectile)
            {
                AddProjectile(nearestWound);
                RefreshVisibility();
                return;
            }
            if (!nearest.Active) { nearest.Active = true; Count++; }
            nearest.Grade = Mathf.Min(3, nearest.Grade + 1);
            float scale = 1f - (nearest.Grade - 1) * .15f;
            nearest.Renderer.GetPropertyBlock(properties);
            properties.SetVector("_BaseMap_ST", new Vector4(scale, scale, (1f - scale) * .5f, (1f - scale) * .5f));
            nearest.Renderer.SetPropertyBlock(properties); properties.Clear();
            last = nearest;
            lastProjectile = null;
            RefreshVisibility();
        }

        private static bool MatchesProjectileEndpoint(string source, Player3DAnatomicalPart part)
        {
            bool left = part == Player3DAnatomicalPart.LeftHand || part == Player3DAnatomicalPart.LeftFoot;
            switch (part)
            {
                case Player3DAnatomicalPart.LeftHand:
                case Player3DAnatomicalPart.RightHand:
                    return source.EndsWith(left ? ".L" : ".R", StringComparison.Ordinal) &&
                        (source.StartsWith("GEO_Hand.", StringComparison.Ordinal) ||
                         source.StartsWith("GEO_HandPalm", StringComparison.Ordinal) ||
                         source.StartsWith("GEO_Finger", StringComparison.Ordinal) ||
                         source.StartsWith("GEO_Thumb.", StringComparison.Ordinal) ||
                         source.StartsWith("CLO_Glove", StringComparison.Ordinal));
                case Player3DAnatomicalPart.LeftFoot:
                case Player3DAnatomicalPart.RightFoot:
                    return source.EndsWith(left ? ".L" : ".R", StringComparison.Ordinal) &&
                        (source.StartsWith("CLO_Boot", StringComparison.Ordinal) ||
                         source.StartsWith("GEO_Foot", StringComparison.Ordinal));
                default: return true;
            }
        }

        private void AddProjectile(ProjectileWound wound)
        {
            Patch patch = wound.Patch;
            ProjectileWound closest = null;
            float best = float.PositiveInfinity;
            foreach (ProjectileWound existing in projectileWounds)
            {
                if (!Retains(existing) || existing.Patch.Source != patch.Source ||
                    existing.Region != wound.Region || existing.BodyPatch != wound.BodyPatch ||
                    Vector3.Dot(existing.Direction, wound.Direction) < .65f) continue;
                float distance = (existing.Position - wound.Position).sqrMagnitude;
                if (distance >= best) continue;
                closest = existing; best = distance;
            }
            float mergeDistance = wound.Surface == ProjectileWoundSurface.Glove ? .024f : .032f;
            if (closest != null && best < mergeDistance * mergeDistance)
            {
                // Reopen and damage this same entry; neither a second overlay nor
                // a new bleed source is fabricated over an existing opening.
                closest.HitCount = Mathf.Min(4, closest.HitCount + 1);
                closest.AgeSeconds = 0f;
                closest.Stretch = Mathf.Max(closest.Stretch, wound.Stretch);
                last = closest.Patch; lastProjectile = closest;
                StoreProjectile(closest);
                UploadProjectileLayer(closest.Layer);
                return;
            }
            // Saturation retains the history already on the body. A far contact
            // cannot enlarge an unrelated old opening just because the pool is full.
            if (projectileWounds.Count >= MaximumProjectileWounds) return;
            // Projection objects belong to their patches and are reused for each
            // pellet. Only a newly retained wound enters the persistent history.
            wound = wound.CopyForHistory();
            ProjectileLayer layer = null;
            foreach (ProjectileLayer candidate in projectileLayers)
                if (candidate.Patch == patch && candidate.Count < HolesPerLayer) { layer = candidate; break; }
            if (layer == null)
            {
                foreach (ProjectileLayer candidate in projectileLayers)
                    if (candidate.Count == 0) { layer = candidate; break; }
                if (layer == null)
                {
                    var host = new GameObject("Projectile Wounds__" + patch.Source.name + "__" + projectileLayers.Count);
                    layer = new ProjectileLayer { Renderer = host.AddComponent<SkinnedMeshRenderer>() };
                    projectileLayers.Add(layer);
                }
                ConfigureProjectileLayer(layer, patch);
            }
            wound.Layer = layer; wound.Slot = layer.Count++;
            last = patch; lastProjectile = wound;
            StoreProjectile(wound);
            UploadProjectileLayer(layer);
            projectileWounds.Add(wound);
            Count++;
        }

        private static void StoreProjectile(ProjectileWound wound)
        {
            ProjectileLayer layer = wound.Layer;
            layer.Holes[wound.Slot] = new Vector4(wound.Uv.x, wound.Uv.y, wound.Retained ? wound.Radius : 0f, (float)wound.Surface);
            layer.Shapes[wound.Slot] = new Vector4(wound.DirectionUv.x, wound.DirectionUv.y, wound.Stretch, wound.Seed);
            layer.States[wound.Slot] = new Vector4(wound.AgeSeconds, wound.Wetness, wound.Spread, wound.HitCount);
            layer.Dirty = true;
        }

        private void UploadProjectileLayer(ProjectileLayer layer)
        {
            properties.Clear();
            properties.SetFloat("_BulletWound", 1f);
            properties.SetFloat(BulletHoleCountId, layer.Count);
            properties.SetVectorArray(BulletHolesId, layer.Holes);
            properties.SetVectorArray(BulletShapesId, layer.Shapes);
            properties.SetVectorArray(BulletStatesId, layer.States);
            layer.Renderer.SetPropertyBlock(properties); properties.Clear();
            layer.Dirty = false;
        }

        /// <summary>The existing duel clock owns drying and soak; rendering never advances it.</summary>
        public void Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return;
            foreach (ProjectileWound wound in projectileWounds)
            {
                if (wound.AgeSeconds >= ProjectileWoundDrySeconds) continue;
                float step = Mathf.Min(seconds, ProjectileWoundDrySeconds - wound.AgeSeconds);
                wound.AgeSeconds += step;
                wound.Spread = Mathf.Lerp(wound.TargetSpread, wound.Spread, Mathf.Exp(-step * .36f));
                StoreProjectile(wound);
            }
            foreach (ProjectileLayer layer in projectileLayers)
                if (layer.Dirty) UploadProjectileLayer(layer);
        }

        private void ConfigureProjectileLayer(ProjectileLayer layer, Patch patch)
        {
            layer.Patch = patch;
            SkinnedMeshRenderer renderer = layer.Renderer;
            renderer.transform.SetParent(patch.Source.transform, false);
            renderer.sharedMesh = patch.Renderer.sharedMesh;
            renderer.bones = patch.Bones; renderer.rootBone = patch.Source.rootBone;
            renderer.sharedMaterial = RequireProjectileMaterial(bloodMaterial);
            renderer.localBounds = patch.Source.localBounds;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.updateWhenOffscreen = true;
            patch.RefreshBlendShapes();
            patch.SyncBlendShapeWeights(renderer);
        }

        private static ProjectileWound LocateProjectile(Patch patch, Vector3 point, Vector3 incoming, float surfaceReach,
            Dictionary<Transform, Matrix4x4> worldBones, float bestScore, bool previousRayContact)
        {
            // A pellet samples one live pose. Share its bone matrices across wound
            // patches, then release the snapshot so later bleeding follows the rig.
            patch.CapturePose(worldBones);
            try { return LocateProjectileInPose(patch, point, incoming, surfaceReach, bestScore, previousRayContact); }
            finally { patch.ReleasePose(); }
        }

        private static ProjectileWound LocateProjectileInPose(Patch patch, Vector3 point, Vector3 incoming, float surfaceReach,
            float bestScore, bool previousRayContact)
        {
            patch.RefreshBlendShapes();
            patch.RefreshSkinnedVertices();
            Vector3 rayDirection = incoming.sqrMagnitude > .000001f ? incoming.normalized : -patch.Direction;
            Vector3 rayOrigin = point - rayDirection * surfaceReach;
            Bounds bounds = patch.SkinnedBounds;
            bounds.Expand(.00002f);
            bool canMeetRay = bounds.IntersectRay(new Ray(rayOrigin, rayDirection), out float entry) &&
                entry <= surfaceReach * 2f;
            Vector3 closest = Vector3.Max(bounds.min, Vector3.Min(point, bounds.max));
            // Every ray contact outranks fallback distance. Without a possible
            // ray, the AABB distance is a lower bound on the nonnegative score.
            if (!canMeetRay && (previousRayContact || (closest - point).sqrMagnitude >= bestScore)) return null;
            ProjectileWound wound = patch.Projection;
            wound.A = wound.B = wound.C = 0; wound.Barycentric = Vector3.right;
            wound.Uv = wound.DirectionUv = Vector2.zero;
            wound.Radius = wound.Stretch = wound.Seed = wound.AgeSeconds = 0f;
            wound.Spread = 1f; wound.HitCount = 1; wound.Slot = 0;
            wound.RayDistance = float.PositiveInfinity; wound.Region = BodyDamageRegion.Chest;
            wound.BodyPatch = -1; wound.Retained = true;
            wound.Surface = ProjectileWoundSurface.Skin; wound.Layer = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < patch.Triangles.Length; i += 3)
            {
                int a = patch.Triangles[i], b = patch.Triangles[i + 1], c = patch.Triangles[i + 2];
                if (RayTriangle(rayOrigin, rayDirection, patch.SkinnedVertices[a], patch.SkinnedVertices[b],
                    patch.SkinnedVertices[c], out float rayDistance, out Vector3 rayBarycentric) &&
                    rayDistance <= surfaceReach * 2f)
                {
                    if (rayDistance < wound.RayDistance)
                    {
                        wound.RayDistance = rayDistance;
                        wound.A = a; wound.B = b; wound.C = c; wound.Barycentric = rayBarycentric;
                    }
                    continue;
                }
                if (float.IsFinite(wound.RayDistance)) continue;
                Vector3 barycentric = ClosestTriangle(patch.SkinnedVertices[a], patch.SkinnedVertices[b], patch.SkinnedVertices[c], point);
                Vector3 surface = patch.SkinnedVertices[a] * barycentric.x + patch.SkinnedVertices[b] * barycentric.y + patch.SkinnedVertices[c] * barycentric.z;
                float distance = (surface - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                wound.A = a; wound.B = b; wound.C = c; wound.Barycentric = barycentric;
            }
            wound.Uv = patch.Uv[wound.A] * wound.Barycentric.x + patch.Uv[wound.B] * wound.Barycentric.y + patch.Uv[wound.C] * wound.Barycentric.z;
            Vector2 uvA = patch.Uv[wound.B] - patch.Uv[wound.A], uvB = patch.Uv[wound.C] - patch.Uv[wound.A];
            bool useA = uvA.sqrMagnitude >= uvB.sqrMagnitude;
            float uvLength = Mathf.Max(.0001f, useA ? uvA.magnitude : uvB.magnitude);
            float worldLength = Vector3.Distance(patch.SkinnedVertices[wound.A], patch.SkinnedVertices[useA ? wound.B : wound.C]);
            wound.Radius = Mathf.Clamp(.014f * uvLength / Mathf.Max(.0001f, worldLength), .015f, .18f);
            wound.Surface = patch.Source.name.StartsWith("CLO_Glove", StringComparison.Ordinal) ||
                patch.Source.name.StartsWith("CLO_Boot", StringComparison.Ordinal) ||
                patch.Source.name.StartsWith("GEO_FootShoe", StringComparison.Ordinal)
                ? ProjectileWoundSurface.Glove : patch.Source.name.StartsWith("CLO_", StringComparison.Ordinal)
                ? ProjectileWoundSurface.Fabric : ProjectileWoundSurface.Skin;
            Vector3 normal = wound.Direction;
            Vector3 travel = incoming.sqrMagnitude > .000001f ? incoming.normalized : -normal;
            Vector3 tangent = Vector3.ProjectOnPlane(travel, normal);
            float obliquity = Mathf.Clamp01(tangent.magnitude);
            if (tangent.sqrMagnitude < .000001f)
            {
                tangent = Vector3.ProjectOnPlane(Vector3.up, normal);
                if (tangent.sqrMagnitude < .000001f) tangent = Vector3.ProjectOnPlane(Vector3.right, normal);
            }
            Vector3 edgeA = patch.SkinnedVertices[wound.B] - patch.SkinnedVertices[wound.A];
            Vector3 edgeB = patch.SkinnedVertices[wound.C] - patch.SkinnedVertices[wound.A];
            float aa = Vector3.Dot(edgeA, edgeA), ab = Vector3.Dot(edgeA, edgeB), bb = Vector3.Dot(edgeB, edgeB);
            float determinant = aa * bb - ab * ab;
            Vector2 directionUv = Vector2.up;
            if (determinant > .000000000001f)
            {
                float da = Vector3.Dot(tangent, edgeA), db = Vector3.Dot(tangent, edgeB);
                directionUv = uvA * ((da * bb - db * ab) / determinant) + uvB * ((db * aa - da * ab) / determinant);
            }
            wound.DirectionUv = directionUv.sqrMagnitude > .000001f ? directionUv.normalized : Vector2.up;
            wound.Stretch = 1f + obliquity * (wound.Surface == ProjectileWoundSurface.Fabric ? .85f :
                wound.Surface == ProjectileWoundSurface.Glove ? .55f : .38f);
            // Stable local variation: it follows the wound through ragdoll and reset,
            // and does not consume any gameplay/particle random sequence.
            wound.Seed = Mathf.Repeat(wound.Uv.x * 17.17f + wound.Uv.y * 37.71f + wound.A * .618f, 1f) * Mathf.PI * 2f;
            return wound;
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
            out float distance, out Vector3 barycentric)
        {
            distance = 0f; barycentric = default;
            Vector3 ab = b - a, ac = c - a;
            Vector3 cross = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, cross);
            if (Mathf.Abs(determinant) < .000000001f) return false;
            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(offset, ab);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(ac, q) * inverse;
            if (distance < 0f) return false;
            barycentric = new Vector3(1f - u - v, u, v);
            return true;
        }

        private static Vector3 ClosestTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 point)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return Vector3.right;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return Vector3.up;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return new Vector3(1f - v, v, 0f); }
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return Vector3.forward;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return new Vector3(1f - w, 0f, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 >= d3 && d5 >= d6)
            { float w = (d4 - d3) / (d4 - d3 + d5 - d6); return new Vector3(0f, 1f - w, w); }
            float total = va + vb + vc;
            return Mathf.Abs(total) > 1e-14f ? new Vector3(va, vb, vc) / total : Vector3.right;
        }

        public void RefreshVisibility()
        {
            foreach (Patch patch in patches)
                if (patch.Renderer != null)
                {
                    patch.RefreshBlendShapes();
                    patch.Renderer.enabled = patch.Active && Retains(patch);
                }
            foreach (ProjectileWound wound in projectileWounds)
            {
                bool retained = Retains(wound);
                if (wound.Retained == retained) continue;
                wound.Retained = retained;
                StoreProjectile(wound);
            }
            foreach (ProjectileLayer layer in projectileLayers)
                if (layer.Renderer != null)
                {
                    layer.Patch.SyncBlendShapeWeights(layer.Renderer);
                    bool visible = false;
                    for (int i = 0; i < layer.Count; i++) visible |= layer.Holes[i].z > 0f;
                    layer.Renderer.enabled = visible && CombatBodyDestruction.SourceAvailable(actor, layer.Patch.Source);
                    if (layer.Dirty) UploadProjectileLayer(layer);
                }
        }

        private void Classify(Patch patch, Vector3 position)
        {
            if (patch.Classified) return;
            CombatBodyDestruction.TryClassifySurface(actor, patch.Source, position, out patch.Region, out patch.BodyPatch);
            patch.Classified = true;
        }

        private bool Retains(Patch patch) =>
            CombatBodyDestruction.RetainsSurface(actor, patch.Source, patch.Region, patch.BodyPatch);

        private bool Retains(ProjectileWound wound) =>
            CombatBodyDestruction.RetainsSurface(actor, wound.Patch.Source, wound.Region, wound.BodyPatch);

        public void Reset()
        {
            foreach (Patch patch in patches)
            {
                patch.Active = false; patch.Grade = 0;
                if (patch.Renderer != null) { patch.Renderer.enabled = false; patch.Renderer.SetPropertyBlock(null); }
            }
            foreach (ProjectileLayer layer in projectileLayers)
            {
                layer.Count = 0; layer.Dirty = false;
                Array.Clear(layer.Holes, 0, layer.Holes.Length);
                Array.Clear(layer.Shapes, 0, layer.Shapes.Length);
                Array.Clear(layer.States, 0, layer.States.Length);
                if (layer.Renderer != null) { layer.Renderer.enabled = false; layer.Renderer.SetPropertyBlock(null); }
            }
            projectileWounds.Clear();
            Count = 0; last = null; lastProjectile = null;
        }

        public void Dispose()
        {
            foreach (Patch patch in patches)
                if (patch.Renderer != null) UnityEngine.Object.Destroy(patch.Renderer.gameObject);
            foreach (ProjectileLayer layer in projectileLayers)
                if (layer.Renderer != null) UnityEngine.Object.Destroy(layer.Renderer.gameObject);
            projectileLayers.Clear(); projectileWounds.Clear(); patches.Clear(); last = null; lastProjectile = null; Count = 0;
        }

        private static Material RequireProjectileMaterial(Material blood)
        {
            if (sharedProjectileMaterial != null) return sharedProjectileMaterial;
            Shader shader = Resources.Load<Shader>("Shaders/CombatWound");
            if (shader == null) throw new InvalidOperationException("Projectile holes require the shared CombatWound shader.");
            sharedProjectileMaterial = new Material(shader) { name = "Combat Projectile Wounds Shared", hideFlags = HideFlags.HideAndDontSave };
            sharedProjectileMaterial.SetTexture("_BaseMap", blood.GetTexture("_BaseMap"));
            return sharedProjectileMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedMaterial()
        {
            if (sharedProjectileMaterial != null) UnityEngine.Object.Destroy(sharedProjectileMaterial);
            sharedProjectileMaterial = null;
        }
    }
}
