using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Chapel catch, spring seeps and actual small cascades; two short production-capsule approaches.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageBrookDetails()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot village = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return village != null && village.IsInitialized ? village : null;
            }, () => VillageBrookDetailShots(village));
            yield return WalkVillageBrookApproaches(village);
        }

        private static Shot[] VillageBrookDetailShots(AlpineVillageRoot village)
        {
            var shots = new List<Shot>();
            AlpineVillageBrookPlan brook = village.Plan.Brook;
            AlpineVillagePlotDescriptor chapel = null;
            foreach (AlpineVillagePlotDescriptor plot in village.Plan.Plots)
                if (plot.Kind == AlpineVillagePlotKind.Chapel) { chapel = plot; break; }
            Assert.That(chapel, Is.Not.Null);
            Assert.That(brook.Seeps, Is.Not.Empty);
            Assert.That(brook.Cascades, Is.Not.Empty);
            AlpineColdExposureDriver frost = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
            Renderer[] hero = village.Player.GameObject.GetComponentsInChildren<Renderer>(true);
            Physics.SyncTransforms();

            Assert.That(TryAbandonmentFoot(village, chapel.GroundCenter,
                Quaternion.LookRotation(chapel.Facing), chapel.FootprintSize, 0, out Vector3 chapelFoot), Is.True);
            Add("brook-details-00-chapel-threshold", chapelFoot,
                chapel.DoorGroundPosition + Vector3.up * 1.2f, 65f);
            Vector3 basinFoot = AlpineVillagePathPlanner.GetChapelSourceApproachPosition(village.Plan, chapel);
            Add("brook-details-01-chapel-pipe-and-catch", basinFoot,
                brook.ChapelBasinPoint + Vector3.up * .40f, 58f);
            Add("brook-details-02-seeps-under-rock", brook.ApproachPosition,
                brook.LedgeCenter + Vector3.up * .65f, 58f);
            Add("brook-details-03-catch-overflow-contact", brook.ApproachPosition + brook.BowlOutletDirection * .65f,
                brook.OverflowLip + brook.BowlOutletDirection * .20f - Vector3.up * .06f, 52f);

            int largest = 0;
            for (int index = 1; index < brook.Cascades.Count; index++)
                if (brook.Cascades[index].Drop > brook.Cascades[largest].Drop) largest = index;
            // A bounded set covers both ends, the middle and the greatest
            // actual drop. Generic channel sample indices cannot promise this.
            var selected = new HashSet<int>();
            foreach (int index in new[] { 0, brook.Cascades.Count / 2, brook.Cascades.Count - 1, largest })
            {
                if (!selected.Add(index)) continue;
                AlpineVillageBrookCascade cascade = brook.Cascades[index];
                Vector3 right = Vector3.Cross(Vector3.up, cascade.Forward).normalized;
                Vector3 aim = cascade.Lip + cascade.Forward * .40f - Vector3.up * (cascade.Drop * .4f);
                Assert.That(TryVillageBrookDetailFoot(village, aim, right, out Vector3 foot), Is.True,
                    "No real bank view for " + cascade.StableId);
                Add("brook-details-10-" + cascade.StableId, foot, aim, 54f);
                if (index == largest)
                    Add("brook-details-11-" + cascade.StableId + "-flow-later", foot, aim, 54f, 15);
                TestContext.Out.WriteLine($"Brook detail {cascade.StableId}: lip {cascade.Lip:F3}, " +
                    $"drop/width {cascade.Drop:F3}/{cascade.Width:F3} m, eye {foot + Vector3.up * EyeHeight:F3}.");
            }
            Assert.That(shots.Count, Is.LessThanOrEqualTo(9));
            return shots.ToArray();

            void Add(string name, Vector3 foot, Vector3 aim, float fov, int delay = 0)
            {
                foot = VillageFootbridgeGround(village.Plan, foot);
                Assert.That(village.World.WalkableArea.Contains(foot, .25f) && AbandonmentCapsuleFree(foot),
                    Is.True, name + ": the eye must stand on accessible ground.");
                int frames = 0;
                shots.Add(Shot.At(name, foot + Vector3.up * EyeHeight, aim, fov, delay, () =>
                {
                    if (frames++ == 0) village.Player.Motor.Teleport(foot + Vector3.up * PlayerFactory.GroundedRootOffset);
                    if (frost != null) frost.Model.Reset();
                    if (frames < 12 || village.StormWave > GustTroughWave) return false;
                    foreach (Renderer renderer in hero) renderer.enabled = false;
                    return true;
                }));
            }
        }

        private static bool TryVillageBrookDetailFoot(AlpineVillageRoot village, Vector3 target,
            Vector3 outward, out Vector3 foot)
        {
            foreach (float distance in new[] { 2.6f, 3.8f, 5f })
            foreach (float angle in new[] { 0f, 180f, -35f, 35f, 145f, 215f })
            {
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * outward;
                foot = VillageFootbridgeGround(village.Plan, target + direction * distance);
                if (!village.World.WalkableArea.Contains(foot, .25f) || !AbandonmentCapsuleFree(foot)) continue;
                if (!Physics.Linecast(foot + Vector3.up * EyeHeight, target + Vector3.up * .04f,
                    ~0, QueryTriggerInteraction.Ignore)) return true;
            }
            foot = default;
            return false;
        }

        private static IEnumerator WalkVillageBrookApproaches(AlpineVillageRoot village)
        {
            IReadOnlyList<AlpineVillagePathDescriptor> paths = AlpineVillagePathPlanner.Create(village.Plan);
            AlpineVillagePathDescriptor spring = default, chapel = default;
            foreach (AlpineVillagePathDescriptor path in paths)
            {
                if (path.Kind == AlpineVillagePathKind.SpringSpur) spring = path;
                if (path.StableId == "village-chapel-source-path-c") chapel = path;
            }
            Assert.That(spring.StableId, Is.Not.Null.And.Not.Empty);
            Assert.That(chapel.StableId, Is.Not.Null.And.Not.Empty);
            PlayerMotor motor = village.Player.Motor;
            Camera camera = Camera.main;
            MeshCollider terrain = village.World.Root.transform.Find("Village Ground").GetComponent<MeshCollider>();
            Vector3 originalPosition = motor.transform.position;
            Quaternion originalRotation = motor.transform.rotation;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView, previousStep = Time.captureDeltaTime;
            bool inputEnabled = motor.InputEnabled, followEnabled = village.CameraFollow.enabled;
            try
            {
                const float step = .04f;
                Time.captureDeltaTime = step;
                motor.SetInputEnabled(false);
                village.CameraFollow.enabled = false;
                foreach (AlpineVillagePathDescriptor path in new[] { spring, chapel })
                {
                    Vector3 forward = Vector3.ProjectOnPlane(path.End - path.Start, Vector3.up).normalized;
                    float length = Vector3.ProjectOnPlane(path.End - path.Start, Vector3.up).magnitude;
                    Assert.That(length, Is.GreaterThan(.5f));
                    Vector3 near = VillageFootbridgeGround(village.Plan, path.End - forward * Mathf.Min(2.2f, length * .8f));
                    Vector3 far = VillageFootbridgeGround(village.Plan, path.End - forward * .15f);
                    motor.Teleport(near + Vector3.up * PlayerFactory.GroundedRootOffset);
                    for (int frame = 0; frame < 8; frame++) yield return null;
                    bool arrived = false;
                    int supported = 0;
                    for (int frame = 0; frame < 180 && !arrived; frame++)
                    {
                        arrived = motor.MoveTowardsApproachWaypoint(far, .05f, step);
                        Vector3 eye = motor.transform.position + Vector3.Cross(Vector3.up, forward) * 2.5f - forward * .7f + Vector3.up * 1.5f;
                        camera.transform.SetPositionAndRotation(eye,
                            Quaternion.LookRotation(motor.transform.position + Vector3.up * .75f - eye));
                        camera.fieldOfView = 66f;
                        yield return null;
                        Assert.That(motor.InteractionPoseMoveStalled, Is.False, path.StableId + ": real approach stalled.");
                        Assert.That(terrain.Raycast(new Ray(motor.transform.position + Vector3.up, Vector3.down),
                            out RaycastHit floor, 3f), Is.True);
                        Assert.That(motor.transform.position.y,
                            Is.EqualTo(floor.point.y + PlayerFactory.GroundedRootOffset).Within(.18f),
                            path.StableId + ": the hero must remain supported at the water approach.");
                        supported++;
                    }
                    CaptureCurrentCamera(camera, SceneIds.AlpineVillage, "brook-details-20-walk-" + path.StableId);
                    Assert.That(arrived && supported > 0, Is.True, path.StableId + ": the short approach was not completed.");
                    motor.CancelInteractionPoseMove();
                }
            }
            finally
            {
                motor.CancelInteractionPoseMove();
                motor.Teleport(originalPosition);
                motor.transform.rotation = originalRotation;
                motor.SetInputEnabled(inputEnabled);
                village.CameraFollow.enabled = followEnabled;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = cameraFov;
                Time.captureDeltaTime = previousStep;
            }
        }
    }
}
