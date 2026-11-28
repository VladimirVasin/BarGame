using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    // Two drop voices and one foliage voice serve the whole loaded exterior.
    // Sampling is independent of the view frustum and the visual particle budget.
    internal sealed class CityWeatherSoundscape : MonoBehaviour
    {
        private readonly AudioSource[] drops = new AudioSource[2];
        private AudioSource leaves;
        private AudioClip leafClip;
        private CityRainField field;
        private CityRainSoundPlayer bed;
        private CityWeatherController weather;
        private float nextSurvey, nextDrop, cover, targetCover, leafGain;
        private Collider roof;
        private Transform crown;
        private Vector3 roofPoint;
        private uint noise;
        private int dropIndex;
        internal float Cover => cover;
        internal RetroSfxId LastContact { get; private set; }

        internal void Initialize(CityRainField rain, CityRainSoundPlayer ambience, CityWeatherController weather, int seed)
        {
            field = rain;
            bed = ambience;
            this.weather = weather;
            noise = unchecked((uint)seed) | 1u;
            for (int i = 0; i < drops.Length; i++) drops[i] = Voice("Rain contact", 9f, false);
            leaves = Voice("Wind in foliage", 19f, true);
            leafClip = CreateLeaves();
            leaves.clip = leafClip;
        }

        private AudioSource Voice(string label, float radius, bool loop)
        {
            var host = new GameObject(label);
            host.transform.SetParent(transform, false);
            var voice = host.AddComponent<AudioSource>();
            voice.loop = loop;
            voice.volume = 0f;
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.dopplerLevel = 0f;
            voice.minDistance = 1f;
            voice.maxDistance = radius;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.priority = 175;
            GameAudioMixer.Route(voice, GameAudioGroup.AmbienceDetails);
            return voice;
        }

        private void Update()
        {
            if (!field || !field.FollowTarget || GameTimeScaleRuntime.IsPaused || AudioListener.pause) return;
            var ear = WorldSoundListener.Active;
            Vector3 position = ear ? ear.Ear : field.FollowTarget.position + Vector3.up * 1.48f;
            if (Time.time >= nextSurvey)
            {
                nextSurvey = Time.time + .20f;
                Survey(position);
            }
            cover = Mathf.MoveTowards(cover, targetCover, Time.deltaTime * 3.5f);
            bed.SetCover(cover);
            float intensity = field.Intensity;
            if (!field.IsSheltered && intensity > .005f && Time.time >= nextDrop)
            {
                nextDrop = Time.time + Mathf.Lerp(.85f, .095f, intensity) * Mathf.Lerp(.60f, 1.35f, Unit());
                Drop(position, intensity);
            }
            float gain = crown && crown.gameObject.activeInHierarchy ? leafGain : 0f;
            leaves.volume = Mathf.MoveTowards(leaves.volume, gain, Time.deltaTime * .28f);
            if (leaves.volume > .001f && !leaves.isPlaying) leaves.Play();
            else if (leaves.volume <= .001f && leaves.isPlaying) leaves.Stop();
        }

        private void Survey(Vector3 ear)
        {
            float best = 7f * 7f;
            roof = null;
            targetCover = field.IsSheltered ? 1f : 0f;
            foreach (var candidate in field.LocalShelters)
            {
                if (!candidate || !candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
                Bounds box = candidate.bounds;
                if (box.max.y < ear.y - .15f || box.SqrDistance(ear) > best) continue;
                Vector3 above = box.ClosestPoint(ear);
                // Cast onto the actual tilted/rotated roof from above. Moving the
                // sample just inside the bounds also catches a lip beside the ear.
                above.x = Mathf.Lerp(above.x, box.center.x, .025f);
                above.z = Mathf.Lerp(above.z, box.center.z, .025f);
                above.y = box.max.y + .08f;
                if (!candidate.Raycast(new Ray(above, Vector3.down), out var hit, box.size.y + .16f)) continue;
                Vector3 offset = hit.point - ear;
                float horizontal = offset.x * offset.x + offset.z * offset.z;
                if (horizontal < .04f && offset.y >= -.08f)
                    targetCover = 1f;
                float score = offset.sqrMagnitude;
                if (score >= best) continue;
                best = score;
                roof = candidate;
                roofPoint = hit.point;
            }
            crown = CitySoundTree.Nearest(ear, 19f, out Vector3 source);
            if (!crown) { leafGain = 0f; return; }
            var wind = weather.CurrentWind;
            float phase = Time.time - Vector3.Dot(source, wind.HorizontalDirection) * .04f;
            float gust = CityWindField.Gust(wind, phase).Strength01;
            var path = WorldSoundListener.Sample(source, 19f);
            leafGain = gust * gust * .38f * path.VolumeMultiplier * (1f - .65f * targetCover);
            leaves.transform.position = source;
        }

        private void Drop(Vector3 ear, float intensity)
        {
            Vector3 point;
            RetroSfxId cue;
            if (roof && roof.gameObject.activeInHierarchy && (targetCover > .5f || Unit() < .62f))
            {
                point = roofPoint;
                var material = roof.GetComponent<CityRainSoundMaterial>();
                cue = material ? material.Contact : RetroSfxId.RainOnStone;
                // Stacked shelter volumes in the ravine include lower storeys.
                // Only the uppermost exposed surface can receive a sky drop.
                if (field.Contacts != null &&
                    field.Contacts.ProbeSoundSurface(new Ray(point + Vector3.up * 24f, Vector3.down), 24.35f, out var top) &&
                    top.Point.y > point.y + .4f)
                {
                    point = top.Point;
                    cue = Contact(top.Kind);
                }
            }
            else
            {
                float angle = Unit() * Mathf.PI * 2f;
                float radius = .8f + 4.6f * Unit();
                Vector3 above = ear + new Vector3(Mathf.Cos(angle) * radius, 25f, Mathf.Sin(angle) * radius);
                if (field.Contacts == null || !field.Contacts.ProbeSoundSurface(new Ray(above, Vector3.down), 34f, out var hit)) return;
                point = hit.Point;
                cue = Contact(hit.Kind);
            }
            if (cue == RetroSfxId.None || (point - ear).sqrMagnitude > 81f) return;
            var voice = drops[dropIndex];
            dropIndex = (dropIndex + 1) % drops.Length;
            var clip = RetroAudioService.EnsureInstalled().GetClip(cue, Mathf.Min(2, (int)(Unit() * 3f)));
            if (!clip) return;
            var path = WorldSoundListener.Sample(point + Vector3.up * .05f, 9f);
            voice.transform.position = point;
            voice.pitch = .89f + Unit() * .23f;
            voice.volume = (.14f + .17f * intensity) * path.VolumeMultiplier;
            voice.clip = clip;
            voice.Play();
            LastContact = cue;
        }

        private static RetroSfxId Contact(CityRainSurfaceKind kind) => kind switch
        {
            CityRainSurfaceKind.Fabric => RetroSfxId.RainOnCanvas,
            CityRainSurfaceKind.Metal or CityRainSurfaceKind.Glass => RetroSfxId.RainOnSheet,
            CityRainSurfaceKind.Stone or CityRainSurfaceKind.Puddle or CityRainSurfaceKind.Water => RetroSfxId.RainOnStone,
            _ => RetroSfxId.None
        };

        private float Unit()
        {
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            return (noise >> 8) * (1f / 16777216f);
        }

        private static AudioClip CreateLeaves()
        {
            const int rate = 22050, count = 3 * rate, overlap = 512;
            var samples = new float[count + overlap];
            uint state = 0x813ab29u;
            float slow = 0f, fast = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                float white = (state >> 8) / 8388608f - 1f;
                fast += .41f * (white - fast);
                slow += .078f * (white - slow);
                float envelope = .55f + .23f * Mathf.Sin(i * .00057f) + .14f * Mathf.Sin(i * .00371f);
                samples[i] = (fast - slow) * envelope;
            }
            for (int i = 0; i < overlap; i++) samples[i] = Mathf.Lerp(samples[count+i], samples[i], i / (float)overlap);
            System.Array.Resize(ref samples, count);
            var clip = AudioClip.Create("Wind through leaves", count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDisable()
        {
            foreach (var drop in drops) if (drop) drop.Stop();
            if (leaves) { leaves.Stop(); leaves.volume = 0f; }
            roof = null; crown = null; cover = targetCover = leafGain = nextSurvey = 0f;
            if (bed) bed.SetCover(0f);
        }

        private void OnDestroy()
        {
            if (leafClip) Destroy(leafClip);
        }
    }

    internal sealed class CityRainSoundMaterial : MonoBehaviour
    {
        internal RetroSfxId Contact;
    }

    // Register real crowns once when their built district is active.
    internal sealed class CitySoundTree : MonoBehaviour
    {
        private static readonly List<CitySoundTree> trees = new();
        internal float Height = 3.1f;
        private void OnEnable() => trees.Add(this);
        private void OnDisable() => trees.Remove(this);

        internal static Transform Nearest(Vector3 ear, float radius, out Vector3 position)
        {
            float best = radius * radius;
            Transform result = null;
            position = default;
            foreach (var tree in trees)
            {
                Vector3 point = tree.transform.TransformPoint(Vector3.up * tree.Height);
                float distance = (point - ear).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                result = tree.transform;
                position = point;
            }
            return result;
        }
    }
}
