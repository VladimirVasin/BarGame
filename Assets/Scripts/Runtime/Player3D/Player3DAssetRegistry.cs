using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum Player3DAnatomicalPart
    {
        Head,
        Neck,
        Torso,
        Pelvis,
        LeftUpperArm,
        LeftForearm,
        LeftHand,
        RightUpperArm,
        RightForearm,
        RightHand,
        LeftThigh,
        LeftShin,
        LeftFoot,
        RightThigh,
        RightShin,
        RightFoot,
        // The continuous torso mesh spans this physics segment and Torso.
        // Appended to preserve the serialized values of anatomical parts.
        LowerTorso
    }

    [Serializable]
    public sealed class Player3DMeshBinding
    {
        [SerializeField] private string meshName;
        [SerializeField] private string role;
        [SerializeField] private string boneName;
        [SerializeField] private string bodyGroup;
        [SerializeField] private string anatomicalSide;
        [SerializeField] private string paletteMaterialName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Transform bone;
        [SerializeField] private Color baseColor = Color.white;

        public Player3DMeshBinding(
            string meshName,
            string role,
            string boneName,
            string bodyGroup,
            string anatomicalSide,
            string paletteMaterialName,
            Renderer renderer,
            Transform bone,
            Color baseColor)
        {
            this.meshName = meshName;
            this.role = role;
            this.boneName = boneName;
            this.bodyGroup = bodyGroup;
            this.anatomicalSide = anatomicalSide;
            this.paletteMaterialName = paletteMaterialName;
            this.renderer = renderer;
            this.bone = bone;
            this.baseColor = baseColor;
        }

        public string MeshName => meshName;
        public string Role => role;
        // The representative gameplay anchor; a continuous skinned surface
        // can additionally use neighboring bones in its renderer's weights.
        public string BoneName => boneName;
        public string BodyGroup => bodyGroup;
        public string AnatomicalSide => anatomicalSide;
        public string PaletteMaterialName => paletteMaterialName;
        public Renderer Renderer => renderer;
        public Transform Bone => bone;
        public Color BaseColor => baseColor;
    }

    [Serializable]
    public sealed class Player3DAnatomicalPartBinding
    {
        [SerializeField] private Player3DAnatomicalPart part;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Transform bone;

        public Player3DAnatomicalPartBinding(
            Player3DAnatomicalPart part,
            Renderer renderer,
            Transform bone)
        {
            this.part = part;
            this.renderer = renderer;
            this.bone = bone;
        }

        public Player3DAnatomicalPart Part => part;
        public Renderer Renderer => renderer;
        public Transform Bone => bone;
    }

    [Serializable]
    public struct Player3DFacialExpressionKey
    {
        [SerializeField, Range(0f, 1f)] private float normalizedTime;
        [SerializeField] private PlayerFacialExpression expression;

        public Player3DFacialExpressionKey(
            float normalizedTime,
            PlayerFacialExpression expression)
        {
            this.normalizedTime = Mathf.Clamp01(normalizedTime);
            this.expression = expression;
        }

        public float NormalizedTime => normalizedTime;
        public PlayerFacialExpression Expression => expression;
    }

    [Serializable]
    public sealed class Player3DAnimationBinding
    {
        [SerializeField] private string clipName;
        [SerializeField] private string category;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private float authoredDuration;
        [SerializeField] private bool looping;
        [SerializeField]
        private Player3DFacialExpressionKey[] facialExpressionKeys =
            Array.Empty<Player3DFacialExpressionKey>();

        public Player3DAnimationBinding(
            string clipName,
            string category,
            AnimationClip clip,
            float authoredDuration,
            bool looping,
            Player3DFacialExpressionKey[] configuredFacialExpressionKeys =
                null)
        {
            this.clipName = clipName;
            this.category = category;
            this.clip = clip;
            this.authoredDuration = authoredDuration;
            this.looping = looping;
            facialExpressionKeys = configuredFacialExpressionKeys ??
                Array.Empty<Player3DFacialExpressionKey>();
        }

        public string ClipName => clipName;
        public string Category => category;
        public AnimationClip Clip => clip;
        public float AuthoredDuration => authoredDuration;
        public bool Looping => looping;

        public IReadOnlyList<Player3DFacialExpressionKey>
            FacialExpressionKeys => facialExpressionKeys;

        public bool TryGetFacialExpression(
            float normalizedTime,
            out PlayerFacialExpression expression)
        {
            expression = PlayerFacialExpression.Neutral;
            if (facialExpressionKeys == null ||
                facialExpressionKeys.Length == 0)
            {
                return false;
            }

            float time = Mathf.Clamp01(normalizedTime);
            int selectedIndex = -1;
            float selectedTime = float.NegativeInfinity;
            for (int index = 0;
                 index < facialExpressionKeys.Length;
                 index++)
            {
                float keyTime =
                    facialExpressionKeys[index].NormalizedTime;
                if (keyTime <= time && keyTime >= selectedTime)
                {
                    selectedIndex = index;
                    selectedTime = keyTime;
                }
            }

            if (selectedIndex < 0)
            {
                return false;
            }

            expression = facialExpressionKeys[selectedIndex].Expression;
            return true;
        }
    }

    [Serializable]
    public struct Player3DFaceAtlasCell
    {
        [SerializeField] private PlayerFacialExpression expression;
        [SerializeField, Min(0)] private int column;
        [SerializeField, Min(0)] private int row;
        [SerializeField] private bool soiled;

        public Player3DFaceAtlasCell(
            PlayerFacialExpression expression,
            int column,
            int row)
            : this(expression, column, row, false)
        {
        }

        /// <summary>
        /// A soiled cell is the same expression with the drink still on the
        /// chin; the hero's atlas carries one twin per face, other rigs none.
        /// </summary>
        public Player3DFaceAtlasCell(
            PlayerFacialExpression expression,
            int column,
            int row,
            bool soiled)
        {
            this.expression = expression;
            this.column = column;
            this.row = row;
            this.soiled = soiled;
        }

        public PlayerFacialExpression Expression => expression;
        public int Column => column;
        public int Row => row;
        public bool Soiled => soiled;
    }

    /// <summary>
    /// Production face surface. Atlas rows use Unity UV order: row zero starts
    /// at the texture bottom.
    /// </summary>
    [Serializable]
    public sealed class Player3DFaceAtlasBinding
    {
        [SerializeField] private Renderer renderer;
        [SerializeField] private Texture2D texture;
        [SerializeField, Min(1)] private int columns = 4;
        [SerializeField, Min(1)] private int rows = 4;
        [SerializeField] private Player3DFaceAtlasCell[] cells =
            Array.Empty<Player3DFaceAtlasCell>();

        public Player3DFaceAtlasBinding(
            Renderer renderer,
            Texture2D texture,
            int columns,
            int rows,
            Player3DFaceAtlasCell[] cells)
        {
            this.renderer = renderer;
            this.texture = texture;
            this.columns = columns;
            this.rows = rows;
            this.cells = cells ?? Array.Empty<Player3DFaceAtlasCell>();
        }

        public Renderer Renderer => renderer;
        public Texture2D Texture => texture;
        public int Columns => columns;
        public int Rows => rows;
        public IReadOnlyList<Player3DFaceAtlasCell> Cells => cells;

        public bool IsConfigured =>
            renderer != null &&
            texture != null &&
            columns > 0 &&
            rows > 0 &&
            HasCanonicalCells();

        public bool TryGetTextureTransform(
            PlayerFacialExpression expression,
            out Vector4 textureTransform)
        {
            return TryGetTextureTransform(expression, false, out textureTransform);
        }

        /// <summary>
        /// The exact (expression, soiled) cell. A soiled request whose twin
        /// the atlas lacks falls back to the clean cell, so a rig without
        /// soiled faces keeps showing the expression rather than nothing.
        /// </summary>
        public bool TryGetTextureTransform(
            PlayerFacialExpression expression,
            bool soiled,
            out Vector4 textureTransform)
        {
            if (TryGetExactTextureTransform(expression, soiled, out textureTransform))
            {
                return true;
            }

            return soiled &&
                   TryGetExactTextureTransform(expression, false, out textureTransform);
        }

        private bool TryGetExactTextureTransform(
            PlayerFacialExpression expression,
            bool soiled,
            out Vector4 textureTransform)
        {
            if (columns <= 0 || rows <= 0 || cells == null)
            {
                textureTransform = new Vector4(1f, 1f, 0f, 0f);
                return false;
            }

            for (int index = 0; index < cells.Length; index++)
            {
                Player3DFaceAtlasCell cell = cells[index];
                if (cell.Expression != expression ||
                    cell.Soiled != soiled ||
                    cell.Column < 0 ||
                    cell.Column >= columns ||
                    cell.Row < 0 ||
                    cell.Row >= rows)
                {
                    continue;
                }

                float width = 1f / columns;
                float height = 1f / rows;
                textureTransform = new Vector4(
                    width,
                    height,
                    cell.Column * width,
                    cell.Row * height);
                return true;
            }

            textureTransform = new Vector4(1f, 1f, 0f, 0f);
            return false;
        }

        private bool HasCanonicalCells()
        {
            return
                TryGetTextureTransform(
                    PlayerFacialExpression.Neutral,
                    out _) &&
                TryGetTextureTransform(
                    PlayerFacialExpression.HalfBlink,
                    out _) &&
                TryGetTextureTransform(
                    PlayerFacialExpression.ClosedBlink,
                    out _) &&
                TryGetTextureTransform(
                    PlayerFacialExpression.Watchful,
                    out _) &&
                TryGetTextureTransform(
                    PlayerFacialExpression.Tense,
                    out _);
        }
    }

    [Serializable]
    public struct Player3DBoneAnchors
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftVessel;
        [SerializeField] private Transform rightCigarette;
        [SerializeField] private Transform mouth;
        [SerializeField] private Transform spine;

        public Player3DBoneAnchors(
            Transform head,
            Transform chest,
            Transform pelvis,
            Transform leftFoot,
            Transform rightFoot,
            Transform leftGrip,
            Transform rightGrip,
            Transform leftVessel,
            Transform rightCigarette,
            Transform mouth,
            Transform spine = null)
        {
            this.head = head;
            this.chest = chest;
            this.pelvis = pelvis;
            this.leftFoot = leftFoot;
            this.rightFoot = rightFoot;
            this.leftGrip = leftGrip;
            this.rightGrip = rightGrip;
            this.leftVessel = leftVessel;
            this.rightCigarette = rightCigarette;
            this.mouth = mouth;
            this.spine = spine;
        }

        public Transform Head => head;
        public Transform Chest => chest;
        public Transform Spine => spine;
        public Transform Pelvis => pelvis;
        public Transform LeftFoot => leftFoot;
        public Transform RightFoot => rightFoot;
        public Transform LeftGrip => leftGrip;
        public Transform RightGrip => rightGrip;
        public Transform LeftVessel => leftVessel;
        public Transform RightCigarette => rightCigarette;
        public Transform Mouth => mouth;
    }

    [Serializable]
    public struct Player3DMetrics
    {
        [SerializeField] private float canonicalHeight;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private Vector3 localForward;

        public Player3DMetrics(
            float canonicalHeight,
            Bounds localBounds,
            Vector3 localForward)
        {
            this.canonicalHeight = canonicalHeight;
            this.localBounds = localBounds;
            this.localForward = localForward.sqrMagnitude > 0.0001f
                ? localForward.normalized
                : Vector3.forward;
        }

        public float CanonicalHeight => canonicalHeight;
        public Bounds LocalBounds => localBounds;
        public Vector3 LocalForward => localForward;
    }

    [DisallowMultipleComponent]
    public sealed class Player3DAssetRegistry : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");
        private static readonly int LegacyMapTransformId =
            Shader.PropertyToID("_MainTex_ST");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private Player3DMeshBinding[] meshBindings =
            Array.Empty<Player3DMeshBinding>();
        [SerializeField]
        private Player3DAnatomicalPartBinding[] anatomicalParts =
            Array.Empty<Player3DAnatomicalPartBinding>();
        [SerializeField] private Player3DAnimationBinding[] animations =
            Array.Empty<Player3DAnimationBinding>();
        [SerializeField] private Player3DBoneAnchors anchors;
        [SerializeField] private Player3DMetrics metrics;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string sourcePose;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string buildSignature;
        [SerializeField] private Player3DFaceAtlasBinding faceAtlas;
        [SerializeField] private bool applyPaletteOnEnable = true;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<Player3DMeshBinding> MeshBindings => meshBindings;
        public IReadOnlyList<Player3DAnatomicalPartBinding> AnatomicalParts =>
            anatomicalParts;
        public IReadOnlyList<Player3DAnimationBinding> Animations => animations;
        public Player3DBoneAnchors Anchors => anchors;
        public Player3DMetrics Metrics => metrics;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string SourcePose => sourcePose;
        public int SourceTriangleCount => sourceTriangleCount;
        public string BuildSignature => buildSignature;
        public Player3DFaceAtlasBinding FaceAtlas => faceAtlas;
        public bool HasFaceAtlas => faceAtlas != null && faceAtlas.IsConfigured;

        public void Configure(
            Animator configuredAnimator,
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            Player3DMeshBinding[] configuredMeshBindings,
            Player3DAnatomicalPartBinding[] configuredAnatomicalParts,
            Player3DAnimationBinding[] configuredAnimations,
            Player3DBoneAnchors configuredAnchors,
            Player3DMetrics configuredMetrics,
            string generatorVersion,
            string pose,
            int triangleCount,
            string configuredBuildSignature,
            Player3DFaceAtlasBinding configuredFaceAtlas = null)
        {
            animator = configuredAnimator;
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            meshBindings = configuredMeshBindings ??
                Array.Empty<Player3DMeshBinding>();
            anatomicalParts = configuredAnatomicalParts ??
                Array.Empty<Player3DAnatomicalPartBinding>();
            animations = configuredAnimations ??
                Array.Empty<Player3DAnimationBinding>();
            anchors = configuredAnchors;
            metrics = configuredMetrics;
            sourceGeneratorVersion = generatorVersion ?? string.Empty;
            sourcePose = pose ?? string.Empty;
            sourceTriangleCount = triangleCount;
            buildSignature = configuredBuildSignature ?? string.Empty;
            faceAtlas = configuredFaceAtlas;
        }

        public bool TryGetPart(
            Player3DAnatomicalPart part,
            out Player3DAnatomicalPartBinding binding)
        {
            for (int index = 0; index < anatomicalParts.Length; index++)
            {
                Player3DAnatomicalPartBinding candidate =
                    anatomicalParts[index];
                if (candidate != null && candidate.Part == part)
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public bool TryGetAnimation(
            string clipName,
            out Player3DAnimationBinding binding)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                binding = null;
                return false;
            }

            for (int index = 0; index < animations.Length; index++)
            {
                Player3DAnimationBinding candidate = animations[index];
                if (candidate != null && candidate.ClipName == clipName)
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public void ApplyPalette()
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            for (int index = 0; index < meshBindings.Length; index++)
            {
                Player3DMeshBinding binding = meshBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                Renderer target = binding.Renderer;
                target.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, binding.BaseColor);
                properties.SetColor(LegacyColorId, binding.BaseColor);
                target.SetPropertyBlock(properties);
                properties.Clear();
            }

            ApplyNeutralFaceAtlas(properties);
        }

        private void ApplyNeutralFaceAtlas(
            MaterialPropertyBlock properties)
        {
            if (!HasFaceAtlas ||
                !faceAtlas.TryGetTextureTransform(
                    PlayerFacialExpression.Neutral,
                    out Vector4 textureTransform))
            {
                return;
            }

            Renderer target = faceAtlas.Renderer;
            target.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, faceAtlas.Texture);
            properties.SetVector(BaseMapTransformId, textureTransform);
            properties.SetTexture(LegacyMapId, faceAtlas.Texture);
            properties.SetVector(LegacyMapTransformId, textureTransform);
            target.SetPropertyBlock(properties);
            properties.Clear();
        }

        private void OnEnable()
        {
            if (applyPaletteOnEnable)
            {
                ApplyPalette();
            }
        }
    }
}
