using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Presentation-neutral dimensions of the production player character.
    /// World plans use these values instead of deriving gameplay anchors from
    /// legacy sprite pixels or from a particular runtime renderer.
    /// </summary>
    public static class PlayerCharacterDimensions
    {
        public const float StandingHeight = 1.75f;

        // Matches the production A-pose pelvis bone head relative to the
        // grounded model root in tools/build-player-3d-model.py.
        public const float PelvisHeight = 0.70f;

        public static Vector3 GetUprightPelvisPosition(
            Vector3 rootPosition,
            float verticalOffset = 0f)
        {
            return rootPosition +
                Vector3.up * (PelvisHeight + verticalOffset);
        }
    }
}
