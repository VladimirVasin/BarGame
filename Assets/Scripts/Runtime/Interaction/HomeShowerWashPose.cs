using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The wash on the production hero: both palms flat on the tile,
    /// elbows out, the torso leaning in and the head hanging under the
    /// water, with a slow sway; then the right hand leaving the wall to
    /// close the hot tap. The full-body clip set is closed, so this is
    /// solved every presentation frame on the actual rig, after the
    /// character presentation has written its own pose: capture the Idle
    /// neutral once, restore it, rebuild the pose on top, hand the arms
    /// to the shared two-bone solver. Every axis used here is a WORLD
    /// axis of the actor — the imported bones' local axes are not
    /// anatomical and are never reasoned about.
    ///
    /// The same component carries the authored pieces the undressed rig
    /// needs and the clothed rig hides: the three bridges that close the
    /// jacket's holes at the nape and the shoulders, and the toilet's
    /// authored anatomy, hanging at rest from the front of the pelvis.
    /// All of them are placed from bone positions only, every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerWashPose : MonoBehaviour
    {
        public const float SpinePitchDegrees = 6f;
        public const float ChestPitchDegrees = 8f;
        public const float NeckPitchDegrees = 18f;
        public const float HeadPitchDegrees = 20f;
        public const float SwayHertz = 0.45f;
        public const float SwayChestRollDegrees = 2.2f;
        public const float SwayHeadRollDegrees = 3.0f;
        public const float SwayPalmSlideMetres = 0.01f;
        public const float PalmFingerSplayDegrees = 12f;
        public const float ElbowHintOutMetres = 0.14f;
        public const float ElbowHintDownMetres = 0.12f;
        public const float ElbowHintForwardMetres = 0.25f;
        public const float ValveElbowHintOutMetres = 0.25f;
        public const float ValveElbowHintDownMetres = 0.30f;
        public const float YokeBelowShoulderMetres = 0.012f;
        public const float DeltoidAlongArmMetres = 0.03f;

        /// <summary>
        /// The resting anatomy hangs from the front of the bare pelvis: its
        /// base sits this far above the pelvis mesh's lowest point, set into
        /// the front surface by the same inset the toilet uses, and the shaft
        /// points this far below the horizontal.
        /// </summary>
        public const float AnatomyAboveCrotchMetres = 0.045f;
        public const float AnatomyBaseInsetMetres = 0.008f;
        public const float AnatomyRestPitchDegrees = 74f;
        public const float AnatomyFallbackForwardMetres = 0.07f;

        private Player3DAssetRegistry registry;
        private Transform actor;
        private Transform room;
        private Transform spine;
        private Transform chest;
        private Transform neck;
        private Transform head;
        private Transform pelvisAnchor;
        private readonly Arm left = new Arm();
        private readonly Arm right = new Arm();
        private readonly Quaternion[] neutral = new Quaternion[10];
        private Transform yoke;
        private Transform deltoidLeft;
        private Transform deltoidRight;
        private Transform anatomyRoot;
        private Transform anatomyAimPivot;
        private Transform scrotumLeft;
        private Transform scrotumRight;
        private Vector3 anatomyBaseInPelvis;
        private Quaternion anatomyRotationInPelvis = Quaternion.identity;
        private bool captured;
        private bool poseApplied;

        public bool IsInitialized => registry != null && actor != null;
        public bool IsCaptured => captured;
        public bool BridgesShown { get; private set; }
        public bool HasBridges => yoke != null && deltoidLeft != null && deltoidRight != null;
        public bool HasAnatomy => anatomyRoot != null && scrotumLeft != null && scrotumRight != null;
        public Transform AnatomyRoot => anatomyRoot;
        public float LeftPalmError { get; private set; }
        public float RightPalmError { get; private set; }
        public float LeftChainLength => left.ChainLength;
        public float RightChainLength => right.ChainLength;
        public Vector3 LeftPalmTarget { get; private set; }
        public Vector3 RightPalmTarget { get; private set; }

        /// <summary>All fallible preparation, before the modal capture.</summary>
        public bool Initialize(HomeInteriorRoot home)
        {
            if (home == null)
            {
                throw new ArgumentNullException(nameof(home));
            }

            Release();
            if (home.Player.GameObject == null ||
                !(home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null)
            {
                return false;
            }

            registry = visual.Registry;
            actor = home.Player.GameObject.transform;
            room = home.Room != null ? home.Room : home.transform;
            chest = ResolveBone(Player3DAnatomicalPart.Torso);
            spine = registry.Anchors.Spine;
            neck = ResolveBone(Player3DAnatomicalPart.Neck);
            head = ResolveBone(Player3DAnatomicalPart.Head);
            pelvisAnchor = registry.Anchors.Pelvis;
            if (chest == null || neck == null || head == null || pelvisAnchor == null ||
                !left.Resolve(registry, false) ||
                !right.Resolve(registry, true))
            {
                registry = null;
                return false;
            }

            if (!TryCreateBridges(home.transform))
            {
                Release();
                return false;
            }

            // The anatomy is a courtesy of the toilet's authored pieces; a
            // build without them still showers, bridges and all.
            TryCreateAnatomy(home.transform);
            SetBridgesShown(false);
            GameLog.Info(
                "home",
                "shower_wash_pose_ready",
                GameLog.Field("left_chain_m", left.ChainLength),
                GameLog.Field("right_chain_m", right.ChainLength),
                GameLog.Field("anatomy", HasAnatomy));
            return true;
        }

        /// <summary>
        /// Remembers the Idle neutral the pose is rebuilt over. Must run
        /// straight after the handoff lock is taken: the lock evaluates
        /// the Idle frame synchronously, so this reads a neutral, never
        /// the last stride of the walk.
        /// </summary>
        public void Capture()
        {
            if (!IsInitialized)
            {
                return;
            }

            neutral[0] = spine != null ? spine.localRotation : Quaternion.identity;
            neutral[1] = chest.localRotation;
            neutral[2] = neck.localRotation;
            neutral[3] = head.localRotation;
            neutral[4] = left.Upper.localRotation;
            neutral[5] = left.Forearm.localRotation;
            neutral[6] = left.Hand.localRotation;
            neutral[7] = right.Upper.localRotation;
            neutral[8] = right.Forearm.localRotation;
            neutral[9] = right.Hand.localRotation;
            captured = true;
        }

        /// <summary>
        /// The brace, at <paramref name="weight"/>; the right hand blends
        /// from the tile to the tap by <paramref name="valveReach"/>; the
        /// sway envelope scales the slow rock.
        /// </summary>
        public void ApplyBrace(
            float weight,
            float valveReach,
            float sway,
            float elapsed)
        {
            if (!IsInitialized || !captured)
            {
                return;
            }

            float w = Mathf.Clamp01(weight);
            RestoreNeutral();
            if (w <= 0.0001f)
            {
                poseApplied = false;
                return;
            }

            poseApplied = true;
            Vector3 actorRight = actor.right;
            Vector3 actorForward = actor.forward;
            Vector3 up = Vector3.up;
            float phase = elapsed * SwayHertz * 2f * Mathf.PI;
            float rock = Mathf.Clamp01(sway) * w;

            if (spine != null)
            {
                Pitch(spine, SpinePitchDegrees * w, actorRight);
            }

            Pitch(chest, ChestPitchDegrees * w, actorRight);
            Roll(chest, SwayChestRollDegrees * Mathf.Sin(phase) * rock, actorForward);
            Pitch(neck, NeckPitchDegrees * w, actorRight);
            Pitch(head, HeadPitchDegrees * w, actorRight);
            Roll(head, SwayHeadRollDegrees * Mathf.Sin(phase + 0.6f) * rock, actorForward);

            float slide = SwayPalmSlideMetres * Mathf.Sin(phase) * rock;
            Vector3 leftPalm = room.TransformPoint(HomeShowerFraming.LeftPalm) +
                actorRight * slide;
            Vector3 rightPalm = room.TransformPoint(HomeShowerFraming.RightPalm) +
                actorRight * slide;
            LeftPalmTarget = leftPalm;
            RightPalmTarget = rightPalm;

            // Fingers up the tile, splayed a little outward; thumbs inward.
            // With fingers up and the thumb toward the body's centre, a
            // real palm faces the wall on either hand — no sign to guess.
            Vector3 leftOut = -actorRight;
            Vector3 rightOut = actorRight;
            Quaternion leftHandRotation = HandRotation(
                left,
                (up + leftOut * Mathf.Tan(PalmFingerSplayDegrees * Mathf.Deg2Rad)).normalized,
                -leftOut);
            Vector3 leftHint = left.Upper.position +
                leftOut * ElbowHintOutMetres -
                up * ElbowHintDownMetres +
                actorForward * ElbowHintForwardMetres;
            left.Solve(leftPalm, leftHandRotation, leftHint, w);
            LeftPalmError = Vector3.Distance(left.PalmPosition, leftPalm);

            float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(valveReach));
            Quaternion rightWallRotation = HandRotation(
                right,
                (up + rightOut * Mathf.Tan(PalmFingerSplayDegrees * Mathf.Deg2Rad)).normalized,
                -rightOut);
            Vector3 rightHint = right.Upper.position +
                rightOut * ElbowHintOutMetres -
                up * ElbowHintDownMetres +
                actorForward * ElbowHintForwardMetres;
            Vector3 rightTarget = rightPalm;
            Quaternion rightRotation = rightWallRotation;
            if (reach > 0.0001f)
            {
                // Fingers over the knob, thumb to the left: the palm faces
                // down onto the cross handle.
                Vector3 grip = room.TransformPoint(HomeShowerFraming.HotHandleGrip);
                Quaternion tapRotation = HandRotation(right, actorForward, -actorRight);
                Vector3 tapHint = right.Upper.position +
                    rightOut * ValveElbowHintOutMetres -
                    up * ValveElbowHintDownMetres;
                rightTarget = Vector3.Lerp(rightPalm, grip, reach);
                rightRotation = Quaternion.Slerp(rightWallRotation, tapRotation, reach);
                rightHint = Vector3.Lerp(rightHint, tapHint, reach);
            }

            right.Solve(rightTarget, rightRotation, rightHint, w);
            RightPalmError = Vector3.Distance(right.PalmPosition, rightTarget);
        }

        /// <summary>The bridges and the anatomy: on with the clothes off, off with them on.</summary>
        public void SetBridgesShown(bool shown)
        {
            BridgesShown = shown && HasBridges;
            if (yoke != null) yoke.gameObject.SetActive(BridgesShown);
            if (deltoidLeft != null) deltoidLeft.gameObject.SetActive(BridgesShown);
            if (deltoidRight != null) deltoidRight.gameObject.SetActive(BridgesShown);
            bool anatomyShown = BridgesShown && HasAnatomy;
            if (anatomyRoot != null) anatomyRoot.gameObject.SetActive(anatomyShown);
            if (scrotumLeft != null) scrotumLeft.gameObject.SetActive(anatomyShown);
            if (scrotumRight != null) scrotumRight.gameObject.SetActive(anatomyShown);
        }

        /// <summary>
        /// Seats the yoke on the torso cap between the shoulder joints,
        /// pointing at the neck, each deltoid a little way down its own
        /// arm, and the anatomy at its measured place on the pelvis — from
        /// bone positions, after the pose has been solved.
        /// </summary>
        public void FollowBridges()
        {
            if (!BridgesShown || !IsInitialized)
            {
                return;
            }

            Vector3 leftShoulder = left.Upper.position;
            Vector3 rightShoulder = right.Upper.position;
            Vector3 across = rightShoulder - leftShoulder;
            Vector3 origin = (leftShoulder + rightShoulder) * 0.5f -
                Vector3.up * YokeBelowShoulderMetres;
            Vector3 toNeck = neck.position - origin;
            if (across.sqrMagnitude > 0.000001f && toNeck.sqrMagnitude > 0.000001f)
            {
                Vector3 acrossDirection = across.normalized;
                Vector3 yokeUp = Vector3.ProjectOnPlane(toNeck, acrossDirection);
                if (yokeUp.sqrMagnitude < 0.000001f)
                {
                    yokeUp = Vector3.up;
                }

                yokeUp.Normalize();
                Vector3 yokeForward = Vector3.Cross(acrossDirection, yokeUp);
                yoke.SetPositionAndRotation(
                    origin,
                    Quaternion.LookRotation(yokeForward, yokeUp));
            }

            PlaceDeltoid(deltoidLeft, left);
            PlaceDeltoid(deltoidRight, right);
            PlaceAnatomy();
        }

        /// <summary>Back to the neutral. Idempotent; the pieces keep their own switch.</summary>
        public void End()
        {
            if (captured && poseApplied)
            {
                RestoreNeutral();
            }

            poseApplied = false;
            captured = false;
            LeftPalmError = 0f;
            RightPalmError = 0f;
        }

        /// <summary>Drops the rig references and the authored pieces.</summary>
        public void Release()
        {
            End();
            SetBridgesShown(false);
            DestroyPivot(ref yoke);
            DestroyPivot(ref deltoidLeft);
            DestroyPivot(ref deltoidRight);
            DestroyPivot(ref anatomyRoot);
            DestroyPivot(ref scrotumLeft);
            DestroyPivot(ref scrotumRight);
            anatomyAimPivot = null;
            registry = null;
            actor = null;
        }

        private void RestoreNeutral()
        {
            if (spine != null) spine.localRotation = neutral[0];
            chest.localRotation = neutral[1];
            neck.localRotation = neutral[2];
            head.localRotation = neutral[3];
            left.Upper.localRotation = neutral[4];
            left.Forearm.localRotation = neutral[5];
            left.Hand.localRotation = neutral[6];
            right.Upper.localRotation = neutral[7];
            right.Forearm.localRotation = neutral[8];
            right.Hand.localRotation = neutral[9];
        }

        private static void Pitch(Transform bone, float degrees, Vector3 actorRight)
        {
            // A positive turn about the actor's right axis nods forward
            // and down; measured against the world, never the bone.
            bone.rotation = Quaternion.AngleAxis(degrees, actorRight) * bone.rotation;
        }

        private static void Roll(Transform bone, float degrees, Vector3 actorForward)
        {
            bone.rotation = Quaternion.AngleAxis(degrees, actorForward) * bone.rotation;
        }

        private static Quaternion HandRotation(Arm arm, Vector3 fingers, Vector3 thumb)
        {
            return Quaternion.LookRotation(fingers, thumb) *
                Quaternion.Inverse(arm.HandFrameInHand);
        }

        private void PlaceDeltoid(Transform pivot, Arm arm)
        {
            if (pivot == null)
            {
                return;
            }

            Vector3 along = arm.Forearm.position - arm.Upper.position;
            Vector3 offset = along.sqrMagnitude > 0.000001f
                ? along.normalized * DeltoidAlongArmMetres
                : Vector3.zero;
            pivot.SetPositionAndRotation(
                arm.Upper.position + offset,
                actor.rotation);
        }

        private void PlaceAnatomy()
        {
            if (!HasAnatomy)
            {
                return;
            }

            Vector3 root = pelvisAnchor.TransformPoint(anatomyBaseInPelvis);
            Quaternion rotation = pelvisAnchor.rotation * anatomyRotationInPelvis;
            anatomyRoot.SetPositionAndRotation(root, rotation);
            if (anatomyAimPivot != null)
            {
                // AimPivot is authored at zero; aligning by measured world
                // position keeps this right if the FBX root ever moves.
                anatomyRoot.position += root - anatomyAimPivot.position;
            }

            // The hanging masses stay on the body and hang under gravity:
            // the actor's yaw, never the shaft's pitch.
            Quaternion hang = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(actor.forward, Vector3.up).sqrMagnitude > 0.000001f
                    ? Vector3.ProjectOnPlane(actor.forward, Vector3.up).normalized
                    : Vector3.forward,
                Vector3.up);
            scrotumLeft.SetPositionAndRotation(
                root + hang * HomeToiletFirstPersonView.LeftScrotumAttachment, hang);
            scrotumRight.SetPositionAndRotation(
                root + hang * HomeToiletFirstPersonView.RightScrotumAttachment, hang);
        }

        private bool TryCreateBridges(Transform parent)
        {
            if (!HomeShowerBridgeResources.TryCreate(
                    HomeShowerBridgeResources.ShoulderYoke, parent, out yoke) ||
                !HomeShowerBridgeResources.TryCreate(
                    HomeShowerBridgeResources.DeltoidLeft, parent, out deltoidLeft) ||
                !HomeShowerBridgeResources.TryCreate(
                    HomeShowerBridgeResources.DeltoidRight, parent, out deltoidRight))
            {
                return false;
            }

            return TryDressBridges();
        }

        /// <summary>
        /// The toilet's authored anatomy, instantiated the way the toilet
        /// does it (the FBX keeps its own unit factor under a pivot), dressed
        /// in the hero's skin and measured onto the bare pelvis once.
        /// </summary>
        private void TryCreateAnatomy(Transform parent)
        {
            GameObject template = Resources.Load<GameObject>(
                HomeToiletFirstPersonView.AnatomyResourcePath);
            GameObject leftTemplate = Resources.Load<GameObject>("HomeToiletAction/Models/ScrotumLeft");
            GameObject rightTemplate = Resources.Load<GameObject>("HomeToiletAction/Models/ScrotumRight");
            if (template == null || leftTemplate == null || rightTemplate == null)
            {
                return;
            }

            anatomyRoot = new GameObject("Home Shower Anatomy").transform;
            anatomyRoot.SetParent(parent, false);
            GameObject model = Instantiate(template, anatomyRoot, false);
            model.name = "Blender Authored Shower Anatomy";
            anatomyAimPivot = FindDescendant(model.transform, "AimPivot");
            scrotumLeft = new GameObject("Home Shower ScrotumLeft Pivot").transform;
            scrotumLeft.SetParent(parent, false);
            Instantiate(leftTemplate, scrotumLeft, false);
            scrotumRight = new GameObject("Home Shower ScrotumRight Pivot").transform;
            scrotumRight.SetParent(parent, false);
            Instantiate(rightTemplate, scrotumRight, false);

            Player3DMeshBinding skin = FindPalette(Player3DBathingAppearance.SkinMaterialName);
            if (skin == null || skin.Renderer == null || skin.Renderer.sharedMaterial == null ||
                !TryMeasureAnatomyBase())
            {
                DestroyPivot(ref anatomyRoot);
                DestroyPivot(ref scrotumLeft);
                DestroyPivot(ref scrotumRight);
                anatomyAimPivot = null;
                return;
            }

            DressPivot(anatomyRoot, skin.Renderer.sharedMaterial, skin.BaseColor, true);
            DressPivot(scrotumLeft, skin.Renderer.sharedMaterial, skin.BaseColor, false);
            DressPivot(scrotumRight, skin.Renderer.sharedMaterial, skin.BaseColor, false);
        }

        /// <summary>
        /// The bare pelvis mesh, baked once in whatever pose the hero holds
        /// at preparation: the base sits a set height above its lowest
        /// point, on its front surface at that height, and both are then
        /// stored in the pelvis anchor's own frame so the walk, the lean
        /// and the sway carry them along.
        /// </summary>
        private bool TryMeasureAnatomyBase()
        {
            Renderer renderer = null;
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding != null && binding.MeshName == "GEO_Pelvis")
                {
                    renderer = binding.Renderer;
                    break;
                }
            }

            if (!(renderer is SkinnedMeshRenderer skinned) || skinned.sharedMesh == null)
            {
                return false;
            }

            Mesh sample = new Mesh();
            try
            {
                skinned.BakeMesh(sample, true);
                Vector3[] vertices = sample.vertices;
                if (vertices.Length == 0)
                {
                    return false;
                }

                Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
                float lowest = float.PositiveInfinity;
                var local = new Vector3[vertices.Length];
                for (int index = 0; index < vertices.Length; index++)
                {
                    local[index] = actor.InverseTransformPoint(
                        toWorld.MultiplyPoint3x4(vertices[index]));
                    lowest = Mathf.Min(lowest, local[index].y);
                }

                float baseHeight = lowest + AnatomyAboveCrotchMetres;
                float front = float.NegativeInfinity;
                for (int index = 0; index < local.Length; index++)
                {
                    if (Mathf.Abs(local[index].y - baseHeight) <= 0.04f)
                    {
                        front = Mathf.Max(front, local[index].z);
                    }
                }

                if (float.IsInfinity(front))
                {
                    front = AnatomyFallbackForwardMetres;
                }

                Vector3 baseLocal = new Vector3(0f, baseHeight, front - AnatomyBaseInsetMetres);
                Vector3 baseWorld = actor.TransformPoint(baseLocal);
                anatomyBaseInPelvis = pelvisAnchor.InverseTransformPoint(baseWorld);
                anatomyRotationInPelvis = Quaternion.Inverse(pelvisAnchor.rotation) *
                    actor.rotation * Quaternion.Euler(AnatomyRestPitchDegrees, 0f, 0f);
                return true;
            }
            finally
            {
                if (Application.isPlaying) Destroy(sample);
                else DestroyImmediate(sample);
            }
        }

        /// <summary>
        /// The pieces wear the hero's own skin: the borrowed skin
        /// material, tinted through a block like every hero part.
        /// </summary>
        private bool TryDressBridges()
        {
            Player3DMeshBinding skin = FindPalette(Player3DBathingAppearance.SkinMaterialName);
            if (skin == null || skin.Renderer == null || skin.Renderer.sharedMaterial == null)
            {
                return false;
            }

            Player3DMeshBinding shadow = FindPalette(Player3DBathingAppearance.SkinShadowMaterialName);
            Color shadowColor = shadow != null ? shadow.BaseColor : skin.BaseColor * 0.75f;
            DressPivot(yoke, skin.Renderer.sharedMaterial, skin.BaseColor, false);
            DressPivot(deltoidLeft, skin.Renderer.sharedMaterial, shadowColor, false);
            DressPivot(deltoidRight, skin.Renderer.sharedMaterial, shadowColor, false);
            return true;
        }

        private void DressPivot(Transform pivot, Material material, Color color, bool darkOutlet)
        {
            var block = new MaterialPropertyBlock();
            Renderer[] renderers = pivot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer target = renderers[index];
                target.gameObject.layer = actor.gameObject.layer;
                target.sharedMaterial = material;
                target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                Color tint = darkOutlet && target.name == "Anatomy_Outlet"
                    ? Player3DBathingAppearance.SkinDark
                    : color;
                block.Clear();
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                target.SetPropertyBlock(block);
            }

            Collider[] colliders = pivot.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private Player3DMeshBinding FindPalette(string paletteMaterialName)
        {
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding candidate = registry.MeshBindings[index];
                if (candidate != null && candidate.Renderer != null &&
                    candidate.PaletteMaterialName == paletteMaterialName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private Transform ResolveBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) && binding != null
                ? binding.Bone
                : null;
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

        private static void DestroyPivot(ref Transform pivot)
        {
            if (pivot != null)
            {
                GameObject instance = pivot.gameObject;
                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
            }

            pivot = null;
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            Release();
        }

        /// <summary>One arm's bones, its measured hand frame and its solve.</summary>
        private sealed class Arm
        {
            public Transform Upper;
            public Transform Forearm;
            public Transform Hand;
            public Quaternion HandFrameInHand;
            public Vector3 PalmOffsetInHand;
            public float ChainLength;

            public Vector3 PalmPosition => Hand.position + Hand.rotation * PalmOffsetInHand;

            public bool Resolve(Player3DAssetRegistry registry, bool rightSide)
            {
                Upper = Bone(registry, rightSide ? Player3DAnatomicalPart.RightUpperArm : Player3DAnatomicalPart.LeftUpperArm);
                Forearm = Bone(registry, rightSide ? Player3DAnatomicalPart.RightForearm : Player3DAnatomicalPart.LeftForearm);
                Hand = Bone(registry, rightSide ? Player3DAnatomicalPart.RightHand : Player3DAnatomicalPart.LeftHand);
                Transform grip = rightSide ? registry.Anchors.RightGrip : registry.Anchors.LeftGrip;
                if (Upper == null || Forearm == null || Hand == null || grip == null)
                {
                    return false;
                }

                if (!TryRigidMeshCenter(registry, rightSide ? "GEO_Hand.R" : "GEO_Hand.L", out Vector3 handCenter) ||
                    !TryRigidMeshCenter(registry, rightSide ? "GEO_Thumb.R" : "GEO_Thumb.L", out Vector3 thumbCenter))
                {
                    return false;
                }

                // The same measured frame the toilet uses for the right
                // hand, taken for both: fingers run from the wrist to the
                // grip socket, the thumb sits off that line.
                Vector3 fingers = grip.position - Hand.position;
                Vector3 thumb = Vector3.ProjectOnPlane(thumbCenter - handCenter, fingers);
                if (fingers.sqrMagnitude < 0.000001f || thumb.sqrMagnitude < 0.000001f)
                {
                    return false;
                }

                HandFrameInHand = Quaternion.Inverse(Hand.rotation) *
                    Quaternion.LookRotation(fingers.normalized, thumb.normalized);
                PalmOffsetInHand = Quaternion.Inverse(Hand.rotation) * (handCenter - Hand.position);
                ChainLength = LimbTwoBoneIk.ChainLength(Upper, Forearm, Hand);
                return ChainLength > 0.1f;
            }

            public void Solve(Vector3 palm, Quaternion handRotation, Vector3 hint, float weight)
            {
                Vector3 wrist = palm - handRotation * PalmOffsetInHand;
                LimbTwoBoneIk.Solve(
                    Upper, Forearm, Hand,
                    wrist, handRotation, hint,
                    weight, LimbTwoBoneIk.DefaultReachFraction, true);
            }

            private static Transform Bone(Player3DAssetRegistry registry, Player3DAnatomicalPart part)
            {
                return registry.TryGetPart(part, out var binding) && binding != null
                    ? binding.Bone
                    : null;
            }

            private static bool TryRigidMeshCenter(
                Player3DAssetRegistry registry,
                string meshName,
                out Vector3 center)
            {
                center = Vector3.zero;
                Renderer renderer = null;
                for (int index = 0; index < registry.MeshBindings.Count; index++)
                {
                    Player3DMeshBinding binding = registry.MeshBindings[index];
                    if (binding != null && binding.MeshName == meshName)
                    {
                        renderer = binding.Renderer;
                        break;
                    }
                }

                if (!(renderer is SkinnedMeshRenderer skinned) || skinned.sharedMesh == null)
                {
                    return false;
                }

                // Production imports disable mesh Read/Write; bake once.
                Mesh sample = new Mesh();
                try
                {
                    skinned.BakeMesh(sample, true);
                    Vector3[] vertices = sample.vertices;
                    if (vertices.Length == 0)
                    {
                        return false;
                    }

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
        }
    }
}
