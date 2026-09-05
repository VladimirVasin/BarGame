using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The hero's drunk muttering: the two line pools against the register the
    /// story bible holds him to, the slur that takes them apart, the clock that
    /// decides when he opens his mouth, and the geometry of the letters flying
    /// away from each other.
    /// </summary>
    public sealed class HeroMutterTests
    {
        private const float Step = 1f / 60f;
        private const int Seed = 7311;

        /// <summary>
        /// §16.4 and §7: these words never sound in this game, and the
        /// exception that let him talk about himself at all does not touch
        /// them.
        /// </summary>
        private static readonly string[] ForbiddenWords =
        {
            "вина", "виноват", "алкоголизм", "зависимость",
            "галлюцинация", "социопат",
            "guilt", "alcoholism", "addiction", "hallucination",
            "sociopath"
        };

        /// <summary>§21: no abstractions.</summary>
        private static readonly string[] ForbiddenAbstractions =
        {
            "судьба", "душа", "зло", "истина", "прощение",
            "fate", "soul", "evil", "truth", "forgiveness"
        };

        private static readonly IntoxicationStage[] Stages =
        {
            IntoxicationStage.Unsteady,
            IntoxicationStage.VeryDrunk
        };

        [Test]
        public void Lines_ResolveInBothCatalogsAndHoldHisRegister()
        {
            string[] resourcePaths = { "Localization/ru", "Localization/en" };
            Assert.That(
                HeroMutterLines.UnsteadyLineKeys.Length,
                Is.EqualTo(HeroMutterLines.LinesPerStage));
            Assert.That(
                HeroMutterLines.VeryDrunkLineKeys.Length,
                Is.EqualTo(HeroMutterLines.LinesPerStage));

            for (int catalogIndex = 0;
                 catalogIndex < resourcePaths.Length;
                 catalogIndex++)
            {
                string resourcePath = resourcePaths[catalogIndex];
                Dictionary<string, string> values =
                    LoadCatalog(resourcePath);

                for (int stageIndex = 0;
                     stageIndex < Stages.Length;
                     stageIndex++)
                {
                    IntoxicationStage stage = Stages[stageIndex];
                    string[] keys = HeroMutterLines.LineKeysFor(stage);
                    int maximum = HeroMutterLines.MaximumLengthFor(stage);
                    for (int keyIndex = 0;
                         keyIndex < keys.Length;
                         keyIndex++)
                    {
                        string key = keys[keyIndex];
                        Assert.That(
                            values.ContainsKey(key),
                            Is.True,
                            $"{resourcePath} is missing '{key}'.");
                        AssertRegister(
                            resourcePath,
                            key,
                            values[key],
                            maximum);
                    }
                }
            }
        }

        [Test]
        public void Slur_AtRestReturnsTheLineItself()
        {
            string line = "Ноги ещё держат.";

            Assert.That(
                HeroMutterSlur.Apply(line, 0f, Seed),
                Is.SameAs(line),
                "A sober line must not even be rebuilt.");
            Assert.That(
                HeroMutterSlur.Apply(line, float.NaN, Seed),
                Is.SameAs(line));
            Assert.That(
                HeroMutterSlur.Apply(line, -1f, Seed),
                Is.SameAs(line));
        }

        [Test]
        public void Slur_IsStableForTheSameSeedAndMovesWithIt()
        {
            string line = "Ключи в правом кармане. Я проверил.";
            string first = HeroMutterSlur.Apply(line, 1f, Seed, 64);

            Assert.That(
                HeroMutterSlur.Apply(line, 1f, Seed, 64),
                Is.EqualTo(first),
                "The same seed must slur the same way twice.");

            bool changed = false;
            bool differed = false;
            for (int seed = 0; seed < 48; seed++)
            {
                string slurred = HeroMutterSlur.Apply(line, 1f, seed, 64);
                changed |= slurred != line;
                differed |= slurred != first;
            }

            Assert.That(
                changed,
                Is.True,
                "At full slur the line has to come out changed.");
            Assert.That(
                differed,
                Is.True,
                "Two seeds must not slur the same line identically.");
        }

        [Test]
        public void Slur_KeepsEveryLineInsideItsBudget()
        {
            string[] resourcePaths = { "Localization/ru", "Localization/en" };
            for (int catalogIndex = 0;
                 catalogIndex < resourcePaths.Length;
                 catalogIndex++)
            {
                Dictionary<string, string> values =
                    LoadCatalog(resourcePaths[catalogIndex]);
                for (int stageIndex = 0;
                     stageIndex < Stages.Length;
                     stageIndex++)
                {
                    IntoxicationStage stage = Stages[stageIndex];
                    int cap =
                        HeroMutterLines.MaximumSlurredLengthFor(stage);
                    string[] keys = HeroMutterLines.LineKeysFor(stage);
                    for (int keyIndex = 0;
                         keyIndex < keys.Length;
                         keyIndex++)
                    {
                        string line = values[keys[keyIndex]];
                        int budget = HeroMutterSlur.ResolveBudget(
                            line.Length,
                            cap);
                        for (int seed = 0; seed < 200; seed++)
                        {
                            string slurred = HeroMutterSlur.Apply(
                                line,
                                1f,
                                seed,
                                cap);
                            Assert.That(
                                slurred,
                                Is.Not.Null.And.Not.Empty,
                                $"'{keys[keyIndex]}' slurred to nothing.");
                            Assert.That(
                                slurred.Length,
                                Is.LessThanOrEqualTo(budget),
                                $"'{keys[keyIndex]}' seed {seed} ran to " +
                                $"{slurred.Length}: '{slurred}'.");
                            Assert.That(
                                slurred.Contains("!"),
                                Is.False,
                                "The slur must not raise his voice.");
                        }
                    }
                }
            }
        }

        [Test]
        public void Slur_ScatteredPoolNeverOutgrowsOneRow()
        {
            Dictionary<string, string> values =
                LoadCatalog("Localization/ru");
            string[] keys = HeroMutterLines.VeryDrunkLineKeys;

            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                string line = values[keys[keyIndex]];
                for (int seed = 0; seed < 200; seed++)
                {
                    Assert.That(
                        HeroMutterSlur.Apply(line, 1f, seed).Length,
                        Is.LessThanOrEqualTo(
                            HeroMutterSlur.MaximumSlurredLength),
                        "A scattered line is laid out on one row.");
                }
            }
        }

        [Test]
        public void Slur_StretchesCyrillicAndLatinAlike()
        {
            Assert.That(
                AnyLonger("Ноги ещё держат.", 64),
                Is.True,
                "Cyrillic vowels have to stretch.");
            Assert.That(
                AnyLonger("My legs still hold.", 64),
                Is.True,
                "Latin vowels have to stretch too.");
        }

        [Test]
        public void Slur_KeepsTheTwoSentencesApart()
        {
            string line = "Надо сесть. Ненадолго.";
            for (int seed = 0; seed < 200; seed++)
            {
                string slurred = HeroMutterSlur.Apply(line, 1f, seed);
                int stop = slurred.IndexOf(". ", StringComparison.Ordinal);
                Assert.That(
                    stop,
                    Is.GreaterThan(0),
                    $"seed {seed} ran the sentences together: '{slurred}'.");
            }
        }

        [Test]
        public void Model_RestNarrowsWithPaceAndSpeaksOncePerCycle()
        {
            AssertRestWithin(0f);
            AssertRestWithin(1f);
        }

        [Test]
        public void Model_SameSeedReplaysTheSameCadence()
        {
            var first = new HeroMutterModel(Seed);
            var second = new HeroMutterModel(Seed);
            var other = new HeroMutterModel(Seed + 1);
            int firstLines = 0;
            int otherLines = 0;

            for (int index = 0; index < 60 * 600; index++)
            {
                first.Advance(Step, true, 1f);
                second.Advance(Step, true, 1f);
                other.Advance(Step, true, 1f);
                bool firstCue = first.ConsumeLineCue();
                Assert.That(second.ConsumeLineCue(), Is.EqualTo(firstCue));
                Assert.That(second.Phase, Is.EqualTo(first.Phase));
                if (firstCue)
                {
                    firstLines++;
                }

                if (other.ConsumeLineCue())
                {
                    otherLines++;
                }
            }

            Assert.That(firstLines, Is.GreaterThan(10));
            Assert.That(
                otherLines,
                Is.Not.EqualTo(0),
                "A different seed still talks.");
        }

        [Test]
        public void Model_HugeStepDoesNotSkipASilence()
        {
            var model = new HeroMutterModel(Seed);

            model.Advance(1e6f, true, 1f);

            Assert.That(
                model.Phase,
                Is.EqualTo(HeroMutterPhase.Rest),
                "One dropped frame may never skip a whole silence.");
            Assert.That(model.ConsumeLineCue(), Is.False);
            Assert.That(
                model.PhaseElapsed,
                Is.EqualTo(HeroMutterModel.MaximumStepSeconds)
                    .Within(0.0001f));
        }

        [Test]
        public void Model_WhileNotAllowedHoldsItsSilenceSpent()
        {
            var model = new HeroMutterModel(Seed);
            for (int index = 0; index < 60 * 120; index++)
            {
                model.Advance(Step, false, 1f);
                Assert.That(model.ConsumeLineCue(), Is.False);
                Assert.That(
                    model.Phase,
                    Is.EqualTo(HeroMutterPhase.Rest));
            }

            // The frame the gate opens is the frame he may speak on: he is not
            // owed a fresh wait for having been sat on a stool.
            model.Advance(Step, true, 1f);
            Assert.That(model.ConsumeLineCue(), Is.True);
            Assert.That(model.IsSpeaking, Is.True);
        }

        [Test]
        public void Model_NeverOpensASecondLineInsideTheFirst()
        {
            var model = new HeroMutterModel(Seed);
            float sinceLine = float.PositiveInfinity;

            for (int index = 0; index < 60 * 900; index++)
            {
                model.Advance(Step, true, 1f);
                sinceLine += Step;
                if (!model.ConsumeLineCue())
                {
                    continue;
                }

                Assert.That(
                    sinceLine,
                    Is.GreaterThanOrEqualTo(
                        HeroMutterModel.SpeakingSeconds +
                        HeroMutterModel.FastRestMinimumSeconds -
                        2f * Step),
                    "Two lines landed inside one bubble's life.");
                sinceLine = 0f;
            }
        }

        [Test]
        public void Order_WalksEachPoolAndWrapsIndependently()
        {
            var order = new HeroMutterOrder();
            var seen = new List<string>();

            for (int index = 0;
                 index < HeroMutterLines.LinesPerStage;
                 index++)
            {
                seen.Add(order.ConsumeKey(IntoxicationStage.Unsteady));
            }

            CollectionAssert.AreEqual(
                HeroMutterLines.UnsteadyLineKeys,
                seen,
                "The pool is walked in its authored order.");
            Assert.That(
                order.ConsumeKey(IntoxicationStage.Unsteady),
                Is.EqualTo(HeroMutterLines.UnsteadyLineKeys[0]),
                "And then it wraps.");
            Assert.That(
                order.ConsumeKey(IntoxicationStage.VeryDrunk),
                Is.EqualTo(HeroMutterLines.VeryDrunkLineKeys[0]),
                "Crossing eighty does not resume the other pool's cursor.");
        }

        [Test]
        public void Scatter_HoldsALetterWhereItWasTyped()
        {
            for (int index = 0; index < 32; index++)
            {
                SpeechScatterLayout.ResolveGlyph(
                    index,
                    (uint)Seed,
                    0f,
                    1f,
                    out Vector2 atBirth,
                    out float turnAtBirth);
                Assert.That(atBirth, Is.EqualTo(Vector2.zero));
                Assert.That(turnAtBirth, Is.EqualTo(0f));

                SpeechScatterLayout.ResolveGlyph(
                    index,
                    (uint)Seed,
                    2f,
                    0f,
                    out Vector2 sober,
                    out float soberTurn);
                Assert.That(sober, Is.EqualTo(Vector2.zero));
                Assert.That(soberTurn, Is.EqualTo(0f));
            }

            Assert.That(SpeechScatterLayout.IsScattering(0f), Is.False);
            Assert.That(SpeechScatterLayout.IsScattering(float.NaN), Is.False);
            Assert.That(SpeechScatterLayout.IsScattering(1f), Is.True);
        }

        [Test]
        public void Scatter_LosesWordShapeAtFullAmount()
        {
            float furthest = 0f;
            for (int index = 0; index < 22; index++)
            {
                for (int sample = 1; sample <= 240; sample++)
                {
                    SpeechScatterLayout.ResolveGlyph(
                        index,
                        (uint)Seed,
                        sample / 60f,
                        1f,
                        out Vector2 offset,
                        out float _);
                    furthest = Mathf.Max(furthest, offset.magnitude);
                }
            }

            Assert.That(
                furthest,
                Is.GreaterThan(6f),
                "At full scatter a letter has to leave its own cell.");
        }

        [Test]
        public void Scatter_IsDeterministicAndStaysOnTheCanvas()
        {
            SpeechScatterLayout.ResolveGlyph(
                7,
                (uint)Seed,
                1.25f,
                0.8f,
                out Vector2 first,
                out float firstTurn);
            SpeechScatterLayout.ResolveGlyph(
                7,
                (uint)Seed,
                1.25f,
                0.8f,
                out Vector2 second,
                out float secondTurn);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(secondTurn, Is.EqualTo(firstTurn));

            Vector2[] corners =
            {
                new Vector2(-40f, -40f),
                new Vector2(RetroUiTheme.LogicalWidth + 40f, -40f),
                new Vector2(-40f, RetroUiTheme.LogicalHeight + 40f),
                new Vector2(
                    RetroUiTheme.LogicalWidth + 40f,
                    RetroUiTheme.LogicalHeight + 40f)
            };
            for (int index = 0; index < corners.Length; index++)
            {
                Rect glyph = SpeechScatterLayout.ResolveGlyphRect(
                    corners[index],
                    120f,
                    5f,
                    9f,
                    new Vector2(60f, -80f));
                Assert.That(
                    glyph.xMin,
                    Is.GreaterThanOrEqualTo(0f),
                    "A glyph must not draw in the letterbox.");
                Assert.That(
                    glyph.yMin,
                    Is.GreaterThanOrEqualTo(0f));
                Assert.That(
                    glyph.xMax,
                    Is.LessThanOrEqualTo(RetroUiTheme.LogicalWidth));
                Assert.That(
                    glyph.yMax,
                    Is.LessThanOrEqualTo(RetroUiTheme.LogicalHeight));
            }
        }

        [Test]
        public void Scatter_AtRestReproducesThePlainRow()
        {
            float[] widths = { 0f, 5f, 11f, 16f, 22f };
            var origin = new Vector2(100f, 80f);

            for (int index = 0; index < widths.Length - 1; index++)
            {
                Rect glyph = SpeechScatterLayout.ResolveGlyphRect(
                    origin,
                    SpeechScatterLayout.ResolvePenX(widths, index),
                    SpeechScatterLayout.ResolveGlyphWidth(widths, index),
                    9f,
                    Vector2.zero);
                Assert.That(
                    glyph.center.x,
                    Is.EqualTo(
                            origin.x +
                            widths[index] +
                            (widths[index + 1] - widths[index]) * 0.5f)
                        .Within(0.51f),
                    "An unscattered glyph sits on its own advance.");
                Assert.That(
                    glyph.center.y,
                    Is.EqualTo(origin.y + 4.5f).Within(0.51f));
            }
        }

        [Test]
        public void Curves_GateTheMutterOnTheBalanceThreshold()
        {
            Assert.That(
                IntoxicationStageRules.Evaluate(60).MutterSlurAmount,
                Is.Zero);
            Assert.That(
                IntoxicationStageRules.Evaluate(61).MutterSlurAmount,
                Is.GreaterThan(0f));
            Assert.That(
                IntoxicationStageRules.Evaluate(80).MutterSlurAmount,
                Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(
                IntoxicationStageRules.Evaluate(100).MutterSlurAmount,
                Is.EqualTo(1f).Within(0.001f));

            // The letters hold together for the whole of the Unsteady stage.
            Assert.That(
                IntoxicationStageRules.Evaluate(80).MutterScatterAmount,
                Is.Zero);
            Assert.That(
                IntoxicationStageRules.Evaluate(81).MutterScatterAmount,
                Is.GreaterThan(0f));
            Assert.That(
                IntoxicationStageRules.Evaluate(100).MutterScatterAmount,
                Is.EqualTo(1f).Within(0.001f));

            Assert.That(
                HeroMutterLines.HasPool(IntoxicationStage.Drunk),
                Is.False);
            Assert.That(
                HeroMutterLines.HasPool(IntoxicationStage.Unsteady),
                Is.True);
            Assert.That(
                HeroMutterLines.ScattersAt(IntoxicationStage.Unsteady),
                Is.False);
            Assert.That(
                HeroMutterLines.ScattersAt(IntoxicationStage.VeryDrunk),
                Is.True);
        }

        /// <summary>
        /// §16.2: the citizens never react to anything strange about him. The
        /// speech views hand out no events, and nothing in the pedestrian stack
        /// so much as names them — asserted here so the law fails a test rather
        /// than a review.
        /// </summary>
        [Test]
        public void Citizens_CannotHearHim()
        {
            Type[] types = typeof(IntoxicationMutterPresenter)
                .Assembly
                .GetTypes();
            var offenders = new List<string>();
            for (int index = 0; index < types.Length; index++)
            {
                Type type = types[index];
                if (type.Namespace == null ||
                    !type.Namespace.Contains("BarPromenade"))
                {
                    continue;
                }

                if (!type.Name.StartsWith(
                        "CityPedestrian",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (ReferencesSpeech(type))
                {
                    offenders.Add(type.Name);
                }
            }

            CollectionAssert.IsEmpty(
                offenders,
                "A pedestrian has learned to hear him.");
        }

        private static bool ReferencesSpeech(Type type)
        {
            System.Reflection.FieldInfo[] fields = type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                Type fieldType = fields[index].FieldType;
                if (fieldType == typeof(NpcSpeechBubbleView) ||
                    fieldType == typeof(NpcSpeaker))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyLonger(string line, int cap)
        {
            for (int seed = 0; seed < 60; seed++)
            {
                if (HeroMutterSlur.Apply(line, 1f, seed, cap).Length >
                    line.Length)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The interval between two lines is the bubble's own life plus the
        /// silence drawn for this pace, so the silence is what is measured.
        /// </summary>
        private static void AssertRestWithin(float pace)
        {
            HeroMutterModel.ResolveRestRange(
                pace,
                out float minimum,
                out float maximum);
            var model = new HeroMutterModel(Seed);
            float sinceCue = 0f;
            bool sawFirst = false;
            int measured = 0;

            for (int index = 0; index < 60 * 1800; index++)
            {
                model.Advance(Step, true, pace);
                sinceCue += Step;
                if (!model.ConsumeLineCue())
                {
                    continue;
                }

                if (sawFirst)
                {
                    float rest =
                        sinceCue - HeroMutterModel.SpeakingSeconds;
                    Assert.That(
                        rest,
                        Is.InRange(
                            minimum - 4f * Step,
                            maximum + 4f * Step),
                        $"pace {pace} drew a {rest} s silence.");
                    measured++;
                }

                sawFirst = true;
                sinceCue = 0f;
            }

            Assert.That(
                measured,
                Is.GreaterThan(5),
                $"pace {pace} produced almost no lines.");
        }

        private static void AssertRegister(
            string resourcePath,
            string key,
            string line,
            int maximumLength)
        {
            Assert.That(
                line,
                Is.Not.Null.And.Not.Empty,
                $"{resourcePath} leaves '{key}' blank.");
            Assert.That(
                line.Length,
                Is.LessThanOrEqualTo(maximumLength),
                $"{resourcePath} '{key}' is too long: {line.Length}.");
            Assert.That(
                line.EndsWith(".", StringComparison.Ordinal),
                Is.True,
                $"{resourcePath} '{key}' must end as a statement.");
            Assert.That(
                line.Contains("!"),
                Is.False,
                $"{resourcePath} '{key}' raises his voice.");
            Assert.That(
                line.Contains("?"),
                Is.False,
                $"{resourcePath} '{key}' asks into the void.");
            Assert.That(
                line.Contains("("),
                Is.False,
                $"{resourcePath} '{key}' explains itself in brackets.");
            Assert.That(
                line.Count(character => character == '.'),
                Is.InRange(1, 2),
                $"{resourcePath} '{key}' runs past two short sentences.");
            Assert.That(
                line.Any(char.IsDigit),
                Is.False,
                $"{resourcePath} '{key}' names a number.");

            string lowered = line.ToLowerInvariant();
            for (int index = 0; index < ForbiddenWords.Length; index++)
            {
                Assert.That(
                    lowered.Contains(ForbiddenWords[index]),
                    Is.False,
                    $"{resourcePath} '{key}' says " +
                    $"'{ForbiddenWords[index]}', which never sounds in " +
                    "this game.");
            }

            for (int index = 0;
                 index < ForbiddenAbstractions.Length;
                 index++)
            {
                Assert.That(
                    lowered.Contains(ForbiddenAbstractions[index]),
                    Is.False,
                    $"{resourcePath} '{key}' reaches for " +
                    $"'{ForbiddenAbstractions[index]}'.");
            }
        }

        private static Dictionary<string, string> LoadCatalog(
            string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            Assert.That(
                asset,
                Is.Not.Null,
                $"Expected a TextAsset at Resources/{resourcePath}.json.");

            Catalog catalog = JsonUtility.FromJson<Catalog>(asset.text);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.entries, Is.Not.Null);

            var values =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                CatalogEntry entry = catalog.entries[index];
                Assert.That(
                    entry,
                    Is.Not.Null,
                    $"{resourcePath} contains a null entry.");
                Assert.That(
                    values.ContainsKey(entry.key),
                    Is.False,
                    $"{resourcePath} contains duplicate key '{entry.key}'.");
                values.Add(entry.key, entry.value);
            }

            return values;
        }

        [Serializable]
        private sealed class Catalog
        {
            public CatalogEntry[] entries = Array.Empty<CatalogEntry>();
        }

        [Serializable]
        private sealed class CatalogEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
