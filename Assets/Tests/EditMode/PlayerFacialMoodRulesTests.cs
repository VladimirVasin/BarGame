using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>The face the moment asks for: a table, pure and complete.</summary>
    public sealed class PlayerFacialMoodRulesTests
    {
        private static PlayerFacialMoodContext Context(
            float intoxication = 1f,
            BalancePhase phase = BalancePhase.Steady,
            float brace = 0f,
            float instability = 0f,
            bool ragdoll = false,
            float ragdollSeconds = 0f,
            bool rise = false,
            PlayerRiseStage stage = PlayerRiseStage.Settling,
            float progress = 0f,
            bool slump = false,
            float nausea = 0f)
        {
            return new PlayerFacialMoodContext(
                intoxication,
                phase,
                brace,
                instability,
                ragdoll,
                ragdollSeconds,
                rise,
                stage,
                progress,
                slump,
                nausea);
        }

        [Test]
        public void Steady_IsNone()
        {
            Assert.That(PlayerFacialMoodRules.Resolve(Context()), Is.EqualTo(PlayerFacialMood.None));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(intoxication: 0f)), Is.EqualTo(PlayerFacialMood.None));
        }

        /// <summary>
        /// The hand at the mouth is a grimace only once it is properly up,
        /// only on his feet, and never over a face a fall is asking for.
        /// </summary>
        [Test]
        public void Nausea_IsAGrimaceOnlyWhileSteady()
        {
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(nausea: 0.4f)),
                Is.EqualTo(PlayerFacialMood.None));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(nausea: 0.5f)),
                Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(nausea: 1f, intoxication: 0f)),
                Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(
                PlayerFacialMoodRules.Resolve(
                    Context(phase: BalancePhase.Recovering, instability: 0.3f, nausea: 1f)),
                Is.EqualTo(PlayerFacialMood.Grimace),
                "A mild stagger does not take the grimace away.");
            Assert.That(
                PlayerFacialMoodRules.Resolve(
                    Context(phase: BalancePhase.Recovering, instability: 0.8f, nausea: 1f)),
                Is.EqualTo(PlayerFacialMood.Tense),
                "The fight for balance comes first.");
            Assert.That(
                PlayerFacialMoodRules.Resolve(
                    Context(phase: BalancePhase.Toppling, brace: 0f, nausea: 1f)),
                Is.EqualTo(PlayerFacialMood.Tense));
            Assert.That(
                PlayerFacialMoodRules.Resolve(
                    Context(ragdoll: true, ragdollSeconds: 2f, nausea: 1f)),
                Is.EqualTo(PlayerFacialMood.Out));
        }

        [Test]
        public void Fight_IsTenseAndTheBraceIsAGrimace()
        {
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(phase: BalancePhase.Recovering, instability: 0.3f)),
                Is.EqualTo(PlayerFacialMood.None));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(phase: BalancePhase.Recovering, instability: 0.8f)),
                Is.EqualTo(PlayerFacialMood.Tense));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(phase: BalancePhase.Toppling)),
                Is.EqualTo(PlayerFacialMood.Tense));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(phase: BalancePhase.Toppling, brace: 0.4f)),
                Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(phase: BalancePhase.Fallen, brace: 1f)),
                Is.EqualTo(PlayerFacialMood.Grimace));
        }

        [Test]
        public void Floor_IsAWinceThenOut()
        {
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(ragdoll: true, ragdollSeconds: 0.2f)),
                Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(ragdoll: true, ragdollSeconds: 2f)),
                Is.EqualTo(PlayerFacialMood.Out));
            // The ragdoll outranks whatever the rise or the balance say.
            Assert.That(
                PlayerFacialMoodRules.Resolve(Context(ragdoll: true, ragdollSeconds: 2f, rise: true, stage: PlayerRiseStage.Kneeling, phase: BalancePhase.Toppling)),
                Is.EqualTo(PlayerFacialMood.Out));
        }

        [Test]
        public void Rise_FollowsItsStages()
        {
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Settling)), Is.EqualTo(PlayerFacialMood.Out));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Stunned)), Is.EqualTo(PlayerFacialMood.Out));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Stirring, progress: 0.2f)), Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Stirring, progress: 0.8f)), Is.EqualTo(PlayerFacialMood.Tense));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.PushingUp)), Is.EqualTo(PlayerFacialMood.Tense));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.PushingUp, slump: true)), Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Crawling)), Is.EqualTo(PlayerFacialMood.Grimace));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Kneeling)), Is.EqualTo(PlayerFacialMood.Tense));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Standing, intoxication: 1f)), Is.EqualTo(PlayerFacialMood.Drowsy));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Standing, intoxication: 0.4f)), Is.EqualTo(PlayerFacialMood.None));
            Assert.That(PlayerFacialMoodRules.Resolve(Context(rise: true, stage: PlayerRiseStage.Done)), Is.EqualTo(PlayerFacialMood.None));
        }
    }
}
