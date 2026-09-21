using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Session-local deposits, attached to stable surfaces rather than scene
    /// objects. Each carries the scope of the scene that left it: Home's own
    /// have none and last the session, the combat polygon's are cleared by R.
    /// </summary>
    public static class HomeUrineResidue
    {
        public const int Capacity = 384;
        public sealed class Deposit
        {
            public string SurfaceId;
            public string Scope;
            public Vector3 LocalPoint;
            public Vector3 LocalNormal;
            public float Radius;
            public bool Wall;
            public int Revision;
        }

        private static readonly List<Deposit> deposits = new List<Deposit>(Capacity);
        private static int replacement;
        private static int revision;
        public static IReadOnlyList<Deposit> Deposits => deposits;
        public static int Generation { get; private set; }

        public static int Add(HomeUrineSurfaceMap.Hit hit, float amount) => Add(hit, amount, null);

        public static int Add(HomeUrineSurfaceMap.Hit hit, float amount, string scope)
        {
            HomeUrineSurfaceMap.Surface surface = hit.Surface;
            bool wall = Mathf.Abs(hit.Normal.y) < 0.65f;
            float initial = wall ? 0.028f : 0.038f;
            float maximum = wall ? 0.115f : 0.19f;
            for (int i = 0; i < deposits.Count; i++)
            {
                Deposit old = deposits[i];
                if (old.SurfaceId != surface.Id || old.Wall != wall || !SameScope(old.Scope, scope)) continue;
                Vector3 oldPoint = surface.Transform.TransformPoint(old.LocalPoint);
                Vector3 oldNormal = surface.Transform.worldToLocalMatrix.transpose.MultiplyVector(old.LocalNormal).normalized;
                if (Vector3.Dot(oldNormal, hit.Normal) < 0.94f ||
                    Mathf.Abs(Vector3.Dot(hit.Point - oldPoint, hit.Normal)) > 0.016f ||
                    Vector3.Distance(oldPoint, hit.Point) > Mathf.Max(initial, old.Radius * 0.7f)) continue;
                old.Radius = Mathf.Min(maximum, Mathf.Sqrt(old.Radius * old.Radius + amount * 0.000045f));
                old.Revision = ++revision;
                return i;
            }
            var deposit = new Deposit
            {
                SurfaceId = surface.Id,
                Scope = scope,
                LocalPoint = surface.Transform.InverseTransformPoint(hit.Point),
                LocalNormal = surface.Transform.localToWorldMatrix.transpose.MultiplyVector(hit.Normal).normalized,
                Radius = initial, Wall = wall, Revision = ++revision
            };
            if (deposits.Count < Capacity) { deposits.Add(deposit); return deposits.Count - 1; }
            int slot = replacement++ % Capacity;
            deposits[slot] = deposit;
            return slot;
        }

        public static int CountScope(string scope)
        {
            int count = 0;
            for (int i = 0; i < deposits.Count; i++) if (SameScope(deposits[i].Scope, scope)) count++;
            return count;
        }

        /// <summary>
        /// Drops one scene's deposits and moves the generation on, so every
        /// effect re-projects what remains; other scopes keep their marks,
        /// not their slots.
        /// </summary>
        public static int RemoveScope(string scope)
        {
            int removed = deposits.RemoveAll(deposit => SameScope(deposit.Scope, scope));
            if (removed > 0) { replacement = 0; Generation++; }
            return removed;
        }

        public static bool SameScope(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        public static void ResetSession()
        { deposits.Clear(); replacement = 0; revision = 0; Generation++; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ResetSession();
    }
}
