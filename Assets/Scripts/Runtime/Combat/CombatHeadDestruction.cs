using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Persistent, source-skinned fractures. Authored sectors detach only once.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatHeadDestruction : MonoBehaviour
    {
        private sealed class Piece
        {
            internal SkinnedMeshRenderer Skin;
            internal SkinnedMeshRenderer Source;
            internal MeshRenderer Released;
            internal Mesh Baked, SoftMesh;
            internal CombatBrainTissue Tissue;
            internal Bounds Bounds;
            internal bool Visible, Anatomical, Detached;
        }
        private sealed class Sector
        {
            internal readonly List<Piece> Pieces = new List<Piece>();
            internal Bounds Bounds, ContactBounds;
            internal bool HasContactBounds;
            internal BoxCollider Proxy;
            internal bool Detached;
            internal int Index;
        }
        private sealed class Fragment
        {
            internal Rigidbody Body;
            internal Collider Collider;
            internal Vector3 Velocity, Spin;
            internal bool Frozen, Settled;
            internal float Age;
            internal CombatBrainTissue Tissue;
        }
        private sealed class Head
        {
            internal CombatActor Actor;
            internal Transform Bone, Neck;
            internal Bounds Bounds;
            internal readonly Sector[] Sectors = new Sector[16];
            internal readonly List<Piece> Brains = new List<Piece>();
            internal readonly List<SkinnedMeshRenderer> Originals = new List<SkinnedMeshRenderer>();
            internal readonly List<bool> OriginalVisibility = new List<bool>();
            internal readonly List<Fragment> Fragments = new List<Fragment>();
            internal readonly List<GameObject> Hosts = new List<GameObject>();
            internal int BrainCount;
            internal bool Active;
            internal Vector3 PreviousPosition, PreviousVelocity;
        }

        private static readonly HashSet<Renderer> suppressed = new HashSet<Renderer>();
        private readonly Dictionary<CombatActor, Head> heads = new Dictionary<CombatActor, Head>();
        private MaterialPropertyBlock properties;
        private Material flesh, brain;
        private bool frozen;
        private void Awake() => properties = new MaterialPropertyBlock();
        public Vector3 LastEjectionDirection { get; private set; }
        internal static bool IsSuppressed(Renderer renderer) => suppressed.Contains(renderer);
        public int DetachedSectorCountFor(CombatActor actor)
        {
            int count = 0;
            if (actor != null && heads.TryGetValue(actor, out Head head))
                foreach (Sector sector in head.Sectors) if (sector != null && sector.Detached) count++;
            return count;
        }
        public int RetainedSectorCountFor(CombatActor actor)
        {
            int count = 0;
            if (actor != null && heads.TryGetValue(actor, out Head head) && head.Active)
                foreach (Sector sector in head.Sectors) if (sector != null && !sector.Detached) count++;
            return count;
        }
        public int BrainFragmentCountFor(CombatActor actor) => actor != null && heads.TryGetValue(actor, out Head head) ? head.BrainCount : 0;
        public int RetainedBrainCountFor(CombatActor actor) => actor != null && heads.TryGetValue(actor, out Head head) ? head.Brains.Count - head.BrainCount : 0;
        public float BrainDeformationFor(CombatActor actor)
        {
            float amount = 0f;
            if (actor != null && heads.TryGetValue(actor, out Head head))
            {
                foreach (Piece piece in head.Brains) if (!piece.Detached) amount = Mathf.Max(amount, piece.Tissue.Deformation);
                foreach (Fragment fragment in head.Fragments) if (fragment.Tissue != null) amount = Mathf.Max(amount, fragment.Tissue.Deformation);
            }
            return amount;
        }
        internal void PrepareActor(CombatActor actor) => RequireHead(actor);
        public int DestroyedMaskFor(CombatActor actor)
        {
            int mask = 0;
            if (actor != null && heads.TryGetValue(actor, out Head head))
                foreach (Sector sector in head.Sectors) if (sector != null && sector.Detached) mask |= 1 << sector.Index;
            return mask;
        }
        public Transform HeadFrameFor(CombatActor actor)
        {
            if (actor == null) return null;
            if (heads.TryGetValue(actor, out Head head)) return head.Bone;
            foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == "head") return bone;
            return null;
        }
        public float DebrisAgeFor(CombatActor actor) => actor != null && heads.TryGetValue(actor, out Head head) &&
            head.Fragments.Count > 0 ? head.Fragments[0].Age : 0f;
        public bool TryGetRetainedTarget(CombatActor actor, out Vector3 point)
        {
            point = Vector3.zero;
            if (actor == null || !heads.TryGetValue(actor, out Head head) || !head.Active) return false;
            foreach (Sector sector in head.Sectors)
                if (sector != null && !sector.Detached)
                { point = head.Bone.TransformPoint(sector.Bounds.center); return true; }
            return false;
        }

        internal void Apply(CombatImpact impact, CombatBloodEffects blood)
        {
            if (impact.Kind != CombatImpactKind.Projectile || impact.Location.Region != MeleeBodyRegion.Head ||
                impact.Target == null || !impact.Target.State.IsDefeated) return;
            Head head = RequireHead(impact.Target);
            if (!head.Active) Activate(head);
            Vector3 localPoint = impact.LocalPoint;
            Vector3 localDirection = impact.LocalDirection.sqrMagnitude > .0001f ? impact.LocalDirection :
                head.Bone.InverseTransformVector(impact.Direction).normalized;
            Vector3 extent = head.Bounds.extents;
            Vector3 point = Divide(localPoint - head.Bounds.center, extent);
            Vector3 direction = Divide(localDirection, extent).normalized;
            var available = new List<Sector>(16);
            foreach (Sector sector in head.Sectors) if (sector != null && !sector.Detached) available.Add(sector);
            if (available.Count == 0) return;
            // The chord through the skull determines both the lost silhouette and
            // wider exit-side breakup. This is independent of actor/world yaw.
            available.Sort((a, b) => Score(a).CompareTo(Score(b)));
            float offset = Vector3.ProjectOnPlane(point, direction).magnitude;
            int amount = Mathf.Min(available.Count, Mathf.RoundToInt(Mathf.Lerp(5f, 3f, Mathf.Clamp01(offset))));
            for (int i = 0; i < amount; i++)
            {
                Sector sector = available[i];
                sector.Detached = true;
                sector.Proxy.enabled = false;
                Vector3 origin = head.Bone.TransformPoint(sector.Bounds.center);
                Vector3 outward = (origin - head.Bone.TransformPoint(head.Bounds.center)).normalized;
                Release(head, sector.Pieces, origin, impact.Direction * (1.4f + i * .12f) + outward * .7f + Vector3.up * .35f, false, sector.Index);
            }
            var retainedBrains = new List<Piece>(8);
            foreach (Piece piece in head.Brains) if (!piece.Detached) retainedBrains.Add(piece);
            retainedBrains.Sort((a, b) => BrainScore(a).CompareTo(BrainScore(b)));
            int lostSectors = DetachedSectorCountFor(head.Actor);
            int targetBrainCount = lostSectors == head.Sectors.Length ? head.Brains.Count :
                Mathf.Clamp(Mathf.RoundToInt(head.Brains.Count * lostSectors / (float)head.Sectors.Length), 1, head.Brains.Count - 1);
            int releaseBrains = Mathf.Min(retainedBrains.Count, targetBrainCount - head.BrainCount);
            for (int i = 0; i < retainedBrains.Count; i++)
            {
                Piece piece = retainedBrains[i];
                if (i < releaseBrains)
                {
                    Vector3 origin = head.Bone.TransformPoint(piece.Bounds.center);
                    Vector3 side = Vector3.Cross(impact.Direction, Vector3.up).normalized;
                    if (side.sqrMagnitude < .1f) side = head.Bone.right;
                    float spread = (i - (releaseBrains - 1) * .5f) * .16f;
                    Release(head, new List<Piece> { piece }, origin,
                        impact.Direction * (1.7f + i * .16f) + side * spread + Vector3.up * (.6f + i * .05f), true, i);
                    piece.Detached = true;
                    head.BrainCount++;
                }
                else piece.Tissue.Impulse(localDirection, .65f);
            }
            LastEjectionDirection = impact.Direction;
            var proxies = new List<BoxCollider>(16);
            var surfaces = new List<SkinnedMeshRenderer>();
            foreach (Sector sector in head.Sectors)
                if (sector != null && !sector.Detached)
                {
                    proxies.Add(sector.Proxy);
                    foreach (Piece piece in sector.Pieces)
                        if (piece.Visible && piece.Anatomical) surfaces.Add(piece.Skin);
                }
            foreach (Piece piece in head.Brains) if (!piece.Detached) surfaces.Add(piece.Skin);
            head.Actor.Hurtboxes.SetHeadShapes(proxies, surfaces);
            head.Actor.Ragdoll.PhysicsController.SetCombatHeadCollisionEnabled(false);
            blood?.SetHeadBleedSource(head.Actor, proxies.Count > 0 ? head.Bone : head.Neck,
                proxies.Count > 0 ? head.Bone.TransformPoint(Vector3.Lerp(localPoint, head.Bounds.center, .45f)) : head.Bone.position,
                proxies.Count > 0 ? -impact.Direction : (head.Bone.position - head.Neck.position).normalized);

            float Score(Sector sector)
            {
                Vector3 centre = Divide(sector.Bounds.center - head.Bounds.center, extent);
                Vector3 delta = centre - point;
                return Vector3.ProjectOnPlane(delta, direction).sqrMagnitude - Vector3.Dot(delta, direction) * .45f;
            }
            float BrainScore(Piece piece)
            {
                Vector3 delta = Divide(piece.Bounds.center - head.Bounds.center, extent) - point;
                return Vector3.ProjectOnPlane(delta, direction).sqrMagnitude - Vector3.Dot(delta, direction) * .45f;
            }
        }

        private Head RequireHead(CombatActor actor)
        {
            if (heads.TryGetValue(actor, out Head existing)) return existing;
            GameObject model = Resources.Load<GameObject>("CombatGore/Head" + (actor.IsHero ? "Hero" : "Npc"));
            if (model == null) throw new InvalidOperationException("Missing authored combat fracture model.");
            var head = new Head { Actor = actor };
            var originals = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer source in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                originals[source.name] = source;
            if (!originals.TryGetValue("GEO_Head", out SkinnedMeshRenderer skull))
                throw new InvalidOperationException("Combat fracture requires the original head skin.");
            foreach (Transform bone in skull.bones) if (bone.name == "head") head.Bone = bone;
            if (head.Bone == null) throw new InvalidOperationException("Combat fracture requires the original head bone.");
            foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == "neck") head.Neck = bone;
            if (head.Neck == null) throw new InvalidOperationException("Combat fracture requires the original neck bone.");
            bool hasBounds = false;
            var included = new HashSet<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer template in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string[] names = template.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (names.Length != 2) throw new InvalidOperationException("Invalid fracture surface: " + template.name);
                bool isBrain = names[0].StartsWith("Brain", StringComparison.Ordinal);
                bool interior = names[1] == "Interior";
                string sourceName = interior ? "GEO_Head" : names[1];
                if (!originals.TryGetValue(sourceName, out SkinnedMeshRenderer source))
                    throw new InvalidOperationException("Fracture source missing: " + sourceName);
                var host = new GameObject("Fracture " + template.name);
                head.Hosts.Add(host);
                host.transform.SetParent(source.transform, false);
                var skin = host.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = template.sharedMesh; skin.bones = source.bones; skin.rootBone = source.rootBone;
                skin.localBounds = source.localBounds; skin.updateWhenOffscreen = true;
                skin.sharedMaterials = isBrain ? new[] { RequireMaterial(true) } : interior ? new[] { RequireMaterial(false) } : source.sharedMaterials;
                if (!interior && !isBrain) { source.GetPropertyBlock(properties); skin.SetPropertyBlock(properties); properties.Clear(); }
                skin.shadowCastingMode = ShadowCastingMode.On; skin.receiveShadows = true;
                bool sourceVisible = source.enabled || Player3DHeadVisibility.IsTemporarilyHidden(source);
                var piece = new Piece { Skin = skin, Source = source, Visible = interior || isBrain || sourceVisible && source.gameObject.activeInHierarchy,
                    Anatomical = interior || CombatHurtboxes.IsHeadFlesh(sourceName) };
                skin.enabled = false;
                Player3DHeadVisibility.RegisterDerived(source, skin);
                if (isBrain)
                {
                    piece.Bounds = MeasureInHead(head, skin);
                    piece.SoftMesh = Instantiate(template.sharedMesh);
                    piece.SoftMesh.name = "Soft " + template.name;
                    skin.sharedMesh = piece.SoftMesh;
                    piece.Tissue = host.AddComponent<CombatBrainTissue>();
                    head.Brains.Add(piece); continue;
                }
                if (!int.TryParse(names[0].Substring(6), out int index) || index < 0 || index >= head.Sectors.Length)
                    throw new InvalidOperationException("Invalid fracture sector: " + template.name);
                Sector sector = head.Sectors[index] ?? (head.Sectors[index] = new Sector { Index = index });
                if (piece.Visible && piece.Anatomical)
                {
                    Bounds bounds = MeasureInHead(head, skin);
                    if (!sector.HasContactBounds) { sector.ContactBounds = bounds; sector.HasContactBounds = true; }
                    else sector.ContactBounds.Encapsulate(bounds);
                    if (interior)
                    {
                        // Ears belong to collision. Only the skull interior owns
                        // the fracture chord; hair/headwear enlarge neither.
                        sector.Bounds = bounds;
                        if (!hasBounds) { head.Bounds = bounds; hasBounds = true; } else head.Bounds.Encapsulate(bounds);
                    }
                }
                sector.Pieces.Add(piece);
                if (!interior && included.Add(source)) { head.Originals.Add(source); head.OriginalVisibility.Add(sourceVisible); }
            }
            Bounds brainBounds = head.Brains[0].SoftMesh.bounds;
            foreach (Piece piece in head.Brains) brainBounds.Encapsulate(piece.SoftMesh.bounds);
            foreach (Piece piece in head.Brains)
            {
                piece.Tissue.Initialize(piece.SoftMesh, piece.Skin.transform, brainBounds, false);
                Player3DHeadVisibility.SetDerivedEnabled(piece.Source, piece.Skin, true);
            }
            foreach (Sector sector in head.Sectors)
            {
                if (sector == null) throw new InvalidOperationException("Fracture must contain every authored sector.");
                var proxy = new GameObject("Retained Head Sector " + sector.Index);
                head.Hosts.Add(proxy);
                proxy.transform.SetParent(head.Bone, false);
                sector.Proxy = proxy.AddComponent<BoxCollider>();
                sector.Proxy.center = sector.ContactBounds.center;
                sector.Proxy.size = sector.ContactBounds.size;
                sector.Proxy.enabled = false;
            }
            heads.Add(actor, head);
            head.PreviousPosition = head.Bone.position;
            return head;
        }

        private static Bounds MeasureInHead(Head head, SkinnedMeshRenderer skin)
        {
            var mesh = new Mesh();
            // Compensate the imported renderer scale before applying its full
            // local-to-world matrix (the source FBX carries a 100x transform).
            skin.BakeMesh(mesh, true);
            Matrix4x4 matrix = head.Bone.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertex));
            Destroy(mesh);
            return bounds;
        }

        private static void Activate(Head head)
        {
            head.Active = true;
            foreach (SkinnedMeshRenderer original in head.Originals) { suppressed.Add(original); original.enabled = false; }
            foreach (Sector sector in head.Sectors)
            {
                sector.Proxy.enabled = true;
                foreach (Piece piece in sector.Pieces) Player3DHeadVisibility.SetDerivedEnabled(piece.Source, piece.Skin, piece.Visible);
                foreach (var anatomical in head.Actor.Ragdoll.PhysicsController.AnatomicalColliders)
                    Physics.IgnoreCollision(sector.Proxy, anatomical.Key, true);
                foreach (Sector other in head.Sectors)
                    if (other != sector) Physics.IgnoreCollision(sector.Proxy, other.Proxy, true);
            }
        }

        private void Release(Head head, List<Piece> pieces, Vector3 origin, Vector3 velocity, bool soft, int index)
        {
            var host = new GameObject(soft ? "Brain Fragment" : "Head Fragment");
            host.transform.SetParent(transform, false);
            host.transform.SetPositionAndRotation(origin, head.Bone.rotation);
            var body = host.AddComponent<Rigidbody>();
            body.mass = soft ? .035f : .09f; body.linearDamping = soft ? .75f : .35f; body.angularDamping = soft ? 2.2f : .8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxAngularVelocity = 20f;
            bool measured = false;
            Bounds localBounds = default;
            foreach (Piece piece in pieces)
            {
                if (!piece.Visible) continue;
                if (piece.Baked == null) { piece.Baked = new Mesh { name = "Fractured Head Skin" }; piece.Baked.MarkDynamic(); }
                piece.Skin.BakeMesh(piece.Baked, true);
                Player3DHeadVisibility.SetDerivedEnabled(piece.Source, piece.Skin, false);
                suppressed.Add(piece.Skin);
                if (piece.Released == null)
                {
                    var surface = new GameObject("Detached " + piece.Skin.name);
                    piece.Released = surface.AddComponent<MeshRenderer>();
                    surface.AddComponent<MeshFilter>().sharedMesh = piece.Baked;
                    piece.Released.sharedMaterials = piece.Skin.sharedMaterials;
                    piece.Skin.GetPropertyBlock(properties); piece.Released.SetPropertyBlock(properties); properties.Clear();
                }
                Transform target = piece.Released.transform, source = piece.Skin.transform;
                target.SetParent(null, false);
                target.SetPositionAndRotation(source.position, source.rotation);
                target.localScale = source.lossyScale;
                target.SetParent(host.transform, true);
                piece.Released.gameObject.SetActive(true);
                Matrix4x4 matrix = host.transform.worldToLocalMatrix * target.localToWorldMatrix;
                foreach (Vector3 vertex in piece.Baked.vertices)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(vertex);
                    if (!measured) { localBounds = new Bounds(point, Vector3.zero); measured = true; }
                    else localBounds.Encapsulate(point);
                }
            }
            var collider = host.AddComponent<BoxCollider>();
            collider.center = localBounds.center;
            collider.size = Vector3.Max(localBounds.size, Vector3.one * .008f);
            body.linearVelocity = velocity;
            body.angularVelocity = new Vector3(5f + index * .31f, -3f + index * .41f, 7f - index * .27f);
            foreach (var anatomical in head.Actor.Ragdoll.PhysicsController.AnatomicalColliders) Physics.IgnoreCollision(collider, anatomical.Key, true);
            if (head.Actor.Body != null) Physics.IgnoreCollision(collider, head.Actor.Body, true);
            foreach (Sector sector in head.Sectors) Physics.IgnoreCollision(collider, sector.Proxy, true);
            foreach (Fragment other in head.Fragments) Physics.IgnoreCollision(collider, other.Collider, true);
            var fragment = new Fragment { Body = body, Collider = collider };
            if (soft)
            {
                Piece piece = pieces[0];
                fragment.Tissue = host.AddComponent<CombatBrainTissue>();
                fragment.Tissue.Initialize(piece.Baked, piece.Released.transform, piece.Baked.bounds, true);
                fragment.Tissue.Impulse(piece.Released.transform.InverseTransformDirection(velocity), 2f);
            }
            head.Fragments.Add(fragment);
            Freeze(fragment, frozen);
        }

        internal void Tick(float seconds)
        {
            if (seconds <= 0f || frozen) return;
            foreach (Head head in heads.Values)
            {
                Vector3 displacement = head.Bone.position - head.PreviousPosition;
                Vector3 movement = displacement / seconds;
                Vector3 inertia = displacement.sqrMagnitude < .25f ?
                    Vector3.ClampMagnitude(head.Bone.InverseTransformDirection((head.PreviousVelocity - movement) / seconds) * .002f, .3f) : Vector3.zero;
                head.PreviousPosition = head.Bone.position; head.PreviousVelocity = movement;
                if (head.Active) foreach (Piece piece in head.Brains) if (!piece.Detached) piece.Tissue.Tick(seconds, inertia);
                foreach (Fragment fragment in head.Fragments)
                {
                    fragment.Age += seconds;
                    if (fragment.Tissue != null)
                    {
                        fragment.Tissue.Tick(seconds, Vector3.zero);
                        UpdateSoftCollider(fragment);
                    }
                    if (fragment.Age >= 5f && !fragment.Settled && !fragment.Frozen &&
                        (fragment.Tissue == null || fragment.Tissue.HasSupport || fragment.Body.IsSleeping()))
                    {
                        fragment.Body.linearVelocity = fragment.Body.angularVelocity = Vector3.zero;
                        fragment.Body.isKinematic = true; fragment.Settled = true;
                    }
                }
            }
        }
        private static void UpdateSoftCollider(Fragment fragment)
        {
            MeshFilter filter = fragment.Body.GetComponentInChildren<MeshFilter>();
            Bounds source = filter.sharedMesh.bounds;
            Matrix4x4 toBody = fragment.Body.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds bounds = new Bounds(toBody.MultiplyPoint3x4(source.center), Vector3.zero);
            for (int i = 0; i < 8; i++) bounds.Encapsulate(toBody.MultiplyPoint3x4(source.center +
                Vector3.Scale(source.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
            var collider = (BoxCollider)fragment.Collider;
            collider.center = bounds.center; collider.size = Vector3.Max(bounds.size, Vector3.one * .008f);
        }
        internal void SetFrozen(bool value)
        {
            frozen = value;
            foreach (Head head in heads.Values) foreach (Fragment fragment in head.Fragments) Freeze(fragment, value);
        }
        private static void Freeze(Fragment fragment, bool value)
        {
            if (fragment.Settled || fragment.Frozen == value || fragment.Body == null) return;
            if (value)
            {
                fragment.Velocity = fragment.Body.linearVelocity; fragment.Spin = fragment.Body.angularVelocity;
                fragment.Body.isKinematic = true;
            }
            else
            {
                fragment.Body.isKinematic = false;
                fragment.Body.linearVelocity = fragment.Velocity; fragment.Body.angularVelocity = fragment.Spin;
            }
            fragment.Frozen = value;
        }
        private void LateUpdate()
        {
            foreach (Head head in heads.Values)
                if (head.Active) foreach (SkinnedMeshRenderer original in head.Originals) if (original != null) original.enabled = false;
        }
        public void ResetActor(CombatActor actor)
        {
            if (actor == null || !heads.TryGetValue(actor, out Head head)) return;
            actor.Ragdoll.PhysicsController.SetCombatHeadCollisionEnabled(true);
            for (int i = 0; i < head.Originals.Count; i++)
            {
                suppressed.Remove(head.Originals[i]);
                if (head.Originals[i] != null) head.Originals[i].enabled = head.OriginalVisibility[i];
            }
            foreach (Sector sector in head.Sectors)
            {
                sector.Detached = false; sector.Proxy.enabled = false;
                foreach (Piece piece in sector.Pieces)
                {
                    suppressed.Remove(piece.Skin);
                    Player3DHeadVisibility.SetDerivedEnabled(piece.Source, piece.Skin, false);
                }
            }
            actor.Hurtboxes.SetHeadShapes(null);
            foreach (Fragment fragment in head.Fragments)
            {
                if (fragment.Body == null) continue;
                if (fragment.Body.gameObject.activeInHierarchy) fragment.Body.gameObject.SetActive(false);
                // Destruction also runs during scene deactivation, when Unity
                // forbids reparenting descendants. Keep baked mesh caches only;
                // the bounded physical hosts own their released surfaces.
                Destroy(fragment.Body.gameObject);
            }
            head.Fragments.Clear(); head.BrainCount = 0; head.Active = false;
            foreach (Sector sector in head.Sectors) foreach (Piece piece in sector.Pieces)
                piece.Released = null;
            foreach (Piece piece in head.Brains)
            {
                piece.Released = null; piece.Detached = false;
                suppressed.Remove(piece.Skin); piece.Tissue.ResetShape();
                Player3DHeadVisibility.SetDerivedEnabled(piece.Source, piece.Skin, true);
            }
            head.PreviousPosition = head.Bone.position; head.PreviousVelocity = Vector3.zero;
        }
        public void ResetRound()
        {
            foreach (CombatActor actor in heads.Keys) ResetActor(actor);
            LastEjectionDirection = Vector3.zero; frozen = false;
        }
        private void OnDestroy()
        {
            foreach (Head head in heads.Values)
            {
                foreach (Renderer original in head.Originals) suppressed.Remove(original);
                foreach (Sector sector in head.Sectors) foreach (Piece piece in sector.Pieces) Dispose(piece);
                foreach (Piece piece in head.Brains) Dispose(piece);
                foreach (GameObject host in head.Hosts) if (host != null) Destroy(host);
            }
            if (flesh != null) Destroy(flesh);
            if (brain != null) Destroy(brain);
            heads.Clear();
        }
        private static void Dispose(Piece piece)
        {
            suppressed.Remove(piece.Skin);
            Player3DHeadVisibility.UnregisterDerived(piece.Source, piece.Skin);
            if (piece.Baked != null) Destroy(piece.Baked);
            if (piece.SoftMesh != null) Destroy(piece.SoftMesh);
        }
        private Material RequireMaterial(bool forBrain)
        {
            Material material = forBrain ? brain : flesh;
            if (material != null) return material;
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            Texture2D texture = Resources.Load<Texture2D>("CombatGore/" + (forBrain ? "BrainSurface" : "FleshSurface"));
            if (shader == null || texture == null) throw new InvalidOperationException("Combat fracture requires its authored shared surfaces.");
            material = new Material(shader) { name = forBrain ? "Combat Brain Shared" : "Combat Flesh Shared" };
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", forBrain ? .58f : .12f); material.SetFloat("_Cull", 2f);
            if (forBrain) brain = material; else flesh = material;
            return material;
        }
        private static Vector3 Divide(Vector3 value, Vector3 divisor) => new Vector3(value.x / Mathf.Max(.000001f, divisor.x),
            value.y / Mathf.Max(.000001f, divisor.y), value.z / Mathf.Max(.000001f, divisor.z));
    }
}
