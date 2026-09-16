using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    [Category("CityArchShelter")]
    public sealed class CityArchShelterConversationTests
    {
        private const int AllRoles = 7;
        private const int WarmersOnly = 3;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            GameSessionState.BeginNewGame();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
            Time.timeScale = previousTimeScale;
        }

        [TestCase("ru")]
        [TestCase("en")]
        public void Catalog_EveryExchangeHasBothLocalizedLinesInTheRegister(string language)
        {
            var catalog = JsonUtility.FromJson<SpeechCatalog>(LoadCatalog(language));
            Dictionary<string, string> lines = catalog.entries.ToDictionary(
                entry => entry.key, entry => entry.value);
            var seen = new HashSet<string>();
            foreach (CityArchShelterConversationKind kind in System.Enum.GetValues(
                         typeof(CityArchShelterConversationKind)))
            {
                for (int variant = 0; variant < CityArchShelterConversationCatalog.Count(kind); variant++)
                {
                    CityArchShelterConversationExchange exchange =
                        CityArchShelterConversationCatalog.Get(kind, variant);
                    Assert.That(exchange.FirstRole, Is.Not.EqualTo(exchange.SecondRole));
                    Assert.That(
                        kind == CityArchShelterConversationKind.Sleeper
                            ? exchange.SecondRole == CityArchShelterConversationCatalog.SleeperRole &&
                              exchange.FirstRole != CityArchShelterConversationCatalog.SleeperRole
                            : exchange.FirstRole != CityArchShelterConversationCatalog.SleeperRole &&
                              exchange.SecondRole != CityArchShelterConversationCatalog.SleeperRole,
                        Is.True,
                        $"{exchange.FirstKey}: the sleeper only ever answers.");
                    foreach (string key in new[] { exchange.FirstKey, exchange.SecondKey })
                    {
                        Assert.That(seen.Add(key), Is.True, key);
                        Assert.That(lines.ContainsKey(key), Is.True, $"{language} lacks '{key}'.");
                        string line = lines[key];
                        Assert.That(line, Does.Not.Contain("!"), key);
                        Assert.That(line, Does.Not.Contain("("), key);
                        Assert.That(line.Trim(), Is.EqualTo(line), key);
                        // Fits the shared ambient bubble with a reading pause,
                        // like every other ambient pool in the city.
                        Assert.That(
                            NpcSpeechBubbleView.VisibleSeconds - line.Length / SpeechDelivery.CharactersPerSecond,
                            Is.GreaterThanOrEqualTo(1f),
                            $"{language} '{key}' must finish before the ambient bubble closes.");
                    }
                }
            }

            Assert.That(
                CityArchShelterConversationCatalog.Count(CityArchShelterConversationKind.Warmers),
                Is.EqualTo(CityArchShelterConversationCatalog.WarmerCount));
            Assert.That(
                CityArchShelterConversationCatalog.Count(CityArchShelterConversationKind.Sleeper),
                Is.EqualTo(CityArchShelterConversationCatalog.SleeperCount));
        }

        [Test]
        public void Schedule_SpeaksEveryPairOnceBeforeRepeatingAndAlwaysInAuthoredOrder()
        {
            var schedule = new CityArchShelterConversationSchedule(20260916);
            var other = new CityArchShelterConversationSchedule(20260916);
            var recorder = new Recorder();
            var otherRecorder = new Recorder();
            List<string> heard = recorder.Heard;
            const int exchanges = 60;
            double life = 0d;
            while (heard.Count < exchanges * 2 && life < 60000d)
            {
                life += .1d;
                recorder.Record(schedule.Advance(life, AllRoles, true, 3d, 3d));
                otherRecorder.Record(other.Advance(life, AllRoles, true, 3d, 3d));
            }

            Assert.That(heard, Is.EqualTo(otherRecorder.Heard), "The same seed tells the same evening.");
            Assert.That(heard, Has.Count.EqualTo(exchanges * 2));
            // Lines come in authored pairs: a first line is always followed
            // by its own reply before anything else is said.
            for (int index = 0; index < heard.Count; index += 2)
            {
                Assert.That(heard[index], Does.EndWith(".a"));
                Assert.That(heard[index + 1], Is.EqualTo(heard[index].Substring(0, heard[index].Length - 2) + ".b"));
            }

            string[] firstLines = heard.Where((key, index) => index % 2 == 0).ToArray();
            for (int index = 1; index < firstLines.Length; index++)
                Assert.That(firstLines[index], Is.Not.EqualTo(firstLines[index - 1]), "No exchange twice in a row.");
            foreach (var pool in new[]
                     {
                         (".warmers.", CityArchShelterConversationCatalog.WarmerCount),
                         (".sleeper.", CityArchShelterConversationCatalog.SleeperCount)
                     })
            {
                string[] heardFromPool = firstLines.Where(key => key.Contains(pool.Item1)).ToArray();
                Assert.That(heardFromPool, Has.Length.GreaterThan(pool.Item2), pool.Item1);
                Assert.That(heardFromPool.Take(pool.Item2).Distinct().Count(), Is.EqualTo(pool.Item2),
                    $"{pool.Item1}: no exchange repeats while an unheard one remains.");
            }
            int firstSleeper = System.Array.FindIndex(firstLines, key => key.Contains(".sleeper."));
            Assert.That(firstSleeper, Is.GreaterThanOrEqualTo(CityArchShelterConversationSchedule.SleeperEvery - 1),
                "The sleeper is not the first thing anyone talks to.");
        }

        [Test]
        public void Schedule_DefersSleeperPairsWhileHeIsUnavailableAndStopsOutOfEarshot()
        {
            var schedule = new CityArchShelterConversationSchedule(7);
            var recorder = new Recorder();
            List<string> heard = recorder.Heard;
            double life = 0d;
            while (heard.Count < 2 * CityArchShelterConversationCatalog.WarmerCount && life < 20000d)
            {
                life += .1d;
                recorder.Record(schedule.Advance(life, WarmersOnly, true, 3d, 3d));
            }

            Assert.That(heard, Has.None.Contains(".sleeper."),
                "An absent partner defers his entries; they never fill the pool.");
            Assert.That(heard.Where((key, index) => index % 2 == 0).Distinct().Count(),
                Is.EqualTo(CityArchShelterConversationCatalog.WarmerCount));

            // Walking out of earshot ends the exchange at once; coming back
            // does not replay what was missed.
            // Let the reply in progress finish, then wait for the next first line.
            while (schedule.Current.HasExchange && life < 20000d)
            {
                life += .1d;
                schedule.Advance(life, AllRoles, true, 3d, 3d);
            }
            int before = schedule.StartedLineCount;
            while (life < 20000d)
            {
                life += .1d;
                CityArchShelterConversationTurn turn = schedule.Advance(life, AllRoles, true, 3d, 3d);
                if (turn.IsSpeaking) break;
            }
            Assert.That(schedule.Current.IsSpeaking, Is.True);
            string cut = schedule.Current.LineKey;
            CityArchShelterConversationTurn silent = schedule.Advance(life + .1d, AllRoles, false, 3d, 3d);
            Assert.That(silent.HasExchange, Is.False);
            Assert.That(schedule.Current.HasExchange, Is.False);
            life += .2d;
            CityArchShelterConversationTurn resumed = schedule.Advance(life, AllRoles, true, 3d, 3d);
            Assert.That(resumed.HasExchange, Is.False, "Nothing queues up while the hero is away.");
            Assert.That(schedule.StartedLineCount, Is.EqualTo(before + 1));
            Assert.That(cut, Does.EndWith(".a"));
        }

        [Test]
        public void Schedule_ATimeJumpResetsWithoutACatchUpBurst()
        {
            var schedule = new CityArchShelterConversationSchedule(11);
            double life = 0d;
            CityArchShelterConversationTurn turn = default;
            while (life < 200d)
            {
                life += .1d;
                turn = schedule.Advance(life, AllRoles, true, 3d, 3d);
                if (turn.IsSpeaking) break;
            }
            Assert.That(turn.IsSpeaking, Is.True);
            int started = schedule.StartedLineCount;
            CityArchShelterConversationTurn jumped = schedule.Advance(life + 60d, AllRoles, true, 3d, 3d);
            Assert.That(jumped.HasExchange, Is.False);
            Assert.That(schedule.StartedLineCount, Is.EqualTo(started));
            Assert.That(schedule.NextAttemptSeconds, Is.GreaterThanOrEqualTo(
                life + 60d + CityArchShelterConversationSchedule.FirstAttemptMinimumSeconds));
        }

        [Test]
        public void Controller_HangsEachLineOverItsRealHeadAndFallsSilentWhenTheHeroLeaves()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityArchShelterPlan plan = CityArchShelterPlanner.Create(layout);
            Assert.That(plan.IsEnabled, Is.True);
            Assert.That(GameSessionState.TryStartGameTimeAt(23 * 60), Is.True);
            var parent = new GameObject("Arch Shelter Conversation Test");
            var hero = new GameObject("Hero").transform;
            try
            {
                CityArchShelterWorldResult world = CityArchShelterWorldBuilder.Build(
                    parent.transform,
                    layout,
                    plan);
                Vector3 barrel = plan.Props.Single(prop => prop.Kind == CityArchShelterPropKind.BurnBarrel).Position;
                hero.position = barrel + new Vector3(-3f, 0f, 0f);
                CityArchShelterConversationController controller =
                    CityArchShelterConversationController.Create(world, hero, null, layout.Seed);
                Assert.That(controller, Is.Not.Null);
                controller.AutoAdvance = false;
                for (int role = 0; role < CityArchShelterConversationCatalog.RoleCount; role++)
                {
                    Assert.That(controller.Speaker(role), Is.Not.Null, role.ToString());
                    Assert.That(controller.Head(role), Is.Not.Null, role.ToString());
                    Assert.That(controller.Bubbles.IsDeclared(controller.Speaker(role)), Is.True);
                }

                var spoken = new List<(string key, int role)>();
                string last = string.Empty;
                for (int frame = 0; frame < 6000 && spoken.Count < 6; frame++)
                {
                    controller.Advance(1f / 30f);
                    if (controller.LastLineKey != last)
                    {
                        last = controller.LastLineKey;
                        spoken.Add((last, controller.LastSpeakerRole));
                        Assert.That(controller.Bubbles.IsShowing(controller.Speaker(controller.LastSpeakerRole)),
                            Is.True, last);
                        Assert.That(NpcSpeechBubbleView.IsPresentingAt(controller.Head(controller.LastSpeakerRole)),
                            Is.True, "The line hangs over the speaker's own head.");
                    }
                }

                Assert.That(spoken, Has.Count.EqualTo(6));
                for (int index = 0; index < spoken.Count; index += 2)
                {
                    Assert.That(spoken[index].key, Does.StartWith("city.shelter."));
                    Assert.That(spoken[index].key, Does.EndWith(".a"));
                    Assert.That(spoken[index + 1].key, Does.EndWith(".b"));
                    Assert.That(spoken[index].role, Is.Not.EqualTo(spoken[index + 1].role),
                        "The reply comes from the partner, never the same man.");
                }

                // The listener walks away: the channel goes quiet at once and
                // leaves no bubble behind.
                hero.position = barrel + new Vector3(60f, 0f, 0f);
                for (int frame = 0; frame < 60; frame++) controller.Advance(1f / 30f);
                for (int role = 0; role < CityArchShelterConversationCatalog.RoleCount; role++)
                    Assert.That(controller.Bubbles.IsShowing(controller.Speaker(role)), Is.False);
                int started = controller.StartedLineCount;
                for (int frame = 0; frame < 900; frame++) controller.Advance(1f / 30f);
                Assert.That(controller.StartedLineCount, Is.EqualTo(started),
                    "Nobody talks to an empty arch.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(hero.gameObject);
            }
        }

        /// <summary>Every started line once, by its serial, however many frames it stays up.</summary>
        private sealed class Recorder
        {
            private int lastSerial = -1;
            public List<string> Heard { get; } = new List<string>();
            public void Record(CityArchShelterConversationTurn turn)
            {
                if (!turn.IsSpeaking || turn.LineSerial == lastSerial) return;
                lastSerial = turn.LineSerial;
                Heard.Add(turn.LineKey);
            }
        }

        private static string LoadCatalog(string language)
        {
            var asset = Resources.Load<TextAsset>($"Localization/{language}");
            Assert.That(asset, Is.Not.Null, $"The '{language}' catalog is missing.");
            return asset.text;
        }

        [System.Serializable]
        private sealed class SpeechCatalog
        {
            public SpeechCatalogEntry[] entries = System.Array.Empty<SpeechCatalogEntry>();
        }

        [System.Serializable]
        private sealed class SpeechCatalogEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
