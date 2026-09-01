using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Puts the mother in her chair and starts the chair rocking.
    ///
    /// One woman, no pool, no director, no spawn band - the drying-yard
    /// babushka pattern with the population removed. She is where she is
    /// whenever the room exists.
    ///
    /// SHE IS PASSIVE AND STAYS PASSIVE. No collider (the chair's own blocker
    /// already stands there), no AudioSource (the room holds exactly three and
    /// a fourth breaks its soundscape contract), no interaction (the hero's
    /// reaction to his mother is not written, and this is not the place to
    /// write it). The hero's attention finds her through a magnet at her
    /// seated head height, the way it finds every other colliderless figure
    /// in the game.
    /// </summary>
    public static class MothersHouseMotherFactory
    {
        public const string RuntimeRootName = "Mother's House Mother";

        /// <summary>
        /// Her seated head, for the attention magnet. The cushion is at
        /// `0.57` and an old woman settled back adds about three quarters of
        /// a metre of spine and skull - well below the `1.45` the standing
        /// babushkas use, because she is sitting down.
        /// </summary>
        public const float SeatedFocusHeight = 1.32f;

        public static MothersHouseMotherPresentation Create(
            Transform roomRoot,
            MothersHouseMotherPlan plan,
            MothersHouseRockingChairMotion chairMotion)
        {
            return Create(
                roomRoot,
                plan,
                chairMotion,
                MothersHouseMotherProvider.Load());
        }

        public static MothersHouseMotherPresentation Create(
            Transform roomRoot,
            MothersHouseMotherPlan plan,
            MothersHouseRockingChairMotion chairMotion,
            MothersHouseMotherProvider provider)
        {
            if (roomRoot == null)
            {
                throw new ArgumentNullException(nameof(roomRoot));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "mothers_house",
                    "mother_provider_missing");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                roomRoot);
            instance.name = RuntimeRootName;
            instance.transform.localPosition = plan.SeatPosition;
            instance.transform.localRotation =
                Quaternion.LookRotation(plan.Facing, Vector3.up);
            instance.transform.localScale = Vector3.one;

            CityPedestrianAssetRegistry registry =
                instance.GetComponentInChildren<
                    CityPedestrianAssetRegistry>(true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException(
                    "The staged mother prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            MothersHouseMotherPresentation presentation =
                instance.AddComponent<MothersHouseMotherPresentation>();
            presentation.Initialize(registry, plan.InitialPhase);

            PlayerAttentionMagnet magnet =
                instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = SeatedFocusHeight;

            // The chair takes her AFTER she is placed, so the pose it records
            // as her rest is the one the plan asked for. Handing her over
            // before placement would freeze her at the room origin and rock
            // her there for the rest of the scene.
            if (chairMotion != null)
            {
                chairMotion.Carry(instance.transform);
            }

            GameLog.Info(
                "mothers_house",
                "mother_seated",
                GameLog.Field("design_id", registry.DesignId),
                GameLog.Field("seat_x", plan.SeatPosition.x),
                GameLog.Field("seat_z", plan.SeatPosition.z),
                GameLog.Field("triangles", registry.SourceTriangleCount),
                GameLog.Field("has_face_atlas", registry.HasFaceAtlas));
            return presentation;
        }

        /// <summary>
        /// The staged prefab is authored passive and the editor pipeline
        /// enforces that. This is the same check on the other side of the
        /// import, because a prefab can gain a component between the two.
        /// </summary>
        private static void ValidatePassivePresentation(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "The staged mother presentation must stay passive.");
            }
        }
    }
}
