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
        public void AcceptedJob_MarksThePlotAndDiggingOpensTheGrave()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(
                    job,
                    out CityCemeteryGroundExcavation excavation);

            Assert.That(controller.HasJob, Is.True);
            Assert.That(controller.IsAccepted, Is.False);
            Assert.That(
                controller.TryDig(),
                Is.False,
                "Nobody digs a grave they were not asked to dig.");

            Assert.That(controller.TryAccept(), Is.True);
            Assert.That(controller.TryAccept(), Is.False);
            Assert.That(controller.IsAccepted, Is.True);

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

            Assert.That(controller.TryDig(), Is.True);
            Assert.That(controller.IsDug, Is.True);
            Assert.That(controller.Site, Is.Null);
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.DigTheGrave),
                Is.EqualTo(QuestStatus.Completed));
            Assert.That(controller.TryDig(), Is.False);
            Assert.That(excavation.Cuts.Count, Is.EqualTo(1));

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
