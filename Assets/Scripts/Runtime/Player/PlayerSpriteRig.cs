using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds and animates a lightweight thirteen-part pixel character.
    /// The generated presentation is visual-only and never receives physics components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpriteRig : MonoBehaviour
    {
        private const float PixelsPerUnit = 32f;
        private const float MovingThreshold = 0.02f;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float fullAnimationSpeed = 4f;
        [SerializeField, Min(0f)] private float walkCyclesPerSecond = 2.2f;
        [SerializeField, Range(0f, 60f)] private float armSwingDegrees = 28f;
        [SerializeField, Range(0f, 60f)] private float legSwingDegrees = 32f;
        [SerializeField, Min(0f)] private float bobHeight = 0.035f;
        [SerializeField, Min(0f)] private float settleSpeed = 12f;

        private readonly List<Texture2D> generatedTextures = new List<Texture2D>(13);
        private readonly List<Sprite> generatedSprites = new List<Sprite>(13);

        private Camera targetCamera;
        private Transform visualRoot;
        private BillboardSprite billboard;
        private Transform head;
        private Transform torso;
        private Transform leftUpperArm;
        private Transform leftForearm;
        private Transform rightUpperArm;
        private Transform rightForearm;
        private Transform leftThigh;
        private Transform leftLowerLeg;
        private Transform rightThigh;
        private Transform rightLowerLeg;

        private Vector3 headRestPosition;
        private Vector3 torsoRestPosition;
        private float animationPhase;
        private float motionAmount;
        private float facingSign = 1f;
        private float wastedBlend;
        private float wastedPhase;
        private bool isWasted;

        public void Initialize(Camera camera)
        {
            targetCamera = camera;
            EnsureRigExists();
            billboard.Initialize(camera);
        }

        public void SetMotion(Vector3 planarVelocity)
        {
            planarVelocity.y = 0f;
            float speed = planarVelocity.magnitude;
            motionAmount = Mathf.Clamp01(speed / Mathf.Max(0.1f, fullAnimationSpeed));

            if (speed <= MovingThreshold)
            {
                return;
            }

            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            float horizontalMotion = camera != null
                ? Vector3.Dot(planarVelocity, camera.transform.right)
                : planarVelocity.x;

            if (Mathf.Abs(horizontalMotion) > MovingThreshold)
            {
                facingSign = Mathf.Sign(horizontalMotion);
            }
        }

        public void SetWasted(bool active)
        {
            isWasted = active;
        }

        private void Awake()
        {
            EnsureRigExists();
        }

        private void Update()
        {
            EnsureRigExists();
            AnimateRig(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (visualRoot != null)
            {
                DestroyGeneratedObject(visualRoot.gameObject);
                visualRoot = null;
            }

            for (int i = 0; i < generatedSprites.Count; i++)
            {
                DestroyGeneratedObject(generatedSprites[i]);
            }

            for (int i = 0; i < generatedTextures.Count; i++)
            {
                DestroyGeneratedObject(generatedTextures[i]);
            }

            generatedSprites.Clear();
            generatedTextures.Clear();
        }

        private void EnsureRigExists()
        {
            if (visualRoot != null)
            {
                return;
            }

            GameObject rootObject = new GameObject("GeneratedSpriteRig");
            visualRoot = rootObject.transform;
            visualRoot.SetParent(transform, false);
            billboard = rootObject.AddComponent<BillboardSprite>();

            head = CreatePart(
                "Head", visualRoot, new Vector3(0f, 1.68f, 0f),
                14, 14, SkinColor, new Vector2(0.5f, 0.5f), 10);
            torso = CreatePart(
                "Torso", visualRoot, new Vector3(0f, 1.30f, 0f),
                18, 20, ShirtColor, new Vector2(0.5f, 0.5f), 7);
            CreatePart(
                "Pelvis", visualRoot, new Vector3(0f, 0.94f, 0f),
                16, 8, TrousersColor, new Vector2(0.5f, 0.5f), 8);

            leftUpperArm = CreatePart(
                "LeftUpperArm", visualRoot, new Vector3(-0.31f, 1.54f, 0f),
                7, 12, ShirtShadowColor, new Vector2(0.5f, 0.92f), 4);
            leftForearm = CreatePart(
                "LeftForearm", leftUpperArm, new Vector3(0f, -0.34f, 0f),
                6, 11, ShirtColor, new Vector2(0.5f, 0.92f), 4);
            CreatePart(
                "LeftHand", leftForearm, new Vector3(0f, -0.31f, 0f),
                6, 6, SkinColor, new Vector2(0.5f, 0.85f), 5);

            rightUpperArm = CreatePart(
                "RightUpperArm", visualRoot, new Vector3(0.31f, 1.54f, 0f),
                7, 12, ShirtColor, new Vector2(0.5f, 0.92f), 5);
            rightForearm = CreatePart(
                "RightForearm", rightUpperArm, new Vector3(0f, -0.34f, 0f),
                6, 11, ShirtShadowColor, new Vector2(0.5f, 0.92f), 5);
            CreatePart(
                "RightHand", rightForearm, new Vector3(0f, -0.31f, 0f),
                6, 6, SkinColor, new Vector2(0.5f, 0.85f), 6);

            leftThigh = CreatePart(
                "LeftThigh", visualRoot, new Vector3(-0.13f, 0.94f, 0f),
                8, 13, TrousersShadowColor, new Vector2(0.5f, 0.92f), 2);
            leftLowerLeg = CreatePart(
                "LeftLowerLeg", leftThigh, new Vector3(0f, -0.37f, 0f),
                7, 13, ShoeColor, new Vector2(0.5f, 0.92f), 2);
            rightThigh = CreatePart(
                "RightThigh", visualRoot, new Vector3(0.13f, 0.94f, 0f),
                8, 13, TrousersColor, new Vector2(0.5f, 0.92f), 3);
            rightLowerLeg = CreatePart(
                "RightLowerLeg", rightThigh, new Vector3(0f, -0.37f, 0f),
                7, 13, ShoeColor, new Vector2(0.5f, 0.92f), 3);

            headRestPosition = head.localPosition;
            torsoRestPosition = torso.localPosition;
            billboard.Initialize(targetCamera);
        }

        private Transform CreatePart(
            string partName,
            Transform parent,
            Vector3 localPosition,
            int pixelWidth,
            int pixelHeight,
            Color32 fillColor,
            Vector2 pivot,
            int sortingOrder)
        {
            GameObject partObject = new GameObject(partName);
            Transform partTransform = partObject.transform;
            partTransform.SetParent(parent, false);
            partTransform.localPosition = localPosition;

            SpriteRenderer renderer = partObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CreatePixelSprite(
                partName + "Sprite",
                pixelWidth,
                pixelHeight,
                fillColor,
                pivot);
            renderer.sortingOrder = sortingOrder;
            return partTransform;
        }

        private Sprite CreatePixelSprite(
            string spriteName,
            int width,
            int height,
            Color32 fillColor,
            Vector2 pivot)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = spriteName + "Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color32[] pixels = new Color32[width * height];
            bool[] shape = BuildRoundedShape(width, height);
            Color32 outline = new Color32(31, 32, 42, 255);
            Color32 highlight = Lighten(fillColor, 24);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (!shape[index])
                    {
                        pixels[index] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    bool edge = IsShapeEdge(shape, width, height, x, y);
                    bool litPixel = !edge && x == 2 && y > height / 2;
                    pixels[index] = edge ? outline : litPixel ? highlight : fillColor;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                pivot,
                PixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            sprite.name = spriteName;
            sprite.hideFlags = HideFlags.DontSave;
            generatedTextures.Add(texture);
            generatedSprites.Add(sprite);
            return sprite;
        }

        private void AnimateRig(float deltaTime)
        {
            if (visualRoot == null)
            {
                return;
            }

            if (motionAmount > MovingThreshold)
            {
                animationPhase += deltaTime * walkCyclesPerSecond * Mathf.PI * 2f;
            }

            float settle = 1f - Mathf.Exp(-settleSpeed * deltaTime);
            wastedBlend = Mathf.MoveTowards(
                wastedBlend,
                isWasted ? 1f : 0f,
                deltaTime * 4f);
            wastedPhase += deltaTime * 4.5f;
            float wave = Mathf.Sin(animationPhase) * motionAmount;
            float doubleWave = Mathf.Abs(Mathf.Sin(animationPhase * 2f)) * motionAmount;
            float armAngle = wave * armSwingDegrees;
            float legAngle = wave * legSwingDegrees;

            SetLocalZRotation(leftUpperArm, Mathf.LerpAngle(GetLocalZ(leftUpperArm), -armAngle, settle));
            SetLocalZRotation(rightUpperArm, Mathf.LerpAngle(GetLocalZ(rightUpperArm), armAngle, settle));
            SetLocalZRotation(leftForearm, Mathf.LerpAngle(GetLocalZ(leftForearm), armAngle * 0.22f, settle));
            SetLocalZRotation(rightForearm, Mathf.LerpAngle(GetLocalZ(rightForearm), -armAngle * 0.22f, settle));
            SetLocalZRotation(leftThigh, Mathf.LerpAngle(GetLocalZ(leftThigh), legAngle, settle));
            SetLocalZRotation(rightThigh, Mathf.LerpAngle(GetLocalZ(rightThigh), -legAngle, settle));
            SetLocalZRotation(leftLowerLeg, Mathf.LerpAngle(GetLocalZ(leftLowerLeg), -Mathf.Max(0f, legAngle) * 0.35f, settle));
            SetLocalZRotation(rightLowerLeg, Mathf.LerpAngle(GetLocalZ(rightLowerLeg), Mathf.Min(0f, legAngle) * 0.35f, settle));

            float bob = doubleWave * bobHeight;
            head.localPosition = Vector3.Lerp(
                head.localPosition,
                headRestPosition + Vector3.up * bob * 0.6f,
                settle);
            torso.localPosition = Vector3.Lerp(
                torso.localPosition,
                torsoRestPosition + Vector3.up * bob,
                settle);

            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * facingSign;
            visualRoot.localScale = scale;
            visualRoot.localPosition = new Vector3(
                Mathf.Sin(wastedPhase) * 0.055f * wastedBlend,
                Mathf.Abs(Mathf.Sin(wastedPhase * 0.5f)) * 0.018f * wastedBlend,
                0f);
        }

        private static bool[] BuildRoundedShape(int width, int height)
        {
            bool[] shape = new bool[width * height];
            int corner = Mathf.Clamp(Mathf.Min(width, height) / 4, 1, 3);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool cutCorner =
                        (x < corner && y < corner && x + y < corner - 1) ||
                        (x < corner && y >= height - corner && x + (height - 1 - y) < corner - 1) ||
                        (x >= width - corner && y < corner && (width - 1 - x) + y < corner - 1) ||
                        (x >= width - corner && y >= height - corner &&
                         (width - 1 - x) + (height - 1 - y) < corner - 1);
                    shape[y * width + x] = !cutCorner;
                }
            }

            return shape;
        }

        private static bool IsShapeEdge(bool[] shape, int width, int height, int x, int y)
        {
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            {
                return true;
            }

            return !shape[y * width + x - 1] ||
                   !shape[y * width + x + 1] ||
                   !shape[(y - 1) * width + x] ||
                   !shape[(y + 1) * width + x];
        }

        private static Color32 Lighten(Color32 color, byte amount)
        {
            return new Color32(
                (byte)Mathf.Min(255, color.r + amount),
                (byte)Mathf.Min(255, color.g + amount),
                (byte)Mathf.Min(255, color.b + amount),
                color.a);
        }

        private static float GetLocalZ(Transform target)
        {
            return target.localEulerAngles.z;
        }

        private static void SetLocalZRotation(Transform target, float degrees)
        {
            target.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private static void DestroyGeneratedObject(Object generatedObject)
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

        private static readonly Color32 SkinColor = new Color32(238, 178, 133, 255);
        private static readonly Color32 ShirtColor = new Color32(214, 68, 72, 255);
        private static readonly Color32 ShirtShadowColor = new Color32(164, 43, 63, 255);
        private static readonly Color32 TrousersColor = new Color32(54, 73, 112, 255);
        private static readonly Color32 TrousersShadowColor = new Color32(38, 51, 83, 255);
        private static readonly Color32 ShoeColor = new Color32(34, 35, 43, 255);
    }
}
