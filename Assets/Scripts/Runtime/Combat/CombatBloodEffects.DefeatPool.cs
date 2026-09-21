using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatBloodEffects
    {
        public const float DefeatPoolGrowthSeconds = 10f;
        private const int PoolLobes = 3;
        private sealed class PoolLobe
        {
            public Transform Transform;
            public Vector3 UnitScale;
            public float Diameter, FinalDiameter, Delay, UnitArea;
        }

        private sealed class DefeatPool
        {
            public readonly PoolLobe[] Lobes = new PoolLobe[PoolLobes];
            public Vector3 Position;
            public float Age;
            public bool Active;
        }

        public int DefeatPoolCount
        {
            get
            {
                int count = 0;
                foreach (Injury injury in injuries.Values) if (injury.Pool != null && injury.Pool.Active) count++;
                return count;
            }
        }

        // Actual displayed mesh area, summed across lobes (overlaps counted),
        // rather than the instantaneous final target used by TotalStainArea.
        public float DefeatPoolAreaFor(CombatActor actor)
        {
            if (!TryGetDefeatPool(actor, out DefeatPool pool)) return 0f;
            float area = 0f;
            foreach (PoolLobe lobe in pool.Lobes)
                if (lobe != null) area += lobe.UnitArea * lobe.Diameter * lobe.Diameter;
            return area;
        }

        public Vector3 DefeatPoolPositionFor(CombatActor actor) =>
            TryGetDefeatPool(actor, out DefeatPool pool) ? pool.Position : Vector3.zero;
        public float DefeatPoolAgeFor(CombatActor actor) =>
            TryGetDefeatPool(actor, out DefeatPool pool) ? pool.Age : 0f;

        private bool TryGetDefeatPool(CombatActor actor, out DefeatPool pool)
        {
            pool = actor != null && injuries.TryGetValue(actor, out Injury injury) ? injury.Pool : null;
            return pool != null && pool.Active;
        }

        private void AdvanceDefeatPool(CombatActor actor, Injury injury, float seconds)
        {
            if (actor == null || !actor.isActiveAndEnabled)
            { ResetDefeatPool(injury); return; }
            DefeatPool pool = injury.Pool;
            if (pool == null || !pool.Active)
            {
                CombatRagdoll ragdoll = actor.Ragdoll;
                if (!actor.State.IsDefeated || ragdoll == null || !ragdoll.HasGroundContact || injury.Marks.Count == 0) return;
                injury.GroundSeconds += seconds;
                // Feet touching while standing are not a fall. Wait for the
                // contacted body to slow before fixing the wound's floor source.
                if (injury.GroundSeconds < .35f || (!ragdoll.IsSettled && ragdoll.MaximumBodySpeed > .8f)) return;
                if (!FindPoolSupport(injury.Marks.BleedPosition + Vector3.up * .1f, 2f, out RaycastHit support)) return;
                pool = injury.Pool ?? (injury.Pool = new DefeatPool());
                StartDefeatPool(pool, support, actor.IsHero ? 1 : 0);
                if (!pool.Active) return;
                // Start from a pinprick even if the caller advances a long step.
                return;
            }

            pool.Age = Mathf.Min(DefeatPoolGrowthSeconds, pool.Age + seconds);
            foreach (PoolLobe lobe in pool.Lobes)
            {
                if (lobe == null || lobe.Transform == null || !lobe.Transform.gameObject.activeSelf) continue;
                float progress = Mathf.Clamp01((pool.Age - lobe.Delay) / (DefeatPoolGrowthSeconds - lobe.Delay));
                // Each lobe opens at its own time; the edge decelerates into a
                // finite outline instead of the entire stain pulsing in unison.
                float spread = 1f - Mathf.Pow(1f - progress, 2f);
                lobe.Diameter = Mathf.Lerp(.025f, lobe.FinalDiameter, spread);
                lobe.Transform.localScale = lobe.UnitScale * lobe.Diameter;
            }
        }

        private void StartDefeatPool(DefeatPool pool, RaycastHit support, int variantOffset)
        {
            pool.Position = support.point;
            pool.Age = 0f;
            Vector3 normal = support.normal.normalized;
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            tangent = Quaternion.AngleAxis(Range(0f, 360f), normal) * tangent;
            Vector3 across = Vector3.Cross(normal, tangent);
            for (int i = 0; i < PoolLobes; i++)
            {
                Vector3 offset = i == 0 ? Vector3.zero : i == 1 ? tangent * .17f : -tangent * .12f + across * .12f;
                Vector3 point = support.point + offset;
                float diameter = i == 0 ? .68f : i == 1 ? .47f : .36f;
                // Check a conservative footprint against the real collider and
                // nearby walls. Near an edge, a lobe stops short of the edge.
                while (diameter >= .065f && !PoolFootprintFits(point, normal, support.collider, diameter * .72f)) diameter *= .72f;
                PoolLobe lobe = pool.Lobes[i];
                if (diameter < .065f)
                {
                    if (lobe != null) { lobe.Diameter = 0f; lobe.Transform.gameObject.SetActive(false); }
                    continue;
                }
                int variant = (i + variantOffset) % splatMeshes.Length;
                if (lobe == null)
                {
                    Vector3 meshNormal = splatNormals[variant];
                    // Preserve the imported plane axes and unit factor.
                    Vector3 stretch = Mathf.Abs(meshNormal.z) > .9f ? new Vector3(1f, .82f, 1f) : new Vector3(1f, 1f, .82f);
                    lobe = new PoolLobe
                    {
                        Transform = CreateRenderer("Defeat Blood Pool " + i, splatMeshes[variant]),
                        UnitScale = stretch / splatUnits[variant],
                        UnitArea = MeshArea(splatMeshes[variant]) * .82f / (splatUnits[variant] * splatUnits[variant])
                    };
                    var color = new MaterialPropertyBlock();
                    color.SetColor("_BaseColor", new Color(.65f, .55f, .52f, 1f).linear);
                    lobe.Transform.GetComponent<MeshRenderer>().SetPropertyBlock(color);
                    pool.Lobes[i] = lobe;
                }
                lobe.Diameter = .025f; lobe.FinalDiameter = diameter; lobe.Delay = i * .85f;
                lobe.Transform.SetPositionAndRotation(point + normal * (.006f + i * .001f),
                    Quaternion.AngleAxis(Range(0f, 360f), normal) * Quaternion.FromToRotation(splatNormals[variant], normal));
                lobe.Transform.localScale = lobe.UnitScale * lobe.Diameter;
                lobe.Transform.gameObject.SetActive(true);
                pool.Active = true;
            }
        }

        private bool FindPoolSupport(Vector3 origin, float distance, out RaycastHit support)
        {
            support = default;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!(hit.collider is MeshCollider) || hit.collider.attachedRigidbody != null ||
                    hit.collider.GetComponentInParent<CombatActor>() != null || hit.distance >= nearest) continue;
                nearest = hit.distance; support = hit;
            }
            return !float.IsPositiveInfinity(nearest) && support.normal.y > .9f;
        }

        private bool PoolFootprintFits(Vector3 centre, Vector3 normal, Collider surface, float radius)
        {
            if (!PoolPointFits(centre, normal, surface)) return false;
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            for (int i = 0; i < 16; i++)
            {
                Vector3 radial = Quaternion.AngleAxis(i * 22.5f, normal) * tangent * radius;
                if (!PoolPointFits(centre + radial, normal, surface)) return false;
                int count = Physics.RaycastNonAlloc(centre + normal * .025f, radial.normalized, hits,
                    radius, ~0, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < count; j++)
                    if (hits[j].collider is MeshCollider && hits[j].collider.GetComponentInParent<CombatActor>() == null)
                        return false;
            }
            return true;
        }

        private bool PoolPointFits(Vector3 point, Vector3 normal, Collider surface) =>
            FindPoolSupport(point + Vector3.up * .12f, .24f, out RaycastHit support) &&
            support.collider == surface && Vector3.Dot(support.normal, normal) > .995f &&
            Mathf.Abs(Vector3.Dot(support.point - point, normal)) < .008f;

        private static float MeshArea(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float area = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
                area += Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).magnitude * .5f;
            return area;
        }

        private static void ResetDefeatPool(Injury injury)
        {
            injury.GroundSeconds = 0f;
            DefeatPool pool = injury.Pool;
            if (pool == null) return;
            pool.Active = false; pool.Age = 0f; pool.Position = Vector3.zero;
            foreach (PoolLobe lobe in pool.Lobes)
                if (lobe != null)
                {
                    lobe.Diameter = 0f;
                    if (lobe.Transform != null) lobe.Transform.gameObject.SetActive(false);
                }
        }
    }
}
