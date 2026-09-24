using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Independent grounded endpoints and authored prop/camera anchors.</summary>
    public sealed class LodgeStovePlan
    {
        public const float WarmRadius = 2.5f;
        public const float CameraSeconds = .8f;
        public const float DoorSeconds = .75f;
        public const float PlaceSeconds = 1.05f;
        public const float IgnitionSeconds = 3.8f;
        public const float FirstClickSeconds = 1.0f;
        public const float SecondClickSeconds = 1.65f;
        public const float ThirdClickSeconds = 2.3f;
        public const float DoorAngle = 105f;

        public LodgeStovePlan(Transform lodge)
        {
            Lodge = lodge != null ? lodge : throw new ArgumentNullException(nameof(lodge));
            Door = Require(lodge, "StoveDoorHinge");
            Handle = Require(lodge, "StoveHandleGrip");
            LogDock = Require(lodge, "StoveLogDock");
            FireDock = Require(lodge, "StoveFireDock");
            LighterDock = Require(lodge, "StoveLighterDock");
            CameraDock = Require(lodge, "StoveCameraDock");
            EntryPose = PoseAt(Require(lodge, "StoveEntryDock"));
            ExitPose = PoseAt(Require(lodge, "StoveExitDock"));
        }

        public Transform Lodge { get; }
        public Transform Door { get; }
        public Transform Handle { get; }
        public Transform LogDock { get; }
        public Transform FireDock { get; }
        public Transform LighterDock { get; }
        public Transform CameraDock { get; }
        public PlayerAnimatedInteractionPose EntryPose { get; }
        public PlayerAnimatedInteractionPose ExitPose { get; }
        public Vector3 CameraTarget => LogDock.position + Lodge.up * .19f + Lodge.right * .12f;

        // The world rig holds ordinary neutral support while props move without
        // rendered hands, as requested. Approach/terminal sampling
        // and input restoration still belong to the shared action controller.
        public static PlayerAnimatedInteractionDefinition CreateDefinition() =>
            new PlayerAnimatedInteractionDefinition("Idle", "Idle", "Idle",
                2, 12f, 16, 8f, 2, 12f);

        private PlayerAnimatedInteractionPose PoseAt(Transform anchor)
        {
            Vector3 root = anchor.position + Vector3.up * PlayerFactory.GroundedRootOffset;
            return new PlayerAnimatedInteractionPose(root, Lodge.rotation,
                PlayerCharacterDimensions.GetUprightPelvisPosition(root));
        }

        internal static Transform Require(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException("Missing stove anchor: " + name);
        }
    }
}
