using System;
using System.Collections.Generic;
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
            internal Mesh Baked, ProxyMesh;
            internal Vector3[] BakedVertices;
            internal BodyDamageRegion Region;
            internal int Patch;
            internal bool Flesh, Bone, Eligible, Emitted, Debris, QueryOriginal;
            internal BoxCollider Proxy;
            internal ProxyGeometry ProxyGeometry;
            internal CentrePose CentrePose;
            internal CombatBodySourceDeformation Deformation;
            internal CombatTorsoDamageSurface Torso;
            internal Fragment Fragment;
            internal uint GeometryVersion => unchecked((Deformation?.GeometryVersion ?? 0u) + (Torso?.GeometryVersion ?? 0u));
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
            internal int VolleyDepth;
            internal bool VolleyNeedsSynchronization;
            internal CombatImpact LastVolleyImpact;
            internal readonly List<Piece> Pieces = new List<Piece>();
            internal readonly List<Piece>[] Regions = new List<Piece>[CombatBodyDamageState.RegionCount];
            internal readonly Dictionary<SkinnedMeshRenderer, bool> Originals = new Dictionary<SkinnedMeshRenderer, bool>();
            internal readonly Dictionary<BodyDamageRegion, Transform> Bones = new Dictionary<BodyDamageRegion, Transform>();
            internal readonly HashSet<BodyDamageRegion> Detached = new HashSet<BodyDamageRegion>();
            internal readonly HashSet<SkinnedMeshRenderer> Replaced = new HashSet<SkinnedMeshRenderer>();
            internal readonly List<Fragment> Fragments = new List<Fragment>();
            internal Body()
            {
                for (int region = 0; region < Regions.Length; region++) Regions[region] = new List<Piece>();
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
        private MaterialPropertyBlock properties;
        private Material flesh, bone;
        private bool frozen;
        private void Awake() => properties = new MaterialPropertyBlock();
        internal int ProxyGeometryBuilds { get; private set; }
        internal long ProxyGeometryTicks { get; private set; }
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
            out BodyDamageRegion region, out int patch)
        {
            region = BodyDamageRegion.Chest; patch = -1;
            if (!owners.TryGetValue(actor, out CombatBodyDestruction owner) || !owner.bodies.TryGetValue(actor, out Body body)) return false;
            float closest = float.PositiveInfinity;
            foreach (Piece piece in body.Pieces)
            {
                if (piece.Source != source || piece.Flesh || piece.Bone) continue;
                float candidate = (SkinCentre(piece) - point).sqrMagnitude;
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
            GameObject model = Resources.Load<GameObject>("CombatGore/Body" + (actor.IsHero ? "Hero" : "Npc"));
            if (model == null) throw new InvalidOperationException("Missing authored combat body model.");
            var body = new Body { Actor = actor };
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
                int patch = patchAt < 0 ? 0 : int.Parse(label.Substring(patchAt + 5));
                var host = new GameObject("Body " + template.name);
                host.transform.SetParent(source.transform, false);
                var skin = host.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = template.sharedMesh; skin.bones = source.bones; skin.rootBone = source.rootBone;
                skin.localBounds = source.localBounds; skin.updateWhenOffscreen = true;
                skin.sharedMaterials = isBone || isFlesh ? new[] { RequireMaterial(isBone) } : source.sharedMaterials;
                if (!isFlesh && !isBone)
                { source.GetPropertyBlock(properties); skin.SetPropertyBlock(properties); properties.Clear(); }
                skin.shadowCastingMode = ShadowCastingMode.On; skin.receiveShadows = true; skin.enabled = false;
                bool visible = source.enabled && source.gameObject.activeInHierarchy;
                var piece = new Piece { Skin = skin, Source = source, Region = (BodyDamageRegion)region,
                    Patch = patch, Flesh = isFlesh, Bone = isBone, Eligible = isFlesh || isBone || visible };
                if (!isFlesh && !isBone || (region is 7 or 10))
                    piece.Deformation = new CombatBodySourceDeformation(template.sharedMesh, source);
                if (piece.Eligible && !isBone && CombatBodyDamageState.IsTorso(piece.Region))
                    piece.Torso = new CombatTorsoDamageSurface(skin, isFlesh);
                body.Pieces.Add(piece);
                body.Regions[region].Add(piece);
                if (!isFlesh && !isBone && !body.Originals.ContainsKey(source)) body.Originals.Add(source, visible);
            }
            if (body.Pieces.Count == 0) throw new InvalidOperationException("Empty authored combat body.");
            foreach (Piece piece in body.Pieces)
                if (piece.Eligible && !piece.Bone)
                { piece.RefreshSurface(); SkinCentre(piece); piece.Torso?.PrepareExteriorMesh(); }
            bodies.Add(actor, body);
            owners[actor] = this;
            actor.Hurtboxes.PrepareBodySurfaces(body.Pieces);
        }

        /// <summary>One actual resolved contact; structural damage is independent of health delta.</summary>
        public void Apply(CombatImpact impact)
        {
            if (impact.Target == null || impact.Result is not (MeleeHitResult.Hit or MeleeHitResult.GuardBroken)) return;
            if (impact.Kind is CombatImpactKind.Shove or CombatImpactKind.Kick) return;
            BodyDamageRegion region = impact.BodyRegion ?? CombatBodyAnatomy.ToRegion(impact.Part);
            if (!bodies.TryGetValue(impact.Target, out Body body)) return;
            if (impact.DetachedPart) ApplyDetachedImpulse(body, region, impact);
            int patch = impact.BodyPatch >= 0 ? impact.BodyPatch : region == BodyDamageRegion.Head ?
                GetComponent<CombatHeadDestruction>().TissuePatchFor(impact.Target, impact.Point) : ClosestPatch(body, region, impact.Point);
            float trauma = impact.Kind == CombatImpactKind.Projectile ?
                (impact.IsPellet ? Mathf.Clamp(impact.WoundDamage / 24f, 0f, 1.1f) * .50f : .18f) :
                Mathf.Lerp(.10f, .24f, Mathf.Clamp01(impact.AttackPower));
            float tissueTrauma = trauma;
            if (region == BodyDamageRegion.Head)
            {
                // The existing sixteen-sector fracture system owns skull continuity,
                // including its fourteen-sector cap for one shotgun volley.
                bool skullGone = GetComponent<CombatHeadDestruction>().DetachedSectorCountFor(impact.Target) == 16;
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

        private static Vector3 SkinCentre(Piece piece)
        {
            // Interior pieces use an anatomical source binding. Using the dominant
            // centre vertex avoids reading a renderer's stale, whole-body bounds.
            piece.CentrePose ??= new CentrePose(piece.Skin);
            return piece.CentrePose.Evaluate(piece);
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

            internal Vector3 Evaluate(Piece piece)
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
                Matrix4x4 p0 = weight.weight0 > 0f ? bones[weight.boneIndex0].localToWorldMatrix : default;
                Matrix4x4 p1 = weight.weight1 > 0f ? bones[weight.boneIndex1].localToWorldMatrix : default;
                Matrix4x4 p2 = weight.weight2 > 0f ? bones[weight.boneIndex2].localToWorldMatrix : default;
                Matrix4x4 p3 = weight.weight3 > 0f ? bones[weight.boneIndex3].localToWorldMatrix : default;
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
            ActorSynchronizationCount++;
            long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            GetComponent<CombatHeadDestruction>().SynchronizeTissue(actor, captureImmediately: false);
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
                var selected = new List<Piece>();
                foreach (Piece piece in body.Regions[r])
                    if (piece.Eligible && !piece.Emitted &&
                        (piece.Bone || actor.BodyDamage.TissueLoss(region, piece.Patch) < 1f)) selected.Add(piece);
                Release(body, selected, impact, true);
                BodyDamageRegion parent = CombatBodyAnatomy.Parent(region);
                if (actor.BodyDamage.IsAttached(parent) && body.Bones.TryGetValue(parent, out Transform retained) &&
                    body.Bones.TryGetValue(region, out Transform cut))
                {
                    GetComponent<CombatBloodEffects>()?.SetBodyBleedSource(actor, region, retained, cut.position,
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
                    var retained = new List<Piece>();
                    var released = new List<Piece>();
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
                if (collides && piece.Proxy == null) CreateProxy(body, piece);
                if (piece.Proxy != null)
                {
                    SetColliderEnabled(piece.Proxy, collides);
                    if (collides && piece.Torso != null) UpdateProxyBounds(piece);
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

        private void CreateProxy(Body body, Piece piece)
        {
            if (piece.Proxy != null) return;
            if (!body.Bones.TryGetValue(piece.Region, out Transform target)) return;
            var proxy = new GameObject("Remaining body " + piece.Region + " " + piece.Patch);
            proxy.transform.SetParent(target, false);
            piece.Proxy = proxy.AddComponent<BoxCollider>(); piece.Proxy.enabled = false;
            body.Actor.Ragdoll.PhysicsController.RegisterCombatBodyCollider(piece.Proxy);
            UpdateProxyBounds(piece);
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

        private void UpdateProxyBounds(Piece piece)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { RefreshProxyBounds(piece); }
            finally { ProxyGeometryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start; }
        }

        private void RefreshProxyBounds(Piece piece)
        {
            piece.RefreshSurface();
            piece.ProxyGeometry ??= new ProxyGeometry();
            Transform target = piece.Proxy.transform.parent;
            if (!piece.ProxyGeometry.Capture(piece, target, out Matrix4x4 matrix, out Vector3 minimum)) return;
            piece.ProxyMesh ??= new Mesh { name = "Remaining anatomy collision" };
            piece.Skin.BakeMesh(piece.ProxyMesh, true);
            ProxyGeometryBuilds++;
            LastProxyCacheInvalidation = piece.ProxyGeometry.LastInvalidation;
            List<Vector3> vertices = piece.ProxyGeometry.Vertices;
            piece.ProxyMesh.GetVertices(vertices);
            int[] indices = piece.ProxyGeometry.ActiveIndices;
            if (indices.Length == 0) { SetColliderEnabled(piece.Proxy, false); return; }
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero);
            foreach (int index in indices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[index]));
            SetColliderBounds(piece.Proxy, bounds.center, Vector3.Max(bounds.size, minimum));
        }

        private static void SetColliderEnabled(Collider collider, bool enabled)
        { if (collider.enabled != enabled) collider.enabled = enabled; }

        private static void SetColliderBounds(BoxCollider collider, Vector3 centre, Vector3 size)
        {
            if (!collider.center.Equals(centre)) collider.center = centre;
            if (!collider.size.Equals(size)) collider.size = size;
        }

        internal sealed class ProxyGeometry
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal int[] ActiveIndices { get; private set; } = Array.Empty<int>();
            private Mesh mesh;
            private Transform[] bones;
            private int[] influencedBones;
            private Matrix4x4[] relativeBones;
            private RelativePose[] relativePoses;
            private float[] blendWeights;
            private Vector3 minimumSize, rendererScale;
            private ScalePose targetScalePose, rendererScalePose;
            private uint geometryVersion;
            private bool captured;
            internal ProxyCacheInvalidation LastInvalidation { get; private set; }

            internal bool Capture(Piece piece, Transform target, out Matrix4x4 matrix, out Vector3 minimum)
            {
                Mesh current = piece.Skin.sharedMesh;
                if (mesh != current)
                {
                    mesh = current; bones = piece.Skin.bones;
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
                        relativePoses[i] = new RelativePose(bones[influencedBones[i]], target);
                    captured = false;
                }
                Matrix4x4 inverse = target.worldToLocalMatrix;
                matrix = inverse * piece.Skin.transform.localToWorldMatrix;
                targetScalePose ??= new ScalePose(target);
                rendererScalePose ??= new ScalePose(piece.Skin.transform);
                bool targetScaleChanged = targetScalePose.Capture(out Vector3 scale);
                minimum = new Vector3(.003f / Mathf.Max(.000001f, Mathf.Abs(scale.x)),
                    .003f / Mathf.Max(.000001f, Mathf.Abs(scale.y)), .003f / Mathf.Max(.000001f, Mathf.Abs(scale.z)));
                bool rendererScaleChanged = rendererScalePose.Capture(out Vector3 currentRendererScale);
                uint currentVersion = piece.GeometryVersion;
                if (!captured || geometryVersion != currentVersion) ActiveIndices = current.triangles;
                // BakeMesh compensates its renderer's position/rotation. That
                // conversion cancels when the baked points enter the proxy frame;
                // a shared rigid actor move cannot change these local bounds.
                ProxyCacheInvalidation invalidation = captured ? ProxyCacheInvalidation.None : ProxyCacheInvalidation.Initial;
                if (geometryVersion != currentVersion) invalidation |= ProxyCacheInvalidation.Geometry;
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
                return invalidation != ProxyCacheInvalidation.None;
            }

            private sealed class ScalePose
            {
                private readonly Transform[] path;
                private readonly Vector3[] scales;

                internal ScalePose(Transform target)
                {
                    var ancestors = new List<Transform>();
                    for (Transform current = target; current != null; current = current.parent) ancestors.Add(current);
                    ancestors.Reverse(); path = ancestors.ToArray(); scales = new Vector3[path.Length];
                }

                internal bool Capture(out Vector3 scale)
                {
                    float uniform = 1f;
                    Matrix4x4 matrix = Matrix4x4.identity;
                    bool anisotropic = false, changed = false;
                    for (int i = 0; i < path.Length; i++)
                    {
                        Vector3 local = path[i].localScale;
                        changed |= !scales[i].Equals(local); scales[i] = local;
                        if (!anisotropic)
                        {
                            float x = Mathf.Abs(local.x), y = Mathf.Abs(local.y), z = Mathf.Abs(local.z);
                            if (x.Equals(y) && x.Equals(z)) { uniform *= x; continue; }
                            anisotropic = true; matrix = Matrix4x4.Scale(local);
                        }
                        else matrix *= Matrix4x4.TRS(Vector3.zero, path[i].localRotation, local);
                    }
                    // Leading scaled orthogonal factors cannot alter column
                    // lengths. Drop them, including the first anisotropic node's
                    // rotation; keep every later rotation that can produce shear.
                    scale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude,
                        matrix.GetColumn(2).magnitude) * uniform;
                    return changed;
                }
            }

            private sealed class RelativePose
            {
                private readonly Transform[] sourcePath, targetPath;

                internal RelativePose(Transform source, Transform target)
                {
                    var ancestors = new HashSet<Transform>();
                    for (Transform current = target; current != null; current = current.parent) ancestors.Add(current);
                    var path = new List<Transform>();
                    Transform common = source;
                    while (common != null && !ancestors.Contains(common))
                    { path.Add(common); common = common.parent; }
                    sourcePath = path.ToArray(); path.Clear();
                    for (Transform current = target; current != common; current = current.parent) path.Add(current);
                    targetPath = path.ToArray();
                }

                internal Matrix4x4 Capture()
                {
                    // Compose beneath the common ancestor. World-space matrix
                    // inversion would introduce false pose changes on root motion.
                    Matrix4x4 source = Compose(sourcePath);
                    return targetPath.Length == 0 ? source : Compose(targetPath).inverse * source;
                }

                private static Matrix4x4 Compose(Transform[] path)
                {
                    Matrix4x4 result = Matrix4x4.identity;
                    foreach (Transform current in path)
                        result = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * result;
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
                if (body.Active) foreach (Piece piece in body.Pieces)
                {
                    if (piece.Skin.enabled) piece.RefreshSurface();
                    bool collides = Collides(body.Actor, piece);
                    if (collides && piece.Proxy == null) CreateProxy(body, piece);
                    if (piece.Proxy != null)
                    {
                        SetColliderEnabled(piece.Proxy, collides);
                        if (collides && (piece.Torso != null || (piece.Region is BodyDamageRegion.LeftHand or BodyDamageRegion.RightHand) &&
                            piece.Skin.sharedMesh.blendShapeCount > 0)) UpdateProxyBounds(piece);
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
        { foreach (Body body in bodies.Values) if (body.Active) foreach (SkinnedMeshRenderer original in body.Replaced) original.enabled = false; }

        public void ResetActor(CombatActor actor)
        {
            if (actor == null || !bodies.TryGetValue(actor, out Body body)) return;
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
                if (fragment.Body != null) { fragment.Body.gameObject.SetActive(false); Destroy(fragment.Body.gameObject); }
            body.Fragments.Clear(); body.Detached.Clear(); body.Replaced.Clear(); body.Active = false;
            body.VolleyDepth = 0; body.VolleyNeedsSynchronization = false;
        }
        public void ResetRound() { foreach (CombatActor actor in bodies.Keys) ResetActor(actor); frozen = false; }
        private void OnDestroy()
        {
            foreach (Body body in bodies.Values)
            {
                owners.Remove(body.Actor);
                body.Actor.Hurtboxes?.SetBodySurfaces(null);
                foreach (var original in body.Originals) suppressed.Remove(original.Key);
                foreach (Piece piece in body.Pieces)
                {
                    if (piece.Baked != null) Destroy(piece.Baked);
                    if (piece.ProxyMesh != null) Destroy(piece.ProxyMesh);
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
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
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
