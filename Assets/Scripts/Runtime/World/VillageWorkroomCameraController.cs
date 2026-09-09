using System;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageWorkroomCameraShot { Outside, MainRoom, PrivatePassage, PrivateRoom, MainRoomEntrance }

    /// <summary>
    /// The same fixed interior framing used at home, entered through this
    /// house's real threshold. Only the lens and bounded aim ease; a camera
    /// never travels through the solid facade to reach its interior anchor.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class VillageWorkroomCameraController : MonoBehaviour
    {
        private PlayerCameraFollow follow;
        private Transform player;
        private VillageWorkroomPlan plan;
        private bool inside, published;
        private Vector3 publishedPosition;
        private Quaternion publishedRotation;
        private float publishedFieldOfView, zoomVelocity;
        private FixedCameraZoom zoom;
        private FixedCameraFocus focus;

        public VillageWorkroomCameraShot CurrentShot { get; private set; }
        public bool OwnsCamera => published && MatchesPublishedPose();

        public void Initialize(PlayerCameraFollow cameraFollow, Transform target, VillageWorkroomPlan roomPlan)
        {
            follow = cameraFollow ?? throw new ArgumentNullException(nameof(cameraFollow));
            player = target ?? throw new ArgumentNullException(nameof(target));
            plan = roomPlan ?? throw new ArgumentNullException(nameof(roomPlan));
        }

        private void LateUpdate()
        {
            // A disabled follow belongs to an explicit inspection viewpoint.
            // Do not even clear a fixed pose while that owner is inspecting it.
            if (follow == null || !follow.enabled || player == null || plan == null) return;
            Vector3 local = plan.Local(player.position);
            inside = plan.ContainsInterior(player.position) || (inside && InExitMargin(local));
            if (!inside)
            {
                ReleaseOwnedCamera();
                CurrentShot = VillageWorkroomCameraShot.Outside;
                return;
            }

            // A contextual owner can replace the fixed pose. Its eventual
            // restoration of our exact base pose, or release to free follow,
            // is the only permission to resume framing this room.
            if (follow.FixedPoseActive && !MatchesPublishedPose()) return;

            VillageWorkroomCameraShot shot = SelectShot(local);
            if (!OwnsCamera || shot != CurrentShot)
                ApplyShot(shot);
            else if (!follow.FixedFocusActive)
                follow.SetFixedFocus(focus);

            float desired = zoom.Resolve(publishedPosition, follow.FixedFocusedRotation,
                publishedFieldOfView, player.position);
            publishedFieldOfView = Mathf.SmoothDamp(publishedFieldOfView, desired,
                ref zoomVelocity, zoom.SmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            follow.SetFixedFieldOfView(publishedFieldOfView);
        }

        private bool InExitMargin(Vector3 p)
        {
            const float margin = .12f;
            return plan.Contains(player.position, margin) ||
                (p.y >= -.10f - margin && p.y <= 2.30f + margin &&
                 ((p.x >= 1.20f - margin && p.x <= 3.50f + margin &&
                   p.z >= -2.27f - margin && p.z <= 1.40f + margin) ||
                  (Mathf.Abs(p.x - plan.House.DoorAcrossOffset) <= .52f + margin &&
                   p.z >= 2.53f - margin && p.z <= 2.79f + margin)));
        }

        private VillageWorkroomCameraShot SelectShot(Vector3 p)
        {
            bool wasPrivate = CurrentShot == VillageWorkroomCameraShot.PrivatePassage ||
                CurrentShot == VillageWorkroomCameraShot.PrivateRoom;
            if (p.x < (wasPrivate ? 1.38f : 1.50f))
            {
                // Both anchors stand clear of the cabinet. Use the opposite
                // one near the entrance so the door operator cannot approach
                // the lens; the overlap prevents cuts while pacing at centre.
                bool entranceView = CurrentShot == VillageWorkroomCameraShot.MainRoomEntrance;
                return p.z > (entranceView ? -.15f : .15f)
                    ? VillageWorkroomCameraShot.MainRoomEntrance : VillageWorkroomCameraShot.MainRoom;
            }
            // The opposite anchor is used near the back of the private pocket,
            // keeping a visitor from walking into the camera by the hidden docks.
            bool backView = CurrentShot == VillageWorkroomCameraShot.PrivateRoom;
            return p.z > (backView ? -.30f : 0f)
                ? VillageWorkroomCameraShot.PrivateRoom : VillageWorkroomCameraShot.PrivatePassage;
        }

        private void ApplyShot(VillageWorkroomCameraShot shot)
        {
            Vector3 position, target;
            float minimumFov, maximumFov;
            if (shot == VillageWorkroomCameraShot.MainRoom || shot == VillageWorkroomCameraShot.MainRoomEntrance)
            {
                bool entranceView = shot == VillageWorkroomCameraShot.MainRoomEntrance;
                position = entranceView ? new Vector3(.80f, 2.10f, -1.80f) : new Vector3(.10f, 2.10f, 1.60f);
                target = new Vector3(-1.45f, .90f, entranceView ? 1f : -.65f);
                minimumFov = 75f; maximumFov = 85f;
            }
            else
            {
                bool farPocket = shot == VillageWorkroomCameraShot.PrivateRoom;
                position = new Vector3(2.80f, 2.12f, farPocket ? -2.02f : 1.18f);
                target = new Vector3(2.45f, .90f, farPocket ? .70f : -1.10f);
                minimumFov = 78f; maximumFov = 90f;
            }
            publishedPosition = plan.World(position);
            publishedRotation = Quaternion.LookRotation(plan.World(target) - publishedPosition, Vector3.up);
            zoom = new FixedCameraZoom(.55f, minimumFov, maximumFov, .45f);
            focus = FixedCameraFocus.Bounded(35f, 30f);
            publishedFieldOfView = zoom.Resolve(publishedPosition, publishedRotation, minimumFov, player.position);
            zoomVelocity = 0f;
            follow.SetFixedPose(publishedPosition, publishedRotation, publishedFieldOfView);
            follow.SetFixedFocus(focus);
            published = true;
            CurrentShot = shot;
        }

        private bool MatchesPublishedPose() => published && follow != null && follow.FixedPoseActive &&
            (follow.FixedBasePosition - publishedPosition).sqrMagnitude < .000001f &&
            Quaternion.Angle(follow.FixedBaseRotation, publishedRotation) < .01f &&
            Mathf.Abs(follow.FixedBaseFieldOfView - publishedFieldOfView) < .01f;

        private void ReleaseOwnedCamera()
        {
            if (OwnsCamera && follow.enabled) follow.ClearFixedPose();
        }

        private void OnDisable() => ReleaseOwnedCamera();
        private void OnDestroy() => ReleaseOwnedCamera();
    }
}
