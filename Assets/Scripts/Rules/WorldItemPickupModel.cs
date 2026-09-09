namespace BarPromenade
{
    /// <summary>
    /// The order of one find: the thing is held up, the player agrees to take
    /// it, it leaves the world.
    ///
    /// The commit is the whole reason this is a model and not two booleans in
    /// a component. A find that credited the inventory the moment E was
    /// pressed would hand the player the item and still leave it lying there
    /// if the screen never finished - a scene unload, a lost modal lock, a
    /// disabled component. So nothing is collected until <see cref="Confirm"/>
    /// says so, and it says so exactly once.
    /// </summary>
    public sealed class WorldItemPickupModel
    {
        private readonly WorldItemInspectionTimeline timeline =
            new WorldItemInspectionTimeline();

        public WorldItemInspectionTimeline Timeline => timeline;

        /// <summary>Whether the found-item screen is on screen at all.</summary>
        public bool IsOpen => timeline.IsActive;

        /// <summary>Whether the item is being held still to be read.</summary>
        public bool IsShowing => timeline.IsInspecting;

        /// <summary>Whether the player has agreed to take the item.</summary>
        public bool IsTaken { get; private set; }

        /// <summary>
        /// Whether the screen has both committed and finished its return, so
        /// the owner may destroy the world object and give input back.
        /// </summary>
        public bool IsSettled => IsTaken && !timeline.IsActive;

        public bool CanOpen => !IsTaken && timeline.CanBeginInspection;

        /// <summary>
        /// Confirming is allowed the moment the screen opens, so a player who
        /// already knows what he found is not made to wait out the flight.
        /// </summary>
        public bool CanConfirm => !IsTaken && timeline.IsActive;

        public bool Open()
        {
            return CanOpen && timeline.BeginInspection();
        }

        /// <summary>
        /// Returns true on the one call that commits the take; the caller
        /// credits the inventory then and only then.
        /// </summary>
        public bool Confirm()
        {
            if (!CanConfirm)
            {
                return false;
            }

            IsTaken = true;
            timeline.BeginReturn();
            return true;
        }

        /// <summary>
        /// Backs out of the find, letting the screen close the way a take
        /// closes it. Nothing is credited. This is the escape a player needs
        /// when the item cannot be accepted at all - without it the only
        /// action on screen is one that keeps failing.
        /// </summary>
        public bool Dismiss()
        {
            // BeginReturn already refuses a browsing or a returning screen,
            // so a second press during the fly-out reports false rather than
            // playing the cancel again over a thing already on its way down.
            return !IsTaken && timeline.BeginReturn();
        }

        /// <summary>
        /// Drops the screen at once without taking anything, for lifecycle
        /// teardown. An already committed take is not undone - the item is in
        /// the inventory by then.
        /// </summary>
        public bool Abandon()
        {
            bool wasOpen = timeline.IsActive;
            timeline.Cancel();
            return wasOpen && !IsTaken;
        }

        public void Advance(float unscaledDeltaTime)
        {
            timeline.Advance(unscaledDeltaTime);
        }

        public bool AddManualRotation(float degrees)
        {
            return timeline.AddManualRotation(degrees);
        }
    }
}
