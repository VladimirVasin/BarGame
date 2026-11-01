using UnityEngine;

namespace BarPromenade
{
    /// <summary>Independent grounded docks and a finite same-axis lens path for one authored reward.</summary>
    public sealed class StairwellPickupCinematicPlan
    {
        public InventoryItemId ItemId { get; }
        public PlayerAnimatedInteractionPose EntryPose { get; }
        public PlayerAnimatedInteractionPose ExitPose { get; }
        public Vector3 ActionHipPosition { get; }
        public Vector3 GroundPosition { get; }
        public Vector3 SourceContact { get; }
        public Quaternion Facing { get; }
        public const float CameraReturnSeconds = .28f;

        public StairwellPickupCinematicPlan(InventoryItemId item, Vector3 sourceContact,
            float floorHeight, Vector3 forward)
        {
            ItemId = item; SourceContact = sourceContact;
            Facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up).normalized, Vector3.up);
            Vector3 reach = PlayerStairwellPickupActions.ReachFromGround(item);
            Vector3 ground = sourceContact - Facing * new Vector3(reach.x, 0f, reach.z);
            ground.y = floorHeight;
            GroundPosition = ground;
            Vector3 entry = ground + Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 exit = ground + Vector3.up * PlayerFactory.GroundedRootOffset;
            EntryPose = new PlayerAnimatedInteractionPose(entry, Facing, PlayerCharacterDimensions.GetUprightPelvisPosition(entry));
            ExitPose = new PlayerAnimatedInteractionPose(exit, Facing, PlayerCharacterDimensions.GetUprightPelvisPosition(exit));
            ActionHipPosition = EntryPose.HipPosition;
        }

        public Vector3 Point(Vector3 local) => GroundPosition + Facing * local;

        public void Shot(float progress, out Pose pose, out Vector3 focus, out float fieldOfView, int side = 1)
        {
            float t = Mathf.SmoothStep(0f, 1f, progress);
            Vector3 start, end, target;
            float roll = 0f;
            if (ItemId == InventoryItemId.ChestFlashlight)
            {
                start = new Vector3(.90f * side, 1.48f, -.55f);
                end = new Vector3(.86f * side, 1.43f, -.68f);
                target = new Vector3(.08f, 1.26f, .28f);
            }
            else if (ItemId == InventoryItemId.CombatCrowbar)
            {
                start = new Vector3(.92f * side, .91f, -.58f);
                end = new Vector3(.98f * side, 1.19f, -.66f);
                target = new Vector3(.10f, 1.08f, .28f);
                roll = Mathf.Sin(Mathf.InverseLerp(.12f, .48f, progress) * Mathf.PI) * 2.5f * side;
            }
            else
            {
                start = new Vector3(.96f * side, 1.16f, -.18f);
                end = new Vector3(.94f * side, 1.30f, -.65f);
                target = new Vector3(.10f, 1.09f, .30f);
            }
            focus = Point(target);
            Vector3 position = Point(Vector3.Lerp(start, end, t));
            pose = new Pose(position, Quaternion.LookRotation(focus - position, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
            fieldOfView = Mathf.Lerp(54f, 46f, Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI));
        }
    }
}
