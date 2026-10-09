using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Scene-local, caller-clocked blood; all visible geometry is Blender-authored.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(31000)]
    public sealed partial class CombatBloodEffects : MonoBehaviour
    {
        public const int MaximumDrops = 256, MaximumStains = 48;
        public const float ProjectileBleedLifetimeSeconds = 24f;
        private const float ActorBloodBudget = 800f;
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
            public CombatActor Actor;
            public CombatDamageMarks Marks;
            public float BleedSeconds, Remainder, BloodAge, PulsePhase;
            public float RemainingBlood = ActorBloodBudget;
            public bool HasProjectileBleed, HeadTrauma;
            public readonly float[] WoundBirth = new float[CombatDamageMarks.MaximumProjectileWounds];
            public readonly float[] WoundRemainder = new float[CombatDamageMarks.MaximumProjectileWounds];
            public readonly BodyCut[] BodyCuts = new BodyCut[CombatBodyDamageState.RegionCount];
            public Transform HeadSource;
            public Vector3 HeadLocalPoint, HeadLocalDirection;
            public float HeadRemainder, PoolFlowStep;
            public int BleedingDrops;
            public float GroundSeconds;
            public DefeatPool Pool;
        }
        private sealed class BodyCut
        {
            public Transform Source;
            public BodyDamageRegion SupportRegion;
            public Vector3 LocalPoint, LocalDirection;
            public float Remainder;
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
            get { int count = 0; foreach (Injury injury in injuries.Values)
                if (injury.BleedSeconds > 0f && injury.RemainingBlood >= 1f && injury.Marks.TryGetBleed(out _, out _) ||
                    ProjectilePressure(injury) > 0f && HasProjectileSource(injury)) count++; return count; }
        }
        public float TotalStainArea
        {
            get { float area = 0f; foreach (Stain stain in stains) if (stain != null && stain.Active) area += Mathf.PI * stain.TargetDiameter * stain.TargetDiameter * .25f; return area; }
        }
        public int WoundCountFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.Marks.Count : 0;
        public int ProjectileWoundCountFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.Marks.ProjectileCount : 0;
        public int BleedingDropCountFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.BleedingDrops : 0;
        public float BleedingPressureFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? ProjectilePressure(injury) : 0f;
        public float BleedingPulseFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? Pulse(injury) : 0f;
        public float BleedingAgeFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.BloodAge : 0f;
        public float RemainingBloodFractionFor(CombatActor actor) => actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.RemainingBlood / ActorBloodBudget : 1f;
        internal bool TryGetProjectileWound(CombatActor actor, int index, out Vector3 point, out Vector3 direction)
        {
            point = direction = Vector3.zero;
            return actor != null && injuries.TryGetValue(actor, out Injury injury) &&
                injury.Marks.TryGetProjectileBleed(index, out point, out direction);
        }

        internal bool TryGetProjectilePresentation(CombatActor actor, int index,
            out CombatDamageMarks.ProjectileWoundPresentation presentation)
        {
            presentation = default;
            return actor != null && injuries.TryGetValue(actor, out Injury injury) &&
                injury.Marks.TryGetProjectilePresentation(index, out presentation);
        }

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

        // A new bullet contact wounds a corpse without inventing another HP transaction.
        public void Emit(CombatImpact impact) => Emit(impact.Target, impact.Point, impact.Direction,
            impact.Kind == CombatImpactKind.Projectile ? (impact.IsPellet ? impact.WoundDamage : Mathf.Max(25f, impact.Damage)) : Mathf.Max(impact.WoundDamage, impact.Damage),
            impact.Kind == CombatImpactKind.Projectile, impact.Location.Region == MeleeBodyRegion.Head, impact.Part, impact.IsPellet);

        public void Emit(CombatActor actor, Vector3 point, Vector3 direction, float damage) =>
            Emit(actor, point, direction, damage, false, false);

        // The destroyed head's original skin is hidden. Keep its open cut on the
        // surviving world rig instead of sampling that now-invisible wound.
        public void SetHeadBleedSource(CombatActor actor, Transform retainedRigSource, Vector3 worldPoint, Vector3 worldOutward)
        {
            if (actor == null || !injuries.TryGetValue(actor, out Injury injury)) return;
            injury.HeadSource = retainedRigSource;
            injury.HeadRemainder = 0f;
            if (retainedRigSource == null || !Finite(worldPoint) || !Finite(worldOutward))
            { injury.HeadSource = null; return; }
            injury.HeadLocalPoint = retainedRigSource.InverseTransformPoint(worldPoint);
            injury.HeadLocalDirection = retainedRigSource.InverseTransformDirection(worldOutward.normalized);
        }

        /// <summary>A separation bleeds from its retained parent, sharing the existing finite supply and clock.</summary>
        public void SetBodyBleedSource(CombatActor actor, BodyDamageRegion cutRegion, Transform retainedRigSource,
            Vector3 worldPoint, Vector3 worldOutward)
        {
            if (!IsInitialized || actor == null || (uint)(int)cutRegion >= CombatBodyDamageState.RegionCount ||
                retainedRigSource == null || !Finite(worldPoint) || !Finite(worldOutward)) return;
            if (!injuries.TryGetValue(actor, out Injury injury))
            {
                injury = new Injury { Actor = actor, Marks = new CombatDamageMarks(actor, RequireMaterial()) };
                injuries.Add(actor, injury);
            }
            int index = (int)cutRegion;
            BodyCut cut = injury.BodyCuts[index];
            if (cut == null) injury.BodyCuts[index] = cut = new BodyCut();
            cut.Source = retainedRigSource;
            cut.SupportRegion = RetainedParent(cutRegion);
            cut.LocalPoint = retainedRigSource.InverseTransformPoint(worldPoint);
            cut.LocalDirection = retainedRigSource.InverseTransformDirection(worldOutward.normalized);
            // Repeated synchronization updates only the same source; it cannot
            // restart the pulse, refill blood or discard a partial emitted drop.
            injury.HasProjectileBleed = true;
        }

        private static BodyDamageRegion RetainedParent(BodyDamageRegion region) => region switch
        {
            BodyDamageRegion.Head => BodyDamageRegion.Neck,
            BodyDamageRegion.Neck or BodyDamageRegion.LeftUpperArm or BodyDamageRegion.RightUpperArm => BodyDamageRegion.Chest,
            BodyDamageRegion.Chest => BodyDamageRegion.Abdomen,
            BodyDamageRegion.Abdomen or BodyDamageRegion.LeftThigh or BodyDamageRegion.RightThigh => BodyDamageRegion.Pelvis,
            BodyDamageRegion.LeftForearm => BodyDamageRegion.LeftUpperArm,
            BodyDamageRegion.LeftHand => BodyDamageRegion.LeftForearm,
            BodyDamageRegion.RightForearm => BodyDamageRegion.RightUpperArm,
            BodyDamageRegion.RightHand => BodyDamageRegion.RightForearm,
            BodyDamageRegion.LeftShin => BodyDamageRegion.LeftThigh,
            BodyDamageRegion.LeftFoot => BodyDamageRegion.LeftShin,
            BodyDamageRegion.RightShin => BodyDamageRegion.RightThigh,
            BodyDamageRegion.RightFoot => BodyDamageRegion.RightShin,
            _ => BodyDamageRegion.Pelvis
        };

        private static bool HasBodyCut(Injury injury, BodyCut cut) => cut != null && cut.Source != null &&
            cut.Source.gameObject.activeInHierarchy && injury.Actor != null && injury.Actor.BodyDamage.IsAttached(cut.SupportRegion);

        private static bool TryGetRetainedBleedSource(Injury injury, out Vector3 point)
        {
            if (injury.HeadSource != null)
            { point = injury.HeadSource.TransformPoint(injury.HeadLocalPoint); return true; }
            if (injury.Marks.TryGetBleed(out point, out _)) return true;
            foreach (BodyCut cut in injury.BodyCuts)
                if (HasBodyCut(injury, cut))
                { point = cut.Source.TransformPoint(cut.LocalPoint); return true; }
            point = Vector3.zero;
            return false;
        }

        private void Emit(CombatActor actor, Vector3 point, Vector3 direction, float damage, bool projectile, bool head,
            Player3DAnatomicalPart? part = null, bool pellet = false)
        {
            if (!IsInitialized || actor == null || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage) || !Finite(point) || !Finite(direction)) return;
            if (!injuries.TryGetValue(actor, out Injury injury))
            {
                injury = new Injury { Actor = actor, Marks = new CombatDamageMarks(actor, RequireMaterial()) };
                injuries.Add(actor, injury);
            }
            Vector3 incoming = direction.sqrMagnitude > .0001f ? direction.normalized : actor.transform.forward;
            int oldWounds = injury.Marks.ProjectileCount;
            injury.Marks.Add(point, incoming, projectile, head, part);
            if (projectile)
            {
                injury.HasProjectileBleed = true;
                injury.HeadTrauma |= head;
                for (int i = oldWounds; i < injury.Marks.ProjectileCount; i++) injury.WoundBirth[i] = injury.BloodAge;
            }
            bool hasSurface = injury.Marks.TryGetBleed(out Vector3 bleedPoint, out Vector3 bleedDirection);
            Vector3 origin = projectile || !hasSurface ? point : bleedPoint;
            Vector3 outward = hasSurface ? bleedDirection : -incoming;
            int amount = Mathf.Clamp(12 + Mathf.RoundToInt(damage * .24f), 12, 26);
            if (projectile) amount = pellet ? 5 : 56;
            float headPower = pellet && head ? Mathf.Clamp01(damage / 24f) : 0f;
            if (pellet && head) amount = Mathf.RoundToInt(Mathf.Lerp(2f, 8f, headPower));
            amount = SpendBlood(injury, amount);
            for (int i = 0; i < amount; i++)
            {
                Vector3 scatter = new Vector3(Range(-1f, 1f), Range(-.25f, 1f), Range(-1f, 1f));
                Vector3 velocity = incoming * Range(.7f, 2.2f) + outward * Range(.4f, 1.3f) +
                    scatter * Range(.25f, .95f) + Vector3.up * Range(.3f, 1.1f);
                if (projectile)
                    velocity = outward * Range(2.2f, head ? 4.8f : 4f) + incoming * Range(.1f, .35f) +
                        scatter * Range(.65f, 1.4f) + Vector3.up * Range(.4f, 1.3f);
                // A short local fan follows the bullet; a smaller part returns
                // from the entry. Close buckshot scales its speed with pellet damage.
                if (projectile && head)
                    velocity = (i < amount * .7f ? incoming : outward) * Range(1.4f, 3.2f) +
                        scatter * Range(.3f, .9f) + Vector3.up * Range(.15f, .65f);
                if (pellet && head) velocity *= 1f + headPower * 1.2f;
                SpawnDrop(origin + outward * .022f, velocity, Range(.021f, head ? .04f : projectile ? .05f : .039f));
            }
            // All wounds share the actor's remaining supply. Another impact
            // adds a source and burst, but never rewinds the actor's bleed clock.
            if (!projectile)
            {
                injury.BleedSeconds = Mathf.Clamp(.35f + damage * .026f, .6f, 1.4f);
                injury.Remainder = 0f;
            }
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
                if (pair.Key == null || !pair.Key.isActiveAndEnabled) { ResetDefeatPool(injury); continue; }
                injury.Marks.Advance(seconds);
                if (injury.BleedSeconds > 0f)
                {
                    float time = Mathf.Min(injury.BleedSeconds, seconds);
                    injury.BleedSeconds = Mathf.Max(0f, injury.BleedSeconds - seconds);
                    injury.Remainder += time * 8f;
                    int count = Mathf.FloorToInt(injury.Remainder);
                    injury.Remainder -= count;
                    if (injury.Marks.TryGetBleed(out Vector3 point, out Vector3 direction))
                        SpawnBleedingDrops(injury, point, direction, count, 0f, 0f);
                }
                AdvanceProjectileBleeding(injury, seconds);
                AdvanceDefeatPool(pair.Key, injury, seconds);
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
                        Deposit(contact.point, contact.normal, contact.collider, drop.Size);
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

        // Grip weights are finalized with the original hero/NPC presentation.
        // Copy only that rendered state here; the duel still exclusively owns
        // injury age, wetness, emission and all other simulation clocks.
        private void LateUpdate()
        {
            foreach (Injury injury in injuries.Values) injury.Marks.RefreshVisibility();
        }

        private void AdvanceProjectileBleeding(Injury injury, float seconds)
        {
            injury.PoolFlowStep = 0f;
            if (!injury.HasProjectileBleed || injury.BloodAge >= ProjectileBleedLifetimeSeconds) return;
            float endAge = Mathf.Min(ProjectileBleedLifetimeSeconds, injury.BloodAge + seconds);
            float remaining = endAge - injury.BloodAge;
            // Sample the shared pulse in bounded small slices. Large caller
            // steps must not accidentally sample only a trough or a peak.
            while (remaining > .00001f)
            {
                float step = Mathf.Min(remaining, 1f / 30f);
                injury.BloodAge = Mathf.Min(ProjectileBleedLifetimeSeconds, injury.BloodAge + step);
                injury.PulsePhase = Mathf.Repeat(injury.PulsePhase + step /
                    Mathf.Lerp(.68f, 1.55f, injury.BloodAge / ProjectileBleedLifetimeSeconds), 1f);
                float pressure = ProjectilePressure(injury), pulse = Pulse(injury);
                if (pressure > 0f)
                {
                    int wounds = injury.Marks.ProjectileCount;
                    float sourceWeight = injury.HeadSource != null ? 2.4f : 0f;
                    foreach (BodyCut cut in injury.BodyCuts)
                        if (HasBodyCut(injury, cut)) sourceWeight += 2.4f;
                    for (int i = 0; i < wounds; i++)
                        if (injury.Marks.TryGetProjectileBleed(i, out _, out _)) sourceWeight += WoundStrength(injury, i);
                    if (sourceWeight > 0f) injury.PoolFlowStep += step * pressure * (.35f + .65f * pulse) / 2.8f;
                    // Many injuries divide one bounded rate and one blood supply.
                    float rateScale = sourceWeight > 0f ? Mathf.Min(1f, 3.5f / sourceWeight) : 0f;
                    for (int i = 0; i < wounds; i++)
                    {
                        if (!injury.Marks.TryGetProjectileBleed(i, out Vector3 point, out Vector3 direction)) continue;
                        injury.WoundRemainder[i] += step * (2.5f + 46f * pulse) * pressure * WoundStrength(injury, i) * rateScale;
                        int count = Mathf.FloorToInt(injury.WoundRemainder[i]);
                        injury.WoundRemainder[i] -= count;
                        SpawnBleedingDrops(injury, point, direction, count, pressure, pulse);
                    }
                    if (injury.HeadSource != null)
                    {
                        injury.HeadRemainder += step * (5f + 110f * pulse) * pressure * rateScale;
                        int count = Mathf.FloorToInt(injury.HeadRemainder);
                        injury.HeadRemainder -= count;
                        SpawnBleedingDrops(injury, injury.HeadSource.TransformPoint(injury.HeadLocalPoint),
                            injury.HeadSource.TransformDirection(injury.HeadLocalDirection), count, pressure, pulse);
                    }
                    foreach (BodyCut cut in injury.BodyCuts)
                    {
                        if (!HasBodyCut(injury, cut)) continue;
                        cut.Remainder += step * (5f + 110f * pulse) * pressure * rateScale;
                        int count = Mathf.FloorToInt(cut.Remainder);
                        cut.Remainder -= count;
                        SpawnBleedingDrops(injury, cut.Source.TransformPoint(cut.LocalPoint),
                            cut.Source.TransformDirection(cut.LocalDirection), count, pressure, pulse);
                    }
                }
                remaining -= step;
            }
            injury.BloodAge = endAge;
        }

        private static bool HasProjectileSource(Injury injury)
        {
            if (injury.HeadSource != null) return true;
            foreach (BodyCut cut in injury.BodyCuts)
                if (HasBodyCut(injury, cut)) return true;
            for (int i = 0; i < injury.Marks.ProjectileCount; i++)
                if (injury.Marks.TryGetProjectileBleed(i, out _, out _)) return true;
            return false;
        }

        private static float WoundStrength(Injury injury, int index) =>
            Mathf.Clamp01(1f - (injury.BloodAge - injury.WoundBirth[index]) / ProjectileBleedLifetimeSeconds);

        private static float ProjectilePressure(Injury injury) => !injury.HasProjectileBleed || injury.RemainingBlood < 1f ? 0f :
            Mathf.Pow(Mathf.Clamp01(1f - injury.BloodAge / ProjectileBleedLifetimeSeconds), 1.15f) *
            Mathf.Sqrt(injury.RemainingBlood / ActorBloodBudget);

        private static float Pulse(Injury injury) =>
            Mathf.Clamp01(Mathf.Pow(Mathf.Max(0f, Mathf.Cos(injury.PulsePhase * Mathf.PI * 2f)), 8f) +
                .18f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos((injury.PulsePhase - .16f) * Mathf.PI * 2f)), 12f));

        private static int SpendBlood(Injury injury, int requested)
        {
            int count = Mathf.Min(requested, Mathf.FloorToInt(injury.RemainingBlood));
            injury.RemainingBlood = Mathf.Max(0f, injury.RemainingBlood - count);
            return count;
        }

        private void SpawnBleedingDrops(Injury injury, Vector3 point, Vector3 direction, int count, float pressure, float pulse)
        {
            count = SpendBlood(injury, count);
            for (int i = 0; i < count; i++)
            {
                float force = pressure * pulse;
                Vector3 scatter = new Vector3(Range(-.16f, .16f), Range(-.1f, .18f), Range(-.16f, .16f));
                SpawnDrop(point + direction * .018f, direction * Range(.1f, .3f + force * 2.4f) +
                    Vector3.down * Range(.12f, .28f) + scatter * force,
                    Range(.014f, .024f + pressure * .017f));
            }
            injury.BleedingDrops += count;
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
            drop.Active = true; drop.Position = point; drop.Velocity = velocity; drop.Life = 2.4f; drop.Size = size;
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
                if (stain == null || !stain.Active || stain.Surface != surface ||
                    Vector3.Dot(stain.SurfaceNormal, normal) < .98f ||
                    Mathf.Abs(Vector3.Dot(stain.Position - point, normal)) > .025f) continue;
                float distance = Vector3.Distance(stain.Position, point);
                if (distance > .16f + stain.TargetDiameter * .4f || distance >= best) continue;
                best = distance; closest = stain;
            }
            if (closest != null)
            {
                // Add a drop-sized area rather than a linear radius term, so
                // repeated hits leave a local puddle instead of flooding the floor.
                closest.TargetDiameter = Mathf.Min(normal.y > .65f ? .9f : .46f,
                    Mathf.Sqrt(closest.TargetDiameter * closest.TargetDiameter + size * size * 9f));
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
            ResetInjury(injury);
        }

        public void ResetRound()
        {
            foreach (Injury injury in injuries.Values)
            {
                ResetInjury(injury);
            }
            foreach (Drop drop in drops)
                if (drop != null) { drop.Active = false; if (drop.Transform != null) drop.Transform.gameObject.SetActive(false); }
            foreach (Stain stain in stains)
                if (stain != null) { stain.Active = false; if (stain.Transform != null) stain.Transform.gameObject.SetActive(false); }
            ActiveDropCount = StainCount = EmissionCount = dropCursor = stainCursor = 0;
            randomState = 0x63BA74D1u;
        }

        private static void ResetInjury(Injury injury)
        {
            injury.Marks.Reset(); injury.BleedSeconds = injury.Remainder = injury.BloodAge = injury.PulsePhase = 0f;
            injury.RemainingBlood = ActorBloodBudget; injury.HasProjectileBleed = injury.HeadTrauma = false;
            injury.HeadSource = null; injury.HeadLocalPoint = injury.HeadLocalDirection = Vector3.zero;
            injury.HeadRemainder = injury.PoolFlowStep = 0f; injury.BleedingDrops = 0;
            Array.Clear(injury.WoundBirth, 0, injury.WoundBirth.Length);
            Array.Clear(injury.WoundRemainder, 0, injury.WoundRemainder.Length);
            Array.Clear(injury.BodyCuts, 0, injury.BodyCuts.Length);
            ResetDefeatPool(injury);
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
