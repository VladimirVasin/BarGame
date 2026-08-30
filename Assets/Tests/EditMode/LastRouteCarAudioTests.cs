using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The Ferryman's car, heard - the pure half. The engine model, the
    /// gain law and the synthesis are all asserted without a scene, and
    /// the one scene test here builds the real car through the production
    /// factory so the five voices are proved to hang off the mechanisms
    /// the game actually raises.
    /// </summary>
    public sealed class LastRouteCarAudioTests
    {
        private const float Step = 1f / 60f;

        [Test]
        public void Engine_TurnsOverThenCatchesThenSettlesToIdle()
        {
            var engine = new LastRouteCarEngineModel();
            Assert.That(engine.Phase, Is.EqualTo(LastRouteCarEnginePhase.Off));
            Assert.That(engine.IsAudible, Is.False);

            engine.Start(false);
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Starting));
            Assert.That(engine.IsAudible, Is.True);

            float elapsed = Run(engine, 0f, 0.5f);
            Assert.That(
                engine.Rpm01,
                Is.LessThan(LastRouteCarEngineModel.IdleRpm01),
                "The starter turns it over well under idle.");
            Assert.That(engine.Rpm01, Is.GreaterThan(0f));

            elapsed = Run(
                engine,
                elapsed,
                LastRouteCarEngineModel.StarterSeconds + 0.1f);
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Starting),
                "It has caught but not settled.");
            Assert.That(
                engine.Rpm01,
                Is.GreaterThan(LastRouteCarEngineModel.IdleRpm01),
                "The catch flares over idle.");

            Run(engine, elapsed, 2.5f);
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Running));
            Assert.That(
                engine.Rpm01,
                Is.EqualTo(LastRouteCarEngineModel.IdleRpm01).Within(0.02f),
                "And settles to idle.");
            Assert.That(engine.ShiftCount, Is.Zero);
            Assert.That(engine.Gear, Is.Zero);
        }

        [Test]
        public void Engine_ComesOutOfTheTunnelAlreadyRunning()
        {
            var engine = new LastRouteCarEngineModel();
            engine.Start(true);
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Running),
                "No starter: it has been running since the island.");
            Assert.That(
                engine.Rpm01,
                Is.EqualTo(LastRouteCarEngineModel.IdleRpm01).Within(0.0001f));
            Assert.That(
                engine.Load01,
                Is.EqualTo(LastRouteCarEngineModel.RollingLoad01)
                    .Within(0.0001f));

            // Starting a running engine is a no-op, not a restart.
            engine.Advance(1f, 5f, 0f, 0f);
            float rpm = engine.Rpm01;
            engine.Start(false);
            Assert.That(engine.Phase, Is.EqualTo(LastRouteCarEnginePhase.Running));
            Assert.That(engine.Rpm01, Is.EqualTo(rpm));
        }

        [Test]
        public void Engine_ClimbsTheBoxOnAStraightAndDropsAGearForTheHairpin()
        {
            var engine = new LastRouteCarEngineModel();
            engine.Start(true);

            float speed = 0f;
            int shiftsSeen = 0;
            float rpmBeforeShift = 0f;
            float dipAfterShift = 1f;
            bool measuringDip = false;
            float dipWindow = 0f;
            while (speed < 8.5f)
            {
                float previousRpm = engine.Rpm01;
                speed = Mathf.Min(8.5f, speed + (1.5f * Step));
                engine.Advance(Step, speed, 1.5f, 0f);
                Assert.That(engine.Rpm01, Is.InRange(0f, 1f));
                if (engine.ShiftCount > shiftsSeen)
                {
                    shiftsSeen = engine.ShiftCount;
                    if (shiftsSeen == 1)
                    {
                        rpmBeforeShift = previousRpm;
                        measuringDip = true;
                        dipWindow = LastRouteCarEngineModel.ShiftSeconds;
                    }
                }

                if (measuringDip)
                {
                    dipAfterShift = Mathf.Min(dipAfterShift, engine.Rpm01);
                    dipWindow -= Step;
                    measuringDip = dipWindow > 0f;
                }
            }

            Run(engine, 0f, 2f, 8.5f);
            Assert.That(
                engine.Gear,
                Is.EqualTo(LastRouteCarEngineModel.GearTopSpeeds.Length - 1),
                "Top gear at cruise.");
            Assert.That(engine.ShiftCount, Is.EqualTo(2), "First to second to third.");
            Assert.That(
                engine.Rpm01,
                Is.EqualTo(LastRouteCarEngineModel.EvaluateGearRpm01(8.5f, 2))
                    .Within(0.03f));
            Assert.That(
                dipAfterShift,
                Is.LessThan(rpmBeforeShift - 0.1f),
                "A change is heard as a dip: the clutch goes in and the " +
                "revs fall before they climb again.");

            // The hairpin: braked to 3 m/s and held there.
            while (speed > 3f)
            {
                speed = Mathf.Max(3f, speed - (2.2f * Step));
                engine.Advance(Step, speed, -2.2f, 0f);
            }

            Run(engine, 0f, 2f, 3f);
            Assert.That(engine.Gear, Is.EqualTo(1), "Down into second for the bend.");
            Assert.That(engine.ShiftCount, Is.EqualTo(3));

            // And out of it.
            while (speed < 6f)
            {
                speed = Mathf.Min(6f, speed + (1.5f * Step));
                engine.Advance(Step, speed, 1.5f, 0.08f);
            }

            Run(engine, 0f, 2f, 6f, 0f, 0.08f);
            Assert.That(engine.Gear, Is.EqualTo(2), "Back up to third on the straight.");
            Assert.That(engine.ShiftCount, Is.EqualTo(4));

            // Standing still drops the box into neutral.
            Run(engine, 0f, 1f, 0f);
            Assert.That(engine.Gear, Is.Zero);
            Assert.That(
                engine.Rpm01,
                Is.EqualTo(LastRouteCarEngineModel.IdleRpm01).Within(0.02f));
        }

        [Test]
        public void Engine_WorksHarderOnTheGradeThanOnTheFlat()
        {
            var flat = new LastRouteCarEngineModel();
            var climb = new LastRouteCarEngineModel();
            var overrun = new LastRouteCarEngineModel();
            flat.Start(true);
            climb.Start(true);
            overrun.Start(true);
            Run(flat, 0f, 3f, 5f);
            Run(climb, 0f, 3f, 5f, 0f, 0.08f);
            Run(overrun, 0f, 3f, 5f, -1f);

            Assert.That(
                flat.Load01,
                Is.EqualTo(LastRouteCarEngineModel.RollingLoad01).Within(0.02f),
                "Steady on the flat is only what it costs to roll.");
            Assert.That(
                climb.Load01,
                Is.GreaterThan(flat.Load01 + 0.3f),
                "The mountain's eight per cent is most of the throttle.");
            Assert.That(
                overrun.Load01,
                Is.LessThan(0.02f),
                "Braking is overrun: throttle shut, no load at all.");
            Assert.That(
                climb.Rpm01,
                Is.EqualTo(flat.Rpm01).Within(0.0001f),
                "The grade changes the load, never the revs a speed asks for.");
        }

        [Test]
        public void Engine_ShutsOffTheKeyAndDies()
        {
            var engine = new LastRouteCarEngineModel();
            engine.Stop();
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Off),
                "Key off on a cold engine does nothing.");

            engine.Start(true);
            engine.Stop();
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Stopping));
            Assert.That(engine.IsAudible, Is.True, "Dying is still heard.");

            Run(engine, 0f, 0.5f);
            Assert.That(
                engine.Phase,
                Is.EqualTo(LastRouteCarEnginePhase.Stopping));
            Assert.That(engine.Rpm01, Is.GreaterThan(0f));
            Assert.That(
                engine.Rpm01,
                Is.LessThan(LastRouteCarEngineModel.IdleRpm01));

            Run(engine, 0.5f, LastRouteCarEngineModel.ShutdownSeconds + 0.2f);
            Assert.That(engine.Phase, Is.EqualTo(LastRouteCarEnginePhase.Off));
            Assert.That(engine.IsAudible, Is.False);
            Assert.That(engine.Rpm01, Is.Zero);
        }

        [Test]
        public void Mix_RevsRaisePitchAndVolumeAndLoadOpensTheTone()
        {
            Assert.That(
                LastRouteCarAudioMix.EvaluateEnginePitch(0f),
                Is.EqualTo(LastRouteCarAudioMix.EnginePitchAtZero));
            Assert.That(
                LastRouteCarAudioMix.EvaluateEnginePitch(1f),
                Is.EqualTo(LastRouteCarAudioMix.EnginePitchAtFull));
            Assert.That(
                LastRouteCarAudioMix.EvaluateEnginePitch(0.5f),
                Is.GreaterThan(LastRouteCarAudioMix.EvaluateEnginePitch(0.2f)));
            Assert.That(
                LastRouteCarAudioMix.EvaluateEngineVolume(1f, 1f),
                Is.EqualTo(LastRouteCarAudioMix.EngineFullVolume).Within(0.0001f));
            Assert.That(
                LastRouteCarAudioMix.EvaluateEngineVolume(1f, 0f),
                Is.LessThan(LastRouteCarAudioMix.EvaluateEngineVolume(1f, 1f)),
                "Overrun is the same revs at a fraction of the voice.");
            Assert.That(
                LastRouteCarAudioMix.EvaluateEngineCutoff(1f),
                Is.GreaterThan(LastRouteCarAudioMix.EvaluateEngineCutoff(0f)),
                "An engine under load opens up.");
            Assert.That(
                LastRouteCarAudioMix.EvaluateCabinVolume(1f, 1f),
                Is.GreaterThan(LastRouteCarAudioMix.EvaluateCabinVolume(0f, 0f)));
            Assert.That(
                LastRouteCarAudioMix.EngineMaximumDistance,
                Is.EqualTo(RuntimeSceneSetup.CityFarClipPlane),
                "The exterior tail ends where the city's visible slice does.");
        }

        [Test]
        public void Mix_SnowTakesHalfTheRoadNoiseAway()
        {
            float wet = LastRouteCarAudioMix.EvaluateTyreVolume(
                8f,
                LastRouteCarRoadSurface.WetAsphalt);
            float snow = LastRouteCarAudioMix.EvaluateTyreVolume(
                8f,
                LastRouteCarRoadSurface.PackedSnow);
            Assert.That(wet, Is.EqualTo(LastRouteCarAudioMix.TyreFullVolume).Within(0.0001f));
            Assert.That(
                snow,
                Is.EqualTo(wet * LastRouteCarAudioMix.PackedSnowGain).Within(0.0001f));
            Assert.That(
                LastRouteCarAudioMix.EvaluateTyreCutoff(LastRouteCarRoadSurface.PackedSnow),
                Is.LessThan(
                    LastRouteCarAudioMix.EvaluateTyreCutoff(
                        LastRouteCarRoadSurface.WetAsphalt)),
                "Packed snow is duller than a wet road.");
            Assert.That(
                LastRouteCarAudioMix.EvaluateTyreVolume(0f, LastRouteCarRoadSurface.WetAsphalt),
                Is.Zero,
                "Standing still, the tyres say nothing.");
            Assert.That(
                LastRouteCarAudioMix.EvaluateTyreVolume(float.NaN, LastRouteCarRoadSurface.WetAsphalt),
                Is.Zero);
            Assert.That(
                LastRouteCarAudioMix.EvaluateDeckVolume(3f),
                Is.EqualTo(LastRouteCarAudioMix.DeckFullVolume * 0.5f).Within(0.0001f));
        }

        [Test]
        public void Synthesis_LoopsAreDeterministicBoundedAndSeamless()
        {
            int expectedLength = Mathf.RoundToInt(
                LastRouteCarSoundSynthesis.SampleRate *
                LastRouteCarSoundSynthesis.LoopDuration);
            foreach (LastRouteCarLoopKind kind in
                     System.Enum.GetValues(typeof(LastRouteCarLoopKind)))
            {
                float[] first = LastRouteCarSoundSynthesis.GenerateLoop(kind);
                float[] second = LastRouteCarSoundSynthesis.GenerateLoop(kind);
                Assert.That(first.Length, Is.EqualTo(expectedLength), kind.ToString());
                Assert.That(first, Is.EqualTo(second), $"{kind} must be deterministic.");

                float peak = 0f;
                double sum = 0d;
                double squares = 0d;
                for (int index = 0; index < first.Length; index++)
                {
                    float sample = first[index];
                    Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                    sum += sample;
                    squares += sample * sample;
                }

                float rms = (float)System.Math.Sqrt(squares / first.Length);
                Assert.That(peak, Is.LessThanOrEqualTo(0.7801f), $"{kind} clips.");
                Assert.That(rms, Is.GreaterThan(0.02f), $"{kind} is silent.");
                Assert.That(
                    System.Math.Abs(sum / first.Length),
                    Is.LessThan(0.05d),
                    $"{kind} carries a DC offset.");
                Assert.That(
                    Mathf.Abs(first[0] - first[first.Length - 1]),
                    Is.LessThan(0.3f),
                    $"{kind} has a step at the seam.");
            }

            // The engine's fundamental divides the loop exactly, which is
            // what keeps the tonal part phase-continuous at the seam.
            float cycles = LastRouteCarSoundSynthesis.EngineFundamentalHz *
                           LastRouteCarSoundSynthesis.LoopDuration;
            Assert.That(cycles, Is.EqualTo(Mathf.Round(cycles)).Within(0.0001f));
        }

        [Test]
        public void Synthesis_CuesEndInSilence()
        {
            foreach (LastRouteCarCueKind kind in
                     System.Enum.GetValues(typeof(LastRouteCarCueKind)))
            {
                float[] samples = LastRouteCarSoundSynthesis.GenerateCue(kind);
                Assert.That(samples.Length, Is.GreaterThan(1000), kind.ToString());
                float peak = 0f;
                for (int index = 0; index < samples.Length; index++)
                {
                    Assert.That(
                        float.IsNaN(samples[index]) || float.IsInfinity(samples[index]),
                        Is.False);
                    peak = Mathf.Max(peak, Mathf.Abs(samples[index]));
                }

                Assert.That(peak, Is.GreaterThan(0.1f), $"{kind} is inaudible.");
                Assert.That(peak, Is.LessThanOrEqualTo(0.7801f), $"{kind} clips.");
                Assert.That(
                    Mathf.Abs(samples[samples.Length - 1]),
                    Is.LessThan(0.02f),
                    $"{kind} ends at a cut rather than in silence.");
            }

            float[] starter = LastRouteCarSoundSynthesis.GenerateCue(
                LastRouteCarCueKind.Starter);
            Assert.That(
                starter.Length,
                Is.EqualTo(
                    Mathf.RoundToInt(
                        LastRouteCarSoundSynthesis.SampleRate *
                        LastRouteCarSoundSynthesis.StarterClipSeconds)));
            Assert.That(
                LastRouteCarSoundSynthesis.StarterClipSeconds,
                Is.GreaterThan(LastRouteCarEngineModel.StarterSeconds),
                "The clip must still be turning it over when the model " +
                "says it catches.");
        }

        [Test]
        public void Car_HangsFiveVoicesOffItsOwnMechanisms()
        {
            var parent = new GameObject("Car Audio Test");
            try
            {
                LastRouteCarAssetRegistry car = LastRouteCarFactory.Create(
                    parent.transform,
                    LastRouteCarPlan.At(Vector3.zero, Vector3.forward));
                Assert.That(car, Is.Not.Null, "The car failed to spawn.");
                Transform root = car.transform.parent;
                var audio = root.GetComponent<LastRouteCarAudio>();
                Assert.That(audio, Is.Not.Null, "The car has no voice.");
                Assert.That(audio.IsInitialized, Is.True);
                Assert.That(
                    audio.OwnedSources.Count,
                    Is.EqualTo(LastRouteCarAudio.OwnedSourceCount));
                Assert.That(
                    root.GetComponentsInChildren<AudioSource>(true).Length,
                    Is.EqualTo(LastRouteCarAudio.OwnedSourceCount),
                    "Every voice on the car is one of the five - the dash " +
                    "clicks through the cue source and brings no source of " +
                    "its own.");
                Assert.That(
                    audio.IsEngineWanted,
                    Is.False,
                    "Parked, with nobody at the wheel.");
                Assert.That(audio.Engine.IsAudible, Is.False);

                AudioSource engine = audio.EngineSource;
                Assert.That(engine.clip, Is.Not.Null);
                Assert.That(engine.loop, Is.True);
                Assert.That(engine.spatialBlend, Is.EqualTo(1f));
                Assert.That(engine.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
                Assert.That(
                    engine.minDistance,
                    Is.EqualTo(LastRouteCarAudioMix.EngineMinimumDistance));
                Assert.That(
                    engine.maxDistance,
                    Is.EqualTo(LastRouteCarAudioMix.EngineMaximumDistance));
                Assert.That(engine.priority, Is.EqualTo(LastRouteCarAudioMix.EnginePriority));
                Assert.That(engine.volume, Is.Zero, "Silent until the key turns.");
                Assert.That(engine.playOnAwake, Is.False);
                Assert.That(
                    engine.outputAudioMixerGroup,
                    Is.SameAs(GameAudioMixer.SfxWorldGroup));
                Assert.That(
                    audio.CabinSource.spatialBlend,
                    Is.Zero,
                    "The cabin is heard from inside, not from a point.");
                Assert.That(
                    audio.CabinSource.clip,
                    Is.Not.SameAs(engine.clip));
                Assert.That(audio.TyreSource.clip, Is.Not.SameAs(engine.clip));
                Assert.That(audio.EngineTone, Is.Not.Null);
                Assert.That(audio.EngineRoom, Is.Not.Null);
                Assert.That(
                    audio.EngineRoom.reverbLevel,
                    Is.EqualTo(-10000f).Within(0.5f),
                    "Under open sky the tunnel is not heard.");

                // The engine bay is at the front, under the bonnet the
                // Ferryman sits on; the tyres are the rear axle.
                Vector3 forward = root.forward;
                float perchSide = Vector3.Dot(
                    car.PerchSolesAnchor.position - root.position,
                    forward);
                Assert.That(perchSide, Is.GreaterThan(0f), "The perch is the bonnet.");
                Assert.That(
                    engine.transform.localPosition.z,
                    Is.GreaterThan(0f),
                    "The engine is under the bonnet.");
                Assert.That(
                    audio.TyreSource.transform.localPosition.z,
                    Is.LessThan(0f),
                    "The tyres are the rear axle.");
                Assert.That(
                    audio.DeckSource.transform,
                    Is.SameAs(audio.TyreSource.transform),
                    "The deck drums under the same axle.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>Advances the model in pinned frames from one elapsed
        /// time to another, returning the new elapsed time.</summary>
        private static float Run(
            LastRouteCarEngineModel engine,
            float from,
            float to,
            float speed = 0f,
            float acceleration = 0f,
            float grade = 0f)
        {
            float elapsed = from;
            while (elapsed < to)
            {
                engine.Advance(Step, speed, acceleration, grade);
                elapsed += Step;
            }

            return elapsed;
        }
    }
}
