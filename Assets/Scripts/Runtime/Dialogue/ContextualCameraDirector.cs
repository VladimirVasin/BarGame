using BarPromenade.Rendering;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared camera loan, transitions, obstacle checks and focus for dialogue
    /// and object inspection. Sessions call Tick after the visible rig samples.
    /// </summary>
    public class ContextualCameraDirector
    {
        public const float EntrySeconds = .3f;
        public const float ExitSeconds = .3f;
        public const float InspectionEntrySeconds = 1.1f;
        public const float InspectionExitSeconds = .8f;
        private const float FrameHeightMeters = 1.2f;
        private const float CameraClearanceMeters = .16f;
        private const float SightClearanceMeters = .035f;
        private const float AimResponseSeconds = .1f;
        private const int ObjectVariantCount = 7;

        private readonly Collider[] overlaps = new Collider[48];
        private readonly RaycastHit[] hits = new RaycastHit[48];
        private readonly Plane[] objectFrustum = new Plane[6];
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
        private bool objectShot;
        private Bounds objectBounds;
        private CharacterController objectShotHeroBody;
        private NarrativeCameraMode objectMode;
        private Vector3 documentFront, documentUp;
        private Vector3 closeUpCenter, closeUpExtents, closeUpUp;
        private readonly List<Vector3> closeUpCorners = new List<Vector3>();
        private static ContextualCameraDirector activeOwner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwner() => activeOwner = null;

        public bool IsReturning => active && returning;
        public bool IsFinished => !active;
        public bool IsSettled => active && !entering && !returning;
        public bool CurrentSpeakerIsHero { get; private set; }
        public bool CurrentShotIsClear { get; private set; }
        public Pose CurrentShotPose { get; private set; }
        public float CurrentShotFieldOfView { get; private set; }
        /// <summary>Persists through cleanup so a failed approach remains diagnosable.</summary>
        public string LastRejectedShotReason { get; private set; } = string.Empty;

        /// <summary>
        /// Call after the visible approach and neutral settle. False leaves
        /// the camera untouched, including a cinematic effect owned elsewhere.
        /// </summary>
        public bool Begin(PlayerCameraFollow cameraFollow, Transform speakerRoot,
            Transform speakerHead, Transform playerRoot, Transform playerHead)
        {
            if (active || activeOwner != null || cameraFollow == null || !cameraFollow.isActiveAndEnabled ||
                cameraFollow.Camera == null || speakerRoot == null || speakerHead == null ||
                playerRoot == null || playerHead == null || CinematicDepthOfField.IsActive)
                return false;

            Vector3 separation = Vector3.ProjectOnPlane(
                playerRoot.position - speakerRoot.position, Vector3.up);
            if (separation.sqrMagnitude < .25f) return false;
            follow = cameraFollow; camera = cameraFollow.Camera;
            objectShot = false;
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

            return AcquireCamera();
        }

        /// <summary>Fits a real object from the hero's accessible side; no invented speaker or head.</summary>
        public bool BeginObject(PlayerCameraFollow cameraFollow, Transform subject, Bounds bounds,
            Transform playerRoot, Vector3 preferredSide, NarrativeCameraMode mode = NarrativeCameraMode.ObjectSide,
            Vector3 cameraFront = default)
        {
            LastRejectedShotReason = string.Empty;
            if (active || activeOwner != null || cameraFollow == null || !cameraFollow.isActiveAndEnabled || cameraFollow.Camera == null ||
                subject == null || playerRoot == null || CinematicDepthOfField.IsActive || bounds.size.sqrMagnitude < .0001f)
            { LastRejectedShotReason = "Camera, subject or camera ownership unavailable"; return false; }
            follow = cameraFollow; camera = cameraFollow.Camera;
            npcRoot = subject; heroRoot = playerRoot; objectBounds = bounds; objectShot = true;
            objectShotHeroBody = playerRoot.GetComponent<CharacterController>();
            objectMode = mode;
            documentFront = (cameraFront.sqrMagnitude > .01f ? cameraFront : subject.forward).normalized;
            documentUp = Vector3.ProjectOnPlane(Vector3.up, documentFront).normalized;
            if (documentUp.sqrMagnitude < .01f) documentUp = subject.up;
            if (objectMode == NarrativeCameraMode.ObjectCloseUp) MeasureObjectCloseUp(subject);
            Vector3 towardHero = Vector3.ProjectOnPlane(playerRoot.position - bounds.center, Vector3.up);
            axis = towardHero.sqrMagnitude > .01f ? towardHero.normalized : -subject.forward;
            Vector3 naturalSide = Vector3.Cross(Vector3.up, axis);
            if (Vector3.Dot(naturalSide, preferredSide) < 0f) naturalSide = -naturalSide;
            bool found = false;
            for (int variant = 0; variant < ShotVariantCount && !found; variant++)
                for (int direction = 0; direction < (IsCloseUpShot ? 1 : 2) && !found; direction++)
                {
                    side = naturalSide * (direction == 0 ? 1f : -1f);
                    found = TryResolveObjectShot(variant, out _, out _);
                    if (found) preferredVariant = variant;
                }
            if (!found) { ClearReferences(); return false; }
            return AcquireCamera();
        }

        private bool AcquireCamera()
        {
            CurrentSpeakerIsHero = false;
            if (!CinematicDepthOfField.TryBeginOwned(this, FocusDistance,
                IsCloseUpShot ? 11f : 5.6f, 45f,
                objectShot ? InspectionEntrySeconds : CinematicDepthOfField.BlendInSeconds,
                objectShot ? InspectionExitSeconds : CinematicDepthOfField.BlendOutSeconds,
                objectShot))
            { ClearReferences(); return false; }
            activeOwner = this;
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
            return true;
        }

        /// <summary>A motivated cut on speaker change; no camera orbit through the other actor.</summary>
        public void Focus(bool hero)
        {
            if (!active || objectShot || returning || CurrentSpeakerIsHero == hero) return;
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
                float seconds = objectShot ? InspectionExitSeconds : ExitSeconds;
                float t = TransitionProgress(elapsed / seconds);
                Apply(Blend(blendStartPose, destination, t), Mathf.Lerp(blendStartFov, fov, t));
                if (elapsed >= seconds) RestoreImmediate();
                return;
            }

            ResolveCurrentShot(out Pose target, out float targetFov);
            if (entering)
            {
                elapsed += dt;
                float seconds = objectShot ? InspectionEntrySeconds : EntrySeconds;
                float t = TransitionProgress(elapsed / seconds);
                Pose candidate = Blend(blendStartPose, target, t);
                // A blend may cross a warehouse corner even though both end
                // shots are clear. Keep its lens outside geometry while the
                // visible hero remains at the already-reached conversation dock.
                candidate = ConstrainBlend(candidate, target);
                Apply(candidate, Mathf.Lerp(blendStartFov, targetFov, t));
                if (elapsed >= seconds) entering = false;
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
            CinematicDepthOfField.EndOwned(this, false);
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
            CinematicDepthOfField.EndOwned(this, true);
            if (ReferenceEquals(activeOwner, this)) activeOwner = null;
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
            for (int variant = 0; variant < ShotVariantCount; variant++)
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
            if (objectShot) return TryResolveObjectShot(variant, out pose, out fov);
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

        private bool TryResolveObjectShot(int variant, out Pose pose, out float fov)
        {
            if (objectMode == NarrativeCameraMode.DocumentCloseUp) return TryResolveDocumentShot(variant, out pose, out fov);
            if (objectMode == NarrativeCameraMode.ObjectCloseUp) return TryResolveObjectCloseUpShot(variant, out pose, out fov);
            fov = 42f;
            // Around a small subject an over-the-shoulder angle makes the nearby
            // hero enormous. Start at fifty degrees and keep him beside the thing.
            // Large subjects already dwarf him and retain their wider frontal view.
            float lateral = variant == 0 ? (objectBounds.size.magnitude > 3.5f ? .52f : 1.2f) :
                variant == 1 ? 1.8f : variant == 2 ? 2.8f : variant == 3 ? .75f : variant == 4 ? 4f : variant == 5 ? 1.2f : 2.8f;
            Vector3 direction = (axis + side * lateral).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, -direction).normalized;
            Vector3 extents = objectBounds.extents;
            float width = 2f * (Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.z) * extents.z);
            float depth = Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.z) * extents.z;
            // Include the base and some surroundings, with the object above the lower reading panel.
            float frameHeight = Mathf.Max(2.2f, Mathf.Max(objectBounds.size.y * 1.55f,
                width * 1.3f / Mathf.Max(.75f, camera.aspect)));
            float distance = frameHeight / (2f * Mathf.Tan(fov * .5f * Mathf.Deg2Rad)) + depth;
            if (variant >= 5) distance *= 1.25f;
            Vector3 position = objectBounds.center + direction * distance + Vector3.up * Mathf.Min(1.35f, distance * .28f);
            position.y = Mathf.Max(position.y, heroRoot.position.y + 1.1f);
            Vector3 aim = objectBounds.center - Vector3.up * frameHeight * .1f;
            pose = new Pose(position, Quaternion.LookRotation(aim - position, Vector3.up));
            return HeroLeavesObjectClear(pose, fov) && CameraPositionClear(position) && SightClear(objectBounds.center, position) &&
                SightClear(objectBounds.center + Vector3.up * Mathf.Min(extents.y, .5f), position);
        }

        private bool TryResolveObjectCloseUpShot(int variant, out Pose pose, out float fov)
        {
            fov = variant == 0 ? 58f : variant == 1 ? 68f : 78f;
            float tangent = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            float aspect = Mathf.Max(.5f, camera.aspect);
            Vector3 right = Vector3.Cross(closeUpUp, documentFront).normalized;
            float distance = .2f;
            // Fit each real corner at its own depth. Adding the whole bounding
            // box depth puts a low oblique floor view unnecessarily far away.
            foreach (Vector3 corner in closeUpCorners)
            {
                Vector3 delta = corner - closeUpCenter;
                float x = Vector3.Dot(delta, right), y = Vector3.Dot(delta, closeUpUp);
                float z = Vector3.Dot(delta, documentFront);
                distance = Mathf.Max(distance, Mathf.Max(Mathf.Abs(x) / (.90f * tangent * aspect) + z,
                    Mathf.Max(y / (.92f * tangent) + z, -y / (.60f * tangent) + z)));
            }
            // The optical axis passes through the authored subject centre.
            // Keep the lower panel clear by fitting depth, never by panning
            // the object off-centre or translating the authored orbit sideways.
            Vector3 position = closeUpCenter + documentFront * distance;
            pose = new Pose(position, Quaternion.LookRotation(-documentFront, closeUpUp));
            float capsulePlaneSupport = 0f;
            if (!CloseUpCornersFit(pose, fov) || !HeroOutsideCloseUpFrame(pose, fov, out capsulePlaneSupport))
            {
                Bounds hero = HeroBounds();
                LastRejectedShotReason = "Hero remains in the object close-up; variant=" + variant +
                    " object=" + objectBounds.ToString("F3") + " fittedCenter=" + closeUpCenter.ToString("F3") +
                    " fittedExtents=" + closeUpExtents.ToString("F3") + " hero=" + hero.ToString("F3") +
                    " heroRect=" + ProjectBounds(hero, pose, fov).ToString("F3") +
                    " dock=" + heroRoot.position.ToString("F3") + " camera=" + position.ToString("F3") +
                    " front=" + documentFront.ToString("F3") + " up=" + closeUpUp.ToString("F3") +
                    " fov=" + fov.ToString("F2") + " aspect=" + camera.aspect.ToString("F3") +
                    " capsulePlaneSupport=" + capsulePlaneSupport.ToString("F3");
                return false;
            }
            return CameraPositionClear(pose.position) && SightClear(closeUpCenter, pose.position) &&
                HeroSightClear(pose.position, closeUpCenter);
        }

        private bool CloseUpCornersFit(Pose pose, float fov)
        {
            Quaternion inverse = Quaternion.Inverse(pose.rotation);
            float tangent = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            foreach (Vector3 corner in closeUpCorners)
            {
                Vector3 point = inverse * (corner - pose.position);
                if (point.z <= .1f) return false;
                float x = .5f + point.x / (2f * tangent * camera.aspect * point.z);
                float y = .5f + point.y / (2f * tangent * point.z);
                if (x < .03f || x > .97f || y < .195f || y > .965f) return false;
            }
            return true;
        }

        private void MeasureObjectCloseUp(Transform subject)
        {
            // Mesh-local corners retain the real surface dimensions. Projecting
            // an already rotated world AABB again can double a floor object's
            // frame and accidentally admit the hero standing alongside it.
            closeUpUp = Vector3.ProjectOnPlane(Vector3.up, documentFront).normalized;
            if (closeUpUp.sqrMagnitude < .01f)
            {
                Vector3 first = Vector3.ProjectOnPlane(subject.right, documentFront).normalized;
                if (first.sqrMagnitude < .01f)
                    first = Vector3.ProjectOnPlane(Vector3.right, documentFront).normalized;
                Vector3 second = Vector3.Cross(documentFront, first).normalized;
                Vector3 towardHero = Vector3.ProjectOnPlane(heroRoot.position - objectBounds.center, documentFront);
                closeUpUp = Mathf.Abs(Vector3.Dot(first, towardHero)) >= Mathf.Abs(Vector3.Dot(second, towardHero)) ? first : second;
                if (Vector3.Dot(closeUpUp, towardHero) < 0f) closeUpUp = -closeUpUp;
            }
            Vector3 right = Vector3.Cross(closeUpUp, documentFront).normalized;
            Vector3 minimum = Vector3.one * float.PositiveInfinity;
            Vector3 maximum = Vector3.one * float.NegativeInfinity;
            bool measured = false;
            closeUpCorners.Clear();
            foreach (MeshFilter filter in subject.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int index = 0; index < 8; index++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                    Vector3 world = filter.transform.TransformPoint(corner);
                    closeUpCorners.Add(world);
                    Vector3 projected = new Vector3(Vector3.Dot(world, right), Vector3.Dot(world, closeUpUp), Vector3.Dot(world, documentFront));
                    minimum = Vector3.Min(minimum, projected); maximum = Vector3.Max(maximum, projected);
                    measured = true;
                }
            }
            if (!measured)
            {
                closeUpCenter = objectBounds.center;
                closeUpExtents = new Vector3(ProjectExtent(right), ProjectExtent(closeUpUp), ProjectExtent(documentFront));
                for (int index = 0; index < 8; index++)
                    closeUpCorners.Add(objectBounds.center + Vector3.Scale(objectBounds.extents,
                        new Vector3((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f)));
                return;
            }
            closeUpCenter = objectBounds.center;
            closeUpExtents = (maximum - minimum) * .5f;
        }

        private bool HeroOutsideCloseUpFrame(Pose pose, float fov, out float minimumPlaneSupport)
        {
            minimumPlaneSupport = float.NaN;
            if (objectShotHeroBody == null)
            {
                Rect visible = Intersection(ProjectBounds(HeroBounds(), pose, fov), new Rect(0f, 0f, 1f, 1f));
                return visible.width * visible.height <= .0001f;
            }
            // A capsule's exact plane support avoids the phantom diagonal coat
            // corners introduced by expanding a world AABB. Retain the same
            // radial/vertical clothing allowance used by the ordinary shots.
            Transform body = objectShotHeroBody.transform;
            Vector3 scale = body.lossyScale;
            float radius = objectShotHeroBody.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, objectShotHeroBody.height * Mathf.Abs(scale.y) * .5f - radius) + .06f;
            Vector3 center = body.TransformPoint(objectShotHeroBody.center);
            Vector3 endA = center + body.up * halfSegment, endB = center - body.up * halfSegment;
            radius += .125f;
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse;
            GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(fov, camera.aspect, .01f, 1000f) * view, objectFrustum);
            minimumPlaneSupport = float.PositiveInfinity;
            foreach (Plane plane in objectFrustum)
            {
                float support = Mathf.Max(plane.GetDistanceToPoint(endA), plane.GetDistanceToPoint(endB)) + radius;
                minimumPlaneSupport = Mathf.Min(minimumPlaneSupport, support);
            }
            return minimumPlaneSupport < 0f;
        }

        private int ShotVariantCount => !objectShot || IsCloseUpShot ? 3 : ObjectVariantCount;

        private bool TryResolveDocumentShot(int variant, out Pose pose, out float fov)
        {
            // A paper uses its authored front, not the hero/prop axis. The lens
            // slips in front of the hero and approaches the sheet directly.
            Vector3 right = Vector3.Cross(documentUp, documentFront).normalized;
            float width = ProjectExtent(right) * 2f;
            float frameHeight = Mathf.Max(ProjectExtent(documentUp) * 3.7f, width * 1.4f / Mathf.Max(.75f, camera.aspect));
            Vector3 heroAtSubjectHeight = new Vector3(heroRoot.position.x, objectBounds.center.y, heroRoot.position.z);
            float heroFrontDistance = Vector3.Dot(heroAtSubjectHeight - objectBounds.center, documentFront);
            float distance = Mathf.Min(variant == 0 ? .85f : variant == 1 ? .68f : .54f,
                Mathf.Max(.45f, heroFrontDistance - .55f));
            // Straight-on optics keep the actual written sheet flat. Moving the
            // lens slightly below its centre leaves the bottom UI its own space.
            Vector3 aim = objectBounds.center - documentUp * frameHeight * .20f;
            Vector3 position = aim + documentFront * distance;
            fov = Mathf.Clamp(2f * Mathf.Atan(frameHeight / (2f * distance)) * Mathf.Rad2Deg, 40f, 95f);
            pose = new Pose(position, Quaternion.LookRotation(-documentFront, documentUp));
            if (!CameraPositionClear(position) || !SightClear(objectBounds.center, position) ||
                !HeroSightClear(position, objectBounds.center)) return false;
            // A world-axis bounding box overestimates a rotated body. The hero's
            // whole standing capsule/coat must be behind this frontal lens plane.
            if (Vector3.Dot(heroAtSubjectHeight - position, documentFront) < .48f)
            { LastRejectedShotReason = "Hero remains in the document close-up"; return false; }
            return true;
        }

        private float ProjectExtent(Vector3 axis) => Mathf.Abs(axis.x) * objectBounds.extents.x +
            Mathf.Abs(axis.y) * objectBounds.extents.y + Mathf.Abs(axis.z) * objectBounds.extents.z;

        private Bounds HeroBounds()
        {
            Bounds body = objectShotHeroBody != null ? objectShotHeroBody.bounds :
                new Bounds(heroRoot.position + Vector3.up * .9f, new Vector3(.65f, 1.8f, .65f));
            body.Expand(new Vector3(.25f, .12f, .25f));
            return body;
        }

        private bool HeroLeavesObjectClear(Pose pose, float fov)
        {
            // The capsule misses the coat, shoulders and free hands.
            Bounds body = HeroBounds();
            if (body.SqrDistance(pose.position) < .65f * .65f)
            { LastRejectedShotReason = "Lens is too close to hero"; return false; }
            Rect hero = ProjectBounds(body, pose, fov);
            // A hero outside the frame is fine. The object and surroundings remain
            // the subject; a cropped foreground torso must not replace either.
            Rect visible = Intersection(hero, new Rect(0f, .26f, 1f, .74f));
            if (visible.width * visible.height > .22f)
            {
                LastRejectedShotReason = "Hero fills " + (visible.width * visible.height).ToString("F2") + " of the upper frame";
                return false;
            }
            // Bounds of a chair, frame or winch contain large empty regions, so
            // overlapping screen rectangles are not proof of occlusion. Test the
            // real hero's collision shapes along two rays into the subject instead.
            return HeroSightClear(pose.position, objectBounds.center) && HeroSightClear(pose.position,
                objectBounds.center + Vector3.up * Mathf.Min(objectBounds.extents.y, .35f));
        }

        private bool HeroSightClear(Vector3 position, Vector3 point)
        {
            Vector3 delta = point - position;
            int count = Physics.SphereCastNonAlloc(position, .08f, delta.normalized, hits,
                delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) { LastRejectedShotReason = "Hero sight query overflow"; return false; }
            for (int index = 0; index < count; index++)
            {
                Transform hit = hits[index].transform;
                if (hit == heroRoot || hit != null && hit.IsChildOf(heroRoot))
                {
                    LastRejectedShotReason = "Hero obscures subject: " + ColliderPath(hit);
                    return false;
                }
            }
            return true;
        }

        private Rect ProjectBounds(Bounds bounds, Pose pose, float fov)
        {
            Quaternion inverse = Quaternion.Inverse(pose.rotation);
            float tangent = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            bool anyInFront = false;
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                Vector3 view = inverse * (corner - pose.position);
                if (view.z <= .01f) continue;
                anyInFront = true;
                Vector2 point = new Vector2(.5f + view.x / (2f * view.z * tangent * camera.aspect),
                    .5f + view.y / (2f * view.z * tangent));
                minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point);
            }
            return anyInFront ? Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y) : default;
        }

        private static Rect Intersection(Rect left, Rect right)
        {
            float x = Mathf.Max(left.xMin, right.xMin), y = Mathf.Max(left.yMin, right.yMin);
            return new Rect(x, y, Mathf.Max(0f, Mathf.Min(left.xMax, right.xMax) - x),
                Mathf.Max(0f, Mathf.Min(left.yMax, right.yMax) - y));
        }

        private Pose ConstrainBlend(Pose candidate, Pose target)
        {
            // An inspection may reveal its subject from behind a corner.
            // Demanding the final sightline on every intermediate frame used
            // to jump straight to the target even with a clear camera path.
            if (CameraPositionClear(candidate.position) &&
                (objectShot || SightClear(FocusPoint, candidate.position))) return candidate;
            Vector3 origin = FocusPoint;
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
            if (count == overlaps.Length) { if (objectShot) LastRejectedShotReason = "Lens overlap query overflow"; return false; }
            for (int i = 0; i < count; i++) if (IsSceneCollider(overlaps[i]))
            {
                if (objectShot) LastRejectedShotReason = "Lens intersects " + ColliderPath(overlaps[i].transform);
                return false;
            }
            return true;
        }

        private bool SightClear(Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance < .01f) return false;
            int count = Physics.SphereCastNonAlloc(origin, SightClearanceMeters, delta / distance, hits,
                distance, follow.CollisionLayerMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) { if (objectShot) LastRejectedShotReason = "Subject sight query overflow"; return false; }
            for (int i = 0; i < count; i++) if (IsSceneCollider(hits[i].collider))
            {
                if (objectShot) LastRejectedShotReason = "Subject blocked by " + ColliderPath(hits[i].transform);
                return false;
            }
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

        private void UpdateFocusDistance() => CinematicDepthOfField.SetOwnedFocusDistance(this, FocusDistance);

        private bool IsDocumentShot => objectShot && objectMode == NarrativeCameraMode.DocumentCloseUp;
        private bool IsCloseUpShot => IsDocumentShot || objectShot && objectMode == NarrativeCameraMode.ObjectCloseUp;
        // URP focuses a plane at eye depth. The note sits above the optical
        // centre to leave room for the reading panel; radial distance would
        // place that plane behind the paper and blur its small lettering.
        private float FocusDistance => IsCloseUpShot
            ? Vector3.Dot(FocusPoint - camera.transform.position, camera.transform.forward)
            : Vector3.Distance(camera.transform.position, FocusPoint);

        private Transform ActiveHead => CurrentSpeakerIsHero ? heroHead : npcHead;
        private Vector3 FocusPoint => objectShot
            ? objectMode == NarrativeCameraMode.ObjectCloseUp ? closeUpCenter : objectBounds.center
            : ActiveHead.position + Vector3.up * .1f;

        private bool ParticipantsExist() => follow != null && camera != null &&
            npcRoot != null && heroRoot != null && (objectShot || npcHead != null && heroHead != null) &&
            npcRoot.gameObject.activeInHierarchy && heroRoot.gameObject.activeInHierarchy;

        private void ClearReferences()
        {
            follow = null; camera = null;
            npcRoot = npcHead = heroRoot = heroHead = null;
            objectShotHeroBody = null;
        }

        private static Pose Blend(Pose from, Pose to, float t) => new Pose(
            Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));

        private float TransitionProgress(float progress)
        {
            float t = Mathf.Clamp01(progress);
            // Inspection starts and ends with zero velocity and acceleration.
            return objectShot ? t * t * t * (t * (t * 6f - 15f) + 10f) : Mathf.SmoothStep(0f, 1f, t);
        }

        private static string ColliderPath(Transform value)
        {
            string path = value.name;
            for (Transform parent = value.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
    }
}
