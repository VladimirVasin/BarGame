using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
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
    public sealed class CompactBarAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            // Rebind cloth and hand anchors to the newly imported production rig.
            Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.BarAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
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

        // Dispose this scene's camera-local service before the next Single
        // load destroys the camera hierarchy in an unspecified order.
        [UnityTearDown]
        public IEnumerator ReleaseBarServiceAfterCapture()
        {
            yield return ReleaseBarService();
        }

        internal static IEnumerator ReleaseBarService()
        {
            BarInteriorRoot bar = Object.FindAnyObjectByType<BarInteriorRoot>();
            if (bar == null || bar.DrinkShop == null) yield break;
            BarDrinkFirstPersonArms arms = bar.DrinkShop.FirstPersonArms;
            Object.Destroy(bar.DrinkShop);
            yield return null;
            if (arms != null) Object.Destroy(arms);
            yield return null;
        }

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
                Func<bool> readyWhen,
                float readyPollSeconds = 0f)
            {
                Name = name;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
                RelativeToHero = relativeToHero;
                DelayFrames = Mathf.Max(0, delayFrames);
                ReadyWhen = readyWhen;
                ReadyPollSeconds = Mathf.Max(0f, readyPollSeconds);
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

            // Opt-in cadence for readiness governed by real-time debouncing.
            public float ReadyPollSeconds { get; }

            /// <summary>A fixed place, for a room whose layout is known.</summary>
            public static Shot At(
                string name,
                Vector3 position,
                Vector3 target,
                float fieldOfView = 60f,
                int delayFrames = 0,
                Func<bool> readyWhen = null,
                float readyPollSeconds = 0f)
            {
                return new Shot(
                    name,
                    position,
                    target,
                    fieldOfView,
                    false,
                    delayFrames,
                    readyWhen,
                    readyPollSeconds);
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
        [PrebuildSetup(typeof(CompactBarAssetsSetup))]
        public IEnumerator BarCeilingFan()
        {
            GameSessionState.BeginNewGame("bar-capture");
            BarTestTime.StartFullAttendance("bar-capture");
            GameSessionState.EnterBar("bar-capture", BarActivityKind.SplitTheG,
                CityDistrictKind.Residential);
            yield return Capture(SceneIds.BarInterior, Root<BarInteriorRoot>(), new[]
            {
                Shot.At("Test compact ceiling fan from entrance",
                    new Vector3(0f, 1.72f, -3.35f), new Vector3(0f, 2.55f, -.65f), 65f),
                Shot.At("Test compact ceiling fan at eye height",
                    new Vector3(1.1f, 1.7f, -1.4f), new Vector3(0f, 2.55f, -.65f), 65f)
            });
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        [PrebuildSetup(typeof(CompactBarAssetsSetup))]
        public IEnumerator Bar()
        {
            float previousDelta = Time.captureDeltaTime;
            GameTimeRuntime clock = BarPromenadeRuntimeBootstrap.EnsureGameTimeRuntimeInstalled();
            bool clockWasEnabled = clock.enabled;
            Time.captureDeltaTime = 1f / 30f;
            // Hold the chosen attendance hour while real dialogue, drinking,
            // physics and animation keep advancing through the close views.
            clock.enabled = false;
            try { yield return CaptureBarWorldAudit(); }
            finally
            {
                if (clock != null) clock.enabled = clockWasEnabled;
                Time.captureDeltaTime = previousDelta;
            }
        }

        private IEnumerator CaptureBarWorldAudit()
        {
            foreach (BarActivityKind activity in new[] { BarActivityKind.BeerPong,
                BarActivityKind.Cocktail, BarActivityKind.SplitTheG, BarActivityKind.TinctureMatch })
                BarInteriorLayoutValidator.ValidateOrThrow(
                    BarInteriorLayoutPlanner.Generate(47, "Test bar", activity));
            GameSessionState.BeginNewGame("bar-capture");
            BarTestTime.StartFullAttendance("bar-capture");
            GameSessionState.EnterBar("bar-capture", BarActivityKind.SplitTheG,
                CityDistrictKind.Residential);
            yield return Capture(
                SceneIds.BarInterior,
                Root<BarInteriorRoot>(),
                new[]
                {
                    Shot.At("00-from-the-door",
                        new Vector3(0f, 1.72f, -3.35f),
                        new Vector3(0f, 1.35f, 1.8f), 65f),
                    Shot.At("01-counter-and-worker",
                        new Vector3(0f, 1.65f, -0.65f),
                        new Vector3(0.2f, 1.35f, 2.7f), 58f),
                    Shot.At("02-counter-drink",
                        new Vector3(-1.8f, 1.5f, 1.95f),
                        new Vector3(-1.65f, 1.35f, 0.95f), 48f,
                        readyWhen: () => IsBarDrinkAtLips("counter-seat-left")),
                    Shot.At("03-counter-rest",
                        new Vector3(-1.85f, 1.65f, 1.95f),
                        new Vector3(-1.65f, 1.17f, 1.35f), 46f,
                        readyWhen: () => IsBarDrinkRestingOnSurface("counter-seat-left")),
                    Shot.At("04-booths",
                        new Vector3(-0.7f, 2.35f, -0.4f),
                        new Vector3(-4.38f, 0.85f, -0.75f), 78f),
                    Shot.At("05-activity-and-music",
                        new Vector3(1.8f, 1.9f, -0.4f),
                        new Vector3(4.7f, 0.95f, -1.6f), 65f),
                    Shot.At("06-pendants-and-ceiling",
                        new Vector3(1f, 1.7f, -1f),
                        new Vector3(0f, 2.65f, 2f), 65f),
                    Shot.At("07-overview",
                        new Vector3(4.8f, 1.95f, -3.2f),
                        new Vector3(-0.6f, 1.2f, 1.1f), 70f),
                    Shot.At("08-counter-materials",
                        new Vector3(-2f, 1.15f, 0.7f),
                        new Vector3(0.5f, 1.03f, 2f), 50f),
                    Shot.At("09-standard-entrance-door",
                        new Vector3(0f, 1.62f, -1.7f),
                        new Vector3(0f, 1.2f, -3.83f), 48f),
                    Shot.At("10-karaoke-from-exit",
                        new Vector3(2.6f, 1.65f, -3.05f),
                        new Vector3(4.65f, 1f, -2.7f), 62f),
                    Shot.At("11-tap-corner-cleared",
                        new Vector3(3.8f, 1.65f, 0.4f),
                        new Vector3(4.85f, 0.65f, 2.95f), 58f),
                    Shot.At("12-opposing-sofas",
                        new Vector3(-2.5f, 1.35f, -2.15f),
                        new Vector3(-4.38f, 0.85f, -2.15f), 70f),
                    Shot.At("13-sofa-front-drink",
                        new Vector3(-2.8f, 1.45f, -2.62f),
                        new Vector3(-4.38f, 1.15f, -2.97f), 50f,
                        readyWhen: () => IsBarDrinkAtLips("booth-1-near")),
                    Shot.At("14-sofa-opposite-drink",
                        new Vector3(-2.8f, 1.45f, -1.68f),
                        new Vector3(-4.38f, 1.15f, -1.33f), 50f,
                        readyWhen: () => IsBarDrinkAtLips("booth-1-far")),
                    Shot.At("15-second-sofa-front-drink",
                        new Vector3(-2.8f, 1.45f, 0.18f),
                        new Vector3(-4.38f, 1.15f, -0.17f), 50f,
                        readyWhen: () => IsBarDrinkAtLips("booth-2-near")),
                    Shot.At("16-second-sofa-opposite-drink",
                        new Vector3(-2.8f, 1.45f, 1.12f),
                        new Vector3(-4.38f, 1.15f, 1.47f), 50f,
                        readyWhen: () => IsBarDrinkAtLips("booth-2-far")),
                    Shot.At("17-shortened-counter-left",
                        new Vector3(-3f, 1.7f, -0.3f),
                        new Vector3(0.25f, 0.95f, 1.95f), 65f),
                });
            BarInteriorRoot posedBar = Object.FindAnyObjectByType<BarInteriorRoot>();
            foreach (BarPatron patron in posedBar.Patrons)
            {
                Animator animator = patron.Registry.Animator;
                Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate),
                    patron.Anchor.Id + " must keep the body, both hands and vessel in the same pose off camera.");
                patron.Drinking.enabled = false;
                Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms),
                    patron.Anchor.Id + " must release its animation mode when drinking stops.");
                patron.Drinking.enabled = true;
                Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
            }
            yield return VerifyAllBarDrinkCycles(posedBar);
            var finalPose = posedBar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            yield return AtWarehouseFridgePresentation(finalPose, AssertBarPatronPlacement);
            Object.Destroy(finalPose);
            BarInteriorRoot bar = Object.FindAnyObjectByType<BarInteriorRoot>();
            BarInteriorLayoutValidator.ValidateOrThrow(bar.Layout);
            Assert.That(bar.Room.Find("Social Pub Table 4"), Is.Null);
            Assert.That(bar.Room.Find("Social Pub Table 4 Leg"), Is.Null);
            Assert.That(bar.Room.Find("Social Pub Table 4 Collision"), Is.Null);
            Assert.That(bar.Room.Find("Pub Table 4 Coaster"), Is.Null);
            Assert.That(bar.Room.Find("Booth Table Top 3"), Is.Null);
            Assert.That(Array.FindAll(bar.Room.GetComponentsInChildren<Renderer>(),
                renderer => renderer.name.StartsWith("Booth Cushion ")).Length, Is.EqualTo(4));
            Assert.That(Array.FindAll(bar.Room.GetComponentsInChildren<Renderer>(),
                renderer => renderer.name.StartsWith("Booth Table Top ")).Length, Is.EqualTo(2));
            Physics.SyncTransforms();
            Assert.That(Physics.OverlapBox(bar.Room.TransformPoint(new Vector3(4.85f, .78f, 2.95f)),
                new Vector3(.28f, .05f, .28f), bar.Room.rotation, ~0, QueryTriggerInteraction.Ignore),
                Is.Empty, "The removed corner table must leave no solid collision behind.");
            Transform musicCabinet = bar.Room.Find("Bar Jukebox");
            Assert.That(musicCabinet, Is.Not.Null);
            Assert.That(Vector3.Distance(musicCabinet.localPosition,
                new Vector3(5f, 0f, -0.65f)), Is.LessThan(0.001f));
            Assert.That(Vector3.Dot(musicCabinet.right, -bar.Room.right),
                Is.GreaterThan(0.999f), "The jukebox face must point into the room.");
            foreach (Transform part in bar.Room.GetComponentsInChildren<Transform>(true))
                Assert.That(part.name.Contains(" Console") ||
                    part.name.StartsWith("SplitTheG Vessel ") ||
                    part.name.StartsWith("Cocktail Vessel ") ||
                    part.name.StartsWith("TinctureMatch Vessel "), Is.False,
                    "The removed table must leave no visible part or collision: " + part.name);
            Assert.That(Vector3.Distance(bar.Music.transform.position,
                musicCabinet.Find("Jukebox Grille").GetComponent<Renderer>().bounds.center),
                Is.LessThan(0.001f), "Music must follow the relocated grille.");
            Bounds stageBounds = bar.Room.Find("Small Stage").GetComponent<Renderer>().bounds;
            Bounds microphoneBounds = bar.Room.Find("Stage Microphone").GetComponent<Renderer>().bounds;
            Vector3 micLocal = bar.Room.InverseTransformPoint(microphoneBounds.center);
            Assert.That(Vector3.Distance(micLocal, BarInteriorLayoutPlanner.StageMicrophonePosition),
                Is.LessThan(0.001f), "The microphone must occupy the exit side of the stage.");
            Vector3 stageFront = Vector3.ProjectOnPlane(microphoneBounds.center - stageBounds.center, bar.Room.up);
            Vector3 towardExit = Vector3.ProjectOnPlane(bar.Room.TransformPoint(new Vector3(0f, 0f, -3.9f)) - microphoneBounds.center, bar.Room.up);
            Assert.That(Vector3.Dot(stageFront.normalized, -bar.Room.right), Is.GreaterThan(.99999f),
                "The whole karaoke assembly must retain its authored quarter-turn toward the exit side.");
            Assert.That(Vector3.Dot(stageFront, towardExit), Is.GreaterThan(0f));
            Bounds stageCollision = bar.Room.Find("Small Stage Collision").GetComponent<BoxCollider>().bounds;
            Assert.That(Vector3.Distance(stageBounds.center, stageCollision.center), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(stageBounds.size, stageCollision.size), Is.LessThan(0.001f),
                "The platform collision must turn with the visible platform.");
            Assert.That(stageBounds.size.x, Is.EqualTo(2.1f).Within(.001f));
            Assert.That(stageBounds.size.z, Is.EqualTo(1.65f).Within(.001f));
            foreach (Transform part in bar.Room.GetComponentsInChildren<Transform>(true))
                Assert.That(part.name.StartsWith("Coat Rack"), Is.False,
                    "The coat rack, hooks and collision must be removed together.");
            BarDrinkServicePlan servicePlan = BarDrinkServicePlan.FromLayout(bar.Layout);
            foreach (BarCounterStation station in bar.CounterStations)
            {
                float guestX = station.Seat.Plan.EntryPose.RootPosition.x;
                Assert.That(Mathf.Abs(servicePlan.ResolveBottlePreparationPosition(guestX).x - guestX),
                    Is.GreaterThan(1.4f), "Pouring must leave the guest's resting menu clear.");
            }
            // Measure placed renderers as well as data: import-unit mistakes
            // can leave every anchor correct while shrinking the visible room.
            Renderer[] surfaces = bar.Room.GetComponentsInChildren<Renderer>();
            Bounds placed = surfaces[0].bounds;
            foreach (Renderer surface in surfaces)
                if (!(surface is ParticleSystemRenderer)) placed.Encapsulate(surface.bounds);
            Assert.That(placed.size.x, Is.InRange(11.3f, 11.9f));
            // These are whole-room bounds, including the authored WC rear
            // wing; RoomSize.y alone describes only the public hall.
            float fullDepth = bar.Layout.RoomSize.y * .5f + bar.Layout.WallThickness * .5f +
                BarWashroomLayout.Bounds.yMax + BarWashroomLayout.WallThickness;
            Assert.That(placed.size.z, Is.EqualTo(fullDepth).Within(.05f),
                "The placed hall and washroom wing must retain their shared metre envelope.");
            Assert.That(placed.max.y, Is.LessThan(3.3f));
            Renderer counter = bar.Room.Find("Counter Top").GetComponent<Renderer>();
            Assert.That(bar.Layout.CounterPosition.x, Is.EqualTo(0.875f));
            Assert.That(bar.Layout.CounterSize.x, Is.EqualTo(5.95f));
            Assert.That(counter.bounds.min.x - bar.Room.position.x,
                Is.EqualTo(-3.225f + 0.9f).Within(0.002f),
                "The left countertop end must retract by exactly one stool pitch.");
            Assert.That(counter.bounds.max.x - bar.Room.position.x,
                Is.EqualTo(4.075f).Within(0.002f), "The right countertop end must stay fixed.");
            Assert.That(bar.Room.Find("Bar Stool 1"), Is.Null);
            Assert.That(bar.Room.Find("Bar Stool 1 Leg"), Is.Null);
            Assert.That(bar.Room.Find("Bar Stool 1 Collision"), Is.Null);
            Assert.That(bar.CounterStations, Has.Count.EqualTo(3));
            Assert.That(Array.FindAll(surfaces, surface => surface.name == "Bar Drink Service Stool" ||
                (surface.name.StartsWith("Bar Stool ") && !surface.name.EndsWith(" Leg"))).Length,
                Is.EqualTo(5), "Exactly one counter seat must be removed.");
            Physics.SyncTransforms();
            Assert.That(Physics.OverlapBox(bar.Room.TransformPoint(new Vector3(-2.65f, .5f, 1.95f)),
                new Vector3(.25f, .45f, .35f), bar.Room.rotation, ~0, QueryTriggerInteraction.Ignore),
                Is.Empty, "The removed left counter bay must leave no collision.");
            Mesh counterMesh = counter.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(counterMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0), Is.True);
            Assert.That(counterMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1), Is.True);
            Assert.That(counter.sharedMaterial.shader.name,
                Is.EqualTo("Bar Promenade/City Surface"));
            var response = new MaterialPropertyBlock();
            counter.GetPropertyBlock(response);
            Assert.That(response.GetTexture("_BaseMap").name,
                Is.EqualTo("BarInteriorAlbedo"));
            Assert.That(response.GetTexture("_SceneMaterialResponse").name,
                Is.EqualTo("BarInteriorResponse"));
            Assert.That(bar.Lighting.BounceRegionCount, Is.EqualTo(4));
            Assert.That(bar.Lighting.Reflection.ReceiverCount, Is.GreaterThan(0));
            if (GraphicsEffectsSettings.CleanReflectionsEnabled)
            {
                float reflectionDeadline = Time.realtimeSinceStartup + 8f;
                while (!bar.Lighting.Reflection.IsReady &&
                       Time.realtimeSinceStartup < reflectionDeadline)
                    yield return null;
                Assert.That(bar.Lighting.Reflection.IsReady, Is.True);
            }
            yield return VerifyBarKaraokeStanding(bar);
        }

        private static IEnumerator VerifyBarKaraokeStanding(BarInteriorRoot bar)
        {
            bar.ArrivalPresentation.Skip();
            PlayerMotor motor = bar.Player.Motor;
            Transform hero = bar.Player.GameObject.transform;
            CharacterController capsule = hero.GetComponent<CharacterController>();
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasFollowing = follow.enabled, inputEnabled = motor.InputEnabled;
            Vector3 previousPosition = hero.position, previousCameraPosition = camera.transform.position;
            Quaternion previousRotation = hero.rotation, previousCameraRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            try
            {
                motor.SetInputEnabled(false);
                follow.enabled = false;
                motor.Teleport(bar.Room.TransformPoint(BarInteriorLayoutPlanner.StageApproachPosition));
                hero.rotation = Quaternion.LookRotation(bar.Room.right, bar.Room.up);
                Physics.SyncTransforms();
                foreach (Vector3 local in new[] { BarInteriorLayoutPlanner.StageEntryPosition,
                             BarInteriorLayoutPlanner.StageStandingPosition })
                {
                    Vector3 target = bar.Room.TransformPoint(local);
                    float deadline = Time.realtimeSinceStartup + 6f;
                    bool reached = false;
                    while (!reached && Time.realtimeSinceStartup < deadline)
                    {
                        reached = motor.MoveTowardsApproachWaypoint(target, .015f, Time.deltaTime);
                        Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                            "The ordinary hero capsule must walk onto the low stage and behind the microphone.");
                        yield return null;
                    }
                    Assert.That(reached, Is.True, "The stage approach must be physically traversable.");
                }
                Quaternion facing = Quaternion.LookRotation(-bar.Room.right, bar.Room.up);
                float turnDeadline = Time.realtimeSinceStartup + 3f;
                bool faced = false;
                while (!faced && Time.realtimeSinceStartup < turnDeadline)
                {
                    faced = motor.MoveTowardsInteractionPose(hero.position, facing, Time.deltaTime);
                    yield return null;
                }
                Assert.That(faced, Is.True);
                motor.CancelInteractionPoseMove();
                for (int frame = 0; frame < 5; frame++) yield return null;
                Bounds platform = bar.Room.Find("Small Stage Collision").GetComponent<BoxCollider>().bounds;
                Vector3 position = bar.Room.InverseTransformPoint(hero.position);
                Assert.That(position.x, Is.EqualTo(4.2f).Within(.025f));
                Assert.That(position.z, Is.EqualTo(-2.7f).Within(.025f));
                Assert.That(capsule.bounds.min.y, Is.EqualTo(platform.max.y).Within(.05f),
                    "The hero must stand on the platform without hovering or sinking.");
                Assert.That(capsule.bounds.min.x, Is.GreaterThan(platform.min.x));
                Assert.That(capsule.bounds.max.x, Is.LessThan(platform.max.x));
                Assert.That(capsule.bounds.min.z, Is.GreaterThan(platform.min.z));
                Assert.That(capsule.bounds.max.z, Is.LessThan(platform.max.z));
                Bounds microphone = bar.Room.Find("Stage Microphone").GetComponent<Renderer>().bounds;
                Assert.That(capsule.bounds.min.x, Is.GreaterThan(microphone.max.x + .04f),
                    "The hero must fit behind the microphone, facing out into the hall.");
                Assert.That(Vector3.Dot(hero.forward, -bar.Room.right), Is.GreaterThan(.999f));
                camera.transform.SetPositionAndRotation(bar.Room.TransformPoint(new Vector3(2.35f, 1.8f, -1.85f)),
                    Quaternion.LookRotation(bar.Room.TransformPoint(new Vector3(4.1f, 1f, -2.7f)) -
                        bar.Room.TransformPoint(new Vector3(2.35f, 1.8f, -1.85f)), bar.Room.up));
                camera.fieldOfView = 64f;
                yield return null;
                yield return AtWarehouseFridgePresentation(probe,
                    () => CaptureCurrentCamera(camera, SceneIds.BarInterior, "18-hero-behind-karaoke-microphone"));
            }
            finally
            {
                probe.CancelPending();
                Object.Destroy(probe);
                motor.CancelInteractionPoseMove();
                motor.Teleport(previousPosition);
                hero.rotation = previousRotation;
                motor.SetInputEnabled(inputEnabled);
                camera.transform.SetPositionAndRotation(previousCameraPosition, previousCameraRotation);
                camera.fieldOfView = previousFov;
                follow.enabled = wasFollowing;
            }
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        [PrebuildSetup(typeof(CompactBarAssetsSetup))]
        public IEnumerator BarMenu()
        {
            GameSessionState.BeginNewGame("bar-menu-capture");
            BarTestTime.StartFullAttendance("bar-menu-capture");
            GameSessionState.EnterBar("bar-menu-capture", BarActivityKind.SplitTheG,
                CityDistrictKind.Residential);
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.BarInterior,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            BarInteriorRoot bar = null;
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (bar == null && Time.realtimeSinceStartup < deadline)
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                yield return null;
            }

            Assert.That(bar, Is.Not.Null);
            bar.ArrivalPresentation.Skip();

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            camera.aspect = (float)Width / Height;
            PlayerCameraFollow cameraFollow =
                camera.GetComponent<PlayerCameraFollow>();
            Assert.That(cameraFollow, Is.Not.Null);

            BarBartenderPresentation bartender = bar.Bartender;
            Assert.That(bartender, Is.Not.Null);
            Assert.That(
                bartender.Registry.TryGetClip(
                    BarBartenderClipKind.Wipe,
                    out AnimationClip wipeClip,
                    out bool wipeLoops),
                Is.True);
            Assert.That(wipeLoops, Is.True);
            float currentWipeTime = Mathf.Repeat(
                bartender.CurrentClipTimeSeconds,
                wipeClip.length);
            float targetWipeTime = wipeClip.length * 0.28f;
            float wipeAdvance = targetWipeTime - currentWipeTime;
            if (wipeAdvance < 0f)
            {
                wipeAdvance += wipeClip.length;
            }

            cameraFollow.enabled = false;
            bartender.enabled = false;
            bartender.Advance(wipeAdvance);
            camera.transform.SetPositionAndRotation(
                bar.Room.TransformPoint(new Vector3(-1.5f, 1.55f, 0.6f)),
                Quaternion.LookRotation(
                    bar.Room.TransformPoint(
                        new Vector3(-0.15f, 1.04f, 2.1f)) -
                    bar.Room.TransformPoint(
                        new Vector3(-1.5f, 1.55f, 0.6f)),
                    Vector3.up));
            camera.fieldOfView = 48f;
            CaptureCurrentCamera(
                camera,
                SceneIds.BarInterior,
                "09-bartender-wipe-contact");
            bartender.enabled = true;
            cameraFollow.enabled = true;

            BarCounterStation station = bar.CounterStation;
            Assert.That(station, Is.Not.Null);
            CounterSeatPlan seatPlan = station.Seat.Plan;
            Vector3 entry = seatPlan.ApproachWaypoints[0];
            entry.y = seatPlan.EntryPose.RootPosition.y;
            bar.Player.Motor.Teleport(entry);
            bar.Player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            Assert.That(station.CanInteract(bar.Player.Interactor), Is.True);
            station.Interact(bar.Player.Interactor);

            deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline &&
                   (!station.Seat.IsSeated ||
                    bar.DrinkShop.MenuState !=
                        BarPromenade.Runtime.World.CounterMenuState.Open ||
                    !station.SeatView.IsMenuFocusComplete))
            {
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(
                bar.DrinkShop.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(station.SeatView.IsMenuFocusComplete, Is.True);
            yield return null;

            CaptureCurrentCamera(
                camera,
                SceneIds.BarInterior,
                "09-menu-close-up");

            Assert.That(bar.DrinkShop.RestPhysicalMenuAtCounter(), Is.True);
            CounterMenuPageView menuPage =
                bar.DrinkShop.MenuPresentation.Page;
            deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline &&
                   (station.SeatView.IsMenuFocusLocked ||
                    menuPage.FoldAmount < 0.999f))
            {
                yield return null;
            }

            Assert.That(station.SeatView.IsMenuFocusLocked, Is.False);
            Assert.That(menuPage.FoldAmount, Is.EqualTo(1f).Within(0.001f));
            Vector3 restingCentre = menuPage.RestingWorldCenter;
            BarMenuTestCameraAimDriver aimDriver =
                camera.gameObject.AddComponent<BarMenuTestCameraAimDriver>();
            aimDriver.Target = restingCentre;
            aimDriver.LookAway = true;
            aimDriver.IsAiming = true;
            yield return null;
            aimDriver.LookAway = false;
            yield return null;
            Assert.That(bar.DrinkShop.IsLookingAtRestingMenu, Is.True);
            Assert.That(menuPage.IsRestingHighlighted, Is.True);
            CaptureCurrentCamera(
                camera,
                SceneIds.BarInterior,
                "10-menu-reopen-highlight");
            Object.Destroy(aimDriver);
        }

        private static bool IsBarDrinkAtLips(string anchorId)
        {
            BarInteriorRoot bar =
                Object.FindAnyObjectByType<BarInteriorRoot>();
            if (bar == null || bar.Patrons == null)
            {
                return false;
            }

            for (int index = 0; index < bar.Patrons.Count; index++)
            {
                BarPatron patron = bar.Patrons[index];
                BarPatronDrinkingArmPose drinking = patron.Drinking;
                bool selected = string.Equals(
                    patron.Anchor.Id,
                    anchorId,
                    StringComparison.Ordinal);
                if (selected &&
                    drinking != null &&
                    drinking.Timeline.Phase == BarPatronDrinkPhase.Sip &&
                    drinking.Timeline.VesselTipWeight >= 0.95f &&
                    drinking.BottleMouthDistance <=
                    BarPatronDrinkingArmPose.BottleLipContactTolerance &&
                    drinking.BottleGripError <=
                    BarPatronDrinkingArmPose.BottleGripContactTolerance &&
                    drinking.BottleHandOrientationErrorDegrees <=
                    BarPatronDrinkingArmPose
                        .BottleHandOrientationToleranceDegrees &&
                    drinking.BottleSipAxisErrorDegrees <= 5f &&
                    drinking.MeasuredTorsoLeanBackDegrees >=
                    Mathf.Max(0f, drinking.ExpectedSipTorsoLeanBackDegrees - 2.2f) &&
                    drinking.MeasuredHeadLeanBackDegrees >=
                    Mathf.Max(0f, drinking.ExpectedSipHeadLeanBackDegrees - 2.2f))
                {
                    if (patron.Anchor.Role == BarNpcRole.CounterPatron)
                    {
                        return patron.Presentation.AuthoredActionClip ==
                                   drinking.AuthoredDrinkClip &&
                               patron.Presentation.AuthoredActionWeight >=
                                   0.95f;
                    }

                    if (patron.Anchor.Role == BarNpcRole.StandingPatron)
                    {
                        return drinking.IsTableLean;
                    }

                    return patron.Presentation.IsSeated && drinking.IsSeatedTable;
                }
            }

            return false;
        }

        private static string DescribeBarDrinkMoments()
        {
            BarInteriorRoot bar = Object.FindAnyObjectByType<BarInteriorRoot>();
            var detail = new System.Text.StringBuilder();
            foreach (BarPatron patron in bar.Patrons)
            {
                BarPatronDrinkingArmPose pose = patron.Drinking;
                if (pose == null) continue;
                detail.Append($"\n{patron.Anchor.Id}/{pose.DrinkProp?.Kind}: {pose.Timeline.Phase}, " +
                    $"phaseSeconds={pose.Timeline.PhaseElapsed:F3}, dt={Time.deltaTime:F4}, paused={GameTimeScaleRuntime.IsPaused}, " +
                    $"drinks={pose.Timeline.CompletedDrinks}, base={pose.BottleSurfaceContactError:F4}, " +
                    $"upright={pose.BottleRestUprightErrorDegrees:F2}, grip={pose.BottleGripError:F4}, " +
                    $"roll={pose.BottleHandOrientationErrorDegrees:F2}, mouth={pose.BottleMouthDistance:F4}, axis={pose.BottleSipAxisErrorDegrees:F2}, support={pose.TableSupportError:F4}, " +
                    $"head={pose.RestHeadMotionDegrees:F2}; {DescribeBarSupportPose(patron)}");
            }

            return detail.ToString();
        }

        private static bool IsBarDrinkRestingOnSurface(string anchorId)
        {
            BarInteriorRoot bar =
                Object.FindAnyObjectByType<BarInteriorRoot>();
            if (bar == null || bar.Patrons == null)
            {
                return false;
            }

            for (int index = 0; index < bar.Patrons.Count; index++)
            {
                BarPatron patron = bar.Patrons[index];
                BarPatronDrinkingArmPose drinking = patron.Drinking;
                if (!string.Equals(
                        patron.Anchor.Id,
                        anchorId,
                        StringComparison.Ordinal) ||
                    drinking == null)
                {
                    continue;
                }

                return drinking.Timeline.Phase ==
                           BarPatronDrinkPhase.Rest &&
                       drinking.Timeline.CompletedDrinks > 0 &&
                       drinking.BottleSurfaceContactError <=
                           BarPatronDrinkingArmPose
                               .BottleSurfaceContactTolerance &&
                       drinking.BottleRestUprightErrorDegrees <=
                           BarPatronDrinkingArmPose
                               .BottleRestUprightToleranceDegrees &&
                       drinking.BottleGripError <=
                           BarPatronDrinkingArmPose
                               .BottleGripContactTolerance &&
                       drinking.BottleHandOrientationErrorDegrees <=
                           BarPatronDrinkingArmPose
                               .BottleHandOrientationToleranceDegrees &&
                       drinking.TableSupportError <=
                           BarPatronDrinkingArmPose
                               .TableSupportContactTolerance &&
                       drinking.RestHeadMotionDegrees >= 0.35f;
            }

            return false;
        }

        private static void AssertBarPatronPlacement()
        {
            BarInteriorRoot bar =
                Object.FindAnyObjectByType<BarInteriorRoot>();
            Assert.That(bar, Is.Not.Null);
            Assert.That(bar.Patrons, Has.Count.EqualTo(8));

            int boothCount = 0;
            int counterCount = 0;
            int tableCount = 0;
            var appearances = new HashSet<string>(StringComparer.Ordinal);
            var occupiedSofas = new HashSet<Transform>();
            var vesselKinds = new HashSet<BarPatronDrinkPropKind>();
            for (int index = 0; index < bar.Patrons.Count; index++)
            {
                BarPatron patron = bar.Patrons[index];
                Assert.That(patron.Registry, Is.Not.Null);
                Assert.That(patron.Presentation, Is.Not.Null);
                Assert.That(DefaultNpcCatalog.ModelIds,
                    Does.Contain(patron.Registry.DesignId), patron.Anchor.Id);
                Assert.That(patron.Registry.SourceGeneratorVersion,
                    Is.EqualTo(CityPedestrianDefaultNpcBody.SourceGeneratorVersion));
                Assert.That(patron.Registry.GetComponent<CityPedestrianDefaultNpcBody>(),
                    Is.Not.Null, patron.Anchor.Id);
                Assert.That(appearances.Add(patron.Registry.BuildSignature), Is.True,
                    "The visible visit cast repeats an appearance: " + patron.Anchor.Id);
                Assert.That(
                    patron.Registry.DesignId,
                    Is.Not.EqualTo(
                        CityPedestrianResources.BabushkaDesignId),
                    patron.Anchor.Id);

                Assert.That(patron.Drinking, Is.Not.Null, patron.Anchor.Id);
                Assert.That(patron.Drinking.DrinkProp, Is.Not.Null, patron.Anchor.Id);
                vesselKinds.Add(patron.Drinking.DrinkProp.Kind);
                if (patron.Drinking != null)
                {
                    BarInteriorSpawnPlayModeTests.AssertHandWrap(
                        patron.Drinking.HandPose, false,
                        patron.Drinking.DrinkGripWorldPosition,
                        patron.Drinking.DrinkOpeningDirection, patron.Anchor.Id);
                    BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                        patron.Drinking.HandPose, false, patron.Drinking.BottleRoot, patron.Anchor.Id);
                }

                Vector3 expectedRoot =
                    bar.transform.TransformPoint(patron.Anchor.Position);
                if (patron.Anchor.Role == BarNpcRole.SeatedPatron ||
                    patron.Anchor.Role == BarNpcRole.CounterPatron)
                {
                    bool counter =
                        patron.Anchor.Role == BarNpcRole.CounterPatron;
                    if (counter)
                    {
                        counterCount++;
                        Assert.That(patron.Drinking.DrinkProp.Kind, Is.Not.EqualTo(BarPatronDrinkPropKind.Bottle),
                            "Counter clips use the typed vessel substitution: " + patron.Anchor.Id);
                    }
                    else
                    {
                        boothCount++;
                    }

                    Assert.That(patron.IsSeated, Is.True, patron.Anchor.Id);
                    Assert.That(
                        patron.Presentation.IsSeated,
                        Is.True,
                        patron.Anchor.Id);
                    Assert.That(
                        patron.Presentation.SeatAnchor,
                        Is.Not.Null,
                        patron.Anchor.Id);
                    Assert.That(
                        CityPedestrianResources.TryGetArchetype(
                            patron.Registry.DesignId,
                            out CityPedestrianArchetype archetype),
                        Is.True,
                        patron.Anchor.Id);
                    Assert.That(
                        archetype.SeatedRide,
                        Is.Not.Null,
                        patron.Anchor.Id);
                    float seatTop = counter
                        ? BarPatronWorldBuilder.CounterSeatHeight
                        : BarPatronWorldBuilder.BoothSeatHeight;
                    Assert.That(
                        patron.Presentation.SeatAnchor.position.y,
                        Is.EqualTo(
                                bar.transform.TransformPoint(
                                    patron.Anchor.Position +
                                    Vector3.up * seatTop).y)
                            .Within(0.002f),
                        patron.Anchor.Id);

                    Vector3 pelvis = patron.Registry.PelvisAnchor.position;
                    float expectedPelvisHeight =
                        patron.Presentation.SeatAnchor.position.y +
                        archetype.SeatedRide.SeatLift;
                    Assert.That(
                        pelvis.y,
                        Is.EqualTo(expectedPelvisHeight).Within(0.012f),
                        $"{patron.Anchor.Id} pelvis is vertically detached " +
                        "from its concrete seat top.");
                    float horizontalError = Vector2.Distance(
                        new Vector2(pelvis.x, pelvis.z),
                        new Vector2(expectedRoot.x, expectedRoot.z));
                    Assert.That(
                        horizontalError,
                        Is.LessThanOrEqualTo(0.035f),
                        $"{patron.Anchor.Id} pelvis misses its seat by " +
                        $"{horizontalError:F3} m.");

                    if (counter)
                    {
                        Assert.That(patron.Drinking, Is.Not.Null);
                        Assert.That(
                            patron.Drinking.AuthoredDrinkClip.name,
                            Does.Contain("CafeManDrink"));
                        Assert.That(
                            patron.Drinking.IsTableLean,
                            Is.False);
                        AssertBarSurfaceRestTargets(bar, patron);
                        AssertTypedBarSip(patron);
                    }
                    else
                    {
                        Assert.That(patron.Drinking, Is.Not.Null, patron.Anchor.Id);
                        Assert.That(patron.Drinking.IsSeatedTable, Is.True, patron.Anchor.Id);
                        Assert.That(patron.Drinking.IsTableLean, Is.True, patron.Anchor.Id);
                        Assert.That(patron.Presentation.AuthoredActionWeight, Is.LessThan(.001f),
                            "A sofa drink must preserve its shared seated lower body.");
                        AssertBarSurfaceRestTargets(bar, patron);
                        AssertTypedBarSip(patron);
                        string[] seatId = patron.Anchor.Id.Split('-');
                        string side = seatId[2] == "near" ? "Near" : "Far";
                        Transform cushion = bar.Room.Find($"Booth Cushion {seatId[1]} {side}");
                        Assert.That(cushion, Is.Not.Null, patron.Anchor.Id);
                        Assert.That(occupiedSofas.Add(cushion), Is.True,
                            "Each sofa has exactly one guest: " + patron.Anchor.Id);
                        Bounds support = cushion.GetComponent<Renderer>().bounds;
                        Assert.That(pelvis.x, Is.InRange(support.min.x, support.max.x), patron.Anchor.Id);
                        Assert.That(pelvis.z, Is.InRange(support.min.z, support.max.z), patron.Anchor.Id);
                        Assert.That(support.max.y, Is.EqualTo(
                            patron.Presentation.SeatAnchor.position.y).Within(.002f), patron.Anchor.Id);
                        Vector3 facing = Quaternion.Euler(0f, patron.Anchor.YawDegrees, 0f) * Vector3.forward;
                        Assert.That(Vector3.Dot(facing, side == "Near" ? Vector3.forward : Vector3.back),
                            Is.GreaterThan(.999f), "Sofas must face each other across the table.");
                    }

                    continue;
                }

                Assert.That(
                    patron.Anchor.Role,
                    Is.EqualTo(BarNpcRole.StandingPatron));
                tableCount++;
                Assert.That(
                    patron.Registry.transform.position.y,
                    Is.EqualTo(expectedRoot.y).Within(0.002f),
                    patron.Anchor.Id);
                Assert.That(patron.IsSeated, Is.False);
                Assert.That(
                    patron.Registry.DesignId,
                    Is.Not.EqualTo(
                        CityPedestrianResources.ChessPlayerDesignId),
                    $"{patron.Anchor.Id} must not borrow a chess-table " +
                    "silhouette for a standing pub pose.");
                Assert.That(
                    patron.Registry.DesignId,
                    Is.Not.EqualTo(
                        CityPedestrianResources.CheckersPlayerDesignId),
                    $"{patron.Anchor.Id} must not borrow a checkers-table " +
                    "silhouette for a standing pub pose.");
                Assert.That(patron.Drinking, Is.Not.Null);
                Assert.That(patron.Drinking.IsTableLean, Is.True);
                AssertBarSurfaceRestTargets(bar, patron);
                AssertTypedBarSip(patron);
            }

            // Counter clips replace Bottle with ShotGlass. If the only
            // allocated bottle lands at a counter, four forms are correct.
            Assert.That(vesselKinds, Is.SupersetOf(new[]
            {
                BarPatronDrinkPropKind.Pint, BarPatronDrinkPropKind.WineGlass,
                BarPatronDrinkPropKind.ShotGlass, BarPatronDrinkPropKind.Snifter
            }));
            Assert.That(vesselKinds.Count, Is.InRange(4, 5));
            Assert.That(boothCount, Is.EqualTo(4));
            Assert.That(occupiedSofas.Count, Is.EqualTo(4));
            Assert.That(counterCount, Is.EqualTo(2));
            Assert.That(tableCount, Is.EqualTo(2));
        }

        private static string DescribeBarSupportPose(BarPatron patron)
        {
            var description = new System.Text.StringBuilder();
            description.Append("target=").Append(patron.Drinking.TableSupportPoint)
                .Append(" owner=").Append(patron.Registry.transform.position)
                .Append(" model=").Append(patron.Registry.ModelRoot.position)
                .Append(" rotation=").Append(patron.Registry.ModelRoot.eulerAngles);
            foreach (Transform bone in
                     patron.Registry.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "SOCKET_Vessel.L" ||
                    bone.name == "clavicle.L" ||
                    bone.name == "upper_arm.L" ||
                    bone.name == "forearm.L" || bone.name == "hand.L")
                {
                    description.Append(' ').Append(bone.name)
                        .Append('=').Append(bone.position.ToString("F3"));
                }
            }

            return description.ToString();
        }

        private static void AssertTypedBarSip(BarPatron patron)
        {
            BarPatronDrinkingArmPose pose = patron.Drinking;
            if (pose.Timeline.Phase != BarPatronDrinkPhase.Sip || pose.Timeline.VesselTipWeight < .95f) return;
            string label = patron.Anchor.Id + "/" + pose.DrinkProp.Kind;
            Assert.That(pose.BottleMouthDistance, Is.LessThanOrEqualTo(.015f), label + " rim misses mouth");
            Assert.That(pose.BottleGripError, Is.LessThanOrEqualTo(.015f), label + " grip misses hand");
            Assert.That(pose.BottleHandOrientationErrorDegrees, Is.LessThanOrEqualTo(1f), label + " palm faces away");
            Assert.That(pose.BottleSipAxisErrorDegrees, Is.LessThanOrEqualTo(5f), label + " wrong typed tilt");
            Assert.That(Mathf.Abs(Vector3.Angle(pose.DrinkOpeningDirection, patron.Registry.transform.up) - pose.SipTiltDegrees),
                Is.LessThanOrEqualTo(5f), label + " wrong vessel opening direction");
            Assert.That(pose.MeasuredTorsoLeanBackDegrees,
                Is.GreaterThanOrEqualTo(Mathf.Max(0f, pose.ExpectedSipTorsoLeanBackDegrees - 2.2f)), label);
            Assert.That(pose.MeasuredHeadLeanBackDegrees,
                Is.GreaterThanOrEqualTo(Mathf.Max(0f, pose.ExpectedSipHeadLeanBackDegrees - 2.2f)), label);
        }

        private static void AssertBarSurfaceRestTargets(BarInteriorRoot bar, BarPatron patron)
        {
            BarPatronDrinkingArmPose pose = patron.Drinking;
            Assert.That(pose.SurfaceRenderer, Is.Not.Null, patron.Anchor.Id);
            Bounds top = pose.SurfaceRenderer.bounds;
            Assert.That(pose.BottleRestPoint.y, Is.EqualTo(top.max.y + .002f).Within(.002f), patron.Anchor.Id);
            Assert.That(pose.TableSupportPoint.y, Is.EqualTo(top.max.y + .015f).Within(.002f), patron.Anchor.Id);
            Assert.That(pose.BottleRestPoint.x, Is.InRange(top.min.x, top.max.x), patron.Anchor.Id);
            Assert.That(pose.BottleRestPoint.z, Is.InRange(top.min.z, top.max.z), patron.Anchor.Id);
            if (pose.Timeline.Phase != BarPatronDrinkPhase.Rest) return;
            Assert.That(pose.TableSupportError, Is.LessThanOrEqualTo(.04f),
                patron.Anchor.Id + " support misses table: " + DescribeBarSupportPose(patron));
            Assert.That(pose.BottleSurfaceContactError, Is.LessThanOrEqualTo(.025f), patron.Anchor.Id);
            Assert.That(pose.BottleRestUprightErrorDegrees, Is.LessThanOrEqualTo(2f), patron.Anchor.Id);
            Assert.That(pose.BottleGripError, Is.LessThanOrEqualTo(.015f), patron.Anchor.Id);
            Assert.That(pose.BottleHandOrientationErrorDegrees, Is.LessThanOrEqualTo(1f), patron.Anchor.Id);
            Bounds local = pose.DrinkProp.PhysicalLocalBounds;
            float minimumY = float.PositiveInfinity;
            bool round = patron.Anchor.Role == BarNpcRole.StandingPatron;
            for (int index = 0; index < 8; index++)
            {
                Vector3 point = local.center + Vector3.Scale(local.extents,
                    new Vector3((index & 1) == 0 ? -1f : 1f,
                        (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                Vector3 world = pose.BottleRoot.position + pose.BottleRoot.rotation * point;
                minimumY = Mathf.Min(minimumY, world.y);
                Assert.That(world.x, Is.InRange(top.min.x - .002f, top.max.x + .002f), patron.Anchor.Id);
                Assert.That(world.z, Is.InRange(top.min.z - .002f, top.max.z + .002f), patron.Anchor.Id);
                if (round)
                    Assert.That(Vector2.Distance(new Vector2(world.x, world.z), new Vector2(top.center.x, top.center.z)),
                        Is.LessThanOrEqualTo(Mathf.Min(top.extents.x, top.extents.z) + .002f), patron.Anchor.Id);
            }
            Assert.That(minimumY, Is.EqualTo(top.max.y + .002f).Within(.003f),
                patron.Anchor.Id + " vessel penetrates or floats above the table");
        }

        private static IEnumerator VerifyAllBarDrinkCycles(BarInteriorRoot bar)
        {
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            var sips = new bool[bar.Patrons.Count];
            var rests = new bool[bar.Patrons.Count];
            bool complete = false;
            Action sample = () =>
            {
                complete = true;
                for (int index = 0; index < bar.Patrons.Count; index++)
                {
                    BarPatron patron = bar.Patrons[index];
                    BarPatronDrinkingArmPose pose = patron.Drinking;
                    bool sip = pose.Timeline.Phase == BarPatronDrinkPhase.Sip && pose.Timeline.VesselTipWeight >= .95f;
                    bool rest = pose.Timeline.Phase == BarPatronDrinkPhase.Rest && pose.Timeline.CompletedDrinks > 0;
                    if ((sip && !sips[index]) || (rest && !rests[index]))
                    {
                        AssertTypedBarSip(patron);
                        AssertBarSurfaceRestTargets(bar, patron);
                        BarInteriorSpawnPlayModeTests.AssertHandWrap(pose.HandPose, false,
                            pose.DrinkGripWorldPosition, pose.DrinkOpeningDirection, patron.Anchor.Id + "/" + pose.DrinkProp.Kind);
                        BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                            pose.HandPose, false, pose.BottleRoot, patron.Anchor.Id + "/" + pose.DrinkProp.Kind);
                        if (sip) sips[index] = true;
                        if (rest) rests[index] = true;
                    }
                    complete &= sips[index] && rests[index];
                }
            };
            float deadline = Time.realtimeSinceStartup + 25f;
            while (!complete && Time.realtimeSinceStartup < deadline)
                yield return AtWarehouseFridgePresentation(probe, sample);
            Object.Destroy(probe);
            Assert.That(complete, Is.True, "Every visitor must drink and return the same prop: " + DescribeBarDrinkMoments());
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
        [PrebuildSetup(typeof(CityWorldAuditAssetsSetup))]
        public IEnumerator City()
        {
            GameSessionState.BeginNewGame("World location audit");
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            CityGameRoot city = null;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () => CityLocationAuditShots(city));
            }
            finally
            {
                GameSessionState.BeginNewGame("World location audit cleanup");
            }
        }

        [UnityTest]
        [Explicit("Focused shoreline continuity, sand treading and advancing/retreating shore captures.")]
        public IEnumerator CityShoreSwash()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));

            CityGameRoot city = null;
            yield return Capture(
                SceneIds.City,
                () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                },
                () => CityShoreSwashShots(city));
        }

        private static Shot[] CityShoreSwashShots(CityGameRoot city)
        {
            CitySeacoastPlan coast = city.World.SeacoastPlan;
            Assert.That(coast, Is.Not.Null);
            GameObject coastRoot = GameObject.Find(CitySeacoastWorldBuilder.RootName);
            Assert.That(coastRoot, Is.Not.Null);
            Transform sea = coastRoot.transform.Find("Sea");
            Assert.That(sea, Is.Not.Null);

            int swashCount = 0;
            Material sharedSwash = null;
            int swashParamsId = Shader.PropertyToID("_ShoreSwashParams");
            foreach (Renderer renderer in sea.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.name.StartsWith("Sea Swash ", StringComparison.Ordinal))
                {
                    continue;
                }

                swashCount++;
                Assert.That(renderer.sharedMaterial, Is.Not.Null, renderer.name);
                if (sharedSwash == null) sharedSwash = renderer.sharedMaterial;
                Assert.That(renderer.sharedMaterial, Is.SameAs(sharedSwash),
                    "Every swash chunk must reuse the one sea swash material.");
                Assert.That(renderer.sharedMaterial.shader,
                    Is.SameAs(CitySeaResources.WaterMaterial.shader));
                Assert.That(renderer.sharedMaterial.GetVector(swashParamsId).w,
                    Is.EqualTo(1f).Within(0.0001f), renderer.name);
                Assert.That(renderer.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "A cosmetic wash must not block the player's feet.");
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null, renderer.name);
                Assert.That(filter.sharedMesh, Is.Not.Null, renderer.name);
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(4), renderer.name);
            }
            Assert.That(swashCount, Is.GreaterThan(0), "The shore has no visible swash meshes.");
            Assert.That(CitySeaResources.WaterMaterial.GetVector(swashParamsId).w,
                Is.EqualTo(0f).Within(0.0001f),
                "The ordinary sea sheet must keep its existing water path.");

            CitySeacoastFrame frame = coast.Frame;
            CitySandTreading treading = AssertCityShoreSandMeshes(city, sea);
            Mesh visualSand = treading.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider sandCollider = treading.GetComponent<MeshCollider>();
            Vector3[] originalSandVertices = visualSand.vertices;
            Vector3[] originalSandNormals = visualSand.normals;
            Vector3[] fixedCollisionVertices = sandCollider.sharedMesh.vertices;
            float x = frame.EastZone.xMin + Mathf.Min(16f, frame.EastZone.width * 0.18f);
            Assert.That(sharedSwash.GetVector(swashParamsId).x,
                Is.EqualTo(frame.WaterlineZ).Within(0.001f));
            // The beach rises inland. The waterline datum puts an eye
            // five metres inland inside the slope on the elevated city.
            Vector3 SandPoint(float pointX, float pointZ, float lift)
            {
                Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(
                    city.Layout, new Vector2(pointX, pointZ), out float top, out _),
                    Is.True);
                return new Vector3(pointX, top + lift, pointZ);
            }
            Vector3 from = SandPoint(x, frame.WaterlineZ - 5.6f, EyeHeight);
            Vector3 target = SandPoint(x, frame.WaterlineZ - 0.8f, 0.02f);
            var treadPath = new List<Vector3>();
            Vector3 treadSpot = default;
            float depthBefore = 0f;
            // Use an actual clear inland passage. The beach's authored
            // props must never be treated as sand merely by their XZ.
            for (int candidate = 0; candidate < 20 && treadPath.Count == 0; candidate++)
            {
                float pathX = x + (candidate % 2 == 0 ? 1f : -1f) * (candidate / 2) * 0.8f;
                var proposed = new List<Vector3>();
                for (float inland = 9f; inland <= 12.001f; inland += 0.1f)
                {
                    Vector3 point = SandPoint(pathX, frame.WaterlineZ - inland, 0f);
                    if (!Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down,
                            out RaycastHit contact, 1.2f, Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore) || contact.collider != sandCollider)
                        break;
                    proposed.Add(contact.point);
                }
                if (proposed.Count < 30) continue;
                Vector3 middle = proposed[proposed.Count / 2];
                float loose = treading.SampleVisibleDepth(middle);
                if (loose <= 0.03f) continue;
                treadPath = proposed;
                treadSpot = middle;
                depthBefore = loose;
            }
            Assert.That(treadPath.Count, Is.GreaterThanOrEqualTo(30),
                "No clear loose-sand passage was found for the walking proof.");
            Vector3 kickSpot = default;
            float kickDepth = 0f;
            for (int candidate = 0; candidate < 12 && kickDepth <= 0f; candidate++)
            {
                float offset = (candidate % 2 == 0 ? 1f : -1f) *
                               (0.9f + candidate / 2 * 0.4f);
                Vector3 point = SandPoint(treadSpot.x + offset, treadSpot.z, 0f);
                if (!Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down,
                        out RaycastHit contact, 1.2f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore) || contact.collider != sandCollider)
                    continue;
                float loose = treading.SampleVisibleDepth(contact.point);
                if (loose <= 0.03f) continue;
                kickSpot = contact.point;
                kickDepth = loose;
            }
            Assert.That(kickDepth, Is.GreaterThan(0.03f),
                "The grain shot needs fresh loose sand beside the compacted trail.");
            Assert.That(coast.TryGetPart(CitySeacoastPlanner.PierDeckRootId,
                out CitySeacoastPartDescriptor pier), Is.True);
            Assert.That(treading.TryPlayFootstep(
                    pier.Center + Vector3.up * (pier.Size.y * 0.5f + 0.02f), 0f),
                Is.False, "A wooden pier must not kick up the sand underneath it.");
            Rect road = city.Layout.GetRoadRect(city.Layout.RoadEdges[0]);
            Assert.That(treading.TryPlayFootstep(
                    new Vector3(road.center.x, 0f, road.center.y), 0f),
                Is.False, "Ordinary city roads must retain their own footstep fallback.");

            bool pressed = false;
            bool verifiedTrail = false;
            float pressTime = 0f;
            bool AfterTreading()
            {
                if (!pressed)
                {
                    foreach (Vector3 point in treadPath) treading.Press(point);
                    pressTime = Time.time;
                    pressed = true;
                }
                if (Time.time - pressTime < CitySandTreading.RebuildInterval * 1.5f)
                    return false;
                if (!verifiedTrail)
                {
                    Assert.That(treading.SampleVisibleDepth(treadSpot),
                        Is.LessThan(depthBefore * 0.75f),
                        "A walking pass must visibly compact the loose surface.");
                    Vector3[] deformed = visualSand.vertices;
                    Vector3[] normals = visualSand.normals;
                    float greatestDepression = 0f;
                    for (int index = 0; index < deformed.Length; index++)
                    {
                        greatestDepression = Mathf.Max(greatestDepression,
                            originalSandVertices[index].y - deformed[index].y);
                        if (Mathf.Abs(deformed[index].z - frame.WaterlineZ) < 0.001f)
                            Assert.That(Vector3.Dot(normals[index], originalSandNormals[index]),
                                Is.GreaterThan(0.9999f),
                                "An inland print must preserve the shoreline's matched normals.");
                    }
                    Assert.That(greatestDepression, Is.GreaterThan(0.01f));
                    CollectionAssert.AreEqual(fixedCollisionVertices, sandCollider.sharedMesh.vertices,
                        "Sand treading must leave the fixed physical ground unchanged.");
                    verifiedTrail = true;
                }
                return true;
            }
            bool kicked = false;
            bool KickSand()
            {
                if (kicked) return true;
                Assert.That(treading.TryPlayFootstep(kickSpot, 0f), Is.True,
                    "An actual loose-sand contact must own its step and grains.");
                Transform kickup = treading.transform.Find("Sand Kickup");
                Assert.That(kickup, Is.Not.Null);
                foreach (Collider pendingCollider in kickup.GetComponentsInChildren<Collider>())
                    Assert.That(pendingCollider.enabled, Is.False,
                        "A grain must be nonphysical even before deferred collider removal.");
                Assert.That(kickup.GetComponentsInChildren<Renderer>().Length,
                    Is.EqualTo(AlpineVillageSnowKickup.MoteCount));
                kicked = true;
                return true;
            }
            Vector3 trailTarget = treadSpot + Vector3.up * depthBefore;
            Vector3 trailEye = trailTarget + new Vector3(1.4f, 2.6f, -2.5f);
            Vector3 kickTarget = kickSpot + Vector3.up * kickDepth;
            float startTime = -1f;
            Func<bool> AtElapsed(float seconds)
            {
                return () =>
                {
                    if (startTime < 0f) startTime = Time.time;
                    return Time.time >= startTime + seconds;
                };
            }

            // Keep the real shader clock: these are seconds elapsed from the
            // first shot, after settling, not invented absolute wave phases.
            // The same close view exposes the changing water reach on sand.
            return new[]
            {
                Shot.At("shore-swash-00s", from, target, 62f, readyWhen: AtElapsed(0f)),
                Shot.At("shore-swash-02s", from, target, 62f, readyWhen: AtElapsed(2f)),
                Shot.At("shore-swash-04s", from, target, 62f, readyWhen: AtElapsed(4f)),
                Shot.At("shore-swash-06s", from, target, 62f, readyWhen: AtElapsed(6f)),
                Shot.At("shore-swash-oblique",
                    SandPoint(x + 8f, frame.WaterlineZ - 4.4f, EyeHeight), target, 62f),
                Shot.At("shore-swash-overhead",
                    target + new Vector3(0f, 8f, -1f), target, 62f),
                Shot.At("shore-sand-before", trailEye, trailTarget, 54f),
                Shot.At("shore-sand-after", trailEye, trailTarget, 54f, readyWhen: AfterTreading),
                Shot.At("shore-sand-kickup",
                    kickTarget + new Vector3(0.65f, 0.8f, -1.25f), kickTarget, 48f,
                    readyWhen: KickSand)
            };
        }

        private static CitySandTreading AssertCityShoreSandMeshes(CityGameRoot city, Transform sea)
        {
            CitySandTreading[] sandComponents =
                city.World.Root.GetComponentsInChildren<CitySandTreading>();
            Assert.That(sandComponents.Length, Is.EqualTo(1));
            CitySandTreading treading = sandComponents[0];
            Assert.That(treading.name, Is.EqualTo("Beach"));
            MeshFilter beachFilter = treading.GetComponent<MeshFilter>();
            MeshCollider beachCollider = treading.GetComponent<MeshCollider>();
            Assert.That(beachFilter, Is.Not.Null);
            Assert.That(beachCollider, Is.Not.Null);
            Assert.That(beachFilter.sharedMesh, Is.Not.SameAs(beachCollider.sharedMesh),
                "Loose sand must deform a visual mesh independently of its fixed collider.");
            Transform bed = sea.Find("Sea Bed Slope");
            Assert.That(bed, Is.Not.Null, "The sand has no underwater continuation.");
            Assert.That(bed.GetComponentsInChildren<Collider>(), Is.Empty);
            Renderer dryRenderer = treading.GetComponent<Renderer>();
            Renderer bedRenderer = bed.GetComponent<Renderer>();
            Assert.That(bedRenderer.sharedMaterial, Is.SameAs(dryRenderer.sharedMaterial));
            var dryProperties = new MaterialPropertyBlock();
            var bedProperties = new MaterialPropertyBlock();
            dryRenderer.GetPropertyBlock(dryProperties);
            bedRenderer.GetPropertyBlock(bedProperties);
            int mapId = Shader.PropertyToID("_BaseMap");
            int colorId = Shader.PropertyToID("_BaseColor");
            // The City presentation replaces the builders' ingredient sheet
            // with the manifest-owned albedo and packed ground response.
            CitySurfaceSheet sandSheet = null;
            foreach (CitySurfaceSheet sheet in CitySurfaceLibrary.Manifest.sheets)
                if (sheet.family == "CitySeacoastSand" &&
                    sheet.source == CitySeacoastSurfaceAppearance.SandTextureResourcePath)
                {
                    Assert.That(sandSheet, Is.Null, "The sand source must have one City surface owner.");
                    sandSheet = sheet;
                }
            Assert.That(sandSheet, Is.Not.Null);
            Texture expectedSand = Resources.Load<Texture2D>(sandSheet.albedo);
            Texture expectedResponse = Resources.Load<Texture2D>(sandSheet.ground_response);
            Assert.That(expectedResponse, Is.Not.Null);
            Texture bedSand = bedProperties.GetTexture(mapId);
            Texture drySand = dryProperties.GetTexture(mapId);
            Assert.That(expectedSand, Is.Not.Null);
            Assert.That(bedSand, Is.Not.Null);
            Assert.That(drySand, Is.Not.Null);
            // Native property-block getters may return another managed wrapper.
            // Require the same native texture, not merely the same asset name.
            Assert.That(bedSand.GetEntityId(), Is.EqualTo(expectedSand.GetEntityId()),
                "Seabed/resource native sand identity diverged: bed=" + bedSand.GetEntityId() +
                ", resource=" + expectedSand.GetEntityId() + ", dry=" + drySand.GetEntityId());
            Assert.That(bedSand.GetEntityId(), Is.EqualTo(drySand.GetEntityId()),
                "Both sand skins must reuse the same native texture.");
            int responseId = Shader.PropertyToID("_GroundResponse");
            Texture bedResponse = bedProperties.GetTexture(responseId);
            Texture dryResponse = dryProperties.GetTexture(responseId);
            Assert.That(bedResponse, Is.Not.Null);
            Assert.That(dryResponse, Is.Not.Null);
            Assert.That(bedResponse.GetEntityId(), Is.EqualTo(expectedResponse.GetEntityId()),
                "The seabed must retain the exact manifest-owned packed ground response.");
            Assert.That(dryResponse.GetEntityId(), Is.EqualTo(expectedResponse.GetEntityId()),
                "The dry sand must retain the same packed ground response.");
            Assert.That(bedProperties.GetColor(colorId), Is.EqualTo(dryProperties.GetColor(colorId)),
                "The water, rather than a tint seam, must darken the submerged sand.");

            CitySeacoastFrame frame = city.World.SeacoastPlan.Frame;
            float tile = CitySeacoastSurfaceAppearance.GetRecipe(CitySeacoastSurfaceKind.Sand)
                .MetersPerTile;
            int shoreVertices = 0;
            int deepVertices = 0;
            foreach (Mesh mesh in new[] { beachFilter.sharedMesh, bed.GetComponent<MeshFilter>().sharedMesh })
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                Assert.That(uvs.Length, Is.EqualTo(vertices.Length));
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 vertex = vertices[index];
                    Assert.That(Vector2.Distance(uvs[index], new Vector2(vertex.x, vertex.z) / tile),
                        Is.LessThan(0.0001f), "Both sand skins must keep the same world UV phase.");
                    if (Mathf.Abs(vertex.z - frame.WaterlineZ) < 0.001f)
                    {
                        Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout,
                            new Vector2(vertex.x, vertex.z), out float top,
                            out CitySurfaceDescriptor surface), Is.True);
                        Assert.That(vertex.y, Is.EqualTo(top).Within(0.0001f),
                            "There is a vertical gap between beach and seabed.");
                        Assert.That(Vector3.Dot(normals[index], CityTerrainSurfacePlan.SampleNormal(
                                city.Layout, surface, new Vector2(vertex.x, vertex.z))),
                            Is.GreaterThan(0.9999f), "The joined sand needs matching shading normals.");
                        shoreVertices++;
                    }
                    if (Mathf.Abs(vertex.z - frame.WaterlineZ - CitySeacoastSeaLayout.SeabedReach) < 0.001f)
                    {
                        Assert.That(frame.SeaTopY - vertex.y,
                            Is.GreaterThan(CitySeaResources.DepthFadeDistance +
                                           CitySeaResources.WaveHeight * CitySeaResources.CrestFactor));
                        deepVertices++;
                    }
                }
            }
            Assert.That(shoreVertices, Is.GreaterThan(2));
            Assert.That(deepVertices, Is.GreaterThan(2));
            return treading;
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
            CityArchShelterPlan plan = cityRoot.World.ArchShelterPlan;
            Assert.That(plan.IsEnabled, Is.True);
            Assert.That(plan.ClearLanes, Is.Not.Empty);
            CityArchShelterClearLaneDescriptor lane = plan.ClearLanes[0];
            PlayerMotor motor = cityRoot.Player.Motor;
            CharacterController body = motor.GetComponent<CharacterController>();
            Vector3 savedPosition = motor.transform.position;
            Quaternion savedRotation = motor.transform.rotation;
            bool savedInput = motor.InputEnabled;
            var landing = new CityMapCityTeleportGround(cityRoot.Layout);
            Physics.SyncTransforms();
            Vector3 south = ArchFoot(lane.Footprint.yMin - .8f);
            Vector3 middle = ArchFoot(lane.Footprint.center.y);
            Vector3 north = ArchFoot(lane.Footprint.yMax + .8f);
            try
            {
                motor.SetInputEnabled(false);
                motor.Teleport(south + Vector3.up * PlayerFactory.GroundedRootOffset);
                yield return null;
                yield return WalkChurchGardenLeg(motor, middle, "arch-south-to-clear-lane");
                yield return WalkChurchGardenLeg(motor, north, "arch-clear-lane-to-north");
                yield return WalkChurchGardenLeg(motor, middle, "arch-north-to-clear-lane");
                yield return WalkChurchGardenLeg(motor, south, "arch-clear-lane-to-south");
                Debug.Log("ARCH ACTUAL MOTOR OK: south -> ClearLanes[0] -> north -> ClearLanes[0] -> south.");
            }
            finally
            {
                motor.CancelInteractionPoseMove();
                motor.Teleport(savedPosition);
                motor.transform.rotation = savedRotation;
                motor.SetInputEnabled(savedInput);
            }

            Vector3 ArchFoot(float z)
            {
                Vector2 xz = new Vector2(lane.Footprint.center.x, z);
                Assert.That(landing.TryResolveStandingPosition(xz, out Vector3 planned), Is.True);
                Assert.That(Vector2.Distance(xz, new Vector2(planned.x, planned.z)), Is.LessThan(.001f),
                    "The arch route must use its real clear lane, not a projected landing.");
                float expected = planned.y - PlayerFactory.GroundedRootOffset;
                RaycastHit? support = null;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(xz.x, expected + 1f, xz.y),
                    Vector3.down, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.normal.y < .7f || Mathf.Abs(hit.point.y - expected) > .25f ||
                        hit.collider.transform.IsChildOf(cityRoot.Player.GameObject.transform) ||
                        hit.collider.GetComponentInParent<DefaultNpcAppearance>() != null) continue;
                    if (!support.HasValue || hit.point.y > support.Value.point.y) support = hit;
                }
                Assert.That(support.HasValue, Is.True, "The actual arch route lacks supporting ground at " + xz);
                Vector3 foot = support.Value.point;
                Assert.That(cityRoot.World.WalkableArea.Contains(foot, body.radius), Is.True);
                AssertLocalClearColumn(cityRoot, foot, body.radius, "arch clear lane");
                return foot;
            }
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
        [Explicit("Church paving overlap regression and moving-camera captures only.")]
        public IEnumerator CityChurchCourtyardSurfaces()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City,
                () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    ValidateChurchPavingSurfaces(city);
                    var shots = new List<Shot>(ChurchGardenShots(city));
                    CityChurchCourtyardPlan garden = city.World.ChurchCourtyardPlan;
                    Vector3 origin = new Vector3(garden.Grounds.xMin,
                        garden.GroundTopY, garden.Grounds.yMin);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 shift = Vector3.right * (i * 0.08f);
                        shots.Add(Shot.At($"church-paving-motion-{i}",
                            origin + new Vector3(33f, EyeHeight, 29f) + shift,
                            origin + new Vector3(37f, 0f, 25.5f) + shift, 65f));
                    }
                    return shots.ToArray();
                });
        }

        private static void ValidateChurchPavingSurfaces(CityGameRoot city)
        {
            CityChurchCourtyardPlan plan = city.World.ChurchCourtyardPlan;
            IReadOnlyList<CityChurchCourtyardSurfaceDescriptor> visible =
                CityChurchCourtyardWorldBuilder.CreateVisibleSurfaces(plan);
            Rect threshold = CityChurchWorldBuilder.GetDoorThresholdBounds(
                city.World.ChurchPlan.DoorGroundPosition);
            for (int i = 0; i < visible.Count; i++)
            {
                Assert.That(ChurchSurfaceOverlap(visible[i].Bounds, threshold), Is.False,
                    "Paving competes with the door threshold: " + visible[i].Id);
                for (int j = 0; j < i; j++)
                    Assert.That(ChurchSurfaceOverlap(visible[i].Bounds, visible[j].Bounds), Is.False,
                        "Coplanar paving: " + visible[i].Id + " / " + visible[j].Id);
            }

            GameObject ground = GameObject.Find(CityChurchGroundWorldBuilder.ObjectName);
            Mesh skin = ground.GetComponent<MeshFilter>().sharedMesh;
            CityGroundSurfaceSystem common = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            Assert.That(common, Is.Not.Null);
            Assert.That(ground.GetComponent<MeshCollider>().enabled, Is.False,
                "The common ground sectors own physical collision after composition.");
            Transform sectors = common.transform.Find("City Ground");
            Assert.That(sectors, Is.Not.Null);
            MeshCollider[] collision = sectors.GetComponentsInChildren<MeshCollider>();
            Assert.That(collision, Is.Not.Empty);
            Transform churchRoot = city.World.Church.transform.parent;
            MeshCollider thresholdSupport = churchRoot.Find(CityChurchWorldBuilder.ThresholdCollisionName)
                .GetComponent<MeshCollider>();
            Assert.That(thresholdSupport.enabled, Is.True);
            Assert.That(thresholdSupport.GetComponent<Renderer>(), Is.Null,
                "Only the imported model presents the sill.");
            CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(city.Layout);
            Vector3[] vertices = skin.vertices;
            int[] triangles = skin.triangles;
            foreach (CityChurchCourtyardSurfaceDescriptor surface in plan.Surfaces)
            {
                if (surface.Kind == CityChurchCourtyardSurfaceKind.Lawn) continue;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = ground.transform.TransformPoint(vertices[triangles[t]]);
                    Vector3 b = ground.transform.TransformPoint(vertices[triangles[t + 1]]);
                    Vector3 c = ground.transform.TransformPoint(vertices[triangles[t + 2]]);
                    if (Vector3.Cross(b - a, c - a).y <= 0f) continue;
                    Rect face = Rect.MinMaxRect(
                        Mathf.Min(a.x, Mathf.Min(b.x, c.x)),
                        Mathf.Min(a.z, Mathf.Min(b.z, c.z)),
                        Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                        Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                    if (!ChurchSurfaceOverlap(face, surface.Bounds)) continue;
                    // Curved streets clip terrain into real polygons. Their
                    // triangle AABB can cross a paving edge without any face
                    // crossing it, so measure the clipped footprint itself.
                    IReadOnlyList<Vector2> clipped = new[] {
                        new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z) };
                    Vector2[] rectangle = CityRoadPolygon.Rectangle(surface.Bounds);
                    for (int edge = 0; edge < rectangle.Length && clipped.Count >= 3; edge++)
                        clipped = CityRoadPolygon.Clip(clipped, rectangle[edge], rectangle[(edge + 1) % rectangle.Length]);
                    Assert.That(Mathf.Abs(CityRoadPolygon.Area(clipped)), Is.LessThanOrEqualTo(.00001f),
                        "Grass is still rendered beneath " + surface.Id);
                }

                // Cover the original route, including trimmed pad intersections
                // and the threshold: one visible owner, continuous walking ground.
                for (float x = surface.Bounds.xMin + 0.125f;
                     x < surface.Bounds.xMax; x += 0.25f)
                for (float z = surface.Bounds.yMin + 0.125f;
                     z < surface.Bounds.yMax; z += 0.25f)
                {
                    var point = new Vector2(x, z);
                    int owners = threshold.Contains(point) ? 1 : 0;
                    foreach (CityChurchCourtyardSurfaceDescriptor patch in visible)
                        if (patch.Bounds.Contains(point)) owners++;
                    Assert.That(owners, Is.EqualTo(1), surface.Id + " at " + point);
                    float top;
                    if (threshold.Contains(point)) top = ChurchThresholdExpectedTop(city.World.ChurchPlan, point);
                    else Assert.That(TryReplanningSurfaceTop(city.Layout, streets, point, out top), Is.True,
                        surface.Id + " has no planned substrate at " + point);
                    Assert.That(TryChurchPavingHit(collision, thresholdSupport,
                        new Vector3(x, top, z), out RaycastHit hit), Is.True,
                        surface.Id + " has no active physical floor at " + point + "; expected=" + top);
                    Assert.That(hit.normal.y, Is.GreaterThan(.7f), surface.Id + " floor normal at " + point);
                    Assert.That(hit.point.y, Is.EqualTo(top).Within(.003f),
                        surface.Id + " floor datum at " + point + "; collider=" + hit.collider.name);
                    FootstepGround marker = hit.collider.GetComponent<FootstepGround>();
                    Assert.That(marker, Is.Not.Null);
                    Assert.That(marker.ResolveKind(hit.triangleIndex), Is.EqualTo(FootstepGroundKind.Stone),
                        surface.Id + " footstep ownership at " + point);
                }
            }
            CityChurchPlan church = city.World.ChurchPlan;
            Vector3 door = church.DoorGroundPosition;
            Vector3 outward = church.EntranceOutwardDirection;
            // Probe two near-endpoints and the real action dock separately;
            // the latter must match the actual action root's support datum.
            foreach (float distance in new[] { .002f, CityChurchWorldBuilder.DoorThresholdDepth - .002f,
                         CityChurchPlanner.DoorDockOutwardDistance })
            {
                Vector3 point = door + outward * distance;
                point.y = ChurchThresholdExpectedTop(church, new Vector2(point.x, point.z));
                Assert.That(TryChurchPavingHit(collision, thresholdSupport, point, out RaycastHit hit), Is.True);
                Assert.That(hit.collider, Is.SameAs(thresholdSupport));
                Assert.That(hit.point.y, Is.EqualTo(point.y).Within(.001f), "Sloped sill endpoint/dock at " + point);
                if (Mathf.Abs(distance - CityChurchPlanner.DoorDockOutwardDistance) < .001f)
                    Assert.That(Mathf.Abs(hit.point.y + PlayerFactory.GroundedRootOffset - church.DoorDockPosition.y),
                        Is.LessThanOrEqualTo(.001f));
            }
        }

        private static float ChurchThresholdExpectedTop(CityChurchPlan church, Vector2 point)
        {
            Vector3 outer = church.DoorGroundPosition + church.EntranceOutwardDirection * CityChurchWorldBuilder.DoorThresholdDepth;
            Vector2 inward = new Vector2(-church.EntranceOutwardDirection.x, -church.EntranceOutwardDirection.z);
            float amount = Vector2.Dot(point - new Vector2(outer.x, outer.z), inward) / CityChurchWorldBuilder.DoorThresholdDepth;
            return church.GroundTopY + Mathf.Lerp(CityChurchCourtyardWorldBuilder.SurfaceTopAboveGround,
                CityChurchPlanner.FoundationTopAboveGround, amount);
        }

        private static bool TryChurchPavingHit(IReadOnlyList<MeshCollider> sectors, MeshCollider threshold,
            Vector3 expected, out RaycastHit nearest)
        {
            RaycastHit best = default;
            bool found = false;
            var ray = new Ray(expected + Vector3.up * .3f, Vector3.down);
            foreach (MeshCollider collider in sectors)
                Sample(collider);
            Sample(threshold);
            nearest = best;
            return found;

            void Sample(MeshCollider collider)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return;
                Bounds bounds = collider.bounds;
                if (expected.x < bounds.min.x - .001f || expected.x > bounds.max.x + .001f ||
                    expected.z < bounds.min.z - .001f || expected.z > bounds.max.z + .001f) return;
                if (collider.Raycast(ray, out RaycastHit hit, .6f) && (!found || hit.distance < best.distance))
                { best = hit; found = true; }
            }
        }

        private static bool ChurchSurfaceOverlap(Rect first, Rect second)
        {
            // Rect stores origin + size: reconstructing an edge near world
            // X=200 can differ by an ULP. Use the plans' millimetre tolerance.
            return Mathf.Min(first.xMax, second.xMax) -
                   Mathf.Max(first.xMin, second.xMin) > 0.001f &&
                   Mathf.Min(first.yMax, second.yMax) -
                   Mathf.Max(first.yMin, second.yMin) > 0.001f;
        }

        [UnityTest]
        [Explicit("Focused church garden route, interaction and visual acceptance.")]
        public IEnumerator CityChurchCourtyard()
        {
            BuildChurchGardenEditorAssets("ChurchGardenAssetSetup");
            BuildChurchGardenEditorAssets("ChurchGardenPotActionAssetSetup");
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City,
                () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () => ChurchGardenShots(city));
            Debug.Log("Church garden acceptance: frames captured; walking physical route.");
            CityChurchCourtyardPlan courtyard = city.World.ChurchCourtyardPlan;
            CityChurchCourtyardPlanner.ValidateOrThrow(city.Layout, city.World.ChurchPlan, courtyard);
            ValidateChurchGardenLights(city, false);
            CaptureChurchGardenLighting(city, "day");
            var landing = new CityMapCityTeleportGround(city.Layout);
            float x = courtyard.Grounds.xMin;
            float z = courtyard.Grounds.yMin;
            Assert.That(landing.TryResolveStandingPosition(
                new Vector2(x - 2f, city.World.ChurchPlan.DoorGroundPosition.z),
                out Vector3 streetStart), Is.True);
            city.Player.Motor.Teleport(streetStart);
            city.Player.Motor.SetInputEnabled(false);
            yield return null;
            IReadOnlyList<Vector3> route = CityChurchCourtyardPlanner.GetLoopWaypoints(courtyard);
            for (int i = 0; i < route.Count; i++)
                yield return WalkChurchGardenLeg(city.Player.Motor, route[i], "loop-" + i);
            yield return WalkChurchGardenLeg(city.Player.Motor,
                new Vector3(route[0].x, courtyard.GroundTopY, z + 1.2f), "south-turn");
            yield return WalkChurchGardenLeg(city.Player.Motor,
                new Vector3(courtyard.Passage.AxisX, courtyard.GroundTopY, z + 1.2f), "passage-approach");
            yield return WalkChurchGardenLeg(city.Player.Motor,
                new Vector3(courtyard.Passage.AxisX, courtyard.GroundTopY, z - 4f), "cemetery-passage");
            yield return WalkChurchGardenLeg(city.Player.Motor,
                new Vector3(courtyard.Passage.AxisX, courtyard.GroundTopY, z + 1.2f), "cemetery-passage-return");

            // The garden's hedge opening is not an opening into the closed
            // checkpoint. Follow it to the real southern return fence first,
            // then cross the graded seam on the checkpoint's public side.
            Rect northOpening = ChurchGardenBorderPlan.GetNorthOpeningBounds(courtyard);
            CityEastExitPlan eastExit = city.World.EastExitPlan;
            Assert.That(eastExit.IsEnabled, Is.True);
            CharacterController heroBody = city.Player.Motor.GetComponent<CharacterController>();
            float seamZ = courtyard.Grounds.yMax;
            Vector2 requestedStart = new Vector2(northOpening.center.x, northOpening.yMin + 1f);
            Assert.That(landing.TryResolveStandingPosition(requestedStart, out Vector3 gradeStart), Is.True);
            Assert.That(Vector2.Distance(new Vector2(gradeStart.x, gradeStart.z), requestedStart), Is.LessThan(.01f),
                "Use the actual hedge opening, never a clamped map substitute.");
            city.Player.Motor.Teleport(gradeStart);
            yield return null;
            yield return WalkChurchGardenLeg(city.Player.Motor,
                new Vector3(northOpening.center.x, courtyard.GroundTopY,
                    seamZ - heroBody.radius - CityEastExitPlan.FenceThickness * .5f - .35f), "north-hedge-to-return-fence");
            for (int frame = 0; frame < 30; frame++)
            {
                city.Player.Motor.MoveTowardsApproachWaypoint(
                    new Vector3(northOpening.center.x, courtyard.GroundTopY, seamZ + 2f), .1f, .1f);
                yield return null;
            }
            city.Player.Motor.CancelInteractionPoseMove();
            Assert.That(city.Player.Motor.transform.position.z, Is.LessThan(seamZ - heroBody.radius),
                "The checkpoint return fence must continue to close its outboard ground.");
            Assert.That(Physics.Raycast(city.Player.Motor.transform.position + Vector3.up * .8f, Vector3.forward,
                out RaycastHit returnFence, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(returnFence.collider.name, Does.Contain("Boundary Fence"),
                "The old hardcoded trial stopped at the actual visible checkpoint fence, not an unexplained walking seam.");

            float publicMinimumX = Mathf.Max(courtyard.Grounds.xMin, eastExit.YardBounds.xMin);
            float publicMaximumX = Mathf.Min(courtyard.Grounds.xMax, eastExit.ClosedGroundBounds.xMin);
            Assert.That(publicMaximumX - publicMinimumX, Is.GreaterThan(heroBody.radius * 2f + 1f));
            float publicX = (publicMinimumX + publicMaximumX) * .5f;
            Assert.That(landing.TryResolveStandingPosition(new Vector2(publicX, seamZ - 1.5f), out Vector3 publicSouth), Is.True);
            Assert.That(landing.TryResolveStandingPosition(new Vector2(publicX, seamZ + 2f), out Vector3 publicNorth), Is.True);
            Assert.That(publicNorth.x, Is.EqualTo(publicX).Within(.01f));
            Assert.That(publicNorth.z, Is.EqualTo(seamZ + 2f).Within(.01f));
            yield return WalkChurchGardenLeg(city.Player.Motor, publicSouth, "north-grade-public-approach");
            yield return WalkChurchGardenLeg(city.Player.Motor, publicNorth, "north-grade-public-seam");
            Assert.That(city.Player.Motor.transform.position.y, Is.EqualTo(publicNorth.y).Within(.08f));
            yield return WalkChurchGardenLeg(city.Player.Motor, publicSouth, "north-grade-public-seam-return");
            Debug.Log("Church garden acceptance: hedge opening reaches the visible closed return fence; actual motor crosses the public north grade in both directions.");

            ChurchGardenFountain fountain = city.World.ChurchCourtyardRoot
                .GetComponentInChildren<ChurchGardenFountain>();
            Assert.That(fountain, Is.Not.Null);
            Assert.That(fountain.GetComponentsInChildren<Light>(), Is.Empty);
            Assert.That(fountain.Voice.maxDistance, Is.EqualTo(4.5f));
            Assert.That(fountain.Voice.spatialBlend, Is.EqualTo(1f));

            ChurchGardenPotInteraction pot = city.ChurchGardenPot;
            Assert.That(pot, Is.Not.Null);
            city.Player.Motor.Teleport(pot.Plan.EntryPose.RootPosition);
            city.Player.GameObject.transform.rotation = pot.Plan.Facing;
            city.Player.Motor.SetInputEnabled(true);
            yield return null;
            Assert.That(pot.Begin(), Is.True, "The pot must offer its real action.");
            yield return WaitForChurchPot(() => pot.Controller.Phase ==
                PlayerAnimatedInteractionPhase.Looping, "pickup");
            yield return null;
            Assert.That(pot.IsHolding, Is.True);
            ValidateChurchPotHeroDimensions(city);
            CaptureChurchPot(city, pot, "church-garden-07-pot-hold");
            Assert.That(pot.SelectDock(1), Is.True);
            Assert.That(pot.RequestPlace(), Is.True);
            yield return WaitForChurchPot(() => !pot.OwnsActiveInteraction, "place-right");
            yield return null;
            Assert.That(pot.DockIndex, Is.EqualTo(1));
            Assert.That(Vector3.Distance(pot.PotTransform.position,
                pot.Plan.GetDockPosition(1)), Is.LessThan(0.005f));
            Assert.That(city.Player.Motor.InputEnabled, Is.True);
            CaptureChurchPot(city, pot, "church-garden-08-pot-placed");
            // Rebinding models after a scene reload reads the same session
            // dock. It must not reset the moved object to its authoring dock.
            pot.Initialize(city.Player, pot.Controller, pot.Plan, pot.PotTransform);
            Assert.That(pot.DockIndex, Is.EqualTo(1));
            Assert.That(pot.Begin(), Is.True);
            yield return WaitForChurchPot(() => pot.Controller.Phase ==
                PlayerAnimatedInteractionPhase.Looping, "pickup-right");
            yield return null;
            Assert.That(pot.SelectDock(0), Is.True);
            Assert.That(pot.RequestPlace(), Is.True);
            yield return WaitForChurchPot(() => !pot.OwnsActiveInteraction, "place-left");
            yield return null;
            Assert.That(pot.DockIndex, Is.EqualTo(0));
            Assert.That(Vector3.Distance(pot.PotTransform.position,
                pot.Plan.GetDockPosition(0)), Is.LessThan(0.005f));
            Assert.That(pot.Begin(), Is.True);
            yield return WaitForChurchPot(() => pot.Controller.Phase ==
                PlayerAnimatedInteractionPhase.Looping, "cancel-pickup");
            yield return null;
            pot.enabled = false;
            yield return null;
            yield return null;
            Assert.That(pot.OwnsActiveInteraction, Is.False);
            Assert.That(pot.Controller.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(pot.DockIndex, Is.EqualTo(0));
            Assert.That(city.Player.Motor.InputEnabled, Is.True);
            Assert.That(Vector3.Distance(pot.PotTransform.position,
                pot.Plan.GetDockPosition(0)), Is.LessThan(0.005f));
            pot.enabled = true;
            Debug.Log("Church garden acceptance: placement, session rebind and disable cleanup passed.");
            GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
            city.DayNight.ApplyCurrentTime(true);
            yield return null;
            ValidateChurchGardenLights(city, true);
            CaptureChurchGardenLighting(city, "night");
            Debug.Log("Church garden acceptance: hedge lighting remains visible by day and night.");
        }

        private static void ValidateChurchGardenLights(CityGameRoot city, bool night)
        {
            ChurchGardenUplight[] fixtures = city.World.ChurchCourtyardRoot
                .GetComponentsInChildren<ChurchGardenUplight>();
            Assert.That(fixtures.Length, Is.EqualTo(city.World.ChurchCourtyardPlan
                .GetFixtureCount(CityChurchCourtyardFixtureKind.Uplight)));
            Assert.That(fixtures.Length, Is.InRange(8, 10));
            int statueLights = 0;
            foreach (ChurchGardenUplight fixture in fixtures)
            {
                if (fixture.LightsStatue) statueLights++;
                Assert.That(fixture.Emitter.enabled, Is.True);
                Assert.That(fixture.Emitter.type, Is.EqualTo(LightType.Spot));
                Assert.That(fixture.Emitter.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(fixture.Emitter.range, Is.EqualTo(ChurchGardenUplight.LightRange));
                Assert.That(fixture.Emitter.intensity, Is.GreaterThanOrEqualTo(
                    fixture.NightIntensity * GameTimeDayNightRules.DayFixtureFloor - 0.001f));
                if (night) Assert.That(fixture.Emitter.intensity,
                    Is.EqualTo(fixture.NightIntensity).Within(0.001f));
                Assert.That(fixture.Emitter.transform.forward.y, Is.InRange(0.5f, 0.65f));
            }
            Assert.That(statueLights, Is.EqualTo(1));
        }

        private static void CaptureChurchGardenLighting(CityGameRoot city, string time)
        {
            CityChurchCourtyardPlan garden = city.World.ChurchCourtyardPlan;
            float x = garden.Grounds.xMin, z = garden.Grounds.yMin, y = garden.GroundTopY;
            Shot[] shots =
            {
                Shot.At("church-garden-10-hedge-west-" + time,
                    new Vector3(x + 17.6f, y + EyeHeight, z + 34f),
                    new Vector3(x + 17.6f, y + 0.5f, z + 37.3f), 68f),
                Shot.At("church-garden-11-hedge-east-" + time,
                    new Vector3(x + 39.5f, y + EyeHeight, z + 32.5f),
                    new Vector3(x + 42f, y + 0.5f, z + 35.8f), 68f),
                Shot.At("church-garden-12-statue-" + time,
                    new Vector3(x + 29f, y + EyeHeight, z + 29f),
                    new Vector3(x + 32.4f, y + 0.9f, z + 32.7f), 58f),
                Shot.At("church-garden-13-garden-" + time,
                    new Vector3(x + 22f, y + 5f, z + 24f),
                    new Vector3(x + 22f, y, z + 34f), 80f)
            };
            Camera camera = Camera.main;
            foreach (Shot shot in shots)
            {
                camera.transform.SetPositionAndRotation(shot.Position,
                    Quaternion.LookRotation(shot.Target - shot.Position, Vector3.up));
                camera.fieldOfView = shot.FieldOfView;
                CaptureCurrentCamera(camera, SceneIds.City, shot.Name);
            }
        }

        private static void BuildChurchGardenEditorAssets(string className)
        {
            Type type = Type.GetType("BarPromenade.Editor." + className + ", BarPromenade.Editor");
            Assert.That(type, Is.Not.Null, className);
            var method = type.GetMethod("BuildOrThrow",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Assert.That(method, Is.Not.Null, className);
            method.Invoke(null, null);
        }

        private static Shot[] ChurchGardenShots(CityGameRoot city)
        {
            CityChurchCourtyardPlan plan = city.World.ChurchCourtyardPlan;
            float x = plan.Grounds.xMin;
            float z = plan.Grounds.yMin;
            float y = plan.GroundTopY;
            CityChurchCemeteryPassagePlan passage = plan.Passage;
            Assert.That(passage, Is.Not.Null, "The passage capture needs the shared physical opening.");
            return new[]
            {
                Shot.At("church-garden-00-overview", new Vector3(x + 22f, y + 10f, z + 37f),
                    new Vector3(x + 22f, y, z + 29f), 78f),
                Shot.At("church-garden-01-street", new Vector3(x - 2f, y + EyeHeight, z + 13f),
                    city.World.ChurchPlan.DoorGroundPosition + Vector3.up * 2.2f, 68f),
                Shot.At("church-garden-02-fountain", new Vector3(x + 17f, y + EyeHeight, z + 28f),
                    new Vector3(x + 20.5f, y + 0.6f, z + 26.5f), 62f),
                Shot.At("church-garden-03-seated", new Vector3(x + 16.5f, y + 1.2f, z + 32.9f),
                    new Vector3(x + 22f, y + 4f, z + 18f), 62f),
                Shot.At("church-garden-04-statue", new Vector3(x + 29f, y + EyeHeight, z + 29f),
                    new Vector3(x + 32.4f, y + 1f, z + 32.7f), 58f),
                Shot.At("church-garden-05-cemetery",
                    new Vector3(passage.AxisX, passage.CemeteryGroundTopY + EyeHeight, passage.BoundaryZ - 3f),
                    new Vector3(passage.AxisX, passage.ChurchGroundTopY + 1.1f, passage.BoundaryZ + 15f), 65f),
                Shot.At("church-garden-06-north-grade", new Vector3(x + 44f, y + 3.5f, z + 47f),
                    new Vector3(x + 24f, y + 1.5f, z + 31f), 65f)
            };
        }

        private static IEnumerator WalkChurchGardenLeg(PlayerMotor motor, Vector3 target, string name)
        {
            bool arrived = false;
            for (int frame = 0; frame < 180 && !arrived; frame++)
            {
                // Larger bounded test steps retain the production controller
                // sweep and navigation constraint, without a minute-long walk.
                arrived = motor.MoveTowardsApproachWaypoint(target, 0.10f, 0.25f);
                yield return null;
            }
            motor.CancelInteractionPoseMove();
            Assert.That(arrived, Is.True, "Blocked church garden leg: " + name +
                " at " + motor.transform.position + " toward " + target);
        }

        private static IEnumerator WaitForChurchPot(Func<bool> ready, string phase)
        {
            float deadline = Time.realtimeSinceStartup + 18f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, "Church pot stalled: " + phase);
        }

        private static void CaptureChurchPot(CityGameRoot city, ChurchGardenPotInteraction pot, string name)
        {
            Camera camera = Camera.main;
            Vector3 origin = pot.Plan.StandingGroundPosition;
            camera.transform.SetPositionAndRotation(origin + new Vector3(1.6f, 1.7f, -1.7f),
                Quaternion.LookRotation(new Vector3(-1.6f, -0.5f, 2.15f), Vector3.up));
            camera.fieldOfView = 58f;
            CaptureCurrentCamera(camera, SceneIds.City, name);
        }

        private static void ValidateChurchPotHeroDimensions(CityGameRoot city)
        {
            Player3DAssetRegistry registry = city.Player.GameObject
                .GetComponentInChildren<Player3DAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            bool first = true;
            Bounds bounds = default;
            foreach (Renderer renderer in registry.Renderers)
            {
                if (renderer == null) continue;
                if (first) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                first = false;
            }
            Assert.That(first, Is.False);
            Assert.That(bounds.size.y, Is.InRange(1f, 2.2f),
                "The imported pot action must retain the hero's metre-scale body.");
            Assert.That(bounds.size.x, Is.LessThan(1.8f));
            Assert.That(bounds.size.z, Is.LessThan(1.8f));
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator CityWindowLighting()
        {
            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));

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
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));

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

        [UnityTest]
        [Explicit("Shared overhead speech through City talk/menu adapters. Run without -batchmode to render Game view UI.")]
        public IEnumerator CitySpokenResponses()
        {
            Assert.That(Application.isBatchMode, Is.False, "IMGUI capture requires the ordinary Editor Game view.");
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(viewType, Is.Not.Null);
            UnityEditor.EditorWindow gameView = UnityEditor.EditorWindow.GetWindow(viewType);
            gameView.Show();
            gameView.Focus();
#endif
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (city == null || !city.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "City initialization timed out.");
                city = Object.FindAnyObjectByType<CityGameRoot>();
                yield return null;
            }
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool followed = follow != null && follow.enabled;
            var motor = city.Player.Motor;
            bool motorEnabled = motor.enabled;
            Vector3 savedHero = motor.transform.position;
            Pose savedCamera = new Pose(camera.transform.position, camera.transform.rotation);
            float savedField = camera.fieldOfView;
            var hidden = new List<Renderer>();
            var prompt = Object.FindAnyObjectByType<InteractionPromptView>();
            Assert.That(prompt, Is.Not.Null);
            if (follow != null) follow.enabled = false;
            motor.enabled = false;
            city.CemeteryWatchman.Talk.AttachGravedigging(null);
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
            try
            {
                Component[] actors = { city.CemeteryWatchman, city.SeacoastFisherman, city.LastRouteFerryman };
                string[] names = { "watchman", "fisherman", "ferryman" };
                foreach (Component actor in actors) Assert.That(actor, Is.Not.Null);
                for (int index = 0; index < actors.Length; index++)
                {
                    Component actor = actors[index];
                    var registry = actor.GetComponentInChildren<CityPedestrianAssetRegistry>(true);
                    Assert.That(registry, Is.Not.Null);
                    Transform head = registry.HeadAnchor;
                    Vector3 forward = actor.transform.forward;
                    motor.Teleport(actor.transform.position + forward * 1.1f);
                    camera.transform.SetPositionAndRotation(head.position + forward * 3.2f + actor.transform.right * .7f,
                        Quaternion.LookRotation(-forward));
                    camera.transform.LookAt(head.position - Vector3.up * .35f);
                    camera.fieldOfView = 58f;
                    Physics.SyncTransforms();
                    prompt.ClearFeedback();
                    if (index == 0) city.CemeteryWatchman.Talk.Interact(city.Player.Interactor);
                    else if (index == 1)
                        Object.FindAnyObjectByType<SeacoastFishermanInteraction>().Interact(city.Player.Interactor);
                    else
                    {
                        var talk = Object.FindAnyObjectByType<LastRouteFerrymanInteraction>();
                        Assert.That(talk, Is.Not.Null);
                        talk.Interact(city.Player.Interactor);
                        var menu = Object.FindAnyObjectByType<InventoryTargetInteractionController>();
                        Assert.That(menu.IsOpen, Is.True);
                        menu.SelectChoice(InventoryTargetInteractionChoice.Talk);
                        Assert.That(menu.SelectedChoice, Is.EqualTo(InventoryTargetInteractionChoice.Talk));
                        Assert.That(menu.Confirm(), Is.True);
                    }
                    Assert.That(prompt.IsSpeaking, Is.True, names[index] + " uses the shared spoken path.");
                    NpcSpeechBubbleView bubble = prompt.SpokenBubbles;
                    Assert.That(bubble, Is.Not.Null);
                    Assert.That(bubble.IsShowing(actor), Is.True, "The actual factory attaches the speaker to its actor.");
                    float shownAt = Time.unscaledTime;
                    while (Time.unscaledTime < shownAt + .6f) yield return null;
                    Assert.That(bubble.HasRenderedLayout, Is.True);
                    Assert.That(bubble.LastRenderedBubbleCount, Is.EqualTo(1));
                    Assert.That(bubble.RevealedTextOf(actor), Is.Not.Empty);
                    Assert.That(prompt.HasRenderedLayout, Is.False, "The answer is never duplicated in the bottom panel.");
                    Rect frame = bubble.LastRenderedPanelRect;
                    string held = bubble.RevealedTextOf(actor);
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        yield return null;
                        held = bubble.RevealedTextOf(actor);
                        for (int paused = 0; paused < 3; paused++) yield return null;
                        Assert.That(bubble.RevealedTextOf(actor), Is.EqualTo(held));
                        Assert.That(bubble.RenderEnabled, Is.False);
                    }
                    yield return null;
                    Assert.That(bubble.RenderEnabled, Is.True);
                    Assert.That(bubble.LastRenderedPanelRect.width, Is.EqualTo(frame.width));
                    Assert.That(bubble.LastRenderedPanelRect.height, Is.EqualTo(frame.height));
                    string completeLine = prompt.GetDisplayedTextAt(Time.unscaledTime);
                    deadline = Time.realtimeSinceStartup + 10f;
                    while (bubble.RevealedTextOf(actor) != completeLine && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(bubble.RevealedTextOf(actor), Is.EqualTo(completeLine));
                    string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "City",
                        "speech-" + names[index] + ".png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    DateTime oldWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    ScreenCapture.CaptureScreenshot(path);
                    deadline = Time.realtimeSinceStartup + 3f;
                    do { yield return null; }
                    while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= oldWrite) && Time.realtimeSinceStartup < deadline);
                    Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > oldWrite, Is.True,
                        "The captured Game view must include the actual overhead UI.");
                    Assert.That(prompt.IsSpeaking, Is.True, "The complete line remains readable during capture.");
                }
            }
            finally
            {
                prompt.ClearFeedback();
                city.CemeteryWatchman.Talk.AttachGravedigging(city.Gravedigging);
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                motor.Teleport(savedHero);
                motor.enabled = motorEnabled;
                camera.transform.SetPositionAndRotation(savedCamera.position, savedCamera.rotation);
                camera.fieldOfView = savedField;
                if (follow != null) follow.enabled = followed;
            }
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
            GameSessionState.AdvanceGameTime((float)(90f / GameTimeState.GameMinutesPerRealSecond));

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
            GameSessionState.AdvanceGameTime((float)(14f * 60f / GameTimeState.GameMinutesPerRealSecond));
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
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillage()
        {
            // Day two at 07:40 keeps the accepted morning light but lands the
            // default seed in a fully developed crosswind slot. The village
            // has a storm floor in every slot; this time is chosen only so the
            // direction reads cleanly across the uphill camera.
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));

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
            GameSessionState.AdvanceGameTime((float)(90f / GameTimeState.GameMinutesPerRealSecond));

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

        [UnityTest]
        [Explicit("Bathroom, neighbouring rooms and the extended house facade only.")]
        [PrebuildSetup(typeof(MothersHouseBathroomAssetsSetup))]
        public IEnumerator MothersHouseBathroom()
        {
            MothersHouseInteriorRoot inside = null;
            yield return Capture(SceneIds.MothersHouseInterior,
                () => inside = Object.FindAnyObjectByType<MothersHouseInteriorRoot>(),
                () => new[] {
                    MothersHouseShot(inside, HomeCameraShotKind.MainRoom, "bathroom-00-ground-niche"),
                    MothersHouseShot(inside, HomeCameraShotKind.StairAndUpperCorridor, "bathroom-01-corridor"),
                    MothersHouseShot(inside, HomeCameraShotKind.UpperNorthRoom, "bathroom-02-parents-room"),
                    MothersHouseShot(inside, HomeCameraShotKind.UpperBathroom, "bathroom-03-room") });
            AlpineVillageRoot outside = null;
            yield return Capture(SceneIds.AlpineVillage,
                () => outside = Object.FindAnyObjectByType<AlpineVillageRoot>(),
                () => {
                    var house = outside.Plan.MothersHouse;
                    Vector3 right = Vector3.Cross(Vector3.up, house.Facing);
                    Vector3 Local(float x, float y, float z) => house.GroundCenter +
                        right * x + Vector3.up * y + house.Facing * z;
                    return new[] {
                        Shot.At("bathroom-04-front-mass", Local(-10, 4.5f, 14), Local(0, 3, 0), 55),
                        Shot.At("bathroom-05-rear-wing", Local(11, 5, -13), Local(1.5f, 3, -2), 56),
                        Shot.At("bathroom-06-stone-side", Local(13, 3, 0), Local(3, 3.5f, -.5f), 59) };
                });
        }

        private static Shot[] MothersHouseShots(
            MothersHouseInteriorRoot root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.FixedCamera, Is.Not.Null);
            Assert.That(root.FixedCamera.IsInitialized, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(root.Layout.CameraShots, Has.Count.EqualTo(5));
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
                    "02-childhood-room"),
                MothersHouseShot(
                    root,
                    HomeCameraShotKind.UpperNorthRoom,
                    "03-parents-bedroom"),
                MothersHouseShot(root, HomeCameraShotKind.UpperBathroom, "07-bathroom"),

                // Diagnostics, not compositions. The ground shot pans onto
                // the hero and re-lenses onto him, so these frames stand at
                // both ends of that travel. They are what caught the shot
                // standing OUTSIDE the east wall: panned north from there,
                // a third of the picture was the empty outside behind the
                // wall the cutaway had hidden. From inside the room every
                // ray ends on a wall, and these two prove it.
                MothersHousePannedShot(root, 30f, 0f, "04-pan-north-limit"),
                MothersHousePannedShot(root, -30f, 0f, "05-pan-west-limit"),
                MothersHousePannedShot(
                    root,
                    0f,
                    0f,
                    "06-lens-near",
                    MothersHouseInteriorLayoutPlanner
                        .MainRoomMaximumFieldOfView)
            };
        }

        private static Shot MothersHousePannedShot(
            MothersHouseInteriorRoot root,
            float yawDegrees,
            float pitchDegrees,
            string name,
            float fieldOfView = -1f)
        {
            Assert.That(
                root.Layout.TryGetCameraShot(
                    HomeCameraShotKind.MainRoom,
                    out HomeCameraShot shot),
                Is.True);
            Quaternion rotation =
                Quaternion.AngleAxis(yawDegrees, Vector3.up) *
                shot.Rotation *
                Quaternion.AngleAxis(pitchDegrees, Vector3.right);
            return Shot.At(
                name,
                shot.Position,
                shot.Position + rotation * Vector3.forward * 10f,
                fieldOfView > 0f ? fieldOfView : shot.FieldOfView);
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
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator Stairwell()
        {
            StairwellInteriorRoot stairwell = null;
            GameSessionState.BeginNewGame();
            try
            {
                yield return Capture(
                    SceneIds.StairwellInterior,
                    () => stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>(),
                    () => StairwellAuthoredShots(stairwell));
                Assert.That(stairwell.Layout.IsCorrupted, Is.False);
                Assert.That(stairwell.World.UpperBlocker, Is.Not.Null);
                Assert.That(stairwell.World.DescentWall, Is.Null);
                GameSessionState.CommitHomeSleepAppearance();
                GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
                yield return Capture(SceneIds.StairwellInterior,
                    () => stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>(),
                    () => CorruptedStairwellShots(stairwell));
                yield return VerifyCorruptedStairwellRoute(stairwell);
            }
            finally { GameSessionState.BeginNewGame(); }
        }

        private static Shot[] StairwellAuthoredShots(StairwellInteriorRoot stairwell)
        {
            Assert.That(stairwell, Is.Not.Null);
            var shots = new List<Shot>();
            // Authored gameplay poses live inside the shell. Hero-relative
            // generic shots could photograph a wall or the outside of the box.
            foreach (StairwellCameraShot pose in StairwellFixedCameraController.CreateDefaultShots(stairwell.Layout))
                shots.Add(Shot.At("0" + (shots.Count + 1) + "-gameplay-" + pose.Kind,
                    stairwell.transform.TransformPoint(pose.Position),
                    stairwell.transform.TransformPoint(pose.Position + pose.Rotation * Vector3.forward * 4f),
                    pose.FieldOfView, delayFrames: 4));
            Transform room = stairwell.World.Root;
            shots.Add(Shot.At("04-cat-perch-and-rail",
                room.TransformPoint(new Vector3(-3.55f, 3.60f, 3.75f)),
                stairwell.Cat.Renderer.bounds.center, 54f, delayFrames: 4));
            shots.Add(Shot.At("05-apartment-door-and-reveal",
                room.TransformPoint(new Vector3(1.9f, 4.7f, -4.0f)),
                room.TransformPoint(new Vector3(4.1f, 4.4f, -3.72f)), 55f, delayFrames: 4));
            shots.Add(Shot.At("06-upper-debris-and-fixture",
                room.TransformPoint(new Vector3(.5f, 5.1f, -3.65f)),
                room.TransformPoint(stairwell.Layout.UpperBlockerBounds.center), 62f, delayFrames: 4));
            shots.Add(Shot.At("07-ground-service-and-grille",
                room.TransformPoint(new Vector3(.1f, 1.8f, -.7f)),
                room.TransformPoint(new Vector3(-1.6f, 1.1f, -3.2f)), 68f, delayFrames: 4));
            shots.Add(Shot.At("08-street-door-handle",
                room.TransformPoint(new Vector3(-2.5f, 1.65f, -3.9f)),
                stairwell.World.StreetDoor.Find("Street Door Hardware").GetComponent<MeshRenderer>().bounds.center, 56f, delayFrames: 4));
            shots.Add(Shot.At("09-cabinet-pull",
                room.TransformPoint(new Vector3(2.65f, 1.6f, .3f)),
                room.TransformPoint(new Vector3(4.05f, 1.16f, -.35f)), 56f, delayFrames: 4));
            return shots.ToArray();
        }

        [UnityTest]
        [Explicit("Capture, not a test. Run one area at a time.")]
        public IEnumerator Supermarket()
        {
            GameSessionState.BeginNewGame("Test supermarket visual itinerary");
            SupermarketInteriorRoot supermarketRoot = null;
            float previousDelta = Time.captureDeltaTime;
            Pose previousHero = default;
            bool borrowed = false, motorInput = false, followEnabled = false;
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                yield return Capture(
                    SceneIds.SupermarketInterior,
                    () =>
                    {
                        supermarketRoot = Object.FindAnyObjectByType<SupermarketInteriorRoot>();
                        return supermarketRoot;
                    },
                    () =>
                    {
                        Assert.That(supermarketRoot, Is.Not.Null);
                        previousHero = new Pose(supermarketRoot.Player.GameObject.transform.position,
                            supermarketRoot.Player.GameObject.transform.rotation);
                        motorInput = supermarketRoot.Player.Motor.InputEnabled;
                        followEnabled = supermarketRoot.CameraFollow.enabled;
                        borrowed = true;
                        supermarketRoot.Player.Motor.SetInputEnabled(false);
                        supermarketRoot.CameraFollow.enabled = false;
                        return SupermarketShots(supermarketRoot);
                    });
            }
            finally
            {
                if (borrowed && supermarketRoot != null)
                {
                    supermarketRoot.Player.Motor.Teleport(previousHero.position);
                    supermarketRoot.Player.GameObject.transform.rotation = previousHero.rotation;
                    supermarketRoot.Player.Motor.SetInputEnabled(motorInput);
                    supermarketRoot.CameraFollow.enabled = followEnabled;
                }
                Time.captureDeltaTime = previousDelta;
            }
        }

        private static Shot[] SupermarketShots(
            SupermarketInteriorRoot supermarketRoot)
        {
            Assert.That(supermarketRoot, Is.Not.Null);
            Assert.That(supermarketRoot.World, Is.Not.Null);
            Assert.That(supermarketRoot.World.Shelves, Has.Count.EqualTo(3));
            Assert.That(supermarketRoot.Cashier, Is.Not.Null);
            Assert.That(supermarketRoot.SecurityCameras, Has.Count.EqualTo(4));
            Transform room = supermarketRoot.World.Root;
            SupermarketInteriorLayoutPlan plan = supermarketRoot.Layout;
            SupermarketInteriorAssetRegistry assets = supermarketRoot.World.AssetRegistry;
            IReadOnlyList<SupermarketShelfView> shelves = supermarketRoot.World.Shelves;
            var shots = new List<Shot>();
            bool CanStand(Vector3 eye)
            {
                const float radius = .32f;
                Vector2 foot = new Vector2(eye.x, eye.z);
                Rect walkable = plan.WalkableBounds;
                if (!Rect.MinMaxRect(walkable.xMin + radius, walkable.yMin + radius,
                    walkable.xMax - radius, walkable.yMax - radius).Contains(foot)) return false;
                bool Blocks(Rect bounds) => Rect.MinMaxRect(bounds.xMin - radius, bounds.yMin - radius,
                    bounds.xMax + radius, bounds.yMax + radius).Contains(foot);
                foreach (SupermarketShelfView shelf in shelves) if (Blocks(shelf.Plan.Bounds)) return false;
                foreach (SupermarketFixturePlan fixture in plan.Fixtures)
                    if (fixture.BlocksMovement && Blocks(fixture.Bounds)) return false;
                return true;
            }
            void AssertLensSurface(SupermarketSecurityCamera controller, string label)
            {
                Transform pivot = controller.HeadPivot;
                MeshFilter lens = null;
                foreach (MeshFilter candidate in pivot.GetComponentsInChildren<MeshFilter>(true))
                    if (candidate.name.EndsWith(" Lens", StringComparison.Ordinal))
                    {
                        Assert.That(lens, Is.Null, label + ": duplicate authored lens mesh.");
                        lens = candidate;
                    }
                Assert.That(lens, Is.Not.Null, label + ": the tracked head must carry its actual lens mesh.");
                var probe = new GameObject("CCTV lens surface proof");
                try
                {
                    probe.transform.SetParent(lens.transform, false);
                    MeshCollider surface = probe.AddComponent<MeshCollider>();
                    surface.sharedMesh = lens.sharedMesh;
                    Physics.SyncTransforms();
                    Vector3 front = pivot.position + pivot.forward * .6075f;
                    Vector3 forward = pivot.forward;
                    Assert.That(surface.Raycast(new Ray(front + forward * .3f, -forward), out RaycastHit hit, .6f),
                        Is.True, label + ": the actual lens front must intersect the production tracking axis.");
                    Assert.That(Vector3.Distance(hit.point, front), Is.LessThan(.002f),
                        label + ": production tracking must aim through the authored iris front, not the housing side.");
                    Assert.That(Vector3.Dot(hit.normal, forward), Is.GreaterThan(.999f),
                        label + ": actual rendered lens triangles must face along tracking +Z.");
                    Assert.That(Vector3.Dot(hit.normal, (Camera.main.transform.position - hit.point).normalized),
                        Is.GreaterThan(.99f), label + ": the recorded observer must see the lens surface.");
                }
                finally { Object.DestroyImmediate(probe); }
            }
            Shot SupermarketShot(Shot shot, SupermarketSecurityCamera trackingCamera = null)
            {
                Vector3 localEye = room.InverseTransformPoint(shot.Position);
                Assert.That(CanStand(localEye), Is.True, shot.Name + ": the ordinary hero capsule must fit this shop-floor viewpoint.");
                Vector3 observer = room.TransformPoint(new Vector3(localEye.x, PlayerFactory.GroundedRootOffset, localEye.z));
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                Assert.That(camera.GetComponent<AudioListener>(), Is.Not.Null);
                int stagedFrame = -1;
                bool Ready()
                {
                    if (stagedFrame < 0)
                    {
                        supermarketRoot.Player.Motor.Teleport(observer);
                        Vector3 facing = Vector3.ProjectOnPlane(shot.Target - observer, room.up);
                        supermarketRoot.Player.GameObject.transform.rotation = Quaternion.LookRotation(facing, room.up);
                        camera.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.Target - shot.Position, room.up));
                        Physics.SyncTransforms();
                        stagedFrame = Time.frameCount;
                    }
                    int elapsed = Time.frameCount - stagedFrame;
                    // Let real cashier/CCTV LateUpdates observe the relocated
                    // hidden hero. Capture already owns renderer hiding/restoring.
                    if (elapsed < 12) return false;
                    Assert.That(Vector3.ProjectOnPlane(supermarketRoot.Player.GameObject.transform.position - observer, room.up).magnitude,
                        Is.LessThan(.01f), shot.Name + ": the observer left its authored shop-floor point.");
                    if (trackingCamera != null)
                    {
                        Transform pivot = trackingCamera.HeadPivot;
                        Vector3 focus = supermarketRoot.Player.GameObject.transform.position + Vector3.up * SupermarketSecurityCamera.FocusHeightMeters;
                        float angle = Quaternion.Angle(pivot.rotation, SupermarketSecurityCamera.ResolveAim(pivot.position, focus));
                        Assert.That(elapsed, Is.LessThanOrEqualTo(90), shot.Name + ": the live CCTV servo did not converge; angle=" + angle);
                        if (angle > 1f) return false;
                        Assert.That(Vector3.Angle(pivot.forward, shot.Position - pivot.position), Is.LessThan(6f),
                            shot.Name + ": the recorded observer must see the actual lens-facing side.");
                        AssertLensSurface(trackingCamera, shot.Name);
                    }
                    return shot.ReadyWhen == null || shot.ReadyWhen();
                }
                return Shot.At(shot.Name, shot.Position, shot.Target, shot.FieldOfView, shot.DelayFrames, Ready, shot.ReadyPollSeconds);
            }
            void Standing(string name, Vector3 eye, Vector3 target, float fov, SupermarketSecurityCamera trackingCamera = null)
            {
                shots.Add(SupermarketShot(Shot.At(name, room.TransformPoint(eye), target, fov, delayFrames: 4), trackingCamera));
            }
            Vector3 entrance = room.InverseTransformPoint(assets.GetAnchor("entrance").position);
            Standing("01-entrance-overview", entrance + new Vector3(0f, 1.65f, 1.1f),
                assets.GetAnchor("room_centre").position + room.up * 1.15f, 62f);
            shots.Add(SupermarketShot(ShelfShot("02-dry-products", shelves[0])));
            shots.Add(SupermarketShot(ShelfShot("03-pantry-products", shelves[1])));
            shots.Add(SupermarketShot(ShelfShot("04-cold-product", shelves[2])));
            Vector3 checkout = room.InverseTransformPoint(assets.GetAnchor("checkout").position);
            Vector3 head = supermarketRoot.Cashier.Presentation.HeadWorldPosition;
            Standing("05-checkout-and-cashier", checkout + new Vector3(-2.2f, 1.65f, -1.1f),
                Vector3.Lerp(head, assets.GetAnchor("checkout").position + room.up * 1.05f, .4f), 62f);
            Standing("06-cashier-face", plan.Cashier.Position + Quaternion.Euler(0f, plan.Cashier.YawDegrees, 0f) *
                new Vector3(.15f, 1.65f, 1.85f), head, 30f);
            Bounds register = assets.GetRendererByRole("register").bounds;
            Standing("07-register-keys-and-support", new Vector3(room.InverseTransformPoint(register.center).x, 1.65f, checkout.z - 1.1f),
                register.center, 48f);
            Vector3 cold = room.InverseTransformPoint(assets.GetAnchor("shelf_cold").position);
            // The rear aisle is too close for the complete wide niche at a
            // normal lens. Two standing views retain both ends and its cavity.
            foreach (float side in new[] { -1.55f, 1.55f })
                Standing(side < 0f ? "08-cold-niche-left" : "09-cold-niche-right",
                    cold + new Vector3(side, 1.65f, -1.35f), room.TransformPoint(cold + new Vector3(side, 1.32f, 0f)), 80f);
            int number = 10;
            foreach (SupermarketShelfView shelf in shelves)
            {
                Vector3 facing = shelf.transform.TransformDirection(shelf.Plan.FacingDirection).normalized;
                foreach (SupermarketProductView product in shelf.Products)
                {
                    Assert.That(product.TryGetWorldBounds(out Bounds bounds), Is.True);
                    Vector3 eye = room.InverseTransformPoint(bounds.center + facing * 1.35f);
                    eye.y = 1.65f;
                    Standing($"{number++:00}-support-and-price-{product.ItemId}", eye,
                        new Vector3(bounds.center.x, bounds.min.y + .04f, bounds.center.z), 36f);
                }
            }
            for (int i = 0; i < supermarketRoot.SecurityCameras.Count; i++)
            {
                Transform pivot = supermarketRoot.SecurityCameras[i].HeadPivot;
                Assert.That(Vector3.Distance(pivot.position, assets.GetAnchor($"cctv_head_{i + 1:00}").position), Is.LessThan(.01f));
                Vector3 corner = room.InverseTransformPoint(pivot.position);
                Vector3 eye = new Vector3(corner.x - Mathf.Sign(corner.x) * 2f, 1.65f, corner.z);
                // The rear-right stockroom facade occupies the first option;
                // stay in its real side passage instead of inside the cabinet.
                if (!CanStand(eye)) eye = new Vector3(corner.x, 1.65f, corner.z - Mathf.Sign(corner.z) * 2f);
                Standing($"{number++:00}-cctv-corner-{i + 1:00}", eye, pivot.position + room.up * .16f, 40f,
                    supermarketRoot.SecurityCameras[i]);
            }
            return shots.ToArray();
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
        [Explicit("Focused church courtyard/interior metre contract, real routes and door roundtrip with lighting/state frames.")]
        [PrebuildSetup(typeof(ChurchRemodelAssetsSetup))]
        public IEnumerator ChurchRemodelAcceptance()
        {
            yield return CaptureChurchRemodelAcceptance();
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
            GameSessionState.AdvanceGameTime((float)(400f / GameTimeState.GameMinutesPerRealSecond));

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
            AppendVillageArtShots(root, shots);
            AppendVillageExpansionShots(root, shots);
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
                        .GetPrefabOrThrow(lot.District, lot.BuildingVariant)
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
            PlayerCameraFollow barCameraFollow = sceneName == SceneIds.BarInterior
                ? camera.GetComponent<PlayerCameraFollow>() : null;
            bool barCameraWasFollowing = barCameraFollow != null && barCameraFollow.enabled;

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

            WarehouseRefrigeratorPresentationProbe finalBarPose =
                sceneName == SceneIds.BarInterior
                    ? camera.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>() : null;
            try
            {
                camera.targetTexture = target;
                if (barCameraFollow != null) barCameraFollow.enabled = false;
                foreach (Shot shot in shots)
                {
                    Action stageCamera = () =>
                    {
                        Vector3 from = shot.Position;
                        Vector3 to = shot.Target;
                        if (shot.RelativeToHero)
                        {
                            Assert.That(player, Is.Not.Null,
                                $"'{sceneName}/{shot.Name}' is framed on the hero, and this scene has none.");
                            from = player.transform.TransformPoint(from);
                            to = player.transform.TransformPoint(to);
                        }
                        camera.transform.SetPositionAndRotation(from,
                            Quaternion.LookRotation((to - from).normalized, Vector3.up));
                        camera.fieldOfView = shot.FieldOfView;
                    };
                    if (sceneName == SceneIds.BarInterior)
                    {
                        // Camera.Render may reuse this frame's skinned vertex buffer.
                        // Expose this shot's actors for a complete skinning frame first.
                        stageCamera();
                        yield return null;
                    }
                    // Weather proofs may wait with the camera following normally.
                    // The bar instead holds its staged view throughout the wait
                    // so modular skins update in the view that will be rendered.
                    for (int frame = 0;
                         frame < shot.DelayFrames;
                         frame++)
                    {
                        yield return null;
                    }

                    //  Then, if the shot names a condition, wait for the
                    //  scene to reach it - bounded, so a condition that is
                    //  never met is a failed frame, not a hung run.
                    if (finalBarPose == null && shot.ReadyWhen != null)
                    {
                        int waited = 0;
                        WaitForSecondsRealtime readyDelay = shot.ReadyPollSeconds > 0f
                            ? new WaitForSecondsRealtime(shot.ReadyPollSeconds) : null;
                        while (!shot.ReadyWhen() &&
                               waited < MaximumReadyFrames)
                        {
                            waited++;
                            // Bar hand contacts publish in LateUpdate, after
                            // the Animator's native transform write. Inspect
                            // that rendered pose before the next graph sample.
                            if (sceneName == SceneIds.BarInterior)
                                yield return new WaitForFixedUpdate();
                            else
                                yield return readyDelay;
                        }

                        Assert.That(
                            shot.ReadyWhen(),
                            Is.True,
                            $"'{sceneName}/{shot.Name}' waited " +
                            $"{MaximumReadyFrames} frames and its moment " +
                            "never came." + (sceneName == SceneIds.BarInterior
                                ? DescribeBarDrinkMoments() : string.Empty));
                    }

                    Action captureShot = () =>
                    {
                        stageCamera();
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
                    };
                    if (finalBarPose == null) captureShot();
                    else
                    {
                        bool captured = false;
                        string failedFinalPose = null;
                        for (int waited = 0; waited < MaximumReadyFrames && !captured; waited++)
                            yield return AtWarehouseFridgePresentation(finalBarPose, () =>
                            {
                                if (shot.ReadyWhen != null && !shot.ReadyWhen())
                                {
                                    if (waited + 1 == MaximumReadyFrames) failedFinalPose = DescribeBarDrinkMoments();
                                    return;
                                }
                                captureShot();
                                captured = true;
                            });
                        Assert.That(captured, Is.True,
                            $"'{sceneName}/{shot.Name}' never reached its final presentation moment." +
                            failedFinalPose);
                    }
                }
            }
            finally
            {
                if (finalBarPose != null)
                {
                    finalBarPose.CancelPending();
                    Object.Destroy(finalBarPose);
                }
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
                if (barCameraFollow != null) barCameraFollow.enabled = barCameraWasFollowing;
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

        internal static void CaptureCurrentCamera(
            Camera camera,
            string sceneName,
            string shotName)
        {
            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                sceneName);
            Directory.CreateDirectory(folder);

            var target = new RenderTexture(Width, Height, 24);
            var frameBuffer = new Texture2D(
                Width,
                Height,
                TextureFormat.RGB24,
                false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frameBuffer.ReadPixels(
                    new Rect(0f, 0f, Width, Height),
                    0,
                    0);
                frameBuffer.Apply();

                string path = Path.Combine(folder, $"{shotName}.png");
                File.WriteAllBytes(path, frameBuffer.EncodeToPNG());
                Debug.Log($"Area capture wrote {path}");
                Assert.That(
                    IsBlank(frameBuffer),
                    Is.False,
                    $"'{sceneName}/{shotName}' came out blank.");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frameBuffer);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
