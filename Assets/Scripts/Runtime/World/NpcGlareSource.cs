using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A directed lamp that can dazzle a person. Its real Light owns aim, power and range.</summary>
    [DisallowMultipleComponent]
    internal sealed class NpcGlareSource : MonoBehaviour
    {
        private static readonly List<NpcGlareSource> active = new();
        private static RaycastHit[] hits = new RaycastHit[8];
        private Light beam;
        private Transform owner;

        internal static void Bind(Light beam, Transform owner)
        {
            if (!beam.TryGetComponent(out NpcGlareSource emitter))
                emitter = beam.gameObject.AddComponent<NpcGlareSource>();
            emitter.beam = beam;
            emitter.owner = owner;
        }

        private void OnEnable() => active.Add(this);
        private void OnDisable() => active.Remove(this);

        internal static bool HasSources => active.Count != 0;

        internal static Light Find(Transform person, Vector3 eyes, Vector3 forward, int faceLayer)
        {
            Light best = null;
            float bestExposure = .10f;
            for (int i = 0; i < active.Count; i++)
            {
                var source = active[i];
                Light light = source.beam;
                if (!light || !light.isActiveAndEnabled || light.intensity <= 0f ||
                    (light.cullingMask & (1 << faceLayer)) == 0 || person.IsChildOf(source.owner)) continue;

                Transform lens = light.transform;
                Vector3 delta = lens.position - eyes;
                float squared = delta.sqrMagnitude;
                float rangeSquared = light.range * light.range;
                if (squared < .0001f || squared >= rangeSquared) continue;
                float distance = Mathf.Sqrt(squared);
                Vector3 direction = delta / distance;
                if (Vector3.Dot(forward, direction) < .24f) continue;
                float cosine = Vector3.Dot(-direction, lens.forward);
                float outer = Mathf.Cos(light.spotAngle * (.5f * Mathf.Deg2Rad));
                if (cosine <= outer) continue;
                float inner = Mathf.Cos(light.innerSpotAngle * (.5f * Mathf.Deg2Rad));
                float cone = Mathf.Clamp01((cosine - outer) / Mathf.Max(.0001f, inner - outer));
                float range = 1f - squared / rangeSquared;
                float exposure = light.intensity * cone * cone * range * range / Mathf.Max(.01f, squared);
                if (exposure <= bestExposure || Blocked(eyes, direction, distance, person, source.owner)) continue;
                best = light;
                bestExposure = exposure;
            }
            return best;
        }

        private static bool Blocked(Vector3 eyes, Vector3 direction, float distance, Transform person, Transform owner)
        {
            // Read from the face so a housing or a body capsule at either endpoint
            // cannot hide an intervening wall. Reuse the buffer across all probes.
            RaycastHit[] result;
            int count;
            while (true)
            {
                result = hits;
                count = Physics.RaycastNonAlloc(eyes, direction, result, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < result.Length) break;
                if (result.Length >= 128)
                {
                    result = Physics.RaycastAll(eyes, direction, distance,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    count = result.Length;
                    break;
                }
                Array.Resize(ref hits, result.Length * 2);
            }
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = result[i].collider.transform;
                if (obstacle.IsChildOf(person) || person.IsChildOf(obstacle) || obstacle.IsChildOf(owner)) continue;
                return true;
            }
            return false;
        }
    }
}
