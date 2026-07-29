using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarNpcActor : MonoBehaviour
    {
        private const float WalkSpeed = 0.62f;

        private BarNpcDefinition definition;
        private Camera targetCamera;
        private Transform billboardRoot;
        private Transform poseRoot;
        private SpriteRenderer spriteRenderer;
        private BarNpcAction currentAction;
        private float actionElapsed;
        private float routeProgress;
        private float routeDirection = 1f;

        public bool IsInitialized { get; private set; }
        public BarNpcDefinition Definition => definition;
        public BarNpcAction CurrentAction => currentAction;
        public SpriteRenderer Renderer => spriteRenderer;
        public Transform BillboardRoot => billboardRoot;
        public Transform PoseRoot => poseRoot;

        public void Initialize(
            BarNpcDefinition npcDefinition,
            Camera camera,
            BarNpcSpriteLibrary spriteLibrary)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The bar NPC actor is already initialized.");
            }

            if (spriteLibrary == null)
            {
                throw new ArgumentNullException(
                    nameof(spriteLibrary));
            }

            definition = npcDefinition;
            targetCamera = camera;
            transform.localPosition = definition.Position;
            transform.localRotation = Quaternion.LookRotation(
                definition.Forward,
                Vector3.up);

            GameObject billboardObject =
                new GameObject("Billboard");
            billboardRoot = billboardObject.transform;
            billboardRoot.SetParent(transform, false);

            GameObject poseObject = new GameObject("Pose");
            poseRoot = poseObject.transform;
            poseRoot.SetParent(billboardRoot, false);
            spriteRenderer = poseObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = spriteLibrary.GetSprite(
                definition.VisualVariant);
            if (spriteLibrary.SharedMaterial != null)
            {
                spriteRenderer.sharedMaterial =
                    spriteLibrary.SharedMaterial;
            }

            spriteRenderer.color = Color.white;
            spriteRenderer.sortingOrder = 0;
            spriteRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            spriteRenderer.receiveShadows = false;
            spriteRenderer.lightProbeUsage = LightProbeUsage.Off;
            spriteRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            spriteRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            currentAction = BarNpcAction.Idle;
            IsInitialized = true;
            AdvancePresentation(0f);
        }

        public void SetAction(BarNpcAction action)
        {
            if (!IsInitialized)
            {
                return;
            }

            currentAction = action;
            actionElapsed = 0f;
        }

        public void AdvancePresentation(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime =
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime)
                    ? 0f
                    : Mathf.Max(0f, deltaTime);
            actionElapsed += safeDeltaTime;
            if (definition.Mobile &&
                currentAction == BarNpcAction.Walk)
            {
                AdvanceRoute(safeDeltaTime);
            }

            FaceCamera();
            ApplyActionPose();
        }

        private void AdvanceRoute(float deltaTime)
        {
            float routeLength = Vector3.Distance(
                definition.Position,
                definition.RouteEnd);
            if (routeLength <= 0.01f)
            {
                return;
            }

            routeProgress +=
                routeDirection *
                WalkSpeed *
                deltaTime /
                routeLength;
            if (routeProgress >= 1f)
            {
                routeProgress = 1f;
                routeDirection = -1f;
            }
            else if (routeProgress <= 0f)
            {
                routeProgress = 0f;
                routeDirection = 1f;
            }

            transform.localPosition = Vector3.Lerp(
                definition.Position,
                definition.RouteEnd,
                routeProgress);
        }

        private void FaceCamera()
        {
            Camera camera = targetCamera != null
                ? targetCamera
                : Camera.main;
            if (camera == null || billboardRoot == null)
            {
                return;
            }

            Vector3 toCamera =
                camera.transform.position -
                billboardRoot.position;
            Vector3 flatDirection = Vector3.ProjectOnPlane(
                toCamera,
                Vector3.up);
            if (flatDirection.sqrMagnitude <= 0.0001f)
            {
                flatDirection = Vector3.ProjectOnPlane(
                    -camera.transform.forward,
                    Vector3.up);
            }

            if (flatDirection.sqrMagnitude > 0.0001f)
            {
                billboardRoot.rotation = Quaternion.LookRotation(
                    flatDirection.normalized,
                    Vector3.up);
            }
        }

        private void ApplyActionPose()
        {
            float phase =
                actionElapsed +
                definition.AnimationPhase01 *
                Mathf.PI *
                2f;
            float offsetY = Mathf.Sin(phase * 1.5f) * 0.004f;
            float offsetX = 0f;
            float roll = Mathf.Sin(phase * 1.1f) * 0.25f;
            float squash = 0f;

            switch (currentAction)
            {
                case BarNpcAction.Talk:
                    offsetX = Mathf.Sin(phase * 3.1f) * 0.008f;
                    roll = Mathf.Sin(phase * 4.2f) * 1.1f;
                    break;
                case BarNpcAction.Listen:
                    roll = Mathf.Sin(phase * 1.7f) * 0.45f;
                    break;
                case BarNpcAction.Sip:
                    offsetY += 0.012f;
                    roll = Mathf.Sin(phase * 2.2f) * 1.6f;
                    break;
                case BarNpcAction.Gesture:
                    offsetX = Mathf.Sin(phase * 2.8f) * 0.012f;
                    roll = Mathf.Sin(phase * 3.6f) * 1.9f;
                    break;
                case BarNpcAction.WipeCounter:
                    offsetX = Mathf.Sin(phase * 3.8f) * 0.018f;
                    roll = Mathf.Sin(phase * 3.8f) * 0.8f;
                    break;
                case BarNpcAction.Serve:
                    offsetY +=
                        Mathf.Max(0f, Mathf.Sin(phase * 2.6f)) *
                        0.012f;
                    roll = Mathf.Sin(phase * 2.6f) * 1.2f;
                    break;
                case BarNpcAction.WatchActivity:
                    roll = Mathf.Sin(phase * 1.3f) * 0.65f;
                    break;
                case BarNpcAction.Perform:
                    offsetY +=
                        Mathf.Abs(Mathf.Sin(phase * 3.2f)) *
                        0.01f;
                    offsetX =
                        Mathf.Sin(phase * 2.4f) *
                        0.014f;
                    roll = Mathf.Sin(phase * 3.2f) * 2.2f;
                    break;
                case BarNpcAction.Walk:
                    float step = Mathf.Sin(phase * 8f);
                    offsetY += Mathf.Abs(step) * 0.015f;
                    roll = step * 1.25f;
                    squash = -Mathf.Abs(step) * 0.012f;
                    break;
            }

            poseRoot.localPosition =
                new Vector3(offsetX, offsetY, 0f);
            poseRoot.localRotation =
                Quaternion.Euler(0f, 0f, roll);
            float scale = definition.Scale;
            poseRoot.localScale = new Vector3(
                scale * (1f - squash * 0.35f),
                scale * (1f + squash),
                scale);
        }
    }
}
