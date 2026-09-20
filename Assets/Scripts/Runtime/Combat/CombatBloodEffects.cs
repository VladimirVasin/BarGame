using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Scene-local, caller-clocked blood; all visible geometry is Blender-authored.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatBloodEffects : MonoBehaviour
    {
        public const int MaximumDrops = 256, MaximumStains = 48;
        private sealed class Drop
        {
            public Transform Transform;
            public Vector3 Position, Velocity;
            public float Life, Size;
            public bool Active;
        }
        private sealed class Stain
        {
            public Transform Transform;
            public Collider Surface;
            public Vector3 Position, MeshNormal, SurfaceNormal;
            public float Diameter, TargetDiameter, MeshUnit;
            public bool Active;
        }
        private sealed class Injury
        {
            public CombatDamageMarks Marks;
            public float BleedSeconds, Remainder;
        }

        private static Material sharedMaterial;
        private readonly Drop[] drops = new Drop[MaximumDrops];
        private readonly Stain[] stains = new Stain[MaximumStains];
        private readonly Dictionary<CombatActor, Injury> injuries = new Dictionary<CombatActor, Injury>();
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private Transform poolRoot;
        private Mesh dropMesh;
        private readonly Mesh[] splatMeshes = new Mesh[4];
        private readonly Vector3[] splatNormals = new Vector3[4];
        private readonly float[] splatUnits = new float[4];
        private Vector3 dropAxis, dropScale;
        private float dropUnit;
        private uint randomState = 0x63BA74D1u;
        private int dropCursor, stainCursor;

        public bool IsInitialized { get; private set; }
        public int ActiveDropCount { get; private set; }
        public int StainCount { get; private set; }
        public int EmissionCount { get; private set; }
        public float MinimumStainNormalAlignment
        {
            get
            {
                float alignment = 1f;
                foreach (Stain stain in stains)
                    if (stain != null && stain.Active)
                        alignment = Mathf.Min(alignment, Vector3.Dot(stain.Transform.TransformDirection(stain.MeshNormal).normalized, stain.SurfaceNormal));
                return alignment;
            }
        }
        public int BleedCount
        {
            get { int count = 0; foreach (Injury injury in injuries.Values) if (injury.BleedSeconds > 0f) count++; return count; }
        }
        public float TotalStainArea
        {
            get { float area = 0f; foreach (Stain stain in stains) if (stain != null && stain.Active) area += Mathf.PI * stain.TargetDiameter * stain.TargetDiameter * .25f; return area; }
        }
        public int WoundCountFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.Marks.Count : 0;

        public void Initialize(Transform sceneRoot)
        {
            if (IsInitialized) return;
            if (sceneRoot == null) throw new ArgumentNullException(nameof(sceneRoot));
            GameObject shapes = Resources.Load<GameObject>("CombatBlood/BloodShapes");
            if (shapes == null) throw new InvalidOperationException("Combat blood requires its authored shape pack.");
            foreach (MeshFilter mesh in shapes.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.name == "Drop") dropMesh = mesh.sharedMesh;
                else if (mesh.name.StartsWith("Splat", StringComparison.Ordinal) &&
                    int.TryParse(mesh.name.Substring(5), out int index) && index >= 0 && index < 4) splatMeshes[index] = mesh.sharedMesh;
            }
            if (dropMesh == null || Array.Exists(splatMeshes, mesh => mesh == null))
                throw new InvalidOperationException("Combat blood has an incomplete authored shape pack.");
            // FBX conversion lives on imported transforms. We reuse raw mesh
            // assets, so measure their own axes instead of assuming Unity Y.
            Vector3 dropSize = dropMesh.bounds.size;
            dropUnit = Mathf.Max(dropSize.x, dropSize.y, dropSize.z);
            dropAxis = dropSize.x >= dropSize.y && dropSize.x >= dropSize.z ? Vector3.right :
                dropSize.y >= dropSize.z ? Vector3.up : Vector3.forward;
            dropScale = Vector3.one + dropAxis * .7f;
            for (int i = 0; i < splatMeshes.Length; i++)
            {
                Mesh mesh = splatMeshes[i];
                Vector3 size = mesh.bounds.size;
                splatUnits[i] = Mathf.Max(size.x, size.y, size.z);
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                if (splatUnits[i] <= 0f || triangles.Length < 3)
                    throw new InvalidOperationException("Combat blood requires an authored splat plane.");
                // The FBX stores centimetre-scaled vertices under a 100x root.
                // Normalize the edges first: their raw area can be smaller
                // than Vector3.Normalize's epsilon despite a metre-wide mesh.
                splatNormals[i] = Vector3.Cross((vertices[triangles[1]] - vertices[triangles[0]]) / splatUnits[i],
                    (vertices[triangles[2]] - vertices[triangles[0]]) / splatUnits[i]).normalized;
                if (splatNormals[i].sqrMagnitude < .9f)
                    throw new InvalidOperationException("Combat blood requires a non-degenerate authored splat plane.");
            }
            var root = new GameObject("Combat Blood");
            root.transform.SetParent(sceneRoot, false); poolRoot = root.transform;
            RequireMaterial();
            IsInitialized = true;
        }

        public void Emit(CombatActor actor, Vector3 point, Vector3 direction, float damage)
        {
            if (!IsInitialized || actor == null || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage) || !Finite(point) || !Finite(direction)) return;
            if (!injuries.TryGetValue(actor, out Injury injury))
            {
                injury = new Injury { Marks = new CombatDamageMarks(actor, RequireMaterial()) };
                injuries.Add(actor, injury);
            }
            Vector3 incoming = direction.sqrMagnitude > .0001f ? direction.normalized : actor.transform.forward;
            injury.Marks.Add(point, incoming);
            Vector3 origin = injury.Marks.Count > 0 ? injury.Marks.BleedPosition : point;
            Vector3 outward = injury.Marks.Count > 0 ? injury.Marks.BleedDirection : -incoming;
            int amount = Mathf.Clamp(12 + Mathf.RoundToInt(damage * .24f), 12, 26);
            for (int i = 0; i < amount; i++)
            {
                Vector3 scatter = new Vector3(Range(-1f, 1f), Range(-.25f, 1f), Range(-1f, 1f));
                Vector3 velocity = incoming * Range(.7f, 2.2f) + outward * Range(.4f, 1.3f) +
                    scatter * Range(.25f, .95f) + Vector3.up * Range(.3f, 1.1f);
                SpawnDrop(origin + outward * .022f, velocity, Range(.021f, .039f));
            }
            // Finite visual bleeding follows the original skin through ragdoll;
            // it does not alter health or perpetually enlarge the arena puddle.
            injury.BleedSeconds = Mathf.Clamp(.35f + damage * .026f, .6f, 1.4f);
            injury.Remainder = 0f;
            EmissionCount++;
        }

        public void Tick(float seconds)
        {
            if (!IsInitialized || seconds <= 0f) return;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            foreach (KeyValuePair<CombatActor, Injury> pair in injuries)
            {
                Injury injury = pair.Value;
                injury.Marks.RefreshVisibility();
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || injury.BleedSeconds <= 0f) continue;
                float time = Mathf.Min(injury.BleedSeconds, seconds);
                injury.BleedSeconds = Mathf.Max(0f, injury.BleedSeconds - seconds);
                injury.Remainder += time * 8f;
                int count = Mathf.FloorToInt(injury.Remainder);
                injury.Remainder -= count;
                for (int i = 0; i < count; i++)
                    SpawnDrop(injury.Marks.BleedPosition + injury.Marks.BleedDirection * .015f,
                        injury.Marks.BleedDirection * Range(.1f, .3f) + Vector3.down * Range(.15f, .35f), Range(.018f, .03f));
            }
            // Sweeps use the complete travelled segment, so a hitch cannot
            // teleport a drop through the floor or the arena's low obstacles.
            foreach (Drop drop in drops)
            {
                if (drop == null || !drop.Active) continue;
                float time = Mathf.Min(seconds, drop.Life);
                Vector3 next = drop.Position + drop.Velocity * time + Vector3.down * (4.905f * time * time);
                Vector3 delta = next - drop.Position;
                float length = delta.magnitude;
                bool collided = false;
                if (length > .00001f)
                {
                    int count = Physics.RaycastNonAlloc(drop.Position, delta / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
                    float nearest = float.PositiveInfinity;
                    RaycastHit contact = default;
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = hits[i];
                        if (!(hit.collider is MeshCollider) || hit.collider.GetComponentInParent<CombatActor>() != null || hit.distance >= nearest) continue;
                        nearest = hit.distance; contact = hit;
                    }
                    if (!float.IsPositiveInfinity(nearest))
                    {
                        if (contact.normal.y > .65f) Deposit(contact.point, contact.normal, contact.collider, drop.Size);
                        collided = true;
                    }
                }
                drop.Life -= seconds;
                if (collided || drop.Life <= 0f || next.y < -2f)
                { drop.Active = false; drop.Transform.gameObject.SetActive(false); ActiveDropCount--; continue; }
                drop.Position = next; drop.Velocity += Vector3.down * (9.81f * time);
                drop.Transform.position = next;
                if (drop.Velocity.sqrMagnitude > .001f) drop.Transform.rotation = Quaternion.FromToRotation(dropAxis, drop.Velocity.normalized);
            }
            foreach (Stain stain in stains)
            {
                if (stain == null || !stain.Active) continue;
                stain.Diameter = Mathf.MoveTowards(stain.Diameter, stain.TargetDiameter, seconds * 1.35f);
                stain.Transform.localScale = Vector3.one * (stain.Diameter / stain.MeshUnit);
            }
        }

        private void SpawnDrop(Vector3 point, Vector3 velocity, float size)
        {
            int index = dropCursor++ % MaximumDrops;
            Drop drop = drops[index];
            if (drop == null)
            {
                drop = new Drop { Transform = CreateRenderer("Blood Drop", dropMesh) };
                drops[index] = drop;
            }
            if (!drop.Active) ActiveDropCount++;
            drop.Active = true; drop.Position = point; drop.Velocity = velocity; drop.Life = 1.7f; drop.Size = size;
            drop.Transform.gameObject.SetActive(true); drop.Transform.position = point;
            if (velocity.sqrMagnitude > .001f) drop.Transform.rotation = Quaternion.FromToRotation(dropAxis, velocity.normalized);
            drop.Transform.localScale = dropScale * (size / dropUnit);
        }

        private void Deposit(Vector3 point, Vector3 normal, Collider surface, float size)
        {
            Stain closest = null;
            float best = float.PositiveInfinity;
            foreach (Stain stain in stains)
            {
                if (stain == null || !stain.Active || stain.Surface != surface) continue;
                float distance = Vector3.Distance(stain.Position, point);
                if (distance > .16f + stain.TargetDiameter * .4f || distance >= best) continue;
                best = distance; closest = stain;
            }
            if (closest != null)
            {
                // Add a drop-sized area rather than a linear radius term, so
                // repeated hits leave a local puddle instead of flooding the floor.
                closest.TargetDiameter = Mathf.Min(.65f, Mathf.Sqrt(closest.TargetDiameter * closest.TargetDiameter + size * size * 9f));
                return;
            }
            int index = stainCursor++ % MaximumStains;
            Stain next = stains[index];
            if (next == null)
            {
                int variant = index % 4;
                next = new Stain { Transform = CreateRenderer("Blood Puddle", splatMeshes[variant]),
                    MeshNormal = splatNormals[variant], MeshUnit = splatUnits[variant] };
                stains[index] = next;
            }
            if (!next.Active) StainCount++;
            next.Active = true; next.Surface = surface; next.Position = point; next.SurfaceNormal = normal.normalized;
            next.Diameter = .05f; next.TargetDiameter = .09f + size * 1.6f;
            next.Transform.gameObject.SetActive(true);
            next.Transform.position = point + normal * .014f;
            next.Transform.rotation = Quaternion.AngleAxis(Range(0f, 360f), next.SurfaceNormal) *
                Quaternion.FromToRotation(next.MeshNormal, next.SurfaceNormal);
            next.Transform.localScale = Vector3.one * (next.Diameter / next.MeshUnit);
        }

        private Transform CreateRenderer(string label, Mesh mesh)
        {
            var host = new GameObject(label);
            host.transform.SetParent(poolRoot, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RequireMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return host.transform;
        }

        public void ResetActor(CombatActor actor)
        {
            if (actor == null || !injuries.TryGetValue(actor, out Injury injury)) return;
            injury.Marks.Reset(); injury.BleedSeconds = injury.Remainder = 0f;
        }

        public void ResetRound()
        {
            foreach (Injury injury in injuries.Values) { injury.Marks.Reset(); injury.BleedSeconds = injury.Remainder = 0f; }
            foreach (Drop drop in drops)
                if (drop != null) { drop.Active = false; if (drop.Transform != null) drop.Transform.gameObject.SetActive(false); }
            foreach (Stain stain in stains)
                if (stain != null) { stain.Active = false; if (stain.Transform != null) stain.Transform.gameObject.SetActive(false); }
            ActiveDropCount = StainCount = EmissionCount = dropCursor = stainCursor = 0;
            randomState = 0x63BA74D1u;
        }

        private void OnDisable() => ResetRound();
        private void OnDestroy()
        {
            foreach (Injury injury in injuries.Values) injury.Marks.Dispose();
            injuries.Clear();
            if (poolRoot != null) Destroy(poolRoot.gameObject);
            IsInitialized = false;
        }

        private float Range(float low, float high)
        {
            randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5;
            return Mathf.Lerp(low, high, (randomState & 0xFFFFFFu) / 16777215f);
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        private static Material RequireMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            Texture2D texture = Resources.Load<Texture2D>("CombatBlood/BloodSurface");
            if (shader == null) throw new InvalidOperationException("Combat blood requires the shared PS1 shader.");
            if (texture == null) throw new InvalidOperationException("Combat blood requires CombatBlood/BloodSurface as a 2D texture.");
            sharedMaterial = new Material(shader) { name = "Combat Blood Shared", hideFlags = HideFlags.HideAndDontSave, renderQueue = 2450 };
            sharedMaterial.SetTexture("_BaseMap", texture); sharedMaterial.SetColor("_BaseColor", Color.white);
            sharedMaterial.SetFloat("_Smoothness", .14f); sharedMaterial.SetFloat("_Metallic", 0f);
            sharedMaterial.SetFloat("_AlphaClip", 1f); sharedMaterial.SetFloat("_Cutoff", .4f); sharedMaterial.SetFloat("_Cull", 0f);
            sharedMaterial.EnableKeyword("_ALPHATEST_ON");
            return sharedMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedMaterial()
        {
            if (sharedMaterial != null) Destroy(sharedMaterial);
            sharedMaterial = null;
        }
    }
}
