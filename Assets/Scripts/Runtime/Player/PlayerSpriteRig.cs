using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum PlayerPuppetPart
    {
        Body = 0,
        LeftUpperArm = 1,
        LeftLowerArm = 2,
        RightUpperArm = 3,
        RightLowerArm = 4,
        LeftUpperLeg = 5,
        LeftLowerLeg = 6,
        RightUpperLeg = 7,
        RightLowerLeg = 8
    }

    /// <summary>
    /// Presents the player as an eight-direction, nine-layer pixel puppet.
    /// Every arm and leg has a parented upper/lower joint. The generated
    /// hierarchy is visual-only and never receives physics components.
    /// </summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class PlayerSpriteRig : MonoBehaviour
    {
        public const string AtlasResourcePath =
            "Player/PlayerDirectionalPartsAtlas";
        public const string ReferenceAtlasResourcePath =
            "Player/PlayerDirectionalAtlas";
        public const int DirectionCount = 8;
        public const int PartCount = 9;
        public const int FrameWidth = 64;
        public const int FrameHeight = 96;
        public const float PixelsPerUnit = 48f;
        public const float FeetPivotXPixels = 32f;
        public const float FeetPivotPixels = 4f;

        private const float MovingThreshold = 0.02f;
        private const float DepthSortingThreshold = 0.005f;
        private const int FarLimbSortingOffset = 5;

        private static readonly PlayerPuppetPose[] DirectionPoses =
        {
            new PlayerPuppetPose(
                new Vector2(22f, 30f),
                new Vector2(19f, 46f),
                new Vector2(42f, 30f),
                new Vector2(44f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                0.55f),
            new PlayerPuppetPose(
                new Vector2(25f, 30f),
                new Vector2(21f, 46f),
                new Vector2(39f, 30f),
                new Vector2(43f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                0.8f),
            new PlayerPuppetPose(
                new Vector2(32f, 30f),
                new Vector2(32f, 46f),
                new Vector2(35f, 30f),
                new Vector2(36f, 46f),
                new Vector2(30f, 56f),
                new Vector2(30f, 75f),
                new Vector2(34f, 56f),
                new Vector2(35f, 75f),
                1f),
            new PlayerPuppetPose(
                new Vector2(24f, 30f),
                new Vector2(21f, 46f),
                new Vector2(40f, 30f),
                new Vector2(44f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                0.8f),
            new PlayerPuppetPose(
                new Vector2(23f, 30f),
                new Vector2(20f, 46f),
                new Vector2(41f, 30f),
                new Vector2(44f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                0.55f),
            new PlayerPuppetPose(
                new Vector2(25f, 30f),
                new Vector2(22f, 46f),
                new Vector2(39f, 30f),
                new Vector2(42f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                0.8f),
            new PlayerPuppetPose(
                new Vector2(29f, 30f),
                new Vector2(29f, 46f),
                new Vector2(32f, 30f),
                new Vector2(32f, 46f),
                new Vector2(30f, 56f),
                new Vector2(29f, 75f),
                new Vector2(34f, 56f),
                new Vector2(34f, 75f),
                1f),
            new PlayerPuppetPose(
                new Vector2(24f, 30f),
                new Vector2(20f, 46f),
                new Vector2(40f, 30f),
                new Vector2(43f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                0.8f)
        };

        [Header("Direction")]
        [SerializeField, Range(0f, 10f)]
        private float directionHysteresisDegrees = 5f;

        [Header("Jointed walk")]
        [SerializeField, Min(0.1f)] private float fullAnimationSpeed = 4f;
        [SerializeField, Min(0f)] private float walkCyclesPerSecond = 2.2f;
        [SerializeField, Range(0f, 60f)] private float armSwingDegrees = 24f;
        [SerializeField, Range(0f, 60f)] private float elbowBendDegrees = 11f;
        [SerializeField, Range(0f, 60f)] private float legSwingDegrees = 28f;
        [SerializeField, Range(0f, 60f)] private float kneeBendDegrees = 17f;
        [SerializeField, Min(0f)] private float walkBobHeight = 0.035f;
        [SerializeField, Range(0f, 10f)] private float walkRockDegrees = 1.4f;
        [SerializeField, Min(0f)] private float settleSpeed = 12f;

        private readonly List<Sprite> directionSprites =
            new List<Sprite>(DirectionCount);
        private readonly List<Sprite> generatedSprites =
            new List<Sprite>(DirectionCount * PartCount);
        private readonly List<SpriteRenderer> partRenderers =
            new List<SpriteRenderer>(PartCount);

        private readonly Transform[] partTransforms =
            new Transform[PartCount];
        private readonly Sprite[,] partSprites =
            new Sprite[PartCount, DirectionCount];

        private Camera targetCamera;
        private Transform facingTransform;
        private Transform visualRoot;
        private Transform poseRoot;
        private BillboardSprite billboard;
        private PlayerViewDirectionSelector directionSelector;
        private float animationPhase;
        private float motionAmount;
        private float wastedBlend;
        private float wastedPhase;
        private bool isWasted;

        public PlayerViewDirection CurrentDirection =>
            directionSelector != null
                ? directionSelector.CurrentDirection
                : PlayerViewDirection.Front;

        public IReadOnlyList<Sprite> DirectionSprites => directionSprites;
        public IReadOnlyList<SpriteRenderer> Renderers => partRenderers;
        public SpriteRenderer Renderer => GetPartRenderer(
            PlayerPuppetPart.Body);
        public SpriteRenderer BodyRenderer => Renderer;
        public Transform VisualRoot => visualRoot;
        public Transform PoseRoot => poseRoot;

        public void Initialize(
            Camera camera,
            Transform playerFacingTransform = null)
        {
            targetCamera = camera;
            facingTransform = playerFacingTransform != null
                ? playerFacingTransform
                : ResolveDefaultFacingTransform();
            EnsurePresentationExists();
            billboard.Initialize(camera);
            RefreshDirection();
        }

        public void SetMotion(Vector3 planarVelocity)
        {
            planarVelocity.y = 0f;
            float speed = planarVelocity.magnitude;
            motionAmount = Mathf.Clamp01(
                speed / Mathf.Max(0.1f, fullAnimationSpeed));
        }

        public void SetWasted(bool active)
        {
            isWasted = active;
        }

        public Sprite GetDirectionSprite(PlayerViewDirection direction)
        {
            return GetPartSprite(PlayerPuppetPart.Body, direction);
        }

        public Sprite GetPartSprite(
            PlayerPuppetPart part,
            PlayerViewDirection direction)
        {
            ValidatePart(part);
            ValidateDirection(direction);
            Sprite sprite = partSprites[(int)part, (int)direction];
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    "Player puppet sprites have not been initialized.");
            }

            return sprite;
        }

        public SpriteRenderer GetPartRenderer(PlayerPuppetPart part)
        {
            ValidatePart(part);
            int index = (int)part;
            if (index >= partRenderers.Count)
            {
                return null;
            }

            return partRenderers[index];
        }

        public Transform GetPartTransform(PlayerPuppetPart part)
        {
            ValidatePart(part);
            return partTransforms[(int)part];
        }

        private void Awake()
        {
            facingTransform = ResolveDefaultFacingTransform();
            EnsurePresentationExists();
        }

        private void Update()
        {
            EnsurePresentationExists();
        }

        private void LateUpdate()
        {
            RefreshDirection();
            AnimatePuppet(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (visualRoot != null)
            {
                DestroyGeneratedObject(visualRoot.gameObject);
                visualRoot = null;
                poseRoot = null;
            }

            for (int index = 0; index < generatedSprites.Count; index++)
            {
                DestroyGeneratedObject(generatedSprites[index]);
            }

            generatedSprites.Clear();
            directionSprites.Clear();
            partRenderers.Clear();
            Array.Clear(partTransforms, 0, partTransforms.Length);
            Array.Clear(partSprites, 0, partSprites.Length);
        }

        private Transform ResolveDefaultFacingTransform()
        {
            return transform.parent != null ? transform.parent : transform;
        }

        private void EnsurePresentationExists()
        {
            if (visualRoot != null)
            {
                return;
            }

            Texture2D atlas = Resources.Load<Texture2D>(AtlasResourcePath);
            ValidateAtlas(atlas);

            GameObject rootObject =
                new GameObject("GeneratedDirectionalPuppet");
            visualRoot = rootObject.transform;
            visualRoot.SetParent(transform, false);
            billboard = rootObject.AddComponent<BillboardSprite>();

            GameObject poseObject = new GameObject("PoseRoot");
            poseRoot = poseObject.transform;
            poseRoot.SetParent(visualRoot, false);

            CreatePartHierarchy();
            CreatePartSprites(atlas);

            directionSelector = new PlayerViewDirectionSelector(
                directionHysteresisDegrees,
                PlayerViewDirection.Front);
            ApplyDirection(PlayerViewDirection.Front);
            billboard.Initialize(targetCamera);
        }

        private void CreatePartHierarchy()
        {
            CreatePart(PlayerPuppetPart.Body, poseRoot);

            CreatePart(PlayerPuppetPart.LeftUpperArm, poseRoot);
            CreatePart(
                PlayerPuppetPart.LeftLowerArm,
                partTransforms[(int)PlayerPuppetPart.LeftUpperArm]);

            CreatePart(PlayerPuppetPart.RightUpperArm, poseRoot);
            CreatePart(
                PlayerPuppetPart.RightLowerArm,
                partTransforms[(int)PlayerPuppetPart.RightUpperArm]);

            CreatePart(PlayerPuppetPart.LeftUpperLeg, poseRoot);
            CreatePart(
                PlayerPuppetPart.LeftLowerLeg,
                partTransforms[(int)PlayerPuppetPart.LeftUpperLeg]);

            CreatePart(PlayerPuppetPart.RightUpperLeg, poseRoot);
            CreatePart(
                PlayerPuppetPart.RightLowerLeg,
                partTransforms[(int)PlayerPuppetPart.RightUpperLeg]);
        }

        private void CreatePart(
            PlayerPuppetPart part,
            Transform parent)
        {
            GameObject partObject = new GameObject(part.ToString());
            Transform partTransform = partObject.transform;
            partTransform.SetParent(parent, false);
            partTransforms[(int)part] = partTransform;

            SpriteRenderer spriteRenderer =
                partObject.AddComponent<SpriteRenderer>();
            spriteRenderer.color = Color.white;
            spriteRenderer.flipX = false;
            spriteRenderer.flipY = false;
            partRenderers.Add(spriteRenderer);
        }

        private void CreatePartSprites(Texture2D atlas)
        {
            for (int partIndex = 0;
                 partIndex < PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part = (PlayerPuppetPart)partIndex;
                for (int directionIndex = 0;
                     directionIndex < DirectionCount;
                     directionIndex++)
                {
                    PlayerViewDirection direction =
                        (PlayerViewDirection)directionIndex;
                    PlayerPuppetPose pose =
                        DirectionPoses[directionIndex];
                    Vector2 pivotPixels =
                        GetPartPivotPixels(part, pose);
                    Vector2 normalizedPivot = new Vector2(
                        pivotPixels.x / FrameWidth,
                        pivotPixels.y / FrameHeight);
                    Sprite sprite = Sprite.Create(
                        atlas,
                        new Rect(
                            directionIndex * FrameWidth,
                            partIndex * FrameHeight,
                            FrameWidth,
                            FrameHeight),
                        normalizedPivot,
                        PixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect);
                    sprite.name = $"Player{direction}{part}";
                    sprite.hideFlags = HideFlags.DontSave;
                    partSprites[partIndex, directionIndex] = sprite;
                    generatedSprites.Add(sprite);
                    if (part == PlayerPuppetPart.Body)
                    {
                        directionSprites.Add(sprite);
                    }
                }
            }
        }

        private static void ValidateAtlas(Texture2D atlas)
        {
            if (atlas == null)
            {
                throw new InvalidOperationException(
                    $"Player puppet atlas was not found at Resources/" +
                    $"{AtlasResourcePath}.");
            }

            int expectedWidth = FrameWidth * DirectionCount;
            int expectedHeight = FrameHeight * PartCount;
            if (atlas.width != expectedWidth ||
                atlas.height != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Player puppet atlas must be {expectedWidth}x" +
                    $"{expectedHeight}, but is {atlas.width}x" +
                    $"{atlas.height}.");
            }
        }

        private void RefreshDirection()
        {
            EnsurePresentationExists();
            Camera camera = targetCamera != null
                ? targetCamera
                : Camera.main;
            Transform actor = facingTransform != null
                ? facingTransform
                : ResolveDefaultFacingTransform();
            if (camera == null || actor == null)
            {
                return;
            }

            Vector3 toCamera = camera.transform.position - actor.position;
            toCamera = Vector3.ProjectOnPlane(toCamera, Vector3.up);
            Vector3 actorForward =
                Vector3.ProjectOnPlane(actor.forward, Vector3.up);
            if (toCamera.sqrMagnitude < 0.0001f ||
                actorForward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float signedAngle = Vector3.SignedAngle(
                actorForward,
                toCamera,
                Vector3.up);
            PlayerViewDirection direction =
                directionSelector.Select(signedAngle);
            ApplyDirection(direction);
        }

        private void ApplyDirection(PlayerViewDirection direction)
        {
            PlayerPuppetPose pose = DirectionPoses[(int)direction];
            for (int partIndex = 0;
                 partIndex < PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part = (PlayerPuppetPart)partIndex;
                SpriteRenderer spriteRenderer =
                    partRenderers[partIndex];
                Sprite sprite = GetPartSprite(part, direction);
                if (spriteRenderer.sprite != sprite)
                {
                    spriteRenderer.sprite = sprite;
                }

                spriteRenderer.sortingOrder =
                    GetSortingOrder(part);
                spriteRenderer.color = Color.white;
                spriteRenderer.flipX = false;
                spriteRenderer.flipY = false;
            }

            SetRestPositions(pose);
        }

        private void SetRestPositions(PlayerPuppetPose pose)
        {
            SetRootPartPosition(PlayerPuppetPart.Body, Vector2.zero);
            SetRootPartPosition(
                PlayerPuppetPart.LeftUpperArm,
                RootOffsetFromTopLeft(pose.LeftShoulder));
            SetChildPartPosition(
                PlayerPuppetPart.LeftLowerArm,
                ChildOffsetFromTopLeft(
                    pose.LeftShoulder,
                    pose.LeftElbow));
            SetRootPartPosition(
                PlayerPuppetPart.RightUpperArm,
                RootOffsetFromTopLeft(pose.RightShoulder));
            SetChildPartPosition(
                PlayerPuppetPart.RightLowerArm,
                ChildOffsetFromTopLeft(
                    pose.RightShoulder,
                    pose.RightElbow));
            SetRootPartPosition(
                PlayerPuppetPart.LeftUpperLeg,
                RootOffsetFromTopLeft(pose.LeftHip));
            SetChildPartPosition(
                PlayerPuppetPart.LeftLowerLeg,
                ChildOffsetFromTopLeft(
                    pose.LeftHip,
                    pose.LeftKnee));
            SetRootPartPosition(
                PlayerPuppetPart.RightUpperLeg,
                RootOffsetFromTopLeft(pose.RightHip));
            SetChildPartPosition(
                PlayerPuppetPart.RightLowerLeg,
                ChildOffsetFromTopLeft(
                    pose.RightHip,
                    pose.RightKnee));
        }

        private void SetRootPartPosition(
            PlayerPuppetPart part,
            Vector2 localPosition)
        {
            Transform partTransform = partTransforms[(int)part];
            partTransform.localPosition =
                new Vector3(localPosition.x, localPosition.y, 0f);
            partTransform.localScale = Vector3.one;
        }

        private void SetChildPartPosition(
            PlayerPuppetPart part,
            Vector2 localPosition)
        {
            SetRootPartPosition(part, localPosition);
        }

        private void AnimatePuppet(float deltaTime)
        {
            if (poseRoot == null)
            {
                return;
            }

            if (motionAmount > MovingThreshold)
            {
                animationPhase +=
                    deltaTime * walkCyclesPerSecond * Mathf.PI * 2f;
            }

            wastedBlend = Mathf.MoveTowards(
                wastedBlend,
                isWasted ? 1f : 0f,
                deltaTime * 4f);
            wastedPhase += deltaTime * 4.5f;

            Vector3 walkRotationAxis =
                GetWalkRotationAxis(CurrentDirection);
            float authoredMotionScale =
                DirectionPoses[(int)CurrentDirection].MotionScale;
            float depthWeight = Mathf.Abs(walkRotationAxis.x);
            float motionScale = Mathf.Lerp(
                authoredMotionScale,
                1f,
                depthWeight);
            float strideWave =
                Mathf.Sin(animationPhase) * motionAmount * motionScale;
            float stepWave =
                Mathf.Abs(Mathf.Sin(animationPhase * 2f)) *
                motionAmount;
            float leftArmPhase = -strideWave;
            float rightArmPhase = strideWave;
            float leftLegPhase = strideWave;
            float rightLegPhase = -strideWave;
            float settle = 1f - Mathf.Exp(-settleSpeed * deltaTime);

            SetJointRotation(
                PlayerPuppetPart.LeftUpperArm,
                walkRotationAxis,
                leftArmPhase * armSwingDegrees,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightUpperArm,
                walkRotationAxis,
                rightArmPhase * armSwingDegrees,
                settle);
            SetJointRotation(
                PlayerPuppetPart.LeftLowerArm,
                walkRotationAxis,
                -leftArmPhase * elbowBendDegrees,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightLowerArm,
                walkRotationAxis,
                -rightArmPhase * elbowBendDegrees,
                settle);

            SetJointRotation(
                PlayerPuppetPart.LeftUpperLeg,
                walkRotationAxis,
                leftLegPhase * legSwingDegrees,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightUpperLeg,
                walkRotationAxis,
                rightLegPhase * legSwingDegrees,
                settle);
            SetJointRotation(
                PlayerPuppetPart.LeftLowerLeg,
                walkRotationAxis,
                -Mathf.Max(0f, leftLegPhase) *
                kneeBendDegrees,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightLowerLeg,
                walkRotationAxis,
                -Mathf.Max(0f, rightLegPhase) *
                kneeBendDegrees,
                settle);

            ApplyLimbDepthSorting(
                PlayerPuppetPart.LeftUpperArm,
                PlayerPuppetPart.LeftLowerArm);
            ApplyLimbDepthSorting(
                PlayerPuppetPart.RightUpperArm,
                PlayerPuppetPart.RightLowerArm);
            ApplyLimbDepthSorting(
                PlayerPuppetPart.LeftUpperLeg,
                PlayerPuppetPart.LeftLowerLeg);
            ApplyLimbDepthSorting(
                PlayerPuppetPart.RightUpperLeg,
                PlayerPuppetPart.RightLowerLeg);

            float targetX =
                Mathf.Sin(wastedPhase) * 0.055f * wastedBlend;
            float targetY =
                stepWave * walkBobHeight +
                Mathf.Abs(Mathf.Sin(wastedPhase * 0.5f)) *
                0.018f *
                wastedBlend;
            float targetRoll =
                strideWave * walkRockDegrees +
                Mathf.Sin(wastedPhase * 0.7f) *
                3.5f *
                wastedBlend;

            poseRoot.localPosition = Vector3.Lerp(
                poseRoot.localPosition,
                new Vector3(targetX, targetY, 0f),
                settle);
            poseRoot.localRotation = Quaternion.Slerp(
                poseRoot.localRotation,
                Quaternion.Euler(0f, 0f, targetRoll),
                settle);
            poseRoot.localScale = Vector3.one;
        }

        private void SetJointRotation(
            PlayerPuppetPart part,
            Vector3 rotationAxis,
            float targetDegrees,
            float interpolation)
        {
            Transform partTransform = partTransforms[(int)part];
            Quaternion targetRotation = Quaternion.AngleAxis(
                targetDegrees,
                rotationAxis);
            partTransform.localRotation = Quaternion.Slerp(
                partTransform.localRotation,
                targetRotation,
                interpolation);
            partTransform.localScale = Vector3.one;
        }

        private void ApplyLimbDepthSorting(
            PlayerPuppetPart upperPart,
            PlayerPuppetPart lowerPart)
        {
            Transform upperTransform =
                partTransforms[(int)upperPart];
            float depth =
                (upperTransform.localRotation * Vector3.down).z;
            int sortingOrder = GetSortingOrder(upperPart);
            if (depth < -DepthSortingThreshold)
            {
                sortingOrder -= FarLimbSortingOffset;
            }

            partRenderers[(int)upperPart].sortingOrder =
                sortingOrder;
            partRenderers[(int)lowerPart].sortingOrder =
                sortingOrder;
        }

        private static Vector3 GetWalkRotationAxis(
            PlayerViewDirection direction)
        {
            float viewAngle =
                (int)direction * 45f * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Cos(viewAngle),
                0f,
                Mathf.Sin(viewAngle)).normalized;
        }

        private static Vector2 GetPartPivotPixels(
            PlayerPuppetPart part,
            PlayerPuppetPose pose)
        {
            Vector2 topLeft;
            switch (part)
            {
                case PlayerPuppetPart.Body:
                    return new Vector2(
                        FeetPivotXPixels,
                        FeetPivotPixels);
                case PlayerPuppetPart.LeftUpperArm:
                    topLeft = pose.LeftShoulder;
                    break;
                case PlayerPuppetPart.LeftLowerArm:
                    topLeft = pose.LeftElbow;
                    break;
                case PlayerPuppetPart.RightUpperArm:
                    topLeft = pose.RightShoulder;
                    break;
                case PlayerPuppetPart.RightLowerArm:
                    topLeft = pose.RightElbow;
                    break;
                case PlayerPuppetPart.LeftUpperLeg:
                    topLeft = pose.LeftHip;
                    break;
                case PlayerPuppetPart.LeftLowerLeg:
                    topLeft = pose.LeftKnee;
                    break;
                case PlayerPuppetPart.RightUpperLeg:
                    topLeft = pose.RightHip;
                    break;
                case PlayerPuppetPart.RightLowerLeg:
                    topLeft = pose.RightKnee;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(part),
                        part,
                        "Unknown player puppet part.");
            }

            return TopLeftToBottomPixels(topLeft);
        }

        private static Vector2 RootOffsetFromTopLeft(
            Vector2 topLeft)
        {
            Vector2 bottomPixels = TopLeftToBottomPixels(topLeft);
            return new Vector2(
                (bottomPixels.x - FeetPivotXPixels) /
                PixelsPerUnit,
                (bottomPixels.y - FeetPivotPixels) /
                PixelsPerUnit);
        }

        private static Vector2 ChildOffsetFromTopLeft(
            Vector2 parentTopLeft,
            Vector2 childTopLeft)
        {
            Vector2 parentBottom =
                TopLeftToBottomPixels(parentTopLeft);
            Vector2 childBottom =
                TopLeftToBottomPixels(childTopLeft);
            return (childBottom - parentBottom) / PixelsPerUnit;
        }

        private static Vector2 TopLeftToBottomPixels(
            Vector2 topLeft)
        {
            return new Vector2(
                topLeft.x,
                FrameHeight - topLeft.y);
        }

        private static int GetSortingOrder(PlayerPuppetPart part)
        {
            switch (part)
            {
                case PlayerPuppetPart.Body:
                    return 0;
                case PlayerPuppetPart.LeftUpperLeg:
                case PlayerPuppetPart.LeftLowerLeg:
                    return 1;
                case PlayerPuppetPart.RightUpperLeg:
                case PlayerPuppetPart.RightLowerLeg:
                    return 2;
                case PlayerPuppetPart.LeftUpperArm:
                case PlayerPuppetPart.LeftLowerArm:
                    return 3;
                case PlayerPuppetPart.RightUpperArm:
                case PlayerPuppetPart.RightLowerArm:
                    return 4;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(part),
                        part,
                        "Unknown player puppet part.");
            }
        }

        private static void ValidatePart(PlayerPuppetPart part)
        {
            int index = (int)part;
            if (index < 0 || index >= PartCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(part),
                    part,
                    "Part must be one of the nine puppet layers.");
            }
        }

        private static void ValidateDirection(
            PlayerViewDirection direction)
        {
            int index = (int)direction;
            if (index < 0 || index >= DirectionCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction),
                    direction,
                    "Direction must be one of the eight defined views.");
            }
        }

        private static void DestroyGeneratedObject(
            UnityEngine.Object generatedObject)
        {
            if (generatedObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedObject);
            }
            else
            {
                DestroyImmediate(generatedObject);
            }
        }

        private readonly struct PlayerPuppetPose
        {
            public PlayerPuppetPose(
                Vector2 leftShoulder,
                Vector2 leftElbow,
                Vector2 rightShoulder,
                Vector2 rightElbow,
                Vector2 leftHip,
                Vector2 leftKnee,
                Vector2 rightHip,
                Vector2 rightKnee,
                float motionScale)
            {
                LeftShoulder = leftShoulder;
                LeftElbow = leftElbow;
                RightShoulder = rightShoulder;
                RightElbow = rightElbow;
                LeftHip = leftHip;
                LeftKnee = leftKnee;
                RightHip = rightHip;
                RightKnee = rightKnee;
                MotionScale = motionScale;
            }

            public Vector2 LeftShoulder { get; }
            public Vector2 LeftElbow { get; }
            public Vector2 RightShoulder { get; }
            public Vector2 RightElbow { get; }
            public Vector2 LeftHip { get; }
            public Vector2 LeftKnee { get; }
            public Vector2 RightHip { get; }
            public Vector2 RightKnee { get; }
            public float MotionScale { get; }
        }
    }
}
