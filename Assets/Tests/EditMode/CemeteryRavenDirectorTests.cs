using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Drives the PURE director machine — no scene, no assets, no
    /// transforms. Every transition the controller will ever act on
    /// is proved here on plain values; the controller's own tests
    /// only have to show it measures and obeys. The two scene guards
    /// at the end are the whole exception: one proves the actor's
    /// adoption and root motion on the fake rig, the other holds the
    /// live controller to the canon budget of zero lights and two
    /// AmbienceDetails voices.
    /// </summary>
    public sealed class CemeteryRavenDirectorTests
    {
        private const int SeedA = 0x0A11;
        private const int SeedB = 0x0B22;

        private readonly List<GameObject> spawned =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = spawned.Count - 1; index >= 0; index--)
            {
                if (spawned[index] != null)
                {
                    Object.DestroyImmediate(spawned[index]);
                }
            }

            spawned.Clear();
            // The controller guard writes the STATIC grave ledger;
            // leave the session clean for whoever runs next.
            GameSessionState.BeginNewGame();
        }

        [Test]
        public void Model_StartsUnarmedAndIgnoresTimeUntilArmed()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);

            Assert.That(model.IsArmed, Is.False);
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Unarmed));

            model.Advance(1f, Input(farFromEverything: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Unarmed));
            Assert.That(model.PhaseElapsedSeconds, Is.Zero);

            // The suppression flag mirrors the session even unarmed:
            // it is a statement about the polled frame, not a phase.
            model.Advance(1f, Input(sessionActive: true));
            Assert.That(model.IsHeadTargetSuppressed, Is.True);
            model.Advance(1f, Input());
            Assert.That(model.IsHeadTargetSuppressed, Is.False);

            // Invalid deltas change nothing.
            model.Arm(false);
            model.Advance(0f, Input());
            model.Advance(float.NaN, Input());
            Assert.That(model.PhaseElapsedSeconds, Is.Zero);
        }

        [Test]
        public void Arm_AlreadySealedAtFirstPollSpawnsPerchedWithNoArrival()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);

            // The seal happened on some earlier build: the arrival
            // played without a witness and the pair is simply THERE.
            model.Arm(true);
            Assert.That(model.IsArmed, Is.True);
            Assert.That(
                model.SpawnedPerchedWithoutArrival,
                Is.True);
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.False);
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenBIndex),
                Is.False);
        }

        [Test]
        public void Arm_IsIdempotentSoASecondSealedGraveChangesNothing()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(false);
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.WaitingToArrive));

            // The first-sealed id never changes within a session, so
            // any later call — grave #2 sealing, a duplicate poll —
            // is a no-op by design rather than caller discipline.
            model.Arm(true);
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.WaitingToArrive));
            Assert.That(
                model.SpawnedPerchedWithoutArrival,
                Is.False);

            // Even deep in a later phase.
            model.Advance(1f, Input(farFromEverything: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ArrivalFlight));
            model.Arm(false);
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ArrivalFlight));
        }

        [Test]
        public void Arrival_WaitsForTheSessionToEndAndTheHeroToStepBack()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(false);

            // The seal commits inside an owned-camera session: no
            // flight may start into that shot.
            model.Advance(1f, Input(
                sessionActive: true,
                farFromEverything: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.WaitingToArrive));

            // Session over, but the hero stands at the mound: birds
            // do not land beside a standing man.
            model.Advance(1f, Input(distanceA: 3.5f, distanceB: 40f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.WaitingToArrive));
            model.Advance(1f, Input(distanceA: 40f, distanceB: 3.5f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.WaitingToArrive));

            // Only the small clearance is asked, never the return
            // gate: one step past 3.5 m of both perches and they come.
            model.Advance(1f, Input(distanceA: 3.6f, distanceB: 3.6f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ArrivalFlight));

            // The arrival lands through the long stagger and finishes
            // only when BOTH flights report done.
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.False);
            model.Advance(
                CemeteryRavenDirectorModel
                    .ReturnStaggerMaximumSeconds,
                Input(farFromEverything: true));
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.True);
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenBIndex),
                Is.True);

            model.Advance(1f, Input(
                farFromEverything: true,
                flightDoneA: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ArrivalFlight));
            model.Advance(1f, Input(
                farFromEverything: true,
                flightDoneA: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));
        }

        [Test]
        public void Startle_AtEitherPerchFlushesBothWithSeededStagger()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(true);

            // Beyond 3.5 m of both the pair sits.
            model.Advance(1f, Input(distanceA: 3.6f, distanceB: 3.6f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));

            // At 3.5 m of EITHER bird BOTH flush.
            model.Advance(1f, Input(distanceA: 20f, distanceB: 3.5f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Startled));

            // The takeoffs are split by the short seeded stagger, so
            // the pair never moves as one object.
            float staggerA = model.GetTakeoffStaggerSeconds(
                CemeteryRavenDirectorModel.RavenAIndex);
            float staggerB = model.GetTakeoffStaggerSeconds(
                CemeteryRavenDirectorModel.RavenBIndex);
            Assert.That(staggerA, Is.InRange(
                CemeteryRavenDirectorModel
                    .TakeoffStaggerMinimumSeconds,
                CemeteryRavenDirectorModel
                    .TakeoffStaggerMaximumSeconds));
            Assert.That(staggerB, Is.InRange(
                CemeteryRavenDirectorModel
                    .TakeoffStaggerMinimumSeconds,
                CemeteryRavenDirectorModel
                    .TakeoffStaggerMaximumSeconds));
            Assert.That(staggerA, Is.Not.EqualTo(staggerB));

            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.False);
            model.Advance(
                CemeteryRavenDirectorModel
                    .TakeoffStaggerMaximumSeconds,
                Input(distanceA: 3.5f, distanceB: 3.5f));
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.True);
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenBIndex),
                Is.True);

            // Both hidden past the fog: the pair is Away.
            model.Advance(8f, Input(
                distanceA: 3.5f,
                distanceB: 3.5f,
                flightDoneA: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));
        }

        [Test]
        public void Startle_IsSuppressedWhileASessionOwnsTheCamera()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(true);

            // The hero's transform parks at a worksite while he is
            // hidden: a flush nobody can see is an off-screen event.
            model.Advance(1f, Input(
                distanceA: 1f,
                distanceB: 1f,
                sessionActive: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));
            Assert.That(model.IsHeadTargetSuppressed, Is.True);

            // The moment the session releases him, the flush runs.
            model.Advance(1f, Input(distanceA: 1f, distanceB: 1f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Startled));
            Assert.That(model.IsHeadTargetSuppressed, Is.False);
        }

        [Test]
        public void Return_UsesTheWideGateAndTheHysteresisIsTheGapItself()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(true);
            model.Advance(1f, Input(distanceA: 2f, distanceB: 2f));
            model.Advance(1f, Input(
                distanceA: 2f,
                distanceB: 2f,
                flightDoneA: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));

            // 20 m from the crown is nowhere near far enough: only at
            // 70% of the visible slice do the birds trust the ground.
            model.Advance(1f, Input(
                farFromEverything: true,
                crownDistance: 20f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));

            // Nor does distance alone open the gate mid-session.
            model.Advance(1f, Input(
                farFromEverything: true,
                crownDistance: 40f,
                sessionActive: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));

            model.Advance(1f, Input(
                farFromEverything: true,
                crownDistance:
                    CemeteryRavenDirectorModel.ReturnDistanceMeters));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ReturnFlight));

            // The return staggers by most of a second and the two
            // touch down one after the other: first done opens the
            // Landing span, both done close it.
            float staggerA = model.GetReturnStaggerSeconds(
                CemeteryRavenDirectorModel.RavenAIndex);
            float staggerB = model.GetReturnStaggerSeconds(
                CemeteryRavenDirectorModel.RavenBIndex);
            Assert.That(staggerA, Is.InRange(
                CemeteryRavenDirectorModel
                    .ReturnStaggerMinimumSeconds,
                CemeteryRavenDirectorModel
                    .ReturnStaggerMaximumSeconds));
            Assert.That(staggerA, Is.Not.EqualTo(staggerB));

            model.Advance(1f, Input(
                farFromEverything: true,
                crownDistance: 40f,
                flightDoneA: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Landing));
            model.Advance(1f, Input(
                farFromEverything: true,
                crownDistance: 40f,
                flightDoneA: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));
        }

        [Test]
        public void RelocatingB_MovesTheGroundBirdAloneAndYieldsToAFlush()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(true);

            // B's ground was signed over (or a coffin now rests on
            // it): B alone moves, A keeps sitting, and the hero's
            // distance does not enter into it.
            model.Advance(1f, Input(
                farFromEverything: true,
                groundPerchDisplaced: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.RelocatingB));
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.False);
            Assert.That(
                model.IsFlightDue(
                    CemeteryRavenDirectorModel.RavenBIndex),
                Is.True);

            model.Advance(6f, Input(
                farFromEverything: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));

            // A same-frame startle outranks the relocation: both
            // flush, and B's return simply lands on the re-selected
            // ground — the displacement self-heals.
            model.Advance(1f, Input(
                distanceA: 2f,
                distanceB: 2f,
                groundPerchDisplaced: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Startled));
        }

        [Test]
        public void RelocatingB_WaitsOutAnActiveSession()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(true);

            // The displacement trigger (a job reaching Marked) fires
            // INSIDE an owned-camera session; a relocation flight
            // then would be exactly the off-screen event the other
            // guards forbid.
            model.Advance(1f, Input(
                farFromEverything: true,
                groundPerchDisplaced: true,
                sessionActive: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));

            model.Advance(1f, Input(
                farFromEverything: true,
                groundPerchDisplaced: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.RelocatingB));
        }

        [Test]
        public void Staggers_AreSeededPerRavenAndRejectABadIndex()
        {
            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            var same = new CemeteryRavenDirectorModel(SeedA, SeedB);

            // Same seeds, same staggers: the desync is authored by
            // the seed split, never by runtime chance.
            Assert.That(
                same.GetTakeoffStaggerSeconds(
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.EqualTo(model.GetTakeoffStaggerSeconds(
                    CemeteryRavenDirectorModel.RavenAIndex)));
            Assert.That(
                same.GetReturnStaggerSeconds(
                    CemeteryRavenDirectorModel.RavenBIndex),
                Is.EqualTo(model.GetReturnStaggerSeconds(
                    CemeteryRavenDirectorModel.RavenBIndex)));

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                model.GetTakeoffStaggerSeconds(2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                model.GetReturnStaggerSeconds(-1));
            Assert.That(model.IsFlightDue(0), Is.False);
        }

        [Test]
        public void Actor_AdoptsPivotsArticulatesAndWritesFlightRootMotion()
        {
            GameObject host = CreateGameObject("Raven Test Actor");
            CemeteryRavenRigAnchors anchors =
                CemeteryRavenTestRig.Create(host);
            var actor = host.AddComponent<CemeteryRavenActor>();
            actor.Initialize(anchors, 321, 0d);
            Assert.That(actor.IsInitialized, Is.True);

            // The wheelchair-pattern adopt: every mesh now rides the
            // pivot its binding names — beak and eyes on the head,
            // legs on the body root, each wing on its own shoulder.
            AssertAdopted(anchors, "GEO_Head", anchors.HeadPivot);
            AssertAdopted(anchors, "GEO_Beak", anchors.HeadPivot);
            AssertAdopted(anchors, "GEO_Eye.L", anchors.HeadPivot);
            AssertAdopted(
                anchors,
                "GEO_Wing.L",
                anchors.WingLeftPivot);
            AssertAdopted(
                anchors,
                "GEO_Wing.R",
                anchors.WingRightPivot);
            AssertAdopted(anchors, "GEO_Tail", anchors.TailPivot);
            AssertAdopted(
                anchors,
                "GEO_Leg.R",
                anchors.BodyRootPivot);

            // Perching is one write: the host root IS the feet point.
            var perch = new Vector3(3f, 1f, -2f);
            actor.SetPerched(perch, 90f);
            Assert.That(actor.IsPerched, Is.True);
            Assert.That(actor.transform.position, Is.EqualTo(perch));
            Assert.That(
                actor.transform.eulerAngles.y,
                Is.EqualTo(90f).Within(0.001f));

            // The bird faces the negation of the model root's axes
            // (the cat's rule); a hero to its side and within the
            // 18 m cutoff turns the head, clamped to the track limit.
            Vector3 ravenForward = -anchors.ModelRoot.forward;
            Vector3 ravenRight = -anchors.ModelRoot.right;
            actor.SetHeadTarget(
                true,
                perch + ravenRight * 2f + ravenForward * 0.5f);
            for (int step = 0; step < 20; step++)
            {
                actor.AdvancePresentation(0.05f);
            }

            Assert.That(actor.HeadYawDegrees, Is.Not.Zero);
            Assert.That(
                Mathf.Abs(actor.HeadYawDegrees),
                Is.LessThanOrEqualTo(
                    CemeteryRavenHeadModel
                        .DefaultMaxTrackYawDegrees));
            Assert.That(
                Quaternion.Angle(
                    anchors.HeadPivot.localRotation,
                    Quaternion.identity),
                Is.GreaterThan(1f));

            // Past the cutoff the same target counts as gone and the
            // head walks itself home — no bird leads a man through
            // the fog.
            actor.SetHeadTarget(true, perch + ravenRight * 30f);
            for (int step = 0; step < 20; step++)
            {
                actor.AdvancePresentation(0.05f);
            }

            Assert.That(
                Mathf.Abs(actor.HeadYawDegrees),
                Is.LessThanOrEqualTo(
                    CemeteryRavenHeadModel
                        .DefaultSettleErrorDegrees));

            // A takeoff owns the root: the actor itself writes the
            // path the pure model proved, so the host recedes and
            // climbs while the wings deploy.
            Vector3 start = actor.transform.position;
            actor.BeginFlight(new CemeteryRavenFlightModel(
                start,
                90f,
                start + new Vector3(46f, 8f, 0f),
                0f,
                CemeteryRavenFlightKind.Takeoff,
                99));
            Assert.That(actor.HasFlight, Is.True);
            Assert.That(actor.IsPerched, Is.False);
            for (int step = 0; step < 6; step++)
            {
                actor.AdvancePresentation(0.1f);
            }

            Assert.That(
                actor.transform.position.x - start.x,
                Is.GreaterThan(0.2f));
            Assert.That(
                actor.transform.position.y,
                Is.GreaterThan(start.y + 0.05f));
            Assert.That(
                Quaternion.Angle(
                    anchors.WingLeftPivot.localRotation,
                    Quaternion.identity),
                Is.GreaterThan(30f));

            // Done past the fog: the flight releases the root and the
            // renderers go dark.
            for (int step = 0;
                 step < 80 && !actor.IsFlightDone;
                 step++)
            {
                actor.AdvancePresentation(0.25f);
            }

            Assert.That(actor.IsFlightDone, Is.True);
            Assert.That(actor.HasFlight, Is.False);
            Assert.That(actor.IsPerched, Is.False);
            Assert.That(anchors.Renderers[0].enabled, Is.False);

            // The return lands on the model's own float-exact
            // endpoint, visible again, wings refolded, and hands the
            // root back to the perched idle.
            actor.BeginFlight(new CemeteryRavenFlightModel(
                perch + new Vector3(-30f, 7f, 20f),
                0f,
                perch,
                45f,
                CemeteryRavenFlightKind.Return,
                7));
            Assert.That(anchors.Renderers[0].enabled, Is.True);
            for (int step = 0;
                 step < 80 && !actor.IsFlightDone;
                 step++)
            {
                actor.AdvancePresentation(0.25f);
            }

            Assert.That(actor.IsFlightDone, Is.True);
            Assert.That(actor.IsPerched, Is.True);
            Assert.That(
                Vector3.Distance(actor.transform.position, perch),
                Is.LessThanOrEqualTo(0.001f));
            Assert.That(
                Mathf.DeltaAngle(
                    actor.transform.eulerAngles.y,
                    45f),
                Is.Zero.Within(0.01f));
            Assert.That(
                Quaternion.Angle(
                    anchors.WingLeftPivot.localRotation,
                    Quaternion.identity),
                Is.LessThan(0.5f));
            Assert.That(
                Quaternion.Angle(
                    anchors.WingRightPivot.localRotation,
                    Quaternion.identity),
                Is.LessThan(0.5f));
        }

        [Test]
        public void Controller_AddsNoLightsAndAtMostTwoAmbienceDetailVoices()
        {
            // The canon-budget guard on the LIVE controller: whatever
            // the pair does, it may add no light to the city's 12-light
            // budget and no more than its two AmbienceDetails voices.
            GameSessionState.BeginNewGame();
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
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

            GameObject surfaces =
                CreateGameObject("Raven Test Cemetery Surfaces");
            GameObject ground = CityCemeteryGroundWorldBuilder.Build(
                surfaces.transform,
                layout,
                null);
            CityCemeteryGroundExcavation excavation =
                CityCemeteryGroundExcavation.Attach(
                    surfaces,
                    layout,
                    ground);
            GameObject cityHost = CreateGameObject("Raven Test City");
            CemeteryGravediggingRegister register =
                CemeteryGravediggingRegister.Create(
                    cityHost.transform,
                    cemetery,
                    watchman,
                    excavation);

            GameObject hero = CreateGameObject("Raven Test Hero");
            hero.transform.position =
                CityCemeterySealedGraveWorldBuilder
                    .GetMoundCrownPoint(grave) +
                new Vector3(40f, 0f, 0f);
            GameObject cameraObject =
                CreateGameObject("Raven Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();

            CityCemeteryRavenController controller =
                CityCemeteryRavenController.Create(
                    cityHost.transform,
                    cemetery,
                    register,
                    null,
                    hero.transform,
                    camera,
                    GameSessionState.DefaultCitySeed);
            Assert.That(controller, Is.Not.Null);

            // EditMode has no player loop, so the poll is driven by
            // hand through the project's reflection idiom. The first
            // call arms (or degrades to inert when the prefab is not
            // built yet — an ambient bird must not break the guard),
            // the second exercises the armed path once.
            MethodInfo update =
                typeof(CityCemeteryRavenController).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(update, Is.Not.Null);
            update.Invoke(controller, null);
            update.Invoke(controller, null);

            Assert.That(
                controller.GetComponentsInChildren<Light>(true),
                Is.Empty,
                "The raven pair must add nothing to the city's " +
                "light budget.");
            AudioSource[] sources =
                controller.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources, Has.Length.LessThanOrEqualTo(2));
            if (controller.IsArmed)
            {
                Assert.That(sources, Has.Length.EqualTo(2));
            }

            for (int index = 0; index < sources.Length; index++)
            {
                Assert.That(
                    sources[index].maxDistance,
                    Is.EqualTo(
                        CemeteryRavenVoice.AudibleRadiusMeters));
                if (GameAudioMixer.IsAvailable)
                {
                    Assert.That(
                        sources[index].outputAudioMixerGroup,
                        Is.SameAs(
                            GameAudioMixer.AmbienceDetailsGroup));
                }
            }

            // The teardown guard the AlpineVillage soundscape taught:
            // OnDestroy must take the DestroyImmediate branch in
            // EditMode, or this very call leaks the voices.
            Object.DestroyImmediate(controller.gameObject);
            Assert.That(controller == null, Is.True);
        }

        private static void AssertAdopted(
            CemeteryRavenRigAnchors anchors,
            string rendererName,
            Transform expectedPivot)
        {
            for (int index = 0;
                 index < anchors.RendererBindings.Count;
                 index++)
            {
                CemeteryRavenRendererBinding binding =
                    anchors.RendererBindings[index];
                if (binding.RendererName == rendererName)
                {
                    Assert.That(
                        binding.Renderer.transform.parent,
                        Is.SameAs(expectedPivot),
                        rendererName);
                    return;
                }
            }

            Assert.Fail(
                $"The rig has no binding named '{rendererName}'.");
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            spawned.Add(gameObject);
            return gameObject;
        }

        /// <summary>One polled frame as plain values. Distances
        /// default to a hero standing well clear of everything so a
        /// test names only what it is about.</summary>
        private static CemeteryRavenDirectorInput Input(
            float distanceA = 10f,
            float distanceB = 10f,
            float crownDistance = 10f,
            bool sessionActive = false,
            bool groundPerchDisplaced = false,
            bool flightDoneA = false,
            bool flightDoneB = false,
            bool farFromEverything = false)
        {
            if (farFromEverything)
            {
                distanceA = 40f;
                distanceB = 40f;
            }

            return new CemeteryRavenDirectorInput(
                distanceA,
                distanceB,
                crownDistance,
                sessionActive,
                groundPerchDisplaced,
                flightDoneA,
                flightDoneB);
        }
    }
}
