using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class StairwellCatActor : MonoBehaviour
    {
        private Camera targetCamera;
        private Transform player;
        private StairwellCatSpriteLibrary spriteLibrary;
        private StairwellCatIdleModel idleModel;
        private StairwellCatLookSelector lookSelector;
        private bool ownsSpriteLibrary;

        public bool IsInitialized { get; private set; }
        public SpriteRenderer Renderer { get; private set; }
        public BillboardSprite Billboard { get; private set; }
        public StairwellCatLook CurrentLook =>
            lookSelector != null
                ? lookSelector.Current
                : StairwellCatLook.Center;
        public int CurrentFrame =>
            idleModel != null
                ? idleModel.CurrentFrame
                : 0;
        public StairwellCatIdleKind CurrentIdleKind =>
            idleModel != null
                ? idleModel.CurrentKind
                : StairwellCatIdleKind.Breathe;

        public void Initialize(
            Camera camera,
            Transform playerTransform,
            Texture2D atlas = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The stairwell cat is already initialized.");
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (playerTransform == null)
            {
                throw new ArgumentNullException(
                    nameof(playerTransform));
            }

            targetCamera = camera;
            player = playerTransform;
            if (atlas != null)
            {
                spriteLibrary =
                    StairwellCatSpriteLibrary.Create(atlas);
                ownsSpriteLibrary = true;
            }
            else
            {
                spriteLibrary =
                    StairwellCatSpriteLibrary.LoadDefault();
            }

            GameObject visualObject =
                new GameObject("Cat Billboard");
            visualObject.transform.SetParent(transform, false);

            Renderer =
                visualObject.AddComponent<SpriteRenderer>();
            Renderer.color = Color.white;
            Renderer.sortingOrder = 0;
            Renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            Renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            Billboard =
                visualObject.AddComponent<BillboardSprite>();
            Billboard.Initialize(targetCamera);
            Billboard.SetCameraPlaneAlignment(true);

            idleModel = new StairwellCatIdleModel();
            lookSelector = new StairwellCatLookSelector();
            IsInitialized = true;
            AdvancePresentation(0f);
        }

        public void AdvancePresentation(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            idleModel.Advance(deltaTime);
            if (targetCamera != null && player != null)
            {
                lookSelector.Update(
                    transform.position,
                    player.position,
                    targetCamera.transform.right);
            }

            Renderer.sprite =
                idleModel.CurrentKind ==
                StairwellCatIdleKind.Groom
                    ? spriteLibrary.GetGroomSprite(
                        idleModel.CurrentFrame)
                    : spriteLibrary.GetSprite(
                        lookSelector.Current,
                        idleModel.CurrentFrame);
        }

        private void Update()
        {
            AdvancePresentation(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (ownsSpriteLibrary)
            {
                spriteLibrary?.Dispose();
            }

            spriteLibrary = null;
            ownsSpriteLibrary = false;
            IsInitialized = false;
        }
    }
}
