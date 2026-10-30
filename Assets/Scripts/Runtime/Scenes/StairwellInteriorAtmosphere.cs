using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class StairwellInteriorAtmosphere : MonoBehaviour
    {
        public const int MaximumPracticalLights = 3;
        public const int MaximumDustParticles = 14;
        public const int MaximumActiveLabyrinthLights = 8;
        public const int MaximumLabyrinthShadowLights = 2;
        public const float LabyrinthActivationRadius = 22f;
        public const float LabyrinthDeactivationRadius = 24f;
        public const float FilmGrainIntensity = 0.05f;
        public const float VignetteIntensity = 0.23f;
        public const float CorruptedFogStart = 9f;
        public const float CorruptedFogEnd = 32f;
        public const float CorruptedHazeStartZ = 6f;
        public const float CorruptedHazeEndZ = 9.8f;
        public static readonly Vector3 UpperGlowPosition = new Vector3(-1.45f, 8.7f, 9.45f);
        public static readonly Color CorruptedFogColor = new Color(.13f, .17f, .14f);

        internal static readonly Vector3 GroundEmitterPosition =
            new Vector3(-1.45f, 2.70f, -3.40f);
        // The middle pendant must hang low over the middle landing
        // (elevation 1.6) and lean toward the back rail, or the cat
        // perch reads as pure black: at the old ceiling height the
        // inverse-square falloff left the landing unlit.
        internal static readonly Vector3 MiddleEmitterPosition =
            new Vector3(-0.45f, 4.15f, 1.62f);
        internal static readonly Vector3 CorruptedMiddleEmitterPosition =
            new Vector3(1.55f, 4.15f, 1.62f);
        internal static readonly Vector3 ApartmentEmitterPosition =
            new Vector3(2.48f, 5.78f, -3.20f);

        private readonly List<Light> practicalLights =
            new List<Light>(MaximumPracticalLights);
        private ReadOnlyCollection<Light> readOnlyPracticalLights;
        private VolumeProfile runtimeProfile;
        private Material distantGlowMaterial;
        private Camera sceneCamera;
        private bool corrupted;
        private bool ownsEnvironment;
        private bool previousFog;
        private FogMode previousFogMode;
        private Color previousFogColor, previousBackground, previousAmbient;
        private float previousFogStart, previousFogEnd;
        private Light sceneSun;
        private Color previousSunColor;
        private float previousSunIntensity;
        private Quaternion previousSunRotation;
        private Renderer[] hazeRenderers;
        private Vector4[] previousHazePlanes, previousHazeColors;
        private Vector4 hazePlane;
        private MaterialPropertyBlock hazeProperties;
        private Transform labyrinthTarget, labyrinthSpace;
        private StairwellLabyrinthPlan labyrinthPlan;
        private Renderer[] labyrinthLenses, lowerEmitters;
        private Color[] labyrinthEmission;
        private bool[] labyrinthRoomLamps;
        private CityLightHalo[] lowerHalos;
        private Light apartmentBounce;
        private StairwellSoundscape labyrinthSoundscape;
        private MaterialPropertyBlock lampProperties;
        private int activeLabyrinthLight = -1;
        // Every powered source retains shadows; the established two-shadow budget
        // prevents the wider activation area from leaking through service-room walls.
        private readonly int[] nearbyLabyrinthLights = new int[MaximumLabyrinthShadowLights];
        private readonly float[] nearbyLabyrinthScores = new float[MaximumLabyrinthShadowLights];
        private bool labyrinthDarkness;
        private bool suppressLampCue;
        private float previousReflectionIntensity, previousAmbientIntensity;
        private AmbientMode previousAmbientMode;
        private SphericalHarmonicsL2 previousAmbientProbe, lowerAmbientProbe;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private float nextLabyrinthUpdate;
        public float LabyrinthProgress { get; private set; }
        public Light[] LabyrinthLights { get; private set; }
        public int ActiveLabyrinthLightCount { get; private set; }
        public int ActiveLabyrinthShadowCount { get; private set; }
        public int ActiveLabyrinthLightIndex => activeLabyrinthLight;
        public bool IsLabyrinthDarkness => labyrinthDarkness;
        private static readonly int HazePlaneId = Shader.PropertyToID("_SurfaceHazePlane");
        private static readonly int HazeColorId = Shader.PropertyToID("_SurfaceHazeColor");

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<Light> PracticalLights =>
            readOnlyPracticalLights ??
            (readOnlyPracticalLights =
                practicalLights.AsReadOnly());
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile => runtimeProfile;
        public ParticleSystem Dust { get; private set; }
        public Light UpperGlow { get; private set; }
        public CityLightHalo UpperGlowHalo { get; private set; }

        public void ConfigureLabyrinth(StairwellLabyrinthPlan route, Transform space, Transform hero,
            StairwellSoundscape soundscape)
        {
            labyrinthPlan = route;
            labyrinthSpace = space;
            labyrinthTarget = hero;
            labyrinthSoundscape = soundscape;
            // The former luminous dead end now contains an actual route.
            if (UpperGlow != null) UpperGlow.gameObject.SetActive(false);
            LabyrinthLights = new Light[route.LightPositions.Count];
            labyrinthLenses = new Renderer[LabyrinthLights.Length];
            labyrinthEmission = new Color[LabyrinthLights.Length];
            labyrinthRoomLamps = new bool[LabyrinthLights.Length];
            lampProperties = new MaterialPropertyBlock();
            for (int i = 0; i < LabyrinthLights.Length; i++)
            {
                StairwellLabyrinthLightPlan fixture = route.LightDescriptors[i];
                foreach (var support in route.Supports)
                    if (support.Id.StartsWith("room-", StringComparison.Ordinal) &&
                        !support.Id.EndsWith("-approach", StringComparison.Ordinal) &&
                        support.Stage == fixture.Stage && support.Bounds.Contains(new Vector2(fixture.Position.x, fixture.Position.z)))
                        labyrinthRoomLamps[i] = true;
                Color tint = fixture.Stage == 1 ? new Color(.54f, .72f, .61f) :
                    fixture.Stage == 2 ? new Color(.83f, .70f, .44f) : new Color(.80f, .28f, .13f);
                if (fixture.IsScenery) tint = Color.Lerp(tint, new Color(.72f, .75f, .67f), .25f);
                float intensity = fixture.Stage == 3 ? 20f : 24f;
                Light light = CreatePracticalLight("Labyrinth Practical " + (i + 1),
                    Vector3.zero, tint, intensity, 14f, 0f, false, flicker: false);
                light.transform.position = space.TransformPoint(fixture.Position);
                Vector3 direction = space.TransformDirection(fixture.Direction);
                light.transform.rotation = Quaternion.LookRotation(direction,
                    Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .95f ? Vector3.forward : Vector3.up);
                if (!fixture.IsScenery)
                {
                    light.type = LightType.Spot;
                    bool directed = Vector3.Dot(fixture.Direction, Vector3.down) < .95f;
                    // The projection lamp also lights the controls beside its
                    // beam; the room's shadowed walls contain that wider cone.
                    light.spotAngle = 150f;
                    light.innerSpotAngle = directed ? 100f : 110f;
                    light.shadowBias = directed ? .015f : .035f;
                    light.shadowNormalBias = directed ? .04f : .16f;
                    UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
                    data.usePipelineSettings = false;
                    if (Application.isPlaying)
                        data.additionalLightsShadowResolutionTier = directed
                            ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium
                            : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                }
                light.bounceIntensity = 0f;
                light.enabled = false;
                LabyrinthLights[i] = light;
            }
            CacheFixturePresentation(space);
            soundscape.PrepareLabyrinthLighting();
            // The warehouse uses this same ventilation synthesis. A fixed source
            // at the final machinery becomes nearer as the player climbs.
            AudioSource ventilation = soundscape.VentilationSource;
            ventilation.transform.position = space.TransformPoint(route.DoorPosition + Vector3.up * 2f);
            ventilation.maxDistance = 52f;
            ventilation.pitch = .7f;
            UpdateLabyrinth();
        }

        private void Update()
        {
            if (labyrinthTarget == null || GameTimeScaleRuntime.IsPaused || Time.unscaledTime < nextLabyrinthUpdate) return;
            nextLabyrinthUpdate = Time.unscaledTime + .1f;
            UpdateLabyrinth();
        }

        private void UpdateLabyrinth()
        {
            if (labyrinthTarget == null) return;
            Vector3 local = labyrinthSpace.InverseTransformPoint(labyrinthTarget.position);
            LabyrinthProgress = labyrinthPlan.ProgressAt(local);
            // The familiar lower stairwell, including the stone Cat, keeps its
            // original lighting until the hero steps onto the first long rise.
            bool inside = labyrinthPlan.ContainsPosition(local, .2f) &&
                local.z >= (labyrinthDarkness ? 3f : 3.2f);
            SetLabyrinthDarkness(inside);
            if (runtimeProfile.TryGet(out ColorAdjustments grade))
            {
                grade.colorFilter.value = Color.Lerp(new Color(.76f, .83f, .70f), Color.white, LabyrinthProgress);
                grade.saturation.value = Mathf.Lerp(-18f, -12f, LabyrinthProgress);
            }
            if (runtimeProfile.TryGet(out DepthOfField depth))
            {
                depth.gaussianStart.value = inside ? 85f : 5f;
                depth.gaussianEnd.value = inside ? 155f : 16f;
            }
            UpdateLabyrinthLights(local, inside);
        }

        private void SetLabyrinthDarkness(bool active, bool force = false)
        {
            if (labyrinthDarkness == active && !force) return;
            labyrinthDarkness = active;
            foreach (Light light in practicalLights) light.enabled = !active;
            if (apartmentBounce != null) apartmentBounce.enabled = !active;
            foreach (Renderer renderer in lowerEmitters) if (renderer != null) renderer.enabled = !active;
            foreach (CityLightHalo halo in lowerHalos) if (halo != null) halo.HaloRenderer.enabled = !active;
            if (runtimeProfile.TryGet(out FilmGrain grain)) grain.intensity.value = active ? 0f : FilmGrainIntensity;
            SetDepthHaze(!active);
            if (Dust != null)
            {
                var shape = Dust.shape;
                shape.scale = active ? new Vector3(3f, 2f, 3f) : new Vector3(7.4f, 5.2f, 7.8f);
                if (!active)
                {
                    Dust.Clear();
                    Dust.transform.localPosition = new Vector3(0f, 3f, -.4f);
                }
            }
            if (active)
            {
                lowerAmbientProbe = RenderSettings.ambientProbe;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0f;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                RenderSettings.reflectionIntensity = 0f;
                if (sceneSun != null) sceneSun.intensity = 0f;
                if (sceneCamera != null) sceneCamera.backgroundColor = Color.black;
            }
            else
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = CorruptedFogColor;
                RenderSettings.fogStartDistance = CorruptedFogStart;
                RenderSettings.fogEndDistance = CorruptedFogEnd;
                RenderSettings.reflectionIntensity = previousReflectionIntensity;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.13f, .16f, .13f);
                RenderSettings.ambientIntensity = previousAmbientIntensity;
                RenderSettings.ambientProbe = lowerAmbientProbe;
                if (sceneSun != null) sceneSun.intensity = .38f;
                if (sceneCamera != null) sceneCamera.backgroundColor = CorruptedFogColor;
            }
        }

        private void CacheFixturePresentation(Transform space)
        {
            lowerEmitters = new Renderer[MaximumPracticalLights];
            lowerHalos = new CityLightHalo[MaximumPracticalLights];
            foreach (Renderer renderer in space.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.EndsWith(" Fluorescent Tube", StringComparison.Ordinal))
                {
                    int lower = renderer.name.StartsWith("Ground", StringComparison.Ordinal) ? 0 :
                        renderer.name.StartsWith("Middle", StringComparison.Ordinal) ? 1 : 2;
                    lowerEmitters[lower] = renderer;
                    continue;
                }
                if (!renderer.name.EndsWith("__Lens", StringComparison.Ordinal)) continue;
                int index = -1;
                int marker = renderer.name.IndexOf("__Fixture_", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    int start = marker + "__Fixture_".Length;
                    int end = renderer.name.IndexOf("__", start, StringComparison.Ordinal);
                    if (end > start && !int.TryParse(renderer.name.Substring(start, end - start), out index)) index = -1;
                }
                else if (renderer.name.Contains("__Motion_suspended-lamp__")) index = 3;
                if (index < 0 || index >= LabyrinthLights.Length || labyrinthLenses[index] != null)
                    throw new InvalidOperationException("Each labyrinth lens needs one authored fixture index: " + renderer.name);
                labyrinthLenses[index] = renderer;
                labyrinthEmission[index] = renderer.sharedMaterial.GetColor(EmissionColorId);
                SetLens(index, false);
            }
            for (int i = 0; i < labyrinthLenses.Length; i++)
                if (labyrinthLenses[i] == null) throw new InvalidOperationException("Missing labyrinth fixture lens " + i);
            foreach (CityLightHalo halo in space.GetComponentsInChildren<CityLightHalo>(true))
            {
                if (!halo.name.EndsWith(" Fluorescent Halo", StringComparison.Ordinal)) continue;
                int index = halo.name.StartsWith("Ground", StringComparison.Ordinal) ? 0 :
                    halo.name.StartsWith("Middle", StringComparison.Ordinal) ? 1 : 2;
                lowerHalos[index] = halo;
            }
        }

        private void SetLens(int index, bool lit)
        {
            Renderer renderer = labyrinthLenses[index];
            if (renderer == null) return;
            renderer.GetPropertyBlock(lampProperties);
            lampProperties.SetColor(EmissionColorId, lit ? labyrinthEmission[index] : Color.black);
            renderer.SetPropertyBlock(lampProperties);
        }

        private void UpdateLabyrinthLights(Vector3 local, bool inside)
        {
            for (int slot = 0; slot < nearbyLabyrinthLights.Length; slot++)
            {
                nearbyLabyrinthLights[slot] = -1;
                nearbyLabyrinthScores[slot] = float.PositiveInfinity;
            }
            if (inside)
            {
                // Room supports win their overlaps with the approach. Otherwise the
                // actual supporting storey, never camera aim, owns lamp selection.
                int supportIndex = -1;
                float supportScore = float.PositiveInfinity;
                for (int i = 0; i < labyrinthPlan.Supports.Count; i++)
                {
                    var support = labyrinthPlan.Supports[i];
                    Rect r = support.Bounds;
                    float dx = local.x - Mathf.Clamp(local.x, r.xMin, r.xMax);
                    float dz = local.z - Mathf.Clamp(local.z, r.yMin, r.yMax);
                    float dy = local.y - PlayerFactory.GroundedRootOffset - support.HeightAt(local);
                    float score = dx * dx + dz * dz + dy * dy * 16f;
                    if (support.Id.StartsWith("room-", StringComparison.Ordinal) &&
                        !support.Id.EndsWith("-approach", StringComparison.Ordinal) && dx == 0f && dz == 0f)
                        score -= 1f;
                    if (score >= supportScore) continue;
                    supportScore = score;
                    supportIndex = i;
                }
                if (supportIndex >= 0)
                {
                    var support = labyrinthPlan.Supports[supportIndex];
                    bool room = support.Id.StartsWith("room-", StringComparison.Ordinal) &&
                        !support.Id.EndsWith("-approach", StringComparison.Ordinal);
                    for (int i = 0; i < labyrinthPlan.LightDescriptors.Count; i++)
                    {
                        var fixture = labyrinthPlan.LightDescriptors[i];
                        if (fixture.IsScenery || fixture.Stage != support.Stage) continue;
                        if (!room && labyrinthRoomLamps[i]) continue;
                        if (room && !support.Bounds.Contains(new Vector2(fixture.Position.x, fixture.Position.z))) continue;
                        Vector3 delta = LabyrinthLights[i].transform.position - labyrinthTarget.position;
                        float distance = delta.sqrMagnitude;
                        float radius = LabyrinthLights[i].enabled ? LabyrinthDeactivationRadius : LabyrinthActivationRadius;
                        if (distance > radius * radius) continue;
                        float score = distance - (LabyrinthLights[i].enabled ? 4f : 0f);
                        for (int slot = 0; slot < nearbyLabyrinthLights.Length; slot++)
                        {
                            if (score >= nearbyLabyrinthScores[slot]) continue;
                            for (int next = nearbyLabyrinthLights.Length - 1; next > slot; next--)
                            {
                                nearbyLabyrinthLights[next] = nearbyLabyrinthLights[next - 1];
                                nearbyLabyrinthScores[next] = nearbyLabyrinthScores[next - 1];
                            }
                            nearbyLabyrinthLights[slot] = i;
                            nearbyLabyrinthScores[slot] = score;
                            break;
                        }
                    }
                }
            }
            int selected = nearbyLabyrinthLights[0];
            int switchedOn = -1, switchedOff = -1;
            float onDistance = float.PositiveInfinity, offDistance = float.PositiveInfinity;
            ActiveLabyrinthLightCount = ActiveLabyrinthShadowCount = 0;
            for (int i = 0; i < LabyrinthLights.Length; i++)
            {
                Light light = LabyrinthLights[i];
                bool powered = Array.IndexOf(nearbyLabyrinthLights, i) >= 0;
                if (light.enabled != powered)
                {
                    float distance = (light.transform.position - labyrinthTarget.position).sqrMagnitude;
                    if (powered && distance < onDistance) { switchedOn = i; onDistance = distance; }
                    if (!powered && distance < offDistance) { switchedOff = i; offDistance = distance; }
                    light.enabled = powered;
                    light.shadows = powered ? LightShadows.Hard : LightShadows.None;
                    SetLens(i, powered);
                }
                if (powered) { ActiveLabyrinthLightCount++; ActiveLabyrinthShadowCount++; }
            }
            if (selected != activeLabyrinthLight)
            {
                activeLabyrinthLight = selected;
                if (Dust != null)
                {
                    Dust.Clear();
                    if (selected >= 0) Dust.transform.position = LabyrinthLights[selected].transform.position + Vector3.down;
                }
            }
            Transform nearest = selected >= 0 ? LabyrinthLights[selected].transform : null;
            // Hum follows the nearest powered lamp. Physical lamp transitions alone
            // trigger the two existing contactor voices, coalesced to the nearest
            // changed source in each batch; ranking changes cannot replay a switch.
            labyrinthSoundscape.SetLabyrinthLighting(inside, nearest, false, preserveContactorTail: true);
            if (!suppressLampCue)
            {
                if (switchedOff >= 0) labyrinthSoundscape.PlayLabyrinthLampCue(LabyrinthLights[switchedOff].transform, false);
                if (switchedOn >= 0) labyrinthSoundscape.PlayLabyrinthLampCue(LabyrinthLights[switchedOn].transform, true);
            }
            suppressLampCue = false;
            if (Dust != null)
            {
                var emission = Dust.emission;
                emission.enabled = !inside || selected >= 0;
            }
        }

        public bool IsLabyrinthPositionLit(Vector3 world)
        {
            if (LabyrinthLights == null) return false;
            foreach (Light light in LabyrinthLights)
            {
                if (!light.enabled) continue;
                Vector3 delta = world - light.transform.position;
                if (delta.sqrMagnitude < light.range * light.range &&
                    (light.type != LightType.Spot || Vector3.Angle(light.transform.forward, delta) < light.spotAngle * .5f)) return true;
            }
            return false;
        }

        public void Initialize(bool corrupted = false, Camera camera = null)
        {
            if (IsInitialized ||
                practicalLights.Count > 0 ||
                PostProcessVolume != null)
            {
                throw new InvalidOperationException(
                    "The stairwell atmosphere is already initialized.");
            }

            this.corrupted = corrupted;
            sceneCamera = camera != null ? camera : Camera.main;
            if (corrupted)
            {
                ApplyCorruptedEnvironment();
                CreateUpperGlow();
                // Finite reflected light from the surviving apartment fixture
                // reaches the south-facing stone without a second visible lamp.
                apartmentBounce = CreatePracticalLight("Apartment Landing Bounce",
                    new Vector3(1.4f, 5f, -4.05f), new Color(.79f, .65f, .46f),
                    2f, 4f, 0f, false, flicker: false);
                apartmentBounce.bounceIntensity = 0f;
            }
            practicalLights.Add(
                CreatePracticalLight(
                    "Ground Entrance Light",
                    GroundEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.50f, 0.70f, 0.55f),
                    6.00f,
                    7.0f,
                    0.2f,
                    false));
            practicalLights.Add(
                CreatePracticalLight(
                    "Middle Flickering Light",
                    (corrupted ? CorruptedMiddleEmitterPosition : MiddleEmitterPosition) + Vector3.down * 0.16f,
                    new Color(0.46f, 0.67f, 0.59f),
                    10.00f,
                    9.5f,
                    1.7f,
                    true));
            practicalLights.Add(
                CreatePracticalLight(
                    "Apartment Landing Light",
                    ApartmentEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.82f, 0.58f, 0.29f),
                    6.00f,
                    7.0f,
                    3.1f,
                    false));
            CreatePostProcessVolume();
            CreateDust();
            IsInitialized = true;
        }

        public void ConfigureCorruptedDepthHaze(Transform world)
        {
            if (!corrupted || hazeRenderers != null) return;
            hazeProperties = new MaterialPropertyBlock();
            hazeRenderers = world.GetComponentsInChildren<Renderer>();
            previousHazePlanes = new Vector4[hazeRenderers.Length];
            previousHazeColors = new Vector4[hazeRenderers.Length];
            hazePlane = world.worldToLocalMatrix.GetRow(2);
            hazePlane.w -= CorruptedHazeStartZ;
            hazePlane /= CorruptedHazeEndZ - CorruptedHazeStartZ;
            distantGlowMaterial.SetVector("_DepthHazePlane", hazePlane);
            for (int i = 0; i < hazeRenderers.Length; i++)
            {
                Renderer receiver = hazeRenderers[i];
                if (receiver.sharedMaterial == null || !receiver.sharedMaterial.HasProperty(HazePlaneId)) continue;
                receiver.GetPropertyBlock(hazeProperties);
                previousHazePlanes[i] = hazeProperties.GetVector(HazePlaneId);
                previousHazeColors[i] = hazeProperties.GetVector(HazeColorId);
            }
            SetDepthHaze(!labyrinthDarkness);
        }

        private void SetDepthHaze(bool active)
        {
            if (hazeRenderers == null) return;
            Color tint = QualitySettings.activeColorSpace == ColorSpace.Linear ? CorruptedFogColor.linear : CorruptedFogColor;
            var color = new Vector4(tint.r, tint.g, tint.b, 1f);
            for (int i = 0; i < hazeRenderers.Length; i++)
            {
                Renderer receiver = hazeRenderers[i];
                if (receiver == null || receiver.sharedMaterial == null || !receiver.sharedMaterial.HasProperty(HazePlaneId)) continue;
                receiver.GetPropertyBlock(hazeProperties);
                hazeProperties.SetVector(HazePlaneId, active ? hazePlane : previousHazePlanes[i]);
                hazeProperties.SetVector(HazeColorId, active ? color : previousHazeColors[i]);
                receiver.SetPropertyBlock(hazeProperties);
            }
        }

        private void ApplyCorruptedEnvironment()
        {
            if (ownsEnvironment) return;
            previousFog = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor;
            previousFogStart = RenderSettings.fogStartDistance;
            previousFogEnd = RenderSettings.fogEndDistance;
            previousAmbient = RenderSettings.ambientLight;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientProbe = RenderSettings.ambientProbe;
            lowerAmbientProbe = previousAmbientProbe;
            previousAmbientIntensity = RenderSettings.ambientIntensity;
            previousReflectionIntensity = RenderSettings.reflectionIntensity;
            if (sceneCamera != null) previousBackground = sceneCamera.backgroundColor;
            sceneSun = RenderSettings.sun;
            if (sceneSun != null)
            {
                previousSunColor = sceneSun.color;
                previousSunIntensity = sceneSun.intensity;
                previousSunRotation = sceneSun.transform.rotation;
                sceneSun.color = new Color(.61f, .70f, .57f);
                sceneSun.intensity = .38f;
                sceneSun.transform.rotation = Quaternion.LookRotation(new Vector3(.15f, -.55f, -1f));
            }
            RenderSettings.ambientLight = new Color(.13f, .16f, .13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = CorruptedFogColor;
            RenderSettings.fogStartDistance = CorruptedFogStart;
            RenderSettings.fogEndDistance = CorruptedFogEnd;
            if (sceneCamera != null) sceneCamera.backgroundColor = CorruptedFogColor;
            ownsEnvironment = true;
        }

        private void RestoreEnvironment()
        {
            if (!ownsEnvironment) return;
            ownsEnvironment = false;
            // A subsequent scene may already have installed its own state.
            // Release only values still owned by this composition.
            if (gameObject.scene == SceneManager.GetActiveScene() && (labyrinthTarget != null || (RenderSettings.fog &&
                RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogColor == CorruptedFogColor &&
                Mathf.Approximately(RenderSettings.fogEndDistance, CorruptedFogEnd))))
            {
                RenderSettings.fog = previousFog;
                RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogColor = previousFogColor;
                RenderSettings.fogStartDistance = previousFogStart;
                RenderSettings.fogEndDistance = previousFogEnd;
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientProbe = previousAmbientProbe;
                RenderSettings.ambientIntensity = previousAmbientIntensity;
                RenderSettings.reflectionIntensity = previousReflectionIntensity;
                if (sceneSun != null && RenderSettings.sun == sceneSun)
                {
                    sceneSun.color = previousSunColor;
                    sceneSun.intensity = previousSunIntensity;
                    sceneSun.transform.rotation = previousSunRotation;
                }
            }
            if (sceneCamera != null && (labyrinthTarget != null || sceneCamera.backgroundColor == CorruptedFogColor))
                sceneCamera.backgroundColor = previousBackground;
        }

        private void OnEnable()
        {
            if (IsInitialized && corrupted)
            {
                ApplyCorruptedEnvironment();
                if (LabyrinthLights != null) SetLabyrinthDarkness(false, true);
                SetDepthHaze(true);
                UpdateLabyrinth();
            }
        }

        private void OnDisable()
        {
            RestoreEnvironment();
            labyrinthDarkness = false;
            SetDepthHaze(false);
            if (LabyrinthLights == null) return;
            foreach (Light light in LabyrinthLights) if (light != null) light.enabled = false;
            for (int i = 0; i < labyrinthLenses.Length; i++) SetLens(i, false);
            activeLabyrinthLight = -1;
            suppressLampCue = true;
            foreach (Light light in practicalLights) if (light != null) light.enabled = false;
            if (apartmentBounce != null) apartmentBounce.enabled = false;
            foreach (Renderer renderer in lowerEmitters) if (renderer != null) renderer.enabled = false;
            foreach (CityLightHalo halo in lowerHalos) if (halo != null) halo.HaloRenderer.enabled = false;
            if (labyrinthSoundscape != null) labyrinthSoundscape.SetLabyrinthLighting(true, null, false);
            ActiveLabyrinthLightCount = ActiveLabyrinthShadowCount = 0;
        }

        private void CreateUpperGlow()
        {
            var source = new GameObject("Corrupted Upper Stair Glow");
            source.transform.SetParent(transform, false);
            source.transform.localPosition = UpperGlowPosition;
            source.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1.6f, -3.84f));
            UpperGlow = source.AddComponent<Light>();
            UpperGlow.type = LightType.Spot;
            UpperGlow.color = new Color(1f, .79f, .43f);
            UpperGlow.intensity = 45f;
            UpperGlow.range = 18f;
            UpperGlow.spotAngle = 55f;
            UpperGlow.innerSpotAngle = 28f;
            UpperGlow.shadows = LightShadows.Hard;
            UpperGlow.shadowBias = .035f;
            UpperGlow.shadowNormalBias = .15f;
            UpperGlow.bounceIntensity = 0f;
            UpperGlow.renderMode = LightRenderMode.ForcePixel;
            var halo = new GameObject("Corrupted Upper Stair Glow Halo");
            halo.transform.SetParent(source.transform, false);
            UpperGlowHalo = halo.AddComponent<CityLightHalo>();
            Shader shader = Resources.Load<Shader>("Shaders/StairwellDistantGlow");
            if (shader == null) throw new InvalidOperationException("The corrupted stairwell requires its distant glow shader.");
            distantGlowMaterial = new Material(shader) { name = "Stairwell Distant Glow", hideFlags = HideFlags.HideAndDontSave };
            UpperGlowHalo.Initialize(distantGlowMaterial, 3.6f, 8f,
                new Color(1f, .94f, .64f, .96f), new Color(.67f, .22f, .075f, .40f));
            UpperGlowHalo.HaloRenderer.maxParticleSize = 1f;
        }

        private Light CreatePracticalLight(
            string lightName,
            Vector3 position,
            Color color,
            float intensity,
            float range,
            float flickerPhase,
            bool strongerFlicker,
            bool flicker = true)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = position;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.06f;
            if (flicker)
            {
                StairwellLightFlicker controller =
                    lightObject.AddComponent<StairwellLightFlicker>();
                controller.Initialize(intensity, flickerPhase, strongerFlicker);
            }
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject =
                new GameObject("Stairwell Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume =
                volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            runtimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name =
                "Runtime Stairwell Interior Grade";
            runtimeProfile.hideFlags =
                HideFlags.HideAndDontSave;
            PostProcessVolume.profile = runtimeProfile;

            Bloom bloom = runtimeProfile.Add<Bloom>(true);
            bloom.intensity.Override(corrupted ? .28f : .18f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.48f);

            ColorAdjustments color =
                runtimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(corrupted ? -.04f : -.12f);
            color.contrast.Override(12f);
            color.saturation.Override(corrupted ? -18f : -28f);
            color.colorFilter.Override(
                corrupted ? new Color(.76f, .83f, .70f, 1f) : new Color(0.72f, 0.82f, 0.71f, 1f));

            Vignette vignette =
                runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(0.56f);

            FilmGrain grain =
                runtimeProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(FilmGrainIntensity);
            grain.response.Override(0.78f);

            RuntimeSceneSetup.AddIndoorGaussianDepthOfField(
                runtimeProfile, 5f, 16f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(runtimeProfile);
        }

        private void CreateDust()
        {
            GameObject dustObject =
                new GameObject("Stairwell Damp Dust");
            dustObject.transform.SetParent(transform, false);
            dustObject.transform.localPosition =
                new Vector3(0f, 3.0f, -0.4f);
            Dust = dustObject.AddComponent<ParticleSystem>();
            Dust.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            Dust.useAutoRandomSeed = false;
            Dust.randomSeed = 0x53544149u;

            ParticleSystem.MainModule main = Dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaximumDustParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(13f, 21f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.006f, 0.021f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.035f, 0.105f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.46f, 0.61f, 0.52f, 0.022f),
                new Color(0.72f, 0.58f, 0.38f, 0.012f));
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = Dust.emission;
            emission.rateOverTime = 0.42f;

            ParticleSystem.ShapeModule shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7.4f, 5.2f, 7.8f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                Dust.velocityOverLifetime;
            velocity.enabled = false;
            velocity.x =
                new ParticleSystem.MinMaxCurve(-0.006f, 0.006f);
            velocity.y =
                new ParticleSystem.MinMaxCurve(0.004f, 0.014f);
            velocity.z =
                new ParticleSystem.MinMaxCurve(-0.004f, 0.004f);
            velocity.enabled = true;

            ParticleSystemRenderer renderer =
                Dust.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial =
                CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.07f;
            Dust.Play();
        }

        private void OnDestroy()
        {
            RestoreEnvironment();
            labyrinthDarkness = false;
            SetDepthHaze(false);
            if (distantGlowMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(distantGlowMaterial);
                else Object.DestroyImmediate(distantGlowMaterial);
                distantGlowMaterial = null;
            }
            if (runtimeProfile == null)
            {
                return;
            }

            if (PostProcessVolume != null)
            {
                PostProcessVolume.profile = null;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeProfile);
            }
            else
            {
                Object.DestroyImmediate(runtimeProfile);
            }

            runtimeProfile = null;
            IsInitialized = false;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class StairwellLightFlicker : MonoBehaviour
    {
        private Light controlledLight;
        private float baseIntensity;
        private float phase;
        private float flickerStrength;

        public float CurrentFactor { get; private set; } = 1f;

        public void Initialize(
            float intensity,
            float phaseOffset,
            bool strongerFlicker)
        {
            controlledLight = GetComponent<Light>();
            baseIntensity = Mathf.Max(0f, intensity);
            phase = phaseOffset;
            flickerStrength = strongerFlicker ? 0.18f : 0.07f;
            Apply(1f);
        }

        private void Update()
        {
            if (controlledLight == null)
            {
                return;
            }

            float time = Time.unscaledTime + phase;
            float wobble =
                Mathf.Sin(time * 7.3f) * 0.45f +
                Mathf.Sin(time * 17.9f + 0.8f) * 0.32f +
                Mathf.Sin(time * 31.1f + 2.1f) * 0.23f;
            float dipSignal =
                Mathf.Sin(time * 2.3f + phase * 1.7f);
            float dip = dipSignal > 0.94f
                ? (dipSignal - 0.94f) / 0.06f
                : 0f;
            float factor =
                1f +
                wobble * flickerStrength -
                dip * flickerStrength * 0.65f;
            Apply(Mathf.Clamp(factor, 0.72f, 1.12f));
        }

        private void Apply(float factor)
        {
            CurrentFactor = factor;
            if (controlledLight != null)
            {
                controlledLight.intensity =
                    baseIntensity * CurrentFactor;
            }
        }
    }
}
