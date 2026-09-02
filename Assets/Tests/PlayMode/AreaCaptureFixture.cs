using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Photographs the game.
    ///
    /// This exists because numbers cannot see. In one session three
    /// defects passed `1710` green tests and were caught only by looking
    /// at a rendered frame: a jukebox standing in the middle of the room,
    /// pendant lamps six millimetres across, and an entire room at a
    /// hundredth of its size. Every one of them kept correct anchors,
    /// correct collision and a correct manifest, because none of those
    /// numbers come from the meshes.
    ///
    /// Frames are taken in PLAY MODE through the scene's own main camera,
    /// so what is photographed is the real thing: the real lighting, the
    /// real post-processing stack, and a world built by the scene's own
    /// root rather than by a mock-up of it.
    ///
    /// Every capture is `[Explicit]`. They are not tests - they assert
    /// almost nothing - and they must not join an ordinary PlayMode
    /// sweep: this project has already hit
    /// `Too many instant steps in test execution mode: ExitPlayModeTask`
    /// from running heavy scene-loading fixtures together. Run one area at
    /// a time:
    ///
    /// <code>
    /// Unity.exe -batchmode -projectPath &lt;p&gt; -runTests
    ///   -testPlatform PlayMode
    ///   -testFilter "AreaCaptureFixture.Bar"
    ///   -testResults &lt;xml&gt; -logFile &lt;log&gt;
    /// </code>
    ///
    /// Frames land in `Captures/&lt;area&gt;/`, which is gitignored. Look at
    /// them; that is the whole point.
    /// </summary>
    public sealed class AreaCaptureFixture
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const float TimeoutSeconds = 60f;

        //  Long enough for a root to finish composing its world, for
        //  atmosphere to light it and for a first physics step to settle
        //  anything that drops.
        private const int SettleFrames = 12;

        /// <summary>
        /// The longest a shot may wait for its <c>readyWhen</c> condition:
        /// fifteen seconds of pinned frames, two full primary gust periods,
        /// so a wave that never crests or never troughs fails the run
        /// instead of hanging it.
        /// </summary>
        private const int MaximumReadyFrames = 900;

        /// <summary>A standing eye, as every village shot uses.</summary>
        private const float EyeHeight = 1.72f;

        /// <summary>Aim at the house's upper storey, where the windows are,
        /// rather than at the ground under it.</summary>
        private const float LandmarkAimHeight = 2f;

        /// <summary>
        /// Where the mid-lane sideways shot aims: past the toe (`52-60 m`
        /// from the lane centre) and up toward the crest, so the frame holds
        /// the slope from its foot to its edge.
        /// </summary>
        private const float WallAimDistance = 60f;

        private const float WallAimHeight = 28f;

        /// <summary>
        /// The wave heights the crest and trough shots wait for. The wave's
        /// simulated maxima sit around `0.95` and its running minima near
        /// `0.05`; these are inside both with margin.
        /// </summary>
        private const float GustCrestWave = 0.85f;

        /// <summary>Metres down the lane from its foot to the platform's
        /// uphill apron - on the platform, clear of the canopy.</summary>
        private const float PlatformApronSetback = 2.5f;

        private const float GustTroughWave = 0.10f;

        /// <summary>
        /// One frame. Either at an absolute place in the world, or - far
        /// more useful in a scene whose geometry you have not measured -
        /// at an offset from the hero, who by definition stands somewhere
        /// worth photographing.
        ///
        /// Invented absolute coordinates are how you get a folder of
        /// pictures of the inside of a wall: the first City capture here
        /// produced exactly that.
        /// </summary>
        private readonly struct Shot
        {
            private Shot(
                string name,
                Vector3 position,
                Vector3 target,
                float fieldOfView,
                bool relativeToHero,
                int delayFrames,
                Func<bool> readyWhen)
            {
                Name = name;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
                RelativeToHero = relativeToHero;
                DelayFrames = Mathf.Max(0, delayFrames);
                ReadyWhen = readyWhen;
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Target { get; }
            public float FieldOfView { get; }
            public bool RelativeToHero { get; }
            public int DelayFrames { get; }

            /// <summary>
            /// Optional: after the fixed delay, keep waiting until this is
            /// true. A shot of a breathing haze at "a gust crest" cannot be
            /// timed by a frame count - the rhythm is hashed per seed - so
            /// it waits on the scene's own wave instead.
            /// </summary>
            public Func<bool> ReadyWhen { get; }

            /// <summary>A fixed place, for a room whose layout is known.</summary>
            public static Shot At(
                string name,
                Vector3 position,
                Vector3 target,
                float fieldOfView = 60f,
                int delayFrames = 0,
                Func<bool> readyWhen = null)
            {
                return new Shot(
                    name,
                    position,
                    target,
                    fieldOfView,
                    false,
                    delayFrames,
                    readyWhen);
            }

            /// <summary>
            /// An offset in the hero's own frame: `+Z` is where he faces.
            /// </summary>
            public static Shot NearHero(
                string name,
                Vector3 offset,
                Vector3 lookOffset,
                float fieldOfView = 60f,
                int delayFrames = 0)
            {
                return new Shot(
                    name,
                    offset,
                    lookOffset,
                    fieldOfView,
                    true,
                    delayFrames,
                    null);
            }
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Bar()
        {
            GameSessionState.EnterBar("bar-capture");
            yield return Capture(
                SceneIds.BarInterior,
                Root<BarInteriorRoot>(),
                new[]
                {
                    Shot.NearHero(
                        "00-over-the-shoulder",
                        new Vector3(0f, 1.5f, -3.2f),
                        new Vector3(0f, 1.2f, 9f), 62f),
                    Shot.At(
                        "01-from-the-door",
                        new Vector3(0f, 1.72f, -7.2f),
                        new Vector3(0f, 1.5f, 5.5f), 62f),
                    Shot.At(
                        "02-counter",
                        new Vector3(-1.6f, 1.65f, 1.4f),
                        new Vector3(-0.6f, 1.35f, 6.2f), 55f),
                    Shot.At(
                        "03-booths-and-stage",
                        new Vector3(-2.5f, 1.8f, -2.2f),
                        new Vector3(-9.5f, 1.2f, 2.0f), 62f),
                    Shot.At(
                        "04-activity-bay",
                        new Vector3(1.5f, 1.75f, -4.2f),
                        new Vector3(7.0f, 1.1f, 0.4f), 58f),
                    Shot.At(
                        "05-pendants-and-ceiling",
                        new Vector3(0f, 1.55f, 0.5f),
                        new Vector3(-0.5f, 3.9f, 5.6f), 62f),
                    Shot.At(
                        "06-overview",
                        new Vector3(-9.4f, 4.2f, -7.0f),
                        new Vector3(1.5f, 0.9f, 4.0f), 72f),
                    Shot.At(
                        "07-jukebox-and-entrance",
                        new Vector3(1.0f, 1.6f, -2.6f),
                        new Vector3(6.6f, 1.1f, -6.9f), 58f),
                });
        }

        //  Every area below is photographed from the hero, because his
        //  position is the one place in a scene guaranteed to be worth
        //  looking at and the one this fixture can find without measuring
        //  the world. The bar keeps hand-placed shots as well, because
        //  its room HAS been measured - and even there the first frame is
        //  the one over his shoulder.
        private static Shot[] HeroShots()
        {
            return new[]
            {
                Shot.NearHero(
                    "01-over-the-shoulder",
                    new Vector3(0f, 1.5f, -3.6f),
                    new Vector3(0f, 1.2f, 9f), 62f),
                Shot.NearHero(
                    "02-what-he-faces",
                    new Vector3(0f, 1.65f, 0.4f),
                    new Vector3(0f, 1.5f, 14f), 68f),
                Shot.NearHero(
                    "03-from-above",
                    new Vector3(0f, 11f, -11f),
                    new Vector3(0f, 0f, 6f), 60f),
            };
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator City()
        {
            yield return Capture(
                SceneIds.City, Root<CityGameRoot>(), HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CityArchShelter()
        {
            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CityArchShelterShots(cityRoot));
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CitySpecialBuildings()
        {
            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CitySpecialBuildingShots(cityRoot));
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CityWindowLighting()
        {
            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(360f);

            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CityWindowLightingShots(cityRoot));
        }

        [UnityTest]
        [Explicit("Capture and focused runtime contract, not a suite test.")]
        public IEnumerator CityBuildingSurfaces()
        {
            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(360f);

            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CityBuildingSurfaceShots(cityRoot));
        }

        /// <summary>
        /// The first sealed grave and the raven pair that holds to
        /// it. The ledger is sealed BEFORE the load, so the city
        /// builds the finished grave and the birds spawn already
        /// perched on it. The frames land in Captures/City/ under the
        /// cemetery- prefix — the folder is the SCENE name, and this
        /// is a City series.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CityCemetery()
        {
            GameSessionState.BeginNewGame();
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.CitySeed);
            CityCemeteryPlan cemetery =
                CityCemeteryPlanner.Create(layout);
            Assert.That(cemetery, Is.Not.Null);
            CemeteryWatchmanPlan watchman =
                CemeteryWatchmanPlan.Create(cemetery);
            CemeteryGravediggingPlan grave =
                CemeteryGravediggingPlan.Create(cemetery, watchman);
            Assert.That(grave.IsPresent, Is.True);
            Assert.That(
                GameSessionState.TryAdvanceGraveWork(
                    grave.Plot.StableId,
                    CemeteryGraveWorkStage.Sealed),
                Is.True);

            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CityCemeteryShots(cityRoot));
        }

        /// <summary>
        /// Framing is computed from the same pure plans the raven
        /// controller derives its perches from, NOT from its live
        /// state: shots are resolved before the settle frames, and
        /// the pair arms only on its first Update.
        /// </summary>
        private static Shot[] CityCemeteryShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(
                root.CemeteryRavens,
                Is.Not.Null,
                "A city with a cemetery must raise the raven " +
                "controller.");
            Assert.That(
                CemeteryMournerPlan.TryGetAccess(
                    root.Layout,
                    out CityOpenAreaAccessDescriptor access),
                Is.True);
            //  Despite its name, OutwardNormal points from the street
            //  INTO the grounds.
            Vector3 inward = access.OutwardNormal.normalized;

            CemeteryGravediggingPlan grave = FindSealedGravePlan(root);
            Vector3 crown = CityCemeterySealedGraveWorldBuilder
                .GetMoundCrownPoint(grave);
            var taken = new System.Collections.Generic.List<string>();
            foreach (CemeteryGraveWorkRecord record in
                     GameSessionState.GraveWork)
            {
                taken.Add(record.PlotId);
            }

            CemeteryRavenPerch groundPerch =
                CemeteryRavenPlan.SelectGroundPerch(
                    root.World.CemeteryPlan,
                    grave,
                    taken,
                    null);
            Assert.That(groundPerch.IsPresent, Is.True);
            Vector3 ground = groundPerch.Position;
            Vector3 toGround = ground - crown;
            toGround.y = 0f;
            toGround = toGround.normalized;
            Vector3 side =
                Vector3.Cross(Vector3.up, toGround).normalized;
            Vector3 mid = (crown + ground) * 0.5f;

            bool heroSentIn = false;
            return new[]
            {
                Shot.At(
                    "cemetery-00-gate-view",
                    access.Center - inward * 2.2f +
                    Vector3.up * EyeHeight,
                    access.Center + inward * 15f + Vector3.up * 1.1f,
                    62f),
                Shot.At(
                    "cemetery-01-first-grave-with-ravens",
                    crown + side * 4f + Vector3.up * 1.6f,
                    mid + Vector3.up * 0.2f,
                    52f),
                Shot.At(
                    "cemetery-02-ground-raven",
                    ground + toGround * 3.6f + side * 0.9f +
                    Vector3.up * 1.45f,
                    crown + Vector3.up * 0.15f,
                    50f),

                //  The teleport is a side effect of this shot's OWN
                //  readyWhen — the way the village storm frames wait
                //  on the scene's wave. resolveShots runs once BEFORE
                //  the loop, so a teleport in the factory would flush
                //  the pair before frame 00 was ever taken. The
                //  closure fires the teleport once, then polls the
                //  phase until the flush actually runs, so the frame
                //  catches the wings out over the grave.
                Shot.At(
                    "cemetery-03-approach-flush",
                    crown + side * 5.5f + Vector3.up * 1.7f,
                    crown + Vector3.up * 0.7f,
                    58f,
                    0,
                    () =>
                    {
                        CityCemeteryRavenController ravens =
                            root.CemeteryRavens;
                        if (!heroSentIn && ravens.IsArmed)
                        {
                            root.Player.Motor.Teleport(
                                crown - side * 2.6f +
                                Vector3.up * 0.5f);
                            heroSentIn = true;
                        }

                        return ravens.Phase ==
                               CemeteryRavenPhase.Startled &&
                               ravens.RavenA != null &&
                               ravens.RavenA.HasFlight &&
                               ravens.RavenB.HasFlight;
                    })
            };
        }

        private static CemeteryGravediggingPlan FindSealedGravePlan(
            CityGameRoot root)
        {
            string plotId = GameSessionState.FirstSealedGravePlotId;
            Assert.That(
                plotId,
                Is.Not.Null,
                "CityCemetery seeds the ledger Sealed before the " +
                "load.");
            for (int index = 0;
                 index < root.Gravedigging.Jobs.Count;
                 index++)
            {
                CemeteryGravediggingController job =
                    root.Gravedigging.Jobs[index];
                if (job != null &&
                    job.HasJob &&
                    job.PlotId == plotId)
                {
                    return job.Plan;
                }
            }

            return CemeteryGravediggingPlan.CreateFor(
                root.World.CemeteryPlan,
                plotId);
        }

        /// <summary>
        /// The outdoor roost pairs across the city, one short series
        /// per roost plus the park flush. The ledger needs no seeding
        /// — the pairs are triggerless — but the ACTIVATION radius
        /// keeps a far roost frozen with its renderers off, so each
        /// roost's establishing frame owns a readyWhen closure that
        /// teleports the hidden hero near that roost once and then
        /// polls the controller until the pair is awake and perched.
        /// Without the teleports the series would photograph empty
        /// coping.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CityRavenRoosts()
        {
            IgnoreUnlessRavenArtBuilt();
            GameSessionState.BeginNewGame();

            CityGameRoot cityRoot = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    cityRoot = Object.FindAnyObjectByType<CityGameRoot>();
                    return cityRoot;
                },
                () => CityRavenRoostShots(cityRoot));
        }

        /// <summary>
        /// Framing is computed from the same pure planner run the
        /// root wires — root.Layout/World through the city teleport
        /// ground — NOT from the controller's live state: shots are
        /// resolved before the settle frames, and the planner is
        /// deterministic over the layout, so descriptor i here IS
        /// controller roost i.
        /// </summary>
        private static Shot[] CityRavenRoostShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            RavenRoostController controller = root.CityRavenRoosts;
            Assert.That(
                controller,
                Is.Not.Null,
                "The default city must raise the roost controller.");
            var ground = new CityMapCityTeleportGround(root.Layout);
            System.Collections.Generic.IReadOnlyList<
                RavenRoostDescriptor> descriptors =
                CityRavenRoostPlanner.Create(
                    root.Layout,
                    root.World,
                    ground,
                    GameSessionState.CitySeed);
            Assert.That(
                controller.RoostCount,
                Is.EqualTo(descriptors.Count),
                "An inert controller (missing raven art) cannot be " +
                "photographed.");

            var shots = new System.Collections.Generic.List<Shot>();
            int parkIndex = -1;
            for (int index = 0; index < descriptors.Count; index++)
            {
                RavenRoostDescriptor roost = descriptors[index];
                if (string.Equals(
                        roost.StableId,
                        "city-roost-park-fountain",
                        StringComparison.Ordinal))
                {
                    parkIndex = index;
                }

                string shortName = ShortRoostName(roost.StableId);
                AddRoostPairShot(
                    shots,
                    root.Player,
                    controller,
                    ground,
                    RavenRoostSettings.City,
                    index,
                    roost,
                    $"roost-{shots.Count:00}-{shortName}");

                //  The close frame: down the pair's own axis from
                //  just past the companion bird, so beak and eye can
                //  be judged in grayscale against the perch ground.
                Vector3 a = roost.PerchA.Position;
                Vector3 toB = roost.PerchB.Position - a;
                toB.y = 0f;
                toB = toB.sqrMagnitude > 0.0001f
                    ? toB.normalized
                    : Vector3.forward;
                Vector3 side =
                    Vector3.Cross(Vector3.up, toB).normalized;
                shots.Add(Shot.At(
                    $"roost-{shots.Count:00}-{shortName}",
                    roost.PerchB.Position + toB * 2.2f +
                    side * 0.9f + Vector3.up * 1.45f,
                    a + Vector3.up * 0.15f,
                    50f));
            }

            if (parkIndex >= 0)
            {
                RavenRoostDescriptor park = descriptors[parkIndex];
                Vector3 a = park.PerchA.Position;
                Vector3 toB = park.PerchB.Position - a;
                toB.y = 0f;
                toB = toB.sqrMagnitude > 0.0001f
                    ? toB.normalized
                    : Vector3.forward;
                Vector3 side =
                    Vector3.Cross(Vector3.up, toB).normalized;
                int index = parkIndex;
                bool heroSentIn = false;
                //  The teleport is a side effect of this shot's OWN
                //  readyWhen — the cemetery flush frame's idiom: fire
                //  once into the flush circle (the reactivation band
                //  wakes a frozen roost on the same poll), then wait
                //  until the flush actually runs, so the frame
                //  catches the wings out over the plaza gravel.
                shots.Add(Shot.At(
                    $"roost-{shots.Count:00}-park-fountain-flush",
                    a + side * 5.5f + Vector3.up * 1.7f,
                    a + Vector3.up * 0.7f,
                    58f,
                    0,
                    () =>
                    {
                        if (!heroSentIn)
                        {
                            root.Player.Motor.Teleport(
                                a - side * 2.6f + Vector3.up * 0.5f);
                            heroSentIn = true;
                        }

                        if (controller.GetRoostPhase(index) !=
                            CemeteryRavenPhase.Startled)
                        {
                            return false;
                        }

                        CemeteryRavenActor[] birds =
                            controller.GetRoostHost(index)
                                .GetComponentsInChildren<
                                    CemeteryRavenActor>(true);
                        return birds.Length == 2 &&
                               birds[0].HasFlight &&
                               birds[1].HasFlight;
                    }));
            }

            Assert.That(shots, Is.Not.Empty);
            return shots.ToArray();
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator MountainRoad()
        {
            // A direct scene load otherwise freezes at 05:59, one minute
            // before the game's opening wake-up. That is a useful night
            // stress case, but a bad single contact sheet for judging six
            // hundred metres of silhouette and material hierarchy.
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(90f);

            MountainRoadRoot mountainRoot = null;
            yield return Capture(
                SceneIds.MountainRoad,
                () =>
                {
                    mountainRoot = Object.FindAnyObjectByType<
                        MountainRoadRoot>();
                    return mountainRoot;
                },
                () => MountainRoadShots(mountainRoot));
        }

        /// <summary>
        /// The summit after dark, which nothing in this repository had ever
        /// photographed.
        ///
        /// Every mountain sheet is shot at `07:30` or `12:40`, so the one
        /// hour at which the pad's practicals are the picture had no frame at
        /// all - which is exactly how a car standing dead centre of the yard
        /// with no Light on it, and a station whose lamps carried no fog
        /// halo, stayed invisible as defects. `20:00` is past `DuskEnd`, so
        /// the night factor is a hard `1` rather than a point on the dusk
        /// ramp, and the clock is seeded BEFORE the load because the root
        /// applies the sample in `Initialize`.
        ///
        /// The three frames are the user's own complaint, in order: the yard
        /// as you meet it, the car in the middle of it, and the way in to the
        /// cableway seen from where the car stands.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator MountainRoadSummitNight()
        {
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(14f * 60f);
            Assert.That(
                GameSessionState.GameHour,
                Is.EqualTo(20),
                "The night sheet must be shot at night.");

            MountainRoadRoot mountainRoot = null;
            yield return Capture(
                SceneIds.MountainRoad,
                () =>
                {
                    mountainRoot = Object.FindAnyObjectByType<
                        MountainRoadRoot>();
                    return mountainRoot;
                },
                () => MountainRoadSummitNightShots(mountainRoot));
        }

        private static Shot[] MountainRoadSummitNightShots(
            MountainRoadRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(
                root.Atmosphere.CurrentSample.NightFactor,
                Is.EqualTo(1f).Within(0.001f),
                "The area is not actually at its night grade.");

            MountainRoadPlateauDescriptor plateau = root.Plan.Plateau;
            MountainRoadVehicleApronPlan apron =
                root.Plan.Terminal.VehicleApron;
            MountainRoadCablewayPlan cableway =
                root.Plan.Terminal.Cableway;

            // Every pose comes off the plan. Invented absolute coordinates
            // are how this fixture produced a folder of pictures of the
            // inside of a wall.
            return new[]
            {
                Shot.At(
                    "n0-yard-from-the-approach",
                    plateau.Center - plateau.Forward * 8f +
                    plateau.Right * 14f + Vector3.up * 3.2f,
                    plateau.Center + plateau.Forward * 6f +
                    Vector3.up * 1.25f,
                    64f),
                Shot.At(
                    "n1-car-on-the-apron",
                    apron.Center - apron.Forward * 11f +
                    Vector3.up * 2.1f,
                    apron.Center + apron.Forward * 4f +
                    Vector3.up * 0.9f,
                    62f),
                // The boarding dock, not the station's middle: the dock is
                // the "вход" the complaint is about, and it is the corner the
                // canopy fixtures were both aimed away from.
                Shot.At(
                    "n2-cableway-entrance-from-the-apron",
                    apron.Center + Vector3.up * EyeHeight,
                    cableway.BoardingDockPosition + Vector3.up * 1.6f,
                    62f),
            };
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator AlpineVillage()
        {
            // Day two at 07:40 keeps the accepted morning light but lands the
            // default seed in a fully developed crosswind slot. The village
            // has a storm floor in every slot; this time is chosen only so the
            // direction reads cleanly across the uphill camera.
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime(100f);

            AlpineVillageRoot villageRoot = null;
            yield return Capture(
                SceneIds.AlpineVillage,
                () =>
                {
                    villageRoot = Object.FindAnyObjectByType<
                        AlpineVillageRoot>();
                    return villageRoot;
                },
                () => AlpineVillageShots(villageRoot));
        }

        /// <summary>
        /// The cableway climb as the passenger sees it: up the line from
        /// the platform, the line from beside the towers, and the
        /// first-person eye along the ride up to and past the cut. This is
        /// the series that showed the old rock planted across the rope for
        /// what it was; now it has to show a rope with no end.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator MountainCableway()
        {
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(90f);

            MountainRoadRoot mountainRoot = null;
            yield return Capture(
                SceneIds.MountainRoad,
                () =>
                {
                    mountainRoot = Object.FindAnyObjectByType<
                        MountainRoadRoot>();
                    return mountainRoot;
                },
                () => MountainCablewayShots(mountainRoot));
        }

        private static Shot[] MountainCablewayShots(MountainRoadRoot root)
        {
            Assert.That(root, Is.Not.Null);
            MountainRoadCablewayPlan cableway = root.Plan.Terminal.Cableway;
            Vector3 forward = cableway.LineForward;
            Vector3 right = cableway.LineRight;
            Vector3 lower = cableway.LowerCableCenter;
            var shots = new System.Collections.Generic.List<Shot>();
            Vector3 platformEye = lower - forward * 4f;
            platformEye.y = cableway.BoardingPlatformTopY + 1.6f;
            shots.Add(Shot.At(
                "c0-platform-up-the-line",
                platformEye,
                MountainCablewayMotion.SampleTrackPosition(cableway, 70f, 1),
                62f));
            Vector3 side = MountainCablewayMotion.SampleTrackPosition(cableway, 30f, 1) -
                           right * 16f + Vector3.down * 5f;
            shots.Add(Shot.At(
                "c1-side-view-of-the-top",
                side,
                MountainCablewayMotion.SampleTrackPosition(cableway, 90f, 1),
                58f));
            Vector3 sideLow = MountainCablewayMotion.SampleTrackPosition(cableway, 44f, 1) +
                              right * 18f + Vector3.down * 8f;
            shots.Add(Shot.At(
                "c2-right-side-of-the-top",
                sideLow,
                MountainCablewayMotion.SampleTrackPosition(cableway, 110f, 1),
                58f));
            float[] eyes = { 20f, 45f, 65f, 73f, 90f, 120f };
            for (int index = 0; index < eyes.Length; index++)
            {
                float d = eyes[index];
                Vector3 attachment = MountainCablewayMotion.SampleTrackPosition(cableway, d, 1);
                Vector3 tangent = MountainCablewayMotion.SampleTrackTangent(cableway, d, 1);
                Vector3 eye = attachment + Vector3.down *
                              (cableway.CabinAttachmentToBottom - MountainRoadCablewayPlan.CabinSkirtHeight - 1.2f);
                Vector3 look = tangent;
                look.y = 0f;
                look = look.normalized;
                shots.Add(Shot.At(
                    $"c{index + 3}-eye-d{d:00.0}",
                    eye,
                    eye + look * 12f + Vector3.up * 1.5f,
                    70f));
            }

            return shots.ToArray();
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Home()
        {
            yield return Capture(
                SceneIds.HomeInterior, Root<HomeInteriorRoot>(), HeroShots());
        }

        /// <summary>
        /// Every planned gameplay composition of the mother's house. The
        /// ground room, real stair/corridor and both empty upper rooms are
        /// read from the same pure camera plan used during play.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. Look at Captures/MothersHouseInterior/.")]
        public IEnumerator MothersHouse()
        {
            MothersHouseInteriorRoot interiorRoot = null;
            yield return Capture(
                SceneIds.MothersHouseInterior,
                () =>
                {
                    interiorRoot = Object.FindAnyObjectByType<
                        MothersHouseInteriorRoot>();
                    return interiorRoot;
                },
                () => MothersHouseShots(interiorRoot));
        }

        private static Shot[] MothersHouseShots(
            MothersHouseInteriorRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.FixedCamera, Is.Not.Null);
            Assert.That(root.FixedCamera.IsInitialized, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(root.Layout.CameraShots, Has.Count.EqualTo(4));
            return new[]
            {
                MothersHouseShot(
                    root,
                    HomeCameraShotKind.MainRoom,
                    "00-ground-floor"),
                MothersHouseShot(
                    root,
                    HomeCameraShotKind.StairAndUpperCorridor,
                    "01-stair-and-upper-corridor"),
                MothersHouseShot(
                    root,
                    HomeCameraShotKind.UpperSouthRoom,
                    "02-empty-south-room"),
                MothersHouseShot(
                    root,
                    HomeCameraShotKind.UpperNorthRoom,
                    "03-empty-north-room")
            };
        }

        private static Shot MothersHouseShot(
            MothersHouseInteriorRoot root,
            HomeCameraShotKind kind,
            string name)
        {
            Assert.That(
                root.Layout.TryGetCameraShot(kind, out HomeCameraShot shot),
                Is.True,
                $"The mother's house is missing camera shot '{kind}'.");
            return Shot.At(
                name,
                shot.Position,
                shot.Position + shot.Rotation * Vector3.forward * 10f,
                shot.FieldOfView);
        }

        /// <summary>
        /// The mother, close enough to judge.
        ///
        /// The room's own composition puts her seven and a half metres away
        /// and directly in front of the hearth, where she reads as a
        /// silhouette - fine for the room, useless for deciding whether her
        /// hips are on the cushion and her back is against the rest. These
        /// shots are a diagnostic and NOT a composition: the gameplay camera
        /// is frozen by the accepted exception and nothing here touches it.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. The mother-* frames in Captures/MothersHouseInterior/.")]
        public IEnumerator MothersHouseMother()
        {
            MothersHouseInteriorRoot interiorRoot = null;
            yield return Capture(
                SceneIds.MothersHouseInterior,
                () =>
                {
                    interiorRoot = Object.FindAnyObjectByType<
                        MothersHouseInteriorRoot>();
                    return interiorRoot;
                },
                () => MothersHouseMotherShots(interiorRoot));
        }

        private static Shot[] MothersHouseMotherShots(
            MothersHouseInteriorRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(
                root.Mother,
                Is.Not.Null,
                "There is nobody in the chair to photograph.");

            // She is animated by a manually driven graph on a culled
            // Animator. Batch mode renders through a RenderTexture, and a
            // culled rig reads back in BIND pose - a standing A-pose in a
            // rocking chair, which would look like a modelling failure rather
            // than a capture setting.
            root.Mother.Registry.Animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;

            Transform room = root.World.Root;
            Vector3 seat = room.TransformPoint(
                new Vector3(
                    MothersHouseMotherPlan.SeatX,
                    1.05f,
                    MothersHouseMotherPlan.SeatZ));
            Vector3 face = room.TransformPoint(
                new Vector3(
                    MothersHouseMotherPlan.SeatX,
                    1.33f,
                    MothersHouseMotherPlan.SeatZ - 0.02f));
            return new[]
            {
                Shot.At(
                    "mother-00-three-quarter",
                    room.TransformPoint(new Vector3(1.15f, 1.45f, 0.10f)),
                    seat,
                    45f,
                    delayFrames: 8),
                // From the LAMP's side. The floor lamp stands at x = -1.72
                // and the gameplay camera at +5.8, so every angle the room
                // itself offers looks at the half of her the lamp does not
                // reach. This one is the control: if she is dark here too,
                // the fault is her palette and not the light.
                Shot.At(
                    "mother-01-profile-lamp-side",
                    room.TransformPoint(new Vector3(-1.60f, 1.10f, 1.55f)),
                    seat,
                    45f,
                    delayFrames: 8),
                // What the player actually gets, walked in to read it. The
                // real shot stands 7.5 m back and she is a thumbnail there.
                Shot.At(
                    "mother-05-gameplay-angle",
                    room.TransformPoint(new Vector3(2.55f, 1.75f, -0.75f)),
                    room.TransformPoint(
                        new Vector3(
                            MothersHouseMotherPlan.SeatX,
                            1.05f,
                            MothersHouseMotherPlan.SeatZ)),
                    45f,
                    delayFrames: 8),
                Shot.At(
                    "mother-02-face",
                    room.TransformPoint(new Vector3(0.16f, 1.42f, 0.72f)),
                    face,
                    30f,
                    delayFrames: 8),
                // From the hearth, looking back into the room. Every other
                // angle has the fire behind her, and a figure between a
                // camera and a fire is a silhouette whatever it is made of.
                // This one is lit rather than backlit, and it is the shot
                // that says whether she is dark or merely in shadow.
                Shot.At(
                    "mother-04-lit-from-hearth",
                    room.TransformPoint(new Vector3(-0.60f, 1.30f, 2.30f)),
                    room.TransformPoint(
                        new Vector3(
                            MothersHouseMotherPlan.SeatX,
                            1.10f,
                            MothersHouseMotherPlan.SeatZ)),
                    45f,
                    delayFrames: 8),
                Shot.At(
                    "mother-03-seat-and-runners",
                    room.TransformPoint(new Vector3(1.30f, 0.62f, 0.55f)),
                    room.TransformPoint(
                        new Vector3(
                            MothersHouseMotherPlan.SeatX,
                            0.50f,
                            MothersHouseMotherPlan.SeatZ)),
                    45f,
                    delayFrames: 8)
            };
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Stairwell()
        {
            yield return Capture(
                SceneIds.StairwellInterior,
                Root<StairwellInteriorRoot>(),
                HeroShots());
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Supermarket()
        {
            SupermarketInteriorRoot supermarketRoot = null;
            yield return Capture(
                SceneIds.SupermarketInterior,
                () =>
                {
                    supermarketRoot = Object.FindAnyObjectByType<
                        SupermarketInteriorRoot>();
                    return supermarketRoot;
                },
                () => SupermarketShots(supermarketRoot));
        }

        private static Shot[] SupermarketShots(
            SupermarketInteriorRoot supermarketRoot)
        {
            Assert.That(supermarketRoot, Is.Not.Null);
            Assert.That(supermarketRoot.World, Is.Not.Null);
            Assert.That(supermarketRoot.World.Shelves, Has.Count.EqualTo(3));
            IReadOnlyList<SupermarketShelfView> shelves =
                supermarketRoot.World.Shelves;
            return new[]
            {
                Shot.NearHero(
                    "01-entrance-overview",
                    new Vector3(0f, 1.5f, -3.6f),
                    new Vector3(0f, 1.2f, 9f),
                    62f),
                ShelfShot("02-dry-products", shelves[0]),
                ShelfShot("03-pantry-products", shelves[1]),
                ShelfShot("04-cold-product", shelves[2]),
            };
        }

        private static Shot ShelfShot(
            string name,
            SupermarketShelfView shelf)
        {
            Assert.That(shelf, Is.Not.Null);
            Assert.That(shelf.Products, Is.Not.Empty);
            Bounds productBounds = default;
            bool hasBounds = false;
            for (int index = 0; index < shelf.Products.Count; index++)
            {
                SupermarketProductView product = shelf.Products[index];
                Assert.That(
                    product.TryGetWorldBounds(out Bounds currentBounds),
                    Is.True,
                    $"{shelf.Id} product {index} has no renderer bounds.");
                Assert.That(
                    Mathf.Abs(
                        currentBounds.min.y - product.transform.position.y),
                    Is.LessThanOrEqualTo(0.012f),
                    $"{product.ItemId} is not grounded on its shelf tier.");
                if (product.ItemId == InventoryItemId.VodkaBottle)
                {
                    float topTierSurface = product.transform.parent
                        .TransformPoint(new Vector3(
                            0f,
                            SupermarketInteriorLayoutPlanner
                                .GondolaThirdTierTop,
                            0f))
                        .y;
                    Assert.That(
                        product.transform.position.y,
                        Is.EqualTo(topTierSurface).Within(0.012f),
                        "The vodka bottle is not on the unobstructed top " +
                        "shelf tier.");
                    float selectedTop = product.transform.position.y +
                        currentBounds.size.y * 1.08f;
                    float shelfTop = product.transform.parent
                        .TransformPoint(new Vector3(
                            0f,
                            shelf.Plan.Height,
                            0f))
                        .y;
                    Assert.That(
                        selectedTop,
                        Is.LessThan(shelfTop),
                        "Selected vodka bottle rises above the shelving " +
                        "unit.");
                }

                if (!hasBounds)
                {
                    productBounds = currentBounds;
                    hasBounds = true;
                }
                else
                {
                    productBounds.Encapsulate(currentBounds);
                }
            }

            Vector3 cameraPosition = shelf.CameraPosition;
            float fieldOfView = shelf.CameraFieldOfView;
            if (shelf.Kind == SupermarketShelfKind.ColdShelf)
            {
                Vector3 facing = shelf.transform.TransformDirection(
                    shelf.Plan.FacingDirection).normalized;
                cameraPosition = productBounds.center + facing * 1.8f +
                    Vector3.up * 0.45f;
                fieldOfView = 48f;
            }
            else if (shelf.Kind ==
                     SupermarketShelfKind.PantryAndSpirits)
            {
                fieldOfView = 62f;
            }

            Debug.Log(
                $"Supermarket capture '{name}': camera={cameraPosition}, " +
                $"target={productBounds.center}, bounds={productBounds}.");
            return Shot.At(
                name,
                cameraPosition,
                productBounds.center,
                fieldOfView);
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Church()
        {
            yield return Capture(
                SceneIds.ChurchInterior,
                Root<ChurchInteriorRoot>(),
                HeroShots());
        }

        // ------------------------------------------------------------

        private static Func<Component> Root<T>() where T : Component
        {
            return () => Object.FindAnyObjectByType<T>();
        }

        private static Shot[] CitySpecialBuildingShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Layout, Is.Not.Null);

            BuildingLot bar = null;
            BuildingLot supermarket = null;
            BuildingLot playerHome = null;
            foreach (BuildingLot lot in root.Layout.BuildingLots)
            {
                if (lot.IsBar)
                {
                    bar = lot;
                }
                else if (lot.IsSupermarket)
                {
                    supermarket = lot;
                }
                else if (lot.IsPlayerHome)
                {
                    playerHome = lot;
                }
            }

            Assert.That(bar, Is.Not.Null);
            Assert.That(supermarket, Is.Not.Null);
            Assert.That(playerHome, Is.Not.Null);
            return new[]
            {
                FrameSpecialBuilding("00-bar", bar),
                FrameSpecialEntrance("00-bar-entrance", bar, 2.35f),
                FrameSpecialEntrance(
                    "00-bar-entrance-opposite",
                    bar,
                    -2.35f),
                FrameSpecialFrontageEdge(
                    "00-bar-edge-left",
                    bar,
                    -1f),
                FrameSpecialFrontageEdge(
                    "00-bar-edge-right",
                    bar,
                    1f),
                FrameSpecialFoundation("00-bar-foundation", bar),
                FrameSpecialBuilding("01-supermarket", supermarket),
                FrameSpecialEntrance(
                    "01-supermarket-entrance-left",
                    supermarket,
                    -2.75f),
                FrameSpecialEntrance(
                    "01-supermarket-entrance-right",
                    supermarket,
                    2.75f),
                FrameSpecialFrontageEdge(
                    "01-supermarket-edge-left",
                    supermarket,
                    -1f),
                FrameSpecialFrontageEdge(
                    "01-supermarket-edge-right",
                    supermarket,
                    1f),
                FrameSpecialFoundation(
                    "01-supermarket-foundation",
                    supermarket),
                FrameSpecialRear(
                    "01-supermarket-rear",
                    supermarket),
                FrameSpecialBuilding("02-player-home", playerHome)
            };
        }

        private static Shot[] CityWindowLightingShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Layout, Is.Not.Null);
            Assert.That(root.DayNight, Is.Not.Null);
            Assert.That(
                root.DayNight.CurrentSample.NightFactor,
                Is.EqualTo(0f),
                "The window contact sheet must hold the noon fixture " +
                "floor, not a dawn or dusk blend.");
            Assert.That(
                Shader.GetGlobalFloat(
                    CityWindowAppearance.FixtureFactorShaderProperty),
                Is.EqualTo(GameTimeDayNightRules.DayFixtureFloor)
                    .Within(0.0001f));

            BuildingLot bar = null;
            foreach (BuildingLot lot in root.Layout.BuildingLots)
            {
                if (lot.IsBar)
                {
                    bar = lot;
                    break;
                }
            }

            Assert.That(bar, Is.Not.Null);
            var districts = new[]
            {
                CityDistrictKind.OldTown,
                CityDistrictKind.Residential,
                CityDistrictKind.Industrial,
                CityDistrictKind.Nightlife
            };
            var shots = new Shot[districts.Length + 1];
            shots[0] = FrameWindowFacade("window-noon-bar", bar);
            for (int index = 0; index < districts.Length; index++)
            {
                CityDistrictKind district = districts[index];
                BuildingLot lot = FindOrdinaryFrontage(
                    root.Layout,
                    district);
                AssertEveryWindowRowIsDistributed(
                    root.Layout,
                    lot,
                    district);
                shots[index + 1] = FrameWindowFacade(
                    "window-noon-" +
                    district.ToString().ToLowerInvariant(),
                    lot);
            }

            return shots;
        }

        private static Shot[] CityBuildingSurfaceShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Layout, Is.Not.Null);
            var districts = new[]
            {
                CityDistrictKind.OldTown,
                CityDistrictKind.Residential,
                CityDistrictKind.Industrial,
                CityDistrictKind.Nightlife
            };
            var shots = new Shot[districts.Length * 3];
            for (int index = 0; index < districts.Length; index++)
            {
                CityDistrictKind district = districts[index];
                BuildingLot lot = FindOrdinaryFrontage(
                    root.Layout,
                    district);
                CityBuildingAssetRegistry registry =
                    FindBuiltPrototype(lot);
                AssertBuiltSurfaceContract(registry);

                string prefix = "building-surfaces-" +
                    district.ToString().ToLowerInvariant();
                int shotIndex = index * 3;
                shots[shotIndex] = FrameBuildingSurface(
                    prefix + "-oblique",
                    lot,
                    registry,
                    false,
                    0f);
                shots[shotIndex + 1] = FrameBuildingSurface(
                    prefix + "-base-a",
                    lot,
                    registry,
                    true,
                    0f);
                shots[shotIndex + 2] = FrameBuildingSurface(
                    prefix + "-base-b",
                    lot,
                    registry,
                    true,
                    0.06f);
            }

            return shots;
        }

        private static CityBuildingAssetRegistry FindBuiltPrototype(
            BuildingLot lot)
        {
            Vector3 expectedFront = lot.DoorPosition +
                Vector3.up * CityFacadeGrid.MassBaseElevation;
            CityBuildingAssetRegistry[] registries =
                Object.FindObjectsByType<CityBuildingAssetRegistry>();
            for (int index = 0; index < registries.Length; index++)
            {
                CityBuildingAssetRegistry candidate = registries[index];
                if (candidate.District == lot.District &&
                    Vector3.Distance(
                        candidate.FrontAnchor.position,
                        expectedFront) <= 0.02f)
                {
                    return candidate;
                }
            }

            Assert.Fail(
                $"No built {lot.District} prototype resolves lot " +
                $"{lot.Cell}.");
            return null;
        }

        private static void AssertBuiltSurfaceContract(
            CityBuildingAssetRegistry registry)
        {
            Assert.That(registry, Is.Not.Null);
            int baseMapId = Shader.PropertyToID("_BaseMap");
            int opaqueCount = 0;
            Bounds prototypeBounds = default;
            bool hasBounds = false;
            for (int index = 0; index < registry.Parts.Count; index++)
            {
                CityBuildingPartBinding binding = registry.Parts[index];
                Renderer renderer = binding.Renderer;
                Assert.That(renderer, Is.Not.Null);
                if (!hasBounds)
                {
                    prototypeBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    prototypeBounds.Encapsulate(renderer.bounds);
                }

                if (binding.Role == CityBuildingMeshRole.WindowGlass)
                {
                    Assert.That(
                        renderer.sharedMaterial.shader.name,
                        Is.EqualTo(
                            "Bar Promenade/City Building Window Slots"));
                    continue;
                }

                Assert.That(
                    CityBuildingSurfaceAppearance.TryResolveSurface(
                        registry.District,
                        binding.SurfaceKind,
                        out CityBuildingSurfaceKind surface),
                    Is.True);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    properties.GetTexture(baseMapId),
                    Is.SameAs(
                        CityBuildingSurfaceAppearance.GetTexture(
                            registry.District,
                            surface)));
                Assert.That(
                    properties.GetTexture(baseMapId),
                    Is.Not.SameAs(Texture2D.whiteTexture));
                opaqueCount++;
            }

            Assert.That(opaqueCount, Is.EqualTo(6));
            Transform foundation = registry.transform.parent.Find(
                CityBuildingPrototypeWorldBuilder.FoundationObjectName);
            Assert.That(foundation, Is.Not.Null);
            Renderer foundationRenderer =
                foundation.GetComponent<Renderer>();
            Assert.That(foundationRenderer, Is.Not.Null);
            var foundationProperties = new MaterialPropertyBlock();
            foundationRenderer.GetPropertyBlock(foundationProperties);
            Assert.That(
                foundationProperties.GetTexture(baseMapId),
                Is.SameAs(
                    CityBuildingSurfaceAppearance.GetTexture(
                        registry.District,
                        CityBuildingSurfaceKind.Plinth)));
            Bounds foundationBounds = foundationRenderer.bounds;
            Assert.That(
                prototypeBounds.size.x - foundationBounds.size.x,
                Is.EqualTo(
                    CityBuildingPrototypeWorldBuilder
                        .FoundationHorizontalInset * 2f).Within(0.01f));
            Assert.That(
                prototypeBounds.size.z - foundationBounds.size.z,
                Is.EqualTo(
                    CityBuildingPrototypeWorldBuilder
                        .FoundationHorizontalInset * 2f).Within(0.01f));
            Assert.That(
                foundationBounds.max.y - prototypeBounds.min.y,
                Is.EqualTo(0.04f).Within(0.005f));
        }

        private static BuildingLot FindOrdinaryFrontage(
            CityLayout layout,
            CityDistrictKind district)
        {
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (lot.IsOrdinaryBuilding &&
                    lot.HasRoadFrontage &&
                    lot.District == district)
                {
                    return lot;
                }
            }

            Assert.Fail(
                $"The default {district} district has no ordinary " +
                "building with a road frontage.");
            return null;
        }

        private static void AssertEveryWindowRowIsDistributed(
            CityLayout layout,
            BuildingLot lot,
            CityDistrictKind district)
        {
            CityBuildingAssetRegistry registry =
                CityBuildingAssetProvider.LoadOrThrow()
                    .GetPrefabOrThrow(district)
                    .GetComponent<CityBuildingAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            for (int index = 0;
                 index < registry.WindowSlots.Count;
                 index++)
            {
                CityBuildingWindowSlot slot = registry.WindowSlots[index];
                if (slot.Bay != 0)
                {
                    continue;
                }

                int side;
                switch (slot.Side)
                {
                    case "Front":
                        side = 0;
                        break;
                    case "Rear":
                        side = 1;
                        break;
                    case "Left":
                        side = 2;
                        break;
                    case "Right":
                        side = 3;
                        break;
                    default:
                        Assert.Fail($"Unknown facade side '{slot.Side}'.");
                        return;
                }

                int paneCount = 0;
                for (int candidateIndex = 0;
                     candidateIndex < registry.WindowSlots.Count;
                     candidateIndex++)
                {
                    CityBuildingWindowSlot candidate =
                        registry.WindowSlots[candidateIndex];
                    if (candidate.Floor == slot.Floor &&
                        string.Equals(
                            candidate.Side,
                            slot.Side,
                            System.StringComparison.Ordinal))
                    {
                        paneCount++;
                    }
                }

                int lit = 0;
                for (int pane = 0; pane < paneCount; pane++)
                {
                    CityWindowFamily family =
                        CityExteriorAppearance.ResolveWindowFamily(
                            lot,
                            layout.Seed,
                            slot.Floor,
                            pane,
                            paneCount,
                            side,
                            out _);
                    if (family == CityWindowFamily.Off)
                    {
                        continue;
                    }

                    Assert.That(
                        family,
                        Is.EqualTo(CityWindowFamily.Warm),
                        $"{district} floor {slot.Floor} has a " +
                        "non-lantern window colour.");
                    lit++;
                }

                Assert.That(
                    lit,
                    Is.GreaterThanOrEqualTo(1),
                    $"{district} floor {slot.Floor} side {slot.Side} " +
                    "is entirely dark.");
                if (paneCount > 1)
                {
                    Assert.That(
                        lit,
                        Is.LessThan(paneCount),
                        $"{district} floor {slot.Floor} side " +
                        $"{slot.Side} lights every pane.");
                }
            }
        }

        private static Shot[] CityArchShelterShots(CityGameRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.World, Is.Not.Null);
            CityArchShelterPlan plan = root.World.ArchShelterPlan;
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.IsEnabled, Is.True);

            CityArchShelterPropDescriptor barrel = default;
            bool foundBarrel = false;
            for (int index = 0; index < plan.Props.Count; index++)
            {
                if (plan.Props[index].Kind !=
                    CityArchShelterPropKind.BurnBarrel)
                {
                    continue;
                }

                barrel = plan.Props[index];
                foundBarrel = true;
                break;
            }

            Assert.That(foundBarrel, Is.True);
            Rect passage = plan.Placement.PassageFootprint;
            Rect facade = plan.Placement.CommonFacadeFootprint;
            Rect platform = plan.Platform.Footprint;
            Bounds structure = plan.Placement.StructureBounds;
            Vector3 target = barrel.Position + Vector3.up * 0.92f;
            float eyeY = barrel.Position.y + 1.68f;
            return new[]
            {
                Shot.At(
                    "00-south-approach",
                    new Vector3(
                        barrel.Position.x,
                        eyeY,
                        passage.yMin - 2.4f),
                    target,
                    58f),
                Shot.At(
                    "01-north-steps",
                    new Vector3(
                        barrel.Position.x - 0.8f,
                        eyeY + 0.35f,
                        passage.yMax + 2.4f),
                    target,
                    60f),
                Shot.At(
                    "02-tableau-close",
                    target + new Vector3(-3.8f, 1.05f, -3.6f),
                    target,
                    52f),
                Shot.At(
                    "03-arch-wide",
                    new Vector3(
                        facade.center.x,
                        structure.min.y + 8.4f,
                        facade.yMin - 14f),
                    new Vector3(
                        facade.center.x,
                        structure.min.y + 2.8f,
                        facade.center.y),
                    58f),
                Shot.At(
                    "04-wall-attachment",
                    new Vector3(
                        platform.xMin - 3.2f,
                        plan.Platform.SurfaceY + 3.2f,
                        platform.yMin - 4f),
                    new Vector3(
                        platform.xMax,
                        plan.Platform.SurfaceY + 0.45f,
                        platform.yMin + 1.25f),
                    46f)
            };
        }

        /// <summary>
        /// The generic hero shots are deliberately useless here: the hero
        /// spawns inside the tunnel, so one camera lands behind its black end
        /// cap and another looks through the roof. These frames instead read
        /// positions from the shipped plan and follow the passenger's whole
        /// sequence: compression, turns, bridge exposure, snow, arrival and
        /// the terminal's single measured opening.
        /// </summary>
        private static Shot[] MountainRoadShots(MountainRoadRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Plan, Is.Not.Null);
            MountainRoadPlan plan = root.Plan;
            MountainRoadRoutePlan route = plan.Route;
            MountainRoadBridgeDescriptor bridge = plan.Bridge;
            MountainRoadPlateauDescriptor plateau = plan.Plateau;
            MountainRoadBrinkDescriptor brink = plateau.Brink;
            Shot[] baseShots =
            {
                FrameMountainRoad(
                    "00-tunnel-threshold",
                    route,
                    3f,
                    20f,
                    3.4f,
                    0f,
                    1.58f,
                    60f),
                FrameMountainRoad(
                    "01-first-hairpin",
                    route,
                    route.Hairpins[0].StartDistance - 8f,
                    (route.Hairpins[0].StartDistance +
                     route.Hairpins[0].EndDistance) * 0.5f,
                    2.4f,
                    0.35f,
                    1.55f,
                    62f),
                FrameMountainRoad(
                    "02-lower-road-reveal",
                    route,
                    route.Hairpins[1].StartDistance - 7f,
                    (route.Hairpins[1].StartDistance +
                     route.Hairpins[1].EndDistance) * 0.5f,
                    2.4f,
                    -0.45f,
                    1.55f,
                    64f),
                FrameMountainRoad(
                    "03-bridge-approach",
                    route,
                    bridge.StartDistance - 14f,
                    bridge.EndDistance - 5f,
                    2.2f,
                    -0.35f,
                    1.48f,
                    62f),
                FrameMountainRoad(
                    "04-bridge-crossing",
                    route,
                    (bridge.StartDistance + bridge.EndDistance) * 0.5f,
                    bridge.EndDistance + 8f,
                    2.8f,
                    0.25f,
                    1.45f,
                    64f),
                FrameMountainRoad(
                    "05-snow-hairpin",
                    route,
                    route.Hairpins[7].StartDistance - 10f,
                    (route.Hairpins[7].StartDistance +
                     route.Hairpins[7].EndDistance) * 0.5f,
                    2.5f,
                    0.35f,
                    1.55f,
                    64f),
                FrameMountainRoad(
                    "06-last-hairpin",
                    route,
                    route.Hairpins[9].StartDistance - 12f,
                    (route.Hairpins[9].StartDistance +
                     route.Hairpins[9].EndDistance) * 0.5f,
                    2.5f,
                    -0.35f,
                    1.55f,
                    62f),
                Shot.At(
                    "07-terminal-approach",
                    route.Sample(plateau.EntryDistance - 18f).Position +
                    Vector3.up * 1.55f,
                    plateau.Center + plateau.Forward * 5f +
                    Vector3.up * 1.25f,
                    62f),
                Shot.At(
                    "08-terminal-yard",
                    plateau.Center - plateau.Forward * 8f +
                    plateau.Right * 14f + Vector3.up * 3.2f,
                    plateau.Center + plateau.Forward * 6f +
                    Vector3.up * 1.25f,
                    64f),
                Shot.At(
                    "09-single-brink-opening",
                    brink.Corridor.Apex - brink.Corridor.Axis * 7f +
                    plateau.Right * 4f + Vector3.up * 1.72f,
                    brink.Corridor.Apex + brink.Corridor.Axis * 32f +
                    Vector3.down * 1.4f,
                    58f)
            };
            var shots =
                new System.Collections.Generic.List<Shot>(baseShots);
            AppendMountainRoadRoostShots(root, shots);
            return shots.ToArray();
        }

        /// <summary>
        /// The culvert, from the road and from below it.
        ///
        /// This is where the mountain's water crosses under the road on its
        /// way to the city, and the frame that has to show the bore actually
        /// pouring - the anchor beside it has been making a running-water
        /// sound over a dark empty cylinder since the road was built.
        /// </summary>
        /// <summary>
        /// The culvert crossing, in DAYLIGHT and on its own.
        ///
        /// The road's own sheet is shot ninety minutes after the wake-up and
        /// its subject is six hundred metres of silhouette; at that hour the
        /// bore is a black hole in a black wall and the first attempt at
        /// these frames came back unreadable. Water is a material question,
        /// so it gets light.
        /// </summary>
        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator MountainRoadCulvert()
        {
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(400f);

            MountainRoadRoot mountainRoot = null;
            yield return Capture(
                SceneIds.MountainRoad,
                () =>
                {
                    mountainRoot = Object.FindAnyObjectByType<
                        MountainRoadRoot>();
                    return mountainRoot;
                },
                () =>
                {
                    var shots = new System.Collections.Generic.List<Shot>();
                    AppendMountainRoadCulvertShots(mountainRoot.Plan, shots);
                    return shots.ToArray();
                });
        }

        private static void AppendMountainRoadCulvertShots(
            MountainRoadPlan plan,
            System.Collections.Generic.List<Shot> shots)
        {
            MountainRoadBrookPlan brook =
                MountainRoadBrookPlanner.Create(plan);
            Vector3 crossing = brook.OutletMouth - brook.InletMouth;
            crossing.y = 0f;
            Vector3 along = crossing.sqrMagnitude <= 0.0001f
                ? Vector3.forward
                : crossing.normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

            shots.Add(Shot.At(
                "30-culvert-bore-pouring",
                brook.Bore + along * 4.2f + across * 2.6f +
                Vector3.up * 1.5f,
                brook.Bore + Vector3.down * 0.25f,
                50f,
                24));

            shots.Add(Shot.At(
                "31-culvert-inlet-from-uphill",
                brook.InletMouth - along * 8f + Vector3.up * 2.6f,
                brook.InletMouth + Vector3.down * 0.2f,
                55f,
                24));

            // Standing BACK ALONG THE WATER, not across it: the first
            // version of this frame stepped nine metres sideways and put the
            // camera inside the hillside, which is the fixture's own oldest
            // warning about invented coordinates.
            Vector3 tail = brook.Outlet.Count > 0
                ? brook.Outlet[brook.Outlet.Count - 1].Position
                : brook.OutletMouth;
            Vector3 downstream = tail - brook.Bore;
            downstream.y = 0f;
            Vector3 back = downstream.sqrMagnitude <= 0.0001f
                ? along
                : downstream.normalized;
            shots.Add(Shot.At(
                "32-water-leaving-toward-the-city",
                brook.Bore - back * 7f + across * 5f + Vector3.up * 5.5f,
                tail,
                60f,
                24));
        }

        /// <summary>
        /// Frames the one uphill composition, one representative of each
        /// ordinary house archetype, and the separate house at the head of
        /// the lane. Every point comes from the shipped plan; no camera
        /// depends on this seed keeping yesterday's world coordinates.
        /// </summary>
        private static Shot[] AlpineVillageShots(AlpineVillageRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Plan, Is.Not.Null);
            Assert.That(
                root.Snow.Kind,
                Is.EqualTo(CityPrecipitationKind.Blizzard));
            Assert.That(root.BlowingSnow, Is.Not.Null);
            Assert.That(root.BlowingSnow.IsInitialized, Is.True);
            Assert.That(root.PeripheralBlizzard, Is.Not.Null);
            Assert.That(root.PeripheralBlizzard.IsInitialized, Is.True);
            Assert.That(root.PeripheralBlizzard.SpatialPlan, Is.Not.Null);
            Assert.That(root.WindSound, Is.Not.Null);
            Assert.That(
                root.Weather.CurrentSample.RainIntensity,
                Is.GreaterThanOrEqualTo(
                    AlpineVillageWeatherRules.SnowFloor));
            Assert.That(
                root.Weather.CurrentWind.Strength01,
                Is.GreaterThanOrEqualTo(
                    AlpineVillageWeatherRules.WindFloor));
            AlpineVillagePlan plan = root.Plan;
            var ordinaryHouses = new AlpineVillagePlotDescriptor[
                VillageAssetProvider.HouseVariantCount];
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind != AlpineVillagePlotKind.House)
                {
                    continue;
                }

                int variant = VillageAssetProvider.SelectVariant(
                    VillageAssetKind.House,
                    plot.StableId);
                if (ordinaryHouses[variant] == null ||
                    plot.LaneDistance <
                    ordinaryHouses[variant].LaneDistance)
                {
                    ordinaryHouses[variant] = plot;
                }
            }

            for (int variant = 0;
                 variant < ordinaryHouses.Length;
                 variant++)
            {
                Assert.That(
                    ordinaryHouses[variant],
                    Is.Not.Null,
                    $"The village has no ordinary house type {variant} " +
                    "to photograph.");
            }

            AlpineVillageLaneSample foot = plan.Lane.Sample(2f);
            AlpineVillageLaneSample reveal = plan.Lane.Sample(
                Mathf.Min(plan.Lane.Length * 0.62f, 52f));
            AlpineVillageLaneSample midLane = plan.Lane.Sample(
                plan.Lane.Length * 0.5f);
            Vector3 houseFacing = plan.MothersHouse.Facing;
            Vector3 houseRight = Vector3.Cross(
                Vector3.up,
                houseFacing).normalized;
            Vector3 rearWall = plan.MothersHouse.GroundCenter -
                               houseFacing *
                               (plan.MothersHouse.FootprintSize.y * 0.5f);
            Vector3 exposedCamera = FindExposedVillageCamera(
                root,
                midLane);
            float houseHalfWidth =
                plan.MothersHouse.FootprintSize.x * 0.5f;
            Shot[] baseShots =
            {
                Shot.At(
                    "00-lower-uphill-axis",
                    foot.Position - foot.Forward * 2.6f +
                    foot.Right * 0.25f + Vector3.up * 1.72f,
                    reveal.Position + Vector3.up * 2.2f,
                    58f,
                    36),
                Shot.At(
                    "00-lower-uphill-axis-gust-b",
                    foot.Position - foot.Forward * 2.6f +
                    foot.Right * 0.25f + Vector3.up * 1.72f,
                    reveal.Position + Vector3.up * 2.2f,
                    58f,
                    30),
                FrameVillageBuilding(
                    "01-heide-house-front-side",
                    ordinaryHouses[0],
                    1f,
                    4.3f,
                    0.34f,
                    50f),
                FrameVillageBuildingSide(
                    "02-heide-house-side-wall",
                    ordinaryHouses[0],
                    -1f),
                FrameVillageBuilding(
                    "03-renaissance-house-front-side",
                    ordinaryHouses[1],
                    -1f,
                    4.7f,
                    0.34f,
                    50f),
                FrameVillageBuildingSide(
                    "04-renaissance-house-side-wall",
                    ordinaryHouses[1],
                    1f),
                FrameVillageBuilding(
                    "05-top-house",
                    plan.MothersHouse,
                    -1f,
                    6.4f,
                    0.30f,
                    54f),

                //  The one composition the canon names: from the platform,
                //  the mother's house is a warm point at the limit of
                //  sight. This is the frame that decides whether the far
                //  plane and the base density keep the landmark; nothing
                //  else in the set stands where the player first stands.
                //  The eye is on the platform's uphill apron, not at the
                //  pad's centre: the centre is under the canopy, whose
                //  slats filled the first frame. And it waits for the
                //  trough, because the landmark is promised BETWEEN gusts.
                Shot.At(
                    "06-platform-landmark",
                    foot.Position - foot.Forward * PlatformApronSetback +
                    Vector3.up * EyeHeight,
                    plan.MothersHouse.GroundCenter +
                    Vector3.up * LandmarkAimHeight,
                    50f,
                    0,
                    () => root.StormWave <= GustTroughWave),

                //  The bowl wall sideways from the middle of the lane: a
                //  cold mass with a crest line, the toe seam under it, and
                //  no cut line where it meets the plane.
                Shot.At(
                    "07-mid-lane-wall-right",
                    midLane.Position + Vector3.up * EyeHeight,
                    midLane.Position + midLane.Right * WallAimDistance +
                    Vector3.up * WallAimHeight,
                    58f),

                //  Shot 00's framing at the two ends of the breath: with the
                //  far half of the lane closed at a gust crest, and open
                //  again in the trough. Each waits on the root's own wave,
                //  not a frame count - the rhythm is hashed per seed.
                Shot.At(
                    "08-lower-uphill-axis-gust-crest",
                    foot.Position - foot.Forward * 2.6f +
                    foot.Right * 0.25f + Vector3.up * EyeHeight,
                    reveal.Position + Vector3.up * 2.2f,
                    58f,
                    0,
                    () => root.StormWave >= GustCrestWave),
                Shot.At(
                    "09-lower-uphill-axis-gust-trough",
                    foot.Position - foot.Forward * 2.6f +
                    foot.Right * 0.25f + Vector3.up * EyeHeight,
                    reveal.Position + Vector3.up * 2.2f,
                    58f,
                    0,
                    () => root.StormWave <= GustTroughWave),

                // The direct landmark cone stays open, but the untouched
                // snow immediately beside it should close into a readable
                // wall even in the trough.
                Shot.At(
                    "10-platform-side-whiteout",
                    foot.Position - foot.Forward * PlatformApronSetback +
                    Vector3.up * EyeHeight,
                    foot.Position + foot.Right * 28f +
                    Vector3.up * 3.2f,
                    58f,
                    0,
                    () => root.StormWave <= GustTroughWave),

                // From untouched snow the path must read as the only calm
                // cut through the frame. This moves only the capture camera;
                // the field itself remains world-anchored and deterministic.
                Shot.At(
                    "11-off-route-looking-to-lane",
                    exposedCamera,
                    midLane.Position + midLane.Forward * 15f +
                    Vector3.up * 1.8f,
                    62f,
                    30),

                // The house is still a landmark from below; behind its rear
                // wall the world closes before the enclosing ridge.
                Shot.At(
                    "12-top-house-rear-closure",
                    plan.MothersHouse.GroundCenter +
                    houseFacing * 7f +
                    houseRight * (houseHalfWidth + 5f) +
                    Vector3.up * EyeHeight,
                    rearWall - houseFacing * 12f +
                    houseRight * (houseHalfWidth + 4f) +
                    Vector3.up * 3.4f,
                    58f,
                    30)
            };
            var shots =
                new System.Collections.Generic.List<Shot>(baseShots);
            AppendAlpineVillageRoostShots(root, shots);
            AppendAlpineVillageSpringShots(root, shots);
            return shots.ToArray();
        }

        /// <summary>
        /// The spring, at the three distances the brief asks to be judged at:
        /// the ledge and its seeps close enough to see water leaving rock,
        /// the catch and its overflow, and the long look down the brook to
        /// where it leaves the bowl by the cableway cut.
        ///
        /// Every frame is computed from the brook PLAN, never from invented
        /// coordinates: this fixture's own first City run produced a folder
        /// of pictures of the inside of a wall that way.
        /// </summary>
        private static void AppendAlpineVillageSpringShots(
            AlpineVillageRoot root,
            System.Collections.Generic.List<Shot> shots)
        {
            AlpineVillageBrookPlan brook = root.Plan.Brook;
            Assert.That(
                brook,
                Is.Not.Null,
                "The village built no water to photograph.");

            Vector3 facing = brook.LedgeFacing;
            Vector3 across = Vector3.Cross(Vector3.up, facing).normalized;

            shots.Add(Shot.At(
                "20-spring-ledge-and-seeps",
                brook.LedgeCenter + facing * 3.1f + across * 1.1f +
                Vector3.up * 1.35f,
                brook.LedgeCenter + Vector3.up * 0.55f,
                46f,
                24));

            shots.Add(Shot.At(
                "21-spring-catch-and-overflow",
                brook.BowlCenter + facing * 2.4f - across * 2.2f +
                Vector3.up * 1.75f,
                brook.OverflowLip,
                52f,
                24));

            // Down the brook from just below the lip: the reach the water
            // actually takes, with the ledge behind the camera.
            int quarter = Mathf.Clamp(
                brook.Samples.Count / 4,
                1,
                brook.Samples.Count - 1);
            Vector3 downstream = brook.Samples[quarter].Position;
            shots.Add(Shot.At(
                "22-brook-below-the-lip",
                brook.OverflowLip - facing * 1.2f + Vector3.up * 1.9f,
                downstream + Vector3.up * 0.2f,
                58f,
                24));

            // And the whole run from above, which is the frame that has to
            // answer "where does this water go" without a word.
            Vector3 middle = brook.Samples[brook.Samples.Count / 2].Position;
            Vector3 overview = brook.BowlCenter +
                Vector3.up * 26f +
                (brook.BowlCenter - middle).normalized * 22f;
            shots.Add(Shot.At(
                "23-brook-from-above",
                overview,
                middle,
                62f,
                24));

            shots.Add(Shot.At(
                "24-brook-outfall-to-the-cut",
                brook.OutfallPoint +
                Vector3.up * 6.5f +
                (brook.BowlCenter - brook.OutfallPoint).normalized * 14f,
                brook.OutfallPoint + Vector3.up * 0.3f,
                58f,
                24));

            // The wet contour arriving at the chapel's basin: the whole of
            // the link between the two stone catches, and the one that must
            // not read as a built channel.
            shots.Add(Shot.At(
                "25-seep-line-to-chapel-basin",
                brook.ChapelBasinPoint +
                (brook.BowlCenter - brook.ChapelBasinPoint).normalized *
                    -5.5f +
                Vector3.up * 2.4f,
                brook.ChapelBasinPoint + Vector3.up * 0.2f,
                55f,
                24));
        }

        private static Vector3 FindExposedVillageCamera(
            AlpineVillageRoot root,
            AlpineVillageLaneSample lane)
        {
            AlpineVillagePlan plan = root.Plan;
            foreach (int side in new[] { -1, 1 })
            {
                for (float distance = 7f; distance <= 16f; distance += 1f)
                {
                    Vector3 candidate = lane.Position +
                                        lane.Right * (side * distance);
                    var point = new Vector2(candidate.x, candidate.z);
                    if (!plan.TerrainBounds.Contains(point) ||
                        !IsVillageCaptureClear(plan, point, 2f) ||
                        AlpineVillageTerrainSampler.SampleRidgeRise(
                            plan,
                            point) > 0.001f ||
                        root.PeripheralBlizzard.SpatialPlan.Evaluate(point)
                            .StormStrength01 < 0.85f)
                    {
                        continue;
                    }

                    candidate.y = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        point) + EyeHeight;
                    return candidate;
                }
            }

            Assert.Fail(
                "The village capture found no exposed, building-clear " +
                "camera beside the middle of the lane.");
            return lane.Position + Vector3.up * EyeHeight;
        }

        private static bool IsVillageCaptureClear(
            AlpineVillagePlan plan,
            Vector2 point,
            float padding)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                Rect bounds = plan.Plots[index].BoundsXZ;
                if (point.x >= bounds.xMin - padding &&
                    point.x <= bounds.xMax + padding &&
                    point.y >= bounds.yMin - padding &&
                    point.y <= bounds.yMax + padding)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The road's three named roost pairs — bridge rail from the
        /// deck approach, portal shoulder from the carriageway, brink
        /// coping from the terrace — and then the §10f mandatory
        /// grayscale series re-run with the brink pair present: the
        /// 35 m frontal approach in day light and the 20 m oblique.
        /// The zone's Проверка pins the terrace as one horizontal
        /// with sky over it, and these frames decide whether the
        /// silhouetted pair breaks that read — if it does, the roost
        /// falls to the culvert fallback chain; it does not slide
        /// along the parapet. Skipped wholesale when the raven art is
        /// not built (the controller is then absent or inert): the
        /// road contact sheet still photographs the road.
        /// </summary>
        private static void AppendMountainRoadRoostShots(
            MountainRoadRoot root,
            System.Collections.Generic.List<Shot> shots)
        {
            RavenRoostController controller = root.RavenRoosts;
            if (controller == null || controller.RoostCount == 0)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<
                RavenRoostDescriptor> descriptors =
                MountainRoadRavenRoostPlanner.Create(
                    root.Plan,
                    new CityMapMountainRoadTeleportGround(
                        root.World.WalkableArea),
                    root.Plan.Seed);
            if (controller.RoostCount != descriptors.Count)
            {
                return;
            }

            int gorge = IndexOfRoost(
                descriptors,
                "road-roost-gorge-bridge");
            if (gorge >= 0)
            {
                //  From the deck approach, down the centreline at the
                //  abutment: the rail bird near, the deck bird five
                //  metres beyond, the gorge behind both.
                MountainRoadBridgeDescriptor bridge = root.Plan.Bridge;
                Vector3 eye = bridge.Start - bridge.Forward * 4.5f;
                eye.y = bridge.Start.y + 1.7f;
                shots.Add(Shot.At(
                    "10-roost-gorge-bridge",
                    eye,
                    Midpoint(descriptors[gorge]) + Vector3.up * 0.1f,
                    55f,
                    0,
                    WakeRoostWhenPerched(
                        root.Player,
                        controller,
                        gorge,
                        bridge.Start - bridge.Forward * 8f +
                        Vector3.up * 0.5f)));
            }

            int portal = IndexOfRoost(
                descriptors,
                "road-roost-exit-portal");
            if (portal >= 0)
            {
                //  From the carriageway, the walk-out view: the pair
                //  on the shoulder as the hero leaving the tunnel
                //  actually meets it on minute one.
                MountainRoadTunnelDescriptor tunnel = root.Plan.Tunnel;
                Vector3 axis = tunnel.OutwardAxis;
                axis.y = 0f;
                axis = axis.sqrMagnitude > 0.0001f
                    ? axis.normalized
                    : Vector3.forward;
                Vector3 eye = tunnel.PortalGroundCenter + axis * 11f;
                eye.y = tunnel.PortalGroundCenter.y + 1.7f;
                shots.Add(Shot.At(
                    "11-roost-exit-portal",
                    eye,
                    Midpoint(descriptors[portal]) +
                    Vector3.up * 0.15f,
                    55f,
                    0,
                    WakeRoostWhenPerched(
                        root.Player,
                        controller,
                        portal,
                        tunnel.PortalGroundCenter + axis * 12f +
                        Vector3.up * 0.5f)));
            }

            int summit = IndexOfRoost(
                descriptors,
                "road-roost-summit-brink");
            if (summit < 0)
            {
                return;
            }

            RavenRoostDescriptor brinkRoost = descriptors[summit];
            Vector3 coping = brinkRoost.PerchA.Position;
            Vector3 terrace = brinkRoost.PerchB.Position;
            Vector3 inward = -root.Plan.Plateau.Brink.Outward;
            inward.y = 0f;
            inward = inward.sqrMagnitude > 0.0001f
                ? inward.normalized
                : Vector3.forward;
            Vector3 along =
                Vector3.Cross(Vector3.up, inward).normalized;
            Vector3 heroStand = terrace + inward * 8f +
                                Vector3.up * 0.5f;
            Vector3 fromTerrace = terrace + inward * 5f +
                                  along * 2.5f;
            fromTerrace.y = terrace.y + 1.72f;
            shots.Add(Shot.At(
                "12-roost-summit-brink",
                fromTerrace,
                coping + Vector3.up * 0.1f,
                50f,
                0,
                WakeRoostWhenPerched(
                    root.Player,
                    controller,
                    summit,
                    heroStand)));

            //  §10f's mandatory reading of the terrace, pair present:
            //  a standing eye on the terrace floor, the coping line
            //  one horizontal with sky over it, the birds punctuating
            //  it. Day light comes from the fixture's own clock
            //  setup; the frames are judged by looking.
            Vector3 approach = coping + inward * 35f;
            approach.y = terrace.y + EyeHeight;
            shots.Add(Shot.At(
                "13-roost-brink-approach-35m",
                approach,
                coping + Vector3.up * 0.2f,
                58f,
                0,
                WakeRoostWhenPerched(
                    root.Player,
                    controller,
                    summit,
                    heroStand)));
            Vector3 oblique = coping +
                              (inward * 0.8f + along * 0.6f)
                              .normalized * 20f;
            oblique.y = terrace.y + EyeHeight;
            shots.Add(Shot.At(
                "14-roost-brink-oblique-20m",
                oblique,
                coping + Vector3.up * 0.15f,
                54f,
                0,
                WakeRoostWhenPerched(
                    root.Player,
                    controller,
                    summit,
                    heroStand)));
        }

        /// <summary>
        /// The village's roost pairs: the adit-mouth pair first, then
        /// whatever second roost the greedy pass kept — the woodpile
        /// cart stands inside the adit's spacing circle on the
        /// default village, so its authored row usually yields to the
        /// lane fence, and the frames follow the plan rather than a
        /// wish. Skipped wholesale when the raven art is not built.
        /// </summary>
        private static void AppendAlpineVillageRoostShots(
            AlpineVillageRoot root,
            System.Collections.Generic.List<Shot> shots)
        {
            RavenRoostController controller = root.RavenRoosts;
            if (controller == null || controller.RoostCount == 0)
            {
                return;
            }

            var villageGround =
                new CityMapAlpineVillageTeleportGround(
                    root.World.WalkableArea);
            System.Collections.Generic.IReadOnlyList<
                RavenRoostDescriptor> descriptors =
                AlpineVillageRavenRoostPlanner.Create(
                    root.Plan,
                    villageGround,
                    root.Plan.Seed);
            if (controller.RoostCount != descriptors.Count)
            {
                return;
            }

            int count = Mathf.Min(2, descriptors.Count);
            for (int index = 0; index < count; index++)
            {
                AddRoostPairShot(
                    shots,
                    root.Player,
                    controller,
                    villageGround,
                    RavenRoostSettings.AlpineVillage,
                    index,
                    descriptors[index],
                    $"{13 + index:00}-roost-" +
                    ShortRoostName(descriptors[index].StableId));
            }
        }

        /// <summary>
        /// One establishing frame of a roost pair, owning the
        /// wake-the-roost teleport: the hero lands nine metres to the
        /// side of the pair — outside the 3.5 m flush circle of both
        /// perches, far inside the reactivation band, hidden from the
        /// lens by the fixture — and the frame waits until the
        /// controller reports the pair awake and perched. Without the
        /// teleport, the activation radius would leave a far roost
        /// frozen and the frame would photograph empty stone.
        /// </summary>
        private static void AddRoostPairShot(
            System.Collections.Generic.List<Shot> shots,
            PlayerRuntime player,
            RavenRoostController controller,
            ICityMapTeleportGround ground,
            RavenRoostSettings settings,
            int roostIndex,
            in RavenRoostDescriptor roost,
            string name)
        {
            Vector3 a = roost.PerchA.Position;
            Vector3 b = roost.PerchB.Position;
            Vector3 toB = b - a;
            toB.y = 0f;
            toB = toB.sqrMagnitude > 0.0001f
                ? toB.normalized
                : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toB).normalized;
            Vector3 mid = (a + b) * 0.5f;
            shots.Add(Shot.At(
                name,
                mid + side * 6f + Vector3.up * 1.6f,
                mid + Vector3.up * 0.2f,
                52f,
                0,
                WakeRoostWhenPerched(
                    player,
                    controller,
                    roostIndex,
                    ResolveRoostStand(
                        ground, settings, roost, mid, side, toB))));
        }

        /// <summary>
        /// A stand point the hero can actually HOLD near a roost.
        /// A raw offset beside a deck roost lands over water, and the
        /// motor's walkable clamp then drags the hero onto the deck
        /// itself — inside the flush circle, where the pair startles
        /// and, with the hero parked well inside the return gate,
        /// never comes back: the mol frame waited its 900 frames on
        /// exactly that. So the stand is resolved through the same
        /// mask-validated ground the planner used, trying both
        /// flanks and both ends of the pair's axis, and accepting
        /// only a point at least 6 m from BOTH perches (outside the
        /// 3.5 m flush with margin) and comfortably inside the
        /// activation radius. When nothing resolves — no walkable
        /// ground anywhere near — the raw flank point stands, and
        /// the frame's own timeout tells the reviewer the roost is
        /// unphotographable rather than silently skipping it.
        /// </summary>
        private static Vector3 ResolveRoostStand(
            ICityMapTeleportGround ground,
            RavenRoostSettings settings,
            in RavenRoostDescriptor roost,
            Vector3 mid,
            Vector3 side,
            Vector3 toB)
        {
            Vector3[] candidates =
            {
                mid + side * 9f,
                mid - side * 9f,
                mid + toB * 9f,
                mid - toB * 9f,
                mid + side * 12f,
                mid - side * 12f
            };
            float ceiling =
                settings.ActivationRadiusMeters -
                RavenRoostSettings.ReactivationHysteresisMeters - 1f;
            for (int index = 0; index < candidates.Length; index++)
            {
                if (!ground.TryResolveStandingPosition(
                        new Vector2(
                            candidates[index].x,
                            candidates[index].z),
                        out Vector3 standing))
                {
                    continue;
                }

                if (PlanarDistanceXZ(
                        standing, roost.PerchA.Position) >= 6f &&
                    PlanarDistanceXZ(
                        standing, roost.PerchB.Position) >= 6f &&
                    PlanarDistanceXZ(
                        standing, roost.HomeReference) <= ceiling)
                {
                    return standing + Vector3.up * 0.25f;
                }
            }

            // Deck roosts (mol, barge, bridge, landing) sit on
            // surfaces the teleport ground cannot HEIGHT-resolve —
            // the mask knows the footprint but not the deck top, so
            // every probe above failed. Walk the pair's own axis
            // instead: past B, away from A, at B's own authored deck
            // level. The deck footprint IS in the walkable mask, so
            // the motor's clamp holds the hero there — seven metres
            // from the nearer bird, outside the flush circle.
            for (float reach = 7f; reach <= 10f; reach += 3f)
            {
                Vector3 stand =
                    roost.PerchB.Position + toB * reach;
                if (PlanarDistanceXZ(
                        stand, roost.PerchA.Position) >= 6f &&
                    PlanarDistanceXZ(
                        stand, roost.HomeReference) <= ceiling)
                {
                    return stand + Vector3.up * 0.5f;
                }
            }

            return mid + side * 9f + Vector3.up * 0.5f;
        }

        private static float PlanarDistanceXZ(
            Vector3 first,
            Vector3 second)
        {
            float dx = first.x - second.x;
            float dz = first.z - second.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The teleport-then-poll closure the roost frames share: it
        /// fires the hero to the stand point exactly once, then
        /// reports whether the roost is awake and perched. Idempotent
        /// across frames of the same roost — once the condition
        /// holds, later closures return true without moving anybody.
        /// </summary>
        private static Func<bool> WakeRoostWhenPerched(
            PlayerRuntime player,
            RavenRoostController controller,
            int roostIndex,
            Vector3 heroStand)
        {
            bool heroSent = false;
            return () =>
            {
                if (!heroSent)
                {
                    player.Motor.Teleport(heroStand);
                    heroSent = true;
                }

                return controller.IsRoostActive(roostIndex) &&
                       controller.GetRoostPhase(roostIndex) ==
                       CemeteryRavenPhase.PerchedIdle;
            };
        }

        private static Vector3 Midpoint(
            in RavenRoostDescriptor roost)
        {
            return (roost.PerchA.Position + roost.PerchB.Position) *
                   0.5f;
        }

        private static int IndexOfRoost(
            System.Collections.Generic.IReadOnlyList<
                RavenRoostDescriptor> descriptors,
            string stableId)
        {
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (string.Equals(
                        descriptors[index].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>"city-roost-park-fountain" -> "park-fountain":
        /// the area prefix is the folder's business, not the file
        /// name's.</summary>
        private static string ShortRoostName(string stableId)
        {
            const string marker = "roost-";
            int cut = stableId.IndexOf(
                marker,
                StringComparison.Ordinal);
            return cut >= 0
                ? stableId.Substring(cut + marker.Length)
                : stableId;
        }

        /// <summary>
        /// The roost series has nothing to photograph until the raven
        /// prefab is built by the editor pipeline: the controller
        /// then degrades to inert and every perch stands empty. An
        /// Ignore says so honestly where a hung readyWhen would fail
        /// the run late and loudly.
        /// </summary>
        private static void IgnoreUnlessRavenArtBuilt()
        {
            CemeteryRavenProvider provider =
                CemeteryRavenProvider.Load();
            if (provider == null || provider.RavenPrefab == null)
            {
                Assert.Ignore(
                    "The cemetery raven prefab is not built yet.");
            }
        }

        private static Shot FrameVillageBuilding(
            string name,
            AlpineVillagePlotDescriptor plot,
            float side,
            float frontDistance,
            float lateralFraction,
            float fieldOfView)
        {
            Vector3 right = Vector3.Cross(
                Vector3.up,
                plot.Facing).normalized;
            Vector3 position = plot.DoorGroundPosition +
                plot.Facing * frontDistance +
                right * (side * plot.FootprintSize.x * lateralFraction) +
                Vector3.up * 1.72f;
            Vector3 target = plot.GroundCenter +
                Vector3.up * (plot.Height * 0.46f);
            return Shot.At(name, position, target, fieldOfView);
        }

        private static Shot FrameVillageBuildingSide(
            string name,
            AlpineVillagePlotDescriptor plot,
            float side)
        {
            Vector3 right = Vector3.Cross(
                Vector3.up,
                plot.Facing).normalized;
            Vector3 position = plot.GroundCenter +
                right * (side * (plot.FootprintSize.x * 0.5f + 4.2f)) +
                plot.Facing * (plot.FootprintSize.y * 0.12f) +
                Vector3.up * 1.78f;
            Vector3 target = plot.GroundCenter +
                Vector3.up * (plot.Height * 0.43f);
            return Shot.At(name, position, target, 50f);
        }

        private static Shot FrameMountainRoad(
            string name,
            MountainRoadRoutePlan route,
            float cameraDistance,
            float targetDistance,
            float cameraBack,
            float lateral,
            float height,
            float fieldOfView)
        {
            MountainRoadRouteSample cameraSample = route.Sample(
                Mathf.Clamp(cameraDistance, 0f, route.Length));
            MountainRoadRouteSample targetSample = route.Sample(
                Mathf.Clamp(targetDistance, 0f, route.Length));
            Vector3 position = cameraSample.Position -
                cameraSample.Forward * cameraBack +
                cameraSample.Right * lateral +
                Vector3.up * height;
            Vector3 target = targetSample.Position + Vector3.up * 1.1f;
            return Shot.At(name, position, target, fieldOfView);
        }

        private static Shot FrameSpecialEntrance(
            string name,
            BuildingLot lot,
            float lateralOffset)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = lot.DoorPosition + Vector3.up * 1.35f;
            Vector3 position = lot.DoorPosition +
                forward * 4.2f +
                right * lateralOffset +
                Vector3.up * 1.72f;
            return Shot.At(name, position, target, 54f);
        }

        private static Shot FrameSpecialFrontageEdge(
            string name,
            BuildingLot lot,
            float side)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 edge = lot.DoorPosition + right * (side * 5.28f);
            Vector3 target = edge + Vector3.up * 1.55f;
            Vector3 position = edge +
                forward * 3.15f -
                right * (side * 1.60f) +
                Vector3.up * 1.75f;
            return Shot.At(name, position, target, 50f);
        }

        private static Shot FrameSpecialFoundation(
            string name,
            BuildingLot lot)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = lot.DoorPosition -
                forward * 2.15f -
                right * 6f +
                Vector3.up * 0.10f;
            Vector3 position = lot.DoorPosition +
                forward * 0.45f -
                right * 8.65f +
                Vector3.up * 0.78f;
            return Shot.At(name, position, target, 42f);
        }

        private static Shot FrameSpecialBuilding(
            string name,
            BuildingLot lot)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            bool frontsAlongX = lot.FrontageDirection.x != 0;
            float depth = frontsAlongX ? lot.Size.x : lot.Size.y;
            float frontage = frontsAlongX ? lot.Size.y : lot.Size.x;
            Vector3 target = lot.Center +
                (Vector3.up * (lot.Height * 0.42f));
            Vector3 position = lot.Center +
                (forward * ((depth * 0.5f) + 8f)) +
                (right * (frontage * 0.42f)) +
                (Vector3.up * ((lot.Height * 0.58f) + 1f));
            return Shot.At(name, position, target, 60f);
        }

        private static Shot FrameSpecialRear(
            string name,
            BuildingLot lot)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 target = lot.Center -
                forward * (lot.Size.y * 0.34f) +
                Vector3.up * (lot.Height * 0.46f);
            Vector3 position = lot.Center -
                forward * (lot.Size.y * 0.5f + 6.5f) -
                right * (lot.Size.x * 0.30f) +
                Vector3.up * (lot.Height * 0.64f + 0.8f);
            return Shot.At(name, position, target, 58f);
        }

        private static Shot FrameBuildingSurface(
            string name,
            BuildingLot lot,
            CityBuildingAssetRegistry registry,
            bool foundationDetail,
            float lateralShift)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 facade = registry.FrontAnchor.position;
            if (foundationDetail)
            {
                Vector3 position = facade +
                    forward * 3.2f +
                    right * (registry.FrontageWidth * 0.34f + lateralShift) +
                    Vector3.up * 0.72f;
                Vector3 target = facade +
                    right * (registry.FrontageWidth * 0.18f) +
                    Vector3.up * 0.35f;
                return Shot.At(name, position, target, 52f);
            }

            float distance = Mathf.Clamp(registry.Height * 0.25f, 9f, 12f);
            Vector3 obliquePosition = facade +
                forward * distance +
                right * (registry.FrontageWidth * 0.72f) +
                Vector3.up * 2.1f;
            Vector3 obliqueTarget = facade +
                right * (registry.FrontageWidth * 0.08f) +
                Vector3.up * (registry.Height * 0.43f);
            return Shot.At(name, obliquePosition, obliqueTarget, 82f);
        }

        private static Shot FrameWindowFacade(
            string name,
            BuildingLot lot)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float height = lot.Height;
            float frontage = lot.FrontageDirection.x != 0
                ? lot.Size.y
                : lot.Size.x;
            if (lot.IsOrdinaryBuilding)
            {
                CityBuildingAssetRegistry registry =
                    CityBuildingAssetProvider.LoadOrThrow()
                        .GetPrefabOrThrow(lot.District)
                        .GetComponent<CityBuildingAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                height = registry.Height;
                frontage = registry.FrontageWidth;
            }

            // City's fixed Exp2 fog hides nearly the whole facade beyond
            // thirty metres. Stay in a player's street-level range and use
            // the vertical FOV to hold the roof instead of backing into fog.
            float facadeDistance = Mathf.Clamp(
                height * 0.20f,
                8f,
                9.5f);
            // The fixed-metre prototype is locked to DoorPosition through
            // its authored +Z front anchor. Generated lot.Center can be
            // offset from that plane, so it is not a reliable camera target.
            Vector3 facade = lot.DoorPosition +
                Vector3.up * CityFacadeGrid.MassBaseElevation;
            Vector3 position = facade +
                forward * facadeDistance +
                right * (frontage * 0.12f) +
                Vector3.up * 1.72f;
            float bottomAngle = Mathf.Atan2(
                0.15f - 1.72f,
                facadeDistance);
            float topAngle = Mathf.Atan2(
                height - 1.72f,
                facadeDistance);
            float aimAngle = (bottomAngle + topAngle) * 0.5f;
            float aimHeight = 1.72f +
                Mathf.Tan(aimAngle) * facadeDistance;
            Vector3 target = facade +
                Vector3.up * aimHeight;
            float fieldOfView = Mathf.Clamp(
                (topAngle - bottomAngle) * Mathf.Rad2Deg + 8f,
                72f,
                105f);
            return Shot.At(name, position, target, fieldOfView);
        }

        private static IEnumerator Capture(
            string sceneName,
            Func<Component> findRoot,
            Shot[] shots)
        {
            return Capture(sceneName, findRoot, () => shots);
        }

        private static IEnumerator Capture(
            string sceneName,
            Func<Component> findRoot,
            Func<Shot[]> resolveShots)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                operation.isDone,
                Is.True,
                $"Scene '{sceneName}' did not load.");

            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (findRoot() == null &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                findRoot(),
                Is.Not.Null,
                $"Scene '{sceneName}' never built its root.");

            Shot[] shots = resolveShots();
            Assert.That(shots, Is.Not.Null.And.Not.Empty);

            for (int frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }

            Camera camera = Camera.main;
            Assert.That(
                camera,
                Is.Not.Null,
                $"Scene '{sceneName}' has no main camera to shoot with.");

            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                sceneName);
            Directory.CreateDirectory(folder);

            //  The scene's OWN camera is borrowed rather than a fresh one
            //  added, so the frames carry the real post-processing stack,
            //  culling mask and volume weights. Its state is restored
            //  afterwards in a finally, because a test that leaves the
            //  main camera pointing at a render texture breaks every test
            //  that runs after it.
            RenderTexture target = new RenderTexture(Width, Height, 24);
            Texture2D frameBuffer =
                new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFieldOfView = camera.fieldOfView;

            //  The main camera is the PLAYER's camera, so the hero stands
            //  in front of it and his head fills the middle of every shot.
            //  These frames are for judging the world; the hero has his
            //  own art spec and his own tests. Hidden by renderer, not by
            //  deactivating him, so nothing he drives stops running.
            var hiddenPlayer = new System.Collections.Generic.List<Renderer>();
            PlayerMotor player = Object.FindAnyObjectByType<PlayerMotor>();
            if (player != null)
            {
                foreach (Renderer renderer in
                         player.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hiddenPlayer.Add(renderer);
                    }
                }
            }

            try
            {
                camera.targetTexture = target;
                foreach (Shot shot in shots)
                {
                    //  A weather proof may deliberately wait before a shot.
                    //  The camera is positioned only AFTER that wait, so its
                    //  follow script can run normally and still cannot pull
                    //  the actual capture pose away between positioning and
                    //  rendering.
                    for (int frame = 0;
                         frame < shot.DelayFrames;
                         frame++)
                    {
                        yield return null;
                    }

                    //  Then, if the shot names a condition, wait for the
                    //  scene to reach it - bounded, so a condition that is
                    //  never met is a failed frame, not a hung run.
                    if (shot.ReadyWhen != null)
                    {
                        int waited = 0;
                        while (!shot.ReadyWhen() &&
                               waited < MaximumReadyFrames)
                        {
                            waited++;
                            yield return null;
                        }

                        Assert.That(
                            shot.ReadyWhen(),
                            Is.True,
                            $"'{sceneName}/{shot.Name}' waited " +
                            $"{MaximumReadyFrames} frames and its moment " +
                            "never came.");
                    }

                    Vector3 from = shot.Position;
                    Vector3 to = shot.Target;
                    if (shot.RelativeToHero)
                    {
                        Assert.That(
                            player,
                            Is.Not.Null,
                            $"'{sceneName}/{shot.Name}' is framed on the " +
                            "hero, and this scene has none.");
                        from = player.transform.TransformPoint(from);
                        to = player.transform.TransformPoint(to);
                    }

                    camera.transform.SetPositionAndRotation(
                        from,
                        Quaternion.LookRotation(
                            (to - from).normalized,
                            Vector3.up));
                    camera.fieldOfView = shot.FieldOfView;
                    camera.Render();

                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = target;
                    frameBuffer.ReadPixels(
                        new Rect(0f, 0f, Width, Height), 0, 0);
                    frameBuffer.Apply();
                    RenderTexture.active = previousActive;

                    string path = Path.Combine(
                        folder,
                        $"{shot.Name}.png");
                    File.WriteAllBytes(path, frameBuffer.EncodeToPNG());
                    Debug.Log($"Area capture wrote {path}");

                    Assert.That(
                        IsBlank(frameBuffer),
                        Is.False,
                        $"'{sceneName}/{shot.Name}' came out a single flat " +
                        "colour: the camera saw nothing. Wrong place, wrong " +
                        "culling mask, or a world that never built.");
                }
            }
            finally
            {
                foreach (Renderer renderer in hiddenPlayer)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                    }
                }

                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(
                    previousPosition,
                    previousRotation);
                camera.fieldOfView = previousFieldOfView;
                Object.DestroyImmediate(frameBuffer);
                target.Release();
                Object.DestroyImmediate(target);
            }

        }

        /// <summary>
        /// True when every sampled pixel is the same colour.
        ///
        /// A folder full of flat rectangles looks like success from the
        /// outside - the files exist, the run is green, the log says it
        /// wrote them - which is precisely the failure this whole fixture
        /// is meant to stop.
        /// </summary>
        private static bool IsBlank(Texture2D frame)
        {
            const int Steps = 24;
            Color32 first = frame.GetPixel(0, 0);
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    Color32 sample = frame.GetPixel(
                        x * (frame.width - 1) / (Steps - 1),
                        y * (frame.height - 1) / (Steps - 1));
                    if (sample.r != first.r ||
                        sample.g != first.g ||
                        sample.b != first.b)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
