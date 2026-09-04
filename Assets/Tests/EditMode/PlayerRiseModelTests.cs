using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The staged rise in isolation: the stages come in order, the
    /// ragdoll is waited for but not forever, a drunk lies stunned
    /// longer, the clip is scrubbed forward except during a slump and
    /// lands on its authored keys at every stage change, slumps are
    /// seeded and bounded, the lead boot is the lying side, and the whole
    /// thing is deterministic and bounded in time.
    /// </summary>
    public sealed class PlayerRiseModelTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerRiseInput Still(float intoxication)
        {
            return new PlayerRiseInput(intoxication, true, 0f);
        }

        private static PlayerRiseInput Moving(float intoxication)
        {
            return new PlayerRiseInput(intoxication, true, 1f);
        }

        private static List<PlayerRiseStage> RunToDone(
            PlayerRiseModel model,
            in PlayerRiseInput input,
            int frameLimit = 20 * 60)
        {
            var stages = new List<PlayerRiseStage> { model.Stage };
            for (int frame = 0; frame < frameLimit && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                if (model.Stage != stages[stages.Count - 1])
                {
                    stages.Add(model.Stage);
                }
            }

            return stages;
        }

        private static void RunToStage(
            PlayerRiseModel model,
            PlayerRiseStage stage,
            Vector2 key,
            int frameLimit = 20 * 60)
        {
            for (int frame = 0; frame < frameLimit && model.Stage < stage; frame++)
            {
                model.SetDownedInput(key);
                model.Advance(Frame, Still(1f));
            }
        }

        [Test]
        public void Crawling_HoldsAllFoursWhileTheKeyIsHeld()
        {
            var model = new PlayerRiseModel(5, 1f);
            RunToStage(model, PlayerRiseStage.Crawling, Vector2.up);
            Assert.That(model.Stage, Is.EqualTo(PlayerRiseStage.Crawling));
            float progress = model.Output.Progress;
            float phaseAtStart = model.CrawlPhase;
            for (int frame = 0; frame < 3 * 60; frame++)
            {
                model.SetDownedInput(Vector2.up);
                model.Advance(Frame, Still(1f));
                Assert.That(model.Stage, Is.EqualTo(PlayerRiseStage.Crawling));
                Assert.That(
                    model.Output.ClipTime,
                    Is.InRange(PlayerRiseRules.AllFoursKey, PlayerRiseRules.AllFoursShiftKey),
                    "a crawl rocks between the two all-fours keys");
                Assert.That(model.Output.Progress, Is.EqualTo(progress).Within(0.00001f), "the rise does not advance while he crawls");
                Assert.That(model.Output.CrawlVelocityLocal.y, Is.GreaterThan(0.1f), "a key straight ahead moves him forward");
                Assert.That(model.Output.CrawlVelocityLocal.x, Is.Zero);
                Assert.That(model.Output.CrawlYawDegreesPerSecond, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(model.Output.LegsWeight, Is.Zero);
                Assert.That(model.Output.Step.Active, Is.False);
            }

            // Three seconds at nine tenths of a hertz or more: over two and a half hand-swings each side.
            Assert.That(model.CrawlPhase - phaseAtStart, Is.GreaterThan(Mathf.PI * 2f * 2.5f));
        }

        [Test]
        public void Crawling_AlternatesHands()
        {
            var model = new PlayerRiseModel(5, 1f);
            RunToStage(model, PlayerRiseStage.Crawling, Vector2.up);
            float start = model.CrawlPhase;
            float leftReach = 0f, rightReach = 0f, leftLift = 0f, rightLift = 0f;
            float leftLiftPhase = 0f, rightLiftPhase = 0f;
            bool bothLifted = false;
            while (model.CrawlPhase < start + Mathf.PI * 2f)
            {
                model.SetDownedInput(Vector2.up);
                model.Advance(Frame, Still(1f));
                PlayerRiseOutput output = model.Output;
                leftReach = Mathf.Max(leftReach, output.LeftHandOffsetLocal.y);
                rightReach = Mathf.Max(rightReach, output.RightHandOffsetLocal.y);
                if (output.LeftHandLift > leftLift)
                {
                    leftLift = output.LeftHandLift;
                    leftLiftPhase = model.CrawlPhase;
                }

                if (output.RightHandLift > rightLift)
                {
                    rightLift = output.RightHandLift;
                    rightLiftPhase = model.CrawlPhase;
                }

                bothLifted |= output.LeftHandLift > 0.005f && output.RightHandLift > 0.005f;
                Assert.That(output.LeftHandWeight, Is.EqualTo(1f));
                Assert.That(output.RightHandWeight, Is.EqualTo(1f));
                Assert.That(output.PelvisOffsetMetres, Is.InRange(-PlayerRiseRules.CrawlBobMetres, 0f));
            }

            Assert.That(leftReach, Is.GreaterThan(0.25f), "the left hand reaches ahead");
            Assert.That(rightReach, Is.GreaterThan(0.25f), "the right hand reaches ahead");
            Assert.That(leftLift, Is.GreaterThan(0.05f));
            Assert.That(rightLift, Is.GreaterThan(0.05f));
            Assert.That(bothLifted, Is.False, "one hand is always on the floor");
            float apart = Mathf.Abs(Mathf.DeltaAngle(
                leftLiftPhase * Mathf.Rad2Deg,
                rightLiftPhase * Mathf.Rad2Deg));
            Assert.That(apart, Is.EqualTo(180f).Within(25f), "the hands swing half a turn apart");
        }

        [Test]
        public void Crawling_IsDiagonalAndEveryLimbSwingsOnceATurn()
        {
            var model = new PlayerRiseModel(5, 1f);
            RunToStage(model, PlayerRiseStage.Crawling, Vector2.up);
            float start = model.CrawlPhase;
            int leftHandSwings = 0;
            int rightKneeSwings = 0;
            int leftKneeSwings = 0;
            int rightHandSwings = 0;
            bool previousLeftHand = model.Output.LeftHandCrawl.Swinging;
            bool previousRightKnee = model.Output.RightKneeCrawl.Swinging;
            bool previousLeftKnee = model.Output.LeftKneeCrawl.Swinging;
            bool previousRightHand = model.Output.RightHandCrawl.Swinging;
            float previousProgress = -1f;
            while (model.CrawlPhase < start + Mathf.PI * 4f)
            {
                model.SetDownedInput(Vector2.up);
                model.Advance(Frame, Still(1f));
                PlayerRiseOutput output = model.Output;
                // Diagonal: the left hand and the right knee swing
                // together, the right hand and the left knee together, and
                // never both hands or both knees at once.
                Assert.That(output.RightKneeCrawl.Swinging, Is.EqualTo(output.LeftHandCrawl.Swinging));
                Assert.That(output.LeftKneeCrawl.Swinging, Is.EqualTo(output.RightHandCrawl.Swinging));
                Assert.That(output.LeftHandCrawl.Swinging, Is.Not.EqualTo(output.RightHandCrawl.Swinging));
                if (output.LeftHandCrawl.Swinging)
                {
                    Assert.That(output.LeftHandCrawl.Progress, Is.EqualTo(output.RightKneeCrawl.Progress));
                    if (previousLeftHand && previousProgress >= 0f)
                    {
                        Assert.That(output.LeftHandCrawl.Progress, Is.GreaterThanOrEqualTo(previousProgress), "a swing only goes forward");
                    }

                    previousProgress = output.LeftHandCrawl.Progress;
                }
                else
                {
                    previousProgress = -1f;
                }

                leftHandSwings += !previousLeftHand && output.LeftHandCrawl.Swinging ? 1 : 0;
                rightKneeSwings += !previousRightKnee && output.RightKneeCrawl.Swinging ? 1 : 0;
                leftKneeSwings += !previousLeftKnee && output.LeftKneeCrawl.Swinging ? 1 : 0;
                rightHandSwings += !previousRightHand && output.RightHandCrawl.Swinging ? 1 : 0;
                previousLeftHand = output.LeftHandCrawl.Swinging;
                previousRightKnee = output.RightKneeCrawl.Swinging;
                previousLeftKnee = output.LeftKneeCrawl.Swinging;
                previousRightHand = output.RightHandCrawl.Swinging;
            }

            Assert.That(leftHandSwings, Is.EqualTo(2).Within(1), "two turns: each limb swings about twice");
            Assert.That(rightKneeSwings, Is.EqualTo(leftHandSwings));
            Assert.That(leftKneeSwings, Is.EqualTo(2).Within(1));
            Assert.That(rightHandSwings, Is.EqualTo(leftKneeSwings));
        }

        [Test]
        public void Crawling_TurnsTowardTheKeyBeforeMoving()
        {
            var model = new PlayerRiseModel(5, 1f);
            RunToStage(model, PlayerRiseStage.Crawling, Vector2.up);

            model.SetDownedInput(Vector2.right);
            model.Advance(Frame, Still(1f));
            Assert.That(model.Output.CrawlYawDegreesPerSecond, Is.EqualTo(PlayerRiseRules.CrawlYawDegreesPerSecond).Within(0.001f));
            Assert.That(model.Output.CrawlVelocityLocal.y, Is.EqualTo(0f).Within(0.001f), "a key square to the side turns him without moving him");

            model.SetDownedInput(Vector2.left);
            model.Advance(Frame, Still(1f));
            Assert.That(model.Output.CrawlYawDegreesPerSecond, Is.EqualTo(-PlayerRiseRules.CrawlYawDegreesPerSecond).Within(0.001f));

            model.SetDownedInput(Vector2.down);
            model.Advance(Frame, Still(1f));
            Assert.That(Mathf.Abs(model.Output.CrawlYawDegreesPerSecond), Is.EqualTo(PlayerRiseRules.CrawlYawDegreesPerSecond).Within(0.001f), "a key behind him turns him round");
            Assert.That(model.Output.CrawlVelocityLocal.y, Is.EqualTo(0f).Within(0.001f));

            model.SetDownedInput(new Vector2(Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad)));
            model.Advance(Frame, Still(1f));
            Assert.That(model.Output.CrawlYawDegreesPerSecond, Is.EqualTo(PlayerRiseRules.CrawlYawDegreesPerSecond * 30f / PlayerRiseRules.CrawlTurnFullDegrees).Within(0.01f));
            Assert.That(model.Output.CrawlVelocityLocal.y, Is.GreaterThan(0.1f), "a key ahead and a little to the side moves him while he turns");

            // A key inside the dead zone is no key.
            model.SetDownedInput(Vector2.up * 0.1f);
            model.Advance(Frame, Still(1f));
            Assert.That(model.HasDownedInput, Is.False);
            Assert.That(model.Output.CrawlVelocityLocal, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Crawling_EndsWithTheKeyAndTheKneelGoesOn()
        {
            var model = new PlayerRiseModel(5, 1f);
            RunToStage(model, PlayerRiseStage.Crawling, Vector2.up);
            for (int frame = 0; frame < 60; frame++)
            {
                model.SetDownedInput(Vector2.up);
                model.Advance(Frame, Still(1f));
            }

            model.SetDownedInput(Vector2.zero);
            int released = 0;
            while (model.Stage == PlayerRiseStage.Crawling && released < 60)
            {
                model.Advance(Frame, Still(1f));
                released++;
            }

            Assert.That(model.Stage, Is.EqualTo(PlayerRiseStage.Kneeling));
            Assert.That(
                released * Frame,
                Is.EqualTo(PlayerRiseRules.CrawlReleaseSeconds).Within(2f * Frame),
                "a key gone for the release time hands him on");
            List<PlayerRiseStage> stages = RunToDone(model, Still(1f));
            Assert.That(
                stages,
                Is.EqualTo(new[]
                {
                    PlayerRiseStage.Kneeling,
                    PlayerRiseStage.Standing,
                    PlayerRiseStage.Done
                }));
            Assert.That(model.Output.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void Kneeling_GoesBackToTheCrawlOnlyAtItsStart()
        {
            var early = new PlayerRiseModel(5, 1f);
            RunToStage(early, PlayerRiseStage.Kneeling, Vector2.zero);
            Assert.That(early.Stage, Is.EqualTo(PlayerRiseStage.Kneeling));
            early.SetDownedInput(Vector2.up);
            early.Advance(Frame, Still(1f));
            Assert.That(early.Stage, Is.EqualTo(PlayerRiseStage.Crawling), "a key at the start of the kneel takes him back to all fours");

            var late = new PlayerRiseModel(5, 1f);
            RunToStage(late, PlayerRiseStage.Kneeling, Vector2.zero);
            while (late.Output.StageProgress < 0.5f)
            {
                late.Advance(Frame, Still(1f));
            }

            late.SetDownedInput(Vector2.up);
            late.Advance(Frame, Still(1f));
            Assert.That(late.Stage, Is.EqualTo(PlayerRiseStage.Kneeling), "half way up, a key is ignored");
            List<PlayerRiseStage> stages = RunToDone(late, Still(1f));
            Assert.That(stages, Has.No.Member(PlayerRiseStage.Crawling));
        }

        [Test]
        public void Crawling_IsDeterministic()
        {
            var first = new PlayerRiseModel(9, 0.8f);
            var second = new PlayerRiseModel(9, 0.8f);
            for (int frame = 0; frame < 12 * 60; frame++)
            {
                Vector2 key = frame < 6 * 60 ? new Vector2(0.3f, 0.9f) : frame < 7 * 60 ? Vector2.zero : Vector2.up;
                first.SetDownedInput(key);
                second.SetDownedInput(key);
                first.Advance(Frame, Still(0.8f));
                second.Advance(Frame, Still(0.8f));
                Assert.That(second.Stage, Is.EqualTo(first.Stage));
                Assert.That(second.Output.ClipTime, Is.EqualTo(first.Output.ClipTime));
                Assert.That(second.Output.LeftHandLift, Is.EqualTo(first.Output.LeftHandLift));
                Assert.That(second.Output.CrawlVelocityLocal, Is.EqualTo(first.Output.CrawlVelocityLocal));
                Assert.That(second.Output.CrawlYawDegreesPerSecond, Is.EqualTo(first.Output.CrawlYawDegreesPerSecond));
            }

            Assert.That(first.Stage, Is.EqualTo(PlayerRiseStage.Done));
        }

        [Test]
        public void NudgeStun_ShortensButNotBelowTheFloor()
        {
            var model = new PlayerRiseModel(11, 1f);
            float before = model.StunSeconds;
            Assert.That(before, Is.GreaterThan(PlayerRiseRules.StunFloorSeconds + 0.15f));
            model.NudgeStun(-0.15f);
            Assert.That(model.StunSeconds, Is.EqualTo(before - 0.15f).Within(0.00001f));
            model.NudgeStun(-10f);
            Assert.That(model.StunSeconds, Is.EqualTo(PlayerRiseRules.StunFloorSeconds));

            // Once he stirs the stun is spent; nudging it changes nothing.
            RunToStage(model, PlayerRiseStage.Stirring, Vector2.zero);
            float stirring = model.StunSeconds;
            model.NudgeStun(5f);
            Assert.That(model.StunSeconds, Is.EqualTo(stirring));
        }

        [Test]
        public void Stages_AdvanceInOrder()
        {
            var model = new PlayerRiseModel(1, 1f);
            List<PlayerRiseStage> stages = RunToDone(model, Still(1f));

            Assert.That(
                stages,
                Is.EqualTo(new[]
                {
                    PlayerRiseStage.Settling,
                    PlayerRiseStage.Stunned,
                    PlayerRiseStage.Stirring,
                    PlayerRiseStage.PushingUp,
                    PlayerRiseStage.Kneeling,
                    PlayerRiseStage.Standing,
                    PlayerRiseStage.Done
                }));
            Assert.That(model.Output.Progress, Is.EqualTo(1f));
            Assert.That(model.Output.ClipTime, Is.EqualTo(1f));
            Assert.That(model.Output.LegsWeight, Is.EqualTo(1f));
        }

        [Test]
        public void Settling_WaitsForRestButNotForever()
        {
            var restless = new PlayerRiseModel(3, 1f);
            int frames = 0;
            while (restless.Stage == PlayerRiseStage.Settling && frames < 10 * 60)
            {
                restless.Advance(Frame, Moving(1f));
                frames++;
            }

            Assert.That(restless.Stage, Is.EqualTo(PlayerRiseStage.Stunned));
            Assert.That(
                frames * Frame,
                Is.EqualTo(PlayerRiseRules.SettleMaximumSeconds).Within(2f * Frame),
                "a body that never comes to rest is given up on at the ceiling");

            var still = new PlayerRiseModel(3, 1f);
            frames = 0;
            while (still.Stage == PlayerRiseStage.Settling && frames < 10 * 60)
            {
                still.Advance(Frame, Still(1f));
                frames++;
            }

            Assert.That(
                frames * Frame,
                Is.EqualTo(PlayerRiseRules.SettleMinimumSeconds).Within(2f * Frame),
                "a body at rest from the start still lies the minimum");

            // Rest has to be sustained: a twitch resets the count, so a
            // body twitching every fifth of a second is still settling at
            // two seconds — and given up on at the ceiling all the same.
            var twitchy = new PlayerRiseModel(3, 1f);
            for (int frame = 0; frame < 2 * 60; frame++)
            {
                twitchy.Advance(Frame, frame % 12 == 0 ? Moving(1f) : Still(1f));
            }

            Assert.That(twitchy.Stage, Is.EqualTo(PlayerRiseStage.Settling), "a twitch every fifth of a second is not rest");
            Assert.That(twitchy.Output.Stage, Is.EqualTo(PlayerRiseStage.Settling));
            Assert.That(twitchy.Output.Progress, Is.EqualTo(0f));
            for (int frame = 2 * 60; frame < 3 * 60; frame++)
            {
                twitchy.Advance(Frame, frame % 12 == 0 ? Moving(1f) : Still(1f));
            }

            Assert.That(twitchy.Stage, Is.EqualTo(PlayerRiseStage.Stunned), "the ceiling gives up on a body that never rests");
        }

        [Test]
        public void Stun_GrowsWithTheDrink()
        {
            float tipsy = 0f;
            float blind = 0f;
            for (int seed = 0; seed < 60; seed++)
            {
                tipsy += new PlayerRiseModel(seed, 0.6f).StunSeconds;
                blind += new PlayerRiseModel(seed, 1f).StunSeconds;
            }

            Assert.That(blind, Is.GreaterThan(tipsy * 1.3f));
            Assert.That(tipsy / 60f, Is.GreaterThan(0.5f).And.LessThan(2f));
            Assert.That(blind / 60f, Is.GreaterThan(1.2f).And.LessThan(2.7f));
        }

        [Test]
        public void ClipTime_IsMonotoneWithinStagesExceptSlumps()
        {
            var model = new PlayerRiseModel(11, 1f);
            model.DebugPlanSlumps(2);
            PlayerRiseInput input = Still(1f);
            float previous = 0f;
            PlayerRiseStage previousStage = model.Stage;
            int slumpFrames = 0;
            int retreatFrames = 0;
            for (int frame = 0; frame < 20 * 60 && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                PlayerRiseOutput output = model.Output;
                if (output.SlumpActive)
                {
                    slumpFrames++;
                    if (output.ClipTime < previous - 0.0001f)
                    {
                        retreatFrames++;
                    }
                }
                else if (model.Stage == previousStage)
                {
                    Assert.That(
                        output.ClipTime,
                        Is.GreaterThanOrEqualTo(previous - 0.0001f),
                        $"clip time ran back outside a slump at frame {frame} in {model.Stage}");
                }

                previous = output.ClipTime;
                previousStage = model.Stage;
            }

            Assert.That(slumpFrames, Is.GreaterThan(20), "two slumps were played");
            Assert.That(retreatFrames, Is.GreaterThan(3), "a slump runs the clip back");
            Assert.That(model.SlumpsTaken, Is.EqualTo(2));
        }

        [Test]
        public void ClipTime_HitsTheAuthoredKeysAtStageChanges()
        {
            var model = new PlayerRiseModel(5, 0.8f);
            PlayerRiseInput input = Still(0.8f);
            var atEntry = new Dictionary<PlayerRiseStage, float>();
            PlayerRiseStage previous = model.Stage;
            for (int frame = 0; frame < 20 * 60 && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                if (model.Stage != previous)
                {
                    atEntry[model.Stage] = model.Output.ClipTime;
                    previous = model.Stage;
                }
            }

            Assert.That(atEntry[PlayerRiseStage.Stirring], Is.EqualTo(PlayerRiseRules.DownKey).Within(0.0001f));
            Assert.That(atEntry[PlayerRiseStage.PushingUp], Is.EqualTo(PlayerRiseRules.BraceKey).Within(0.0001f));
            Assert.That(atEntry[PlayerRiseStage.Kneeling], Is.EqualTo(PlayerRiseRules.AllFoursKey).Within(0.0001f));
            Assert.That(atEntry[PlayerRiseStage.Standing], Is.EqualTo(PlayerRiseRules.HalfKneelKey).Within(0.0001f));
            Assert.That(atEntry[PlayerRiseStage.Done], Is.EqualTo(PlayerRiseRules.RelaxedKey).Within(0.0001f));
        }

        [Test]
        public void Slumps_AreSeededAndBounded()
        {
            int zeroes = 0;
            int twos = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                int planned = new PlayerRiseModel(seed, 1f).SlumpsPlanned;
                Assert.That(planned, Is.InRange(0, PlayerRiseRules.MaximumSlumps));
                Assert.That(new PlayerRiseModel(seed, 1f).SlumpsPlanned, Is.EqualTo(planned), "same seed, same slumps");
                Assert.That(new PlayerRiseModel(seed, 0.6f).SlumpsPlanned, Is.LessThan(2), "at level 60 never two");
                zeroes += planned == 0 ? 1 : 0;
                twos += planned == 2 ? 1 : 0;
            }

            Assert.That(zeroes, Is.GreaterThan(10), "some blind-drunk rises go straight up");
            Assert.That(twos, Is.GreaterThan(10), "some slump twice");
            Assert.That(new PlayerRiseModel(1, 0f).SlumpsPlanned, Is.Zero, "sober never slumps");
        }

        [Test]
        public void Durations_AreBounded()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var model = new PlayerRiseModel(seed, 1f);
                PlayerRiseInput input = Still(1f);
                while (model.Stage < PlayerRiseStage.Stirring)
                {
                    model.Advance(Frame, input);
                }

                float start = model.Elapsed;
                RunToDone(model, input);
                float seconds = model.Elapsed - start;
                Assert.That(seconds, Is.GreaterThan(2.7f).And.LessThan(5.3f), $"seed {seed}: {seconds:F2} s from stirring to done");
                Assert.That(model.Elapsed, Is.LessThan(11f), "the whole rise, stun included, is under eleven seconds");
            }
        }

        [Test]
        public void LeadFoot_IsTheLyingSide()
        {
            var model = new PlayerRiseModel(2, 1f);
            model.SetLyingSide(FootSide.Left);
            PlayerRiseInput input = Still(1f);
            bool sawStep = false;
            bool sawKnee = false;
            for (int frame = 0; frame < 20 * 60 && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                PlayerRiseOutput output = model.Output;
                if (output.Step.Active)
                {
                    sawStep = true;
                    Assert.That(output.Step.Side, Is.EqualTo(FootSide.Left));
                    Assert.That(output.Step.TargetLocal.x, Is.LessThan(0f), "the left boot steps to the left of the line");
                    Assert.That(output.Step.TargetLocal.y, Is.GreaterThan(0.2f), "and ahead");
                    Assert.That(model.Stage, Is.EqualTo(PlayerRiseStage.Kneeling));
                }

                if (output.HandOnKnee)
                {
                    sawKnee = true;
                    Assert.That(output.KneeSide, Is.EqualTo(FootSide.Left));
                }
            }

            Assert.That(sawStep, Is.True);
            Assert.That(sawKnee, Is.True);
            Assert.That(model.LeadFoot, Is.EqualTo(FootSide.Left));

            // Still allowed on the frame he first stirs — that is when
            // the status controller learns which shoulder is down — and
            // too late once he pushes up.
            var stirring = new PlayerRiseModel(2, 1f);
            while (stirring.Stage < PlayerRiseStage.Stirring)
            {
                stirring.Advance(Frame, input);
            }

            stirring.SetLyingSide(FootSide.Left);
            Assert.That(stirring.LeadFoot, Is.EqualTo(FootSide.Left));

            var late = new PlayerRiseModel(2, 1f);
            while (late.Stage < PlayerRiseStage.PushingUp)
            {
                late.Advance(Frame, input);
            }

            late.SetLyingSide(FootSide.Left);
            Assert.That(late.LeadFoot, Is.EqualTo(FootSide.Right));
        }

        [Test]
        public void Hands_RestOnTheFloorThenLetGo()
        {
            var model = new PlayerRiseModel(7, 1f);
            PlayerRiseInput input = Still(1f);
            float pushWeight = 0f;
            for (int frame = 0; frame < 20 * 60 && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                if (model.Stage == PlayerRiseStage.PushingUp)
                {
                    pushWeight = Mathf.Max(pushWeight, model.Output.LeftHandWeight);
                    Assert.That(model.Output.RightHandWeight, Is.EqualTo(1f));
                }
            }

            Assert.That(pushWeight, Is.EqualTo(1f));
            Assert.That(model.Output.LeftHandWeight, Is.EqualTo(0f));
            Assert.That(model.Output.RightHandWeight, Is.EqualTo(0f));
            Assert.That(model.Output.HandOnKnee, Is.False);
        }

        [Test]
        public void HeadLift_PeaksMidStirring()
        {
            var model = new PlayerRiseModel(9, 1f);
            PlayerRiseInput input = Still(1f);
            float peak = 0f;
            float atEnd = 0f;
            while (model.Stage < PlayerRiseStage.PushingUp)
            {
                model.Advance(Frame, input);
                if (model.Stage == PlayerRiseStage.Stirring)
                {
                    peak = Mathf.Max(peak, model.Output.HeadLiftDegrees);
                    atEnd = model.Output.HeadLiftDegrees;
                }
            }

            Assert.That(peak, Is.EqualTo(PlayerRiseRules.HeadLiftPeakDegrees).Within(0.5f));
            Assert.That(atEnd, Is.LessThan(peak));
            Assert.That(atEnd, Is.GreaterThan(peak * 0.4f));
        }

        [Test]
        public void Wobble_AppearsAtTheTopAndHandsBackABoundedVelocity()
        {
            var model = new PlayerRiseModel(13, 1f);
            PlayerRiseInput input = Still(1f);
            float peakWobble = 0f;
            float earlyWobble = 0f;
            for (int frame = 0; frame < 20 * 60 && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                if (model.Stage == PlayerRiseStage.Standing)
                {
                    float wobble = Mathf.Abs(model.Output.WobbleLeanDegrees.x);
                    if (model.Output.StageProgress < 0.6f)
                    {
                        earlyWobble = Mathf.Max(earlyWobble, wobble);
                    }

                    peakWobble = Mathf.Max(peakWobble, wobble);
                }
            }

            Assert.That(earlyWobble, Is.EqualTo(0f), "no wobble before the top");
            Assert.That(peakWobble, Is.GreaterThan(1f).And.LessThanOrEqualTo(PlayerRiseRules.WobbleDegreesAtMaximum + 0.001f));
            Assert.That(model.HandbackVelocity.magnitude, Is.LessThanOrEqualTo(0.4f));
            Assert.That(
                new PlayerRiseModel(13, 1f).HandbackVelocity,
                Is.EqualTo(Vector2.zero),
                "nothing is handed back before the rise is done");

            var sober = new PlayerRiseModel(13, 0f);
            RunToDone(sober, Still(0f));
            Assert.That(sober.HandbackVelocity, Is.EqualTo(Vector2.zero), "a sober rise wobbles nothing");
        }

        [Test]
        public void Progress_IsMonotoneAndEndsAtOne()
        {
            var model = new PlayerRiseModel(21, 1f);
            model.DebugPlanSlumps(2);
            PlayerRiseInput input = Still(1f);
            float previous = 0f;
            for (int frame = 0; frame < 20 * 60 && model.Stage != PlayerRiseStage.Done; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.Output.Progress, Is.GreaterThanOrEqualTo(previous - 0.0001f), "progress at frame " + frame);
                previous = model.Output.Progress;
            }

            Assert.That(model.Output.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void Seed_IsDeterministic()
        {
            var a = new PlayerRiseModel(77, 0.9f);
            var b = new PlayerRiseModel(77, 0.9f);
            PlayerRiseInput input = Still(0.9f);
            for (int frame = 0; frame < 20 * 60 && a.Stage != PlayerRiseStage.Done; frame++)
            {
                a.Advance(Frame, input);
                b.Advance(Frame, input);
                Assert.That(b.Stage, Is.EqualTo(a.Stage), "stage at frame " + frame);
                Assert.That(b.Output.ClipTime, Is.EqualTo(a.Output.ClipTime), "clip at frame " + frame);
                Assert.That(b.Output.PelvisOffsetMetres, Is.EqualTo(a.Output.PelvisOffsetMetres), "pelvis at frame " + frame);
            }

            Assert.That(b.HandbackVelocity, Is.EqualTo(a.HandbackVelocity));
            Assert.That(new PlayerRiseModel(78, 0.9f).StunSeconds, Is.Not.EqualTo(a.StunSeconds), "another seed, another rise");
        }

        [Test]
        public void Advance_IgnoresNonPositiveDeltas()
        {
            var model = new PlayerRiseModel(1, 1f);
            model.Advance(0f, Still(1f));
            model.Advance(-1f, Still(1f));
            model.Advance(float.NaN, Still(1f));
            Assert.That(model.Elapsed, Is.EqualTo(0f));
            Assert.That(model.Stage, Is.EqualTo(PlayerRiseStage.Settling));
        }
    }
}
