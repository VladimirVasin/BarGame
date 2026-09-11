using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A pair of conversation shots on one side of the participants' axis.
    /// The dialogue session owns input and calls Tick after both rigs have
    /// sampled their animation. This owner only borrows the existing camera.
    /// </summary>
    public sealed class DialogueCameraDirector
    {
        public const float EntrySeconds = .3f;
        public const float ExitSeconds = .3f;
        private const float FrameHeightMeters = 1.2f;
        private const float CameraClearanceMeters = .16f;
        private const float SightClearanceMeters = .035f;
        private const float AimResponseSeconds = .1f;

        private readonly Collider[] overlaps = new Collider[48];
        private readonly RaycastHit[] hits = new RaycastHit[48];
        private PlayerCameraFollow follow;
        private Camera camera;
        private Transform npcRoot, npcHead, heroRoot, heroHead;
        private Vector3 axis, side;
        private float npcHeadHeight, heroHeadHeight;
        private bool active, entering, returning, previousFixed;
        private float elapsed, previousFixedFov, blendStartFov;
        private Pose previousFixedPose, blendStartPose;
        private FixedCameraFocus previousFocus;
        private int preferredVariant;

        public bool IsReturning => active && returning;
        public bool IsFinished => !active;
        public bool IsSettled => active && !entering && !returning;
        public bool CurrentSpeakerIsHero { get; private set; }
        public bool CurrentShotIsClear { get; private set; }
        public Pose CurrentShotPose { get; private set; }
        public float CurrentShotFieldOfView { get; private set; }

        /// <summary>
        /// Call after the visible approach and neutral settle. False leaves
        /// the camera untouched, including a cinematic effect owned elsewhere.
        /// </summary>
        public bool Begin(PlayerCameraFollow cameraFollow, Transform speakerRoot,
            Transform speakerHead, Transform playerRoot, Transform playerHead)
        {
            if (active || cameraFollow == null || !cameraFollow.isActiveAndEnabled ||
                cameraFollow.Camera == null || speakerRoot == null || speakerHead == null ||
                playerRoot == null || playerHead == null || CinematicDepthOfField.IsActive)
                return false;

            Vector3 separation = Vector3.ProjectOnPlane(
                playerRoot.position - speakerRoot.position, Vector3.up);
            if (separation.sqrMagnitude < .25f) return false;
            follow = cameraFollow; camera = cameraFollow.Camera;
            npcRoot = speakerRoot; npcHead = speakerHead;
            heroRoot = playerRoot; heroHead = playerHead;
            axis = separation.normalized;
            side = Vector3.Cross(Vector3.up, axis);
            npcHeadHeight = npcHead.position.y - npcRoot.position.y;
            heroHeadHeight = heroHead.position.y - heroRoot.position.y;

            // Pick a SIDE for the whole exchange, rather than independently
            // flipping each close-up across the line between the two people.
            bool found = false;
            for (int variant = 0; variant < 3 && !found; variant++)
            {
                for (int direction = 0; direction < 2 && !found; direction++)
                {
                    side = Vector3.Cross(Vector3.up, axis) * (direction == 0 ? 1f : -1f);
                    found = TryResolveShot(false, variant, out _, out _) &&
                        TryResolveShot(true, variant, out _, out _);
                    if (found) preferredVariant = variant;
                }
            }
            if (!found)
            {
                ClearReferences();
                return false;
            }

            previousFixed = follow.FixedPoseActive;
            previousFixedPose = follow.FixedBasePose;
            previousFixedFov = follow.FixedBaseFieldOfView;
            previousFocus = follow.FixedFocus;
            blendStartPose = new Pose(camera.transform.position, camera.transform.rotation);
            blendStartFov = camera.fieldOfView;
            CurrentSpeakerIsHero = false;
            active = entering = true; returning = false; elapsed = 0f;
            ResolveCurrentShot(out Pose shot, out float fov);
            CurrentShotPose = shot; CurrentShotFieldOfView = fov;
            // Acquire at the exact live pose; the first Tick starts the move.
            follow.SetFixedPose(blendStartPose.position, blendStartPose.rotation, blendStartFov);
            CinematicDepthOfField.Begin(Vector3.Distance(camera.transform.position, npcHead.position), 5.6f, 45f);
            return true;
        }

        /// <summary>A motivated cut on speaker change; no camera orbit through the other actor.</summary>
        public void Focus(bool hero)
        {
            if (!active || returning || CurrentSpeakerIsHero == hero) return;
            if (!ParticipantsExist()) { RestoreImmediate(); return; }
            CurrentSpeakerIsHero = hero;
            entering = false;
            ResolveCurrentShot(out Pose shot, out float fov);
            Apply(shot, fov);
            UpdateFocusDistance();
        }

        public void Tick(float deltaTime)
        {
            if (!active) return;
            if (!ParticipantsExist()) { RestoreImmediate(); return; }
            float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                ? 0f : Mathf.Max(0f, deltaTime);
            // The modal input lease does not stop the world clock. A real
            // pause still freezes camera transitions just like actor motion.
            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) dt = 0f;

            if (returning)
            {
                elapsed += dt;
                Pose destination = ResolveReturnPose();
                float fov = previousFixed ? previousFixedFov : follow.FollowFieldOfView;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / ExitSeconds));
                Apply(Blend(blendStartPose, destination, t), Mathf.Lerp(blendStartFov, fov, t));
                if (elapsed >= ExitSeconds) RestoreImmediate();
                return;
            }

            ResolveCurrentShot(out Pose target, out float targetFov);
            if (entering)
            {
                elapsed += dt;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / EntrySeconds));
                Pose candidate = Blend(blendStartPose, target, t);
                // A blend may cross a warehouse corner even though both end
                // shots are clear. Keep its lens outside geometry while the
                // visible hero remains at the already-reached conversation dock.
                candidate = ConstrainBlend(candidate, target);
                Apply(candidate, Mathf.Lerp(blendStartFov, targetFov, t));
                if (elapsed >= EntrySeconds) entering = false;
            }
            else
            {
                // Fixed roots hold the lens still; small animated head motion
                // only adjusts the aim, without turning breathing into shake.
                float response = 1f - Mathf.Exp(-dt / AimResponseSeconds);
                Pose adjusted = new Pose(target.position,
                    Quaternion.Slerp(CurrentShotPose.rotation, target.rotation, response));
                Apply(adjusted, targetFov);
            }
            UpdateFocusDistance();
        }

        public void BeginExit()
        {
            if (!active || returning) return;
            if (!ParticipantsExist()) { RestoreImmediate(); return; }
            blendStartPose = new Pose(camera.transform.position, camera.transform.rotation);
            blendStartFov = camera.fieldOfView;
            entering = false; returning = true; elapsed = 0f;
            CinematicDepthOfField.End();
        }

        /// <summary>Idempotent cancellation, disable, destruction and scene-exit cleanup.</summary>
        public void RestoreImmediate()
        {
            if (!active) return;
            active = entering = returning = false;
            if (follow != null)
            {
                if (previousFixed)
                {
                    follow.SetFixedPose(previousFixedPose.position, previousFixedPose.rotation, previousFixedFov);
                    if (previousFocus.Enabled) follow.SetFixedFocus(previousFocus);
                }
                else follow.ClearFixedPose();
            }
            CinematicDepthOfField.EndImmediately();
            CurrentShotIsClear = false;
            ClearReferences();
        }

        private bool ResolveCurrentShot(out Pose pose, out float fov)
        {
            if (TryResolveShot(CurrentSpeakerIsHero, preferredVariant, out pose, out fov))
            {
                CurrentShotIsClear = true;
                return true;
            }
            for (int variant = 0; variant < 3; variant++)
            {
                if (variant == preferredVariant) continue;
                if (!TryResolveShot(CurrentSpeakerIsHero, variant, out pose, out fov)) continue;
                CurrentShotIsClear = true;
                return true;
            }
            // Do not push the lens through a newly arrived obstacle or zoom
            // into a face. The session can cancel if the whole staging is lost.
            pose = CurrentShotPose; fov = CurrentShotFieldOfView;
            CurrentShotIsClear = false;
            return false;
        }

        private bool TryResolveShot(bool hero, int variant, out Pose pose, out float fov)
        {
            Transform root = hero ? heroRoot : npcRoot;
            Transform head = hero ? heroHead : npcHead;
            float height = hero ? heroHeadHeight : npcHeadHeight;
            // The reverse view fits BETWEEN the standing hero and the seated
            // NPC's wall. The wider variants move sideways, never behind him.
            float forward = hero ? 1.18f : 1.52f;
            float lateral = variant == 0 ? .9f : variant == 1 ? 1.22f : 1.55f;
            if (variant > 0) forward *= .88f;
            Vector3 position = root.position + Vector3.up * (height + .025f) +
                axis * (hero ? -forward : forward) + side * lateral;
            Vector3 bodyAim = head.position - Vector3.up * .12f;
            float distance = Vector3.Distance(position, bodyAim);
            float frameHeight = FrameHeightMeters + variant * .1f;
            fov = Mathf.Clamp(2f * Mathf.Atan(frameHeight / (2f * distance)) * Mathf.Rad2Deg, 28f, 48f);
            Quaternion centralAim = Quaternion.LookRotation(bodyAim - position, Vector3.up);
            // Face around 60% of frame height leaves its shared bubble above
            // and the silent answer panel below. Reciprocal look-room keeps
            // the two faces from staring into the same edge of the picture.
            float sideSign = Vector3.Dot(side, Vector3.Cross(Vector3.up, axis)) >= 0f ? 1f : -1f;
            float subjectX = .5f + (hero ? .06f : -.06f) * sideSign;
            float width = frameHeight * Mathf.Max(.5f, camera.aspect);
            Vector3 aim = bodyAim + centralAim * Vector3.right * ((.5f - subjectX) * width);
            pose = new Pose(position, Quaternion.LookRotation(aim - position, Vector3.up));
            return CameraPositionClear(position) &&
                SightClear(head.position + Vector3.up * .10f, position) &&
                SightClear(head.position - Vector3.up * .25f, position);
        }

        private Pose ConstrainBlend(Pose candidate, Pose target)
        {
            if (CameraPositionClear(candidate.position) &&
                SightClear(ActiveHead.position + Vector3.up * .1f, candidate.position)) return candidate;
            Vector3 origin = ActiveHead.position + Vector3.up * .1f;
            Vector3 direction = candidate.position - origin;
            float distance = direction.magnitude;
            if (distance > .01f)
            {
                int count = Physics.SphereCastNonAlloc(origin, CameraClearanceMeters, direction / distance,
                    hits, distance, follow.CollisionLayerMask, QueryTriggerInteraction.Ignore);
                float allowed = distance;
                for (int i = 0; i < count; i++)
                    if (IsSceneCollider(hits[i].collider))
                        allowed = Mathf.Min(allowed, Mathf.Max(0f, hits[i].distance - .05f));
                Vector3 constrained = origin + direction / distance * allowed;
                if (allowed >= 1f && CameraPositionClear(constrained))
                    return new Pose(constrained, candidate.rotation);
            }
            return target;
        }

        private bool CameraPositionClear(Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(position, CameraClearanceMeters, overlaps,
                follow.CollisionLayerMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (IsSceneCollider(overlaps[i])) return false;
            return true;
        }

        private bool SightClear(Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance < .01f) return false;
            int count = Physics.SphereCastNonAlloc(origin, SightClearanceMeters, delta / distance, hits,
                distance, follow.CollisionLayerMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++) if (IsSceneCollider(hits[i].collider)) return false;
            return true;
        }

        private bool IsSceneCollider(Collider collider)
        {
            if (collider == null) return false;
            Transform candidate = collider.transform;
            return candidate != npcRoot && !candidate.IsChildOf(npcRoot) &&
                candidate != heroRoot && !candidate.IsChildOf(heroRoot);
        }

        private Pose ResolveReturnPose()
        {
            if (!previousFixed) return follow.ResolveFollowPose(heroRoot.position);
            Quaternion rotation = previousFixedPose.rotation;
            if (previousFocus.Enabled)
            {
                Vector2 offset = previousFocus.Resolve(previousFixedPose.position, rotation,
                    previousFixedFov, camera.aspect, heroRoot.position);
                rotation = FixedCameraFocus.Compose(rotation, offset);
            }
            return new Pose(previousFixedPose.position, rotation);
        }

        private void Apply(Pose pose, float fov)
        {
            follow.SetFixedPose(pose.position, pose.rotation, fov);
            CurrentShotPose = pose; CurrentShotFieldOfView = fov;
        }

        private void UpdateFocusDistance() => CinematicDepthOfField.SetFocusDistance(
            Vector3.Distance(camera.transform.position, ActiveHead.position + Vector3.up * .1f));

        private Transform ActiveHead => CurrentSpeakerIsHero ? heroHead : npcHead;

        private bool ParticipantsExist() => follow != null && camera != null &&
            npcRoot != null && npcHead != null && heroRoot != null && heroHead != null &&
            npcRoot.gameObject.activeInHierarchy && heroRoot.gameObject.activeInHierarchy;

        private void ClearReferences()
        {
            follow = null; camera = null;
            npcRoot = npcHead = heroRoot = heroHead = null;
        }

        private static Pose Blend(Pose from, Pose to, float t) => new Pose(
            Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));
    }
}
