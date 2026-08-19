using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CemeteryGravediggingTests
    {
        private readonly List<GameObject> spawned =
            new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < spawned.Count; index++)
            {
                if (spawned[index] != null)
                {
                    Object.DestroyImmediate(spawned[index]);
                }
            }

            spawned.Clear();
            GameSessionState.BeginNewGame();
        }

        [Test]
        public void DefaultCity_OffersOneJobOnAVacantPlotByTheWatchman()
        {
            Job job = CreateJob();

            Assert.That(job.Plan.IsPresent, Is.True);
            Assert.That(
                job.Plan.Plot.State,
                Is.EqualTo(CityCemeteryPlotState.Vacant));
            Assert.That(
                job.Cemetery.Plots.Any(plot =>
                    plot.StableId == job.Plan.Plot.StableId),
                Is.True);

            // He sends the hero to the nearest free plot he has, not
            // to whichever one happened to come first in the list.
            Vector3 post = job.Watchman.Stance.Position;
            float chosen = Planar(job.Plan.Plot.Ground, post);
            foreach (CityCemeteryPlotDescriptor plot in
                     job.Cemetery.Plots.Where(item => item.IsVacant))
            {
                Assert.That(
                    Planar(plot.Ground, post),
                    Is.GreaterThanOrEqualTo(chosen - 0.001f),
                    plot.StableId);
            }

            // Hole, collar and spoil all stay on the plot he signed
            // over, so digging can never disturb a neighbour.
            Rect work = job.Plan.WorkFootprint;
            Rect plotRect = job.Plan.Plot.Footprint;
            Assert.That(work.xMin, Is.GreaterThanOrEqualTo(plotRect.xMin));
            Assert.That(work.xMax, Is.LessThanOrEqualTo(plotRect.xMax));
            Assert.That(work.yMin, Is.GreaterThanOrEqualTo(plotRect.yMin));
            Assert.That(work.yMax, Is.LessThanOrEqualTo(plotRect.yMax));
            Assert.That(
                job.Plan.PitFloorY,
                Is.EqualTo(
                    job.Cemetery.GroundTopY -
                    CemeteryGravediggingPlan.PitDepthMeters)
                    .Within(0.0001f));
            Assert.DoesNotThrow(() =>
                CemeteryGravediggingPlan.ValidateOrThrow(job.Plan));

            // The lamp stands on the collar, on refilled earth rather
            // than over the void it is there to light, and the whole
            // fixture clears the mouth even turned off the axis.
            var lamp = new Vector2(
                job.Plan.LampGround.x,
                job.Plan.LampGround.z);
            Assert.That(job.Plan.PitMouth.Contains(lamp), Is.False);
            Assert.That(
                CityCemeteryPitWorldBuilder
                    .GetExcavationRect(job.Plan)
                    .Contains(lamp),
                Is.True);
            Assert.That(
                job.Plan.LampGround.y,
                Is.EqualTo(job.Plan.GroundTopY).Within(0.0001f));
            float reach = CityHandLampWorldBuilder.SpanMeters *
                          0.5f * Mathf.Sqrt(2f);
            Assert.That(
                Mathf.Min(
                    Mathf.Abs(lamp.x - job.Plan.PitMouth.xMin),
                    Mathf.Abs(lamp.x - job.Plan.PitMouth.xMax)),
                Is.GreaterThan(reach));
            Assert.That(
                Mathf.Min(
                    Mathf.Abs(lamp.y - job.Plan.PitMouth.yMin),
                    Mathf.Abs(lamp.y - job.Plan.PitMouth.yMax)),
                Is.GreaterThan(reach));

            // The stone waiting for this plot is one of the four
            // single-grave silhouettes, cut from fresh stone.
            Assert.That(
                new[]
                {
                    CityCemeteryGraveVariant.ClassicStele,
                    CityCemeteryGraveVariant.ArchedHeadstone,
                    CityCemeteryGraveVariant.OrthodoxCross,
                    CityCemeteryGraveVariant.Obelisk
                },
                Does.Contain(job.Plan.Monument));
            Assert.That(
                new[]
                {
                    CityCemeteryStyle.GraniteDark,
                    CityCemeteryStyle.MarbleLight
                },
                Does.Contain(job.Plan.MonumentStyle));

            // The heading is the plot's own, snapped to the axis the
            // hole is cut along.
            Vector3 heading = job.Plan.Heading * Vector3.forward;
            Assert.That(
                Mathf.Abs(job.Plan.RunsAlongX
                    ? heading.x
                    : heading.z),
                Is.EqualTo(1f).Within(0.0001f));

            // The same seed always sends him to the same grave.
            Job second = CreateJob();
            Assert.That(
                second.Plan.Plot.StableId,
                Is.EqualTo(job.Plan.Plot.StableId));
            Assert.That(
                second.Plan.PitMouth,
                Is.EqualTo(job.Plan.PitMouth));
        }

        [Test]
        public void Excavation_TakesTheGraveOutOfTheCemeteryPatches()
        {
            Job job = CreateJob();
            Rect cut = CityCemeteryPitWorldBuilder.GetExcavationRect(
                job.Plan);
            var cuts = new[] { cut };

            float before = 0f;
            float after = 0f;
            bool touched = false;
            foreach (CitySurfaceDescriptor surface in
                     job.Layout.Surfaces.Where(item =>
                         item.Kind == CitySurfaceKind.CemeteryGround))
            {
                List<Rect> plain = CityTerrainSurfaceWorldBuilder
                    .CreateSurfacePatches(job.Layout, surface, null);
                List<Rect> dug = CityTerrainSurfaceWorldBuilder
                    .CreateSurfacePatches(job.Layout, surface, cuts);
                before += plain.Sum(patch => patch.width * patch.height);
                after += dug.Sum(patch => patch.width * patch.height);
                foreach (Rect patch in dug)
                {
                    Assert.That(
                        patch.Overlaps(cut),
                        Is.False,
                        "No ground patch may survive inside the grave.");
                }

                touched |= surface.WorldBounds.Overlaps(cut);
            }

            Assert.That(touched, Is.True, "The grave must land on the cemetery.");
            Assert.That(
                before - after,
                Is.EqualTo(cut.width * cut.height).Within(0.001f),
                "Exactly the grave's rectangle leaves the ground.");
        }

        [Test]
        public void Excavation_RebuildsTheSlabWithARealHoleInIt()
        {
            Job job = CreateJob();
            var host = new GameObject("Test Cemetery Surfaces");
            spawned.Add(host);

            GameObject ground = CityCemeteryGroundWorldBuilder.Build(
                host.transform,
                job.Layout,
                null);
            Assert.That(ground, Is.Not.Null);
            Rect probe = Probe(job.Plan.PitMouth.center);
            Assert.That(
                CountGeometryOver(ground, probe),
                Is.GreaterThan(0),
                "The plot starts as solid ground.");

            CityCemeteryGroundExcavation excavation =
                CityCemeteryGroundExcavation.Attach(
                    host,
                    job.Layout,
                    ground);
            Assert.That(
                excavation.Excavate(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        job.Plan)),
                Is.True);

            Assert.That(excavation.Cuts.Count, Is.EqualTo(1));
            Assert.That(excavation.Ground, Is.Not.Null);
            Assert.That(
                excavation.Ground,
                Is.Not.SameAs(ground),
                "The slab is rebuilt, not patched over.");
            Assert.That(
                CountGeometryOver(excavation.Ground, probe),
                Is.EqualTo(0),
                "Nothing may be left standing over the open grave.");

            // A second cut into the same ground is refused: one hole
            // per grave, and no double-digging.
            Assert.That(
                excavation.Excavate(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        job.Plan)),
                Is.False);
            Assert.That(excavation.Cuts.Count, Is.EqualTo(1));

            // Filling it in puts the ground back exactly, and asking
            // again is not an error: the work is restored from a
            // stage, not from a list of holes.
            Assert.That(
                excavation.Fill(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        job.Plan)),
                Is.True);
            Assert.That(excavation.Cuts.Count, Is.EqualTo(0));
            Assert.That(
                CountGeometryOver(excavation.Ground, probe),
                Is.GreaterThan(0),
                "A filled grave is ground again.");
            Assert.That(
                excavation.Fill(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        job.Plan)),
                Is.True);
        }

        [Test]
        public void WatchmanOffer_IsAnsweredAndSurvivesARefusal()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(job, out _);
            CemeteryWatchmanInteraction watchman =
                CreateWatchman(controller);
            PlayerInteractor interactor = CreateInteractor();

            Assert.That(controller.CanOffer, Is.True);
            Assert.That(
                watchman.PromptKey,
                Is.EqualTo(CemeteryWatchmanInteraction.TalkPromptKey));

            // The first word out of him is the offer, not a quip.
            watchman.Interact(interactor);
            Assert.That(watchman.IsOffering, Is.True);
            Assert.That(
                watchman.PromptKey,
                Is.EqualTo(CemeteryWatchmanInteraction.OfferPromptKey));
            Assert.That(watchman.LastLineIndex, Is.EqualTo(-1));
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.DigTheGrave),
                Is.EqualTo(QuestStatus.NotStarted));

            // Refusing costs nothing: the hole still needs digging.
            Assert.That(watchman.Decline(), Is.True);
            Assert.That(watchman.Decline(), Is.False);
            Assert.That(watchman.IsOffering, Is.False);
            Assert.That(controller.CanOffer, Is.True);
            Assert.That(controller.Site, Is.Null);

            // Taking it puts the job in the log and marks the plot.
            watchman.Interact(interactor);
            Assert.That(watchman.IsOffering, Is.True);
            watchman.Interact(interactor);
            Assert.That(watchman.IsOffering, Is.False);
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.DigTheGrave),
                Is.EqualTo(QuestStatus.Active));
            Assert.That(controller.CanOffer, Is.False);
            Assert.That(controller.Site, Is.Not.Null);

            // And with the job taken he goes back to being himself.
            watchman.Interact(interactor);
            Assert.That(watchman.IsOffering, Is.False);
            Assert.That(watchman.LastLineIndex, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void AcceptedJob_IsThreeActsAndOnlyTheLastOneIsAGrave()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(
                    job,
                    out CityCemeteryGroundExcavation excavation);

            Assert.That(controller.HasJob, Is.True);
            Assert.That(controller.IsAccepted, Is.False);
            Assert.That(
                controller.TryAdvance(),
                Is.False,
                "Nobody digs a grave they were not asked to dig.");

            Assert.That(controller.TryAccept(), Is.True);
            Assert.That(controller.TryAccept(), Is.False);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Marked));

            CemeteryGraveDigSiteInteraction site = controller.Site;
            Assert.That(site, Is.Not.Null);
            Assert.That(
                site.PromptKey,
                Is.EqualTo(
                    CemeteryGraveDigSiteInteraction.DigPromptKey));
            Assert.That(
                site.InteractionPosition,
                Is.EqualTo(job.Plan.Ground));
            Assert.That(
                site.GetComponent<BoxCollider>().isTrigger,
                Is.True);

            // The marker breathes rather than blinks.
            site.ApplyPulse(0f);
            float low = site.LastPulse;
            site.ApplyPulse(
                CemeteryGraveDigSiteInteraction.PulsePeriodSeconds *
                0.5f);
            Assert.That(site.LastPulse, Is.GreaterThan(low));

            // Act one: the hole, and the lamp that lights it.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Dug));
            Assert.That(excavation.Cuts.Count, Is.EqualTo(1));
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.DigTheGrave),
                Is.EqualTo(QuestStatus.Active),
                "An open hole is not a finished grave.");
            Assert.That(controller.Site, Is.SameAs(site));
            Assert.That(
                site.PromptKey,
                Is.EqualTo(
                    CemeteryGraveDigSiteInteraction.CoffinPromptKey));
            Assert.That(
                site.GetComponentsInChildren<Renderer>().Length,
                Is.EqualTo(0),
                "The hole is its own marker now.");

            // The grave itself: earth lining the hole, a floor at the
            // bottom of it and the cap that keeps the hero out.
            Transform grave = controller.transform.Find(
                CityCemeteryPitWorldBuilder.RootName);
            Assert.That(grave, Is.Not.Null);
            Renderer earth = grave.GetComponentInChildren<Renderer>();
            Assert.That(earth, Is.Not.Null);
            Assert.That(
                earth.bounds.min.y,
                Is.LessThan(job.Plan.PitFloorY + 0.001f),
                "The grave reaches its own floor.");
            Assert.That(
                earth.GetComponent<MeshCollider>(),
                Is.Not.Null,
                "A grave you can fall through is not a grave.");
            Transform guard = grave.Find(
                CityCemeteryPitWorldBuilder.GuardName);
            Assert.That(guard, Is.Not.Null);
            Assert.That(
                guard.GetComponent<BoxCollider>().isTrigger,
                Is.False);

            // The lamp is not a lamp of its own: it is the fixture
            // the lake pier stands at the end of, set down here.
            Transform lampObject = controller.transform.Find(
                CemeteryGravediggingController.LampName);
            Assert.That(lampObject, Is.Not.Null);
            Assert.That(
                lampObject.Find("Hand Lamp Glass"),
                Is.Not.Null,
                "The grave lamp is the shared hand lamp.");
            Light lampLight =
                lampObject.GetComponentInChildren<Light>();
            Assert.That(lampLight, Is.Not.Null);
            Assert.That(
                lampLight.intensity,
                Is.EqualTo(CityHandLampWorldBuilder.NightIntensity)
                    .Within(0.0001f));
            Assert.That(
                lampLight.range,
                Is.EqualTo(CityHandLampWorldBuilder.Range)
                    .Within(0.0001f));
            Assert.That(
                lampLight.color,
                Is.EqualTo(CityHandLampWorldBuilder.LampColor));
            Assert.That(
                lampLight.range,
                Is.GreaterThan(
                    CemeteryGravediggingPlan.PitLengthMeters));
            Assert.That(
                lampObject.position.y,
                Is.EqualTo(job.Plan.GroundTopY).Within(0.001f),
                "It stands on the ground, not in it.");
            Assert.That(
                new Vector2(
                    lampObject.position.x - job.Plan.LampGround.x,
                    lampObject.position.z - job.Plan.LampGround.z)
                    .magnitude,
                Is.LessThan(0.001f));
            Assert.That(
                lampObject.GetComponentsInChildren<Collider>().Length,
                Is.EqualTo(0),
                "Nobody squeezes past a lamp beside a grave.");

            // Act two: the coffin, down at the bottom of the hole and
            // clear of every wall of it.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Coffined));
            Assert.That(
                site.PromptKey,
                Is.EqualTo(
                    CemeteryGraveDigSiteInteraction.FillPromptKey));
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.DigTheGrave),
                Is.EqualTo(QuestStatus.Active));
            Transform coffin = controller.transform.Find(
                CityCemeteryCoffinWorldBuilder.RootName);
            Assert.That(coffin, Is.Not.Null);
            Bounds box = Envelope(coffin);
            Assert.That(
                box.min.y,
                Is.GreaterThanOrEqualTo(job.Plan.PitFloorY - 0.001f),
                "It rests on the floor of the hole.");
            Assert.That(
                box.max.y,
                Is.LessThan(job.Plan.GroundTopY),
                "And stays down in it.");
            Rect mouth = job.Plan.PitMouth;
            Assert.That(box.min.x, Is.GreaterThan(mouth.xMin));
            Assert.That(box.max.x, Is.LessThan(mouth.xMax));
            Assert.That(box.min.z, Is.GreaterThan(mouth.yMin));
            Assert.That(box.max.z, Is.LessThan(mouth.yMax));
            Assert.That(
                coffin.GetComponentsInChildren<Collider>().Length,
                Is.EqualTo(0));

            // Act three, and only now is it a grave: the earth goes
            // back, the hole is gone and a stone stands at the head.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Sealed));
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.DigTheGrave),
                Is.EqualTo(QuestStatus.Completed));
            Assert.That(controller.Site, Is.Null);
            Assert.That(excavation.Cuts.Count, Is.EqualTo(0));
            Assert.That(
                controller.transform.Find(
                    CityCemeteryPitWorldBuilder.RootName),
                Is.Null,
                "There is no hole left to line.");
            Assert.That(
                controller.transform.Find(
                    CityCemeteryCoffinWorldBuilder.RootName),
                Is.Null,
                "The coffin is under the ground it was buried in.");
            Assert.That(
                controller.transform.Find(
                    CemeteryGravediggingController.LampName),
                Is.Null,
                "The lamp is picked up with the last spadeful.");
            Assert.That(controller.TryAdvance(), Is.False);

            Transform closed = controller.transform.Find(
                CityCemeterySealedGraveWorldBuilder.RootName);
            Assert.That(closed, Is.Not.Null);
            Assert.That(
                closed.Find(
                    CityCemeterySealedGraveWorldBuilder.MoundName),
                Is.Not.Null);
            Bounds standing = Envelope(closed);
            Assert.That(
                standing.max.y,
                Is.GreaterThan(job.Plan.GroundTopY + 1.0f),
                "A monument stands at the head of it.");

            // And it stays on its own plot, stone and mound alike.
            Rect plot = job.Plan.Plot.Footprint;
            Assert.That(standing.min.x, Is.GreaterThan(plot.xMin));
            Assert.That(standing.max.x, Is.LessThan(plot.xMax));
            Assert.That(standing.min.z, Is.GreaterThan(plot.yMin));
            Assert.That(standing.max.z, Is.LessThan(plot.yMax));

            // The stone is one of the yard's own, not a second
            // vocabulary that only looks like one.
            foreach (CityCemeteryPartDescriptor part in
                     CityCemeterySealedGraveWorldBuilder
                         .CreateMonumentParts(job.Plan))
            {
                Assert.That(
                    part.Variant,
                    Is.EqualTo(job.Plan.Monument));
                Assert.That(
                    part.Kind,
                    Is.Not.EqualTo(CityCemeteryPartKind.GraveSlab),
                    "A slab comes years later, not the same hour.");
            }
        }

        [Test]
        public void EveryStageIsRebuiltFromTheStageAlone()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(job, out _);
            Assert.That(controller.TryAccept(), Is.True);
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(controller.TryAdvance(), Is.True);

            // The hero goes indoors with the coffin already down.
            CemeteryGravediggingController rebuilt =
                CreateController(job, out _);
            Assert.That(
                rebuilt.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Coffined));
            Assert.That(rebuilt.Site, Is.Not.Null);
            Assert.That(
                rebuilt.Site.PromptKey,
                Is.EqualTo(
                    CemeteryGraveDigSiteInteraction.FillPromptKey));
            Assert.That(
                rebuilt.transform.Find(
                    CityCemeteryPitWorldBuilder.RootName),
                Is.Not.Null);
            Assert.That(
                rebuilt.transform.Find(
                    CityCemeteryCoffinWorldBuilder.RootName),
                Is.Not.Null);
            Assert.That(
                rebuilt.transform.Find(
                    CemeteryGravediggingController.LampName),
                Is.Not.Null);

            // And again with the grave closed: no hole, no coffin, no
            // worksite — a mound and a stone.
            Assert.That(rebuilt.TryAdvance(), Is.True);
            CemeteryGravediggingController finished =
                CreateController(job, out _);
            Assert.That(
                finished.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Sealed));
            Assert.That(finished.Site, Is.Null);
            Assert.That(
                finished.transform.Find(
                    CityCemeteryPitWorldBuilder.RootName),
                Is.Null);
            Assert.That(
                finished.transform.Find(
                    CityCemeterySealedGraveWorldBuilder.RootName),
                Is.Not.Null);
            Assert.That(
                finished.transform.Find(
                    CemeteryGravediggingController.LampName),
                Is.Null,
                "A closed grave needs nothing lit over it.");
        }

        [Test]
        public void FinishedGrave_IsPaidForOnceAtTheWatchmansWindow()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(job, out _);
            CemeteryWatchmanInteraction watchman =
                CreateWatchman(controller);
            PlayerInteractor interactor = CreateInteractor();

            watchman.Interact(interactor);
            watchman.Interact(interactor);
            Assert.That(controller.IsAccepted, Is.True);

            // Unfinished work buys nothing: he only talks.
            int wallet = GameSessionState.CashBalance;
            Assert.That(controller.TryCollectWage(), Is.False);
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(controller.CanCollectWage, Is.False);
            watchman.Interact(interactor);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(wallet));
            Assert.That(
                watchman.LastLineIndex,
                Is.GreaterThanOrEqualTo(0));

            // Closed and stoned, and the next word out of him is the
            // wage.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(controller.CanCollectWage, Is.True);
            watchman.Interact(interactor);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(
                    wallet + CemeteryGravediggingController.Wage));
            Assert.That(controller.IsPaid, Is.True);

            // And he does not pay twice for one grave.
            int lastLine = watchman.LastLineIndex;
            watchman.Interact(interactor);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(
                    wallet + CemeteryGravediggingController.Wage));
            Assert.That(controller.TryCollectWage(), Is.False);
            Assert.That(
                watchman.LastLineIndex,
                Is.Not.EqualTo(lastLine),
                "He is back to being the same old man.");
        }

        // ------------------------------------------------------------

        private readonly struct Job
        {
            public Job(
                CityLayout layout,
                CityCemeteryPlan cemetery,
                CemeteryWatchmanPlan watchman,
                CemeteryGravediggingPlan plan)
            {
                Layout = layout;
                Cemetery = cemetery;
                Watchman = watchman;
                Plan = plan;
            }

            public CityLayout Layout { get; }
            public CityCemeteryPlan Cemetery { get; }
            public CemeteryWatchmanPlan Watchman { get; }
            public CemeteryGravediggingPlan Plan { get; }
        }

        private static Job CreateJob()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityCemeteryPlan cemetery =
                CityCemeteryPlanner.Create(layout);
            CemeteryWatchmanPlan watchman =
                CemeteryWatchmanPlan.Create(cemetery);
            return new Job(
                layout,
                cemetery,
                watchman,
                CemeteryGravediggingPlan.Create(cemetery, watchman));
        }

        private CemeteryGravediggingController CreateController(
            Job job,
            out CityCemeteryGroundExcavation excavation)
        {
            var host = new GameObject("Test Cemetery Surfaces");
            spawned.Add(host);
            GameObject ground = CityCemeteryGroundWorldBuilder.Build(
                host.transform,
                job.Layout,
                null);
            excavation = CityCemeteryGroundExcavation.Attach(
                host,
                job.Layout,
                ground);

            var root = new GameObject("Test City");
            spawned.Add(root);
            return CemeteryGravediggingController.Create(
                root.transform,
                job.Plan,
                excavation);
        }

        private CemeteryWatchmanInteraction CreateWatchman(
            CemeteryGravediggingController controller)
        {
            var host = new GameObject("Test Watchman Talk");
            spawned.Add(host);
            var watchman =
                host.AddComponent<CemeteryWatchmanInteraction>();
            watchman.Initialize(
                Vector3.zero,
                GameSessionState.DefaultCitySeed);
            watchman.AttachGravedigging(controller);
            return watchman;
        }

        private PlayerInteractor CreateInteractor()
        {
            var host = new GameObject("Test Interactor");
            spawned.Add(host);
            return host.AddComponent<PlayerInteractor>();
        }

        /// <summary>The world box every renderer under a root
        /// actually occupies.</summary>
        private static Bounds Envelope(Transform root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static float Planar(Vector3 point, Vector3 other)
        {
            return new Vector2(
                point.x - other.x,
                point.z - other.z).magnitude;
        }

        /// <summary>A hand's width of ground at a point, the area
        /// the hero would be standing on.</summary>
        private static Rect Probe(Vector2 point)
        {
            const float half = 0.1f;
            return Rect.MinMaxRect(
                point.x - half,
                point.y - half,
                point.x + half,
                point.y + half);
        }

        /// <summary>
        /// Triangles of a built slab that span a patch of ground.
        /// A bounds check cannot answer "is there still ground here",
        /// and neither can a vertex check: the slab is built from
        /// boxes whose faces are two triangles wide, so the question
        /// is whether any triangle still covers the spot.
        /// </summary>
        private static int CountGeometryOver(
            GameObject slab,
            Rect area)
        {
            Mesh mesh = slab.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int count = 0;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                Rect span = Rect.MinMaxRect(
                    Mathf.Min(first.x, Mathf.Min(second.x, third.x)),
                    Mathf.Min(first.z, Mathf.Min(second.z, third.z)),
                    Mathf.Max(first.x, Mathf.Max(second.x, third.x)),
                    Mathf.Max(first.z, Mathf.Max(second.z, third.z)));
                if (span.Overlaps(area))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
