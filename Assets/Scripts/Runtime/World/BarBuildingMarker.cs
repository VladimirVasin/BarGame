using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Presents a shared, collider-free pixel sign that identifies a bar building.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(BillboardSprite))]
    public sealed class BarBuildingMarker : MonoBehaviour
    {
        private const int TextureWidth = 40;
        private const int TextureHeight = 48;
        private const float PixelsPerUnit = 32f;
        private const string SpriteName = "SharedBarBuildingMarkerSprite";

        private static Texture2D sharedTexture;
        private static Sprite sharedSprite;
        private static int activeLeaseCount;

        [SerializeField] private string barId = string.Empty;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private BillboardSprite billboard;

        private Sprite leasedSprite;

        public string BarId => barId;
        public SpriteRenderer Renderer => spriteRenderer;

        public void Initialize(string id, Camera camera)
        {
            barId = id ?? string.Empty;
            EnsureComponents();
            billboard.Initialize(camera);

            if (isActiveAndEnabled)
            {
                EnsureVisual();
            }
        }

        private void Awake()
        {
            EnsureComponents();
        }

        private void OnEnable()
        {
            EnsureVisual();
        }

        private void OnDisable()
        {
            ClearRendererSprite();
            ReleaseSharedSprite();
        }

        private void OnDestroy()
        {
            ReleaseSharedSprite();
        }

        private void EnsureComponents()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (billboard == null)
            {
                billboard = GetComponent<BillboardSprite>();
            }
        }

        private void EnsureVisual()
        {
            EnsureComponents();

            if (sharedSprite == null)
            {
                CreateSharedSprite();
            }

            if (leasedSprite != sharedSprite)
            {
                ReleaseSharedSprite();
                leasedSprite = sharedSprite;
                activeLeaseCount++;
            }

            spriteRenderer.sprite = sharedSprite;
            spriteRenderer.color = new Color(1.35f, 1.18f, 0.82f, 1f);
            spriteRenderer.sortingOrder = 0;
        }

        private void ClearRendererSprite()
        {
            if (spriteRenderer != null &&
                spriteRenderer.sprite != null &&
                spriteRenderer.sprite == leasedSprite)
            {
                spriteRenderer.sprite = null;
            }
        }

        private void ReleaseSharedSprite()
        {
            if (leasedSprite == null)
            {
                leasedSprite = null;
                return;
            }

            Sprite releasedSprite = leasedSprite;
            leasedSprite = null;
            if (sharedSprite == null || sharedSprite != releasedSprite)
            {
                return;
            }

            activeLeaseCount = Mathf.Max(0, activeLeaseCount - 1);
            if (activeLeaseCount > 0)
            {
                return;
            }

            Sprite spriteToDestroy = sharedSprite;
            Texture2D textureToDestroy = sharedTexture;
            sharedSprite = null;
            sharedTexture = null;
            DestroyGeneratedObject(spriteToDestroy);
            DestroyGeneratedObject(textureToDestroy);
        }

        private static void CreateSharedSprite()
        {
            sharedTexture = new Texture2D(
                TextureWidth,
                TextureHeight,
                TextureFormat.RGBA32,
                false)
            {
                name = SpriteName + "Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color32[] pixels = BuildSignPixels();
            sharedTexture.SetPixels32(pixels);
            sharedTexture.Apply(false, true);

            sharedSprite = Sprite.Create(
                sharedTexture,
                new Rect(0f, 0f, TextureWidth, TextureHeight),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            sharedSprite.name = SpriteName;
            sharedSprite.hideFlags = HideFlags.DontSave;
        }

        private static Color32[] BuildSignPixels()
        {
            var pixels = new Color32[TextureWidth * TextureHeight];
            Color32 outline = new Color32(35, 18, 29, 255);
            Color32 burgundy = new Color32(91, 22, 43, 255);
            Color32 burgundyHighlight = new Color32(118, 31, 51, 255);
            Color32 goldShadow = new Color32(151, 96, 31, 255);
            Color32 gold = new Color32(232, 176, 59, 255);
            Color32 pale = new Color32(249, 233, 184, 255);
            Color32 drink = new Color32(222, 139, 42, 255);

            // Wall bracket and two short chains make the panel read as a hanging sign.
            FillRect(pixels, 5, 44, 34, 47, outline);
            FillRect(pixels, 7, 45, 32, 46, gold);
            FillRect(pixels, 8, 37, 12, 45, outline);
            FillRect(pixels, 10, 38, 10, 44, gold);
            FillRect(pixels, 27, 37, 31, 45, outline);
            FillRect(pixels, 29, 38, 29, 44, gold);

            // Dark panel with a chunky two-tone gold frame.
            FillRect(pixels, 1, 2, 38, 39, outline);
            FillRect(pixels, 2, 3, 37, 38, goldShadow);
            FillRect(pixels, 4, 5, 35, 36, gold);
            FillRect(pixels, 6, 7, 33, 34, burgundy);
            FillRect(pixels, 7, 31, 32, 33, burgundyHighlight);
            ClearPanelCorners(pixels);

            // Pale beer mug icon with amber contents and a hollow handle.
            FillRect(pixels, 24, 18, 30, 28, pale);
            FillRect(pixels, 26, 20, 28, 25, burgundy);
            FillRect(pixels, 10, 13, 25, 31, pale);
            FillRect(pixels, 13, 16, 22, 28, drink);
            FillRect(pixels, 14, 18, 15, 26, pale);
            FillRect(pixels, 9, 12, 26, 15, pale);
            FillRect(pixels, 10, 29, 25, 32, pale);
            FillRect(pixels, 12, 32, 15, 33, pale);
            FillRect(pixels, 19, 32, 22, 33, pale);

            return pixels;
        }

        private static void FillRect(
            Color32[] pixels,
            int minimumX,
            int minimumY,
            int maximumX,
            int maximumY,
            Color32 color)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    pixels[(y * TextureWidth) + x] = color;
                }
            }
        }

        private static void ClearPanelCorners(Color32[] pixels)
        {
            Color32 clear = new Color32(0, 0, 0, 0);
            pixels[(2 * TextureWidth) + 1] = clear;
            pixels[(2 * TextureWidth) + 38] = clear;
            pixels[(39 * TextureWidth) + 1] = clear;
            pixels[(39 * TextureWidth) + 38] = clear;
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedResources()
        {
            Sprite spriteToDestroy = sharedSprite;
            Texture2D textureToDestroy = sharedTexture;
            sharedSprite = null;
            sharedTexture = null;
            activeLeaseCount = 0;
            DestroyGeneratedObject(spriteToDestroy);
            DestroyGeneratedObject(textureToDestroy);
        }
    }
}
