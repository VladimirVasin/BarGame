using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class DoorTransitionRoot : MonoBehaviour
    {
        private const float OpenAngle = 86f;
        private const float HandleAngle = -34f;
        private const float CameraPushDistance = 0.38f;
        private const float SecondCreakDelay = 0.82f;

        private readonly Vector3 cameraStartPosition =
            new Vector3(-0.04f, 1.52f, -4.25f);
        private readonly Vector3 cameraLookTarget =
            new Vector3(0f, 1.46f, 0.12f);

        private Light thresholdLight;
        private float thresholdLightIntensity;
        private Sprite blackOpeningSprite;
        private bool isPlaying;
        private float blackOpacity = 1f;

        public bool IsInitialized { get; private set; }
        public bool IsComplete { get; private set; }
        public DoorTransitionDirection Direction { get; private set; }
        public Camera Camera { get; private set; }
        public Transform DoorPivot { get; private set; }
        public Transform HandlePivot { get; private set; }
        public SpriteRenderer OpeningRenderer { get; private set; }
        public float BlackOpacity => blackOpacity;
        public DoorTransitionPose CurrentPose { get; private set; }

        public void Initialize(DoorTransitionDirection direction)
        {
            if (IsInitialized)
            {
                return;
            }

            GameAudioMixer.ApplyProfile(
                GameAudioProfile.DoorTransition);
            Direction = direction;
            Camera = RuntimeSceneSetup.EnsureDoorTransition();
            ConfigureEnvironment(direction);
            BuildPresentation(direction);
            IsInitialized = true;
            IsComplete = false;
            ApplyPose(DoorTransitionTimeline.Evaluate(0f));
        }

        public IEnumerator Play()
        {
            if (!IsInitialized || IsComplete)
            {
                yield break;
            }

            if (isPlaying)
            {
                while (isPlaying)
                {
                    yield return null;
                }

                yield break;
            }

            isPlaying = true;
            bool latchPlayed = false;
            bool firstCreakPlayed = false;
            bool secondCreakPlayed = false;
            float elapsedTime = 0f;

            ApplyPose(DoorTransitionTimeline.Evaluate(0f));
            try
            {
                while (elapsedTime < DoorTransitionTimeline.TotalDuration)
                {
                    yield return null;
                    elapsedTime = Mathf.Min(
                        DoorTransitionTimeline.TotalDuration,
                        elapsedTime + Mathf.Max(0f, Time.unscaledDeltaTime));
                    ApplyPose(DoorTransitionTimeline.Evaluate(elapsedTime));

                    if (!latchPlayed &&
                        elapsedTime >=
                        DoorTransitionTimeline.HandleStartTime)
                    {
                        RetroAudio.PlayAt(
                            RetroSfxId.Door,
                            DoorPivot.position + Vector3.up * 1.35f);
                        latchPlayed = true;
                    }

                    if (!firstCreakPlayed &&
                        elapsedTime >=
                        DoorTransitionTimeline.DoorOpenStartTime)
                    {
                        RetroAudio.PlayAt(
                            RetroSfxId.DoorCreak,
                            DoorPivot.position + Vector3.up * 1.40f);
                        firstCreakPlayed = true;
                    }

                    if (!secondCreakPlayed &&
                        elapsedTime >=
                        DoorTransitionTimeline.DoorOpenStartTime +
                        SecondCreakDelay)
                    {
                        RetroAudio.PlayAt(
                            RetroSfxId.DoorCreak,
                            DoorPivot.position + Vector3.up * 1.40f);
                        secondCreakPlayed = true;
                    }
                }

                IsComplete = true;
            }
            finally
            {
                isPlaying = false;
            }
        }

        internal void ForceBlackout()
        {
            CurrentPose = DoorTransitionTimeline.Evaluate(
                DoorTransitionTimeline.TotalDuration);
            blackOpacity = 1f;
            IsComplete = true;
            isPlaying = false;
        }

        private void ConfigureEnvironment(
            DoorTransitionDirection direction)
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight =
                UsesWarmInteriorPalette(direction)
                    ? new Color(0.055f, 0.035f, 0.028f)
                    : new Color(0.030f, 0.050f, 0.047f);
            RenderSettings.reflectionIntensity = 0f;
            DynamicGI.UpdateEnvironment();
        }

        private void BuildPresentation(
            DoorTransitionDirection direction)
        {
            Transform presentation =
                new GameObject("Door Transition Presentation").transform;
            presentation.SetParent(transform, false);

            BuildCamera(presentation);
            BuildLights(presentation, direction);
            BuildVoid(presentation);
            BuildDoor(presentation);
        }

        private void BuildCamera(Transform parent)
        {
            GameObject cameraObject = Camera.gameObject;
            cameraObject.name = "Door Camera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = cameraStartPosition;
            cameraObject.transform.LookAt(cameraLookTarget);

            Camera.tag = "MainCamera";
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.fieldOfView = 42f;
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 18f;
            Camera.allowHDR = true;
            Camera.allowMSAA = false;

            if (cameraObject.GetComponent<AudioListener>() == null &&
                FindAnyObjectByType<AudioListener>() == null)
            {
                cameraObject.AddComponent<AudioListener>();
            }
        }

        private void BuildLights(
            Transform parent,
            DoorTransitionDirection direction)
        {
            Color keyColor =
                UsesWarmInteriorPalette(direction)
                    ? new Color(0.62f, 0.43f, 0.31f)
                    : new Color(0.36f, 0.49f, 0.47f);
            Color beyondColor =
                UsesWarmInteriorPalette(direction)
                    ? new Color(1f, 0.45f, 0.12f)
                    : new Color(0.28f, 0.54f, 0.48f);

            GameObject keyObject = new GameObject("Door Key Light");
            keyObject.transform.SetParent(parent, false);
            keyObject.transform.localRotation =
                Quaternion.Euler(38f, -28f, 0f);
            Light keyLight = keyObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = keyColor;
            keyLight.intensity = 0.78f;
            keyLight.shadows = LightShadows.Hard;
            keyLight.shadowStrength = 0.58f;
            RenderSettings.sun = keyLight;

            GameObject thresholdObject =
                new GameObject("Threshold Light");
            thresholdObject.transform.SetParent(parent, false);
            thresholdObject.transform.localPosition =
                new Vector3(0f, 1.38f, 0.58f);
            thresholdObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.back, Vector3.up);
            thresholdLight = thresholdObject.AddComponent<Light>();
            thresholdLight.type = LightType.Spot;
            thresholdLight.color = beyondColor;
            thresholdLight.range = 6f;
            thresholdLight.spotAngle = 82f;
            thresholdLight.innerSpotAngle = 50f;
            thresholdLightIntensity =
                UsesWarmInteriorPalette(direction)
                    ? 2.25f
                    : 1.65f;
            thresholdLight.shadows = LightShadows.Hard;
            thresholdLight.shadowStrength = 0.72f;
        }

        private void BuildVoid(Transform parent)
        {
            CreateBox(
                "Black Void",
                parent,
                new Vector3(0f, 1.55f, 1.35f),
                new Vector3(8f, 6f, 0.20f),
                new Color(0.002f, 0.002f, 0.002f));
            BuildBlackOpening(parent);
            CreateBox(
                "Threshold Floor",
                parent,
                new Vector3(0f, -0.035f, -0.30f),
                new Vector3(5.5f, 0.07f, 4.8f),
                new Color(0.018f, 0.014f, 0.013f));
        }

        private void BuildBlackOpening(Transform parent)
        {
            blackOpeningSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f,
                0u,
                SpriteMeshType.FullRect);
            blackOpeningSprite.name = "Black Door Opening Sprite";

            GameObject opening =
                new GameObject("Black Door Opening");
            opening.transform.SetParent(parent, false);
            opening.transform.localPosition =
                new Vector3(0f, 1.47f, 0.82f);
            opening.transform.localScale =
                new Vector3(1.82f, 2.86f, 1f);

            OpeningRenderer = opening.AddComponent<SpriteRenderer>();
            OpeningRenderer.sprite = blackOpeningSprite;
            OpeningRenderer.color = Color.black;
            OpeningRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            OpeningRenderer.receiveShadows = false;
            OpeningRenderer.lightProbeUsage =
                LightProbeUsage.Off;
            OpeningRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            OpeningRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private void BuildDoor(Transform parent)
        {
            Color frameColor = new Color(0.105f, 0.040f, 0.025f);
            Color frameEdgeColor = new Color(0.050f, 0.020f, 0.014f);
            Color doorColor = new Color(0.205f, 0.070f, 0.043f);
            Color panelColor = new Color(0.118f, 0.035f, 0.025f);
            Color brassColor = new Color(0.58f, 0.39f, 0.12f);

            CreateBox(
                "Left Door Frame",
                parent,
                new Vector3(-1f, 1.54f, 0f),
                new Vector3(0.24f, 3.08f, 0.27f),
                frameColor);
            CreateBox(
                "Right Door Frame",
                parent,
                new Vector3(1f, 1.54f, 0f),
                new Vector3(0.24f, 3.08f, 0.27f),
                frameColor);
            CreateBox(
                "Door Lintel",
                parent,
                new Vector3(0f, 3.01f, 0f),
                new Vector3(2.24f, 0.28f, 0.31f),
                frameColor);
            CreateBox(
                "Door Threshold",
                parent,
                new Vector3(0f, 0.035f, -0.01f),
                new Vector3(1.82f, 0.07f, 0.34f),
                frameEdgeColor);

            DoorPivot = new GameObject("Door Pivot").transform;
            DoorPivot.SetParent(parent, false);
            DoorPivot.localPosition = new Vector3(-0.87f, 0.08f, 0f);

            Transform doorLeaf = CreateBox(
                "Door Leaf",
                DoorPivot,
                new Vector3(0.87f, 1.43f, 0f),
                new Vector3(1.73f, 2.84f, 0.14f),
                doorColor).transform;
            AddDoorPanels(doorLeaf, panelColor);
            BuildHandle(brassColor);
        }

        private void AddDoorPanels(Transform doorLeaf, Color panelColor)
        {
            Vector3[] panelPositions =
            {
                new Vector3(-0.23f, 0.27f, -0.56f),
                new Vector3(0.23f, 0.27f, -0.56f),
                new Vector3(-0.23f, -0.27f, -0.56f),
                new Vector3(0.23f, -0.27f, -0.56f)
            };

            for (int index = 0; index < panelPositions.Length; index++)
            {
                CreateBox(
                    $"Door Recess {index + 1}",
                    doorLeaf,
                    panelPositions[index],
                    new Vector3(0.36f, 0.31f, 0.12f),
                    panelColor);
            }

            CreateBox(
                "Door Center Rail",
                doorLeaf,
                new Vector3(0f, 0f, -0.56f),
                new Vector3(0.84f, 0.075f, 0.11f),
                panelColor);
        }

        private void BuildHandle(Color brassColor)
        {
            HandlePivot = new GameObject("Handle Pivot").transform;
            HandlePivot.SetParent(DoorPivot, false);
            HandlePivot.localPosition = new Vector3(1.46f, 1.40f, -0.105f);

            GameObject spindle = RuntimePrimitiveFactory.CreateCylinder(
                "Handle Spindle",
                HandlePivot,
                Vector3.zero,
                new Vector3(0.105f, 0.055f, 0.105f),
                brassColor,
                false);
            spindle.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
            DisableCollider(spindle);

            CreateBox(
                "Handle Lever",
                HandlePivot,
                new Vector3(-0.16f, 0f, -0.015f),
                new Vector3(0.34f, 0.065f, 0.075f),
                brassColor);
        }

        private void ApplyPose(DoorTransitionPose pose)
        {
            CurrentPose = pose;
            blackOpacity = pose.BlackOpacity;

            DoorPivot.localRotation = Quaternion.Euler(
                0f,
                OpenAngle * pose.DoorOpen,
                0f);
            HandlePivot.localRotation = Quaternion.Euler(
                0f,
                0f,
                HandleAngle * pose.HandleTurn);

            Camera.transform.localPosition =
                cameraStartPosition +
                Vector3.forward *
                (CameraPushDistance * pose.CameraPush);
            Camera.transform.LookAt(cameraLookTarget);

            thresholdLight.intensity =
                thresholdLightIntensity *
                Mathf.Lerp(0.24f, 1f, pose.DoorOpen);
        }

        private static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            Material sharedMaterial = null)
        {
            GameObject result = sharedMaterial == null
                ? RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    localPosition,
                    size,
                    color,
                    false)
                : RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    localPosition,
                    size,
                    color,
                    sharedMaterial,
                    false);
            DisableCollider(result);
            return result;
        }

        private static bool UsesWarmInteriorPalette(
            DoorTransitionDirection direction)
        {
            return direction == DoorTransitionDirection.EnterBar ||
                   direction ==
                   DoorTransitionDirection.EnterApartment ||
                   direction == DoorTransitionDirection.EnterChurch;
        }

        private static void DisableCollider(GameObject target)
        {
            Collider primitiveCollider = target.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
            }
        }

        private void OnGUI()
        {
            if (!IsInitialized ||
                blackOpacity <= 0f ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, blackOpacity);
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture,
                ScaleMode.StretchToFill);
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private void OnDestroy()
        {
            if (blackOpeningSprite == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(blackOpeningSprite);
            }
            else
            {
                DestroyImmediate(blackOpeningSprite);
            }
        }
    }
}
