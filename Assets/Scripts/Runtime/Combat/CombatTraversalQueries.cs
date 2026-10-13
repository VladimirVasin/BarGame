using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reusable movement scratch. A full buffer must not hide the floor or a wall behind remains.</summary>
    internal static class CombatTraversalQueries
    {
        private const int MaximumScratch = 256;

        internal static RaycastHit[] Raycast(Vector3 origin, Vector3 direction, ref RaycastHit[] scratch,
            float distance, int mask, QueryTriggerInteraction triggers, out int count)
        {
            while (true)
            {
                count = Physics.RaycastNonAlloc(origin, direction, scratch, distance, mask, triggers);
                if (count < scratch.Length) return scratch;
                if (scratch.Length >= MaximumScratch)
                {
                    RaycastHit[] all = Physics.RaycastAll(origin, direction, distance, mask, triggers);
                    count = all.Length;
                    return all;
                }
                Array.Resize(ref scratch, Math.Min(MaximumScratch, scratch.Length * 2));
            }
        }

        internal static RaycastHit[] SphereCast(Vector3 origin, float radius, Vector3 direction,
            ref RaycastHit[] scratch, float distance, int mask, QueryTriggerInteraction triggers, out int count)
        {
            while (true)
            {
                count = Physics.SphereCastNonAlloc(origin, radius, direction, scratch, distance, mask, triggers);
                if (count < scratch.Length) return scratch;
                if (scratch.Length >= MaximumScratch)
                {
                    RaycastHit[] all = Physics.SphereCastAll(origin, radius, direction, distance, mask, triggers);
                    count = all.Length;
                    return all;
                }
                Array.Resize(ref scratch, Math.Min(MaximumScratch, scratch.Length * 2));
            }
        }

        internal static RaycastHit[] CapsuleCast(Vector3 top, Vector3 bottom, float radius, Vector3 direction,
            ref RaycastHit[] scratch, float distance, int mask, QueryTriggerInteraction triggers, out int count)
        {
            while (true)
            {
                count = Physics.CapsuleCastNonAlloc(top, bottom, radius, direction, scratch, distance, mask, triggers);
                if (count < scratch.Length) return scratch;
                if (scratch.Length >= MaximumScratch)
                {
                    RaycastHit[] all = Physics.CapsuleCastAll(top, bottom, radius, direction, distance, mask, triggers);
                    count = all.Length;
                    return all;
                }
                Array.Resize(ref scratch, Math.Min(MaximumScratch, scratch.Length * 2));
            }
        }

        internal static Collider[] OverlapSphere(Vector3 point, float radius, ref Collider[] scratch,
            int mask, QueryTriggerInteraction triggers, out int count)
        {
            while (true)
            {
                count = Physics.OverlapSphereNonAlloc(point, radius, scratch, mask, triggers);
                if (count < scratch.Length) return scratch;
                if (scratch.Length >= MaximumScratch)
                {
                    Collider[] all = Physics.OverlapSphere(point, radius, mask, triggers);
                    count = all.Length;
                    return all;
                }
                Array.Resize(ref scratch, Math.Min(MaximumScratch, scratch.Length * 2));
            }
        }
    }
}
