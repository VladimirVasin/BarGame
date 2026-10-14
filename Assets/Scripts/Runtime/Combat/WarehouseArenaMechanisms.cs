using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Four finite rigged machines. Scene-owned time, cached pivots and causal local audio.</summary>
    public sealed class WarehouseArenaMechanisms : MonoBehaviour
    {
        private enum Motion { Fan, Lift, Drum, Pusher }
        private struct Machine
        {
            public Motion Motion;
            public Transform Pivot;
            public Vector3 Origin;
            public Quaternion Rotation;
            public float Phase;
            public AudioSource Voice;
            public int Cycle;
        }

        private Machine[] machines = Array.Empty<Machine>();
        private CombatTestRoot owner;
        private AudioClip contact;
        private bool audioPaused;
        public double ElapsedSeconds { get; private set; }
        public Transform FanRotor { get; private set; }
        public Light FanLight { get; private set; }
        public int MachineCount => machines.Length;

        public void Initialize(WarehouseArenaPlan plan, CombatTestRoot warehouse)
        {
            owner = warehouse;
            machines = new Machine[plan.Mechanisms.Count];
            float[] samples = StairwellSoundscapeSynthesis.GenerateCueSamples(StairwellSoundscapeCueKind.MetalStress);
            contact = AudioClip.Create("Warehouse mechanism strain", samples.Length, 1, StairwellSoundscapeSynthesis.SampleRate, false);
            contact.SetData(samples, 0);
            for (int i = 0; i < machines.Length; i++)
            {
                WarehouseArenaMechanism specification = plan.Mechanisms[i];
                Quaternion rotation = Quaternion.Euler(0f, specification.Yaw, 0f);
                Transform pivot;
                Motion motion;
                switch (specification.Kind)
                {
                    case "Fan":
                        GameObject housing = WarehouseArenaAssetProvider.Create("FanHousing", transform,
                            specification.Position + Vector3.up * .42f, rotation);
                        FanLight = WarehouseArenaLighting.CreateFanOverhead(
                            WarehouseArenaAssetProvider.FindAnchor(housing, "FanLight"), transform);
                        pivot = WarehouseArenaAssetProvider.Create("FanRotor", transform, specification.Position, rotation).transform;
                        FanRotor = pivot;
                        gameObject.AddComponent<WarehouseArenaAmbience>().Initialize(pivot, owner);
                        motion = Motion.Fan;
                        break;
                    case "Lift":
                        WarehouseArenaAssetProvider.Create("LiftFrame", transform, specification.Position, rotation);
                        pivot = WarehouseArenaAssetProvider.Create("LiftPlatform", transform, specification.Position, rotation).transform;
                        motion = Motion.Lift;
                        break;
                    case "Drum":
                        GameObject drum = WarehouseArenaAssetProvider.Create("ChainDrum", transform, specification.Position, rotation);
                        pivot = WarehouseArenaAssetProvider.FindAnchor(drum, "DrumRotor");
                        motion = Motion.Drum;
                        break;
                    case "Pusher":
                        WarehouseArenaAssetProvider.Create("PusherFrame", transform, specification.Position, rotation);
                        pivot = WarehouseArenaAssetProvider.Create("PusherHead", transform, specification.Position, rotation).transform;
                        motion = Motion.Pusher;
                        break;
                    default: throw new InvalidOperationException("Unknown warehouse machine " + specification.Kind);
                }
                machines[i] = new Machine { Motion = motion, Pivot = pivot, Origin = pivot.localPosition,
                    Rotation = pivot.localRotation, Phase = specification.Phase, Cycle = -1 };
                if (motion != Motion.Fan)
                {
                    var voice = new GameObject("Warehouse " + specification.Kind + " Strain");
                    voice.transform.SetParent(transform, false);
                    voice.transform.localPosition = specification.Position + Vector3.up;
                    AudioSource source = voice.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    CityWorkAudio.Configure(source, true, 2100f, 13f, false);
                    source.clip = contact;
                    source.volume = .07f;
                    source.pitch = motion == Motion.Lift ? .64f : motion == Motion.Drum ? .82f : 1.04f;
                    machines[i].Voice = source;
                }
            }
            ApplyPoses(false);
        }

        private bool Running => owner != null && owner.isActiveAndEnabled && !GameTimeScaleRuntime.IsPaused;

        private void Update()
        {
            bool running = Running;
            if (audioPaused == running)
            {
                foreach (Machine machine in machines)
                    if (machine.Voice != null)
                    {
                        if (running) machine.Voice.UnPause();
                        else machine.Voice.Pause();
                    }
                audioPaused = !running;
            }
            if (running) Advance(Time.deltaTime);
        }

        public void Advance(float seconds)
        {
            if (!Running || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            ElapsedSeconds += seconds;
            // A background jump restores phase without replaying missed contacts.
            ApplyPoses(seconds <= .25f);
        }

        private void ApplyPoses(bool sound)
        {
            for (int i = 0; i < machines.Length; i++)
            {
                ref Machine machine = ref machines[i];
                double time = ElapsedSeconds + machine.Phase;
                float phase;
                int cycle;
                switch (machine.Motion)
                {
                    case Motion.Fan:
                        machine.Pivot.localRotation = machine.Rotation * Quaternion.Euler(0f, (float)(time * 9d % 360d), 0f);
                        continue;
                    case Motion.Drum:
                        machine.Pivot.localRotation = machine.Rotation * Quaternion.Euler((float)(time * -17d % 360d), 0f, 0f);
                        cycle = (int)(time / 13d);
                        break;
                    case Motion.Lift:
                        phase = (float)(time % 24d) / 24f;
                        machine.Pivot.localPosition = machine.Origin + Vector3.up * Mathf.Lerp(.4f, 6.6f, .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2f));
                        cycle = (int)(time / 12d);
                        break;
                    default:
                        phase = (float)(time % 8d) / 8f;
                        machine.Pivot.localPosition = machine.Origin + machine.Rotation * Vector3.forward *
                            Mathf.Lerp(.2f, .65f, .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2f));
                        cycle = (int)(time / 4d);
                        break;
                }
                if (cycle != machine.Cycle && machine.Cycle >= 0 && sound && owner.Hero != null &&
                    (owner.Hero.transform.position - machine.Voice.transform.position).sqrMagnitude < 169f)
                    machine.Voice.Play();
                machine.Cycle = cycle;
            }
        }

        private void OnDisable()
        {
            foreach (Machine machine in machines) if (machine.Voice != null) machine.Voice.Stop();
            audioPaused = false;
        }

        private void OnDestroy() { if (contact != null) Destroy(contact); }
    }
}
