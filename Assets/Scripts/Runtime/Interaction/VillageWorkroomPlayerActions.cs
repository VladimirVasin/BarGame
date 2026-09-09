using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Optional bone-only bank on the live hero; never rewrites its prefab.</summary>
    public static class VillageWorkroomPlayerActions
    {
        public const string ResourcePath = "Player/VillageWorkroomPlayerActions";
        public const float TransferSeconds = 1.5f;
        public const float HoldSeconds = 6f;
        public static readonly Vector3 LeftGripFromGround = new Vector3(-.22f, 1f, .42f);
        public static readonly Vector3 RightGripFromGround = new Vector3(.22f, 1f, .42f);
        public static readonly string[] RequiredClipNames =
            { "VillageChairHelpEnter", "VillageChairHelpLoop", "VillageChairHelpExit" };

        public static PlayerAnimatedInteractionDefinition CreateDefinition() =>
            new PlayerAnimatedInteractionDefinition(RequiredClipNames[0], RequiredClipNames[1], RequiredClipNames[2],
                enterFrameCount: 36, enterFramesPerSecond: 24f,
                loopFrameCount: 144, loopFramesPerSecond: 24f,
                exitFrameCount: 36, exitFramesPerSecond: 24f);

        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null) return false;
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>(ResourcePath);
            var bindings = new List<Player3DAnimationBinding>(registry.Animations);
            foreach (string name in RequiredClipNames)
            {
                if (registry.TryGetAnimation(name, out _)) continue;
                AnimationClip clip = clips.FirstOrDefault(candidate => candidate.name == name);
                bool loop = name == RequiredClipNames[1];
                float duration = loop ? HoldSeconds : TransferSeconds;
                if (clip == null || clip.isLooping != loop || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - duration) > .003f) return false;
                bindings.Add(new Player3DAnimationBinding(name, "village_workroom", clip, duration, loop));
            }
            registry.Configure(registry.Animator, registry.ModelRoot, registry.Renderers.ToArray(),
                registry.MeshBindings.ToArray(), registry.AnatomicalParts.ToArray(), bindings.ToArray(),
                registry.Anchors, registry.Metrics, registry.SourceGeneratorVersion, registry.SourcePose,
                registry.SourceTriangleCount, registry.BuildSignature, registry.FaceAtlas);
            return true;
        }
    }

    /// <summary>Small contact correction after the authored frame; bone lengths never change.</summary>
    internal sealed class VillageWorkroomHandContacts
    {
        private readonly Transform actor;
        private readonly Arm right, left;
        public Transform RightGrip => right.Grip;
        public Transform LeftGrip => left.Grip;
        public float RightWristDistance => right.WristDistance;
        public float RightReachLimit => right.ReachLimit;

        internal static void RefreshPresentation(PlayerRuntime player, PlayerAnimatedInteractionController controller)
        {
            // The normal late pass and a batch capture use this same no-time
            // seam. Sampling in Update has just restored the authored bones.
            (player.Visual as Player3DCharacterPresentation)?.ReapplyLatePresentationPose();
            controller?.RefreshActiveClipAlignment();
        }
        public VillageWorkroomHandContacts(Transform playerRoot, Player3DAssetRegistry registry)
        {
            actor = playerRoot;
            right = new Arm(registry, "R", registry.Anchors.RightGrip);
            left = new Arm(registry, "L", registry.Anchors.LeftGrip);
        }
        public void Apply(Vector3? rightTarget, Vector3? leftTarget, float weight)
        {
            if (rightTarget.HasValue) Solve(right, rightTarget.Value, weight, false);
            if (leftTarget.HasValue) Solve(left, leftTarget.Value, weight, true);
        }
        private void Solve(Arm arm, Vector3 target, float weight, bool isLeft)
        {
            target = Vector3.Lerp(arm.Grip.position, target, Mathf.Clamp01(weight));
            Quaternion rotation = arm.Hand.rotation;
            Vector3 wrist = target - (arm.Grip.position - arm.Hand.position);
            Vector3 shoulder = arm.Upper.position;
            float a = Vector3.Distance(shoulder, arm.Forearm.position), b = Vector3.Distance(arm.Forearm.position, arm.Hand.position);
            Vector3 delta = wrist - shoulder;
            arm.WristDistance = delta.magnitude; arm.ReachLimit = a + b - .001f;
            if (delta.sqrMagnitude < .000001f) return;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 axis = delta.normalized;
            wrist = shoulder + axis * distance;
            Vector3 bend = Vector3.ProjectOnPlane(actor.right * (isLeft ? -1f : 1f) - actor.forward * .15f, axis).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 elbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            arm.Upper.rotation = Quaternion.FromToRotation(arm.Forearm.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            arm.Forearm.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Forearm.position, wrist - arm.Forearm.position) * arm.Forearm.rotation;
            arm.Hand.rotation = rotation;
        }
        private sealed class Arm
        {
            public readonly Transform Upper, Forearm, Hand, Grip;
            public float WristDistance, ReachLimit;
            public Arm(Player3DAssetRegistry registry, string suffix, Transform grip)
            {
                Upper = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "upper_arm." + suffix);
                Forearm = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "forearm." + suffix);
                Hand = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "hand." + suffix);
                Grip = grip;
                if (Upper == null || Forearm == null || Hand == null || Grip == null)
                    throw new InvalidOperationException("Workroom contacts require the complete production Generic arm.");
            }
        }
    }
}
