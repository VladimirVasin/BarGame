using UnityEngine;

namespace BarPromenade
{
    // Spatial contacts use the player's ear height even when the third-person camera
    // swings across a wall. Unity still pans sources in the active camera's frame.
    [DefaultExecutionOrder(32000), DisallowMultipleComponent]
    internal sealed class WorldSoundListener : MonoBehaviour
    {
        internal static WorldSoundListener Active { get; private set; }
        internal Vector3 Ear => transform.position + Vector3.up * 1.48f;
        internal const float RefreshSeconds = .20f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private AudioSource reflection;
        private Vector3 wall, probedEar, reflectionPoint;
        private float wallDistance, nextWallProbe;
        private double nextReflection;
        private bool foundWall;

        private void OnEnable()
        {
            Active = this;
            nextWallProbe = 0f;
            nextReflection = 0d;
            foundWall = false;
        }
        private void OnDisable()
        {
            if (Active == this) Active = null;
            if (reflection) reflection.Stop();
        }

        // Four short scene queries per refresh, independent of the number of feet.
        private void LateUpdate()
        {
            if (reflection) reflection.transform.position = reflectionPoint;
            if (Active != this || GameTimeScaleRuntime.IsPaused || Time.time < nextWallProbe) return;
            nextWallProbe = Time.time + RefreshSeconds;
            probedEar = Ear;
            foundWall = false;
            wallDistance = 4.5f;
            ProbeWall(Vector3.left);
            ProbeWall(Vector3.right);
            ProbeWall(Vector3.forward);
            ProbeWall(Vector3.back);
        }

        private void ProbeWall(Vector3 direction)
        {
            if (!Trace(probedEar, direction, wallDistance, null, out RaycastHit hit) ||
                Mathf.Abs(hit.normal.y) > .45f) return;
            foundWall = true;
            wallDistance = hit.distance;
            wall = hit.point + hit.normal * .08f;
        }

        internal static CitySoundOcclusionSample Sample(Vector3 position, float radius, Transform emitter = null)
        {
            var listener = Active;
            if (!listener) return Clear;
            Vector3 delta = listener.Ear - position;
            float distance = delta.magnitude;
            if (distance < .5f || distance > radius) return Clear;
            Vector3 direction = delta / distance;
            bool blocked = Trace(position + direction * .12f, direction, distance - .24f, emitter, out _);
            float depth = Mathf.InverseLerp(radius * .22f, radius, distance);
            float cutoff = Mathf.Lerp(20000f, 3000f, depth * depth);
            return blocked ? new CitySoundOcclusionSample(1, .43f, Mathf.Min(cutoff, 1450f))
                : new CitySoundOcclusionSample(0, 1f, cutoff);
        }

        internal static CitySoundOcclusionSample Clear => new(0, 1f, 22000f);

        private static bool Trace(Vector3 origin, Vector3 direction, float distance, Transform emitter, out RaycastHit closest)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var hits = Hits;
            if (count == Hits.Length)
            {
                hits = Physics.RaycastAll(origin, direction, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            closest = default;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                ref var hit = ref hits[i];
                var collider = hit.collider;
                if (hit.distance >= best || collider is CharacterController || collider is CapsuleCollider ||
                    (emitter && collider.transform.IsChildOf(emitter)) ||
                    (Active && collider.transform.IsChildOf(Active.transform))) continue;
                // A chair or a hand-held prop cannot screen the street.
                Vector3 size = collider.bounds.size;
                if (Mathf.Max(size.x, size.z) < 1.4f || Mathf.Max(size.y, Mathf.Min(size.x, size.z)) < .8f) continue;
                closest = hit;
                best = hit.distance;
            }
            return best < float.PositiveInfinity;
        }

        internal static void ReflectStep(AudioClip clip, Vector3 foot, float directGain, float pitch)
        {
            var ear = Active;
            if (!ear || !ear.foundWall || !clip || (ear.Ear - foot).sqrMagnitude > 20f ||
                (ear.Ear - ear.probedEar).sqrMagnitude > 2.25f ||
                AudioSettings.dspTime < ear.nextReflection) return;
            Vector3 contact = foot + Vector3.up * .12f;
            Vector3 towardWall = ear.wall - contact;
            float travelToWall = towardWall.magnitude;
            if (travelToWall > .3f && Trace(contact, towardWall / travelToWall,
                    travelToWall - .10f, null, out _)) return;
            if (!ear.reflection)
            {
                var host = new GameObject("Footstep wall reflection");
                host.transform.SetParent(ear.transform, false);
                ear.reflection = host.AddComponent<AudioSource>();
                ear.reflection.playOnAwake = false;
                ear.reflection.spatialBlend = 1f;
                ear.reflection.dopplerLevel = 0f;
                ear.reflection.rolloffMode = AudioRolloffMode.Linear;
                ear.reflection.maxDistance = 8f;
                ear.reflection.bypassReverbZones = true;
                GameAudioMixer.Route(ear.reflection, GameAudioGroup.SfxWorld);
                host.AddComponent<AudioLowPassFilter>().cutoffFrequency = 2600f;
            }
            var voice = ear.reflection;
            voice.transform.position = ear.reflectionPoint = ear.wall;
            float returnDistance = travelToWall + Vector3.Distance(ear.wall, ear.Ear);
            float extraTravel = Mathf.Max(0f, returnDistance - Vector3.Distance(contact, ear.Ear));
            voice.volume = directGain * Mathf.Lerp(.24f, .09f, Mathf.Clamp01(returnDistance / 9f));
            voice.pitch = pitch;
            voice.clip = clip;
            double now = AudioSettings.dspTime;
            voice.PlayScheduled(now + .012d + extraTravel / 343d);
            ear.nextReflection = now + clip.length / Mathf.Max(.2f, pitch) + .04d;
        }
    }

    // Continuous emitters reuse one sample between acoustic updates.
    internal struct MovingSoundPath
    {
        private WorldSoundListener listener;
        private float nextProbe;
        private CitySoundOcclusionSample sample;
        internal CitySoundOcclusionSample Read(Vector3 position, float radius, Transform emitter = null)
        {
            var current = WorldSoundListener.Active;
            if (!current) return WorldSoundListener.Clear;
            if (listener != current || Time.time >= nextProbe)
            {
                listener = current;
                nextProbe = Time.time + WorldSoundListener.RefreshSeconds;
                sample = WorldSoundListener.Sample(position, radius, emitter);
            }
            return sample;
        }
    }
}
