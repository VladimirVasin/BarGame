using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Plays the staged Pipeback Roller loops and turns its mechanism from
    /// the distance actually covered.
    ///
    /// This deliberately does not reuse <see cref="CityPedestrianPresentation"/>:
    /// that one grounds a walker by its shoe soles, and a wheelchair has to
    /// sit on its wheels. The staged prefab also ships no AnimatorController,
    /// so the clips run through a small manual PlayableGraph.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class YardWheelchairPresentation : MonoBehaviour
    {
        public const float RollBlendDuration = 0.35f;

        /// <summary>
        /// The baked FBX axis conversion leaves the axle on local X
        /// with a positive spin rolling backwards; this sign turns the
        /// covered ground into forward wheel travel.
        /// </summary>
        public const float RollSign = -1f;

        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable rollPlayable;
        private CityWheelchairNpcAssetRegistry registry;
        private CityPedestrianAssetRegistry pedestrianRegistry;
        private Quaternion leftWheelRest;
        private Quaternion rightWheelRest;
        private Quaternion leftCasterRest;
        private Quaternion rightCasterRest;
        private Vector3 bellowsRest;
        private Quaternion pipeBankRest;
        private float rollWeight;

        public bool IsInitialized { get; private set; }

        public void Initialize(
            CityWheelchairNpcAssetRegistry wheelchairRegistry)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The yard wheelchair presentation is already initialized.");
            }

            registry = wheelchairRegistry != null
                ? wheelchairRegistry
                : throw new ArgumentNullException(
                    nameof(wheelchairRegistry));
            pedestrianRegistry = registry.PedestrianRegistry;
            if (pedestrianRegistry == null ||
                pedestrianRegistry.Animator == null ||
                pedestrianRegistry.IdleClip == null ||
                pedestrianRegistry.WalkClip == null)
            {
                throw new InvalidOperationException(
                    "The staged wheelchair prefab requires a pedestrian " +
                    "registry with an Animator and both loops.");
            }

            BindMechanismParts();
            CaptureRestPoses();

            Animator animator = pedestrianRegistry.Animator;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.cullingMode =
                AnimatorCullingMode.CullUpdateTransforms;

            graph = PlayableGraph.Create("Yard Wheelchair");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            idlePlayable = AnimationClipPlayable.Create(
                graph,
                pedestrianRegistry.IdleClip);
            rollPlayable = AnimationClipPlayable.Create(
                graph,
                pedestrianRegistry.WalkClip);
            idlePlayable.SetApplyFootIK(false);
            idlePlayable.SetApplyPlayableIK(false);
            rollPlayable.SetApplyFootIK(false);
            rollPlayable.SetApplyPlayableIK(false);
            graph.Connect(idlePlayable, 0, mixer, 0);
            graph.Connect(rollPlayable, 0, mixer, 1);
            AnimationPlayableOutput output =
                AnimationPlayableOutput.Create(
                    graph,
                    "Yard Wheelchair Pose",
                    animator);
            output.SetSourcePlayable(mixer);
            ApplyWeights();
            graph.Evaluate(0f);
            IsInitialized = true;
        }

        /// <summary>
        /// Advances the loops and the mechanism. <paramref name="distance"/>
        /// is the total distance ridden, so the wheels stay locked to the
        /// ground however the speed changes.
        /// </summary>
        public void Advance(
            float deltaTime,
            YardWheelchairPose pose,
            float distance,
            bool moving)
        {
            if (!IsInitialized)
            {
                return;
            }

            float step = deltaTime > 0f &&
                         !float.IsNaN(deltaTime) &&
                         !float.IsInfinity(deltaTime)
                ? Mathf.Min(deltaTime, 0.1f)
                : 0f;
            float target = moving ? 1f : 0f;
            rollWeight = Mathf.MoveTowards(
                rollWeight,
                target,
                step / RollBlendDuration);
            ApplyWeights();
            // The arm loop chases the ragged push rhythm: it races
            // through the stroke and dawdles through the coast.
            rollPlayable.SetSpeed(moving
                ? Mathf.Clamp(
                    pose.Speed / YardWheelchairMotion.CruiseSpeed,
                    0.30f,
                    1.60f)
                : 0d);
            graph.Evaluate(step);
            ApplyMechanism(pose, distance);
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (graph.IsValid())
            {
                graph.Destroy();
            }

            IsInitialized = false;
            rollWeight = 0f;
            registry = null;
            pedestrianRegistry = null;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        /// <summary>
        /// Adopts the static chair meshes under their authored pivots.
        /// The exporter deliberately ships them beside the empties (an
        /// FBX skinned mesh parented through an auxiliary Empty picks
        /// up a second unit conversion), each mesh with its origin on
        /// the pivot — so a runtime reparent is exact, and turning a
        /// pivot finally turns real geometry.
        /// </summary>
        private void BindMechanismParts()
        {
            Transform modelRoot =
                pedestrianRegistry.ModelRoot != null
                    ? pedestrianRegistry.ModelRoot
                    : registry.transform;
            Transform[] children =
                modelRoot.GetComponentsInChildren<Transform>(true);
            Adopt(children, registry.LeftWheelPivot,
                "ACC_WheelTyre.L", "ACC_PushRim.L", "ACC_WheelSpokes.L");
            Adopt(children, registry.RightWheelPivot,
                "ACC_WheelTyre.R", "ACC_PushRim.R", "ACC_WheelSpokes.R");
            Adopt(children, registry.LeftCasterPivot,
                "ACC_CasterTyre.L", "ACC_CasterHub.L");
            Adopt(children, registry.RightCasterPivot,
                "ACC_CasterTyre.R", "ACC_CasterHub.R");
        }

        private static void Adopt(
            Transform[] children,
            Transform pivot,
            params string[] partNames)
        {
            if (pivot == null)
            {
                return;
            }

            for (int index = 0; index < children.Length; index++)
            {
                Transform child = children[index];
                if (child == null || child.parent == pivot)
                {
                    continue;
                }

                for (int nameIndex = 0;
                     nameIndex < partNames.Length;
                     nameIndex++)
                {
                    if (string.Equals(
                            child.name,
                            partNames[nameIndex],
                            StringComparison.Ordinal))
                    {
                        child.SetParent(pivot, true);
                        break;
                    }
                }
            }
        }

        private void CaptureRestPoses()
        {
            if (registry.LeftWheelPivot != null)
            {
                leftWheelRest = registry.LeftWheelPivot.localRotation;
            }

            if (registry.RightWheelPivot != null)
            {
                rightWheelRest = registry.RightWheelPivot.localRotation;
            }

            if (registry.LeftCasterPivot != null)
            {
                leftCasterRest = registry.LeftCasterPivot.localRotation;
            }

            if (registry.RightCasterPivot != null)
            {
                rightCasterRest = registry.RightCasterPivot.localRotation;
            }

            if (registry.BellowsPivot != null)
            {
                bellowsRest = registry.BellowsPivot.localPosition;
            }

            if (registry.PipeBankPivot != null)
            {
                pipeBankRest = registry.PipeBankPivot.localRotation;
            }
        }

        private void ApplyWeights()
        {
            if (!mixer.IsValid())
            {
                return;
            }

            mixer.SetInputWeight(0, 1f - rollWeight);
            mixer.SetInputWeight(1, rollWeight);
        }

        private void ApplyMechanism(
            YardWheelchairPose pose,
            float distance)
        {
            // The drive wheels turn about their own local X, which is the
            // axle in the authored rest pose.
            if (registry.LeftWheelPivot != null)
            {
                registry.LeftWheelPivot.localRotation =
                    leftWheelRest *
                    Quaternion.Euler(
                        RollSign * pose.LeftWheelSpin,
                        0f,
                        0f);
            }

            if (registry.RightWheelPivot != null)
            {
                registry.RightWheelPivot.localRotation =
                    rightWheelRest *
                    Quaternion.Euler(
                        RollSign * pose.RightWheelSpin,
                        0f,
                        0f);
            }

            // Casters trail the slide: they are dragged round to point
            // where the chair is actually going, not where it faces.
            float casterYaw = -pose.SlipDegrees;
            float casterSpin = RollSign * distance /
                               YardWheelchairMotion.WheelRadius *
                               Mathf.Rad2Deg * 1.6f;
            if (registry.LeftCasterPivot != null)
            {
                registry.LeftCasterPivot.localRotation =
                    leftCasterRest *
                    Quaternion.Euler(casterSpin, casterYaw, 0f);
            }

            if (registry.RightCasterPivot != null)
            {
                registry.RightCasterPivot.localRotation =
                    rightCasterRest *
                    Quaternion.Euler(casterSpin, casterYaw, 0f);
            }

            // The bellows works with every push: it squeezes through
            // the stroke and reinflates on the coast, locked to the
            // same ground the wheels ride.
            if (registry.BellowsPivot != null)
            {
                float pump = Mathf.Sin(
                    pose.PushPhase * Mathf.PI * 2f) * 0.022f;
                registry.BellowsPivot.localPosition =
                    bellowsRest + new Vector3(0f, pump, 0f);
            }

            if (registry.PipeBankPivot != null)
            {
                float rock = Mathf.Sin(distance * 0.8f) * 2.2f;
                registry.PipeBankPivot.localRotation =
                    pipeBankRest *
                    Quaternion.Euler(0f, 0f, rock - pose.SlipDegrees * 0.08f);
            }
        }
    }
}
