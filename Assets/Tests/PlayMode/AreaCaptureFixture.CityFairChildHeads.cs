using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class FairChildHeadReport
        {
            public bool repeated_pose_stable, all_directions_correct, target_release_restores_pose;
            public bool all_activities_checked, pause_stable, reenable_restores_pose;
            public float maximum_repeat_drift_degrees, maximum_neck_offset_degrees;
            public float maximum_target_error_increase_degrees, maximum_head_displacement_metres;
            public string[] captures;
        }

        [UnityTest]
        [Explicit("Fair child heads: imported rig orientation, repeated look offsets, real activities and their transitions.")]
        [Timeout(180000)]
        public IEnumerator CityFairChildHeads()
        {
            var report = new FairChildHeadReport();
            var captures = new List<string>();
            var references = new CityFairChildPresentation[3];
            GameObject referenceRoot = null;
            CityGameRoot city = null;
            Camera camera = null;
            PlayerCameraFollow follow = null;
            bool followEnabled = false;
            Vector3 cameraPosition = default;
            Quaternion cameraRotation = default;
            float cameraFov = 60f, previousDelta = Time.captureDeltaTime;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = .05f;
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                CityFairWorld fair = city.World.Fair;
                Assert.That(fair?.Children, Is.Not.Null);
                CityFairChildren children = fair.Children;
                camera = Camera.main;
                follow = camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow != null && follow.enabled;
                if (follow != null) follow.enabled = false;
                cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation; cameraFov = camera.fieldOfView;
                city.Player.Motor.Teleport(fair.Plan.CenterPathSouth + Vector3.up * PlayerFactory.GroundedRootOffset);
                referenceRoot = new GameObject("Child head pose references");
                for (int i = 0; i < references.Length; i++)
                {
                    GameObject model = CityFairChildAssetProvider.Create(i, referenceRoot.transform);
                    foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                    references[i] = CityFairChildPresentation.Attach(model);
                    AssertFairHeadSampling(references[i], report);
                }

                var observed = new bool[3];
                var completedBefore = Enumerable.Range(0, 3).Select(children.CompletedCycles).ToArray();
                for (int frame = 0; frame < 1050; frame++)
                {
                    yield return null;
                    for (int i = 0; i < 3; i++)
                    {
                        CityFairChildPresentation actor = children.Actors[i], reference = references[i];
                        reference.transform.SetPositionAndRotation(actor.transform.position, actor.transform.rotation);
                        reference.Sample(actor.CurrentAction, actor.CurrentSeconds);
                        float offset = Quaternion.Angle(FaceFrame(reference), FaceFrame(actor));
                        report.maximum_neck_offset_degrees = Mathf.Max(report.maximum_neck_offset_degrees, offset);
                        Assert.That(offset, Is.LessThan(40f), $"Child {i} {actor.CurrentAction}: head escaped its authored pose by {offset:F3} degrees.");
                        CityFairChildPhase phase = children.Phase(i);
                        bool busy = phase == CityFairChildPhase.Entering || phase == CityFairChildPhase.Looping || phase == CityFairChildPhase.Exiting;
                        if (busy)
                        {
                            // Align the reference's datum with the grounded/seated body, not with its turned head.
                            reference.ModelRoot.position += actor.Pelvis.position - reference.Pelvis.position;
                            Vector3 target = i == 0 ? fair.Plan.Stalls[3].Position + fair.Plan.Stalls[3].Rotation * new Vector3(-.85f, 1.06f, .57f) :
                                i == 1 ? children.Car.position : fair.Plan.OrganPosition + Vector3.up * 1.1f;
                            AssertLookImprovesAim(actor, reference.FaceForward, reference.Head.position, target, report, "Actual child " + i);
                            float displacement = Vector3.Distance(actor.Head.position, reference.Head.position);
                            report.maximum_head_displacement_metres = Mathf.Max(report.maximum_head_displacement_metres, displacement);
                            Assert.That(displacement, Is.LessThan(.065f), "A head turns about the neck instead of leaving the body.");
                        }
                        else if (actor.CurrentAction == CityFairChildAction.Walk || actor.CurrentAction == CityFairChildAction.Idle)
                            Assert.That(Vector3.Angle(actor.FaceForward, actor.transform.forward), Is.LessThan(5f), "A walking or idle child faces with its body.");
                        if (!observed[i] && phase == CityFairChildPhase.Looping && actor.CurrentSeconds > .4f)
                        {
                            observed[i] = true;
                            Vector3 focus = actor.Head.position;
                            Vector3 side = i == 0 ? new Vector3(.7f, .2f, -.9f) : i == 1 ? new Vector3(.8f, .30f, .7f) : new Vector3(.7f, .05f, 1.0f);
                            yield return ChildHeadFrame(camera, captures, "activity-" + i, focus + side, focus + Vector3.down * .15f, 60f);
                        }
                    }
                    if (observed.All(value => value) && Enumerable.Range(0, 3).All(i => children.CompletedCycles(i) > completedBefore[i])) break;
                    Assert.That(frame, Is.LessThan(1049), "Every child must finish a full activity with a bounded head pose.");
                }
                report.all_activities_checked = true;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    Quaternion[] paused = children.Actors.Select(FaceFrame).ToArray();
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    for (int i = 0; i < 3; i++) Assert.That(Quaternion.Angle(paused[i], FaceFrame(children.Actors[i])), Is.LessThan(.06f));
                    report.pause_stable = true;
                }
                children.enabled = false; children.enabled = true;
                yield return null;
                for (int i = 0; i < 3; i++)
                    Assert.That(Vector3.Angle(children.Actors[i].FaceForward, children.Actors[i].transform.forward), Is.LessThan(5f));
                report.reenable_restores_pose = true;
                // Freeze the real actors at neutral so front and back are unambiguous.
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    for (int i = 0; i < 3; i++)
                    {
                        CityFairChildPresentation actor = children.Actors[i];
                        actor.ResetPose();
                        Vector3 focus = actor.Head.position;
                        yield return ChildHeadFrame(camera, captures, "front-" + i, focus + actor.transform.forward * .75f, focus, 43f);
                        yield return ChildHeadFrame(camera, captures, "back-" + i, focus - actor.transform.forward * .75f, focus, 43f);
                    }
                }
                Debug.Log("FAIR CHILD HEADS: anatomy axes, repeated sampling/look, bounded residual aim, all activities, pause and re-enable verified.");
            }
            finally
            {
                if (referenceRoot != null) Object.Destroy(referenceRoot);
                Time.captureDeltaTime = previousDelta;
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                    camera.fieldOfView = cameraFov;
                }
                if (follow != null) follow.enabled = followEnabled;
                report.captures = captures.ToArray();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityFairChildHeads");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true));
            }
        }

        private static void AssertFairHeadSampling(CityFairChildPresentation actor, FairChildHeadReport report)
        {
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                actor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                actor.ResetPose();
                Assert.That(Vector3.Angle(actor.FaceForward, actor.transform.forward), Is.LessThan(.1f), "Imported face must turn with the actor in every direction.");
            }
            report.all_directions_correct = true;
            foreach (CityFairChildAction action in Enum.GetValues(typeof(CityFairChildAction)))
            {
                actor.ResetPose();
                float time = CityFairChildPresentation.Duration(action) * .37f;
                actor.Sample(action, time);
                Quaternion source = FaceFrame(actor);
                Vector3 sourcePosition = actor.Head.position;
                Vector3 target = sourcePosition + actor.transform.forward * 1.3f + actor.transform.right * .25f - Vector3.up * .35f;
                actor.ApplyLook(target);
                AssertLookImprovesAim(actor, source * Vector3.forward, sourcePosition, target, report, action.ToString());
                Quaternion looked = FaceFrame(actor);
                for (int repetition = 0; repetition < 90; repetition++)
                {
                    actor.ApplyLook(target);
                    CheckStable();
                    actor.Sample(action, time);
                    actor.ApplyLook(target);
                    CheckStable();
                }
                void CheckStable()
                {
                    float drift = Quaternion.Angle(looked, FaceFrame(actor));
                    report.maximum_repeat_drift_degrees = Mathf.Max(report.maximum_repeat_drift_degrees, drift);
                    Assert.That(drift, Is.LessThan(.08f), action + " accumulates neck rotation when the same pose is requested again.");
                }
                actor.ApplyLook(null);
                Assert.That(Quaternion.Angle(source, FaceFrame(actor)), Is.LessThan(.08f), action + " did not release the look offset.");
                actor.ApplyLook(target); actor.ApplyLook(target, 0f);
                Assert.That(Quaternion.Angle(source, FaceFrame(actor)), Is.LessThan(.08f), action + " zero weight must restore the clip pose.");
                actor.Sample(CityFairChildAction.Walk, .25f);
                actor.Sample(CityFairChildAction.Idle, 0f);
                actor.Sample(CityFairChildAction.Idle, .2f);
                Assert.That(Vector3.Angle(actor.FaceForward, actor.transform.forward), Is.LessThan(3f), "A finished activity cannot leave a head twisted during idle.");
            }
            report.repeated_pose_stable = report.target_release_restores_pose = true;
        }

        private static Quaternion FaceFrame(CityFairChildPresentation actor) => Quaternion.LookRotation(actor.FaceForward, actor.FaceUp);

        private static void AssertLookImprovesAim(CityFairChildPresentation actor, Vector3 originalForward, Vector3 originalPosition,
            Vector3 target, FairChildHeadReport report, string label)
        {
            float before = Vector3.Angle(originalForward, target - originalPosition);
            float after = Vector3.Angle(actor.FaceForward, target - actor.Head.position);
            report.maximum_target_error_increase_degrees = Mathf.Max(report.maximum_target_error_increase_degrees, after - before);
            Assert.That(after, Is.LessThanOrEqualTo(before + .3f), $"{label}: looking moved away from the target ({before:F3} -> {after:F3} degrees).");
        }

        private static IEnumerator ChildHeadFrame(Camera camera, List<string> captures, string name, Vector3 position, Vector3 target, float fov)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fov;
            yield return null;
            CaptureCurrentCamera(camera, "CityFairChildHeads", name);
            captures.Add(name + ".png");
        }
    }
}
