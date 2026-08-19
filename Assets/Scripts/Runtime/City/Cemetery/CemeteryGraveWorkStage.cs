namespace BarPromenade
{
    /// <summary>
    /// How far the gravedigger's job has got. It is one monotone
    /// ladder and nothing on it ever goes back down, so the whole
    /// worksite — the marked-out plot, the open hole, the lamp beside
    /// it, the coffin and the finished stone — is a pure function of
    /// this single value. That is what lets a trip indoors and back
    /// rebuild the work exactly as it was left.
    /// </summary>
    public enum CemeteryGraveWorkStage
    {
        /// <summary>The old man still has the job to give.</summary>
        Unclaimed = 0,

        /// <summary>Taken. The plot is marked out and unbroken.
        /// </summary>
        Marked = 1,

        /// <summary>The hole is open and the lamp is burning beside
        /// it.</summary>
        Dug = 2,

        /// <summary>The coffin is down at the bottom of it.</summary>
        Coffined = 3,

        /// <summary>Filled in with the stone standing at the head:
        /// this, and nothing earlier, is a finished grave. The lamp is
        /// picked up with the last spadeful — it was on the ground
        /// because there was a hole to see into.</summary>
        Sealed = 4,

        /// <summary>The watchman has settled up for the work.
        /// </summary>
        Paid = 5
    }
}
