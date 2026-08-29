using System;
using System.Collections;
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
                int delayFrames)
            {
                Name = name;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
                RelativeToHero = relativeToHero;
                DelayFrames = Mathf.Max(0, delayFrames);
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Target { get; }
            public float FieldOfView { get; }
            public bool RelativeToHero { get; }
            public int DelayFrames { get; }

            /// <summary>A fixed place, for a room whose layout is known.</summary>
            public static Shot At(
                string name,
                Vector3 position,
                Vector3 target,
                float fieldOfView = 60f,
                int delayFrames = 0)
            {
                return new Shot(
                    name,
                    position,
                    target,
                    fieldOfView,
                    false,
                    delayFrames);
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
                    delayFrames);
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
            yield return Capture(
                SceneIds.SupermarketInterior,
                Root<SupermarketInteriorRoot>(),
                HeroShots());
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
            return new[]
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
        }

        /// <summary>
        /// Frames the one uphill composition and then proves that both the
        /// ordinary and terminal house shells survive the runtime material's
        /// back-face culling. Every point comes from the shipped plan; no
        /// camera depends on this seed keeping yesterday's world coordinates.
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
            AlpineVillagePlotDescriptor lowerHouse = null;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind != AlpineVillagePlotKind.House ||
                    (lowerHouse != null &&
                     plot.LaneDistance >= lowerHouse.LaneDistance))
                {
                    continue;
                }

                lowerHouse = plot;
            }

            Assert.That(
                lowerHouse,
                Is.Not.Null,
                "The village has no ordinary house to photograph.");

            AlpineVillageLaneSample foot = plan.Lane.Sample(2f);
            AlpineVillageLaneSample reveal = plan.Lane.Sample(
                Mathf.Min(plan.Lane.Length * 0.62f, 52f));
            return new[]
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
                    "01-ordinary-house-front-side",
                    lowerHouse,
                    1f,
                    4.3f,
                    0.34f,
                    50f),
                FrameVillageBuildingSide(
                    "02-ordinary-house-side-wall",
                    lowerHouse,
                    -1f),
                FrameVillageBuilding(
                    "03-top-house",
                    plan.MothersHouse,
                    -1f,
                    6.4f,
                    0.30f,
                    54f)
            };
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
