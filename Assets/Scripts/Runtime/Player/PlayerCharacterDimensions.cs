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

        // Matches the production Hero V2 A-pose pelvis bone head relative to
        // the grounded model root in tools/build-player-3d-model-v2.py.
        public const float PelvisHeight = 0.835f;

        // Contextual clips are pinned to the world by their pelvis bone and
        // nothing grounds the rig while one plays, so how far the hero's
        // weight hangs below that bone has to be a known quantity rather than
        // a guessed clearance. tools/player_3d_model_common.py measures all
        // three against the real posed meshes on every rebuild and publishes
        // them as `bed_contract` in both model manifests; the focused player
        // asset tests fail if these copies drift. Take a new number from the
        // production generator's report — never tune one here.
        public const float SupinePelvisSupportOffset = 0.1377f;
        public const float SupineHeadSupportOffset = 0.0656f;
        public const float SeatedPelvisSupportOffset = 0.0239f;

        public static Vector3 GetUprightPelvisPosition(
            Vector3 rootPosition,
            float verticalOffset = 0f)
        {
            return rootPosition +
                Vector3.up * (PelvisHeight + verticalOffset);
        }
    }
}
