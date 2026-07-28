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
        public const string ExpressionAtlasResourcePath =
            "Player/PlayerDirectionalBodyExpressionsAtlas";
        public const int DirectionCount = 8;
        public const int PartCount = 9;
        public const int ExpressionCount = 5;
        public const int FrameWidth = 64;
        public const int FrameHeight = 96;
        public const float PixelsPerUnit = 48f;
        public const float FeetPivotXPixels = 32f;
        public const float FeetPivotPixels = 4f;

        private const float MovingThreshold = 0.02f;
        private const float DepthSortingThreshold = 0.005f;
        private const float IdleDepthSortingThreshold = 0.01f;
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
                new Vector2(21.5f, 92f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                new Vector2(41f, 92f),
                0.55f),
            new PlayerPuppetPose(
                new Vector2(25f, 30f),
                new Vector2(21f, 46f),
                new Vector2(39f, 30f),
                new Vector2(43f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(23f, 91f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                new Vector2(37f, 92f),
                0.8f),
            new PlayerPuppetPose(
                new Vector2(32f, 30f),
                new Vector2(32f, 46f),
                new Vector2(35f, 30f),
                new Vector2(36f, 46f),
                new Vector2(30f, 56f),
                new Vector2(30f, 75f),
                new Vector2(30.5f, 92f),
                new Vector2(34f, 56f),
                new Vector2(35f, 75f),
                new Vector2(30.5f, 92f),
                1f),
            new PlayerPuppetPose(
                new Vector2(24f, 30f),
                new Vector2(21f, 46f),
                new Vector2(40f, 30f),
                new Vector2(44f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(23f, 92f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                new Vector2(38.5f, 92f),
                0.8f),
            new PlayerPuppetPose(
                new Vector2(23f, 30f),
                new Vector2(20f, 46f),
                new Vector2(41f, 30f),
                new Vector2(44f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(23f, 92f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                new Vector2(40f, 92f),
                0.55f),
            new PlayerPuppetPose(
                new Vector2(25f, 30f),
                new Vector2(22f, 46f),
                new Vector2(39f, 30f),
                new Vector2(42f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(25f, 92f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                new Vector2(36.5f, 92f),
                0.8f),
            new PlayerPuppetPose(
                new Vector2(29f, 30f),
                new Vector2(29f, 46f),
                new Vector2(32f, 30f),
                new Vector2(32f, 46f),
                new Vector2(30f, 56f),
                new Vector2(29f, 75f),
                new Vector2(31f, 92f),
                new Vector2(34f, 56f),
                new Vector2(34f, 75f),
                new Vector2(31f, 92f),
                1f),
            new PlayerPuppetPose(
                new Vector2(24f, 30f),
                new Vector2(20f, 46f),
                new Vector2(40f, 30f),
                new Vector2(43f, 46f),
                new Vector2(28f, 56f),
                new Vector2(27f, 75f),
                new Vector2(26f, 92f),
                new Vector2(36f, 56f),
                new Vector2(37f, 75f),
                new Vector2(39.5f, 91f),
                0.8f)
        };

        [Header("Direction")]
        [SerializeField, Range(0f, 10f)]
        private float directionHysteresisDegrees = 5f;

        [Header("Jointed walk")]
        [SerializeField, Min(0.1f)] private float fullAnimationSpeed = 5.2f;
        [SerializeField, Min(0.1f)] private float walkCycleDistance = 2.7f;
        [SerializeField, Range(0f, 60f)] private float armSwingDegrees = 24f;
        [SerializeField, Range(0f, 60f)] private float elbowBendDegrees = 11f;
        [SerializeField, Range(0f, 60f)] private float legSwingDegrees = 28f;
        [SerializeField, Range(0f, 60f)] private float kneeBendDegrees = 17f;
        [SerializeField, Min(0f)]
        private float walkBodyCompressionHeight = 0.012f;
        [SerializeField, Min(0f)]
        private float footPlantCompressionHeight = 0.005f;
        [SerializeField, Range(0f, 10f)] private float walkRockDegrees = 1.8f;
        [SerializeField, Min(0f)] private float settleSpeed = 8f;

        [Header("Living idle")]
        [SerializeField, Min(1f)] private float idleBreathingPeriod = 3.6f;
        [SerializeField, Min(0f)] private float idleBreathingHeight = 0.01f;
        [SerializeField, Min(1f)] private float idleWeightShiftPeriod = 5.8f;
        [SerializeField, Min(0f)] private float idleWeightShiftDistance = 0.0075f;
        [SerializeField, Range(0f, 2f)]
        private float idleWeightShiftDegrees = 0.6f;
        [SerializeField, Min(2f)] private float idleFidgetPeriod = 6.8f;
        [SerializeField, Range(0f, 5f)]
        private float idleArmFidgetDegrees = 1.8f;
        [SerializeField, Min(0f)] private float idleBlendSpeed = 2.5f;

        private readonly List<Sprite> directionSprites =
            new List<Sprite>(DirectionCount);
        private readonly List<Sprite> generatedSprites =
            new List<Sprite>(
                DirectionCount * (PartCount + ExpressionCount));
        private readonly List<SpriteRenderer> partRenderers =
            new List<SpriteRenderer>(PartCount);

        private readonly Transform[] partTransforms =
            new Transform[PartCount];
        private readonly Sprite[,] partSprites =
            new Sprite[PartCount, DirectionCount];
        private readonly Sprite[,] expressionSprites =
            new Sprite[ExpressionCount, DirectionCount];
        private readonly PlayerFacialAnimationState facialAnimationState =
            new PlayerFacialAnimationState();

        private Camera targetCamera;
        private Transform facingTransform;
        private Transform visualRoot;
        private Transform poseRoot;
        private BillboardSprite billboard;
        private PlayerViewDirectionSelector directionSelector;
        private float animationPhase;
        private float motionSpeed;
        private float motionAmount;
        private float idlePhase;
        private float idleBlend;
        private float intoxicationTarget;
        private float intoxicationBlend;
        private float intoxicationPhase;
        private float balanceLeanTarget;
        private float balanceLean;
        private float fallAmountTarget;
        private float fallAmount;
        private float fallDirection = 1f;
        private float footPlantAmount = 1f;
        private Vector3 upperBodyOffset;

        public PlayerViewDirection CurrentDirection =>
            directionSelector != null
                ? directionSelector.CurrentDirection
                : PlayerViewDirection.Front;
        public PlayerFacialExpression CurrentFacialExpression =>
            facialAnimationState.CurrentExpression;

        public IReadOnlyList<Sprite> DirectionSprites => directionSprites;
        public IReadOnlyList<SpriteRenderer> Renderers => partRenderers;
        public SpriteRenderer Renderer => GetPartRenderer(
            PlayerPuppetPart.Body);
        public SpriteRenderer BodyRenderer => Renderer;
        public Transform VisualRoot => visualRoot;
        public Transform PoseRoot => poseRoot;
        public float FootPlantAmount => footPlantAmount;
        public float IntoxicationAmount => intoxicationBlend;
        public float BalanceLean => balanceLean;
        public float FallAmount => fallAmount;
        public float FallDirection => fallDirection;
        public Vector3 UpperBodyOffset => upperBodyOffset;
        public Vector3 LeftFootContactWorldPosition =>
            GetFootContactWorldPosition(true);
        public Vector3 RightFootContactWorldPosition =>
            GetFootContactWorldPosition(false);

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
            facialAnimationState.Reset();
            RefreshDirection();
        }

        public void SetMotion(Vector3 planarVelocity)
        {
            planarVelocity.y = 0f;
            float speed = planarVelocity.magnitude;
            motionSpeed = speed;
            motionAmount = Mathf.Clamp01(
                speed / Mathf.Max(0.1f, fullAnimationSpeed));
        }

        public void SetIntoxication(float intensity)
        {
            intoxicationTarget = Mathf.Clamp01(intensity);
        }

        public void SetBalancePose(float signedLean)
        {
            balanceLeanTarget = Mathf.Clamp(
                signedLean,
                -1f,
                1f);
        }

        public void SetFallPose(
            float signedDirection,
            float amount)
        {
            if (!Mathf.Approximately(signedDirection, 0f))
            {
                fallDirection = Mathf.Sign(signedDirection);
            }

            fallAmountTarget = Mathf.Clamp01(amount);
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

        public Sprite GetFacialExpressionSprite(
            PlayerFacialExpression expression,
            PlayerViewDirection direction)
        {
            ValidateExpression(expression);
            ValidateDirection(direction);
            Sprite sprite =
                expressionSprites[(int)expression, (int)direction];
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    "Player facial sprites have not been initialized.");
            }

            return sprite;
        }

        public Transform GetPartTransform(PlayerPuppetPart part)
        {
            ValidatePart(part);
            return partTransforms[(int)part];
        }

        internal Vector3 GetPartPoseLocalPosition(
            PlayerPuppetPart part,
            PlayerViewDirection direction)
        {
            ValidatePart(part);
            ValidateDirection(direction);
            return GetPartPoseLocalPosition(
                part,
                DirectionPoses[(int)direction]);
        }

        internal Quaternion GetPartPoseLocalRotation(
            PlayerPuppetPart part,
            PlayerViewDirection direction)
        {
            ValidatePart(part);
            ValidateDirection(direction);
            Quaternion sourceRotation =
                partTransforms[(int)part].localRotation;
            Vector3 sourceAxis =
                GetWalkRotationAxis(CurrentDirection);
            float sinHalfAngle = Vector3.Dot(
                new Vector3(
                    sourceRotation.x,
                    sourceRotation.y,
                    sourceRotation.z),
                sourceAxis);
            float signedAngle = Mathf.DeltaAngle(
                0f,
                2f *
                Mathf.Atan2(sinHalfAngle, sourceRotation.w) *
                Mathf.Rad2Deg);
            return Quaternion.AngleAxis(
                signedAngle,
                GetWalkRotationAxis(direction));
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
            facialAnimationState.Advance(
                Time.deltaTime,
                motionAmount <= MovingThreshold &&
                intoxicationBlend <= 0.35f &&
                Mathf.Abs(balanceLean) <= 0.05f &&
                fallAmount <= 0.01f);
            ApplyFacialExpression(CurrentDirection);
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
            Array.Clear(
                expressionSprites,
                0,
                expressionSprites.Length);
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
            Texture2D expressionAtlas = Resources.Load<Texture2D>(
                ExpressionAtlasResourcePath);
            ValidateExpressionAtlas(expressionAtlas);

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
            CreateExpressionSprites(expressionAtlas);

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

        private void CreateExpressionSprites(Texture2D atlas)
        {
            Vector2 normalizedPivot = new Vector2(
                FeetPivotXPixels / FrameWidth,
                FeetPivotPixels / FrameHeight);
            for (int expressionIndex = 0;
                 expressionIndex < ExpressionCount;
                 expressionIndex++)
            {
                PlayerFacialExpression expression =
                    (PlayerFacialExpression)expressionIndex;
                for (int directionIndex = 0;
                     directionIndex < DirectionCount;
                     directionIndex++)
                {
                    PlayerViewDirection direction =
                        (PlayerViewDirection)directionIndex;
                    Sprite sprite = Sprite.Create(
                        atlas,
                        new Rect(
                            directionIndex * FrameWidth,
                            expressionIndex * FrameHeight,
                            FrameWidth,
                            FrameHeight),
                        normalizedPivot,
                        PixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect);
                    sprite.name =
                        $"Player{direction}Body{expression}";
                    sprite.hideFlags = HideFlags.DontSave;
                    expressionSprites[
                        expressionIndex,
                        directionIndex] = sprite;
                    generatedSprites.Add(sprite);
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

        private static void ValidateExpressionAtlas(Texture2D atlas)
        {
            if (atlas == null)
            {
                throw new InvalidOperationException(
                    "Player facial atlas was not found at Resources/" +
                    $"{ExpressionAtlasResourcePath}.");
            }

            int expectedWidth = FrameWidth * DirectionCount;
            int expectedHeight = FrameHeight * ExpressionCount;
            if (atlas.width != expectedWidth ||
                atlas.height != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Player facial atlas must be {expectedWidth}x" +
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
            ApplyFacialExpression(direction);
        }

        private void ApplyFacialExpression(
            PlayerViewDirection direction)
        {
            Sprite bodySprite;
            PlayerFacialExpression expression =
                facialAnimationState.CurrentExpression;
            if (expression == PlayerFacialExpression.Neutral ||
                !HasVisibleFace(direction))
            {
                bodySprite = GetPartSprite(
                    PlayerPuppetPart.Body,
                    direction);
            }
            else
            {
                bodySprite = GetFacialExpressionSprite(
                    expression,
                    direction);
            }

            SpriteRenderer bodyRenderer = BodyRenderer;
            if (bodyRenderer.sprite != bodySprite)
            {
                bodyRenderer.sprite = bodySprite;
            }
        }

        private static bool HasVisibleFace(
            PlayerViewDirection direction)
        {
            return direction == PlayerViewDirection.Front ||
                   direction == PlayerViewDirection.FrontRight ||
                   direction == PlayerViewDirection.Right ||
                   direction == PlayerViewDirection.Left ||
                   direction == PlayerViewDirection.FrontLeft;
        }

        private void SetRestPositions(PlayerPuppetPose pose)
        {
            for (int partIndex = 0;
                 partIndex < PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                Transform partTransform =
                    partTransforms[partIndex];
                partTransform.localPosition =
                    GetPartPoseLocalPosition(part, pose);
                partTransform.localScale = Vector3.one;
            }
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

        private Vector3 GetPartPoseLocalPosition(
            PlayerPuppetPart part,
            PlayerPuppetPose pose)
        {
            Vector2 bodyOffset = new Vector2(
                upperBodyOffset.x,
                upperBodyOffset.y);
            Vector2 localPosition;
            switch (part)
            {
                case PlayerPuppetPart.Body:
                    localPosition = bodyOffset;
                    break;
                case PlayerPuppetPart.LeftUpperArm:
                    localPosition =
                        RootOffsetFromTopLeft(pose.LeftShoulder) +
                        bodyOffset;
                    break;
                case PlayerPuppetPart.LeftLowerArm:
                    localPosition = ChildOffsetFromTopLeft(
                        pose.LeftShoulder,
                        pose.LeftElbow);
                    break;
                case PlayerPuppetPart.RightUpperArm:
                    localPosition =
                        RootOffsetFromTopLeft(pose.RightShoulder) +
                        bodyOffset;
                    break;
                case PlayerPuppetPart.RightLowerArm:
                    localPosition = ChildOffsetFromTopLeft(
                        pose.RightShoulder,
                        pose.RightElbow);
                    break;
                case PlayerPuppetPart.LeftUpperLeg:
                    localPosition =
                        RootOffsetFromTopLeft(pose.LeftHip);
                    break;
                case PlayerPuppetPart.LeftLowerLeg:
                    localPosition = ChildOffsetFromTopLeft(
                        pose.LeftHip,
                        pose.LeftKnee);
                    break;
                case PlayerPuppetPart.RightUpperLeg:
                    localPosition =
                        RootOffsetFromTopLeft(pose.RightHip);
                    break;
                case PlayerPuppetPart.RightLowerLeg:
                    localPosition = ChildOffsetFromTopLeft(
                        pose.RightHip,
                        pose.RightKnee);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(part),
                        part,
                        "Unknown player puppet part.");
            }

            return new Vector3(
                localPosition.x,
                localPosition.y,
                0f);
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
                    deltaTime *
                    motionSpeed /
                    Mathf.Max(0.1f, walkCycleDistance) *
                    Mathf.PI *
                    2f;
            }

            idlePhase += deltaTime;
            idleBlend = Mathf.MoveTowards(
                idleBlend,
                motionAmount <= MovingThreshold ? 1f : 0f,
                deltaTime * idleBlendSpeed);
            intoxicationBlend = Mathf.MoveTowards(
                intoxicationBlend,
                intoxicationTarget,
                deltaTime / 0.7f);
            balanceLean = Mathf.MoveTowards(
                balanceLean,
                balanceLeanTarget,
                deltaTime * 4f);
            fallAmount = Mathf.MoveTowards(
                fallAmount,
                fallAmountTarget,
                deltaTime * 5f);
            intoxicationPhase +=
                deltaTime *
                Mathf.Lerp(1.4f, 4.2f, intoxicationBlend);

            PlayerIntoxicationPose intoxicationPose =
                PlayerIntoxicationPoseEvaluator.Evaluate(
                    intoxicationBlend,
                    intoxicationPhase,
                    balanceLean,
                    fallDirection,
                    fallAmount);

            PlayerPuppetPose activePose =
                DirectionPoses[(int)CurrentDirection];
            Vector3 walkRotationAxis =
                GetWalkRotationAxis(CurrentDirection);
            float authoredMotionScale = activePose.MotionScale;
            float depthWeight = Mathf.Abs(walkRotationAxis.x);
            float motionScale = Mathf.Lerp(
                authoredMotionScale,
                1f,
                depthWeight);
            float strideWave =
                Mathf.Sin(animationPhase) * motionAmount * motionScale;
            float normalizedFootfall = Mathf.Pow(
                1f - Mathf.Abs(Mathf.Sin(animationPhase)),
                3f);
            float walkingFootfall = normalizedFootfall * motionAmount;
            footPlantAmount = motionAmount > MovingThreshold
                ? normalizedFootfall
                : 1f;
            float leftArmPhase = -strideWave;
            float rightArmPhase = strideWave;
            float leftLegPhase = strideWave;
            float rightLegPhase = -strideWave;
            float effectiveIdleBlend =
                idleBlend *
                Mathf.Lerp(1f, 0.08f, intoxicationBlend);
            float breathingWave = Mathf.Sin(
                idlePhase * Mathf.PI * 2f /
                Mathf.Max(1f, idleBreathingPeriod));
            float weightShiftWave = Mathf.Sin(
                idlePhase * Mathf.PI * 2f /
                Mathf.Max(1f, idleWeightShiftPeriod));
            float fidgetWave = GetIdleFidgetWave(
                idlePhase,
                out bool fidgetLeftArm);
            float gestureIdleBlend =
                idleBlend *
                (1f - intoxicationBlend) *
                (1f - intoxicationBlend);
            float leftGestureWeight =
                fidgetLeftArm ? 1f : 0.14f;
            float rightGestureWeight =
                fidgetLeftArm ? 0.14f : 1f;
            float idleLeftUpperArm =
                breathingWave * 0.2f * effectiveIdleBlend +
                fidgetWave *
                idleArmFidgetDegrees *
                leftGestureWeight *
                gestureIdleBlend;
            float idleLeftLowerArm =
                -breathingWave * 0.1f * effectiveIdleBlend -
                fidgetWave *
                idleArmFidgetDegrees *
                0.5f *
                leftGestureWeight *
                gestureIdleBlend;
            float idleRightUpperArm =
                -breathingWave * 0.16f * effectiveIdleBlend -
                fidgetWave *
                idleArmFidgetDegrees *
                rightGestureWeight *
                gestureIdleBlend;
            float idleRightLowerArm =
                breathingWave * 0.08f * effectiveIdleBlend +
                fidgetWave *
                idleArmFidgetDegrees *
                0.5f *
                rightGestureWeight *
                gestureIdleBlend;
            float idleLegShift =
                weightShiftWave * 0.22f * effectiveIdleBlend;
            float settle = 1f - Mathf.Exp(-settleSpeed * deltaTime);

            SetJointRotation(
                PlayerPuppetPart.LeftUpperArm,
                walkRotationAxis,
                leftArmPhase * armSwingDegrees +
                idleLeftUpperArm -
                intoxicationPose.ArmSpread,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightUpperArm,
                walkRotationAxis,
                rightArmPhase * armSwingDegrees +
                idleRightUpperArm +
                intoxicationPose.ArmSpread,
                settle);
            SetJointRotation(
                PlayerPuppetPart.LeftLowerArm,
                walkRotationAxis,
                -leftArmPhase * elbowBendDegrees +
                idleLeftLowerArm -
                intoxicationPose.ArmSpread * 0.38f,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightLowerArm,
                walkRotationAxis,
                -rightArmPhase * elbowBendDegrees +
                idleRightLowerArm +
                intoxicationPose.ArmSpread * 0.38f,
                settle);

            SetJointRotation(
                PlayerPuppetPart.LeftUpperLeg,
                walkRotationAxis,
                leftLegPhase * legSwingDegrees +
                idleLegShift +
                intoxicationPose.KneeBend * 0.18f,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightUpperLeg,
                walkRotationAxis,
                rightLegPhase * legSwingDegrees -
                idleLegShift -
                intoxicationPose.KneeBend * 0.18f,
                settle);
            SetJointRotation(
                PlayerPuppetPart.LeftLowerLeg,
                walkRotationAxis,
                -Mathf.Max(0f, leftLegPhase) *
                kneeBendDegrees -
                idleLegShift * 0.5f -
                intoxicationPose.KneeBend,
                settle);
            SetJointRotation(
                PlayerPuppetPart.RightLowerLeg,
                walkRotationAxis,
                -Mathf.Max(0f, rightLegPhase) *
                kneeBendDegrees +
                idleLegShift * 0.5f -
                intoxicationPose.KneeBend,
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
                weightShiftWave *
                idleWeightShiftDistance *
                effectiveIdleBlend +
                intoxicationPose.BodyOffsetX;
            float targetUpperBodyY =
                (0.5f + breathingWave * 0.5f) *
                idleBreathingHeight *
                effectiveIdleBlend +
                intoxicationPose.BodyLift -
                walkingFootfall *
                walkBodyCompressionHeight;
            float targetRoll =
                strideWave * walkRockDegrees +
                weightShiftWave *
                idleWeightShiftDegrees *
                effectiveIdleBlend +
                intoxicationPose.BodyRoll;

            poseRoot.localRotation = Quaternion.Slerp(
                poseRoot.localRotation,
                Quaternion.Euler(0f, 0f, targetRoll),
                settle);
            Vector3 posePosition = poseRoot.localPosition;
            posePosition.x = Mathf.Lerp(
                posePosition.x,
                targetX,
                settle);
            posePosition.z = 0f;
            poseRoot.localPosition = posePosition;

            float groundedPoseY = CalculateGroundedPoseY();
            posePosition.y =
                groundedPoseY -
                walkingFootfall *
                footPlantCompressionHeight;
            poseRoot.localPosition = posePosition;
            ApplyUpperBodyOffset(
                activePose,
                new Vector3(0f, targetUpperBodyY, 0f),
                settle);
            poseRoot.localScale = Vector3.one;
            footPlantAmount *= 1f - fallAmount;
        }

        private float CalculateGroundedPoseY()
        {
            float leftFootY = visualRoot.InverseTransformPoint(
                GetFootContactWorldPosition(true)).y;
            float rightFootY = visualRoot.InverseTransformPoint(
                GetFootContactWorldPosition(false)).y;
            return poseRoot.localPosition.y -
                   Mathf.Min(leftFootY, rightFootY);
        }

        private Vector3 GetFootContactWorldPosition(bool left)
        {
            if (poseRoot == null)
            {
                return transform.position;
            }

            PlayerPuppetPose pose =
                DirectionPoses[(int)CurrentDirection];
            PlayerPuppetPart lowerLeg = left
                ? PlayerPuppetPart.LeftLowerLeg
                : PlayerPuppetPart.RightLowerLeg;
            Vector2 knee = left ? pose.LeftKnee : pose.RightKnee;
            Vector2 foot = left ? pose.LeftFoot : pose.RightFoot;
            Vector2 localContact =
                (TopLeftToBottomPixels(foot) -
                 TopLeftToBottomPixels(knee)) /
                PixelsPerUnit;
            return partTransforms[(int)lowerLeg].TransformPoint(
                new Vector3(
                    localContact.x,
                    localContact.y,
                    0f));
        }

        private void ApplyUpperBodyOffset(
            PlayerPuppetPose pose,
            Vector3 targetOffset,
            float interpolation)
        {
            upperBodyOffset = Vector3.Lerp(
                upperBodyOffset,
                targetOffset,
                interpolation);
            Vector2 offset = new Vector2(
                upperBodyOffset.x,
                upperBodyOffset.y);
            SetRootPartPosition(PlayerPuppetPart.Body, offset);
            SetRootPartPosition(
                PlayerPuppetPart.LeftUpperArm,
                RootOffsetFromTopLeft(pose.LeftShoulder) +
                offset);
            SetRootPartPosition(
                PlayerPuppetPart.RightUpperArm,
                RootOffsetFromTopLeft(pose.RightShoulder) +
                offset);
        }

        private float GetIdleFidgetWave(
            float phase,
            out bool useLeftArm)
        {
            const float startDelay = 0.55f;
            const float duration = 1.35f;
            float period = Mathf.Max(duration + startDelay, idleFidgetPeriod);
            float shiftedPhase = phase + period - startDelay;
            int cycleIndex = Mathf.FloorToInt(
                (phase - startDelay) / period);
            useLeftArm = cycleIndex % 2 == 0;
            float localPhase = Mathf.Repeat(
                shiftedPhase,
                period);
            if (localPhase > duration)
            {
                return 0f;
            }

            float normalizedPhase = localPhase / duration;
            float pulse = Mathf.Sin(normalizedPhase * Mathf.PI);
            return pulse * pulse;
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
            float sortingThreshold =
                motionAmount > MovingThreshold
                    ? DepthSortingThreshold
                    : IdleDepthSortingThreshold;
            if (depth < -sortingThreshold)
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

        private static void ValidateExpression(
            PlayerFacialExpression expression)
        {
            int index = (int)expression;
            if (index < 0 || index >= ExpressionCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expression),
                    expression,
                    "Expression must be one of the three blink states.");
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
                Vector2 leftFoot,
                Vector2 rightHip,
                Vector2 rightKnee,
                Vector2 rightFoot,
                float motionScale)
            {
                LeftShoulder = leftShoulder;
                LeftElbow = leftElbow;
                RightShoulder = rightShoulder;
                RightElbow = rightElbow;
                LeftHip = leftHip;
                LeftKnee = leftKnee;
                LeftFoot = leftFoot;
                RightHip = rightHip;
                RightKnee = rightKnee;
                RightFoot = rightFoot;
                MotionScale = motionScale;
            }

            public Vector2 LeftShoulder { get; }
            public Vector2 LeftElbow { get; }
            public Vector2 RightShoulder { get; }
            public Vector2 RightElbow { get; }
            public Vector2 LeftHip { get; }
            public Vector2 LeftKnee { get; }
            public Vector2 LeftFoot { get; }
            public Vector2 RightHip { get; }
            public Vector2 RightKnee { get; }
            public Vector2 RightFoot { get; }
            public float MotionScale { get; }
        }
    }
}
