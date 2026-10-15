using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two retained material voices; a sleeping crate owns no audio update.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatZombieAudio : MonoBehaviour
    {
        public const int MaximumGroans = 2;
        private const int RevealIndex = 0, GroanIndex = 1, AttackIndex = 4,
            WoodIndex = 6, FleshIndex = 8, DeathIndex = 10;
        private static readonly string[] ClipNames =
        {
            "ZombieReveal", "ZombieGroan", "ZombieGroan2", "ZombieGroan3",
            "ZombieLunge", "ZombieLunge2", "ZombieHurtWood", "ZombieHurtWood2",
            "ZombieHurtFlesh", "ZombieHurtFlesh2", "ZombieDeath", "ZombieDeath2"
        };
        private static readonly CombatZombieAudio[] GroanOwners = new CombatZombieAudio[MaximumGroans];
        private static AudioClip[] clips;
        private static uint variantSequence;
        private AudioSource body, effect;
        private Transform sourceAnchor;
        private bool initialized, alive, dead, revealed, frozen, ownsGroan, revealing;
        private int lastGroan = -1, lastAttack = -1, lastWood = -1, lastFlesh = -1, lastDeath = -1;
        private double nextHurtTime;

        public AudioSource BodySource => body;
        public AudioSource EffectSource => effect;
        public int RevealPlayCount { get; private set; }
        public int DeathPlayCount { get; private set; }
        public int AcceptedGroanCount { get; private set; }
        public static int ActiveGroanCount
        {
            get
            {
                PruneGroans();
                int count = 0;
                for (int i = 0; i < GroanOwners.Length; i++) if (GroanOwners[i] != null) count++;
                return count;
            }
        }

        public void Initialize(Transform anchor)
        {
            if (initialized) return;
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            WarmClips();
            sourceAnchor = anchor;
            body = CreateVoice(anchor, "Porter Breath And Strain");
            effect = CreateVoice(anchor, "Porter Wood And Catch");
            initialized = true;
            Reset();
        }

        private static void WarmClips()
        {
            if (clips != null) return;
            var bank = new AudioClip[ClipNames.Length];
            for (int i = 0; i < bank.Length; i++) bank[i] = CombatZombieAssetProvider.LoadAudio(ClipNames[i]);
            clips = bank;
        }

        private static AudioSource CreateVoice(Transform anchor, string name)
        {
            var host = new GameObject(name);
            host.transform.SetParent(anchor, false);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = source.loop = false;
            // The warehouse bus owns the room response once. A local filter
            // retains the body of the signal without a second reverb tail.
            CityWorkAudio.Configure(source, true, 7200f, 14f, false);
            source.minDistance = .9f;
            source.ignoreListenerPause = false;
            return source;
        }

        public void Reveal()
        {
            if (!CanStart || revealed || dead) return;
            revealed = revealing = true;
            alive = false;
            ReleaseGroan();
            StopVoice(effect);
            Play(body, clips[RevealIndex], .84f, 1f, 60);
            RevealPlayCount++;
        }

        /// <summary>The laboratory and the completed reveal enter life without another cue.</summary>
        public void Activate()
        {
            if (!initialized || dead) return;
            revealed = alive = true;
            revealing = false;
        }

        public void AliveGroan()
        {
            if (!CanStart || !alive || dead || body.isPlaying || !AcquireGroan()) return;
            Play(body, Select(GroanIndex, 3, ref lastGroan), .35f, 1f, 126);
            AcceptedGroanCount++;
        }

        public void Attack()
        {
            if (!CanStart || !alive || dead || revealing) return;
            ReleaseGroan();
            Play(body, Select(AttackIndex, 2, ref lastAttack), .61f, 1f, 82);
        }

        public void Hurt(bool wood)
        {
            if (!CanStart || !revealed || dead || AudioSettings.dspTime < nextHurtTime) return;
            nextHurtTime = AudioSettings.dspTime + .12d;
            ReturnEffectToAnchor();
            Play(effect, wood ? Select(WoodIndex, 2, ref lastWood) : Select(FleshIndex, 2, ref lastFlesh),
                wood ? .64f : .52f, 1f, 90);
        }

        /// <summary>Released timber and fish share one bounded effect voice, including after death.</summary>
        public bool MaterialContact(bool wood, Vector3 position, float speed)
        {
            if (!CanStart || (!revealed && !dead) || !Finite(speed) || speed < .6f ||
                !Finite(position.x) || !Finite(position.y) || !Finite(position.z) ||
                AudioSettings.dspTime < nextHurtTime) return false;
            nextHurtTime = AudioSettings.dspTime + .12d;
            // The short contact stays where it happened while the corpse moves.
            // Both voices remain retained and are stopped by this owner's lifecycle.
            effect.transform.SetParent(null, true);
            effect.transform.position = position;
            float strength = Mathf.InverseLerp(.6f, 4.2f, speed);
            Play(effect, wood ? Select(WoodIndex, 2, ref lastWood) : Select(FleshIndex, 2, ref lastFlesh),
                Mathf.Lerp(wood ? .24f : .18f, wood ? .72f : .62f, strength),
                Mathf.Lerp(.94f, 1.06f, strength), 104);
            return true;
        }

        private void ReturnEffectToAnchor()
        {
            if (effect == null || sourceAnchor == null) return;
            if (effect.transform.parent != sourceAnchor) effect.transform.SetParent(sourceAnchor, false);
            effect.transform.localPosition = Vector3.zero;
        }

        /// <summary>One finite release also covers a lethal strike before a sleeping crate can open.</summary>
        public void Death()
        {
            if (!initialized || !isActiveAndEnabled || dead) return;
            dead = true;
            alive = revealing = false;
            ReleaseGroan();
            StopVoice(effect);
            Play(body, Select(DeathIndex, 2, ref lastDeath), .76f, 1f, 56);
            DeathPlayCount++;
        }

        private bool CanStart => initialized && isActiveAndEnabled && !frozen && !GameTimeScaleRuntime.IsPaused;

        private void Play(AudioSource source, AudioClip clip, float volume, float pitch, int priority)
        {
            source.Stop();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.priority = priority;
            source.Play();
            if (frozen || GameTimeScaleRuntime.IsPaused) source.Pause();
        }

        private static AudioClip Select(int start, int count, ref int previous)
        {
            int index = (int)(variantSequence++ % (uint)count);
            if (index == previous) index = (index + 1) % count;
            previous = index;
            return clips[start + index];
        }

        private bool AcquireGroan()
        {
            PruneGroans();
            for (int i = 0; i < GroanOwners.Length; i++)
            {
                if (GroanOwners[i] != null) continue;
                GroanOwners[i] = this;
                ownsGroan = true;
                return true;
            }
            return false;
        }

        private static void PruneGroans()
        {
            for (int i = 0; i < GroanOwners.Length; i++)
            {
                CombatZombieAudio owner = GroanOwners[i];
                if (owner != null && owner.ownsGroan && owner.isActiveAndEnabled &&
                    owner.body != null && (owner.frozen || GameTimeScaleRuntime.IsPaused || owner.body.isPlaying)) continue;
                if (owner != null) owner.ownsGroan = false;
                GroanOwners[i] = null;
            }
        }

        private void ReleaseGroan()
        {
            if (!ownsGroan) return;
            for (int i = 0; i < GroanOwners.Length; i++) if (GroanOwners[i] == this) GroanOwners[i] = null;
            ownsGroan = false;
        }

        public void SetFrozen(bool value)
        {
            if (frozen == value) return;
            frozen = value;
            if (!initialized) return;
            if (value) { body.Pause(); effect.Pause(); }
            else { body.UnPause(); effect.UnPause(); }
        }

        public void Reset()
        {
            ReleaseGroan();
            StopVoice(body);
            StopVoice(effect);
            // CombatActor also resets during hierarchy deactivation, when
            // Unity forbids reparenting into the hierarchy being disabled.
            if (gameObject.activeInHierarchy) ReturnEffectToAnchor();
            alive = dead = revealed = frozen = revealing = false;
            nextHurtTime = 0d;
            lastGroan = lastAttack = lastWood = lastFlesh = lastDeath = -1;
            RevealPlayCount = DeathPlayCount = AcceptedGroanCount = 0;
        }

        private static void StopVoice(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void OnDisable()
        {
            ReleaseGroan();
            StopVoice(body);
            StopVoice(effect);
            frozen = false;
        }

        private void OnDestroy()
        {
            ReleaseGroan();
            ClearVoice(ref body);
            ClearVoice(ref effect);
        }

        private static void ClearVoice(ref AudioSource source)
        {
            if (source == null) return;
            StopVoice(source);
            Destroy(source.gameObject);
            source = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Array.Clear(GroanOwners, 0, GroanOwners.Length);
            clips = null;
            variantSequence = 0;
        }
    }
}
