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
        private StairwellCatFeedingSpriteLibrary feedingSpriteLibrary;
        private StairwellCatIdleModel idleModel;
        private StairwellCatLookSelector lookSelector;
        private StairwellCatFeedingTimeline feedingTimeline;
        private bool ownsSpriteLibrary;
        private bool ownsFeedingSpriteLibrary;
        private bool feedingPrepared;

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
        public bool IsFeeding =>
            feedingTimeline != null &&
            feedingTimeline.IsActive;
        public bool IsFeedingPrepared => feedingPrepared;
        public int CurrentFeedingFrame =>
            IsFeeding
                ? feedingTimeline.FrameIndex
                : -1;

        public void Initialize(
            Camera camera,
            Transform playerTransform,
            Texture2D atlas = null,
            Texture2D feedingAtlas = null)
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

            if (feedingAtlas != null)
            {
                feedingSpriteLibrary =
                    StairwellCatFeedingSpriteLibrary.Create(
                        feedingAtlas);
                ownsFeedingSpriteLibrary = true;
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
            feedingTimeline =
                new StairwellCatFeedingTimeline();
            IsInitialized = true;
            AdvancePresentation(0f);
        }

        public bool BeginFeeding()
        {
            if (!TryPrepareFeeding())
            {
                return false;
            }

            if (BeginPreparedFeeding())
            {
                return true;
            }

            CancelFeedingPreparation();
            return false;
        }

        public bool TryPrepareFeeding()
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                IsFeeding)
            {
                return false;
            }

            if (feedingPrepared)
            {
                return true;
            }

            feedingPrepared =
                TryEnsureFeedingSpriteLibrary();
            return feedingPrepared;
        }

        public bool BeginPreparedFeeding()
        {
            if (!feedingPrepared ||
                !IsInitialized ||
                !isActiveAndEnabled ||
                IsFeeding ||
                feedingSpriteLibrary == null ||
                feedingSpriteLibrary.IsDisposed ||
                !feedingTimeline.Begin())
            {
                return false;
            }

            feedingPrepared = false;
            ApplyFeedingPresentation();
            return true;
        }

        public bool CancelFeedingPreparation()
        {
            if (!feedingPrepared)
            {
                return false;
            }

            feedingPrepared = false;
            return true;
        }

        public bool CompleteFeeding()
        {
            if (feedingTimeline == null ||
                !feedingTimeline.Complete())
            {
                return false;
            }

            ApplyIdlePresentation();
            return true;
        }

        public bool CancelFeeding()
        {
            if (feedingTimeline == null ||
                !feedingTimeline.Cancel())
            {
                return false;
            }

            ApplyIdlePresentation();
            return true;
        }

        public void AdvancePresentation(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (IsFeeding)
            {
                feedingTimeline.Advance(deltaTime);
                if (IsFeeding)
                {
                    ApplyFeedingPresentation();
                }
                else
                {
                    ApplyIdlePresentation();
                }

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

            ApplyIdlePresentation();
        }

        private bool TryEnsureFeedingSpriteLibrary()
        {
            if (feedingSpriteLibrary != null &&
                !feedingSpriteLibrary.IsDisposed)
            {
                return true;
            }

            ownsFeedingSpriteLibrary = false;
            return StairwellCatFeedingSpriteLibrary
                .TryLoadDefault(out feedingSpriteLibrary);
        }

        private void ApplyFeedingPresentation()
        {
            if (Renderer == null ||
                feedingSpriteLibrary == null ||
                !IsFeeding)
            {
                return;
            }

            Renderer.sprite = feedingSpriteLibrary.GetSprite(
                feedingTimeline.FrameIndex);
        }

        private void ApplyIdlePresentation()
        {
            if (Renderer == null ||
                spriteLibrary == null ||
                idleModel == null ||
                lookSelector == null)
            {
                return;
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

        private void OnDisable()
        {
            CancelFeedingPreparation();
            CancelFeeding();
        }

        private void OnDestroy()
        {
            feedingPrepared = false;
            feedingTimeline?.Cancel();
            if (ownsSpriteLibrary)
            {
                spriteLibrary?.Dispose();
            }

            if (ownsFeedingSpriteLibrary)
            {
                feedingSpriteLibrary?.Dispose();
            }

            spriteLibrary = null;
            feedingSpriteLibrary = null;
            ownsSpriteLibrary = false;
            ownsFeedingSpriteLibrary = false;
            IsInitialized = false;
        }
    }
}
