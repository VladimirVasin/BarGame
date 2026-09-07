using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The approved camera-only second toilet branch, on the shared bathroom lifecycle.</summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class HomeToiletPlungeInteraction : HomeBathroomSceneInteraction
    {
        public const float SubmergedNearClip = .008f;
        public const float AboveBowlTravel = .68f;
        private readonly HomeToiletPlungeTimeline timeline = new HomeToiletPlungeTimeline();
        private HomeToiletLid lid;
        private Camera sceneCamera;
        private HomeToiletUnderwaterEffect underwater;
        private Transform bowl;
        private Transform water;
        private Vector3 aboveLocal, submergedLocal;
        private float previousNearClip;
        private bool captured;

        public HomeToiletPlungeTimeline Timeline => timeline;
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
        protected override bool SceneCompleted => timeline.IsCompleted;
        protected override bool StopPromptVisible => timeline.Phase == HomeToiletPlungePhase.Entering ||
            timeline.Phase == HomeToiletPlungePhase.SubmergedHold;

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
            underwater != null && underwater.Prepare();

        protected override void OnSceneCaptured()
        {
            previousNearClip = sceneCamera.nearClipPlane;
            captured = true;
            sceneCamera.nearClipPlane = Mathf.Min(previousNearClip, SubmergedNearClip);
            lid.Open();
            underwater.Begin(Home.transform, water);
        }

        protected override void OnSceneBegin() => timeline.Begin();
        protected override void OnSceneAdvance(float deltaTime) => timeline.Advance(deltaTime);
        protected override bool OnRequestStop() => timeline.RequestFinish();
        protected override void OnSceneCommit() { }

        protected override bool TryEvaluateCameraPath(float amount, Vector3 start, Quaternion startRotation,
            Vector3 target, Quaternion targetRotation, out Vector3 position, out Quaternion rotation)
        {
            EvaluatePath(amount, start, startRotation, AboveBowlPosition, SubmergedPosition,
                Home.transform.up, Home.transform.right, out position, out rotation);
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
            // angular motion also passes the mouth continuously. Ten degrees toward
            // the cistern still keeps the hero's face outside the held view.
            rotation = Quaternion.Slerp(startRotation, down, settleDown) *
                Quaternion.AngleAxis(-170f * turnUp, Vector3.right);
        }

        protected override void OnSceneRestore()
        {
            underwater?.End();
            if (captured && sceneCamera != null) sceneCamera.nearClipPlane = previousNearClip;
            captured = false;
            lid?.Close();
            timeline.Reset();
        }
    }
}
