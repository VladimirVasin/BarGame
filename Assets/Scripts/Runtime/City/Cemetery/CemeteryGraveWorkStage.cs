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

        /// <summary>Taken. The plot is marked out and unbroken, with
        /// the work lamp already burning at the head of it — he was
        /// sent to dig, and a man sent to dig is sent with a light.
        /// </summary>
        Marked = 1,

        /// <summary>The hole is open.</summary>
        Dug = 2,

        /// <summary>The coffin is down at the bottom of it.</summary>
        Coffined = 3,

        /// <summary>The earth is back and the mound is turned over
        /// him, but the head of the grave is still bare. The lamp is
        /// picked up with the last spadeful — it was on the ground
        /// because there was a hole to see into, and there is no
        /// longer a hole.</summary>
        Filled = 4,

        /// <summary>The stone is standing at the head: this, and
        /// nothing earlier, is a finished grave. Filling and setting
        /// are separate rungs because closing a hole cannot be undone
        /// — a man who walks off halfway through the stone must not
        /// find the earth open again.</summary>
        Sealed = 5,

        /// <summary>The watchman has settled up for the work.
        /// </summary>
        Paid = 6
    }
}
