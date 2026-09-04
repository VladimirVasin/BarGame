using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Marks a camera that does not present the game frame: its geometry
    /// is not snapped to the PS1 pixel grid, and the Begotten print is
    /// not laid over it.
    ///
    /// The snap is a screen-space effect, so it only means anything for a
    /// camera that presents the game frame. Two cameras in this project
    /// render something else entirely and are ruined by it: the inventory
    /// preview, an orthographic camera a few centimetres wide where the
    /// grid is coarse enough to quantize a whole item into a handful of
    /// positions, and the reflection probe, which renders six cube faces
    /// whose grids do not line up with each other and so would seam along
    /// every cube edge. The print is worse still on them: it holds one
    /// picture across frames, which would freeze every cube face on the
    /// first one.
    ///
    /// A marker component rather than a heuristic on purpose: rendering to
    /// a texture is not the discriminator, because the composite's own
    /// tests render to one and must keep the effect.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Ps1VertexJitterExclusion : MonoBehaviour
    {
    }
}
