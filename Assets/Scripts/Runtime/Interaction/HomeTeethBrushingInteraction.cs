using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class HomeTeethBrushingInteraction : HomeBathroomSceneInteraction
    {
        public const string BrushPromptKey = "interaction.brush_teeth";
        public const string StopPromptKeyName = "interaction.stop_brushing";
        public const int StressRelief = 5;
        public const float BrushVisibleWeight = 0.15f;
        private static readonly Vector3 EntryEye = new Vector3(2.075f, 1.75f, 3.02f);
        public static readonly Vector3 BasinTarget = new Vector3(1.995f, 0.724f, 3.425f);
        private readonly HomeTeethBrushingTimeline timeline = new HomeTeethBrushingTimeline();
        private readonly HomeTeethBrushingProgress progress = new HomeTeethBrushingProgress();
        private HomeTeethBrushingArmPose armPose;
        private HomeTeethBrushingArmPose valvePose;
        private HomeBrushingFirstPersonView firstPerson;
        private HomeSinkFaucet faucet;
        private HomeBrushingSpitEffect spit;
        private Player3DCharacterPresentation visual;
        private GameObject toothbrush, foam;
        private Transform brushTip;
        private bool committed, ownsHandoff, previousHandoff, cursorCaptured, discardMouse;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private PlayerFacialExpression previousExpression;
        private float pendingSpitSeconds, scrubDistance;
        private float presentedFaucetOpen;
        private int valveSoundDirection;
        private bool brushingPromptVisible;
        private Func<bool> stopAction;

        public HomeTeethBrushingTimeline Timeline => timeline;
        public HomeTeethBrushingProgress Progress => progress;
        public HomeTeethBrushingArmPose ArmPose => armPose;
        public HomeTeethBrushingArmPose ValvePose => valvePose;
        public HomeBrushingFirstPersonView FirstPersonView => firstPerson;
        public HomeSinkFaucet Faucet => faucet;
        public Transform ReflectedToothbrush { get; private set; }
        public Transform ReflectedFoam { get; private set; }
        public Transform ReflectedSpit { get; private set; }
        public GameObject Toothbrush => toothbrush;
        public HomeBrushingSpitEffect SpitEffect => spit;
        public bool GaugeVisible => OwnsScene && timeline.Phase == HomeTeethBrushingPhase.Brushing;
        public override string PromptKey => OwnsScene ? string.Empty : BrushPromptKey;
        protected override string StopPromptKey => StopPromptKeyName;
        protected override Vector3 CameraLocalPosition => EntryEye;
        protected override Vector3 CameraLocalLookAt => EntryEye + Vector3.forward;
        protected override float CameraFieldOfView => HomeBrushingFirstPersonView.FieldOfView;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => 0f;
        protected override bool SceneCompleted => timeline.IsCompleted;
        protected override bool StopPromptVisible => timeline.Phase == HomeTeethBrushingPhase.Brushing;
        protected override bool WalkOutBackward => true;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            stopAction = () => { RequestStop(); return true; };
            // Step back with the ordinary reverse gait, still facing the sink.
            InitializeScene(homeRoot, new Vector3(2.075f, 0f, 2.86f),
                Quaternion.LookRotation(Vector3.forward), new Vector3(2.075f, 0f, 2.50f),
                Quaternion.LookRotation(Vector3.forward), new Vector3(2.075f, 0f, 2.78f));
            gameObject.AddComponent<HomeBrushingGaugeView>().Bind(this);
            var effects = new GameObject("Home Brushing Spit");
            effects.transform.SetParent(homeRoot.transform, false);
            spit = effects.AddComponent<HomeBrushingSpitEffect>();
            spit.Initialize(homeRoot);
        }

        protected override bool PrepareScene()
        {
            if (!(Home.Player.Visual is Player3DCharacterPresentation presentation)) return false;
            visual = presentation;
            faucet = Home.Room.GetComponentInChildren<HomeSinkFaucet>(true);
            if (faucet == null || Home.BathroomMirror == null) return false;
            EnsureProps(visual.Registry);
            if (armPose == null)
            {
                armPose = gameObject.AddComponent<HomeTeethBrushingArmPose>();
                armPose.Initialize(visual.Registry, Home.Player.GameObject.transform);
            }
            armPose.Effector = brushTip;
            if (valvePose == null)
            {
                valvePose = gameObject.AddComponent<HomeTeethBrushingArmPose>();
                valvePose.Initialize(visual.Registry, Home.Player.GameObject.transform, true);
                valvePose.Effector = visual.Registry.Anchors.LeftGrip;
            }
            if (firstPerson == null) firstPerson = gameObject.AddComponent<HomeBrushingFirstPersonView>();
            if (!firstPerson.Initialize(Home)) return false;
            ReflectedToothbrush = Home.BathroomMirror.RegisterReflectedProp(toothbrush.transform);
            ReflectedFoam = Home.BathroomMirror.RegisterReflectedProp(foam.transform);
            ReflectedSpit = Home.BathroomMirror.RegisterReflectedProp(spit.transform);
            return true;
        }

        protected override void OnSceneBegin()
        {
            previousExpression = visual.CurrentFacialExpression;
            if (!visual.TrySetContextualFacialExpression(this, previousExpression)) { CancelScene(); return; }
            previousHandoff = visual.InteractionHandoffLocked;
            visual.SetInteractionHandoffLocked(true);
            ownsHandoff = true;
            armPose.Capture();
            valvePose.Capture();
            firstPerson.Begin();
            previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; cursorCaptured = true;
            timeline.Begin(); progress.Reset(); spit.Begin(); faucet.SetOpen(0f);
            pendingSpitSeconds = scrubDistance = 0f; committed = false; discardMouse = true;
            presentedFaucetOpen = 0f; valveSoundDirection = 0;
        }

        public void ApplyBrushDelta(Vector2 mousePixels)
        {
            if (timeline.Phase == HomeTeethBrushingPhase.Brushing && !PauseMenuController.IsAnyPaused)
                progress.Move(mousePixels);
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            float before = timeline.EmissionSeconds;
            timeline.Advance(deltaTime);
            pendingSpitSeconds += timeline.EmissionSeconds - before;
            if (timeline.Phase != HomeTeethBrushingPhase.Brushing) return;
            if (Mouse.current != null && !discardMouse) ApplyBrushDelta(Mouse.current.delta.ReadValue());
            if (Gamepad.current != null) ApplyBrushDelta(Gamepad.current.rightStick.ReadValue() * (1400f * deltaTime));
            discardMouse = false;
        }

        protected override void OnScenePresentation(float deltaTime)
        {
            if (armPose == null || visual == null) return;
            float bend = pendingSpitSeconds > 0f ? Mathf.Max(0.95f, timeline.SpitBend) : timeline.SpitBend;
            armPose.Apply(progress.Offset, timeline.ArmWeight, bend, timeline.ValveReach);
            faucet.SetOpen(timeline.FaucetOpen);
            valvePose.ApplyValveGrip(faucet.GripPosition, faucet.GripRotation, timeline.ValveReach);
            PlayValveTurnCue();
            if (timeline.Phase == HomeTeethBrushingPhase.Brushing)
            {
                float credit = progress.Credit(armPose.ActualBrushTravel, armPose.ContactError < 0.012f, deltaTime);
                scrubDistance += credit;
                if (scrubDistance >= 0.04f)
                {
                    scrubDistance %= 0.04f;
                    Home.Audio?.TryPlay(RetroSfxId.TeethBrushScrub, visual.Registry.Anchors.Mouth.position);
                }
                if (progress.Complete) timeline.CompleteBrushing();
            }
            toothbrush.SetActive(timeline.ArmWeight > BrushVisibleWeight);
            foam.SetActive(timeline.Phase == HomeTeethBrushingPhase.Brushing && progress.Amount >= 0.1f);
            PlayerFacialExpression expression = timeline.Phase == HomeTeethBrushingPhase.Spit ? PlayerFacialExpression.Spit :
                timeline.Phase == HomeTeethBrushingPhase.Brushing || timeline.Phase == HomeTeethBrushingPhase.ShowTeeth ?
                PlayerFacialExpression.TeethDisplay : previousExpression;
            visual.TrySetContextualFacialExpression(this, expression, timeline.Cleaned);
            if (pendingSpitSeconds > 0f)
            {
                spit.EmitStep(visual.Registry.Anchors.Mouth.position, Home.transform.TransformPoint(BasinTarget), pendingSpitSeconds);
                pendingSpitSeconds = 0f;
            }
            firstPerson.Present(timeline.CameraBlend, faucet.GripPosition, timeline.ValveReach,
                Home.transform.TransformPoint(BasinTarget), bend);
            if (timeline.IsCompleted) ReleasePose();
            bool showPrompt = timeline.Phase == HomeTeethBrushingPhase.Brushing;
            if (showPrompt != brushingPromptVisible)
            {
                brushingPromptVisible = showPrompt;
                Home.InteractionPrompt?.SetPrompt(showPrompt ? StopPromptKeyName : string.Empty,
                    showPrompt ? stopAction : null);
            }
        }

        private void PlayValveTurnCue()
        {
            float change = faucet.OpenAmount - presentedFaucetOpen;
            presentedFaucetOpen = faucet.OpenAmount;
            if (Mathf.Abs(change) <= 0.000001f) return;
            int direction = change > 0f ? 1 : -1;
            if (direction == valveSoundDirection) return;
            // A reversed turn also covers cancellation while opening. Reaching,
            // holding and forced cleanup do not produce a mechanical cue.
            if (Home.Soundscape != null && Home.Soundscape.PlayBathroomValveTurn(
                    Home.Audio, faucet.GripPosition, direction > 0))
                valveSoundDirection = direction;
        }

        protected override bool TryGetSceneCamera(out Vector3 position, out Quaternion rotation)
        {
            position = firstPerson.Position;
            rotation = firstPerson.Rotation;
            return true;
        }

        protected override bool OnRequestStop() => timeline.RequestFinish();
        protected override void OnSceneCommit()
        {
            if (committed || !timeline.CanCommit) return;
            committed = true;
            GameSessionState.TryCommitTeethBrushingRelief(StressRelief);
        }
        private void ReleasePose()
        {
            armPose?.End();
            valvePose?.End();
            if (ownsHandoff && visual != null) visual.SetInteractionHandoffLocked(previousHandoff);
            ownsHandoff = false;
        }
        protected override void OnSceneRestore()
        {
            faucet?.SetOpen(0f);
            Home?.Soundscape?.StopBathroomActionSounds();
            firstPerson?.End();
            spit?.Stop();
            ReleasePose();
            visual?.ReleaseContextualFacialExpression(this);
            timeline.Reset(); progress.Reset(); pendingSpitSeconds = 0f;
            presentedFaucetOpen = 0f; valveSoundDirection = 0;
            brushingPromptVisible = false;
            if (toothbrush != null) toothbrush.SetActive(false);
            if (foam != null) foam.SetActive(false);
            if (cursorCaptured) { Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible; cursorCaptured = false; }
        }

        private void EnsureProps(Player3DAssetRegistry registry)
        {
            if (toothbrush == null)
            {
                Transform grip = registry.Anchors.RightGrip;
                toothbrush = new GameObject("Player Toothbrush");
                toothbrush.transform.SetParent(grip, false);
                toothbrush.transform.localScale =
                    InverseScale(grip.lossyScale);
                var handle = new GameObject("Handle");
                handle.transform.SetParent(toothbrush.transform, false);
                handle.transform.localPosition = new Vector3(0f, 0.045f, 0f);
                handle.AddComponent<MeshFilter>().sharedMesh = HomeBrushingResources.Mesh("BrushHandle");
                MeshRenderer handleRenderer = handle.AddComponent<MeshRenderer>();
                handleRenderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                RuntimePrimitiveFactory.SetColor(handleRenderer, new Color(0.55f, 0.15f, 0.12f));
                HomeAuthoredVisualFactory.CreateBox(
                    "Bristles",
                    toothbrush.transform,
                    new Vector3(0f, 0.115f, 0.008f),
                    new Vector3(0.014f, 0.025f, 0.010f),
                    new Color(0.75f, 0.75f, 0.68f),
                    false);
                var tip = new GameObject("Brush Tip");
                tip.transform.SetParent(toothbrush.transform, false);
                tip.transform.localPosition =
                    new Vector3(0f, 0.115f, 0.008f);
                brushTip = tip.transform;
                toothbrush.SetActive(false);
            }

            if (foam == null)
            {
                Transform mouth = registry.Anchors.Mouth;
                foam = new GameObject("Player Brushing Foam");
                foam.transform.SetParent(mouth, false);
                foam.transform.localScale =
                    InverseScale(mouth.lossyScale);
                Vector3[] blobs =
                {
                    new Vector3(-0.012f, -0.004f, 0.012f),
                    new Vector3(0.011f, -0.006f, 0.010f),
                    new Vector3(0f, 0.006f, 0.014f)
                };
                for (int index = 0; index < blobs.Length; index++)
                {
                    HomeAuthoredVisualFactory.CreateBox(
                        $"Foam {index + 1}",
                        foam.transform,
                        blobs[index],
                        new Vector3(0.014f, 0.011f, 0.011f),
                        new Color(0.80f, 0.80f, 0.75f),
                        false);
                }

                foam.SetActive(false);
            }
        }

        private static Vector3 InverseScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (toothbrush != null)
            {
                Destroy(toothbrush);
            }

            if (foam != null)
            {
                Destroy(foam);
            }
        }
    }
}
