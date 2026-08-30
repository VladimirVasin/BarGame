using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The pure halves of the dash: the detent model behind the radio's
    /// tuning knob, the glovebox lid's two curves, and the three clicks the
    /// car's voice makes for them. None of this needs a scene.
    /// </summary>
    public sealed class LastRouteCarRadioTests
    {
        [Test]
        public void Detents_StepRoundAndWrap()
        {
            Assert.That(LastRouteCarRadioModel.StepDetent(0), Is.EqualTo(1));
            Assert.That(
                LastRouteCarRadioModel.StepDetent(
                    LastRouteCarRadioModel.DetentCount - 1),
                Is.Zero,
                "Off the far end, back to the start.");
            Assert.That(LastRouteCarRadioModel.WrapDetent(-1),
                Is.EqualTo(LastRouteCarRadioModel.DetentCount - 1));
            Assert.That(LastRouteCarRadioModel.Tuning01FromDetent(0), Is.Zero);
            Assert.That(
                LastRouteCarRadioModel.Tuning01FromDetent(
                    LastRouteCarRadioModel.DetentCount - 1),
                Is.EqualTo(1f));
            float previous = -1f;
            for (int detent = 0; detent < LastRouteCarRadioModel.DetentCount; detent++)
            {
                float tuning = LastRouteCarRadioModel.Tuning01FromDetent(detent);
                Assert.That(tuning, Is.GreaterThan(previous));
                Assert.That(
                    LastRouteCarRadioModel.TuningKnobDegrees(detent),
                    Is.EqualTo(detent * LastRouteCarRadioModel.KnobDegreesPerDetent)
                        .Within(0.0001f));
                previous = tuning;
            }

            Assert.That(
                LastRouteCarRadioModel.DefaultDetent,
                Is.GreaterThan(0).And.LessThan(LastRouteCarRadioModel.DetentCount - 1),
                "A new game's needle stands off both ends so the first click moves it.");
            var state = new LastRouteCarDashboardState(false, 11, false);
            Assert.That(state.TuningDetent, Is.EqualTo(3), "The state wraps too.");
        }

        [Test]
        public void GloveboxCurves_DropAndCatchThenPushShut()
        {
            Assert.That(LastRouteCarGloveboxTimeline.EvaluateOpenness(0f, true), Is.Zero);
            Assert.That(LastRouteCarGloveboxTimeline.EvaluateOpenness(1f, true), Is.EqualTo(1f));
            Assert.That(LastRouteCarGloveboxTimeline.EvaluateOpenness(0f, false), Is.EqualTo(1f));
            Assert.That(LastRouteCarGloveboxTimeline.EvaluateOpenness(1f, false), Is.Zero);
            Assert.That(
                LastRouteCarGloveboxTimeline.EvaluateOpenness(0.5f, true),
                Is.GreaterThan(0.5f),
                "A released lid drops fast and settles.");
            Assert.That(
                LastRouteCarGloveboxTimeline.EvaluateOpenness(0.5f, false),
                Is.GreaterThan(0.5f),
                "A pushed lid starts easy and ends on the catch.");

            float previous = -1f;
            for (float progress = 0f; progress <= 1.0001f; progress += 0.05f)
            {
                float openness = LastRouteCarGloveboxTimeline.EvaluateOpenness(progress, true);
                Assert.That(openness, Is.GreaterThanOrEqualTo(previous));
                previous = openness;
                Assert.That(
                    LastRouteCarGloveboxTimeline.EvaluateOpenness(
                        LastRouteCarGloveboxTimeline.ProgressForOpenness(openness, true),
                        true),
                    Is.EqualTo(openness).Within(0.0001f),
                    "The inverse lands back on the curve.");
                float closing = LastRouteCarGloveboxTimeline.EvaluateOpenness(progress, false);
                Assert.That(
                    LastRouteCarGloveboxTimeline.EvaluateOpenness(
                        LastRouteCarGloveboxTimeline.ProgressForOpenness(closing, false),
                        false),
                    Is.EqualTo(closing).Within(0.0001f));
            }

            Assert.That(
                LastRouteCarGloveboxTimeline.EvaluateOpenness(float.NaN, true),
                Is.EqualTo(1f),
                "NaN progress lands on the end state rather than the lid.");
            Assert.That(LastRouteCarGloveboxTimeline.SwingSeconds, Is.GreaterThan(0.2f).And.LessThan(0.6f));
        }

        [Test]
        public void DashClicks_AreShortDeterministicAndEndInSilence()
        {
            foreach ((LastRouteCarCueKind kind, float seconds) in new[]
                     {
                         (LastRouteCarCueKind.RadioSwitch,
                             LastRouteCarSoundSynthesis.RadioSwitchClipSeconds),
                         (LastRouteCarCueKind.KnobDetent,
                             LastRouteCarSoundSynthesis.KnobDetentClipSeconds),
                         (LastRouteCarCueKind.GloveboxLatch,
                             LastRouteCarSoundSynthesis.GloveboxLatchClipSeconds)
                     })
            {
                float[] first = LastRouteCarSoundSynthesis.GenerateCue(kind);
                float[] second = LastRouteCarSoundSynthesis.GenerateCue(kind);
                Assert.That(
                    first.Length,
                    Is.EqualTo(Mathf.RoundToInt(
                        LastRouteCarSoundSynthesis.SampleRate * seconds)),
                    $"{kind} length");
                Assert.That(first, Is.EqualTo(second), $"{kind} is deterministic");
                float peak = 0f;
                foreach (float sample in first)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                    Assert.That(Mathf.Abs(sample), Is.LessThanOrEqualTo(0.79f));
                }

                Assert.That(peak, Is.GreaterThan(0.05f), $"{kind} is audible");
                Assert.That(
                    Mathf.Abs(first[first.Length - 1]),
                    Is.LessThan(0.02f),
                    $"{kind} ends in silence, not at a cut");
            }
        }

        [Test]
        public void Dash_ClicksThroughTheCarsOwnVoice()
        {
            var parent = new GameObject("Dash Click Test");
            try
            {
                LastRouteCarAssetRegistry car = LastRouteCarFactory.Create(
                    parent.transform,
                    LastRouteCarPlan.At(Vector3.zero, Vector3.forward));
                Assert.That(car, Is.Not.Null);
                Transform root = car.transform.parent;
                var dashboard = root.GetComponent<LastRouteCarDashboard>();
                var audio = root.GetComponent<LastRouteCarAudio>();
                Assert.That(dashboard, Is.Not.Null);
                Assert.That(audio, Is.Not.Null);
                Assert.That(
                    audio.OwnedSources.Count,
                    Is.EqualTo(LastRouteCarAudio.OwnedSourceCount),
                    "The dash brings no source of its own.");

                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                dashboard.Operate(LastRouteCarDashboardTarget.Glovebox);
                Assert.That(audio.RadioSwitchCueCount, Is.EqualTo(1));
                Assert.That(audio.KnobDetentCueCount, Is.EqualTo(2));
                Assert.That(audio.GloveboxLatchCueCount, Is.EqualTo(1));
                Assert.That(
                    audio.IsEngineWanted,
                    Is.False,
                    "Fiddling with the radio does not start the car.");
            }
            finally
            {
                GameSessionState.BeginNewGame();
                Object.DestroyImmediate(parent);
            }
        }
    }
}
