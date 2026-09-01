using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Something the hero can walk on that has its own opinion about a step.
    ///
    /// The motor decides WHEN a step happens - it counts stride against
    /// achieved movement - and hands the moment over. Whoever takes it owns
    /// the whole step, sound and effect together, so a surface and the
    /// default can never both fire and double the sound.
    /// </summary>
    public interface IPlayerFootstepSurface
    {
        /// <summary>
        /// Plays this step if the surface claims it. Returns `false` to let
        /// the motor fall back to the ordinary footstep.
        /// </summary>
        bool TryPlayFootstep(Vector3 position, float runBlend);
    }
}
