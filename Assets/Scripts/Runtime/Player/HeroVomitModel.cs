using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The numbers of a bout of vomiting. The bout is one fixed score:
    /// the head and neck go down and the torso doubles over with the
    /// hands on the knees, three bursts come with a breath between them
    /// (one long, two short dregs), each opening with a heave and
    /// convulsing him with every push of the pump while it runs, a wet
    /// cough follows each, and the head comes back up while the right
    /// hand wipes the mouth. Nothing in it is seeded — the same bout
    /// plays every time, and only the residue it leaves on the ground
    /// varies with the seed.
    /// </summary>
    public static class HeroVomitRules
    {
        /// <summary>The head goes down first; the stream follows.</summary>
        public const float OnsetSeconds = 0.4f;

        /// <summary>Length of each burst, indexed by burst.</summary>
        public static readonly float[] BurstSeconds = { 3f, 1f, 1f };

        /// <summary>The breath between bursts. The head stays down through it.</summary>
        public const float PauseSeconds = 2f;

        /// <summary>The first burst is the meal; the two after it are the dregs.</summary>
        public static readonly float[] BurstStrengths = { 1f, 0.55f, 0.55f };

        /// <summary>The stream comes on quicker than it dies.</summary>
        public const float FlowAttackSeconds = 0.12f;
        public const float FlowReleaseSeconds = 0.2f;

        /// <summary>The stomach pumps: the flow dips by the depth between pushes.</summary>
        public const float PulseHertz = 3.2f;
        public const float PulseDepth = 0.45f;

        /// <summary>
        /// The head and neck pitch this far over the ground for the whole
        /// bout. The torso remains folded; a slightly raised chin leaves
        /// room for the initial forward jet instead of aiming at the boots.
        /// </summary>
        public const float HeadDownDegrees = 24f;

        /// <summary>Each burst opens with a heave: this much further down, for this long.</summary>
        public const float HeadDownHeaveExtraDegrees = 12f;
        public const float HeadDownHeaveSeconds = 0.42f;

        /// <summary>While the stream runs the head jerks down with every push of the pump.</summary>
        public const float PumpHeadDegrees = 5f;

        /// <summary>
        /// Doubled over: extra forward pitch of the spine and chest for
        /// the whole bout, more at the heave, and a jerk with every push.
        /// Twenty-two, not sixteen: at sixteen the shoulders stay too high
        /// for the hands to reach the thighs, and the brace read as arms
        /// hanging forward rather than a body propped on its legs.
        /// </summary>
        public const float TorsoPitchDegrees = 22f;
        public const float TorsoHeaveExtraDegrees = 9f;
        public const float PumpTorsoDegrees = 4f;

        /// <summary>The knees give under him: a pelvis drop held through the bout, deeper at the heave.</summary>
        public const float CrouchMetres = 0.05f;
        public const float CrouchHeaveExtraMetres = 0.04f;

        /// <summary>
        /// Once the last burst is over and the head comes up, the right
        /// hand wipes the mouth: up over this long, held, and let fall.
        /// </summary>
        public const float WipeRiseSeconds = 0.4f;
        public const float WipeHoldSeconds = 0.6f;
        public const float WipeFallSeconds = 0.6f;

        /// <summary>The head drops fast and comes back slowly.</summary>
        public const float HeadDownBlendInSeconds = 0.35f;
        public const float HeadDownBlendOutSeconds = 0.8f;

        /// <summary>The retch announces the later bursts this long before they start.</summary>
        public const float PreRetchLeadSeconds = 0.3f;

        /// <summary>Splashes on the ground: the first this long into a burst, then on an interval.</summary>
        public const float GushFirstDelaySeconds = 0.25f;
        public const float GushIntervalSeconds = 0.9f;

        /// <summary>
        /// A whole bout takes this much intoxication off him, split over
        /// the bursts (the user's decision of 2026-09-05: 7 / 7 / 6) so
        /// that a bout cut short by a transition keeps what it earned.
        /// </summary>
        public const int ReliefPointsPerBout = 20;

        /// <summary>The mouth is soiled once the first burst is done.</summary>
        public const int SoilAtBurstIndex = 0;

        /// <summary>The heave is a snap and a slower settle, like a hiccup.</summary>
        private const float HeaveRiseSeconds = 0.08f;

        public static int BurstCount => BurstSeconds.Length;

        /// <summary>Points relieved at the end of a burst: 7, 7, 6 — the remainder goes to the earlier bursts.</summary>
        public static int ReliefForBurst(int burstIndex)
        {
            if (burstIndex < 0 || burstIndex >= BurstCount)
            {
                return 0;
            }

            int share = ReliefPointsPerBout / BurstCount;
            int remainder = ReliefPointsPerBout % BurstCount;
            return share + (burstIndex < remainder ? 1 : 0);
        }

        /// <summary>Seconds from Begin at which a burst starts: 0.4, 5.4, 8.4.</summary>
        public static float BurstStart(int burstIndex)
        {
            float start = OnsetSeconds;
            for (int index = 0; index < burstIndex && index < BurstCount; index++)
            {
                start += BurstSeconds[index] + PauseSeconds;
            }

            return start;
        }

        /// <summary>Seconds from Begin at which a burst ends: 3.4, 6.4, 9.4.</summary>
        public static float BurstEnd(int burstIndex)
        {
            if (burstIndex < 0 || burstIndex >= BurstCount)
            {
                return BurstStart(burstIndex);
            }

            return BurstStart(burstIndex) + BurstSeconds[burstIndex];
        }

        /// <summary>The last burst's end — the Finished cue, and the key comes free.</summary>
        public static float ScheduleEndSeconds => BurstEnd(BurstCount - 1);

        /// <summary>The whole wipe, from the last burst's end.</summary>
        public static float WipeSeconds => WipeRiseSeconds + WipeHoldSeconds + WipeFallSeconds;

        /// <summary>The head is back up and the mouth is wiped; the bout is over.</summary>
        public static float TotalSeconds =>
            ScheduleEndSeconds + Mathf.Max(HeadDownBlendOutSeconds, WipeSeconds);

        /// <summary>The shape of one heave over its whole duration, 0..1.</summary>
        public static float HeaveShape(float secondsIntoBurst)
        {
            if (float.IsNaN(secondsIntoBurst) || secondsIntoBurst <= 0f)
            {
                return 0f;
            }

            if (secondsIntoBurst < HeaveRiseSeconds)
            {
                return Mathf.SmoothStep(0f, 1f, secondsIntoBurst / HeaveRiseSeconds);
            }

            float back = (secondsIntoBurst - HeaveRiseSeconds) /
                         (HeadDownHeaveSeconds - HeaveRiseSeconds);
            return back >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, back);
        }
    }

    /// <summary>What the bout wants done at an instant.</summary>
    public enum HeroVomitCueKind
    {
        /// <summary>The retching sound; the first one opens the bout.</summary>
        Retch,

        /// <summary>The stream starts at this strength.</summary>
        BurstBegin,

        /// <summary>A splash lands: the gush sound.</summary>
        Gush,

        /// <summary>The stream stops.</summary>
        BurstEnd,

        /// <summary>The wet cough and spit that follows a burst.</summary>
        Cough,

        /// <summary>Take these points of intoxication off him.</summary>
        Relief,

        /// <summary>The mouth is soiled from here on.</summary>
        Soil,

        /// <summary>The last burst is over; release the key. The head is still coming up.</summary>
        Finished
    }

    /// <summary>One instant of the score, as handed to the controller.</summary>
    public readonly struct HeroVomitCue
    {
        public HeroVomitCue(
            HeroVomitCueKind kind,
            int burstIndex,
            float strength,
            int points,
            float atSeconds)
        {
            Kind = kind;
            BurstIndex = burstIndex;
            Strength = strength;
            Points = points;
            AtSeconds = atSeconds;
        }

        public HeroVomitCueKind Kind { get; }

        /// <summary>The burst this cue belongs to; -1 for Finished.</summary>
        public int BurstIndex { get; }

        /// <summary>The burst's strength, for BurstBegin and its neighbours.</summary>
        public float Strength { get; }

        /// <summary>Points of intoxication, for Relief.</summary>
        public int Points { get; }

        /// <summary>Seconds from Begin at which the cue is due.</summary>
        public float AtSeconds { get; }
    }

    /// <summary>
    /// A bout of vomiting, pure and unseeded: a clock over a fixed score.
    /// The clock runs on SCALED time — the stream's arc, the particles,
    /// the sounds and the ragdoll's physics all live on
    /// <c>Time.deltaTime</c>, and a pause (timeScale 0) must freeze the
    /// whole thing together rather than let the score run on under a
    /// frozen stream. The head stays down through the pauses between
    /// bursts because a man between heaves does not look up; only the
    /// last burst's end lets it rise, and the bout counts as active until
    /// it is back up. Cues are queued as the clock crosses their instants
    /// and drained in order by <see cref="TryConsumeCue"/>, so a dropped
    /// frame delivers everything it crossed instead of skipping a relief
    /// or a stop.
    /// </summary>
    public sealed class HeroVomitModel
    {
        private static readonly HeroVomitCue[] Schedule = BuildSchedule();
        private static readonly float[] BurstStarts = BuildEdges(false);
        private static readonly float[] BurstEnds = BuildEdges(true);
        private static readonly float ScheduleEnd = HeroVomitRules.ScheduleEndSeconds;
        private static readonly float Total = HeroVomitRules.TotalSeconds;

        private readonly Queue<HeroVomitCue> pending = new Queue<HeroVomitCue>(8);
        private float time;
        private bool active;
        private int nextCue;

        public bool IsActive => active;

        /// <summary>Seconds of scaled time since Begin; frozen once the bout is over.</summary>
        public float Time => time;

        /// <summary>Points handed out as Relief cues so far in this bout.</summary>
        public int ReliefGranted { get; private set; }

        /// <summary>The burst under way, or -1 in the onset, the pauses and the recovery.</summary>
        public int BurstIndex => active ? ResolveBurstIndex(time) : -1;

        public bool IsVomiting => BurstIndex >= 0;

        public float BurstStrength
        {
            get
            {
                int index = BurstIndex;
                return index >= 0 ? HeroVomitRules.BurstStrengths[index] : 0f;
            }
        }

        public float Flow => active ? EvaluateFlow(time) : 0f;

        public float HeadDownDegrees => active ? EvaluateHeadDown(time) : 0f;

        public float Spasm => active ? EvaluateHeave(time) : 0f;

        /// <summary>Where the stomach's pump is in its beat, 0..1, scaled by the burst; zero between bursts.</summary>
        public float Pump => active ? EvaluatePump(time) : 0f;

        public float TorsoPitchDegrees => active ? EvaluateTorsoPitch(time) : 0f;

        public float CrouchMetres => active ? EvaluateCrouch(time) : 0f;

        /// <summary>The hands on the knees: with the head, down first and up last.</summary>
        public float BraceWeight => active ? EvaluateHeld(time) : 0f;

        public float WipeWeight => active ? EvaluateWipe(time) : 0f;

        public PlayerVomitPose Pose =>
            new PlayerVomitPose(
                active,
                HeadDownDegrees,
                Flow,
                Spasm,
                TorsoPitchDegrees,
                CrouchMetres,
                BraceWeight,
                WipeWeight,
                Pump);

        /// <summary>
        /// Start the bout from the top. A bout already running starts
        /// over: its unconsumed cues are dropped, because they belong to
        /// a score that is no longer playing.
        /// </summary>
        public void Begin()
        {
            time = 0f;
            active = true;
            nextCue = 0;
            ReliefGranted = 0;
            pending.Clear();
            EmitDue();
        }

        /// <summary>One frame of scaled time. Nothing moves when the bout is not running.</summary>
        public void Advance(float deltaTime)
        {
            if (!active)
            {
                return;
            }

            float step = float.IsNaN(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
            if (step <= 0f)
            {
                return;
            }

            time += step;
            EmitDue();
            if (time >= Total)
            {
                active = false;
            }
        }

        /// <summary>True once per cue, in schedule order — the controller drains it every frame after Advance.</summary>
        public bool TryConsumeCue(out HeroVomitCue cue)
        {
            if (pending.Count == 0)
            {
                cue = default;
                return false;
            }

            cue = pending.Dequeue();
            return true;
        }

        /// <summary>
        /// Stop at once: the pose is None from here and no further cue
        /// is queued. Cues already queued stay drainable, and a Relief
        /// already handed out stays granted — the bout keeps what it
        /// earned before it was cut short.
        /// </summary>
        public void Cancel()
        {
            active = false;
        }

        /// <summary>Everything back to the start, queue and counters included, for a shutdown.</summary>
        public void Reset()
        {
            Cancel();
            time = 0f;
            nextCue = 0;
            ReliefGranted = 0;
            pending.Clear();
        }

        private void EmitDue()
        {
            while (nextCue < Schedule.Length && Schedule[nextCue].AtSeconds <= time)
            {
                HeroVomitCue cue = Schedule[nextCue++];
                if (cue.Kind == HeroVomitCueKind.Relief)
                {
                    ReliefGranted += cue.Points;
                }

                pending.Enqueue(cue);
            }
        }

        private static int ResolveBurstIndex(float atSeconds)
        {
            for (int index = 0; index < BurstStarts.Length; index++)
            {
                if (atSeconds >= BurstStarts[index] && atSeconds < BurstEnds[index])
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Strength times an attack/release envelope times the pump: the
        /// pulse only ever takes flow away, so a burst never exceeds its
        /// strength and the first one peaks at exactly one.
        /// </summary>
        private static float EvaluateFlow(float atSeconds)
        {
            int index = ResolveBurstIndex(atSeconds);
            if (index < 0)
            {
                return 0f;
            }

            float into = atSeconds - BurstStarts[index];
            float left = BurstEnds[index] - atSeconds;
            float envelope =
                Mathf.Clamp01(into / HeroVomitRules.FlowAttackSeconds) *
                Mathf.Clamp01(left / HeroVomitRules.FlowReleaseSeconds);
            float pulse =
                1f - HeroVomitRules.PulseDepth +
                HeroVomitRules.PulseDepth *
                Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * HeroVomitRules.PulseHertz * into));
            return HeroVomitRules.BurstStrengths[index] * envelope * pulse;
        }

        private static float EvaluateHeave(float atSeconds)
        {
            for (int index = 0; index < BurstStarts.Length; index++)
            {
                float into = atSeconds - BurstStarts[index];
                if (into >= 0f && into < HeroVomitRules.HeadDownHeaveSeconds)
                {
                    return HeroVomitRules.HeaveShape(into);
                }
            }

            return 0f;
        }

        /// <summary>
        /// The bout's hold: down fast at the start, one through every
        /// burst and pause, and up slowly after the last burst. The head,
        /// the torso, the knees and the brace all ride this one curve.
        /// </summary>
        private static float EvaluateHeld(float atSeconds)
        {
            if (atSeconds < HeroVomitRules.HeadDownBlendInSeconds)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    atSeconds / HeroVomitRules.HeadDownBlendInSeconds);
            }

            if (atSeconds < ScheduleEnd)
            {
                return 1f;
            }

            return 1f - Mathf.SmoothStep(
                0f,
                1f,
                (atSeconds - ScheduleEnd) /
                HeroVomitRules.HeadDownBlendOutSeconds);
        }

        /// <summary>
        /// The pump's beat while a burst runs: the same half-sine the
        /// flow throbs to, scaled by the burst's strength so the dregs
        /// convulse him less than the meal did. Zero between bursts.
        /// </summary>
        private static float EvaluatePump(float atSeconds)
        {
            int index = ResolveBurstIndex(atSeconds);
            if (index < 0)
            {
                return 0f;
            }

            float into = atSeconds - BurstStarts[index];
            return HeroVomitRules.BurstStrengths[index] *
                   Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * HeroVomitRules.PulseHertz * into));
        }

        private static float EvaluateHeadDown(float atSeconds)
        {
            return HeroVomitRules.HeadDownDegrees * EvaluateHeld(atSeconds) +
                   HeroVomitRules.HeadDownHeaveExtraDegrees * EvaluateHeave(atSeconds) +
                   HeroVomitRules.PumpHeadDegrees * EvaluatePump(atSeconds);
        }

        private static float EvaluateTorsoPitch(float atSeconds)
        {
            return HeroVomitRules.TorsoPitchDegrees * EvaluateHeld(atSeconds) +
                   HeroVomitRules.TorsoHeaveExtraDegrees * EvaluateHeave(atSeconds) +
                   HeroVomitRules.PumpTorsoDegrees * EvaluatePump(atSeconds);
        }

        private static float EvaluateCrouch(float atSeconds)
        {
            return HeroVomitRules.CrouchMetres * EvaluateHeld(atSeconds) +
                   HeroVomitRules.CrouchHeaveExtraMetres * EvaluateHeave(atSeconds);
        }

        /// <summary>
        /// The wipe: nothing until the last burst is over, then the hand
        /// rises to the mouth as the head comes up, holds there and
        /// falls. It ends exactly at the bout's total.
        /// </summary>
        private static float EvaluateWipe(float atSeconds)
        {
            float into = atSeconds - ScheduleEnd;
            if (into <= 0f)
            {
                return 0f;
            }

            if (into < HeroVomitRules.WipeRiseSeconds)
            {
                return Mathf.SmoothStep(0f, 1f, into / HeroVomitRules.WipeRiseSeconds);
            }

            into -= HeroVomitRules.WipeRiseSeconds;
            if (into < HeroVomitRules.WipeHoldSeconds)
            {
                return 1f;
            }

            into -= HeroVomitRules.WipeHoldSeconds;
            if (into >= HeroVomitRules.WipeFallSeconds)
            {
                return 0f;
            }

            return 1f - Mathf.SmoothStep(0f, 1f, into / HeroVomitRules.WipeFallSeconds);
        }

        private static float[] BuildEdges(bool ends)
        {
            var edges = new float[HeroVomitRules.BurstCount];
            for (int index = 0; index < edges.Length; index++)
            {
                edges[index] = ends
                    ? HeroVomitRules.BurstEnd(index)
                    : HeroVomitRules.BurstStart(index);
            }

            return edges;
        }

        /// <summary>
        /// The score, in time order by construction: the retch lead is
        /// shorter than the pause, so every burst's retch falls after the
        /// previous burst's end. Cues sharing an instant keep the order
        /// the controller needs — the stream stops before the relief is
        /// granted, the relief before the mouth is marked, and Finished
        /// last of all.
        /// </summary>
        private static HeroVomitCue[] BuildSchedule()
        {
            var cues = new List<HeroVomitCue>(24);
            for (int index = 0; index < HeroVomitRules.BurstCount; index++)
            {
                float start = HeroVomitRules.BurstStart(index);
                float end = HeroVomitRules.BurstEnd(index);
                float strength = HeroVomitRules.BurstStrengths[index];

                // The first retch IS the onset — it opens the bout at
                // once; the later ones run ahead of their burst.
                float retchAt = index == 0 ? 0f : start - HeroVomitRules.PreRetchLeadSeconds;
                cues.Add(new HeroVomitCue(HeroVomitCueKind.Retch, index, strength, 0, retchAt));
                cues.Add(new HeroVomitCue(HeroVomitCueKind.BurstBegin, index, strength, 0, start));

                // A gush lands only while the stream is still at full
                // strength: one that fell in the release tail would be a
                // splash with nothing behind it.
                float releaseAt = end - HeroVomitRules.FlowReleaseSeconds;
                for (int gush = 0; ; gush++)
                {
                    float gushAt = start +
                                   HeroVomitRules.GushFirstDelaySeconds +
                                   gush * HeroVomitRules.GushIntervalSeconds;
                    if (gushAt >= releaseAt)
                    {
                        break;
                    }

                    cues.Add(new HeroVomitCue(HeroVomitCueKind.Gush, index, strength, 0, gushAt));
                }

                cues.Add(new HeroVomitCue(HeroVomitCueKind.BurstEnd, index, strength, 0, end));
                // The cough and spit that follow every burst, on the
                // same instant the stream stops and before the relief.
                cues.Add(new HeroVomitCue(HeroVomitCueKind.Cough, index, strength, 0, end));
                cues.Add(new HeroVomitCue(
                    HeroVomitCueKind.Relief,
                    index,
                    strength,
                    HeroVomitRules.ReliefForBurst(index),
                    end));
                if (index == HeroVomitRules.SoilAtBurstIndex)
                {
                    cues.Add(new HeroVomitCue(HeroVomitCueKind.Soil, index, strength, 0, end));
                }
            }

            cues.Add(new HeroVomitCue(
                HeroVomitCueKind.Finished,
                -1,
                0f,
                0,
                HeroVomitRules.ScheduleEndSeconds));
            return cues.ToArray();
        }
    }
}
