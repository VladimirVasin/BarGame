using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The foot layer splits one locomotion cycle into a plant per boot with
    /// <see cref="PlayerFootPlacementRules.FootPlantAmounts"/>, and that rule
    /// assumes an authoring fact it cannot see: the LEFT heel contacts at
    /// cycle <c>0</c> and the right at <c>0.5</c>, for Walk and Run alike
    /// (<c>tools/player_3d_model_common.py</c> keys <c>walk_left_contact</c>
    /// at 0 and its mirror at 0.5; the V2 Run does the same). Nothing in
    /// Unity re-derives the order, so a re-authored clip with the other foot
    /// leading would leave every rules test green while the IK planted the
    /// swinging boot and let the standing one float. This samples the
    /// shipped clips and checks the order against the rules themselves.
    /// </summary>
    public sealed class Player3DWalkContactOrderTests
    {
        private const string WalkClipName = "Walk";
        private const string RunClipName = "Run";

        /// <summary>
        /// How far ahead the contacting foot must be, along the actor's
        /// forward. The authored walk stride separates the ankles by roughly
        /// <c>0.35 m</c> at heel strike; the bind pose separates them by
        /// nothing, so an inert sample fails here rather than passing by
        /// symmetry.
        /// </summary>
        private const float MinimumStrideSeparation = 0.05f;

        /// <summary>
        /// A planted heel keeps its ankle at or below the other foot's: the
        /// trailing boot is at toe-off with its heel raised. Two centimetres
        /// covers the authored ankle bob without hiding a swapped pair.
        /// </summary>
        private const float PlantedHeightTolerance = 0.02f;

        [Test]
        public void Player3DClipContact_WalkLeftFootPlantsAtCycleZero()
        {
            AssertLeftFootContactsAtCycleZero(WalkClipName, false);
        }

        [Test]
        public void Player3DClipContact_RunLeftFootPlantsAtCycleZero()
        {
            AssertLeftFootContactsAtCycleZero(RunClipName, true);
        }

        private static void AssertLeftFootContactsAtCycleZero(
            string clipName,
            bool runLandmarks)
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore(
                    "Production Player 3D prefab has not been generated yet.");
            }

            // The rule this clip has to agree with: full plant on the left
            // and none on the right at cycle 0, the reverse at 0.5.
            PlayerFootPlacementRules.FootPlantAmounts(
                0f,
                runLandmarks,
                0f,
                out float leftPlantAtZero,
                out float rightPlantAtZero);
            Assert.That(leftPlantAtZero, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(rightPlantAtZero, Is.EqualTo(0f).Within(0.0001f));
            PlayerFootPlacementRules.FootPlantAmounts(
                0.5f,
                runLandmarks,
                0f,
                out float leftPlantAtHalf,
                out float rightPlantAtHalf);
            Assert.That(leftPlantAtHalf, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(rightPlantAtHalf, Is.EqualTo(1f).Within(0.0001f));

            GameObject owner = new GameObject("Hero clip contact owner");
            try
            {
                Player3DAssetRegistry registry =
                    Player3DResources.Instantiate(owner.transform);
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.Animator, Is.Not.Null);
                Assert.That(registry.Anchors.LeftFoot, Is.Not.Null);
                Assert.That(registry.Anchors.RightFoot, Is.Not.Null);

                if (!registry.TryGetAnimation(
                        clipName,
                        out Player3DAnimationBinding binding))
                {
                    if (clipName == RunClipName)
                    {
                        Assert.Ignore(
                            "The production hero registry has no Run clip; " +
                            "the Run landmarks have nothing to check against.");
                    }

                    Assert.Fail(
                        $"The production hero registry has no '{clipName}' " +
                        "clip; the foot layer cannot walk without it.");
                }

                AnimationClip clip = binding.Clip;
                Assert.That(clip, Is.Not.Null, $"'{clipName}' binding has no clip.");
                Assert.That(
                    clip.length,
                    Is.GreaterThan(0f),
                    $"'{clipName}' has no duration to sample.");

                Vector3 forward = Vector3.ProjectOnPlane(
                    registry.transform.TransformDirection(
                        registry.Metrics.LocalForward),
                    Vector3.up);
                Assert.That(
                    forward.sqrMagnitude,
                    Is.GreaterThan(0.0001f),
                    "The registry's declared forward must be horizontal.");
                forward.Normalize();

                Vector3 leftAtZero;
                Vector3 rightAtZero;
                SampleFeet(registry, clip, 0f, out leftAtZero, out rightAtZero);
                Vector3 leftAtHalf;
                Vector3 rightAtHalf;
                SampleFeet(
                    registry,
                    clip,
                    0.5f * clip.length,
                    out leftAtHalf,
                    out rightAtHalf);

                float leftLeadAtZero = Vector3.Dot(
                    leftAtZero - rightAtZero,
                    forward);
                Assert.That(
                    leftLeadAtZero,
                    Is.GreaterThan(MinimumStrideSeparation),
                    $"At cycle 0 of '{clipName}' the LEFT foot must lead by " +
                    $"more than {MinimumStrideSeparation:F2} m along the " +
                    $"actor's forward (it leads by {leftLeadAtZero:F3} m). " +
                    "FootPlantAmounts plants the left heel here; a right-led " +
                    "clip would pin the wrong boot. A lead near zero means " +
                    "SampleAnimation did not move the rig at all.");
                Assert.That(
                    leftAtZero.y,
                    Is.LessThanOrEqualTo(rightAtZero.y + PlantedHeightTolerance),
                    $"At cycle 0 of '{clipName}' the planted left ankle " +
                    $"({leftAtZero.y:F3}) must sit at or below the trailing " +
                    $"right ankle ({rightAtZero.y:F3}), within " +
                    $"{PlantedHeightTolerance:F2} m.");

                float rightLeadAtHalf = Vector3.Dot(
                    rightAtHalf - leftAtHalf,
                    forward);
                Assert.That(
                    rightLeadAtHalf,
                    Is.GreaterThan(MinimumStrideSeparation),
                    $"At cycle 0.5 of '{clipName}' the RIGHT foot must lead " +
                    $"by more than {MinimumStrideSeparation:F2} m along the " +
                    $"actor's forward (it leads by {rightLeadAtHalf:F3} m); " +
                    "FootPlantAmounts plants the right heel here.");
                Assert.That(
                    rightAtHalf.y,
                    Is.LessThanOrEqualTo(leftAtHalf.y + PlantedHeightTolerance),
                    $"At cycle 0.5 of '{clipName}' the planted right ankle " +
                    $"({rightAtHalf.y:F3}) must sit at or below the trailing " +
                    $"left ankle ({leftAtHalf.y:F3}), within " +
                    $"{PlantedHeightTolerance:F2} m.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>
        /// Poses the rig at one clip time and reads both ankle bones. The
        /// clip's paths are relative to the Animator's object, which is the
        /// imported model root the anchors hang under.
        /// </summary>
        private static void SampleFeet(
            Player3DAssetRegistry registry,
            AnimationClip clip,
            float time,
            out Vector3 leftFoot,
            out Vector3 rightFoot)
        {
            clip.SampleAnimation(registry.Animator.gameObject, time);
            leftFoot = registry.Anchors.LeftFoot.position;
            rightFoot = registry.Anchors.RightFoot.position;
        }
    }
}
