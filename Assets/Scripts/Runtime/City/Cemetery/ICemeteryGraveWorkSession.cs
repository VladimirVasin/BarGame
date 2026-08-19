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
        /// Opens the work for one act. True when the session took it,
        /// and the caller must then leave the stage alone until the
        /// session commits it. False when this act is not one the
        /// session knows how to run, or one is already open.
        /// </summary>
        bool TryBegin(CemeteryGraveWorkStage stage);
    }
}
