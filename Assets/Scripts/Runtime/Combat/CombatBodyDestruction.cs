using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Source-derived body surfaces, finite tissue loss and pose-preserving separation.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatBodyDestruction : MonoBehaviour
    {
        internal sealed class Piece
        {
            internal SkinnedMeshRenderer Skin, Source;
            internal MeshRenderer Released;
            internal Mesh Baked, ProxyMesh, AuthoredMesh;
            internal Vector3[] BakedVertices;
            internal BodyDamageRegion Region;
            internal int Patch;
            internal bool Flesh, Bone, Eligible, Emitted, Debris, QueryOriginal;
            internal BoxCollider Proxy;
            internal ProxyGeometry ProxyGeometry;
            internal ProxyPoseSnapshot ProxyPose;
            internal CentrePose CentrePose;
            internal CombatBodySourceDeformation Deformation;
            internal CombatTorsoDamageSurface Torso;
            internal Fragment Fragment;
            internal uint GeometryVersion => unchecked((Deformation?.GeometryVersion ?? 0u) + (Torso?.GeometryVersion ?? 0u));
            internal bool PreservesSkinBinding(Mesh candidate) => candidate != null && (candidate == AuthoredMesh ||
                Deformation?.PreservesPartitionSkinBinding(candidate) == true || Torso?.PreservesSkinBinding(candidate) == true);
            internal void RefreshSurface()
            {
                Deformation?.Refresh(Skin, Torso != null && Torso.IsRenderingOwnedMesh);
                Torso?.Refresh(Deformation?.GeometryVersion ?? 0u);
            }
        }
        private sealed class Body
        {
            internal CombatActor Actor;
            internal bool Active;
            internal ulong PoseRevision, AppliedPoseRevision;
            internal bool PoseCaptured;
            internal PiecePoseState[] PieceStates;
            internal SourcePoseState[] SourceStates;
            internal int VolleyDepth;
            internal bool VolleyNeedsSynchronization;
            internal CombatImpact LastVolleyImpact;
            internal ProxyPoseSnapshot ProxyPose;
            internal readonly List<Piece> Pieces = new List<Piece>();
            internal readonly Dictionary<SkinnedMeshRenderer, List<Piece>> SurfacePieces =
                new Dictionary<SkinnedMeshRenderer, List<Piece>>();
            internal readonly List<Piece>[] Regions = new List<Piece>[CombatBodyDamageState.RegionCount];
            internal readonly Dictionary<SkinnedMeshRenderer, bool> Originals = new Dictionary<SkinnedMeshRenderer, bool>();
            internal readonly Dictionary<BodyDamageRegion, Transform> Bones = new Dictionary<BodyDamageRegion, Transform>();
            internal readonly HashSet<BodyDamageRegion> Detached = new HashSet<BodyDamageRegion>();
            internal readonly HashSet<SkinnedMeshRenderer> Replaced = new HashSet<SkinnedMeshRenderer>();
            internal readonly List<Fragment> Fragments = new List<Fragment>();
            internal readonly Dictionary<SkinnedMeshRenderer, CombatBodySourceDeformation.SourceSnapshot> SourceSnapshots =
                new Dictionary<SkinnedMeshRenderer, CombatBodySourceDeformation.SourceSnapshot>();
            internal readonly List<Piece> RetainedScratch = new List<Piece>(), ReleasedScratch = new List<Piece>();
            internal Body()
            {
                for (int region = 0; region < Regions.Length; region++) Regions[region] = new List<Piece>();
            }
        }
        private sealed class SourcePoseState
        {
            private readonly SkinnedMeshRenderer source;
            private readonly CombatBodySourceDeformation deformation;
            private Mesh mesh;
            private uint geometryVersion;
            private float[] blendWeights;
            private bool active;
            internal SourcePoseState(Piece piece)
            {
                source = piece.Source; deformation = piece.Deformation;
                blendWeights = new float[source.sharedMesh.blendShapeCount];
            }
            internal bool Capture()
            {
                Mesh current = source.sharedMesh;
                uint version = deformation?.SourceGeometryVersion ?? 0u;
                bool visible = source.enabled && source.gameObject.activeInHierarchy;
                bool changed = mesh != current || geometryVersion != version || active != visible;
                mesh = current; geometryVersion = version; active = visible;
                if (blendWeights.Length != current.blendShapeCount)
                { blendWeights = new float[current.blendShapeCount]; changed = true; }
                for (int i = 0; i < blendWeights.Length; i++)
                {
                    float weight = source.GetBlendShapeWeight(i);
                    changed |= !blendWeights[i].Equals(weight); blendWeights[i] = weight;
                }
                return changed;
            }
        }
        private sealed class PiecePoseState
        {
            private readonly Piece piece;
            private Mesh mesh;
            private uint geometryVersion;
            private int topologyVersion;
            private bool visible, colliding;
            private float[] blendWeights;
            internal PiecePoseState(Piece piece)
            { this.piece = piece; blendWeights = new float[piece.Skin.sharedMesh.blendShapeCount]; }
            internal bool Capture()
            {
                Mesh current = piece.Skin.sharedMesh;
                uint version = piece.GeometryVersion;
                int topology = piece.Torso?.TopologyBuilds ?? 0;
                bool enabled = piece.Skin.enabled, collision = piece.Proxy != null && piece.Proxy.enabled;
                bool changed = mesh != current || geometryVersion != version || topologyVersion != topology ||
                    visible != enabled || colliding != collision ||
                    (enabled || collision) && piece.Torso?.HasPendingDamage == true;
                mesh = current; geometryVersion = version; topologyVersion = topology;
                visible = enabled; colliding = collision;
                if (blendWeights.Length != current.blendShapeCount)
                { blendWeights = new float[current.blendShapeCount]; changed = true; }
                for (int i = 0; i < blendWeights.Length; i++)
                {
                    float weight = piece.Skin.GetBlendShapeWeight(i);
                    changed |= !blendWeights[i].Equals(weight); blendWeights[i] = weight;
                }
                return changed;
            }
        }
        internal sealed class Fragment
        {
            internal Rigidbody Body;
            internal Collider Collider;
            internal Vector3 Velocity, Spin;
            internal bool Frozen, Settled;
            internal float Age;
            internal Vector3 PendingImpulse, ImpactPoint;
            internal readonly List<Piece> Pieces = new List<Piece>();
            internal bool Anatomical;
        }
        private static readonly HashSet<Renderer> suppressed = new HashSet<Renderer>();
        private static readonly Dictionary<CombatActor, CombatBodyDestruction> owners = new Dictionary<CombatActor, CombatBodyDestruction>();
        private readonly Dictionary<CombatActor, Body> bodies = new Dictionary<CombatActor, Body>();
        private readonly ProxyVertexGroupingScratch proxyVertexGroupingScratch = new ProxyVertexGroupingScratch();
        private MaterialPropertyBlock properties;
        private Material flesh, bone;
        private CombatHeadDestruction headEffects;
        private CombatBloodEffects bloodEffects;
        private CombatHeadDestruction HeadEffects => headEffects != null ? headEffects : headEffects = GetComponent<CombatHeadDestruction>();
        private CombatBloodEffects BloodEffects => bloodEffects != null ? bloodEffects : bloodEffects = GetComponent<CombatBloodEffects>();
        private bool frozen;
        private void Awake() => properties = new MaterialPropertyBlock();
        internal int ProxyGeometryBuilds { get; private set; }
        internal long ProxyNativeBoundsFolds { get; private set; }
        internal long ProxyBurstBoundsFolds { get; private set; }
        internal long ProxyGeometryTicks { get; private set; }
        internal long ProxyTransformSamples { get; private set; }
        internal long UnchangedBodyPoseSkips { get; private set; }
        internal int ProxyTransformCountFor(CombatActor actor) =>
            bodies.TryGetValue(actor, out Body body) ? body.ProxyPose.Count : 0;
        internal int ActorSynchronizationCount { get; private set; }
        internal long ActorSynchronizationTicks { get; private set; }
        internal long HeadSynchronizationTicks { get; private set; }
        internal long SurfaceRefreshTicks { get; private set; }
        internal long SurfaceVisibilityTicks { get; private set; }
        internal long PhysicsSynchronizationTicks { get; private set; }
        internal long ContactCaptureTicks { get; private set; }
        internal ProxyCacheInvalidation LastProxyCacheInvalidation { get; private set; }

        [Flags]
        internal enum ProxyCacheInvalidation
        { None = 0, Initial = 1, Geometry = 2, Minimum = 4, RendererScale = 8, BonePose = 16, BlendShape = 32 }

        internal static void BeginVolley(CombatActor actor)
        {
            if (actor != null && owners.TryGetValue(actor, out CombatBodyDestruction owner) &&
                owner.bodies.TryGetValue(actor, out Body body)) body.VolleyDepth++;
        }

        internal static void EndVolley(CombatActor actor)
        {
            if (actor == null || !owners.TryGetValue(actor, out CombatBodyDestruction owner) ||
                !owner.bodies.TryGetValue(actor, out Body body) || body.VolleyDepth == 0) return;
            if (--body.VolleyDepth != 0 || !body.VolleyNeedsSynchronization) return;
            body.VolleyNeedsSynchronization = false;
            owner.SynchronizeActor(actor, body.LastVolleyImpact);
        }

        internal static bool IsSuppressed(Renderer renderer) => suppressed.Contains(renderer);
        internal static bool SourceAvailable(CombatActor actor, SkinnedMeshRenderer source)
        {
            if (source == null || !source.gameObject.activeInHierarchy) return false;
            if (owners.TryGetValue(actor, out CombatBodyDestruction owner) && owner.bodies.TryGetValue(actor, out Body body) &&
                body.Active && body.Originals.TryGetValue(source, out bool visible)) return visible;
            return source.enabled || Player3DHeadVisibility.IsTemporarilyHidden(source);
        }
        internal static bool TryClassifySurface(CombatActor actor, SkinnedMeshRenderer source, Vector3 point,
            out BodyDamageRegion region, out int patch, Dictionary<Transform, Matrix4x4> worldBones = null)
        {
            region = BodyDamageRegion.Chest; patch = -1;
            if (!owners.TryGetValue(actor, out CombatBodyDestruction owner) || !owner.bodies.TryGetValue(actor, out Body body)) return false;
            if (ReferenceEquals(source, null) || !body.SurfacePieces.TryGetValue(source, out List<Piece> pieces)) return false;
            float closest = float.PositiveInfinity;
            foreach (Piece piece in pieces)
            {
                float candidate = (SkinCentre(piece, worldBones) - point).sqrMagnitude;
                if (candidate >= closest) continue;
                closest = candidate; region = piece.Region; patch = piece.Patch;
            }
            return patch >= 0;
        }
        internal static bool RetainsSurface(CombatActor actor, SkinnedMeshRenderer source, BodyDamageRegion region, int patch) =>
            SourceAvailable(actor, source) && (patch < 0 || actor.BodyDamage.IsAttached(region) && actor.BodyDamage.TissueLoss(region, patch) < .25f);
        public int ActiveFragmentCount { get { int n = 0; foreach (Body body in bodies.Values) n += body.Fragments.Count; return n; } }
        public int DetachedPartCountFor(CombatActor actor) => bodies.TryGetValue(actor, out Body body) ? body.Detached.Count : 0;
        public float DebrisAgeFor(CombatActor actor) => bodies.TryGetValue(actor, out Body body) && body.Fragments.Count > 0 ? body.Fragments[0].Age : 0f;
        public int TissueRendererCountFor(CombatActor actor) => Count(actor, false);
        public int ExposedBoneCountFor(CombatActor actor) => Count(actor, true);
        internal int DepletedTorsoCellCountFor(CombatActor actor)
        {
            int count = 0;
            if (bodies.TryGetValue(actor, out Body body))
                foreach (Piece piece in body.Pieces) if (piece.Flesh && piece.Torso != null) count += piece.Torso.DepletedCellCount;
            return count;
        }
        private int Count(CombatActor actor, bool bones)
        {
            int count = 0;
            if (actor == null || !bodies.TryGetValue(actor, out Body body) || !body.Active) return count;
            foreach (Piece piece in body.Pieces)
                if (piece.Bone == bones && (bones ? HasExposedPatch(actor, piece.Region) : piece.Flesh) &&
                    (piece.Skin.enabled || piece.Released != null && piece.Released.enabled)) count++;
            return count;
        }
        private static bool HasExposedPatch(CombatActor actor, BodyDamageRegion region)
        {
            if (HasDistalSever(actor, region)) return true;
            if (CombatBodyDamageState.IsTorso(region) && owners.TryGetValue(actor, out CombatBodyDestruction owner) &&
                owner.bodies.TryGetValue(actor, out Body body))
            {
                foreach (Piece piece in body.Regions[(int)region])
                    if (piece.Torso != null && piece.Torso.DepletedCellCount > 0) return true;
                return false;
            }
            for (int p = 0; p < CombatBodyDamageState.PatchCount; p++)
                if (actor.BodyDamage.TissueLoss(region, p) >= 1f) return true;
            return false;
        }
        private static bool HasDistalSever(CombatActor actor, BodyDamageRegion region)
        {
            if (!actor.BodyDamage.IsAttached(region)) return false;
            for (int r = 0; r < CombatBodyDamageState.RegionCount; r++)
            {
                var child = (BodyDamageRegion)r;
                if (child != region && CombatBodyAnatomy.Parent(child) == region && !actor.BodyDamage.IsAttached(child)) return true;
            }
            return false;
        }

        internal void PrepareActor(CombatActor actor)
        {
            if (bodies.ContainsKey(actor)) return;
            GameObject model = Resources.Load<GameObject>("CombatGore/Body" + (actor.IsHero ? "Hero" : actor.IsCrane ? "Crane" : actor.IsRoom ? "Room" : actor.IsImprint ? "Imprint" : actor.IsLongExposure ? "LongExposure" : "Npc"));
            if (model == null) throw new InvalidOperationException("Missing authored combat body model.");
            var body = new Body { Actor = actor, ProxyPose = new ProxyPoseSnapshot(this) };
            var originals = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer source in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                originals[source.name] = source;
            foreach (Transform target in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                for (int r = 1; r <= 16; r++)
                    if (target.name == CombatBodyAnatomy.BoneName((BodyDamageRegion)r)) body.Bones[(BodyDamageRegion)r] = target;
            foreach (SkinnedMeshRenderer template in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string[] names = template.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (names.Length != 2 || !originals.TryGetValue(names[1], out SkinnedMeshRenderer source))
                    throw new InvalidOperationException("Combat body derivative lost its source: " + template.name);
                string label = names[0];
                bool isFlesh = label.StartsWith("FleshRegion", StringComparison.Ordinal), isBone = label.StartsWith("BoneRegion", StringComparison.Ordinal);
                int start = isFlesh ? 11 : isBone ? 10 : 6;
                int patchAt = label.IndexOf("Patch", StringComparison.Ordinal);
                int end = patchAt < 0 ? label.Length : patchAt;
                if (!int.TryParse(label.Substring(start, end - start), out int region) || region < 1 || region > 16)
                    throw new InvalidOperationException("Invalid body region: " + label);
                // This actor has an authored metal wrist instead of a human
                // hand. Neither damage derivatives nor reset may recreate it.
                if (actor.IsCrane && (region == (int)BodyDamageRegion.RightHand || actor.IsCraneReplacedHandSurface(source))) continue;
                int patch = patchAt < 0 ? 0 : int.Parse(label.Substring(patchAt + 5));
                var host = new GameObject("Body " + template.name);
                host.transform.SetParent(source.transform, false);
                var skin = host.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = template.sharedMesh; skin.bones = source.bones; skin.rootBone = source.rootBone;
                skin.localBounds = source.localBounds; skin.updateWhenOffscreen = true;
                bool roomShell = actor.IsRoom && CombatBodyDamageState.IsTorso((BodyDamageRegion)region);
                skin.sharedMaterials = isBone || isFlesh ? new[] { actor.IsImprint ? CombatImprintAssetProvider.LoadBodyMaterial("chalk") :
                    actor.IsLongExposure ? CombatLongExposureAssetProvider.LoadBodyMaterial() :
                    roomShell ? CombatRoomAssetProvider.LoadBodyMaterial("plaster") :
                    RequireMaterial(isBone) } : source.sharedMaterials;
                if (!isFlesh && !isBone)
                { source.GetPropertyBlock(properties); skin.SetPropertyBlock(properties); properties.Clear(); }
                skin.shadowCastingMode = ShadowCastingMode.On; skin.receiveShadows = true; skin.enabled = false;
                bool visible = source.enabled && source.gameObject.activeInHierarchy;
                var piece = new Piece { Skin = skin, Source = source, AuthoredMesh = template.sharedMesh, Region = (BodyDamageRegion)region,
                    Patch = patch, Flesh = isFlesh, Bone = isBone, Eligible = isFlesh || isBone || visible,
                    ProxyPose = body.ProxyPose };
                if (!isFlesh && !isBone || (region is 7 or 10))
                {
                    if (!body.SourceSnapshots.TryGetValue(source, out CombatBodySourceDeformation.SourceSnapshot snapshot))
                    {
                        snapshot = new CombatBodySourceDeformation.SourceSnapshot();
                        body.SourceSnapshots.Add(source, snapshot);
                    }
                    piece.Deformation = new CombatBodySourceDeformation(template.sharedMesh, source, snapshot);
                }
                if (piece.Eligible && !isBone && CombatBodyDamageState.IsTorso(piece.Region))
                    piece.Torso = new CombatTorsoDamageSurface(skin, isFlesh);
                body.Pieces.Add(piece);
                body.Regions[region].Add(piece);
                if (!isFlesh && !isBone && !body.Originals.ContainsKey(source)) body.Originals.Add(source, visible);
            }
            if (body.Pieces.Count == 0) throw new InvalidOperationException("Empty authored combat body.");
            // Preserve the authored filtered order, including classification ties.
            foreach (Piece piece in body.Pieces)
                if (!piece.Flesh && !piece.Bone)
                {
                    if (!body.SurfacePieces.TryGetValue(piece.Source, out List<Piece> surfaces))
                    { surfaces = new List<Piece>(); body.SurfacePieces.Add(piece.Source, surfaces); }
                    surfaces.Add(piece);
                }
            foreach (Piece piece in body.Pieces)
                if (piece.Eligible && !piece.Bone)
                { piece.RefreshSurface(); SkinCentre(piece); piece.Torso?.PrepareExteriorMesh(); }
            // Prepare hidden collision resources before the first damaging hit.
            // Activation still fits their bounds to the current damaged pose.
            body.ProxyPose.BeginPass();
            foreach (Piece piece in body.Pieces)
                if (piece.Eligible && (piece.Flesh || piece.Bone || piece.Torso != null))
                    CreateProxy(body, piece, !piece.Bone);
            body.PieceStates = new PiecePoseState[body.Pieces.Count];
            var sources = new Dictionary<SkinnedMeshRenderer, SourcePoseState>();
            for (int i = 0; i < body.Pieces.Count; i++)
            {
                Piece piece = body.Pieces[i];
                body.PieceStates[i] = new PiecePoseState(piece);
                if (piece.Deformation != null && !sources.ContainsKey(piece.Source))
                    sources.Add(piece.Source, new SourcePoseState(piece));
            }
            body.SourceStates = new List<SourcePoseState>(sources.Values).ToArray();
            bodies.Add(actor, body);
            owners[actor] = this;
            actor.Hurtboxes.PrepareBodySurfaces(body.Pieces);
            RetainAuthoredIntactQueries(body);
            if (actor.IsAuthoredEnemy) actor.Hurtboxes.CapturePreparedPose();
        }

        private static void RetainAuthoredIntactQueries(Body body)
        {
            if (!body.Actor.IsAuthoredEnemy) return;
            // Authored silhouettes can extend beyond the retained human capsules
            // or contain real openings. Query the prewarmed original skin before
            // its first wound, without activating damage draws or physics proxies.
            foreach (Piece piece in body.Pieces)
                piece.QueryOriginal = piece.Eligible && !piece.Flesh && !piece.Bone;
            body.Actor.Hurtboxes.SetBodySurfaces(body.Pieces);
        }

        /// <summary>One actual resolved contact; structural damage is independent of health delta.</summary>
        public void Apply(CombatImpact impact)
        {
            if (impact.Target == null || impact.Result is not (MeleeHitResult.Hit or MeleeHitResult.GuardBroken)) return;
            if (impact.Target.IsHero || impact.Target.IsCraneRegenerating) return;
            if (impact.Kind is CombatImpactKind.Shove or CombatImpactKind.Kick or CombatImpactKind.Unarmed or CombatImpactKind.Fall or CombatImpactKind.Hook) return;
            BodyDamageRegion region = impact.BodyRegion ?? CombatBodyAnatomy.ToRegion(impact.Part);
            if (!bodies.TryGetValue(impact.Target, out Body body)) return;
            if (impact.DetachedPart) ApplyDetachedImpulse(body, region, impact);
            int patch = impact.BodyPatch >= 0 ? impact.BodyPatch : region == BodyDamageRegion.Head ?
                HeadEffects.TissuePatchFor(impact.Target, impact.Point) : ClosestPatch(body, region, impact.Point);
            float trauma = impact.Kind == CombatImpactKind.Projectile ?
                (impact.IsPellet ? Mathf.Clamp(impact.WoundDamage / 24f, 0f, 1.1f) * .50f : .18f) :
                Mathf.Lerp(.10f, .24f, Mathf.Clamp01(impact.AttackPower));
            float tissueTrauma = trauma;
            trauma *= impact.Target.State.Settings.IncomingDamageScale;
            if (region == BodyDamageRegion.Head)
            {
                // The existing sixteen-sector fracture system owns skull continuity,
                // including its fourteen-sector cap for one shotgun volley.
                bool skullGone = HeadEffects.DetachedSectorCountFor(impact.Target) == 16;
                trauma = skullGone ? 2f : Mathf.Min(trauma, 1f - impact.Target.BodyDamage.TissueLoss(region, patch));
            }
            else if (impact.IsPellet)
            {
                float tissue = impact.Target.BodyDamage.TissueLoss(region, patch);
                // Flesh tears readily in a close cluster; the exposed skeleton
                // needs further contacts. The central cage resists more than limbs.
                bool core = region is BodyDamageRegion.Neck or BodyDamageRegion.Chest or BodyDamageRegion.Abdomen or BodyDamageRegion.Pelvis;
                trauma = tissue < 1f ? Mathf.Min(trauma, 1f - tissue) : trauma * (core ? .06f : .12f);
            }
            if (trauma <= 0f) return;
            int stream = unchecked((impact.Source != null ? impact.Source.GetEntityId().GetHashCode() : 0) * 397 ^ (int)impact.Kind);
            uint contactRevision = impact.Target.BodyDamage.ContactRevision;
            bool changed = impact.Target.BodyDamage.Apply(region, patch, trauma, stream,
                impact.AttackSequence, impact.PelletIndex);
            bool newContact = impact.Target.BodyDamage.ContactRevision != contactRevision;
            if (newContact && CombatBodyDamageState.IsTorso(region))
            {
                // Exposed bone contacts project back to the authored outer surface;
                // new wounds in a saturated coarse patch remain locally meaningful.
                float distance = float.PositiveInfinity; Vector3 centre = impact.Point;
                foreach (Piece piece in body.Regions[(int)region])
                    if (piece.Region == region && piece.Flesh && piece.Torso != null)
                        piece.Torso.ClosestSurfacePoint(impact.Point, ref distance, ref centre);
                float radius = impact.Kind == CombatImpactKind.Projectile ?
                    impact.IsPellet ? Mathf.Lerp(.055f, .12f, Mathf.Clamp01(impact.WoundDamage / 24f)) : .075f :
                    Mathf.Lerp(.07f, .12f, Mathf.Clamp01(impact.AttackPower));
                foreach (Piece piece in body.Pieces)
                    if (piece.Torso != null) changed |= piece.Torso.Apply(centre, radius, tissueTrauma);
            }
            if (changed)
            {
                impact.Target.ApplyBodyCapabilities(impact);
                if (body.VolleyDepth > 0)
                { body.VolleyNeedsSynchronization = true; body.LastVolleyImpact = impact; }
                else SynchronizeActor(impact.Target, impact);
            }
        }

        private static int ClosestPatch(Body body, BodyDamageRegion region, Vector3 point)
        {
            int patch = 0; float distance = float.PositiveInfinity;
            foreach (Piece piece in body.Regions[(int)region])
            {
                if (!piece.Flesh) continue;
                // Bounds are expressed in the source mesh frame; the weighted centre follows its live rig.
                Vector3 centre = SkinCentre(piece);
                float candidate = (centre - point).sqrMagnitude;
                if (candidate < distance) { distance = candidate; patch = piece.Patch; }
            }
            return patch;
        }

        private static Vector3 SkinCentre(Piece piece, Dictionary<Transform, Matrix4x4> worldBones = null)
        {
            // Interior pieces use an anatomical source binding. Using the dominant
            // centre vertex avoids reading a renderer's stale, whole-body bounds.
            piece.CentrePose ??= new CentrePose(piece.Skin);
            return piece.CentrePose.Evaluate(piece, worldBones);
        }

        internal sealed class CentrePose
        {
            private readonly BoneWeight[] weights;
            private readonly Matrix4x4[] bind;
            private readonly Transform[] bones;
            private readonly List<Vector3> vertices = new List<Vector3>();
            private Mesh capturedMesh;
            private uint capturedVersion;
            private Vector3 capturedCentre;
            private BoneWeight centreWeight;
            private Matrix4x4 world0, world1, world2, world3;
            private Vector3 worldCentre;
            private bool worldCentreReady;

            internal CentrePose(SkinnedMeshRenderer skin)
            { weights = skin.sharedMesh.boneWeights; bind = skin.sharedMesh.bindposes; bones = skin.bones; }

            internal Vector3 Evaluate(Piece piece, Dictionary<Transform, Matrix4x4> worldBones = null)
            {
                Mesh mesh = piece.Skin.sharedMesh;
                Vector3 centre = mesh.bounds.center;
                uint version = piece.GeometryVersion;
                bool geometryChanged = mesh != capturedMesh || version != capturedVersion || !centre.Equals(capturedCentre);
                if (geometryChanged)
                {
                    mesh.GetVertices(vertices);
                    if (vertices.Count != weights.Length)
                        throw new InvalidOperationException("Combat body centre lost its authored skin topology: " + mesh.name);
                    int nearest = 0; float best = float.PositiveInfinity;
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        float distance = (vertices[i] - centre).sqrMagnitude;
                        if (distance < best) { best = distance; nearest = i; }
                    }
                    centreWeight = weights[nearest]; capturedMesh = mesh;
                    capturedVersion = version; capturedCentre = centre;
                }
                BoneWeight weight = centreWeight;
                Matrix4x4 p0 = weight.weight0 > 0f ? SampleWorld(bones[weight.boneIndex0], worldBones) : default;
                Matrix4x4 p1 = weight.weight1 > 0f ? SampleWorld(bones[weight.boneIndex1], worldBones) : default;
                Matrix4x4 p2 = weight.weight2 > 0f ? SampleWorld(bones[weight.boneIndex2], worldBones) : default;
                Matrix4x4 p3 = weight.weight3 > 0f ? SampleWorld(bones[weight.boneIndex3], worldBones) : default;
                if (worldCentreReady && !geometryChanged && (weight.weight0 <= 0f || p0.Equals(world0)) &&
                    (weight.weight1 <= 0f || p1.Equals(world1)) && (weight.weight2 <= 0f || p2.Equals(world2)) &&
                    (weight.weight3 <= 0f || p3.Equals(world3))) return worldCentre;
                world0 = p0; world1 = p1; world2 = p2; world3 = p3;
                Vector3 point = weight.weight0 > 0f ? (p0 * bind[weight.boneIndex0]).MultiplyPoint3x4(centre) * weight.weight0 : Vector3.zero;
                if (weight.weight1 > 0f) point += (p1 * bind[weight.boneIndex1]).MultiplyPoint3x4(centre) * weight.weight1;
                if (weight.weight2 > 0f) point += (p2 * bind[weight.boneIndex2]).MultiplyPoint3x4(centre) * weight.weight2;
                if (weight.weight3 > 0f) point += (p3 * bind[weight.boneIndex3]).MultiplyPoint3x4(centre) * weight.weight3;
                worldCentreReady = true;
                return worldCentre = point;
            }

            private static Matrix4x4 SampleWorld(Transform bone, Dictionary<Transform, Matrix4x4> worldBones)
            {
                if (worldBones == null) return bone.localToWorldMatrix;
                if (!worldBones.TryGetValue(bone, out Matrix4x4 world))
                { world = bone.localToWorldMatrix; worldBones.Add(bone, world); }
                return world;
            }
        }

        public void SynchronizeActor(CombatActor actor, CombatImpact impact)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { SynchronizeActorSurfaces(actor, impact); }
            finally { ActorSynchronizationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start; }
        }

        private void SynchronizeActorSurfaces(CombatActor actor, CombatImpact impact)
        {
            if (!bodies.TryGetValue(actor, out Body body)) return;
            body.PoseCaptured = false;
            ActorSynchronizationCount++;
            long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            HeadEffects.SynchronizeTissue(actor, captureImmediately: false);
            HeadSynchronizationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
            if (!body.Active)
            {
                body.Active = true;
                actor.Hurtboxes.SetBodySurfaces(body.Pieces);
            }
            // Each region leaves the attached graph once. Already released pieces
            // retain their own posed surfaces and remain damage-query targets.
            for (int r = 1; r <= 16; r++)
            {
                var region = (BodyDamageRegion)r;
                if (actor.BodyDamage.IsAttached(region) || !body.Detached.Add(region)) continue;
                List<Piece> selected = body.RetainedScratch;
                selected.Clear();
                foreach (Piece piece in body.Regions[r])
                    if (piece.Eligible && !piece.Emitted &&
                        (piece.Bone || actor.BodyDamage.TissueLoss(region, piece.Patch) < 1f)) selected.Add(piece);
                Release(body, selected, impact, true);
                BodyDamageRegion parent = CombatBodyAnatomy.Parent(region);
                if (actor.BodyDamage.IsAttached(parent) && body.Bones.TryGetValue(parent, out Transform retained) &&
                    body.Bones.TryGetValue(region, out Transform cut))
                {
                    BloodEffects?.SetBodyBleedSource(actor, region, retained, cut.position,
                        (cut.position - retained.position).normalized);
                    if (CombatBodyDamageState.IsTorso(parent))
                        foreach (Piece piece in body.Regions[(int)parent]) piece.Torso?.Apply(cut.position, .065f, .35f);
                }
            }
            // A damaged patch is one physical piece even when its authored surface
            // crosses several source meshes or garment layers.
            for (int r = 1; r <= 16; r++)
                for (int p = 0; p < CombatBodyDamageState.PatchCount; p++)
                {
                    var region = (BodyDamageRegion)r;
                    if (CombatBodyDamageState.IsTorso(region) || actor.BodyDamage.TissueLoss(region, p) < 1f) continue;
                    List<Piece> retained = body.RetainedScratch, released = body.ReleasedScratch;
                    retained.Clear(); released.Clear();
                    foreach (Piece piece in body.Regions[r])
                    {
                        if (piece.Patch != p || piece.Bone || !piece.Eligible || piece.Debris) continue;
                        if (!piece.Emitted) retained.Add(piece);
                        else if (piece.Released != null) released.Add(piece);
                    }
                    Release(body, retained, impact, false);
                    ReleaseDetachedTissue(body, released, impact);
                }
            stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (Piece piece in body.Pieces) if (piece.Eligible && !piece.Emitted) piece.RefreshSurface();
            SurfaceRefreshTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
            stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            body.ProxyPose.BeginPass();
            // Keep each intact production surface as one draw. Only a source that
            // actually loses a patch needs its authored replacement surfaces.
            foreach (Piece piece in body.Pieces)
                if (piece.Eligible && !piece.Flesh && !piece.Bone &&
                    (!actor.BodyDamage.IsAttached(piece.Region) || (piece.Torso != null ? piece.Torso.HasDamage :
                        actor.BodyDamage.TissueLoss(piece.Region, piece.Patch) >= .25f)) &&
                    body.Replaced.Add(piece.Source))
                { suppressed.Add(piece.Source); piece.Source.enabled = false; }
            foreach (Piece piece in body.Pieces)
            {
                if (!piece.Eligible) continue;
                float loss = actor.BodyDamage.TissueLoss(piece.Region, piece.Patch);
                bool attached = actor.BodyDamage.IsAttached(piece.Region);
                bool visible = piece.Eligible && (piece.Torso != null ? piece.Torso.HasGeometry : piece.Bone ? HasExposedPatch(actor, piece.Region) :
                    piece.Flesh ? loss < 1f && (loss >= .25f || HasDistalSever(actor, piece.Region)) : loss < .25f);
                piece.QueryOriginal = !piece.Flesh && !piece.Bone && attached && !piece.Emitted && visible &&
                    !body.Replaced.Contains(piece.Source);
                piece.Skin.enabled = attached && !piece.Emitted && visible && !piece.QueryOriginal;
                if (piece.Released != null) piece.Released.enabled = piece.Debris ? piece.Eligible : visible;
                bool collides = Collides(actor, piece);
                bool created = collides && piece.Proxy == null;
                if (created) CreateProxy(body, piece, !piece.Emitted);
                if (piece.Proxy != null)
                {
                    if (!created && collides && (!piece.Proxy.enabled || piece.Torso != null))
                        UpdateProxyBounds(piece, !piece.Emitted);
                    SetColliderEnabled(piece.Proxy, collides);
                }
            }
            SurfaceVisibilityTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
            stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            actor.Ragdoll.PhysicsController.SetCombatBodyDamage(actor.BodyDamage, true);
            foreach (Fragment fragment in body.Fragments) if (fragment.Anatomical) ResizeFragment(fragment);
            PhysicsSynchronizationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
            stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            actor.Hurtboxes.CapturePreparedPose();
            ContactCaptureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
        }

        private void CreateProxy(Body body, Piece piece, bool surfaceRefreshed = false)
        {
            if (piece.Proxy != null) return;
            if (!body.Bones.TryGetValue(piece.Region, out Transform target)) return;
            var proxy = new GameObject("Remaining body " + piece.Region + " " + piece.Patch);
            proxy.transform.SetParent(target, false);
            piece.Proxy = proxy.AddComponent<BoxCollider>(); piece.Proxy.enabled = false;
            body.Actor.Ragdoll.PhysicsController.RegisterCombatBodyCollider(piece.Proxy);
            CombatCorpseTraversal.RegisterCollider(body.Actor, piece.Proxy);
            UpdateProxyBounds(piece, surfaceRefreshed);
            foreach (var collider in body.Actor.Ragdoll.PhysicsController.AnatomicalColliders)
                Physics.IgnoreCollision(piece.Proxy, collider.Key, true);
            if (body.Actor.Body != null) Physics.IgnoreCollision(piece.Proxy, body.Actor.Body, true);
            foreach (Piece other in body.Pieces) if (other.Proxy != null && other != piece) Physics.IgnoreCollision(piece.Proxy, other.Proxy, true);
            // Proxies may be created after an earlier standing hit released debris.
            foreach (Fragment fragment in body.Fragments) Physics.IgnoreCollision(piece.Proxy, fragment.Collider, true);
        }

        private static bool Collides(CombatActor actor, Piece piece) => (piece.Flesh || piece.Bone || piece.Torso != null) && piece.Eligible && actor.IsRagdollActive &&
            actor.BodyDamage.IsAttached(piece.Region) && (piece.Bone ? HasExposedPatch(actor, piece.Region) :
                piece.Torso != null ? piece.Torso.HasGeometry : piece.Flesh && actor.BodyDamage.TissueLoss(piece.Region, piece.Patch) < 1f);

        private void UpdateProxyBounds(Piece piece, bool surfaceRefreshed = false)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { RefreshProxyBounds(piece, surfaceRefreshed); }
            finally { ProxyGeometryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start; }
        }

        private void RefreshProxyBounds(Piece piece, bool surfaceRefreshed)
        {
            if (!surfaceRefreshed) piece.RefreshSurface();
            piece.ProxyGeometry ??= new ProxyGeometry();
            Transform target = piece.Proxy.transform.parent;
            if (!piece.ProxyGeometry.Capture(piece, target, out Matrix4x4 matrix, out Vector3 minimum)) return;
            piece.ProxyMesh ??= new Mesh { name = "Remaining anatomy collision" };
            piece.Skin.BakeMesh(piece.ProxyMesh, true);
            ProxyGeometryBuilds++;
            LastProxyCacheInvalidation = piece.ProxyGeometry.LastInvalidation;
            List<Vector3> vertices = piece.ProxyGeometry.Vertices;
            piece.ProxyMesh.GetVertices(vertices);
            IReadOnlyList<int> indices = piece.ProxyGeometry.ActiveIndices;
            if (indices.Count == 0) { SetColliderEnabled(piece.Proxy, false); return; }
            if (piece.ProxyGeometry.TryNativeBounds(piece, vertices, indices, piece.ProxyGeometry.ActiveIndexBuilds, matrix, minimum,
                out Vector3 centre, out Vector3 size, out bool burst))
            {
                ProxyNativeBoundsFolds++;
                if (burst) ProxyBurstBoundsFolds++;
                SetColliderBounds(piece.Proxy, centre, size);
                return;
            }
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero);
            for (int i = 0; i < indices.Count; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[indices[i]]));
            SetColliderBounds(piece.Proxy, bounds.center, Vector3.Max(bounds.size, minimum));
        }

        internal readonly struct ProxyBoundsDiagnostic
        {
            internal readonly Vector3[] Vertices;
            internal readonly int[] ActiveIndices, CachedActiveIndices;
            internal readonly int IndexBuilds, SkinBindingBuilds, UniqueVertexTransforms, CornerCount;
            internal readonly Matrix4x4 Matrix;
            internal readonly Vector3 Minimum, NativeCentre, NativeSize;
            internal readonly bool ExecutedInBurst;

            internal ProxyBoundsDiagnostic(Vector3[] vertices, int[] indices, int[] cachedIndices, int indexBuilds, int skinBindingBuilds,
                Matrix4x4 matrix, Vector3 minimum, Vector3 centre, Vector3 size, bool burst,
                int uniqueVertexTransforms, int cornerCount)
            {
                Vertices = vertices; ActiveIndices = indices; CachedActiveIndices = cachedIndices; IndexBuilds = indexBuilds;
                SkinBindingBuilds = skinBindingBuilds;
                Matrix = matrix; Minimum = minimum;
                NativeCentre = centre; NativeSize = size; ExecutedInBurst = burst;
                UniqueVertexTransforms = uniqueVertexTransforms; CornerCount = cornerCount;
            }
        }

        internal bool TryCaptureProxyBoundsForDiagnostics(CombatActor actor, BoxCollider proxy, out ProxyBoundsDiagnostic sample)
        {
            sample = default;
            if (!PlayerScarfBodyContacts.NativeClothSupported || actor == null || proxy == null ||
                !bodies.TryGetValue(actor, out Body body)) return false;
            foreach (Piece piece in body.Pieces)
            {
                if (piece.Proxy != proxy || piece.ProxyGeometry == null || piece.Skin == null) continue;
                var baked = new Mesh();
                try
                {
                    piece.Skin.BakeMesh(baked, true);
                    var vertices = new List<Vector3>(); baked.GetVertices(vertices);
                    int[] indices = piece.Skin.sharedMesh.triangles;
                    Matrix4x4 matrix = proxy.transform.parent.worldToLocalMatrix * piece.Skin.transform.localToWorldMatrix;
                    Vector3 minimum = piece.ProxyGeometry.MinimumSize;
                    if (!piece.ProxyGeometry.TryNativeBounds(piece, vertices, indices, 0, matrix, minimum,
                        out Vector3 centre, out Vector3 size, out bool burst)) return false;
                    sample = new ProxyBoundsDiagnostic(vertices.ToArray(), indices, piece.ProxyGeometry.DiagnosticActiveIndices,
                        piece.ProxyGeometry.ActiveIndexBuilds, piece.ProxyGeometry.SkinBindingBuilds, matrix, minimum, centre, size, burst,
                        piece.ProxyGeometry.LastUniqueVertexTransforms, piece.ProxyGeometry.LastCornerCount);
                    return true;
                }
                finally { Destroy(baked); }
            }
            return false;
        }

        private static void SetColliderEnabled(Collider collider, bool enabled)
        {
            if (collider.enabled == enabled) return;
            collider.enabled = enabled;
            if (enabled) CombatCorpseTraversal.NotifyColliderEnabled(collider);
        }

        private static void SetColliderBounds(BoxCollider collider, Vector3 centre, Vector3 size)
        {
            if (!collider.center.Equals(centre)) collider.center = centre;
            if (!collider.size.Equals(size)) collider.size = size;
        }

        // This observes exact shared state without clearing Transform.hasChanged.
        // Contacts, cloth writes, root/scale changes and corrective weights can all
        // arrive more than once within one rendered frame.
        private static ulong CaptureBodyPoseRevision(Body body, bool beginPass = true)
        {
            if (beginPass) body.ProxyPose.BeginPass();
            bool changed = body.ProxyPose.CaptureAll() || !body.PoseCaptured;
            foreach (SourcePoseState source in body.SourceStates) changed |= source.Capture();
            foreach (PiecePoseState piece in body.PieceStates) changed |= piece.Capture();
            if (changed) body.PoseRevision++;
            body.PoseCaptured = true;
            return body.PoseRevision;
        }

        internal static bool TryCaptureBodyPoseRevision(CombatActor actor, out ulong revision)
        {
            revision = 0;
            if (actor == null || !actor.State.IsDefeated || actor.Ragdoll == null || !actor.Ragdoll.IsSettled ||
                !owners.TryGetValue(actor, out CombatBodyDestruction owner) ||
                !owner.bodies.TryGetValue(actor, out Body body)) return false;
            revision = CaptureBodyPoseRevision(body);
            return true;
        }

        /// <summary>Exact local transforms shared by every proxy in one body validation pass.</summary>
        internal sealed class ProxyPoseSnapshot
        {
            private readonly CombatBodyDestruction owner;
            private readonly Dictionary<Transform, Node> nodes = new Dictionary<Transform, Node>();
            private readonly Dictionary<(Transform, Transform), ProxyGeometry.RelativePose> relativePoses =
                new Dictionary<(Transform, Transform), ProxyGeometry.RelativePose>();
            private readonly Dictionary<Transform, ProxyGeometry.ScalePose> scalePoses = new Dictionary<Transform, ProxyGeometry.ScalePose>();
            private ulong pass;
            private ulong revision;
            internal int Count => nodes.Count;
            internal ulong Pass => pass;
            internal ProxyVertexGroupingScratch VertexGroupingScratch => owner.proxyVertexGroupingScratch;

            internal ProxyPoseSnapshot(CombatBodyDestruction owner) => this.owner = owner;
            internal void BeginPass() => pass++;
            internal bool CaptureAll()
            {
                ulong previous = revision;
                foreach (Node node in nodes.Values) node.Capture();
                return previous != revision;
            }

            internal Node Register(Transform transform)
            {
                if (!nodes.TryGetValue(transform, out Node node))
                { node = new Node(this, transform); nodes.Add(transform, node); }
                return node;
            }

            internal ProxyGeometry.ScalePose ScaleFor(Transform transform)
            {
                if (!scalePoses.TryGetValue(transform, out ProxyGeometry.ScalePose pose))
                    scalePoses.Add(transform, pose = new ProxyGeometry.ScalePose(transform, this));
                return pose;
            }

            internal ProxyGeometry.RelativePose RelativeFor(Transform source, Transform target)
            {
                var key = (source, target);
                if (!relativePoses.TryGetValue(key, out ProxyGeometry.RelativePose pose) || !pose.MatchesHierarchy(source, target))
                    relativePoses[key] = pose = new ProxyGeometry.RelativePose(source, target, this);
                return pose;
            }

            internal sealed class Node
            {
                private readonly ProxyPoseSnapshot snapshot;
                private readonly Transform transform;
                private ulong capturedPass;
                private bool captured, matrixReady;
                private Matrix4x4 matrix;
                internal Vector3 Position { get; private set; }
                internal Quaternion Rotation { get; private set; }
                internal Vector3 Scale { get; private set; }
                internal ulong PoseVersion { get; private set; }
                internal ulong ScaleVersion { get; private set; }
                internal ulong RotationVersion { get; private set; }
                internal Transform Transform => transform;

                internal Node(ProxyPoseSnapshot snapshot, Transform transform)
                { this.snapshot = snapshot; this.transform = transform; }

                internal void Capture()
                {
                    if (captured && capturedPass == snapshot.pass) return;
                    transform.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
                    Vector3 scale = transform.localScale;
                    snapshot.owner.ProxyTransformSamples++;
                    bool scaleChanged = !captured || !Scale.Equals(scale);
                    bool rotationChanged = !captured || !Rotation.Equals(rotation);
                    if (!captured || !Position.Equals(position) || rotationChanged || scaleChanged)
                    { PoseVersion++; snapshot.revision++; matrixReady = false; }
                    if (scaleChanged) ScaleVersion++;
                    if (rotationChanged) RotationVersion++;
                    Position = position; Rotation = rotation; Scale = scale;
                    capturedPass = snapshot.pass; captured = true;
                }

                internal Matrix4x4 Matrix
                {
                    get
                    {
                        if (!matrixReady)
                        { matrix = Matrix4x4.TRS(Position, Rotation, Scale); matrixReady = true; }
                        return matrix;
                    }
                }
            }
        }

        // One cold-build workspace for every prepared body/partition owned by
        // this component. Partitions retain only their representative int map.
        internal sealed class ProxyVertexGroupingScratch
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly Dictionary<VertexBinding, int> representatives = new Dictionary<VertexBinding, int>();

            internal bool TryBuild(Mesh mesh, ref int[] mapping)
            {
                if (mesh.blendShapeCount != 0) return false;
                // Unity owns these read-only mesh views; never dispose them.
                NativeArray<byte> counts = mesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
                if (counts.Length != mesh.vertexCount) return false;
                int total = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] == 0 || counts[i] > 4) return false;
                    total += counts[i];
                }
                if (total != weights.Length) return false;
                mesh.GetVertices(positions);
                if (positions.Count != counts.Length) return false;
                if (mapping == null || mapping.Length < counts.Length) mapping = new int[counts.Length];
                representatives.Clear();
                int offset = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    var key = new VertexBinding(positions[i], weights, offset, counts[i]);
                    if (!representatives.TryGetValue(key, out int representative))
                    { representative = i; representatives.Add(key, i); }
                    mapping[i] = representative;
                    offset += counts[i];
                }
                return true;
            }

            private readonly struct VertexBinding : IEquatable<VertexBinding>
            {
                private readonly int x, y, z, count;
                private readonly int bone0, bone1, bone2, bone3, weight0, weight1, weight2, weight3;

                internal VertexBinding(Vector3 position, NativeArray<BoneWeight1> weights, int offset, int count)
                {
                    x = BitConverter.SingleToInt32Bits(position.x); y = BitConverter.SingleToInt32Bits(position.y);
                    z = BitConverter.SingleToInt32Bits(position.z); this.count = count;
                    BoneWeight1 a = weights[offset], b = count > 1 ? weights[offset + 1] : default;
                    BoneWeight1 c = count > 2 ? weights[offset + 2] : default, d = count > 3 ? weights[offset + 3] : default;
                    bone0 = a.boneIndex; bone1 = b.boneIndex; bone2 = c.boneIndex; bone3 = d.boneIndex;
                    weight0 = BitConverter.SingleToInt32Bits(a.weight); weight1 = BitConverter.SingleToInt32Bits(b.weight);
                    weight2 = BitConverter.SingleToInt32Bits(c.weight); weight3 = BitConverter.SingleToInt32Bits(d.weight);
                }

                public bool Equals(VertexBinding other) => x == other.x && y == other.y && z == other.z && count == other.count &&
                    bone0 == other.bone0 && bone1 == other.bone1 && bone2 == other.bone2 && bone3 == other.bone3 &&
                    weight0 == other.weight0 && weight1 == other.weight1 && weight2 == other.weight2 && weight3 == other.weight3;
                public override bool Equals(object other) => other is VertexBinding binding && Equals(binding);
                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = x; hash = hash * 31 + y; hash = hash * 31 + z; hash = hash * 31 + count;
                        hash = hash * 31 + bone0; hash = hash * 31 + bone1; hash = hash * 31 + bone2; hash = hash * 31 + bone3;
                        hash = hash * 31 + weight0; hash = hash * 31 + weight1; hash = hash * 31 + weight2;
                        return hash * 31 + weight3;
                    }
                }
            }
        }

        internal sealed class ProxyGeometry : IDisposable
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            private readonly List<int> activeIndices = new List<int>(), submeshIndices = new List<int>();
            internal IReadOnlyList<int> ActiveIndices => activeIndices;
            internal int ActiveIndexBuilds { get; private set; }
            internal int SkinBindingBuilds { get; private set; }
            private int diagnosticIndexBuilds = -1;
            private int[] diagnosticIndices;
            internal int[] DiagnosticActiveIndices
            {
                get
                {
                    if (diagnosticIndexBuilds != ActiveIndexBuilds)
                    { diagnosticIndices = activeIndices.ToArray(); diagnosticIndexBuilds = ActiveIndexBuilds; }
                    return diagnosticIndices;
                }
            }
            private Mesh bindingMesh;
            private Mesh indicesMesh;
            private int indicesTopologyBuilds;
            private Transform[] bones;
            private int[] influencedBones;
            private Matrix4x4[] relativeBones;
            private RelativePose[] relativePoses;
            private float[] blendWeights;
            private Vector3 minimumSize, rendererScale;
            private ScalePose targetScalePose, rendererScalePose;
            private uint geometryVersion;
            private bool captured;
            private ProxyBoundsWork boundsWork;
            private Mesh representativeMesh;
            private uint representativeGeometryVersion;
            private int representativeTopologyVersion, representativeBuilds;
            private int[] vertexRepresentatives;
            internal ProxyCacheInvalidation LastInvalidation { get; private set; }
            internal Vector3 MinimumSize => minimumSize;
            internal int LastUniqueVertexTransforms => boundsWork?.LastUniqueVertexTransforms ?? 0;
            internal int LastCornerCount => boundsWork?.LastCornerCount ?? 0;

            internal bool TryNativeBounds(Piece piece, List<Vector3> vertices, IReadOnlyList<int> indices, int indicesVersion, Matrix4x4 matrix, Vector3 minimum,
                out Vector3 centre, out Vector3 size, out bool burst)
            {
                centre = size = default; burst = false;
                if (!PlayerScarfBodyContacts.NativeClothSupported || indices.Count == 0) return false;
                Mesh current = piece.Skin.sharedMesh;
                if (current.vertexCount != vertices.Count) return false;
                int topology = piece.Torso?.TopologyBuilds ?? 0;
                if (representativeMesh != current || representativeGeometryVersion != piece.GeometryVersion ||
                    representativeTopologyVersion != topology)
                {
                    if (!piece.ProxyPose.VertexGroupingScratch.TryBuild(current, ref vertexRepresentatives)) vertexRepresentatives = null;
                    representativeMesh = current; representativeGeometryVersion = piece.GeometryVersion;
                    representativeTopologyVersion = topology; representativeBuilds++;
                }
                boundsWork ??= new ProxyBoundsWork();
                return boundsWork.Run(vertices, indices, indicesVersion, vertexRepresentatives, representativeBuilds,
                    matrix, minimum, out centre, out size, out burst);
            }

            public void Dispose()
            { boundsWork?.Dispose(); boundsWork = null; vertexRepresentatives = null; representativeMesh = null; }

            private sealed class ProxyBoundsWork : IDisposable
            {
                private NativeArray<Vector3> vertices, result;
                private NativeArray<int> indices, uniqueIndices;
                private Vector3[] upload = Array.Empty<Vector3>();
                private int[] indexUpload = Array.Empty<int>();
                private bool[] referencedVertices = Array.Empty<bool>();
                private IReadOnlyList<int> uploadedIndices;
                private int uploadedIndicesVersion;
                private int uploadedRepresentativeBuilds;
                private int uniqueIndexCount;
                internal int LastUniqueVertexTransforms { get; private set; }
                internal int LastCornerCount { get; private set; }

                internal bool Run(List<Vector3> cloud, IReadOnlyList<int> corners, int cornersVersion,
                    int[] vertexRepresentatives, int representativeBuilds, Matrix4x4 matrix, Vector3 minimum,
                    out Vector3 centre, out Vector3 size, out bool burst)
                {
                    centre = size = default; burst = false;
                    LastUniqueVertexTransforms = LastCornerCount = 0;
                    if (cloud.Count == 0 || corners.Count == 0) return false;
                    try
                    {
                        if (!vertices.IsCreated || vertices.Length < cloud.Count)
                        {
                            if (vertices.IsCreated) vertices.Dispose();
                            vertices = new NativeArray<Vector3>(cloud.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                            upload = new Vector3[cloud.Count];
                            referencedVertices = new bool[cloud.Count];
                        }
                        if (!indices.IsCreated || indices.Length < corners.Count)
                        {
                            if (indices.IsCreated) indices.Dispose();
                            indices = new NativeArray<int>(corners.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                            indexUpload = new int[corners.Count];
                            uploadedIndices = null;
                        }
                        int uniqueCapacity = Math.Min(cloud.Count, corners.Count);
                        if (!uniqueIndices.IsCreated || uniqueIndices.Length < uniqueCapacity)
                        {
                            if (uniqueIndices.IsCreated) uniqueIndices.Dispose();
                            uniqueIndices = new NativeArray<int>(uniqueCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                            uploadedIndices = null;
                        }
                        if (!result.IsCreated) result = new NativeArray<Vector3>(2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        cloud.CopyTo(upload);
                        NativeArray<Vector3>.Copy(upload, 0, vertices, 0, cloud.Count);
                        if (!ReferenceEquals(uploadedIndices, corners) || uploadedIndicesVersion != cornersVersion ||
                            uploadedRepresentativeBuilds != representativeBuilds)
                        {
                            if (corners is List<int> ordered) ordered.CopyTo(indexUpload);
                            else if (corners is int[] array) Array.Copy(array, indexUpload, corners.Count);
                            else for (int i = 0; i < corners.Count; i++) indexUpload[i] = corners[i];
                            // Equal rest-position and full skin-binding bits produce
                            // identical BakeMesh positions under every current pose.
                            // Preserve every corner in order, replacing only its
                            // position-equivalent vertex reference; no welded mesh.
                            if (vertexRepresentatives != null)
                                for (int i = 0; i < corners.Count; i++)
                                {
                                    int index = indexUpload[i];
                                    if ((uint)index >= (uint)cloud.Count) return false;
                                    indexUpload[i] = vertexRepresentatives[index];
                                }
                            NativeArray<int>.Copy(indexUpload, 0, indices, 0, corners.Count);
                            // The native corner array retains the original fold order.
                            // Reuse its managed upload scratch to cache each referenced
                            // vertex once, without another full-size position buffer.
                            Array.Clear(referencedVertices, 0, cloud.Count);
                            uniqueIndexCount = 0;
                            for (int i = 0; i < corners.Count; i++)
                            {
                                int index = indexUpload[i];
                                if ((uint)index >= (uint)cloud.Count) return false;
                                if (referencedVertices[index]) continue;
                                referencedVertices[index] = true;
                                indexUpload[uniqueIndexCount++] = index;
                            }
                            NativeArray<int>.Copy(indexUpload, 0, uniqueIndices, 0, uniqueIndexCount);
                            uploadedIndices = corners; uploadedIndicesVersion = cornersVersion;
                            uploadedRepresentativeBuilds = representativeBuilds;
                        }
                        var job = new CombatBodyProxyBoundsJob
                        {
                            Vertices = vertices, Indices = indices, UniqueIndices = uniqueIndices, Result = result, Matrix = matrix,
                            MinimumSize = minimum, VertexCount = cloud.Count, IndexCount = corners.Count, UniqueIndexCount = uniqueIndexCount
                        };
                        CombatBodyProxyBoundsJob.RunSynchronous(ref job);
                        if (job.Completed == 0) return false;
                        LastUniqueVertexTransforms = job.TransformedVertexCount; LastCornerCount = corners.Count;
                        centre = result[0]; size = result[1]; burst = job.ExecutedInBurst != 0;
                        return true;
                    }
                    catch { Dispose(); throw; }
                }

                public void Dispose()
                {
                    if (vertices.IsCreated) vertices.Dispose();
                    if (indices.IsCreated) indices.Dispose();
                    if (uniqueIndices.IsCreated) uniqueIndices.Dispose();
                    if (result.IsCreated) result.Dispose();
                    vertices = default; indices = uniqueIndices = default; result = default;
                    upload = Array.Empty<Vector3>(); indexUpload = Array.Empty<int>(); uploadedIndices = null;
                    referencedVertices = Array.Empty<bool>();
                    uniqueIndexCount = LastUniqueVertexTransforms = LastCornerCount = 0;
                }
            }

            internal bool Capture(Piece piece, Transform target, out Matrix4x4 matrix, out Vector3 minimum)
            {
                Mesh current = piece.Skin.sharedMesh;
                // Our cloth/erosion meshes clone the same weights, bones and blend
                // definitions. A changed vertex/index revision does not rebind skin.
                if (bindingMesh != current && !(piece.PreservesSkinBinding(bindingMesh) && piece.PreservesSkinBinding(current)))
                {
                    bindingMesh = current; bones = piece.Skin.bones;
                    SkinBindingBuilds++;
                    var used = new SortedSet<int>();
                    foreach (BoneWeight weight in current.boneWeights)
                    {
                        if (weight.weight0 > 0f) used.Add(weight.boneIndex0);
                        if (weight.weight1 > 0f) used.Add(weight.boneIndex1);
                        if (weight.weight2 > 0f) used.Add(weight.boneIndex2);
                        if (weight.weight3 > 0f) used.Add(weight.boneIndex3);
                    }
                    influencedBones = new int[used.Count]; used.CopyTo(influencedBones);
                    relativeBones = new Matrix4x4[used.Count]; blendWeights = new float[current.blendShapeCount];
                    relativePoses = new RelativePose[used.Count];
                    for (int i = 0; i < influencedBones.Length; i++)
                        relativePoses[i] = piece.ProxyPose.RelativeFor(bones[influencedBones[i]], target);
                    captured = false;
                }
                matrix = default;
                targetScalePose ??= piece.ProxyPose.ScaleFor(target);
                rendererScalePose ??= piece.ProxyPose.ScaleFor(piece.Skin.transform);
                bool targetScaleChanged = targetScalePose.Capture(out Vector3 scale);
                minimum = new Vector3(.003f / Mathf.Max(.000001f, Mathf.Abs(scale.x)),
                    .003f / Mathf.Max(.000001f, Mathf.Abs(scale.y)), .003f / Mathf.Max(.000001f, Mathf.Abs(scale.z)));
                bool rendererScaleChanged = rendererScalePose.Capture(out Vector3 currentRendererScale);
                uint currentVersion = piece.GeometryVersion;
                int currentTopologyBuilds = piece.Torso != null && piece.Torso.IsRenderingOwnedMesh ? piece.Torso.TopologyBuilds : 0;
                bool topologyChanged = indicesMesh != current || indicesTopologyBuilds != currentTopologyBuilds;
                if (topologyChanged)
                {
                    // Cloth/grip revisions move vertices without changing corner
                    // order. Only erosion SetTriangles or mesh replacement can
                    // change the active topology of these prepared partitions.
                    activeIndices.Clear();
                    for (int sub = 0; sub < current.subMeshCount; sub++)
                    { current.GetTriangles(submeshIndices, sub); activeIndices.AddRange(submeshIndices); }
                    ActiveIndexBuilds++;
                    indicesMesh = current; indicesTopologyBuilds = currentTopologyBuilds;
                }
                // BakeMesh compensates its renderer's position/rotation. That
                // conversion cancels when the baked points enter the proxy frame;
                // a shared rigid actor move cannot change these local bounds.
                ProxyCacheInvalidation invalidation = captured ? ProxyCacheInvalidation.None : ProxyCacheInvalidation.Initial;
                if (geometryVersion != currentVersion || topologyChanged) invalidation |= ProxyCacheInvalidation.Geometry;
                if (targetScaleChanged || !minimumSize.Equals(minimum)) invalidation |= ProxyCacheInvalidation.Minimum;
                if (rendererScaleChanged || !rendererScale.Equals(currentRendererScale)) invalidation |= ProxyCacheInvalidation.RendererScale;
                for (int i = 0; i < influencedBones.Length; i++)
                {
                    Matrix4x4 relative = relativePoses[i].Capture();
                    if (!relativeBones[i].Equals(relative)) invalidation |= ProxyCacheInvalidation.BonePose;
                    relativeBones[i] = relative;
                }
                for (int i = 0; i < blendWeights.Length; i++)
                {
                    float weight = piece.Skin.GetBlendShapeWeight(i);
                    if (!blendWeights[i].Equals(weight)) invalidation |= ProxyCacheInvalidation.BlendShape;
                    blendWeights[i] = weight;
                }
                geometryVersion = currentVersion;
                minimumSize = minimum; rendererScale = currentRendererScale; captured = true;
                LastInvalidation = invalidation;
                if (invalidation == ProxyCacheInvalidation.None) return false;
                matrix = target.worldToLocalMatrix * piece.Skin.transform.localToWorldMatrix;
                return true;
            }

            internal sealed class ScalePose
            {
                private readonly ProxyPoseSnapshot snapshot;
                private readonly ProxyPoseSnapshot.Node[] path;
                private readonly ulong[] scaleVersions, rotationVersions;
                private Vector3 capturedScale;
                private bool captured, changedInPass;
                private ulong capturedPass;

                internal ScalePose(Transform target, ProxyPoseSnapshot snapshot)
                {
                    this.snapshot = snapshot;
                    var ancestors = new List<Transform>();
                    for (Transform current = target; current != null; current = current.parent) ancestors.Add(current);
                    ancestors.Reverse(); path = new ProxyPoseSnapshot.Node[ancestors.Count];
                    scaleVersions = new ulong[path.Length]; rotationVersions = new ulong[path.Length];
                    for (int i = 0; i < path.Length; i++) path[i] = snapshot.Register(ancestors[i]);
                }

                internal bool Capture(out Vector3 scale)
                {
                    if (captured && capturedPass == snapshot.Pass)
                    { scale = capturedScale; return changedInPass; }
                    bool changed = !captured, rotationChanged = false, anisotropic = false;
                    for (int i = 0; i < path.Length; i++)
                    {
                        ProxyPoseSnapshot.Node node = path[i]; node.Capture();
                        changed |= scaleVersions[i] != node.ScaleVersion;
                        if (anisotropic) rotationChanged |= rotationVersions[i] != node.RotationVersion;
                        scaleVersions[i] = node.ScaleVersion; rotationVersions[i] = node.RotationVersion;
                        Vector3 local = node.Scale;
                        if (!anisotropic)
                        {
                            float x = Mathf.Abs(local.x), y = Mathf.Abs(local.y), z = Mathf.Abs(local.z);
                            anisotropic = !x.Equals(y) || !x.Equals(z);
                        }
                    }
                    capturedPass = snapshot.Pass;
                    if (captured && !changed && !rotationChanged)
                    { scale = capturedScale; changedInPass = false; return false; }
                    float uniform = 1f;
                    Matrix4x4 matrix = Matrix4x4.identity;
                    anisotropic = false;
                    for (int i = 0; i < path.Length; i++)
                    {
                        Vector3 local = path[i].Scale;
                        if (!anisotropic)
                        {
                            float x = Mathf.Abs(local.x), y = Mathf.Abs(local.y), z = Mathf.Abs(local.z);
                            if (x.Equals(y) && x.Equals(z)) { uniform *= x; continue; }
                            anisotropic = true; matrix = Matrix4x4.Scale(local);
                        }
                        else matrix *= Matrix4x4.TRS(Vector3.zero, path[i].Rotation, local);
                    }
                    // Leading scaled orthogonal factors cannot alter column
                    // lengths. Drop them, including the first anisotropic node's
                    // rotation; keep every later rotation that can produce shear.
                    scale = capturedScale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude,
                        matrix.GetColumn(2).magnitude) * uniform;
                    captured = true; changedInPass = changed;
                    return changedInPass;
                }
            }

            internal sealed class RelativePose
            {
                private readonly ProxyPoseSnapshot snapshot;
                private readonly Transform common;
                private readonly ProxyPoseSnapshot.Node[] sourcePath, targetPath;
                private readonly ulong[] sourceVersions, targetVersions;
                private ulong capturedPass;
                private bool captured;
                private Matrix4x4 relative;

                internal RelativePose(Transform source, Transform target, ProxyPoseSnapshot snapshot)
                {
                    this.snapshot = snapshot;
                    var ancestors = new HashSet<Transform>();
                    for (Transform current = target; current != null; current = current.parent) ancestors.Add(current);
                    var path = new List<Transform>();
                    Transform common = source;
                    while (common != null && !ancestors.Contains(common))
                    { path.Add(common); common = common.parent; }
                    this.common = common;
                    sourcePath = new ProxyPoseSnapshot.Node[path.Count];
                    for (int i = 0; i < path.Count; i++) sourcePath[i] = snapshot.Register(path[i]);
                    sourceVersions = new ulong[sourcePath.Length]; path.Clear();
                    for (Transform current = target; current != common; current = current.parent) path.Add(current);
                    targetPath = new ProxyPoseSnapshot.Node[path.Count];
                    for (int i = 0; i < path.Count; i++) targetPath[i] = snapshot.Register(path[i]);
                    targetVersions = new ulong[targetPath.Length];
                }

                internal Matrix4x4 Capture()
                {
                    if (captured && capturedPass == snapshot.Pass) return relative;
                    // Compose beneath the common ancestor. World-space matrix
                    // inversion would introduce false pose changes on root motion.
                    bool changed = CapturePath(sourcePath, sourceVersions) | CapturePath(targetPath, targetVersions);
                    capturedPass = snapshot.Pass;
                    if (captured && !changed) return relative;
                    Matrix4x4 source = Compose(sourcePath);
                    relative = targetPath.Length == 0 ? source : Compose(targetPath).inverse * source;
                    captured = true; return relative;
                }

                internal bool MatchesHierarchy(Transform source, Transform target) =>
                    MatchesPath(source, sourcePath) && MatchesPath(target, targetPath);

                private bool MatchesPath(Transform current, ProxyPoseSnapshot.Node[] path)
                {
                    foreach (ProxyPoseSnapshot.Node node in path)
                    {
                        if (current != node.Transform) return false;
                        current = current.parent;
                    }
                    return current == common;
                }

                private static bool CapturePath(ProxyPoseSnapshot.Node[] path, ulong[] versions)
                {
                    bool changed = false;
                    for (int i = 0; i < path.Length; i++)
                    {
                        path[i].Capture(); changed |= versions[i] != path[i].PoseVersion;
                        versions[i] = path[i].PoseVersion;
                    }
                    return changed;
                }

                private static Matrix4x4 Compose(ProxyPoseSnapshot.Node[] path)
                {
                    Matrix4x4 result = Matrix4x4.identity;
                    foreach (ProxyPoseSnapshot.Node current in path) result = current.Matrix * result;
                    return result;
                }
            }
        }

        private void Release(Body body, List<Piece> pieces, CombatImpact impact, bool anatomical)
        {
            if (pieces.Count == 0) return;
            var host = new GameObject(anatomical ? "Detached body part" : "Detached tissue");
            host.transform.SetParent(transform, false);
            host.transform.SetPositionAndRotation(impact.Point, Quaternion.identity);
            var marker = host.AddComponent<CombatBodyFragment>(); marker.Owner = body.Actor;
            var rigid = host.AddComponent<Rigidbody>();
            rigid.mass = anatomical ? RegionMass(pieces[0].Region) : .035f;
            rigid.linearDamping = .6f; rigid.angularDamping = 1.1f; rigid.maxAngularVelocity = 14f;
            rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            bool measured = false; Bounds bounds = default;
            foreach (Piece piece in pieces)
            {
                if (piece.Emitted) continue;
                piece.RefreshSurface();
                piece.Emitted = true; piece.Debris = !anatomical;
                if (piece.Baked == null) piece.Baked = new Mesh { name = "Released body surface" };
                piece.Skin.BakeMesh(piece.Baked, true);
                piece.BakedVertices = piece.Baked.vertices;
                var surface = new GameObject("Detached " + piece.Skin.name);
                var renderer = surface.AddComponent<MeshRenderer>(); surface.AddComponent<MeshFilter>().sharedMesh = piece.Baked;
                renderer.sharedMaterials = piece.Skin.sharedMaterials;
                piece.Skin.GetPropertyBlock(properties); renderer.SetPropertyBlock(properties); properties.Clear();
                surface.transform.SetPositionAndRotation(piece.Skin.transform.position, piece.Skin.transform.rotation);
                surface.transform.localScale = piece.Skin.transform.lossyScale;
                surface.transform.SetParent(host.transform, true); piece.Released = renderer;
                piece.Skin.enabled = false;
                Matrix4x4 matrix = host.transform.worldToLocalMatrix * surface.transform.localToWorldMatrix;
                foreach (Vector3 vertex in piece.BakedVertices)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(vertex);
                    if (!measured) { bounds = new Bounds(point, Vector3.zero); measured = true; } else bounds.Encapsulate(point);
                }
            }
            var collider = host.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = Vector3.Max(bounds.size, Vector3.one * .005f);
            foreach (var original in body.Actor.Ragdoll.PhysicsController.AnatomicalColliders) Physics.IgnoreCollision(collider, original.Key, true);
            if (body.Actor.Body != null) Physics.IgnoreCollision(collider, body.Actor.Body, true);
            foreach (Piece piece in body.Pieces) if (piece.Proxy != null) Physics.IgnoreCollision(collider, piece.Proxy, true);
            foreach (Fragment previous in body.Fragments) Physics.IgnoreCollision(collider, previous.Collider, true);
            Rigidbody origin = body.Actor.Ragdoll.PhysicsController.CombatBodyForPart(CombatBodyAnatomy.ToPart(pieces[0].Region));
            Vector3 inherited = origin != null && !origin.isKinematic ? origin.GetPointVelocity(bounds.center + host.transform.position) : Vector3.zero;
            rigid.linearVelocity = inherited + impact.Direction * (anatomical ? 1.1f : 2.4f) + Vector3.up * .6f;
            rigid.angularVelocity = origin != null && !origin.isKinematic ? origin.angularVelocity : new Vector3(2f, 3f, -2f);
            var fragment = new Fragment { Body = rigid, Collider = collider, Anatomical = anatomical };
            foreach (Piece piece in pieces) { fragment.Pieces.Add(piece); piece.Fragment = fragment; }
            body.Fragments.Add(fragment); Freeze(fragment, frozen);
            CombatCorpseTraversal.RegisterCollider(body.Actor, collider, detached: true);
        }

        private void ReleaseDetachedTissue(Body body, List<Piece> pieces, CombatImpact impact)
        {
            if (pieces.Count == 0) return;
            Fragment former = pieces[0].Fragment;
            var host = new GameObject("Detached tissue"); host.transform.SetParent(transform, false);
            host.transform.position = pieces[0].Released.bounds.center;
            host.AddComponent<CombatBodyFragment>().Owner = body.Actor;
            var rigid = host.AddComponent<Rigidbody>(); rigid.mass = .035f;
            rigid.linearDamping = .6f; rigid.angularDamping = 1.1f;
            rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var fragment = new Fragment { Body = rigid, Collider = host.AddComponent<BoxCollider>() };
            foreach (Piece piece in pieces)
            {
                piece.Released.transform.SetParent(host.transform, true);
                piece.Released.enabled = true;
                piece.Debris = true;
                fragment.Pieces.Add(piece); piece.Fragment = fragment;
                former?.Pieces.Remove(piece);
            }
            ResizeFragment(fragment);
            foreach (var original in body.Actor.Ragdoll.PhysicsController.AnatomicalColliders) Physics.IgnoreCollision(fragment.Collider, original.Key, true);
            if (body.Actor.Body != null) Physics.IgnoreCollision(fragment.Collider, body.Actor.Body, true);
            foreach (Piece other in body.Pieces) if (other.Proxy != null) Physics.IgnoreCollision(fragment.Collider, other.Proxy, true);
            foreach (Fragment other in body.Fragments) Physics.IgnoreCollision(fragment.Collider, other.Collider, true);
            rigid.linearVelocity = (former != null ? former.Frozen ? former.Velocity : former.Body.linearVelocity : Vector3.zero) + impact.Direction * 2.4f;
            rigid.angularVelocity = new Vector3(2f, 3f, -2f);
            body.Fragments.Add(fragment); Freeze(fragment, frozen);
            CombatCorpseTraversal.RegisterCollider(body.Actor, fragment.Collider, detached: true);
        }

        private static void ResizeFragment(Fragment fragment)
        {
            bool measured = false; Bounds bounds = default;
            Matrix4x4 inverse = fragment.Body.transform.worldToLocalMatrix;
            foreach (Piece piece in fragment.Pieces)
            {
                if (piece.Released == null || !piece.Released.enabled) continue;
                Matrix4x4 matrix = inverse * piece.Released.transform.localToWorldMatrix;
                foreach (Vector3 vertex in piece.BakedVertices)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(vertex);
                    if (!measured) { bounds = new Bounds(point, Vector3.zero); measured = true; } else bounds.Encapsulate(point);
                }
            }
            SetColliderEnabled(fragment.Collider, measured);
            if (measured)
            {
                SetColliderBounds((BoxCollider)fragment.Collider, bounds.center, Vector3.Max(bounds.size, Vector3.one * .005f));
            }
            if (fragment.Anatomical && fragment.Pieces.Count > 0)
            {
                bool flesh = fragment.Pieces.Exists(p => !p.Bone && p.Released != null && p.Released.enabled);
                fragment.Body.mass = RegionMass(fragment.Pieces[0].Region) * (flesh ? 1f : .2f);
            }
        }

        private static void ApplyDetachedImpulse(Body body, BodyDamageRegion region, CombatImpact impact)
        {
            Fragment selected = null; float best = float.PositiveInfinity;
            foreach (Piece piece in body.Pieces)
                if (piece.Region == region && !piece.Debris && piece.Released != null && piece.Released.enabled)
                {
                    float distance = piece.Released.bounds.SqrDistance(impact.Point);
                    if (distance < best) { best = distance; selected = piece.Fragment; }
                }
            if (selected == null) return;
            selected.PendingImpulse += impact.Impulse;
            selected.ImpactPoint = impact.Point;
        }

        private static float RegionMass(BodyDamageRegion region) => region switch
        {
            BodyDamageRegion.LeftHand or BodyDamageRegion.RightHand => .4f,
            BodyDamageRegion.LeftForearm or BodyDamageRegion.RightForearm => 1.1f,
            BodyDamageRegion.LeftUpperArm or BodyDamageRegion.RightUpperArm => 2.2f,
            BodyDamageRegion.LeftThigh or BodyDamageRegion.RightThigh => 7f,
            BodyDamageRegion.LeftShin or BodyDamageRegion.RightShin => 4f,
            BodyDamageRegion.LeftFoot or BodyDamageRegion.RightFoot => 1f, _ => 8f
        };

        internal void Tick(float seconds)
        {
            if (seconds <= 0f || frozen) return;
            foreach (Body body in bodies.Values)
            {
                if (body.Active)
                {
                    // Physics or a new contact may change a pose within one rendered
                    // frame. Each validation pass samples shared transforms anew.
                    body.ProxyPose.BeginPass();
                    bool settled = body.Actor.State.IsDefeated && body.Actor.Ragdoll.IsSettled;
                    ulong revision = settled ? CaptureBodyPoseRevision(body, beginPass: false) : 0;
                    if (settled && body.AppliedPoseRevision == revision) UnchangedBodyPoseSkips++;
                    else
                    {
                        foreach (Piece piece in body.Pieces)
                        {
                            bool surfaceRefreshed = piece.Skin.enabled;
                            if (surfaceRefreshed) piece.RefreshSurface();
                            bool collides = Collides(body.Actor, piece);
                            bool created = collides && piece.Proxy == null;
                            if (created) CreateProxy(body, piece, surfaceRefreshed);
                            if (piece.Proxy != null)
                            {
                                if (!created && collides && (!piece.Proxy.enabled || piece.Torso != null ||
                                    (piece.Region is BodyDamageRegion.LeftHand or BodyDamageRegion.RightHand) &&
                                    piece.Skin.sharedMesh.blendShapeCount > 0)) UpdateProxyBounds(piece, surfaceRefreshed);
                                SetColliderEnabled(piece.Proxy, collides);
                            }
                        }
                        if (settled) body.AppliedPoseRevision = CaptureBodyPoseRevision(body, beginPass: false);
                        else body.PoseCaptured = false;
                    }
                }
                foreach (Fragment fragment in body.Fragments)
                {
                    if (fragment.PendingImpulse.sqrMagnitude > 0f)
                    {
                        fragment.Body.isKinematic = false; fragment.Settled = false;
                        fragment.Body.AddForceAtPosition(fragment.PendingImpulse, fragment.ImpactPoint, ForceMode.Impulse);
                        fragment.PendingImpulse = Vector3.zero;
                    }
                    fragment.Age += seconds;
                    if (fragment.Age >= 6f && !fragment.Settled && fragment.Body.IsSleeping())
                    { fragment.Body.isKinematic = true; fragment.Settled = true; }
                }
            }
        }
        internal void SetFrozen(bool value)
        { frozen = value; foreach (Body body in bodies.Values) foreach (Fragment fragment in body.Fragments) Freeze(fragment, value); }
        private static void Freeze(Fragment fragment, bool value)
        {
            if (fragment.Settled || fragment.Frozen == value) return;
            if (value) { fragment.Velocity = fragment.Body.linearVelocity; fragment.Spin = fragment.Body.angularVelocity; fragment.Body.isKinematic = true; }
            else { fragment.Body.isKinematic = false; fragment.Body.linearVelocity = fragment.Velocity; fragment.Body.angularVelocity = fragment.Spin; }
            fragment.Frozen = value;
        }
        private void LateUpdate()
        {
            foreach (Body body in bodies.Values) if (body.Active)
                foreach (SkinnedMeshRenderer original in body.Replaced)
                    if (original.enabled) original.enabled = false;
        }

        public void ResetActor(CombatActor actor)
        {
            if (actor == null || !bodies.TryGetValue(actor, out Body body)) return;
            body.PoseCaptured = false;
            actor.Hurtboxes.SetBodySurfaces(null);
            actor.Ragdoll.PhysicsController.SetCombatBodyDamage(null, false);
            foreach (var original in body.Originals) { suppressed.Remove(original.Key); original.Key.enabled = original.Value; }
            foreach (Piece piece in body.Pieces)
            {
                piece.Torso?.Reset();
                piece.Skin.enabled = false; piece.Released = null; piece.Fragment = null;
                piece.Emitted = piece.Debris = piece.QueryOriginal = false;
                if (piece.Proxy != null) SetColliderEnabled(piece.Proxy, false);
            }
            foreach (Fragment fragment in body.Fragments)
            {
                CombatCorpseTraversal.ReleaseCollider(fragment.Collider);
                if (fragment.Body != null) { fragment.Body.gameObject.SetActive(false); Destroy(fragment.Body.gameObject); }
            }
            body.Fragments.Clear(); body.Detached.Clear(); body.Replaced.Clear(); body.Active = false;
            body.VolleyDepth = 0; body.VolleyNeedsSynchronization = false;
            RetainAuthoredIntactQueries(body);
        }
        public void ResetRound() { foreach (CombatActor actor in bodies.Keys) ResetActor(actor); frozen = false; }
        private void OnDestroy()
        {
            foreach (Body body in bodies.Values)
            {
                owners.Remove(body.Actor);
                body.Actor.Hurtboxes?.SetBodySurfaces(null);
                foreach (var original in body.Originals) suppressed.Remove(original.Key);
                foreach (Fragment fragment in body.Fragments) CombatCorpseTraversal.ReleaseCollider(fragment.Collider);
                foreach (Piece piece in body.Pieces)
                {
                    CombatCorpseTraversal.ReleaseCollider(piece.Proxy);
                    if (piece.Baked != null) Destroy(piece.Baked);
                    if (piece.ProxyMesh != null) Destroy(piece.ProxyMesh);
                    piece.ProxyGeometry?.Dispose();
                    piece.Torso?.Dispose();
                    piece.Deformation?.Dispose();
                    if (piece.Skin != null) Destroy(piece.Skin.gameObject);
                    if (piece.Proxy != null) Destroy(piece.Proxy.gameObject);
                }
            }
            if (flesh != null) Destroy(flesh); if (bone != null) Destroy(bone);
        }
        private Material RequireMaterial(bool skeleton)
        {
            Material material = skeleton ? bone : flesh;
            if (material != null) return material;
            Shader shader = Resources.Load<Shader>("Shaders/Lit");
            Texture2D texture = Resources.Load<Texture2D>("CombatGore/" + (skeleton ? "BoneSurface" : "FleshSurface"));
            if (shader == null || texture == null) throw new InvalidOperationException("Missing authored body surfaces.");
            material = new Material(shader) { name = skeleton ? "Combat Bone Shared" : "Combat Flesh Shared" };
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .14f); material.SetFloat("_Cull", 2f);
            if (skeleton) bone = material; else flesh = material;
            return material;
        }
    }

    /// <summary>Detached anatomy is queried by its owner, never classified as a wall.</summary>
    internal sealed class CombatBodyFragment : MonoBehaviour { internal CombatActor Owner; }
}
