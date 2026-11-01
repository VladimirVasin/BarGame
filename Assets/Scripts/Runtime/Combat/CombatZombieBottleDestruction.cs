using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Rigid chest glass uses authored closed cells; flesh keeps its own retained surface.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatZombieBottleDestruction : MonoBehaviour
    {
        public const int MaximumShardCount = 12;
        public const int MaximumGlobalShardCount = 36;
        private const float ShardLifetime = 3.2f;
        private static readonly List<CombatZombieBottleDestruction> Owners = new List<CombatZombieBottleDestruction>(24);
        private static readonly GameObject[] Banks = new GameObject[3];
        private readonly Receipt[] receipts = new Receipt[32];
        private readonly Shard[] shards = new Shard[MaximumShardCount];
        private CombatActor actor;
        private Transform bank;
        private MeshFilter intact;
        private MeshRenderer retainedRenderer;
        private SkinnedMeshRenderer torso;
        private Mesh originalTorso, fleshTorso, retainedMesh;
        private MeshFilter[] cells;
        private int[][] cellTriangles;
        private Bounds[] cellBounds;
        private Vector3[] vertices;
        private bool[] broken;
        private float[] stress;
        private readonly List<int> retainedIndices = new List<int>(8192);
        private WarehouseFishLampBurstAudio audio;
        private int receiptCursor, nextShard;
        private uint geometryVersion;
        private bool initialized, paused;
        private struct Receipt { internal CombatActor Source; internal int Sequence, Pellet; internal CombatImpactKind Kind; internal bool Used; }
        private sealed class Shard
        {
            internal GameObject Host;
            internal MeshFilter Mesh;
            internal MeshRenderer Renderer;
            internal Rigidbody Body;
            internal BoxCollider Collider;
            internal Vector3 Velocity, Spin;
            internal float Age;
            internal bool Active;
        }
        public int Variant { get; private set; }
        public int CellCount => cells?.Length ?? 0;
        public int BrokenCellCount { get; private set; }
        public int GlassImpactCount { get; private set; }
        public int GlassAudioCount { get; private set; }
        public Vector3 LastGlassPoint { get; private set; }
        public int ActiveShardCount
        { get { int count = 0; foreach (Shard shard in shards) if (shard != null && shard.Active) count++; return count; } }
        public Bounds WorldBounds
        {
            get
            {
                if (intact == null) return default;
                return TransformBounds(retainedMesh.bounds, intact.transform.localToWorldMatrix);
            }
        }
        internal Transform GlassTransform => intact.transform;
        internal Vector3[] GlassVertices => vertices;
        internal int[][] CellTriangles => cellTriangles;
        internal Bounds[] CellBounds => cellBounds;
        internal bool[] BrokenCells => broken;
        internal uint GeometryVersion => geometryVersion;
        public Vector3 GetCellWorldPoint(int index) => cells[index].transform.TransformPoint(cells[index].sharedMesh.bounds.center);
        public bool IsCellBroken(int index) => broken[index];

        internal void Initialize(CombatActor owner)
        {
            if (initialized) return;
            actor = owner != null && owner.IsOrdinaryZombie ? owner : throw new ArgumentException("Bottle glass belongs to an authored ordinary zombie.");
            Variant = owner.OrdinaryZombieVariant;
            Transform chest = CityPedestrianHandProps.FindSocket(owner.DamageRigRoot, "chest");
            torso = Array.Find(owner.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true), skin => skin.name == "GEO_Torso");
            if (chest == null || torso == null || torso.sharedMesh.subMeshCount != 2)
                throw new InvalidOperationException("Bottle glass requires the exact retained chest and two-material torso.");
            if (Banks[Variant] == null)
            {
                GameObject source = Resources.Load<GameObject>("Combat/ZombieBottleFracture")
                    ?? throw new InvalidOperationException("Missing authored bottle fracture bank.");
                Transform variant = FindDescendant(source.transform, "Variant" + Variant);
                Banks[Variant] = variant != null ? variant.gameObject : throw new InvalidOperationException("Missing bottle fracture variant.");
            }
            bank = Instantiate(Banks[Variant], chest, false).transform;
            bank.name = "Retained Bottle Glass";
            bank.localPosition = Vector3.zero; bank.localRotation = Quaternion.identity; bank.localScale = Vector3.one;
            var found = new List<MeshFilter>();
            foreach (MeshFilter filter in bank.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;
                if (filter.name == "Intact") intact = filter;
                else if (filter.name.StartsWith("Cell_", StringComparison.Ordinal)) found.Add(filter);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            cells = found.ToArray();
            if (intact == null || cells.Length != 35 || intact.sharedMesh.subMeshCount != cells.Length)
                throw new InvalidOperationException("Bottle glass requires its finite exact closed-cell bank.");
            Material glass = CombatZombieOrdinaryAssets.LoadTorsoMaterials(Variant)[1];
            retainedRenderer = intact.GetComponent<MeshRenderer>();
            // The imported bank has one slot per cell. Trimming only the mesh
            // leaves surplus slots drawing its final submesh over the glass.
            retainedRenderer.sharedMaterials = new[] { glass };
            retainedRenderer.shadowCastingMode = ShadowCastingMode.On;
            retainedMesh = Instantiate(intact.sharedMesh); retainedMesh.name = "Retained Bottle " + Variant;
            vertices = retainedMesh.vertices;
            cellTriangles = new int[cells.Length][]; cellBounds = new Bounds[cells.Length];
            broken = new bool[cells.Length]; stress = new float[cells.Length];
            int capacity = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                int[] indices = cellTriangles[i] = retainedMesh.GetTriangles(i); capacity += indices.Length;
                Bounds bounds = new Bounds(vertices[indices[0]], Vector3.zero);
                foreach (int index in indices) bounds.Encapsulate(vertices[index]);
                cellBounds[i] = bounds;
            }
            retainedIndices.Capacity = Mathf.Max(retainedIndices.Capacity, capacity);
            retainedMesh.subMeshCount = 1;
            intact.sharedMesh = retainedMesh;
            originalTorso = torso.sharedMesh;
            fleshTorso = Instantiate(originalTorso); fleshTorso.name = "Retained Flesh " + Variant;
            fleshTorso.SetTriangles(Array.Empty<int>(), 1, false);
            torso.sharedMesh = fleshTorso;
            for (int i = 0; i < shards.Length; i++)
            {
                var host = new GameObject("Bottle Glass Shard " + i);
                host.transform.SetParent(transform, false);
                var filter = host.AddComponent<MeshFilter>();
                var renderer = host.AddComponent<MeshRenderer>(); renderer.sharedMaterial = glass;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var body = host.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = true;
                body.mass = .012f; body.linearDamping = .15f; body.angularDamping = .3f;
                body.solverIterations = 4; body.solverVelocityIterations = 1;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                var collider = host.AddComponent<BoxCollider>(); collider.enabled = false;
                CombatCorpseTraversal.RegisterCollider(actor, collider, true);
                shards[i] = new Shard { Host = host, Mesh = filter, Renderer = renderer, Body = body, Collider = collider };
                host.SetActive(false);
            }
            audio = gameObject.AddComponent<WarehouseFishLampBurstAudio>(); audio.Initialize(transform);
            audio.Source.minDistance = .8f; audio.Source.maxDistance = 16f;
            initialized = true; Owners.Add(this);
            actor.DamageReset += OnReset;
            RebuildIndices();
            actor.Hurtboxes.SetBottleSurfaces(this, torso);
        }

        public static bool IsGlassImpact(CombatImpact impact) => impact.IsGlassContact;
        internal bool ContainsGlassPoint(Vector3 point) => initialized && ClosestCell(point, .018f, out _) >= 0;
        internal CombatImpact Annotate(CombatImpact impact)
        {
            if (impact.Target != actor || impact.DetachedPart || !initialized) return impact;
            return ContainsGlassPoint(impact.Point) ? impact.WithGlassContact() : impact;
        }
        public void ApplyImpact(CombatImpact impact)
        {
            if (!initialized || impact.Target != actor || impact.DetachedPart) return;
            bool fatal = impact.HealthBefore > 0f && impact.HealthAfter <= 0f;
            if (!impact.IsGlassContact)
            {
                if (fatal) ShatterRemaining(impact);
                return;
            }
            if (impact.Result is MeleeHitResult.Ignored or MeleeHitResult.Parried or MeleeHitResult.Blocked ||
                impact.Kind is not (CombatImpactKind.Weapon or CombatImpactKind.Projectile or CombatImpactKind.Kick or CombatImpactKind.Hook) ||
                !Fresh(impact)) return;
            int cell = ClosestCell(impact.Point, .04f, out _);
            if (cell < 0) return;
            GlassImpactCount++; LastGlassPoint = impact.Point;
            float force = Mathf.Max(impact.WoundDamage, impact.Damage);
            if (impact.Kind == CombatImpactKind.Weapon) force = Mathf.Max(18f, force + impact.WeaponSpeed * 1.5f);
            stress[cell] += force;
            if (stress[cell] >= 8f)
            {
                int count = impact.Kind == CombatImpactKind.Projectile ? 1 : Mathf.Clamp(1 + Mathf.FloorToInt(force / 16f), 2, 4);
                Break(cell, impact);
                for (int step = 1; step < count; step++)
                {
                    int next = ClosestCell(impact.Point, .28f, out _);
                    if (next < 0) break;
                    Break(next, impact);
                }
                if (fatal) ShatterRemaining(impact);
                else { RebuildIndices(); PlayGlass(impact.Point); }
            }
        }
        private bool Fresh(CombatImpact impact)
        {
            foreach (Receipt receipt in receipts)
                if (receipt.Used && receipt.Source == impact.Source && receipt.Sequence == impact.AttackSequence &&
                    receipt.Pellet == impact.PelletIndex && receipt.Kind == impact.Kind) return false;
            receipts[receiptCursor] = new Receipt { Source = impact.Source, Sequence = impact.AttackSequence,
                Pellet = impact.PelletIndex, Kind = impact.Kind, Used = true };
            receiptCursor = (receiptCursor + 1) % receipts.Length;
            return true;
        }
        private void ShatterRemaining(CombatImpact impact)
        {
            if (BrokenCellCount == CellCount) return;
            LastGlassPoint = impact.Point;
            for (int i = 0; i < cells.Length; i++) if (!broken[i]) Break(i, impact);
            RebuildIndices(); PlayGlass(impact.Point);
        }
        private void Break(int index, CombatImpact impact)
        {
            if (broken[index]) return;
            broken[index] = true; BrokenCellCount++;
            // Keep the visual hole even when older airborne shards are retired.
            SpawnShard(index, impact);
        }
        private void RebuildIndices()
        {
            retainedIndices.Clear();
            for (int i = 0; i < cellTriangles.Length; i++) if (!broken[i]) retainedIndices.AddRange(cellTriangles[i]);
            retainedMesh.SetTriangles(retainedIndices, 0, false);
            retainedRenderer.enabled = retainedIndices.Count > 0;
            geometryVersion++;
            actor?.Hurtboxes?.InvalidateBottleGeometry();
        }
        private int ClosestCell(Vector3 point, float maximum, out Vector3 closest)
        {
            float best = maximum * maximum; int result = -1; closest = point;
            Matrix4x4 matrix = intact.transform.localToWorldMatrix;
            for (int i = 0; i < cellTriangles.Length; i++)
            {
                if (broken[i] || TransformBounds(cellBounds[i], matrix).SqrDistance(point) > best) continue;
                int[] indices = cellTriangles[i];
                for (int triangle = 0; triangle < indices.Length; triangle += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[triangle]]);
                    Vector3 b = matrix.MultiplyPoint3x4(vertices[indices[triangle + 1]]);
                    Vector3 c = matrix.MultiplyPoint3x4(vertices[indices[triangle + 2]]);
                    Vector3 candidate = CombatHurtboxes.ClosestBottleTriangle(point, a, b, c);
                    float square = (point - candidate).sqrMagnitude;
                    if (square > best) continue;
                    best = square; result = i; closest = candidate;
                }
            }
            return result;
        }
        private void SpawnShard(int index, CombatImpact impact)
        {
            RetireGlobalOverflow();
            Shard shard = shards[nextShard]; nextShard = (nextShard + 1) % shards.Length;
            StopShard(shard);
            MeshFilter source = cells[index];
            shard.Mesh.sharedMesh = source.sharedMesh;
            shard.Host.transform.SetParent(null, false);
            shard.Host.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            shard.Host.transform.localScale = source.transform.lossyScale;
            shard.Host.transform.SetParent(transform.parent, true);
            Bounds bounds = source.sharedMesh.bounds;
            shard.Collider.center = bounds.center;
            shard.Collider.size = Vector3.Max(bounds.size, Vector3.one * .0001f);
            shard.Host.SetActive(true); shard.Collider.enabled = true;
            CombatCorpseTraversal.NotifyColliderEnabled(shard.Collider);
            // Disabled pooled colliders lose native exclusions. Reapply at the
            // handoff so a thin cell cannot kick its owner or its sibling cells.
            if (actor.Body != null) Physics.IgnoreCollision(shard.Collider, actor.Body);
            foreach (var shape in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Key != null) Physics.IgnoreCollision(shard.Collider, shape.Key);
            foreach (Shard sibling in shards)
                if (sibling != shard && sibling.Active) Physics.IgnoreCollision(shard.Collider, sibling.Collider);
            shard.Body.isKinematic = false;
            shard.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Vector3 outward = (shard.Body.worldCenterOfMass - impact.Point).normalized;
            Vector3 direction = impact.Direction.sqrMagnitude > .00001f ? impact.Direction.normalized : transform.forward;
            shard.Body.linearVelocity = direction * 1.1f + outward * (1.3f + .1f * (index % 4)) + Vector3.up * .7f;
            shard.Body.angularVelocity = new Vector3(index % 2 == 0 ? 7f : -7f, 5f, (index % 3 - 1) * 8f);
            shard.Age = 0f; shard.Active = true;
            if (paused) FreezeShard(shard, true);
        }
        private static void RetireGlobalOverflow()
        {
            int count = 0; Shard oldest = null;
            foreach (CombatZombieBottleDestruction owner in Owners)
            {
                if (owner == null) continue;
                foreach (Shard shard in owner.shards)
                    if (shard != null && shard.Active)
                    { count++; if (oldest == null || shard.Age > oldest.Age) oldest = shard; }
            }
            if (count >= MaximumGlobalShardCount && oldest != null) StopShard(oldest);
        }
        private void PlayGlass(Vector3 point)
        {
            int playing = 0;
            foreach (CombatZombieBottleDestruction owner in Owners)
                if (owner != null && owner.audio != null && owner.audio.Source.isPlaying) playing++;
            if (playing >= 3)
                foreach (CombatZombieBottleDestruction owner in Owners)
                    if (owner != null && owner != this && owner.audio != null && owner.audio.Source.isPlaying)
                    { owner.audio.Stop(); break; }
            audio.Source.transform.position = point;
            audio.PlayBurst(); audio.Source.volume = .48f; audio.Source.pitch = .78f + .035f * Variant;
            audio.SetPaused(paused); GlassAudioCount++;
        }
        private void Update()
        {
            if (!initialized) return;
            bool pause = GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || !GameCursor.ApplicationFocused;
            if (pause != paused)
            {
                paused = pause; audio.SetPaused(pause);
                foreach (Shard shard in shards) if (shard.Active) FreezeShard(shard, pause);
            }
            if (paused) return;
            foreach (Shard shard in shards)
                if (shard.Active)
                {
                    shard.Age += Time.deltaTime;
                    if (shard.Age >= ShardLifetime) StopShard(shard);
                    else if (shard.Age >= 1.5f && shard.Body.IsSleeping())
                    { shard.Body.collisionDetectionMode = CollisionDetectionMode.Discrete; shard.Body.isKinematic = true; shard.Collider.enabled = false; }
                }
        }
        private static void FreezeShard(Shard shard, bool value)
        {
            if (value)
            {
                if (!shard.Body.isKinematic)
                { shard.Velocity = shard.Body.linearVelocity; shard.Spin = shard.Body.angularVelocity; }
                shard.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                shard.Body.isKinematic = true;
            }
            else if (shard.Collider.enabled)
            {
                shard.Body.isKinematic = false; shard.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                shard.Body.linearVelocity = shard.Velocity; shard.Body.angularVelocity = shard.Spin;
            }
        }
        private static void StopShard(Shard shard)
        {
            if (shard == null) return;
            shard.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            shard.Body.isKinematic = true; shard.Collider.enabled = false;
            shard.Host.SetActive(false); shard.Active = false;
        }
        private void OnReset(CombatActor _) => ResetFracture();
        public void ResetFracture()
        {
            if (!initialized) return;
            Array.Clear(broken, 0, broken.Length); Array.Clear(stress, 0, stress.Length); Array.Clear(receipts, 0, receipts.Length);
            BrokenCellCount = GlassImpactCount = GlassAudioCount = receiptCursor = nextShard = 0;
            foreach (Shard shard in shards) StopShard(shard);
            audio.Stop(); paused = false; RebuildIndices();
        }
        private void OnDisable()
        {
            if (!initialized) return;
            foreach (Shard shard in shards) StopShard(shard);
            audio.Stop(); paused = false;
        }
        private void OnDestroy()
        {
            Owners.Remove(this);
            if (actor != null) actor.DamageReset -= OnReset;
            foreach (Shard shard in shards)
            {
                if (shard == null) continue;
                CombatCorpseTraversal.ReleaseCollider(shard.Collider);
                if (shard.Host != null) Destroy(shard.Host);
            }
            if (torso != null && torso.sharedMesh == fleshTorso) torso.sharedMesh = originalTorso;
            if (bank != null) Destroy(bank.gameObject);
            if (fleshTorso != null) Destroy(fleshTorso);
            if (retainedMesh != null) Destroy(retainedMesh);
            if (audio != null) Destroy(audio);
        }
        internal static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            Vector3 y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            Vector3 z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            return new Bounds(matrix.MultiplyPoint3x4(local.center), (Abs(x) + Abs(y) + Abs(z)) * 2f);
        }
        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        private static Transform FindDescendant(Transform root, string name)
        { foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child; return null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Owners.Clear(); Array.Clear(Banks, 0, Banks.Length); }
    }
}
