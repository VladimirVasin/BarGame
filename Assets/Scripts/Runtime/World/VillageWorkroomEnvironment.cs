using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>The real closed lower room muffles the same exterior weather.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class VillageWorkroomEnvironment : MonoBehaviour, IPlayerFootstepSurface
    {
        private AlpineVillageRoot village;
        private VillageWorkroomInstance room;
        private ParticleSystem[] fields;
        private ParticleSystem.Particle[][] particles;
        private Light[] lights;
        private float[] lightLevels;
        private VillageLifeAudio contacts;
        public float Enclosure { get; private set; }
        public int CulledParticles { get; private set; }
        public bool IsInside => village != null && room != null && village.Player.GameObject != null &&
            room.Plan.ContainsInterior(village.Player.GameObject.transform.position);

        public void Initialize(AlpineVillageRoot owner, VillageWorkroomInstance instance, VillageLifeAudio audio)
        {
            village = owner; room = instance; contacts = audio;
            fields = new[] { owner.Snow.Particles, owner.Fog.Particles,
                owner.BlowingSnow.Particles, owner.PeripheralBlizzard.Particles };
            particles = new ParticleSystem.Particle[fields.Length][];
            for (int i = 0; i < fields.Length; i++)
                particles[i] = new ParticleSystem.Particle[fields[i].main.maxParticles];
            lights = new[] { CreateLight("AmbientLight", .95f, 5.1f, true),
                CreateLight("RepairLamp", .45f, 2.4f, false), CreateLight("SewingLamp", .45f, 2.4f, false) };
            lightLevels = new[] { .95f, .45f, .45f };
            owner.Player.Motor.SetFootstepSurface(this);
        }

        private Light CreateLight(string anchor, float intensity, float range, bool shadows)
        {
            var host = new GameObject("Workroom " + anchor);
            host.transform.SetParent(transform, false);
            host.transform.position = room.Plan.Anchor(anchor);
            var light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .73f, .46f);
            light.intensity = intensity; light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .8f; light.shadowBias = .035f; light.shadowNormalBias = .1f;
            if (shadows && Application.isPlaying)
            {
                // A point light needs six faces. URP's default High (1024)
                // overfills the PC 2048 atlas and rescales every face to 512.
                // Request that effective resolution directly, leaving room
                // in the atlas without changing the lamp or its soft shadows.
                light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier =
                    UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            }
            return light;
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        public void Advance(float dt)
        {
            if (room == null || village == null || dt < 0f || GameTimeScaleRuntime.IsPaused) return;
            bool inside = IsInside;
            float doorOpen = village.World.ResidentDoors["village-house-08"].OpenFraction;
            Enclosure = Mathf.MoveTowards(Enclosure, inside ? Mathf.Lerp(1f, .72f, doorOpen) : 0f, dt * 1.8f);
            if (!GameSessionState.IsRidingAVehicle) village.WindSound.SetEnclosure(Enclosure);
            village.Soundscape.SetListenerEnclosure(Enclosure);
            contacts.SetRoomAcoustics(inside, doorOpen);
            for (int i = 0; i < lights.Length; i++)
                lights[i].intensity = lightLevels[i] * Mathf.Lerp(1f, .4f, village.WarmthGrade);
            CullInteriorWeather();
        }

        public void CullInteriorWeather()
        {
            if (fields == null) return;
            // Keep the field outside the walls even when the observer is
            // outdoors. A player-centred shelter hole would erase nearby snow
            // through the window and still let flakes appear in an empty room.
            for (int field = 0; field < fields.Length; field++)
            {
                ParticleSystem system = fields[field];
                if (system == null || system.particleCount == 0) continue;
                var buffer = particles[field];
                int count = system.GetParticles(buffer), kept = 0;
                var main = system.main;
                for (int i = 0; i < count; i++)
                {
                    Vector3 world = buffer[i].position;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Local)
                        world = system.transform.TransformPoint(world);
                    else if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
                        world = main.customSimulationSpace.TransformPoint(world);
                    if (room.Plan.ContainsInterior(world)) { CulledParticles++; continue; }
                    buffer[kept++] = buffer[i];
                }
                if (kept != count) system.SetParticles(buffer, kept);
            }
        }

        public bool TryPlayFootstep(Vector3 position, float runBlend)
        {
            if (room != null && room.Plan.ContainsInterior(position))
            {
                contacts.PlayWood(position, .20f);
                return true;
            }
            return village != null && village.World.SnowTreading != null &&
                village.World.SnowTreading.TryPlayFootstep(position, runBlend);
        }

        private void OnDisable()
        {
            if (village == null) return;
            village.WindSound?.SetEnclosure(0f);
            village.Soundscape?.SetListenerEnclosure(0f);
            if (village.Player.Motor != null) village.Player.Motor.SetFootstepSurface(village.World.SnowTreading);
        }
    }
}
