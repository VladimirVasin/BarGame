using UnityEngine;

namespace BarPromenade
{
    internal static class CityRoadBraking
    {
        private const float BuildSeconds = .28f;
        internal const float SedanEmergencyDeceleration = 4.8f;

        internal static float LookAhead(float speed, float deceleration) =>
            speed * speed / (2f * deceleration) + speed * (BuildSeconds + .2f);

        internal static float SpeedLimit(float clearance, float speed, float deceleration)
        {
            float room = Mathf.Max(0f, clearance);
            float reserve = Mathf.Min(room * .3f, speed * BuildSeconds);
            return Mathf.Sqrt(2f * deceleration * (room - reserve));
        }

        internal static float Apply(float speed, float limit, float room, float service,
            float emergency, float step, ref float rate)
        {
            float required = speed * speed / (2f * Mathf.Max(.02f, room));
            float target = Mathf.Clamp(required, service, emergency);
            rate = required > service * 1.2f
                ? target
                : Mathf.MoveTowards(rate, target, service * step / BuildSeconds);
            return Mathf.MoveTowards(speed, limit, rate * step);
        }
    }

    internal struct CityHornTimeline
    {
        internal const float FirstDelay = .85f;
        internal const float ContactTime = .23f;
        internal const float PressDuration = .22f;
        internal const float GestureDuration = .72f;
        private float elapsed;
        private float nextContact;
        private float gesture;
        private bool active;
        private bool engaged;
        private bool withdrawing;

        internal float Reach => active
            ? Mathf.Min(Mathf.SmoothStep(0f, 1f, gesture / ContactTime),
                Mathf.SmoothStep(0f, 1f, (GestureDuration - gesture) /
                    (GestureDuration - ContactTime - PressDuration)))
            : 0f;

        internal bool Advance(float deltaTime, bool blocked, float repeatSeconds)
        {
            if (!blocked)
            {
                engaged = false;
                elapsed = 0f;
                if (active && gesture < ContactTime) withdrawing = true;
            }
            else
            {
                if (!engaged)
                {
                    engaged = true;
                    nextContact = FirstDelay;
                }
                elapsed += deltaTime;
            }

            if (withdrawing)
            {
                gesture = Mathf.Max(0f, gesture - deltaTime);
                if (gesture > 0f) return false;
                withdrawing = active = false;
            }
            if (active)
            {
                float previous = gesture;
                gesture += deltaTime;
                if (gesture >= GestureDuration) active = false;
                if (blocked && previous < ContactTime && gesture >= ContactTime)
                {
                    nextContact += repeatSeconds;
                    return true;
                }
            }

            if (!blocked || active || elapsed < nextContact - ContactTime) return false;
            active = true;
            gesture = elapsed - (nextContact - ContactTime);
            if (gesture < ContactTime) return false;
            nextContact += repeatSeconds;
            return true;
        }
    }

    internal static class CityHornHand
    {
        internal static Pose PressPose(Transform wheel, Vector3 driverSide, Vector3 driverUp,
            NpcHandPose fingers, float faceDepth)
        {
            AnatomicalHandFrame hand = fingers.GetAnatomicalFrame(false);
            Vector3 distal = Vector3.ProjectOnPlane(driverUp, driverSide).normalized;
            Quaternion rotation = hand.ResolveHandRotation(-driverSide, distal);
            Vector3 contact = wheel.position + driverSide * faceDepth;
            return new Pose(hand.ResolveHandPosition(contact, rotation), rotation);
        }
    }

    [DisallowMultipleComponent]
    internal sealed class CityRoadVehicleFeedback : MonoBehaviour
    {
        internal const float AudibleDistance = 38f;
        private const int SampleRate = 16000;
        private static AudioClip brakeClip;
        private static AudioClip sedanHorn;
        private static AudioClip busHorn;
        private AudioSource brakes;
        private AudioSource horn;
        private CityHornTimeline timeline;
        private float repeatSeconds;
        private float brakeVolume;
        internal float HornReach => timeline.Reach;
        internal int HornCount { get; private set; }
        internal float BrakeVolume => brakeVolume;

        internal void Initialize(bool bus, float repeat, Vector3 front)
        {
            repeatSeconds = repeat;
            if (brakeClip == null) brakeClip = CreateBrakeClip();
            if (bus && busHorn == null) busHorn = CreateHornClip(true);
            if (!bus && sedanHorn == null) sedanHorn = CreateHornClip(false);
            brakes = CreateSource("Road Brake Audio", Vector3.up * .45f, brakeClip, true);
            horn = CreateSource("Road Horn Audio", front, bus ? busHorn : sedanHorn, false);
            horn.volume = bus ? .23f : .18f;
        }

        internal void Advance(float step, float speed, float deceleration, bool obstruction,
            bool hornAllowed, bool audible)
        {
            if (step <= 0f || !isActiveAndEnabled || brakes == null) return;
            if (!audible)
            {
                ResetPlayback();
                return;
            }

            float target = obstruction
                ? Mathf.InverseLerp(.18f, 2.4f, deceleration) * Mathf.InverseLerp(.12f, 5f, speed) * .24f
                : 0f;
            brakeVolume = Mathf.MoveTowards(brakeVolume, target, step * (target > brakeVolume ? 1.8f : .9f));
            brakes.volume = brakeVolume;
            if (brakeVolume > .001f && !brakes.isPlaying && Application.isPlaying && !AudioListener.pause)
                brakes.Play();
            else if (brakeVolume <= .001f && brakes.isPlaying)
                brakes.Stop();

            if (timeline.Advance(step, hornAllowed, repeatSeconds))
            {
                HornCount++;
                if (Application.isPlaying && !AudioListener.pause) horn.Play();
            }
        }

        internal void ResetPlayback()
        {
            if (brakeVolume > 0f && brakes != null)
            {
                brakes.Stop();
                brakes.volume = 0f;
            }
            if (horn != null && (horn.isPlaying || timeline.Reach > 0f)) horn.Stop();
            brakeVolume = 0f;
            timeline = default;
        }

        private void OnDisable() => ResetPlayback();

        private AudioSource CreateSource(string sourceName, Vector3 position, AudioClip clip, bool loop)
        {
            Transform anchor = new GameObject(sourceName).transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = position;
            var source = anchor.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.dopplerLevel = .15f;
            source.minDistance = 2f;
            source.maxDistance = AudibleDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.volume = 0f;
            return source;
        }

        private static AudioClip CreateBrakeClip()
        {
            // Soft, band-limited friction. The live envelope supplies the stop.
            const int length = SampleRate;
            var samples = new float[length];
            uint state = 0xB03F5917u;
            float low = 0f;
            float upper = 0f;
            for (int i = 0; i < length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                float noise = (state >> 8) / 8388608f - 1f;
                low += (noise - low) * .035f;
                upper += (noise - upper) * .24f;
                float join = Mathf.Min(1f, Mathf.Min(i, length - 1 - i) / 240f);
                samples[i] = (upper - low) * .72f * join;
            }
            return Clip("City Road Brake Friction", samples);
        }

        private static AudioClip CreateHornClip(bool bus)
        {
            var samples = new float[Mathf.RoundToInt(SampleRate * CityHornTimeline.PressDuration)];
            float low = bus ? 264f : 406f;
            float high = bus ? 335f : 507f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = (float)i / SampleRate;
                float f = 2f * Mathf.PI * time;
                float level = Mathf.Min(1f, Mathf.Min(time / .025f,
                    (CityHornTimeline.PressDuration - time) / .05f));
                samples[i] = level * (.42f * Mathf.Sin(low * f) + .28f * Mathf.Sin(high * f) +
                    .13f * Mathf.Sin(2f * low * f) + .09f * Mathf.Sin(3f * high * f));
            }
            return Clip(bus ? "City Bus Horn" : "City Sedan Horn", samples);
        }

        private static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
