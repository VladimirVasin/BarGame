namespace BarPromenade
{
    /// <summary>
    /// What the spade meets on the way down. A grave is not one
    /// material: the sod comes off in a sheet, the loam under it cuts
    /// clean, and somewhere below that the yard keeps a stone or a
    /// root that has to be worked at rather than lifted.
    ///
    /// The kind is fixed per course of per segment before anybody
    /// starts, so a grave digs the same way every time it is dug.
    /// </summary>
    public enum CemeterySoilKind
    {
        /// <summary>The turf lid. One clean sheet, always the top
        /// course.</summary>
        Turf = 0,

        /// <summary>Ordinary graveyard loam. The measure everything
        /// else is harder than.</summary>
        Loam = 1,

        /// <summary>Wet clay. It holds the blade, so the swing has to
        /// be truer and it takes two goes.</summary>
        Clay = 2,

        /// <summary>A stone in the way. Narrowest bite of all: it is
        /// levered out, not cut.</summary>
        Stone = 3,

        /// <summary>A root across the cut. Two clean chops sever it,
        /// and a jarred blade puts you back to the first.</summary>
        Root = 4,

        /// <summary>Loose spoil off the heap, going back in. Nothing
        /// resists a shovel of earth you dug yourself an hour
        /// ago.</summary>
        Spoil = 5
    }

    /// <summary>
    /// How one kind of ground answers the spade: how wide the window
    /// of a good strike is, how fast the swing runs through it, and
    /// how many good strikes the course is worth.
    /// </summary>
    public readonly struct CemeterySoilProfile
    {
        public CemeterySoilProfile(
            CemeterySoilKind kind,
            float biteHalfWidth,
            float grazeHalfWidth,
            float swingsPerSecond)
        {
            Kind = kind;
            BiteHalfWidth = biteHalfWidth;
            GrazeHalfWidth = grazeHalfWidth;
            SwingsPerSecond = swingsPerSecond;
        }

        public CemeterySoilKind Kind { get; }

        /// <summary>Half-width of the biting window on the swing,
        /// which runs from <c>-1</c> to <c>1</c> about its
        /// centre.</summary>
        public float BiteHalfWidth { get; }

        /// <summary>Half-width of the band outside the bite where the
        /// blade only grazes. Beyond it the blade jars.</summary>
        public float GrazeHalfWidth { get; }

        /// <summary>Full sweeps of the swing marker per second. Hard
        /// ground is met faster as well as narrower.</summary>
        public float SwingsPerSecond { get; }
    }

    /// <summary>
    /// The table of ground, in one place, so the numbers can be read
    /// against each other rather than hunted through a switch.
    /// </summary>
    public static class CemeterySoilTable
    {
        /// <summary>
        /// Tuned by how long the marker actually spends inside the
        /// window, not by how the numbers look. The marker is a sine,
        /// so it is at its fastest exactly where the window is, and a
        /// bite band that reads generously on screen can still be
        /// three frames wide in the hand — which is what these were
        /// before, and it was unplayable.
        ///
        /// The floor is the hard ground, at a little over a tenth of a
        /// second, and the sod is more than twice that.
        /// `CemeteryGraveWorkTests` measures every row against the
        /// model rather than against this comment.
        ///
        /// Every course is one good strike, whatever it is made of.
        /// Hard ground used to want two, and two strikes on one square
        /// is not difficulty — it is the same shot asked for twice.
        /// What makes stone hard is that the window to hit it in is
        /// half the width of loam's, and that is enough.
        /// </summary>
        private static readonly CemeterySoilProfile[] Profiles =
        {
            new CemeterySoilProfile(
                CemeterySoilKind.Turf, 0.34f, 0.22f, 0.45f),
            new CemeterySoilProfile(
                CemeterySoilKind.Loam, 0.31f, 0.21f, 0.52f),
            new CemeterySoilProfile(
                CemeterySoilKind.Clay, 0.27f, 0.20f, 0.62f),
            new CemeterySoilProfile(
                CemeterySoilKind.Stone, 0.23f, 0.19f, 0.70f),
            new CemeterySoilProfile(
                CemeterySoilKind.Root, 0.25f, 0.19f, 0.66f),
            new CemeterySoilProfile(
                CemeterySoilKind.Spoil, 0.33f, 0.22f, 0.50f)
        };

        public static CemeterySoilProfile Get(CemeterySoilKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < Profiles.Length
                ? Profiles[index]
                : Profiles[(int)CemeterySoilKind.Loam];
        }

        /// <summary>Every profile, in enum order, for tests that want
        /// to hold the whole table to one rule.</summary>
        public static int Count => Profiles.Length;
    }
}
