using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Marks preview cameras that must exclude the shared world lens, SMAA,
    /// and optional warehouse lighting. A render texture alone is not an
    /// exclusion: gameplay captures deliberately use the same native pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenePresentationExclusion : MonoBehaviour { }
}
