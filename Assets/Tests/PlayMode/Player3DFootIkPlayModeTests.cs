using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The hero's per-foot leg-IK layer against real colliders: a flat
    /// floor keeps the legacy grounding band, a kerb under one boot lifts
    /// that boot alone, a render-only tread on the <c>FootProbe</c> layer
    /// is a floor for the boots but not for the controller, and the late
    /// layer reapplies to the same pose.
    ///
    /// The hero stands still (the motor is disabled) on a cube whose top
    /// face is <c>y = 0</c>, with its root at
    /// <see cref="PlayerFactory.GroundedRootOffset"/>. The clock is pinned
    /// so every frame count below is a duration.
    /// </summary>
    public sealed class Player3DFootIkPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const float GroundTopY = 0f;
        private const float KerbHeight = 0.06f;
        private const float BlockHeight = 0.10f;
        private const float SoleTolerance = 0.005f;
        private const float RaisedSoleTolerance = 0.015f;
        private const float StepFootprint = 0.6f;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject playerObject;
        private GameObject stepObject;
        private PlayerRuntime player;
        private Player3DCharacterPresentation presentation;
        private Player3DAssetRegistry registry;
        private Mesh bakedFootMesh;
        private readonly List<Vector3> bakedFootVertices =
            new List<Vector3>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Foot IK Test Ground";
            groundObject.transform.position =
                new Vector3(0f, GroundTopY - 0.1f, 0f);
            groundObject.transform.localScale = new Vector3(8f, 0.2f, 8f);

            cameraObject = new GameObject("Foot IK Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            player = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            Assert.That(
                player.Visual,
                Is.TypeOf<Player3DCharacterPresentation>());
            presentation = (Player3DCharacterPresentation)player.Visual;
            registry = presentation.Registry;
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Anchors.LeftFoot, Is.Not.Null);
            Assert.That(registry.Anchors.RightFoot, Is.Not.Null);
            Assert.That(registry.Anchors.Pelvis, Is.Not.Null);
            bakedFootMesh = new Mesh
            {
                name = "Foot IK Test Bake Mesh"
            };

            Physics.SyncTransforms();
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (stepObject != null)
            {
                Object.Destroy(stepObject);
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (groundObject != null)
            {
                Object.Destroy(groundObject);
            }

            if (bakedFootMesh != null)
            {
                Object.Destroy(bakedFootMesh);
            }

            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FlatGround_KeepsNeutralSolesOnTheFloor()
        {
            yield return Frames(30);
            presentation.ReapplyLatePresentationPose();

            float leftSole = GetLowestVisibleMeshY(registry, "foot.L");
            float rightSole = GetLowestVisibleMeshY(registry, "foot.R");
            float floorBand = GroundTopY +
                              PlayerFactory.GroundedRootOffset +
                              SoleTolerance;
            Assert.That(
                leftSole,
                Is.InRange(GroundTopY - SoleTolerance, floorBand),
                "The idle left sole must rest on the probed floor, " +
                "floating at most the controller's skin width.");
            Assert.That(
                rightSole,
                Is.InRange(GroundTopY - SoleTolerance, floorBand),
                "The idle right sole must rest on the probed floor, " +
                "floating at most the controller's skin width.");
            Assert.That(
                presentation.LeftFootGround.HasSurface,
                Is.True,
                "The left heel ray must find the floor collider.");
            Assert.That(
                presentation.RightFootGround.HasSurface,
                Is.True,
                "The right heel ray must find the floor collider.");
            Assert.That(
                presentation.LeftFootGround.HeelY,
                Is.EqualTo(GroundTopY).Within(0.01f));
            Assert.That(
                presentation.RightFootGround.HeelY,
                Is.EqualTo(GroundTopY).Within(0.01f));
            Assert.That(
                presentation.FootIkBlend,
                Is.GreaterThanOrEqualTo(0.99f),
                "Half a second of idle must fade the leg solve fully in.");
        }

        [UnityTest]
        public IEnumerator FlatGround_WalkKeepsABootPlantedEveryFrame()
        {
            presentation.SetMotion(new PlayerMotionSample(
                Vector3.forward *
                Player3DCharacterPresentation.FullWalkSpeed,
                Player3DCharacterPresentation.FullWalkSpeed,
                0f));
            yield return WaitForLocomotionBlend(0.99f, 180);
            Assert.That(
                presentation.CurrentLocomotionState,
                Is.EqualTo(Player3DLocomotionState.Walk));

            const int SampleFrames = 70;
            const float PlantedBand = 0.008f;
            float firstLowerSole = float.NaN;
            float lowestLowerSole = float.PositiveInfinity;
            float highestLowerSole = float.NegativeInfinity;
            for (int frame = 0; frame < SampleFrames; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                float lowerSole = Mathf.Min(
                    GetLowestVisibleMeshY(registry, "foot.L"),
                    GetLowestVisibleMeshY(registry, "foot.R"));
                if (frame == 0)
                {
                    firstLowerSole = lowerSole;
                }

                lowestLowerSole = Mathf.Min(lowestLowerSole, lowerSole);
                highestLowerSole = Mathf.Max(highestLowerSole, lowerSole);
            }

            Assert.That(float.IsNaN(firstLowerSole), Is.False);
            Assert.That(
                lowestLowerSole,
                Is.GreaterThanOrEqualTo(firstLowerSole - PlantedBand),
                "The planted boot must not sink below its first-frame " +
                "floor contact through the walk cycle.");
            Assert.That(
                highestLowerSole,
                Is.LessThanOrEqualTo(firstLowerSole + PlantedBand),
                "The walk must keep a visible boot planted in every " +
                "sampled pose; the lower sole may not float.");
            Assert.That(
                lowestLowerSole,
                Is.GreaterThanOrEqualTo(GroundTopY - SoleTolerance),
                "No sole may pass through the probed floor.");
        }

        [UnityTest]
        public IEnumerator Kerb_LeadingFootRestsOnTheKerbTopWhileTheOtherStaysOnTheFloor()
        {
            yield return Frames(30);
            stepObject = CreateStepUnderLeftFoot(
                "Foot IK Test Kerb",
                KerbHeight,
                renderOnly: false);
            Physics.SyncTransforms();

            // The planted target follows the probe at 0.6 m/s, so a
            // 0.06 m kerb takes six frames; forty leaves margin.
            yield return Frames(40);
            presentation.ReapplyLatePresentationPose();

            AssertLeftFootRaised(KerbHeight);
        }

        [UnityTest]
        public IEnumerator Tread_FootProbeLayerColliderIsSeenByTheFootButNotTheController()
        {
            yield return Frames(30);
            // Exactly what the stair builders do: a render-only primitive
            // from the factory, then a tread collider on the probe layer.
            stepObject = CreateStepUnderLeftFoot(
                "Foot IK Test Tread",
                KerbHeight,
                renderOnly: true);
            BoxCollider tread = FootProbeSurface.AddTreadCollider(stepObject);
            Physics.SyncTransforms();
            yield return Frames(40);

            Assert.That(
                tread != null,
                Is.True,
                "AddTreadCollider must leave a live collider on a " +
                "render-only primitive. The factory removes the " +
                "primitive's own BoxCollider with a deferred Destroy, so a " +
                "doomed collider found by GetComponent in the same frame " +
                "must not be the one returned.");
            Assert.That(
                tread.enabled,
                Is.True,
                "The tread collider must be enabled for the foot probes.");
            Assert.That(
                tread.gameObject.layer,
                Is.EqualTo(FootProbeSurface.LayerIndex),
                "The tread's probe collider must live on the FootProbe layer.");
            Assert.That(
                tread.transform.parent,
                Is.EqualTo(stepObject.transform),
                "The probe collider is a child of the visible tread, which " +
                "keeps its own layer for the cameras.");
            Assert.That(
                LayerMask.LayerToName(FootProbeSurface.LayerIndex),
                Is.EqualTo(FootProbeSurface.LayerName));
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    playerObject.layer,
                    FootProbeSurface.LayerIndex),
                Is.True,
                "The walking hero's layer must not collide with treads.");

            // A tread is a trigger on the FootProbe layer: the foot probes
            // ask for triggers (QueryTriggerInteraction.Collide) and see it;
            // every obstacle sweep in the project passes Ignore and must not.
            Assert.That(
                tread.isTrigger,
                Is.True,
                "A tread collider must be a trigger so obstacle sweeps " +
                "that ignore triggers never see it.");
            Vector3 leftAnkle = registry.Anchors.LeftFoot.position;
            Vector3 rayOrigin = new Vector3(leftAnkle.x, 0.5f, leftAnkle.z);
            Assert.That(
                TryFindTreadHit(
                    rayOrigin,
                    QueryTriggerInteraction.Collide,
                    out float treadHitY),
                Is.True,
                "A default-mask ray that accepts triggers, as the foot " +
                "probes do, must see the tread under the left boot.");
            Assert.That(
                treadHitY,
                Is.EqualTo(GroundTopY + KerbHeight).Within(0.005f),
                "The ray must land on the tread's top face.");
            Assert.That(
                TryFindTreadHit(
                    rayOrigin,
                    QueryTriggerInteraction.Ignore,
                    out _),
                Is.False,
                "A ray that ignores triggers, as every obstacle sweep " +
                "does, must not see the tread.");

            presentation.ReapplyLatePresentationPose();
            AssertLeftFootRaised(KerbHeight);
        }

        [UnityTest]
        public IEnumerator Descent_LowerFootReachesDownAndPelvisDrops()
        {
            // The pelvis follows the LOWER foot, and here the lower foot
            // is the one still on the floor, so the capsule and the pelvis
            // stay where the clip put them while the left leg folds up
            // onto the block. What is asserted is that folding: each sole
            // on its own surface, both knees forward, the raised knee bent
            // more, and no pelvis excursion beyond the layer's lift cap.
            yield return Frames(30);
            presentation.ReapplyLatePresentationPose();
            float neutralPelvisY = registry.Anchors.Pelvis.position.y;

            stepObject = CreateStepUnderLeftFoot(
                "Foot IK Test Block",
                BlockHeight,
                renderOnly: false);
            Physics.SyncTransforms();
            yield return Frames(40);
            presentation.ReapplyLatePresentationPose();

            AssertLeftFootRaised(BlockHeight);
            float pelvisLowering =
                neutralPelvisY - registry.Anchors.Pelvis.position.y;
            Assert.That(
                pelvisLowering,
                Is.InRange(-0.02f, PlayerFootPlacementRules.DefaultPelvisMaximumLift),
                "With one boot on a block the pelvis must stay with the " +
                "lower boot: no drop past the lift cap and no rise beyond " +
                "the idle sway.");
        }

        [UnityTest]
        public IEnumerator ReapplyLatePresentationPose_IsIdempotentWithProbes()
        {
            yield return Frames(30);
            stepObject = CreateStepUnderLeftFoot(
                "Foot IK Test Kerb",
                KerbHeight,
                renderOnly: false);
            Physics.SyncTransforms();
            yield return Frames(40);

            presentation.ReapplyLatePresentationPose();
            Vector3 pelvisBefore = registry.Anchors.Pelvis.position;
            Vector3 leftFootBefore = registry.Anchors.LeftFoot.position;
            Vector3 rightFootBefore = registry.Anchors.RightFoot.position;
            float leftSoleBefore = GetLowestVisibleMeshY(registry, "foot.L");
            float rightSoleBefore = GetLowestVisibleMeshY(registry, "foot.R");

            presentation.ReapplyLatePresentationPose();

            const float Tolerance = 0.0005f;
            Assert.That(
                Vector3.Distance(
                    registry.Anchors.Pelvis.position,
                    pelvisBefore),
                Is.LessThan(Tolerance),
                "Reapplying the late pose must not accumulate a pelvis " +
                "offset.");
            Assert.That(
                Vector3.Distance(
                    registry.Anchors.LeftFoot.position,
                    leftFootBefore),
                Is.LessThan(Tolerance),
                "Reapplying the late pose must solve the left leg to the " +
                "same ankle.");
            Assert.That(
                Vector3.Distance(
                    registry.Anchors.RightFoot.position,
                    rightFootBefore),
                Is.LessThan(Tolerance),
                "Reapplying the late pose must solve the right leg to the " +
                "same ankle.");
            Assert.That(
                GetLowestVisibleMeshY(registry, "foot.L"),
                Is.EqualTo(leftSoleBefore).Within(Tolerance));
            Assert.That(
                GetLowestVisibleMeshY(registry, "foot.R"),
                Is.EqualTo(rightSoleBefore).Within(Tolerance));
        }

        private bool TryFindTreadHit(
            Vector3 origin,
            QueryTriggerInteraction triggerInteraction,
            out float hitY)
        {
            hitY = float.NaN;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                1f,
                Physics.DefaultRaycastLayers,
                triggerInteraction);
            for (int index = 0; index < hits.Length; index++)
            {
                if (hits[index].collider != null &&
                    hits[index].collider.transform.IsChildOf(
                        stepObject.transform))
                {
                    hitY = hits[index].point.y;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator Frames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                yield return null;
            }
        }

        private IEnumerator WaitForLocomotionBlend(
            float minimumBlend,
            int maximumFrames)
        {
            int frame = 0;
            while (presentation.LocomotionBlend < minimumBlend &&
                   frame < maximumFrames)
            {
                yield return null;
                frame++;
            }

            Assert.That(
                presentation.LocomotionBlend,
                Is.GreaterThanOrEqualTo(minimumBlend),
                "The locomotion crossfade did not reach the requested " +
                "weight in time.");
        }

        /// <summary>
        /// A box whose top is <paramref name="height"/> above the floor,
        /// under the LEFT boot only: it reaches from just short of the
        /// midline between the ankles outward past the left boot, and far
        /// enough fore and aft to catch both the heel and the toe ray.
        /// </summary>
        private GameObject CreateStepUnderLeftFoot(
            string name,
            float height,
            bool renderOnly)
        {
            Transform actor = playerObject.transform;
            Vector3 forward = actor.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = actor.right;
            right.y = 0f;
            right.Normalize();

            Vector3 leftAnkle = registry.Anchors.LeftFoot.position;
            Vector3 rightAnkle = registry.Anchors.RightFoot.position;
            float lateral = Vector3.Dot(leftAnkle - rightAnkle, right);
            Assert.That(
                Mathf.Abs(lateral),
                Is.GreaterThan(0.12f),
                "The idle stance must separate the boots enough for a " +
                "one-boot step.");
            float side = Mathf.Sign(lateral);
            float inwardReach = Mathf.Min(0.12f, Mathf.Abs(lateral) * 0.45f);
            Vector3 centre = leftAnkle +
                             right * (side * (StepFootprint * 0.5f - inwardReach)) +
                             forward * 0.08f;
            centre.y = GroundTopY + height * 0.5f;
            Vector3 size = new Vector3(StepFootprint, height, StepFootprint);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            GameObject step;
            if (renderOnly)
            {
                step = RuntimePrimitiveFactory.CreateBox(
                    name,
                    null,
                    centre,
                    size,
                    new Color(0.55f, 0.52f, 0.48f),
                    false);
            }
            else
            {
                step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = name;
                step.transform.position = centre;
                step.transform.localScale = size;
            }

            step.transform.rotation = rotation;
            return step;
        }

        private void AssertLeftFootRaised(float stepHeight)
        {
            float stepTop = GroundTopY + stepHeight;
            float leftSole = GetLowestVisibleMeshY(registry, "foot.L");
            float rightSole = GetLowestVisibleMeshY(registry, "foot.R");
            Assert.That(
                leftSole,
                Is.InRange(
                    stepTop - SoleTolerance,
                    stepTop +
                    PlayerFactory.GroundedRootOffset +
                    RaisedSoleTolerance),
                "The left sole must rest on the step top.");
            Assert.That(
                rightSole,
                Is.InRange(
                    GroundTopY - SoleTolerance,
                    GroundTopY +
                    PlayerFactory.GroundedRootOffset +
                    RaisedSoleTolerance),
                "The right sole must stay on the floor.");

            FootGroundSample leftGround = presentation.LeftFootGround;
            FootGroundSample rightGround = presentation.RightFootGround;
            Assert.That(leftGround.HasSurface, Is.True);
            Assert.That(rightGround.HasSurface, Is.True);
            Assert.That(
                leftGround.HeelY,
                Is.EqualTo(stepTop).Within(0.01f),
                "The left heel ray must land on the step top.");
            Assert.That(
                rightGround.HeelY,
                Is.EqualTo(GroundTopY).Within(0.01f),
                "The right heel ray must still land on the floor.");

            Transform leftThigh = GetPartBone(Player3DAnatomicalPart.LeftThigh);
            Transform leftShin = GetPartBone(Player3DAnatomicalPart.LeftShin);
            Transform leftFoot = GetPartBone(Player3DAnatomicalPart.LeftFoot);
            Transform rightThigh = GetPartBone(Player3DAnatomicalPart.RightThigh);
            Transform rightShin = GetPartBone(Player3DAnatomicalPart.RightShin);
            Transform rightFoot = GetPartBone(Player3DAnatomicalPart.RightFoot);
            Assert.That(leftThigh, Is.Not.Null);
            Assert.That(leftShin, Is.Not.Null);
            Assert.That(leftFoot, Is.Not.Null);
            Assert.That(rightThigh, Is.Not.Null);
            Assert.That(rightShin, Is.Not.Null);
            Assert.That(rightFoot, Is.Not.Null);

            Vector3 actorForward = playerObject.transform.forward;
            actorForward.y = 0f;
            actorForward.Normalize();
            Assert.That(
                KneeForwardOffset(leftThigh, leftShin, leftFoot, actorForward),
                Is.GreaterThan(0f),
                "The raised left knee must bend forward, never backward.");
            // The standing leg is the authored idle leg, untouched by the
            // solve because its boot already rests on the floor; the
            // authored relaxed knee sits a few millimetres BEHIND the
            // hip-ankle line, so only a real backward bend is a failure.
            Assert.That(
                KneeForwardOffset(rightThigh, rightShin, rightFoot, actorForward),
                Is.GreaterThan(-0.01f),
                "The standing right knee must not bend backward.");
            Assert.That(
                KneeAngleDegrees(leftThigh, leftShin, leftFoot),
                Is.LessThan(KneeAngleDegrees(rightThigh, rightShin, rightFoot)),
                "The leg on the step must bend more at the knee than the " +
                "leg on the floor.");
        }

        private static float KneeForwardOffset(
            Transform thigh,
            Transform shin,
            Transform foot,
            Vector3 actorForward)
        {
            Vector3 midpoint = 0.5f * (thigh.position + foot.position);
            return Vector3.Dot(shin.position - midpoint, actorForward);
        }

        /// <summary>Interior angle at the knee: <c>180</c> is a straight leg.</summary>
        private static float KneeAngleDegrees(
            Transform thigh,
            Transform shin,
            Transform foot)
        {
            return Vector3.Angle(
                thigh.position - shin.position,
                foot.position - shin.position);
        }

        private Transform GetPartBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) &&
                   binding != null
                ? binding.Bone
                : null;
        }

        private float GetLowestVisibleMeshY(
            Player3DAssetRegistry assetRegistry,
            params string[] boneNames)
        {
            bool filterByBone = boneNames != null && boneNames.Length > 0;
            float lowestY = float.PositiveInfinity;
            for (int index = 0;
                 index < assetRegistry.MeshBindings.Count;
                 index++)
            {
                Player3DMeshBinding binding =
                    assetRegistry.MeshBindings[index];
                if (binding == null ||
                    (filterByBone &&
                     !ContainsBoneName(boneNames, binding.BoneName)) ||
                    binding.Renderer == null ||
                    !binding.Renderer.enabled ||
                    !(binding.Renderer is SkinnedMeshRenderer renderer))
                {
                    continue;
                }

                bakedFootMesh.Clear(false);
                renderer.BakeMesh(bakedFootMesh, true);
                bakedFootVertices.Clear();
                bakedFootMesh.GetVertices(bakedFootVertices);
                for (int vertexIndex = 0;
                     vertexIndex < bakedFootVertices.Count;
                     vertexIndex++)
                {
                    Vector3 worldVertex = renderer.transform.TransformPoint(
                        bakedFootVertices[vertexIndex]);
                    lowestY = Mathf.Min(lowestY, worldVertex.y);
                }
            }

            Assert.That(
                float.IsPositiveInfinity(lowestY),
                Is.False,
                filterByBone
                    ? "The production registry must expose visible meshes " +
                      "for " + string.Join(", ", boneNames) + "."
                    : "The production registry must expose visible meshes.");
            return lowestY;
        }

        private static bool ContainsBoneName(
            IReadOnlyList<string> boneNames,
            string candidate)
        {
            for (int index = 0; index < boneNames.Count; index++)
            {
                if (boneNames[index] == candidate)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
