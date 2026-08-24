using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The Ferryman's pure halves: where he is allowed to exist, how his
    /// coin flies, and what he is allowed to say.
    ///
    /// The coin gets the most attention here because it is the part with no
    /// state to inspect at runtime - its whole correctness is in three
    /// functions of one number, and a coin that drifts out of the hand is
    /// the single most visible way this character can break.
    /// </summary>
    public sealed class LastRouteFerrymanTests
    {
        private const float Tolerance = 1e-5f;

        // ------------------------------------------------------- presence

        [Test]
        public void Plan_IsAbsent_WithoutACar()
        {
            LastRouteFerrymanPlan plan =
                LastRouteFerrymanPlan.Create(null);

            Assert.That(plan, Is.Not.Null);
            Assert.That(
                plan.IsPresent,
                Is.False,
                "A man perched on a car that was never parked is worse " +
                "than no man.");
        }

        // ----------------------------------------------------------- coin

        [Test]
        public void CoinArc_StartsAndEndsInTheHand()
        {
            Assert.That(
                LastRouteFerrymanCoin.ArcHeightAt(0f),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                LastRouteFerrymanCoin.ArcHeightAt(1f),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void CoinArc_PeaksExactlyHalfWay()
        {
            Assert.That(
                LastRouteFerrymanCoin.ArcHeightAt(0.5f),
                Is.EqualTo(1f).Within(Tolerance));

            // And nowhere else: sampled densely, no point outranks the apex.
            for (int step = 0; step <= 200; step++)
            {
                float phase = step / 200f;
                Assert.That(
                    LastRouteFerrymanCoin.ArcHeightAt(phase),
                    Is.LessThanOrEqualTo(1f + Tolerance),
                    $"The arc rises past its apex at {phase:0.###}.");
            }
        }

        [Test]
        public void CoinDrift_IsSymmetricAboutTheHand()
        {
            Assert.That(
                LastRouteFerrymanCoin.ArcDriftAt(0f),
                Is.EqualTo(-0.5f).Within(Tolerance));
            Assert.That(
                LastRouteFerrymanCoin.ArcDriftAt(0.5f),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                LastRouteFerrymanCoin.ArcDriftAt(1f),
                Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void CoinSpin_IsAnOddNumberOfHalfTurns()
        {
            // This is what makes it a coin flip rather than a spinning prop:
            // an odd count of half-turns lands the other face up.
            int halfTurns = LastRouteFerrymanCoin.FlipsPerToss;
            Assert.That(
                halfTurns % 2,
                Is.EqualTo(1),
                "An even number of flips returns the same face and reads " +
                "as a prop being twirled.");
        }

        [Test]
        public void CoinSpin_ClosesOnAWholeNumberOfTurns()
        {
            // Three flips is 1080 degrees, which is congruent to zero: the
            // catch therefore has no rotational seam even though the face
            // has changed.
            float degrees = LastRouteFerrymanCoin.SpinDegreesAt(1f);
            Assert.That(
                Mathf.Repeat(degrees, 360f),
                Is.EqualTo(0f).Within(0.001f),
                $"A toss ending at {degrees} degrees snaps on the catch.");
        }

        // ------------------------------------------------- the toss window

        [Test]
        public void CoinIsAirborne_OnlyBetweenTheAuthoredKeys()
        {
            Assert.That(
                LastRouteFerrymanPresentation.IsCoinAirborneAt(0f),
                Is.False);
            Assert.That(
                LastRouteFerrymanPresentation.IsCoinAirborneAt(
                    LastRouteFerrymanPresentation.TossReleasePhase),
                Is.True);
            Assert.That(
                LastRouteFerrymanPresentation.IsCoinAirborneAt(
                    LastRouteFerrymanPresentation.TossCatchPhase),
                Is.True);
            Assert.That(
                LastRouteFerrymanPresentation.IsCoinAirborneAt(0.5f),
                Is.False,
                "Half way through the loop he is breathing, not throwing.");
            Assert.That(
                LastRouteFerrymanPresentation.IsCoinAirborneAt(0.99f),
                Is.False);
        }

        [Test]
        public void TossWindow_MatchesTheAuthoredKeyGrid()
        {
            // These two numbers are a contract with the FerrymanWait key
            // grid in tools/build-city-pedestrian-3d-model.py: the flick key
            // sits at 1/16 of the loop and the catch key at 5/16. If the
            // clip is ever re-timed without re-timing these, the coin leaves
            // a hand that has not moved.
            Assert.That(
                LastRouteFerrymanPresentation.TossReleasePhase,
                Is.EqualTo(1f / 16f).Within(Tolerance));
            Assert.That(
                LastRouteFerrymanPresentation.TossCatchPhase,
                Is.EqualTo(5f / 16f).Within(Tolerance));
        }

        [Test]
        public void FlightPhase_RunsZeroToOneAcrossTheWindow()
        {
            Assert.That(
                LastRouteFerrymanPresentation.TossFlightPhaseAt(
                    LastRouteFerrymanPresentation.TossReleasePhase),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                LastRouteFerrymanPresentation.TossFlightPhaseAt(
                    LastRouteFerrymanPresentation.TossCatchPhase),
                Is.EqualTo(1f).Within(Tolerance));

            // Outside the window it reads as "in the hand" rather than as
            // some clamped point half way up the arc.
            Assert.That(
                LastRouteFerrymanPresentation.TossFlightPhaseAt(0.7f),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void FlightPhase_IsContinuousAcrossTheLoopSeam()
        {
            // Nothing is airborne at either end of the loop, so the seam is
            // a coin sitting still in a hand on both sides of it.
            Assert.That(
                LastRouteFerrymanCoin.ArcHeightAt(
                    LastRouteFerrymanPresentation.TossFlightPhaseAt(
                        0.9999f)),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                LastRouteFerrymanCoin.ArcHeightAt(
                    LastRouteFerrymanPresentation.TossFlightPhaseAt(0f)),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void BreathGrid_RestsAtEveryQuarterOfTheLoop()
        {
            Assert.That(
                LastRouteFerrymanPresentation.BreathsPerLoop,
                Is.EqualTo(4),
                "The FerrymanWait grid keys a rest at every quarter.");
        }

        // ---------------------------------------------------------- quips

        [Test]
        public void Quips_AreDeterministicForASeed()
        {
            Assert.That(
                DrawLines(4242, 40),
                Is.EqualTo(DrawLines(4242, 40)));
        }

        [Test]
        public void Quips_NeverRepeatTwiceRunning()
        {
            List<int> drawn = DrawLines(90210, 400);
            for (int index = 1; index < drawn.Count; index++)
            {
                Assert.That(
                    drawn[index],
                    Is.Not.EqualTo(drawn[index - 1]),
                    $"He said line {drawn[index]} twice in a row.");
            }
        }

        [Test]
        public void Quips_ReachEveryLineEventually()
        {
            var seen = new HashSet<int>(DrawLines(7, 2000));
            Assert.That(
                seen.Count,
                Is.EqualTo(LastRouteFerrymanQuips.LineKeys.Length),
                "A line nobody can ever hear is a line that is not there.");
        }

        [Test]
        public void Quips_NeverOfferARide()
        {
            // The offer lives on the menu and only on the menu. That is
            // both the joke and an honest interface: nothing in his mouth
            // may promise a thing the game does not have.
            string[] forbidden =
            {
                "садись", "поехали", "поедем", "подвезу", "довезу",
                "get in", "let's go", "i'll drive you", "hop in",
                "ride with me"
            };

            Dictionary<string, string> russian =
                LoadCatalog("Localization/ru");
            Dictionary<string, string> english =
                LoadCatalog("Localization/en");

            for (int index = 0;
                 index < LastRouteFerrymanQuips.LineKeys.Length;
                 index++)
            {
                string key = LastRouteFerrymanQuips.LineKeys[index];
                AssertNoOffer(key, russian[key], forbidden);
                AssertNoOffer(key, english[key], forbidden);
            }
        }

        [Test]
        public void Quips_AndPromptsResolveInBothCatalogs()
        {
            Dictionary<string, string> russian =
                LoadCatalog("Localization/ru");
            Dictionary<string, string> english =
                LoadCatalog("Localization/en");

            var required = new List<string>(
                LastRouteFerrymanQuips.LineKeys)
            {
                LastRouteFerrymanInteraction.DefaultPromptKey,
                LastRouteFerrymanInteraction.LeaveConfirmationPromptKey
            };

            for (int index = 0; index < required.Count; index++)
            {
                string key = required[index];
                Assert.That(
                    russian.ContainsKey(key),
                    Is.True,
                    $"ru.json has no '{key}'.");
                Assert.That(
                    english.ContainsKey(key),
                    Is.True,
                    $"en.json has no '{key}'.");
                Assert.That(
                    russian[key],
                    Is.Not.Null.And.Not.Empty,
                    $"ru.json leaves '{key}' blank.");
                Assert.That(
                    english[key],
                    Is.Not.Null.And.Not.Empty,
                    $"en.json leaves '{key}' blank.");
            }
        }

        [Test]
        public void Quips_StayShort()
        {
            // Two short clauses at most, level, no exclamations. Charon
            // does not raise his voice.
            Dictionary<string, string> russian =
                LoadCatalog("Localization/ru");

            for (int index = 0;
                 index < LastRouteFerrymanQuips.LineKeys.Length;
                 index++)
            {
                string key = LastRouteFerrymanQuips.LineKeys[index];
                string line = russian[key];
                Assert.That(
                    line.Length,
                    Is.LessThanOrEqualTo(48),
                    $"'{key}' runs long for a man who does not explain.");
                Assert.That(
                    line.Contains("!"),
                    Is.False,
                    $"'{key}' raises its voice.");
            }
        }

        // -------------------------------------------------------- helpers

        private static void AssertNoOffer(
            string key,
            string line,
            string[] forbidden)
        {
            for (int index = 0; index < forbidden.Length; index++)
            {
                Assert.That(
                    line.IndexOf(
                        forbidden[index],
                        StringComparison.OrdinalIgnoreCase),
                    Is.LessThan(0),
                    $"'{key}' offers a ride ('{forbidden[index]}'): " +
                    $"\"{line}\".");
            }
        }

        private static List<int> DrawLines(int seed, int count)
        {
            uint state = LastRouteFerrymanQuips.CreateState(seed);
            int previous = -1;
            var drawn = new List<int>(count);
            for (int index = 0; index < count; index++)
            {
                previous = LastRouteFerrymanQuips.NextIndex(
                    ref state,
                    previous);
                drawn.Add(previous);
            }

            return drawn;
        }

        private static Dictionary<string, string> LoadCatalog(
            string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            Assert.That(
                asset,
                Is.Not.Null,
                $"Expected a TextAsset at Resources/{resourcePath}.json.");

            var catalog =
                JsonUtility.FromJson<Catalog>(asset.text);
            var valuesByKey =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                valuesByKey[catalog.entries[index].key] =
                    catalog.entries[index].value;
            }

            return valuesByKey;
        }

        [Serializable]
        private sealed class Catalog
        {
            public Entry[] entries = Array.Empty<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
