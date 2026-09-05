using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Hero-local distant scenery. Each active pass stays fixed in world space.</summary>
    public sealed class CityOffshoreBoatController : MonoBehaviour
    {
        public const string RootName = "Offshore Fishing Boats";
        public const float RelocationDistance = 32f;
        public const float PresenceFadeSeconds = 3f;
        private static readonly int PresenceId = Shader.PropertyToID("_Presence");
        private static readonly int NightId = Shader.PropertyToID("_NightFactor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int UniformId = Shader.PropertyToID("_Uniform");
        private MaterialPropertyBlock block;
        private Transform[] boats = Array.Empty<Transform>();
        private Transform[] pivots;
        private Renderer[][] hulls;
        private Renderer[][] lenses;
        private Renderer[][] beams;
        private Renderer[][] cabins;
        private CityWaterWaveProfile waves;
        private double elapsed;
        private float waveTime;
        private int seed;
        private CitySeacoastPlan coast;
        private CityLighthouseIslandPlan island;
        private IReadOnlyList<BuildingLot> buildings;
        private Transform hero;
        private GameObject localRoot;
        private float localPresence;
        private bool retiring;
        private float failedSpawnX = float.NaN;

        public CityOffshoreBoatPlan Plan { get; private set; }
        public CityOffshoreBoatSound Sound { get; private set; }
        public IReadOnlyList<Transform> Boats => boats;
        public double ElapsedSeconds => elapsed;
        public bool IsSpawned => localRoot != null;
        public Vector3 SpawnAnchor { get; private set; }

        public static CityOffshoreBoatController Build(Transform parent, int seed,
            CitySeacoastPlan coast, CityLighthouseIslandPlan island, IReadOnlyList<BuildingLot> buildings)
        {
            if (coast == null) return null;
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            var controller = root.AddComponent<CityOffshoreBoatController>();
            controller.seed = seed;
            controller.coast = coast;
            controller.island = island;
            controller.buildings = buildings;
            return controller;
        }

        public void AttachHero(Transform value)
        {
            if (hero == value) return;
            ReleaseBoats();
            hero = value;
            failedSpawnX = float.NaN;
        }

        private void SpawnBoats(CityOffshoreBoatPlan plan)
        {
            block = new MaterialPropertyBlock();
            Plan = plan;
            SpawnAnchor = hero.position;
            localPresence = 0f;
            retiring = false;
            localRoot = new GameObject("Local Passing Vessels");
            localRoot.transform.SetParent(transform, false);
            int count = plan.Routes.Count;
            boats = new Transform[count];
            pivots = new Transform[count];
            hulls = new Renderer[count][];
            lenses = new Renderer[count][];
            beams = new Renderer[count][];
            cabins = new Renderer[count][];
            var engineAnchors = new Transform[count];
            var hornAnchors = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var route = plan.Routes[i];
                var model = CityOffshoreBoatAssetProvider.Create(route.Variant, localRoot.transform);
                model.name = route.StableId;
                boats[i] = model.transform;
                boats[i].localScale = Vector3.one * route.VisualScale;
                pivots[i] = Required(model, "SearchlightPivot");
                lenses[i] = Required(model, "Lens").GetComponentsInChildren<Renderer>(true);
                beams[i] = Required(model, "Beam").GetComponentsInChildren<Renderer>(true);
                cabins[i] = Required(model, "CabinGlow").GetComponentsInChildren<Renderer>(true);
                engineAnchors[i] = Required(model, "ANCHOR_Engine");
                hornAnchors[i] = Required(model, "ANCHOR_Horn");
                // Wake is drawn on the existing displaced sea, so its water contact
                // cannot detach when the hull rolls. The authored optional mesh stays off.
                Transform wake = CityOffshoreBoatAssetProvider.FindPart(model, "Wake");
                if (wake != null) wake.gameObject.SetActive(false);
                var opaque = new List<Renderer>();
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (wake != null && renderer.transform.IsChildOf(wake)) continue;
                    bool luminous = Contains(lenses[i], renderer) || Contains(beams[i], renderer) ||
                        Contains(cabins[i], renderer);
                    renderer.sharedMaterial = luminous ? CityOffshoreBoatResources.Glow : CityOffshoreBoatResources.Hull;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic = false;
                    if (!luminous) opaque.Add(renderer);
                }
                hulls[i] = opaque.ToArray();
            }
            waves = CitySeaResources.CreateWaveProfile();
            Sound = localRoot.AddComponent<CityOffshoreBoatSound>();
            Sound.Initialize(seed, boats, engineAnchors, hornAnchors);
            Sound.SetOcclusionContext(null, buildings);
            // A coastal visit starts its own pass; time spent in the city cannot
            // consume the boats or queue a horn before the hero arrives.
            elapsed = plan.Routes[0].DurationSeconds * 0.5d - plan.Routes[0].PhaseSeconds;
            ApplyAt(elapsed, Time.timeSinceLevelLoad);
        }

        private static Transform Required(GameObject model, string name)
        {
            var part = CityOffshoreBoatAssetProvider.FindPart(model, name);
            if (part == null) throw new InvalidOperationException("Missing offshore boat part: " + name);
            return part;
        }

        private static bool Contains(Renderer[] values, Renderer value) => Array.IndexOf(values, value) >= 0;

        private void Update() => Advance(Time.deltaTime);

        internal void Advance(float deltaTime)
        {
            bool running = GameSessionState.IsGameTimeRunning && !GameTimeScaleRuntime.IsPaused;
            if (running)
            {
                float delta = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                    ? 0f : Mathf.Max(0f, deltaTime);
                float shorePresence = hero != null && hero.gameObject.activeInHierarchy
                    ? CityOffshoreBoatPlanner.ShorePresence(coast, hero.position) : 0f;
                if (shorePresence <= 0f) failedSpawnX = float.NaN;
                if (!IsSpawned && shorePresence > 0f &&
                    (float.IsNaN(failedSpawnX) || Mathf.Abs(hero.position.x - failedSpawnX) >= 12f))
                {
                    var plan = CityOffshoreBoatPlanner.Create(seed, coast, island, hero.position.x);
                    if (plan != null) SpawnBoats(plan);
                    else failedSpawnX = hero.position.x;
                }
                if (!IsSpawned) return;
                // Never drag a visible hull after the hero. Retire the old pass
                // completely before selecting a new safe course nearby.
                if (hero == null || shorePresence <= 0f ||
                    Mathf.Abs(hero.position.x - SpawnAnchor.x) > RelocationDistance)
                    retiring = true;
                localPresence = Mathf.MoveTowards(localPresence,
                    retiring ? 0f : shorePresence, delta / PresenceFadeSeconds);
                if (retiring && localPresence <= 0f)
                {
                    ReleaseBoats();
                    return;
                }
                elapsed += delta;
                waveTime = Time.timeSinceLevelLoad;
            }
            ApplyAt(elapsed, waveTime);
        }

        // One explicit presentation sampler also lets the focused capture select a
        // pass without waiting minutes or adding a player-facing/debug interaction.
        internal void ApplyAt(double seconds, float sceneWaveTime)
        {
            elapsed = seconds;
            waveTime = sceneWaveTime;
            if (Plan == null) return;
            float night = CityNightSiteLightRegistry.NightFactor;
            float lampPower = Mathf.Lerp(2f / 3f, 1f, night);
            for (int i = 0; i < boats.Length; i++)
            {
                CityOffshoreBoatPose pose = Plan.Routes[i].Sample(seconds);
                float presence = pose.Presence * localPresence;
                Vector3 position = pose.Position;
                Vector3 forward = pose.Rotation * Vector3.forward;
                Vector3 right = pose.Rotation * Vector3.right;
                float fore = Height(position + forward * 1.7f, sceneWaveTime);
                float aft = Height(position - forward * 1.7f, sceneWaveTime);
                float port = Height(position - right * 0.65f, sceneWaveTime);
                float starboard = Height(position + right * 0.65f, sceneWaveTime);
                position.y = Plan.SeaTopY + (fore + aft + port + starboard) * 0.25f - 0.015f;
                float pitch = Mathf.Clamp(-Mathf.Atan2(fore - aft, 3.4f) * Mathf.Rad2Deg, -3.5f, 3.5f);
                float roll = Mathf.Clamp(Mathf.Atan2(starboard - port, 1.3f) * Mathf.Rad2Deg, -4f, 4f);
                boats[i].SetPositionAndRotation(position, pose.Rotation * Quaternion.Euler(pitch, 0f, roll));
                // Slow working sweep, confined to open water ahead; pitch stays down.
                float sweep = Mathf.Sin((float)(seconds % 10000d) * 0.083f + i * 2.7f) * 10f;
                pivots[i].localRotation = Quaternion.Euler(18f, sweep, 0f);

                block.Clear();
                block.SetFloat(PresenceId, presence);
                block.SetFloat(NightId, night);
                foreach (var renderer in hulls[i])
                {
                    renderer.enabled = presence > 0.003f;
                    renderer.SetPropertyBlock(block);
                }
                SetGlow(lenses[i], presence * lampPower * 0.65f, 1f);
                SetGlow(cabins[i], presence * lampPower * 0.065f, 1f);
                SetGlow(beams[i], presence * lampPower * 0.060f, 0f);
                Sound.SetPresence(i, presence);
                CitySeaResources.SetOffshoreBoat(i, position, forward, pivots[i].position,
                    pivots[i].forward, presence, lampPower);
            }
        }

        private float Height(Vector3 position, float seconds) =>
            CityWaterWaveModel.SampleHeight(waves, position.x, position.z, seconds);

        private void SetGlow(Renderer[] renderers, float intensity, float uniform)
        {
            block.Clear();
            block.SetFloat(IntensityId, intensity);
            block.SetFloat(UniformId, uniform);
            foreach (var renderer in renderers)
            {
                renderer.enabled = intensity > 0.0001f;
                renderer.SetPropertyBlock(block);
            }
        }

        private void ReleaseBoats()
        {
            CitySeaResources.ClearOffshoreBoats();
            if (localRoot != null)
            {
                localRoot.SetActive(false);
                Destroy(localRoot);
            }
            localRoot = null;
            Plan = null;
            Sound = null;
            boats = Array.Empty<Transform>();
            pivots = null;
            hulls = lenses = beams = cabins = null;
            block = null;
            localPresence = 0f;
            retiring = false;
        }

        private void OnDisable() => ReleaseBoats();
    }
}
