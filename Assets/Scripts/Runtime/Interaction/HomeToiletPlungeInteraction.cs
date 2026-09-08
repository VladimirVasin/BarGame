using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bathroom owner coordinates the real hero, bowl camera and physical contacts.</summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class HomeToiletPlungeInteraction : HomeBathroomSceneInteraction
    {
        public const float SubmergedNearClip = .008f;
        public const float AboveBowlTravel = .68f;
        private readonly HomeToiletPlungeTimeline timeline = new HomeToiletPlungeTimeline();
        private readonly HomeToiletSeatedTimeline sequence = new HomeToiletSeatedTimeline();
        private readonly HomeToiletFlushVortex vortex = new HomeToiletFlushVortex();
        private HomeToiletActorPresentation actor;
        private HomeToiletSeatedAppearance appearance;
        private HomeToiletBowelEffect bowelEffect;
        private HomeToiletBowlLighting lighting;
        private HomeToiletLid lid;
        private Camera sceneCamera;
        private HomeToiletUnderwaterEffect underwater;
        private Transform bowl;
        private Transform water;
        private Vector3 aboveLocal, submergedLocal;
        private float previousNearClip;
        private bool captured;
        private bool prepared;
        private bool seatClearAtRiseEnd;
        private bool seatClearAtFlushEnd;
        private float seatedTime = -1f;

        public HomeToiletPlungeTimeline Timeline => timeline;
        public HomeToiletSeatedTimeline Sequence => sequence;
        public HomeToiletFlushVortex Vortex => vortex;
        public HomeToiletActorPresentation Actor => actor;
        public HomeToiletSeatedAppearance Appearance => appearance;
        public HomeToiletBowelEffect BowelEffect => bowelEffect;
        public HomeToiletBowlLighting Lighting => lighting;
        public HomeToiletUnderwaterEffect Underwater => underwater;
        public bool IsActive => OwnsScene;
        public Vector3 SubmergedPosition => bowl.TransformPoint(submergedLocal);
        public Vector3 AboveBowlPosition => bowl.TransformPoint(aboveLocal);
        public float WaterHeight => water.position.y;
        public override string PromptKey => OwnsScene ? string.Empty : HomeToiletInteraction.UsePromptKey;
        protected override string StopPromptKey => HomeToiletInteraction.StopPromptKeyName;
        protected override Vector3 CameraLocalPosition => Home.transform.InverseTransformPoint(SubmergedPosition);
        protected override Vector3 CameraLocalLookAt => Home.transform.InverseTransformPoint(
            SubmergedPosition + Home.transform.up + Home.transform.right * .02f);
        protected override float CameraFieldOfView => 65f;
        protected override float CameraBlend => timeline.Travel;
        protected override float CameraDriftWeight => 0f;
        protected override bool SceneCompleted => sequence.IsCompleted;
        protected override bool StopPromptVisible => sequence.Phase > HomeToiletSeatedPhase.Idle &&
            sequence.Phase < HomeToiletSeatedPhase.Rising && !sequence.WasCancelled;

        public void Initialize(HomeInteriorRoot home)
        {
            Vector3 dock = new Vector3(3.32f, 0f, 1.40f);
            InitializeScene(home, dock, Quaternion.LookRotation(Vector3.right),
                new Vector3(3.10f, 0f, 1.40f), Quaternion.LookRotation(Vector3.left), dock);
            lid = home.Room.GetComponentInChildren<HomeToiletLid>(true);
            bowl = home.Room.Find("Home Bathroom Toilet Bowl");
            water = home.Room.Find("Home Bathroom Toilet Water");
            sceneCamera = home.CameraFollow.GetComponent<Camera>();
            GameObject template = Resources.Load<GameObject>("HomeToiletAction/Models/ToiletBowl");
            if (template == null || bowl == null || water == null)
                throw new InvalidOperationException("The toilet plunge requires the authored deep bowl and water.");
            aboveLocal = ReadAnchor(template, "CameraAboveBowl");
            submergedLocal = ReadAnchor(template, "CameraSubmerged");
            underwater = sceneCamera.gameObject.AddComponent<HomeToiletUnderwaterEffect>();
            actor = gameObject.AddComponent<HomeToiletActorPresentation>();
            actor.Initialize(home);
            appearance = gameObject.AddComponent<HomeToiletSeatedAppearance>();
            appearance.Initialize(home);
            var effects = new GameObject("Home Toilet Bowl Effects");
            effects.transform.SetParent(home.transform, false);
            bowelEffect = effects.AddComponent<HomeToiletBowelEffect>();
            lighting = effects.AddComponent<HomeToiletBowlLighting>();
        }

        private static Vector3 ReadAnchor(GameObject template, string name)
        {
            foreach (Transform child in template.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    // The imported root's 100x unit factor must remain in this measurement,
                    // just as it does when HomeUrineResources combines the model's mesh.
                    return child.position - template.transform.position;
            throw new InvalidOperationException("Missing toilet camera anchor: " + name);
        }

        protected override bool PrepareScene() => lid != null && sceneCamera != null &&
            underwater != null && underwater.Prepare() && actor.Prepare() && appearance.Prepare() &&
            bowelEffect.Prepare(Home, water, sceneCamera);

        protected override void OnSceneCaptured()
        {
            previousNearClip = sceneCamera.nearClipPlane;
            captured = true;
            sceneCamera.nearClipPlane = Mathf.Min(previousNearClip, SubmergedNearClip);
            underwater.Begin(Home.transform, water);
            lighting.Begin(SubmergedPosition, Home.transform.right, Home.transform.forward,
                bowl, water, Home.Room.Find("Home Bathroom Toilet Seat"),
                Home.Player.GameObject.transform, bowelEffect.transform);
        }

        protected override void OnSceneBegin()
        {
            timeline.Reset();
            vortex.Reset();
            sequence.Begin();
            prepared = false;
            seatClearAtRiseEnd = false;
            seatClearAtFlushEnd = false;
            seatedTime = -1f;
            if (!actor.Begin()) CancelScene();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            vortex.Advance(deltaTime);
            if (sequence.CanAdvance) AdvanceSequence();
            if (!OwnsScene || sequence.IsCompleted) return;
            sequence.Advance(deltaTime);
            switch (sequence.Phase)
            {
                case HomeToiletSeatedPhase.TurningToSeat:
                case HomeToiletSeatedPhase.TurningToLid:
                case HomeToiletSeatedPhase.TurningToInspect:
                    bool toSeat = sequence.Phase == HomeToiletSeatedPhase.TurningToSeat;
                    HomeGuidedWalkStep turn = AdvanceGuidedWalk(DockPosition,
                        Quaternion.LookRotation(toSeat ? Vector3.left : Vector3.right), deltaTime);
                    if (turn == HomeGuidedWalkStep.Stalled) { CancelScene(); return; }
                    if (turn == HomeGuidedWalkStep.Arrived) sequence.CompleteExternal();
                    break;
                case HomeToiletSeatedPhase.Diving:
                    timeline.Advance(Mathf.Min(deltaTime,
                        HomeToiletPlungeTimeline.EnterSeconds - timeline.PhaseElapsed));
                    break;
                case HomeToiletSeatedPhase.Exiting:
                    timeline.Advance(deltaTime);
                    if (timeline.IsCompleted) sequence.CompleteExternal();
                    break;
                case HomeToiletSeatedPhase.VortexHold:
                    if (vortex.Elapsed >= HomeToiletFlushVortex.HoldSeconds) sequence.CompleteExternal();
                    break;
            }
        }

        private void AdvanceSequence()
        {
            switch (sequence.Phase)
            {
                case HomeToiletSeatedPhase.OpeningLid:
                    if (sequence.WasCancelled) MoveTo(HomeToiletSeatedPhase.ClosingLid);
                    else MoveTo(HomeToiletSeatedPhase.TurningToSeat);
                    break;
                case HomeToiletSeatedPhase.TurningToSeat:
                    MoveTo(sequence.WasCancelled ? HomeToiletSeatedPhase.TurningToLid : HomeToiletSeatedPhase.Preparing);
                    break;
                case HomeToiletSeatedPhase.Preparing:
                    MoveTo(sequence.WasCancelled ? HomeToiletSeatedPhase.Dressing : HomeToiletSeatedPhase.Diving);
                    break;
                case HomeToiletSeatedPhase.Diving: MoveTo(HomeToiletSeatedPhase.Sitting); break;
                case HomeToiletSeatedPhase.Sitting:
                    MoveTo(sequence.WasCancelled ? HomeToiletSeatedPhase.Rising : HomeToiletSeatedPhase.Seated);
                    break;
                case HomeToiletSeatedPhase.Seated: MoveTo(HomeToiletSeatedPhase.Rising); break;
                case HomeToiletSeatedPhase.Rising:
                    if (!seatClearAtRiseEnd) { CancelScene(); return; }
                    MoveTo(sequence.WasCancelled ? HomeToiletSeatedPhase.Exiting : HomeToiletSeatedPhase.TurningToInspect);
                    break;
                case HomeToiletSeatedPhase.TurningToInspect: MoveTo(HomeToiletSeatedPhase.Inspecting); break;
                case HomeToiletSeatedPhase.Inspecting: MoveTo(HomeToiletSeatedPhase.Flushing); break;
                case HomeToiletSeatedPhase.Flushing:
                    if (!seatClearAtFlushEnd) { CancelScene(); return; }
                    MoveTo(HomeToiletSeatedPhase.VortexHold);
                    break;
                case HomeToiletSeatedPhase.VortexHold: MoveTo(HomeToiletSeatedPhase.Exiting); break;
                case HomeToiletSeatedPhase.Exiting:
                    if (vortex.IsActive)
                    {
                        appearance.End();
                        MoveTo(HomeToiletSeatedPhase.ClosingLid);
                    }
                    else MoveTo(prepared ? HomeToiletSeatedPhase.Dressing : HomeToiletSeatedPhase.TurningToLid);
                    break;
                case HomeToiletSeatedPhase.Dressing:
                    appearance.End();
                    MoveTo(HomeToiletSeatedPhase.TurningToLid);
                    break;
                case HomeToiletSeatedPhase.TurningToLid: MoveTo(HomeToiletSeatedPhase.ClosingLid); break;
                case HomeToiletSeatedPhase.ClosingLid:
                    actor.End();
                    MoveTo(HomeToiletSeatedPhase.Completed);
                    break;
            }
        }

        private void MoveTo(HomeToiletSeatedPhase phase)
        {
            sequence.MoveTo(phase);
            if (phase == HomeToiletSeatedPhase.TurningToSeat || phase == HomeToiletSeatedPhase.TurningToLid ||
                phase == HomeToiletSeatedPhase.TurningToInspect)
                actor.End();
            else if (phase != HomeToiletSeatedPhase.Completed && !actor.Begin()) { CancelScene(); return; }
            if (phase == HomeToiletSeatedPhase.Preparing)
            {
                prepared = appearance.Begin();
                if (!prepared) { CancelScene(); return; }
                bowelEffect.Begin(appearance.Outlet);
            }
            if (phase == HomeToiletSeatedPhase.Diving) timeline.Begin();
            if (phase == HomeToiletSeatedPhase.Exiting) timeline.BeginReturn(sequence.WasCancelled);
        }

        protected override void OnScenePresentation(float deltaTime)
        {
            switch (sequence.Phase)
            {
                case HomeToiletSeatedPhase.OpeningLid: actor.ApplyPhase(HomeToiletActorPhase.OpenLid, sequence.Progress); break;
                case HomeToiletSeatedPhase.Preparing: actor.ApplyPhase(HomeToiletActorPhase.Prepare, sequence.Progress); break;
                case HomeToiletSeatedPhase.Diving:
                    float sit = (sequence.PhaseElapsed - HomeToiletSeatedTimeline.SitStartsDuringDive) /
                        HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Sit);
                    if (sit <= 0f) actor.ApplyPhase(HomeToiletActorPhase.Prepare, 1f);
                    else actor.ApplyPhase(HomeToiletActorPhase.Sit, sit);
                    break;
                case HomeToiletSeatedPhase.Sitting:
                    actor.ApplyPhase(HomeToiletActorPhase.Sit,
                        (HomeToiletPlungeTimeline.EnterSeconds - HomeToiletSeatedTimeline.SitStartsDuringDive + sequence.PhaseElapsed) /
                        HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Sit));
                    break;
                case HomeToiletSeatedPhase.Seated:
                    actor.ApplyPhase(HomeToiletActorPhase.Seated, sequence.Progress);
                    seatedTime = sequence.PhaseElapsed;
                    break;
                case HomeToiletSeatedPhase.Rising: actor.ApplyPhase(HomeToiletActorPhase.Rise, sequence.Progress); break;
                case HomeToiletSeatedPhase.Inspecting: actor.ApplyPhase(HomeToiletActorPhase.Inspect, sequence.Progress); break;
                case HomeToiletSeatedPhase.Flushing: actor.ApplyPhase(HomeToiletActorPhase.Flush, sequence.Progress); break;
                case HomeToiletSeatedPhase.VortexHold: actor.ApplyPhase(HomeToiletActorPhase.Flush, 1f); break;
                case HomeToiletSeatedPhase.Exiting:
                    if (vortex.IsActive)
                        actor.ApplyPhase(HomeToiletActorPhase.Dress,
                            sequence.PhaseElapsed / HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Dress));
                    else actor.ApplyPhase(HomeToiletActorPhase.Prepare, 1f);
                    break;
                case HomeToiletSeatedPhase.Dressing: actor.ApplyPhase(HomeToiletActorPhase.Dress, sequence.Progress); break;
                case HomeToiletSeatedPhase.ClosingLid: actor.ApplyPhase(HomeToiletActorPhase.CloseLid, sequence.Progress); break;
            }
            if (appearance.IsActive) appearance.Present(sequence.Phase == HomeToiletSeatedPhase.TurningToInspect
                ? 1f : actor.ClothingAmount);
            if (sequence.Phase == HomeToiletSeatedPhase.Rising && sequence.AtEnd)
                seatClearAtRiseEnd = actor.SeatClear;
            if (sequence.Phase == HomeToiletSeatedPhase.Flushing)
            {
                if (!vortex.IsActive && sequence.PhaseElapsed >= HomeToiletActorPresentation.FlushCueSeconds)
                {
                    // This marker is clamped until the real hand has presented its
                    // pressed endpoint. A long frame cannot flush before the touch.
                    vortex.Begin();
                    bowelEffect.BeginFlush();
                    sequence.MarkFlushPresented();
                }
                if (sequence.AtEnd) seatClearAtFlushEnd = actor.SeatClear;
            }
            bowelEffect.Present(SceneElapsed, seatedTime,
                sequence.Phase == HomeToiletSeatedPhase.Seated && !sequence.WasCancelled);
            if (vortex.IsActive) bowelEffect.PresentFlush(vortex.Elapsed, vortex.Strength);
            underwater.PresentVortex(vortex.Strength, vortex.RollDegrees * Mathf.Deg2Rad);
            float inspectionLight = sequence.Phase == HomeToiletSeatedPhase.Inspecting
                ? HomeToiletPlungeTimeline.Ease(sequence.Progress)
                : sequence.Phase == HomeToiletSeatedPhase.Flushing
                    ? 1f - HomeToiletPlungeTimeline.Ease((sequence.PhaseElapsed - HomeToiletActorPresentation.FlushCueSeconds) /
                        (HomeToiletActorPresentation.FlushDurationSeconds - HomeToiletActorPresentation.FlushCueSeconds))
                    : 0f;
            lighting.Present(timeline.Travel, inspectionLight);
            sequence.MarkPresented();
        }

        protected override bool OnRequestStop()
        {
            if (!sequence.RequestFinish()) return false;
            if (sequence.Phase == HomeToiletSeatedPhase.Diving &&
                sequence.PhaseElapsed < HomeToiletSeatedTimeline.SitStartsDuringDive)
                MoveTo(HomeToiletSeatedPhase.Exiting);
            return true;
        }
        protected override void OnSceneCommit() { }

        protected override bool TryEvaluateCameraPath(float amount, Vector3 start, Quaternion startRotation,
            Vector3 target, Quaternion targetRotation, out Vector3 position, out Quaternion rotation)
        {
            EvaluatePath(amount, start, startRotation, AboveBowlPosition, SubmergedPosition,
                Home.transform.up, Home.transform.right, out position, out rotation);
            vortex.ApplyCamera(amount, Home.transform.right, Home.transform.forward, ref position, ref rotation);
            underwater.Present(position.y, SceneElapsed);
            return true;
        }

        internal static void EvaluatePath(float travel, Vector3 start, Quaternion startRotation,
            Vector3 above, Vector3 submerged, Vector3 worldUp, Vector3 worldRight,
            out Vector3 position, out Quaternion rotation)
        {
            float amount = Mathf.Clamp01(travel);
            Vector3 descent = (submerged - above) / (1f - AboveBowlTravel);
            if (amount <= AboveBowlTravel)
            {
                float t = amount / AboveBowlTravel;
                Vector3 arrivalTangent = descent * AboveBowlTravel;
                Vector3 departureTangent = Vector3.ProjectOnPlane(above - start, worldUp) * 1.5f + worldUp * .12f;
                // A quintic Hermite approach meets the vertical descent with the
                // same nonzero tangent and zero second derivative. The mouth is a
                // point we fly through, never a separately eased stopping place.
                float t2 = t * t;
                float t3 = t2 * t;
                float t4 = t3 * t;
                float t5 = t4 * t;
                position = Vector3.LerpUnclamped(start, above, HomeToiletPlungeTimeline.Ease(t)) +
                    departureTangent * (t - 6f * t3 + 8f * t4 - 3f * t5) +
                    arrivalTangent * (-4f * t3 + 7f * t4 - 3f * t5);
            }
            else
            {
                position = above + descent * (amount - AboveBowlTravel);
            }

            Quaternion down = Quaternion.LookRotation(-worldUp, worldRight);
            float settleDown = HomeToiletPlungeTimeline.Ease(amount / (AboveBowlTravel + .10f));
            float turnUp = HomeToiletPlungeTimeline.Ease(
                (amount - (AboveBowlTravel - .08f)) / (1f - (AboveBowlTravel - .08f)));
            // Overlap the last downward framing with the upward pitch so the
            // angular motion also passes the mouth continuously. The slight
            // cisternward pitch frames the seat and the later inspection pose.
            rotation = Quaternion.Slerp(startRotation, down, settleDown) *
                Quaternion.AngleAxis(-170f * turnUp, Vector3.right);
        }

        protected override void OnSceneRestore()
        {
            actor?.End();
            appearance?.End();
            bowelEffect?.End();
            lighting?.End();
            underwater?.End();
            if (captured && sceneCamera != null) sceneCamera.nearClipPlane = previousNearClip;
            captured = false;
            lid?.Close();
            timeline.Reset();
            sequence.Reset();
            vortex.Reset();
            prepared = false;
            seatedTime = -1f;
        }
    }
}
