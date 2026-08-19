using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The four acts of the gravedigging as games rather than as
    /// presses.
    ///
    /// Everything worth holding to a rule here is pure: the lattice
    /// that divides the hole and decides which corner may be worked
    /// next, the swing that judges a strike, the two ropes, the plumb
    /// of the stone, and the geometry those first two are drawn over.
    /// None of it needs a scene, and none of it is checked by looking
    /// at the screen — which matters, because the panel itself is
    /// IMGUI and cannot be captured headlessly at all.
    /// </summary>
    public sealed class CemeteryGraveWorkTests
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
        public void TheLatticeRefusesAPillarAndNeverDeadlocks()
        {
            var lattice = new CemeteryGraveLatticeModel(
                CemeteryGraveLatticeMode.Digging);

            // Take one corner down as far as the rule allows, then
            // find it shut while its neighbours are still shallow.
            int taken = 0;
            while (lattice.IsWorkable(0) && taken < 12)
            {
                if (Strike(lattice, 0))
                {
                    taken++;
                }
            }

            Assert.That(
                lattice.GetCoursesDone(0),
                Is.EqualTo(1),
                "A segment may not get ahead of its shallowest " +
                "neighbour.");
            Assert.That(lattice.IsWorkable(0), Is.False);
            Assert.That(
                lattice.FindWorkable(0, 1),
                Is.Not.EqualTo(-1),
                "There is always somewhere left to put the spade.");

            // And the whole hole can still be finished from here, one
            // legal strike at a time.
            int guard = 0;
            while (!lattice.IsComplete && guard < 2000)
            {
                guard++;
                int segment = lattice.FindWorkable(0, 1);
                Assert.That(segment, Is.Not.EqualTo(-1));
                Strike(lattice, segment);
            }

            Assert.That(lattice.IsComplete, Is.True);
            Assert.That(
                lattice.Progress01,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                lattice.FindWorkable(0, 1),
                Is.EqualTo(-1),
                "A finished hole offers no corner at all.");
            for (int segment = 0;
                 segment < CemeteryGraveLatticeModel.SegmentCount;
                 segment++)
            {
                Assert.That(
                    lattice.GetCoursesDone(segment),
                    Is.EqualTo(
                        CemeteryGraveLatticeModel.CoursesPerSegment));
            }
        }

        [Test]
        public void OnlyABiteTakesEarthAndABlockedSquareRefusesIt()
        {
            var lattice = new CemeteryGraveLatticeModel(
                CemeteryGraveLatticeMode.Digging);

            // The top course is turf everywhere: one strike, and a
            // graze is worth nothing at all.
            Assert.That(
                lattice.TryStrike(
                    0,
                    CemeteryStrokeOutcome.Graze,
                    out bool grazed),
                Is.True);
            Assert.That(grazed, Is.False);
            Assert.That(lattice.GetCoursesDone(0), Is.EqualTo(0));

            Assert.That(
                lattice.TryStrike(
                    0,
                    CemeteryStrokeOutcome.Bite,
                    out bool bit),
                Is.True);
            Assert.That(bit, Is.True);
            Assert.That(lattice.GetCoursesDone(0), Is.EqualTo(1));

            // A blocked segment refuses the stroke outright rather
            // than swallowing it.
            Assert.That(
                lattice.TryStrike(
                    0,
                    CemeteryStrokeOutcome.Bite,
                    out _),
                Is.False);

        }

        [Test]
        public void TheOnlyTimedSwingLeftIsWideEnoughToHit()
        {
            // Digging and filling are a choice of square and a press
            // now, so the only swing still timed is the three blows
            // that set the stone. The marker is a sine and runs
            // fastest exactly where the window is, so what matters is
            // the time in hand, measured off the model.
            CemeterySwingProfile tamp =
                CemeteryStoneSettleSettings.Default.TampProfile;
            float window = MeasureBiteWindowSeconds(tamp);
            Assert.That(
                window,
                Is.GreaterThan(0.12f),
                "The last swing of the job gives only " +
                Mathf.RoundToInt(window * 1000f) +
                " ms, which is a handful of frames.");
            Assert.That(
                window,
                Is.LessThan(0.40f),
                "And it is wide enough that it stops being timing.");
        }

        [Test]
        public void ASwingRunsTheBarAndOnlyAReleaseStrikes()
        {
            var model = new CemeteryStrokeModel();
            Assert.That(
                model.Release(),
                Is.EqualTo(CemeteryStrokeOutcome.None),
                "Nothing was swung.");

            model.Begin(
                CemeteryStoneSettleSettings.Default.TampProfile,
                12345);
            Assert.That(model.IsSwinging, Is.True);

            // It reaches both ends of the bar inside one sweep, and it
            // does that whether the frames are even or ragged.
            float lowest = 1f;
            float highest = -1f;
            for (int step = 0; step < 400; step++)
            {
                model.Advance(1f / 90f);
                lowest = Mathf.Min(lowest, model.Position);
                highest = Mathf.Max(highest, model.Position);
            }

            Assert.That(lowest, Is.LessThan(-0.9f));
            Assert.That(highest, Is.GreaterThan(0.9f));
            Assert.That(model.Sweeps, Is.GreaterThan(0));

            model.Cancel();
            Assert.That(model.IsSwinging, Is.False);
            Assert.That(
                model.Release(),
                Is.EqualTo(CemeteryStrokeOutcome.None));
        }

        [Test]
        public void NoWayOfNotPlayingLowersTheCoffin()
        {
            // Every one of these was, at some point in tuning, a way
            // to finish act two without looking at it. Holding both
            // keys was the one that shipped, and it is why the balance
            // point moves at all.
            AssertLosesTheBox(
                (tilt) => new[] { false, false },
                "Touching nothing at all — the box does not move, " +
                "but level moves out from under it.");
            AssertLosesTheBox(
                (tilt) => new[] { true, true },
                "Holding both keys down and waiting it out.");
            AssertLosesTheBox(
                (tilt) => new[] { true, false },
                "Paying out the head for the whole descent.");
            AssertLosesTheBox(
                (tilt) => new[] { false, true },
                "Paying out the foot for the whole descent.");
        }

        [Test]
        public void FollowingTheBalancePointLandsIt()
        {
            // And it lands for a hand that is twitchy and for one that
            // is a quarter of a second slow, because a window only one
            // reaction speed wide is not a mechanic.
            foreach (float deadzone in new[] { 0.02f, 0.05f, 0.09f })
            {
                foreach (float delay in new[] { 0f, 0.12f, 0.25f })
                {
                    foreach (int seed in new[] { 3, 7, 29, 101 })
                    {
                        AssertLandsByChasing(deadzone, delay, seed);
                    }
                }
            }
        }

        [Test]
        public void TheBalancePointNeverStandsStillAndNeverOutrunsHim()
        {
            CemeteryCoffinLowerSettings settings =
                CemeteryCoffinLowerSettings.Default;

            // Wider than the tolerance, or standing still would be
            // safe — and standing still has to lose, because it is the
            // ground moving and not the box.
            Assert.That(
                settings.DriftAmplitude,
                Is.GreaterThan(settings.TiltTolerance),
                "A point that never leaves the safe band is not a " +
                "point that has to be chased.");

            // And slower than the ropes can chase it, or chasing it
            // would be impossible rather than hard.
            // Measured rather than derived: what matters for chasing
            // is how fast the point moves on average, not the instant
            // at the middle of a swing where it is briefly quickest.
            // The bound is the *slower* rope, because that is the one
            // that has to catch it going the other way.
            var probe = new CemeteryCoffinLowerModel(settings, 7);
            const float probeStep = 1f / 120f;
            float previous = probe.Drift;
            float travelled = 0f;
            for (int index = 0; index < 1200; index++)
            {
                probe.Advance(probeStep, true, true);
                travelled += Mathf.Abs(probe.Drift - previous);
                previous = probe.Drift;
            }

            Assert.That(
                travelled / (1200f * probeStep),
                Is.LessThan(settings.FootPayRate),
                "The point must crawl slower than the slower rope " +
                "can move the balance, or chasing it is not hard, " +
                "it is impossible.");
            Assert.That(
                settings.HeadPayRate,
                Is.GreaterThan(settings.FootPayRate),
                "The shoulders are the heavy end.");

            // And it really does wander while the box is in the air,
            // rather than being a constant offset that could be set
            // once and forgotten.
            var model = new CemeteryCoffinLowerModel(settings, 7);
            float low = model.Drift;
            float high = model.Drift;
            for (int step = 0;
                 step < 2400 && !model.IsComplete;
                 step++)
            {
                model.Advance(1f / 120f, true, true);
                low = Mathf.Min(low, model.Drift);
                high = Mathf.Max(high, model.Drift);
            }

            Assert.That(
                high - low,
                Is.GreaterThan(settings.TiltTolerance * 0.5f),
                "The point has to move noticeably inside one " +
                "descent, not merely over a long enough window.");
        }

        [Test]
        public void TheStoneIsHeavedUpAndThenDrivenHome()
        {
            var model = new CemeteryStoneSettleModel(
                CemeteryStoneSettleSettings.Default);
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryStonePhase.Raising));

            // Heaving is repeated effort and nothing else: no window,
            // no failure, every press worth the same.
            int presses = 0;
            while (model.Phase == CemeteryStonePhase.Raising &&
                   presses < 100)
            {
                presses++;
                model.Press();
            }

            Assert.That(
                presses,
                Is.InRange(8, 24),
                "Standing a stone up should be a steady effort, not " +
                "a single tap and not a marathon.");
            Assert.That(
                model.Lift01,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryStonePhase.Setting));

            // Blows are timed, and a missed one costs only the swing.
            model.Strike(CemeteryStrokeOutcome.Graze);
            model.Strike(CemeteryStrokeOutcome.Jar);
            Assert.That(
                model.StrikesLanded,
                Is.EqualTo(0),
                "Only a clean blow drives it.");
            Assert.That(model.IsComplete, Is.False);

            for (int index = 0;
                 index <
                 CemeteryStoneSettleSettings.Default.StrikesRequired;
                 index++)
            {
                model.Strike(CemeteryStrokeOutcome.Bite);
            }

            Assert.That(model.IsComplete, Is.True);
            Assert.That(
                model.Set01,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void AStoneLeftAloneSettlesBackButNeverFails()
        {
            var model = new CemeteryStoneSettleModel(
                CemeteryStoneSettleSettings.Default);
            model.Press();
            model.Press();
            model.Press();
            float gained = model.Lift01;
            Assert.That(gained, Is.GreaterThan(0f));

            // Let go and it sags — gently, and never past the floor.
            for (int step = 0; step < 1200; step++)
            {
                model.Advance(1f / 120f);
            }

            Assert.That(model.Lift01, Is.LessThan(gained));
            Assert.That(
                model.Lift01,
                Is.GreaterThanOrEqualTo(0f),
                "It comes to rest on the grass, not through it.");
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryStonePhase.Raising),
                "There is no way to fail the last act of the job.");

            // The sag is slow enough that ordinary pressing gains on
            // it, or the half would be unwinnable rather than heavy.
            CemeteryStoneSettleSettings settings =
                CemeteryStoneSettleSettings.Default;
            Assert.That(
                settings.PressLift,
                Is.GreaterThan(settings.SagRate * 0.5f),
                "One press has to be worth more than half a second " +
                "of letting go.");
        }

        [Test]
        public void AnEpitaphIsShortOrItIsNotAnEpitaph()
        {
            Assert.That(CemeteryEpitaph.CountWords(null), Is.EqualTo(0));
            Assert.That(CemeteryEpitaph.CountWords("   "), Is.EqualTo(0));
            Assert.That(
                CemeteryEpitaph.CountWords("  спи   спокойно "),
                Is.EqualTo(2),
                "Runs of whitespace are one gap, not several words.");

            Assert.That(
                CemeteryEpitaph.IsWithinLimits(string.Empty),
                Is.True,
                "A man may have nothing to say over a stranger.");
            Assert.That(
                CemeteryEpitaph.IsWithinLimits("один два три"),
                Is.True);

            var tooMany = new System.Text.StringBuilder();
            for (int index = 0;
                 index <= CemeteryEpitaph.MaximumWords;
                 index++)
            {
                tooMany.Append("сло ");
            }

            Assert.That(
                CemeteryEpitaph.IsWithinLimits(tooMany.ToString()),
                Is.False);

            // What is kept is exactly what the limits allow, and it is
            // trimmed rather than refused.
            string cut = CemeteryEpitaph.Sanitize(tooMany.ToString());
            Assert.That(
                CemeteryEpitaph.CountWords(cut),
                Is.EqualTo(CemeteryEpitaph.MaximumWords));
            Assert.That(
                CemeteryEpitaph.IsWithinLimits(cut),
                Is.True,
                "Sanitising has to produce something the limits " +
                "would have accepted in the first place.");
            Assert.That(
                cut.Length,
                Is.LessThanOrEqualTo(
                    CemeteryEpitaph.MaximumCharacters));

            // One very long word is still cut down: the board has a
            // width and the word count alone does not know it.
            string wall = new string('ы', 400);
            Assert.That(
                CemeteryEpitaph.Sanitize(wall).Length,
                Is.LessThanOrEqualTo(
                    CemeteryEpitaph.MaximumCharacters));
        }

        [Test]
        public void ThePlaqueIsWrittenOnceAndThenItIsCut()
        {
            Assert.That(
                GameSessionState.GraveEpitaph,
                Is.Empty,
                "A new game starts with a bare board.");
            Assert.That(
                GameSessionState.TrySetGraveEpitaph("   "),
                Is.False,
                "Whitespace is not an inscription.");

            Assert.That(
                GameSessionState.TrySetGraveEpitaph(
                    "  спи   спокойно, незнакомец  "),
                Is.True);
            Assert.That(
                GameSessionState.GraveEpitaph,
                Is.EqualTo("спи спокойно, незнакомец"),
                "It is kept as the plaque would carry it.");

            Assert.That(
                GameSessionState.TrySetGraveEpitaph("другое"),
                Is.False,
                "Nobody goes back and revises a stranger's epitaph.");
            Assert.That(
                GameSessionState.GraveEpitaph,
                Is.EqualTo("спи спокойно, незнакомец"));

            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.GraveEpitaph,
                Is.Empty,
                "A new game is a new grave.");
        }

        [Test]
        public void TheSegmentsTileTheMouthAndTheFaceWalksTheDepth()
        {
            CemeteryGravediggingPlan plan = CreatePlan();
            var lattice = new CemeteryGraveLatticeModel(
                CemeteryGraveLatticeMode.Digging);

            float area = 0f;
            Rect mouth = plan.PitMouth;
            for (int segment = 0;
                 segment < CemeteryGraveLatticeModel.SegmentCount;
                 segment++)
            {
                Rect patch =
                    CityCemeteryProgressivePitWorldBuilder
                        .GetSegmentRect(plan, segment);
                area += patch.width * patch.height;
                Assert.That(
                    patch.xMin,
                    Is.GreaterThanOrEqualTo(mouth.xMin - 0.0001f));
                Assert.That(
                    patch.xMax,
                    Is.LessThanOrEqualTo(mouth.xMax + 0.0001f));
                Assert.That(
                    patch.yMin,
                    Is.GreaterThanOrEqualTo(mouth.yMin - 0.0001f));
                Assert.That(
                    patch.yMax,
                    Is.LessThanOrEqualTo(mouth.yMax + 0.0001f));
                Assert.That(
                    CityCemeteryProgressivePitWorldBuilder
                        .GetSegmentFaceY(plan, lattice, segment),
                    Is.EqualTo(plan.GroundTopY).Within(0.0001f),
                    "Nothing dug yet is ground level.");
            }

            Assert.That(
                area,
                Is.EqualTo(mouth.width * mouth.height).Within(0.0001f),
                "The segments tile the mouth exactly.");

            // The last course of a segment lands on the floor, not a
            // sliver above it. Hard ground is worth more than one
            // strike, so this works the segment until it is out
            // rather than counting swings.
            DigDownTo(
                lattice,
                0,
                CemeteryGraveLatticeModel.CoursesPerSegment);
            Assert.That(
                lattice.GetCoursesDone(0),
                Is.EqualTo(
                    CemeteryGraveLatticeModel.CoursesPerSegment));
            Assert.That(
                CityCemeteryProgressivePitWorldBuilder
                    .GetSegmentFaceY(plan, lattice, 0),
                Is.EqualTo(plan.PitFloorY).Within(0.0001f));
        }

        [Test]
        public void TheHeapIsWhateverIsNotInTheGround()
        {
            var digging = new CemeteryGraveLatticeModel(
                CemeteryGraveLatticeMode.Digging);
            var filling = new CemeteryGraveLatticeModel(
                CemeteryGraveLatticeMode.Filling);

            Assert.That(
                CityCemeteryProgressivePitWorldBuilder
                    .GetHeapFullness(digging),
                Is.EqualTo(0f).Within(0.0001f),
                "Nothing is out of the ground yet.");
            Assert.That(
                CityCemeteryProgressivePitWorldBuilder
                    .GetHeapFullness(filling),
                Is.EqualTo(1f).Within(0.0001f),
                "Everything is still on the heap.");

            Finish(digging);
            Finish(filling);
            Assert.That(
                CityCemeteryProgressivePitWorldBuilder
                    .GetHeapFullness(digging),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                CityCemeteryProgressivePitWorldBuilder
                    .GetHeapFullness(filling),
                Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void TheShotLooksIntoTheHoleFromTheDrySideOfIt()
        {
            CemeteryGravediggingPlan plan = CreatePlan();
            Vector3 work =
                CemeteryGraveWorkStance.GetWorkDirection(plan);
            Vector3 stand =
                CemeteryGraveWorkStance.GetStandPosition(plan);

            Assert.That(
                work.magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                Vector3.Dot(
                    work,
                    (plan.SpoilCenter - plan.Ground).normalized),
                Is.GreaterThan(0.99f),
                "He throws toward the heap.");
            Assert.That(
                Vector3.Dot(
                    (plan.Ground - stand).normalized,
                    work),
                Is.GreaterThan(0.99f),
                "And stands on the far side of the hole from it.");
            Assert.That(
                stand.y,
                Is.EqualTo(plan.GroundTopY).Within(0.0001f));
            Assert.That(
                new Vector2(
                    stand.x - plan.Ground.x,
                    stand.z - plan.Ground.z).magnitude,
                Is.GreaterThan(
                    CemeteryGravediggingPlan.PitWidthMeters * 0.5f),
                "Not standing in his own hole.");

            CemeteryGraveWorkStance.EvaluateCamera(
                plan,
                CemeteryGraveWorkStance.GetRestingFocus(plan),
                out Vector3 position,
                out Quaternion rotation);
            Assert.That(
                position.y - plan.GroundTopY,
                Is.EqualTo(CemeteryGraveWorkStance.EyeHeightMeters)
                    .Within(0.0001f),
                "The shot is taken from his own eye line.");
            Vector3 forward = rotation * Vector3.forward;
            Assert.That(
                forward.y,
                Is.LessThan(0f),
                "It looks down into the grave.");
            Assert.That(
                Vector3.Dot(
                    new Vector3(forward.x, 0f, forward.z).normalized,
                    work),
                Is.GreaterThan(0.9f));
        }

        [Test]
        public void TheCoffinAndSpadeWaitClearOfTheWork()
        {
            CemeteryGravediggingPlan plan = CreatePlan();
            var coffin = new Vector2(
                plan.CoffinRestGround.x,
                plan.CoffinRestGround.z);
            var spade = new Vector2(
                plan.SpadeRestGround.x,
                plan.SpadeRestGround.z);

            // Not on the hole, not on its collar, not in the heap —
            // the three places the hero has to be able to work.
            Assert.That(
                plan.WorkFootprint.Contains(coffin),
                Is.False,
                "The coffin must not wait on the worksite.");
            Assert.That(
                plan.WorkFootprint.Contains(spade),
                Is.False,
                "Nor the spade.");
            Assert.That(
                plan.PitMouth.Contains(coffin),
                Is.False);
            Assert.That(
                plan.SpoilFootprint.Contains(spade),
                Is.False);

            // Both sit past the foot of the grave, which is the only
            // clear ground on the plot: the head end carries the lamp
            // and the two flanks carry the heap and the digger.
            Vector3 toCoffin = plan.CoffinRestGround - plan.Ground;
            Vector3 alongGrave = plan.Heading * Vector3.forward;
            Assert.That(
                Vector3.Dot(toCoffin, alongGrave),
                Is.LessThan(0f),
                "The coffin waits below the feet, not above the head.");
            Assert.That(
                Vector3.Dot(
                    plan.LampGround - plan.Ground,
                    alongGrave),
                Is.GreaterThan(0f),
                "And the lamp is at the other end from it.");

            // Near enough to read as belonging to this grave.
            Assert.That(
                new Vector2(
                    toCoffin.x,
                    toCoffin.z).magnitude,
                Is.LessThan(2.4f));
            Assert.That(
                Vector2.Distance(coffin, spade),
                Is.LessThan(1.8f),
                "The tools are set down together.");

            // Past the end of the box, not through the lid of it.
            Vector3 coffinLength = Quaternion.Euler(
                0f,
                plan.CoffinRestYawDegrees,
                0f) * Vector3.forward;
            Vector3 apart = plan.SpadeRestGround -
                            plan.CoffinRestGround;
            Assert.That(
                Mathf.Abs(
                    (apart.x * coffinLength.x) +
                    (apart.z * coffinLength.z)),
                Is.GreaterThan(
                    CemeteryGravediggingPlan.CoffinHalfSpanMeters),
                "The spade must stand clear of the coffin's outline.");
            Assert.That(
                plan.CoffinRestGround.y,
                Is.EqualTo(plan.GroundTopY).Within(0.0001f));

            // The spade is driven in, not floating over the grass.
            Assert.That(
                CityGravediggerShovelWorldBuilder
                    .GetRestPosition(plan).y,
                Is.LessThan(plan.GroundTopY));
        }

        [Test]
        public void ThereIsOnlyEverOneSpadeOnTheWorksite()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(job);
            Assert.That(controller.TryAccept(), Is.True);

            // Taking the job stands one spade beside the plot, and it
            // is the same object the digging act will animate — the
            // work borrows it rather than raising a second.
            Assert.That(
                CountSpades(controller.transform),
                Is.EqualTo(1),
                "A worksite has one spade on it.");
            CemeteryShovelAnimator spade = controller.Spade;
            Assert.That(spade, Is.Not.Null);
            Assert.That(
                spade.enabled,
                Is.False,
                "It is parked until somebody picks it up.");
            Assert.That(
                spade.transform.position.y,
                Is.LessThan(job.Plan.GroundTopY),
                "Blade in the ground.");

            // It survives the acts that use it and is only tidied away
            // with the lamp when the earth goes back.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                CountSpades(controller.transform),
                Is.EqualTo(1));
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                CountSpades(controller.transform),
                Is.EqualTo(1),
                "Still one, with the coffin down.");
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Filled));
            Assert.That(
                CountSpades(controller.transform),
                Is.EqualTo(1),
                "Still one: the stone is driven home with the back " +
                "of the same spade.");

            // Only the finished grave puts the tools away.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Sealed));
            Assert.That(
                CountSpades(controller.transform),
                Is.EqualTo(0),
                "The stone going up is what ends the job.");
            Assert.That(controller.Spade, Is.Null);
        }

        [Test]
        public void TheWaitingCoffinIsTheOneThatGoesInTheHole()
        {
            Job job = CreateJob();
            CemeteryGravediggingController controller =
                CreateController(job);
            Assert.That(controller.TryAccept(), Is.True);

            Transform waiting = controller.WaitingCoffin;
            Assert.That(waiting, Is.Not.Null);
            Assert.That(
                CountCoffins(controller.transform),
                Is.EqualTo(1),
                "One box on the plot, waiting on its blocks.");
            Assert.That(
                controller.transform.Find(
                    CityCemeteryCoffinRestWorldBuilder.RootName),
                Is.Not.Null,
                "Nobody sets a coffin down in the mud.");
            Assert.That(
                waiting.position.y,
                Is.GreaterThan(job.Plan.GroundTopY),
                "It stands on the blocks, not in the grass.");

            // Digging leaves it where it was; lowering consumes it, so
            // there is never a second box beside an occupied hole.
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                CountCoffins(controller.transform),
                Is.EqualTo(1));
            Assert.That(controller.TryAdvance(), Is.True);
            Assert.That(
                controller.Stage,
                Is.EqualTo(CemeteryGraveWorkStage.Coffined));
            Assert.That(controller.WaitingCoffin, Is.Null);
            Assert.That(
                CountCoffins(controller.transform),
                Is.EqualTo(1),
                "The one box is now the one in the grave.");
            Assert.That(
                controller.transform.Find(
                    CityCemeteryCoffinRestWorldBuilder.RootName),
                Is.Not.Null,
                "The blocks stay: nobody carries those away.");
        }

        [Test]
        public void TheRopeKeysFollowTheShotAndNotTheGrave()
        {
            // `Q` is the left of the shot and `E` the right. Which end
            // of the box that is depends on the plot's own heading
            // against the side the digger stands on, so binding the
            // keys to head and foot puts them the right way round on
            // half the graves in the yard and backwards on the other
            // half. Both cases have to work out.
            foreach (bool headTowardX in new[] { true, false })
            {
                Vector3 work = new Vector3(0f, 0f, 1f);
                Vector3 cameraRight =
                    Vector3.Cross(Vector3.up, work);
                Vector3 towardHead = headTowardX
                    ? new Vector3(1f, 0f, 0f)
                    : new Vector3(-1f, 0f, 0f);
                bool headOnRight =
                    Vector3.Dot(cameraRight, towardHead) >= 0f;

                Assert.That(
                    headOnRight,
                    Is.EqualTo(headTowardX),
                    "The side the head lands on has to follow the " +
                    "grave's heading, not be assumed.");
            }

            // And the real plan resolves to one side or the other
            // rather than to something degenerate.
            CemeteryGravediggingPlan plan = CreatePlan();
            Vector3 planWork =
                CemeteryGraveWorkStance.GetWorkDirection(plan);
            Vector3 planHead = plan.Heading * Vector3.forward;
            Assert.That(
                Mathf.Abs(
                    Vector3.Dot(
                        Vector3.Cross(Vector3.up, planWork),
                        planHead)),
                Is.GreaterThan(0.9f),
                "The head of a grave is squarely to one side of the " +
                "shot, never ambiguous.");
        }

        [Test]
        public void ThePlaqueFaceCarriesTheWholeRussianAlphabet()
        {
            // The board is cut in a real face rather than a drawn one
            // precisely so that coverage is a property of the font and
            // not of what somebody remembered to draw. This checks the
            // source `.ttf` itself, which is the thing that could
            // quietly be swapped for a Latin-only one.
            var face = Resources.Load<Font>(
                CemeteryPlaqueFont.ResourcePath);
            Assert.That(
                face,
                Is.Not.Null,
                "The plaque face must ship at " +
                CemeteryPlaqueFont.ResourcePath + ".");

            Assert.That(
                face.HasCharacter('Я'),
                Is.True,
                "A Latin-only face would leave holes in the one line " +
                "the player writes.");
            Assert.That(face.HasCharacter('ё'), Is.True);
            Assert.That(face.HasCharacter('Ж'), Is.True);
            Assert.That(face.HasCharacter('Щ'), Is.True);

            for (char glyph = 'А'; glyph <= 'я'; glyph++)
            {
                Assert.That(
                    face.HasCharacter(glyph),
                    Is.True,
                    "No glyph for '" + glyph + "'.");
            }

            for (char glyph = 'A'; glyph <= 'Z'; glyph++)
            {
                Assert.That(face.HasCharacter(glyph), Is.True);
            }

            for (char glyph = '0'; glyph <= '9'; glyph++)
            {
                Assert.That(face.HasCharacter(glyph), Is.True);
            }
        }

        [Test]
        public void TheBoardSitsOnTheStoneAndCarriesReadableLines()
        {
            // Both halves of this shipped broken and neither was
            // visible to any test: a board seated on the bounding box
            // of a monument that is mostly air, and type sized as
            // though TextMeshPro measured in metres, which overflowed
            // its rect and rendered nothing at all. Numbers, not eyes.
            CemeteryGravediggingPlan plan = CreatePlan();
            var host = new GameObject("Plaque Host");
            spawned.Add(host);
            GameObject stone = CityCemeterySealedGraveWorldBuilder
                .BuildStandingStone(host.transform, plan);
            Assert.That(stone, Is.Not.Null);

            GameObject board = CityCemeteryPlaqueWorldBuilder.Attach(
                stone.transform,
                plan);
            Assert.That(
                board,
                Is.Not.Null,
                "Every monument the yard stands must take a board.");

            Bounds stoneBounds = MeasureStone(stone.transform);
            Bounds plate = FindRenderer(
                board.transform,
                CityCemeteryPlaqueWorldBuilder.PlateName).bounds;

            // Screwed to the stone, not hanging in front of it.
            Assert.That(
                stoneBounds.Intersects(
                    FindRenderer(
                        board.transform,
                        CityCemeteryPlaqueWorldBuilder.BezelName)
                        .bounds),
                Is.True,
                "The board has to touch the stone it is fixed to.");
            Assert.That(
                plate.center.y,
                Is.InRange(stoneBounds.min.y, stoneBounds.max.y),
                "And sit on its face rather than above or below it.");

            // Up where a mason puts a plate. It once slid to the
            // plinth because the search demanded a full-width face and
            // only the base was that wide.
            float up = (plate.center.y - stoneBounds.min.y) /
                       stoneBounds.size.y;
            Assert.That(
                up,
                Is.GreaterThan(0.40f),
                "The board is down at the foot of the stone (" +
                Mathf.RoundToInt(up * 100f) +
                "% up it), which reads as something that fell off.");
            Assert.That(up, Is.LessThan(0.90f));

            // Centred across the face rather than shoved to one edge.
            Vector3 sideways = Vector3.Cross(
                Vector3.up,
                plan.Heading * Vector3.back);
            float offset = Mathf.Abs(
                ((plate.center.x - stoneBounds.center.x) *
                 sideways.x) +
                ((plate.center.z - stoneBounds.center.z) *
                 sideways.z));
            Assert.That(
                offset,
                Is.LessThan(0.05f),
                "The board is off to one side of the stone.");

            // Three lines, each of them actually drawn and each of
            // them inside the brass.
            TMPro.TMP_Text[] lines =
                board.GetComponentsInChildren<TMPro.TMP_Text>(true);
            Assert.That(lines.Length, Is.EqualTo(3));
            foreach (TMPro.TMP_Text line in lines)
            {
                line.ForceMeshUpdate(true, true);
                Assert.That(
                    line.textInfo.characterCount,
                    Is.GreaterThan(0),
                    line.name + " has no characters.");
                Assert.That(
                    line.font,
                    Is.Not.Null,
                    line.name + " has no face to cut it in.");

                Bounds drawn = line.GetComponent<Renderer>().bounds;
                float tall = drawn.size.y;
                Assert.That(
                    tall,
                    Is.GreaterThan(0.012f),
                    line.name +
                    " draws " +
                    Mathf.RoundToInt(tall * 1000f) +
                    " mm of letter, which is nothing at arm's " +
                    "length.");
                Assert.That(
                    tall,
                    Is.LessThan(plate.size.y),
                    line.name + " is taller than the plate.");

                float wide = Mathf.Max(drawn.size.x, drawn.size.z);
                float across = Mathf.Max(plate.size.x, plate.size.z);
                Assert.That(
                    wide,
                    Is.LessThanOrEqualTo(across),
                    line.name + " runs off the side of the brass.");
            }
        }

        [Test]
        public void ThePanelHoldsEverythingItDraws()
        {
            CemeteryGraveWorkView.CreateLayout(
                out Rect panel,
                out Rect title,
                out Rect body,
                out Rect hint);

            // On the retro canvas, not hanging off the side of it.
            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                panel.xMax,
                Is.LessThanOrEqualTo(RetroUiTheme.LogicalWidth));
            Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                panel.yMax,
                Is.LessThanOrEqualTo(RetroUiTheme.LogicalHeight));

            // Three rows, in order, each inside the panel and none of
            // them on top of another.
            foreach (Rect row in new[] { title, body, hint })
            {
                Assert.That(
                    row.xMin,
                    Is.GreaterThanOrEqualTo(panel.xMin));
                Assert.That(
                    row.xMax,
                    Is.LessThanOrEqualTo(panel.xMax));
                Assert.That(
                    row.yMin,
                    Is.GreaterThanOrEqualTo(panel.yMin));
                Assert.That(
                    row.yMax,
                    Is.LessThanOrEqualTo(panel.yMax));
            }

            Assert.That(title.yMax, Is.LessThanOrEqualTo(body.yMin));
            Assert.That(body.yMax, Is.LessThanOrEqualTo(hint.yMin));

            // The hint carries three controls in a sentence; it is the
            // row that overflowed, so it gets room for two lines of it.
            Assert.That(
                hint.height,
                Is.GreaterThanOrEqualTo(20f),
                "The hint must be able to wrap rather than clip.");

            // The spade acts have no panel at all now — the square
            // is outlined down in the hole — so what is left to hold
            // is that the two acts which still carry a gauge have room
            // for one.
            Assert.That(
                body.height,
                Is.GreaterThan(40f),
                "A gauge needs a body to live in.");
        }

        [Test]
        public void EveryActTheLadderOffersIsAnActTheWorkCanRun()
        {
            foreach (CemeteryGraveWorkStage stage in
                     System.Enum.GetValues(
                         typeof(CemeteryGraveWorkStage)))
            {
                Assert.That(
                    CemeteryGraveWorkController.IsPlayableAct(stage),
                    Is.EqualTo(
                        CemeteryGraveDigSiteInteraction
                            .IsWorkingStage(stage)),
                    "A rung the site offers must be a rung the work " +
                    "knows how to run.");
                if (!CemeteryGraveWorkController.IsPlayableAct(stage))
                {
                    continue;
                }

                Assert.That(
                    CemeteryGraveWorkController.GetTitleKey(stage),
                    Is.Not.Empty);
                Assert.That(
                    CemeteryGraveWorkController.GetHintKey(stage),
                    Is.Not.Empty);
            }

            // Four acts, four lines. The two spade acts run off the
            // same lattice and the same key, but one takes earth out
            // and the other puts it back, and a line reading "dig" at
            // a hero shovelling a hole shut is the wrong instruction
            // however cheap it is to share.
            var lines = new System.Collections.Generic.HashSet<string>();
            foreach (CemeteryGraveWorkStage stage in
                     new[]
                     {
                         CemeteryGraveWorkStage.Marked,
                         CemeteryGraveWorkStage.Dug,
                         CemeteryGraveWorkStage.Coffined,
                         CemeteryGraveWorkStage.Filled
                     })
            {
                Assert.That(
                    lines.Add(
                        CemeteryGraveWorkController.GetHintKey(stage)),
                    Is.True,
                    "Every act asks for something of its own.");
            }
        }

        [Test]
        public void CuttingTheSameHoleTwiceIsNotAnError()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            var root = new GameObject("Excavation Host");
            spawned.Add(root);
            CityWorldResult world = CityWorldBuilder.Build(
                root.transform,
                layout,
                CityGenerationSettings.Default);
            CityCemeteryGroundExcavation excavation =
                world.CemeteryGroundExcavation;
            Assert.That(excavation, Is.Not.Null);

            CemeteryGravediggingPlan plan =
                CemeteryGravediggingPlan.Create(
                    world.CemeteryPlan,
                    CemeteryWatchmanPlan.Create(world.CemeteryPlan));
            Rect cut =
                CityCemeteryPitWorldBuilder.GetExcavationRect(plan);

            // The work opens the ground long before the act that
            // records it is committed, so the commit has to survive
            // finding the hole already there.
            Assert.That(excavation.Excavate(cut), Is.True);
            Assert.That(excavation.Cuts.Count, Is.EqualTo(1));
            Assert.That(
                excavation.Excavate(cut),
                Is.True,
                "The same rectangle twice is a no-op, not a refusal.");
            Assert.That(excavation.Cuts.Count, Is.EqualTo(1));
            Assert.That(excavation.Fill(cut), Is.True);
            Assert.That(excavation.Cuts.Count, Is.EqualTo(0));
        }

        // ---- helpers -----------------------------------------

        /// <summary>
        /// Plays act two to its end with a fixed pair of keys and
        /// insists the box is lost. Runs several balance-point phases,
        /// because a lazy strategy that only fails on one of them is a
        /// lazy strategy that works.
        /// </summary>
        private static void AssertLosesTheBox(
            System.Func<float, bool[]> keys,
            string what)
        {
            foreach (int seed in new[] { 3, 7, 29, 101 })
            {
                var model = new CemeteryCoffinLowerModel(
                    CemeteryCoffinLowerSettings.Default,
                    seed);
                for (int step = 0;
                     step < 12000 && !model.IsComplete;
                     step++)
                {
                    bool[] held = keys(model.Tilt);
                    model.Advance(1f / 120f, held[0], held[1]);
                }

                Assert.That(
                    model.Failed,
                    Is.True,
                    what + " must lose the box (seed " + seed + ").");
            }
        }

        /// <summary>
        /// Plays it the way it is meant to be played: keep letting
        /// rope out, at whichever end is riding high, and at both when
        /// the box is level. Rope only ever goes out, so correcting
        /// and descending are the same action.
        /// </summary>
        private static void AssertLandsByChasing(
            float deadzone,
            float reactionDelay,
            int seed)
        {
            var model = new CemeteryCoffinLowerModel(
                CemeteryCoffinLowerSettings.Default,
                seed);
            const float step = 1f / 120f;
            bool holdHead = false;
            bool holdFoot = false;
            float decidedAt = -1f;
            float now = 0f;
            for (int index = 0;
                 index < 12000 && !model.IsComplete;
                 index++)
            {
                if (now - decidedAt >= reactionDelay)
                {
                    decidedAt = now;
                    // Head-down means the foot rides high, so the foot
                    // is the end to let out.
                    holdFoot = model.Tilt > deadzone;
                    holdHead = model.Tilt < -deadzone;
                    if (Mathf.Abs(model.Tilt) <= deadzone)
                    {
                        holdHead = true;
                        holdFoot = true;
                    }
                }

                model.Advance(step, holdHead, holdFoot);
                now += step;
            }

            Assert.That(
                model.Succeeded,
                Is.True,
                "Chasing the point with a deadzone of " +
                deadzone +
                " and a delay of " +
                reactionDelay +
                "s must land it (seed " +
                seed +
                ").");
        }

        /// <summary>
        /// The longest unbroken stretch the marker spends where a
        /// release would bite, walked at a step far finer than a
        /// frame so the answer is the model's and not the sampling's.
        /// </summary>
        private static float MeasureBiteWindowSeconds(
            CemeterySwingProfile profile)
        {
            var model = new CemeteryStrokeModel();
            model.Begin(profile, 1);
            const float step = 1f / 600f;
            float longest = 0f;
            float run = 0f;
            for (int index = 0; index < 4000; index++)
            {
                model.Advance(step);
                if (CemeteryStrokeModel.Resolve(
                        model.Position,
                        profile) == CemeteryStrokeOutcome.Bite)
                {
                    run += step;
                    longest = Mathf.Max(longest, run);
                }
                else
                {
                    run = 0f;
                }
            }

            return longest;
        }

        /// <summary>
        /// A live gravedigging controller over the default city's own
        /// cemetery slab, so the acts can be committed for real and
        /// the props they leave standing can be counted.
        /// </summary>
        private CemeteryGravediggingController CreateController(
            Job job)
        {
            var host = new GameObject("Test Cemetery Surfaces");
            spawned.Add(host);
            GameObject ground = CityCemeteryGroundWorldBuilder.Build(
                host.transform,
                job.Layout,
                null);
            CityCemeteryGroundExcavation excavation =
                CityCemeteryGroundExcavation.Attach(
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

        private Job CreateJob()
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
                CemeteryGravediggingPlan.Create(cemetery, watchman));
        }

        private readonly struct Job
        {
            public Job(
                CityLayout layout,
                CemeteryGravediggingPlan plan)
            {
                Layout = layout;
                Plan = plan;
            }

            public CityLayout Layout { get; }
            public CemeteryGravediggingPlan Plan { get; }
        }

        /// <summary>The stone's own bounds, with the board on it
        /// left out.</summary>
        private static Bounds MeasureStone(Transform stone)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in
                     stone.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer
                        .GetComponentInParent<CemeteryPlaqueSurface>()
                    != null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            Assert.That(any, Is.True, "The stone has no geometry.");
            return bounds;
        }

        private static Renderer FindRenderer(
            Transform root,
            string name)
        {
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == name)
                {
                    return renderer;
                }
            }

            Assert.Fail("No renderer named " + name + ".");
            return null;
        }

        private static int CountSpades(Transform root)
        {
            return CountNamed(
                root,
                CityGravediggerShovelWorldBuilder.RootName);
        }

        private static int CountCoffins(Transform root)
        {
            return CountNamed(
                root,
                CityCemeteryCoffinWorldBuilder.RootName);
        }

        private static int CountNamed(Transform root, string name)
        {
            int found = 0;
            Transform[] all =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < all.Length; index++)
            {
                if (all[index].name == name)
                {
                    found++;
                }
            }

            return found;
        }

        private static CemeteryGravediggingPlan CreatePlan()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityCemeteryPlan cemetery =
                CityCemeteryPlanner.Create(layout);
            return CemeteryGravediggingPlan.Create(
                cemetery,
                CemeteryWatchmanPlan.Create(cemetery));
        }

        /// <summary>One legal strike, however many the ground wants.
        /// </summary>
        private static bool Strike(
            CemeteryGraveLatticeModel lattice,
            int segment)
        {
            return lattice.TryStrike(
                segment,
                CemeteryStrokeOutcome.Bite,
                out _);
        }

        private static void Finish(CemeteryGraveLatticeModel lattice)
        {
            for (int guard = 0;
                 guard < 2000 && !lattice.IsComplete;
                 guard++)
            {
                int segment = lattice.FindWorkable(0, 1);
                if (segment < 0)
                {
                    return;
                }

                Strike(lattice, segment);
            }
        }

        /// <summary>
        /// Digs one segment down to a named course, bringing its
        /// neighbours along so the lattice rule is satisfied the whole
        /// way, and leaves it open for the next strike.
        /// </summary>
        private static void DigDownTo(
            CemeteryGraveLatticeModel lattice,
            int segment,
            int course)
        {
            for (int guard = 0; guard < 600; guard++)
            {
                bool deepEnough =
                    lattice.GetCoursesDone(segment) >= course;
                if (deepEnough &&
                    (lattice.IsWorkable(segment) ||
                     course >= CemeteryGraveLatticeModel
                         .CoursesPerSegment))
                {
                    return;
                }

                int chosen =
                    !deepEnough && lattice.IsWorkable(segment)
                        ? segment
                        : lattice.FindWorkable(0, 1);
                if (chosen < 0)
                {
                    return;
                }

                Strike(lattice, chosen);
            }
        }

    }
}
