namespace BarPromenade
{
    /// <summary>
    /// Whatever makes the hero earn an act of the gravedigging rather
    /// than be handed it.
    ///
    /// It is an interface for one reason: the job has to keep working
    /// without one. Every EditMode test, and any build with no camera
    /// to hand the work over to, drives
    /// <see cref="CemeteryGravediggingController.TryAdvance"/> straight
    /// and gets the ladder it has always got. The session is a layer
    /// over that, never a replacement for it.
    /// </summary>
    public interface ICemeteryGraveWorkSession
    {
        /// <summary>
        /// Opens the work for one act on one grave. True when the
        /// session took it, and the caller must then leave the stage
        /// alone until the session commits it. False when this act is
        /// not one the session knows how to run, or one is already
        /// open.
        ///
        /// The grave is passed in rather than held: there are several
        /// worksites in the yard at once and only one camera, so the
        /// session belongs to whichever hole the hero is standing in.
        /// </summary>
        bool TryBegin(
            CemeteryGravediggingController job,
            CemeteryGraveWorkStage stage);
    }
}
