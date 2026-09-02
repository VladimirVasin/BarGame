using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Who is looking at the board and who is looking at his neighbour, as a
    /// pure function of the clock.
    ///
    /// Two men over a backgammon board do not stare at it continuously. One
    /// studies the pieces while the other watches him; then the first looks
    /// up to say something and the second has already turned back down. THEY
    /// ARE NEVER BOTH LOOKING AWAY AT ONCE - that is the whole read, and it is
    /// why this is one model shared by the pair rather than two independent
    /// idles that would drift into looking at nothing together.
    ///
    /// Absolute-time and seeded, the shape `CemeteryRavenIdleModel` uses: the
    /// answer depends only on elapsed seconds, so frame chunking cannot move
    /// the chosen moment and a sweep can assert the whole cycle headlessly
    /// without a scene.
    ///
    /// It exists because the seated half of the courtyard cannot be fixed the
    /// way the standing half was. A standing resident got its own six-second
    /// working loop for free on 2026-09-02; both seated clips this cast can
    /// use - `WatchmanSit` and `WeigherSit` - are one slow breath each, so
    /// without this the pair would still be two men sitting perfectly still.
    /// </summary>
    public static class CityCourtyardNardiExchange
    {
        /// <summary>
        /// One full exchange: one man's turn to the other and back, then the
        /// other's. Slow on purpose - this is a game that has been going on
        /// all afternoon, not a conversation.
        /// </summary>
        public const float CycleSeconds = 17f;

        /// <summary>How much of a half-cycle a man spends actually turned to
        /// his neighbour. The rest he is over the board.</summary>
        public const float LookShare = 0.34f;

        /// <summary>
        /// How far apart the two halves are pushed. Exactly half a cycle
        /// would make the pair metronomic; this is the seeded wobble that
        /// keeps two boards in one city from beating together.
        /// </summary>
        public const float MaximumSkew = 0.12f;

        /// <summary>
        /// The weight for one seat, in `[0, 1]`: `0` is head down over the
        /// board, `1` is fully turned to the neighbour.
        /// </summary>
        /// <param name="elapsedSeconds">Absolute time since the pocket was
        /// built.</param>
        /// <param name="seed">The pocket's own seed, so two courtyards in one
        /// city do not run in step.</param>
        /// <param name="isSecondSeat">Which of the pair this is.</param>
        public static float Evaluate(
            float elapsedSeconds,
            int seed,
            bool isSecondSeat)
        {
            if (float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds))
            {
                return 0f;
            }

            float skew = Skew(seed);
            float offset = isSecondSeat ? 0.5f + skew : 0f;
            float phase = Mathf.Repeat(
                (elapsedSeconds / CycleSeconds) + offset,
                1f);

            // A raised-cosine window rather than a step: the turn has to have
            // a beginning and an end, and this is the same eased shape the
            // cafe pair's own weight ramp produces.
            if (phase >= LookShare)
            {
                return 0f;
            }

            float within = phase / LookShare;
            return 0.5f - (0.5f * Mathf.Cos(within * 2f * Mathf.PI));
        }

        /// <summary>
        /// True while this seat is the one bent over the pieces. Exposed
        /// because it is the readable half of the contract: a test asserts
        /// that at every sampled moment at least one of the two is.
        /// </summary>
        public static bool IsAtTheBoard(
            float elapsedSeconds,
            int seed,
            bool isSecondSeat)
        {
            return Evaluate(elapsedSeconds, seed, isSecondSeat) < 0.5f;
        }

        /// <summary>
        /// The pocket's own displacement of the second seat, in `[-Maximum,
        /// +Maximum]` cycles. Deterministic from the seed and nothing else.
        /// </summary>
        public static float Skew(int seed)
        {
            unchecked
            {
                uint hash = (uint)seed * 2654435761u;
                hash ^= hash >> 15;
                float unit = (hash % 10007u) / 10007f;
                return ((unit * 2f) - 1f) * MaximumSkew;
            }
        }
    }
}
