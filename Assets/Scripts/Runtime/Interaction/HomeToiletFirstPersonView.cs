using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Eye-level toilet presentation on the production hero. The authored
    /// anatomy stays attached to the pelvis and the actual right arm closes
    /// on its measured grip. The bathroom owner supplies the timeline and
    /// calls Tick after the ordinary hero pose, before resolving its camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletFirstPersonView : MonoBehaviour
    {
        public const string AnatomyResourcePath =
            "HomeToiletAction/Models/Anatomy";
        public const float FieldOfView = 78f;
        public const float BaseCameraPitchDegrees = 62f;
        public const float MinimumAimPitchDegrees = -55f;
        public const float MaximumAimPitchDegrees = 70f;
        public const float MaximumWristYawDegrees = 30f;
        public const float GripContactToleranceMeters = 0.02f;

        private const float MouseYawSensitivity = 0.16f;
        private const float MousePitchSensitivity = 0.14f;
        private const float StickDegreesPerSecond = 105f;
        private const float EyeHeightAboveMouth = 0.068f;
        private const float ShakeHertz = 2.5f;
        private const float ShakeDegrees = 11f;
        private const float AnatomyHeightAbovePelvis = 0.020f;
        private const float AnatomyBaseInset = 0.008f;
        public static readonly Vector3 LeftScrotumAttachment = new Vector3(-0.011f, -0.016f, -0.006f);
        public static readonly Vector3 RightScrotumAttachment = new Vector3(0.011f, -0.016f, -0.006f);
        private readonly HomeToiletAnatomyDynamics dynamics = new HomeToiletAnatomyDynamics();

        private HomeInteriorRoot home;
        private Player3DAssetRegistry registry;
        private Transform actor;
        private Transform upperArm;
        private Transform forearm;
        private Transform hand;
        private Transform head;
        private Transform anatomyRoot;
        private Transform anatomyGrip;
        private Transform anatomyOutlet;
        private Transform anatomyAimPivot;
        private Transform leftScrotum;
        private Transform rightScrotum;
        private Player3DHeadVisibility hiddenHead;
        private SeatedArmHandAttachment handAttachment;
        private Quaternion handFrameInHand;
        private Quaternion neutralUpper;
        private Quaternion neutralForearm;
        private Quaternion neutralHand;
        private Quaternion neutralHead;
        private Quaternion entryRotation;
        private Quaternion cameraRotation;
        private Vector3 cameraPosition;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool cursorCaptured;
        private bool occlusionCaptured;
        private bool previousOcclusionEnabled;
        private bool poseApplied;
        private bool returning;
        private bool wasAimingAllowed;
        private float lastWeight;
        private float aimYaw;
        private float bodyYaw;
        private float aimPitch;
        private float freeYaw;
        private float freePitch;
        private float anatomyForwardMeters = 0.18f;
        private bool freeLook;

        public bool IsInitialized => home != null;
        public bool IsPrepared => anatomyRoot != null && registry != null;
        public bool IsActive { get; private set; }
        public Player3DAssetRegistry Registry => registry;
        public Transform AnatomyRoot => anatomyRoot;
        public Transform Grip => anatomyGrip;
        public Transform Outlet => anatomyOutlet;
        public Transform LeftScrotum => leftScrotum;
        public Transform RightScrotum => rightScrotum;
        public HomeToiletAnatomyDynamics Dynamics => dynamics;
        public float AimYawDegrees => aimYaw;
        public float AimPitchDegrees => aimPitch;
        public float AnatomyForwardMeters => anatomyForwardMeters;
        public bool IsFreeLook => freeLook;
        public int HiddenHeadRendererCount =>
            hiddenHead?.HiddenRendererCount ?? 0;
        public Vector3 OutletPosition => anatomyOutlet != null
            ? anatomyOutlet.position : Vector3.zero;
        public Vector3 OutletDirection => anatomyRoot != null
            ? anatomyRoot.forward : Vector3.forward;
        public float GripError => anatomyGrip != null && registry != null
            ? Vector3.Distance(anatomyGrip.position,
                registry.Anchors.RightGrip.position)
            : float.PositiveInfinity;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            End();
            ReleaseAnatomy();
            home = homeRoot;
            registry = null;
        }

        /// <summary>All fallible asset preparation precedes modal capture.</summary>
        public bool Prepare()
        {
            if (IsPrepared)
            {
                return true;
            }

            if (home == null || home.Player.GameObject == null ||
                !(home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null)
            {
                return false;
            }

            registry = visual.Registry;
            actor = home.Player.GameObject.transform;
            upperArm = ResolveBone(Player3DAnatomicalPart.RightUpperArm);
            forearm = ResolveBone(Player3DAnatomicalPart.RightForearm);
            hand = ResolveBone(Player3DAnatomicalPart.RightHand);
            head = ResolveBone(Player3DAnatomicalPart.Head);
            if (upperArm == null || forearm == null || hand == null ||
                head == null || registry.Anchors.Pelvis == null ||
                registry.Anchors.Mouth == null ||
                registry.Anchors.RightGrip == null)
            {
                return false;
            }

            if (!TryCaptureHandFrame())
            {
                return false;
            }

            GameObject template = Resources.Load<GameObject>(
                AnatomyResourcePath);
            if (template == null)
            {
                return false;
            }

            anatomyRoot = new GameObject("Home Toilet Anatomy Aim").transform;
            anatomyRoot.SetParent(home.transform, false);
            anatomyRoot.gameObject.SetActive(false);
            // Keep the imported FBX's own 100x authoring-root unit factor.
            GameObject model = Instantiate(template, anatomyRoot, false);
            model.name = "Blender Authored Toilet Anatomy";
            anatomyGrip = FindDescendant(model.transform, "Grip");
            anatomyOutlet = FindDescendant(model.transform, "Outlet");
            anatomyAimPivot = FindDescendant(model.transform, "AimPivot");
            leftScrotum = CreateScrotum("ScrotumLeft");
            rightScrotum = CreateScrotum("ScrotumRight");
            Renderer[] renderers = anatomyRoot.GetComponentsInChildren<Renderer>(true);
            if (anatomyGrip == null || anatomyOutlet == null ||
                anatomyAimPivot == null || leftScrotum == null || rightScrotum == null || renderers.Length == 0 ||
                Vector3.Distance(anatomyOutlet.position,
                    anatomyAimPivot.position) < 0.10f ||
                Vector3.Distance(anatomyOutlet.position,
                    anatomyAimPivot.position) > 0.17f)
            {
                ReleaseAnatomy();
                return false;
            }

            try
            {
                ApplyHeroMaterials(renderers);
            }
            catch (InvalidOperationException)
            {
                ReleaseAnatomy();
                return false;
            }
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            return true;
        }

        public void Begin()
        {
            if (IsActive)
            {
                return;
            }

            if (!Prepare())
            {
                throw new InvalidOperationException(
                    "The toilet view requires the authored anatomy and hero rig.");
            }

            entryRotation = actor.rotation;
            neutralUpper = upperArm.localRotation;
            neutralForearm = forearm.localRotation;
            neutralHand = hand.localRotation;
            neutralHead = head.localRotation;
            handAttachment = new SeatedArmHandAttachment(
                hand, registry.Anchors.RightGrip);
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (home.PlayerOcclusion != null)
            {
                previousOcclusionEnabled = home.PlayerOcclusion.enabled;
                occlusionCaptured = true;
                home.PlayerOcclusion.enabled = false;
                home.PlayerOcclusion.ClearOcclusion();
            }
            aimYaw = 0f;
            bodyYaw = 0f;
            anatomyForwardMeters = MeasureGarmentFront();
            aimPitch = ResolveInitialAimPitch();
            freeYaw = 0f;
            freePitch = 0f;
            freeLook = false;
            lastWeight = 0f;
            poseApplied = false;
            returning = false;
            wasAimingAllowed = false;
            IsActive = true;
            anatomyRoot.gameObject.SetActive(true);
            UpdateCameraPose();
            dynamics.Reset(cameraRotation);
            ApplyPose(0f, -1f);
            UpdateCameraPose();
        }

        /// <param name="shakeElapsed">Negative outside the shake phase.</param>
        public void Tick(
            float deltaTime,
            float poseWeight,
            float shakeElapsed,
            bool aimingAllowed)
        {
            if (!IsActive || registry == null || actor == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(poseWeight);
            if (weight + 0.0001f < lastWeight)
            {
                returning = true;
            }

            if (aimingAllowed && !returning &&
                !PauseMenuController.IsAnyPaused)
            {
                ReadAimInput(Mathf.Max(0f, deltaTime), !wasAimingAllowed);
            }

            wasAimingAllowed = aimingAllowed;
            if (returning && weight <= 0.0001f)
            {
                RestorePose();
                RestoreHead();
                anatomyRoot.gameObject.SetActive(false);
                RestoreCursor();
            }
            else
            {
                ApplyPose(weight, shakeElapsed, deltaTime);
                UpdateCameraPose();
                // Only head geometry is hidden; the actual arm, body and
                // contact shadow stay visible throughout the action.
                if (weight >= 0.90f && hiddenHead == null)
                {
                    hiddenHead = Player3DHeadVisibility.Hide(registry);
                }
                else if (weight < 0.90f && hiddenHead != null)
                {
                    RestoreHead();
                }
            }

            lastWeight = weight;
        }

        public void EvaluateCamera(out Vector3 position, out Quaternion rotation)
        {
            position = cameraPosition;
            rotation = cameraRotation;
        }

        /// <summary>
        /// A wrist-sized yaw range passes its overflow to an on-the-spot body
        /// turn. Absolute yaw is deliberately unbounded: no toilet-facing cone.
        /// The same path accepts mouse, stick and focused deterministic checks.
        /// </summary>
        public void ApplyAimDelta(Vector2 degrees, bool independentLook)
        {
            if (!IsActive || !IsFinite(degrees.x) || !IsFinite(degrees.y))
            {
                return;
            }

            freeLook = independentLook;
            if (independentLook)
            {
                freeYaw = Mathf.Clamp(freeYaw + degrees.x, -125f, 125f);
                freePitch = Mathf.Clamp(freePitch + degrees.y, -115f, 35f);
                return;
            }

            aimYaw += degrees.x;
            aimPitch = Mathf.Clamp(aimPitch + degrees.y,
                MinimumAimPitchDegrees, MaximumAimPitchDegrees);
            float wristYaw = aimYaw - bodyYaw;
            bodyYaw += wristYaw - Mathf.Clamp(wristYaw,
                -MaximumWristYawDegrees, MaximumWristYawDegrees);
        }

        public void ApplyAimDelta(float yawDegrees, float pitchDegrees)
        {
            ApplyAimDelta(new Vector2(yawDegrees, pitchDegrees), false);
        }

        public void End()
        {
            if (IsActive)
            {
                RestorePose();
            }

            RestoreHead();
            RestoreCursor();
            if (occlusionCaptured)
            {
                if (home != null && home.PlayerOcclusion != null)
                {
                    home.PlayerOcclusion.enabled = previousOcclusionEnabled;
                }

                occlusionCaptured = false;
            }
            if (anatomyRoot != null)
            {
                anatomyRoot.gameObject.SetActive(false);
            }

            IsActive = false;
            dynamics.Reset(Quaternion.identity);
            freeLook = false;
            wasAimingAllowed = false;
        }

        private void ApplyPose(float weight, float shakeElapsed, float deltaTime = 0f)
        {
            upperArm.localRotation = neutralUpper;
            forearm.localRotation = neutralForearm;
            hand.localRotation = neutralHand;
            head.localRotation = neutralHead;
            float yawWeight = returning ? weight : 1f;
            actor.rotation = entryRotation *
                Quaternion.Euler(0f, Mathf.DeltaAngle(0f, bodyYaw) * yawWeight, 0f);

            float shake = 0f;
            if (shakeElapsed >= 0f)
            {
                float envelope = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(shakeElapsed / 0.24f)) *
                    Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((HomeToiletSceneTimeline.ShakingSeconds -
                        shakeElapsed) / 0.40f));
                shake = Mathf.Sin(shakeElapsed * ShakeHertz * 2f * Mathf.PI) *
                    ShakeDegrees * envelope;
            }

            UpdateCameraPose();
            if (!PauseMenuController.IsAnyPaused)
                dynamics.Advance(deltaTime, cameraRotation, actor.rotation, shake);
            Vector2 sway = dynamics.ShaftDegrees;
            Quaternion aimRotation = entryRotation *
                Quaternion.Euler(aimPitch + shake + sway.x, aimYaw + sway.y, 0f);
            // At both endpoints the fixed-size model is physically behind
            // the existing trousers. No opacity or scale animation masks it.
            float reveal = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.14f, 0.78f, weight));
            Vector3 pelvis = registry.Anchors.Pelvis.position;
            Vector3 rootPosition = pelvis + Vector3.up * AnatomyHeightAbovePelvis +
                actor.forward * Mathf.Lerp(-0.078f, anatomyForwardMeters, reveal);
            anatomyRoot.SetPositionAndRotation(rootPosition,
                Quaternion.Slerp(actor.rotation, aimRotation, reveal));
            // AimPivot is authored at zero, but aligning by measured world
            // position also preserves correctness if the FBX root changes.
            anatomyRoot.position += rootPosition - anatomyAimPivot.position;
            // Both upper attachments stay on the body. The hanging masses
            // use their own gravity/damping response, independent of shaft aim.
            ApplyScrotum(leftScrotum, LeftScrotumAttachment, dynamics.LeftDegrees, rootPosition, reveal);
            ApplyScrotum(rightScrotum, RightScrotumAttachment, dynamics.RightDegrees, rootPosition, reveal);

            // A generic prop socket gives contact, not the palm's facing.
            // Use the actual hand/thumbnail geometry's measured frame:
            // fingers descend around the shaft, thumb follows its upper
            // length and the right palm faces inward. Copying the bottle
            // socket rotation turned the back of this hand toward the grip.
            Vector3 fingerDirection =
                (-anatomyRoot.up - anatomyRoot.right * 1.25f).normalized;
            Quaternion handRotation = Quaternion.LookRotation(
                fingerDirection, anatomyRoot.forward) *
                Quaternion.Inverse(handFrameInHand);
            Vector3 wristTarget = anatomyGrip.position -
                handRotation * handAttachment.SocketPositionInHand;
            Vector3 elbowHint = upperArm.position +
                actor.right * 0.38f - actor.forward * 0.04f - Vector3.up * 0.32f;
            LimbTwoBoneIk.Solve(upperArm, forearm, hand,
                wristTarget, handRotation, elbowHint, weight,
                float.PositiveInfinity, true);
            poseApplied |= weight > 0.0001f;
        }

        private Transform CreateScrotum(string resourceName)
        {
            GameObject template = Resources.Load<GameObject>("HomeToiletAction/Models/" + resourceName);
            if (template == null) return null;
            Transform pivot = new GameObject("Home Toilet " + resourceName + " Pivot").transform;
            pivot.SetParent(anatomyRoot, false);
            // Retain the FBX's authored unit factor on its own root.
            Instantiate(template, pivot, false);
            return pivot;
        }

        private void ApplyScrotum(Transform pivot, Vector3 offset, Vector2 swing,
            Vector3 attachment, float reveal)
        {
            if (pivot == null) return;
            pivot.SetPositionAndRotation(attachment + actor.rotation * offset,
                actor.rotation * Quaternion.Euler(swing.x * reveal, 0f, swing.y * reveal));
        }

        private void UpdateCameraPose()
        {
            // The hero's measured mouth/eye gap keeps the lens inside the
            // real head, including this production rig's slight tired lean.
            cameraPosition = registry.Anchors.Mouth.position +
                Vector3.up * EyeHeightAboveMouth;
            float pitch = Mathf.Clamp(
                BaseCameraPitchDegrees + aimPitch * 0.08f + freePitch,
                -70f, 87f);
            cameraRotation = entryRotation *
                Quaternion.Euler(pitch, aimYaw + freeYaw, 0f);
        }

        private float ResolveInitialAimPitch()
        {
            Transform water = home.Room != null
                ? home.Room.Find("Home Bathroom Toilet Water") : null;
            if (water == null)
            {
                return 37f;
            }

            Vector3 facing = entryRotation * Vector3.forward;
            Vector3 pivot = registry.Anchors.Pelvis.position +
                Vector3.up * AnatomyHeightAbovePelvis + facing * anatomyForwardMeters;
            // A low stream from the attached body base must pass over
            // the near seat. Aim inside the far edge of the 0.34 m water
            // oval to retain clearance after bringing the base inward.
            Vector3 waterTarget = water.position + facing * 0.15f;
            Vector3 towardBowl = Vector3.ProjectOnPlane(
                waterTarget - pivot, Vector3.up);
            if (towardBowl.sqrMagnitude < 0.01f)
            {
                return 37f;
            }

            aimYaw = Vector3.SignedAngle(facing, towardBowl, Vector3.up);
            Vector3 outletOffset = anatomyRoot.InverseTransformPoint(
                anatomyOutlet.position) - anatomyRoot.InverseTransformPoint(
                anatomyAimPivot.position);
            // Solve the low arc against the actual eye-level rig's pelvis
            // and curved authored outlet. A guessed pitch can hit the front
            // rim when the same adult's idle pelvis sits a little lower.
            float lower = 0f;
            float upper = 65f;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                float pitch = (lower + upper) * 0.5f;
                Quaternion rotation = entryRotation *
                    Quaternion.Euler(pitch, aimYaw, 0f);
                Vector3 outlet = pivot + rotation * outletOffset;
                Vector3 velocity = rotation * Vector3.forward *
                    HomeUrineEffect.StreamSpeed;
                float horizontalSpeed = Vector3.ProjectOnPlane(
                    velocity, Vector3.up).magnitude;
                float horizontalDistance = Vector3.ProjectOnPlane(
                    waterTarget - outlet, Vector3.up).magnitude;
                float seconds = horizontalDistance / Mathf.Max(0.01f,
                    horizontalSpeed);
                float arrivalHeight = HomeUrineTrajectory.Position(
                    outlet, velocity, seconds).y;
                if (arrivalHeight > waterTarget.y) lower = pitch;
                else upper = pitch;
            }

            return (lower + upper) * 0.5f;
        }

        private void ReadAimInput(float deltaTime, bool discardMouseDelta)
        {
            Vector2 degrees = Vector2.zero;
            Mouse mouse = Mouse.current;
            bool independent = mouse != null && mouse.rightButton.isPressed;
            if (mouse != null && !discardMouseDelta)
            {
                Vector2 delta = mouse.delta.ReadValue();
                degrees += new Vector2(delta.x * MouseYawSensitivity,
                    -delta.y * MousePitchSensitivity);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                degrees += new Vector2(stick.x, -stick.y) *
                    (StickDegreesPerSecond * deltaTime);
                independent |= gamepad.leftShoulder.isPressed;
            }

            ApplyAimDelta(degrees, independent);
            if (!independent)
            {
                float returnWeight = 1f - Mathf.Exp(-12f * deltaTime);
                freeYaw = Mathf.Lerp(freeYaw, 0f, returnWeight);
                freePitch = Mathf.Lerp(freePitch, 0f, returnWeight);
            }
        }

        private void RestorePose()
        {
            if (!poseApplied)
            {
                return;
            }

            if (upperArm != null) upperArm.localRotation = neutralUpper;
            if (forearm != null) forearm.localRotation = neutralForearm;
            if (hand != null) hand.localRotation = neutralHand;
            if (head != null) head.localRotation = neutralHead;
            if (actor != null) actor.rotation = entryRotation;
            poseApplied = false;
        }

        private void RestoreHead()
        {
            hiddenHead?.Restore();
            hiddenHead = null;
        }

        private void RestoreCursor()
        {
            if (!cursorCaptured)
            {
                return;
            }

            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorCaptured = false;
        }

        private Transform ResolveBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) && binding != null
                ? binding.Bone : null;
        }

        private bool TryCaptureHandFrame()
        {
            Renderer handRenderer = null;
            Renderer thumbRenderer = null;
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding == null) continue;
                if (binding.MeshName == "GEO_Hand.R") handRenderer = binding.Renderer;
                if (binding.MeshName == "GEO_Thumb.R") thumbRenderer = binding.Renderer;
            }

            if (!TryRigidMeshCenter(handRenderer, out Vector3 handCenter) ||
                !TryRigidMeshCenter(thumbRenderer, out Vector3 thumbCenter))
            {
                return false;
            }

            Vector3 fingers = registry.Anchors.RightGrip.position - hand.position;
            Vector3 thumb = Vector3.ProjectOnPlane(thumbCenter - handCenter, fingers);
            if (fingers.sqrMagnitude < 0.000001f || thumb.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            handFrameInHand = Quaternion.Inverse(hand.rotation) *
                Quaternion.LookRotation(fingers.normalized, thumb.normalized);
            return true;
        }

        private float MeasureGarmentFront()
        {
            Vector3 origin = registry.Anchors.Pelvis.position +
                Vector3.up * AnatomyHeightAbovePelvis;
            Vector3 facing = actor.forward;
            float front = 0f;
            Mesh sample = new Mesh();
            try
            {
                for (int index = 0; index < registry.MeshBindings.Count; index++)
                {
                    Player3DMeshBinding binding = registry.MeshBindings[index];
                    if (binding == null ||
                        (binding.MeshName != "CLO_JacketBody" &&
                         binding.MeshName != "GEO_Torso" &&
                         binding.MeshName != "GEO_Pelvis") ||
                        !(binding.Renderer is SkinnedMeshRenderer renderer))
                    {
                        continue;
                    }

                    sample.Clear(false);
                    renderer.BakeMesh(sample, true);
                    Vector3[] vertices = sample.vertices;
                    int[] triangles = sample.triangles;
                    Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                    for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                    {
                        Vector3 a = toWorld.MultiplyPoint3x4(vertices[triangles[triangle]]);
                        Vector3 b = toWorld.MultiplyPoint3x4(vertices[triangles[triangle + 1]]);
                        Vector3 c = toWorld.MultiplyPoint3x4(vertices[triangles[triangle + 2]]);
                        if (TryRayTriangle(origin, facing, a, b, c, out float distance))
                            front = Mathf.Max(front, distance);
                    }
                }
            }
            finally
            {
                if (Application.isPlaying) Destroy(sample);
                else DestroyImmediate(sample);
            }

            // Attach at the actual garment cross-section through the base.
            // A maximum over the leaning upper torso plus extra clearance
            // suspended the whole model in front of the trousers. The small
            // overlap keeps the authored base ring joined even at aim limits;
            // its distal length and the hand's base grip stay unchanged.
            return (front > 0f ? front : 0.105f) - AnatomyBaseInset;
        }

        private static bool TryRayTriangle(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edgeA = b - a;
            Vector3 edgeB = c - a;
            Vector3 cross = Vector3.Cross(direction, edgeB);
            float determinant = Vector3.Dot(edgeA, cross);
            if (Mathf.Abs(determinant) < 0.0000001f) return false;

            float inverse = 1f / determinant;
            Vector3 relative = origin - a;
            float u = Vector3.Dot(relative, cross) * inverse;
            if (u < -0.0001f || u > 1.0001f) return false;
            Vector3 q = Vector3.Cross(relative, edgeA);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -0.0001f || u + v > 1.0001f) return false;
            distance = Vector3.Dot(edgeB, q) * inverse;
            return distance > 0f && distance < 0.45f;
        }

        private bool TryRigidMeshCenter(Renderer renderer, out Vector3 center)
        {
            center = Vector3.zero;
            if (!(renderer is SkinnedMeshRenderer skinned) ||
                skinned.sharedMesh == null)
            {
                return false;
            }

            // Production imports disable mesh Read/Write. This is the same
            // pose readback used by Player3DFootGroundProbe, once at prepare,
            // never new visible geometry or a replacement hand model.
            Mesh sample = new Mesh();
            try
            {
                skinned.BakeMesh(sample, true);
                Vector3[] vertices = sample.vertices;
                if (vertices.Length == 0) return false;
                for (int index = 0; index < vertices.Length; index++)
                {
                    center += vertices[index];
                }

                center = skinned.transform.TransformPoint(center / vertices.Length);
                return true;
            }
            finally
            {
                if (Application.isPlaying) Destroy(sample);
                else DestroyImmediate(sample);
            }
        }

        private void ApplyHeroMaterials(Renderer[] renderers)
        {
            Player3DMeshBinding skin = null;
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding candidate = registry.MeshBindings[index];
                if (candidate != null && candidate.Renderer != null &&
                    candidate.BoneName == "hand.R" &&
                    candidate.PaletteMaterialName == "MAT_Skin")
                {
                    skin = candidate;
                    break;
                }
            }

            if (skin == null)
            {
                throw new InvalidOperationException(
                    "The toilet anatomy requires the production hero skin material.");
            }

            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer target = renderers[index];
                target.gameObject.layer = actor.gameObject.layer;
                target.sharedMaterial = skin.Renderer.sharedMaterial;
                Color color = target.name == "Anatomy_Outlet"
                    ? new Color(57f / 255f, 45f / 255f, 46f / 255f)
                    : skin.BaseColor;
                properties.Clear();
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
                target.SetPropertyBlock(properties);
            }
        }

        private void ReleaseAnatomy()
        {
            if (anatomyRoot != null)
            {
                GameObject instance = anatomyRoot.gameObject;
                instance.SetActive(false);
                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
            }

            anatomyRoot = null;
            anatomyGrip = null;
            anatomyOutlet = null;
            anatomyAimPivot = null;
            leftScrotum = rightScrotum = null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                if (descendants[index].name == name) return descendants[index];
            }

            return null;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            End();
            ReleaseAnatomy();
        }
    }
}
