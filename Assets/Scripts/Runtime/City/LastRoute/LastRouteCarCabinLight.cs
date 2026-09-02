using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The light INSIDE the car: a plafond over the seats, the two
    /// instrument faces, and a bulb in the glovebox.
    ///
    /// Added 2026-09-02 on the user's instruction. The car had three lights
    /// and all three pointed out of it - two beams and a spill, sized to
    /// throw twenty metres of mountain road - so the cabin they burned from
    /// was the one place on the whole journey nothing lit at all. His words
    /// were exact: "при поездке по лесу ты едешь просто в черноте... нужен
    /// тусклый свет внутри салона чтобы можно было разглядеть водителя и
    /// приборную доску", and then "ну и не только, всю приборную панель +
    /// бардачок".
    ///
    /// THE WINDSCREEN IS WHY THE LAMP IS TILTED BACKWARDS, and it is the one
    /// decision everything else here is arranged around. The glass is built
    /// with `add_double_quad`, so there is a real inward-facing pane a
    /// hand's breadth in front of the sitters, and `Glass` has its
    /// ShadowCaster pass disabled - a shadowed lamp would not contain
    /// anything, it would light the bonnet through the hole. A plafond
    /// pointed straight down puts that pane about `58°` off its axis, well
    /// inside any cone wide enough to reach the driver, and lays a milky
    /// veil over the entire two-minute first-person ride. Aiming the axis
    /// back at the seats instead puts the pane at `81°` and the bonnet at
    /// `85°`, both outside the `70°` outer half-angle, both receiving
    /// exactly nothing - while the driver's face sits at `58°` and the dash
    /// between `41°` and `56°`, inside the cone. Widen this cone past about
    /// `150°`, straighten the aim, or move the lamp forward toward the
    /// header, and the veil comes straight back.
    ///
    /// The panel is lit by EMISSION rather than by the lamp, and that is not
    /// a shortcut: the instrument faces stand vertical, facing the sitter,
    /// which is the one orientation no lamp the cabin can hold lights well.
    /// Reaching a readable level with the plafond alone would take about
    /// four times this intensity and blow the driver's face out. Emissive
    /// surfaces also put nothing on the windscreen, which is the only lever
    /// in here that buys legibility for free.
    ///
    /// It builds NO Light until somebody is in the cabin - the hero in the
    /// seat, or the Ferryman behind the wheel. The lit LENS burns always, at
    /// every hour, on every car, which is what §20 asks of a fixture; the
    /// realtime pool is pooled to an occupied cabin exactly as the City
    /// pools its street lights nearest-first while every fixture's emission
    /// burns regardless. That also keeps the parked island car what the
    /// canon calls it - a lamp you look AT - and it is why
    /// `CityIslandCar_StillCarriesNoLightOfItsOwn` still passes verbatim.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(278)]
    public sealed class LastRouteCarCabinLight : MonoBehaviour
    {
        /// <summary>The drawn lens over the front seats, the bulb in the
        /// glovebox, and the two gauge faces. Roles, not names: the asset
        /// build binds them and a renamed mesh fails there.</summary>
        public const string CabinLampLensRole = "cabin_lamp_lens";

        public const string GloveboxBulbRole = "glovebox_bulb";
        public const string InstrumentFacesRole = "instrument_faces";

        /// <summary>
        /// The plafond, derived rather than chosen.
        ///
        /// The emitter stands `0.533 m` from the Ferryman's face at `58.2°`
        /// off the axis, where the spot's own angular falloff between the
        /// `55°` and `70°` half-angles leaves `0.628`; `N·L` on his cheek is
        /// `0.375` and the range fade `0.906`, so `1.90` arrives as about
        /// `0.91` and puts his skin near `114/255` after the mountain
        /// grade. Skin does not clip until roughly `5.9`, so there is six
        /// times the headroom; the brightest thing in the frame is the
        /// hero's own hands in his lap at about `179/255`, which is what a
        /// dome lamp over a lap should do. The usable band is `1.2`-`2.4`
        /// and this is the ONLY dial worth moving if it wants tuning -
        /// never the cone, never the range.
        /// </summary>
        public const float CabinLampIntensity = 1.90f;

        /// <summary>
        /// Range does two jobs at once here, and both are load-bearing.
        ///
        /// CONTAINMENT: the emitter stands `1.505 m` above the plane the
        /// wheels touch, and URP's fade is `saturate(1 - (d²/r²)²)²`, which
        /// is EXACTLY zero at the range - so a pool of cabin light on the
        /// road is arithmetically impossible, with `0.40 m` of margin
        /// against the suspension's own heave.
        ///
        /// DISCRIMINATION: the dash sits `0.57`-`0.60 m` away and keeps
        /// `85`-`88%` of the inverse-square value at this range, while
        /// anything past about `0.95 m` is already down to `11%`. Shortening
        /// it to `0.85` - which looks safer - starves the very surface the
        /// user asked to be able to read, leaving `4`-`27%` at the dash.
        /// </summary>
        public const float CabinLampRange = 1.10f;

        /// <summary>The cone the windscreen has to stay outside of. See the
        /// class summary: this is the constant the whole design rests
        /// on.</summary>
        public const float CabinLampSpotAngle = 140f;

        public const float CabinLampInnerSpotAngle = 110f;

        /// <summary>
        /// How far the aim point stands above the midpoint of the two seat
        /// anchors, and how far forward of it. Together they tilt the axis
        /// about `23°` back off vertical.
        ///
        /// Expressed as a POINT built from the two drawn seat anchors and
        /// the runtime root's own axes, never as a local Euler. The lamp
        /// hangs on the sprung body, whose `localRotation` is copied off an
        /// imported node whose forward is very nearly vertical - aiming in
        /// that space is what threw both headlight beams at the sky and
        /// shipped a black scene.
        /// </summary>
        public const float CabinLampAimRise = 0.53f;

        public const float CabinLampAimReach = 0.15f;

        /// <summary>How far below the drawn lens the emitter sits, so the
        /// light starts at the glass rather than inside the housing - the
        /// headlights' own rule, at cabin scale.</summary>
        public const float CabinLampStandoff = 0.0245f;

        /// <summary>
        /// The glovebox bulb, set by a `9 cm` throw, which is why it is a
        /// hundredth of the plafond. The only thing a seated passenger can
        /// SEE inside the box is the floor strip his sightline reaches past
        /// the aperture's top edge; its centre is `0.093 m` from the bulb,
        /// where `0.055` arrives as about `3.65` and reads as a warm lit
        /// drawer rather than a torch.
        /// </summary>
        public const float GloveboxLampIntensity = 0.055f;

        /// <summary>
        /// And its containment, which it needs: with no shadows the
        /// compartment walls block nothing. The fade is exactly zero at
        /// `0.45 m`; the hero's nearest knee is `0.48 m` away, the driver's
        /// hands `0.90`, the road `0.96`. Every dash face around the
        /// aperture has `N·L < 0` to a bulb recessed behind the face plane,
        /// so nothing bleeds onto the panel either.
        /// </summary>
        public const float GloveboxLampRange = 0.45f;

        public const float GloveboxLampStandoff = 0.029f;

        /// <summary>
        /// How far the lid has to have swung before the bulb is at full.
        ///
        /// It follows the ANIMATED openness, never `GloveboxOpen` - the
        /// boolean flips a frame before the leaf moves, and a bulb lit
        /// behind a shut dash face is a glowing dashboard. The opening
        /// curve reaches this about `0.03 s` in, which is a plunger switch
        /// coming off its stop, and is exactly zero at zero.
        /// </summary>
        public const float GloveboxLampSwitchOpenness = 0.15f;

        /// <summary>What the gauge faces glow, as a fraction of the level
        /// authored on the shared lamp material. A lit dial should read a
        /// little under the bulb lighting it.</summary>
        public const float PanelEmissionFraction = 0.85f;

        /// <summary>Warm, and inside the car's own family - its halos are
        /// `(1, 0.94, 0.74)` and the bus's plafond `(1, 0.65, 0.34)` - so it
        /// opens no third colour voice at the summit, where the night is
        /// already an argument between cold mercury and the cafe's
        /// sulphur.</summary>
        private static readonly Color CabinLampColor =
            new Color(1.00f, 0.82f, 0.56f);

        /// <summary>What the lit surfaces glow when the material carries no
        /// readable emission - a build without one is a build somebody will
        /// notice, so this is only a floor.</summary>
        private static readonly Color FallbackLampEmission =
            new Color(0.427f, 0.364f, 0.279f, 1f);

        /// <summary>Below this the lamp is off rather than merely faint. The
        /// headlights record the matching trap from the other end: a
        /// `power > 0.004` test switched a legitimately dipped beam off as
        /// rounding error, so the question is asked in INTENSITY.</summary>
        private const float MinimumBurningIntensity = 0.001f;

        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        private Transform root;
        private Transform carrier;
        private LastRouteCarAssetRegistry registry;
        private LastRouteCarDashboard dashboard;
        private LastRouteCarSeatInteraction seat;
        private Renderer lens;
        private Renderer bulb;
        private Renderer panel;
        private Color lensEmission = FallbackLampEmission;
        private Color bulbEmission = FallbackLampEmission;
        private Color panelEmission = FallbackLampEmission;
        private MaterialPropertyBlock properties;
        private Light cabinLamp;
        private Light gloveboxLamp;
        private float fixtureFactor = 1f;
        private int appliedMinute = int.MinValue;
        private bool forcedOccupied;
        private bool forceOccupancy;

        public bool IsInitialized { get; private set; }

        /// <summary>The plafond over the seats. Null until somebody is in
        /// the cabin - that is the design, not a failure.</summary>
        public Light CabinLamp => cabinLamp;

        public Light GloveboxLamp => gloveboxLamp;

        /// <summary>True while the cabin has somebody in it: the hero in the
        /// seat, or the Ferryman behind the wheel. The second half is what
        /// lights the man at the moment the player first meets him, sitting
        /// in a car he has just been invited into.</summary>
        public bool IsOccupied =>
            forceOccupancy
                ? forcedOccupied
                : seat != null &&
                  (seat.IsSeated || seat.IsFirstPerson || seat.IsInvited);

        /// <summary>What the three lit surfaces are actually glowing, so a
        /// test can read them back rather than infer them.</summary>
        public Color ReadLensEmission() => ReadEmission(lens);

        public Color ReadGloveboxEmission() => ReadEmission(bulb);

        public Color ReadPanelEmission() => ReadEmission(panel);

        /// <summary>
        /// Binds the three drawn surfaces and remembers where the lamps will
        /// hang. It builds nothing that burns: the lights are raised the
        /// first frame the cabin is occupied, and a car nobody ever sits in
        /// costs one idle component.
        /// </summary>
        public void Initialize(
            Transform runtimeRoot,
            Transform lampCarrier,
            LastRouteCarAssetRegistry carRegistry,
            LastRouteCarDashboard carDashboard)
        {
            if (IsInitialized || runtimeRoot == null || carRegistry == null)
            {
                return;
            }

            root = runtimeRoot;
            carrier = lampCarrier != null ? lampCarrier : runtimeRoot;
            registry = carRegistry;
            dashboard = carDashboard;
            lens = FindRenderer(CabinLampLensRole);
            bulb = FindRenderer(GloveboxBulbRole);
            panel = FindRenderer(InstrumentFacesRole);

            // Read, never construct. The authored value carries whatever
            // colour space the asset build wrote it in, and scaling it by a
            // scalar is correct under either reading; inventing a colour
            // here would be a guess that only a readback could settle.
            lensEmission = ResolveEmission(lens);
            bulbEmission = ResolveEmission(bulb);
            panelEmission = ResolveEmission(panel);
            IsInitialized = true;
            RefreshFixtureFactor(true);
            ApplyEmission();
        }

        /// <summary>
        /// Drives occupancy directly, for tests that have no hero to seat
        /// and no Ferryman to invite - and runs the frame that follows,
        /// because an edit-mode test has no frame loop to run it. Unity
        /// refuses `SendMessage("Update")` to a behaviour outside play
        /// mode, so the tick has to be reachable as a method.
        /// </summary>
        internal void ForceOccupiedForTests(bool occupied)
        {
            forceOccupancy = true;
            forcedOccupied = occupied;
            Tick();
        }

        private void Update()
        {
            // The seat is created AFTER this component and only where there
            // is a player to sit in it, so it is resolved lazily and cached.
            // A GetComponent in Initialize is exactly what left the
            // speedometer needle dead for a whole build.
            if (IsInitialized && seat == null && !forceOccupancy)
            {
                seat = root.GetComponentInChildren<LastRouteCarSeatInteraction>(
                    true);
            }

            Tick();
        }

        private void Tick()
        {
            if (!IsInitialized)
            {
                return;
            }

            RefreshFixtureFactor(false);
            if (IsOccupied)
            {
                EnsureLights();
            }

            ApplyLevels();
            ApplyEmission();
        }

        /// <summary>
        /// The hour, in the form a fixture rides: §20's floor rather than
        /// the raw night factor, so the lens is still lit at noon.
        ///
        /// Deliberately NOT `CityNightGlowRegistry` - that static is written
        /// only by the City, so a car that drove up the mountain would
        /// freeze at whatever the City last left, at any hour.
        /// </summary>
        private void RefreshFixtureFactor(bool force)
        {
            int minute = GameSessionState.GameMinuteOfDay;
            if (!force && minute == appliedMinute)
            {
                return;
            }

            appliedMinute = minute;
            fixtureFactor = GameTimeDayNightRules.FixtureFactor(
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes).NightFactor);
        }

        private void EnsureLights()
        {
            if (cabinLamp != null)
            {
                return;
            }

            // Both emitters are derived from the DRAWN lenses rather than
            // from typed coordinates, so the constants in the generator and
            // the ones here can never drift apart - and so a mesh that
            // failed to bind produces no lamp instead of a lamp in the
            // middle of the road.
            if (lens != null)
            {
                Vector3 emitter =
                    lens.bounds.center - (root.up * CabinLampStandoff);
                Vector3 aim = SeatMidpoint() +
                              (root.up * CabinLampAimRise) +
                              (root.forward * CabinLampAimReach);
                cabinLamp = CreateLight(
                    "Cabin Plafond",
                    LightType.Spot,
                    emitter,
                    Quaternion.LookRotation(aim - emitter, root.up));
                cabinLamp.range = CabinLampRange;
                cabinLamp.spotAngle = CabinLampSpotAngle;
                cabinLamp.innerSpotAngle = CabinLampInnerSpotAngle;
            }

            if (bulb != null)
            {
                Vector3 emitter =
                    bulb.bounds.center - (root.up * GloveboxLampStandoff);
                gloveboxLamp = CreateLight(
                    "Glovebox Lamp",
                    LightType.Point,
                    emitter,
                    Quaternion.LookRotation(root.forward, root.up));
                gloveboxLamp.range = GloveboxLampRange;
            }
        }

        /// <summary>
        /// The midpoint of the two drawn seat anchors, which is the middle
        /// of the bench between the two men - the point the plafond looks
        /// at, tilted up and back off it.
        /// </summary>
        private Vector3 SeatMidpoint()
        {
            Transform driver = registry.DriverSeatAnchor;
            Transform passenger = registry.PassengerSeatAnchor;
            if (driver != null && passenger != null)
            {
                return (driver.position + passenger.position) * 0.5f;
            }

            if (driver != null)
            {
                return driver.position;
            }

            return passenger != null ? passenger.position : root.position;
        }

        private void ApplyLevels()
        {
            float occupancy = IsOccupied ? 1f : 0f;
            if (cabinLamp != null)
            {
                float intensity =
                    CabinLampIntensity * fixtureFactor * occupancy;
                cabinLamp.intensity = intensity;
                cabinLamp.enabled = intensity > MinimumBurningIntensity;
            }

            if (gloveboxLamp != null)
            {
                float intensity = GloveboxLampIntensity *
                                  fixtureFactor *
                                  occupancy *
                                  GloveboxOpenness01();
                gloveboxLamp.intensity = intensity;
                gloveboxLamp.enabled = intensity > MinimumBurningIntensity;
            }
        }

        /// <summary>
        /// How far the plunger switch has come off its stop, from the lid's
        /// ANIMATED openness. Zero at zero, so the bulb can never show
        /// through a shut dash face.
        /// </summary>
        private float GloveboxOpenness01()
        {
            if (dashboard == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                dashboard.GloveboxOpenness / GloveboxLampSwitchOpenness);
        }

        private void ApplyEmission()
        {
            // The lens and the dials burn at every hour on every car, which
            // is what §20 asks of a fixture and what makes a parked car read
            // as a car somebody is waiting in. Only the realtime pool is
            // pooled to an occupied cabin.
            Write(lens, lensEmission * fixtureFactor);
            Write(
                panel,
                panelEmission * (PanelEmissionFraction * fixtureFactor));
            Write(
                bulb,
                bulbEmission *
                (fixtureFactor *
                 (IsOccupied ? 1f : 0f) *
                 GloveboxOpenness01()));
        }

        private void Write(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }

            if (properties == null)
            {
                properties = new MaterialPropertyBlock();
            }

            target.GetPropertyBlock(properties);
            properties.SetColor(EmissionColorId, color);
            target.SetPropertyBlock(properties);
        }

        private Color ReadEmission(Renderer target)
        {
            if (target == null)
            {
                return Color.black;
            }

            if (properties == null)
            {
                properties = new MaterialPropertyBlock();
            }

            target.GetPropertyBlock(properties);
            return properties.GetColor(EmissionColorId);
        }

        /// <summary>
        /// The authored level off the shared material, guarded exactly as
        /// the radio dial's is: a material carrying no emission would
        /// otherwise hand back black and the surface could never light.
        /// </summary>
        private static Color ResolveEmission(Renderer target)
        {
            if (target == null)
            {
                return FallbackLampEmission;
            }

            Material material = target.sharedMaterial;
            if (material != null && material.HasProperty(EmissionColorId))
            {
                Color authored = material.GetColor(EmissionColorId);
                if (authored.maxColorComponent > 0.01f)
                {
                    return authored;
                }
            }

            return FallbackLampEmission;
        }

        /// <summary>
        /// The headlights' own construction, line for line: parent FIRST,
        /// then write the WORLD pose, because the carrier's own basis is an
        /// imported node's and cannot be aimed in.
        ///
        /// Shadowless on purpose. `Glass` has its ShadowCaster pass
        /// disabled, so a shadowed cabin lamp would not be contained by the
        /// bodywork at all - it would escape through the windscreen at the
        /// bonnet, cost an atlas slice and buy nothing.
        /// </summary>
        private Light CreateLight(
            string objectName,
            LightType type,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            var host = new GameObject(objectName);
            host.layer = gameObject.layer;
            host.transform.SetParent(carrier, false);
            host.transform.SetPositionAndRotation(worldPosition, worldRotation);
            Light light = host.AddComponent<Light>();
            light.type = type;
            light.color = CabinLampColor;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            light.enabled = false;
            return light;
        }

        private Renderer FindRenderer(string role)
        {
            for (int index = 0; index < registry.Bindings.Count; index++)
            {
                LastRouteCarRendererBinding binding = registry.Bindings[index];
                if (binding.Role == role && binding.Renderer != null)
                {
                    return binding.Renderer;
                }
            }

            return null;
        }
    }
}
