using UnityEngine;

namespace BarPromenade
{
    /// <summary>The whole passenger journey, across both scene-local controllers.</summary>
    public sealed class LastRouteRideSpeechState
    {
        public const int LineCount = 10;
        public const int MaximumLinesPerTrip = 5;
        public const float FirstMinimumSeconds = 12f;
        public const float FirstMaximumSeconds = 18f;
        public const float SilenceMinimumSeconds = 30f;
        public const float SilenceMaximumSeconds = 45f;

        private readonly int[] bag = new int[LineCount];
        private int cursor = LineCount;
        private int lastLine = -1;
        private uint random;

        public LastRouteRideSpeechState(uint seed)
        {
            random = seed == 0 ? 0x68E31DA4u : seed;
        }

        public bool IsTripActive { get; private set; }
        public bool IsLineActive { get; private set; }
        public int LinesThisTrip { get; private set; }
        public float SilenceRemaining { get; private set; }
        public float RadioReactionRemaining { get; private set; } = -1f;
        public bool HasPendingRadioReaction => RadioReactionRemaining >= 0f;
        public int DislikedRadioStationIndex { get; private set; } = -1;
        public bool HasReactedToRadioThisTrip { get; private set; }
        public int LastLineIndex => lastLine;

        public static string LineKey(int index) =>
            "lastroute.ride.quip." + (index + 1).ToString("00");

        public void BeginTrip()
        {
            IsTripActive = true;
            IsLineActive = false;
            LinesThisTrip = 0;
            DislikedRadioStationIndex = (int)(Next() % LastRouteCarRadioModel.DetentCount);
            HasReactedToRadioThisTrip = false;
            CancelRadioReaction();
            SilenceRemaining = Range(FirstMinimumSeconds, FirstMaximumSeconds);
        }

        public void EnsureTrip()
        {
            if (!IsTripActive)
                BeginTrip();
        }

        public void EndTrip()
        {
            IsTripActive = false;
            IsLineActive = false;
        }

        /// <summary>Only visible, unpaused driving consumes the silence. No backlog.</summary>
        public int AdvanceSilence(float seconds)
        {
            if (!IsTripActive || IsLineActive || LinesThisTrip >= MaximumLinesPerTrip ||
                seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return -1;

            SilenceRemaining = Mathf.Max(0f, SilenceRemaining - seconds);
            if (SilenceRemaining > 0f)
                return -1;

            if (cursor >= bag.Length)
                Shuffle();
            lastLine = bag[cursor++];
            LinesThisTrip++;
            IsLineActive = true;
            return lastLine;
        }

        /// <summary>Reading, or a cut at the tunnel, must finish before silence starts.</summary>
        public void FinishLine()
        {
            if (!IsLineActive)
                return;
            IsLineActive = false;
            SilenceRemaining = Range(SilenceMinimumSeconds, SilenceMaximumSeconds);
        }

        /// <summary>A cabin remark gets silence without consuming the road bag.</summary>
        public void FinishCabinRemark()
        {
            IsLineActive = false;
            SilenceRemaining = Range(SilenceMinimumSeconds, SilenceMaximumSeconds);
        }

        /// <summary>The pending complaint belongs to the session, not either scene.</summary>
        public void ArmRadioReaction(float delaySeconds)
        {
            if (HasReactedToRadioThisTrip || DislikedRadioStationIndex < 0) return;
            RadioReactionRemaining = Mathf.Max(0f, delaySeconds);
        }

        public void CancelRadioReaction() => RadioReactionRemaining = -1f;

        public bool AdvanceRadioReaction(float seconds)
        {
            if (!HasPendingRadioReaction || seconds <= 0f ||
                float.IsNaN(seconds) || float.IsInfinity(seconds))
                return false;
            RadioReactionRemaining -= seconds;
            if (RadioReactionRemaining > 0f)
                return false;
            CancelRadioReaction();
            HasReactedToRadioThisTrip = true;
            return true;
        }

        private void Shuffle()
        {
            for (int i = 0; i < bag.Length; i++)
                bag[i] = i;
            for (int i = bag.Length - 1; i > 0; i--)
            {
                int other = (int)(Next() % (uint)(i + 1));
                (bag[i], bag[other]) = (bag[other], bag[i]);
            }
            if (bag[0] == lastLine)
                (bag[0], bag[1]) = (bag[1], bag[0]);
            cursor = 0;
        }

        private uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        private float Range(float minimum, float maximum) =>
            minimum + (Next() & 0x00FFFFFFu) / 16777215f * (maximum - minimum);
    }

    public static class LastRouteRideSpeechSession
    {
        public static LastRouteRideSpeechState State { get; private set; } =
            new LastRouteRideSpeechState(unchecked((uint)System.Environment.TickCount));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetSession()
        {
            State = new LastRouteRideSpeechState(unchecked((uint)System.Environment.TickCount));
        }
    }
}
